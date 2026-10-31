using System;
using BetterFG.Customization.UI;
using BetterFG.Customization.UI.Styles;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class LayoutTab : Tab
    {
        public LayoutTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Layout";
        protected override string TitleId => "layout.title";
        protected override string BgResource => "BetterFG.assets.ui.tab.layout.png";

        private static readonly Color WHITE = UGUIShip.WHITE;
        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color BTN_DARK = UGUIShip.BTN_DARK;

        private Text[] _countLabels;
        private Text _statusLabel;
        private TooltipTrigger[] _locks;
        private CanvasGroup _listCg;
        private Dropdown _styleDd;
        private System.Collections.Generic.List<string> _styleIds;

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;
            float y = VPAD;
            UGUIShip.CreateLabel(contentRoot, new Rect(PAD, y, w * 0.4f, BTN_H), "layout.style", FS, WHITE, TextAnchor.MiddleLeft);
            var styleIds = new System.Collections.Generic.List<string>();
            int selected = 0;
            for (int i = 0; i < UIStyles.All.Length; i++)
            {
                styleIds.Add(UIStyles.All[i].LabelId);
                if (UIStyles.All[i] == UIStyles.Active) selected = i;
            }
            _styleIds = styleIds;
            _styleDd = UGUIShip.CreateDropdown(contentRoot, new Rect(PAD + w * 0.4f, y, w * 0.6f, BTN_H), styleIds, selected,
                i => { UIStyles.Select(UIStyles.All[i].Id); RefreshLock(); }, FS);
            y += BTN_H + SH;
            var kinds = MenuLayoutService.Kinds;
            float itemH = BTN_H * 2.6f;
            float listH = TabHeight - y - BTN_H - SH - VPAD;

            var (_, content) = UGUIShip.CreateScrollView(contentRoot, new Rect(PAD, y, w, listH));
            content.sizeDelta = new Vector2(0f, kinds.Length * (itemH + SH) - SH);
            float iw = w - UGUIShip.SCROLLBAR_INSET * 2f;
            float pw = (itemH - 10f) * 16f / 9f;
            float textX = pw + 14f;
            float textW = iw - textX - 8f;

            _countLabels = new Text[kinds.Length];
            _locks = new TooltipTrigger[kinds.Length];
            _listCg = content.gameObject.AddComponent<CanvasGroup>();
            for (int i = 0; i < kinds.Length; i++)
            {
                var k = kinds[i];
                var btn = UGUIShip.CreateButton(content, new Rect(0f, i * (itemH + SH), iw, itemH), k.LabelId,
                    BTN_DARK, WHITE, FS, new Action(() => OpenEditor(k)));
                _locks[i] = TooltipTrigger.Attach(btn.gameObject);
                _locks[i].locId = "layout.style.locked_tip";
                _locks[i].instant = true;
                _locks[i].InstallLoc();
                var name = btn.GetComponentInChildren<Text>();
                UGUIShip.SetPixelRect(name.rectTransform, new Rect(textX, itemH * 0.5f - BTN_H, textW, BTN_H));
                name.alignment = TextAnchor.LowerLeft;

                _countLabels[i] = UGUIShip.CreateLabel(btn.transform, new Rect(textX, itemH * 0.5f, textW, BTN_H),
                    "", FS_SM, HINT, TextAnchor.UpperLeft);

                var (frame, inner) = UGUIShip.CreateFramedImage(btn.transform);
                UGUIShip.SetPixelRect(frame, new Rect(5f, 5f, pw, itemH - 10f));
                var img = inner.gameObject.AddComponent<RawImage>();
                img.texture = k.Preview;
                img.raycastTarget = false;
            }

            _statusLabel = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, y + listH + SH, w, BTN_H),
                "", FS_SM, HINT, TextAnchor.MiddleLeft);

            RefreshCount();
            RefreshLock();
        }

        private void RefreshLock()
        {
            if (_locks == null) return;
            foreach (var l in _locks) l.enabled = UIStyles.Styled;
            _listCg.alpha = UIStyles.Styled ? 0.45f : 1f;
            if (!UIStyles.Styled) UGUIShip.RelabelText(_statusLabel, "");
        }

        private void OpenEditor(MenuLayoutService.Kind kind)
        {
            if (UIStyles.Styled)
            {
                _statusLabel.text = LocalizationService.Format("layout.style.locked", LocalizationService.Get(UIStyles.Active.LabelId));
                return;
            }
            if (!MenuLayoutEditor.CanOpen(kind))
            {
                UGUIShip.RelabelText(_statusLabel, "layout.need_menu");
                return;
            }
            UGUIShip.RelabelText(_statusLabel, "");
            MenuLayoutEditor.Open(kind);
        }

        private void RefreshCount()
        {
            if (_countLabels == null) return;
            for (int i = 0; i < _countLabels.Length; i++)
                _countLabels[i].text = LocalizationService.Format("layout.overrides", MenuLayoutService.Count(MenuLayoutService.Kinds[i]));
        }

        protected override void RefreshLocalized()
        {
            RefreshCount();
            for (int i = 0; i < _styleIds.Count; i++) _styleDd.options[i].text = LocalizationService.Get(_styleIds[i]);
            _styleDd.RefreshShownValue();
        }

        public override void OnOpened() => RefreshCount();
    }
}
