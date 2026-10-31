using System;
using BetterFG.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Components
{
    // rectangular outline that pulses inward and fades, for highlighting a widget as a box rather
    // than ringing its centre. two outlines run half a cycle apart so there's always one on screen.
    //
    // each outline is four thin bars, so the stroke is a number we set instead of something the
    // scale drags along with it: the box gets skinnier as it gets bigger, matching the circle pulse.
    // point Follow at a rect and it takes that rect's size, so it frames the actual widget.
    public class HollowBoxPulse : MonoBehaviour
    {
        public HollowBoxPulse(IntPtr ptr) : base(ptr) { }

        public Vector2 BaseSize = new Vector2(120f, 40f);
        public float Pad = 6f;
        public float StartScale = 1.18f;
        public float EndScale = 0.96f;
        public float Period = 1.4f;
        public float Stroke = 3.6f;
        public Color Tint = new Color(1f, 0.85f, 0.35f, 0.95f);

        public RectTransform Follow;
        public Canvas HostCanvas;

        private RectTransform _selfRt;
        private readonly Image[] _edge = new Image[8];
        private float _tA, _tB;

        void Awake()
        {
            _selfRt = GetComponent<RectTransform>();
            BuildOutline(0, "Box_A");
            BuildOutline(1, "Box_B");
            _tA = 0f;
            _tB = 0.5f;
        }

        private void BuildOutline(int idx, string name)
        {
            var g = new GameObject(name);
            g.transform.SetParent(transform, false);
            var rt = g.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            for (int i = 0; i < 4; i++)
            {
                var e = new GameObject("Edge_" + i);
                e.transform.SetParent(g.transform, false);
                var ert = e.AddComponent<RectTransform>();
                ert.anchorMin = ert.anchorMax = new Vector2(0.5f, 0.5f);
                ert.pivot = new Vector2(0.5f, 0.5f);
                var img = e.AddComponent<Image>();
                img.raycastTarget = false;
                _edge[idx * 4 + i] = img;
            }
        }

        void Update()
        {
            if (Follow != null && HostCanvas != null && _selfRt != null)
            {
                var box = CanvasGeom.AabbOf(HostCanvas, Follow, Pad);
                _selfRt.anchoredPosition = box.center;
                BaseSize = box.size;
            }

            float dt = Time.unscaledDeltaTime / Mathf.Max(0.01f, Period);
            _tA = (_tA + dt) % 1f;
            _tB = (_tB + dt) % 1f;
            Apply(0, _tA);
            Apply(1, _tB);
        }

        private void Apply(int idx, float p)
        {
            float s = Mathf.Lerp(StartScale, EndScale, p);
            float w = BaseSize.x * s;
            float h = BaseSize.y * s;
            float t = Stroke / Mathf.Max(0.2f, s);

            var c = Tint;
            c.a *= (1f - p) * 0.95f;

            // corners overlap by the stroke width, which is what keeps them square
            Place(_edge[idx * 4 + 0], new Vector2(w, t), new Vector2(0f, (h - t) * 0.5f), c);
            Place(_edge[idx * 4 + 1], new Vector2(w, t), new Vector2(0f, -(h - t) * 0.5f), c);
            Place(_edge[idx * 4 + 2], new Vector2(t, h), new Vector2(-(w - t) * 0.5f, 0f), c);
            Place(_edge[idx * 4 + 3], new Vector2(t, h), new Vector2((w - t) * 0.5f, 0f), c);
        }

        private static void Place(Image img, Vector2 size, Vector2 pos, Color c)
        {
            if (img == null) return;
            var rt = img.rectTransform;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            img.color = c;
        }
    }
}
