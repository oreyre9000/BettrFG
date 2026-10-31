using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Features.Onboarding
{
    // eats every click on the BettrFG UI except inside one allowed rect, so a tutorial step can only
    // be completed the way it asks. four invisible bands frame the hole; with no hole the top band
    // covers the whole canvas. clear + raycastTarget still blocks, alpha isn't hit-tested unless
    // alphaHitTestMinimumThreshold is set.
    public sealed class OnboardingBlocker
    {
        private readonly RectTransform _canvasRt;
        private readonly RectTransform[] _bands = new RectTransform[4];

        public OnboardingBlocker(Canvas canvas)
        {
            _canvasRt = canvas.GetComponent<RectTransform>();
            for (int i = 0; i < 4; i++) _bands[i] = MakeBand(canvas.transform, i);
            SetHole(null);
        }

        private static RectTransform MakeBand(Transform parent, int i)
        {
            var go = new GameObject("Blocker_" + i);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            var img = go.AddComponent<Image>();
            img.color = Color.clear;
            img.raycastTarget = true;
            return rt;
        }

        // `hole` is in canvas-local space (canvas centred on 0,0). null blocks everything.
        public void SetHole(Rect? hole)
        {
            float w = _canvasRt.rect.width;
            float h = _canvasRt.rect.height;
            float left = -w * 0.5f, right = w * 0.5f, bottom = -h * 0.5f, top = h * 0.5f;

            if (!hole.HasValue)
            {
                Place(_bands[0], left, right, bottom, top);
                for (int i = 1; i < 4; i++) Place(_bands[i], 0f, 0f, 0f, 0f);
                return;
            }

            var r = hole.Value;
            float hx0 = Mathf.Clamp(r.xMin, left, right);
            float hx1 = Mathf.Clamp(r.xMax, left, right);
            float hy0 = Mathf.Clamp(r.yMin, bottom, top);
            float hy1 = Mathf.Clamp(r.yMax, bottom, top);

            Place(_bands[0], left, right, hy1, top);      // above the hole
            Place(_bands[1], left, right, bottom, hy0);   // below it
            Place(_bands[2], left, hx0, hy0, hy1);        // left of it
            Place(_bands[3], hx1, right, hy0, hy1);       // right of it
        }

        private static void Place(RectTransform rt, float x0, float x1, float y0, float y1)
        {
            float w = Mathf.Max(0f, x1 - x0);
            float h = Mathf.Max(0f, y1 - y0);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x0 + w * 0.5f, y0 + h * 0.5f);
        }
    }
}
