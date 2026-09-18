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
    internal sealed class ObjectThemeIdentifier : IIdentifierObject
    {
        private static readonly HashSet<IntPtr> _ourRows = new HashSet<IntPtr>();
        private static ParameterChangedIndex _themeCb;
        private static Il2CppSystem.Action<bool> _balloonCb;
        private static LevelEditorPlaceableObject _current;

        public bool KeepButtons => false;

        public bool Matches(LevelEditorPlaceableObject lepo) => ObjectThemes.Enabled && ObjectThemes.IsThemable(lepo);

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
            ObjectThemes.OnParamMenu(lepo);

            if (_themeCb == null) _themeCb = DelegateSupport.ConvertDelegate<ParameterChangedIndex>(new Action<int>(OnThemePicked));

            var items = new Il2CppSystem.Collections.ArrayList();
            foreach (var label in ObjectThemes.Labels(lepo)) items.Add((Il2CppSystem.String)label);

            var row = ParameterUtils.CreateStringEntry(LocalizationService.Get("objectthemes.theme"),
                ObjectThemes.ThemeOf(lepo), items.Cast<Il2CppSystem.Collections.ICollection>(),
                ParameterWrapMode.Wrap, _themeCb, null, null, false, 1f, null, false, false);
            if (row == null) return;

            // Appended, never inserted at the top. The game rebuilds this menu whenever a parameter
            // changes size and puts focus back on the first row, so a row of ours at index 0 stole
            // the focus mid-edit and the next left/right press changed the theme instead of the size.
            var rows = lepo.CustomParameters;
            lepo.AddParameter(row, rows != null ? rows.Count : 0);
            _ourRows.Add(row.Pointer);

            if (!ObjectThemes.HasBalloon(lepo)) return;

            if (_balloonCb == null) _balloonCb = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>(new Action<bool>(OnBalloonToggled));

            var balloon = ParameterUtils.CreateBoolEntry(LocalizationService.Get("objectthemes.balloon"),
                ObjectThemes.BalloonOn(lepo), ParameterWrapMode.NoWrap, _balloonCb, null, null, false, 1f, null, false, false);
            if (balloon == null) return;
            rows = lepo.CustomParameters;
            lepo.AddParameter(balloon, rows != null ? rows.Count : 0);
            _ourRows.Add(balloon.Pointer);
        }

        public Il2CppReferenceArray<NodeEntry> FilterRows(LevelEditorParameterMenuViewModel vm, LevelEditorPlaceableObject lepo)
            => vm.NodeEntries;

        private static void OnThemePicked(int index)
        {
            if (_current == null) return;
            ObjectThemes.SetTheme(_current, index);
        }

        private static void OnBalloonToggled(bool on)
        {
            if (_current == null) return;
            ObjectThemes.SetBalloonOn(_current, on);
        }
    }
}
