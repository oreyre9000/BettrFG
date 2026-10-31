using System;
using BetterFG.Nametag;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Tab
{
    public class NametagColourTab : NametagWizardTab
    {
        public NametagColourTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Nametag - Colour";

        private const string KEY_COLOR_R = "nametag.color.r";
        private const string KEY_COLOR_G = "nametag.color.g";
        private const string KEY_COLOR_B = "nametag.color.b";
        private const string KEY_BOLD = "nametag.bold";
        private const string KEY_ITALIC = "nametag.italic";
        private const string KEY_ENABLED = "nametag.enabled";
        private const string KEY_NAME_STYLE = "nametag.namestyle";

        private enum NameStyle { None, Default, Gold, GoldColored }

        private float _r = 1f, _g = 1f, _b = 1f;
        private bool _bold, _italic;
        private NameStyle _nameStyle = NameStyle.Default;

        private Text _boldBtnLabel, _italicBtnLabel;
        private Text _styleCarouselLabel, _styleHintLabel;
        private RectTransform _colourContent;
        private float _colourWidth;

        private static readonly NameStyle[] STYLE_ORDER = { NameStyle.None, NameStyle.Default, NameStyle.Gold, NameStyle.GoldColored };
        private static readonly string[] STYLE_LABELS = { "None", "Default", "Gold", "Gold RGB" };
        private static readonly string[] STYLE_HINTS =
        {
            "None: leaves the game font/material alone, colour applied as a tag.",
            "Default: solid tinted colour on the default nametag material.",
            "Gold: fixed gold material. Colour has no effect.",
            "Gold RGB: gold outline with a coloured face.",
        };

        protected override string[] StepTitles => new[] { "Preset style", "Text colour", "Bold / italic" };

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
                       : NameStyle.Default;
        }

        protected override void BuildStep(int step, RectTransform root, float w, float bodyH)
        {
            var (_, c) = UGUIShip.CreateScrollView(root, new Rect(0f, 0f, TabWidth, bodyH));
            float cw = w - 26f;
            switch (step)
            {
                case 0: BuildStyleStep(c, cw); break;
                case 1: BuildColourStep(c, cw); break;
                case 2: BuildWeightStep(c, cw); break;
            }
        }

        private void BuildStyleStep(RectTransform c, float w)
        {
            float x = PAD, cy = PAD;
            UGUIShip.CreateLabel(c, new Rect(x, cy, w, LH), "PREDEFINED STYLE", FS_SM, HINT);
            cy += LH + SH;
            int current = Array.IndexOf(STYLE_ORDER, _nameStyle);
            if (current < 0) current = 1;
            float arrow = BTN_H;
            UGUIShip.CreateButton(c, new Rect(x, cy, arrow, BTN_H), "<", BTN_DARK, WHITE, FS_SM, new Action(() => StepStyle(-1)));
            _styleCarouselLabel = UGUIShip.CreateLabel(c, new Rect(x + arrow, cy, w - arrow * 2f, BTN_H),
                STYLE_LABELS[current], FS_SM, WHITE, TextAnchor.MiddleCenter);
            UGUIShip.CreateButton(c, new Rect(x + w - arrow, cy, arrow, BTN_H), ">", BTN_DARK, WHITE, FS_SM, new Action(() => StepStyle(1)));
            cy += BTN_H + SH;
            _styleHintLabel = UGUIShip.CreateLabel(c, new Rect(x, cy, w, LH * 2f), STYLE_HINTS[current], FS_SM, HINT, TextAnchor.UpperLeft);
            cy += LH * 2f + PAD;
            c.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void StepStyle(int delta)
        {
            int i = Array.IndexOf(STYLE_ORDER, _nameStyle);
            if (i < 0) i = 1;
            i = ((i + delta) % STYLE_ORDER.Length + STYLE_ORDER.Length) % STYLE_ORDER.Length;
            _nameStyle = STYLE_ORDER[i];
            if (_styleCarouselLabel != null) _styleCarouselLabel.text = STYLE_LABELS[i];
            if (_styleHintLabel != null) _styleHintLabel.text = STYLE_HINTS[i];
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

            float x = PAD, cy = PAD, w = _colourWidth;

            if (_nameStyle == NameStyle.Gold)
            {
                UGUIShip.CreateLabel(_colourContent, new Rect(x, cy, w, LH * 2f), STYLE_HINTS[2], FS_SM, HINT, TextAnchor.UpperLeft);
                cy += LH * 2f + PAD;
                _colourContent.sizeDelta = new Vector2(0f, cy + PAD);
                return;
            }

            UGUIShip.CreateLabel(_colourContent, new Rect(x, cy, w, LH), "COLOUR", FS_SM, HINT);
            cy += LH + SH;
            UGUIShip.CreateColorControls(_colourContent, x, ref cy, w,
                () => _r, () => _g, () => _b,
                v => _r = v, v => _g = v, v => _b = v, () => RefreshPreview(),
                out _, out _, out _, Color.white);
            _colourContent.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void BuildWeightStep(RectTransform c, float w)
        {
            float x = PAD, cy = PAD;
            UGUIShip.CreateLabel(c, new Rect(x, cy, w, LH), "STYLE", FS_SM, HINT);
            cy += LH + SH;
            float togglew = (w - PAD * 0.5f) / 2f;
            var boldBtn = UGUIShip.CreateButton(c, new Rect(x, cy, togglew, BTN_H),
                _bold ? "Bold: ON" : "Bold: OFF", BTN_DARK, WHITE, FS_SM, new Action(OnToggleBold));
            _boldBtnLabel = boldBtn.GetComponentInChildren<Text>();
            var italicBtn = UGUIShip.CreateButton(c, new Rect(x + togglew + PAD * 0.5f, cy, togglew, BTN_H),
                _italic ? "Italic: ON" : "Italic: OFF", BTN_DARK, WHITE, FS_SM, new Action(OnToggleItalic));
            _italicBtnLabel = italicBtn.GetComponentInChildren<Text>();
            cy += BTN_H + PAD;
            c.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void OnToggleBold()
        {
            _bold = !_bold;
            if (_boldBtnLabel != null) _boldBtnLabel.text = _bold ? "Bold: ON" : "Bold: OFF";
            RefreshPreview();
        }

        private void OnToggleItalic()
        {
            _italic = !_italic;
            if (_italicBtnLabel != null) _italicBtnLabel.text = _italic ? "Italic: ON" : "Italic: OFF";
            RefreshPreview();
        }

        private string StyleString() =>
            _nameStyle == NameStyle.None ? "none"
            : _nameStyle == NameStyle.Gold ? "gold"
            : _nameStyle == NameStyle.GoldColored ? "goldcolored"
            : "default";

        public override void RefreshPreview()
        {
            var cfg = NametagIconApplicator.CfgFromSettings();
            cfg.enabled = true;
            cfg.r = _r; cfg.g = _g; cfg.b = _b;
            cfg.bold = _bold; cfg.italic = _italic;
            cfg.style = StyleString();
            ApplyPreview(cfg, CrownRankService.CfgFromSettings(),
                NametagPreview.PlatformHideFromSettings(), NametagPreview.PlatformCustomFromSettings());
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

            NametagIconApplicator.ApplyNametag();
            NametagFinder.ReapplyAllNameplates();
            BetterFG.Customization.Menu.MenuCustomizationApplication.Instance?.ReapplySpecialForeground(
                BetterFG.Customization.Menu.MenuCustomizationApplication.SpecialScreen.PrivateLobbyPlayerList);
            return true;
        }

        protected override void OnRemoveClicked()
        {
            SettingsService.Set(KEY_COLOR_R, "1");
            SettingsService.Set(KEY_COLOR_G, "1");
            SettingsService.Set(KEY_COLOR_B, "1");
            SettingsService.Set(KEY_BOLD, "false");
            SettingsService.Set(KEY_ITALIC, "false");
            SettingsService.Set(KEY_NAME_STYLE, "default");

            NametagIconApplicator.ApplyNametag();
            NametagFinder.ReapplyAllNameplates();
        }
    }
}
