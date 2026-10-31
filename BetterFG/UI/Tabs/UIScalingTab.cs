using System;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class UIScalingTab : UISubTab
    {
        public UIScalingTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "UI - Scaling";
        protected override string TitleId => "ui.ui_scaling";

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;
            float y = VPAD;
            BuildScalingPanel(contentRoot, PAD, y, w, TabHeight - y);
            PositionSwitchLink();
        }

        private int _edgesIdx = 1; // 0=Soft, 1=Default, 2=Hard
        private float _canvasScale = 1.3333f;
        private InputField _scaleInput;
        private Slider _scaleSlider;
        private Button _btnEdgeSoft, _btnEdgeDefault, _btnEdgeHard;
        private Button _btnCanvasScaleEnabled;
        private const string KEY_CANVAS_EDGES = "ui.canvas.edges";
        private const string KEY_CANVAS_SCALE = "ui.canvas.scale";
        private const string KEY_CANVAS_SCALE_ENABLED = "ui.canvas.scale.enabled";
        private static readonly float[] EdgesValues = { 125f, 100f, 85f };

        private void BuildScalingPanel(RectTransform parent, float x, float y, float w, float h)
        {
            LoadScalingSettings();

            var (_, content) = UGUIShip.CreateScrollView(parent, new Rect(0f, y, TabWidth, h));
            float ew = w - 26f;
            float cy = PAD;

            // Edges
            UGUIShip.CreateLabel(content, new Rect(x, cy, ew, LH), "ui.edges", FS_SM, HINT);
            cy += LH + SH;

            float btnW3 = (ew - PAD) / 3f;
            _btnEdgeSoft = UGUIShip.CreateButton(content, new Rect(x, cy, btnW3, BTN_H), "ui.soft",
                _edgesIdx == 0 ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetEdges(0)));
            _btnEdgeDefault = UGUIShip.CreateButton(content, new Rect(x + btnW3 + PAD * 0.5f, cy, btnW3, BTN_H), "ui.default",
                _edgesIdx == 1 ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetEdges(1)));
            _btnEdgeHard = UGUIShip.CreateButton(content, new Rect(x + (btnW3 + PAD * 0.5f) * 2f, cy, btnW3, BTN_H), "ui.hard",
                _edgesIdx == 2 ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetEdges(2)));
            cy += BTN_H + PAD;

            UGUIShip.CreatePanel(content, new Rect(x, cy, ew, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            // row geometry shared by both scaling rows: [toggle][slider][value]
            float togW = BTN_H * 1.5f;
            float inputW = BTN_H * 1.9f;
            float sliderW = ew - togW - inputW - PAD * 2f;
            float inputH = LH;            // the value field sits vertically short, centred in the row
            float inputY = (BTN_H - inputH) * 0.5f;

            // ── Fall Guys UI scale ────────────────────────────────────────────────
            UGUIShip.CreateLabel(content, new Rect(x, cy, ew, LH), "ui.fall_guys_ui_scale", FS_SM, HINT);
            cy += LH + SH;

            const float scaleMin = 0.6f, scaleMax = 1.6f;
            _canvasScale = Mathf.Clamp(_canvasScale, scaleMin, scaleMax);

            bool canvasOn = CanvasScaleEnabled;
            _btnCanvasScaleEnabled = UGUIShip.CreateButton(content, new Rect(x, cy, togW, BTN_H),
                canvasOn ? "ui.on" : "ui.off", canvasOn ? BTN_ON : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    bool on = !CanvasScaleEnabled;
                    SettingsService.Set(KEY_CANVAS_SCALE_ENABLED, on ? "true" : "false");
                    var lbl = _btnCanvasScaleEnabled?.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, on ? "ui.on" : "ui.off");
                    var img = _btnCanvasScaleEnabled?.GetComponent<Image>();
                    if (img != null) img.color = on ? BTN_ON : BTN_DARK;
                    ApplyCanvasScale();
                }));

            _scaleSlider = UGUIShip.CreateSlider(content, x + togW + PAD, cy + (BTN_H - LH) * 0.5f, sliderW, "",
                Mathf.InverseLerp(scaleMin, scaleMax, _canvasScale), LH, PAD, FS_SM,
                new Action<float>(t =>
                {
                    float f = Mathf.Lerp(scaleMin, scaleMax, t);
                    _canvasScale = f;
                    SettingsService.Set(KEY_CANVAS_SCALE, f.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (_scaleInput != null) UGUIShip.RelabelText(_scaleInput, f.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                    ApplyCanvasScale();
                }), reserveLabel: false, resetTo: Mathf.InverseLerp(scaleMin, scaleMax, 1.3333f));

            _scaleInput = UGUIShip.CreateInputField(content, new Rect(x + togW + PAD + sliderW + PAD, cy + inputY, inputW, inputH),
                "1.33", Color.black, WHITE, FS_SM);
            _scaleInput.contentType = InputField.ContentType.DecimalNumber;
            _scaleInput.text = _canvasScale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            _scaleInput.onEndEdit.AddListener(new Action<string>(v =>
            {
                if (float.TryParse(v, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float f))
                {
                    f = Mathf.Clamp(f, scaleMin, scaleMax);
                    _canvasScale = f;
                    SettingsService.Set(KEY_CANVAS_SCALE, f.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (_scaleInput != null) UGUIShip.RelabelText(_scaleInput, f.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                    ApplyCanvasScale();
                    if (_scaleSlider != null) _scaleSlider.value = Mathf.InverseLerp(scaleMin, scaleMax, f);
                }
            }));
            cy += BTN_H + PAD;

            content.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void LoadScalingSettings()
        {
            _edgesIdx = int.TryParse(SettingsService.Get(KEY_CANVAS_EDGES, "1"), out int e) ? Mathf.Clamp(e, 0, 2) : 1;
            if (float.TryParse(SettingsService.Get(KEY_CANVAS_SCALE, "1.3333"),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float s) && s > 0f)
                _canvasScale = s;
            else
                _canvasScale = 1.3333f;
        }

        private void SetEdges(int idx)
        {
            _edgesIdx = idx;
            SettingsService.Set(KEY_CANVAS_EDGES, idx.ToString());
            UGUIShip.SetButtonSelected(_btnEdgeSoft, idx == 0, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnEdgeDefault, idx == 1, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnEdgeHard, idx == 2, SEL_COLOR);
            ApplyCanvasEdges();
        }

        private static Canvas GetUICanvas() =>
            GameObject.Find("UICanvas_Client_V2(Clone)")?.GetComponent<Canvas>();
        private static Canvas GetLobbyForegroundCanvas() =>
            GameObject.Find("Menu_Screen_Lobby(Clone)/ForegroundCanvas")?.GetComponent<Canvas>();
        private static Canvas GetNavOverlayCanvas() =>
            GameObject.Find("Prefab_UI_NavigationOverlay(Clone)")?.GetComponent<Canvas>();

        private void ApplyCanvasEdges()
        {
            float ppu = EdgesValues[_edgesIdx];
            var ui = GetUICanvas();        if (ui != null) ui.referencePixelsPerUnit = ppu;
            var lf = GetLobbyForegroundCanvas(); if (lf != null) lf.referencePixelsPerUnit = ppu;
            var no = GetNavOverlayCanvas();      if (no != null) no.referencePixelsPerUnit = ppu;
        }

        internal static bool CanvasScaleEnabled => SettingsService.Get(KEY_CANVAS_SCALE_ENABLED, "false") == "true";

        // each canvas's untouched scaleFactor, captured the first time we see it so OFF restores
        // the real stock value (some resolutions / overlay canvases aren't 1f at rest).
        private static float? _stockMain, _stockLobbyFg, _stockNavOverlay;

        private static void ApplyOne(Canvas canvas, ref float? stock, float target)
        {
            if (canvas == null) return;
            if (stock == null) stock = canvas.scaleFactor;
            canvas.scaleFactor = CanvasScaleEnabled ? target : stock.Value;
        }

        private void ApplyCanvasScale()
        {
            ApplyOne(GetUICanvas(),               ref _stockMain,        _canvasScale);
            ApplyOne(GetLobbyForegroundCanvas(),  ref _stockLobbyFg,     _canvasScale);
            ApplyOne(GetNavOverlayCanvas(),       ref _stockNavOverlay,  _canvasScale);
        }

        public static void ApplyCanvasScalingFromSettings()
        {
            int edgeIdx = 1;
            if (int.TryParse(SettingsService.Get("ui.canvas.edges", "1"), out int e))
                edgeIdx = Mathf.Clamp(e, 0, 2);
            float ppu = EdgesValues[edgeIdx];

            float scale = 1.3333f;
            float.TryParse(SettingsService.Get("ui.canvas.scale", "1.3333"),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out scale);

            void Apply(Canvas c, ref float? stock)
            {
                if (c == null) return;
                c.referencePixelsPerUnit = ppu;
                if (stock == null) stock = c.scaleFactor;
                c.scaleFactor = CanvasScaleEnabled && scale > 0f ? scale : stock.Value;
            }

            Apply(GetUICanvas(),               ref _stockMain);
            Apply(GetLobbyForegroundCanvas(),  ref _stockLobbyFg);
            Apply(GetNavOverlayCanvas(),       ref _stockNavOverlay);
        }
    }
}
