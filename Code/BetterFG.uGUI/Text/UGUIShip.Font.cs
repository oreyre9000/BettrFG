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
        // ── one knob for the whole mod UI font ───────────────────────────────
        // this game's IL2CPP build strips Font.CreateDynamicFontFromOSFont, so a legacy UI.Text can't
        // take a ttf at runtime. instead every mod label keeps its (invisible) legacy Text as the
        // layout/measure driver and gets a TextMeshProUGUI child that mirrors it, rendered in a
        // TMP_FontAsset built from the embedded ttf. swap the font = drop a ttf in assets/ui/general/
        // and repoint UIFontResource.
        public static string UIFontResource = null;

        public static readonly (string Code, string DisplayName, string Resource)[] AvailableFonts =
        {
            ("arial",       "Arial",        "BetterFG.assets.ui.general.arial.ttf"),
            ("charter",     "Charter Bold", "BetterFG.assets.ui.general.CharterBold.ttf"),
            ("tmp_default", "TMP Default",  null),
        };

        public static string SelectedFontCode { get; private set; } = "tmp_default";

        public static void SetFontByCode(string code, bool resolveNow = true)
        {
            int idx = 0;
            for (int i = 0; i < AvailableFonts.Length; i++)
                if (AvailableFonts[i].Code == code) { idx = i; break; }
            var (c, _, res) = AvailableFonts[idx];
            if (c == SelectedFontCode && _uiFontTried) return;
            SelectedFontCode = c;
            UIFontResource = res;
            _uiFont = null;
            _uiFontTried = false;
            if (!resolveNow) return;

            var newFont = UIFont;
            if (newFont == null && c != "arial")
            {
                Log.LogWarning("UI font: '" + c + "' didn't resolve, falling back to Arial so the mod stays readable");
                UIFontResource = "BetterFG.assets.ui.general.arial.ttf";
                _uiFontTried = false;
                newFont = UIFont;
            }
            if (newFont == null) return;
            for (int i = _mirrors.Count - 1; i >= 0; i--)
            {
                var m = _mirrors[i];
                if (m.dst == null || m.src == null) { _mirrors.RemoveAt(i); continue; }
                m.dst.font = newFont;
            }
        }

        private static Font _arial;
        private static Font Arial => _arial != null ? _arial : (_arial = Resources.GetBuiltinResource<Font>("Arial.ttf"));

        private static bool _uiFontTried;
        private static TMPro.TMP_FontAsset _uiFont;
        public static TMPro.TMP_FontAsset UIFont
        {
            get
            {
                if (_uiFont != null || _uiFontTried) return _uiFont;
                _uiFontTried = true;
                try
                {
                    if (string.IsNullOrEmpty(UIFontResource))
                    {
                        _uiFont = TMPro.TMP_Settings.defaultFontAsset;
                        if (_uiFont == null) _uiFontTried = false;
                        else Log.LogInfo("UI font ready: TMP default (" + _uiFont.name + ")");
                        return _uiFont;
                    }
                    using var s = ResourceAssembly.GetManifestResourceStream(UIFontResource);
                    if (s == null) { Log.LogWarning("UI font: no embedded resource " + UIFontResource); return null; }
                    var bytes = new byte[s.Length];
                    s.Read(bytes, 0, bytes.Length);
                    var ttfPath = Path.Combine(Application.temporaryCachePath, "bfg_" + Path.GetFileName(UIFontResource));
                    File.WriteAllBytes(ttfPath, bytes);
                    var source = Path.GetFileName(UIFontResource);
                    var fa = TMPro.TMP_FontAsset.CreateFontAsset(ttfPath, 0, 90, 9,
                        UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024,
                        TMPro.AtlasPopulationMode.Dynamic, false);
                    if (fa == null) { Log.LogWarning("UI font: CreateFontAsset came back null"); return null; }
                    fa.hideFlags = HideFlags.HideAndDontSave;
                    fa.name = "BFG_UIFont";
                    var warm = new System.Text.StringBuilder();
                    for (int c = 0x20; c <= 0x7E; c++) warm.Append((char)c);
                    try { fa.TryAddCharacters(warm.ToString(), false); } catch { }
                    _uiFont = fa;
                    Log.LogInfo("UI font ready: BFG_UIFont from " + source);
                }
                catch (Exception ex) { Log.LogWarning("UI font build failed: " + ex.Message); }
                return _uiFont;
            }
        }

        private sealed class TmpMirror
        {
            public Text src;
            public TMPro.TextMeshProUGUI dst;
            public string lastText;
            public Color lastColor = new Color(-1f, -1f, -1f, -1f);
            public int lastSize = -1;
            public TextAnchor lastAnchor = (TextAnchor)(-1);
            public FontStyle lastStyle = (FontStyle)(-1);
            public int lastRich = -1;
            public bool lastEnabled = true;
        }
        private static readonly System.Collections.Generic.List<TmpMirror> _mirrors = new System.Collections.Generic.List<TmpMirror>();

        private static readonly System.Collections.Generic.List<Text> _pendingStylize = new System.Collections.Generic.List<Text>();

        // give a freshly built legacy Text the mod font: keep it as the (invisible) measure/layout
        // driver, overlay a TMP child that tracks its text/colour/size/alignment every frame.
        public static void Stylize(Text t)
        {
            if (t == null) return;
            t.font = Arial;
            var font = UIFont;
            if (font == null) { _pendingStylize.Add(t); return; }
            if (t.transform.Find("TmpMirror") != null) return;

            t.canvasRenderer.SetColor(new Color(1f, 1f, 1f, 0f));
            // a SetActive(false)->true cycle (tab/window open-close, list rebuilds) resets the
            // CanvasRenderer colour back to opaque, so the Arial glyphs reappear offset behind the
            // TMP mirror ("ghost text"). re-assert the invisible multiplier on every re-enable.
            if (t.gameObject.GetComponent<StylizeGuard>() == null) t.gameObject.AddComponent<StylizeGuard>();

            var go = new GameObject("TmpMirror");
            go.SetActive(false);
            go.transform.SetParent(t.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var d = go.AddComponent<TMPro.TextMeshProUGUI>();
            d.font = font;
            d.raycastTarget = false;
            d.richText = t.supportRichText;
            d.enableWordWrapping = t.horizontalOverflow == HorizontalWrapMode.Wrap;
            d.overflowMode = TMPro.TextOverflowModes.Overflow;
            d.text = t.text;
            d.color = t.color;
            d.fontSize = t.fontSize;
            d.alignment = MapAlign(t.alignment);
            d.fontStyle = MapStyle(t.fontStyle);
            go.SetActive(true);

            _mirrors.Add(new TmpMirror
            {
                src = t, dst = d,
                lastText = t.text, lastColor = t.color, lastSize = t.fontSize,
                lastAnchor = t.alignment, lastStyle = t.fontStyle,
                lastRich = t.supportRichText ? 1 : 0, lastEnabled = t.enabled,
            });
        }

        // Text.preferredHeight measures in Arial, but a stylized label is DRAWN by its TMP mirror in
        // the mod font, which is usually wider and wraps to more lines. anything sizing a panel around
        // wrapped text has to ask the mirror or it lays out for fewer lines than the player sees.
        public static float PreferredHeight(Text t, float width)
        {
            if (t == null) return 0f;
            var mirror = t.transform.Find("TmpMirror");
            var tmp = mirror != null ? mirror.GetComponent<TMPro.TextMeshProUGUI>() : null;
            if (tmp == null) return t.preferredHeight;
            return tmp.GetPreferredValues(t.text, width, 0f).y;
        }

        // same trap as PreferredHeight, on the other axis: the legacy Text measures Arial while the
        // player sees the mod font, so anything sizing a panel to fit one line has to ask the mirror
        public static float PreferredWidth(Text t)
        {
            if (t == null) return 0f;
            var mirror = t.transform.Find("TmpMirror");
            var tmp = mirror != null ? mirror.GetComponent<TMPro.TextMeshProUGUI>() : null;
            if (tmp == null) return t.preferredWidth;
            tmp.fontSize = t.fontSize;
            return tmp.GetPreferredValues(t.text).x;
        }

        // opt a label back out of the mod font — drop its TMP mirror and let the legacy Arial Text
        // render again. for the few surfaces that should stay Arial (tab titles).
        public static void Unstylize(Text t)
        {
            if (t == null) return;
            for (int i = _mirrors.Count - 1; i >= 0; i--)
            {
                if (_mirrors[i].src != t) continue;
                if (_mirrors[i].dst != null) UnityEngine.Object.Destroy(_mirrors[i].dst.gameObject);
                _mirrors.RemoveAt(i);
            }
            _pendingStylize.Remove(t);
            var guard = t.gameObject.GetComponent<StylizeGuard>();
            if (guard != null) UnityEngine.Object.Destroy(guard);
            t.canvasRenderer.SetColor(Color.white);
        }

        // pumped once per frame from BetterFGUIMan.Update
        public static void PumpTextMirrors()
        {
            if (_pendingStylize.Count > 0 && UIFont != null)
            {
                var late = _pendingStylize.ToArray();
                _pendingStylize.Clear();
                for (int i = 0; i < late.Length; i++)
                    if (late[i] != null) Stylize(late[i]);
            }

            for (int i = _mirrors.Count - 1; i >= 0; i--)
            {
                var m = _mirrors[i];
                if (m.src == null || m.dst == null) { _mirrors.RemoveAt(i); continue; }
                var s = m.src;

                // InputField hides its placeholder by disabling the Text component, not the
                // GameObject (SetActive) — mirror that or the TMP overlay keeps showing stale
                // placeholder text on top of what's actually typed.
                if (s.enabled != m.lastEnabled) { m.dst.enabled = s.enabled; m.lastEnabled = s.enabled; }
                if (!string.Equals(s.text, m.lastText, StringComparison.Ordinal)) { m.dst.text = s.text; m.lastText = s.text; }
                if (s.color != m.lastColor) { m.dst.color = s.color; m.lastColor = s.color; }
                if (s.fontSize != m.lastSize) { m.dst.fontSize = s.fontSize; m.lastSize = s.fontSize; }
                if (s.alignment != m.lastAnchor) { m.dst.alignment = MapAlign(s.alignment); m.lastAnchor = s.alignment; }
                if (s.fontStyle != m.lastStyle) { m.dst.fontStyle = MapStyle(s.fontStyle); m.lastStyle = s.fontStyle; }
                int rich = s.supportRichText ? 1 : 0;
                if (rich != m.lastRich) { m.dst.richText = s.supportRichText; m.lastRich = rich; }
            }
        }

        private static TMPro.TextAlignmentOptions MapAlign(TextAnchor a) => a switch
        {
            TextAnchor.UpperLeft => TMPro.TextAlignmentOptions.TopLeft,
            TextAnchor.UpperCenter => TMPro.TextAlignmentOptions.Top,
            TextAnchor.UpperRight => TMPro.TextAlignmentOptions.TopRight,
            TextAnchor.MiddleLeft => TMPro.TextAlignmentOptions.Left,
            TextAnchor.MiddleCenter => TMPro.TextAlignmentOptions.Center,
            TextAnchor.MiddleRight => TMPro.TextAlignmentOptions.Right,
            TextAnchor.LowerLeft => TMPro.TextAlignmentOptions.BottomLeft,
            TextAnchor.LowerCenter => TMPro.TextAlignmentOptions.Bottom,
            TextAnchor.LowerRight => TMPro.TextAlignmentOptions.BottomRight,
            _ => TMPro.TextAlignmentOptions.Left,
        };

        private static TMPro.FontStyles MapStyle(FontStyle s) => s switch
        {
            FontStyle.Bold => TMPro.FontStyles.Bold,
            FontStyle.Italic => TMPro.FontStyles.Italic,
            FontStyle.BoldAndItalic => TMPro.FontStyles.Bold | TMPro.FontStyles.Italic,
            _ => TMPro.FontStyles.Normal,
        };
    }
}
