using System;
using System.Collections.Generic;
using System.IO;
using BetterFG.Customization.UI;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class UIFontWizardTab : WizardTab
    {
        public UIFontWizardTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => EditIndex >= 0 ? "UI - Font Edit" : "UI - Font New";
        protected override string BgResource => "BetterFG.assets.ui.tab.ui.png";

        enum WizardStep { File, Target, Name }
        protected override string[] StepTitles => new[] { "ui.pick_the_font_file", "ui.choose_the_game_font_to_replace", "ui.name_it" };

        string _fontPath = "";
        Text _fontPathLbl;
        TMPro.TextMeshProUGUI _fontPreviewTmp;

        // only these two TMP_FontAssets are actually used in-game — the rest exist in memory but
        // never render. real game name → user-facing label.
        static readonly (string real, string display)[] FontWhitelist =
        {
            ("TitanOne-Expanded SDF (Title)", "Titan One"),
            ("Asap-Bold SDF (Body)", "Asap"),
        };
        RectTransform _targetContent;
        readonly List<Button> _targetRows = new List<Button>();
        string _targetName = "";

        InputField _nameField;
        Text _summaryLbl;

        protected override void BuildStep(int step, RectTransform root, float w, float bodyH)
        {
            switch ((WizardStep)step)
            {
                case WizardStep.File: BuildFileStep(root, w, bodyH); break;
                case WizardStep.Target: BuildTargetStep(root, w, bodyH); break;
                case WizardStep.Name: BuildNameStep(root, w, bodyH); break;
            }
        }

        protected override int LoadEditedEntry()
        {
            var entries = FontReplacementService.LoadAll();
            if (EditIndex >= entries.Count) { EditIndex = -1; return -1; }

            var e = entries[EditIndex];
            _fontPath = e.fontPath;
            _targetName = e.targetFontName;
            UGUIShip.SetInputText(_nameField, e.entryName, false);
            LoadFontPreview();
            RebuildTargetRows();
            return -1;
        }

        protected override Tab MakeListTarget() => BetterFGTabRegistry.NewTab<UIFontTab>();

        protected override bool CanAdvance(int step)
        {
            switch ((WizardStep)step)
            {
                case WizardStep.File: return !string.IsNullOrEmpty(_fontPath);
                case WizardStep.Target: return !string.IsNullOrEmpty(_targetName);
                default: return true;
            }
        }

        void BuildFileStep(RectTransform root, float w, float bodyH)
        {
            float cy = SH;
            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, LH),
                "ui.pick_the_font_file_to_use_ttf_otf", FS_SM, LABEL);
            cy += LH + SH;

            float browseW = 80f * UIScale.S;
            UGUIShip.CreateButton(root.transform, new Rect(PAD, cy, browseW, BTN_H),
                "ui.browse_2", BTN_BLUE, WHITE, FS_SM, new Action(OnBrowseFont));
            _fontPathLbl = UGUIShip.CreateLabel(root.transform, new Rect(PAD + browseW + PAD, cy, w - browseW - PAD, BTN_H),
                "ui.no_file_picked", FS_SM, HINT, TextAnchor.MiddleLeft);
            cy += BTN_H + SH * 2f;

            var prevGo = new GameObject("FontPreview");
            prevGo.transform.SetParent(root.transform, false);
            UGUIShip.SetPixelRect(prevGo.AddComponent<RectTransform>(), new Rect(PAD, cy, w, bodyH - cy - SH));
            UGUIShip.ApplyDeluxPanelDark(prevGo.AddComponent<Image>());

            var prevTmpGo = new GameObject("PreviewTmp");
            prevTmpGo.transform.SetParent(prevGo.transform, false);
            var prevRt = prevTmpGo.AddComponent<RectTransform>();
            prevRt.anchorMin = Vector2.zero; prevRt.anchorMax = Vector2.one;
            prevRt.offsetMin = new Vector2(6f, 6f); prevRt.offsetMax = new Vector2(-6f, -6f);
            _fontPreviewTmp = prevTmpGo.AddComponent<TMPro.TextMeshProUGUI>();
            UGUIShip.RelabelText(_fontPreviewTmp, "ui.abg_123");
            _fontPreviewTmp.fontSize = 24f;
            _fontPreviewTmp.alignment = TMPro.TextAlignmentOptions.Center;
            _fontPreviewTmp.enableWordWrapping = true;
            _fontPreviewTmp.raycastTarget = false;
        }

        void OnBrowseFont()
        {
            WinDialogs.PickFile("Pick a font (.ttf / .otf)", new Action<string>(path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                _fontPath = path;
                if (string.IsNullOrWhiteSpace(_nameField.text))
                    UGUIShip.SetInputText(_nameField, Path.GetFileNameWithoutExtension(path), false);
                LoadFontPreview();
                RefreshStep();
                SetStatus(Path.GetFileName(path) + " ready");
            }));
        }

        void LoadFontPreview()
        {
            if (_fontPathLbl != null) UGUIShip.RelabelText(_fontPathLbl, string.IsNullOrEmpty(_fontPath) ? "no file picked" : Path.GetFileName(_fontPath));
            if (string.IsNullOrEmpty(_fontPath) || _fontPreviewTmp == null) return;
            var asset = FontReplacementService.BuildPreview(new FontOverride { fontPath = _fontPath });
            if (asset != null) _fontPreviewTmp.font = asset;
        }

        void BuildTargetStep(RectTransform root, float w, float bodyH)
        {
            float cy = SH;
            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, LH),
                "ui.which_game_font_should_it_replace", FS_SM, LABEL);
            cy += LH + SH;

            var scroll = UGUIShip.CreateScrollView(root.transform, new Rect(PAD, cy, w, bodyH - cy - SH));
            _targetContent = scroll.content;
            var layout = _targetContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 1f;
            _targetContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            RebuildTargetRows();
        }

        void RebuildTargetRows()
        {
            if (_targetContent == null) return;
            _targetRows.Clear();
            for (int i = _targetContent.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_targetContent.GetChild(i).gameObject);

            bool any = false;
            foreach (var pair in FontWhitelist)
            {
                var fa = FontReplacementService.GetFontAssetByName(pair.real);
                if (fa == null) continue; // not loaded yet (open in menu first)
                any = true;

                string real = pair.real;
                var btn = UGUIShip.CreateButton(_targetContent, new Rect(0f, 0f, TabWidth - PAD * 2f, ROW_H), "",
                    real == _targetName ? ROW_SEL : ROW_IDLE, WHITE, FS_SM, new Action(() => SelectTarget(real)));
                btn.transition = Selectable.Transition.None;
                var trigger = btn.GetComponent<UnityEngine.EventSystems.EventTrigger>();
                if (trigger != null) GameObject.Destroy(trigger);
                var le = btn.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = ROW_H;
                le.flexibleWidth = 1f;

                var lbl = btn.GetComponentInChildren<Text>();
                var tmp = UGUIShip.ReplaceTextWithTmp(lbl, pair.display, fa);
                if (tmp != null) tmp.alignment = TMPro.TextAlignmentOptions.MidlineLeft;

                _targetRows.Add(btn);
            }

            if (!any)
                UGUIShip.CreateLabel(_targetContent, new Rect(6f, 0f, TabWidth, ROW_H),
                    "ui.open_the_in_game_menu_first_so_these_fonts_are_l", FS_SM, HINT);
        }

        void SelectTarget(string real)
        {
            _targetName = real;
            RebuildTargetRows();
            RefreshStep();
        }

        void BuildNameStep(RectTransform root, float w, float bodyH)
        {
            float cy = SH;
            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, LH),
                "ui.what_should_this_override_be_called", FS_SM, LABEL);
            cy += LH + SH;

            _nameField = UGUIShip.CreateInputField(root.transform, new Rect(PAD, cy, w, BTN_H),
                "ui.my_font_override", Color.black, WHITE, FS_SM);
            cy += BTN_H + SH * 2f;

            _summaryLbl = UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, LH * 3f), "", FS_SM, HINT);
            _summaryLbl.alignment = TextAnchor.UpperLeft;
        }

        protected override void RefreshSummary()
        {
            _summaryLbl.text = $"file: {(string.IsNullOrEmpty(_fontPath) ? "?" : Path.GetFileName(_fontPath))}\ntarget: {_targetName}";
        }

        protected override bool Save()
        {
            string name = _nameField.text?.Trim() ?? "";
            if (string.IsNullOrEmpty(name)) { SetStatus("ui.give_it_a_name_first"); return false; }
            if (string.IsNullOrEmpty(_fontPath)) { SetStatus("ui.pick_a_font_file_first"); return false; }
            if (string.IsNullOrEmpty(_targetName)) { SetStatus("ui.pick_a_game_font_to_replace"); return false; }

            var entries = FontReplacementService.LoadAll();
            for (int i = 0; i < entries.Count; i++)
            {
                if (i == EditIndex) continue;
                if (entries[i].entryName == name) { SetStatus("ui.you_already_have_one_called_that"); return false; }
            }

            bool editing = EditIndex >= 0 && EditIndex < entries.Count;
            var entry = editing ? entries[EditIndex] : new FontOverride { enabled = true };

            entry.entryName = name;
            entry.fontPath = _fontPath;
            entry.targetFontName = _targetName;

            if (!editing) entries.Add(entry);

            FontReplacementService.SaveAll(entries);
            FontReplacementService.RebuildAndApply();
            Plugin.Log.LogInfo($"font override {(editing ? "updated" : "added")}: {name} -> {_targetName}");
            return true;
        }
    }
}
