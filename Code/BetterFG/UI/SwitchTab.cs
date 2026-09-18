using System;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI
{
    // core mechanism for a tab that offers a "switch to another tab" link sitting right beside its
    // title (Plinths ⇄ In-game, Replays ⇄ Images, UI detail → back). subclasses only supply the link
    // text (SwitchLabel) and the target (MakeSwitchTarget); everything else — creating the link,
    // placing it next to the title, showing it only while the tab is open, and doing the slot swap —
    // lives here once. empty SwitchLabel = no link (a plain textured tab).
    public class SwitchTab : Tab
    {
        public SwitchTab(IntPtr ptr) : base(ptr) { }

        protected static readonly Color LINK = new Color(1f, 0.72f, 0.35f, 0.85f);

        private Text _titleText;
        private Text _switchLink;

        protected virtual string SwitchLabel => "";
        protected virtual Tab MakeSwitchTarget() => null;

        public override Tab MakeFallbackTab() => MakeSwitchTarget();

        protected override void BuildTitleExtras(Transform titleBar, Text title)
        {
            _titleText = title;
            // an empty label still leaves a full-size invisible Button behind (CreateLinkText always
            // makes one) sitting right next to the title text, over the title bar's own open/close
            // button - eating clicks there for nothing since SwitchNow() is a no-op with no target
            if (string.IsNullOrEmpty(SwitchLabel)) return;
            _switchLink = UGUIShip.CreateLinkText(title.transform, new Rect(0f, 0f, 90f, TITLE_H), SwitchLabel,
                new Action(SwitchNow), LINK, UIScale.FS_SM);
            _switchLink.gameObject.SetActive(IsOpen);
            PositionSwitchLink();
        }

        private void SwitchNow()
        {
            var target = MakeSwitchTarget();
            if (target != null) BetterFGUIMan.Instance?.SwitchSlotTab(this, target);
        }

        public override void OnOpened()
        {
            if (_switchLink != null) _switchLink.gameObject.SetActive(true);
            PositionSwitchLink();
        }

        public override void OnClosed()
        {
            if (_switchLink != null) _switchLink.gameObject.SetActive(false);
        }

        // sit the link just past the end of the title text. it's a child of the title label, anchored to
        // that label's left/middle, so it follows the nudge the label takes when the tab opens
        protected void PositionSwitchLink()
        {
            if (_switchLink == null) return;
            var rt = _switchLink.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, TITLE_H);
            rt.anchoredPosition = new Vector2(UGUIShip.PreferredWidth(_titleText) + UIScale.PAD * 1.5f, 0f);
        }
    }
}
