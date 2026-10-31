using System;
using BetterFG.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Components
{
    // hollow ring that pulses inward and fades, forever. two rings run half a cycle apart so there's
    // always one on screen.
    //
    // the ring gets SKINNIER as it gets bigger: the stroke is held to a roughly constant width on
    // screen instead of scaling with the ring, so a wide sweep reads as a thin hoop closing in
    // rather than a fat donut. one texture can't do that under scale, so it picks off a ladder of
    // pre-baked rings whose stroke fraction cancels the current scale.
    public class HollowCirclePulse : MonoBehaviour
    {
        public HollowCirclePulse(IntPtr ptr) : base(ptr) { }

        public float BaseSize = 80f;
        public float StartScale = 1.28f;
        public float EndScale = 0.92f;
        public float Period = 1.4f;
        public Color Tint = new Color(1f, 0.85f, 0.35f, 0.95f);

        // optional: re-centre on this rect every frame. tab titles slide as their tab opens and the
        // wheel re-lays its icons on an orbit each frame, so a position captured once drifts off.
        public RectTransform Follow;
        public Canvas HostCanvas;

        private const float StrokeTarget = 0.045f;
        private const int Rungs = 8;
        private const float ThinnestFrac = 0.022f;
        private const float ThickestFrac = 0.080f;
        private static Texture2D[] _ladder;

        private RectTransform _selfRt;
        private readonly RawImage[] _ring = new RawImage[2];
        private float _tA, _tB;

        void Awake()
        {
            _selfRt = GetComponent<RectTransform>();
            EnsureLadder();
            _ring[0] = BuildRingObject("Ring_A");
            _ring[1] = BuildRingObject("Ring_B");
            _tA = 0f;
            _tB = 0.5f;
        }

        private RawImage BuildRingObject(string name)
        {
            var g = new GameObject(name);
            g.transform.SetParent(transform, false);
            var rt = g.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            var img = g.AddComponent<RawImage>();
            img.raycastTarget = false;
            return img;
        }

        void Update()
        {
            if (Follow != null && HostCanvas != null && _selfRt != null)
                _selfRt.anchoredPosition = CanvasGeom.AabbOf(HostCanvas, Follow, 0f).center;

            float dt = Time.unscaledDeltaTime / Mathf.Max(0.01f, Period);
            _tA = (_tA + dt) % 1f;
            _tB = (_tB + dt) % 1f;
            Apply(_ring[0], _tA);
            Apply(_ring[1], _tB);
        }

        private void Apply(RawImage img, float p)
        {
            if (img == null) return;
            float s = Mathf.Lerp(StartScale, EndScale, p);

            // thicker texture the smaller the ring, so the drawn stroke stays put
            int rung = Mathf.Clamp(Mathf.RoundToInt(
                (StrokeTarget / Mathf.Max(0.2f, s) - ThinnestFrac) / (ThickestFrac - ThinnestFrac) * (Rungs - 1)),
                0, Rungs - 1);
            if (img.texture != _ladder[rung]) img.texture = _ladder[rung];

            var rt = img.rectTransform;
            if (rt.sizeDelta.x != BaseSize) rt.sizeDelta = new Vector2(BaseSize, BaseSize);
            rt.localScale = new Vector3(s, s, 1f);

            var c = Tint;
            c.a *= (1f - p) * 0.95f;
            img.color = c;
        }

        private static void EnsureLadder()
        {
            if (_ladder != null) return;
            _ladder = new Texture2D[Rungs];
            for (int i = 0; i < Rungs; i++)
                _ladder[i] = BuildTexture(Mathf.Lerp(ThinnestFrac, ThickestFrac, i / (float)(Rungs - 1)));
        }

        private static Texture2D BuildTexture(float thicknessFrac)
        {
            const int N = 128;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            float mid = N * 0.5f;
            float r = N * 0.44f;
            float thickHalf = N * thicknessFrac * 0.5f;
            const float aa = 1.5f;
            var pixels = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = x + 0.5f - mid;
                    float dy = y + 0.5f - mid;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Min(1f, Mathf.Clamp01((thickHalf - Mathf.Abs(d - r)) / aa + 1f));
                    pixels[y * N + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(pixels);
            tex.Apply(false);
            return tex;
        }
    }
}
