using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using FG.Common.Character;
using FG.Common.Character.MotorSystem;
using FallGuysLib.Players;
using FallGuysLib.Round;
using FallGuysLib.UI;
using FG.Common;
using FGClient;
using FGClient.UI;
using FGClient.UI.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using PlayerUtils = FallGuysLib.Players.PlayerUtils;
using Levels.Progression;
using BetterFG.Services;
using BetterFG.Features.UnityRound.Editor;
using BetterFG.Features.QualificationTime;
using MPG.Utility;

namespace BetterFG.Tweaks
{
    public class ImmediateRespawnTweak : BfgTweak
    {
        public ImmediateRespawnTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "immediate_respawn";
        public override string TweakLabel => "tweak.immediate_respawn_button";
        public override bool DefaultEnabled => true;

        public static ImmediateRespawnTweak Instance { get; private set; }
        void Awake() => Instance = this;

        private static bool _claimed;
        private static bool _respawning;
        private static bool _eligible;

        private const string QuickModeKey = "tweak.immediate_respawn.quick_mode_start";
        private static bool QuickModeIsStart => SettingsService.Get(QuickModeKey, "false") == "true";
        internal static void SetQuickMode(bool start) => SettingsService.Set(QuickModeKey, start ? "true" : "false");

        public override List<TweakSetting> GetSettings() => new List<TweakSetting>
        {
            new TweakSetting
            {
                Label = "respawn.quick_mode_label",
                Options = new[] { "respawn.quick_option_checkpoint", "respawn.quick_option_start" },
                Selected = () => QuickModeIsStart ? 1 : 0,
                OnPick = i => SettingsService.Set(QuickModeKey, i == 1 ? "true" : "false"),
            },
        };

        private static Vector3? _startSpawn;

        // GameStates/PlayingState. it's a FocusableViewModel, so the game itself drops its focus
        // when the options menu (or a banner, or spectator) takes over and hands it back when that
        // closes - which is the whole reason the prompt follows the options menu correctly instead
        // of us trying to guess when to re-add it.
        private static InGamePlayingState _playingState;
        private static LevelEditorTestOverlayViewModel _testOverlay;
        private static float _nextFocusOwnerScan;

        private static ScreenViewModel _editorPopup;

        internal static void SuppressWhile(ScreenViewModel popup)
        {
            _editorPopup = popup;
            DestroyPrompt();
        }

        private static bool EditorPopupUp
        {
            get
            {
                if (_editorPopup == null) return false;
                if (_editorPopup.gameObject.activeInHierarchy && !_editorPopup.IsBeingRemoved) return true;
                _editorPopup = null;
                return false;
            }
        }

        public override void EnableTweak()
        {
            var gs = GlobalGameStateClient.Instance?._gameStateMachine?.CurrentState;
            if (gs?.TryCast<StateGameInProgress>() != null) OnRoundStart();
        }
        public override void DisableTweak()
        {
            _eligible = false;
            DestroyPrompt();
            _respawning = false;
        }

        public override void OnRoundStart()
        {
            var levelName = GlobalGameStateClient.Instance?.GameStateView?.CurrentGameLevelName;
            if (!(levelName?.StartsWith("ugc-") ?? false)) { SetEligible(false); return; }
            SetEligible(true);
        }

        public override void OnLevelEditorPlaytest() => SetEligible(true);

        public override void OnLevelEditorPlaytestEnd() => SetEligible(false);

        public override void OnStateChanged(GameStateMachine.IGameState newState)
        {
            if (newState == null || newState.TryCast<StateGameInProgress>() != null) return;
            SetEligible(false);
        }

        private void SetEligible(bool on)
        {
            _respawning = false;
            _playingState = null;
            _testOverlay = null;
            _nextFocusOwnerScan = 0f;
            _startSpawn = null;
            _eligible = on
                && IsEnabled
                && !GameRulesUtils.IsSurvivalRound()
                && PlayerUtils.GetOtherPlayerIds().Count == 0;
            if (!_eligible) DestroyPrompt();
        }

        void Update()
        {
            if (!_eligible) return;

            if (EditorPopupUp) { DestroyPrompt(); return; }

            if (_startSpawn == null) TryCaptureStartSpawn();

            if (_playingState == null && _testOverlay == null && Time.unscaledTime >= _nextFocusOwnerScan)
            {
                _nextFocusOwnerScan = Time.unscaledTime + 0.5f;
                foreach (var st in Resources.FindObjectsOfTypeAll<InGamePlayingState>())
                    if (st != null && st.gameObject.scene.IsValid()) { _playingState = st; break; }
                foreach (var ov in Resources.FindObjectsOfTypeAll<LevelEditorTestOverlayViewModel>())
                    if (ov != null && ov.gameObject.scene.IsValid()) { _testOverlay = ov; break; }
            }

            bool shouldShow = _testOverlay != null && _testOverlay.gameObject.activeInHierarchy
                ? _testOverlay._isInFocus
                : _playingState == null || _playingState._isInFocus;

            if (!shouldShow) { DestroyPrompt(); return; }

            if (!_claimed || !NavPromptCore.OverlayRowActive) Claim();

            if (_respawning) return;
            if (FGInputLockService.IsLocked) return;
            if (BfgBinds.Respawn.Down) QuickRespawn();
            else if (BfgBinds.RespawnMode.Down) ToggleQuickMode();
        }

        private static void ToggleQuickMode()
        {
            SetQuickMode(!QuickModeIsStart);
            _claimed = false;
        }

        // "where we first spawned" = the local bean's own position on the first frame it exists this
        // round. same source live and in editor playtest, no per-mode divergence, and it captures the
        // real spawn even when the level authored something other than checkpoint zone 0 for it.
        private static void TryCaptureStartSpawn()
        {
            var fgcc = UnityRoundLoader.InLevelEditor
                ? BeanMonitorService.LocalPlayerBean?.GetComponent<FallGuysCharacterController>()
                : PlayerUtils.PlayerController;
            if (fgcc == null) return;
            _startSpawn = fgcc.transform.position;
        }

        private static void Claim()
        {
            bool start = QuickModeIsStart;
            NavPromptCore.ClaimOverlayRow(
                new NavPromptCore.OverlayClaim
                {
                    Key = NavPrompt.Report,
                    LabelKey = "bfg_respawn_label",
                    LabelText = LocalizationService.Get("respawn.prompt_respawn"),
                    OnPressed = QuickRespawn,
                    IconAction = BfgBinds.Respawn.ActionId,
                    IconCategory = RewiredConsts.Category.Default,
                },
                new NavPromptCore.OverlayClaim
                {
                    Key = NavPromptCore.Favourite,
                    LabelKey = start ? "bfg_respawn_mode_start" : "bfg_respawn_mode_checkpoint",
                    LabelText = LocalizationService.Get(start ? "respawn.prompt_mode_start" : "respawn.prompt_mode_checkpoint"),
                    OnPressed = ToggleQuickMode,
                    IconAction = BfgBinds.RespawnMode.ActionId,
                    IconCategory = RewiredConsts.Category.Default,
                });
            _claimed = true;
        }

        private static void QuickRespawn()
        {
            if (_respawning) return;
            if (QuickModeIsStart) DoStartRespawn();
            else DoCheckpointRespawn();
        }

        private static void DestroyPrompt()
        {
            if (!_claimed) return;
            _claimed = false;
            NavPromptCore.ReleaseOverlayRow();
        }

        internal static void DoCheckpointRespawn()
        {
            if (UnityRoundLoader.InLevelEditor) { DoEditorRespawn(); return; }

            var localFgcc = PlayerUtils.PlayerController;
            if (localFgcc == null) return;

            uint localNetId = PlayerUtils.GetLocalNetObjectId();
            if (localNetId == 0) return;

            var checkpointMgr = UnityEngine.Object.FindObjectOfType<CheckpointManager>();
            if (checkpointMgr == null) return;

            var cpMap = checkpointMgr.NetIDToCheckpointMap;
            if (cpMap == null || !cpMap.TryGetValue((MPGNetID)localNetId, out uint cpId)) return;

            var zones = checkpointMgr._checkpointZones;
            CheckpointZonePositions targetZone = null;
            for (int i = 0; i < zones.Count; i++)
            {
                var czp = zones[i]?.TryCast<CheckpointZonePositions>();
                if (czp != null && czp.uniqueId == cpId) { targetZone = czp; break; }
            }
            if (targetZone == null) return;

            var spawnTransform = targetZone.GetRandomTransform();
            if (spawnTransform == null) return;

            TeleportTo(localFgcc, spawnTransform.position, resetTime: false);
        }

        // editor playtest: LevelEditorManager knows the current respawn point (current checkpoint, or the level
        // start if none reached yet) — same source the kill-zones use. local bean is LevelEditorManager's player.
        private static void DoEditorRespawn()
        {
            var mgr = LevelEditorManager.Instance;
            if (mgr == null) return;

            var bean = BeanMonitorService.LocalPlayerBean;
            if (bean == null) return;

            var localFgcc = bean.GetComponent<FallGuysCharacterController>();
            if (localFgcc == null) return;

            if (!mgr.TryGetRespawnTransform(out Vector3 position, out _)) return;

            TeleportTo(localFgcc, position, resetTime: false);
        }

        internal static void DoStartRespawn()
        {
            if (_startSpawn == null) { Plugin.Log.LogWarning("respawn-at-start with no cached spawn yet — did the bean not tick?"); return; }

            var localFgcc = UnityRoundLoader.InLevelEditor
                ? BeanMonitorService.LocalPlayerBean?.GetComponent<FallGuysCharacterController>()
                : PlayerUtils.PlayerController;
            if (localFgcc == null) return;

            TeleportTo(localFgcc, _startSpawn.Value, resetTime: true);
        }

        private static void TeleportTo(FallGuysCharacterController localFgcc, Vector3 position, bool resetTime)
        {
            var teleport = localFgcc.TeleportMotorFunction;
            if (teleport == null) return;

            teleport.TeleportPosition = position;

            var states = teleport.OriginalStates;
            if (states == null || states.Count < 2) return;

            var activeState = states[1].TryCast<MotorFunctionTeleportStateActive>();
            if (activeState == null) return;

            _respawning = true;
            activeState.Begin(-1);
            Instance.StartCoroutine(FinishTeleport(activeState, resetTime).WrapToIl2Cpp());
        }

        private static IEnumerator FinishTeleport(MotorFunctionTeleportStateActive activeState, bool resetTime)
        {
            yield return new WaitForSeconds(0.5f);
            activeState.End(-1);
            if (resetTime) FeatureQualificationTime.ResetElapsedBaseline();
            _respawning = false;
        }
    }
}
