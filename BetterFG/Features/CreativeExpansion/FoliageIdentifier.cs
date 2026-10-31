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
    internal sealed class FoliageIdentifier : IIdentifierObject
    {
        private static readonly HashSet<IntPtr> _ourRows = new HashSet<IntPtr>();
        private static LevelEditorPlaceableObject _current;

        public bool Matches(LevelEditorPlaceableObject lepo) => Foliage.Enabled && Foliage.IsFoliage(lepo);

        public string DisplayName(LevelEditorPlaceableObject lepo) => LocalizationService.Get("foliage.name");

        public string Description(LevelEditorPlaceableObject lepo) => LocalizationService.Get("foliage.description");

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
            Add(lepo, IdentifierObjects.StepRow(LocalizationService.Get("foliage.size"), Foliage.SizeOf(lepo), Foliage.MinSize, Foliage.MaxSize, 0.1f, OnSize).Tag(ParamCategory.Size).Slider());
        }

        private static void Add(LevelEditorPlaceableObject lepo, NodeEntry row)
        {
            if (row == null) return;
            lepo.AddParameter(row, -1);
            _ourRows.Add(row.Pointer);
        }

        public Il2CppReferenceArray<NodeEntry> FilterRows(LevelEditorParameterMenuViewModel vm, LevelEditorPlaceableObject lepo)
        {
            var entries = vm.NodeEntries;
            var kept = new List<NodeEntry>(entries != null ? entries.Length : 0);
            bool tintable = Foliage.Tintable(lepo);
            if (entries != null)
                for (int i = 0; i < entries.Length; i++)
                {
                    var e = entries[i];
                    if (e != null && (_ourRows.Contains(e.Pointer) || (tintable && e.NodeType == ParameterNodeType.Color))) kept.Add(e);
                }

            var arr = new Il2CppReferenceArray<NodeEntry>(kept.Count);
            for (int i = 0; i < kept.Count; i++) arr[i] = kept[i];
            return arr;
        }

        private static void OnSize(float v) { if (_current != null) Foliage.SetSize(_current, v); }
    }
}
