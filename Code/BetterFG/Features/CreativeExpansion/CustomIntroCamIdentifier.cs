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
    internal sealed class CustomIntroCamIdentifier : IIdentifierObject
    {
        private static readonly HashSet<IntPtr> _ourRows = new HashSet<IntPtr>();
        private static Il2CppSystem.Action<float> _durationCb;
        private static Il2CppSystem.Action<int> _orderCb;
        private static ParameterChangedIndex _showCb;
        private static LevelEditorPlaceableObject _current;

        public bool KeepButtons => false;

        public bool Matches(LevelEditorPlaceableObject lepo) => CustomIntroCams.Enabled && (CustomIntroCams.IsBase(lepo) || CustomIntroCams.IsShot(lepo));

        public string DisplayName(LevelEditorPlaceableObject lepo)
            => LocalizationService.Get(CustomIntroCams.IsBase(lepo) ? "introcams.base_name" : "introcams.shot_name");

        public string Description(LevelEditorPlaceableObject lepo)
            => LocalizationService.Get(CustomIntroCams.IsBase(lepo) ? "introcams.base_description" : "introcams.shot_description");

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

            if (CustomIntroCams.IsBase(lepo))
            {
                if (_showCb == null) _showCb = DelegateSupport.ConvertDelegate<ParameterChangedIndex>(new Action<int>(OnShowPressed));
                if (_durationCb == null) _durationCb = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<float>>(new Action<float>(OnDuration));

                var show = ParameterUtils.CreateButtonEntry(LocalizationService.Get("introcams.show"),
                    ParameterWrapMode.NoWrap, _showCb, null, null, false, 1f, null, false, false, null);
                if (show != null) { lepo.AddParameter(show, 0); _ourRows.Add(show.Pointer); }

                var duration = ParameterUtils.CreateFloatEntry(LocalizationService.Get("introcams.duration"), CustomIntroCams.DurationOf(lepo),
                    CustomIntroCams.MinDuration, CustomIntroCams.MaxDuration, 0.5f, ParameterWrapMode.NoWrap,
                    _durationCb, "{0}", "F1", null, null, false, 1f, null, false, false);
                if (duration != null) { lepo.AddParameter(duration, 0); _ourRows.Add(duration.Pointer); }
                return;
            }

            if (_orderCb == null) _orderCb = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>(new Action<int>(OnOrder));

            var order = ParameterUtils.CreateIntEntry(LocalizationService.Get("introcams.order"), CustomIntroCams.OrderOf(lepo),
                CustomIntroCams.MinOrder, CustomIntroCams.ShotCount(), 1, ParameterWrapMode.NoWrap, _orderCb, "{0}",
                null, null, false, 1f, null, false, null, false);
            if (order != null) { lepo.AddParameter(order, 0); _ourRows.Add(order.Pointer); }
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
                    if (_ourRows.Contains(e.Pointer)) kept.Add(e);
                }

            var arr = new Il2CppReferenceArray<NodeEntry>(kept.Count);
            for (int i = 0; i < kept.Count; i++) arr[i] = kept[i];
            return arr;
        }

        private static void OnDuration(float v) { if (_current != null) CustomIntroCams.SetDuration(_current, v); }

        private static void OnOrder(int v) { if (_current != null) CustomIntroCams.SetOrder(_current, v); }

        private static void OnShowPressed(int index) => IntroCamPreview.Play();
    }
}
