using System;
using System.Collections.Generic;
using BetterFG.Services;
using BetterFG.Utilities;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LevelEditor;
using NodeEntry = LevelEditorParameterMenuViewModel.ParameterMenuNodeEntry;

namespace BetterFG.Features.CreativeExpansion
{
    internal sealed class CustomLightIdentifier : IIdentifierObject
    {
        private static readonly HashSet<IntPtr> _ourRows = new HashSet<IntPtr>();
        private static Il2CppSystem.Action<float> _radiusCb;
        private static Il2CppSystem.Action<float> _intensityCb;
        private static LevelEditorPlaceableObject _current;

        public bool KeepButtons => true;

        public bool Matches(LevelEditorPlaceableObject lepo) => CustomLights.Enabled && CustomLights.IsLight(lepo);

        public string DisplayName(LevelEditorPlaceableObject lepo) => LocalizationService.Get("customlights.name");

        public string Description(LevelEditorPlaceableObject lepo) => LocalizationService.Get("customlights.description");

        public void CleanupRows(LevelEditorPlaceableObject lepo)
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

        public void PrepareRows(LevelEditorPlaceableObject lepo)
        {
            _current = lepo;

            if (_radiusCb == null) _radiusCb = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<float>>(new Action<float>(OnRadius));
            if (_intensityCb == null) _intensityCb = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<float>>(new Action<float>(OnIntensity));

            var radius = ParameterUtils.CreateFloatEntry(LocalizationService.Get("customlights.radius"), CustomLights.RadiusOf(lepo),
                0.5f, 60f, 0.5f, ParameterWrapMode.NoWrap, _radiusCb, "{0}", "F1", null, null, false, 1f, null, false, false);
            if (radius != null) { lepo.AddParameter(radius, 0); _ourRows.Add(radius.Pointer); }

            var intensity = ParameterUtils.CreateFloatEntry(LocalizationService.Get("customlights.intensity"), CustomLights.IntensityOf(lepo),
                0f, 8f, 0.1f, ParameterWrapMode.NoWrap, _intensityCb, "{0}", "F2", null, null, false, 1f, null, false, false);
            if (intensity != null) { lepo.AddParameter(intensity, 0); _ourRows.Add(intensity.Pointer); }
        }

        public Il2CppReferenceArray<NodeEntry> FilterRows(LevelEditorParameterMenuViewModel vm, LevelEditorPlaceableObject lepo)
        {
            var entries = vm.NodeEntries;
            var kept = new List<NodeEntry>(entries != null ? entries.Length : 0);
            if (entries != null)
                for (int i = 0; i < entries.Length; i++)
                {
                    var e = entries[i];
                    if (e == null) continue;
                    if (_ourRows.Contains(e.Pointer) || e.NodeType == ParameterNodeType.Color) kept.Add(e);
                }

            var arr = new Il2CppReferenceArray<NodeEntry>(kept.Count);
            for (int i = 0; i < kept.Count; i++) arr[i] = kept[i];
            return arr;
        }

        private static void OnRadius(float v) { if (_current != null) CustomLights.SetRadius(_current, v); }
        private static void OnIntensity(float v) { if (_current != null) CustomLights.SetIntensity(_current, v); }
    }
}
