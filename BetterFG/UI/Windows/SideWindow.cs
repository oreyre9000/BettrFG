using System;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Windows
{
    public class SideWindow : BetterFGWindow
    {
        public SideWindow(IntPtr ptr) : base(ptr) { }

        private const float TitleLocalX = 21.0566f;
        private const float TitleNudgeY = 3f;
        private const float TitleNudgeScale = 1.08f;

        protected override string BgResourceName => "BetterFG.assets.ui.windows.generalbg_sidewheel.png";

        protected override void ApplyTitleTransform()
        {
            if (_titleLabelRt == null) return;
            _titleLabelRt.localPosition = new Vector3(
                TitleLocalX, TitlePosition.y + TitleNudgeY, TitlePosition.z);
            _titleLabelRt.localScale = new Vector3(
                TitleScale.x * TitleNudgeScale, TitleScale.y * TitleNudgeScale, TitleScale.z);
            _titleLabelRt.localRotation = Quaternion.Euler(22f, 345f, 0f);
        }

        protected override void ApplyContentTransform()
        {
            base.ApplyContentTransform();
            if (_contentRt == null) return;

            var min = ContentOffsetMin ?? Vector2.zero;
            int pad = Mathf.RoundToInt(Mathf.Max(0f, -min.y - ContentPosition.y));
            foreach (var vlg in _contentRt.GetComponentsInChildren<VerticalLayoutGroup>(true))
            {
                if (vlg.padding.bottom == pad || vlg.transform.parent.GetComponentInParent<VerticalLayoutGroup>(true) != null) continue;
                vlg.padding.bottom = pad;
                LayoutRebuilder.MarkLayoutForRebuild(vlg.GetComponent<RectTransform>());
            }
        }
    }
}
