using System;
using BetterFG.Nametag;
using TMPro;
using UnityEngine;

namespace BetterFG.UI.Tab
{
    public class NametagTab : BetterFGTab
    {
        public NametagTab(IntPtr ptr) : base(ptr) { }

        public static NametagTab Instance { get; private set; }

        void Awake() => Instance = this;

        protected override string BgResource => "BetterFG.assets.ui.nametag.bg.png";
        public override string TabTitle => "Nametag";

        private static readonly Color WHITE = Color.white;
        private static readonly Color BTN_DARK = new Color(0.2f, 0.2f, 0.2f, 1f);

        private static TMP_FontAsset _cachedFont;
        private static Material _cachedDefaultMat;
        private static Material _cachedGoldMat;

        public static void CacheNameAssets()
        {
            if (_cachedFont != null && _cachedDefaultMat != null && _cachedGoldMat != null) return;

            bool resolved = false;
            if (_cachedFont == null && (_cachedFont = BetterFG.Core.AssetManager.NameFontAsset) != null) resolved = true;
            if (_cachedDefaultMat == null && (_cachedDefaultMat = BetterFG.Core.AssetManager.DefaultNameMaterial) != null) resolved = true;
            if (_cachedGoldMat == null && (_cachedGoldMat = BetterFG.Core.AssetManager.GoldNameMaterial) != null) resolved = true;
            if (resolved) NametagPreview.Active?.Refresh();
        }

        private readonly NametagPreview _preview = new NametagPreview();

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;
            float y = VPAD;
            int rows = 4;

            float rowsBlockH = rows * BTN_H + (rows - 1) * SH;
            float prevH = TabHeight - VPAD * 2f - rowsBlockH - SH;

            _preview.Build(contentRoot, new Rect(PAD, y, w, prevH), RefreshPreview);
            y += prevH + SH;

            Row(contentRoot, y, w, "Colour & Style", OpenColour);
            y += BTN_H + SH;
            Row(contentRoot, y, w, "Icon", OpenIcon);
            y += BTN_H + SH;
            Row(contentRoot, y, w, "Nameplate", OpenNameplate);
            y += BTN_H + SH;
            Row(contentRoot, y, w, "Crown Rank", OpenCrown);
        }

        public override void OnOpened() => _preview.Refresh();

        private void Row(RectTransform parent, float y, float w, string label, Action onClick)
            => UGUIShip.CreateButton(parent, new Rect(PAD, y, w, BTN_H), label, BTN_DARK, WHITE, FS_SM, new Action(onClick));

        private void RefreshPreview()
        {
            var cfg = NametagIconApplicator.CfgFromSettings();
            cfg.enabled = true;
            _preview.Apply(cfg, CrownRankService.CfgFromSettings(),
                NametagPreview.PlatformHideFromSettings(), NametagPreview.PlatformCustomFromSettings());
        }

        private void OpenColour() => BetterFGUIMan.Instance?.SwitchSlotTab(this, BetterFGTabRegistry.NewTab<NametagColourTab>());
        private void OpenIcon() => BetterFGUIMan.Instance?.SwitchSlotTab(this, BetterFGTabRegistry.NewTab<NametagIconTab>());
        private void OpenNameplate() => BetterFGUIMan.Instance?.SwitchSlotTab(this, BetterFGTabRegistry.NewTab<NametagNameplateTab>());
        private void OpenCrown() => BetterFGUIMan.Instance?.SwitchSlotTab(this, BetterFGTabRegistry.NewTab<NametagCrownTab>());
    }
}
