using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;

namespace BetterFG.Utilities
{
    internal static class RulebookRowClone
    {
        public struct Result
        {
            public GameObject Clone;
            public bool Created;
            public int CloneVmId;
            public TMP_Text LabelTmp;
            public TMP_Text ValueTmp;
        }

        public static Result Inject(global::RulebookMenuCollectionBinding binding, string rowName)
        {
            var result = new Result();

            Transform parent = null;
            try { parent = binding._itemsParent; } catch { }
            if (parent == null) return result;

            var src = parent.GetComponentInChildren<LevelEditorRulebookEntryHorizontalListViewModel>(true);
            if (src == null) return result;
            var srcGo = src.transform.parent != null && binding._selectables.Contains(src.transform.parent.gameObject)
                ? src.transform.parent.gameObject : src.gameObject;

            GameObject clone = null;
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == rowName) { clone = parent.GetChild(i).gameObject; break; }

            if (clone == null)
            {
                string srcLabel = null, srcValue = null;
                try { srcLabel = src.EntryName; } catch { }
                try { srcValue = src.CurrentValue; } catch { }

                clone = UnityEngine.Object.Instantiate(srcGo, parent);
                clone.name = rowName;
                clone.transform.SetSiblingIndex(srcGo.transform.GetSiblingIndex() + 1);

                var cloneVm = clone.GetComponentInChildren<LevelEditorRulebookEntryHorizontalListViewModel>(true);
                result.CloneVmId = cloneVm != null ? cloneVm.GetInstanceID() : 0;

                TMP_Text labelTmp = null, valueTmp = null;
                var all = clone.GetComponentsInChildren<TMP_Text>(true);
                foreach (var t in all)
                {
                    if (valueTmp == null && !string.IsNullOrEmpty(srcValue) && t.text == srcValue) { valueTmp = t; continue; }
                    if (labelTmp == null && !string.IsNullOrEmpty(srcLabel) && t.text == srcLabel) labelTmp = t;
                }
                if (labelTmp == null || valueTmp == null)
                {
                    var vis = new List<TMP_Text>();
                    foreach (var t in all) if (!string.IsNullOrEmpty(t.text)) vis.Add(t);
                    if (vis.Count >= 2) { labelTmp = labelTmp ?? vis[0]; valueTmp = valueTmp ?? vis[vis.Count - 1]; }
                }

                result.LabelTmp = labelTmp;
                result.ValueTmp = valueTmp;
                result.Created = true;
            }

            Register(binding, srcGo, clone);

            result.Clone = clone;
            return result;
        }

        public static void Register(global::RulebookMenuCollectionBinding binding, GameObject srcGo, GameObject clone, bool before = false)
        {
            RegisterInList(binding._instances, srcGo, clone, before);
            RegisterInList(binding._selectables, srcGo, clone, before);

            var ih = binding._inputHandler;
            if (ih != null)
            {
                var sel = binding._selectables;
                var arr = new Il2CppReferenceArray<GameObject>(sel.Count);
                for (int k = 0; k < sel.Count; k++) arr[k] = sel[k];
                int keep = Mathf.Clamp(ih.CurrentIndex, 0, Mathf.Max(0, sel.Count - 1));
                ih.SetOptions(arr, keep, false);
            }
        }

        private static void RegisterInList(Il2CppSystem.Collections.Generic.List<GameObject> list, GameObject anchor, GameObject go, bool before)
        {
            if (list == null || go == null) return;
            if (list.Contains(go)) return;
            int at = list.IndexOf(anchor);
            if (at >= 0) list.Insert(before ? at : at + 1, go); else list.Add(go);
        }
    }
}
