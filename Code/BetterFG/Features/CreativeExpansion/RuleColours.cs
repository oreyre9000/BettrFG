using System;
using System.Collections.Generic;
using BetterFG.UI.Windows.Creative;
using BetterFG.Utilities;
using LevelEditor;
using UnityEngine;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class RuleColours
    {
        internal static bool Enabled => FeatureCreativeExpansion.On("colouring");

        private static readonly MarkerKind Kind = MarkerKinds.RuleColour;
        internal static string MarkerName => Kind.Prefab;
        private const float MatchRadius = 0.3f;

        internal const int Base = 0;
        internal const int Rim = 1;
        internal const int Fences = 2;

        private static readonly Vector3[] Offsets = { new Vector3(1f, 0f, 0f), Vector3.zero, new Vector3(-1f, 0f, 0f) };

        private static readonly string[] Stock = { "#0055DC", "#510CEF", "#FFB308" };

        private static readonly int ColourId = Shader.PropertyToID("_Color");
        private static readonly int VertexColourId = Shader.PropertyToID("_UseVertexColorAsAlbedo");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        private const string RemapShaderName = "BettrFG/RuleColourRemap";
        private const float MatchTolerance = 0.06f;
        private static readonly int ToleranceId = Shader.PropertyToID("_Tolerance");
        private static readonly string[][] VertexKeys =
        {
            new[] { "#0059F3", "#0055DB", "#108EF3" },
            new[] { "#510CEF" },
            new[] { "#F3AA08" },
        };

        private sealed class Host
        {
            public LevelEditorPlaceableObject Lepo;
            public readonly Color[] Slots = new Color[3];
            public readonly LevelEditorPlaceableObject[] Markers = new LevelEditorPlaceableObject[3];
        }

        private static readonly Dictionary<int, Host> _hosts = new Dictionary<int, Host>();
        private static readonly HashSet<int> _unclaimed = new HashSet<int>();
        private static MaterialPropertyBlock _block;

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            c.a = 1f;
            return c;
        }

        internal static bool IsHost(LevelEditorPlaceableObject lepo) => lepo != null && IsHostName(lepo.name);

        private static bool IsHostName(string n)
            => n.StartsWith("Placeable_Rule_FloorStart", StringComparison.Ordinal)
               || n.StartsWith("Placeable_Rule_FloorEnd", StringComparison.Ordinal)
               || n.StartsWith("Placeable_Rule_Checkpoint", StringComparison.Ordinal);

        internal static bool IsMarker(LevelEditorPlaceableObject lepo) => Kind.Matches(lepo);

        internal static LevelEditorPlaceableObject OwnerOf(LevelEditorPlaceableObject marker)
        {
            if (marker == null) return null;
            foreach (var kv in _hosts)
                for (int s = 0; s < Offsets.Length; s++)
                    if (kv.Value.Markers[s] != null && kv.Value.Markers[s].Pointer == marker.Pointer) return kv.Value.Lepo;
            return null;
        }

        internal static void OpenPicker(LevelEditorPlaceableObject lepo, int slot)
        {
            var host = HostFor(lepo);
            var marker = host.Markers[slot];
            if (marker == null)
            {
                if (host.Slots[slot].a <= 0f) host.Slots[slot] = Hex(Stock[slot]);
                Reconcile(lepo);
                marker = host.Markers[slot];
            }
            if (marker == null) return;

            var param = BatchTargets.GetColourParam(marker);
            if (param == null)
            {
                Plugin.Log.LogWarning($"rule colours: marker for {lepo.name} slot {slot} has no colour parameter, no picker to open");
                return;
            }

            LevelEditorColourPickerParameterViewModel.OpenColourPickerScreen(param);
        }

        internal static void OnMarkerColourChanged(LevelEditorPlaceableObject marker)
        {
            foreach (var kv in _hosts)
            {
                var host = kv.Value;
                for (int s = 0; s < Offsets.Length; s++)
                {
                    if (host.Markers[s] == null || host.Markers[s].Pointer != marker.Pointer) continue;
                    var colour = BatchTargets.GetColourParam(marker);
                    if (colour == null) return;
                    var c = colour.CurrentColour;
                    c.a = 1f;
                    host.Slots[s] = c;
                    if (host.Lepo != null) Apply(host.Lepo.transform, host.Slots);
                    return;
                }
            }
        }

        internal static Color ColourOf(LevelEditorPlaceableObject lepo, int slot)
        {
            if (_hosts.TryGetValue(lepo.gameObject.GetInstanceID(), out var host) && host.Slots[slot].a > 0f) return host.Slots[slot];
            return Hex(Stock[slot]);
        }

        internal static void OnCloned(LevelEditorPlaceableObject clone, LevelEditorPlaceableObject source)
        {
            if ((IdentifierObjects.InEditor() && !Enabled) || clone == null || source == null || !IsHost(clone)) return;
            if (!_hosts.TryGetValue(source.gameObject.GetInstanceID(), out var from)) return;

            var host = new Host { Lepo = clone };
            for (int s = 0; s < Offsets.Length; s++) host.Slots[s] = from.Slots[s];
            _hosts[clone.gameObject.GetInstanceID()] = host;
            Apply(clone.transform, host.Slots);
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
            for (int s = 0; s < Offsets.Length; s++)
                if (host.Markers[s] != null) IdentifierObjects.Remove(host.Markers[s]);
        }

        internal static void ResetEditorState()
        {
            _hosts.Clear();
            _unclaimed.Clear();
            _tick = 0;
        }

        private static int _tick;

        internal static void Tick()
        {
            if (!Enabled || _hosts.Count == 0 || ++_tick % 15 != 0) return;

            foreach (var kv in _hosts)
            {
                var host = kv.Value;
                bool alive = host.Lepo != null && host.Lepo.gameObject.activeInHierarchy;
                bool marked = false;
                bool wanted = false;
                for (int s = 0; s < Offsets.Length; s++)
                {
                    if (host.Markers[s] != null) marked = true;
                    if (host.Slots[s].a > 0f) wanted = true;
                }

                if (!alive && marked)
                {
                    for (int s = 0; s < Offsets.Length; s++)
                    {
                        if (host.Markers[s] == null) continue;
                        IdentifierObjects.Remove(host.Markers[s]);
                        host.Markers[s] = null;
                    }
                    Plugin.Log.LogInfo("a coloured rule object went away, its colour markers went with it");
                }
                else if (alive && wanted && !marked)
                {
                    Reconcile(host.Lepo);
                    Plugin.Log.LogInfo($"{host.Lepo.name} is back, put its colour markers back too");
                }
            }
        }

        private static Host HostFor(LevelEditorPlaceableObject lepo)
        {
            int id = lepo.gameObject.GetInstanceID();
            if (_hosts.TryGetValue(id, out var host)) { host.Lepo = lepo; return host; }
            host = new Host { Lepo = lepo };
            _hosts[id] = host;
            return host;
        }

        private static void Reconcile(LevelEditorPlaceableObject lepo)
        {
            if (!_hosts.TryGetValue(lepo.gameObject.GetInstanceID(), out var host)) return;
            var origin = lepo.transform.position;

            for (int s = 0; s < Offsets.Length; s++)
            {
                if (host.Slots[s].a <= 0f) continue;
                var pos = origin + Offsets[s];

                if (host.Markers[s] == null)
                {
                    var spawned = Kind.Spawn(pos, "#" + ColorUtility.ToHtmlStringRGB(host.Slots[s]));
                    if (spawned == null) continue;
                    host.Markers[s] = spawned;
                    continue;
                }

                host.Markers[s].transform.position = pos;
            }
        }

        private static void HideMarker(LevelEditorPlaceableObject marker)
        {
            foreach (var r in marker.GetComponentsInChildren<Renderer>(true))
                if (r != null) r.forceRenderingOff = true;
        }

        private static int Classify(Transform t, Material m)
        {
            if (m == null) return -1;
            string s = (t.name + "|" + (t.parent != null ? t.parent.name : string.Empty) + "|" + m.name).ToLowerInvariant();

            if (s.Contains("logo") || s.Contains("arrow") || s.Contains("checker") || s.Contains("finishline")
                || s.Contains("arch_mat") || s.Contains("xray") || s.Contains("noise")) return -1;

            if (s.Contains("flag") || s.Contains("wallparent") || s.Contains("inflat")) return Fences;

            if (m.HasProperty(ColourId))
            {
                float h, sat, v;
                Color.RGBToHSV(m.GetColor(ColourId), out h, out sat, out v);
                if (sat > 0.35f && v > 0.15f)
                {
                    float deg = h * 360f;
                    if (deg >= 150f && deg < 245f) return Base;
                    if (deg >= 245f && deg < 350f) return Rim;
                    if (deg >= 15f && deg < 65f) return Fences;
                    return -1;
                }
            }

            if (s.Contains("hardplastic")) return Rim;
            if (s.Contains("vinyl")) return Base;
            return -1;
        }

        internal static void Apply(Transform root, Color[] slots)
        {
            if (_block == null) _block = new MaterialPropertyBlock();

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                bool swapped = false;

                for (int i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    bool ours = mat != null && mat.shader != null && mat.shader.name == RemapShaderName;

                    if (ours || IsVertexArt(mat))
                    {
                        if (!ours)
                        {
                            var remap = NewRemapMaterial(mat);
                            if (remap == null) continue;
                            r.SetPropertyBlock(null, i);
                            mats[i] = remap;
                            mat = remap;
                            swapped = true;
                        }
                        WriteKeys(mat, slots);
                        continue;
                    }

                    int slot = Classify(r.transform, mat);
                    if (slot < 0 || slots[slot].a <= 0f) continue;

                    r.GetPropertyBlock(_block, i);
                    _block.SetColor(ColourId, slots[slot]);
                    _block.SetFloat(VertexColourId, 0f);
                    _block.SetTexture(MainTexId, Texture2D.whiteTexture);
                    r.SetPropertyBlock(_block, i);
                }

                if (swapped) r.sharedMaterials = mats;
            }
        }

        private static bool IsVertexArt(Material m)
            => m != null
               && m.name.StartsWith("ENV_S01_Vanilla_", StringComparison.Ordinal)
               && m.HasProperty(VertexColourId)
               && m.GetFloat(VertexColourId) > 0.5f;

        private static Material NewRemapMaterial(Material src)
        {
            var shader = Core.AssetManager.GetShader("RuleColourRemap");
            if (shader == null)
            {
                Plugin.Log.LogWarning("rule colours: the remap shader didn't load, vertex-coloured platforms stay stock");
                return null;
            }
            var mat = new Material(shader) { name = "BettrFG_RuleColourRemap" };
            mat.CopyPropertiesFromMaterial(src);
            mat.SetFloat(ToleranceId, MatchTolerance);
            return mat;
        }

        private static void WriteKeys(Material mat, Color[] slots)
        {
            int n = 0;
            for (int s = 0; s < VertexKeys.Length; s++)
            {
                if (slots[s].a <= 0f) continue;
                var rep = new Vector4(slots[s].r, slots[s].g, slots[s].b, 1f);
                foreach (var hex in VertexKeys[s])
                {
                    var key = Hex(hex);
                    mat.SetVector("_Key" + n, new Vector4(key.r, key.g, key.b, 1f));
                    mat.SetVector("_Rep" + n, rep);
                    n++;
                }
            }
            for (; n < 6; n++) mat.SetVector("_Key" + n, Vector4.zero);
        }

        internal static void Sync()
        {
            if (IdentifierObjects.InEditor()) { if (Enabled) SyncEditor(); }
            else SyncRound();
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
            if (markers.Count == 0) { _unclaimed.Clear(); return; }

            var claimed = new HashSet<int>();
            foreach (var lepo in hosts)
            {
                var origin = lepo.transform.position;
                Host host = null;

                for (int s = 0; s < Offsets.Length; s++)
                {
                    var want = origin + Offsets[s];
                    LevelEditorPlaceableObject found = null;
                    foreach (var m in markers)
                    {
                        if (m == null || claimed.Contains(m.gameObject.GetInstanceID())) continue;
                        if ((m.transform.position - want).sqrMagnitude > MatchRadius * MatchRadius) continue;
                        found = m;
                        break;
                    }
                    if (found == null) continue;

                    var colour = BatchTargets.GetColourParam(found);
                    if (colour == null) continue;

                    claimed.Add(found.gameObject.GetInstanceID());
                    HideMarker(found);

                    if (host == null) host = HostFor(lepo);
                    host.Markers[s] = found;
                    var c = colour.CurrentColour;
                    c.a = 1f;
                    host.Slots[s] = c;
                }

                if (host != null) Apply(lepo.transform, host.Slots);
            }

            foreach (var m in markers)
            {
                if (m == null) continue;
                int id = m.gameObject.GetInstanceID();
                if (claimed.Contains(id)) { _unclaimed.Remove(id); continue; }
                if (_unclaimed.Add(id)) continue;

                _unclaimed.Remove(id);
                IdentifierObjects.Remove(m);
                Plugin.Log.LogInfo("binned a colour marker whose rule object isn't in the level any more");
            }
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

            var hosts = RoundHosts();
            if (hosts.Count == 0)
            {
                Plugin.Log.LogWarning($"rule colours: {found.Count} marker(s) in the round json but no start line, checkpoint or finish line to paint");
                return;
            }

            var slotsByHost = new Dictionary<int, Color[]>();
            var rootByHost = new Dictionary<int, Transform>();

            foreach (var m in found)
            {
                Color c;
                if (string.IsNullOrEmpty(m.Hex) || !ColorUtility.TryParseHtmlString(m.Hex, out c)) continue;
                c.a = 1f;

                for (int s = 0; s < Offsets.Length; s++)
                {
                    var host = NearestHost(hosts, m.Position - Offsets[s]);
                    if (host == null) continue;

                    int id = host.gameObject.GetInstanceID();
                    if (!slotsByHost.TryGetValue(id, out var slots))
                    {
                        slots = new Color[Offsets.Length];
                        slotsByHost[id] = slots;
                        rootByHost[id] = host;
                    }
                    slots[s] = c;
                    break;
                }
            }

            foreach (var kv in slotsByHost) Apply(rootByHost[kv.Key], kv.Value);
            Plugin.Log.LogInfo($"rule colours: painted {slotsByHost.Count} object(s) from round json");
        }

        private static Transform NearestHost(List<Transform> hosts, Vector3 pos)
        {
            Transform best = null;
            float bestSq = MatchRadius * MatchRadius;
            foreach (var h in hosts)
            {
                if (h == null) continue;
                float d = (h.position - pos).sqrMagnitude;
                if (d > bestSq) continue;
                bestSq = d;
                best = h;
            }
            return best;
        }

        private static List<Transform> RoundHosts()
        {
            var list = new List<Transform>();

            var col = LevelEditorPlaceableObject.Collection;
            if (col != null)
                for (int i = 0; i < col.Count; i++)
                    if (IsHost(col[i])) list.Add(col[i].transform);
            if (list.Count > 0) return list;

            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null) continue;
                var t = r.transform;
                while (t != null && !IsHostName(t.name)) t = t.parent;
                if (t != null && !list.Contains(t)) list.Add(t);
            }
            return list;
        }
    }
}
