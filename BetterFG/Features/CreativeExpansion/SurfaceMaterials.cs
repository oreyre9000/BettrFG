using System;
using System.Collections.Generic;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.UI.Windows.Creative;
using BetterFG.Utilities;
using LevelEditor;
using UnityEngine;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class SurfaceMaterials
    {
        internal static bool Enabled => FeatureCreativeContent.On("colouring");

        private static readonly MarkerKind Kind = MarkerKinds.SurfaceMaterial;
        private const float MatchRadius = 0.3f;
        private const float IndexStep = 0.01f;
        private const string Suffix = "_bfgsurf";

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
        private static readonly int WorldScaleId = Shader.PropertyToID("_WorldScale");
        private static readonly int PlanarTileId = Shader.PropertyToID("_PlanarTile");
        private static readonly int VinylBoxHalfId = Shader.PropertyToID("_VinylBoxHalf");
        private static readonly int BoxHalfId = Shader.PropertyToID("_BoxHalf");

        private sealed class Def
        {
            public string Label;
            public string Shader;
            public string Source;
            public float Tile;
            public Action<Material, Material> Prep;
            public Action<Material, Color> Tint;
        }

        private static readonly Color WoodToneA = new Color(0.74f, 0.74f, 0.74f, 1f);
        private static readonly Color WoodToneB = new Color(0.66f, 0.66f, 0.66f, 1f);
        private static readonly Color WoodToneC = new Color(0.86f, 0.86f, 0.86f, 1f);

        private static Color Brighter(Color c)
            => new Color(Mathf.Min(1f, c.r * 1.18f + 0.06f), Mathf.Min(1f, c.g * 1.18f + 0.06f), Mathf.Min(1f, c.b * 1.18f + 0.06f), 1f);

        private static readonly Def[] Defs =
        {
            new Def { Label = "surfacematerial.vinyl", Shader = "vinylscaling", Source = null, Tile = 0f,
                Prep = (m, src) => m.CopyPropertiesFromMaterial(src) },
            new Def { Label = "surfacematerial.hardplastic", Shader = "uberscaling", Source = "c5e8686206973c040a795afd7a359c5e", Tile = 4f,
                Prep = (m, src) => { m.CopyPropertiesFromMaterial(src); m.SetFloat("_UseVertexColorAsAlbedo", 0f); } },
            new Def { Label = "surfacematerial.wood", Shader = "uberscaling", Source = "df66987859b5e8e4da6f68a2beef5d1f", Tile = 6f,
                Prep = (m, src) =>
                {
                    m.CopyPropertiesFromMaterial(src);
                    m.SetColor("_ColorA", WoodToneA);
                    m.SetColor("_ColorB", WoodToneB);
                    m.SetColor("_ColorC", WoodToneC);
                },
                Tint = (m, c) => m.SetColor("_Color2", Brighter(c)) },
            new Def { Label = "surfacematerial.ice", Shader = "disortionscaling", Source = "486e207e2e3d2d2498a14eee6571cb28", Tile = 3f,
                Prep = (m, src) =>
                {
                    m.CopyPropertiesFromMaterial(src);
                    m.SetFloat("_EdgeFrost", 0.6f);
                    m.SetFloat("_EdgeWidth", 2.5f);
                },
                Tint = (m, c) =>
                {
                    m.SetColor("_AlbedoTint", new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.55f, 1f));
                    m.SetColor("_EdgeGlow", new Color(c.r * 0.3f, c.g * 0.3f, c.b * 0.3f, 1f));
                    m.SetColor("_RimColor", new Color(c.r * 0.9f, c.g * 0.9f, c.b * 0.9f, 1f));
                } },
            new Def { Label = "surfacematerial.glass", Shader = "alphascaling", Source = "a7d01f1cab78be245a4bd62ded06f099", Tile = 4f,
                Prep = (m, src) => m.CopyPropertiesFromMaterial(src) },
            new Def { Label = "neon.material_entry", Shader = "neonscaling", Source = "", Tile = 0f,
                Prep = (m, src) =>
                {
                    m.SetFloat("_Glow", 4f);
                    m.SetFloat("_RimGlow", 2.5f);
                    m.SetFloat("_CoreDarken", 0.15f);
                } },
        };

        private static readonly int[] Listed = { 1, 2, 3, 4 };
        private const int NeonDef = 5;

        internal static int Count => Listed.Length;

        internal static string Label(int row) => LocalizationService.Get(Defs[Listed[row]].Label);

        private sealed class Entry
        {
            public LevelEditorPlaceableObject Lepo;
            public int Index;
            public Material[][] StockMats;
            public Renderer[] Renderers;
            public LevelEditorPlaceableObject Marker;
        }

        private static readonly Dictionary<int, Entry> _objects = new Dictionary<int, Entry>();
        private static readonly List<Entry> _dirty = new List<Entry>();

        internal static bool IsMarker(LevelEditorPlaceableObject lepo) => Kind.Matches(lepo);

        internal static int Current(LevelEditorPlaceableObject lepo)
            => _objects.TryGetValue(lepo.gameObject.GetInstanceID(), out var e) ? Array.IndexOf(Listed, e.Index) : -1;

        internal static void Set(LevelEditorPlaceableObject lepo, int row) => SetDef(lepo, row < 0 ? -1 : Listed[row]);

        private static readonly Dictionary<int, Entry> _neon = new Dictionary<int, Entry>();

        internal static void PaintNeon(LevelEditorPlaceableObject lepo)
        {
            int id = lepo.gameObject.GetInstanceID();
            if (!_neon.TryGetValue(id, out var entry)) _neon[id] = entry = new Entry { Lepo = lepo, Index = NeonDef };
            Apply(entry);
        }

        internal static void UnpaintNeon(LevelEditorPlaceableObject lepo)
        {
            int id = lepo.gameObject.GetInstanceID();
            if (!_neon.TryGetValue(id, out var entry)) return;
            Restore(entry);
            _neon.Remove(id);
        }

        private static void SetDef(LevelEditorPlaceableObject lepo, int index)
        {
            int id = lepo.gameObject.GetInstanceID();
            _objects.TryGetValue(id, out var entry);
            if (index < 0)
            {
                if (entry == null) return;
                Restore(entry);
                entry.Index = -1;
                if (!_dirty.Contains(entry)) _dirty.Add(entry);
                return;
            }
            if (entry == null) _objects[id] = entry = new Entry { Lepo = lepo, Index = -1 };
            entry.Index = index;
            Apply(entry);
            if (!_dirty.Contains(entry)) _dirty.Add(entry);
        }

        internal static void OnParamMenuClosed()
        {
            for (int i = 0; i < _dirty.Count; i++)
            {
                var entry = _dirty[i];
                if (entry.Lepo == null) continue;
                WriteMarker(entry);
            }
            _dirty.Clear();
        }

        internal static void OnPlaced(LevelEditorPlaceableObject lepo)
        {
            if (_objects.TryGetValue(lepo.gameObject.GetInstanceID(), out var entry) && entry.Index >= 0) WriteMarker(entry);
        }

        internal static void OnCloned(LevelEditorPlaceableObject clone, LevelEditorPlaceableObject source)
        {
            if (clone == null || source == null) return;
            if (!_objects.TryGetValue(source.gameObject.GetInstanceID(), out var src) || src.Index < 0) return;
            SetDef(clone, src.Index);
        }

        internal static void OnColourChanged(LevelEditorPlaceableObject lepo)
        {
            if (_objects.TryGetValue(lepo.gameObject.GetInstanceID(), out var entry) && entry.Index >= 0) Apply(entry);
        }

        internal static void OnDrawableRescaled(LevelEditorDrawableData drawable)
        {
            if (_objects.Count == 0 && _neon.Count == 0) return;
            var lepo = drawable.Placeable;
            if (lepo == null) return;
            int id = lepo.gameObject.GetInstanceID();
            if (!_objects.TryGetValue(id, out var entry) || entry.Index < 0)
                if (!_neon.TryGetValue(id, out entry)) return;
            var ws = drawable.GetShaderScale();
            foreach (var r in entry.Renderers)
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.name.Contains(Suffix)) m.SetVector(WorldScaleId, ws);
        }

        internal static void OnDestroyed(LevelEditorPlaceableObject lepo)
        {
            int id = lepo.gameObject.GetInstanceID();
            _neon.Remove(id);
            if (!_objects.TryGetValue(id, out var entry)) return;
            _dirty.Remove(entry);
            _objects.Remove(id);
            if (entry.Marker != null) IdentifierObjects.Remove(entry.Marker);
        }

        internal static void ResetEditorState()
        {
            _objects.Clear();
            _neon.Clear();
            _dirty.Clear();
        }

        private static void WriteMarker(Entry entry)
        {
            if (entry.Marker != null) { IdentifierObjects.Remove(entry.Marker); entry.Marker = null; }
            if (entry.Index < 0)
            {
                _objects.Remove(entry.Lepo.gameObject.GetInstanceID());
                return;
            }
            entry.Marker = Kind.Spawn(entry.Lepo.transform.position, "#FFFFFF", IndexStep * (entry.Index + 1));
        }

        private static int IndexFromScale(float y) => Mathf.RoundToInt(y / IndexStep) - 1;

        internal static void Sync()
        {
            bool inEditor = IdentifierObjects.InEditor();
            if (inEditor && !Enabled) return;

            var col = LevelEditorPlaceableObject.Collection;
            if (col == null) return;

            int done = 0, missed = 0;
            if (inEditor)
            {
                for (int i = 0; i < col.Count; i++)
                {
                    var marker = col[i];
                    if (!IsMarker(marker)) continue;
                    int index = IndexFromScale(marker._levelEditorScaleParameter.CurrentScale.y);
                    var target = Nearest(col, marker.transform.position);
                    if (target == null || index < 0 || index >= Defs.Length) { missed++; continue; }

                    int id = target.gameObject.GetInstanceID();
                    if (!_objects.TryGetValue(id, out var entry)) _objects[id] = entry = new Entry { Lepo = target, Index = -1 };
                    entry.Marker = marker;
                    if (entry.Index != index) { entry.Index = index; Apply(entry); }
                    done++;
                }
            }
            else
            {
                foreach (var m in IdentifierObjects.ReadRound(Kind.Prefab))
                {
                    if (!Kind.MatchesRotation(m.Rotation)) continue;
                    int index = IndexFromScale(m.Scale.y);
                    var target = Nearest(col, m.Position);
                    if (target == null || index < 0 || index >= Defs.Length) { missed++; continue; }

                    int id = target.gameObject.GetInstanceID();
                    if (!_objects.TryGetValue(id, out var entry)) _objects[id] = entry = new Entry { Lepo = target, Index = -1 };
                    entry.Index = index;
                    Apply(entry);
                    done++;
                }
            }

            if (done > 0 || missed > 0)
                Plugin.Log.LogInfo($"surface materials: {done} swapped" + (missed > 0 ? $", {missed} marker(s) with nothing near them" : ""));
        }

        private static LevelEditorPlaceableObject Nearest(
            Il2CppSystem.Collections.Generic.List<LevelEditorPlaceableObject> col, Vector3 pos)
        {
            LevelEditorPlaceableObject best = null;
            float bestSq = MatchRadius * MatchRadius;
            for (int i = 0; i < col.Count; i++)
            {
                var lepo = col[i];
                if (lepo == null || MarkerKinds.Of(lepo) != null || NeonMaterial.IsMarkerObject(lepo)) continue;
                float d = (lepo.transform.position - pos).sqrMagnitude;
                if (d > bestSq) continue;
                bestSq = d;
                best = lepo;
            }
            return best;
        }

        private static Renderer[] Targets(LevelEditorPlaceableObject lepo, out LevelEditorDrawableData drawable)
        {
            drawable = lepo.GetComponentInChildren<LevelEditorDrawableData>(true);
            if (drawable != null && drawable.Renderer != null) return new[] { drawable.Renderer };

            var list = new List<Renderer>();
            foreach (var r in lepo.GetComponentsInChildren<MeshRenderer>(true))
            {
                var m = r.sharedMaterial;
                if (m == null || m.shader == null) continue;
                var n = m.shader.name;
                if (n.StartsWith("Mediatonic/ENV", StringComparison.Ordinal) || n.StartsWith("BettrFG/", StringComparison.Ordinal)) list.Add(r);
            }
            return list.ToArray();
        }

        private static Material _blank;

        private static Material Base(Def def)
        {
            if (def.Source == null) return AssetManager.GetMaterial("bfgvinyl");
            if (def.Source.Length == 0) return _blank ??= new Material(AssetManager.GetShader(def.Shader)) { name = "bfg_blank" };
            return AssetLoads.Get<Material>(def.Source);
        }

        private static void Apply(Entry entry)
        {
            var def = Defs[entry.Index];
            var shader = AssetManager.GetShader(def.Shader);
            var src = Base(def);
            if (shader == null || src == null)
            {
                Plugin.Log.LogWarning($"{def.Label} has no shader ({shader != null}) or base material ({src != null}), leaving {entry.Lepo.name} as it was");
                return;
            }

            var rends = Targets(entry.Lepo, out var drawable);
            if (entry.StockMats == null || entry.Renderers == null || entry.Renderers.Length != rends.Length)
            {
                entry.Renderers = rends;
                entry.StockMats = new Material[rends.Length][];
            }

            var colourParam = BatchTargets.GetColourParam(entry.Lepo);
            var colour = colourParam != null ? colourParam.CurrentColour : Color.white;
            var ws = drawable != null ? drawable.GetShaderScale() : Vector3.one;

            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                var now = r.sharedMaterials;
                var stock = entry.StockMats[i] ?? new Material[now.Length];
                if (stock.Length != now.Length) stock = new Material[now.Length];
                for (int k = 0; k < now.Length; k++)
                    if (now[k] != null && !now[k].name.Contains(Suffix)) stock[k] = now[k];
                entry.StockMats[i] = stock;

                var mf = r.GetComponent<MeshFilter>();
                var half = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.extents : Vector3.one;
                if (drawable != null)
                    half = new Vector3(half.x - 2f * Mathf.Max(Mathf.Abs(ws.x) - 1f, 0f),
                                       half.y - 2f * Mathf.Max(Mathf.Abs(ws.y) - 1f, 0f),
                                       half.z - 2f * Mathf.Max(Mathf.Abs(ws.z) - 1f, 0f));

                var live = new Material[now.Length];
                for (int k = 0; k < now.Length; k++)
                {
                    var m = new Material(shader) { name = def.Label + Suffix };
                    def.Prep(m, src);
                    m.shader = shader;
                    m.renderQueue = shader.renderQueue;
                    m.SetColor(ColorId, colour);
                    def.Tint?.Invoke(m, colour);
                    if (m.HasProperty(TintColorId)) m.SetColor(TintColorId, new Color(colour.r, colour.g, colour.b, src.GetColor(TintColorId).a));
                    m.SetVector(WorldScaleId, ws);
                    m.SetFloat(PlanarTileId, drawable != null ? def.Tile : 0f);
                    m.SetVector(VinylBoxHalfId, half);
                    m.SetVector(BoxHalfId, half);
                    live[k] = m;
                }
                r.sharedMaterials = live;
                DropOurs(now);
            }
            SurfaceFinish.OnColourChanged(entry.Lepo);
        }

        private static void DropOurs(Material[] mats)
        {
            foreach (var m in mats)
                if (m != null && m.name.Contains(Suffix)) UnityEngine.Object.Destroy(m);
        }

        private static void Restore(Entry entry)
        {
            if (entry.StockMats == null || entry.Renderers == null) return;
            for (int i = 0; i < entry.Renderers.Length; i++)
            {
                var r = entry.Renderers[i];
                if (r == null || entry.StockMats[i] == null) continue;
                var now = r.sharedMaterials;
                r.sharedMaterials = entry.StockMats[i];
                DropOurs(now);
            }
            if (entry.Lepo != null) SurfaceFinish.OnColourChanged(entry.Lepo);
        }
    }
}
