using System;
using BetterFG.Nametag;
using UnityEngine;

namespace BetterFG.UI.Tabs
{
    // shared base for the Nametag editors that need the live name+icon+crown preview (Colour, Icon,
    // Crown Rank - not Nameplate, which has its own backing preview and no use for this one). never
    // registered/instantiated directly, same as WizardTab itself.
    public class NametagWizardTab : WizardTab
    {
        public NametagWizardTab(IntPtr ptr) : base(ptr) { }

        private readonly NametagPreview _preview = new NametagPreview();

        protected override float HeaderHeight => TabHeight * 0.22f;

        protected override void BuildHeader(RectTransform contentRoot, Rect area)
            => _preview.Build(contentRoot, area, RefreshPreview);

        protected void ApplyPreview(NametagIconApplicator.NametagCfg nameCfg, CrownRankService.CrownCfg crownCfg,
            bool platformHide, string platformCustom)
            => _preview.Apply(nameCfg, crownCfg, platformHide, platformCustom);

        public virtual void RefreshPreview() { }
    }
}
