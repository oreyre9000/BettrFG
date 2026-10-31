using System;
using System.Collections.Generic;
using BetterFG.Services;
using BetterFG.Utilities;
using FG.Common;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LevelEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class ObjectThemes
    {
        internal static bool Enabled => FeatureCreativeContent.On("objectthemes");

        private static readonly MarkerKind Kind = MarkerKinds.ObjectTheme;
        private const string BalloonNodeName = "Feature_Floating_Canon_Balloon";
        private const int BalloonBit = 100;
        internal static string MarkerName => Kind.Prefab;
        private const float MarkerScale = 0.01f;
        private const float MatchRadius = 0.3f;

        private sealed class PartState
        {
            public Transform Node;
            public Renderer Renderer;
            public MeshFilter Filter;
            public SkinnedMeshRenderer Skinned;
            public Mesh StockMesh;
            public Material[] StockMaterials;
            public Vector3 StockPos;
            public Vector3 StockScale;
            public Quaternion StockRot;
            public BouncingDrumDotween Tween;
            public Vector3 TweenExpanded;
            public bool Extra;

            // The art node also carries the object's colliders, so fitting its transform would
            // resize the collision too. Render the themed art on our own node instead.
            // What we last wrote, so a game-side overwrite (a conveyor changing direction, a round
            // starting) can be spotted and undone.
            public Material Expected;
            // True when another themed part is this one's ancestor: the parent's fit already carries
            // it, so it must not be fitted a second time.
            public bool Parented;
            public bool Guarded;
            public bool GameScaled;
            public Transform Proxy;
            public bool ProxyWanted;
            public MeshFilter ProxyFilter;
            public MeshRenderer ProxyRenderer;
            public LevelEditorCommonRotator Flipper;
            public int FlipSlot;

            public Transform Art => Guarded ? Proxy : Node;

            public void SetScale(Vector3 scale)
            {
                if (Flipper == null) { Node.localScale = scale; return; }
                Flipper._defaultFlipScales[FlipSlot] = scale;
                Flipper.UpdateVisualRotation();
            }

            public void Place(Vector3 pos, Quaternion rot, Vector3 scale)
            {
                if (GameScaled) return;
                if (!Guarded) { Node.localPosition = pos; Node.localRotation = rot; SetScale(scale); return; }
                var undo = Quaternion.Inverse(StockRot);
                Proxy.localRotation = undo * rot;
                Proxy.localScale = new Vector3(
                    StockScale.x != 0f ? scale.x / StockScale.x : scale.x,
                    StockScale.y != 0f ? scale.y / StockScale.y : scale.y,
                    StockScale.z != 0f ? scale.z / StockScale.z : scale.z);
                var d = undo * (pos - StockPos);
                Proxy.localPosition = new Vector3(
                    StockScale.x != 0f ? d.x / StockScale.x : d.x,
                    StockScale.y != 0f ? d.y / StockScale.y : d.y,
                    StockScale.z != 0f ? d.z / StockScale.z : d.z);
            }
            public Renderer View => Guarded ? (Renderer)ProxyRenderer : Renderer;

            public void SetMesh(Mesh mesh)
            {
                if (Guarded) ProxyFilter.sharedMesh = mesh;
                else if (Skinned != null) Skinned.sharedMesh = mesh;
                else if (Filter != null) Filter.sharedMesh = mesh;
            }
        }

        private sealed class Host
        {
            public ObjectThemeFamily Family;
            public bool Reversed;
            public LevelEditorConveyorResponder Belt;
            public int BeltSlot;
            public Transform Root;
            public LevelEditorPlaceableObject Lepo;
            public LevelEditorPlaceableObject Marker;
            public int Size;
            public int Theme;
            public int Applied = -1;
            public bool NoBalloon;
            public Transform BalloonNode;
            public PartState[] Parts;
            public int[] Slots;
            public Il2CppReferenceArray<Renderer> OutlineStock;
            public bool Outlined;
        }

        private static readonly Dictionary<int, Host> _hosts = new Dictionary<int, Host>();
        // Survives ResetEditorState: a node captured after a theme was applied would otherwise
        // record the themed mesh and the fitted scale as "stock" and fit on top of them.
        private static readonly Dictionary<int, PartState> _stock = new Dictionary<int, PartState>();
        // Position -> the code we last wrote there. Survives ResetEditorState on purpose.
        private static readonly Dictionary<long, int> _known = new Dictionary<long, int>();

        private static long PosKey(Vector3 p)
            => ((long)Mathf.RoundToInt(p.x * 4f) * 73856093)
             ^ ((long)Mathf.RoundToInt(p.y * 4f) * 19349663)
             ^ ((long)Mathf.RoundToInt(p.z * 4f) * 83492791);
        private static readonly Dictionary<int, int> _unclaimed = new Dictionary<int, int>();
        private const int OrphanStrikes = 40;
        private static readonly Dictionary<string, UnityEngine.Object> _assets = new Dictionary<string, UnityEngine.Object>();
        private static readonly Dictionary<string, string[]> _matLists = new Dictionary<string, string[]>();
        private static int _tick;

        private static ObjectThemeFamily FamilyOf(string name)
        {
            var all = ObjectThemeTable.All;
            for (int i = 0; i < all.Length; i++)
                foreach (var prefix in all[i].Prefixes)
                    if (name.StartsWith(prefix, StringComparison.Ordinal)) return all[i];
            return null;
        }

        private static ObjectThemeFamily FamilyOf(LevelEditorPlaceableObject lepo)
            => lepo == null ? null : FamilyOf(lepo.name);

        internal static bool IsThemable(LevelEditorPlaceableObject lepo) => FamilyOf(lepo) != null;

        internal static bool HasBalloon(LevelEditorPlaceableObject lepo)
        {
            var family = FamilyOf(lepo);
            return family != null && family.Balloon;
        }

        internal static bool BalloonOn(LevelEditorPlaceableObject lepo)
            => lepo == null || !_hosts.TryGetValue(lepo.gameObject.GetInstanceID(), out var h) || !h.NoBalloon;

        internal static void SetBalloonOn(LevelEditorPlaceableObject lepo, bool on)
        {
            var host = HostFor(lepo);
            if (host == null) return;
            host.NoBalloon = !on;
            if (host.Lepo != null) _known[PosKey(host.Lepo.transform.position)] = Code(host);
            ApplyBalloon(host);
            Reconcile(host);
        }

        private static void ApplyBalloon(Host host)
        {
            if (host.Root == null || !host.Family.Balloon) return;
            if (host.BalloonNode == null)
            {
                foreach (var t in host.Root.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith(BalloonNodeName, StringComparison.Ordinal)) { host.BalloonNode = t; break; }
                if (host.BalloonNode == null) return;
            }
            bool replaced = host.Family.Themes[Mathf.Clamp(host.Theme, 0, host.Family.Themes.Length - 1)].ReplacesBalloon;
            host.BalloonNode.gameObject.SetActive(!host.NoBalloon && !replaced);
            if (replaced && host.Parts != null)
                foreach (var part in host.Parts)
                    if (part.Extra) part.Renderer.forceRenderingOff = host.NoBalloon;
        }

        private static int Code(Host host) => host.Theme + (host.NoBalloon ? BalloonBit : 0);

        private static void ReadCode(Host host, int code)
        {
            host.NoBalloon = code >= BalloonBit;
            host.Theme = code % BalloonBit;
        }

        internal static bool IsMarker(LevelEditorPlaceableObject lepo)
        {
            // No scale parameter yet: this may well be a marker, but we cannot read which theme it
            // encodes, so leave it alone this sweep rather than misread it.
            if (lepo == null || lepo._levelEditorScaleParameter == null) return false;
            return Kind.Matches(lepo);
        }

        private static bool TryScale(LevelEditorPlaceableObject lepo, out Vector3 scale)
        {
            var sp = lepo != null ? lepo._levelEditorScaleParameter : null;
            scale = sp != null ? sp.CurrentScale : Vector3.zero;
            return sp != null;
        }

        private static int SizeOf(string name)
        {
            if (name.IndexOf("XSMALL", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
            if (name.IndexOf("XLARGE", StringComparison.OrdinalIgnoreCase) >= 0) return 4;
            if (name.IndexOf("SMALL", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
            if (name.IndexOf("LARGE", StringComparison.OrdinalIgnoreCase) >= 0) return 3;
            return 2;
        }

        private static string[] KeysFor(ObjectTheme theme, int size)
        {
            switch (size)
            {
                case 0: return theme.XSmall ?? theme.Small ?? theme.Medium ?? theme.Large ?? theme.XLarge;
                case 1: return theme.Small ?? theme.Medium ?? theme.XSmall ?? theme.Large;
                case 3: return theme.Large ?? theme.Medium ?? theme.XLarge ?? theme.Small;
                case 4: return theme.XLarge ?? theme.Large ?? theme.Medium ?? theme.Small;
                default: return theme.Medium ?? theme.Large ?? theme.Small ?? theme.XLarge ?? theme.XSmall;
            }
        }

        internal static string[] Labels(LevelEditorPlaceableObject lepo)
        {
            var family = FamilyOf(lepo);
            if (family == null) return new string[0];
            var labels = new string[family.Themes.Length];
            for (int i = 0; i < labels.Length; i++) labels[i] = LocalizationService.Get(family.Themes[i].LocKey);
            return labels;
        }

        internal static int ThemeOf(LevelEditorPlaceableObject lepo)
            => lepo != null && _hosts.TryGetValue(lepo.gameObject.GetInstanceID(), out var h) ? h.Theme : 0;

        internal static void SetTheme(LevelEditorPlaceableObject lepo, int index)
        {
            var host = HostFor(lepo);
            if (host == null) return;
            host.Theme = Mathf.Clamp(index, 0, host.Family.Themes.Length - 1);
            if (host.Lepo != null) _known[PosKey(host.Lepo.transform.position)] = Code(host);
            Apply(host);
            ApplyBalloon(host);
            Reconcile(host);
            ObjectColours.OnThemeChanged(lepo);
        }

        internal static void OnParamMenu(LevelEditorPlaceableObject lepo)
        {
            var host = HostFor(lepo);
            if (host == null) return;
            if (host.Marker == null)
            {
                var marker = FindMarker(lepo.transform.position);
                if (marker != null && TryScale(marker, out var mscale))
                {
                    int code = CodeFromScale(host, mscale);
                    if (code >= 0) { host.Marker = marker; ReadCode(host, code); }
                }
            }
            Apply(host);
            ApplyBalloon(host);
        }

        internal static void OnCloned(LevelEditorPlaceableObject clone, LevelEditorPlaceableObject source)
        {
            if ((IdentifierObjects.InEditor() && !Enabled) || source == null || !IsThemable(clone)) return;
            if (!_hosts.TryGetValue(source.gameObject.GetInstanceID(), out var from))
            {
                Plugin.Log.LogInfo($"cloned {clone.name}, nothing on record for the original so it comes out stock");
                return;
            }
            if (from.Theme <= 0 && !from.NoBalloon) return;

            var host = HostFor(clone);
            if (host == null) return;
            host.Theme = from.Theme;
            host.NoBalloon = from.NoBalloon;
            Apply(host);
            ApplyBalloon(host);
            Plugin.Log.LogInfo($"clone of {clone.name} keeps theme {host.Theme}{(host.NoBalloon ? ", balloon off" : "")}");
        }

        internal static void OnPlaced(LevelEditorPlaceableObject lepo)
        {
            if ((IdentifierObjects.InEditor() && !Enabled) || lepo == null || !_hosts.TryGetValue(lepo.gameObject.GetInstanceID(), out var host)) return;
            Apply(host);
            ApplyBalloon(host);
            Reconcile(host);
        }

        internal static void ResetEditorState()
        {
            _hosts.Clear();
            _unclaimed.Clear();
            _assets.Clear();
            _tick = 0;

            List<int> gone = null;
            foreach (var kv in _stock)
                if (kv.Value.Renderer == null) (gone ?? (gone = new List<int>())).Add(kv.Key);
            if (gone == null) return;
            foreach (int id in gone) _stock.Remove(id);
        }

        internal static void Tick()
        {
            if (!Enabled || _hosts.Count == 0 || ++_tick % 15 != 0) return;
            Prune();
            Reassert();
            CarryLooks.Tick();
            foreach (var kv in _hosts)
            {
                var host = kv.Value;
                FollowActive(host);
                if (host.Family.Tiles && host.Theme > 0) ApplyTiles(host, false);
                if (host.Applied > 0 && host.Family.Themes[host.Applied].Forcefield != null) ForcefieldArt.Fit(host.Root);
                if (host.Applied > 0 && host.Family.Themes[host.Applied].DoorDash != null && DoorDashArt.Fit(host.Root)) Reselect(host);
                if (host.Lepo == null) continue;
                var pos = host.Lepo.transform.position;
                if (host.Marker != null && (host.Marker.transform.position - pos).sqrMagnitude > 0.0001f)
                    host.Marker.transform.position = pos;
            }
        }

        // The game owns some of these materials too and rewrites them behind us. Rather than fight it
        // every frame, notice the overwrite, work out which way it wanted the belt to run, and redo ours.
        private static void Reassert()
        {
            foreach (var kv in _hosts)
            {
                var host = kv.Value;
                if (host.Theme <= 0 || host.Applied != host.Theme || host.Parts == null) continue;
                if (!host.Family.Volatile) continue;

                bool reversed = host.Belt != null && host.Belt.DirectionReversed;
                bool drifted = reversed != host.Reversed;
                if (!drifted)
                {
                    foreach (var part in host.Parts)
                    {
                        if (part == null || part.Extra || part.Expected == null) continue;
                        var view = part.View;
                        if (view == null) continue;
                        var live = view.sharedMaterials;
                        int slot = Mathf.Clamp(host.BeltSlot, 0, live.Length - 1);
                        if (live.Length == 0 || live[slot] == null) continue;
                        if (live[slot].GetInstanceID() == part.Expected.GetInstanceID()) continue;
                        drifted = true;
                        break;
                    }
                }
                if (!drifted) continue;

                host.Reversed = reversed;
                host.Applied = -1;
                Apply(host);
            }
        }

        internal static void Sync()
        {
            Reassert();
            if (IdentifierObjects.InEditor()) { if (Enabled) SyncEditor(); }
            else SyncRound();
        }

        private static void SyncEditor()
        {
            var col = LevelEditorPlaceableObject.Collection;
            if (col == null) return;

            IntPtr inHand = InReticle();

            var targets = new List<LevelEditorPlaceableObject>();
            var markers = new List<LevelEditorPlaceableObject>();
            for (int i = 0; i < col.Count; i++)
            {
                var lepo = col[i];
                if (lepo == null || lepo.Pointer == inHand) continue;
                if (IsMarker(lepo)) markers.Add(lepo);
                else if (IsThemable(lepo)) targets.Add(lepo);
            }

            var claimed = new HashSet<int>();
            foreach (var lepo in targets)
            {
                var pos = lepo.transform.position;

                LevelEditorPlaceableObject found = null;
                foreach (var m in markers)
                {
                    if (m == null || claimed.Contains(m.gameObject.GetInstanceID())) continue;
                    if ((m.transform.position - pos).sqrMagnitude > MatchRadius * MatchRadius) continue;
                    found = m;
                    break;
                }

                if (found == null)
                {
                    if (_hosts.TryGetValue(lepo.gameObject.GetInstanceID(), out var known) && Code(known) > 0)
                    {
                        Apply(known);
                        ApplyBalloon(known);
                        Reconcile(known);
                    }
                    continue;
                }

                claimed.Add(found.gameObject.GetInstanceID());
                HideMarker(found);

                var host = HostFor(lepo);
                if (host == null) continue;
                if (!TryScale(found, out var mark)) continue;
                int code = CodeFromScale(host, mark);
                if (code < 0) continue;

                long key = PosKey(lepo.transform.position);
                if (_known.TryGetValue(key, out int remembered) && remembered != code)
                {
                    // The marker disagrees with what we last wrote here, so it came back defaulted.
                    host.Marker = found;
                    ReadCode(host, remembered);
                    Apply(host);
                    ApplyBalloon(host);
                    Reconcile(host);
                    continue;
                }

                host.Marker = found;
                ReadCode(host, code);
                _known[key] = code;
                Apply(host);
                ApplyBalloon(host);
            }

            // Nothing themable is loaded yet, so every marker would look orphaned. Wait.
            if (targets.Count == 0) { Prune(); return; }

            foreach (var m in markers)
            {
                if (m == null) continue;
                int id = m.gameObject.GetInstanceID();
                if (claimed.Contains(id)) { _unclaimed.Remove(id); continue; }

                _unclaimed.TryGetValue(id, out int strikes);
                if (++strikes < OrphanStrikes) { _unclaimed[id] = strikes; continue; }

                _unclaimed.Remove(id);
                IdentifierObjects.Remove(m);
                Plugin.Log.LogInfo("binned a theme marker, nothing left at its spot to skin");
            }

            Prune();
        }

        private static IntPtr InReticle()
        {
            try
            {
                var reticle = LevelEditorManager.Instance?.GetReticleBase();
                if (reticle == null) return IntPtr.Zero;
                var held = reticle.SelectedObject;
                return held != null ? held.Pointer : IntPtr.Zero;
            }
            catch { return IntPtr.Zero; }
        }

        private static void SyncRound()
        {
            var found = new List<IdentifierMarker>();
            foreach (var m in IdentifierObjects.ReadRound(MarkerName))
            {
                if (!Kind.MatchesRotation(m.Rotation)) continue;
                if (m.Scale.x > Kind.Scale * 4f) continue;
                found.Add(m);
            }
            if (found.Count == 0) return;

            var targets = RoundTargets();
            if (targets.Count == 0)
            {
                Plugin.Log.LogWarning($"object themes: {found.Count} marker(s) in the round json, nothing skinnable to put them on");
                return;
            }

            int done = 0;
            int popped = 0;
            foreach (var m in found)
            {
                Transform best = null;
                float bestSq = MatchRadius * MatchRadius;
                foreach (var t in targets)
                {
                    if (t == null) continue;
                    float d = (t.position - m.Position).sqrMagnitude;
                    if (d > bestSq) continue;
                    bestSq = d;
                    best = t;
                }
                if (best == null) continue;

                var host = HostFor(best, null);
                if (host == null) continue;
                int code = CodeFromScale(host, m.Scale);
                if (code < 0) continue;
                ReadCode(host, code);
                Apply(host);
                ApplyBalloon(host);
                done++;
                if (host.NoBalloon) popped++;
            }
            Plugin.Log.LogInfo($"object themes: {done} object(s) reskinned from round json"
                + (popped > 0 ? $", {popped} cannon(s) without their balloon" : ""));
        }

        private static List<Transform> RoundTargets()
        {
            var list = new List<Transform>();

            var col = LevelEditorPlaceableObject.Collection;
            if (col != null)
                for (int i = 0; i < col.Count; i++)
                    if (IsThemable(col[i])) list.Add(col[i].transform);
            if (list.Count > 0) return list;

            foreach (var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
            {
                if (r == null) continue;
                for (var t = r.transform; t != null; t = t.parent)
                {
                    if (FamilyOf(t.name) == null) continue;
                    if (!list.Contains(t)) list.Add(t);
                    break;
                }
            }
            return list;
        }

        private static Host HostFor(LevelEditorPlaceableObject lepo)
            => lepo == null ? null : HostFor(lepo.transform, lepo);

        private static Host HostFor(Transform root, LevelEditorPlaceableObject lepo)
        {
            int id = root.gameObject.GetInstanceID();
            if (_hosts.TryGetValue(id, out var host))
            {
                host.Root = root;
                if (lepo != null) host.Lepo = lepo;
                return host;
            }

            var family = FamilyOf(root.name);
            if (family == null) return null;

            host = new Host { Family = family, Root = root, Lepo = lepo, Size = SizeOf(root.name) };
            _hosts[id] = host;
            return host;
        }

        private static void Prune()
        {
            List<int> dead = null;
            foreach (var kv in _hosts)
                if (kv.Value.Root == null) (dead ?? (dead = new List<int>())).Add(kv.Key);
            if (dead == null) return;
            foreach (int id in dead)
            {
                var marker = _hosts[id].Marker;
                if (marker != null)
                {
                    IdentifierObjects.Remove(marker);
                    Plugin.Log.LogInfo("themed object got deleted, its theme marker goes with it");
                }
                _hosts.Remove(id);
            }
        }

        private static LevelEditorPlaceableObject FindMarker(Vector3 pos)
        {
            var col = LevelEditorPlaceableObject.Collection;
            if (col == null) return null;
            for (int i = 0; i < col.Count; i++)
            {
                var m = col[i];
                if (!IsMarker(m)) continue;
                if ((m.transform.position - pos).sqrMagnitude <= MatchRadius * MatchRadius) return m;
            }
            return null;
        }

        private static int CodeFromScale(Host host, Vector3 scale)
        {
            int code = Mathf.RoundToInt(scale.y / MarkerScale);
            if (code < 0) return -1;
            if (code >= BalloonBit && !host.Family.Balloon) return -1;
            return code % BalloonBit < host.Family.Themes.Length ? code : -1;
        }

        private static void HideMarker(LevelEditorPlaceableObject marker)
        {
            foreach (var r in marker.GetComponentsInChildren<Renderer>(true))
                if (r != null) r.forceRenderingOff = true;
        }

        private static void Reconcile(Host host)
        {
            if (host.Lepo == null) return;
            int code = Code(host);

            if (host.Marker != null && TryScale(host.Marker, out var live)
                && CodeFromScale(host, live) != code)
            {
                IdentifierObjects.Remove(host.Marker);
                host.Marker = null;
            }

            if (code <= 0)
            {
                if (host.Marker != null) { IdentifierObjects.Remove(host.Marker); host.Marker = null; }
                return;
            }

            if (host.Marker == null)
            {
                var near = FindMarker(host.Lepo.transform.position);
                if (near != null && TryScale(near, out var held) && CodeFromScale(host, held) == code)
                {
                    host.Marker = near;
                    return;
                }
                if (near != null) IdentifierObjects.Remove(near);
                host.Marker = Kind.Spawn(host.Lepo.transform.position, "#FFFFFF", MarkerScale * code);
                return;
            }

            host.Marker.transform.position = host.Lepo.transform.position;
        }

        private static bool Capture(Host host)
        {
            if (host.Parts != null) return true;
            if (host.Root == null) return false;

            var nodes = host.Family.Nodes;
            if (host.Family.Volatile)
            {
                host.Belt = host.Root.GetComponentInChildren<LevelEditorConveyorResponder>(true);
                if (host.Belt != null)
                {
                    host.BeltSlot = host.Belt._materialElement;
                    host.Reversed = host.Belt.DirectionReversed;
                    var belt = host.Belt._conveyorRenderer;
                    if (belt != null) nodes = new[] { belt.gameObject.name };
                }
            }
            var parts = new List<PartState>();
            var slots = new List<int>();
            bool gameScaled = host.Family.Fit == ObjectThemeFit.None
                && host.Root.GetComponentInChildren<LevelEditorDrawableData>(true) != null;
            var renderers = host.Root.GetComponentsInChildren<Renderer>(true);
            var tweens = host.Root.GetComponentsInChildren<BouncingDrumDotween>(true);
            var rotators = host.Root.GetComponentsInChildren<LevelEditorCommonRotator>(true);
            // Node names are matched by prefix and some are prefixes of each other (ENV_Button and
            // ENV_ButtonPlatform), so a renderer only ever belongs to the first node that claims it.
            var claimed = new HashSet<int>();

            for (int i = 0; i < nodes.Length; i++)
            {
                foreach (var r in renderers)
                {
                    if (r == null || !r.gameObject.name.StartsWith(nodes[i], StringComparison.Ordinal)) continue;
                    if (!claimed.Add(r.gameObject.GetInstanceID())) continue;

                    var skinned = r.TryCast<SkinnedMeshRenderer>();
                    var filter = skinned != null ? null : r.GetComponent<MeshFilter>();
                    var live = skinned != null ? skinned.sharedMesh : (filter != null ? filter.sharedMesh : null);
                    if (live == null) continue;

                    int id = r.gameObject.GetInstanceID();
                    if (!_stock.TryGetValue(id, out var part))
                    {
                        var mats = r.sharedMaterials;
                        var stock = new Material[mats.Length];
                        for (int m = 0; m < mats.Length; m++) stock[m] = mats[m];

                        part = new PartState
                        {
                            StockMesh = live,
                            StockMaterials = stock,
                            StockPos = r.transform.localPosition,
                            StockScale = r.transform.localScale,
                            StockRot = r.transform.localRotation,
                        };
                        foreach (var rot in rotators)
                        {
                            var flips = rot._transformsToFlip;
                            var defaults = rot._defaultFlipScales;
                            if (flips == null || defaults == null) continue;
                            for (int f = 0; f < flips.Length && f < defaults.Length; f++)
                                if (flips[f] != null && flips[f].gameObject.GetInstanceID() == id)
                                {
                                    part.Flipper = rot;
                                    part.FlipSlot = f;
                                    part.StockScale = defaults[f];
                                }
                        }
                        _stock[id] = part;
                    }

                    part.Node = r.transform;
                    part.Renderer = r;
                    part.GameScaled = gameScaled;
                    part.Filter = filter;
                    part.Skinned = skinned;

                    foreach (var tween in tweens)
                    {
                        if (tween == null || tween._bounceObject == null) continue;
                        if (tween._bounceObject.GetInstanceID() != r.gameObject.GetInstanceID()) continue;
                        part.Tween = tween;
                        part.TweenExpanded = tween._expandedScale;
                        break;
                    }

                    // A tween owns the node's scale, so that node has to stay the one we render on.
                    if (host.Family.Fit != ObjectThemeFit.None && part.Tween == null
                        && r.GetComponentInChildren<Collider>(true) != null)
                        Guard(part);

                    parts.Add(part);
                    slots.Add(i);
                }
            }

            if (parts.Count == 0)
            {
                Plugin.Log.LogWarning($"object themes: {host.Root.name} matched a family but none of its art nodes are there, leaving it alone");
                return false;
            }

            var donor = parts[0];
            for (int i = 0; i < parts.Count; i++)
                if (slots[i] == host.Family.ExtrasNode) { donor = parts[i]; break; }

            for (int e = 0; e < host.Family.Extras; e++)
            {
                parts.Add(BuildExtra(donor, "BettrFG_ThemePart" + e));
                slots.Add(nodes.Length + e);
            }

            host.Parts = parts.ToArray();
            host.Slots = slots.ToArray();
            foreach (var part in host.Parts)
            {
                if (part == null || part.Extra || part.Node == null) continue;
                foreach (var other in host.Parts)
                {
                    if (other == null || other == part || other.Node == null) continue;
                    for (var t = part.Node.parent; t != null; t = t.parent)
                        if (t == other.Node) { part.Parented = true; break; }
                    if (part.Parented) break;
                }
            }
            return true;
        }

        private static void Guard(PartState part)
        {
            part.Guarded = true;
            if (part.Proxy != null) return;

            var go = new GameObject("BettrFG_ThemeArt");
            go.transform.SetParent(part.Node, false);
            part.Proxy = go.transform;
            part.ProxyFilter = go.AddComponent<MeshFilter>();
            part.ProxyRenderer = go.AddComponent<MeshRenderer>();
            part.ProxyRenderer.enabled = false;
        }

        private static PartState BuildExtra(PartState donor, string name)
        {
            var parent = donor.Node != null ? donor.Node.parent : null;
            var existing = parent != null ? parent.Find(name) : null;

            GameObject go;
            if (existing != null) go = existing.gameObject;
            else
            {
                go = new GameObject(name);
                go.transform.SetParent(parent, false);
            }

            go.transform.localPosition = donor.StockPos;
            go.transform.localRotation = donor.StockRot;
            go.transform.localScale = donor.StockScale;

            var filter = go.GetComponent<MeshFilter>() ?? go.AddComponent<MeshFilter>();
            var renderer = go.GetComponent<MeshRenderer>() ?? go.AddComponent<MeshRenderer>();
            renderer.enabled = false;

            return new PartState
            {
                Node = go.transform,
                Filter = filter,
                Renderer = renderer,
                StockMesh = donor.StockMesh,
                StockMaterials = new Material[0],
                StockPos = donor.StockPos,
                StockScale = donor.StockScale,
                StockRot = donor.StockRot,
                Extra = true,
            };
        }

        // Mesh bounds expressed in the art node's PARENT space. Several art nodes are authored
        // upside down (the swinging axe), so aligning raw mesh bounds moves them the wrong way.
        private static Bounds InParent(Bounds b, Quaternion rot, Vector3 scale)
        {
            var c = b.center;
            var e = b.extents;
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    c.x + ((i & 1) == 0 ? -e.x : e.x),
                    c.y + ((i & 2) == 0 ? -e.y : e.y),
                    c.z + ((i & 4) == 0 ? -e.z : e.z));
                var p = rot * Vector3.Scale(corner, scale);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            var fitted = new Bounds();
            fitted.SetMinMax(min, max);
            return fitted;
        }

        private static void ApplyTiles(Host host, bool force)
        {
            if (host.Root == null) return;
            var theme = host.Family.Themes[Mathf.Clamp(host.Theme, 0, host.Family.Themes.Length - 1)];
            if (host.Theme > 0 && theme.IceTile != null)
            {
                var ice = theme.IceTile;
                var iceMeshes = new Mesh[ice.Meshes.Length];
                for (int i = 0; i < iceMeshes.Length; i++)
                    if ((iceMeshes[i] = LoadMesh(ice.Meshes[i])) == null) return;
                var iceMat = LoadMaterial(ice.Material);
                var normal = AssetLoads.Get<Texture>(ice.Normal);
                if (iceMat == null || normal == null) return;
                RespawnTileArt.ApplyIce(host.Root, iceMeshes, iceMat, normal, ice, force);
                host.Applied = host.Theme;
                return;
            }
            var keys = host.Theme > 0 ? KeysFor(theme, host.Size) : null;
            var mesh = keys != null ? LoadMesh(keys[0]) : null;
            if (keys != null && mesh == null) return;
            var mats = mesh != null ? MaterialsFor(theme, 0) : null;
            if (keys != null && mats == null) return;
            var place = mesh != null ? PlacementFrom(keys[0]) : null;
            var lift = place != null && place.Length == 3 ? new Vector3(place[0], place[1], place[2]) : Vector3.zero;
            RespawnTileArt.Apply(host.Root, mesh, mats?[0], theme.Atlas, theme.Grey, lift, force);
            host.Applied = host.Theme;
        }

        internal const string PickName = "BettrFG_ThemePick";

        private static void Apply(Host host)
        {
            ApplyTheme(host);
            Reselect(host);
        }

        private static bool IsThemeArt(Transform t, Transform root)
        {
            for (var p = t; p != null && p != root; p = p.parent)
                if (p.name.StartsWith("BettrFG_Theme", StringComparison.Ordinal)) return true;
            return false;
        }

        private static void Reselect(Host host)
        {
            var lepo = host.Lepo;
            if (lepo == null || host.Root == null) return;

            var art = new List<MeshRenderer>();
            var visible = new List<Renderer>();
            foreach (var r in host.Root.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (r.enabled && !r.forceRenderingOff) visible.Add(r);
                if (IsThemeArt(r.transform, host.Root)) art.Add(r);
            }

            if (art.Count > 0)
            {
                if (!host.Outlined) { host.OutlineStock = lepo._outlineMultiRenderers; host.Outlined = true; }
                IdentifierObjects.OutlineWith(lepo, visible);
                foreach (var r in art) Pick(lepo, r);
            }
            else if (host.Outlined)
            {
                lepo._outlineMultiRenderers = host.OutlineStock;
                if (host.OutlineStock != null && host.OutlineStock.Length > 0) lepo.UseUserDefinedOutlineRenderers();
                else lepo.UseDefaultOutlineRenderers();
                host.Outlined = false;
            }

            var outliner = LevelEditorOutlineManager.Instance;
            if (outliner != null) outliner.RefreshOutlineRenderers(lepo.gameObject);
        }

        private static void Pick(LevelEditorPlaceableObject lepo, MeshRenderer art)
        {
            var filter = art.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return;

            var holder = art.transform.Find(PickName);
            if (holder == null)
            {
                holder = new GameObject(PickName).transform;
                holder.SetParent(art.transform, false);
                var box = holder.gameObject.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.center = filter.sharedMesh.bounds.center;
                box.size = filter.sharedMesh.bounds.size;
            }
            var collider = holder.GetComponent<BoxCollider>();
            if (!LevelEditorPlaceableObject.ColliderToPlaceable.ContainsKey(collider)) LevelEditorPlaceableObject.RegisterColliderForPlaceable(lepo, collider);
        }

        private static void ApplyTheme(Host host)
        {
            if (host.Family.Tiles) { ApplyTiles(host, true); return; }
            if (host.Family.Carry)
            {
                CarryLooks.Apply(host.Root, host.Theme > 0 ? host.Family.Themes[Mathf.Min(host.Theme, host.Family.Themes.Length - 1)].Medium[0] : null);
                host.Applied = host.Theme;
                return;
            }

            if (host.Parts != null)
                foreach (var part in host.Parts)
                    if (part == null || part.Renderer == null || part.Node == null)
                    { host.Parts = null; host.Applied = -1; break; }

            if (host.Applied == host.Theme || !Capture(host)) return;

            var family = host.Family;
            var theme = family.Themes[Mathf.Clamp(host.Theme, 0, family.Themes.Length - 1)];
            var keys = !theme.Prop ? KeysFor(theme, host.Size)
                : host.Size <= 1 ? theme.Small : host.Size == 2 ? theme.Medium : theme.Large;

            if (family.Clutter != null)
                foreach (var t in host.Root.GetComponentsInChildren<Transform>(true))
                    if (Array.IndexOf(family.Clutter, t.name) >= 0) t.gameObject.SetActive(host.Theme <= 0 || keys == null);

            if (theme.Attach != null)
            {
                foreach (var part in host.Parts) Hide(part);
                GravityZoneArt.Apply(host.Root, theme.Attach);
                host.Applied = host.Theme;
                return;
            }
            GravityZoneArt.Clear(host.Root);

            if (theme.Forcefield != null)
            {
                if (!ForcefieldArt.Apply(host.Root, theme.Forcefield)) return;
                foreach (var part in host.Parts) Hide(part);
                host.Applied = host.Theme;
                return;
            }
            ForcefieldArt.Clear(host.Root);

            if (theme.DoorDash != null)
            {
                if (!DoorDashArt.Apply(host.Root, theme.DoorDash)) return;
                foreach (var part in host.Parts) Hide(part);
                host.Applied = host.Theme;
                return;
            }
            DoorDashArt.Clear(host.Root);

            if (theme.Prop && keys != null)
            {
                if (!AttachProp(host, keys)) return;
                foreach (var part in host.Parts) Hide(part);
                host.Applied = host.Theme;
                return;
            }
            ClearProp(host);

            if (host.Theme <= 0 || (keys == null && theme.Materials == null))
            {
                foreach (var part in host.Parts) Restore(part);
                host.Applied = 0;
                Settle(host);
                return;
            }

            var meshes = new Mesh[host.Parts.Length];
            for (int i = 0; i < host.Parts.Length; i++)
            {
                int slot = host.Slots[i];
                if (host.Parts[i] == null || keys == null || slot >= keys.Length
                    || string.IsNullOrEmpty(keys[slot]) || keys[slot] == ObjectThemeTable.Hidden) continue;
                meshes[i] = LoadMesh(keys[slot]);
                if (meshes[i] == null) return;
            }

            for (int i = 0; i < host.Parts.Length; i++)
            {
                var part = host.Parts[i];
                if (part == null) continue;

                var mesh = meshes[i];
                string keyed = keys != null && host.Slots[i] < keys.Length ? keys[host.Slots[i]] : null;
                if (keyed == ObjectThemeTable.Hidden) { Hide(part); continue; }
                if (mesh == null && (keys != null || part.Extra)) { Restore(part); continue; }

                var mats = host.Reversed && !string.IsNullOrEmpty(theme.Reverse)
                    ? Resolve(theme.Reverse)
                    : MaterialsFrom(keyed) ?? MaterialsFor(theme, host.Slots[i]);
                if (mats == null) return;

                float flip = theme.MirrorX ? -1f : 1f;
                var rot = theme.RotateX != 0f || theme.RotateZ != 0f
                    ? part.StockRot * Quaternion.Euler(theme.RotateX, 0f, theme.RotateZ)
                    : part.StockRot;
                float themeScale = theme.Scale > 0.0001f ? theme.Scale : 1f;
                var scale = new Vector3(part.StockScale.x * flip, part.StockScale.y, part.StockScale.z) * themeScale;

                // Always write the mesh, never just the materials: a material-only theme picked after
                // a mesh theme would otherwise keep the previous theme's geometry and UVs.
                var live = mesh ?? part.StockMesh;
                part.SetMesh(live);
                if (part.Guarded)
                {
                    part.ProxyFilter.sharedMesh = live;
                    part.Renderer.forceRenderingOff = true;
                    part.ProxyRenderer.enabled = true;
                    part.ProxyWanted = true;
                }
                else part.Renderer.enabled = true;

                var view = part.View;
                int subs = Mathf.Max(Mathf.Max(1, live.subMeshCount), part.StockMaterials.Length);
                var arr = new Il2CppReferenceArray<Material>(subs);
                for (int sm = 0; sm < subs; sm++)
                {
                    var pick = family.Volatile && sm != host.BeltSlot ? null : mats[Mathf.Min(sm, mats.Length - 1)];
                    if (pick == null)
                    {
                        arr[sm] = sm < part.StockMaterials.Length ? part.StockMaterials[sm] : part.StockMaterials[0];
                        continue;
                    }
                    if (family.Volatile)
                        pick = BeltMaterial(pick, part.StockMaterials[Mathf.Min(sm, part.StockMaterials.Length - 1)],
                            theme.FlipArt, theme.Tiling);
                    if (family.Drawable)
                    {
                        var inst = new Material(pick) { name = "BettrFG_" + pick.name };
                        var from = sm < part.StockMaterials.Length ? part.StockMaterials[sm] : null;
                        if (from != null && !inst.HasProperty(WorldScaleId) && from.HasProperty(MainTexId) && inst.HasProperty(MainTexId))
                        {
                            inst.SetTextureScale(MainTexId, from.GetTextureScale(MainTexId));
                            inst.SetTextureOffset(MainTexId, from.GetTextureOffset(MainTexId));
                        }
                        pick = inst;
                    }
                    arr[sm] = pick;
                }
                view.sharedMaterials = arr;
                part.Expected = subs > 0 ? arr[Mathf.Clamp(host.BeltSlot, 0, subs - 1)] : null;
                if (family.Drawable) PushDrawableScale(host);

                var place = PlacementFrom(keyed);
                var offset = place != null && place.Length == 3
                    ? new Vector3(place[0], place[1], place[2])
                    : Vector3.zero;

                if (part.Parented)
                {
                    part.Place(offset == Vector3.zero ? part.StockPos : offset, rot, part.StockScale);
                    continue;
                }

                if (place != null && place.Length == 10)
                {
                    part.Place(new Vector3(place[0], place[1], place[2]),
                        new Quaternion(place[3], place[4], place[5], place[6]),
                        new Vector3(place[7], place[8], place[9]));
                    continue;
                }

                if (mesh == null || family.Fit == ObjectThemeFit.None)
                {
                    part.Place(part.StockPos + rot * Vector3.Scale(offset, scale), rot, scale);
                    continue;
                }

                var stockBounds = InParent(part.StockMesh.bounds, part.StockRot, part.StockScale);
                var artBounds = InParent(mesh.bounds, rot, scale);
                // A theme whose keys carry offsets is one object split across several nodes, so it has
                // to be fitted as a whole rather than each piece being squared up on its own.
                if (family.Extras > 0 || HasOffsets(keys))
                    artBounds = AssemblyBounds(host, meshes, keys, rot, scale);

                if (theme.Stretch)
                {
                    var fit = new Vector3(
                        artBounds.size.x > 0.0001f ? stockBounds.size.x / artBounds.size.x : 1f,
                        artBounds.size.y > 0.0001f ? stockBounds.size.y / artBounds.size.y : 1f,
                        artBounds.size.z > 0.0001f ? stockBounds.size.z / artBounds.size.z : 1f);
                    var local = Quaternion.Inverse(rot) * fit;
                    local = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
                    part.Place(part.StockPos + stockBounds.center - Vector3.Scale(artBounds.center, fit),
                        rot, Vector3.Scale(scale, local));
                    continue;
                }

                if (family.Fit == ObjectThemeFit.Length)
                {
                    float want = Mathf.Max(Mathf.Abs(stockBounds.min.z), Mathf.Abs(stockBounds.max.z));
                    float have = Mathf.Max(Mathf.Abs(artBounds.min.z), Mathf.Abs(artBounds.max.z));
                    float lz = have > 0.0001f ? want / have : 1f;
                    part.Place(part.StockPos + rot * Vector3.Scale(offset, scale * lz), rot, scale * lz);
                    continue;
                }

                if (family.Fit == ObjectThemeFit.Center)
                {
                    part.Place(part.StockPos + (stockBounds.center - artBounds.center)
                        + rot * Vector3.Scale(offset, scale), rot, scale);
                    continue;
                }

                if (family.Fit == ObjectThemeFit.Hanging)
                {
                    // The end of the art nearest its own origin is the mount the obstacle swings from.
                    float dy = Mathf.Abs(stockBounds.min.y) <= Mathf.Abs(stockBounds.max.y)
                        ? stockBounds.min.y - artBounds.min.y
                        : stockBounds.max.y - artBounds.max.y;
                    part.Place(part.StockPos + Vector3.up * dy + rot * Vector3.Scale(offset, scale), rot, scale);
                    continue;
                }

                float k = artBounds.extents.x > 0.0001f ? stockBounds.extents.x / artBounds.extents.x : 1f;
                var fitted = new Bounds();
                fitted.SetMinMax(artBounds.min * k, artBounds.max * k);
                part.Place(part.StockPos + new Vector3(
                    stockBounds.center.x - fitted.center.x,
                    stockBounds.min.y - fitted.min.y,
                    stockBounds.center.z - fitted.center.z)
                    + rot * Vector3.Scale(offset, scale * k), rot, scale * k);
            }

            host.Applied = host.Theme;
            Settle(host);
            FollowActive(host);
        }

        private const string PropName = "BettrFG_ThemeProp";

        private static bool AttachProp(Host host, string[] keys)
        {
            var prefabs = new GameObject[keys.Length];
            var meshes = new Mesh[keys.Length];
            var swapNames = new string[keys.Length][];
            var swaps = new Material[keys.Length][];
            for (int k = 0; k < keys.Length; k++)
            {
                bool meshPart = keys[k][0] == '#';
                var bits = keys[k].TrimStart('#').Split('|');
                if (meshPart) meshes[k] = LoadMesh(bits[0] + "|" + bits[1]);
                else prefabs[k] = AssetLoads.Get<GameObject>(bits[0]);
                swapNames[k] = !meshPart && bits.Length > 2 && bits[1].Length > 0 ? bits[1].Split(',') : new string[0];
                swaps[k] = bits.Length > 2 && bits[2].Length > 0 ? Array.ConvertAll(bits[2].Split(','), LoadMaterial) : new Material[0];
                if ((meshPart ? meshes[k] == null : prefabs[k] == null) || Array.IndexOf(swaps[k], null) >= 0) return false;
            }

            ClearProp(host);
            for (int k = 0; k < keys.Length; k++) AttachPropPart(host.Parts[0].Node.parent.parent, keys[k], prefabs[k], meshes[k], swapNames[k], swaps[k]);
            return true;
        }

        private static void AttachPropPart(Transform parent, string key, GameObject prefab, Mesh mesh, string[] swapNames, Material[] swaps)
        {
            var holder = new GameObject(PropName);
            holder.SetActive(false);
            holder.transform.SetParent(parent, false);
            var place = PlacementFrom(key);
            if (place != null && place.Length == 10)
            {
                holder.transform.localPosition = new Vector3(place[0], place[1], place[2]);
                holder.transform.localRotation = new Quaternion(place[3], place[4], place[5], place[6]);
                holder.transform.localScale = new Vector3(place[7], place[8], place[9]);
            }

            if (mesh != null)
            {
                holder.AddComponent<MeshFilter>().sharedMesh = mesh;
                holder.AddComponent<MeshRenderer>().sharedMaterials = Wrap(swaps);
                holder.SetActive(true);
                return;
            }

            var art = UnityEngine.Object.Instantiate(prefab, holder.transform, false);
            art.transform.localPosition = Vector3.zero;
            art.transform.localRotation = Quaternion.identity;
            foreach (var c in art.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in art.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in art.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in art.GetComponentsInChildren<ParticleSystem>(true)) if (c != null) UnityEngine.Object.DestroyImmediate(c.gameObject);
            foreach (var t in art.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("ENV_Propeller", StringComparison.Ordinal)) t.gameObject.SetActive(false);

            foreach (var r in art.GetComponentsInChildren<Renderer>(true))
                for (int i = 0; i < swapNames.Length && i < swaps.Length; i++)
                    if (r.name.StartsWith(swapNames[i], StringComparison.Ordinal)) r.sharedMaterial = swaps[i];
            holder.SetActive(true);
        }

        private static void ClearProp(Host host)
        {
            var parent = host.Parts?[0].Node.parent?.parent;
            if (parent == null) return;
            for (var old = parent.Find(PropName); old != null; old = parent.Find(PropName))
                UnityEngine.Object.DestroyImmediate(old.gameObject);
        }

        private static void Hide(PartState part)
        {
            if (part.Guarded)
            {
                part.ProxyWanted = false;
                part.ProxyRenderer.enabled = false;
                if (part.Proxy != null) part.Proxy.gameObject.SetActive(false);
            }
            part.Renderer.forceRenderingOff = true;
        }

        private static void Restore(PartState part)
        {
            if (part == null) return;
            if (part.Extra) { part.Renderer.enabled = false; return; }

            if (part.Guarded)
            {
                part.ProxyWanted = false;
                part.ProxyRenderer.enabled = false;
                if (part.Proxy != null) part.Proxy.gameObject.SetActive(false);
                part.Renderer.forceRenderingOff = false;
                return;
            }

            part.Renderer.forceRenderingOff = false;
            part.SetMesh(part.StockMesh);
            part.Renderer.sharedMaterials = Wrap(part.StockMaterials);
            if (part.GameScaled) return;
            part.Node.localPosition = part.StockPos;
            part.SetScale(part.StockScale);
            part.Node.localRotation = part.StockRot;
        }

        // A themed art node usually needs the game's own local transform too, not just its mesh:
        // the retro chevron sign is moved 5 units up, scaled 9x and laid flat. Key segment 4 carries
        // "px,py,pz,qx,qy,qz,qw,sx,sy,sz" lifted straight off the game's themed placeable.
        private static readonly Dictionary<string, float[]> _placements = new Dictionary<string, float[]>();

        private static float[] PlacementFrom(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_placements.TryGetValue(key, out var hit)) return hit;

            float[] trs = null;
            var bits = key.Split('|');
            if (bits.Length >= 4 && bits[3].Length > 0)
            {
                var nums = bits[3].Split(',');
                if (nums.Length == 10 || nums.Length == 3)
                {
                    trs = new float[nums.Length];
                    for (int i = 0; i < nums.Length; i++)
                        if (!float.TryParse(nums[i], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out trs[i]))
                        { trs = null; break; }
                }
                if (trs == null) Plugin.Log.LogWarning($"object themes: placement on {key} isn't three or ten numbers, ignoring it");
            }
            _placements[key] = trs;
            return trs;
        }

        private static bool HasOffsets(string[] keys)
        {
            if (keys == null) return false;
            foreach (var k in keys)
            {
                var o = PlacementFrom(k);
                if (o != null && o.Length == 3) return true;
            }
            return false;
        }

        private static Bounds AssemblyBounds(Host host, Mesh[] meshes, string[] keys, Quaternion rot, Vector3 scale)
        {
            bool any = false;
            var min = Vector3.zero;
            var max = Vector3.zero;
            for (int i = 0; i < host.Parts.Length; i++)
            {
                var m = meshes[i];
                if (m == null) continue;
                int slot = host.Slots[i];
                var o = keys != null && slot < keys.Length ? PlacementFrom(keys[slot]) : null;
                var shift = o != null && o.Length == 3
                    ? rot * Vector3.Scale(new Vector3(o[0], o[1], o[2]), scale)
                    : Vector3.zero;
                var b = InParent(m.bounds, rot, scale);
                var lo = b.min + shift;
                var hi = b.max + shift;
                if (!any) { min = lo; max = hi; any = true; continue; }
                min = Vector3.Min(min, lo);
                max = Vector3.Max(max, hi);
            }
            var union = new Bounds();
            if (any) union.SetMinMax(min, max);
            return union;
        }

        private static Material[] MaterialsFrom(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var bits = key.Split('|');
            if (bits.Length < 3 || bits[2].Length == 0) return null;
            return Resolve(bits[2]);
        }

        private static Material[] MaterialsFor(ObjectTheme theme, int part)
        {
            return Resolve(theme.Materials[Mathf.Min(part, theme.Materials.Length - 1)]);
        }

        private static Material[] Resolve(string spec)
        {
            if (!_matLists.TryGetValue(spec, out var guids))
            {
                guids = spec.Split(',');
                _matLists[spec] = guids;
            }

            var mats = new Material[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                if (guids[i].Length == 0) continue;
                mats[i] = LoadMaterial(guids[i]);
                if (mats[i] == null) return null;
            }
            return mats;
        }

        private static readonly int WorldScaleId = Shader.PropertyToID("_WorldScale");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int OffsetUId = Shader.PropertyToID("_OffsetU");
        private static readonly Dictionary<string, Material> _belts = new Dictionary<string, Material>();

        private static Material BeltMaterial(Material pick, Material template, bool flip, float tiling)
        {
            bool scrolls = pick.HasProperty(OffsetUId);
            if (scrolls && !flip && tiling <= 0f) return pick;
            string key = pick.GetInstanceID() + (flip ? "f" : "");
            if (_belts.TryGetValue(key, out var hit) && hit != null) return hit;

            Material mat;
            Vector2 st;
            if (scrolls)
            {
                mat = new Material(pick);
                st = pick.mainTextureScale;
            }
            else
            {
                mat = new Material(template);
                mat.SetTexture(MainTexId, pick.GetTexture(MainTexId));
                mat.SetTexture("_NormalMap", pick.GetTexture("_BumpMap"));
                mat.SetTexture("_MetallicRoughness", pick.GetTexture("_MetallicSmoothness"));
                st = new Vector2(Mathf.Abs(pick.mainTextureScale.x), -Mathf.Abs(pick.mainTextureScale.y));
            }
            if (tiling > 0f) st = new Vector2(tiling, Mathf.Sign(st.y) * tiling);
            if (flip) st.y = -st.y;
            mat.mainTextureScale = st;
            mat.mainTextureOffset = new Vector2(0.5f * (1f - st.x), mat.mainTextureOffset.y);
            mat.name = "BettrFG_" + pick.name;
            _belts[key] = mat;
            return mat;
        }

        internal static void OnDrawableRescaled(LevelEditorDrawableData drawable)
        {
            if (_hosts.Count == 0 || drawable == null) return;
            var lepo = drawable.Placeable;
            if (lepo == null) return;
            if (!_hosts.TryGetValue(lepo.gameObject.GetInstanceID(), out var host)) return;
            if (host.Family.Drawable) PushDrawableScale(host);

            var themed = host.Family.Themes[Mathf.Clamp(host.Theme, 0, host.Family.Themes.Length - 1)];
            if (themed.Attach != null) GravityZoneArt.Apply(host.Root, themed.Attach);
            if (themed.Forcefield != null) ForcefieldArt.Fit(host.Root);
        }

        private static void PushDrawableScale(Host host)
        {
            if (host.Root == null || host.Parts == null) return;
            var drawable = host.Root.GetComponentInChildren<LevelEditorDrawableData>(true);
            if (drawable == null) return;

            var scale = drawable.GetShaderScale();
            foreach (var part in host.Parts)
            {
                if (part == null) continue;
                foreach (var mat in part.View.sharedMaterials)
                    if (mat != null && mat.HasProperty(WorldScaleId)) mat.SetVector(WorldScaleId, scale);
            }
        }

        // Keep each guarded proxy's visibility tied to the node it stands in for.
        private static void FollowActive(Host host)
        {
            if (host.Parts == null) return;
            foreach (var part in host.Parts)
            {
                if (part == null || !part.Guarded || part.Proxy == null || part.Node == null) continue;
                bool live = part.Node.gameObject.activeInHierarchy && part.ProxyWanted;
                if (part.Proxy.gameObject.activeSelf != live) part.Proxy.gameObject.SetActive(live);
            }
        }

        private static void Settle(Host host)
        {
            if (host.Root == null) return;

            var controller = host.Root.GetComponentInChildren<LevelEditorObjectMaterialController>(true);
            if (controller != null && controller._visible) controller.CacheOriginalMaterials();

            foreach (var part in host.Parts)
            {
                if (part == null || part.Tween == null) continue;
                var live = part.Art.localScale;
                float k = part.StockScale.x > 0.0001f ? live.x / part.StockScale.x : 1f;
                part.Tween._initialScale = live;
                part.Tween._expandedScale = part.TweenExpanded * k;
            }
        }

        private static Il2CppReferenceArray<Material> Wrap(Material[] mats)
        {
            var arr = new Il2CppReferenceArray<Material>(mats.Length);
            for (int i = 0; i < mats.Length; i++) arr[i] = mats[i];
            return arr;
        }

        internal static Mesh LoadMesh(string key)
        {
            if (_assets.TryGetValue(key, out var hit)) return hit as Mesh;

            var bits = key.Split('|');
            string guid = bits[0];
            string wanted = bits.Length > 1 && bits[1].Length > 0 ? bits[1] : null;
            Mesh mesh = null;
            GameObject root = null;
            try
            {
                if (wanted == null) mesh = AssetLoads.Get<Mesh>(guid);
                else
                {
                    root = AssetLoads.Get<GameObject>(guid);
                    if (root != null)
                    {
                        foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                            if (f != null && f.sharedMesh != null && f.sharedMesh.name == wanted) { mesh = f.sharedMesh; break; }
                        // Blend-shaped art (every flipper paddle) hangs off a SkinnedMeshRenderer inside
                        // the fbx, not a MeshFilter, and a MeshFilter-only scan silently found nothing.
                        if (mesh == null)
                            foreach (var sk in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                                if (sk != null && sk.sharedMesh != null && sk.sharedMesh.name == wanted) { mesh = sk.sharedMesh; break; }
                    }
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"object themes: mesh {key} wouldn't load: {ex.Message}"); }

            if (mesh != null) _assets[key] = mesh;
            else if (root != null) Plugin.Log.LogWarning($"object themes: {guid} loaded but has no mesh called {wanted}");
            return mesh;
        }

        private static readonly int UseDetailId = Shader.PropertyToID("_UseDetailAlbedo");
        private static readonly int DetailTexId = Shader.PropertyToID("_AlbedoDetail");

        internal static Material LoadMaterial(string guid)
        {
            var mat = AssetLoads.Get<Material>(guid);
            if (mat != null && mat.HasProperty(UseDetailId) && mat.HasProperty(DetailTexId)
                && mat.GetFloat(UseDetailId) == 0f && mat.GetTexture(DetailTexId) != null)
                mat.SetFloat(UseDetailId, 1f);
            return mat;
        }
    }
}
