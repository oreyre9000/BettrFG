using System;
using BetterFG.Customization.UI;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public partial class UITab
    {
        private class FgSlot
        {
            public string label, key;
            public Color def, c;
            public Image swatch;
        }

        private readonly FgSlot[] _fgSlots =
        {
            new FgSlot { label = "ui.cyan_replacement", key = "menu.fg.cyan", def = MenuCustomizationApplication.FgDefaultCyan },
            new FgSlot { label = "ui.black_replacement", key = "menu.fg.black", def = MenuCustomizationApplication.FgDefaultBlack },
            new FgSlot { label = "ui.yellow_replacement", key = "menu.fg.yellow", def = MenuCustomizationApplication.FgDefaultYellow },
            new FgSlot { label = "ui.blue_replacement", key = "menu.fg.blue", def = MenuCustomizationApplication.FgDefaultBlue },
            new FgSlot { label = "ui.pink_replacement", key = "menu.fg.pink", def = MenuCustomizationApplication.FgDefaultPink },
            new FgSlot { label = "ui.orange_replacement", key = "menu.fg.orange", def = MenuCustomizationApplication.FgDefaultOrange },
        };

        private RectTransform _fgPanelRt;
        private float _fgPanelW, _fgPanelH;
        private bool _fgDirty;
        private float _fgNextApply;

        void Update()
        {
            if (!_fgDirty || Time.unscaledTime < _fgNextApply) return;
            _fgDirty = false;
            _fgNextApply = Time.unscaledTime + 0.5f;
            OnApply();
        }

        private void BuildFgPanel(RectTransform parent, float x, float y, float w, float h)
        {
            _fgPanelRt = parent;
            _fgPanelW = w;
            _fgPanelH = h;
            var (_, content) = UGUIShip.CreateScrollView(parent, new Rect(0f, y, TabWidth, h));

            float cy = PAD;
            float swH = (LH + SH) * 4f - SH;
            float sliderW = w - swH - PAD * 2f;
            foreach (var s in _fgSlots)
            {
                var slot = s;
                UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), slot.label, FS_SM, HINT);
                cy += LH + SH;

                var swGo = new GameObject("Swatch");
                swGo.transform.SetParent(content, false);
                UGUIShip.SetPixelRect(swGo.AddComponent<RectTransform>(), new Rect(x + sliderW + PAD * 2f, cy, swH, swH));
                slot.swatch = swGo.AddComponent<Image>();
                UGUIShip.ApplyDeluxColorFill(slot.swatch);
                slot.swatch.color = slot.c;
                slot.swatch.raycastTarget = false;

                UGUIShip.CreateColorControls(content, x, ref cy, sliderW,
                    () => slot.c.r, () => slot.c.g, () => slot.c.b,
                    v => slot.c.r = v, v => slot.c.g = v, v => slot.c.b = v,
                    () => { slot.swatch.color = slot.c; _fgDirty = true; }, out _, out _, out _, slot.def);

                UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
                cy += 1f + PAD;
            }

            UGUIShip.CreateButton(content, new Rect(x, cy, w, BTN_H), "themes.reset_all", BTN_REMOVE, WHITE, FS_SM, new Action(ResetCustomColours));
            cy += BTN_H + PAD * 2f;
            content.sizeDelta = new Vector2(0f, cy);
        }

        private void ResetCustomColours()
        {
            foreach (var s in _fgSlots) s.c = s.def;
            OnApply();
            for (int i = _fgPanelRt.childCount - 1; i >= 0; i--)
                Destroy(_fgPanelRt.GetChild(i).gameObject);
            BuildFgPanel(_fgPanelRt, PAD, 0f, _fgPanelW, _fgPanelH);
        }

        private void OnApply()
        {
            SaveSettings();
            MenuCustomizationApplication.Instance?.ReapplyForegroundFromSettings();
            MenuCustomizationApplication.Instance?.ReapplyBakedPinkGreyTextures();
            MenuCustomizationApplication.Instance?.ReapplyCreativeBgLive();
            MenuCustomizationApplication.Instance?.ReapplyLevelBrowserTilesLive();
            BetterFG.Features.QualificationTime.FeatureQualificationTime.ReapplyTimerColors();
        }

        private void LoadSettings()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float P(string key, float def) =>
                float.TryParse(SettingsService.Get(key, def.ToString(ci)),
                    System.Globalization.NumberStyles.Float, ci, out float v) ? v : def;

            foreach (var s in _fgSlots)
                s.c = SettingsService.Get(s.key + ".on", "false") == "true"
                    ? new Color(P(s.key + ".r", s.def.r), P(s.key + ".g", s.def.g), P(s.key + ".b", s.def.b))
                    : s.def;
        }

        private void SaveSettings()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var s in _fgSlots)
            {
                SettingsService.Set(s.key + ".on", ScreenBackgroundService.NearColor(s.c, s.def) ? "false" : "true");
                SettingsService.Set(s.key + ".r", s.c.r.ToString(ci));
                SettingsService.Set(s.key + ".g", s.c.g.ToString(ci));
                SettingsService.Set(s.key + ".b", s.c.b.ToString(ci));
            }
        }
    }
}
