using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Player;
using FG.Common.CMS;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.UI;
using LayoutElement = UnityEngine.UI.LayoutElement;

namespace BetterFG.UI.Tab
{
    public class AllCosmeticsWizardTab : WizardTab
    {
        public AllCosmeticsWizardTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "All Cosmetics - Add";
        protected override string BgResource => "BetterFG.assets.ui.tab.allcosm.png";

        protected override string[] StepTitles => _category == Category.Emote
            ? new[] { "Choose a cosmetic to add", "Pick a wheel slot" }
            : new[] { "Choose a cosmetic to add" };
        protected override int PanelCount => 2;

        private enum Category { Costume, Colour, Pattern, Faceplate, Emote }
        private static readonly string[] CATEGORY_LABELS = { "Costumes", "Colours", "Patterns", "Faces", "Emotes" };

        private readonly List<Button> _slotButtons = new List<Button>();
        private int _slot = -1;

        private Category _category = Category.Costume;
        private Text _categoryLbl;

        private InputField _searchField;
        private RectTransform _resultContent;
        private readonly List<UnityEngine.Object> _results = new List<UnityEngine.Object>();
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
            float cy = SH;

            float arrow = BTN_H;
            UGUIShip.CreateButton(root.transform, new Rect(PAD, cy, arrow, BTN_H), "<", BTN_DARK, WHITE, FS_SM, new Action(() => StepCategory(-1)));
            _categoryLbl = UGUIShip.CreateLabel(root.transform, new Rect(PAD + arrow, cy, w - arrow * 2f, BTN_H),
                CATEGORY_LABELS[(int)_category], FS_SM, WHITE, TextAnchor.MiddleCenter);
            UGUIShip.CreateButton(root.transform, new Rect(PAD + w - arrow, cy, arrow, BTN_H), ">", BTN_DARK, WHITE, FS_SM, new Action(() => StepCategory(1)));
            cy += BTN_H + SH;

            float fetchW = 60f * UIScale.S;
            _searchField = UGUIShip.CreateInputField(root.transform, new Rect(PAD, cy, w - fetchW - PAD, BTN_H),
                "search by name", Color.black, WHITE, FS_SM);
            _searchField.onEndEdit.AddListener(new Action<string>(v => OnFetch()));
            UGUIShip.CreateButton(root.transform, new Rect(PAD + w - fetchW, cy, fetchW, BTN_H),
                "Search", BTN_BLUE, WHITE, FS_SM, new Action(OnFetch));
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

        private void StepCategory(int d)
        {
            _category = (Category)(((int)_category + d + CATEGORY_LABELS.Length) % CATEGORY_LABELS.Length);
            _results.Clear();
            _selectedCostumeIds.Clear();
            _selectedSingleId = "";
            _categoryLbl.text = CATEGORY_LABELS[(int)_category];
            RebuildResultRows();
            RefreshStep();
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
            SetStatus("fetching...");
            yield return null;

            var type = _category == Category.Colour ? Il2CppType.Of<ColourOption>()
                : _category == Category.Pattern ? Il2CppType.Of<SkinPatternOption>()
                : _category == Category.Faceplate ? Il2CppType.Of<FaceplateOption>()
                : _category == Category.Emote ? Il2CppType.Of<EmotesOption>()
                : Il2CppType.Of<CostumeOption>();

            var raw = Resources.FindObjectsOfTypeAll(type);
            if (raw == null || raw.Length == 0) { SetStatus("none found"); yield break; }

            var seen = new HashSet<string>();
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
                if (_category == Category.Emote && !seen.Add(GetOptionId(opt))) continue;
                string name = AllCosmeticsTab.GetDisplayName(opt);
                if (string.IsNullOrEmpty(filter) || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    _results.Add(opt);
            }

            SetStatus(_results.Count + " result(s)");
            RebuildResultRows();
        }

        private void RebuildResultRows()
        {
            for (int i = _resultContent.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_resultContent.GetChild(i).gameObject);

            if (_results.Count == 0)
            {
                UGUIShip.CreateLabel(_resultContent, new Rect(6f, 0f, TabWidth, ROW_H), "search to find items", FS_SM, HINT);
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

                Sprite icon = AllCosmeticsTab.GetIconSprite(_results[i]);
                float iconSz = ROW_H - 4f;
                float textX = 5f;
                if (icon != null)
                {
                    var iconGo = new GameObject("Icon");
                    iconGo.transform.SetParent(btn.transform, false);
                    var iconRt = iconGo.AddComponent<RectTransform>();
                    iconRt.anchorMin = iconRt.anchorMax = new Vector2(0f, 0.5f);
                    iconRt.pivot = new Vector2(0f, 0.5f);
                    iconRt.anchoredPosition = new Vector2(3f, 0f);
                    iconRt.sizeDelta = new Vector2(iconSz, iconSz);
                    var img = iconGo.AddComponent<Image>();
                    img.sprite = icon;
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                    textX = 3f + iconSz + 4f;
                }

                UGUIShip.CreateLabel(btn.transform, new Rect(textX, 0f, TabWidth - PAD * 2f - textX - 8f, ROW_H),
                    AllCosmeticsTab.GetDisplayName(_results[i]), FS_SM, WHITE, TextAnchor.MiddleLeft);
            }
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

            RebuildResultRows();
            RefreshStep();
            SetStatus(name + (nowSelected ? " selected" : " deselected"));
        }

        protected override bool Save()
        {
            var svc = SkinApplicationService.Instance;
            if (svc == null) { SetStatus("SkinApplicationService not ready"); return false; }

            if (_category == Category.Colour)
            {
                var opt = FindResultById(_selectedSingleId);
                if (opt == null) { SetStatus("pick a colour first"); return false; }
                svc.ApplyGameColour(opt.Cast<ColourOption>());
                return true;
            }
            if (_category == Category.Pattern)
            {
                var opt = FindResultById(_selectedSingleId);
                if (opt == null) { SetStatus("pick a pattern first"); return false; }
                svc.ApplyGamePattern(opt.Cast<SkinPatternOption>());
                return true;
            }
            if (_category == Category.Faceplate)
            {
                var opt = FindResultById(_selectedSingleId);
                if (opt == null) { SetStatus("pick a faceplate first"); return false; }
                svc.ApplyGameFaceplate(opt.Cast<FaceplateOption>());
                return true;
            }
            if (_category == Category.Emote)
            {
                var opt = FindResultById(_selectedSingleId);
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
