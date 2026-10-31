using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Customization.UI.Styles
{
    public static class StyleGlyphs
    {
        public static ActiveControllerUI Live(Transform stock)
        {
            if (stock == null) return null;
            ActiveControllerUI first = null;
            foreach (var a in stock.GetComponentsInChildren<ActiveControllerUI>(true))
            {
                if (a.gameObject.activeInHierarchy) return a;
                first ??= a;
            }
            return first;
        }

        public static void Bind(Image target, ActiveControllerUI src)
        {
            if (target == null || src == null) return;
            var go = target.gameObject;
            var old = go.GetComponent<ActiveControllerUI>();
            if (old != null) Object.DestroyImmediate(old);
            bool was = go.activeSelf;
            go.SetActive(false);
            var a = go.AddComponent<ActiveControllerUI>();
            a._isUIButtonShortcut = src._isUIButtonShortcut;
            a._mappeable = src._mappeable;
            a._preferenceAxisGlyph = src._preferenceAxisGlyph;
            a.RewiredAction = src.RewiredAction;
            a.RewiredCategory = src.RewiredCategory;
            a._axisContribution = src._axisContribution;
            a._notMappeableJoystickSprite = src._notMappeableJoystickSprite;
            a._notMappeableKeyboardSprite = src._notMappeableKeyboardSprite;
            a._notMappeableJoystickGlyphText = src._notMappeableJoystickGlyphText;
            a._notMappeableKeyboardGlyphText = src._notMappeableKeyboardGlyphText;
            a._switchScaleOverride = src._switchScaleOverride;
            a._spriteManager = src._spriteManager;
            a._image = target;
            a._text = target.GetComponentInChildren<TextMeshProUGUI>(true);
            target.sprite = src._image != null ? src._image.sprite : target.sprite;
            go.SetActive(was);
        }
    }
}
