using System;
using BetterFG.Core;
using BetterFG.Nametag;
using TMPro;
using UnityEngine;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class NametagTab : Tab
    {
        public NametagTab(IntPtr ptr) : base(ptr) { }

        protected override string BgResource => "BetterFG.assets.ui.nametag.bg.png";
        public override string TabTitle => "Nametag";
        protected override string TitleId => "ui.nametag";

        private static readonly Color WHITE = UGUIShip.WHITE;
        private static readonly Color BTN_DARK = UGUIShip.BTN_DARK;

        private static TMP_FontAsset _cachedFont;
        private static Material _cachedDefaultMat;
        private static Material _cachedGoldMat;

        public static void CacheNameAssets()
        {
            if (_cachedFont != null && _cachedDefaultMat != null && _cachedGoldMat != null) return;

            bool resolved = false;
            if (_cachedFont == null && (_cachedFont = AssetManager.NameFontAsset) != null) resolved = true;
            if (_cachedDefaultMat == null && (_cachedDefaultMat = AssetManager.DefaultNameMaterial) != null) resolved = true;
            if (_cachedGoldMat == null && (_cachedGoldMat = AssetManager.GoldNameMaterial) != null) resolved = true;
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

            Row(contentRoot, y, w, "nametag.colour_style", OpenColour);
            y += BTN_H + SH;
            Row(contentRoot, y, w, "nametag.icon", OpenIcon);
            y += BTN_H + SH;
            Row(contentRoot, y, w, "nametag.nameplate", OpenNameplate);
            y += BTN_H + SH;
            Row(contentRoot, y, w, "nametag.crown_rank", OpenCrown);
        }

        private void Row(RectTransform parent, float y, float w, string label, Action onClick)
            => UGUIShip.CreateButton(parent, new Rect(PAD, y, w, BTN_H), label, BTN_DARK, WHITE, FS_SM, new Action(onClick));

        private void RefreshPreview()
        {
            var cfg = NametagIconApplicator.CfgFromSettings();
            cfg.enabled = true;
            var crownCfg = CrownRankService.CfgFromSettings();
            _preview.Apply(cfg, crownCfg, NametagIconApplicator.PlatformHideFromSettings(), NametagIconApplicator.PlatformCustomFromSettings());
        }

        private void OpenColour() => BetterFGUIMan.Instance?.SwitchSlotTab(this, BetterFGTabRegistry.NewTab<NametagColourTab>());
        private void OpenIcon() => BetterFGUIMan.Instance?.SwitchSlotTab(this, BetterFGTabRegistry.NewTab<NametagIconTab>());
        private void OpenNameplate() => BetterFGUIMan.Instance?.SwitchSlotTab(this, BetterFGTabRegistry.NewTab<NametagNameplateTab>());
        private void OpenCrown() => BetterFGUIMan.Instance?.SwitchSlotTab(this, BetterFGTabRegistry.NewTab<NametagCrownTab>());
    }
}
