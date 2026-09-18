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
        // every text-creating widget below takes `text` as a localization id, not literal display
        // text — LocText resolves it through LocalizeGet and LocBind attaches the live-switch binding.
        // an id that isn't in the table just renders as itself, so unkeyed/dynamic strings still work.
        private static string LocText(string id) => LocalizeGet != null ? LocalizeGet(id) : id;

        private static void LocBind(Text t, string id)
        {
            if (t == null || string.IsNullOrEmpty(id)) return;
            BindLocalized?.Invoke(t, id);
        }

        // re-points an already-created Text at a new id (e.g. relabeling a stateful ON/OFF button)
        // so it keeps tracking language switches instead of the binding going stale.
        public static void RelabelText(Text t, string id)
        {
            if (t == null) return;
            t.text = LocText(id);
            LocBind(t, id);
        }

        // TMP_Text overload — no live-switch binding (that only targets UI.Text), just resolves
        // the id once at call time. good enough for the handful of TMP labels that get relabeled.
        public static void RelabelText(TMPro.TMP_Text t, string id)
        {
            if (t == null) return;
            string s = LocText(id);
            if (t.text != s) t.text = s;
        }

        // �� Label �������������������������������������������������������������
        public static Text CreateLabel(Transform parent, Rect rect, string text,
            int fontSize = 14, Color? color = null, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            SetPixelRect(go.AddComponent<RectTransform>(), rect);

            var t = go.AddComponent<Text>();
            t.text = LocText(text);
            Stylize(t);
            t.fontSize = fontSize;
            t.color = color ?? Color.white;
            t.alignment = anchor;
            t.raycastTarget = false;
            LocBind(t, text);
            return t;
        }

        // clickable URL/link label. shows `text` in `linkColor`, brightens to `hoverColor` on hover,
        // opens `url` on click (or runs onClick if given). transparent hit rect stretched behind the
        // text so the whole line is clickable. hover recolor is driven by a tiny watcher component so
        // callers don't have to poll it in their own Update.
        public static Text CreateLinkLabel(Transform parent, Rect rect, string text, string url,
            int fontSize = 15, Color? linkColor = null, Color? hoverColor = null, Action onClick = null,
            TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var link = linkColor ?? new Color(0.55f, 0.80f, 1.00f, 1f);
            var hover = hoverColor ?? new Color(0.25f, 0.50f, 0.90f, 1f);

            var hitGo = new GameObject("LinkHit");
            hitGo.transform.SetParent(parent, false);
            SetPixelRect(hitGo.AddComponent<RectTransform>(), rect);
            var hit = hitGo.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;

            var btn = hitGo.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = hit;
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            var capturedUrl = url;
            AddButtonClick(btn, onClick ?? (() => { if (!string.IsNullOrEmpty(capturedUrl)) Application.OpenURL(capturedUrl); }));

            var textGo = new GameObject("LinkText");
            textGo.transform.SetParent(hitGo.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero; textRt.anchorMax = Vector2.one;
            textRt.offsetMin = textRt.offsetMax = Vector2.zero;
            var t = textGo.AddComponent<Text>();
            t.text = LocText(text);
            Stylize(t);
            t.fontSize = fontSize;
            t.color = link;
            t.alignment = anchor;
            t.fontStyle = FontStyle.Bold;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.raycastTarget = false;
            LocBind(t, text);

            var w = hitGo.AddComponent<LinkHover>();
            w.Text = t; w.Idle = link; w.Hover = hover;
            return t;
        }

        // plain clickable text, no button background/sprite — brightens on hover (LinkHover),
        // click sound + action on click. for "this setting moved, click here" style references that
        // shouldn't look like a button.
        public static Text CreateLinkText(Transform parent, Rect rect, string label,
            Action onClick, Color? idle = null, int fontSize = 11, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var idleColor = idle ?? new Color(0.4f, 0.7f, 1f, 1f);
            var go = new GameObject("Link_" + label);
            go.transform.SetParent(parent, false);
            SetPixelRect(go.AddComponent<RectTransform>(), rect);

            var hitGo = new GameObject("Hit");
            hitGo.transform.SetParent(go.transform, false);
            var hitRt = hitGo.AddComponent<RectTransform>();
            hitRt.anchorMin = Vector2.zero; hitRt.anchorMax = Vector2.one;
            hitRt.offsetMin = hitRt.offsetMax = Vector2.zero;
            var hit = hitGo.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;

            var text = go.AddComponent<Text>();
            text.text = LocText(label);
            text.fontSize = fontSize;
            text.color = idleColor;
            text.alignment = align;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            Stylize(text);
            LocBind(text, label);

            var hover = go.AddComponent<LinkHover>();
            hover.Text = text;
            hover.Idle = idleColor;
            hover.Hover = Color.white;

            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = hit;
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;
            AddButtonClick(btn, onClick);

            return text;
        }

        public static TMPro.TextMeshProUGUI ReplaceTextWithTmp(Text src, string text, TMPro.TMP_FontAsset font)
        {
            if (src == null || font == null) return null;

            var keep = src.color;
            if (keep.a <= 0.001f) keep.a = 1f;
            var clear = new Color(keep.r, keep.g, keep.b, 0f);

            var existing = src.transform.Find("TmpLabel");
            if (existing != null)
            {
                var already = existing.GetComponent<TMPro.TextMeshProUGUI>();
                if (already != null)
                {
                    already.font = font;
                    already.text = text;
                    already.color = keep;
                    existing.gameObject.SetActive(true);
                    src.color = clear;
                    return already;
                }
            }

            var align = src.alignment;
            src.color = clear;

            var tmpGo = new GameObject("TmpLabel");
            tmpGo.transform.SetParent(src.gameObject.transform, false);
            var trt = tmpGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var tmp = tmpGo.AddComponent<TMPro.TextMeshProUGUI>();
            tmp.font = font;
            tmp.text = text;
            tmp.fontSize = src.fontSize + 2; // TMP looks slightly smaller at the same px
            tmp.color = keep;
            tmp.raycastTarget = false;
            tmp.alignment = align == TextAnchor.MiddleRight ? TMPro.TextAlignmentOptions.MidlineRight
                : align == TextAnchor.MiddleCenter ? TMPro.TextAlignmentOptions.Center
                : TMPro.TextAlignmentOptions.MidlineLeft;
            // never let the font sweep replace these previews with the user's chosen font.
            ProtectFont(tmp);
            return tmp;
        }

        public const int ROW_MAX_LINES = 3;

        // grows a row's LayoutElement height (and the label's own rect) to fit however many wrapped
        // lines the text actually needs, instead of clipping against the row above/below it
        public static void FitWrappedRow(LayoutElement le, Text lbl, float x, float availW, float rowH, int maxLines = ROW_MAX_LINES)
        {
            lbl.horizontalOverflow = HorizontalWrapMode.Wrap;
            lbl.verticalOverflow = VerticalWrapMode.Overflow;

            float wide = PreferredWidth(lbl);
            if (wide <= availW || availW <= 0f) return;

            float single = PreferredHeight(lbl, 100000f);
            float wrapped = PreferredHeight(lbl, availW);
            float pad = Mathf.Max(0f, rowH - single);
            float want = Mathf.Min(wrapped, single * maxLines) + pad;
            le.preferredHeight = want;
            SetPixelRect(lbl.rectTransform, new Rect(x, 0f, availW, want));
        }

        // �� Flow label (inside vertical layout groups) ������������������������
        public static Text CreateFlowLabel(Transform parent, string text, int fontSize, Color color, bool multiline = false)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var le = go.AddComponent<LayoutElement>();
            var t = go.AddComponent<Text>();
            t.text = LocText(text);
            Stylize(t);
            t.fontSize = fontSize;
            t.color = color;
            t.raycastTarget = false;
            LocBind(t, text);
            if (multiline)
            {
                // grow the rect downward to fit every wrapped line instead of clipping to one
                le.minHeight = fontSize + 2f;
                t.alignment = TextAnchor.UpperLeft;
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Overflow;
            }
            else
            {
                le.preferredHeight = fontSize + 2f;
                t.alignment = TextAnchor.MiddleLeft;
            }
            return t;
        }

        // �� Stretch label (anchored fill, centered) ���������������������������
        public static Text CreateStretchLabel(Transform parent, string text, int fontSize, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<Text>();
            t.text = LocText(text);
            Stylize(t);
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            LocBind(t, text);
            return t;
        }
    }
}
