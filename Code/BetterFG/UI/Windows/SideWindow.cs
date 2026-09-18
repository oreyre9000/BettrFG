using System;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Windows
{
    public class SideWindow : BetterFGWindow
    {
        public SideWindow(IntPtr ptr) : base(ptr) { }

        internal const float RowsLeftShift = 26f;

        internal const float RowPad = 6f;
        internal const float RowLabelX = RowPad + 20f + RowsLeftShift;
        internal const float RowSubLabelX = RowLabelX + 14f;
        internal const float RowRightPad = RowPad;

        private const float TitleLocalX = 21.0566f;
        private const float TitleNudgeY = 3f;
        private const float TitleNudgeScale = 1.08f;

        protected override string BgResourceName => "BetterFG.assets.ui.windows.generalbg_sidewheel.png";

        private bool _ringMasked;
        private Vector2 _baseOffMin, _baseOffMax;

        protected override void BuildRootGraphic(GameObject rootGo)
        {
            var wheel = BetterFG.UI.SideWheel.SideWheelManager.Instance;
            if (wheel == null) { base.BuildRootGraphic(rootGo); return; }

            rootGo.AddComponent<BetterFG.UI.SideWheel.RingHoleGraphic>()
                  .Init(wheel.RingRect, wheel.RingLocalRadius);
            rootGo.AddComponent<Mask>().showMaskGraphic = false;
            _ringMasked = true;
            _baseOffMin = new Vector2(0f, 0f);
            _baseOffMax = new Vector2(0f, -TITLE_H);
        }

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
            if (!_ringMasked) { base.ApplyContentTransform(); return; }
            if (_contentRt == null) return;

            _contentRt.localPosition = ContentPosition;
            _contentRt.localScale = ContentScale;
            var min = ContentOffsetMin ?? _baseOffMin;
            min.x -= RowsLeftShift;
            _contentRt.offsetMin = min;
            _contentRt.offsetMax = ContentOffsetMax ?? _baseOffMax;

            // the content rect hangs below the window rect (negative offsetMin.y plus a downward
            // ContentPosition), and that strip falls outside the panel art the Mask stencils against,
            // so the last row of a list gets sliced. pad the list by exactly that overhang.
            int pad = Mathf.RoundToInt(Mathf.Max(0f, -min.y - ContentPosition.y));
            foreach (var vlg in _contentRt.GetComponentsInChildren<VerticalLayoutGroup>(true))
            {
                if (vlg.padding.bottom == pad) continue;
                vlg.padding.bottom = pad;
                LayoutRebuilder.MarkLayoutForRebuild(vlg.GetComponent<RectTransform>());
            }
        }
    }
}
