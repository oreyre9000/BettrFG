using UnityEngine;
using UnityEngine.UI;

namespace BettrFG.uGUI
{
    public static partial class UGUIShip
    {
        public static LoadingBar CreateLoadingBar(Transform parent, Rect rect, Color fillColour)
        {
            var trackGo = new GameObject("LoadingBar");
            trackGo.transform.SetParent(parent, false);
            SetPixelRect(trackGo.AddComponent<RectTransform>(), rect);
            var trackImg = trackGo.AddComponent<Image>();
            ApplyDeluxBase(trackImg, 3f);
            trackImg.color = new Color(0f, 0f, 0f, 0.55f);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(trackGo.transform, false);
            var fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.pivot = new Vector2(0f, 0.5f);
            ApplyDeluxSkin(fillGo, fillGo.AddComponent<Image>(), fillColour, true, 3f);

            var outGo = new GameObject("Outline");
            outGo.transform.SetParent(trackGo.transform, false);
            var oRt = outGo.AddComponent<RectTransform>();
            oRt.anchorMin = Vector2.zero;
            oRt.anchorMax = Vector2.one;
            oRt.offsetMin = oRt.offsetMax = Vector2.zero;
            var oImg = outGo.AddComponent<Image>();
            ApplyDeluxPanelOutline(oImg, 3f);
            oImg.raycastTarget = false;

            var bar = trackGo.AddComponent<LoadingBar>();
            bar.Bind(fillRt, 2f);
            return bar;
        }

        public static LoadingBar CreateLoadingBar(Transform parent, Color fillColour)
        {
            var trackGo = new GameObject("LoadingBar");
            trackGo.transform.SetParent(parent, false);
            var trackRt = trackGo.AddComponent<RectTransform>();
            trackRt.anchorMin = new Vector2(0f, 0f);
            trackRt.anchorMax = new Vector2(1f, 0f);
            trackRt.pivot = new Vector2(0.5f, 0f);
            trackRt.offsetMin = Vector2.zero;
            trackRt.offsetMax = new Vector2(0f, 2f);
            trackGo.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(trackGo.transform, false);
            var fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillGo.AddComponent<Image>().color = fillColour;

            var bar = trackGo.AddComponent<LoadingBar>();
            bar.Bind(fillRt, 0f);
            return bar;
        }
    }
}
