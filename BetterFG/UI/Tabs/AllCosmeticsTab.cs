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
    public class AllCosmeticsTab : Tab
    {
        public AllCosmeticsTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "All Cosmetics";
        protected override string TitleId => "ui.all_cosmetics_2";
        protected override string BgResource => "BetterFG.assets.ui.tab.allcosm.png";


        private static readonly Color BTN_DARK = UGUIShip.BTN_DARK;
        private static readonly Color BTN_APPLY = new Color(0.25f, 0.45f, 0.25f, 1f);
        private static readonly Color BTN_ADD = new Color(0.3f, 0.3f, 0.15f, 1f);
        private static readonly Color BTN_REMOVE = UGUIShip.BTN_REMOVE;
        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color DIM = new Color(1f, 1f, 1f, 0.4f);
        private static readonly Color WHITE = UGUIShip.WHITE;

        private static float ROW_H => 30f * UIScale.S;

        private List<GameCosmeticEntry> _entries = new List<GameCosmeticEntry>();
        private int _selectedEntry = -1;

        private RectTransform _entryContent;
        private Text _statusLbl;
        private bool _subscribed;

        private void EnsureSubscribed()
        {
            if (_subscribed) return;
            var svc = SkinApplicationService.Instance;
            if (svc == null) return;
            svc.OnSkinApplied += OnAnySkinApplied;
            svc.OnSkinRemoved += OnAnySkinRemoved;
            _subscribed = true;
        }

        private static readonly List<AllCosmeticsTab> _live = new List<AllCosmeticsTab>();

        void Awake()
        {
            EnsureSubscribed();
            _live.Add(this);
        }

        void OnDestroy()
        {
            _live.Remove(this);
            var svc = SkinApplicationService.Instance;
            if (svc == null || !_subscribed) return;
            svc.OnSkinApplied -= OnAnySkinApplied;
            svc.OnSkinRemoved -= OnAnySkinRemoved;
            _subscribed = false;
        }

        public static void RefreshLiveInstances()
        {
            for (int i = 0; i < _live.Count; i++)
                _live[i]?.RefreshEntryList();
        }

        private void OnAnySkinApplied(SkinApplyEvent e)
        {
            if (e == null || e.skinInfo == null || e.skinInfo.type != "Costume") return;
            if (string.IsNullOrEmpty(e.skinInfo.file) || !e.skinInfo.file.StartsWith("gamecosm:")) return;
            RefreshEntryList();
        }

        private void OnAnySkinRemoved(string _) => RefreshEntryList();

        protected override void BuildContent(RectTransform contentRoot)
        {
            EnsureSubscribed();

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
                "ui.remove_selected", BTN_DARK, WHITE, FS_SM, new Action(OnRemoveSelected));
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + halfW + PAD * 0.5f, y, halfW, BTN_H),
                "ui.remove_all", BTN_REMOVE, WHITE, FS_SM, new Action(OnRemoveAll));
            y += BTN_H + 2f;

            _statusLbl = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, y, w, LH), "", FS_SM, HINT, TextAnchor.MiddleCenter);

            RefreshEntryList();
        }

        public override void OnOpened() => RefreshEntryList();

        private void RefreshEntryList()
        {
            if (_entryContent == null) return;

            var svc = SkinApplicationService.Instance;
            _entries = svc != null ? svc.GetSavedGameCosmetics() : new List<GameCosmeticEntry>();
            if (_selectedEntry >= _entries.Count) _selectedEntry = _entries.Count - 1;

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
                if (entry.option == null && Customization.UI.MenuCustomizationApplication.InMenuScene)
                    entry.option = SkinApplicationService.ResolveGameCosmeticOption(entry.kind, entry.id);
                if (CosmeticCell.Wants(entry.option)) CosmeticCell.Fill(thumbGo.transform, entry.option);
                else
                {
                    var icon = thumbGo.AddComponent<Image>();
                    icon.raycastTarget = false;
                    icon.preserveAspect = true;
                    var sprite = SkinApplicationService.ResolveOptionIconSprite(entry.option);
                    if (sprite != null) icon.sprite = sprite;
                    else icon.color = new Color(0f, 0f, 0f, 0.4f);
                }

                float toggleW = 30f * UIScale.S, removeW = 22f * UIScale.S;
                float nameX = 3f + thumbSz * 1.2f + 6f;
                float nameW = rowW - toggleW - removeW - nameX - 10f;

                string label = string.IsNullOrEmpty(entry.name) ? entry.id : entry.name;
                var nameLbl = UGUIShip.CreateLabel(rowBtn.transform,
                    new Rect(nameX, 0f, nameW, ROW_H), label,
                    FS_SM, WHITE, TextAnchor.MiddleLeft);

                var toggleBtn = UGUIShip.CreateRowEndButton(rowBtn.transform, -(removeW + toggleW + 2f), toggleW, ROW_H,
                    entry.enabled ? "ui.on_2" : "ui.off_2", entry.enabled ? BTN_APPLY : BTN_DARK, () => ToggleEntry(idx));

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
                "ui.add_cosmetic", BTN_ADD, WHITE, FS, new Action(OpenWizard));
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

        private void OpenWizard()
        {
            var wizard = BetterFGTabRegistry.NewTab<AllCosmeticsWizardTab>();
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
            var entry = _entries[idx];
            SetStatus((string.IsNullOrEmpty(entry.name) ? entry.id : entry.name) + " selected");
        }

        private void ToggleEntry(int idx)
        {
            if (idx < 0 || idx >= _entries.Count) return;
            var svc = SkinApplicationService.Instance;
            if (svc == null) return;
            var entry = _entries[idx];
            svc.SetGameCosmeticEnabled(entry, !entry.enabled);
            PaintRows();
            SetStatus((string.IsNullOrEmpty(entry.name) ? entry.id : entry.name) + (entry.enabled ? " on" : " off"));
        }

        private void OnRemoveSelected()
        {
            if (_selectedEntry < 0 || _selectedEntry >= _entries.Count) { SetStatus("ui.select_an_entry_first"); return; }
            RemoveEntry(_selectedEntry);
        }

        private void RemoveEntry(int idx)
        {
            if (idx < 0 || idx >= _entries.Count) return;
            var svc = SkinApplicationService.Instance;
            if (svc == null) return;
            var entry = _entries[idx];
            string label = string.IsNullOrEmpty(entry.name) ? entry.id : entry.name;

            svc.ForgetGameCosmetic(entry);

            _selectedEntry = -1;
            RefreshEntryList();
            SetStatus("removed " + label);
        }

        private void OnRemoveAll()
        {
            var svc = SkinApplicationService.Instance;
            if (svc == null) return;
            svc.RemoveAllGameCosmetics();
            svc.RemoveGameColour();
            svc.RemoveGamePattern();
            svc.RemoveGameFaceplate();
            svc.RemoveAllGameEmotes();
            SkinApplicationService.SaveSavedGameCosmetics(new List<GameCosmeticEntry>());
            _selectedEntry = -1;
            RefreshEntryList();
            SetStatus("ui.removed_all");
        }



        public void SetStatus(string msg)
        {
            if (_statusLbl != null) UGUIShip.RelabelText(_statusLbl, msg);
        }
    }
}
