using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Customization.UI.Styles.Season1
{
    internal static class S1Skins
    {
        public const int MODAL = 13;
        const string TITLE_FONT = "TitanOne-Regular SDF (Title)";
        const string TITLE_MAT = "TitanOne-Regular SDF Material";
        static readonly HashSet<int> _done = new HashSet<int>();
        static readonly Dictionary<string, Material> _titleMats = new Dictionary<string, Material>();
        static Transform _modal;
        static TMP_FontAsset _title;

        public static void Teardown() => _done.Clear();

        static Transform Modal(UIStyle style) =>
            _modal != null ? _modal : _modal = S1Kit.Donor(style, "Prefab_UI_Modal_Message").Find("ModalMessageContainer/ModalMessageBacking");

        public static void Button(Transform b, Transform donor, bool size, Sprite idle)
        {
            TMP_Text dText = null;
            foreach (var t in donor.GetComponentsInChildren<TMP_Text>(true)) if (t.name != "GlyphText") { dText = t; break; }
            foreach (var state in new[] { "Panel_Unselected", "Panel_Selected" })
            {
                var p = b.Find(state);
                if (p == null) continue;
                for (int i = 0; i < p.childCount; i++)
                {
                    var c = p.GetChild(i);
                    if (c.name != "Panel_Fill")
                    {
                        foreach (var g in c.GetComponentsInChildren<Graphic>(true)) { StyleUndo.Graphic(g); g.enabled = false; }
                        continue;
                    }
                    S1Kit.Image(c, donor);
                    if (idle != null && state == "Panel_Unselected") c.GetComponent<Image>().sprite = idle;
                    StyleUndo.Rect(c);
                    var rt = c.GetComponent<RectTransform>();
                    rt.offsetMin = rt.offsetMax = Vector2.zero;
                }
            }
            S1Kit.Text(b.Find("Content/Text"), dText.transform, size);
        }

        public static void Compact(Transform root)
        {
            const float scale = 0.75f;
            const float k = 1f / scale;
            void Place(Transform t, Vector2 min, Vector2 max, Vector2 pivot, Vector2 pos, Vector2 size)
            {
                StyleUndo.Rect(t);
                var r = t.GetComponent<RectTransform>();
                r.anchorMin = min;
                r.anchorMax = max;
                r.pivot = pivot;
                r.anchoredPosition = pos * k;
                r.sizeDelta = size * k;
            }
            StyleUndo.Rect(root);
            root.localScale = new Vector3(scale, scale, 1f);
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(1000f, 605f + 78.1f * k);
            var title = root.Find("Panel/Title");
            Place(title, new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 68.1f));
            Place(title.Find("Pattern_Mask"), Vector2.zero, Vector2.one, new Vector2(0.5f, 1f), new Vector2(-3.5f, 0f), new Vector2(-19f, 0f));
            Place(title.Find("Pattern_Mask/Pattern"), Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(-3f, -2.3f), new Vector2(4.9f, 193.6f));
            Place(title.Find("Separator"), Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 1f), new Vector2(-6.5f, 0f), new Vector2(-77f, 2f));
            Place(title.Find("Title_Text"), new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(-60f, 43.5f));
            var tt = title.Find("Title_Text").GetComponent<TMP_Text>();
            StyleUndo.Graphic(tt);
            tt.enableAutoSizing = false;
            tt.fontSize = 38f * k;
            tt.alignment = TextAlignmentOptions.Midline;
            tt.enableWordWrapping = false;
            StyleUndo.Rect(root.Find("Content"));
            var content = root.Find("Content").GetComponent<RectTransform>();
            content.offsetMax = new Vector2(content.offsetMax.x, -78.1f * k);
            var bc = root.Find("ButtonContainer");
            StyleUndo.Layout(bc.GetComponent<HorizontalLayoutGroup>());
            bc.GetComponent<HorizontalLayoutGroup>().spacing = 28f * k;
            Place(bc, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 83f));
            for (int i = 0; i < bc.childCount; i++)
            {
                var b = bc.GetChild(i);
                foreach (var im in b.GetComponentsInChildren<Image>(true)) if (im.name == "Panel_Fill") { StyleUndo.Graphic(im); im.pixelsPerUnitMultiplier = scale; }
                TMP_Text text = null;
                foreach (var t in b.GetComponentsInChildren<TMP_Text>(true)) if (t.gameObject.activeInHierarchy && (t.name == "Text" || t.name == "PurchaseText")) text = t;
                StyleUndo.Graphic(text);
                text.enableAutoSizing = false;
                text.fontSize = 28f * k;
                text.enableWordWrapping = false;
                text.alignment = TextAlignmentOptions.Midline;
                text.text = text.text.Replace("\\n", " ").Replace("\n", " ");
                var hold = b.Find("Content/PurchaseText/Text/PromptContainer");
                var glyph = (hold ?? b.Find("Content/Prompt")).GetComponent<RectTransform>();
                var le = glyph.GetComponent<LayoutElement>();
                StyleUndo.Element(le);
                StyleUndo.Rect(glyph);
                le.preferredWidth = le.preferredHeight = 38f * k;
                glyph.sizeDelta = new Vector2(38f * k, 38f * k);
                if (hold != null)
                {
                    StyleUndo.Rect(hold.Find("FillBackground"));
                    StyleUndo.Rect(hold.Find("FillBackground/Prompt "));
                    StyleUndo.Element(b.Find("Content/PurchaseText/Text").GetComponent<LayoutElement>());
                    hold.Find("FillBackground").GetComponent<RectTransform>().sizeDelta = new Vector2(38f * k, 38f * k);
                    hold.Find("FillBackground/Prompt ").GetComponent<RectTransform>().sizeDelta = new Vector2(30f * k, 30f * k);
                    b.Find("Content/PurchaseText/Text").GetComponent<LayoutElement>().preferredWidth = -1f;
                }
                foreach (var h in b.GetComponentsInChildren<HorizontalLayoutGroup>(true))
                {
                    StyleUndo.Layout(h);
                    h.spacing = 14f * k;
                    h.padding = new RectOffset(0, 0, 0, 0);
                }
                StyleUndo.Rect(b.Find("Content"));
                StyleUndo.Rect(b);
                var c = b.Find("Content").GetComponent<RectTransform>();
                c.offsetMin = new Vector2(0f, 8f * k);
                c.offsetMax = Vector2.zero;
                float pad = hold == null ? 32f : 28f;
                b.GetComponent<RectTransform>().sizeDelta = new Vector2((pad * 2f + 38f + 14f) * k + text.GetPreferredValues(text.text).x, 83f * k);
            }
        }

        public static void Modal(UIStyle style, Transform root)
        {
            var d = Modal(style);
            var panel = root.Find("Panel");
            if (panel != null)
            {
                S1Kit.Image(panel.Find("Panel_Fill"), d);
                S1Kit.Off(panel.Find("Panel_Shadow"));
                S1Kit.Off(panel.Find("Panel_Outline"));
                var title = panel.Find("Title");
                if (title != null)
                {
                    S1Kit.Off(title.Find("Title_Fill"));
                    S1Kit.Image(title.Find("Pattern_Mask"), d.Find("ModalMessagePatternMask"));
                    S1Kit.Image(title.Find("Pattern_Mask/Pattern"), d.Find("ModalMessagePatternMask/ModalMessagePatternIcon"));
                    var mask = title.Find("Pattern_Mask");
                    var dcg = d.Find("ModalMessagePatternMask")?.GetComponent<CanvasGroup>();
                    if (mask != null && dcg != null) StyleUndo.Group(mask).alpha = dcg.alpha;
                    S1Kit.Image(title.Find("Separator"), d.Find("ModalMessageLine"));
                    S1Kit.Text(title.Find("Title_Text"), d.Find("ModalMessageTitle"));
                }
            }
            var content = root.Find("Content");
            if (content != null)
                foreach (var t in content.GetComponentsInChildren<TMP_Text>(true))
                    if (t.font != null && t.font.name.StartsWith("Asap") && _done.Add(t.GetInstanceID())) S1Kit.Text(t.transform, d.Find("ModalMessageText"));
            var bc = root.Find("ButtonContainer");
            if (bc != null)
                for (int i = 0; i < bc.childCount; i++)
                {
                    var b = bc.GetChild(i);
                    bool cancel = b.name.Contains("Left") || b.name.Contains("Cancel") || b.name.Contains("Close");
                    Button(b, d.Find(cancel ? "ModalMessageButtonsContainer/ModalMessageCancelButton" : "ModalMessageButtonsContainer/ModalMessageSubmitButton"), true, null);
                }
            Broad(style, root);
        }

        static readonly HashSet<string> BodyFills = new HashSet<string> { "Panel_Fill", "Popup_BG", "Blue_BG", "BrightBlueBackground", "BlueBackground" };
        static readonly HashSet<string> BodyHides = new HashSet<string>
        {
            "Panel_Shadow", "Panel_Outline", "Outline", "DropShadow", "Title_Fill", "Title_BG", "Container_Title",
            "HeaderPattern", "Bluebackgroundbody", "BlueBackgroundBody",
        };

        public static void Panels(UIStyle style, Transform root)
        {
            var d = Modal(style);
            var body = d.GetComponent<Image>();
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                if (!BodyFills.Contains(img.name) || img.sprite == null || !img.sprite.name.StartsWith("UI_RoundedPanel1")) continue;
                var parent = img.transform.parent;
                if (parent == null || parent.name.StartsWith("Panel_Unselected") || parent.name.StartsWith("Panel_Selected") || S1Kit.Ours(img.transform)) continue;
                var size = img.rectTransform.rect.size;
                if (size.x < 300f || size.y < 150f || !_done.Add(img.GetInstanceID())) continue;
                StyleUndo.Graphic(img);
                img.sprite = body.sprite;
                img.type = body.type;
                img.color = Color.white;
                img.pixelsPerUnitMultiplier = body.pixelsPerUnitMultiplier;
                for (int i = 0; i < parent.childCount; i++)
                {
                    var sib = parent.GetChild(i);
                    if (BodyHides.Contains(sib.name)) S1Kit.Off(sib);
                    var head = sib.Find("HeaderFill");
                    if (head != null) S1Kit.Off(head);
                }
                var title = parent.Find("Title");
                if (title == null) continue;
                S1Kit.Off(title.Find("Title_Fill"));
                S1Kit.Image(title.Find("Pattern_Mask"), d.Find("ModalMessagePatternMask"));
                S1Kit.Image(title.Find("Pattern_Mask/Pattern"), d.Find("ModalMessagePatternMask/ModalMessagePatternIcon"));
                S1Kit.Image(title.Find("Separator"), d.Find("ModalMessageLine"));
                var mask = title.Find("Pattern_Mask");
                var dcg = d.Find("ModalMessagePatternMask")?.GetComponent<CanvasGroup>();
                if (mask == null || dcg == null) continue;
                StyleUndo.Group(mask).alpha = dcg.alpha;
            }
        }

        public static void Broad(UIStyle style, Transform root)
        {
            var submit = Modal(style).Find("ModalMessageButtonsContainer/ModalMessageSubmitButton");
            var idle = style.Assets.Sprite("UI_standard_button3");
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.Find("Panel_Unselected") == null || t.Find("Panel_Selected") == null || !_done.Add(t.GetInstanceID()) || S1Kit.Ours(t)) continue;
                Button(t, submit, false, idle);
            }
            if (_title == null) _title = style.Assets.Font(TITLE_FONT);
            foreach (var x in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (x.font == null || !x.font.name.StartsWith("TitanOne-Expanded") || !_done.Add(x.GetInstanceID()) || S1Kit.Ours(x.transform)) continue;
                var src = x.fontSharedMaterial == null ? "" : x.fontSharedMaterial.name;
                int dash = src.IndexOf(" - ", StringComparison.Ordinal);
                var suffix = dash < 0 ? "" : src.Substring(dash).Replace("_2_0", "").Replace(" (Instance)", "");
                if (!_titleMats.TryGetValue(suffix, out var m))
                    _titleMats[suffix] = m = style.Assets.MaterialNamed(TITLE_MAT + suffix) ?? style.Assets.MaterialNamed(TITLE_MAT + " - DropShadowAlpha");
                StyleUndo.Graphic(x);
                x.font = _title;
                if (m != null) x.fontSharedMaterial = m;
            }
        }

        public static void ByName(Transform root, Transform donorRoot, HashSet<string> names)
        {
            var map = new Dictionary<string, Transform>();
            foreach (var d in donorRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!names.Contains(d.name)) continue;
                map[d.name] = d;
                if (d.parent != null) map[d.parent.name + "/" + d.name] = d;
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!_done.Add(t.GetInstanceID()) || S1Kit.Ours(t)) continue;
                Transform d = null;
                if (t.parent != null) map.TryGetValue(t.parent.name + "/" + t.name, out d);
                if (d == null && !map.TryGetValue(t.name, out d)) continue;
                if (t.GetComponent<TMP_Text>() != null) S1Kit.Text(t, d);
                else if (t.GetComponent<Image>() != null && d.GetComponent<Image>() != null && d.GetComponent<Image>().sprite != null) S1Kit.Image(t, d);
            }
        }
    }
}
