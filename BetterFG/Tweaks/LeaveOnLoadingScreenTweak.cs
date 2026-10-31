using System;
using BetterFG.Core;
using BetterFG.Utilities;
using FallGuysLib.Round;
using FallGuysLib.UI;
using FG.Common;
using FG.Common.CMS;
using FGClient;
using FGClient.UI;
using FGClient.UI.Core;
using Rewired;
using UnityEngine;
using static FGClient.UI.UIModalMessage;

namespace BetterFG.Tweaks
{
    public class LeaveOnLoadingScreenTweak : BfgTweak
    {
        public LeaveOnLoadingScreenTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "leave_on_loading_screen";
        public override string TweakLabel => "Allow leaving during loading";
        public override bool DefaultEnabled => true;

        private static bool _claimed;

        private const string TitleKey = "bfg_leaveloading_title";
        private const string MessageKey = "bfg_leaveloading_msg";
        private const string LeaveLabelKey = "bfg_leaveloading_label";

        private static bool _stateLeavable;

        public override void EnableTweak()
        {
            RoundEvents.OnPlayingStarted -= HandlePlayingStarted;
            RoundEvents.OnPlayingStarted += HandlePlayingStarted;
            OnStateChanged(GlobalGameStateClient.Instance?._gameStateMachine?.CurrentState);
        }

        public override void DisableTweak()
        {
            RoundEvents.OnPlayingStarted -= HandlePlayingStarted;
            OnExternalLeaveTriggerEnd();
            DestroyPrompt();
        }

        public override void OnRoundStart() => _awaitingPlayingStart = true;

        public override void OnMainMenuEntered() => OnExternalLeaveTriggerEnd();

        private static void HandlePlayingStarted()
        {
            if (_awaitingPlayingStart) Plugin.Log.LogInfo("round's actually live now, shutting the leave window");
            _awaitingPlayingStart = false;
        }

        public override void OnStateChanged(GameStateMachine.IGameState newState)
        {
            _castedState = IntPtr.Zero;
            _stateLeavable = newState != null
                && (newState.TryCast<StateConnectToGame>() != null
                    || newState.TryCast<StateConnectionAuthentication>() != null
                    || newState.TryCast<StateGameLoading>() != null
                    || newState.TryCast<StatePrivateLobbyMinimal>() != null);
        }

        void Update()
        {
            if ((!_stateLeavable && !ExternalWindowOpen) || !IsInLeavableLoadingState())
            {
                DestroyPrompt();
                return;
            }

            if (!_claimed || !NavPromptCore.OverlayRowActive) Claim();

            if (NavPromptCore.PollActionDirect(RewiredConsts.Action.Menu_UICancel, IsBackElementName, false)
                || Input.GetKeyDown(KeyCode.Escape))
                OnLeavePressed();
        }

        public static void OnRewardScreenEntered()
        {
            ActiveEliminatedBannerClose = null;
            OnExternalLeaveTriggerEnd();
            DestroyPrompt();
        }

        private static void Claim()
        {
            NavPromptCore.ClaimOverlayRow(NavPrompt.Back, LeaveLabelKey, "Leave", OnLeavePressed);
            _claimed = true;
        }

        private static void DestroyPrompt()
        {
            if (!_claimed) return;
            _claimed = false;
            NavPromptCore.ReleaseOverlayRow();
        }

        private static void OnLeavePressed()
        {
            if (ActiveEliminatedBannerClose != null)
            {
                try { ActiveEliminatedBannerClose(); }
                catch (Exception ex) { Plugin.Log?.LogWarning("LeaveOnLoading: banner OnClosed: " + ex.Message); }
                ActiveEliminatedBannerClose = null;
                OnExternalLeaveTriggerEnd();
                DestroyPrompt();
                return;
            }
            ShowLeaveConfirm();
        }

        private static void EnsureStringsRegistered()
        {
            var strings = CMSLoader.Instance._localisedStrings;
            if (!strings._localisedStrings.ContainsKey(TitleKey))
                strings._localisedStrings.Add(TitleKey, "Leave match?");
            if (!strings._localisedStrings.ContainsKey(MessageKey))
                strings._localisedStrings.Add(MessageKey, "Bail out of the current loading screen and return to the menu?");
        }

        private static void ShowLeaveConfirm()
        {
            if (PopUp.HasActive) return;

            EnsureStringsRegistered();
            PopUp.Confirm(TitleKey, MessageKey, OnLeaveConfirmClosed, okType: OKButtonType.Disruptive);
        }

        public static Action ActiveEliminatedBannerClose;

        private static void OnLeaveConfirmClosed(bool wasOk)
        {
            if (!wasOk) return;
            if (ActiveEliminatedBannerClose != null)
            {
                try { ActiveEliminatedBannerClose(); }
                catch (Exception ex) { Plugin.Log?.LogWarning("LeaveOnLoading: banner OnClosed: " + ex.Message); }
                ActiveEliminatedBannerClose = null;
                return;
            }
            OnExternalLeaveTriggerEnd();
            DestroyPrompt();
            LeaveMatch();
        }

        public static void LeaveMatch()
        {
            StopAmbienceMuteSnapshot();
            GlobalGameStateClient.Instance.ReloadGame(true, EnumDisconnectReasonGraceful.ConnectToGameFailed);
        }

        private static void StopAmbienceMuteSnapshot() =>
            FallGuysLib.Audio.FmodUtils.StopSnapshots(path => path == "snapshot:/Mix/SNAP_Mute_Ambience");

        private static float _externalWindowUntil;
        private static bool _awaitingPlayingStart;
        private static bool ExternalWindowOpen => _awaitingPlayingStart || Time.realtimeSinceStartup < _externalWindowUntil;
        public static void OnExternalLeaveTrigger() => _externalWindowUntil = Time.realtimeSinceStartup + 8f;
        public static void OnExternalLeaveTriggerEnd() { _externalWindowUntil = 0f; _awaitingPlayingStart = false; }

        private static IntPtr _castedState;

        private static bool IsInLeavableLoadingState()
        {
            if (ExternalWindowOpen) return true;

            var state = GlobalGameStateClient.Instance?._gameStateMachine?.CurrentState;
            if (state == null) return false;

            var ptr = state.Pointer;
            if (ptr != _castedState)
            {
                _castedState = ptr;
                _stateLeavable = state.TryCast<StateConnectToGame>() != null
                    || state.TryCast<StateConnectionAuthentication>() != null
                    || state.TryCast<StateGameLoading>() != null
                    || state.TryCast<StatePrivateLobbyMinimal>() != null;
            }
            if (!_stateLeavable) return false;

            var loading = GameObject.Find("UICanvas_Client_V2(Clone)/LoadingScreen");
            if (loading == null) return false;
            var t = loading.transform;
            for (int i = 0; i < t.childCount; i++)
                if (t.GetChild(i).gameObject.activeSelf) return true;
            return false;
        }

        private static bool IsBackElementName(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            string s = n.ToLowerInvariant();
            if (s.Contains("circle")) return true;
            if (s == "b" || s.StartsWith("b ") || s.Contains("b button")) return true;
            if (s.Contains("button 1") || s.Contains("button1")) return true;
            return false;
        }
    }
}
