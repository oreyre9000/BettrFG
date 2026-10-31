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
        /// <summary>
        /// Builds a labeled horizontal slider using Unity's exact DefaultControls hierarchy.
        /// Fill Area: anchored (0,0.25)-(1,0.75), offsetMin.x=5, offsetMax.x=-5
        /// Fill: anchorMin(0,0) anchorMax(1,1), offsetMax.x=0  � Slider writes anchorMax.x
        /// Handle Slide Area: full stretch, offsetMin.x=10, offsetMax.x=-10
        /// Handle: anchorMin(0,0) anchorMax(0,1), sizeDelta.x=20 � Slider writes anchorMin/Max.x
        /// </summary>
        public static Slider CreateSlider(Transform parent, float x, float y, float w,
            string lbl, float init, float lh, float pad, int fontSize, Action<float> onChange,
            Color? labelColor = null, Color? fillColor = null, bool reserveLabel = true,
            float? resetTo = null, float minLabelW = 0f)
        {
            bool hasLabel = reserveLabel && !string.IsNullOrEmpty(lbl);
            float lblW = hasLabel ? Mathf.Max(fontSize * 2f, minLabelW) : 0f;
            float lblGap = hasLabel ? pad : 0f;

            if (hasLabel)
            {
                var lblGo = new GameObject("Label_" + lbl);
                lblGo.transform.SetParent(parent, false);
                var lblRt = lblGo.AddComponent<RectTransform>();
                var lt = lblGo.AddComponent<Text>();
                lt.text = LocText(lbl);
                lt.fontSize = fontSize;
                lt.horizontalOverflow = HorizontalWrapMode.Overflow;
                lt.verticalOverflow = VerticalWrapMode.Overflow;
                Stylize(lt);
                LocBind(lt, lbl);
                lt.color = labelColor ?? Color.white;
                lt.alignment = TextAnchor.MiddleLeft;
                lt.raycastTarget = false;
                lblW = Mathf.Max(lblW, Mathf.Ceil(PreferredWidth(lt)) + 2f);
                SetPixelRect(lblRt, new Rect(x, y, lblW, lh));
            }
            float sldW = w - lblW - lblGap;

            // slider root � same height as lh, full row
            var sldGo = new GameObject("Slider_" + lbl);
            sldGo.transform.SetParent(parent, false);
            var sldRt = sldGo.AddComponent<RectTransform>();
            SetPixelRect(sldRt, new Rect(x + lblW + lblGap, y, sldW, lh));

            // Background � full stretch
            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(sldGo.transform, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0f, 0.25f);
            bgRt.anchorMax = new Vector2(1f, 0.75f);
            bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
            ApplyDeluxBase(bgGo.AddComponent<Image>(), 5f);

            // Fill Area � Unity DefaultControls exact values
            var fillAreaGo = new GameObject("Fill Area");
            fillAreaGo.transform.SetParent(sldGo.transform, false);
            var faRt = fillAreaGo.AddComponent<RectTransform>();
            faRt.anchorMin = new Vector2(0f, 0.25f);
            faRt.anchorMax = new Vector2(1f, 0.75f);
            faRt.offsetMin = new Vector2(5f, 0f);
            faRt.offsetMax = new Vector2(-5f, 0f);

            // Fill � Slider component drives anchorMax.x at runtime
            var fillBaseColor = fillColor ?? new Color(0.8f, 0.8f, 0.8f, 1f);
            RectTransform fillRt;
            if (LoadDeluxSlice("BetterFG.assets.ui.general.uisprite_delux_colorfill.png", ref _deluxFillSprite) != null)
            {
                var fillImg = AddDeluxSlice(fillAreaGo.transform, "BetterFG.assets.ui.general.uisprite_delux_colorfill.png",
                    ref _deluxFillSprite, fillBaseColor, "Fill", 4f);
                RegisterFill(fillImg, fillBaseColor);
                fillRt = fillImg.GetComponent<RectTransform>();
            }
            else
            {
                var fillGo = new GameObject("Fill");
                fillGo.transform.SetParent(fillAreaGo.transform, false);
                fillRt = fillGo.AddComponent<RectTransform>();
                fillRt.anchorMin = Vector2.zero;
                fillRt.anchorMax = Vector2.one;
                fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
                var fillImg2 = fillGo.AddComponent<Image>();
                fillImg2.color = fillBaseColor;
                fillImg2.raycastTarget = false;
            }

            // Handle Slide Area � inset 10px each side (Unity default)
            var hsGo = new GameObject("Handle Slide Area");
            hsGo.transform.SetParent(sldGo.transform, false);
            var hsRt = hsGo.AddComponent<RectTransform>();
            hsRt.anchorMin = Vector2.zero;
            hsRt.anchorMax = Vector2.one;
            hsRt.offsetMin = new Vector2(10f, 0f);
            hsRt.offsetMax = new Vector2(-10f, 0f);

            // Handle � Slider drives anchorMin/Max.x; sizeDelta.x = width, height = stretch
            var handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(hsGo.transform, false);
            var handleRt = handleGo.AddComponent<RectTransform>();
            handleRt.anchorMin = new Vector2(0f, 0f);
            handleRt.anchorMax = new Vector2(0f, 1f);
            handleRt.pivot = new Vector2(0.5f, 0.5f);
            handleRt.sizeDelta = new Vector2(20f, 0f);
            var handleImg = handleGo.AddComponent<Image>();
            var handleFill = ApplyDeluxSkin(handleGo, handleImg, Color.white, withShine: true, ppuMult: 3f);

            // Slider component
            var slider = sldGo.AddComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleFill ?? handleImg;
            var slColors = slider.colors;
            slColors.normalColor = new Color(0.22f, 0.22f, 0.22f, 1f);
            slColors.highlightedColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            slColors.pressedColor = new Color(0.16f, 0.16f, 0.16f, 1f);
            slColors.fadeDuration = 0f;
            slider.colors = slColors;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.value = init;

            slider.onValueChanged.AddListener(new Action<float>(v => onChange(v)));

            if (resetTo.HasValue) WireSliderReset(slider, resetTo.Value);

            return slider;
        }

        public static void WireSliderReset(Slider slider, float resetTo)
        {
            float def = Mathf.Clamp(resetTo, slider.minValue, slider.maxValue);
            var go = slider.gameObject;
            ForwardScrollToParent(go);

            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            entry.callback.AddListener(new Action<BaseEventData>(data =>
            {
                var ped = data?.TryCast<PointerEventData>();
                if (ped == null || ped.button != PointerEventData.InputButton.Right) return;
                if (Mathf.Approximately(slider.value, def)) return;
                PlayClick();
                slider.value = def;
            }));
            go.GetComponent<EventTrigger>().triggers.Add(entry);
        }

        // ── RGB sliders + compact hex input ──────────────────────────────────
        // builds R/G/B sliders plus a short #RRGGBB field on its own row. getR/G/B read the
        // current channel, setR/G/B write it, onApply saves + applies (swatch + game). editing
        // the hex moves the sliders; dragging a slider rewrites the hex — guarded so they don't
        // fight. sliders are returned in case the caller needs to push values into them later.
        public static void CreateColorControls(Transform parent, float x, ref float cy, float w,
            Func<float> getR, Func<float> getG, Func<float> getB,
            Action<float> setR, Action<float> setG, Action<float> setB, Action onApply,
            out Slider sR, out Slider sG, out Slider sB, Color? resetTo = null)
        {
            float lh = UIScale.LH, sh = UIScale.SH, pad = UIScale.PAD;
            int fs = UIScale.FS_SM;
            var suppress = new bool[1];   // shared 1-cell flag so all closures see the same value
            InputField hex = null;
            Slider lsR = null, lsG = null, lsB = null;

            void RefreshHex()
            {
                if (hex == null) return;
                suppress[0] = true;
                SetInputText(hex, "#" + ColorToHex(getR(), getG(), getB()));
                suppress[0] = false;
            }

            lsR = CreateSlider(parent, x, cy, w, "R", getR(), lh, pad, fs,
                v => { if (suppress[0]) return; setR(v); onApply(); RefreshHex(); },
                new Color(1f, 0.3f, 0.3f), new Color(1f, 0.3f, 0.3f), true, resetTo?.r);
            cy += lh + sh;
            lsG = CreateSlider(parent, x, cy, w, "G", getG(), lh, pad, fs,
                v => { if (suppress[0]) return; setG(v); onApply(); RefreshHex(); },
                new Color(0.3f, 1f, 0.3f), new Color(0.3f, 1f, 0.3f), true, resetTo?.g);
            cy += lh + sh;
            lsB = CreateSlider(parent, x, cy, w, "B", getB(), lh, pad, fs,
                v => { if (suppress[0]) return; setB(v); onApply(); RefreshHex(); },
                new Color(0.4f, 0.6f, 1f), new Color(0.4f, 0.6f, 1f), true, resetTo?.b);
            cy += lh + sh;

            float lblW = fs * 2.4f;
            float fieldW = fs * 7f;   // fits "#RRGGBB", not a whole row
            CreateLabel(parent, new Rect(x, cy, lblW, lh), "HEX", fs, new Color(1f, 1f, 1f, 0.35f));
            hex = CreateInputField(parent, new Rect(x + lblW, cy, fieldW, lh), "#RRGGBB", null, null, fs);
            hex.characterLimit = 7;
            hex.onEndEdit.AddListener(new Action<string>(txt =>
            {
                if (suppress[0]) return;
                if (!HexToColor(txt, out float r, out float g, out float b)) { RefreshHex(); return; }
                setR(r); setG(g); setB(b);
                suppress[0] = true;
                if (lsR != null) lsR.value = r;
                if (lsG != null) lsG.value = g;
                if (lsB != null) lsB.value = b;
                suppress[0] = false;
                onApply();
            }));
            RefreshHex();
            cy += lh + pad;

            sR = lsR; sG = lsG; sB = lsB;
        }

        public static string ColorToHex(float r, float g, float b)
        {
            int ir = Mathf.Clamp(Mathf.RoundToInt(r * 255f), 0, 255);
            int ig = Mathf.Clamp(Mathf.RoundToInt(g * 255f), 0, 255);
            int ib = Mathf.Clamp(Mathf.RoundToInt(b * 255f), 0, 255);
            return ir.ToString("X2") + ig.ToString("X2") + ib.ToString("X2");
        }

        public static bool HexToColor(string s, out float r, out float g, out float b)
        {
            r = g = b = 0f;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim().TrimStart('#');
            if (s.Length == 3) s = "" + s[0] + s[0] + s[1] + s[1] + s[2] + s[2];   // #RGB → #RRGGBB
            if (s.Length != 6) return false;
            const System.Globalization.NumberStyles hx = System.Globalization.NumberStyles.HexNumber;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (!int.TryParse(s.Substring(0, 2), hx, ci, out int ir)) return false;
            if (!int.TryParse(s.Substring(2, 2), hx, ci, out int ig)) return false;
            if (!int.TryParse(s.Substring(4, 2), hx, ci, out int ib)) return false;
            r = ir / 255f; g = ig / 255f; b = ib / 255f;
            return true;
        }
    }
}
