using System;
using BetterFG.Nametag;
using UnityEngine;

namespace BetterFG.UI.Tab
{
    public class NametagWizardTab : WizardTab
    {
        public NametagWizardTab(IntPtr ptr) : base(ptr) { }

        private readonly NametagPreview _preview = new NametagPreview();

        protected override string BgResource => "BetterFG.assets.ui.nametag.bg.png";
        protected override float HeaderHeight => TabHeight * 0.22f;
        protected override bool HasRemove => true;
        protected override BetterFGTab MakeListTarget() => BetterFGTabRegistry.NewTab<NametagTab>();

        protected override void BuildHeader(RectTransform contentRoot, Rect area)
            => _preview.Build(contentRoot, area, RefreshPreview);

        protected void ApplyPreview(NametagIconApplicator.NametagCfg nameCfg, CrownRankService.CrownCfg crownCfg,
            bool platformHide, string platformCustom)
            => _preview.Apply(nameCfg, crownCfg, platformHide, platformCustom);

        public virtual void RefreshPreview() { }
    }
}
