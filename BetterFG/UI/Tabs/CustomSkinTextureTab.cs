using System;
using System.Collections.Generic;
using BetterFG.Customization.Player;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using LayoutElement = UnityEngine.UI.LayoutElement;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class CustomSkinTextureTab : Tab
    {
        public CustomSkinTextureTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Skin Texture";
        protected override string TitleId => "ui.skin_texture";
        protected override string BgResource => "BetterFG.assets.ui.tab.customskintexture.png";


        private static readonly Color BTN_DARK = UGUIShip.BTN_DARK;
        private static readonly Color BTN_APPLY = new Color(0.25f, 0.45f, 0.25f, 1f);
        private static readonly Color BTN_REMOVE = UGUIShip.BTN_REMOVE;
        private static readonly Color BTN_ADD = new Color(0.3f, 0.3f, 0.15f, 1f);
        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color DIM = new Color(1f, 1f, 1f, 0.4f);
        private static readonly Color WHITE = UGUIShip.WHITE;

        private static float ROW_H => 30f * UIScale.S;

        private List<SkinTexEntry> _entries = new List<SkinTexEntry>();
        private int _selectedEntry = -1;

        private RectTransform _entryContent;
        private Text _statusLbl;

        private static readonly List<CustomSkinTextureTab> _live = new List<CustomSkinTextureTab>();

        void Awake() => _live.Add(this);
        void OnDestroy() => _live.Remove(this);

        public static void RefreshLiveInstances()
        {
            for (int i = 0; i < _live.Count; i++)
                _live[i]?.RefreshEntryList();
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            _entries = SkinApplicationService.LoadEntries();

            float w = TabWidth - PAD * 2f;
            float y = VPAD;

            float listH = TabHeight - y - BTN_H - LH - VPAD - 4f;
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
            y += listH + 2f;

            float halfW = (w - PAD * 0.5f) / 2f;
            UGUIShip.CreateButton(contentRoot, new Rect(PAD, y, halfW, BTN_H),
                "ui.apply_selected", BTN_APPLY, WHITE, FS_SM, new Action(OnApplySelected));
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + halfW + PAD * 0.5f, y, halfW, BTN_H),
                "ui.revert_all", BTN_REMOVE, WHITE, FS_SM, new Action(OnRevert));
            y += BTN_H + 2f;

            _statusLbl = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, y, w, LH), "", FS_SM, HINT, TextAnchor.MiddleCenter);

            RefreshEntryList();
        }

        public override void OnOpened()
        {
            _entries = SkinApplicationService.LoadEntries();
            if (_selectedEntry >= _entries.Count) _selectedEntry = _entries.Count - 1;
            RefreshEntryList();
        }

        private void RefreshEntryList()
        {
            if (_entryContent == null) return;

            for (int i = _entryContent.childCount - 1; i >= 0; i--)
            {
                var ch = _entryContent.GetChild(i);
                if (ch != null) GameObject.Destroy(ch.gameObject);
            }

            _rows.Clear();
            float rowW = TabWidth - PAD * 2f - 8f;

            for (int i = 0; i < _entries.Count; i++)
            {
                int idx = i;
                var entry = _entries[i];

                var rowGo = new GameObject("ERow_" + i);
                rowGo.transform.SetParent(_entryContent, false);
                rowGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, ROW_H);
                var le = rowGo.AddComponent<LayoutElement>();
                le.preferredHeight = ROW_H;
                le.flexibleWidth = 1f;
                rowGo.AddComponent<Image>().color = WHITE;

                var rowBtn = rowGo.AddComponent<Button>();
                var nav = rowBtn.navigation;
                nav.mode = UnityEngine.UI.Navigation.Mode.None;
                rowBtn.navigation = nav;
                rowBtn.onClick.AddListener(new Action(() =>
                {
                    AudioService.PlayButtonClick();
                    SelectEntry(idx);
                }));

                float thumbSz = (ROW_H - 6f) * 1.4f;
                var thumbGo = new GameObject("Thumb");
                thumbGo.transform.SetParent(rowBtn.transform, false);
                var thumbRt = thumbGo.AddComponent<RectTransform>();
                UGUIShip.SetPixelRect(thumbRt, new Rect(3f + thumbSz * 0.2f, 3f - thumbSz * 0.2f, thumbSz, thumbSz));
                var raw = thumbGo.AddComponent<RawImage>();
                raw.raycastTarget = false;
                Texture thumb = SkinApplicationService.ResolveOptionIconTexture(entry.category, entry.costumeName);
                if (thumb == null) thumb = SkinApplicationService.GetCachedCustomTex(entry);
                if (thumb != null) raw.texture = thumb;
                else raw.color = new Color(0f, 0f, 0f, 0.4f);

                float editW = 30f * UIScale.S, toggleW = 30f * UIScale.S, removeW = 22f * UIScale.S;
                float nameX = 3f + thumbSz * 1.2f + 6f;
                float nameW = rowW - editW - toggleW - removeW - nameX - 10f;

                var nameLbl = UGUIShip.CreateLabel(rowBtn.transform,
                    new Rect(nameX, 0f, nameW, ROW_H), entry.entryName,
                    FS_SM, WHITE, TextAnchor.MiddleLeft);

                UGUIShip.CreateRowEndButton(rowBtn.transform, -(removeW + toggleW + editW + 4f), editW, ROW_H,
                    "ui.edit_2", BTN_DARK, () => OpenWizard(idx));

                var toggleBtn = UGUIShip.CreateRowEndButton(rowBtn.transform, -(removeW + toggleW + 2f), toggleW, ROW_H,
                    "ui.on_2", BTN_APPLY, () => ToggleEntry(idx));

                UGUIShip.CreateRowEndButton(rowBtn.transform, -2f, removeW, ROW_H, "x", BTN_REMOVE, () => RemoveEntry(idx));

                _rows.Add(new RowRefs
                {
                    Row = rowBtn,
                    Name = nameLbl,
                    Toggle = toggleBtn,
                    ToggleLbl = toggleBtn.GetComponentInChildren<Text>()
                });
            }

            PaintRows();

            var addBtn = UGUIShip.CreateButton(_entryContent, new Rect(0f, 0f, TabWidth - PAD * 2f - 8f, ROW_H),
                "ui.add_texture", BTN_ADD, WHITE, FS, new Action(() => OpenWizard(-1)));
            var addLe = addBtn.gameObject.AddComponent<LayoutElement>();
            addLe.preferredHeight = ROW_H;
            addLe.flexibleWidth = 1f;
        }

        private struct RowRefs
        {
            public Button Row;
            public Button Toggle;
            public Text ToggleLbl;
            public Text Name;
        }

        private readonly List<RowRefs> _rows = new List<RowRefs>();

        private void PaintRows()
        {
            for (int i = 0; i < _rows.Count && i < _entries.Count; i++)
            {
                var entry = _entries[i];
                var row = _rows[i];

                UGUIShip.PaintListRow(row.Row, i, i == _selectedEntry);

                UGUIShip.SetButtonColor(row.Toggle, entry.enabled ? BTN_APPLY : BTN_DARK);
                UGUIShip.RelabelText(row.ToggleLbl, entry.enabled ? "ui.on_2" : "ui.off_2");
                row.Name.color = entry.enabled ? WHITE : DIM;
            }
        }

        private void OpenWizard(int editIdx)
        {
            var wizard = BetterFGTabRegistry.NewTab<SkinTextureWizardTab>();
            wizard.EditIndex = editIdx;
            BetterFGUIMan.Instance?.SwitchSlotTab(this, wizard);
        }

        private void SelectEntry(int idx)
        {
            if (_selectedEntry == idx)
            {
                _selectedEntry = -1;
                PaintRows();
                return;
            }
            _selectedEntry = idx;
            PaintRows();
            SetStatus(_entries[idx].entryName + " selected");
        }

        private void ToggleEntry(int idx)
        {
            _entries[idx].enabled = !_entries[idx].enabled;
            SkinApplicationService.SaveEntries(_entries);
            PaintRows();
            RevertAllEnabled();
        }

        private void RemoveEntry(int idx)
        {
            bool wasEnabled = _entries[idx].enabled;
            _entries.RemoveAt(idx);
            if (_selectedEntry >= _entries.Count) _selectedEntry = _entries.Count - 1;
            SkinApplicationService.SaveEntries(_entries);
            RefreshEntryList();

            if (wasEnabled) RevertAllEnabled();
        }

        private void OnApplySelected()
        {
            if (_selectedEntry < 0 || _selectedEntry >= _entries.Count)
            {
                SetStatus("ui.select_an_entry_first");
                return;
            }
            if (!_entries[_selectedEntry].enabled) { SetStatus("ui.entry_is_disabled"); return; }
            RevertAllEnabled();
        }

        private void RevertAllEnabled()
            => SkinApplicationService.ReapplyAllEnabled(_entries, SetStatus);

        private void OnRevert()
        {
            SkinApplicationService.RevertAllBeans();
            SetStatus("ui.reverted");
        }

        public void SetStatus(string msg)
        {
            if (_statusLbl != null) UGUIShip.RelabelText(_statusLbl, msg);
        }
    }
}
