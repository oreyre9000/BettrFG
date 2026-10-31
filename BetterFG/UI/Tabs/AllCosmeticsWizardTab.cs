using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Player;
using BetterFG.Services;
using BetterFG.UI.Components;
using FG.Common.CMS;
using FGClient;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.UI;
using LayoutElement = UnityEngine.UI.LayoutElement;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class AllCosmeticsWizardTab : WizardTab
    {
        public AllCosmeticsWizardTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "All Cosmetics - Add";
        protected override string TitleId => "ui.all_cosmetics_add";
        protected override string BgResource => "BetterFG.assets.ui.tab.allcosm.png";

        protected override string[] StepTitles => _category == Category.Emote
            ? new[] { "ui.choose_a_cosmetic_to_add", "all_cosmetics.pick_wheel_slot" }
            : new[] { "ui.choose_a_cosmetic_to_add" };
        protected override int PanelCount => 2;

        private enum Category { Costume, Colour, Pattern, Faceplate, Emote }
        private static readonly (Category id, string label)[] CATEGORIES =
        {
            (Category.Costume, "ui.costumes"),
            (Category.Colour, "ui.colours"),
            (Category.Pattern, "ui.patterns"),
            (Category.Faceplate, "ui.faces"),
            (Category.Emote, "ui.emotes"),
        };

        private SocialWheelPreview _wheel;
        private Text _slotLbl;
        private int _slot = -1;

        private Category _category = Category.Costume;
        private Text _categoryLbl;

        private InputField _searchField;
        private RectTransform _resultContent;
        private readonly List<UnityEngine.Object> _results = new List<UnityEngine.Object>();
        private readonly List<Button> _resultRows = new List<Button>();
        private readonly HashSet<string> _selectedCostumeIds = new HashSet<string>();
        private string _selectedSingleId = "";

        protected override void BuildStep(int step, RectTransform root, float w, float bodyH)
        {
            if (step == 0) BuildPickStep(root, w, bodyH);
            else _slotLbl = UGUIShip.CreateLabel(root.transform, new Rect(PAD, SH, w, BTN_H), "", FS_SM, WHITE, TextAnchor.MiddleCenter);
        }

        protected override Tab MakeListTarget() => BetterFGTabRegistry.CreateTab("All Cosmetics");

        protected override bool CanAdvance(int step)
        {
            if (step == 1) return _slot >= 0;
            return _category == Category.Costume ? _selectedCostumeIds.Count > 0 : !string.IsNullOrEmpty(_selectedSingleId);
        }

        protected override void OnStepShown(int step)
        {
            _wheel ??= new SocialWheelPreview(this, EmoteWheelType, OnWheelSlotClicked);
            if (step != 1) _wheel.Destroy();
            else if (!_wheel.Shown) _wheel.Spawn(Root);
        }

        protected override void OnLeave() => _wheel?.Destroy();

        public override void OnClosed() => _wheel?.Destroy();

        private static WheelType EmoteWheelType() => WheelType.EmotesAndEmoticons;

        private void OnWheelSlotClicked(int slot)
        {
            _slot = slot;
            UGUIShip.RelabelText(_slotLbl, LocalizationService.Format("all_cosmetics.slot_picked", slot));
            RefreshStep();
        }

        private void BuildPickStep(RectTransform root, float w, float bodyH)
        {
            float cy = SH;

            _categoryLbl = UGUIShip.CreateCarousel(root.transform, new Rect(PAD, cy, w, BTN_H),
                CategoryLabels(), (int)_category,
                d => SelectCategory(CATEGORIES[((int)_category + d + CATEGORIES.Length) % CATEGORIES.Length].id),
                BTN_DARK, FS_SM);
            cy += BTN_H + SH;

            float fetchW = 60f * UIScale.S;
            _searchField = UGUIShip.CreateInputField(root.transform, new Rect(PAD, cy, w - fetchW - PAD, BTN_H),
                "ui.search_by_name", Color.black, WHITE, FS_SM);
            _searchField.onEndEdit.AddListener(new Action<string>(v => OnFetch()));
            UGUIShip.CreateButton(root.transform, new Rect(PAD + w - fetchW, cy, fetchW, BTN_H),
                "ui.search", BTN_BLUE, WHITE, FS_SM, new Action(OnFetch));
            cy += BTN_H + SH;

            var scroll = UGUIShip.CreateScrollView(root.transform, new Rect(PAD, cy, w, bodyH - cy - SH));
            _resultContent = scroll.content;
            var layout = _resultContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 1f;
            _resultContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RebuildResultRows();
        }

        private static string[] CategoryLabels()
        {
            var names = new string[CATEGORIES.Length];
            for (int i = 0; i < CATEGORIES.Length; i++) names[i] = CATEGORIES[i].label;
            return names;
        }

        private void SelectCategory(Category cat)
        {
            if (_category == cat) return;
            _category = cat;
            _results.Clear();
            _selectedCostumeIds.Clear();
            _selectedSingleId = "";
            UGUIShip.RelabelText(_categoryLbl, CATEGORIES[(int)_category].label);
            RebuildResultRows();
            RefreshStep();
            SetStatus(CATEGORIES[(int)_category].label);
        }

        private void OnFetch()
        {
            string filter = _searchField?.text?.Trim() ?? "";
            StartCoroutine(FetchRoutine(filter).WrapToIl2Cpp());
        }

        private IEnumerator FetchRoutine(string filter)
        {
            _results.Clear();
            RebuildResultRows();
            SetStatus("ui.fetching_2");
            yield return null;

            var type = _category == Category.Colour ? Il2CppType.Of<ColourOption>()
                : _category == Category.Pattern ? Il2CppType.Of<SkinPatternOption>()
                : _category == Category.Faceplate ? Il2CppType.Of<FaceplateOption>()
                : _category == Category.Emote ? Il2CppType.Of<EmotesOption>()
                : Il2CppType.Of<CostumeOption>();

            var raw = Resources.FindObjectsOfTypeAll(type);
            if (raw == null || raw.Length == 0) { SetStatus("ui.none_found"); yield break; }

            for (int i = 0; i < raw.Length && _results.Count < 120; i++)
            {
                if (raw[i] == null) continue;
                UnityEngine.Object opt = null;
                try
                {
                    if (_category == Category.Colour) opt = raw[i].Cast<ColourOption>();
                    else if (_category == Category.Pattern) opt = raw[i].Cast<SkinPatternOption>();
                    else if (_category == Category.Faceplate) opt = raw[i].Cast<FaceplateOption>();
                    else if (_category == Category.Emote) opt = raw[i].Cast<EmotesOption>();
                    else opt = raw[i].Cast<CostumeOption>();
                }
                catch { continue; }
                string name = SkinApplicationService.GetOptionDisplayName(opt);
                if (string.IsNullOrEmpty(filter) || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    _results.Add(opt);
            }

            SetStatus(LocalizationService.Format("all_cosmetics.results", _results.Count));
            RebuildResultRows();
        }

        private void RebuildResultRows()
        {
            _resultRows.Clear();
            for (int i = _resultContent.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_resultContent.GetChild(i).gameObject);

            if (_results.Count == 0)
            {
                UGUIShip.CreateLabel(_resultContent, new Rect(6f, 0f, TabWidth, ROW_H), "ui.search_to_find_items", FS_SM, HINT);
                return;
            }

            for (int i = 0; i < _results.Count; i++)
            {
                int idx = i;
                string id = GetOptionId(_results[i]);
                bool isSel = _category == Category.Costume ? _selectedCostumeIds.Contains(id) : _selectedSingleId == id;

                var btn = UGUIShip.CreateButton(_resultContent, new Rect(0f, 0f, 0f, ROW_H), "",
                    isSel ? ROW_SEL : ROW_IDLE, WHITE, FS_SM, new Action(() => SelectResult(idx)));
                btn.transition = Selectable.Transition.None;
                var trigger = btn.GetComponent<UnityEngine.EventSystems.EventTrigger>();
                if (trigger != null) GameObject.Destroy(trigger);
                var le = btn.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = ROW_H;
                le.flexibleWidth = 1f;

                bool cell = CosmeticCell.Wants(_results[i]);
                Sprite icon = cell ? null : SkinApplicationService.ResolveOptionIconSprite(_results[i]);
                float iconSz = (ROW_H - 4f) * 1.4f;
                float textX = 5f;
                if (icon != null || cell)
                {
                    var iconGo = new GameObject("Icon");
                    iconGo.transform.SetParent(btn.transform, false);
                    var iconRt = iconGo.AddComponent<RectTransform>();
                    iconRt.anchorMin = new Vector2(0f, 0.5f);
                    iconRt.anchorMax = new Vector2(0f, 0.5f);
                    iconRt.pivot = new Vector2(0f, 0.5f);
                    iconRt.anchoredPosition = new Vector2(3f + iconSz * 0.2f, iconSz * 0.2f);
                    iconRt.sizeDelta = new Vector2(iconSz, iconSz);
                    if (cell) CosmeticCell.Fill(iconGo.transform, _results[i]);
                    else
                    {
                        var img = iconGo.AddComponent<Image>();
                        img.sprite = icon;
                        img.preserveAspect = true;
                        img.raycastTarget = false;
                    }
                    textX = 3f + iconSz * 1.2f + 4f;
                }

                UGUIShip.CreateLabel(btn.transform, new Rect(textX, 0f, TabWidth - PAD * 2f - textX - 8f, ROW_H),
                    SkinApplicationService.GetOptionDisplayName(_results[i]), FS_SM, WHITE, TextAnchor.MiddleLeft);
                _resultRows.Add(btn);
            }
        }

        private void SelectResult(int idx)
        {
            string id = GetOptionId(_results[idx]);
            string name = SkinApplicationService.GetOptionDisplayName(_results[idx]);
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

            RebuildResultRows();
            RefreshStep();
            SetStatus(LocalizationService.Format(nowSelected ? "all_cosmetics.selected" : "all_cosmetics.deselected", name));
        }

        protected override bool Save()
        {
            var svc = SkinApplicationService.Instance;
            if (svc == null) { SetStatus("ui.skinapplicationservice_not_ready"); return false; }

            if (_category == Category.Colour)
            {
                var opt = FindResultById(_selectedSingleId);
                if (opt == null) { SetStatus("ui.pick_a_colour_first"); return false; }
                svc.ApplyGameColour(opt.Cast<ColourOption>());
                return true;
            }
            if (_category == Category.Pattern)
            {
                var opt = FindResultById(_selectedSingleId);
                if (opt == null) { SetStatus("ui.pick_a_pattern_first"); return false; }
                svc.ApplyGamePattern(opt.Cast<SkinPatternOption>());
                return true;
            }
            if (_category == Category.Faceplate)
            {
                var opt = FindResultById(_selectedSingleId);
                if (opt == null) { SetStatus("ui.pick_a_faceplate_first"); return false; }
                svc.ApplyGameFaceplate(opt.Cast<FaceplateOption>());
                return true;
            }
            if (_category == Category.Emote)
            {
                var opt = FindResultById(_selectedSingleId);
                if (opt == null) { SetStatus("all_cosmetics.pick_an_emote_first"); return false; }
                if (_slot < 0) { SetStatus("all_cosmetics.pick_a_slot_first"); return false; }
                svc.ApplyGameEmote(opt.Cast<EmotesOption>(), _slot);
                return true;
            }

            if (_selectedCostumeIds.Count == 0) { SetStatus("ui.pick_a_costume_first"); return false; }

            var combined = new List<CostumeOption>();
            var wantedIds = new HashSet<string>(_selectedCostumeIds);
            foreach (var entry in svc.GetAppliedGameCosmetics())
                if (entry.kind == "costume") { combined.Add(entry.option.Cast<CostumeOption>()); wantedIds.Add(entry.id); }
            foreach (var r in _results)
                if (_selectedCostumeIds.Contains(GetOptionId(r))) combined.Add(r.Cast<CostumeOption>());

            svc.ApplyGameCosmeticSelection(combined, wantedIds);
            return true;
        }

        private UnityEngine.Object FindResultById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var r in _results)
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
