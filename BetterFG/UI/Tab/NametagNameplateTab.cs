using System;
using System.Collections;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Nametag;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Tab
{
    public class NametagNameplateTab : WizardTab
    {
        public NametagNameplateTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Nametag - Nameplate";
        protected override string BgResource => "BetterFG.assets.ui.nametag.bg.png";

        private const string KEY_BACKING_ENABLED = "nametag.backing.enabled";
        private const string KEY_BACKING_PATH = "nametag.backing.path";
        private const string KEY_BACKING_OFFSET_X = "nametag.backing.offset.x";
        private const string KEY_BACKING_OFFSET_Y = "nametag.backing.offset.y";
        private const string KEY_BACKING_SCALE = "nametag.backing.scale";
        private const string KEY_NICKNAME_ENABLED = "nametag.nickname.enabled";
        private const string KEY_NICKNAME_TEXT = "nametag.nickname.text";

        private const float BACKING_SCALE_MAX = 10f;

        private bool _backingEnabled;
        private string _backingPath = "";
        private float _backingScale = 1f;
        private float _backingOffX, _backingOffY;
        private bool _backingApplyPending;
        private Coroutine _backingApplyRoutine;
        private bool _nicknameEnabled;
        private string _nicknameText = "";

        private Text _backingEnabledLabel, _backingPathLabel, _nicknameEnabledLabel;
        private RawImage _backingPreview;
        private InputField _nicknameField;

        protected override string[] StepTitles => new[] { "Backing image", "Nickname subtext" };
        protected override bool HasRemove => true;
        protected override BetterFGTab MakeListTarget() => BetterFGTabRegistry.NewTab<NametagTab>();

        void Awake()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            _backingEnabled = SettingsService.Get(KEY_BACKING_ENABLED, "false") == "true";
            _backingPath = SettingsService.Get(KEY_BACKING_PATH, "");
            _backingScale = float.TryParse(SettingsService.Get(KEY_BACKING_SCALE, "1"), System.Globalization.NumberStyles.Float, ci, out float bsv) ? bsv : 1f;
            _backingOffX = float.TryParse(SettingsService.Get(KEY_BACKING_OFFSET_X, "0"), System.Globalization.NumberStyles.Float, ci, out float box) ? box : 0f;
            _backingOffY = float.TryParse(SettingsService.Get(KEY_BACKING_OFFSET_Y, "0"), System.Globalization.NumberStyles.Float, ci, out float boy) ? boy : 0f;
            _nicknameEnabled = SettingsService.Get(KEY_NICKNAME_ENABLED, "false") == "true";
            _nicknameText = SettingsService.Get(KEY_NICKNAME_TEXT, "");
        }

        protected override void BuildStep(int step, RectTransform root, float w, float bodyH)
        {
            var (_, c) = UGUIShip.CreateScrollView(root, new Rect(0f, 0f, TabWidth, bodyH));
            float cw = w - 26f;
            switch (step)
            {
                case 0: BuildBackingStep(c, cw); break;
                case 1: BuildNicknameStep(c, cw); break;
            }
        }

        private void BuildBackingStep(RectTransform c, float w)
        {
            float x = PAD, cy = PAD;
            float colGap = PAD * 0.5f;
            float halfW = (w - colGap) / 2f;
            var enabledBtn = UGUIShip.CreateButton(c, new Rect(x, cy, halfW, BTN_H),
                _backingEnabled ? "Backing: ON" : "Backing: OFF", BTN_DARK, WHITE, FS_SM, new Action(OnToggleBacking));
            _backingEnabledLabel = enabledBtn.GetComponentInChildren<Text>();
            UGUIShip.CreateButton(c, new Rect(x + halfW + colGap, cy, halfW, BTN_H),
                "Browse...", new Color(0.25f, 0.35f, 0.45f, 1f), WHITE, FS_SM, new Action(OnBrowseBacking));
            cy += BTN_H + SH;

            _backingPathLabel = UGUIShip.CreateLabel(c, new Rect(x, cy, w, LH),
                string.IsNullOrEmpty(_backingPath) ? "No file selected" : Path.GetFileName(_backingPath), FS_SM, HINT);
            cy += LH + SH;

            float prevBoxH = w / 3.65f;
            var prevGo = new GameObject("BackingPreview");
            prevGo.transform.SetParent(c, false);
            UGUIShip.SetPixelRect(prevGo.AddComponent<RectTransform>(), new Rect(x, cy, w, prevBoxH));
            prevGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);
            var rawGo = new GameObject("Raw");
            rawGo.transform.SetParent(prevGo.transform, false);
            var rawRt = rawGo.AddComponent<RectTransform>();
            rawRt.anchorMin = Vector2.zero;
            rawRt.anchorMax = Vector2.one;
            rawRt.offsetMin = rawRt.offsetMax = Vector2.zero;
            _backingPreview = rawGo.AddComponent<RawImage>();
            _backingPreview.raycastTarget = false;
            cy += prevBoxH + SH;

            UGUIShip.CreateSlider(c, x, cy, w, "S", _backingScale / BACKING_SCALE_MAX, LH, PAD, FS_SM,
                val => { _backingScale = val * BACKING_SCALE_MAX; SaveBackingTransform(); RefreshBackingPreview(); QueueBackingApply(); },
                null, null, true, 0f, 1f / BACKING_SCALE_MAX);
            cy += LH + SH;
            UGUIShip.CreateSlider(c, x, cy, w, "X", _backingOffX + 0.5f, LH, PAD, FS_SM,
                val => { _backingOffX = val - 0.5f; SaveBackingTransform(); RefreshBackingPreview(); QueueBackingApply(); }, null, null, true, 0f, 0.5f);
            cy += LH + SH;
            UGUIShip.CreateSlider(c, x, cy, w, "Y", _backingOffY + 0.5f, LH, PAD, FS_SM,
                val => { _backingOffY = val - 0.5f; SaveBackingTransform(); RefreshBackingPreview(); QueueBackingApply(); }, null, null, true, 0f, 0.5f);
            cy += LH + PAD;
            c.sizeDelta = new Vector2(0f, cy + PAD);
            RefreshBackingPreview();
        }

        private void BuildNicknameStep(RectTransform c, float w)
        {
            float x = PAD, cy = PAD;
            float nickColGap = PAD * 0.5f;
            float toggleW = w * 0.42f;
            var nickBtn = UGUIShip.CreateButton(c, new Rect(x, cy, toggleW, BTN_H),
                _nicknameEnabled ? "Nickname: ON" : "Nickname: OFF", BTN_DARK, WHITE, FS_SM, new Action(OnToggleNickname));
            _nicknameEnabledLabel = nickBtn.GetComponentInChildren<Text>();
            float nickFieldW = w - toggleW - nickColGap;
            _nicknameField = UGUIShip.CreateInputField(c, new Rect(x + toggleW + nickColGap, cy, nickFieldW, BTN_H),
                "nickname", new Color(0.12f, 0.12f, 0.12f, 1f), WHITE, FS_SM);
            UGUIShip.SetInputText(_nicknameField, _nicknameText, false);
            _nicknameField.onEndEdit.AddListener(new Action<string>(OnNicknameEdited));
            cy += BTN_H + PAD;
            c.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void OnToggleNickname()
        {
            _nicknameEnabled = !_nicknameEnabled;
            if (_nicknameEnabledLabel != null) _nicknameEnabledLabel.text = _nicknameEnabled ? "Nickname: ON" : "Nickname: OFF";
            SettingsService.Set(KEY_NICKNAME_ENABLED, _nicknameEnabled ? "true" : "false");
            ApplyNicknameNow();
        }

        private void OnNicknameEdited(string value)
        {
            _nicknameText = value ?? "";
            SettingsService.Set(KEY_NICKNAME_TEXT, _nicknameText);
            ApplyNicknameNow();
        }

        private void ApplyNicknameNow()
        {
            NametagFinder.ReapplyAllNameplates();
            var localTag = NametagFinder.FindLocalNameTagSprite();
            if (localTag != null)
                NametagIconApplicator.ApplyLocalNickname(localTag, party: false);
        }

        private void RefreshBackingPreview()
        {
            if (_backingPreview == null) return;
            if (string.IsNullOrEmpty(_backingPath))
            {
                _backingPreview.texture = null;
                _backingPreview.color = new Color(1f, 1f, 1f, 0f);
                return;
            }
            var spr = NametagIconApplicator.GetBackingPreviewSprite(_backingPath, _backingOffX, _backingOffY, _backingScale);
            _backingPreview.texture = spr != null ? spr.texture : null;
            _backingPreview.color = spr != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        }

        private void OnToggleBacking()
        {
            _backingEnabled = !_backingEnabled;
            if (_backingEnabledLabel != null) _backingEnabledLabel.text = _backingEnabled ? "Backing: ON" : "Backing: OFF";
        }

        private void OnBrowseBacking()
        {
            WinDialogs.PickPng("Select nameplate backing PNG", path =>
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                _backingPath = path;
                if (_backingPathLabel != null) _backingPathLabel.text = Path.GetFileName(_backingPath);
                RefreshBackingPreview();
            });
        }

        private void SaveBackingTransform()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            SettingsService.Set(KEY_BACKING_SCALE, _backingScale.ToString(ci));
            SettingsService.Set(KEY_BACKING_OFFSET_X, _backingOffX.ToString(ci));
            SettingsService.Set(KEY_BACKING_OFFSET_Y, _backingOffY.ToString(ci));
        }

        private void QueueBackingApply()
        {
            _backingApplyPending = true;
            if (_backingApplyRoutine == null)
                _backingApplyRoutine = StartCoroutine(ApplyBackingLoop().WrapToIl2Cpp());
        }

        private IEnumerator ApplyBackingLoop()
        {
            while (_backingApplyPending)
            {
                _backingApplyPending = false;
                if (_backingEnabled) NametagFinder.ReapplyAllNameplates();
                yield return new WaitForSeconds(0.15f);
            }
            _backingApplyRoutine = null;
        }

        protected override bool Save()
        {
            SettingsService.Set(KEY_BACKING_ENABLED, _backingEnabled ? "true" : "false");
            SettingsService.Set(KEY_BACKING_PATH, _backingPath ?? "");
            SaveBackingTransform();
            SettingsService.Set(KEY_NICKNAME_ENABLED, _nicknameEnabled ? "true" : "false");
            SettingsService.Set(KEY_NICKNAME_TEXT, _nicknameText ?? "");

            NametagFinder.ReapplyAllNameplates();
            return true;
        }

        protected override void OnRemoveClicked()
        {
            SettingsService.Set(KEY_BACKING_ENABLED, "false");
            NametagFinder.ReapplyAllNameplates();
        }
    }
}
