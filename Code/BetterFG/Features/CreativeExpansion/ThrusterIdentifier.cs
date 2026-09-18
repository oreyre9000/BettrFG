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
    internal sealed class ThrusterIdentifier : IIdentifierObject
    {
        private static readonly HashSet<IntPtr> _ourRows = new HashSet<IntPtr>();
        private static Il2CppSystem.Action<float> _sizeCb;
        private static Il2CppSystem.Action<float> _beamCb;
        private static ParameterChangedIndex _colourCb;
        private static LevelEditorPlaceableObject _current;

        public bool KeepButtons => true;

        public bool Matches(LevelEditorPlaceableObject lepo) => Thrusters.Enabled && Thrusters.IsThruster(lepo);

        public string DisplayName(LevelEditorPlaceableObject lepo) => LocalizationService.Get("thruster.name");

        public string Description(LevelEditorPlaceableObject lepo) => LocalizationService.Get("thruster.description");

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

            if (_sizeCb == null) _sizeCb = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<float>>(new Action<float>(OnSize));
            if (_beamCb == null) _beamCb = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<float>>(new Action<float>(OnBeam));

            var size = ParameterUtils.CreateFloatEntry(LocalizationService.Get("thruster.size"), Thrusters.SizeOf(lepo),
                0.2f, 10f, 0.1f, ParameterWrapMode.NoWrap, _sizeCb, "{0}", "F1", null, null, false, 1f, null, false, false);
            if (size != null) { lepo.AddParameter(size, 0); _ourRows.Add(size.Pointer); }

            var beam = ParameterUtils.CreateFloatEntry(LocalizationService.Get("thruster.beam"), Thrusters.BeamOf(lepo),
                0f, 60f, 0.5f, ParameterWrapMode.NoWrap, _beamCb, "{0}", "F1", null, null, false, 1f, null, false, false);
            if (beam != null) { lepo.AddParameter(beam, 0); _ourRows.Add(beam.Pointer); }

            if (_colourCb == null) _colourCb = DelegateSupport.ConvertDelegate<ParameterChangedIndex>(new Action<int>(OnColour));
            var colour = ParameterUtils.CreateButtonEntry(LocalizationService.Get("thruster.colour"), ParameterWrapMode.NoWrap,
                _colourCb, null, null, false, 1f, null, false, false, null);
            if (colour != null) { lepo.AddParameter(colour, 0); _ourRows.Add(colour.Pointer); }
        }

        public Il2CppReferenceArray<NodeEntry> FilterRows(LevelEditorParameterMenuViewModel vm, LevelEditorPlaceableObject lepo)
        {
            var entries = vm.NodeEntries;
            var kept = new List<NodeEntry>(entries != null ? entries.Length : 0);
            if (entries != null)
                for (int i = 0; i < entries.Length; i++)
                {
                    var e = entries[i];
                    if (e != null && _ourRows.Contains(e.Pointer)) kept.Add(e);
                }

            var arr = new Il2CppReferenceArray<NodeEntry>(kept.Count);
            for (int i = 0; i < kept.Count; i++) arr[i] = kept[i];
            return arr;
        }

        private static void OnSize(float v) { if (_current != null) Thrusters.SetSize(_current, v); }
        private static void OnBeam(float v) { if (_current != null) Thrusters.SetBeam(_current, v); }
        private static void OnColour(int index) { if (_current != null) Thrusters.OpenPicker(_current); }
    }
}
