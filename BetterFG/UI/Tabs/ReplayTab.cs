using System;
using UnityEngine;
using UnityEngine.UI;
using BetterFG.Utilities;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class ReplayTab : SwitchTab
    {
        public ReplayTab(IntPtr ptr) : base(ptr) { }

        protected override string BgResource => "BetterFG.assets.ui.tab.replay.png";


        protected const float HEADER_H = 20f;

        protected static readonly Color GREEN = new Color(0.22f, 0.42f, 0.26f, 1f);
        protected static readonly Color DARK = new Color(0.18f, 0.18f, 0.2f, 1f);
        protected static readonly Color ROW_ALT = new Color(1f, 1f, 1f, 0.03f);
        protected static readonly Color ROW_CLEAR = new Color(0f, 0f, 0f, 0f);
        protected static readonly Color ROW_HOVER = new Color(1f, 1f, 1f, 0.13f);
        protected static readonly Color ROW_PRESS = new Color(1f, 1f, 1f, 0.2f);
        protected static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        protected static readonly Color DIM = new Color(1f, 1f, 1f, 0.4f);
        protected static readonly Color UGC_COL = new Color(0.75f, 0.9f, 1f);

        Text _statusLbl;
        Button _prevBtn, _nextBtn;

        protected RectTransform ListContent;
        protected int Page;

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;
            float y = BuildHeader(contentRoot, PAD, w);

            float statusH = UIScale.LH;
            float statusY = TabHeight - PAD - statusH;

            var (_, content) = UGUIShip.CreateScrollView(contentRoot, new Rect(PAD, y, w, statusY - SH - y));
            ListContent = content;
            BuildListLayout(content);

            float pageBtnW = 52f;
            _prevBtn = UGUIShip.CreateButton(contentRoot, new Rect(PAD, statusY, pageBtnW, statusH), "ui.prev",
                DARK, Color.white, FS_SM, new Action(() => { Page--; RenderPage(); }));
            _nextBtn = UGUIShip.CreateButton(contentRoot, new Rect(PAD + w - pageBtnW, statusY, pageBtnW, statusH), "ui.next_2",
                DARK, Color.white, FS_SM, new Action(() => { Page++; RenderPage(); }));
            _statusLbl = UGUIShip.CreateLabel(contentRoot, new Rect(PAD + pageBtnW, statusY, w - pageBtnW * 2f, statusH),
                "", FS_SM, HINT, TextAnchor.MiddleCenter);

            PositionSwitchLink();
            Refresh();
        }

        protected virtual float BuildHeader(RectTransform root, float y, float w) => y;
        protected virtual void BuildListLayout(RectTransform content) { }
        protected virtual void Refresh() { }
        protected virtual void RenderPage() { }

        protected void SetStatus(string msg)
        {
            if (_statusLbl != null) UGUIShip.RelabelText(_statusLbl, msg);
        }

        protected void ShowPaging(bool on)
        {
            _prevBtn.gameObject.SetActive(on);
            _nextBtn.gameObject.SetActive(on);
        }

        protected void ClearList()
        {
            for (int i = ListContent.childCount - 1; i >= 0; i--)
                Destroy(ListContent.GetChild(i).gameObject);
        }
    }
}
