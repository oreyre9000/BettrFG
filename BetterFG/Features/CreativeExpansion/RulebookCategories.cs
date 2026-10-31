using System.Collections.Generic;
using BetterFG.Services;
using TMPro;
using UnityEngine;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class RulebookCategories
    {
        private const string HeaderPrefix = "BFG_RulebookHeader_";
        private static readonly string[] HeaderKeys = { "rulebookcategory.info", "rulebookcategory.appearance" };

        internal static void Apply(global::RulebookMenuCollectionBinding binding)
        {
            var parent = binding._itemsParent;
            for (int i = parent.childCount - 1; i >= 0; i--)
                if (parent.GetChild(i).name.StartsWith(HeaderPrefix)) Object.DestroyImmediate(parent.GetChild(i).gameObject);

            var rows = new List<Transform>();
            for (int i = 0; i < binding._instances.Count; i++) rows.Add(binding._instances[i].transform);
            rows.Sort((a, b) => a.GetSiblingIndex().CompareTo(b.GetSiblingIndex()));

            int first = rows.FindIndex(IsHeader);
            if (first < 0) return;
            int end = rows.FindIndex(first + 1, IsHeader);
            if (end < 0) end = rows.Count;

            var buckets = new[] { new List<Transform>(), new List<Transform>(), new List<Transform>() };
            for (int i = first + 1; i < end; i++) buckets[Bucket(rows[i])].Add(rows[i]);

            var rulesHeader = rows[first];
            int sibling = rulesHeader.GetSiblingIndex();
            for (int c = 0; c < HeaderKeys.Length; c++)
            {
                if (buckets[c].Count == 0) continue;
                var header = Object.Instantiate(rulesHeader.gameObject, parent);
                header.name = HeaderPrefix + c;
                var text = header.transform.Find("Text");
                Object.DestroyImmediate(text.GetComponent<Mediatonic.Tools.MVVM.TMPTextBinding>());
                Object.DestroyImmediate(text.GetComponent<LocalisedStaticLabel>());
                text.GetComponent<TMP_Text>().text = LocalizationService.Get(HeaderKeys[c]);
                header.transform.SetSiblingIndex(sibling++);
                foreach (var row in buckets[c]) row.SetSiblingIndex(sibling++);
            }
            rulesHeader.SetSiblingIndex(sibling++);
            foreach (var row in buckets[2]) row.SetSiblingIndex(sibling++);

            var sel = binding._selectables;
            var ih = binding._inputHandler;
            var focused = ih != null && ih.CurrentIndex >= 0 && ih.CurrentIndex < sel.Count ? sel[ih.CurrentIndex] : null;
            var ordered = new List<GameObject>();
            for (int i = 0; i < sel.Count; i++) ordered.Add(sel[i]);
            ordered.Sort((a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
            sel.Clear();
            foreach (var go in ordered) sel.Add(go);

            if (ih == null) return;
            var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject>(ordered.ToArray());
            ih.SetOptions(arr, Mathf.Max(0, ordered.IndexOf(focused)), false);
        }

        private static bool IsHeader(Transform row) => row.GetComponent<LevelEditorRulebookEntryHeaderViewModel>() != null;

        private static int Bucket(Transform row)
        {
            if (row.GetComponentInChildren<LevelEditorRulebookEntryEditStringViewModel>(true) != null
                || row.GetComponentInChildren<LevelEditorRulebookEntryTagsButtonViewModel>(true) != null) return 0;
            if (row.name == BfgDisableBackgroundRow.RowName) return 1;
            var list = row.GetComponentInChildren<LevelEditorRulebookEntryHorizontalListViewModel>(true);
            return list != null && Definers.IsNativeRowTarget(list.GetInstanceID()) ? 1 : 2;
        }
    }
}
