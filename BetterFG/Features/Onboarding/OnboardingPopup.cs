using System;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.Features.Onboarding
{
    public enum PopUpKind { None, Info, Warning, Dangerous }

    // tutorial popup, styled off the sidewheel windows: delux dark panel, tilted bold title in a
    // band at the top, wrapped body, and small tilted buttons in the bottom-right corner. the title
    // tilt matches the windows/tab titles; the buttons mirror it.
    public static class OnboardingPopup
    {
        public const float Width = 360f;

        private const float PadX = 16f;
        private const float PadTop = 8f;
        private const float PadBottom = 10f;
        private const float TitleBandH = 24f;
        private const float GapTitleBody = 2f;
        private const float GapBodyButtons = 10f;

        private const float InputH = 24f;

        private const float BtnW = 94.4f;
        private const float BtnH = 26f;
        private const float BtnDrawH = 29.4f;
        private const float BtnGap = 8f;
        private const float BtnPadX = 12f;
        private const float BtnOverhangX = 8f;

        private const int FsTitle = 21;
        private const int FsBody = 13;
        private const int FsBtn = 14;

        public static readonly Quaternion TitleRot = Quaternion.Euler(22f, 345f, 0f);
        public static readonly Vector3 TitleScale = new Vector3(0.9382f, 1.0713f, 1f);
        private static readonly Quaternion BtnRot = Quaternion.Euler(22f, 15f, 0f);
        private static readonly Vector3 BtnScale = new Vector3(1.02f, 1.08f, 1f);

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

        private static readonly Color TitleCol = Color.white;
        private static readonly Color BodyCol = new Color(1f, 1f, 1f, 0.78f);
        public static readonly Color BtnPrimary = new Color(0.25f, 0.5f, 0.25f, 1f);
        public static readonly Color BtnGhost = new Color(0.18f, 0.18f, 0.18f, 1f);
        public static readonly Color BtnDanger = new Color(0.75f, 0.18f, 0.18f, 1f);
        public static readonly Color BtnWarning = new Color(1f, 0.82f, 0.18f, 1f);
        public static readonly Color BtnInfo = new Color(0.25f, 0.5f, 0.95f, 1f);

        private const float IconShift = 28f;
        private const float GlowAlpha = 0.08f;
        private static readonly Color KindUnder = new Color(0.03f, 0.03f, 0.035f, 1f);
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> _icons =
            new System.Collections.Generic.Dictionary<string, Sprite>();
        private static Material _additive;

        private const float PopDur = 0.5f;
        private const float PopFps = 12f;
        private const int PopSteps = 6;
        private const float PopTravel = 16f;
        private const float PopSize = 20f;
        private const float PopInset = 2f;
        private static Sprite _popSprite;
        private static AnimationClip _popClip;

        public static Color ConfirmColor(PopUpKind kind) => kind switch
        {
            PopUpKind.Dangerous => BtnDanger,
            PopUpKind.Warning => BtnWarning,
            PopUpKind.Info => BtnInfo,
            _ => BtnPrimary,
        };

        public static float DimAlpha(PopUpKind kind) => kind switch
        {
            PopUpKind.Dangerous => 0.5f,
            PopUpKind.Warning => 0.35f,
            PopUpKind.Info => 0.25f,
            _ => 0f,
        };

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
            Action<LoadingBar> onBar = null, Action<Text> onBody = null,
            float customH = 0f, Action<RectTransform> onCustom = null, PopUpKind kind = PopUpKind.None, float width = Width)
        {
            var canvasRt = canvas.GetComponent<RectTransform>();
            int nBtn = buttons != null ? buttons.Length : 0;
            float innerW = width - PadX * 2f;

            var root = new GameObject("Onboarding_Popup");
            root.transform.SetParent(canvas.transform, false);
            var rt = root.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = pivot;
            rt.sizeDelta = new Vector2(width, 160f);

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
            UGUIShip.ApplyDeluxPanelHighShadow(panelGo.AddComponent<Image>(), 1f);

            Text title = null;
            if (titleId != null)
            {
                title = UGUIShip.CreateLabel(root.transform,
                    new Rect(0f, 0f, innerW, TitleBandH), titleId, FsTitle, TitleCol, TextAnchor.MiddleLeft);
                title.fontStyle = FontStyle.Bold;
                title.horizontalOverflow = HorizontalWrapMode.Overflow;
                title.verticalOverflow = VerticalWrapMode.Overflow;
                UGUIShip.Unstylize(title);
                var trt = title.rectTransform;
                trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f);
                trt.pivot = new Vector2(0f, 0.5f);
                trt.anchoredPosition = new Vector2(PadX, -(PadTop + TitleBandH * 0.5f));
                trt.localRotation = TitleRot;
                trt.localScale = TitleScale;
            }

            float bodyTop = titleId != null ? PadTop + TitleBandH + GapTitleBody : PadTop;
            float bodyH = 0f;
            if (bodyId != null)
            {
                var body = UGUIShip.CreateLabel(root.transform,
                    new Rect(PadX, bodyTop, innerW, 40f), bodyId, FsBody, BodyCol, TextAnchor.UpperLeft);
                body.horizontalOverflow = HorizontalWrapMode.Wrap;
                body.verticalOverflow = VerticalWrapMode.Overflow;

                // the label is parented and sized, so preferredHeight now reflects the real wrapped
                // line count at this width, size the panel to it instead of guessing.
                bodyH = Mathf.Max(UGUIShip.PreferredHeight(body, innerW), FsBody * 2f);
                body.rectTransform.sizeDelta = new Vector2(innerW, bodyH);
                onBody?.Invoke(body);
            }

            bool wantInput = inputInitial != null;
            bool wantBar = onBar != null;
            float inputBlock = wantInput || wantBar ? GapBodyButtons + InputH : 0f;
            float customGap = bodyId != null ? GapBodyButtons : 0f;
            float customTop = bodyTop + bodyH + inputBlock + customGap;
            float customBlock = onCustom != null ? customGap + customH : 0f;
            float height = bodyTop + bodyH + inputBlock + customBlock
                           + (nBtn > 0 ? (bodyId != null || onCustom != null ? GapBodyButtons : 0f) + BtnH : 0f) + PadBottom;
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = ClampToCanvas(pos, pivot, new Vector2(width, height), canvasRt.rect.size);

            // now the height is known, squash the panel to land where it should (see PanelScaleX)
            panelRt.localScale = new Vector3(PanelScaleX,
                (height + PanelTopOverhang - PanelBottomTrim) / (height + PanelBleedY * 2f), 1f);

            if (kind != PopUpKind.None) DressKind(kind, root.transform, panelRt, title);

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

            if (onCustom != null)
            {
                var cgo = new GameObject("Custom");
                cgo.transform.SetParent(root.transform, false);
                var crt = cgo.AddComponent<RectTransform>();
                UGUIShip.SetPixelRect(crt, new Rect(PadX, customTop, innerW, customH));
                onCustom(crt);
            }

            float bx = BtnOverhangX;
            float by = -(height - PadBottom - BtnH * 0.5f);
            for (int i = nBtn - 1; i >= 0; i--)
            {
                var b = buttons[i];
                var btn = UGUIShip.CreateButton(root.transform,
                    new Rect(0f, 0f, BtnW, BtnDrawH), b.LabelId, b.Bg, UGUIShip.WHITE, FsBtn, b.OnClick);
                var label = btn.transform.Find("Label").GetComponent<Text>();
                float w = Mathf.Max(BtnW, Mathf.Ceil(UGUIShip.PreferredWidth(label)) + BtnPadX * 2f);
                var brt = btn.GetComponent<RectTransform>();
                brt.anchorMin = brt.anchorMax = new Vector2(1f, 1f);
                brt.pivot = new Vector2(1f, 0.5f);
                brt.sizeDelta = new Vector2(w, BtnDrawH);
                brt.anchoredPosition = new Vector2(bx, by);
                brt.localRotation = BtnRot;
                brt.localScale = BtnScale;
                bx -= w + BtnGap;

                var lrt = label.rectTransform;
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.pivot = new Vector2(0.5f, 0.5f);
                lrt.offsetMin = lrt.offsetMax = Vector2.zero;
                lrt.localRotation = Quaternion.Inverse(BtnRot);
                lrt.localScale = new Vector3(1f / BtnScale.x, 1f / BtnScale.y, 1f);
            }


            foreach (var s in root.GetComponentsInChildren<Selectable>(true))
            {
                var cb = s.colors;
                cb.normalColor = Opaque(cb.normalColor);
                cb.highlightedColor = Opaque(cb.highlightedColor);
                cb.pressedColor = Opaque(cb.pressedColor);
                cb.selectedColor = Opaque(cb.selectedColor);
                cb.disabledColor = Opaque(cb.disabledColor);
                s.colors = cb;
                var baseImg = s.GetComponent<Image>();
                if (baseImg != null && baseImg != s.targetGraphic) baseImg.canvasRenderer.SetColor(Color.white);
            }

            rt.localScale = new Vector3(0f, 1f, 1f);
            return root;
        }

        private static void DressKind(PopUpKind kind, Transform root, RectTransform panelRt, Text title)
        {
            var (iconName, glowCol, iconTint) = kind switch
            {
                PopUpKind.Dangerous => ("HUD_danger_icon_1080p", new Color(1f, 0.1f, 0.15f, 0f), Color.white),
                PopUpKind.Warning => ("Icon_Notification", new Color(1f, 0.6f, 0.1f, 0f), Color.white),
                _ => ("UI_Icon_Info", new Color(0.2f, 0.5f, 1f, 0f), new Color(0.45f, 0.72f, 1f, 1f)),
            };

            var under = UnityEngine.Object.Instantiate(panelRt.gameObject, root);
            under.name = "Under";
            under.transform.SetSiblingIndex(panelRt.GetSiblingIndex());
            var underImg = under.GetComponent<Image>();
            underImg.color = KindUnder;
            underImg.raycastTarget = false;

            if (_additive == null)
                foreach (var sh in Resources.FindObjectsOfTypeAll<Shader>())
                    if (sh != null && sh.name == "UI/Additive") { _additive = new Material(sh); break; }
            if (_additive != null)
            {
                panelRt.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                var glowGo = new GameObject("Glow");
                glowGo.transform.SetParent(panelRt, false);
                var grt = glowGo.AddComponent<RectTransform>();
                grt.anchorMin = Vector2.zero;
                grt.anchorMax = Vector2.one;
                grt.offsetMin = grt.offsetMax = Vector2.zero;
                var glow = glowGo.AddComponent<Image>();
                glow.raycastTarget = false;
                glow.material = _additive;
                var grad = glowGo.AddComponent<GradientImage>();
                grad.Vertical = true;
                grad.TopColor = glowCol;
                grad.BottomColor = new Color(glowCol.r, glowCol.g, glowCol.b, GlowAlpha);
            }

            if (title == null) return;
            if (!_icons.TryGetValue(iconName, out var sprite) || sprite == null)
            {
                sprite = null;
                foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                    if (s != null && s.name == iconName) { sprite = s; break; }
                if (sprite == null) return;
                _icons[iconName] = sprite;
            }

            var trt = title.rectTransform;
            trt.anchoredPosition += new Vector2(IconShift, 0f);
            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(trt, false);
            var irt = iconGo.AddComponent<RectTransform>();
            irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(1f, 0.5f);
            irt.sizeDelta = new Vector2(30f, 27f);
            irt.anchoredPosition = new Vector2(-3f, 0f);
            irt.localScale = new Vector3(1f / TitleScale.x, 1f / TitleScale.y, 1f);
            var icon = iconGo.AddComponent<Image>();
            icon.sprite = sprite;
            icon.color = iconTint;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
        }

        public const float ToggleH = 24f;

        public static void BuildToggle(RectTransform box, string labelId, bool on, Action<bool> changed)
        {
            const float w = 44f, h = 18f;
            float boxW = box.sizeDelta.x;
            UGUIShip.CreateLabel(box, new Rect(0f, 0f, boxW - w - 8f, ToggleH), labelId, FsBody, TitleCol, TextAnchor.MiddleLeft);
            Button btn = null;
            btn = UGUIShip.CreateButton(box, new Rect(boxW - w, (ToggleH - h) * 0.5f, w, h),
                on ? "ui.on" : "ui.off", on ? UGUIShip.TOGGLE_ON : UGUIShip.TOGGLE_OFF, UGUIShip.WHITE, 11, new Action(() =>
                {
                    on = !on;
                    UGUIShip.SetButtonColor(btn, on ? UGUIShip.TOGGLE_ON : UGUIShip.TOGGLE_OFF);
                    UGUIShip.RelabelText(btn.GetComponentInChildren<Text>(), on ? "ui.on" : "ui.off");
                    changed(on);
                }));
        }

        public static void AddCornerPops(GameObject popup) =>
            AddCornerPops(popup.transform, popup.transform.Find("Panel").GetComponent<RectTransform>(), "Pops");

        public static void AddCornerPopsAround(RectTransform frame)
        {
            string name = frame.name + "_Pops";
            var old = frame.parent.Find(name);
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            var pops = AddCornerPops(frame.parent, frame, name);
            pops.localScale = Vector3.one;
            pops.SetSiblingIndex(frame.GetSiblingIndex() + 1);
            pops.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            UnityEngine.Object.Destroy(pops.gameObject, PopDur + 0.1f);
        }

        private static RectTransform AddCornerPops(Transform host, RectTransform panel, string name)
        {
            if (_popSprite == null)
            {
                var tex = BetterFG.Utilities.EmbeddedResourceandUnity.LoadTexture("BetterFG.assets.ui.general.pop.png");
                _popSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                _popSprite.hideFlags = HideFlags.HideAndDontSave;
            }
            if (_popClip == null) _popClip = BuildPopClip();

            var pops = new GameObject(name);
            pops.transform.SetParent(host, false);
            var prt = pops.AddComponent<RectTransform>();
            prt.anchorMin = panel.anchorMin;
            prt.anchorMax = panel.anchorMax;
            prt.offsetMin = panel.offsetMin;
            prt.offsetMax = panel.offsetMax;
            prt.localRotation = panel.localRotation;
            prt.localScale = panel.localScale;

            foreach (var c in new[] { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) })
            {
                var corner = new GameObject("Corner");
                corner.transform.SetParent(pops.transform, false);
                var crt = corner.AddComponent<RectTransform>();
                crt.anchorMin = crt.anchorMax = c;
                crt.sizeDelta = Vector2.zero;
                crt.anchoredPosition = new Vector2(c.x == 0f ? PopInset : -PopInset, c.y == 1f ? -PopInset : PopInset);
                crt.localScale = new Vector3(c.x == 0f ? 1f : -1f, c.y == 1f ? 1f : -1f, 1f);

                var pop = new GameObject("Pop");
                pop.transform.SetParent(corner.transform, false);
                var rt = pop.AddComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.75f, 0.25f);
                rt.sizeDelta = new Vector2(PopSize, PopSize);
                var img = pop.AddComponent<Image>();
                img.sprite = _popSprite;
                img.raycastTarget = false;
                pop.AddComponent<CanvasGroup>().alpha = 0f;
                var anim = pop.AddComponent<Animation>();
                anim.AddClip(_popClip, "pop");
                anim.clip = _popClip;
                anim.Play("pop");
            }
            return prt;
        }

        private static AnimationClip BuildPopClip()
        {
            var clip = new AnimationClip { legacy = true, wrapMode = WrapMode.Once, hideFlags = HideFlags.HideAndDontSave };
            var rt = Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>();
            clip.SetCurve("", rt, "m_AnchoredPosition.x", SteppedCurve(t => -PopTravel * PopEase(t)));
            clip.SetCurve("", rt, "m_AnchoredPosition.y", SteppedCurve(t => PopTravel * PopEase(t)));
            var scale = SteppedCurve(t => t < 0.05f ? 0.6f : t < 0.12f ? 1.1f : 1f - 0.4f * Mathf.Clamp01((t - 0.12f) / 0.25f));
            clip.SetCurve("", rt, "m_LocalScale.x", scale);
            clip.SetCurve("", rt, "m_LocalScale.y", scale);
            clip.SetCurve("", Il2CppInterop.Runtime.Il2CppType.Of<CanvasGroup>(), "m_Alpha",
                SteppedCurve(t => t < 0.05f ? 0.6f : t < 0.25f ? 1f : t < 0.35f ? 0.4f : 0f));
            return clip;
        }

        private static float PopEase(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 0.2f), 3f);

        private static AnimationCurve SteppedCurve(Func<float, float> f)
        {
            var keys = new Keyframe[PopSteps + 2];
            for (int i = 0; i <= PopSteps; i++)
            {
                float t = i / PopFps;
                keys[i] = new Keyframe(t, f(t), float.PositiveInfinity, float.PositiveInfinity);
            }
            keys[PopSteps + 1] = new Keyframe(PopDur, f(PopDur), float.PositiveInfinity, float.PositiveInfinity);
            return new AnimationCurve(keys);
        }

        private static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 1f);

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
