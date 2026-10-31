using System;
using BetterFG.UI;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.Features.Replay
{
    internal class ReplayPostFxKeyframeWindow
    {
        public static ReplayPostFxKeyframeWindow Instance;

        const float ROW = ReplayWindowKit.ROW;
        const float PAD = ReplayWindowKit.PAD;
        const float GRAPH_H = 70f;
        const int GRAPH_SAMPLES = 20;
        const float GRAPH_LINE_W = 2f;

        static readonly Color GRAPH_BG = new Color(0f, 0f, 0f, 0.35f);
        static readonly Color GRAPH_BASELINE = new Color(1f, 1f, 1f, 0.15f);

        readonly ReplayPostFxKeyframe _kf;
        readonly Action _onClose;
        readonly RectTransform _root;
        readonly float _width;
        RectTransform _content;
        float _y;
        int _row;

        public ReplayPostFxKeyframe Keyframe => _kf;

        ReplayPostFxKeyframeWindow(Transform parent, Rect rect, ReplayPostFxKeyframe kf, Action onClose)
        {
            _kf = kf;
            _onClose = onClose;
            _width = rect.width;
            _root = UGUIShip.CreatePanel(parent, rect, Color.clear, "ReplayPostFxKeyframeWindow");
            ReplayWindowKit.EditBackdrop(_root);
            Rebuild();
        }

        public static ReplayPostFxKeyframeWindow Show(Transform parent, Rect rect, ReplayPostFxKeyframe kf, Action onClose)
        {
            Instance?.Close();
            Instance = new ReplayPostFxKeyframeWindow(parent, rect, kf, onClose);
            return Instance;
        }

        public void Close()
        {
            if (Instance == this) Instance = null;
            ReplayWindowKit.Close(_root);
            _onClose?.Invoke();
        }

        void Rebuild()
        {
            if (_content != null) UnityEngine.Object.Destroy(_content.gameObject);
            _content = ReplayWindowKit.Content(_root);

            var stamp = TimeSpan.FromSeconds(_kf.time);
            ReplayWindowKit.Title(_content, _width, BetterFG.Services.LocalizationService.Format("ui.postfx_keyframe_fmt",
                stamp.Minutes, stamp.Seconds, stamp.Milliseconds));

            _y = ReplayWindowKit.HEAD + 4f;
            _row = 0;

            Section("ui.exposure_color");
            Line("ui.exposure", (_kf.exposure >= 0f ? "+" : "") + _kf.exposure.ToString("0.0") + " EV",
                () => StepExposure(-1), () => StepExposure(1));
            Line("ui.contrast", Signed(_kf.contrast), () => StepContrast(-1), () => StepContrast(1));
            Line("ui.saturation", Signed(_kf.saturation), () => StepSaturation(-1), () => StepSaturation(1));
            Line("ui.temperature", Signed(_kf.temperature), () => StepTemperature(-1), () => StepTemperature(1));
            Line("ui.tint", Signed(_kf.tint), () => StepTint(-1), () => StepTint(1));

            Section("ui.effects");
            Line("ui.vignette", Mathf.RoundToInt(_kf.vignette * 100f) + "%", () => StepVignette(-1), () => StepVignette(1));
            Line("ui.chromatic_aberration", Mathf.RoundToInt(_kf.chromaticAberration * 100f) + "%",
                () => StepChroma(-1), () => StepChroma(1));

            Section("ui.bloom");
            Line("ui.bloom_intensity", _kf.bloomIntensity.ToString("0.00"), () => StepBloomIntensity(-1), () => StepBloomIntensity(1));
            Line("ui.bloom_threshold", _kf.bloomThreshold.ToString("0.00"), () => StepBloomThreshold(-1), () => StepBloomThreshold(1));

            Section("ui.sharpen");
            Line("ui.amount", _kf.sharpenAmount.ToString("0.00"), () => StepSharpenAmount(-1), () => StepSharpenAmount(1));
            Line("ui.radius", _kf.sharpenRadius.ToString("0.00"), () => StepSharpenRadius(-1), () => StepSharpenRadius(1));

            Section("ui.depth_of_field");
            Line("ui.strength", Mathf.RoundToInt(_kf.dofStrength * 100f) + "%", () => StepDofStrength(-1), () => StepDofStrength(1));
            Line("ui.distance", _kf.dofDistance.ToString("0.0") + " m", () => StepDofDistance(-1), () => StepDofDistance(1));

            Section("replay.motion_blur");
            Line("ui.amount", Mathf.RoundToInt(_kf.motionBlur * 100f) + "%", () => StepMotionBlur(-1), () => StepMotionBlur(1));
            Line("replay.motion_blur_samples", _kf.motionBlurSamples.ToString(), () => StepMotionSamples(-1), () => StepMotionSamples(1));

            Section("ui.preview");
            UGUIShip.CreateLabel(_content, new Rect(PAD, _y, _width - PAD * 2f, 14f),
                "ui.tone_curve_colour_hints_at_temperature_tint", UIScale.FS_SM - 1, ReplayWindowKit.HINT);
            _y += 16f;
            BuildGraph();
            _y += GRAPH_H + 6f;

            UGUIShip.CreateLabel(_content, new Rect(PAD, _y, _width - PAD * 2f, ROW),
                "ui.x_deletes", UIScale.FS_SM - 1, ReplayWindowKit.HINT);
            _y += ROW;

            _root.sizeDelta = new Vector2(_width, _y + PAD);
        }

        static string Signed(float v) => (v >= 0f ? "+" : "") + Mathf.RoundToInt(v);

        void Section(string title)
        {
            ReplayWindowKit.Section(_content, _y, _width, title);
            _y += ReplayWindowKit.SECTION_H;
        }

        void Line(string label, string value, Action prev, Action next)
        {
            ReplayWindowKit.Carousel(_content, _y, _width, _row, label, value, prev, next);
            _y += ROW;
            _row++;
        }

        void BuildGraph()
        {
            float graphW = _width - PAD * 2f;
            ReplayWindowKit.ColorFill(UGUIShip.CreatePanel(_content, new Rect(PAD, _y, graphW, GRAPH_H), GRAPH_BG, "Graph"));

            GraphLine(new Vector2(PAD, _y + GRAPH_H), new Vector2(PAD + graphW, _y), GRAPH_BASELINE, 1f);

            var tint = TintColor(_kf.temperature, _kf.tint);
            Vector2 prev = default;
            for (int i = 0; i < GRAPH_SAMPLES; i++)
            {
                float x = i / (float)(GRAPH_SAMPLES - 1);
                float v = CurveY(x, _kf.exposure, _kf.contrast);
                var p = new Vector2(PAD + x * graphW, _y + GRAPH_H * (1f - v));
                if (i > 0) GraphLine(prev, p, tint, GRAPH_LINE_W);
                prev = p;
            }
        }

        void GraphLine(Vector2 from, Vector2 to, Color color, float thickness)
        {
            var go = new GameObject("GraphLine");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(_content, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(from.x, -from.y);

            var delta = to - from;
            rt.sizeDelta = new Vector2(delta.magnitude, thickness);
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(-delta.y, delta.x) * Mathf.Rad2Deg);

            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }

        static float CurveY(float x, float exposure, float contrast)
        {
            float v = (x - 0.5f) * (1f + contrast / 100f) + 0.5f;
            v *= Mathf.Pow(2f, exposure);
            return Mathf.Clamp01(v);
        }

        static Color TintColor(float temperature, float tint)
        {
            float t = temperature / 100f;
            float g = tint / 100f;
            float r = Mathf.Clamp01(0.9f + 0.25f * t - 0.1f * g);
            float gr = Mathf.Clamp01(0.9f - 0.05f * t + 0.25f * g);
            float b = Mathf.Clamp01(0.9f - 0.3f * t - 0.15f * g);
            return new Color(r, gr, b);
        }

        static float Step(float v, int dir, float step, float min, float max) => ReplayWindowKit.Step(v, dir, step, min, max, true);

        void StepExposure(int dir)
        {
            _kf.exposure = Step(_kf.exposure, dir, ReplayPostFxKeyframe.ExposureStep, ReplayPostFxKeyframe.ExposureMin, ReplayPostFxKeyframe.ExposureMax);
            Rebuild();
        }

        void StepContrast(int dir)
        {
            _kf.contrast = Step(_kf.contrast, dir, ReplayPostFxKeyframe.GradeStep, ReplayPostFxKeyframe.GradeMin, ReplayPostFxKeyframe.GradeMax);
            Rebuild();
        }

        void StepSaturation(int dir)
        {
            _kf.saturation = Step(_kf.saturation, dir, ReplayPostFxKeyframe.GradeStep, ReplayPostFxKeyframe.GradeMin, ReplayPostFxKeyframe.GradeMax);
            Rebuild();
        }

        void StepTemperature(int dir)
        {
            _kf.temperature = Step(_kf.temperature, dir, ReplayPostFxKeyframe.GradeStep, ReplayPostFxKeyframe.GradeMin, ReplayPostFxKeyframe.GradeMax);
            Rebuild();
        }

        void StepTint(int dir)
        {
            _kf.tint = Step(_kf.tint, dir, ReplayPostFxKeyframe.GradeStep, ReplayPostFxKeyframe.GradeMin, ReplayPostFxKeyframe.GradeMax);
            Rebuild();
        }

        void StepVignette(int dir)
        {
            _kf.vignette = Step(_kf.vignette, dir, ReplayPostFxKeyframe.VignetteStep, ReplayPostFxKeyframe.VignetteMin, ReplayPostFxKeyframe.VignetteMax);
            Rebuild();
        }

        void StepChroma(int dir)
        {
            _kf.chromaticAberration = Step(_kf.chromaticAberration, dir, ReplayPostFxKeyframe.ChromaStep, ReplayPostFxKeyframe.ChromaMin, ReplayPostFxKeyframe.ChromaMax);
            Rebuild();
        }

        void StepBloomIntensity(int dir)
        {
            _kf.bloomIntensity = Step(_kf.bloomIntensity, dir, ReplayPostFxKeyframe.BloomIntensityStep, ReplayPostFxKeyframe.BloomIntensityMin, ReplayPostFxKeyframe.BloomIntensityMax);
            Rebuild();
        }

        void StepBloomThreshold(int dir)
        {
            _kf.bloomThreshold = Step(_kf.bloomThreshold, dir, ReplayPostFxKeyframe.BloomThresholdStep, ReplayPostFxKeyframe.BloomThresholdMin, ReplayPostFxKeyframe.BloomThresholdMax);
            Rebuild();
        }

        void StepSharpenAmount(int dir)
        {
            _kf.sharpenAmount = Step(_kf.sharpenAmount, dir, ReplayPostFxKeyframe.SharpenAmountStep, ReplayPostFxKeyframe.SharpenAmountMin, ReplayPostFxKeyframe.SharpenAmountMax);
            Rebuild();
        }

        void StepSharpenRadius(int dir)
        {
            _kf.sharpenRadius = Step(_kf.sharpenRadius, dir, ReplayPostFxKeyframe.SharpenRadiusStep, ReplayPostFxKeyframe.SharpenRadiusMin, ReplayPostFxKeyframe.SharpenRadiusMax);
            Rebuild();
        }

        void StepDofStrength(int dir)
        {
            _kf.dofStrength = Step(_kf.dofStrength, dir, ReplayPostFxKeyframe.DofStrengthStep, ReplayPostFxKeyframe.DofStrengthMin, ReplayPostFxKeyframe.DofStrengthMax);
            Rebuild();
        }

        void StepMotionBlur(int dir)
        {
            _kf.motionBlur = Step(_kf.motionBlur, dir, ReplayPostFxKeyframe.MotionBlurStep, ReplayPostFxKeyframe.MotionBlurMin, ReplayPostFxKeyframe.MotionBlurMax);
            Rebuild();
        }

        void StepMotionSamples(int dir)
        {
            var steps = ReplayPostFxKeyframe.MotionBlurSampleSteps;
            int at = Array.IndexOf(steps, _kf.motionBlurSamples);
            if (at < 0) at = 3;
            _kf.motionBlurSamples = steps[ReplayWindowKit.Step(at, dir, steps.Length, true)];
            Rebuild();
        }

        void StepDofDistance(int dir)
        {
            float step = _kf.dofDistance < 5f ? 0.25f : _kf.dofDistance < 20f ? 1f : 5f;
            _kf.dofDistance = Mathf.Clamp(_kf.dofDistance + dir * step,
                ReplayPostFxKeyframe.DofDistanceMin, ReplayPostFxKeyframe.DofDistanceMax);
            Rebuild();
        }
    }
}
