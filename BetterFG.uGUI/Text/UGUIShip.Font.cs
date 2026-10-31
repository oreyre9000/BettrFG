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
                if (m.shadow != null) m.shadow.font = newFont;
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
                    var stock = TMPro.TMP_Settings.defaultFontAsset;
                    if (string.IsNullOrEmpty(UIFontResource))
                    {
                        if (stock == null) { _uiFontTried = false; return null; }
                        _uiFont = UnityEngine.Object.Instantiate(stock);
                        _uiFont.hideFlags = HideFlags.HideAndDontSave;
                        _uiFont.name = "BFG_UIFont_Default";
                        var latin = BuildEmbeddedFont(AvailableFonts[0].Resource);
                        if (latin != null) AddFallback(_uiFont, latin);
                        Log.LogInfo("UI font ready: TMP default (" + stock.name + ") with Arial fallback");
                        return _uiFont;
                    }
                    _uiFont = BuildEmbeddedFont(UIFontResource);
                    if (_uiFont == null) return null;
                    if (stock != null) AddFallback(_uiFont, stock);
                    Log.LogInfo("UI font ready: BFG_UIFont from " + System.IO.Path.GetFileName(UIFontResource));
                }
                catch (Exception ex) { Log.LogWarning("UI font build failed: " + ex.Message); }
                return _uiFont;
            }
        }

        private static void AddFallback(TMPro.TMP_FontAsset fa, TMPro.TMP_FontAsset fallback)
        {
            if (fa.fallbackFontAssetTable == null) fa.fallbackFontAssetTable = new();
            fa.fallbackFontAssetTable.Add(fallback);
        }

        private static TMPro.TMP_FontAsset BuildEmbeddedFont(string resource)
        {
            using var s = ResourceAssembly.GetManifestResourceStream(resource);
            if (s == null) { Log.LogWarning("UI font: no embedded resource " + resource); return null; }
            var bytes = new byte[s.Length];
            s.Read(bytes, 0, bytes.Length);
            if (bytes.Length > 1 && bytes[0] == 0x1f && bytes[1] == 0x8b)
            {
                using var ms = new MemoryStream();
                using (var z = new System.IO.Compression.GZipStream(new MemoryStream(bytes), System.IO.Compression.CompressionMode.Decompress)) z.CopyTo(ms);
                bytes = ms.ToArray();
            }
            var ttfPath = Path.Combine(Application.temporaryCachePath, "bfg_" + Path.GetFileName(resource));
            File.WriteAllBytes(ttfPath, bytes);
            var fa = TMPro.TMP_FontAsset.CreateFontAsset(ttfPath, 0, 90, 9,
                UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024,
                TMPro.AtlasPopulationMode.Dynamic, false);
            if (fa == null) { Log.LogWarning("UI font: CreateFontAsset came back null"); return null; }
            fa.hideFlags = HideFlags.HideAndDontSave;
            fa.name = "BFG_UIFont";
            var warm = new System.Text.StringBuilder();
            for (int c = 0x20; c <= 0x7E; c++) warm.Append((char)c);
            try { fa.TryAddCharacters(warm.ToString(), false); } catch { }
            return fa;
        }

        private sealed class TmpMirror
        {
            public Text src;
            public TMPro.TextMeshProUGUI dst;
            public TMPro.TextMeshProUGUI shadow;
            public string lastText;
            public Color lastColor = new Color(-1f, -1f, -1f, -1f);
            public int lastSize = -1;
            public TextAnchor lastAnchor = (TextAnchor)(-1);
            public FontStyle lastStyle = (FontStyle)(-1);
            public int lastRich = -1;
            public bool lastEnabled = true;
        }
        private static readonly System.Collections.Generic.List<TmpMirror> _mirrors = new System.Collections.Generic.List<TmpMirror>();
        private static readonly System.Collections.Generic.List<TmpMirror> _enabledPolled = new System.Collections.Generic.List<TmpMirror>();
        private static readonly System.Collections.Generic.List<Text> _pendingStylize = new System.Collections.Generic.List<Text>();

        private static Material _hiddenText;
        private static Material HiddenText
        {
            get
            {
                if (_hiddenText != null) return _hiddenText;
                _hiddenText = new Material(Graphic.defaultGraphicMaterial);
                _hiddenText.name = "BFG_HiddenText";
                _hiddenText.hideFlags = HideFlags.HideAndDontSave;
                _hiddenText.color = new Color(1f, 1f, 1f, 0f);
                return _hiddenText;
            }
        }

        public static void PollEnabled(Text t)
        {
            for (int i = _mirrors.Count - 1; i >= 0; i--)
                if (_mirrors[i].src == t) { _enabledPolled.Add(_mirrors[i]); return; }
        }

        public static void Stylize(Text t)
        {
            if (t == null) return;
            t.font = Arial;
            var font = UIFont;
            if (font == null) { _pendingStylize.Add(t); return; }
            if (t.transform.Find("TmpMirror") != null) return;

            t.material = HiddenText;

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

            var shGo = UnityEngine.Object.Instantiate(go, t.transform);
            shGo.name = "TmpShadow";
            shGo.transform.SetSiblingIndex(go.transform.GetSiblingIndex());
            var shRt = shGo.GetComponent<RectTransform>();
            shRt.offsetMin = shRt.offsetMax = new Vector2(0f, UpShadowY);
            var sd = shGo.GetComponent<TMPro.TextMeshProUGUI>();
            sd.color = new Color(UpShadowColor.r, UpShadowColor.g, UpShadowColor.b, UpShadowColor.a * t.color.a);

            var mirror = new TmpMirror
            {
                src = t, dst = d, shadow = sd,
                lastText = t.text, lastColor = t.color, lastSize = t.fontSize,
                lastAnchor = t.alignment, lastStyle = t.fontStyle,
                lastRich = t.supportRichText ? 1 : 0, lastEnabled = t.enabled,
            };
            if ((_mirrors.Count & 255) == 255) PruneMirrors();
            _mirrors.Add(mirror);
            ApplyFit(mirror);
            t.RegisterDirtyVerticesCallback(new Action(() => Sync(mirror)));
        }

        private static void ApplyFit(TmpMirror m)
        {
            var s = m.src;
            var rect = s.rectTransform.rect;
            var host = s.transform.parent;
            bool inButton = host != null && host.GetComponent<Button>() != null && s.text.IndexOf('\n') < 0;
            bool wrap = !inButton && rect.height >= s.fontSize * 2.2f && s.horizontalOverflow == HorizontalWrapMode.Wrap;
            bool fit = false;
            if (s.verticalOverflow == VerticalWrapMode.Truncate && rect.width > 0f && rect.height > 0f && !string.IsNullOrEmpty(s.text))
            {
                var probe = m.dst;
                probe.enableAutoSizing = false;
                probe.fontSize = s.fontSize;
                var need = probe.GetPreferredValues(s.text, wrap ? rect.width : 100000f, 0f);
                fit = wrap ? need.y > rect.height + s.fontSize * 0.5f : need.x > rect.width + 0.5f;
            }
            for (int i = 0; i < 2; i++)
            {
                var d = i == 0 ? m.dst : m.shadow;
                if (d == null) continue;
                d.enableAutoSizing = fit;
                if (fit)
                {
                    d.fontSizeMax = s.fontSize;
                    d.fontSizeMin = Mathf.Max(6f, s.fontSize * 0.5f);
                    d.enableWordWrapping = wrap;
                }
                else
                {
                    d.fontSize = s.fontSize;
                    d.enableWordWrapping = s.horizontalOverflow == HorizontalWrapMode.Wrap;
                }
            }
        }

        private static void PruneMirrors()
        {
            for (int i = _mirrors.Count - 1; i >= 0; i--)
                if (_mirrors[i].src == null || _mirrors[i].dst == null) _mirrors.RemoveAt(i);
        }

        private static void Sync(TmpMirror m)
        {
            var s = m.src;
            var sh = m.shadow;
            if (s.enabled != m.lastEnabled) { m.dst.enabled = s.enabled; if (sh != null) sh.enabled = s.enabled; m.lastEnabled = s.enabled; }
            var text = s.text;
            if (!string.Equals(text, m.lastText, StringComparison.Ordinal)) { m.dst.text = text; if (sh != null) sh.text = text; m.lastText = text; }
            var color = s.color;
            if (color != m.lastColor) { m.dst.color = color; if (sh != null) sh.color = new Color(UpShadowColor.r, UpShadowColor.g, UpShadowColor.b, UpShadowColor.a * color.a); m.lastColor = color; }
            int size = s.fontSize;
            if (size != m.lastSize) { m.dst.fontSize = size; if (sh != null) sh.fontSize = size; m.lastSize = size; }
            var anchor = s.alignment;
            if (anchor != m.lastAnchor) { m.dst.alignment = MapAlign(anchor); if (sh != null) sh.alignment = m.dst.alignment; m.lastAnchor = anchor; }
            var style = s.fontStyle;
            if (style != m.lastStyle) { m.dst.fontStyle = MapStyle(style); if (sh != null) sh.fontStyle = m.dst.fontStyle; m.lastStyle = style; }
            int rich = s.supportRichText ? 1 : 0;
            if (rich != m.lastRich) { m.dst.richText = rich == 1; if (sh != null) sh.richText = rich == 1; m.lastRich = rich; }
            ApplyFit(m);
        }

        public static readonly Color UpShadowColor = new Color(0f, 0f, 0f, 0.8f);
        public const float UpShadowY = 1.6f;

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
                if (_mirrors[i].shadow != null) UnityEngine.Object.Destroy(_mirrors[i].shadow.gameObject);
                _mirrors.RemoveAt(i);
            }
            _pendingStylize.Remove(t);
            t.material = null;
            t.canvasRenderer.SetColor(Color.white);
            if (t.GetComponent<Shadow>() == null)
            {
                var sh = t.gameObject.AddComponent<Shadow>();
                sh.effectColor = UpShadowColor;
                sh.effectDistance = new Vector2(0f, UpShadowY);
            }
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

            RowLabelFit.Pump();
            LinkHover.Pump();
            for (int i = _enabledPolled.Count - 1; i >= 0; i--)
            {
                var m = _enabledPolled[i];
                if (m.src == null || m.dst == null) { _enabledPolled.RemoveAt(i); continue; }
                if (m.src.enabled != m.lastEnabled) Sync(m);
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
