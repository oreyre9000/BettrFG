using System;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class UISubTab : SwitchTab
    {
        public UISubTab(IntPtr ptr) : base(ptr) { }

        protected override string BgResource => "BetterFG.assets.ui.tab.ui.png";
        protected override string SwitchLabel => "ui.back_4";
        protected override Tab MakeSwitchTarget() => BetterFGTabRegistry.NewTab<UITab>();

        protected static float dropdownRowH => BTN_H * 0.8f;

        protected static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        protected static readonly Color WHITE = UGUIShip.WHITE;
        protected static readonly Color BTN_APPLY = new Color(0.45f, 0.35f, 0.25f, 1f);
        protected static readonly Color BTN_REMOVE = UGUIShip.BTN_REMOVE;
        protected static readonly Color BTN_DARK = UGUIShip.BTN_DARK;
        protected static readonly Color SEL_COLOR = new Color(0.25f, 0.5f, 0.25f, 1f);
        protected static readonly Color BTN_ON = new Color(0.25f, 0.5f, 0.25f, 1f);

        // scroll view wrapped in its own slightly-inner outline frame, sitting below the preview.
        // returns the same (scrollRect, content) pair CreateScrollView gives.
        protected static (ScrollRect scrollRect, RectTransform content) BuildFramedScroll(RectTransform parent, float x, float y, float w, float h)
        {
            var frameGo = new GameObject("ScrollFrame");
            frameGo.transform.SetParent(parent, false);
            UGUIShip.SetPixelRect(frameGo.AddComponent<RectTransform>(), new Rect(x, y, w, h));
            var outGo = new GameObject("Outline");
            outGo.transform.SetParent(frameGo.transform, false);
            var oRt = outGo.AddComponent<RectTransform>();
            oRt.anchorMin = Vector2.zero; oRt.anchorMax = Vector2.one; oRt.offsetMin = oRt.offsetMax = Vector2.zero;
            var oImg = outGo.AddComponent<Image>();
            UGUIShip.ApplyDeluxPanelHighShadow(oImg);
            oImg.raycastTarget = false;
            return UGUIShip.CreateScrollView(frameGo.transform, new Rect(3f, 3f, w - 6f, h - 6f));
        }
    }
}
