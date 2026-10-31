using System;
using System.Collections.Generic;
using BetterFG.Customization.Player;
using BetterFG.Services;
using FG.Common.CMS;
using UnityEngine;
using UnityEngine.UI;
using LayoutElement = UnityEngine.UI.LayoutElement;

namespace BetterFG.UI.Tab
{
    public class AllCosmeticsTab : BetterFGTab
    {
        public AllCosmeticsTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "All Cosmetics";
        protected override string BgResource => "BetterFG.assets.ui.tab.allcosm.png";

        private static readonly Color BTN_DARK = new Color(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Color BTN_ADD = new Color(0.3f, 0.3f, 0.15f, 1f);
        private static readonly Color BTN_REMOVE = new Color(0.55f, 0.15f, 0.15f, 1f);
        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color WHITE = Color.white;
        private static readonly Color ROW_A = new Color(0.12f, 0.12f, 0.12f, 1f);
        private static readonly Color ROW_B = new Color(0.09f, 0.09f, 0.09f, 1f);
        private static readonly Color ROW_SELECTED = new Color(0.25f, 0.45f, 0.25f, 1f);

        private static float ROW_H => 30f * UIScale.S;

        private List<GameCosmeticEntry> _entries = new List<GameCosmeticEntry>();
        private readonly List<Image> _rowBgs = new List<Image>();
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

        void Awake() => EnsureSubscribed();

        void OnDestroy()
        {
            var svc = SkinApplicationService.Instance;
            if (svc == null || !_subscribed) return;
            svc.OnSkinApplied -= OnAnySkinApplied;
            svc.OnSkinRemoved -= OnAnySkinRemoved;
            _subscribed = false;
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

            float listH = TabHeight - y - BTN_H - LH * 2f - VPAD - 4f;
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
                "Remove Selected", BTN_DARK, WHITE, FS_SM, new Action(OnRemoveSelected));
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + halfW + PAD * 0.5f, y, halfW, BTN_H),
                "Remove All", BTN_REMOVE, WHITE, FS_SM, new Action(OnRemoveAll));
            y += BTN_H + 2f;

            _statusLbl = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, y, w, LH), "", FS_SM, HINT, TextAnchor.MiddleCenter);
            y += LH;
            UGUIShip.CreateLabel(contentRoot, new Rect(PAD, y, w, LH), "Credits to Floyzi",
                FS_SM, new Color(1f, 1f, 1f, 0.25f), TextAnchor.MiddleCenter);

            RefreshEntryList();
        }

        public override void OnOpened() => RefreshEntryList();

        private void RefreshEntryList()
        {
            if (_entryContent == null) return;

            var svc = SkinApplicationService.Instance;
            bool menu = SkinApplicationService.MainMenuReached;
            if (menu) svc?.ActiveGameEmotes(true);
            _entries = svc != null ? svc.GetAppliedGameCosmetics() : new List<GameCosmeticEntry>();
            if (_selectedEntry >= _entries.Count) _selectedEntry = _entries.Count - 1;

            for (int i = _entryContent.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_entryContent.GetChild(i).gameObject);

            _rowBgs.Clear();
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
                var bg = rowGo.AddComponent<Image>();
                var btnSpr = UGUIShip.GetButtonSprite();
                if (btnSpr != null) { bg.sprite = btnSpr; bg.type = Image.Type.Simple; }
                _rowBgs.Add(bg);

                var rowBtn = rowGo.AddComponent<Button>();
                rowBtn.transition = Selectable.Transition.None;
                var nav = rowBtn.navigation;
                nav.mode = UnityEngine.UI.Navigation.Mode.None;
                rowBtn.navigation = nav;
                rowBtn.onClick.AddListener(new Action(() =>
                {
                    AudioService.PlayButtonClick();
                    SelectEntry(idx);
                }));

                float thumbSz = ROW_H - 6f;
                var thumbGo = new GameObject("Thumb");
                thumbGo.transform.SetParent(rowGo.transform, false);
                var thumbRt = thumbGo.AddComponent<RectTransform>();
                if (menu && CosmeticCell.Wants(entry.option))
                {
                    float cellSz = thumbSz * 1.4f;
                    UGUIShip.SetPixelRect(thumbRt, new Rect(3f + cellSz * 0.2f, 3f - cellSz * 0.2f, cellSz, cellSz));
                    CosmeticCell.Fill(thumbGo.transform, entry.option);
                }
                else
                {
                    UGUIShip.SetPixelRect(thumbRt, new Rect(3f, 3f, thumbSz, thumbSz));
                    var icon = thumbGo.AddComponent<Image>();
                    icon.raycastTarget = false;
                    icon.preserveAspect = true;
                    var sprite = menu ? SkinApplicationService.ResolveOptionIconSprite(entry.option) ?? GetIconSprite(entry.option) : null;
                    if (sprite != null) icon.sprite = sprite;
                    else icon.color = new Color(0f, 0f, 0f, 0.4f);
                }

                float removeW = 22f * UIScale.S;
                float nameX = 3f + thumbSz + 6f;
                float nameW = rowW - removeW - nameX - 10f;

                string label = string.IsNullOrEmpty(entry.name) || entry.name == entry.id
                    ? (entry.option != null ? GetDisplayName(entry.option) : entry.id)
                    : entry.name;
                if (entry.kind == "emote") label = (entry.slot + 1) + ". " + label;
                UGUIShip.CreateLabel(rowGo.transform, new Rect(nameX, 0f, nameW, ROW_H), label,
                    FS_SM, WHITE, TextAnchor.MiddleLeft);

                float bh = Mathf.Min(ROW_H - 6f, 24f * UIScale.S);
                var xBtn = UGUIShip.CreateButton(rowGo.transform, new Rect(0f, 0f, removeW, bh), "x", BTN_REMOVE, WHITE, FS_SM - 1,
                    new Action(() => RemoveEntry(idx)));
                var xRt = xBtn.GetComponent<RectTransform>();
                xRt.anchorMin = xRt.anchorMax = new Vector2(1f, 0.5f);
                xRt.pivot = new Vector2(1f, 0.5f);
                xRt.anchoredPosition = new Vector2(-2f, 0f);
                xRt.sizeDelta = new Vector2(removeW, bh);
            }

            PaintRows();

            var addBtn = UGUIShip.CreateButton(_entryContent, new Rect(0f, 0f, rowW, ROW_H),
                "+ Add Cosmetic", BTN_ADD, WHITE, FS, new Action(OpenWizard));
            var addLe = addBtn.gameObject.AddComponent<LayoutElement>();
            addLe.preferredHeight = ROW_H;
            addLe.flexibleWidth = 1f;
        }

        private void PaintRows()
        {
            for (int i = 0; i < _rowBgs.Count; i++)
                _rowBgs[i].color = i == _selectedEntry ? ROW_SELECTED : (i % 2 == 0 ? ROW_A : ROW_B);
        }

        private void OpenWizard() => BetterFGUIMan.Instance?.SwitchSlotTab(this, BetterFGTabRegistry.NewTab<AllCosmeticsWizardTab>());

        private void SelectEntry(int idx)
        {
            _selectedEntry = _selectedEntry == idx ? -1 : idx;
            PaintRows();
            if (_selectedEntry >= 0)
            {
                var entry = _entries[idx];
                SetStatus((string.IsNullOrEmpty(entry.name) ? entry.id : entry.name) + " selected");
            }
        }

        private void OnRemoveSelected()
        {
            if (_selectedEntry < 0 || _selectedEntry >= _entries.Count) { SetStatus("select an entry first"); return; }
            RemoveEntry(_selectedEntry);
        }

        private void RemoveEntry(int idx)
        {
            if (idx < 0 || idx >= _entries.Count) return;
            var app = SkinApplicationService.Instance;
            if (app == null) return;
            var entry = _entries[idx];
            string label = string.IsNullOrEmpty(entry.name) ? entry.id : entry.name;

            if (entry.kind == "colour") app.RemoveGameColour();
            else if (entry.kind == "pattern") app.RemoveGamePattern();
            else if (entry.kind == "faceplate") app.RemoveGameFaceplate();
            else if (entry.kind == "emote") app.RemoveGameEmote(entry.slot);
            else app.RemoveGameCosmetic(entry.id);

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
            _selectedEntry = -1;
            RefreshEntryList();
            SetStatus("removed all");
        }

        public static string GetDisplayName(CostumeOption option)
        {
            try { return option.CMSData.Name._text ?? option.name ?? ""; } catch { }
            try { return option.name ?? ""; } catch { }
            return "";
        }

        public static string GetDisplayName(UnityEngine.Object option)
        {
            if (option == null) return "";
            try { if (option.TryCast<CostumeOption>() != null) return GetDisplayName(option.Cast<CostumeOption>()); } catch { }
            try { var c = option.Cast<ColourOption>(); return c.CMSData.Name._text ?? c.name ?? ""; } catch { }
            try { var p = option.Cast<SkinPatternOption>(); return p.CMSData.Name._text ?? p.name ?? ""; } catch { }
            try { var f = option.Cast<FaceplateOption>(); return ((ItemDefinitionSO)f).CMSData.Name._text ?? f.name ?? ""; } catch { }
            try { return SkinApplicationService.GetEmoteName(option.Cast<EmotesOption>(), ""); } catch { }
            try { return option.name ?? ""; } catch { }
            return "";
        }

        internal static Sprite GetIconSprite(UnityEngine.Object option)
        {
            if (option == null) return null;
            ItemDefinitionSO def = null;
            try { def = option.Cast<ItemDefinitionSO>(); } catch { }
            if (def == null) return null;
            try { var s = def.MenuDisplaySprite; if (s != null) return s; } catch { }
            try { var s = def._spriteAtlasLoadableAsset.AssetRef.LoadAsset<Sprite>().Result; if (s != null) return s; } catch { }
            return null;
        }

        private void SetStatus(string text)
        {
            if (_statusLbl != null) _statusLbl.text = text;
        }
    }
}
