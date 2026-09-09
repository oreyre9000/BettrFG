using System;
using System.Collections.Generic;
using BetterFG.Services;
using BetterFG.UI.Windows.Creative;
using FG.Common;
using FG.Common.Fraggle;
using FG.Common.LevelEditor.Serialization;
using FGClient;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LevelEditor;
using MPG.Utility;
using ScriptableObjects;
using UnityEngine;
using NodeEntry = LevelEditorParameterMenuViewModel.ParameterMenuNodeEntry;

namespace BetterFG.Utilities
{
    public struct IdentifierMarker
    {
        public LevelEditorPlaceableObject Lepo;
        public Vector3 Position;
        public Vector3 Rotation;
        public Vector3 Scale;
        public string Hex;
    }

    public interface IIdentifierObject
    {
        bool KeepButtons { get; }
        bool Matches(LevelEditorPlaceableObject lepo);
        string DisplayName(LevelEditorPlaceableObject lepo);
        string Description(LevelEditorPlaceableObject lepo);
        void PrepareRows(LevelEditorPlaceableObject lepo);
        void CleanupRows(LevelEditorPlaceableObject lepo);
        Il2CppReferenceArray<NodeEntry> FilterRows(LevelEditorParameterMenuViewModel vm, LevelEditorPlaceableObject lepo);
    }

    public static class IdentifierObjectRegistry
    {
        private const int KeepButtonCount = 5;

        private static readonly List<IIdentifierObject> _all = new List<IIdentifierObject>();
        private static readonly HashSet<IntPtr> _warningRows = new HashSet<IntPtr>();
        private static ParameterChangedIndex _warningCb;
        private static IIdentifierObject _liveMatch;
        private static LevelEditorPlaceableObject _liveTarget;

        public static void Register(IIdentifierObject obj) => _all.Add(obj);

        public static IIdentifierObject Find(LevelEditorPlaceableObject lepo)
        {
            if (lepo == null) return null;
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].Matches(lepo)) return _all[i];
            return null;
        }

        public static string ResolveDisplayName(LevelEditorPlaceableObject lepo) => Find(lepo)?.DisplayName(lepo);

        public static string ResolveDescription(LevelEditorPlaceableObject lepo) => Find(lepo)?.Description(lepo);

        public static void OnBuildParameterEntriesPrefix(LevelEditorPlaceableObject target)
        {
            if (_liveMatch != null && _liveTarget != null)
            {
                _liveMatch.CleanupRows(_liveTarget);
                CleanupWarningRows(_liveTarget);
            }
            _liveMatch = null;
            _liveTarget = null;

            var match = Find(target);
            if (match == null) return;

            _liveMatch = match;
            _liveTarget = target;
            match.PrepareRows(target);
            if (match.KeepButtons) AddKeepButtons(target);
        }

        public static Il2CppReferenceArray<NodeEntry> OnBuildParameterEntriesPostfix(LevelEditorParameterMenuViewModel vm)
        {
            if (_liveMatch == null) return null;

            var own = _liveMatch.FilterRows(vm, _liveTarget);
            var kept = new List<NodeEntry>((own != null ? own.Length : 0) + _warningRows.Count);
            if (own != null) for (int i = 0; i < own.Length; i++) kept.Add(own[i]);

            var entries = vm.NodeEntries;
            if (entries != null)
                for (int i = 0; i < entries.Length; i++)
                {
                    var e = entries[i];
                    if (e != null && _warningRows.Contains(e.Pointer)) kept.Add(e);
                }

            var arr = new Il2CppReferenceArray<NodeEntry>(kept.Count);
            for (int i = 0; i < kept.Count; i++) arr[i] = kept[i];
            return arr;
        }

        private static void AddKeepButtons(LevelEditorPlaceableObject lepo)
        {
            if (_warningCb == null) _warningCb = DelegateSupport.ConvertDelegate<ParameterChangedIndex>(new Action<int>(OnWarningRowPressed));

            string keepLabel = LocalizationService.Get("identifierobject.keep_button");
            for (int i = 0; i < KeepButtonCount; i++)
            {
                var row = ParameterUtils.CreateButtonEntry(keepLabel, ParameterWrapMode.NoWrap, _warningCb,
                    null, null, false, 1f, null, false, false, null);
                if (row != null) { lepo.AddParameter(row, 0); _warningRows.Add(row.Pointer); }
            }
        }

        private static void CleanupWarningRows(LevelEditorPlaceableObject lepo)
        {
            var existing = lepo.CustomParameters;
            if (existing == null || _warningRows.Count == 0) { _warningRows.Clear(); return; }

            for (int i = existing.Count - 1; i >= 0; i--)
            {
                var e = existing[i].ParameterEntry;
                if (e == null || !_warningRows.Remove(e.Pointer)) continue;
                existing.RemoveAt(i);
            }
            _warningRows.Clear();
        }

        private static void OnWarningRowPressed(int index) { }
    }

    public static class GizmoMarkers
    {
        private struct Held
        {
            public LevelEditorWorldSpaceUI Ui;
            public LevelEditorWorldSpaceUI.WorldSpaceUIType Type;
        }

        private static readonly Dictionary<int, Held> _held = new Dictionary<int, Held>();

        public static void Ensure(LevelEditorPlaceableObject lepo, LevelEditorWorldSpaceUI.WorldSpaceUIType type)
        {
            int id = lepo.gameObject.GetInstanceID();
            if (_held.TryGetValue(id, out var existing) && existing.Ui != null) return;

            var mgr = LevelEditorWorldSpaceUIManager.Instance;
            if (mgr == null) return;

            var ui = mgr.CreateObjectWorldSpaceUI(type, lepo.transform);
            if (ui == null) return;

            _held[id] = new Held { Ui = ui, Type = type };
            foreach (var c in ui.GetComponentsInChildren<Collider>(true))
                if (c != null) UnityEngine.Object.Destroy(c);
        }

        public static void SetVisible(int instanceId, bool visible)
        {
            if (_held.TryGetValue(instanceId, out var h) && h.Ui != null) h.Ui.gameObject.SetActive(visible);
        }

        public static void Drop(int instanceId)
        {
            if (!_held.TryGetValue(instanceId, out var h)) return;
            _held.Remove(instanceId);
            if (h.Ui == null) return;

            var mgr = LevelEditorWorldSpaceUIManager.Instance;
            if (mgr != null) mgr.RemoveWorldSpaceUIForObject(h.Ui, true);
            else UnityEngine.Object.Destroy(h.Ui.gameObject);
        }

        public static void Prune(LevelEditorWorldSpaceUI.WorldSpaceUIType type, HashSet<int> keep)
        {
            if (_held.Count == 0) return;

            List<int> dead = null;
            foreach (var kv in _held)
                if (kv.Value.Type == type && !keep.Contains(kv.Key)) (dead ?? (dead = new List<int>())).Add(kv.Key);
            if (dead == null) return;
            foreach (int id in dead) Drop(id);
        }
    }

    public static class IdentifierObjects
    {
        private static readonly Dictionary<string, Il2CppSystem.Guid> _variantByName = new Dictionary<string, Il2CppSystem.Guid>();

        public static bool InEditor()
        {
            try { return GlobalGameStateClient.Instance != null && GlobalGameStateClient.Instance.IsInCreativeEditor; }
            catch { return LevelEditorManager.Instance != null; }
        }

        public static readonly int[] ColourPropertyIds =
        {
            Shader.PropertyToID("_Color"),
            Shader.PropertyToID("_TintColor"),
            Shader.PropertyToID("_BaseColor"),
            Shader.PropertyToID("_MainColor"),
        };

        private static readonly string[] UnlitShaderNames =
        {
            "Amplify/VFX_Opaque_Unlit",
            "Unlit/ENV_Standard_Triplanar",
            "Unlit/Color",
        };

        private static Shader _unlit;

        public static Shader UnlitShader()
        {
            if (_unlit != null) return _unlit;

            var all = Resources.FindObjectsOfTypeAll<Shader>();
            foreach (var want in UnlitShaderNames)
            {
                var s = Shader.Find(want);
                if (s == null)
                    foreach (var c in all)
                        if (c != null && c.name == want) { s = c; break; }
                if (s != null) { _unlit = s; return s; }
            }
            Plugin.Log.LogWarning("no unlit shader anywhere in the build, gizmo lines and neon shells will look wrong");
            return null;
        }

        public static Il2CppSystem.Guid? VariantGuid(string prefabName)
        {
            if (_variantByName.TryGetValue(prefabName, out var cached)) return cached;

            var pods = LevelEditorObjectList.CurrentObjects?.Cast<Il2CppSystem.Collections.Generic.List<PlaceableObjectData>>();
            if (pods == null) return null;

            foreach (var pod in pods)
            {
                if (pod == null || pod.DefaultVariant == null || pod.DefaultVariant.Prefab == null) continue;
                if (pod.DefaultVariant.Prefab.name != prefabName) continue;
                _variantByName[prefabName] = pod.DefaultVariant.Guid;
                return pod.DefaultVariant.Guid;
            }
            return null;
        }

        public static LevelEditorPlaceableObject Spawn(string prefabName, Vector3 pos, Vector3 rot, Vector3 scale, string hex, bool selectAtReticle = false)
        {
            var mgr = LevelEditorManager.Instance;
            var variant = VariantGuid(prefabName);
            if (mgr == null || !variant.HasValue)
            {
                Plugin.Log.LogWarning($"no variant guid for {prefabName}, not dropping an identifier");
                return null;
            }

            var schema = new UGCObjectDataSchema
            {
                Name = prefabName,
                ID = new Il2CppSystem.Nullable<int>(unchecked(0x42464700 + hex.GetHashCode() ^ pos.GetHashCode())),
                VariantGuid = new Il2CppSystem.Nullable<Il2CppSystem.Guid>(variant.Value),
                GUID = new Il2CppSystem.Nullable<Il2CppSystem.Guid>(Il2CppSystem.Guid.NewGuid()),
                Position = new Il2CppStructArray<float>(new[] { pos.x, pos.y, pos.z }),
                CurrentRotation = new Il2CppStructArray<float>(new[] { rot.x, rot.y, rot.z }),
                LocalScale = new Il2CppStructArray<float>(new[] { scale.x, scale.y, scale.z }),
                CurrentScale = new Il2CppStructArray<float>(new[] { scale.x, scale.y, scale.z }),
                ColourHexCode = hex,
            };

            var go = LevelLoader.LoadObject(schema, false);
            var lepo = go != null ? go.GetComponent<LevelEditorPlaceableObject>() : null;
            if (lepo == null) return null;

            BatchTargets.HideAndDecollide(lepo);

            if (selectAtReticle)
            {
                if (mgr.PlaceObjectFromLibrary(lepo, true, true))
                {
                    var held = mgr.GetReticleBase()?.SelectedObject;
                    if (held == null)
                    {
                        Plugin.Log.LogWarning($"{prefabName} went through the library path but the reticle is holding nothing, leaving it where it landed");
                        return lepo;
                    }
                    if (held.Pointer == lepo.Pointer) return lepo;

                    Plugin.Log.LogInfo($"library handed the reticle its own copy of {prefabName}, binning the one we loaded");
                    Remove(lepo);
                    return held;
                }

                Plugin.Log.LogWarning($"{prefabName} wouldn't go in hand off the library path, falling back to register + select");
                if (!LevelIO.IsObjectRegistered(lepo)) mgr.RegisterObject(lepo, true, true, true);
                var reticle = mgr.GetReticleBase()?.TryCast<LevelEditorStateReticleInputHandler>();
                if (reticle != null)
                    try { reticle.SetSelectedObject(lepo, true, true, false); }
                    catch (Exception ex) { Plugin.Log.LogWarning($"identifier select-at-reticle threw: {ex.Message}"); }
                return lepo;
            }

            try { lepo.OnDeselectInGameEditor(true); } catch { }
            if (!LevelIO.IsObjectRegistered(lepo)) mgr.RegisterObject(lepo, true, false, false);
            return lepo;
        }

        public static void Remove(LevelEditorPlaceableObject lepo)
        {
            if (lepo == null) return;
            try
            {
                var schema = LevelSaver.GetObjectSchema(lepo);
                if (schema.GUID.HasValue) LevelIO.RemoveGameObject(schema.GUID.Value);
            }
            catch { }
            UnityEngine.Object.Destroy(lepo.gameObject);
        }

        private static string _levelJson;
        private static IntPtr _levelJsonOwner = IntPtr.Zero;

        public static void InvalidateLevelJson()
        {
            _levelJson = null;
            _levelJsonOwner = IntPtr.Zero;
        }

        public static string LevelJson()
        {
            try
            {
                var loader = SingletonBehaviour<FraggleCommonManager>.Instance?.LevelLoader?.TryCast<LevelLoader>();
                if (loader == null) { InvalidateLevelJson(); return null; }
                if (loader.Pointer != _levelJsonOwner)
                {
                    _levelJsonOwner = loader.Pointer;
                    _levelJson = loader.WholeFile;
                }
                return _levelJson;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"identifier scan: WholeFile threw {ex.Message}");
                return null;
            }
        }

        public static IEnumerable<IdentifierMarker> ReadRound(string prefabName)
        {
            string json = LevelJson();
            if (string.IsNullOrEmpty(json)) yield break;

            string tag = "\"Name\":\"" + prefabName;
            int cursor = 0;
            while (true)
            {
                int nameIdx = json.IndexOf(tag, cursor, StringComparison.Ordinal);
                if (nameIdx < 0) yield break;
                cursor = nameIdx + tag.Length;

                char after = nameIdx + tag.Length < json.Length ? json[nameIdx + tag.Length] : '\0';
                if (after != '"' && after != '(') continue;

                int end = json.IndexOf("\"Name\":\"", cursor, StringComparison.Ordinal);
                if (end < 0) end = Math.Min(json.Length, nameIdx + 8192);

                yield return new IdentifierMarker
                {
                    Lepo = null,
                    Position = Float3(json, "Position", nameIdx, end) ?? Vector3.zero,
                    Rotation = Float3(json, "CurrentRotation", nameIdx, end) ?? Vector3.zero,
                    Scale = Float3(json, "CurrentScaleParam", nameIdx, end)
                            ?? Float3(json, "Local Scale", nameIdx, end)
                            ?? Vector3.one,
                    Hex = Str(json, "ColourHexCode", nameIdx, end),
                };
            }
        }

        public static string FindNearestColourHex(Vector3 pos, float radius, string excludeNamePrefix)
        {
            string json = LevelJson();
            if (string.IsNullOrEmpty(json)) return null;

            string bestHex = null;
            float bestSq = radius * radius;
            string tag = "\"Name\":\"";
            int cursor = 0;
            while (true)
            {
                int nameIdx = json.IndexOf(tag, cursor, StringComparison.Ordinal);
                if (nameIdx < 0) break;
                int nameStart = nameIdx + tag.Length;
                int nameEnd = json.IndexOf('"', nameStart);
                if (nameEnd < 0) break;

                int blockEnd = json.IndexOf(tag, nameEnd, StringComparison.Ordinal);
                if (blockEnd < 0) blockEnd = Math.Min(json.Length, nameIdx + 8192);

                bool excluded = excludeNamePrefix != null
                    && string.CompareOrdinal(json, nameStart, excludeNamePrefix, 0, excludeNamePrefix.Length) == 0;
                if (!excluded)
                {
                    var posV = Float3(json, "Position", nameIdx, blockEnd);
                    if (posV.HasValue)
                    {
                        float d = (posV.Value - pos).sqrMagnitude;
                        if (d <= bestSq)
                        {
                            string hex = Str(json, "ColourHexCode", nameIdx, blockEnd);
                            if (!string.IsNullOrEmpty(hex)) { bestSq = d; bestHex = hex; }
                        }
                    }
                }
                cursor = blockEnd;
            }
            return bestHex;
        }

        private static Vector3? Float3(string json, string field, int start, int end)
        {
            int i = json.IndexOf("\"" + field + "\":[", start, StringComparison.Ordinal);
            if (i < 0 || i >= end) return null;
            i += field.Length + 4;
            int close = json.IndexOf(']', i);
            if (close < 0 || close > end) return null;
            var p = json.Substring(i, close - i).Split(',');
            if (p.Length < 3) return null;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var ns = System.Globalization.NumberStyles.Float;
            if (!float.TryParse(p[0], ns, ci, out var x)) return null;
            if (!float.TryParse(p[1], ns, ci, out var y)) return null;
            if (!float.TryParse(p[2], ns, ci, out var z)) return null;
            return new Vector3(x, y, z);
        }

        private static string Str(string json, string field, int start, int end)
        {
            string key = "\"" + field + "\":\"";
            int i = json.IndexOf(key, start, StringComparison.Ordinal);
            if (i < 0 || i >= end) return null;
            i += key.Length;
            int close = json.IndexOf('"', i);
            if (close < 0 || close > end) return null;
            return json.Substring(i, close - i);
        }
    }
}
