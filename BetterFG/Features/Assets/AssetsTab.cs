using System;
using BetterFG.UI;
using BettrFG.uGUI;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Features.Assets
{
    public class AssetsTab : Tab
    {
        public AssetsTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Assets";
        protected override string TitleId => "asset_export.tab_title";
        protected override string BgResource => "BetterFG.assets.ui.tab.customskintexture.png";

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;
            float cy = VPAD + LH;

            UGUIShip.CreateLabel(contentRoot, new Rect(PAD, cy, w, LH * 4f),
                "assets.tab_blurb", FS_SM, new Color(1f, 1f, 1f, 0.72f)).alignment = TextAnchor.UpperLeft;
            cy += LH * 4f + SH * 2f;

            UGUIShip.CreateButton(contentRoot, new Rect(PAD, cy, w, BTN_H * 1.4f),
                "assets.open", new Color(0.22f, 0.34f, 0.55f, 1f), UGUIShip.WHITE, FS_SM,
                new Action(AssetsScreen.Open));
        }
    }
}
