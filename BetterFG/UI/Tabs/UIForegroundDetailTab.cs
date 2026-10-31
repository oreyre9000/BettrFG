using System;
using BetterFG.Customization.UI;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public enum UIForegroundKind { CustomUI, Qualified, Eliminated, EliminatedSquad, Winner, RoundOver, Nametag }

    public partial class UIForegroundDetailTab : UISubTab
    {
        public UIForegroundDetailTab(IntPtr ptr) : base(ptr) { }

        public UIForegroundKind What;

        public override string TabTitle => BetterFG.Services.LocalizationService.Format("foreground.tab_title", BetterFG.Services.LocalizationService.Get(Label(What)));

        public static string Label(UIForegroundKind k)
        {
            switch (k)
            {
                case UIForegroundKind.Qualified: return "foreground.banner.qualified";
                case UIForegroundKind.Eliminated: return "foreground.banner.eliminated";
                case UIForegroundKind.EliminatedSquad: return "foreground.banner.eliminated_squad";
                case UIForegroundKind.Winner: return "foreground.banner.winner";
                case UIForegroundKind.RoundOver: return "foreground.banner.round_over";
                case UIForegroundKind.Nametag: return "foreground.banner.nametag";
                default: return "foreground.banner.custom";
            }
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;
            float y = UITab.BuildSectionBar(this, contentRoot, PAD, VPAD, w, "ui.foreground");

            float btnRowH = BTN_H + PAD * 2f + 1f;
            float bodyH = TabHeight - y - VPAD - btnRowH;

            var bodyGo = new GameObject("FgBody");
            bodyGo.transform.SetParent(contentRoot, false);
            var bodyRt = bodyGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(bodyRt, new Rect(0f, y, TabWidth, bodyH));

            BuildBannerPanel(bodyRt, PAD, 0f, w, bodyH, What);
            RefreshBannerPreview();

            float by = y + bodyH + PAD;
            UGUIShip.CreatePanel(contentRoot, new Rect(PAD, by, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            by += 1f + PAD;
            float btnw = (w - PAD) / 3f;
            UGUIShip.CreateButton(contentRoot, new Rect(PAD, by, btnw, BTN_H),
                "ui.apply", BTN_APPLY, WHITE, FS, new Action(() => OnBannerApply(What)));
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + btnw + PAD * 0.5f, by, btnw, BTN_H),
                "ui.enable_all_2", BTN_ON, WHITE, FS_SM, new Action(() => SetAllEnabled(true)));
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + (btnw + PAD * 0.5f) * 2f, by, btnw, BTN_H),
                "ui.disable_all_2", BTN_REMOVE, WHITE, FS_SM, new Action(() => SetAllEnabled(false)));

            PositionSwitchLink();
        }

        private void SetAllEnabled(bool on)
        {
            var def = GetBannerDef(What);
            if (def == null) return;
            def.enabled = on;
            SettingsService.Set(def.enabledKey, on ? "true" : "false");
            var elbl = def.enabledBtn?.GetComponentInChildren<Text>();
            if (elbl != null) UGUIShip.RelabelText(elbl, on ? "ui.custom_colours_on_2" : "ui.custom_colours_off_2");
            UGUIShip.SetButtonSelected(def.enabledBtn, on, SEL_COLOR);
            foreach (var s in def.slots) SetBannerToggle(s.ui, on);
            SetBannerToggle(def.highlight.ui, on);
            UpdateBannerPreviewColours();
            OnBannerApply(What);
        }
    }
}
