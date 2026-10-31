using System;
using UnityEngine;

namespace BetterFG.UI.Tab
{
    public class UISubTab : SwitchTab
    {
        public UISubTab(IntPtr ptr) : base(ptr) { }

        protected override string BgResource => "BetterFG.assets.ui.tab.ui.png";
        protected override string SwitchLabel => "‹ back";
        protected override BetterFGTab MakeSwitchTarget() => BetterFGTabRegistry.NewTab<UITab>();

        protected static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        protected static readonly Color WHITE = Color.white;
        protected static readonly Color BTN_APPLY = new Color(0.45f, 0.35f, 0.25f, 1f);
        protected static readonly Color BTN_REMOVE = new Color(0.55f, 0.15f, 0.15f, 1f);
        protected static readonly Color BTN_DARK = new Color(0.2f, 0.2f, 0.2f, 1f);
        protected static readonly Color SEL_COLOR = new Color(0.25f, 0.5f, 0.25f, 1f);
        protected static readonly Color BTN_ON = new Color(0.25f, 0.5f, 0.25f, 1f);
    }
}
