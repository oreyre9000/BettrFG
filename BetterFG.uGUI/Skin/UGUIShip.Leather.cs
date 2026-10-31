using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BettrFG.uGUI
{
    public static partial class UGUIShip
    {
        public static bool Leather => DefaultButtonStyle == ButtonStyle.DeluxLeather;

        private const float LeatherTilePx = 64f, LeatherAdd = 0.016f, LeatherStrength = 0.06f;
        private const float SpotGrow = 0.25f, SpotShrink = 0.2f;
        private const float PressDepth = 1.5f, PressTiltX = 4f, PressTiltY = 1f;

        private sealed class Spot
        {
            public Graphic G;
            public RectTransform Area;
            public Transform Press;
            public float Radius, Feather, Gain, T, R, From;
            public bool Over;
            public Material Base, Inst;
            public Vector3 Rest;
            public Graphic[] Clipped;
            public RectMask2D Outer;
            public Rect Clip;
        }

        private static readonly Dictionary<int, Material> _leatherMats = new Dictionary<int, Material>();
        private static readonly List<Spot> _spots = new List<Spot>();
        private static Spot _pressed;

        public static void ApplyLeather(Graphic g)
        {
            if (!Leather || g.material != Graphic.defaultGraphicMaterial) return;
            var src = LeatherMaterial();
            if (src == null) return;
            var cv = g.canvas;
            float s = g.transform.lossyScale.x / (cv != null ? cv.rootCanvas.transform.lossyScale.x : 1f);
            int key = Mathf.Max(1, Mathf.RoundToInt(s * 100f));
            if (!_leatherMats.TryGetValue(key, out var m) || m == null)
            {
                m = new Material(src) { name = "BettrFG_Leather_" + key, hideFlags = HideFlags.HideAndDontSave };
                m.SetFloat("_GrainScale", LeatherTilePx * 100f / key);
                m.SetFloat("_GrainAdd", LeatherAdd);
                m.SetFloat("_GrainStrength", LeatherStrength);
                _leatherMats[key] = m;
            }
            if (cv != null) cv.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
            if (g.GetComponent<PositionAsUV1>() == null) g.gameObject.AddComponent<PositionAsUV1>();
            g.material = m;
        }

        public static void AddLeatherSpot(Graphic g, RectTransform area, float radius, float feather, float gain, Transform press = null)
        {
            ApplyLeather(g);
            if (g.material == Graphic.defaultGraphicMaterial) return;
            _spots.Add(new Spot { G = g, Area = area, Radius = radius, Feather = feather, Gain = gain, Press = press });
        }

        public static void TickLeather()
        {
            if (_spots.Count == 0) return;
            var mouse = (Vector2)Input.mousePosition;
            float dt = Time.unscaledDeltaTime;
            bool down = Input.GetMouseButtonDown(0);

            if (_pressed != null && (Input.GetMouseButtonUp(0) || _pressed.Press == null)) ReleasePress();

            for (int i = _spots.Count - 1; i >= 0; i--)
            {
                var s = _spots[i];
                if (s.G == null || s.Area == null)
                {
                    if (s.Inst != null) Object.Destroy(s.Inst);
                    _spots.RemoveAt(i);
                    continue;
                }
                bool over = s.G.isActiveAndEnabled && RectTransformUtility.RectangleContainsScreenPoint(s.Area, mouse, null);
                if (over != s.Over) { s.Over = over; s.From = s.R; s.T = 0f; }
                if (!over && s.R <= 0f) continue;

                s.T += dt;
                if (!over && s.T >= SpotShrink) { s.R = 0f; s.G.material = s.Base; continue; }
                s.R = over ? Mathf.Lerp(s.From, s.Radius, OutCubic(s.T / SpotGrow)) : s.From * (1f - OutCubic(s.T / SpotShrink));

                if (s.Inst == null)
                {
                    s.Base = s.G.material;
                    s.Inst = new Material(s.Base) { hideFlags = HideFlags.HideAndDontSave };
                    s.Inst.SetFloat("_HoverFeather", s.Feather);
                    s.Inst.SetFloat("_HoverGain", s.Gain);
                }
                if (s.G.material != s.Inst) s.G.material = s.Inst;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(s.G.rectTransform, mouse, null, out var lp);
                s.Inst.SetVector("_HoverPos", new Vector4(lp.x, lp.y, 0f, 0f));
                s.Inst.SetFloat("_HoverRadius", Mathf.Max(0.01f, s.R));

                if (down && over && s.Press != null && _pressed == null) PressDown(s, lp);
            }

            if (_pressed != null)
                foreach (var g in _pressed.Clipped)
                    if (g != null) g.canvasRenderer.EnableRectClipping(_pressed.Clip);
        }

        private static void PressDown(Spot s, Vector2 lp)
        {
            var r = s.G.rectTransform.rect;
            float nx = Mathf.Clamp((lp.x - r.center.x) / (r.width * 0.5f), -1f, 1f);
            float ny = Mathf.Clamp((lp.y - r.center.y) / (r.height * 0.5f), -1f, 1f);
            var t = (RectTransform)s.Press;
            s.Rest = t.localPosition;
            s.Clipped = t.GetComponentsInChildren<Graphic>();
            s.Outer = t.GetComponentInParent<RectMask2D>();

            var cv = s.G.canvas.rootCanvas.transform;
            var tr = t.rect;
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                var c = cv.InverseTransformPoint(t.TransformPoint(new Vector3((i & 1) == 0 ? tr.xMin : tr.xMax, (i & 2) == 0 ? tr.yMin : tr.yMax, 0f)));
                x0 = Mathf.Min(x0, c.x); x1 = Mathf.Max(x1, c.x); y0 = Mathf.Min(y0, c.y);
            }
            var clip = Rect.MinMaxRect(x0 - 50f, y0, x1 + 50f, y0 + 10000f);
            if (s.Outer != null)
            {
                var o = s.Outer.canvasRect;
                clip = Rect.MinMaxRect(Mathf.Max(clip.xMin, o.xMin), Mathf.Max(clip.yMin, o.yMin), Mathf.Min(clip.xMax, o.xMax), Mathf.Min(clip.yMax, o.yMax));
            }
            s.Clip = clip;

            t.localPosition = s.Rest + new Vector3(0f, -PressDepth, 0f);
            t.localRotation = Quaternion.Euler(PressTiltX * (0.6f + 0.4f * ny), -nx * PressTiltY, 0f);
            _pressed = s;
        }

        private static void ReleasePress()
        {
            var s = _pressed;
            _pressed = null;
            if (s.Press == null) return;
            s.Press.localPosition = s.Rest;
            s.Press.localRotation = Quaternion.identity;
            foreach (var g in s.Clipped)
            {
                if (g == null) continue;
                if (s.Outer != null) g.canvasRenderer.EnableRectClipping(s.Outer.canvasRect);
                else g.canvasRenderer.DisableRectClipping();
            }
        }

        private static float OutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            float u = 1f - t;
            return 1f - u * u * u;
        }
    }
}
