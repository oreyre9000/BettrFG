using System;
using System.Collections.Generic;
using BetterFG.Customization.Pets;
using BetterFG.Customization.Player;
using BetterFG.Customization.Social;
using BetterFG.Services;
using BetterFG.Network;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class PetWizardTab : WizardTab
    {
        public PetWizardTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => EditIndex >= 0 ? "Pet - Edit" : "Pet - New";
        protected override string BgResource => "BetterFG.assets.ui.tab.customskintexture.png";

        enum WizardStep { Bean, SkinTexture, Phrases }
        protected override string[] StepTitles => new[]
        {
            "pet.step_bean", "ui.skin_texture_optional", "ui.phrases_optional"
        };

        string _id = Guid.NewGuid().ToString("N");
        string _name = "Pet";
        float _scale = 0.6f;
        string _top = "", _bottom = "", _pattern = "", _faceplate = "", _colour = "";
        readonly List<RemoteSkinEntry> _skins = new List<RemoteSkinEntry>();
        List<SkinTexEntry> _skinTexEntries = new List<SkinTexEntry>();
        readonly List<PhraseEntry> _phrases = new List<PhraseEntry>();
        float _phraseIntervalMin = 15f, _phraseIntervalMax = 45f;

        RawImage _previewImg;
        Text _lookLbl;
        Text _skinTexLbl;

        void Update() { if (IsOpen) PetPreview.Render(); }

        protected override void BuildContent(RectTransform contentRoot)
        {
            _previewImg = PetPreviewPanel.Build(Root, TabWidth, TabHeight, TITLE_H, SH, UIScale.S);
            _previewImg.transform.parent.gameObject.SetActive(false);
            base.BuildContent(contentRoot);
        }

        public override void OnOpened()
        {
            if (_previewImg != null) _previewImg.transform.parent.gameObject.SetActive(true);
        }

        public override void OnClosed()
        {
            if (_previewImg != null) _previewImg.transform.parent.gameObject.SetActive(false);
            PetPreview.Invalidate();
        }

        protected override void BuildStep(int step, RectTransform root, float w, float bodyH)
        {
            switch ((WizardStep)step)
            {
                case WizardStep.Bean: BuildBeanStep(root, w); break;
                case WizardStep.SkinTexture: BuildSkinTextureStep(root, w); break;
                case WizardStep.Phrases: BuildPhrasesStep(root, w); break;
            }
        }

        void BuildBeanStep(RectTransform root, float w)
        {
            float y = SH;
            UGUIShip.CreateLabel(root, new Rect(PAD, y, w, LH), "pet.bean_blurb", FS_SM, HINT, TextAnchor.UpperLeft);
            y += LH + SH;

            _lookLbl = UGUIShip.CreateLabel(root, new Rect(PAD, y, w, LH * 3f), LookSummary(), FS_SM, WHITE, TextAnchor.UpperLeft);
            y += LH * 3f + SH;

            UGUIShip.CreateButton(root, new Rect(PAD, y, w, BTN_H), "pet.edit_bean", BTN_BLUE, WHITE, FS_SM,
                new Action(OpenBeanCreator));
        }

        string LookSummary() =>
            LocalizationService.Format("pet.look_summary_fmt", _name,
                Named(_top), Named(_bottom), Named(_pattern), Named(_faceplate), Named(_colour),
                _scale.ToString("0.00"), _skins.Count);

        static string Named(string optionName) =>
            string.IsNullOrEmpty(optionName) ? "-" : SkinApplicationService.ResolveOptionDisplayName(optionName);

        void OpenBeanCreator()
        {
            BetterFGUIMan.Instance?.SetVisible(false);
            BeanCreatorScreen.Open(BeanLook.From(CurrentData()), new Action<BeanLook>(OnBeanCreatorClosed),
                new Action(OpenSkinTextures), new Action(OpenUgc));
        }

        void OnBeanCreatorClosed(BeanLook look)
        {
            BetterFGUIMan.Instance?.SetVisible(true);
            if (look != null)
            {
                _name = string.IsNullOrEmpty(look.name) ? _name : look.name;
                _top = look.top; _bottom = look.bottom; _pattern = look.pattern;
                _faceplate = look.faceplate; _colour = look.colour;
                _scale = look.scale;
                _skins.Clear();
                _skins.AddRange(look.skins);
                _skinTexEntries = new List<SkinTexEntry>(look.texEntries);
            }
            if (_lookLbl != null) UGUIShip.RelabelText(_lookLbl, LookSummary());
            if (_skinTexLbl != null) UGUIShip.RelabelText(_skinTexLbl, SkinTexSummary());
            RefreshPreviewFromLook();
        }

        void BuildSkinTextureStep(RectTransform root, float w)
        {
            float y = SH;
            UGUIShip.CreateLabel(root, new Rect(PAD, y, w, LH),
                "ui.optional_give_this_pet_its_own_texture_overrides", FS_SM, HINT, TextAnchor.UpperLeft);
            y += LH + SH;

            _skinTexLbl = UGUIShip.CreateLabel(root, new Rect(PAD, y, w, LH), SkinTexSummary(), FS_SM, WHITE);
            y += LH + SH;

            UGUIShip.CreateButton(root, new Rect(PAD, y, w, BTN_H), "ui.edit_skin_textures", BTN_BLUE, WHITE, FS_SM,
                new Action(OpenSkinTextures));
        }

        string SkinTexSummary() =>
            _skinTexEntries.Count == 0
                ? LocalizationService.Get("pet.none_yet")
                : LocalizationService.Format("pet.textures_applied_fmt", _skinTexEntries.Count);

        CustomizationTab _ugcPicker;

        // the bean creator stays up the whole time - its panel just steps aside so the real UGC tab
        // has the screen, and the 3D bean keeps rebuilding behind it as pieces go on and off
        void OpenUgc()
        {
            var creator = BeanCreatorScreen.Instance;
            if (creator == null) return;

            _ugcPicker = BetterFGTabRegistry.NewTab<CustomizationTab>();
            _ugcPicker.PickTargetList = creator.LookSkins;
            _ugcPicker.OnPickChanged = new Action(creator.RefreshFromLook);
            _ugcPicker.PickBack = new Action(CloseUgc);

            creator.SetPanelVisible(false);
            BetterFGUIMan.Instance?.SetVisible(true);
            BetterFGUIMan.Instance?.PushSlotTab(this, _ugcPicker);
        }

        void CloseUgc()
        {
            var picker = _ugcPicker;
            _ugcPicker = null;
            if (picker != null) BetterFGUIMan.Instance?.PopSlotTab(picker);
            BetterFGUIMan.Instance?.SetVisible(false);
            BeanCreatorScreen.Instance?.SetPanelVisible(true);
        }

        void OpenSkinTextures()
        {
            var picker = BetterFGTabRegistry.NewTab<PetSkinTextureTab>();
            picker.Snapshot = CurrentData();
            picker.EditIndexCarry = EditIndex;
            BetterFGUIMan.Instance?.SwitchSlotTab(this, picker);
        }

        void BuildPhrasesStep(RectTransform root, float w)
        {
            float y = SH;
            UGUIShip.CreateLabel(root, new Rect(PAD, y, w, LH),
                "ui.optional_phrases_the_pet_pops_up_above_its_head", FS_SM, HINT, TextAnchor.UpperLeft);
            y += LH + SH;

            UGUIShip.CreateLabel(root, new Rect(PAD, y, w, LH),
                _phrases.Count == 0
                    ? LocalizationService.Get("pet.none_yet")
                    : LocalizationService.Format("pet.phrase_count_fmt", _phrases.Count), FS_SM, WHITE);
            y += LH + SH;

            UGUIShip.CreateButton(root, new Rect(PAD, y, w, BTN_H), "ui.edit_phrases", BTN_BLUE, WHITE, FS_SM,
                new Action(OpenPhrases));
        }

        void OpenPhrases()
        {
            var picker = BetterFGTabRegistry.NewTab<PetPhrasesTab>();
            picker.Snapshot = CurrentData();
            picker.EditIndexCarry = EditIndex;
            BetterFGUIMan.Instance?.SwitchSlotTab(this, picker);
        }

        void RefreshPreviewFromLook()
        {
            PetPreview.Rebuild(this, CurrentData());
            if (_previewImg != null) _previewImg.texture = PetPreview.Ensure();
        }

        PetData CurrentData()
        {
            var data = new PetData
            {
                id = _id,
                name = string.IsNullOrEmpty(_name) ? "Pet" : _name.Trim(),
                costumeTop = _top, costumeBottom = _bottom, pattern = _pattern, faceplate = _faceplate, colour = _colour,
                scale = Mathf.Clamp(_scale, 0.3f, 2f),
                skinTexEntries = _skinTexEntries,
                phrases = new List<PhraseEntry>(_phrases),
                phraseIntervalMin = _phraseIntervalMin,
                phraseIntervalMax = _phraseIntervalMax,
            };
            data.skins.AddRange(_skins);
            return data;
        }

        public PetSkinTextureTab ResumeFromSkinTexture;
        public PetPhrasesTab ResumeFromPhrases;
        protected override bool SkipLoadEditedEntry =>
            ResumeFromSkinTexture != null || ResumeFromPhrases != null;

        PetData _pendingLoad;

        protected override int LoadEditedEntry()
        {
            var pets = PetService.Instance?.Pets;
            if (pets == null || EditIndex >= pets.Count) { EditIndex = -1; return -1; }
            _pendingLoad = pets[EditIndex];
            return 0;
        }

        protected override void ResumeWip()
        {
            PetData data = null;
            if (ResumeFromSkinTexture != null)
            {
                data = ResumeFromSkinTexture.Snapshot;
                Step = (int)WizardStep.SkinTexture;
                ResumeFromSkinTexture = null;
            }
            else if (ResumeFromPhrases != null)
            {
                data = ResumeFromPhrases.Snapshot;
                Step = (int)WizardStep.Phrases;
                ResumeFromPhrases = null;
            }
            else if (_pendingLoad != null)
            {
                data = _pendingLoad;
                _pendingLoad = null;
            }

            if (data != null) ApplyData(data);
            RefreshPreviewFromLook();
        }

        void ApplyData(PetData data)
        {
            _id = data.id;
            _name = data.name ?? "Pet";
            _scale = data.scale;
            _top = data.costumeTop ?? ""; _bottom = data.costumeBottom ?? "";
            _pattern = data.pattern ?? ""; _faceplate = data.faceplate ?? ""; _colour = data.colour ?? "";
            _skins.Clear();
            _skins.AddRange(data.skins);
            _skinTexEntries = data.skinTexEntries ?? new List<SkinTexEntry>();
            _phrases.Clear();
            if (data.phrases != null) _phrases.AddRange(data.phrases);
            _phraseIntervalMin = data.phraseIntervalMin;
            _phraseIntervalMax = data.phraseIntervalMax;
            if (_lookLbl != null) UGUIShip.RelabelText(_lookLbl, LookSummary());
            if (_skinTexLbl != null) UGUIShip.RelabelText(_skinTexLbl, SkinTexSummary());
        }

        protected override Tab MakeListTarget() => BetterFGTabRegistry.CreateTab("Pets");

        protected override bool Save()
        {
            var data = CurrentData();
            if (string.IsNullOrEmpty(data.name)) { SetStatus("ui.give_your_pet_a_name_first"); return false; }
            PetService.Instance?.SavePet(data);
            Plugin.Log.LogInfo($"pet saved: {data.name} ({data.costumeTop}/{data.costumeBottom}/{data.pattern}, scale {data.scale}, {data.skins.Count} ugc)");
            return true;
        }

        protected override void OnLeave() => PetPreview.Invalidate();
    }
}
