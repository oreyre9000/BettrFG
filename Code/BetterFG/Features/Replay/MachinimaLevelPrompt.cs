#if MACHINIMA
using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Features.Assets;
using BetterFG.Features.Onboarding;
using BetterFG.Services;
using BetterFG.UI;
using BettrFG.uGUI;
using FG.Common;
using FG.Common.Fraggle;
using MPG.Utility;
using UnityEngine;
using UnityEngine.UI;
using LayoutElement = UnityEngine.UI.LayoutElement;

namespace BetterFG.Features.Replay
{
    public class MachinimaLevelPrompt : PromptHost
    {
        public MachinimaLevelPrompt(IntPtr ptr) : base(ptr) { }

        const int CanvasOrder = 1402;
        const int BlockerOrder = 1401;

        const float W = 460f;
        const float H = 380f;
        const float PAD = 14f;
        const float ROW = 22f;

        static readonly Color HINT = new Color(1f, 1f, 1f, 0.4f);
        static readonly Color WHITE = UGUIShip.WHITE;
        static readonly Color BTN_DARK = UGUIShip.BTN_DARK;
        static readonly Color BTN_GREEN = new Color(0.25f, 0.5f, 0.25f, 1f);
        static readonly Color ROW_SEL = new Color(0.25f, 0.45f, 0.25f, 1f);
        static readonly Color SCRIM = new Color(0f, 0f, 0f, 0.72f);

        static MachinimaLevelPrompt _live;

        InputField _search, _codeField;
        RectTransform _listContent;
        Text _status;
        string _scene = "";
        readonly List<string> _shown = new List<string>();
        bool _busy;

        public static void Open()
        {
            if (_live != null) return;
            var go = new GameObject("BettrFG_MachinimaLevelPrompt");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            _live = go.AddComponent<MachinimaLevelPrompt>();
            _live.Build();
        }

        void Build()
        {
            CreateSurfaces("MachinimaLevel", CanvasOrder, BlockerOrder);

            var scrimGo = new GameObject("Scrim");
            scrimGo.transform.SetParent(_canvas.transform, false);
            var scrimRt = scrimGo.AddComponent<RectTransform>();
            scrimRt.anchorMin = Vector2.zero;
            scrimRt.anchorMax = Vector2.one;
            scrimRt.offsetMin = scrimRt.offsetMax = Vector2.zero;
            scrimGo.AddComponent<Image>().color = SCRIM;

            var root = UGUIShip.CreatePanel(_canvas.transform, new Rect(0f, 0f, W, H), Color.white, "MachinimaLevelPicker");
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta = new Vector2(W, H);
            UGUIShip.ApplyDeluxPanelDark(root.GetComponent<Image>(), 2f);
            _popup = root.gameObject;

            float y = PAD;
            var title = UGUIShip.CreateLabel(root, new Rect(PAD, y, W - PAD * 2f, 26f),
                "machinima.pick_level_title", 18, WHITE, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            y += 26f + 4f;

            var body = UGUIShip.CreateLabel(root, new Rect(PAD, y, W - PAD * 2f, 16f),
                "machinima.pick_level_body", UIScale.FS_SM, HINT, TextAnchor.UpperLeft);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;
            float bodyH = UGUIShip.PreferredHeight(body, W - PAD * 2f);
            body.rectTransform.sizeDelta = new Vector2(W - PAD * 2f, bodyH);
            y += bodyH + 8f;

            float codeBtnW = 90f;
            _codeField = UGUIShip.CreateInputField(root, new Rect(PAD, y, W - PAD * 2f - codeBtnW - 6f, ROW),
                "machinima.share_code_hint", Color.black, WHITE, UIScale.FS_SM);
            UGUIShip.CreateButton(root, new Rect(W - PAD - codeBtnW, y, codeBtnW, ROW),
                "machinima.use_code", BTN_DARK, WHITE, UIScale.FS_SM, new Action(StartFromCode));
            y += ROW + 6f;

            _search = UGUIShip.CreateInputField(root, new Rect(PAD, y, W - PAD * 2f, ROW),
                "machinima.search_rounds", Color.black, WHITE, UIScale.FS_SM);
            _search.onValueChanged.AddListener(new Action<string>(v => RebuildRows()));
            y += ROW + 6f;

            float btnRowH = ROW + 4f;
            float listH = H - y - PAD - btnRowH - 24f;
            var scroll = UGUIShip.CreateScrollView(root, new Rect(PAD, y, W - PAD * 2f, listH));
            _listContent = scroll.content;
            var layout = _listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 1f;
            _listContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            y += listH + 4f;

            _status = UGUIShip.CreateLabel(root, new Rect(PAD, y, W - PAD * 2f, 18f), "", UIScale.FS_SM, HINT);
            y += 18f + 2f;

            float half = (W - PAD * 2f - 6f) / 2f;
            UGUIShip.CreateButton(root, new Rect(PAD, y, half, btnRowH),
                "ui.cancel", BTN_DARK, WHITE, UIScale.FS_SM, new Action(Cancel));
            UGUIShip.CreateButton(root, new Rect(PAD + half + 6f, y, half, btnRowH),
                "machinima.start", BTN_GREEN, WHITE, UIScale.FS_SM, new Action(StartFromRound));

            RebuildRows();
            StartCoroutine(PromptHost.OpenPopup(root.gameObject).WrapToIl2Cpp());
        }

        void RebuildRows()
        {
            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);
            _shown.Clear();

            string filter = _search.text?.Trim() ?? "";
            var scenes = SceneExport.Scenes();
            foreach (string scene in scenes)
            {
                string label = SceneExport.RoundLabel(scene) ?? scene;
                if (filter.Length > 0
                    && label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                    && scene.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                _shown.Add(scene);
                AddRow(scene, label);
                if (_shown.Count >= 200) break;
            }

            SetStatus(LocalizationService.Format("machinima.rounds_found_fmt", _shown.Count));
        }

        void AddRow(string scene, string label)
        {
            var btn = UGUIShip.CreateButton(_listContent, new Rect(0f, 0f, 0f, ROW), "",
                scene == _scene ? ROW_SEL : new Color(0.12f, 0.12f, 0.12f, 1f), WHITE, UIScale.FS_SM,
                new Action(() => { _scene = scene; RebuildRows(); }));
            btn.transition = Selectable.Transition.None;
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = ROW;
            le.flexibleWidth = 1f;

            var text = UGUIShip.CreateLabel(btn.transform, new Rect(6f, 0f, W - PAD * 2f - 30f, ROW),
                label, UIScale.FS_SM, WHITE, TextAnchor.MiddleLeft);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
        }

        void SetStatus(string msg) { if (_status != null) UGUIShip.RelabelText(_status, msg); }

        static ReplayRecording NewRecording()
        {
            var rec = new ReplayRecording
            {
                isMachinima = true,
                name = LocalizationService.Get("machinima.untitled"),
                recordedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                buildHash = BetterFGInfo.BuildHash,
                duration = 30f,
                trimEnd = 30f,
            };
            rec.postFxKeyframes.Add(new ReplayPostFxKeyframe { time = 0f });
            return rec;
        }

        void StartFromRound()
        {
            if (_busy) return;
            if (string.IsNullOrEmpty(_scene)) { SetStatus(LocalizationService.Get("machinima.pick_a_round")); return; }

            var rec = NewRecording();
            rec.sceneName = _scene;
            rec.roundName = SceneExport.RoundLabel(_scene) ?? _scene;
            rec.roundId = _scene;
            Launch(rec);
        }

        void StartFromCode()
        {
            if (_busy) return;
            string code = (_codeField.text ?? "").Trim().ToUpperInvariant();
            if (code.Length < 4) { SetStatus(LocalizationService.Get("machinima.type_a_code")); return; }

            var fcm = SingletonBehaviour<FraggleCommonManager>.Instance;
            if (fcm == null) { SetStatus(LocalizationService.Get("machinima.no_creative_service")); return; }

            _busy = true;
            SetStatus(LocalizationService.Format("machinima.fetching_level_fmt", code));
            StartCoroutine(FetchLevel(fcm, code).WrapToIl2Cpp());
        }

        IEnumerator FetchLevel(FraggleCommonManager fcm, string code)
        {
            var key = new LevelLookupKey(code, new Il2CppSystem.Nullable<int>());

            LevelInfoDto info = null;
            string failure = null;
            fcm.FraggleLevelRepository.RequestFraggleLevelData(key,
                new Action<FraggleLevelData>(data => info = data?.LevelInfoDto),
                new Action<Il2CppSystem.Exception>(ex => failure = ex != null ? ex.Message : "unknown"));

            float deadline = Time.realtimeSinceStartup + 20f;
            while (info == null && failure == null && Time.realtimeSinceStartup < deadline) yield return null;

            if (info == null)
            {
                _busy = false;
                SetStatus(LocalizationService.Format("machinima.level_lookup_failed_fmt", code, failure ?? "timed out"));
                Plugin.Log.LogWarning($"machinima couldn't look {code} up: {failure ?? "no answer in 20s"}");
                yield break;
            }

            string json = null;
            failure = null;
            fcm.FraggleLevelRepository.RequestFraggleLevelJSON(key,
                new Action<string>(text => json = text),
                new Action<Il2CppSystem.Exception>(ex => failure = ex != null ? ex.Message : "unknown"));

            deadline = Time.realtimeSinceStartup + 30f;
            while (string.IsNullOrEmpty(json) && failure == null && Time.realtimeSinceStartup < deadline) yield return null;

            if (string.IsNullOrEmpty(json))
            {
                _busy = false;
                SetStatus(LocalizationService.Format("machinima.level_lookup_failed_fmt", code, failure ?? "timed out"));
                Plugin.Log.LogWarning($"machinima got {code}'s metadata but not its level json: {failure ?? "no answer in 30s"}");
                yield break;
            }

            var rec = NewRecording();
            rec.isUgc = true;
            rec.shareCode = code;
            rec.levelJson = json;
            rec.levelVersion = info.Version;
            rec.archetypeId = info.GameModeId ?? "";
            rec.roundName = string.IsNullOrEmpty(info.Title) ? code : info.Title;
            rec.roundId = code;
            Plugin.Log.LogInfo($"machinima level {code} v{info.Version} '{rec.roundName}' pulled down, {json.Length} chars of json");
            Launch(rec);
        }

        void Launch(ReplayRecording rec)
        {
            _busy = true;
            _live = null;
            StartCoroutine(LaunchRoutine(rec).WrapToIl2Cpp());
        }

        IEnumerator LaunchRoutine(ReplayRecording rec)
        {
            yield return TearDownSurfaces();
            BetterFGUIMan.Instance?.SetVisible(false);
            ReplayViewer.Open(rec);
            Destroy(gameObject);
        }

        void Cancel()
        {
            _live = null;
            StartCoroutine(CancelRoutine().WrapToIl2Cpp());
        }

        IEnumerator CancelRoutine()
        {
            yield return TearDownSurfaces();
            Destroy(gameObject);
        }
    }
}
#endif
