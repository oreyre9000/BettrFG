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
        private static Il2CppSystem.Action<float> _beamCb;
        private static ParameterChangedIndex _colourCb;
        private static ParameterChangedIndex _coreCb;
        private static LevelEditorPlaceableObject _marker;
        private static bool _onController;

        public bool Matches(LevelEditorPlaceableObject lepo)
            => Thrusters.Enabled && (Thrusters.IsThruster(lepo) || Thrusters.IsThrusterController(lepo));

        public string DisplayName(LevelEditorPlaceableObject lepo) => LocalizationService.Get("thruster.name");

        public string Description(LevelEditorPlaceableObject lepo) => LocalizationService.Get("thruster.description");

        public void CleanupRows(LevelEditorPlaceableObject lepo)
        {
            var existing = lepo.CustomParameters;
            for (int i = existing.Count - 1; i >= 0; i--)
            {
                var e = existing[i].ParameterEntry;
                if (e != null && _ourRows.Contains(e.Pointer)) existing.RemoveAt(i);
            }
            _ourRows.Clear();
        }

        public void PrepareRows(LevelEditorPlaceableObject lepo)
        {
            _onController = !Thrusters.IsThruster(lepo);
            _marker = _onController ? Thrusters.MarkerOf(lepo) : lepo;

            if (_beamCb == null) _beamCb = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<float>>(new Action<float>(v => Thrusters.SetBeam(_marker, v)));
            if (_colourCb == null) _colourCb = DelegateSupport.ConvertDelegate<ParameterChangedIndex>(new Action<int>(_ => Thrusters.OpenPicker(_marker)));
            if (_coreCb == null) _coreCb = DelegateSupport.ConvertDelegate<ParameterChangedIndex>(new Action<int>(_ => Thrusters.OpenCorePicker(_marker)));

            Add(lepo, 0, IdentifierObjects.StepRow(LocalizationService.Get("thruster.size"), Thrusters.SizeOf(_marker), 0.2f, 10f, 0.1f,
                v => Thrusters.SetSize(_marker, v)).Tag(ParamCategory.Size).Slider());
            Add(lepo, 1, ParameterUtils.CreateFloatEntry(LocalizationService.Get("thruster.beam"), Thrusters.BeamOf(_marker),
                0f, Thrusters.MaxBeam, 0.5f, ParameterWrapMode.NoWrap, _beamCb, "{0}", "F1", null, null, false, 1f, null, false, false).Tag(ParamCategory.Appearance).Slider());
            Add(lepo, 2, ParameterUtils.CreateButtonEntry(LocalizationService.Get("thruster.colour"), ParameterWrapMode.NoWrap,
                _colourCb, null, null, false, 1f, null, false, false, null).Tag(ParamCategory.Appearance));
            Add(lepo, 3, ParameterUtils.CreateButtonEntry(LocalizationService.Get("thruster.core_colour"), ParameterWrapMode.NoWrap,
                _coreCb, null, null, false, 1f, null, false, false, null).Tag(ParamCategory.Appearance));
            ColourSwatches.Show(LocalizationService.Get("thruster.colour"), () => Thrusters.StockBeam, () => Thrusters.BeamSwatch(_marker));
            ColourSwatches.Show(LocalizationService.Get("thruster.core_colour"), () => Thrusters.StockCore, () => Thrusters.CoreSwatch(_marker));
        }

        private static void Add(LevelEditorPlaceableObject lepo, int at, NodeEntry row)
        {
            lepo.AddParameter(row, at);
            _ourRows.Add(row.Pointer);
        }

        public Il2CppReferenceArray<NodeEntry> FilterRows(LevelEditorParameterMenuViewModel vm, LevelEditorPlaceableObject lepo)
        {
            var kept = new List<NodeEntry>();
            foreach (var e in vm.NodeEntries)
                if (_onController || _ourRows.Contains(e.Pointer)) kept.Add(e);
            return new Il2CppReferenceArray<NodeEntry>(kept.ToArray());
        }
    }
}
