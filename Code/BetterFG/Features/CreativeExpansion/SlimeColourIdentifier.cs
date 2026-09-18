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
    internal sealed class SlimeColourIdentifier : IIdentifierObject
    {
        private static readonly HashSet<IntPtr> _ourRows = new HashSet<IntPtr>();
        private static ParameterChangedIndex _colourCb;
        private static LevelEditorPlaceableObject _current;

        public bool KeepButtons => false;

        public bool Matches(LevelEditorPlaceableObject lepo) => SlimeColours.Enabled && SlimeColours.IsHost(lepo);

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
            SlimeColours.OnPlaced(lepo);

            if (_colourCb == null) _colourCb = DelegateSupport.ConvertDelegate<ParameterChangedIndex>(new Action<int>(OnColour));

            var row = ParameterUtils.CreateButtonEntry(LocalizationService.Get("slimecolour.colour"),
                ParameterWrapMode.NoWrap, _colourCb, null, null, false, 1f, null, false, false, null);
            if (row == null) return;
            lepo.AddParameter(row, 0);
            _ourRows.Add(row.Pointer);
        }

        public Il2CppReferenceArray<NodeEntry> FilterRows(LevelEditorParameterMenuViewModel vm, LevelEditorPlaceableObject lepo)
            => vm.NodeEntries;

        private static void OnColour(int index)
        {
            if (_current == null) return;
            SlimeColours.OpenPicker(_current);
        }
    }

    [HarmonyPatch(typeof(LevelEditorParameterMenuViewModel), nameof(LevelEditorParameterMenuViewModel.OpenParametersScreen))]
    internal static class SlimeColourParamScreenTargetPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref LevelEditorPlaceableObject __0)
        {
            if (!SlimeColours.IsMarker(__0)) return;
            var owner = SlimeColours.OwnerOf(__0);
            if (owner != null) __0 = owner;
        }
    }
}
