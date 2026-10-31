using System;
using System.Collections.Generic;
using BetterFG.Services;
using BetterFG.UI.Windows.Creative;
using BetterFG.Utilities;
using Il2CppInterop.Runtime;
using LevelEditor;
using UnityEngine;
using NodeEntry = LevelEditorParameterMenuViewModel.ParameterMenuNodeEntry;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class SurfaceFinish
    {
        internal static bool Enabled => FeatureCreativeContent.On("colouring");

        private static readonly MarkerKind Kind = MarkerKinds.SurfaceFinish;
        internal static string MarkerName => Kind.Prefab;
        private const float MatchRadius = 0.3f;
        private const float SmoothnessSpan = 2f;
        private const float FloorSpan = 3f;
        private const string FinishSuffix = "_bfgfinish";

        private static readonly int RangeId = Shader.PropertyToID("_SmoothnessRange");
        private static readonly int MapId = Shader.PropertyToID("_MetallicSmoothness");
        private static readonly int FloorRangeId = Shader.PropertyToID("_RoughnessRange");
        private static readonly int FloorMapId = Shader.PropertyToID("_MetallicRoughness");
        private static readonly int MetalBoostId = Shader.PropertyToID("_MetalBoost");

        private struct Finish { public float Rough; public float Metal; }

        private sealed class Entry
        {
            public LevelEditorPlaceableObject Lepo;
            public Finish Value;
            public Finish Stock;
            public LevelEditorPlaceableObject Marker;
            public Material[][] StockMats;
        }

        private static readonly Dictionary<int, Entry> _objects = new Dictionary<int, Entry>();
        private static readonly Dictionary<int, Texture2D> _maps = new Dictionary<int, Texture2D>();
        private static readonly List<Entry> _dirty = new List<Entry>();

        internal static bool IsMarker(LevelEditorPlaceableObject lepo) => Kind.Matches(lepo);

        internal static bool CanFinish(LevelEditorPlaceableObject lepo)
        {
            if (lepo == null || Foliage.IsFoliage(lepo)) return false;
            if (BatchTargets.GetColourParam(lepo) == null) return false;
            if (lepo.CurrentSurfaceDefinition != null) return false;
            return StockMaterial(lepo) != null;
        }

        private static Material StockMaterial(LevelEditorPlaceableObject lepo)
        {
            foreach (var r in lepo.GetComponentsInChildren<Renderer>(true))
            {
                var mat = r != null ? r.sharedMaterial : null;
                if (mat == null) continue;
                if (Finishable(mat)) return mat;
            }
            return null;
        }

        private static bool Finishable(Material mat)
            => mat.HasProperty(RangeId) || mat.HasProperty(MapId) || mat.HasProperty(FloorRangeId) || mat.HasProperty(FloorMapId);

        internal static float RoughnessOf(LevelEditorPlaceableObject lepo) => Current(lepo).Rough;

        internal static float MetallicOf(LevelEditorPlaceableObject lepo) => Current(lepo).Metal;

        private static Finish Current(LevelEditorPlaceableObject lepo)
            => _objects.TryGetValue(lepo.gameObject.GetInstanceID(), out var e) ? e.Value : Stock(lepo);

        private static Finish Stock(LevelEditorPlaceableObject lepo)
        {
            var mat = StockMaterial(lepo);
            float rough = 0.5f;
            if (mat != null && mat.HasProperty(RangeId))
                rough = Mathf.Clamp01(1f - mat.GetFloat(RangeId) / SmoothnessSpan);
            else if (mat != null && mat.HasProperty(FloorRangeId))
                rough = Mathf.Clamp01(1f - mat.GetFloat(FloorRangeId) / FloorSpan);
            return new Finish { Rough = rough, Metal = 0f };
        }

        internal static void SetRoughness(LevelEditorPlaceableObject lepo, float v)
            => Set(lepo, new Finish { Rough = Mathf.Clamp01(v), Metal = MetallicOf(lepo) });

        internal static void SetMetallic(LevelEditorPlaceableObject lepo, float v)
            => Set(lepo, new Finish { Rough = RoughnessOf(lepo), Metal = Mathf.Clamp01(v) });

        private static void Set(LevelEditorPlaceableObject lepo, Finish f)
        {
            var entry = Track(lepo);
            entry.Value = f;
            Apply(entry);
            if (!_dirty.Contains(entry)) _dirty.Add(entry);
        }

        private static Entry Track(LevelEditorPlaceableObject lepo)
        {
            int id = lepo.gameObject.GetInstanceID();
            if (_objects.TryGetValue(id, out var entry)) return entry;

            entry = new Entry { Lepo = lepo, Stock = Stock(lepo) };
            entry.Value = entry.Stock;
            _objects[id] = entry;
            return entry;
        }

        internal static void OnParamMenuClosed()
        {
            for (int i = 0; i < _dirty.Count; i++)
            {
                var entry = _dirty[i];
                if (entry.Lepo == null) continue;
                if (entry.Lepo.CurrentSurfaceDefinition != null) { Clear(entry); continue; }
                WriteMarker(entry);
            }
            _dirty.Clear();

            List<Entry> swapped = null;
            foreach (var kv in _objects)
            {
                var entry = kv.Value;
                if (entry.Lepo == null || entry.Lepo.CurrentSurfaceDefinition == null) continue;
                (swapped ?? (swapped = new List<Entry>())).Add(entry);
            }
            if (swapped == null) return;
            foreach (var entry in swapped)
            {
                Plugin.Log.LogInfo($"{entry.Lepo.name} took a surface material, its roughness and metallic go back to stock");
                Clear(entry);
            }
        }

        internal static void OnPlaced(LevelEditorPlaceableObject lepo)
        {
            if (lepo == null) return;
            if (!_objects.TryGetValue(lepo.gameObject.GetInstanceID(), out var entry)) return;
            WriteMarker(entry);
        }

        internal static void OnCloned(LevelEditorPlaceableObject clone, LevelEditorPlaceableObject source)
        {
            if (clone == null || source == null) return;
            if (!_objects.TryGetValue(source.gameObject.GetInstanceID(), out var src)) return;

            var entry = Track(clone);
            entry.Value = src.Value;
            Apply(entry);
        }

        internal static void OnColourChanged(LevelEditorPlaceableObject lepo)
        {
            if (lepo == null) return;
            if (!_objects.TryGetValue(lepo.gameObject.GetInstanceID(), out var entry)) return;
            entry.StockMats = null;
            Apply(entry);
        }

        internal static void OnDestroyed(LevelEditorPlaceableObject lepo)
        {
            if (lepo == null) return;
            int id = lepo.gameObject.GetInstanceID();
            if (!_objects.TryGetValue(id, out var entry)) return;
            _dirty.Remove(entry);
            _objects.Remove(id);
            if (entry.Marker != null) IdentifierObjects.Remove(entry.Marker);
        }

        private static void Clear(Entry entry)
        {
            Restore(entry);
            if (entry.Marker != null) IdentifierObjects.Remove(entry.Marker);
            entry.Marker = null;
            _dirty.Remove(entry);
            if (entry.Lepo != null) _objects.Remove(entry.Lepo.gameObject.GetInstanceID());
        }

        private static void WriteMarker(Entry entry)
        {
            if (entry.Marker != null) { IdentifierObjects.Remove(entry.Marker); entry.Marker = null; }

            if (Mathf.Abs(entry.Value.Rough - entry.Stock.Rough) < 0.01f && entry.Value.Metal < 0.01f)
            {
                Restore(entry);
                _objects.Remove(entry.Lepo.gameObject.GetInstanceID());
                return;
            }

            string hex = "#" + ColorUtility.ToHtmlStringRGB(new Color(entry.Value.Rough, entry.Value.Metal, 0f));
            entry.Marker = Kind.Spawn(entry.Lepo.transform.position, hex);
        }

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

                    var target = Nearest(col, marker.transform.position);
                    if (target == null) { missed++; continue; }

                    var colour = BatchTargets.GetColourParam(marker);
                    var rgb = colour != null ? colour.CurrentColour : Color.white;

                    var entry = Track(target);
                    entry.Marker = marker;
                    entry.Value = new Finish { Rough = Mathf.Clamp01(rgb.r), Metal = Mathf.Clamp01(rgb.g) };
                    Apply(entry);
                    done++;
                }
            }
            else
            {
                foreach (var m in IdentifierObjects.ReadRound(MarkerName))
                {
                    if (!Kind.MatchesRotation(m.Rotation)) continue;
                    if (string.IsNullOrEmpty(m.Hex) || !ColorUtility.TryParseHtmlString(m.Hex, out var rgb)) continue;

                    var target = Nearest(col, m.Position);
                    if (target == null) { missed++; continue; }

                    var entry = Track(target);
                    entry.Value = new Finish { Rough = Mathf.Clamp01(rgb.r), Metal = Mathf.Clamp01(rgb.g) };
                    Apply(entry);
                    done++;
                }
            }

            if (done > 0 || missed > 0)
                Plugin.Log.LogInfo($"surface finish: {done} refinished"
                    + (missed > 0 ? $", {missed} marker(s) with nothing near them" : ""));
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

        private static void Apply(Entry entry)
        {
            var rends = entry.Lepo.GetComponentsInChildren<Renderer>(true);
            if (entry.StockMats == null || entry.StockMats.Length != rends.Length) entry.StockMats = new Material[rends.Length][];
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null) continue;
                var now = rends[i].sharedMaterials;
                var kept = entry.StockMats[i];
                if (kept == null || kept.Length != now.Length) kept = new Material[now.Length];
                for (int k = 0; k < now.Length; k++)
                    if (now[k] != null && !now[k].name.EndsWith(FinishSuffix, StringComparison.Ordinal)) kept[k] = now[k];
                entry.StockMats[i] = kept;
            }

            bool metal = entry.Value.Metal > 0.01f;
            float smooth = 1f - entry.Value.Rough;

            for (int i = 0; i < rends.Length && i < entry.StockMats.Length; i++)
            {
                var r = rends[i];
                var stock = entry.StockMats[i];
                if (r == null || stock == null) continue;

                var live = new Material[stock.Length];
                bool touched = false;
                for (int m = 0; m < stock.Length; m++)
                {
                    var src = stock[m];
                    if (src == null) continue;
                    live[m] = src;
                    if (!Finishable(src)) continue;

                    var copy = new Material(src);
                    copy.name = src.name + FinishSuffix;
                    if (copy.HasProperty(RangeId)) copy.SetFloat(RangeId, smooth * SmoothnessSpan);
                    if (copy.HasProperty(FloorRangeId)) copy.SetFloat(FloorRangeId, smooth * FloorSpan);
                    if (copy.HasProperty(MetalBoostId)) copy.SetFloat(MetalBoostId, entry.Value.Metal);
                    else if (metal && copy.HasProperty(MapId)) copy.SetTexture(MapId, MetalMap(entry.Value.Metal, false));
                    if (metal && copy.HasProperty(FloorMapId)) copy.SetTexture(FloorMapId, MetalMap(entry.Value.Metal, true));
                    live[m] = copy;
                    touched = true;
                }
                if (touched) r.sharedMaterials = live;
            }
        }

        private static void Restore(Entry entry)
        {
            if (entry.StockMats == null || entry.Lepo == null) return;

            var rends = entry.Lepo.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length && i < entry.StockMats.Length; i++)
                if (rends[i] != null && entry.StockMats[i] != null) rends[i].sharedMaterials = entry.StockMats[i];
            entry.StockMats = null;
        }

        private static Texture2D MetalMap(float metal, bool floor)
        {
            int m = Mathf.RoundToInt(Mathf.Clamp01(metal) * 255f);
            int key = floor ? m + 256 : m;
            if (_maps.TryGetValue(key, out var tex) && tex != null) return tex;

            tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.name = "bfg_metallic";
            tex.SetPixel(0, 0, floor ? new Color(m / 255f, 0.5f, 0f, 1f) : new Color(m / 255f, 1f, 1f, 1f));
            tex.Apply();
            _maps[key] = tex;
            return tex;
        }

        private static readonly HashSet<IntPtr> _ourRows = new HashSet<IntPtr>();
        private static Il2CppSystem.Action<float> _roughCb;
        private static Il2CppSystem.Action<float> _metalCb;
        private static LevelEditorPlaceableObject _rowTarget;

        internal static void OnParamMenu(LevelEditorPlaceableObject lepo)
        {
            if (_rowTarget != null) CleanupRows(_rowTarget);
            _rowTarget = null;

            if (!Enabled || lepo == null) return;
            if (MarkerKinds.Of(lepo) != null || NeonMaterial.IsMarkerObject(lepo)) return;
            if (!CanFinish(lepo)) return;

            if (_roughCb == null) _roughCb = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<float>>(new Action<float>(OnRoughnessRow));
            if (_metalCb == null) _metalCb = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<float>>(new Action<float>(OnMetallicRow));

            _rowTarget = lepo;

            AddRow(lepo, ParameterUtils.CreateFloatEntry(LocalizationService.Get("surfacefinish.roughness"),
                IdentifierObjects.OnGrid(RoughnessOf(lepo), 0f, RowStep), 0f, 1f, RowStep, ParameterWrapMode.NoWrap, _roughCb,
                "{0}", "F2", null, null, false, 1f, null, false, false).Slider());

            AddRow(lepo, ParameterUtils.CreateFloatEntry(LocalizationService.Get("surfacefinish.metallic"),
                IdentifierObjects.OnGrid(MetallicOf(lepo), 0f, RowStep), 0f, 1f, RowStep, ParameterWrapMode.NoWrap, _metalCb,
                "{0}", "F2", null, null, false, 1f, null, false, false).Slider());
        }

        private const float RowStep = 0.05f;

        private static void AddRow(LevelEditorPlaceableObject lepo, NodeEntry row)
        {
            if (row == null) return;
            row.Tag(ParamCategory.Appearance);
            lepo.AddParameter(row, -1);
            _ourRows.Add(row.Pointer);
        }

        private static void CleanupRows(LevelEditorPlaceableObject lepo)
        {
            var existing = lepo.CustomParameters;
            if (existing == null || _ourRows.Count == 0) { _ourRows.Clear(); return; }

            for (int i = existing.Count - 1; i >= 0; i--)
            {
                var e = existing[i].ParameterEntry;
                if (e == null || !_ourRows.Remove(e.Pointer)) continue;
                existing.RemoveAt(i);
            }
            _ourRows.Clear();
        }

        private static void OnRoughnessRow(float v) { if (_rowTarget != null) SetRoughness(_rowTarget, v); }
        private static void OnMetallicRow(float v) { if (_rowTarget != null) SetMetallic(_rowTarget, v); }
    }
}
