using System;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

namespace BettrFG.uGUI
{
    public static partial class UGUIShip
    {
        // every dropdown panel registers here so opening one closes the rest. dead (destroyed)
        // entries get pruned lazily on open.
        static readonly System.Collections.Generic.List<GameObject> _openDropdownPanels = new System.Collections.Generic.List<GameObject>();

        // �� Dropdown ����������������������������������������������������������
        // pass options
        // and an onChange; templateHeight controls how tall the open list gets. listWidth, when
        // > 0, fixes the open list to that pixel width (left-aligned) instead of matching the button.
        public static Dropdown CreateDropdown(Transform parent, Rect rect,
            System.Collections.Generic.List<string> options, int selected, Action<int> onChange,
            int fontSize = 10, float templateHeight = 120f, float listWidth = 0f)
        {
            var go = new GameObject("Dropdown");
            go.transform.SetParent(parent, false);
            SetPixelRect(go.AddComponent<RectTransform>(), rect);
            var bg = go.AddComponent<Image>();
            bg.color = Color.black; // fully black header, like the pb-tab dropdowns
            var btnSpr = GetButtonSprite();
            if (btnSpr != null) { bg.sprite = btnSpr; bg.type = Image.Type.Simple; }
            var dd = go.AddComponent<Dropdown>();
            dd.transition = Selectable.Transition.None;
            dd.alphaFadeSpeed = 0f; // no fade in/out on the popup

            // shine + hover/click audio so it feels like every other button
            if (btnSpr != null)
            {
                var shineGo = BuildShine(go);
                if (shineGo != null) WireShineHover(go, shineGo);
            }
            WireButtonAudio(go);
            // click sound when the dropdown is opened (pointer down), not just on value change
            {
                var trig = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();
                var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
                down.callback.AddListener(new Action<BaseEventData>(_ => PlayClick()));
                trig.triggers.Add(down);
            }

            var lblGo = new GameObject("Label");
            lblGo.transform.SetParent(go.transform, false);
            var lblRt = lblGo.AddComponent<RectTransform>();
            lblRt.anchorMin = Vector2.zero; lblRt.anchorMax = Vector2.one;
            lblRt.offsetMin = new Vector2(6f, 2f); lblRt.offsetMax = new Vector2(-24f, -2f);
            var lbl = lblGo.AddComponent<Text>();
            Stylize(lbl);
            lbl.fontSize = fontSize; lbl.color = Color.white; lbl.alignment = TextAnchor.MiddleLeft;
            dd.captionText = lbl;

            var templateGo = new GameObject("Template");
            templateGo.transform.SetParent(go.transform, false);
            var tRt = templateGo.AddComponent<RectTransform>();
            if (listWidth > 0f)
            {
                // fixed-width list, left-aligned to the button instead of matching its width
                tRt.anchorMin = new Vector2(0f, 0f); tRt.anchorMax = new Vector2(0f, 0f);
                tRt.pivot = new Vector2(0f, 1f); tRt.anchoredPosition = Vector2.zero;
                tRt.sizeDelta = new Vector2(listWidth, templateHeight);
            }
            else
            {
                tRt.anchorMin = new Vector2(0f, 0f); tRt.anchorMax = new Vector2(1f, 0f);
                tRt.pivot = new Vector2(0.5f, 1f); tRt.anchoredPosition = Vector2.zero;
                tRt.sizeDelta = new Vector2(0f, templateHeight);
            }
            templateGo.AddComponent<Image>().color = Color.black; // fully black list, like the pb-tab dropdowns
            var sr2 = templateGo.AddComponent<ScrollRect>();
            sr2.horizontal = false; sr2.vertical = true; sr2.movementType = ScrollRect.MovementType.Clamped;
            sr2.scrollSensitivity = 20f;

            var vpGo = new GameObject("Viewport");
            vpGo.transform.SetParent(templateGo.transform, false);
            var vpRt = vpGo.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = vpRt.offsetMax = Vector2.zero;
            vpGo.AddComponent<Image>();
            vpGo.AddComponent<Mask>().showMaskGraphic = false;
            sr2.viewport = vpRt;

            var cGo = new GameObject("Content");
            cGo.transform.SetParent(vpGo.transform, false);
            var cRt = cGo.AddComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0f, 1f); cRt.anchorMax = new Vector2(1f, 1f);
            cRt.pivot = new Vector2(0.5f, 1f); cRt.anchoredPosition = Vector2.zero;
            cRt.sizeDelta = new Vector2(0f, 28f);
            sr2.content = cRt;

            var itemGo = new GameObject("Item");
            itemGo.transform.SetParent(cGo.transform, false);
            var itemRt = itemGo.AddComponent<RectTransform>();
            itemRt.anchorMin = new Vector2(0f, 0.5f); itemRt.anchorMax = new Vector2(1f, 0.5f);
            itemRt.sizeDelta = new Vector2(0f, 20f);
            // faint 3% white base + brighten on hover, matching the pb-tab dropdown rows. Unity clones
            // this one item per option, so the hover uses the Toggle's color states (applied per-clone).
            var itemImg = itemGo.AddComponent<Image>();
            itemImg.color = Color.white; // tinted by the toggle's normalColor below
            var tog = itemGo.AddComponent<Toggle>();
            tog.transition = Selectable.Transition.ColorTint;
            tog.targetGraphic = itemImg;
            var itemCols = tog.colors;
            itemCols.normalColor = new Color(1f, 1f, 1f, 0.03f);
            itemCols.highlightedColor = new Color(1f, 1f, 1f, 0.18f);
            itemCols.pressedColor = new Color(1f, 1f, 1f, 0.18f);
            itemCols.selectedColor = new Color(1f, 1f, 1f, 0.03f);
            itemCols.fadeDuration = 0f;
            tog.colors = itemCols;

            var iLblGo = new GameObject("Item Label");
            iLblGo.transform.SetParent(itemGo.transform, false);
            var iLblRt = iLblGo.AddComponent<RectTransform>();
            iLblRt.anchorMin = Vector2.zero; iLblRt.anchorMax = Vector2.one;
            iLblRt.offsetMin = new Vector2(6f, 0f); iLblRt.offsetMax = new Vector2(-28f, 0f);
            var iLbl = iLblGo.AddComponent<Text>();
            Stylize(iLbl);
            iLbl.fontSize = fontSize; iLbl.color = Color.white; iLbl.alignment = TextAnchor.MiddleLeft;

            var chkGo = new GameObject("Checkmark");
            chkGo.transform.SetParent(itemGo.transform, false);
            var chkRt = chkGo.AddComponent<RectTransform>();
            chkRt.anchorMin = new Vector2(1f, 0f); chkRt.anchorMax = new Vector2(1f, 1f);
            chkRt.pivot = new Vector2(1f, 0.5f);
            chkRt.offsetMin = new Vector2(-24f, 0f); chkRt.offsetMax = new Vector2(-6f, 0f);
            var chk = chkGo.AddComponent<Text>();
            chk.text = "\u2714";
            Stylize(chk);
            chk.fontSize = fontSize + 2;
            chk.color = new Color(1f, 0.85f, 0.2f);
            chk.alignment = TextAnchor.MiddleCenter;
            chk.raycastTarget = false;

            tog.isOn = true;
            tog.graphic = chk;
            dd.itemText = iLbl;
            dd.template = tRt;
            templateGo.SetActive(false);

            if (options != null && options.Count > 0)
            {
                dd.ClearOptions();
                foreach (var o in options) dd.options.Add(new Dropdown.OptionData(LocText(o)));
                dd.value = Mathf.Clamp(selected, 0, options.Count - 1);
                dd.RefreshShownValue();
            }

            if (onChange != null)
                dd.onValueChanged.AddListener(new Action<int>(v => onChange(v)));

            return dd;
        }

        // �� Multi-select dropdown ���������������������������������������������
        // a button with a fixed label that opens a small panel of toggle rows, each with a
        // checkmark on the right showing on/off. onToggle(index, newState) fires per click.
        // returns the button so the caller can recolour it (e.g. yellow when any option is on).
        public static Button CreateMultiSelectDropdown(Transform parent, Rect rect, string label,
            System.Collections.Generic.List<string> options, System.Collections.Generic.List<bool> initial,
            Action<int, bool> onToggle, int fontSize = 10, float listWidth = 0f, float rowH = 20f,
            bool singleSelect = false, bool closeOnPick = false, bool showAbove = false,
            System.Collections.Generic.List<Sprite> rowSprites = null, bool rightAlignText = false)
        {
            int n = options?.Count ?? 0;
            float w = listWidth > 0f ? listWidth : rect.width;
            float panelH = rowH * n + 4f;
            var checks = new System.Collections.Generic.List<GameObject>(); // for single-select radio behaviour

            // header button � fully black like the panel (shine + audio come free from CreateButton)
            var btn = CreateButton(parent, rect, label, Color.black, Color.white, fontSize);

            // optional image overlay (stretched across the button, behind the text) + right-aligned text
            if (rowSprites != null)
            {
                int selIdx = 0;
                if (initial != null)
                    for (int k = 0; k < initial.Count; k++) if (initial[k]) { selIdx = k; break; }
                var headImgGo = new GameObject("HeaderImg");
                headImgGo.transform.SetParent(btn.transform, false);
                var hiRt = headImgGo.AddComponent<RectTransform>();
                hiRt.anchorMin = Vector2.zero; hiRt.anchorMax = Vector2.one;
                hiRt.offsetMin = hiRt.offsetMax = Vector2.zero;
                var hiImg = headImgGo.AddComponent<Image>();
                hiImg.raycastTarget = false;
                hiImg.preserveAspect = false;
                if (selIdx < rowSprites.Count) hiImg.sprite = rowSprites[selIdx];
                headImgGo.transform.SetAsFirstSibling(); // behind the label
            }
            if (rightAlignText)
            {
                var headLbl = btn.GetComponentInChildren<Text>();
                if (headLbl != null)
                {
                    headLbl.alignment = TextAnchor.MiddleRight;
                    // nudge text off the right edge without resizing the (top-left anchored) rect
                    var lrt = headLbl.GetComponent<RectTransform>();
                    if (lrt != null) lrt.sizeDelta = new Vector2(lrt.sizeDelta.x - 8f, lrt.sizeDelta.y);
                    headLbl.transform.SetAsLastSibling(); // on top of the image
                }
            }

            // panel sits under the button by default, or above it when showAbove is set (so it never
            // runs off the bottom). parent it to the SAME parent (not the button) and make it the last
            // sibling so it draws ON TOP. fully black, no button sprite (rounded edges wash it out).
            float panelY = showAbove ? rect.y - panelH - 2f : rect.y + rect.height + 2f;
            var panelGo = new GameObject("MSPanel");
            panelGo.transform.SetParent(parent, false);
            var pRt = panelGo.AddComponent<RectTransform>();
            SetPixelRect(pRt, new Rect(rect.x, panelY, w, panelH));
            var pImg = panelGo.AddComponent<Image>();
            pImg.color = Color.black;
            pImg.raycastTarget = true; // eats clicks so nothing behind the panel steals them
            panelGo.SetActive(false);

            // register so opening any dropdown closes the others (only one open at a time)
            _openDropdownPanels.Add(panelGo);

            // toggle the panel open/closed on header click. bring it to front EVERY time it opens
            // (anything created after this, like a scrollview, would otherwise draw on top of it).
            btn.onClick.AddListener(new Action(() =>
            {
                bool show = !panelGo.activeSelf;
                // close every other dropdown panel before opening this one
                for (int k = _openDropdownPanels.Count - 1; k >= 0; k--)
                {
                    var p = _openDropdownPanels[k];
                    if (p == null) { _openDropdownPanels.RemoveAt(k); continue; }
                    if (p != panelGo) p.SetActive(false);
                }
                panelGo.SetActive(show);
                if (show) panelGo.transform.SetAsLastSibling();
            }));

            for (int i = 0; i < n; i++)
            {
                int idx = i;
                bool on = initial != null && i < initial.Count && initial[i];

                // each row is a plain top-left pixel rect inside the panel � no stretched anchors.
                // zebra: every other row 3% white, the rest fully clear (panel behind is black)
                var rowGo = new GameObject("MSRow_" + i);
                rowGo.transform.SetParent(panelGo.transform, false);
                SetPixelRect(rowGo.AddComponent<RectTransform>(), new Rect(2f, 2f + rowH * i, w - 4f, rowH));
                var rImg = rowGo.AddComponent<Image>();
                rImg.color = new Color(0f, 0f, 0f, 0f);
                PaintRowStripe(rowGo, i % 2 == 0, new Color(1f, 1f, 1f, 0.03f));

                // optional image overlay stretched across the row, behind the text
                if (rowSprites != null && i < rowSprites.Count && rowSprites[i] != null)
                {
                    var riGo = new GameObject("RowImg");
                    riGo.transform.SetParent(rowGo.transform, false);
                    var riRt = riGo.AddComponent<RectTransform>();
                    riRt.anchorMin = Vector2.zero; riRt.anchorMax = Vector2.one;
                    riRt.offsetMin = riRt.offsetMax = Vector2.zero;
                    var riImg = riGo.AddComponent<Image>();
                    riImg.sprite = rowSprites[i];
                    riImg.raycastTarget = false;
                }

                CreateLabel(rowGo.transform, new Rect(6f, 0f, w - 28f, rowH), options[i],
                    fontSize, Color.white, rightAlignText ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);

                // checkmark on the right, visible when on
                var chk = CreateLabel(rowGo.transform, new Rect(w - 24f, 0f, 18f, rowH), "\u2714",
                    fontSize + 2, new Color(1f, 0.85f, 0.2f), TextAnchor.MiddleCenter);
                chk.gameObject.SetActive(on);
                checks.Add(chk.gameObject);

                var rowBtn = rowGo.AddComponent<Button>();
                var nav2 = rowBtn.navigation; nav2.mode = Navigation.Mode.None; rowBtn.navigation = nav2;
                rowBtn.transition = Selectable.Transition.None;
                rowBtn.targetGraphic = rImg;
                rowBtn.onClick.AddListener(new Action(() =>
                {
                    bool ns;
                    if (singleSelect)
                    {
                        // radio: this row on, all others off
                        for (int k = 0; k < checks.Count; k++)
                            if (checks[k] != null) checks[k].SetActive(k == idx);
                        ns = true;
                    }
                    else
                    {
                        ns = !chk.gameObject.activeSelf;
                        chk.gameObject.SetActive(ns);
                    }
                    PlayClick();
                    onToggle?.Invoke(idx, ns);
                    if (closeOnPick) panelGo.SetActive(false);
                }));
                WireButtonAudio(rowGo);
            }

            return btn;
        }

        // a menu-bar dropdown: same delux panel + zebra rows as the multi-select one, but each row
        // is a one-shot action instead of a toggle, so no checkmarks and it always closes on pick.
        // the panel sizes itself to the widest row label, clamped to ACTION_MENU_MAX_W.
        public const float ACTION_MENU_MAX_W = 400f;

        static Text _measure;

        // one throwaway Text, styled exactly like the real ones, reused for sizing
        public static float MeasureLabel(string id, int fontSize)
        {
            if (_measure == null)
            {
                var go = new GameObject("UGUIShip_Measure");
                go.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<RectTransform>();
                _measure = go.AddComponent<Text>();
                Stylize(_measure);
                _measure.horizontalOverflow = HorizontalWrapMode.Overflow;
                _measure.verticalOverflow = VerticalWrapMode.Overflow;
            }
            _measure.fontSize = fontSize;
            _measure.text = LocText(id);
            return PreferredWidth(_measure);
        }

        public static Button CreateActionDropdown(Transform parent, Rect rect, string label,
            System.Collections.Generic.List<string> options, Action<int> onPick,
            int fontSize = 10, float rowH = 20f, bool showAbove = false)
        {
            int n = options?.Count ?? 0;

            // guessing from character count gets it wrong the moment the font changes, so the labels
            // are built first and asked how wide they actually are
            float w = rect.width;
            for (int i = 0; i < n; i++)
                w = Mathf.Max(w, MeasureLabel(options[i], fontSize) + 26f);
            w = Mathf.Min(w, ACTION_MENU_MAX_W);

            float panelH = rowH * n + 4f;
            var btn = CreateButton(parent, rect, label, Color.black, Color.white, fontSize);

            float panelY = showAbove ? rect.y - panelH - 2f : rect.y + rect.height + 2f;
            var panelGo = new GameObject("ActionPanel");
            panelGo.transform.SetParent(parent, false);
            var pRt = panelGo.AddComponent<RectTransform>();
            SetPixelRect(pRt, new Rect(rect.x, panelY, w, panelH));
            var pImg = panelGo.AddComponent<Image>();
            ApplyDeluxPanelDark(pImg, 2f);
            pImg.raycastTarget = true;
            panelGo.AddComponent<RectMask2D>();
            panelGo.SetActive(false);

            _openDropdownPanels.Add(panelGo);

            btn.onClick.AddListener(new Action(() =>
            {
                bool show = !panelGo.activeSelf;
                for (int k = _openDropdownPanels.Count - 1; k >= 0; k--)
                {
                    var p = _openDropdownPanels[k];
                    if (p == null) { _openDropdownPanels.RemoveAt(k); continue; }
                    if (p != panelGo) p.SetActive(false);
                }
                panelGo.SetActive(show);
                if (show) panelGo.transform.SetAsLastSibling();
            }));

            for (int i = 0; i < n; i++)
            {
                int idx = i;
                var rowGo = new GameObject("ActionRow_" + i);
                rowGo.transform.SetParent(panelGo.transform, false);
                SetPixelRect(rowGo.AddComponent<RectTransform>(), new Rect(2f, 2f + rowH * i, w - 4f, rowH));
                var rImg = rowGo.AddComponent<Image>();
                rImg.color = ROW_CLEAR;

                var rowBtn = rowGo.AddComponent<Button>();
                var nav = rowBtn.navigation; nav.mode = Navigation.Mode.None; rowBtn.navigation = nav;
                rowBtn.targetGraphic = rImg;
                PaintListRow(rowBtn, i, false);

                var itemLbl = CreateLabel(rowGo.transform, new Rect(8f, 0f, w - 16f, rowH), options[i],
                    fontSize, Color.white, TextAnchor.MiddleLeft);
                itemLbl.raycastTarget = false;
                itemLbl.horizontalOverflow = HorizontalWrapMode.Overflow;

                rowBtn.onClick.AddListener(new Action(() =>
                {
                    PlayClick();
                    panelGo.SetActive(false);
                    onPick?.Invoke(idx);
                }));
                WireButtonAudio(rowGo);
            }

            return btn;
        }
    }
}
