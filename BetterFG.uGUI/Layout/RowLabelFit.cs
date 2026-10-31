using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Text = UnityEngine.UI.Text;

namespace BettrFG.uGUI
{
    public static class RowLabelFit
    {
        private const int SettleFrames = 6;

        private sealed class Entry
        {
            public GameObject row;
            public RectTransform parentRt;
            public LayoutElement le;
            public Text label;
            public RectTransform labelRt;
            public float baseHeight;
            public int settle = SettleFrames;
        }

        private static readonly List<Entry> _active = new List<Entry>();

        public static void Attach(GameObject row) => _active.Add(new Entry { row = row });

        public static void Pump()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var e = _active[i];
                if (e.row == null) { _active.RemoveAt(i); continue; }
                if (!e.row.activeInHierarchy) continue;
                if (!Step(e)) _active.RemoveAt(i);
            }
        }

        private static bool Step(Entry e)
        {
            if (e.le == null)
            {
                e.le = e.row.GetComponent<LayoutElement>();
                if (e.le == null) return false;
                var p = e.row.transform.parent;
                e.parentRt = p != null ? p.GetComponent<RectTransform>() : null;
                e.baseHeight = e.le.preferredHeight;
            }

            if (e.label == null)
            {
                e.label = WidestChildText(e.row.transform);
                if (e.label == null) return --e.settle > 0;
                e.labelRt = e.label.rectTransform;
                e.label.horizontalOverflow = HorizontalWrapMode.Wrap;
                e.label.verticalOverflow = VerticalWrapMode.Overflow;
                var watched = e;
                e.label.RegisterDirtyVerticesCallback(new Action(() => Wake(watched)));
            }

            Fit(e);
            return --e.settle > 0;
        }

        private static void Wake(Entry e)
        {
            if (e.settle > 0) return;
            e.settle = 1;
            _active.Add(e);
        }

        private static void Fit(Entry e)
        {
            var mirror = e.labelRt.Find("TmpMirror");
            var tmp = mirror != null ? mirror.GetComponent<TMPro.TextMeshProUGUI>() : null;
            float width = e.labelRt.rect.width;
            float extra = tmp != null
                ? tmp.GetPreferredValues(e.label.text, width, 0f).y - tmp.GetPreferredValues("A", width, 0f).y
                : e.label.preferredHeight - (e.label.fontSize + 4f);
            float want = Mathf.Ceil(e.baseHeight + Mathf.Max(0f, extra));
            if (Mathf.Approximately(e.le.preferredHeight, want)) return;

            e.le.preferredHeight = want;
            if (Mathf.Approximately(e.labelRt.anchorMin.y, e.labelRt.anchorMax.y))
                e.labelRt.sizeDelta = new Vector2(e.labelRt.sizeDelta.x, want);
            if (e.parentRt != null) LayoutRebuilder.MarkLayoutForRebuild(e.parentRt);
        }

        private static Text WidestChildText(Transform row)
        {
            Text best = null;
            float bestW = float.NegativeInfinity;
            for (int i = 0; i < row.childCount; i++)
            {
                var child = row.GetChild(i);
                var t = child.GetComponent<Text>();
                if (t == null) continue;
                float w = child.GetComponent<RectTransform>().rect.width;
                if (w > bestW) { best = t; bestW = w; }
            }
            return best;
        }
    }
}
