using System;
using BetterFG.Customization.UI;
using BetterFG.Features.CreativeIncrements;
using BetterFG.Features.UnityRound.Editor;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    // Creative-only tab. Subtabs: Load (pick a round info.json + load/unload), Custom Textures
    // (override obstacle textures), Args (player speed/gravity etc - not done yet). When you're not
    // in the level editor the whole body is just a "you're not in Creative" message, no subtabs.
    public class CreativeTab : Tab
    {
        public CreativeTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Creative";
        protected override string TitleId => "ui.creative";
        protected override string BgResource => "BetterFG.assets.ui.tab.creative.png";

        private static float subTabH => BTN_H * 0.9f;

        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color WHITE = UGUIShip.WHITE;
        private static readonly Color OK = new Color(0.55f, 0.85f, 0.55f, 1f);
        private static readonly Color ERR = new Color(0.9f, 0.45f, 0.45f, 1f);
        private static readonly Color SEL_COLOR = new Color(0.25f, 0.5f, 0.25f, 1f);
        private static readonly Color BTN_DARK = UGUIShip.BTN_DARK;
        private static readonly Color BTN_BLUE = new Color(0.22f, 0.34f, 0.55f, 1f);
        private static readonly Color BTN_GREEN = new Color(0.25f, 0.5f, 0.25f, 1f);
        private static readonly Color BTN_RED = new Color(0.45f, 0.25f, 0.25f, 1f);

        private enum SubTab { Args, UnityRound }
        // remembered across rebuilds so drilling into the Custom Textures tab and coming back lands
        // you on the same subtab + wizard step you left (a rebuilt tab is a fresh instance)
        private static SubTab _lastSub = SubTab.Args;
        private SubTab _sub = _lastSub;
        private Button _btnArgs, _btnRound;
        private GameObject _bodyRoot, _argsPanel, _roundPanel, _notInCreative;

        // Unity Round subtab is a 3-step wizard: Load -> Configuration -> Publish.
        private enum Step { Load, Config, Publish }
        private static Step _lastStep = Step.Load;
        private Step _step = _lastStep;
        private static readonly string[] StepTitles = { "Load round", "Configuration", "Publish" };
        private GameObject _loadStep, _configStep, _publishStep;
        private Text _stepHeader;
        private Button _backBtn, _nextBtn;

        // load step
        private InputField _pathField;
        private Text _statusLabel;
        // share-code section: paste an info.json url, get the owner/repo/round_xxx string to put in
        // your level's description so it loads while playing online.
        private InputField _shareUrlField;
        private Text _shareStatus;

        // args panel
        private Button _incToggle, _batchToggle;
        private Text _gizmoValue;

        // config step
        private Button _keepToggle;

        private bool _wasInEditor;
        private bool _wasSpawned;

        // ── build ─────────────────────────────────────────────────────────────

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;
            float y = VPAD;

            // subtab bar
            float half = (w - PAD * 0.5f) / 2f;
            _btnArgs = UGUIShip.CreateButton(contentRoot, new Rect(PAD, y, half, subTabH),
                "ui.args", _sub == SubTab.Args ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetSub(SubTab.Args)));
            _btnRound = UGUIShip.CreateButton(contentRoot, new Rect(PAD + half + PAD * 0.5f, y, half, subTabH),
                "ui.unity_round", _sub == SubTab.UnityRound ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetSub(SubTab.UnityRound)));
            y += subTabH + SH;

            UGUIShip.CreatePanel(contentRoot, new Rect(PAD, y, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            y += 1f + SH;

            float bodyH = TabHeight - y - VPAD;

            _bodyRoot = MakePanel(contentRoot, y, bodyH);

            _argsPanel = MakePanel(_bodyRoot.GetComponent<RectTransform>(), 0f, bodyH);
            BuildArgsPanel(_argsPanel.GetComponent<RectTransform>(), w, bodyH);

            _roundPanel = MakePanel(_bodyRoot.GetComponent<RectTransform>(), 0f, bodyH);
            BuildRoundWizard(_roundPanel.GetComponent<RectTransform>(), w, bodyH);

            // "not in creative" message, shown over everything when out of the editor
            _notInCreative = MakePanel(contentRoot, y, bodyH);
            UGUIShip.CreateLabel(_notInCreative.transform, new Rect(PAD, bodyH * 0.5f - 12f, w, 24f),
                "ui.you_are_not_in_creative_right_now", FS, HINT, TextAnchor.MiddleCenter);

            Refresh();
        }

        GameObject MakePanel(RectTransform parent, float y, float h)
        {
            var go = new GameObject("Panel");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(rt, new Rect(0f, y, TabWidth, h));
            return go;
        }

        // external nav entry point (e.g. BatchEditWindow's "increment settings" link) — same pattern
        // as EmoticonsPhrasesTab.ShowEmotesSubTab / UITab.ShowScreenSubTab.
        public void ShowArgsSubTab() => SetSub(SubTab.Args);

        void SetSub(SubTab sub)
        {
            _sub = _lastSub = sub;
            UGUIShip.SetButtonSelected(_btnArgs, sub == SubTab.Args, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnRound, sub == SubTab.UnityRound, SEL_COLOR);
            Refresh();
        }

        void Refresh()
        {
            bool inEditor = UnityRoundLoader.InLevelEditor;

            // out of creative: hide the subtab bar + body entirely, just show the message
            _btnArgs.gameObject.SetActive(inEditor);
            _btnRound.gameObject.SetActive(inEditor);
            _bodyRoot.SetActive(inEditor);
            _notInCreative.SetActive(!inEditor);

            if (!inEditor) return;
            _argsPanel.SetActive(_sub == SubTab.Args);
            _roundPanel.SetActive(_sub == SubTab.UnityRound);

            if (_sub == SubTab.UnityRound) RefreshWizard();
        }

        // ── Unity Round wizard nav ────────────────────────────────────────────────

        void RefreshWizard()
        {
            bool hasRound = UnityRoundLoader.HasSpawned;

            // steps past Load need a round loaded; snap back to Load if it went away under us
            if (_step != Step.Load && !hasRound) _step = Step.Load;

            _loadStep.SetActive(_step == Step.Load);
            _configStep.SetActive(_step == Step.Config);
            _publishStep.SetActive(_step == Step.Publish);

            _stepHeader.text = $"Step {(int)_step + 1} of 3  —  {StepTitles[(int)_step]}";

            _backBtn.gameObject.SetActive(_step != Step.Load);

            bool isLast = _step == Step.Publish;
            _nextBtn.gameObject.SetActive(!isLast);
            // can't leave Load until a round is actually loaded
            bool canNext = _step != Step.Load || hasRound;
            _nextBtn.interactable = canNext;
            var nlbl = _nextBtn.GetComponentInChildren<Text>();
            if (nlbl != null) nlbl.color = canNext ? WHITE : HINT;
        }

        void GoStep(int delta)
        {
            int next = Mathf.Clamp((int)_step + delta, 0, 2);
            if ((next == (int)Step.Config || next == (int)Step.Publish) && !UnityRoundLoader.HasSpawned) return; // gated
            _step = _lastStep = (Step)next;
            RefreshWizard();
        }

        void Update()
        {
            // re-refresh on entering/leaving the editor AND on a round loading/unloading — the
            // wizard gates its later steps on a round being present.
            if (UnityRoundLoader.InLevelEditor != _wasInEditor || UnityRoundLoader.HasSpawned != _wasSpawned)
            {
                _wasInEditor = UnityRoundLoader.InLevelEditor;
                _wasSpawned = UnityRoundLoader.HasSpawned;
                Refresh();
            }
        }

        // ── Unity Round wizard ────────────────────────────────────────────────────

        void BuildRoundWizard(RectTransform root, float w, float bodyH)
        {
            float rh = UIScale.LH;
            float navH = BTN_H;

            _stepHeader = UGUIShip.CreateLabel(root.transform, new Rect(PAD, SH, w, rh), "", FS_SM, new Color(1f, 1f, 1f, 0.72f));

            // step body sits between the header and the nav bar
            float bodyY = SH + rh + SH;
            float stepH = bodyH - bodyY - navH - SH;

            _loadStep = MakePanel(root, bodyY, stepH);
            BuildLoadStep(_loadStep.GetComponent<RectTransform>(), w, stepH);
            _configStep = MakePanel(root, bodyY, stepH);
            BuildConfigStep(_configStep.GetComponent<RectTransform>(), w, stepH);
            _publishStep = MakePanel(root, bodyY, stepH);
            BuildPublishStep(_publishStep.GetComponent<RectTransform>(), w, stepH);

            float navY = bodyH - navH;
            float bw = (w - PAD) / 2f;
            _backBtn = UGUIShip.CreateButton(root.transform, new Rect(PAD, navY, bw, navH), "ui.back", BTN_DARK, WHITE, FS_SM, new Action(() => GoStep(-1)));
            _nextBtn = UGUIShip.CreateButton(root.transform, new Rect(PAD + bw + PAD * 0.5f, navY, bw, navH), "ui.next", BTN_BLUE, WHITE, FS_SM, new Action(() => GoStep(1)));
        }

        void BuildLoadStep(RectTransform root, float w, float bodyH)
        {
            float cy = SH;
            float rh = UIScale.LH;

            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, rh), "ui.round_info_json_path", FS_SM, new Color(1f, 1f, 1f, 0.72f));
            cy += rh + SH;

            float browseW = 70f * UIScale.S;
            float fieldW = w - browseW - PAD;
            _pathField = UGUIShip.CreateInputField(root.transform, new Rect(PAD, cy, fieldW, BTN_H),
                "ui.c_path_to_info_json", new Color(0.12f, 0.12f, 0.12f, 1f), WHITE, FS_SM);
            UGUIShip.SetInputText(_pathField, SettingsService.Get(UnityRoundLoader.PathSettingKey, ""), false);
            _pathField.onEndEdit.AddListener(new Action<string>(v => SettingsService.Set(UnityRoundLoader.PathSettingKey, v ?? "")));

            UGUIShip.CreateButton(root.transform, new Rect(PAD + fieldW + PAD, cy, browseW, BTN_H),
                "ui.browse_2", BTN_BLUE, WHITE, FS_SM, new Action(OnBrowse));
            cy += BTN_H + SH;

            float bw = (w - PAD) / 2f;
            UGUIShip.CreateButton(root.transform, new Rect(PAD, cy, bw, BTN_H), "ui.load_2", BTN_GREEN, WHITE, FS, new Action(OnLoad));
            UGUIShip.CreateButton(root.transform, new Rect(PAD + bw + PAD * 0.5f, cy, bw, BTN_H), "ui.unload", BTN_RED, WHITE, FS, new Action(OnUnload));
            cy += BTN_H + SH;

            _statusLabel = UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, rh),
                UnityRoundLoader.HasSpawned ? $"loaded: {UnityRoundLoader.Spawned?.name}" : "",
                FS_SM, UnityRoundLoader.HasSpawned ? OK : HINT);
        }

        // step 2: things that used to need a json edit or a Unity rebuild — live now.
        void BuildConfigStep(RectTransform root, float w, float bodyH)
        {
            float cy = SH;
            float rh = UIScale.LH;
            float tglW = 70f * UIScale.S;

            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w - tglW - PAD, BTN_H), "ui.show_existing_creative_objects", FS_SM, WHITE, TextAnchor.MiddleLeft);
            bool keep = UnityRoundLoader.KeepExistingObjects;
            _keepToggle = UGUIShip.CreateButton(root.transform, new Rect(PAD + w - tglW, cy, tglW, BTN_H),
                keep ? "ui.on" : "ui.off", keep ? BTN_GREEN : BTN_DARK, WHITE, FS_SM, new Action(() =>
                {
                    bool next = !UnityRoundLoader.KeepExistingObjects;
                    UnityRoundLoader.SetKeepExistingObjects(next);
                    UGUIShip.SetButtonSelected(_keepToggle, next, BTN_GREEN);
                    var lbl = _keepToggle.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, next ? "ui.on" : "ui.off");
                }));
            cy += BTN_H + SH * 2f;

            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, rh), "ui.ambient_light", FS_SM, new Color(1f, 1f, 1f, 0.72f));
            cy += rh + SH;
            UGUIShip.CreateColorControls(root.transform, PAD, ref cy, w,
                () => RenderSettings.ambientLight.r, () => RenderSettings.ambientLight.g, () => RenderSettings.ambientLight.b,
                r => SetAmbient(0, r), g => SetAmbient(1, g), b => SetAmbient(2, b), () => { },
                out _, out _, out _, RenderSettings.ambientLight);
            cy += SH;

            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, rh), "ui.reflection_intensity", FS_SM, new Color(1f, 1f, 1f, 0.72f));
            cy += rh + SH;
            UGUIShip.CreateSlider(root.transform, PAD, cy, w, "", RenderSettings.reflectionIntensity, rh, PAD, FS_SM,
                v => RenderSettings.reflectionIntensity = v, null, null, false, RenderSettings.reflectionIntensity);
            cy += rh + SH * 2f;

            // custom textures live in their own drill-in tab; here it's a button + applied count
            UGUIShip.CreateButton(root.transform, new Rect(PAD, cy, w, BTN_H), "ui.custom_textures", BTN_DARK, WHITE, FS_SM,
                new Action(() => BetterFGUIMan.Instance?.SwitchSlotTab(this, BetterFGTabRegistry.NewTab<CustomCreativeTextureTab>())));
            cy += BTN_H + SH;
            int n = ObstacleTextureLoader.Overrides.Count;
            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, rh),
                n == 0 ? "no custom textures applied" : $"{n} custom texture{(n == 1 ? "" : "s")} applied", FS_SM, HINT);
        }

        void SetAmbient(int ch, float v)
        {
            var c = RenderSettings.ambientLight;
            if (ch == 0) c.r = v; else if (ch == 1) c.g = v; else c.b = v;
            RenderSettings.ambientLight = c;
        }

        void BuildPublishStep(RectTransform root, float w, float bodyH)
        {
            float cy = SH;
            float rh = UIScale.LH;

            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, rh), "ui.put_your_level_s_info_json_on_github_paste_the_l", FS_SM, new Color(1f, 1f, 1f, 0.72f));
            cy += rh + SH;

            _shareUrlField = UGUIShip.CreateInputField(root.transform, new Rect(PAD, cy, w, BTN_H),
                "ui.paste_the_github_link_to_your_level_s_info_json",
                new Color(0.12f, 0.12f, 0.12f, 1f), WHITE, FS_SM);
            UGUIShip.SetInputText(_shareUrlField, SettingsService.Get(UnityRoundLoader.ShareUrlSettingKey, ""), false);
            _shareUrlField.onEndEdit.AddListener(new Action<string>(v => SettingsService.Set(UnityRoundLoader.ShareUrlSettingKey, v ?? "")));
            cy += BTN_H + SH;

            UGUIShip.CreateButton(root.transform, new Rect(PAD, cy, w, BTN_H),
                "ui.set_level_description", BTN_BLUE, WHITE, FS_SM, new Action(OnSetDescription));
            cy += BTN_H + SH;

            _shareStatus = UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, rh * 2f), "", FS_SM, HINT);
        }

        void OnSetDescription()
        {
            string url = _shareUrlField != null ? _shareUrlField.text : SettingsService.Get(UnityRoundLoader.ShareUrlSettingKey, "");
            SettingsService.Set(UnityRoundLoader.ShareUrlSettingKey, url ?? "");

            string code = UnityRoundLoader.ShareCodeFromUrl(url);
            if (string.IsNullOrEmpty(code)) { SetShareStatus("ui.bad_link_need_a_github_link_to_a_rounds_round_xx", ERR); return; }

            if (UnityRoundLoader.SetLevelDescription(code, out string error))
                SetShareStatus("ui.description_set_save_your_level_and_it_ll_load_f", OK);
            else
                SetShareStatus("couldn't set description: " + error, ERR);
        }

        void SetShareStatus(string text, Color col)
        {
            if (_shareStatus == null) return;
            _shareStatus.text = text;
            _shareStatus.color = col;
        }

        void OnBrowse()
        {
            WinDialogs.PickFile("Select round info.json", path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                UGUIShip.SetInputText(_pathField, path, false);
                SettingsService.Set(UnityRoundLoader.PathSettingKey, path);
            });
        }

        void OnLoad()
        {
            string path = _pathField != null ? _pathField.text : SettingsService.Get(UnityRoundLoader.PathSettingKey, "");
            SettingsService.Set(UnityRoundLoader.PathSettingKey, path ?? "");
            if (UnityRoundLoader.LoadFromInfoJson(path, out string error))
                SetStatus($"loaded: {UnityRoundLoader.Spawned?.name}", OK);
            else SetStatus($"error: {error}", ERR);
        }

        void OnUnload()
        {
            UnityRoundLoader.UnloadAndForget();
            SetStatus("ui.unloaded", HINT);
        }

        void SetStatus(string text, Color col)
        {
            if (_statusLabel == null) return;
            _statusLabel.text = text;
            _statusLabel.color = col;
        }

        // ── Args panel ──────────────────────────────────────────────────────────

        // creative increment override. toggle + a step amount + a max. it generates 0..max in
        // that step procedurally and retargets the node's value closure so it actually applies.
        void BuildArgsPanel(RectTransform root, float w, float bodyH)
        {
            float cy = SH;
            float rh = UIScale.LH;

            float tglW = 70f * UIScale.S;

            // batch edit on/off — gates the multi-select nav prompt + Batch Edit window in the editor
            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w - tglW - PAD, BTN_H), "ui.batch_edit_multi_select", FS_SM, WHITE, TextAnchor.MiddleLeft);
            bool batchOn = Windows.Creative.BatchEditWindow.FeatureEnabled;
            _batchToggle = UGUIShip.CreateButton(root.transform, new Rect(PAD + w - tglW, cy, tglW, BTN_H),
                batchOn ? "ui.on" : "ui.off", batchOn ? BTN_GREEN : BTN_DARK, WHITE, FS_SM, new Action(() =>
                {
                    bool next = !Windows.Creative.BatchEditWindow.FeatureEnabled;
                    Windows.Creative.BatchEditWindow.FeatureEnabled = next;
                    UGUIShip.SetButtonSelected(_batchToggle, next, BTN_GREEN);
                    var lbl = _batchToggle.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, next ? "ui.on" : "ui.off");
                }));
            cy += BTN_H + SH * 2f;

            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, rh), "creative.gizmo_scale", FS_SM, new Color(1f, 1f, 1f, 0.72f));
            _gizmoValue = UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, rh),
                CreativeGizmoScale.Scale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), FS_SM, HINT, TextAnchor.MiddleRight);
            cy += rh + SH;

            UGUIShip.CreateSlider(root.transform, PAD, cy, w, "",
                Mathf.InverseLerp(CreativeGizmoScale.Min, CreativeGizmoScale.Max, CreativeGizmoScale.Scale), rh, PAD, FS_SM,
                new Action<float>(t =>
                {
                    float f = Mathf.Lerp(CreativeGizmoScale.Min, CreativeGizmoScale.Max, t);
                    CreativeGizmoScale.Scale = f;
                    if (_gizmoValue != null) _gizmoValue.text = f.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                    CreativeGizmoScale.Apply();
                }), null, null, false, Mathf.InverseLerp(CreativeGizmoScale.Min, CreativeGizmoScale.Max, 1f));
            cy += rh + SH * 2f;

            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, rh), "ui.parameter_increments", FS_SM, new Color(1f, 1f, 1f, 0.72f));
            cy += rh + SH;

            // on/off toggle
            float tbw = 70f * UIScale.S;
            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w - tbw - PAD, BTN_H), "ui.override_editor_steps", FS_SM, WHITE, TextAnchor.MiddleLeft);
            bool on = CreativeIncrements.Enabled;
            _incToggle = UGUIShip.CreateButton(root.transform, new Rect(PAD + w - tbw, cy, tbw, BTN_H),
                on ? "ui.on" : "ui.off", on ? BTN_GREEN : BTN_DARK, WHITE, FS_SM, new Action(() =>
                {
                    bool next = !CreativeIncrements.Enabled;
                    CreativeIncrements.Enabled = next;
                    UGUIShip.SetButtonSelected(_incToggle, next, BTN_GREEN);
                    var lbl = _incToggle.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, next ? "ui.on" : "ui.off");
                }));
            cy += BTN_H + SH * 2f;

            float thirdW = (w - PAD * 2f) / 3f;
            float incH = BTN_H * 0.82f;

            // min / increment / max across
            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, thirdW, rh), "ui.min", FS_SM, new Color(1f, 1f, 1f, 0.72f));
            UGUIShip.CreateLabel(root.transform, new Rect(PAD + (thirdW + PAD), cy, thirdW, rh), "ui.increment", FS_SM, new Color(1f, 1f, 1f, 0.72f));
            UGUIShip.CreateLabel(root.transform, new Rect(PAD + (thirdW + PAD) * 2f, cy, thirdW, rh), "ui.max", FS_SM, new Color(1f, 1f, 1f, 0.72f));
            cy += rh + SH;

            UGUIShip.CreateIncrement(root.transform, new Rect(PAD, cy, thirdW, incH),
                0f, 100f, () => CreativeIncrements.Min, v => CreativeIncrements.Min = v,
                0.25f, true, false, FS_SM);
            UGUIShip.CreateIncrement(root.transform, new Rect(PAD + (thirdW + PAD), cy, thirdW, incH),
                0.01f, 25f, () => CreativeIncrements.Step, v => CreativeIncrements.Step = v,
                0.05f, true, false, FS_SM);
            UGUIShip.CreateIncrement(root.transform, new Rect(PAD + (thirdW + PAD) * 2f, cy, thirdW, incH),
                1f, 1000f, () => CreativeIncrements.Max, v => CreativeIncrements.Max = v,
                5f, true, false, FS_SM);
            cy += incH + SH * 2f;

            // increment speed = nav cooldown (lower = scrolls faster when held)
            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, rh), "ui.increment_speed_lower_faster", FS_SM, new Color(1f, 1f, 1f, 0.72f));
            cy += rh + SH;

            UGUIShip.CreateIncrement(root.transform, new Rect(PAD, cy, thirdW, incH),
                0.01f, 1f, () => CreativeIncrements.Speed, v => CreativeIncrements.Speed = v,
                0.01f, true, false, FS_SM);
            cy += incH + SH;

            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, rh * 2f),
                "ui.reopen_a_parameter_menu_to_apply_generates_0_to", FS_SM, HINT);
        }

    }
}
