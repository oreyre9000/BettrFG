using System;
using System.IO;
using BetterFG.Customization.UI;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class MenuBackgroundImageWizardTab : WizardTab
    {
        public MenuBackgroundImageWizardTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => EditIndex >= 0 ? "Background Image - Edit" : "Background Image - New";
        protected override string BgResource => "BetterFG.assets.ui.tab.menu.png";

        private enum WizardStep { Image, Transform, Name }
        protected override string[] StepTitles => new[]
        {
            "ui.pick_the_image",
            "ui.position_it",
            "ui.name_it"
        };

        private string _path = "";
        private Texture2D _tex;
        private RawImage _preview;
        private Text _pathLbl;

        private float _posX, _posY, _posZ;
        private float _rotX, _rotY, _rotZ;
        private float _scale = MenuCustomizationApplication.BG_IMG_SCALE_DEFAULT;
        private float _scaleX = MenuCustomizationApplication.BG_IMG_SCALE_AXIS_DEFAULT;
        private float _scaleY = MenuCustomizationApplication.BG_IMG_SCALE_AXIS_DEFAULT;

        private InputField _nameField;
        private Text _summaryLbl;

        // Transform step's sliders are built (with whatever the fields hold at that moment) before
        // LoadEditedEntry ever runs — WizardTab builds every step up front, then loads the edited entry
        // after. so on edit, the fields get the saved values but the slider handles are stuck showing
        // their build-time defaults. keep refs + ranges so LoadEditedEntry can push the handles back in
        // sync once the real values are known.
        private readonly Slider[] _transformSliders = new Slider[9];
        private readonly (float min, float max)[] _transformRanges = new (float, float)[9];

        // preview key: editing reuses the entry's OWN name, so the live scrub updates that entry's
        // real, already-visible quad in place instead of spawning a second one beside it. adding a new
        // entry has no real quad to reuse yet, so it gets a scratch key nothing else will ever collide
        // with in practice.
        private string _previewKey = "__wizard_preview_new__";

        protected override void BuildStep(int step, RectTransform root, float w, float bodyH)
        {
            switch ((WizardStep)step)
            {
                case WizardStep.Image: BuildImageStep(root, w, bodyH); break;
                case WizardStep.Transform: BuildTransformStep(root, w, bodyH); break;
                case WizardStep.Name: BuildNameStep(root, w, bodyH); break;
            }
        }

        protected override int LoadEditedEntry()
        {
            var entries = MenuCustomizationApplication.LoadBgImageEntries();
            if (EditIndex >= entries.Count) { EditIndex = -1; return -1; }

            var e = entries[EditIndex];
            _path = e.path;
            _posX = e.posX; _posY = e.posY; _posZ = e.posZ;
            _rotX = e.rotX; _rotY = e.rotY; _rotZ = e.rotZ;
            _scale = e.scale; _scaleX = e.scaleX; _scaleY = e.scaleY;
            _previewKey = e.entryName;
            UGUIShip.SetInputText(_nameField, e.entryName, false);
            LoadPreview();
            SyncTransformSliders();
            UpdateWorldPreview();
            return -1;
        }

        private void SyncTransformSliders()
        {
            float[] vals = { _posX, _posY, _posZ, _rotX, _rotY, _rotZ, _scale, _scaleX, _scaleY };
            for (int i = 0; i < _transformSliders.Length; i++)
            {
                if (_transformSliders[i] == null) continue;
                var (min, max) = _transformRanges[i];
                _transformSliders[i].SetValueWithoutNotify(Mathf.InverseLerp(min, max, vals[i]));
            }
        }

        private void UpdateWorldPreview()
        {
            MenuCustomizationApplication.Instance?.PreviewBgImage(_previewKey, _path,
                _posX, _posY, _posZ, _rotX, _rotY, _rotZ, _scale, _scaleX, _scaleY);
        }

        // called by WizardTab's LeaveToList() on every way out (Save, or Back-out at step 0) — NOT
        // OnClosed(), which SwitchSlotTab never actually calls (it destroys the wizard's GameObject
        // directly). the wizard's changes aren't saved until Save() but the quad it previews onto IS
        // the real one in the menu, so on the way out reapply the actual saved settings — that's a
        // no-op after Save (already matches) and a full revert of any unsaved scrub after Back-out.
        protected override Tab MakeListTarget()
        {
            MenuCustomizationApplication.Instance?.ApplyImageBgFromSettings();
            return BetterFGTabRegistry.CreateTab("Main Menu");
        }

        protected override bool CanAdvance(int step)
        {
            switch ((WizardStep)step)
            {
                case WizardStep.Image: return !string.IsNullOrEmpty(_path);
                default: return true;
            }
        }

        private void BuildImageStep(RectTransform root, float w, float bodyH)
        {
            float cy = SH;
            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, LH),
                "ui.pick_the_image_to_use_as_a_background", FS_SM, LABEL);
            cy += LH + SH;

            float browseW = 80f * UIScale.S;
            UGUIShip.CreateButton(root.transform, new Rect(PAD, cy, browseW, BTN_H),
                "ui.browse_2", BTN_BLUE, WHITE, FS_SM, new Action(OnBrowse));
            _pathLbl = UGUIShip.CreateLabel(root.transform, new Rect(PAD + browseW + PAD, cy, w - browseW - PAD, BTN_H),
                "ui.no_file_picked", FS_SM, HINT, TextAnchor.MiddleLeft);
            cy += BTN_H + SH * 2f;

            float previewSz = Mathf.Min(bodyH - cy - SH, w);
            var frameGo = new GameObject("BgImgPreview");
            frameGo.transform.SetParent(root.transform, false);
            var frameRt = frameGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(frameRt, new Rect(PAD + (w - previewSz) * 0.5f, cy, previewSz, previewSz));
            var (_, inner) = UGUIShip.CreateFramedImage(frameGo.transform, new Color(0f, 0f, 0f, 0.45f));
            _preview = inner.gameObject.AddComponent<RawImage>();
            _preview.raycastTarget = false;
            _preview.color = Color.clear;

            LoadPreview();
        }

        private void OnBrowse()
        {
            WinDialogs.PickPng("Select background image", path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                _path = path;
                LoadPreview();
                UpdateWorldPreview();
                if (_nameField != null && string.IsNullOrWhiteSpace(_nameField.text))
                    UGUIShip.SetInputText(_nameField, Path.GetFileNameWithoutExtension(path), false);
                RefreshStep();
                SetStatus(Path.GetFileName(path) + " ready");
            });
        }

        private void LoadPreview()
        {
            if (_pathLbl != null) UGUIShip.RelabelText(_pathLbl, string.IsNullOrEmpty(_path) ? "no file picked" : Path.GetFileName(_path));
            if (string.IsNullOrEmpty(_path) || !File.Exists(_path) || _preview == null) return;

            try
            {
                if (_tex == null) _tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                _tex.LoadImage(File.ReadAllBytes(_path));
                _tex.Apply();
                _preview.texture = _tex;
                _preview.color = WHITE;
            }
            catch (Exception e) { SetStatus("couldn't read that image: " + e.Message); }
        }

        private void BuildTransformStep(RectTransform root, float w, float bodyH)
        {
            var (scrollRect, content) = UGUIShip.CreateScrollView(root, new Rect(0f, 0f, TabWidth, bodyH));
            float cy = SH;

            UGUIShip.CreateLabel(content, new Rect(PAD, cy, w, LH), "ui.position", FS_SM, HINT);
            cy += LH + SH;
            Sl(0, content, cy, w, "X", _posX, MenuCustomizationApplication.BG_IMG_POS_MIN, MenuCustomizationApplication.BG_IMG_POS_MAX,
                v => { _posX = v; UpdateWorldPreview(); }, MenuCustomizationApplication.BG_IMG_POS_DEFAULT);
            cy += LH + SH;
            Sl(1, content, cy, w, "Y", _posY, MenuCustomizationApplication.BG_IMG_POS_MIN, MenuCustomizationApplication.BG_IMG_POS_MAX,
                v => { _posY = v; UpdateWorldPreview(); }, MenuCustomizationApplication.BG_IMG_POS_DEFAULT);
            cy += LH + SH;
            Sl(2, content, cy, w, "Z", _posZ, MenuCustomizationApplication.BG_IMG_POS_MIN, MenuCustomizationApplication.BG_IMG_POS_MAX,
                v => { _posZ = v; UpdateWorldPreview(); }, MenuCustomizationApplication.BG_IMG_POS_DEFAULT);
            cy += LH + PAD;

            UGUIShip.CreateLabel(content, new Rect(PAD, cy, w, LH), "ui.rotation", FS_SM, HINT);
            cy += LH + SH;
            Sl(3, content, cy, w, "X", _rotX, 0f, 360f, v => { _rotX = v; UpdateWorldPreview(); }, 0f);
            cy += LH + SH;
            Sl(4, content, cy, w, "Y", _rotY, 0f, 360f, v => { _rotY = v; UpdateWorldPreview(); }, 0f);
            cy += LH + SH;
            Sl(5, content, cy, w, "Z", _rotZ, 0f, 360f, v => { _rotZ = v; UpdateWorldPreview(); }, 0f);
            cy += LH + PAD;

            UGUIShip.CreateLabel(content, new Rect(PAD, cy, w, LH), "ui.scale", FS_SM, HINT);
            cy += LH + SH;
            Sl(6, content, cy, w, "Uniform", _scale, MenuCustomizationApplication.BG_IMG_SCALE_MIN, MenuCustomizationApplication.BG_IMG_SCALE_MAX,
                v => { _scale = v; UpdateWorldPreview(); }, MenuCustomizationApplication.BG_IMG_SCALE_DEFAULT);
            cy += LH + SH;
            Sl(7, content, cy, w, "X", _scaleX, MenuCustomizationApplication.BG_IMG_SCALE_AXIS_MIN, MenuCustomizationApplication.BG_IMG_SCALE_AXIS_MAX,
                v => { _scaleX = v; UpdateWorldPreview(); }, MenuCustomizationApplication.BG_IMG_SCALE_AXIS_DEFAULT);
            cy += LH + SH;
            Sl(8, content, cy, w, "Y", _scaleY, MenuCustomizationApplication.BG_IMG_SCALE_AXIS_MIN, MenuCustomizationApplication.BG_IMG_SCALE_AXIS_MAX,
                v => { _scaleY = v; UpdateWorldPreview(); }, MenuCustomizationApplication.BG_IMG_SCALE_AXIS_DEFAULT);
            cy += LH + PAD;

            content.sizeDelta = new Vector2(0f, cy + PAD);
        }

        // arbitrary-range slider, matches MenuTab's BuildSliderRaw shape — slotIdx keeps the built
        // Slider + its range around so LoadEditedEntry can resync the handle after loading real values
        private void Sl(int slotIdx, Transform parent, float y, float w, string lbl, float init, float min, float max,
            Action<float> onChange, float resetTo)
        {
            var slider = UGUIShip.CreateSlider(parent, PAD, y, w, lbl, Mathf.InverseLerp(min, max, init),
                LH, PAD, FS_SM, t => onChange(Mathf.Lerp(min, max, t)), null, null, true,
                Mathf.InverseLerp(min, max, resetTo));
            _transformSliders[slotIdx] = slider;
            _transformRanges[slotIdx] = (min, max);
        }

        private void BuildNameStep(RectTransform root, float w, float bodyH)
        {
            float cy = SH;
            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, LH),
                "ui.what_should_this_background_be_called", FS_SM, LABEL);
            cy += LH + SH;

            _nameField = UGUIShip.CreateInputField(root.transform, new Rect(PAD, cy, w, BTN_H),
                "ui.my_background", Color.black, WHITE, FS_SM);
            cy += BTN_H + SH * 2f;

            _summaryLbl = UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, LH * 4f), "", FS_SM, HINT);
            _summaryLbl.alignment = TextAnchor.UpperLeft;
        }

        protected override void RefreshSummary()
        {
            _summaryLbl.text =
                $"image: {(string.IsNullOrEmpty(_path) ? "?" : Path.GetFileName(_path))}\n" +
                $"pos: ({_posX:0.##}, {_posY:0.##}, {_posZ:0.##})\n" +
                $"rot: ({_rotX:0.##}, {_rotY:0.##}, {_rotZ:0.##})\n" +
                $"scale: {_scale:0.##} ({_scaleX:0.##}x, {_scaleY:0.##}y)";
        }

        protected override bool Save()
        {
            string name = _nameField.text?.Trim() ?? "";
            if (string.IsNullOrEmpty(name)) { SetStatus("ui.give_it_a_name_first"); return false; }

            var entries = MenuCustomizationApplication.LoadBgImageEntries();
            for (int i = 0; i < entries.Count; i++)
            {
                if (i == EditIndex) continue;
                if (entries[i].entryName == name) { SetStatus("ui.you_already_have_one_called_that"); return false; }
            }

            bool editing = EditIndex >= 0 && EditIndex < entries.Count;
            var entry = editing ? entries[EditIndex] : new MenuCustomizationApplication.BgImageEntry();

            entry.entryName = name;
            entry.path = _path;
            entry.posX = _posX; entry.posY = _posY; entry.posZ = _posZ;
            entry.rotX = _rotX; entry.rotY = _rotY; entry.rotZ = _rotZ;
            entry.scale = _scale; entry.scaleX = _scaleX; entry.scaleY = _scaleY;
            if (!editing) entry.enabled = true;

            if (!editing) entries.Add(entry);
            MenuCustomizationApplication.SaveBgImageEntries(entries);
            // MakeListTarget() (called right after this returns true) reapplies from settings — no
            // need to do it here too.

            Plugin.Log.LogInfo($"background image {(editing ? "updated" : "added")}: {name}");
            return true;
        }
    }
}
