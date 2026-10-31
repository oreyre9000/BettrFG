using System;
using System.Collections.Generic;
using BetterFG.Customization.Player;
using FG.Common.CMS;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Tab
{
    public class AllCosmeticsWizardTab : WizardTab
    {
        public AllCosmeticsWizardTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "All Cosmetics - Add";
        protected override string BgResource => "BetterFG.assets.ui.tab.allcosm.png";
        public override float OpenHeight => UIScale.TAB_CONTENT_H * 1.15f;

        protected override string[] StepTitles => _category == Category.Emote
            ? new[] { "Choose a cosmetic to add", "Pick a wheel slot" }
            : new[] { "Choose a cosmetic to add" };
        protected override int PanelCount => 2;

        private static readonly Color TILE_FILL = new Color(0.04f, 0.04f, 0.04f, 1f);
        private const int GridCols = 5;
        private const float TileGap = 5f;
        private const int MaxTiles = 150;

        private enum Category { Costume, Colour, Pattern, Faceplate, Emote }
        private static readonly string[] CATEGORY_LABELS = { "Costumes", "Colours", "Patterns", "Faces", "Emotes" };

        private readonly List<Button> _slotButtons = new List<Button>();
        private int _slot = -1;

        private Category _category = Category.Costume;
        private Text _categoryLbl;

        private string _filter = "";
        private float _tile;
        private RectTransform _resultContent;
        private readonly List<UnityEngine.Object> _all = new List<UnityEngine.Object>();
        private readonly List<string> _allNames = new List<string>();
        private readonly List<UnityEngine.Object> _results = new List<UnityEngine.Object>();
        private readonly List<Button> _tiles = new List<Button>();
        private readonly HashSet<string> _selectedCostumeIds = new HashSet<string>();
        private string _selectedSingleId = "";

        protected override void BuildStep(int step, RectTransform root, float w, float bodyH)
        {
            if (step == 0) BuildPickStep(root, w, bodyH);
            else BuildSlotStep(root, w);
        }

        protected override BetterFGTab MakeListTarget() => BetterFGTabRegistry.CreateTab("All Cosmetics");

        protected override bool CanAdvance(int step)
        {
            if (step == 1) return _slot >= 0;
            return _category == Category.Costume ? _selectedCostumeIds.Count > 0 : !string.IsNullOrEmpty(_selectedSingleId);
        }

        private void BuildSlotStep(RectTransform root, float w)
        {
            float gap = 3f;
            float bw = (w - gap * 3f) / 4f;
            for (int i = 0; i < 8; i++)
            {
                int slot = i;
                var btn = UGUIShip.CreateButton(root.transform,
                    new Rect(PAD + (i % 4) * (bw + gap), SH + (i / 4) * (BTN_H + gap), bw, BTN_H),
                    (i + 1).ToString(), BTN_DARK, WHITE, FS_SM, new Action(() => OnSlotClicked(slot)));
                _slotButtons.Add(btn);
            }
        }

        private void OnSlotClicked(int slot)
        {
            _slot = slot;
            for (int i = 0; i < _slotButtons.Count; i++)
                UGUIShip.SetButtonSelected(_slotButtons[i], i == slot, ROW_SEL);
            SetStatus("slot " + (slot + 1));
            RefreshStep();
        }

        private void BuildPickStep(RectTransform root, float w, float bodyH)
        {
            float arrow = BTN_H;
            UGUIShip.CreateButton(root.transform, new Rect(PAD, 0f, arrow, BTN_H), "<", BTN_DARK, WHITE, FS_SM, new Action(() => StepCategory(-1)));
            _categoryLbl = UGUIShip.CreateLabel(root.transform, new Rect(PAD + arrow, 0f, w - arrow * 2f, BTN_H),
                CATEGORY_LABELS[(int)_category], FS_SM, WHITE, TextAnchor.MiddleCenter);
            UGUIShip.CreateButton(root.transform, new Rect(PAD + w - arrow, 0f, arrow, BTN_H), ">", BTN_DARK, WHITE, FS_SM, new Action(() => StepCategory(1)));

            float cy = BTN_H + SH;
            var search = UGUIShip.CreateInputField(root.transform, new Rect(PAD, cy, w, BTN_H),
                "search by name", new Color(0f, 0f, 0f, 0.4f), WHITE, FS_SM);
            search.onValueChanged.AddListener(new Action<string>(v => { _filter = v?.Trim() ?? ""; RebuildTiles(); }));

            float gridY = cy + BTN_H + SH;
            var scroll = UGUIShip.CreateScrollView(root.transform, new Rect(PAD, gridY, w, bodyH - gridY));
            _resultContent = scroll.content;
            _tile = Mathf.Floor((w - UGUIShip.SCROLLBAR_INSET * 2f - 4f - TileGap * (GridCols - 1)) / GridCols);
            var grid = _resultContent.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(_tile, _tile);
            grid.spacing = new Vector2(TileGap, TileGap);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = GridCols;
            grid.padding = new RectOffset(2, 2, 2, 2);
            _resultContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            LoadCategory();
            RebuildTiles();
        }

        private void StepCategory(int d)
        {
            _category = (Category)(((int)_category + d + CATEGORY_LABELS.Length) % CATEGORY_LABELS.Length);
            _selectedCostumeIds.Clear();
            _selectedSingleId = "";
            _categoryLbl.text = CATEGORY_LABELS[(int)_category];
            LoadCategory();
            RebuildTiles();
            RefreshStep();
        }

        private void LoadCategory()
        {
            _all.Clear();
            _allNames.Clear();
            var type = _category == Category.Colour ? Il2CppType.Of<ColourOption>()
                : _category == Category.Pattern ? Il2CppType.Of<SkinPatternOption>()
                : _category == Category.Faceplate ? Il2CppType.Of<FaceplateOption>()
                : _category == Category.Emote ? Il2CppType.Of<EmotesOption>()
                : Il2CppType.Of<CostumeOption>();

            var raw = Resources.FindObjectsOfTypeAll(type);
            var seen = new HashSet<string>();
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == null) continue;
                UnityEngine.Object opt;
                try
                {
                    if (_category == Category.Colour) opt = raw[i].Cast<ColourOption>();
                    else if (_category == Category.Pattern) opt = raw[i].Cast<SkinPatternOption>();
                    else if (_category == Category.Faceplate) opt = raw[i].Cast<FaceplateOption>();
                    else if (_category == Category.Emote) opt = raw[i].Cast<EmotesOption>();
                    else opt = raw[i].Cast<CostumeOption>();
                }
                catch { continue; }
                if (!seen.Add(GetOptionId(opt))) continue;
                _all.Add(opt);
                _allNames.Add(AllCosmeticsTab.GetDisplayName(opt));
            }
        }

        private void RebuildTiles()
        {
            if (_resultContent == null) return;
            _results.Clear();
            for (int i = 0; i < _all.Count && _results.Count < MaxTiles; i++)
                if (_allNames[i].IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    _results.Add(_all[i]);

            _tiles.Clear();
            _resultContent.anchoredPosition = new Vector2(_resultContent.anchoredPosition.x, 0f);
            for (int i = _resultContent.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_resultContent.GetChild(i).gameObject);

            for (int i = 0; i < _results.Count; i++)
            {
                int idx = i;
                var opt = _results[i];
                var btn = UGUIShip.CreateButton(_resultContent, new Rect(0f, 0f, _tile, _tile), "",
                    IsSelected(opt) ? ROW_SEL : ROW_IDLE, WHITE, FS_SM, new Action(() => SelectResult(idx)));
                BetterFGUIMan.MakeObjectTooltip(btn.GetComponent<RectTransform>(), AllCosmeticsTab.GetDisplayName(opt));

                var fillGo = new GameObject("Art");
                fillGo.transform.SetParent(btn.transform, false);
                var fillRt = fillGo.AddComponent<RectTransform>();
                fillRt.anchorMin = Vector2.zero;
                fillRt.anchorMax = Vector2.one;
                fillRt.offsetMin = new Vector2(4f, 4f);
                fillRt.offsetMax = new Vector2(-4f, -4f);
                var fill = fillGo.AddComponent<Image>();
                fill.color = TILE_FILL;
                fill.raycastTarget = false;

                var picGo = new GameObject("Pic");
                picGo.transform.SetParent(fillGo.transform, false);
                var picRt = picGo.AddComponent<RectTransform>();
                picRt.anchorMin = Vector2.zero;
                picRt.anchorMax = Vector2.one;
                picRt.offsetMin = new Vector2(2f, 2f);
                picRt.offsetMax = new Vector2(-2f, -2f);
                if (CosmeticCell.Wants(opt)) CosmeticCell.Fill(picGo.transform, opt);
                else
                {
                    var icon = AllCosmeticsTab.GetIconSprite(opt);
                    if (icon != null)
                    {
                        var img = picGo.AddComponent<Image>();
                        img.sprite = icon;
                        img.preserveAspect = true;
                        img.raycastTarget = false;
                    }
                }
                _tiles.Add(btn);
            }

            SetStatus(_results.Count + " result(s)");
        }

        private bool IsSelected(UnityEngine.Object opt)
        {
            string id = GetOptionId(opt);
            return _category == Category.Costume ? _selectedCostumeIds.Contains(id) : _selectedSingleId == id;
        }

        private void SelectResult(int idx)
        {
            string id = GetOptionId(_results[idx]);
            string name = AllCosmeticsTab.GetDisplayName(_results[idx]);
            bool nowSelected;
            if (_category == Category.Costume)
            {
                if (_selectedCostumeIds.Contains(id)) { _selectedCostumeIds.Remove(id); nowSelected = false; }
                else { _selectedCostumeIds.Add(id); nowSelected = true; }
            }
            else
            {
                nowSelected = _selectedSingleId != id;
                _selectedSingleId = nowSelected ? id : "";
            }

            for (int i = 0; i < _tiles.Count; i++)
                UGUIShip.SetButtonSelected(_tiles[i], IsSelected(_results[i]), ROW_SEL);
            RefreshStep();
            SetStatus(name + (nowSelected ? " selected" : " deselected"));
        }

        protected override bool Save()
        {
            var svc = SkinApplicationService.Instance;
            if (svc == null) { SetStatus("SkinApplicationService not ready"); return false; }

            if (_category == Category.Colour)
            {
                var opt = FindById(_selectedSingleId);
                if (opt == null) { SetStatus("pick a colour first"); return false; }
                svc.ApplyGameColour(opt.Cast<ColourOption>());
                return true;
            }
            if (_category == Category.Pattern)
            {
                var opt = FindById(_selectedSingleId);
                if (opt == null) { SetStatus("pick a pattern first"); return false; }
                svc.ApplyGamePattern(opt.Cast<SkinPatternOption>());
                return true;
            }
            if (_category == Category.Faceplate)
            {
                var opt = FindById(_selectedSingleId);
                if (opt == null) { SetStatus("pick a faceplate first"); return false; }
                svc.ApplyGameFaceplate(opt.Cast<FaceplateOption>());
                return true;
            }
            if (_category == Category.Emote)
            {
                var opt = FindById(_selectedSingleId);
                if (opt == null) { SetStatus("pick an emote first"); return false; }
                if (_slot < 0) { SetStatus("pick a slot first"); return false; }
                svc.ApplyGameEmote(opt.Cast<EmotesOption>(), _slot);
                return true;
            }

            if (_selectedCostumeIds.Count == 0) { SetStatus("pick a costume first"); return false; }

            var combined = new List<CostumeOption>();
            var wantedIds = new HashSet<string>(_selectedCostumeIds);
            foreach (var entry in svc.GetAppliedGameCosmetics())
                if (entry.kind == "costume" && entry.option != null) { combined.Add(entry.option.Cast<CostumeOption>()); wantedIds.Add(entry.id); }
            foreach (var r in _all)
                if (_selectedCostumeIds.Contains(GetOptionId(r))) combined.Add(r.Cast<CostumeOption>());

            svc.ApplyGameCosmeticSelection(combined, wantedIds);
            return true;
        }

        private UnityEngine.Object FindById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var r in _all)
                if (GetOptionId(r) == id) return r;
            return null;
        }

        private string GetOptionId(UnityEngine.Object option)
        {
            if (option == null) return "";
            try
            {
                if (_category == Category.Colour) return SkinApplicationService.GetGameColourOptionId(option.Cast<ColourOption>());
                if (_category == Category.Pattern) return SkinApplicationService.GetGamePatternOptionId(option.Cast<SkinPatternOption>());
                if (_category == Category.Faceplate) return SkinApplicationService.GetGameFaceplateOptionId(option.Cast<FaceplateOption>());
                if (_category == Category.Emote) return SkinApplicationService.GetGameEmoteOptionId(option.Cast<EmotesOption>());
                return SkinApplicationService.GetGameCosmeticOptionId(option.Cast<CostumeOption>());
            }
            catch { return ""; }
        }
    }
}
