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
        // �� Canvas ������������������������������������������������������������
        public static Canvas CreateCanvas(string name = "UGUIShip_Canvas")
        {
            var go = new GameObject(name);
            UnityEngine.Object.DontDestroyOnLoad(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            RegisterCanvas(canvas);

            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        // �� Solid panel �������������������������������������������������������
        public static RectTransform CreatePanel(Transform parent, Rect rect,
            Color color, string name = "Panel")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            SetPixelRect(rt, rect);
            var img = go.AddComponent<Image>();
            img.color = color;
            ApplyLeather(img);
            return rt;
        }

        // �� Gradient panel (top � bottom) �������������������������������������
        public static RectTransform CreateGradientPanel(Transform parent, Rect rect,
            Color topColor, Color bottomColor, string name = "GradientPanel")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            SetPixelRect(rt, rect);

            var img = go.AddComponent<Image>();
            img.color = Color.white;
            ApplyLeather(img);

            var grad = go.AddComponent<GradientImage>();
            grad.Vertical = true;
            grad.TopColor = topColor;
            grad.BottomColor = bottomColor;

            return rt;
        }

        // �� Draggable window (solid bg) ���������������������������������������
        public static RectTransform CreateDraggableWindow(Transform parent, Rect rect,
            Color bgColor, string name = "Window")
        {
            var rt = CreatePanel(parent, rect, bgColor, name);
            rt.gameObject.AddComponent<DragHandler>();
            return rt;
        }

        // small "?" help marker. hovering it pops `tip` on top of the "?" with no delay. the tooltip
        // itself lives on the root canvas (BetterFGUIMan draws it there) so it's never clipped by a
        // scroll viewport — this just wires the hover trigger. drop one after any label that needs a
        // note/credit. returns the GameObject so callers can position/size it however they like.
        public static GameObject CreateHelp(Transform parent, Rect rect, string tip, int fontSize = 11)
        {
            var go = new GameObject("Help");
            go.transform.SetParent(parent, false);
            SetPixelRect(go.AddComponent<RectTransform>(), rect);

            // transparent hit graphic on the root so the whole rect catches the hover
            var hit = go.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;

            // faint filled circle behind the "?" — square + centered so it stays round whatever the
            // rect aspect is. unicode circled glyphs don't render in Arial, so draw one. Knob.psd is
            // Unity's builtin round sprite.
            float d = Mathf.Min(rect.width, rect.height);
            var circGo = new GameObject("Circle");
            circGo.transform.SetParent(go.transform, false);
            var circRt = circGo.AddComponent<RectTransform>();
            circRt.anchorMin = circRt.anchorMax = new Vector2(0.5f, 0.5f);
            circRt.pivot = new Vector2(0.5f, 0.5f);
            circRt.anchoredPosition = Vector2.zero;
            circRt.sizeDelta = new Vector2(d, d);
            var knob = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
            var circle = circGo.AddComponent<Image>();
            circle.sprite = knob;
            circle.type = Image.Type.Simple;
            circle.color = new Color(0.04f, 0.04f, 0.04f, 1f);
            circle.raycastTarget = false;

            var innerGo = new GameObject("Inner");
            innerGo.transform.SetParent(circGo.transform, false);
            var innerRt = innerGo.AddComponent<RectTransform>();
            innerRt.anchorMin = Vector2.zero; innerRt.anchorMax = Vector2.one;
            innerRt.offsetMin = new Vector2(1f, 1f); innerRt.offsetMax = new Vector2(-1f, -1f);
            var inner = innerGo.AddComponent<Image>();
            inner.sprite = knob;
            inner.raycastTarget = false;
            innerGo.AddComponent<Mask>().showMaskGraphic = false;
            AddDeluxSlice(innerGo.transform, "BetterFG.assets.ui.general.uisprite_delux_colorfill.png",
                ref _deluxFillSprite, new Color(0.32f, 0.32f, 0.36f, 1f), "ColorFill", 2f);
            RegisterShine(AddDeluxSlice(innerGo.transform, "BetterFG.assets.ui.general.uisprite_delux_shineoverlay.png",
                ref _deluxShineSprite, new Color(1f, 1f, 1f, 0.4f), "Shine", 2f));

            var qGo = new GameObject("Q");
            qGo.transform.SetParent(go.transform, false);
            var qRt = qGo.AddComponent<RectTransform>();
            qRt.anchorMin = Vector2.zero; qRt.anchorMax = Vector2.one;
            qRt.offsetMin = qRt.offsetMax = Vector2.zero;
            var t = qGo.AddComponent<Text>();
            t.text = "?";
            Stylize(t);
            t.fontSize = fontSize;
            t.fontStyle = FontStyle.Bold;
            t.color = new Color(1f, 1f, 1f, 0.85f);
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;

            AddTooltip(go, tip);
            return go;
        }

// �� Notice (info text + bean + take-me-there button) �����������������������
        // recurring "X has moved to Y � take me there" block. bean on the left at the row's full
        // height (aspect-fit), info label + button stacked to its right. returns the y-height
        // consumed so callers can advance their cursor by `cy += UGUIShip.CreateNotice(...) + PAD`.
        //
        // info: short label (supports \n). action: what the button does. beanRes defaults to
        // bean_victorious.png; pass null to omit the bean and stretch text+button full-width.
        public static float CreateNotice(Transform parent, float x, float y, float w,
            string info, Action action, string buttonLabel = "Take me there",
            string beanRes = "BetterFG.assets.ui.bean.bean_victorious.png")
        {
            const int lines = 2;
            float labelH = UIScale.LH * lines;
            float btnH = UIScale.BTN_H * 0.7f;
            float totalH = labelH + UIScale.SH + btnH;

            float textX = x;
            float textW = w;
            if (!string.IsNullOrEmpty(beanRes))
            {
                var beanTex = LoadTexture(beanRes);
                if (beanTex != null)
                {
                    float beanW = totalH * 0.6f;
                    CreateImage(parent, new Rect(x, y, beanW, totalH), beanTex, "NoticeBean");
                    textX = x + beanW + UIScale.PAD;
                    textW = w - beanW - UIScale.PAD;
                }
            }

            CreateLabel(parent, new Rect(textX, y, textW, labelH), info, UIScale.FS_SM,
                new Color(1f, 0.85f, 0.3f, 0.9f));
            var btnColor = new Color(0.45f, 0.35f, 0.25f, 1f);
            float btnW = Mathf.Min(textW, UIScale.BTN_W * 0.9f);
            CreateButton(parent, new Rect(textX, y + labelH + UIScale.SH, btnW, btnH),
                buttonLabel, btnColor, Color.white, UIScale.FS_SM, action);

            return totalH;
        }

        // non-interactive image. give it a rect (or just width/height) and a texture; aspect-fits
        // inside the rect so the source ratio is preserved. used for decorative beans / icons next to
        // text in tabs.
        public static RawImage CreateImage(Transform parent, Rect rect, Texture2D tex, string name = "Image")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            SetPixelRect(go.AddComponent<RectTransform>(), rect);

            var raw = go.AddComponent<RawImage>();
            raw.texture = tex;
            raw.raycastTarget = false;

            if (tex != null && tex.width > 0 && tex.height > 0)
            {
                float srcAspect = (float)tex.width / tex.height;
                float boxAspect = rect.width / rect.height;
                if (srcAspect > boxAspect)
                {
                    // letterbox vertically: shrink height to match texture aspect within the box width.
                    float h = rect.width / srcAspect;
                    float yOff = (rect.height - h) * 0.5f;
                    SetPixelRect(raw.rectTransform, new Rect(rect.x, rect.y + yOff, rect.width, h));
                }
                else if (srcAspect < boxAspect)
                {
                    // pillarbox horizontally.
                    float w = rect.height * srcAspect;
                    float xOff = (rect.width - w) * 0.5f;
                    SetPixelRect(raw.rectTransform, new Rect(rect.x + xOff, rect.y, w, rect.height));
                }
            }

            return raw;
        }

        // �� Divider (1px horizontal line) �������������������������������������
        public static void CreateDivider(Transform parent, float x, float y, float w)
        {
            CreatePanel(parent, new Rect(x, y, w, 1f), new Color(1f, 1f, 1f, 0.06f));
        }



        // �� Helpers �����������������������������������������������������������
        public static void SetPixelRect(RectTransform rt, Rect rect)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(rect.x, -rect.y);
            rt.sizeDelta = new Vector2(rect.width, rect.height);
        }
    }
}
