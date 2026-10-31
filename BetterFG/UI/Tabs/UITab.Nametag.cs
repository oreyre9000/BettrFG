using System;
using BetterFG.Nametag;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public partial class UITab
    {
        private bool _ntHideArrow;
        private bool _ntHidePlatform;
        private float _ntScale = 1f;
        private float _ntArrowScale = 1f;
        private Button _ntArrowBtn;
        private Button _ntHidePlatformBtn;
        private Slider _ntScaleSlider;
        private Text _ntScaleValue;
        private Slider _ntArrowScaleSlider;
        private Text _ntArrowScaleValue;
        private readonly NametagPreview _ntPreview = new NametagPreview();
        private Vector3 _ntArrowBaseScale;
        private float _ntArrowH;
        private Transform[] _ntRest;
        private Vector3[] _ntRestBase;

        private void SetAllNametagEnabled(bool on)
        {
            _ntHideArrow = on;
            _ntHidePlatform = on;
            _ntScale = 1f;
            _ntArrowScale = 1f;
            SetToggle(_ntArrowBtn, on);
            SetToggle(_ntHidePlatformBtn, on);
            if (_ntScaleSlider != null) _ntScaleSlider.value = 1f;
            if (_ntScaleValue != null) _ntScaleValue.text = "1.00x";
            if (_ntArrowScaleSlider != null) _ntArrowScaleSlider.value = 1f;
            if (_ntArrowScaleValue != null) _ntArrowScaleValue.text = "1.00x";
            RefreshNametagPreview();
            NametagIconApplicator.SetPlatformHideEveryone(on);
        }

        private void BuildNametagPanel(RectTransform parent, float x, float y, float w, float h)
        {
            float rowH = BTN_H + SH;
            float toggleW = BTN_H * 2.2f;
            float valW = BTN_H * 2f;
            float lblW = w * 0.42f;
            float sliderW = w - lblW - valW - PAD * 2f;

            float cy = y + PAD;
            float previewH = h - PAD * 3f - rowH * 4f;
            if (previewH < 120f) previewH = Mathf.Max(80f, h * 0.5f);

            _ntPreview.Build(parent, new Rect(x, cy, w, previewH), RefreshNametagPreview);
            cy += previewH + PAD;

            UGUIShip.CreateLabel(parent, new Rect(x, cy, w - toggleW - PAD, BTN_H),
                "nametag_all.remove_arrow", FS_SM, HINT, TextAnchor.MiddleLeft);
            _ntArrowBtn = UGUIShip.CreateButton(parent, new Rect(x + w - toggleW, cy, toggleW, BTN_H),
                _ntHideArrow ? "ui.on" : "ui.off", _ntHideArrow ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _ntHideArrow = !_ntHideArrow;
                    var lbl = _ntArrowBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, _ntHideArrow ? "ui.on" : "ui.off");
                    UGUIShip.SetButtonSelected(_ntArrowBtn, _ntHideArrow, SEL_COLOR);
                    OnNametagApply();
                    RefreshNametagPreview();
                }));
            cy += rowH;

            UGUIShip.CreateLabel(parent, new Rect(x, cy, w - toggleW - PAD, BTN_H),
                "nametag_all.hide_platform_icons", FS_SM, HINT, TextAnchor.MiddleLeft);
            _ntHidePlatformBtn = UGUIShip.CreateButton(parent, new Rect(x + w - toggleW, cy, toggleW, BTN_H),
                _ntHidePlatform ? "ui.on" : "ui.off", _ntHidePlatform ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _ntHidePlatform = !_ntHidePlatform;
                    var lbl = _ntHidePlatformBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, _ntHidePlatform ? "ui.on" : "ui.off");
                    UGUIShip.SetButtonSelected(_ntHidePlatformBtn, _ntHidePlatform, SEL_COLOR);
                    NametagIconApplicator.SetPlatformHideEveryone(_ntHidePlatform);
                    RefreshNametagPreview();
                }));
            cy += rowH;

            UGUIShip.CreateLabel(parent, new Rect(x, cy, lblW, BTN_H), "nametag_all.scale", FS_SM, HINT, TextAnchor.MiddleLeft);
            _ntScaleValue = UGUIShip.CreateLabel(parent, new Rect(x + w - valW, cy, valW, BTN_H),
                _ntScale.ToString("0.00") + "x", FS_SM, WHITE, TextAnchor.MiddleRight);
            _ntScaleSlider = UGUIShip.CreateSlider(parent, x + lblW + PAD, cy, sliderW, "", _ntScale, BTN_H, PAD, FS_SM,
                v =>
                {
                    _ntScale = v;
                    if (_ntScaleValue != null) _ntScaleValue.text = v.ToString("0.00") + "x";
                    OnNametagApply();
                    RefreshNametagPreview();
                },
                reserveLabel: false, resetTo: 1f);
            _ntScaleSlider.minValue = 0.25f;
            _ntScaleSlider.maxValue = 3f;
            _ntScaleSlider.value = _ntScale;
            cy += rowH;

            UGUIShip.CreateLabel(parent, new Rect(x, cy, lblW, BTN_H), "nametag_all.arrow_scale", FS_SM, HINT, TextAnchor.MiddleLeft);
            _ntArrowScaleValue = UGUIShip.CreateLabel(parent, new Rect(x + w - valW, cy, valW, BTN_H),
                _ntArrowScale.ToString("0.00") + "x", FS_SM, WHITE, TextAnchor.MiddleRight);
            _ntArrowScaleSlider = UGUIShip.CreateSlider(parent, x + lblW + PAD, cy, sliderW, "", _ntArrowScale, BTN_H, PAD, FS_SM,
                v =>
                {
                    _ntArrowScale = v;
                    if (_ntArrowScaleValue != null) _ntArrowScaleValue.text = v.ToString("0.00") + "x";
                    OnNametagApply();
                    RefreshNametagPreview();
                },
                reserveLabel: false, resetTo: 1f);
            _ntArrowScaleSlider.minValue = 0.25f;
            _ntArrowScaleSlider.maxValue = 3f;
            _ntArrowScaleSlider.value = _ntArrowScale;
        }

        private void RefreshNametagPreview()
        {
            var nameCfg = NametagIconApplicator.CfgFromSettings();
            nameCfg.enabled = true;
            var crownCfg = CrownRankService.CfgFromSettings();
            crownCfg.enabled = true;
            _ntPreview.Apply(nameCfg, crownCfg,
                NametagIconApplicator.PlatformHideFromSettings(),
                NametagIconApplicator.PlatformCustomFromSettings());
            var canvas = _ntPreview.Canvas;
            if (canvas != null)
            {
                BetterFG.Nametag.CrownRankService.SetCrownRankSafe(canvas, 100000);
                canvas.transform.localScale = Vector3.one * _ntScale;
                if (canvas._arrowImage != null)
                {
                    var arrowT = canvas._arrowImage.transform;
                    if (_ntArrowBaseScale == Vector3.zero)
                    {
                        _ntArrowBaseScale = arrowT.localScale;
                        var art = canvas._arrowImage.rectTransform;
                        _ntArrowH = art.rect.height * art.localScale.y * (1f - art.pivot.y);
                        var parent = arrowT.parent;
                        int n = parent.childCount - 1;
                        _ntRest = new Transform[n];
                        _ntRestBase = new Vector3[n];
                        int j = 0;
                        for (int c = 0; c < parent.childCount && j < n; c++)
                        {
                            var ch = parent.GetChild(c);
                            if (ch.Pointer == arrowT.Pointer) continue;
                            _ntRest[j] = ch;
                            _ntRestBase[j] = ch.localPosition;
                            j++;
                        }
                    }
                    float lift = (_ntArrowScale - 1f) * _ntArrowH;
                    arrowT.localScale = _ntArrowBaseScale * _ntArrowScale;
                    for (int k = 0; k < _ntRest.Length; k++)
                    {
                        var bp = _ntRestBase[k];
                        _ntRest[k].localPosition = new Vector3(bp.x, bp.y + lift, bp.z);
                    }
                    canvas._arrowImage.gameObject.SetActive(!_ntHideArrow);
                }
            }
        }

        private void LoadNametagSettings()
        {
            _ntHideArrow = SettingsService.Get(NametagAllPlayersService.KEY_HIDE_ARROW, "false") == "true";
            _ntHidePlatform = NametagIconApplicator.PlatformHideEveryoneFromSettings();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            _ntScale = float.TryParse(SettingsService.Get(NametagAllPlayersService.KEY_SCALE, "1"),
                System.Globalization.NumberStyles.Float, ci, out float s) ? s : 1f;
            _ntArrowScale = float.TryParse(SettingsService.Get(NametagAllPlayersService.KEY_ARROW_SCALE, "1"),
                System.Globalization.NumberStyles.Float, ci, out float a) ? a : 1f;
        }

        private void OnNametagApply()
        {
            SettingsService.Set(NametagAllPlayersService.KEY_HIDE_ARROW, _ntHideArrow ? "true" : "false");
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            SettingsService.Set(NametagAllPlayersService.KEY_SCALE, _ntScale.ToString(ci));
            SettingsService.Set(NametagAllPlayersService.KEY_ARROW_SCALE, _ntArrowScale.ToString(ci));
            NametagAllPlayersService.Invalidate();
        }
    }
}
