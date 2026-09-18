using System;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.Features.Onboarding
{
    // tutorial popup, styled off the sidewheel windows: delux dark panel, tilted bold title in a
    // band at the top, wrapped body, and small tilted buttons in the bottom-right corner. the title
    // tilt matches the windows/tab titles; the buttons mirror it.
    public static class OnboardingPopup
    {
        public const float Width = 360f;

        private const float PadX = 16f;
        private const float PadTop = 8f;
        private const float PadBottom = 10f;
        private const float TitleBandH = 28f;
        private const float GapTitleBody = 2f;
        private const float GapBodyButtons = 10f;

        private const float InputH = 24f;

        private const float BtnW = 86f;
        private const float BtnH = 26f;
        private const float BtnGap = 8f;
        private const float BtnOverhangX = 8f;

        private const int FsTitle = 18;
        private const int FsBody = 13;
        private const int FsBtn = 12;

        private static readonly Quaternion TitleRot = Quaternion.Euler(22f, 345f, 0f);
        private static readonly Vector3 TitleScale = new Vector3(1.276f, 1.457f, 1f);
        private static readonly Quaternion BtnRot = Quaternion.Euler(22f, 15f, 0f);
        private static readonly Vector3 BtnScale = new Vector3(1.12f, 1.22f, 1f);

        // the panel takes the title's tilt so the card reads as one skewed piece. tuned in-game on
        // the welcome popup: rotation 22/345/0, local position 5.5053/7.6217, scale 1.0353/0.7658.
        //
        // position and x-scale carry over as-is (every popup is the same width). the y-scale can't:
        // it only reads as 0.7658 at that popup's height, and on a taller one the panel would stop
        // well above the buttons instead of behind them. what actually holds is where the rendered
        // panel lands, poking a little past the top, stopping short of the bottom so the buttons
        // straddle its edge, so those two are the constants and the y-scale is derived per popup.
        // (their midpoint is 7.62, which is the tuned local position y, height-independent.)
        private const float PanelBleedX = 14f;
        private const float PanelBleedY = 10f;
        private const float PanelScaleX = 1.0353f;
        private const float PanelTopOverhang = 2.4f;
        private const float PanelBottomTrim = 12.85f;
        private static readonly Vector2 PanelNudge = new Vector2(5.5053f, 7.6217f);

        private static readonly Color TitleCol = new Color(1f, 1f, 1f, 0.85f);
        private static readonly Color BodyCol = new Color(1f, 1f, 1f, 0.78f);
        public static readonly Color BtnPrimary = new Color(0.25f, 0.5f, 0.25f, 1f);
        public static readonly Color BtnGhost = new Color(0.18f, 0.18f, 0.18f, 1f);

        // same open curve the sidewheel windows use, x only, so the popup unrolls sideways with a
        // slight overshoot and never squashes vertically.
        public const float AnimDur = 0.18f;
        public static readonly AnimationCurve OpenCurve = new AnimationCurve(new Keyframe[]
        {
            new Keyframe(0f,   0f,    0f,   2.5f),
            new Keyframe(0.6f, 1.05f, 0.3f, 0.3f),
            new Keyframe(1f,   1f,    0f,   0f),
        });

        public readonly struct ButtonSpec
        {
            public readonly string LabelId;
            public readonly Action OnClick;
            public readonly Color Bg;
            public ButtonSpec(string labelId, Action onClick, Color bg)
            { LabelId = labelId; OnClick = onClick; Bg = bg; }
        }

        // `pos`/`pivot` are canvas space (canvas is centered, +y up). buttons run right-to-left from
        // the bottom-right corner, so the last spec is the primary action.
        public static GameObject Show(Canvas canvas, string titleId, string bodyId,
            Vector2 pos, Vector2 pivot, ButtonSpec[] buttons,
            string inputInitial = null, Action<UnityEngine.UI.InputField> onInput = null,
            Action<LoadingBar> onBar = null, Action<Text> onBody = null)
        {
            var canvasRt = canvas.GetComponent<RectTransform>();
            int nBtn = buttons != null ? buttons.Length : 0;
            float innerW = Width - PadX * 2f;

            var root = new GameObject("Onboarding_Popup");
            root.transform.SetParent(canvas.transform, false);
            var rt = root.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = pivot;
            rt.sizeDelta = new Vector2(Width, 160f);

            // panel on its own child so it can carry the tilt while the text and buttons stay put
            var panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(root.transform, false);
            var panelRt = panelGo.AddComponent<RectTransform>();
            panelRt.anchorMin = Vector2.zero;
            panelRt.anchorMax = Vector2.one;
            // the tuned nudge goes into the offsets, NOT localPosition: this rect is stretched, so
            // localPosition is measured from the PARENT's pivot origin, and the root's pivot changes
            // with where the popup is anchored, setting it outright threw the panel half a popup
            // wide on every step that hangs off a target. shifting both edges is pivot-independent.
            panelRt.offsetMin = new Vector2(-PanelBleedX + PanelNudge.x, -PanelBleedY + PanelNudge.y);
            panelRt.offsetMax = new Vector2(PanelBleedX + PanelNudge.x, PanelBleedY + PanelNudge.y);
            panelRt.localRotation = TitleRot;
            UGUIShip.ApplyDeluxPanelDark(panelGo.AddComponent<Image>(), 2f);

            var title = UGUIShip.CreateLabel(root.transform,
                new Rect(0f, 0f, innerW, TitleBandH), titleId, FsTitle, TitleCol, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            UGUIShip.Unstylize(title);
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f);
            trt.pivot = new Vector2(0f, 0.5f);
            trt.anchoredPosition = new Vector2(PadX, -(PadTop + TitleBandH * 0.5f));
            trt.localRotation = TitleRot;
            trt.localScale = TitleScale;

            float bodyTop = PadTop + TitleBandH + GapTitleBody;
            var body = UGUIShip.CreateLabel(root.transform,
                new Rect(PadX, bodyTop, innerW, 40f), bodyId, FsBody, BodyCol, TextAnchor.UpperLeft);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;

            // the label is parented and sized, so preferredHeight now reflects the real wrapped
            // line count at this width, size the panel to it instead of guessing.
            float bodyH = Mathf.Max(UGUIShip.PreferredHeight(body, innerW), FsBody * 2f);
            body.rectTransform.sizeDelta = new Vector2(innerW, bodyH);
            onBody?.Invoke(body);

            bool wantInput = inputInitial != null;
            bool wantBar = onBar != null;
            float inputBlock = wantInput || wantBar ? GapBodyButtons + InputH : 0f;
            float height = bodyTop + bodyH + inputBlock
                           + (nBtn > 0 ? GapBodyButtons + BtnH : 0f) + PadBottom;
            rt.sizeDelta = new Vector2(Width, height);
            rt.anchoredPosition = ClampToCanvas(pos, pivot, new Vector2(Width, height), canvasRt.rect.size);

            // now the height is known, squash the panel to land where it should (see PanelScaleX)
            panelRt.localScale = new Vector3(PanelScaleX,
                (height + PanelTopOverhang - PanelBottomTrim) / (height + PanelBleedY * 2f), 1f);

            if (wantInput)
            {
                var field = UGUIShip.CreateInputField(root.transform,
                    new Rect(PadX, bodyTop + bodyH + GapBodyButtons, innerW, InputH),
                    "", Color.black, UGUIShip.WHITE, FsBody);
                field.text = inputInitial;
                var frt = field.GetComponent<RectTransform>();
                frt.anchorMin = frt.anchorMax = new Vector2(0f, 1f);
                frt.pivot = new Vector2(0f, 1f);
                frt.anchoredPosition = new Vector2(PadX, -(bodyTop + bodyH + GapBodyButtons));
                onInput?.Invoke(field);
            }
            else if (wantBar)
            {
                onBar(UGUIShip.CreateLoadingBar(root.transform,
                    new Rect(PadX, bodyTop + bodyH + GapBodyButtons + 3f, innerW, InputH - 6f), BtnPrimary));
            }

            float bx = BtnOverhangX;
            float by = -(height - PadBottom - BtnH * 0.5f);
            for (int i = nBtn - 1; i >= 0; i--)
            {
                var b = buttons[i];
                var btn = UGUIShip.CreateButton(root.transform,
                    new Rect(0f, 0f, BtnW, BtnH), b.LabelId, b.Bg, UGUIShip.WHITE, FsBtn, b.OnClick);
                var brt = btn.GetComponent<RectTransform>();
                brt.anchorMin = brt.anchorMax = new Vector2(1f, 1f);
                brt.pivot = new Vector2(1f, 0.5f);
                brt.anchoredPosition = new Vector2(bx, by);
                brt.localRotation = BtnRot;
                brt.localScale = BtnScale;
                bx -= BtnW + BtnGap;
            }

            rt.localScale = new Vector3(0f, 1f, 1f);
            return root;
        }

        private static Vector2 ClampToCanvas(Vector2 pos, Vector2 pivot, Vector2 size, Vector2 canvasSize)
        {
            float leftEdge = pos.x - pivot.x * size.x;
            float rightEdge = leftEdge + size.x;
            float bottomEdge = pos.y - pivot.y * size.y;
            float topEdge = bottomEdge + size.y;

            float xMin = -canvasSize.x * 0.5f + 24f;
            float xMax = canvasSize.x * 0.5f - 24f;
            float yMin = -canvasSize.y * 0.5f + 24f;
            float yMax = canvasSize.y * 0.5f - 24f;

            if (leftEdge < xMin) pos.x += xMin - leftEdge;
            if (rightEdge > xMax) pos.x -= rightEdge - xMax;
            if (bottomEdge < yMin) pos.y += yMin - bottomEdge;
            if (topEdge > yMax) pos.y -= topEdge - yMax;
            return pos;
        }
    }
}
