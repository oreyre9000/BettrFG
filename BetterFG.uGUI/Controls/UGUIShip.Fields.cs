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
        // InputField overload of RelabelText (Text overload lives in UGUIShip.Labels.cs) — some
        // reassignment call sites target an InputField's own .text rather than a Text label.
        public static void RelabelText(InputField field, string id)
        {
            if (field == null) return;
            SetInputText(field, LocText(id), false);
        }

        public static void SetInputText(InputField field, string value, bool notify = false)
        {
            if (field == null) return;
            value = value ?? "";
            field.text = value;
            if (field.textComponent != null) field.textComponent.text = value;
            if (field.placeholder != null) field.placeholder.gameObject.SetActive(string.IsNullOrEmpty(value));
            if (notify) field.onValueChanged?.Invoke(value);
        }

        // �� InputField ��������������������������������������������������������
        public static InputField CreateInputField(Transform parent, Rect rect,
            string placeholder = "", Color? bgColor = null, Color? textColor = null,
            int fontSize = 13)
        {
            var bg = bgColor ?? new Color(0.15f, 0.15f, 0.15f, 1f);
            var tc = textColor ?? Color.white;

            var go = new GameObject("InputField");
            go.transform.SetParent(parent, false);
            SetPixelRect(go.AddComponent<RectTransform>(), rect);

            var img = go.AddComponent<Image>();
            var fill = ApplyDeluxSkin(go, img, bg, withShine: false, ppuMult: 3f);
            if (fill != null) fill.transform.SetAsFirstSibling();

            var field = go.AddComponent<InputField>();
            var nav = field.navigation;
            nav.mode = Navigation.Mode.None;
            field.navigation = nav;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(6, 2);
            textRt.offsetMax = new Vector2(-6, -2);
            var textComp = textGo.AddComponent<Text>();
            textComp.font = Arial; // NOT Stylize: the mirror is a once-per-frame poll and hides the real Text, which InputField needs live for caret/selection/editing
            textComp.fontSize = fontSize;
            textComp.color = tc;
            textComp.alignment = TextAnchor.MiddleLeft;
            textComp.supportRichText = false;
            textComp.raycastTarget = false;
            textComp.horizontalOverflow = HorizontalWrapMode.Overflow;
            textComp.verticalOverflow = VerticalWrapMode.Overflow;

            var phGo = new GameObject("Placeholder");
            phGo.transform.SetParent(go.transform, false);
            var phRt = phGo.AddComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(6, 2);
            phRt.offsetMax = new Vector2(-6, -2);
            var phText = phGo.AddComponent<Text>();
            phText.font = Arial;
            phText.fontSize = fontSize;
            phText.color = new Color(tc.r, tc.g, tc.b, 0.4f);
            phText.fontStyle = FontStyle.Italic;
            phText.text = LocText(placeholder);
            phText.alignment = TextAnchor.MiddleLeft;
            phText.supportRichText = false;
            phText.raycastTarget = false;
            Stylize(phText); // safe here: placeholder isn't the live-edited text, so hiding it behind a TMP mirror doesn't break caret/selection
            PollEnabled(phText);
            LocBind(phText, placeholder);

            field.textComponent = textComp;
            field.placeholder = phText;
            SetInputText(field, "", false);

            // auto: any field whose placeholder id mentions "search" gets the magnifying-glass icon +
            // left-pad shift. one place to maintain it; every search bar gets it for free.
            if (!string.IsNullOrEmpty(placeholder) &&
                placeholder.IndexOf("search", StringComparison.OrdinalIgnoreCase) >= 0)
                AddSearchIcon(field);

            return field;
        }

        // small icon left of the centered label on a header dropdown. shifts the label right and
        // parks the icon at the (shifted) label's left edge so icon+text read as one centered block
        // instead of the icon floating off in the left margin alone.
        public static void AddHeaderIcon(Button btn, string resource)
        {
            if (btn == null) return;
            var sprite = LoadSprite(resource);
            if (sprite == null) return;

            var lbl = btn.GetComponentInChildren<Text>();
            float size = (lbl != null ? lbl.fontSize : UIScale.FS_SM) * 0.75f;
            float gap = 3f;

            if (lbl != null)
            {
                var lrt = lbl.GetComponent<RectTransform>();
                if (lrt != null) lrt.offsetMin += new Vector2(size + gap * 2f, 0f);
            }

            var go = new GameObject("HeaderIcon");
            go.transform.SetParent(btn.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            rt.anchoredPosition = new Vector2(gap, 0f);
        }

        // sized off the field's font size at 0.75x — same ratio as the header dropdown icons so all
        // search bars match. exposed public for the one hand-rolled search field that doesn't go
        // through CreateInputField (CustomizationTab).
        public static void AddSearchIcon(InputField field, string resource = "BetterFG.assets.ui.button.search.png")
        {
            if (field == null) return;
            var sprite = LoadSprite(resource);
            if (sprite == null) return;

            int fs = field.textComponent != null ? field.textComponent.fontSize : 13;
            float size = fs * 0.75f;
            float gap = 4f;
            float leftPad = 6f + size + gap;

            var iconGo = new GameObject("SearchIcon");
            iconGo.transform.SetParent(field.transform, false);
            var rt = iconGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(6f, 0f);
            rt.sizeDelta = new Vector2(size, size);
            var img = iconGo.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;

            if (field.textComponent != null)
            {
                var trt = field.textComponent.GetComponent<RectTransform>();
                if (trt != null) trt.offsetMin = new Vector2(leftPad, trt.offsetMin.y);
            }
            if (field.placeholder != null)
            {
                var prt = field.placeholder.GetComponent<RectTransform>();
                if (prt != null) prt.offsetMin = new Vector2(leftPad, prt.offsetMin.y);
            }
        }

        // �� Increment stepper ([-] value [+]) ��������������������������������
        // one place for every -/value/+ control. lays out the two step buttons either side of the value
        // inside `rect`, wired to get/set. wrap=true (default) loops min<->max on overflow (7 +1 -> 0),
        // wrap=false clamps. isFloat keeps decimals, otherwise the value stays whole. `fmt` formats the
        // value text. onChange fires after set, for the caller to save/refresh. the value sits in a real
        // InputField, so a pad can hold +/- and a keyboard can type the extreme the steps won't reach.
        // returns it so callers can resync if the value changes elsewhere.
        // pass a 2-long `holds` array to get hold-to-repeat on the −/+ buttons (filled minus-then-plus);
        // that makes it the caller's job to Tick() them every frame, which is why it's opt-in.
        public static readonly Color STEP_COL = new Color(0.22f, 0.32f, 0.42f, 1f);
        public static InputField CreateIncrement(Transform parent, Rect rect, float min, float max,
            Func<float> get, Action<float> set, float step, bool isFloat = false, bool wrap = true,
            int fontSize = 13, Func<float, string> fmt = null, Action<float> onChange = null,
            HoldButtonState[] holds = null)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (fmt == null) fmt = v => isFloat ? v.ToString(ci) : Mathf.RoundToInt(v).ToString(ci);

            float stepW = rect.height;                    // square step buttons
            float valW = rect.width - stepW * 2f;

            var field = CreateInputField(parent, new Rect(rect.x + stepW, rect.y, valW, rect.height),
                "", new Color(0.12f, 0.12f, 0.12f, 1f), Color.white, fontSize);
            var incFill = field.gameObject.transform.Find("ColorFill")?.GetComponent<Image>();
            if (incFill != null) RegisterFill(incFill, new Color(0.12f, 0.12f, 0.12f, 1f));
            field.contentType = isFloat ? InputField.ContentType.DecimalNumber : InputField.ContentType.IntegerNumber;
            field.textComponent.alignment = TextAnchor.MiddleCenter;
            SetInputText(field, fmt(get()), false);

            void Commit(float v)
            {
                v = Mathf.Clamp(v, min, max);
                if (!isFloat) v = Mathf.Round(v);
                set(v);
                SetInputText(field, fmt(v), false);
                onChange?.Invoke(v);
            }

            field.onEndEdit.AddListener(new Action<string>(s =>
            {
                if (float.TryParse(s, System.Globalization.NumberStyles.Float, ci, out var typed)) Commit(typed);
                else SetInputText(field, fmt(get()), false);
            }));

            void Step(float x, float delta, string glyph, int slot)
            {
                var fire = new Action(() =>
                {
                    // decimal, and anchored at 0 instead of min. float stepping drifts (0.1+0.05
                    // = 0.15000001) and the field renders every digit of it; anchoring the grid
                    // at a min of 0.01 walks you along 0.06/0.11/0.16 instead of round numbers
                    decimal grid = (decimal)step;
                    float nv = (float)(Math.Round(((decimal)get() + (decimal)delta) / grid) * grid);
                    float span = max - min + (isFloat ? 0f : 1f);
                    if (wrap && span > 0f) nv = min + Mathf.Repeat(nv - min, span);
                    Commit(nv);
                });
                var r = new Rect(x, rect.y, stepW, rect.height);
                if (holds == null) CreateButton(parent, r, glyph, STEP_COL, Color.white, fontSize, fire);
                else CreateHoldButton(parent, r, glyph, STEP_COL, Color.white, fontSize, fire, out holds[slot]);
            }

            Step(rect.x, -step, "−", 0);                 // minus sign, matches existing rows
            Step(rect.x + stepW + valW, step, "+", 1);
            return field;
        }

        // int flavour, the original shape — every existing caller still lands here
        public static InputField CreateIncrement(Transform parent, Rect rect, int min, int max,
            Func<int> get, Action<int> set, bool wrap = true, int fontSize = 13,
            Func<int, string> fmt = null, Action<int> onChange = null)
            => CreateIncrement(parent, rect, min, max, () => get(), v => set(Mathf.RoundToInt(v)),
                1f, false, wrap, fontSize,
                fmt == null ? null : new Func<float, string>(v => fmt(Mathf.RoundToInt(v))),
                onChange == null ? null : new Action<float>(v => onChange(Mathf.RoundToInt(v))));

        public static Text CreateCarousel(Transform parent, Rect rect, string[] labels, int current,
            Action<int> onStep, Color? bg = null, int fontSize = 13)
        {
            var col = bg ?? new Color(0.28f, 0.28f, 0.34f, 1f);
            float arrowW = rect.height;
            current = Mathf.Clamp(current, 0, labels.Length - 1);
            CreateButton(parent, new Rect(rect.x, rect.y, arrowW, rect.height), "‹", col, Color.white, fontSize,
                new Action(() => onStep(-1)));
            var lbl = CreateLabel(parent,
                new Rect(rect.x + arrowW, rect.y, rect.width - arrowW * 2f, rect.height),
                labels[current], fontSize, Color.white, TextAnchor.MiddleCenter);
            CreateButton(parent, new Rect(rect.x + rect.width - arrowW, rect.y, arrowW, rect.height), "›", col, Color.white, fontSize,
                new Action(() => onStep(+1)));
            return lbl;
        }
    }
}
