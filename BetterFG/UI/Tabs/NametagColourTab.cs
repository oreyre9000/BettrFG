using System;
using System.Collections.Generic;
using BetterFG.Services;
using BetterFG.Nametag;
using BetterFG.Customization.UI;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class NametagColourTab : NametagWizardTab
    {
        public NametagColourTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Nametag - Colour";
        protected override string TitleId => "ui.nametag_colour";
        protected override string BgResource => "BetterFG.assets.ui.nametag.bg.png";

        private const string KEY_COLOR_R = "nametag.color.r";
        private const string KEY_COLOR_G = "nametag.color.g";
        private const string KEY_COLOR_B = "nametag.color.b";
        private const string KEY_BOLD = "nametag.bold";
        private const string KEY_ITALIC = "nametag.italic";
        private const string KEY_ENABLED = "nametag.enabled";
        private const string KEY_NAME_STYLE = "nametag.namestyle";
        private const string KEY_GRADIENT_COLORS = "nametag.gradient.colors";
        private const string KEY_GRADIENT_ANGLE = "nametag.gradient.angle";
        private const string KEY_OUTLINE_ENABLED = "nametag.outline.enabled";
        private const string KEY_OUTLINE_R = "nametag.outline.r";
        private const string KEY_OUTLINE_G = "nametag.outline.g";
        private const string KEY_OUTLINE_B = "nametag.outline.b";
        private const string KEY_OUTLINE_WIDTH = "nametag.outline.width";
        private const string KEY_OUTLINE_MODE = "nametag.outline.mode";
        private const string KEY_GHOST_NAMETAG = "nametag.ghost.enabled";

        private static readonly Color WHITE2 = Color.white;
        private static readonly Color BTN_DARK2 = new Color(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Color MARKER_SEL = new Color(1f, 1f, 1f, 1f);
        private static readonly Color MARKER_IDLE = new Color(0f, 0f, 0f, 1f);

        private enum NameStyle { None, Default, Gold, GoldColored, Gradient }

        private float _r = 1f, _g = 1f, _b = 1f;
        private bool _bold, _italic;
        private NameStyle _nameStyle = NameStyle.Default;
        private bool _ghostNametag;
        private List<Color> _gradient = new List<Color>();
        private float _gradientAngle;
        private int _selectedStop;
        private bool _outlineEnabled;
        private float _outlineR, _outlineG, _outlineB;
        private float _outlineWidth = 0.2f;
        private string _outlineMode = "outset";
        private Text _outlineToggleLabel;
        private RectTransform _outlineContent;
        private float _outlineContentWidth;

        private Text _boldBtnLabel, _italicBtnLabel, _ghostNametagBtnLabel;
        private Text _styleCarouselLabel, _styleHintLabel;
        private RectTransform _colourContent;
        private float _colourWidth;
        private Image _previewImage;
        private Texture2D _previewTex;

        private static readonly NameStyle[] STYLE_ORDER = { NameStyle.None, NameStyle.Default, NameStyle.Gold, NameStyle.GoldColored, NameStyle.Gradient };
        private static readonly string[] STYLE_LOC_KEYS = { "ui.none_2", "ui.default", "ui.gold", "ui.gold_rgb", "nametag_gradient.style" };
        private static readonly string[] STYLE_HINT_KEYS = { "nametag_style.hint_none", "nametag_style.hint_default", "nametag_style.hint_gold", "nametag_style.hint_goldrgb", "nametag_style.hint_gradient" };

        protected override string[] StepTitles => new[] { "ui.preset_style", "ui.text_colour", "nametag_outline.step_title", "ui.bold_italic", "ui.ghost_nametag" };
        protected override bool HasRemove => true;
        protected override Tab MakeListTarget() => BetterFGTabRegistry.NewTab<NametagTab>();

        void Awake()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            _r = float.TryParse(SettingsService.Get(KEY_COLOR_R, "1"), System.Globalization.NumberStyles.Float, ci, out float r) ? r : 1f;
            _g = float.TryParse(SettingsService.Get(KEY_COLOR_G, "1"), System.Globalization.NumberStyles.Float, ci, out float g) ? g : 1f;
            _b = float.TryParse(SettingsService.Get(KEY_COLOR_B, "1"), System.Globalization.NumberStyles.Float, ci, out float b) ? b : 1f;
            _bold = SettingsService.Get(KEY_BOLD, "false") == "true";
            _italic = SettingsService.Get(KEY_ITALIC, "false") == "true";
            string styleStr = SettingsService.Get(KEY_NAME_STYLE, "default");
            _nameStyle = styleStr == "none" ? NameStyle.None
                       : styleStr == "gold" ? NameStyle.Gold
                       : styleStr == "goldcolored" ? NameStyle.GoldColored
                       : styleStr == "gradient" ? NameStyle.Gradient
                       : NameStyle.Default;
            _gradient = NametagIconApplicator.ParseGradientColors(SettingsService.Get(KEY_GRADIENT_COLORS, ""));
            _gradientAngle = float.TryParse(SettingsService.Get(KEY_GRADIENT_ANGLE, "0"), System.Globalization.NumberStyles.Float, ci, out float ga) ? ga : 0f;
            if (_gradient.Count == 0) _gradient.Add(new Color(_r, _g, _b, 1f));
            _selectedStop = 0;
            _outlineEnabled = SettingsService.Get(KEY_OUTLINE_ENABLED, "false") == "true";
            _outlineR = float.TryParse(SettingsService.Get(KEY_OUTLINE_R, "0"), System.Globalization.NumberStyles.Float, ci, out float orv) ? orv : 0f;
            _outlineG = float.TryParse(SettingsService.Get(KEY_OUTLINE_G, "0"), System.Globalization.NumberStyles.Float, ci, out float ogv) ? ogv : 0f;
            _outlineB = float.TryParse(SettingsService.Get(KEY_OUTLINE_B, "0"), System.Globalization.NumberStyles.Float, ci, out float obv) ? obv : 0f;
            _outlineWidth = float.TryParse(SettingsService.Get(KEY_OUTLINE_WIDTH, "0.2"), System.Globalization.NumberStyles.Float, ci, out float owv) ? owv : 0.2f;
            _outlineMode = SettingsService.Get(KEY_OUTLINE_MODE, "outset");
            _ghostNametag = SettingsService.Get(KEY_GHOST_NAMETAG, "false") == "true";
        }

        protected override void BuildStep(int step, RectTransform root, float w, float bodyH)
        {
            var (_, c) = UGUIShip.CreateScrollView(root, new Rect(0f, 0f, TabWidth, bodyH));
            float cw = w - 26f;
            switch (step)
            {
                case 0: BuildStyleStep(c, cw); break;
                case 1: BuildColourStep(c, cw); break;
                case 2: BuildOutlineStep(c, cw); break;
                case 3: BuildWeightStep(c, cw); break;
                case 4: BuildGhostStep(c, cw); break;
            }
        }

        private void BuildStyleStep(RectTransform c, float w)
        {
            float x = PAD, cy = PAD;
            UGUIShip.CreateLabel(c, new Rect(x, cy, w, LH), "ui.predefined_style", FS_SM, HINT);
            cy += LH + SH;
            int current = System.Array.IndexOf(STYLE_ORDER, _nameStyle);
            if (current < 0) current = 1;
            _styleCarouselLabel = UGUIShip.CreateCarousel(c, new Rect(x, cy, w, BTN_H), STYLE_LOC_KEYS, current,
                new Action<int>(StepStyle), BTN_DARK2, FS_SM);
            cy += BTN_H + SH;
            _styleHintLabel = UGUIShip.CreateLabel(c, new Rect(x, cy, w, LH * 2f), STYLE_HINT_KEYS[current], FS_SM, HINT, TextAnchor.UpperLeft);
            cy += LH * 2f + PAD;
            c.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void StepStyle(int delta)
        {
            int i = System.Array.IndexOf(STYLE_ORDER, _nameStyle);
            if (i < 0) i = 1;
            i = ((i + delta) % STYLE_ORDER.Length + STYLE_ORDER.Length) % STYLE_ORDER.Length;
            _nameStyle = STYLE_ORDER[i];
            if (_styleCarouselLabel != null) UGUIShip.RelabelText(_styleCarouselLabel, STYLE_LOC_KEYS[i]);
            if (_styleHintLabel != null) UGUIShip.RelabelText(_styleHintLabel, STYLE_HINT_KEYS[i]);
            if (_nameStyle == NameStyle.Gradient && _gradient.Count == 0)
                _gradient.Add(new Color(_r, _g, _b, 1f));
            RebuildColourStep();
            RefreshPreview();
        }

        private void BuildColourStep(RectTransform c, float w)
        {
            _colourContent = c;
            _colourWidth = w;
            RebuildColourStep();
        }

        private void RebuildColourStep()
        {
            if (_colourContent == null) return;
            for (int i = _colourContent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_colourContent.GetChild(i).gameObject);
            _previewImage = null;

            float x = PAD, cy = PAD, w = _colourWidth;

            if (_nameStyle == NameStyle.Gold)
            {
                UGUIShip.CreateLabel(_colourContent, new Rect(x, cy, w, LH * 2f), "nametag_style.hint_gold", FS_SM, HINT, TextAnchor.UpperLeft);
                cy += LH * 2f + PAD;
                _colourContent.sizeDelta = new Vector2(0f, cy + PAD);
                return;
            }

            if (_nameStyle == NameStyle.Gradient)
            {
                BuildGradientEditor(x, ref cy, w);
                _colourContent.sizeDelta = new Vector2(0f, cy + PAD);
                return;
            }

            UGUIShip.CreateLabel(_colourContent, new Rect(x, cy, w, LH), "ui.colour", FS_SM, HINT);
            cy += LH + SH;
            UGUIShip.CreateColorControls(_colourContent, x, ref cy, w,
                () => _r, () => _g, () => _b,
                v => _r = v, v => _g = v, v => _b = v, () => RefreshPreview(),
                out _, out _, out _, Color.white);
            _colourContent.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void BuildGradientEditor(float x, ref float cy, float w)
        {
            const float STRIP_H = 36f;
            const float MARKER_SIZE = 22f;

            UGUIShip.CreateLabel(_colourContent, new Rect(x, cy, w, LH), "nametag_gradient.hint", FS_SM, HINT);
            cy += LH + SH * 2f;

            var stripGo = new GameObject("GradientStrip");
            stripGo.transform.SetParent(_colourContent, false);
            var stripRt = stripGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(stripRt, new Rect(x, cy, w, STRIP_H));
            _previewImage = stripGo.AddComponent<Image>();
            _previewImage.color = Color.white;
            UpdatePreviewTexture();
            cy += STRIP_H + SH;

            float markerRowY = cy;
            for (int i = 0; i < _gradient.Count; i++)
            {
                int index = i;
                float t = _gradient.Count == 1 ? 0.5f : (float)i / (_gradient.Count - 1);
                float mx = x + t * (w - MARKER_SIZE);
                var col = _gradient[i];
                bool selected = i == _selectedStop;
                var mBtn = UGUIShip.CreateButton(_colourContent, new Rect(mx, markerRowY, MARKER_SIZE, MARKER_SIZE), "",
                    col, selected ? MARKER_SEL : MARKER_IDLE, FS_SM, new Action(() =>
                    {
                        _selectedStop = index;
                        RebuildColourStep();
                    }));
                var outline = mBtn.gameObject.AddComponent<Outline>();
                outline.effectColor = selected ? MARKER_SEL : MARKER_IDLE;
                outline.effectDistance = new Vector2(selected ? 2f : 1f, selected ? -2f : -1f);
            }
            cy += MARKER_SIZE + PAD;

            float halfW = (w - PAD * 0.5f) / 2f;
            UGUIShip.CreateButton(_colourContent, new Rect(x, cy, halfW, BTN_H), "nametag_gradient.add",
                BTN_DARK2, WHITE2, FS_SM, new Action(() =>
                {
                    var seed = _gradient.Count > 0 ? _gradient[_gradient.Count - 1] : new Color(_r, _g, _b, 1f);
                    _gradient.Add(seed);
                    _selectedStop = _gradient.Count - 1;
                    RebuildColourStep();
                    RefreshPreview();
                }));
            bool canRemove = _gradient.Count > 1;
            var rmBtn = UGUIShip.CreateButton(_colourContent, new Rect(x + halfW + PAD * 0.5f, cy, halfW, BTN_H),
                "nametag_gradient.remove", new Color(0.55f, 0.15f, 0.15f, 1f), WHITE2, FS_SM, new Action(() =>
                {
                    if (_gradient.Count <= 1) return;
                    _gradient.RemoveAt(_selectedStop);
                    if (_selectedStop >= _gradient.Count) _selectedStop = _gradient.Count - 1;
                    RebuildColourStep();
                    RefreshPreview();
                }));
            rmBtn.interactable = canRemove;
            cy += BTN_H + PAD * 1.5f;

            var angleLbl = UGUIShip.CreateLabel(_colourContent, new Rect(x, cy, w, LH),
                LocalizationService.Get("nametag_gradient.angle") + "  " + Mathf.RoundToInt(_gradientAngle) + "°", FS_SM, HINT);
            cy += LH + SH;
            UGUIShip.CreateSlider(_colourContent, x, cy, w, "", _gradientAngle / 360f, LH, PAD, FS_SM,
                v =>
                {
                    _gradientAngle = Mathf.Round(v * 360f);
                    if (angleLbl != null) angleLbl.text = LocalizationService.Get("nametag_gradient.angle") + "  " + Mathf.RoundToInt(_gradientAngle) + "°";
                    UpdatePreviewTexture();
                    RefreshPreview();
                },
                null, new Color(0.7f, 0.7f, 0.7f, 1f), false, 0f);
            cy += LH + PAD * 1.5f;

            UGUIShip.CreateLabel(_colourContent, new Rect(x, cy, w, LH),
                LocalizationService.Get("nametag_gradient.editing_stop") + " " + (_selectedStop + 1), FS_SM, HINT);
            cy += LH + SH;
            int captured = _selectedStop;
            UGUIShip.CreateColorControls(_colourContent, x, ref cy, w,
                () => captured < _gradient.Count ? _gradient[captured].r : 0f,
                () => captured < _gradient.Count ? _gradient[captured].g : 0f,
                () => captured < _gradient.Count ? _gradient[captured].b : 0f,
                v => { if (captured < _gradient.Count) { var c = _gradient[captured]; c.r = v; _gradient[captured] = c; } },
                v => { if (captured < _gradient.Count) { var c = _gradient[captured]; c.g = v; _gradient[captured] = c; } },
                v => { if (captured < _gradient.Count) { var c = _gradient[captured]; c.b = v; _gradient[captured] = c; } },
                () => { UpdatePreviewTexture(); RefreshPreview(); },
                out _, out _, out _, Color.white);
        }

        private void UpdatePreviewTexture()
        {
            if (_previewImage == null) return;
            if (_previewTex == null)
            {
                _previewTex = new Texture2D(256, 1, TextureFormat.RGBA32, false);
                _previewTex.wrapMode = TextureWrapMode.Clamp;
                _previewTex.filterMode = FilterMode.Bilinear;
                _previewTex.hideFlags = HideFlags.HideAndDontSave;
            }
            var stops = _gradient.Count > 0 ? _gradient : new List<Color> { new Color(_r, _g, _b, 1f) };
            var pixels = new Color[256];
            for (int i = 0; i < 256; i++)
            {
                float t = i / 255f;
                pixels[i] = SampleStops(stops, t);
            }
            _previewTex.SetPixels(pixels);
            _previewTex.Apply();
            var spr = Sprite.Create(_previewTex, new UnityEngine.Rect(0, 0, 256, 1), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            spr.hideFlags = HideFlags.HideAndDontSave;
            _previewImage.sprite = spr;
        }

        private static Color SampleStops(List<Color> stops, float t)
        {
            if (stops.Count == 1) return stops[0];
            float pos = Mathf.Clamp01(t) * (stops.Count - 1);
            int i = (int)pos;
            if (i >= stops.Count - 1) return stops[stops.Count - 1];
            return Color.Lerp(stops[i], stops[i + 1], pos - i);
        }

        private void BuildWeightStep(RectTransform c, float w)
        {
            float x = PAD, cy = PAD;
            UGUIShip.CreateLabel(c, new Rect(x, cy, w, LH), "ui.style", FS_SM, HINT);
            cy += LH + SH;
            float togglew = (w - PAD * 0.5f) / 2f;
            var boldBtn = UGUIShip.CreateButton(c, new Rect(x, cy, togglew, BTN_H),
                _bold ? "ui.bold_on" : "ui.bold_off", BTN_DARK2, WHITE2, FS_SM, new Action(OnToggleBold));
            _boldBtnLabel = boldBtn.GetComponentInChildren<Text>();
            var italicBtn = UGUIShip.CreateButton(c, new Rect(x + togglew + PAD * 0.5f, cy, togglew, BTN_H),
                _italic ? "ui.italic_on" : "ui.italic_off", BTN_DARK2, WHITE2, FS_SM, new Action(OnToggleItalic));
            _italicBtnLabel = italicBtn.GetComponentInChildren<Text>();
            cy += BTN_H + PAD;
            c.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void BuildOutlineStep(RectTransform c, float w)
        {
            _outlineContent = c;
            _outlineContentWidth = w;
            RebuildOutlineStep();
        }

        private void RebuildOutlineStep()
        {
            if (_outlineContent == null) return;
            for (int i = _outlineContent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_outlineContent.GetChild(i).gameObject);

            float x = PAD, cy = PAD, w = _outlineContentWidth;
            UGUIShip.CreateLabel(_outlineContent, new Rect(x, cy, w, LH), "nametag_outline.hint", FS_SM, HINT);
            cy += LH + SH;
            var toggleBtn = UGUIShip.CreateButton(_outlineContent, new Rect(x, cy, w, BTN_H),
                _outlineEnabled ? "nametag_outline.on" : "nametag_outline.off",
                _outlineEnabled ? UGUIShip.TOGGLE_ON : UGUIShip.TOGGLE_OFF, WHITE2, FS_SM, new Action(() =>
                {
                    _outlineEnabled = !_outlineEnabled;
                    RebuildOutlineStep();
                    RefreshPreview();
                }));
            _outlineToggleLabel = toggleBtn.GetComponentInChildren<Text>();
            cy += BTN_H + PAD;

            if (!_outlineEnabled)
            {
                _outlineContent.sizeDelta = new Vector2(0f, cy + PAD);
                return;
            }

            UGUIShip.CreateLabel(_outlineContent, new Rect(x, cy, w, LH), "nametag_outline.mode", FS_SM, HINT);
            cy += LH + SH;
            string OutsetLbl() => LocalizationService.Get("nametag_outline.mode_outset");
            string InsetLbl() => LocalizationService.Get("nametag_outline.mode_inset");
            var modeOptions = new System.Collections.Generic.List<string> { OutsetLbl(), InsetLbl() };
            var modeInitial = new System.Collections.Generic.List<bool> { _outlineMode != "inset", _outlineMode == "inset" };
            Button modeDd = null;
            modeDd = UGUIShip.CreateMultiSelectDropdown(_outlineContent, new Rect(x, cy, w, BTN_H),
                _outlineMode == "inset" ? InsetLbl() : OutsetLbl(), modeOptions, modeInitial,
                new Action<int, bool>((idx, _) =>
                {
                    _outlineMode = idx == 1 ? "inset" : "outset";
                    var lbl = modeDd?.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, _outlineMode == "inset" ? InsetLbl() : OutsetLbl());
                    RefreshPreview();
                }), FS_SM, w, 20f, true, true);
            cy += BTN_H + PAD;

            UGUIShip.CreateLabel(_outlineContent, new Rect(x, cy, w, LH), "nametag_outline.colour", FS_SM, HINT);
            cy += LH + SH;
            UGUIShip.CreateColorControls(_outlineContent, x, ref cy, w,
                () => _outlineR, () => _outlineG, () => _outlineB,
                v => _outlineR = v, v => _outlineG = v, v => _outlineB = v, () => RefreshPreview(),
                out _, out _, out _, Color.black);
            cy += SH;

            var widthLbl = UGUIShip.CreateLabel(_outlineContent, new Rect(x, cy, w, LH),
                LocalizationService.Get("nametag_outline.width") + "  " + _outlineWidth.ToString("0.00"), FS_SM, HINT);
            cy += LH + SH;
            UGUIShip.CreateSlider(_outlineContent, x, cy, w, "", _outlineWidth, LH, PAD, FS_SM,
                v =>
                {
                    _outlineWidth = v;
                    if (widthLbl != null) widthLbl.text = LocalizationService.Get("nametag_outline.width") + "  " + _outlineWidth.ToString("0.00");
                    RefreshPreview();
                },
                null, new Color(0.7f, 0.7f, 0.7f, 1f), false, 0.2f);
            cy += LH + PAD;

            _outlineContent.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void BuildGhostStep(RectTransform c, float w)
        {
            float x = PAD, cy = PAD;
            UGUIShip.CreateLabel(c, new Rect(x, cy, w, LH), "ui.ghost", FS_SM, HINT);
            cy += LH + SH;
            var ghostBtn = UGUIShip.CreateButton(c, new Rect(x, cy, w, BTN_H),
                _ghostNametag ? "ui.show_ghost_nametag_on" : "ui.show_ghost_nametag_off",
                BTN_DARK2, WHITE2, FS_SM, new Action(OnToggleGhostNametag));
            _ghostNametagBtnLabel = ghostBtn.GetComponentInChildren<Text>();
            cy += BTN_H + PAD;
            c.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void OnToggleBold()
        {
            _bold = !_bold;
            if (_boldBtnLabel != null) UGUIShip.RelabelText(_boldBtnLabel, _bold ? "ui.bold_on" : "ui.bold_off");
            RefreshPreview();
        }

        private void OnToggleItalic()
        {
            _italic = !_italic;
            if (_italicBtnLabel != null) UGUIShip.RelabelText(_italicBtnLabel, _italic ? "ui.italic_on" : "ui.italic_off");
            RefreshPreview();
        }

        private void OnToggleGhostNametag()
        {
            _ghostNametag = !_ghostNametag;
            SettingsService.Set(KEY_GHOST_NAMETAG, _ghostNametag ? "true" : "false");
            if (_ghostNametagBtnLabel != null) UGUIShip.RelabelText(_ghostNametagBtnLabel, _ghostNametag ? "ui.show_ghost_nametag_on" : "ui.show_ghost_nametag_off");
        }

        private string StyleString() =>
            _nameStyle == NameStyle.None ? "none"
            : _nameStyle == NameStyle.Gold ? "gold"
            : _nameStyle == NameStyle.GoldColored ? "goldcolored"
            : _nameStyle == NameStyle.Gradient ? "gradient"
            : "default";

        public override void RefreshPreview()
        {
            var cfg = NametagIconApplicator.CfgFromSettings();
            cfg.enabled = true;
            cfg.r = _r; cfg.g = _g; cfg.b = _b;
            cfg.bold = _bold; cfg.italic = _italic;
            cfg.style = StyleString();
            cfg.gradientColors = NametagIconApplicator.SerializeGradientColors(_gradient);
            cfg.gradientAngle = _gradientAngle;
            cfg.outlineEnabled = _outlineEnabled;
            cfg.outlineR = _outlineR; cfg.outlineG = _outlineG; cfg.outlineB = _outlineB;
            cfg.outlineWidth = _outlineWidth;
            cfg.outlineMode = _outlineMode;
            var crownCfg = CrownRankService.CfgFromSettings();
            ApplyPreview(cfg, crownCfg, NametagIconApplicator.PlatformHideFromSettings(), NametagIconApplicator.PlatformCustomFromSettings());
        }

        protected override bool Save()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            SettingsService.Set(KEY_COLOR_R, _r.ToString(ci));
            SettingsService.Set(KEY_COLOR_G, _g.ToString(ci));
            SettingsService.Set(KEY_COLOR_B, _b.ToString(ci));
            SettingsService.Set(KEY_BOLD, _bold ? "true" : "false");
            SettingsService.Set(KEY_ITALIC, _italic ? "true" : "false");
            SettingsService.Set(KEY_ENABLED, "true");
            SettingsService.Set(KEY_NAME_STYLE, StyleString());
            SettingsService.Set(KEY_GRADIENT_COLORS, NametagIconApplicator.SerializeGradientColors(_gradient));
            SettingsService.Set(KEY_GRADIENT_ANGLE, _gradientAngle.ToString(ci));
            SettingsService.Set(KEY_OUTLINE_ENABLED, _outlineEnabled ? "true" : "false");
            SettingsService.Set(KEY_OUTLINE_R, _outlineR.ToString(ci));
            SettingsService.Set(KEY_OUTLINE_G, _outlineG.ToString(ci));
            SettingsService.Set(KEY_OUTLINE_B, _outlineB.ToString(ci));
            SettingsService.Set(KEY_OUTLINE_WIDTH, _outlineWidth.ToString(ci));
            SettingsService.Set(KEY_OUTLINE_MODE, _outlineMode);

            NametagIconApplicator.ApplyNametag();
            NametagFinder.ReapplyAllNameplates();
            MenuCustomizationApplication.Instance?.ReapplySpecialForeground(
                MenuCustomizationApplication.SpecialScreen.PrivateLobbyPlayerList);
            return true;
        }

        protected override void OnRemoveClicked()
        {
            _r = _g = _b = 1f;
            _bold = _italic = false;
            _nameStyle = NameStyle.Default;
            _gradient.Clear();
            _gradientAngle = 0f;
            _outlineEnabled = false;
            _outlineR = _outlineG = _outlineB = 0f;
            _outlineWidth = 0.2f;
            _outlineMode = "outset";

            SettingsService.Set(KEY_COLOR_R, "1");
            SettingsService.Set(KEY_COLOR_G, "1");
            SettingsService.Set(KEY_COLOR_B, "1");
            SettingsService.Set(KEY_BOLD, "false");
            SettingsService.Set(KEY_ITALIC, "false");
            SettingsService.Set(KEY_NAME_STYLE, "default");
            SettingsService.Set(KEY_GRADIENT_COLORS, "");
            SettingsService.Set(KEY_GRADIENT_ANGLE, "0");
            SettingsService.Set(KEY_OUTLINE_ENABLED, "false");
            SettingsService.Set(KEY_OUTLINE_R, "0");
            SettingsService.Set(KEY_OUTLINE_G, "0");
            SettingsService.Set(KEY_OUTLINE_B, "0");
            SettingsService.Set(KEY_OUTLINE_WIDTH, "0.2");
            SettingsService.Set(KEY_OUTLINE_MODE, "outset");

            NametagIconApplicator.ApplyNametag();
            NametagFinder.ReapplyAllNameplates();
        }
    }
}
