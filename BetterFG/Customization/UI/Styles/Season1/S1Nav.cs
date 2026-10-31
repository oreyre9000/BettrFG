using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Customization.UI.Styles.Season1
{
    internal static class S1Nav
    {
        static readonly HashSet<int> _done = new HashSet<int>();
        static Transform _text;
        static RectTransform _icon;
        static HorizontalLayoutGroup _row, _item;

        public static void Teardown() => _done.Clear();

        public static void Apply(UIStyle style, Transform overlay)
        {
            if (_text == null)
            {
                var d = S1Kit.Donor(style, "screen_Settings_UICanvas").Find("Prefab_UI_SubMenu_Settings/SafeArea/Prefab_UI_Settings_Page_Audio/SubMenuNavigation");
                _text = d.Find("Back/SubMenuNavigationBackText");
                _icon = d.Find("Back/SubMenuNavigationBackIcon").GetComponent<RectTransform>();
                _row = d.GetComponent<HorizontalLayoutGroup>();
                _item = d.Find("Back").GetComponent<HorizontalLayoutGroup>();
            }
            var sa = overlay.Find("SafeArea");
            if (sa == null) return;
            for (int i = 0; i < sa.childCount; i++)
            {
                var g = sa.GetChild(i);
                if (!_done.Add(g.GetInstanceID())) continue;
                foreach (var b in g.GetComponents<Behaviour>()) if (b.GetIl2CppType().Name == "AppearTween") { StyleUndo.Enabled(b); b.enabled = false; }
                var h = g.GetComponent<HorizontalLayoutGroup>();
                if (h != null && g.name.EndsWith("_Right"))
                {
                    StyleUndo.Layout(h);
                    h.padding = new RectOffset(_row.padding.left, _row.padding.right, _row.padding.top, _row.padding.bottom);
                    h.spacing = _row.spacing;
                    h.childAlignment = _row.childAlignment;
                }
                StyleUndo.Rect(g);
                g.localScale = Vector3.one;
                var rt = g.GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, 0f);
                if (g.GetComponent<CanvasGroup>() != null) StyleUndo.Group(g).alpha = 1f;
            }
            foreach (var img in sa.GetComponentsInChildren<Image>(true))
            {
                if (!_done.Add(img.GetInstanceID())) continue;
                if (img.name.StartsWith("Backdrop_Fill")) { StyleUndo.Graphic(img); img.enabled = false; }
                else if (img.name.StartsWith("PromptIcon") && img.name != "PromptIcon - Pad") { StyleUndo.Rect(img.transform); img.rectTransform.sizeDelta = _icon.sizeDelta; }
            }
            foreach (var t in sa.GetComponentsInChildren<TMP_Text>(true))
            {
                if (!t.name.StartsWith("PromptText") || !_done.Add(t.GetInstanceID())) continue;
                S1Kit.Text(t.transform, _text);
                StyleUndo.Rect(t.transform);
                t.transform.SetSiblingIndex(0);
                var h = t.transform.parent.GetComponent<HorizontalLayoutGroup>();
                if (h == null) continue;
                StyleUndo.Layout(h);
                h.spacing = _item.spacing;
                h.childAlignment = _item.childAlignment;
            }
        }
    }
}
