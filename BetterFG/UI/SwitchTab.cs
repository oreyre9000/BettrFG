using System;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI
{
    public class SwitchTab : BetterFGTab
    {
        public SwitchTab(IntPtr ptr) : base(ptr) { }

        protected static readonly Color LINK = new Color(1f, 0.72f, 0.35f, 0.85f);

        private Text _titleText;
        private Text _switchLink;

        protected virtual string SwitchLabel => "";
        protected virtual BetterFGTab MakeSwitchTarget() => null;

        protected override void BuildTitleExtras(Transform titleBar, Text title)
        {
            _titleText = title;
            if (string.IsNullOrEmpty(SwitchLabel)) return;
            _switchLink = UGUIShip.CreateLinkText(title.transform, new Rect(0f, 0f, 90f, TITLE_H), SwitchLabel,
                new Action(SwitchNow), LINK, UIScale.FS_SM);
            _switchLink.gameObject.SetActive(IsOpen);
            PositionSwitchLink();
            _titleText.RegisterDirtyVerticesCallback(new Action(PositionSwitchLink));
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

        protected void PositionSwitchLink()
        {
            if (_switchLink == null) return;
            var rt = _switchLink.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, TITLE_H);
            rt.anchoredPosition = new Vector2(_titleText.preferredWidth + UIScale.PAD * 1.5f, 0f);
        }
    }
}
