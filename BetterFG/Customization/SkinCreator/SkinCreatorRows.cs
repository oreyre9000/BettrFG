using System;
using BettrFG.uGUI;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Customization.SkinCreator
{
    internal static class SkinCreatorRows
    {
        public const float RowH = 40f;
        public const float HeaderH = 84f;
        public const int FsRow = 12;

        public static void Header(Transform parent, string titleId, string subId)
        {
            var head = UGUIShip.CreateLabel(parent, new Rect(16f, 12f, 330f, 34f), titleId, 13, UGUIShip.WHITE);
            head.fontStyle = FontStyle.Bold;
            head.rectTransform.localScale = new Vector3(1.3f, 1.3f, 1f);
            UGUIShip.CreateLabel(parent, new Rect(16f, 52f, 408f, 22f), subId, 10, new Color(1f, 1f, 1f, 0.55f));
        }

        public static (ScrollRect scroll, RectTransform content) List(RectTransform parent, float contentH)
        {
            var (sr, content) = UGUIShip.CreateScrollView(parent, new Rect(0f, HeaderH, 440f, 100f));
            var rt = sr.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = new Vector2(0f, -HeaderH);
            content.sizeDelta = new Vector2(0f, contentH);
            return (sr, content);
        }

        public static float ContentHeight(int rows) => rows * RowH + 8f;

        public static Button Row(RectTransform content, int index, float w, string label, float labelW)
        {
            var go = new GameObject("Row" + index);
            go.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(go.AddComponent<RectTransform>(), new Rect(0f, 4f + index * RowH, w, RowH));
            var img = go.AddComponent<Image>();
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            UGUIShip.PaintListRow(btn, index, false);
            UGUIShip.CreateLabel(go.transform, new Rect(14f, 0f, labelW, RowH), label, FsRow, UGUIShip.WHITE);
            return btn;
        }

        public static float Clips(RectTransform content, float w, string[] names, Action<int> pick)
        {
            var rows = new Button[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int ci = i;
                rows[i] = Row(content, i, w, names[i], w - 28f);
                rows[i].onClick.AddListener(new Action(() =>
                {
                    for (int j = 0; j < rows.Length; j++) UGUIShip.PaintListRow(rows[j], j, j == ci);
                    pick(ci);
                }));
            }
            UGUIShip.PaintListRow(rows[0], 0, true);
            return ContentHeight(names.Length);
        }
    }
}
