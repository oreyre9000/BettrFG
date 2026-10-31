using System;
using BetterFG.UI.SideWheel;
using UnityEngine;
using BettrFG.uGUI;

namespace BetterFG.UI.Windows
{
    // a window you reach from inside another one (a tweak's CFG button, an entry's detail view).
    // same frame as any BetterFGWindow plus a back link beside the title that swaps the sidewheel
    // slot over to whatever MakeBackTarget hands back. empty BackLabel = no link.
    public class PartialWindow : BetterFGWindow
    {
        public PartialWindow(IntPtr ptr) : base(ptr) { }

        protected static readonly Color LINK = new Color(1f, 0.72f, 0.35f, 0.85f);

        protected virtual string BackLabel => "ui.back_5";
        protected virtual BetterFGWindow MakeBackTarget() => null;

        protected override void BuildTitleExtras(Transform titleRoot)
        {
            base.BuildTitleExtras(titleRoot);
            if (_titleText == null || string.IsNullOrEmpty(BackLabel)) return;

            // child of the title label so it inherits TitlePosition/TitleScale and stays glued to
            // the end of the text no matter how the window retunes its title
            var link = UGUIShip.CreateLinkText(_titleText.transform, new Rect(0f, 0f, 70f, TITLE_H),
                BackLabel, new Action(GoBack), LINK, FS_SM);
            var rt = link.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(UGUIShip.PreferredWidth(_titleText) + PAD, 0f);
        }

        private void GoBack()
        {
            var target = MakeBackTarget();
            if (target != null) SideWheelManager.Instance?.SwapWindow(target);
        }
    }
}
