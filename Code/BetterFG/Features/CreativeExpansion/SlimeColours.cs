using System;
using System.Collections.Generic;
using BetterFG.UI.Windows.Creative;
using BetterFG.Utilities;
using FG.Common;
using LevelEditor;
using UnityEngine;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class SlimeColours
    {
        internal static bool Enabled => FeatureCreativeExpansion.On("colouring");

        private static readonly MarkerKind Kind = MarkerKinds.SlimeColour;
        private const float MatchRadius = 0.3f;

        private const string Stock = "#FF8FFD";

        private static readonly int ColourId = Shader.PropertyToID("_Color");
        private static readonly int Colour2Id = Shader.PropertyToID("_Color2");
        private const string GoopVisualName = "GoopVisual";

        private sealed class Host
        {
            public LevelEditorPlaceableObject Lepo;
            public Transform Root;
            public Color Slot;
            public LevelEditorPlaceableObject Marker;
            public Renderer View;
        }

        private static readonly Dictionary<int, Host> _hosts = new Dictionary<int, Host>();
        private static MaterialPropertyBlock _block;
        private static int _tick;

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            c.a = 1f;
            return c;
        }

        internal static bool IsHost(LevelEditorPlaceableObject lepo) => lepo != null && IsHostName(lepo.name);

        private static bool IsHostName(string n)
            => n.StartsWith("Placeable_Floor_Goop_", StringComparison.Ordinal)
               || n.StartsWith("Placeable_Floor_Vinyl_Goop", StringComparison.Ordinal);

        internal static bool IsMarker(LevelEditorPlaceableObject lepo) => Kind.Matches(lepo);

        internal static LevelEditorPlaceableObject OwnerOf(LevelEditorPlaceableObject marker)
        {
            if (marker == null) return null;
            foreach (var kv in _hosts)
                if (kv.Value.Marker != null && kv.Value.Marker.Pointer == marker.Pointer) return kv.Value.Lepo;
            return null;
        }

        internal static void OpenPicker(LevelEditorPlaceableObject lepo)
        {
            var host = HostFor(lepo);
            if (host.Marker == null)
            {
                if (host.Slot.a <= 0f) host.Slot = Hex(Stock);
                Reconcile(lepo);
            }
            if (host.Marker == null) return;

            var param = BatchTargets.GetColourParam(host.Marker);
            if (param == null)
            {
                Plugin.Log.LogWarning($"slime colour: marker for {lepo.name} has no colour parameter, no picker to open");
                return;
            }

            LevelEditorColourPickerParameterViewModel.OpenColourPickerScreen(param);
        }

        internal static void OnMarkerColourChanged(LevelEditorPlaceableObject marker)
        {
            foreach (var kv in _hosts)
            {
                var host = kv.Value;
                if (host.Marker == null || host.Marker.Pointer != marker.Pointer) continue;
                var colour = BatchTargets.GetColourParam(marker);
                if (colour == null) return;
                var c = colour.CurrentColour;
                c.a = 1f;
                host.Slot = c;
                if (host.Lepo != null) Apply(host.Lepo.transform, c);
                TintVfx(c);
                return;
            }
        }

        internal static void OnCloned(LevelEditorPlaceableObject clone, LevelEditorPlaceableObject source)
        {
            if ((IdentifierObjects.InEditor() && !Enabled) || clone == null || source == null || !IsHost(clone)) return;
            if (!_hosts.TryGetValue(source.gameObject.GetInstanceID(), out var from)) return;
            if (from.Slot.a <= 0f) return;

            _hosts[clone.gameObject.GetInstanceID()] = new Host { Lepo = clone, Root = clone.transform, Slot = from.Slot };
            Apply(clone.transform, from.Slot);
        }

        internal static void OnPlaced(LevelEditorPlaceableObject lepo)
        {
            if ((IdentifierObjects.InEditor() && !Enabled) || !IsHost(lepo) || !_hosts.ContainsKey(lepo.gameObject.GetInstanceID())) return;
            Reconcile(lepo);
        }

        internal static void OnDestroyed(LevelEditorPlaceableObject lepo)
        {
            if (!IsHost(lepo)) return;
            int id = lepo.gameObject.GetInstanceID();
            if (!_hosts.TryGetValue(id, out var host)) return;
            _hosts.Remove(id);
            if (host.Marker != null) IdentifierObjects.Remove(host.Marker);
        }

        internal static void ResetEditorState()
        {
            _hosts.Clear();
            _tick = 0;
            _impact.Clear();
            _impactStock.Clear();
            _impactProbe = 0;
            _impactTinted = false;
            _tracker = null;
            _trackerProbe = 0;
        }

        internal static void Tick()
        {
            if (!Enabled || _hosts.Count == 0 || ++_tick % 15 != 0) return;

            foreach (var kv in _hosts)
            {
                var host = kv.Value;
                bool alive = host.Lepo != null && host.Lepo.gameObject.activeInHierarchy;

                if (!alive && host.Marker != null)
                {
                    IdentifierObjects.Remove(host.Marker);
                    host.Marker = null;
                    Plugin.Log.LogInfo("a coloured slime floor went away, its colour marker went with it");
                }
                else if (alive && host.Slot.a > 0f && host.Marker == null)
                {
                    Reconcile(host.Lepo);
                    Plugin.Log.LogInfo($"{host.Lepo.name} is back, put its colour marker back too");
                }
            }
        }

        private static Host HostFor(LevelEditorPlaceableObject lepo)
        {
            int id = lepo.gameObject.GetInstanceID();
            if (_hosts.TryGetValue(id, out var host)) { host.Lepo = lepo; host.Root = lepo.transform; return host; }
            host = new Host { Lepo = lepo, Root = lepo.transform };
            _hosts[id] = host;
            return host;
        }

        private static void Reconcile(LevelEditorPlaceableObject lepo)
        {
            if (!_hosts.TryGetValue(lepo.gameObject.GetInstanceID(), out var host)) return;
            if (host.Slot.a <= 0f) return;

            if (host.Marker == null)
            {
                host.Marker = Kind.Spawn(lepo.transform.position, "#" + ColorUtility.ToHtmlStringRGB(host.Slot));
                return;
            }

            host.Marker.transform.position = lepo.transform.position;
        }

        internal static void Apply(Transform root, Color colour)
        {
            var view = GoopViewOf(root);
            if (view == null) return;

            if (_block == null) _block = new MaterialPropertyBlock();
            view.GetPropertyBlock(_block);
            _block.SetColor(ColourId, colour);
            _block.SetColor(Colour2Id, colour);
            view.SetPropertyBlock(_block);
        }

        internal static Renderer GoopViewOf(Transform root)
        {
            var surface = root.GetComponentInChildren<LevelEditorDrawableGoopSurface>(true);
            if (surface != null && surface._goopVisualRenderer != null) return surface._goopVisualRenderer;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                if (r != null && r.gameObject.name == GoopVisualName) return r;
            return null;
        }

        private static bool _moanedNoPlayer;

        internal static void FollowFloorUnderPlayer()
        {
            if (_hosts.Count == 0) return;

            int coloured = 0;
            foreach (var kv in _hosts) if (kv.Value.Slot.a > 0f) coloured++;
            if (coloured == 0) return;

            var bean = Services.BeanMonitorService.LocalPlayerBean;
            if (bean == null)
            {
                var fgcc = FallGuysLib.Players.PlayerUtils.PlayerController;
                if (fgcc != null) bean = fgcc.gameObject;
            }
            if (bean == null)
            {
                var mgr = LevelEditorManager.Instance;
                if (mgr != null) bean = mgr._playerGameObject;
            }
            if (bean == null)
            {
                if (!_moanedNoPlayer) { _moanedNoPlayer = true; Plugin.Log.LogWarning($"slime vfx: {coloured} coloured floor(s) but no local bean yet, colour stays put"); }
                return;
            }
            _moanedNoPlayer = false;
            var at = bean.transform.position;

            Host best = null;
            float bestDrop = float.MaxValue;
            foreach (var kv in _hosts)
            {
                var host = kv.Value;
                if (host.Slot.a <= 0f || host.Root == null) continue;
                if (host.View == null) host.View = GoopViewOf(host.Root);
                if (host.View == null) continue;

                var b = host.View.bounds;
                if (at.x < b.min.x || at.x > b.max.x || at.z < b.min.z || at.z > b.max.z) continue;
                float drop = at.y - b.max.y;
                if (drop < -2f || drop >= bestDrop) continue;
                bestDrop = drop;
                best = host;
            }

            if (best == null)
            {
                if (_impactTinted) PushImpact(Color.white, false);
                return;
            }
            PushImpact(best.Slot, true);
            if (best.Slot == _vfxWanted)
            {
                if (++_trailTick % 6 == 0) TintTracker();
                return;
            }
            TintVfx(best.Slot);
        }

        private static Color _vfxWanted;
        private static bool _vfxPending;
        private static int _skipped;

        private static void TintVfx(Color colour)
        {
            _vfxWanted = colour;
            _vfxPending = true;
            PushVfx();
        }

        private static void PushVfx()
        {
            if (!_vfxPending) return;
            var manager = PhysicsManager.Instance;
            if (manager == null) return;
            var list = manager.SurfaceModifiers;
            if (list == null) return;

            SurfaceModifier def = null;
            for (int i = 0; i < list.Count; i++)
            {
                var sm = list[i];
                if (sm == null || sm.PhysicMaterial == null) continue;
                if (sm.PhysicMaterial.name.IndexOf("Slime", StringComparison.OrdinalIgnoreCase) < 0) continue;
                def = sm;
                break;
            }
            if (def == null) return;

            _skipped = 0;
            bool all = TintPool(def.FootstepVFXPoolData);
            all &= TintPool(def.CollisionVFXPoolData);
            all &= TintPool(def.ContinuousVFXPoolData);
            all &= TintPool(def.SplashSlideVFXPoolData);
            all &= TintPool(def.ContinuousSlideVFXPoolData);
            TintTracker();
            _vfxPending = !all;
            if (!all) Plugin.Log.LogInfo($"slime vfx: {_skipped} pool(s) not loaded yet, will retry");
        }

        private static bool TintPool(VfxPoolData data)
        {
            if (data == null) return true;
            if (!data.IsLoaded) { _skipped++; return false; }
            var pool = data.Pool;
            if (pool == null) { _skipped++; return false; }

            TintRoot(pool.gameObject);
            var template = pool._object;
            if (template != null) TintRoot(template.gameObject);

            var live = pool._activeParticles;
            if (live != null)
                for (int i = 0; i < live.Count; i++)
                {
                    var ps = live[i].ParticleSystem;
                    if (ps != null) TintRoot(ps.gameObject);
                }
            return true;
        }

        private static readonly List<ParticleSystem> _impact = new List<ParticleSystem>();
        private static readonly List<Color> _impactStock = new List<Color>();
        private static int _impactProbe;
        private static bool _impactTinted;
        private static Color _impactColour;

        private static void EnsureImpact()
        {
            if (_impact.Count > 0) return;
            if (++_impactProbe % 120 != 1) return;
            foreach (var ps in Resources.FindObjectsOfTypeAll<ParticleSystem>())
            {
                if (ps == null) continue;
                bool landing = false;
                var t = ps.transform;
                for (int d = 0; d < 6 && t != null; d++, t = t.parent)
                    if (t.name.StartsWith("VFX_POOL_Impact", StringComparison.Ordinal)) { landing = true; break; }
                if (!landing) continue;
                _impact.Add(ps);
                _impactStock.Add(ps.main.startColor.m_ColorMax);
            }
            if (_impact.Count > 0)
                Plugin.Log.LogInfo($"slime colour: {_impact.Count} landing emitter(s) on file, they follow the goop underfoot");
        }

        private static void PushImpact(Color colour, bool tint)
        {
            EnsureImpact();
            if (tint && _impactTinted && _impactColour == colour) return;
            if (!tint && !_impactTinted) return;
            for (int i = 0; i < _impact.Count; i++)
            {
                var ps = _impact[i];
                if (ps == null) continue;
                var main = ps.main;
                main.startColor = tint ? colour : _impactStock[i];
            }
            _impactTinted = tint;
            _impactColour = colour;
        }

        private static Transform _tracker;
        private static int _trailTick;
        private static int _trackerProbe;

        private static void TintTracker()
        {
            if (_tracker == null)
            {
                if (++_trackerProbe % 120 != 1) return;
                foreach (var ps in Resources.FindObjectsOfTypeAll<ParticleSystem>())
                {
                    if (ps == null) continue;
                    var t = ps.transform;
                    for (int d = 0; d < 6 && t != null; d++, t = t.parent)
                        if (t.name.StartsWith("Prefab_VFX_Character_Tracker", StringComparison.Ordinal)) { _tracker = t; break; }
                    if (_tracker != null) break;
                }
                if (_tracker == null) return;
                Plugin.Log.LogInfo("slime colour: found the character vfx tracker, its goop trail follows the floor now");
            }

            foreach (var ps in _tracker.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;
                if (ps.gameObject.name.IndexOf("Goop", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var main = ps.main;
                main.startColor = _vfxWanted;
            }
        }

        private static void TintRoot(GameObject root)
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;
                var main = ps.main;
                main.startColor = _vfxWanted;
            }
        }

        internal static void Sync()
        {
            if (IdentifierObjects.InEditor()) { if (Enabled) SyncEditor(); }
            else SyncRound();

            PushVfx();
        }

        private static void SyncEditor()
        {
            var col = LevelEditorPlaceableObject.Collection;
            if (col == null) return;

            var hosts = new List<LevelEditorPlaceableObject>();
            var markers = new List<LevelEditorPlaceableObject>();
            for (int i = 0; i < col.Count; i++)
            {
                var lepo = col[i];
                if (lepo == null) continue;
                if (IsMarker(lepo)) markers.Add(lepo);
                else if (IsHost(lepo)) hosts.Add(lepo);
            }
            if (markers.Count == 0) return;

            foreach (var lepo in hosts)
            {
                var host = HostFor(lepo);
                if (host.Marker != null) continue;

                var pos = lepo.transform.position;
                foreach (var m in markers)
                {
                    if (m == null || (m.transform.position - pos).sqrMagnitude > MatchRadius * MatchRadius) continue;
                    var colour = BatchTargets.GetColourParam(m);
                    if (colour == null) continue;
                    var c = colour.CurrentColour;
                    c.a = 1f;
                    host.Marker = m;
                    host.Slot = c;
                    Apply(lepo.transform, c);
                    TintVfx(c);
                    break;
                }
            }
        }

        private static void SyncRound()
        {
            var found = new List<IdentifierMarker>();
            foreach (var m in IdentifierObjects.ReadRound(Kind.Prefab))
            {
                if (!Kind.MatchesRotation(m.Rotation)) continue;
                found.Add(m);
            }
            if (found.Count == 0) return;

            var hosts = RoundHosts();
            if (hosts.Count == 0)
            {
                Plugin.Log.LogWarning($"slime colour: {found.Count} marker(s) in the round json but no slime floor to paint");
                return;
            }

            int painted = 0;
            foreach (var m in found)
            {
                Color c;
                if (string.IsNullOrEmpty(m.Hex) || !ColorUtility.TryParseHtmlString(m.Hex, out c)) continue;
                c.a = 1f;

                Transform best = null;
                float bestSq = MatchRadius * MatchRadius;
                foreach (var h in hosts)
                {
                    if (h == null) continue;
                    float d = (h.position - m.Position).sqrMagnitude;
                    if (d > bestSq) continue;
                    bestSq = d;
                    best = h;
                }
                if (best == null) continue;
                Apply(best, c);
                int id = best.gameObject.GetInstanceID();
                if (!_hosts.TryGetValue(id, out var host)) { host = new Host(); _hosts[id] = host; }
                host.Root = best;
                host.Slot = c;
                painted++;
            }
            if (painted > 0) Plugin.Log.LogInfo($"slime colour: painted {painted} floor(s) from round json");
        }

        private static List<Transform> RoundHosts()
        {
            var list = new List<Transform>();

            var col = LevelEditorPlaceableObject.Collection;
            if (col != null)
                for (int i = 0; i < col.Count; i++)
                    if (IsHost(col[i])) list.Add(col[i].transform);
            if (list.Count > 0) return list;

            foreach (var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
            {
                if (r == null || r.gameObject.name != GoopVisualName) continue;
                var t = r.transform;
                while (t != null && !IsHostName(t.name)) t = t.parent;
                if (t != null && !list.Contains(t)) list.Add(t);
            }
            return list;
        }
    }
}
