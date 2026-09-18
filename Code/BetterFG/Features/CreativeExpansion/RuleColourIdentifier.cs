using System;
using System.Collections.Generic;
using BetterFG.Services;
using BetterFG.Utilities;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LevelEditor;
using NodeEntry = LevelEditorParameterMenuViewModel.ParameterMenuNodeEntry;

namespace BetterFG.Features.CreativeExpansion
{
    internal sealed class RuleColourIdentifier : IIdentifierObject
    {
        private static readonly HashSet<IntPtr> _ourRows = new HashSet<IntPtr>();
        private static ParameterChangedIndex _baseCb;
        private static ParameterChangedIndex _rimCb;
        private static ParameterChangedIndex _fencesCb;
        private static LevelEditorPlaceableObject _current;

        public bool KeepButtons => false;

        public bool Matches(LevelEditorPlaceableObject lepo) => RuleColours.Enabled && RuleColours.IsHost(lepo);

        public string DisplayName(LevelEditorPlaceableObject lepo) => null;

        public string Description(LevelEditorPlaceableObject lepo) => null;

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
            RuleColours.OnPlaced(lepo);

            if (_baseCb == null) _baseCb = DelegateSupport.ConvertDelegate<ParameterChangedIndex>(new Action<int>(OnBase));
            if (_rimCb == null) _rimCb = DelegateSupport.ConvertDelegate<ParameterChangedIndex>(new Action<int>(OnRim));
            if (_fencesCb == null) _fencesCb = DelegateSupport.ConvertDelegate<ParameterChangedIndex>(new Action<int>(OnFences));

            AddRow(lepo, "rulecolours.fences", _fencesCb);
            AddRow(lepo, "rulecolours.rim", _rimCb);
            AddRow(lepo, "rulecolours.base", _baseCb);
        }

        private static void AddRow(LevelEditorPlaceableObject lepo, string key, ParameterChangedIndex cb)
        {
            var row = ParameterUtils.CreateButtonEntry(LocalizationService.Get(key), ParameterWrapMode.NoWrap, cb,
                null, null, false, 1f, null, false, false, null);
            if (row == null) return;
            lepo.AddParameter(row, 0);
            _ourRows.Add(row.Pointer);
        }

        public Il2CppReferenceArray<NodeEntry> FilterRows(LevelEditorParameterMenuViewModel vm, LevelEditorPlaceableObject lepo)
            => vm.NodeEntries;

        private static void OnBase(int index) => Pick(RuleColours.Base);

        private static void OnRim(int index) => Pick(RuleColours.Rim);

        private static void OnFences(int index) => Pick(RuleColours.Fences);

        private static void Pick(int slot)
        {
            if (_current == null) return;
            RuleColours.OpenPicker(_current, slot);
        }
    }

    [HarmonyPatch(typeof(LevelEditorParameterMenuViewModel), nameof(LevelEditorParameterMenuViewModel.OpenParametersScreen))]
    internal static class RuleColourParamScreenTargetPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref LevelEditorPlaceableObject __0)
        {
            if (!RuleColours.IsMarker(__0)) return;
            var owner = RuleColours.OwnerOf(__0);
            if (owner != null) __0 = owner;
        }
    }
}
