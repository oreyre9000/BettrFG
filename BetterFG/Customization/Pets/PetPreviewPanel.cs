using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.Customization.Pets
{
    // above-tab preview panel shared by every pet tab (list, wizard, look picker, skin texture,
    // phrases) - lives above the tab's own title bar, outside its normal content bounds, so it
    // stays visible and doesn't rebuild as the user moves between tabs/steps
    internal static class PetPreviewPanel
    {
        public const float Width = 200f;
        public const float Height = 140f;

        public static (GameObject frame, RawImage img) Build(RectTransform root, float tabWidth, float tabHeight, float titleH, float sh, float scale)
        {
            float w = Width * scale, h = Height * scale;

            var frameGo = new GameObject("PetPreview_Above");
            frameGo.transform.SetParent(root, false);
            var frameRt = frameGo.AddComponent<RectTransform>();
            frameRt.anchorMin = frameRt.anchorMax = Vector2.zero;
            frameRt.pivot = Vector2.zero;
            frameRt.sizeDelta = new Vector2(w, h);
            frameRt.anchoredPosition = new Vector2((tabWidth - w) * 0.5f, tabHeight + titleH + sh);
            var (_, inner) = UGUIShip.CreateFramedImage(frameGo.transform, new Color(0f, 0f, 0f, 0.7f));
            var img = inner.gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            img.texture = PetPreview.Ensure();
            return (frameGo, img);
        }
    }
}
