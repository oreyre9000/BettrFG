using UnityEngine;

namespace BetterFG.Utilities
{
    // where a widget on some other canvas lands on ours. everything BettrFG draws sits on its own
    // canvas with its own scaler, so the only common ground is screen space.
    public static class CanvasGeom
    {
        public static Vector2 PointOf(Canvas canvas, RectTransform target, Vector2 normalised)
        {
            var world = target.TransformPoint(new Vector3(
                (normalised.x - target.pivot.x) * target.rect.width,
                (normalised.y - target.pivot.y) * target.rect.height, 0f));
            var screen = RectTransformUtility.WorldToScreenPoint(null, world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas.GetComponent<RectTransform>(), screen, null, out var local);
            return local;
        }

        // tab titles and wheel icons are tilted in 3D, so take the bounding box of the four projected
        // corners rather than pretending the rect is axis-aligned.
        public static Rect AabbOf(Canvas canvas, RectTransform target, float pad)
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                var p = PointOf(canvas, target, new Vector2(i == 0 || i == 3 ? 0f : 1f, i < 2 ? 0f : 1f));
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }
            return Rect.MinMaxRect(minX - pad, minY - pad, maxX + pad, maxY + pad);
        }

        // popups sit just off the target's right edge, level with it
        public static Vector2 Beside(Rect box) => new Vector2(box.xMax + 40f, box.center.y);
    }
}
