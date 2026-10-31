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
            ApplyDeluxPanelHighShadow(trackGo.AddComponent<Image>(), 3f);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(trackGo.transform, false);
            var fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.pivot = new Vector2(0f, 0.5f);
            ApplyDeluxSkin(fillGo, fillGo.AddComponent<Image>(), fillColour, true, 3f);

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
            trackRt.offsetMax = new Vector2(0f, 6f);
            ApplyDeluxPanelHighShadow(trackGo.AddComponent<Image>(), 3f);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(trackGo.transform, false);
            var fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.pivot = new Vector2(0f, 0.5f);
            ApplyDeluxSkin(fillGo, fillGo.AddComponent<Image>(), fillColour, true, 3f);

            var bar = trackGo.AddComponent<LoadingBar>();
            bar.Bind(fillRt, 1f);
            return bar;
        }
    }
}
