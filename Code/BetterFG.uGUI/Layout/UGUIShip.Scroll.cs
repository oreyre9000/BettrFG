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
        // Forward mouse-wheel scroll from this element up to the enclosing ScrollRect. UGUI routes a
        // scroll to the first ancestor implementing IScrollHandler; the EventTrigger we add for hover
        // /audio implements it, so it swallows the wheel and the list freezes while the pointer is over
        // a button. Handing the scroll back to the ScrollRect makes lists scroll no matter what's hovered.
        public static void ForwardScrollToParent(GameObject go)
        {
            if (go == null) return;
            var trigger = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();
            var scroll = new EventTrigger.Entry { eventID = EventTriggerType.Scroll };
            scroll.callback.AddListener(new Action<BaseEventData>(data =>
            {
                var sr = go.GetComponentInParent<ScrollRect>();
                var ped = data?.TryCast<PointerEventData>();
                if (sr != null && ped != null) sr.OnScroll(ped);
            }));
            trigger.triggers.Add(scroll);
        }

        // width the scroll view's viewport is inset by on each side (bar sits in the right one)
        public const float SCROLLBAR_INSET = 13f;

        // �� Scroll view �������������������������������������������������������
        public static (ScrollRect scrollRect, RectTransform content) CreateScrollView(
            Transform parent, Rect rect)
        {
            const float barW = 12f;
            const float barGap = 1f;

            var rootGo = new GameObject("ScrollView");
            rootGo.transform.SetParent(parent, false);
            var rootRt = rootGo.AddComponent<RectTransform>();
            SetPixelRect(rootRt, rect);

            var sr = rootGo.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.scrollSensitivity = 25f;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.inertia = false;

            // needs an Image so the ScrollRect has a raycast surface for scroll events
            var rootImg = rootGo.AddComponent<Image>();
            rootImg.color = Color.clear;
            rootImg.raycastTarget = true;

            var vpGo = new GameObject("Viewport");
            vpGo.transform.SetParent(rootGo.transform, false);
            var vpRt = vpGo.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(barW + barGap, 0f);
            vpRt.offsetMax = new Vector2(-(barW + barGap), 0f);
            vpRt.pivot = new Vector2(0f, 1f);
            // Image needed on viewport too for RectMask2D to clip correctly
            var vpImg = vpGo.AddComponent<Image>();
            vpImg.color = Color.clear;
            vpImg.raycastTarget = false;
            vpGo.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(vpGo.transform, false);
            var contentRt = contentGo.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0f, 1f);
            contentRt.offsetMin = contentRt.offsetMax = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, rect.height);

            sr.viewport = vpRt;
            sr.content = contentRt;

            var barGo = new GameObject("Scrollbar");
            barGo.transform.SetParent(rootGo.transform, false);
            var barRt = barGo.AddComponent<RectTransform>();
            barRt.anchorMin = new Vector2(1f, 0f);
            barRt.anchorMax = new Vector2(1f, 1f);
            barRt.pivot = new Vector2(1f, 0.5f);
            barRt.offsetMin = new Vector2(-(barW + barGap), 0f);
            barRt.offsetMax = new Vector2(-barGap, 0f);
            var barBgGo = new GameObject("Background");
            barBgGo.transform.SetParent(barGo.transform, false);
            var barBgRt = barBgGo.AddComponent<RectTransform>();
            barBgRt.anchorMin = Vector2.zero;
            barBgRt.anchorMax = Vector2.one;
            barBgRt.offsetMin = barBgRt.offsetMax = Vector2.zero;
            barBgRt.localScale = new Vector3(1f, -1f, 1f);
            var barBg = barBgGo.AddComponent<Image>();
            barBg.sprite = GetButtonShineSprite();
            barBg.type = Image.Type.Sliced;
            barBg.pixelsPerUnitMultiplier = 8f;
            barBg.color = new Color(1f, 1f, 1f, 0.35f);
            RegisterShine(barBg);

            var slideGo = new GameObject("Sliding Area");
            slideGo.transform.SetParent(barGo.transform, false);
            var slideRt = slideGo.AddComponent<RectTransform>();
            slideRt.anchorMin = Vector2.zero;
            slideRt.anchorMax = Vector2.one;
            slideRt.offsetMin = slideRt.offsetMax = Vector2.zero;

            var handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(slideGo.transform, false);
            var handleRt = handleGo.AddComponent<RectTransform>();
            handleRt.anchorMin = Vector2.zero;
            handleRt.anchorMax = Vector2.one;
            handleRt.offsetMin = handleRt.offsetMax = Vector2.zero;
            var handleImg = handleGo.AddComponent<Image>();
            var handleFill = ApplyDeluxSkin(handleGo, handleImg, Color.white, withShine: true);

            var bar = barGo.AddComponent<Scrollbar>();
            bar.handleRect = handleRt;
            bar.targetGraphic = handleFill ?? handleImg;
            bar.direction = Scrollbar.Direction.BottomToTop;
            var barColors = bar.colors;
            barColors.normalColor = new Color(0.22f, 0.22f, 0.22f, 1f);
            barColors.highlightedColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            barColors.pressedColor = new Color(0.16f, 0.16f, 0.16f, 1f);
            barColors.fadeDuration = 0f;
            bar.colors = barColors;
            var nav = bar.navigation;
            nav.mode = Navigation.Mode.None;
            bar.navigation = nav;

            sr.verticalScrollbar = bar;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            return (sr, contentRt);
        }

        public static Button CreateRowEndButton(Transform parent, float anchoredX, float bw, float rowH,
            string label, Color bg, Action onClick)
        {
            float bh = Mathf.Min(rowH - 6f, 24f * UIScale.S);
            var btn = CreateButton(parent, new Rect(0f, 0f, bw, bh), label, bg, Color.white, UIScale.FS_SM - 1, onClick);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(anchoredX, 0f);
            rt.sizeDelta = new Vector2(bw, bh);
            return btn;
        }

        public static void PaintListRow(Button row, int index, bool selected)
        {
            if (row == null) return;
            bool zebra = index % 2 == 0;

            if (!PaintRowStripe(row.gameObject, selected || zebra, selected ? ROW_SEL : ROW_ALT))
            {
                // no delux art on disk -> old flat ColorTint zebra
                var cols = row.colors;
                cols.normalColor = selected ? ROW_SEL : (zebra ? ROW_ALT : ROW_CLEAR);
                cols.highlightedColor = ROW_HOVER;
                cols.pressedColor = ROW_PRESS;
                cols.selectedColor = cols.normalColor;
                cols.fadeDuration = 0f;
                row.colors = cols;
                return;
            }

            // fill + hover shine carry the state now; kill the brighten-on-hover tint
            row.transition = Selectable.Transition.None;
            var baseImg = row.targetGraphic as Image ?? row.GetComponent<Image>();
            if (baseImg != null) baseImg.color = ROW_CLEAR;
        }

        // modernised zebra/selected row backing shared by every list + dropdown: an untinted delux
        // colorfill 9-slice behind the content for "lit" rows, plus a delux shine 9-slice that only
        // shows while hovered (instead of a brighter tint). idempotent — safe every repaint. returns
        // false (and does nothing) when the delux art is missing so callers keep their flat fallback.
        public static bool PaintRowStripe(GameObject rowGo, bool lit, Color fillColor)
        {
            if (rowGo == null) return false;
            if (LoadDeluxSlice("BetterFG.assets.ui.general.uisprite_delux_colorfill.png", ref _deluxFillSprite) == null)
                return false;

            var fill = rowGo.transform.Find("RowFill")?.GetComponent<Image>();
            if (fill == null)
                fill = AddDeluxSlice(rowGo.transform, "BetterFG.assets.ui.general.uisprite_delux_colorfill.png",
                    ref _deluxFillSprite, Color.white, "RowFill", 3f);
            fill.gameObject.SetActive(lit);
            if (lit) fill.color = fillColor * Tint();
            fill.transform.SetAsFirstSibling();

            var shine = rowGo.transform.Find("RowShine")?.GetComponent<Image>();
            if (shine == null)
            {
                shine = AddDeluxSlice(rowGo.transform, "BetterFG.assets.ui.general.uisprite_delux_shineoverlay.png",
                    ref _deluxShineSprite, new Color(1f, 1f, 1f, 0.4f), "RowShine", 3f);
                RegisterShine(shine);
                shine.gameObject.SetActive(false);

                var capShine = shine;
                var trg = rowGo.GetComponent<EventTrigger>() ?? rowGo.AddComponent<EventTrigger>();
                var en = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                en.callback.AddListener(new Action<BaseEventData>(_ => { if (capShine != null) capShine.gameObject.SetActive(true); }));
                trg.triggers.Add(en);
                var ex = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                ex.callback.AddListener(new Action<BaseEventData>(_ => { if (capShine != null) capShine.gameObject.SetActive(false); }));
                trg.triggers.Add(ex);

                // the EventTrigger otherwise swallows wheel + drag, freezing the enclosing list while
                // the pointer is over a row — hand both back to the ScrollRect.
                ForwardScrollToParent(rowGo);
                AddDragForward(trg, rowGo, EventTriggerType.BeginDrag);
                AddDragForward(trg, rowGo, EventTriggerType.Drag);
                AddDragForward(trg, rowGo, EventTriggerType.EndDrag);
            }
            shine.transform.SetSiblingIndex(1);
            return true;
        }

        // always-on delux colorfill row backing, no hover shine/EventTrigger — for static settings
        // rows (zebra stripes in the sidewheel windows) that never needed the hover machinery.
        // falls back to a plain Image fill when the delux art is missing.
        public static void PaintStaticRowFill(GameObject go, Color fillColor)
        {
            if (go == null) return;
            if (go.GetComponent<RowLabelFit>() == null) go.AddComponent<RowLabelFit>();
            if (LoadDeluxSlice("BetterFG.assets.ui.general.uisprite_delux_colorfill.png", ref _deluxFillSprite) == null)
            {
                var img = go.GetComponent<Image>() ?? go.AddComponent<Image>();
                img.color = fillColor;
                return;
            }
            var fill = go.transform.Find("RowFill")?.GetComponent<Image>();
            if (fill == null)
                fill = AddDeluxSlice(go.transform, "BetterFG.assets.ui.general.uisprite_delux_colorfill.png",
                    ref _deluxFillSprite, fillColor, "RowFill", 3f);
            RegisterFill(fill, fillColor);
            fill.transform.SetAsFirstSibling();
        }

        // delux colorfill that only shows while hovered — no persistent stripe, no shine overlay.
        // for header / group rows that should light up on hover but stay flat otherwise.
        public static void PaintHoverFill(GameObject go, Color fillColor)
        {
            if (go == null) return;
            if (LoadDeluxSlice("BetterFG.assets.ui.general.uisprite_delux_colorfill.png", ref _deluxFillSprite) == null)
                return;
            if (go.transform.Find("RowFill") != null) return;

            var fill = AddDeluxSlice(go.transform, "BetterFG.assets.ui.general.uisprite_delux_colorfill.png",
                ref _deluxFillSprite, fillColor, "RowFill", 3f);
            RegisterFill(fill, fillColor);
            fill.gameObject.SetActive(false);
            fill.transform.SetAsFirstSibling();

            var capFill = fill;
            var trg = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();
            var en = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            en.callback.AddListener(new Action<BaseEventData>(_ => { if (capFill != null) capFill.gameObject.SetActive(true); }));
            trg.triggers.Add(en);
            var ex = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            ex.callback.AddListener(new Action<BaseEventData>(_ => { if (capFill != null) capFill.gameObject.SetActive(false); }));
            trg.triggers.Add(ex);
        }

        static void AddDragForward(EventTrigger trg, GameObject go, EventTriggerType type)
        {
            var e = new EventTrigger.Entry { eventID = type };
            e.callback.AddListener(new Action<BaseEventData>(data =>
            {
                var sr = go.GetComponentInParent<ScrollRect>();
                var ped = data?.TryCast<PointerEventData>();
                if (sr == null || ped == null) return;
                if (type == EventTriggerType.BeginDrag) sr.OnBeginDrag(ped);
                else if (type == EventTriggerType.Drag) sr.OnDrag(ped);
                else sr.OnEndDrag(ped);
            }));
            trg.triggers.Add(e);
        }
    }
}
