using System;
using System.Collections.Generic;
using BetterFG.Customization.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Tab
{
    public class UIFontTab : UISubTab
    {
        public UIFontTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "UI - Font";

        static float ROW_H => 30f * UIScale.S;
        static readonly Color ROW_ALT = new Color(1f, 1f, 1f, 0.03f);
        static readonly Color ROW_CLEAR = new Color(0f, 0f, 0f, 0f);
        static readonly Color DIM = new Color(1f, 1f, 1f, 0.4f);
        static readonly Color BTN_ADD = new Color(0.3f, 0.3f, 0.15f, 1f);

        List<FontOverride> _entries = new List<FontOverride>();
        bool _fontMaster;
        Button _btnFontMaster;
        RectTransform _entryContent;
        Text _statusLbl;

        protected override void BuildContent(RectTransform contentRoot)
        {
            _entries = FontReplacementService.LoadAll();
            _fontMaster = FontReplacementService.MasterOn;

            float w = TabWidth - PAD * 2f;
            float y = UITab.BuildSectionBar(this, contentRoot, PAD, VPAD, w, "Font");

            _btnFontMaster = UGUIShip.CreateButton(contentRoot, new Rect(PAD, y, w, BTN_H),
                _fontMaster ? "FONT REPLACEMENT: ON" : "FONT REPLACEMENT: OFF",
                _fontMaster ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(OnToggleFontMaster));
            y += BTN_H + SH;

            float listH = TabHeight - y - LH - SH;
            var scroll = UGUIShip.CreateScrollView(contentRoot, new Rect(PAD, y, w, listH));
            _entryContent = scroll.content;
            var vlg = _entryContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(2, 2, 2, 2);
            vlg.spacing = 2f;
            vlg.childControlHeight = false;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            _entryContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            y += listH + SH;

            _statusLbl = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, y, w, LH), "", FS_SM, HINT, TextAnchor.MiddleCenter);

            RefreshEntryList();
            PositionSwitchLink();
        }

        public override void OnOpened()
        {
            base.OnOpened();
            _entries = FontReplacementService.LoadAll();
            RefreshEntryList();
        }

        void OnToggleFontMaster()
        {
            _fontMaster = !_fontMaster;
            var lbl = _btnFontMaster?.GetComponentInChildren<Text>();
            if (lbl != null) lbl.text = _fontMaster ? "FONT REPLACEMENT: ON" : "FONT REPLACEMENT: OFF";
            UGUIShip.SetButtonSelected(_btnFontMaster, _fontMaster, SEL_COLOR);
            FontReplacementService.SetMaster(_fontMaster);
            FontReplacementService.RebuildAndApply();
        }

        void RefreshEntryList()
        {
            if (_entryContent == null) return;
            for (int i = _entryContent.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_entryContent.GetChild(i).gameObject);

            float rowW = TabWidth - PAD * 2f - 8f;

            for (int i = 0; i < _entries.Count; i++)
            {
                int idx = i;
                var entry = _entries[i];

                var rowGo = new GameObject("FRow_" + i);
                rowGo.transform.SetParent(_entryContent, false);
                rowGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, ROW_H);
                var le = rowGo.AddComponent<LayoutElement>();
                le.preferredHeight = ROW_H;
                le.flexibleWidth = 1f;
                rowGo.AddComponent<Image>().color = i % 2 == 0 ? ROW_ALT : ROW_CLEAR;

                var fa = FontReplacementService.GetFontAssetByName(entry.targetFontName);
                var previewGo = new GameObject("Preview");
                previewGo.transform.SetParent(rowGo.transform, false);
                var previewRt = previewGo.AddComponent<RectTransform>();
                UGUIShip.SetPixelRect(previewRt, new Rect(3f, 3f, ROW_H - 6f, ROW_H - 6f));
                var previewTmp = previewGo.AddComponent<TMPro.TextMeshProUGUI>();
                previewTmp.text = "Aa";
                previewTmp.fontSize = 16f;
                previewTmp.alignment = TMPro.TextAlignmentOptions.Center;
                previewTmp.raycastTarget = false;
                if (fa != null) previewTmp.font = fa;

                float editW = 30f * UIScale.S, toggleW = 30f * UIScale.S, removeW = 22f * UIScale.S;
                float nameX = ROW_H + 3f;
                float nameW = rowW - editW - toggleW - removeW - nameX - 10f;

                string rowName = entry.entryName + "  →  " +
                    (string.IsNullOrEmpty(entry.targetFontName) ? "(no target)" : entry.targetFontName);
                UGUIShip.CreateLabel(rowGo.transform, new Rect(nameX, 0f, nameW, ROW_H),
                    rowName, FS_SM, entry.enabled ? WHITE : DIM, TextAnchor.MiddleLeft);

                BuildRowBtn(rowGo.transform, -(removeW + toggleW + editW + 4f), editW,
                    "edit", BTN_DARK, () => OpenWizard(idx));
                BuildRowBtn(rowGo.transform, -(removeW + toggleW + 2f), toggleW,
                    entry.enabled ? "on" : "off", entry.enabled ? BTN_APPLY : BTN_DARK,
                    () => OnToggleFontEntry(idx));
                BuildRowBtn(rowGo.transform, -2f, removeW, "x", BTN_REMOVE, () => OnRemoveFontEntry(idx));
            }

            var addBtn = UGUIShip.CreateButton(_entryContent, new Rect(0f, 0f, rowW, ROW_H),
                "+ Add Font Override", BTN_ADD, WHITE, FS_SM, new Action(() => OpenWizard(-1)));
            var addLe = addBtn.gameObject.AddComponent<LayoutElement>();
            addLe.preferredHeight = ROW_H;
            addLe.flexibleWidth = 1f;
        }

        Button BuildRowBtn(Transform parent, float anchoredX, float bw, string label, Color bg, Action onClick)
        {
            float bh = Mathf.Min(ROW_H - 6f, 24f * UIScale.S);
            var btn = UGUIShip.CreateButton(parent, new Rect(0f, 0f, bw, bh), label, bg, WHITE, FS_SM - 1, onClick);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(anchoredX, 0f);
            rt.sizeDelta = new Vector2(bw, bh);
            return btn;
        }

        void OpenWizard(int editIdx)
        {
            var wizard = BetterFGTabRegistry.NewTab<UIFontWizardTab>();
            wizard.EditIndex = editIdx;
            BetterFGUIMan.Instance?.SwitchSlotTab(this, wizard);
        }

        void OnToggleFontEntry(int idx)
        {
            _entries[idx].enabled = !_entries[idx].enabled;
            FontReplacementService.SaveAll(_entries);
            RefreshEntryList();
            FontReplacementService.RebuildAndApply();
        }

        void OnRemoveFontEntry(int idx)
        {
            _entries.RemoveAt(idx);
            FontReplacementService.SaveAll(_entries);
            RefreshEntryList();
            FontReplacementService.RebuildAndApply();
        }

        public void SetStatus(string msg)
        {
            if (_statusLbl != null) _statusLbl.text = msg;
        }
    }
}
