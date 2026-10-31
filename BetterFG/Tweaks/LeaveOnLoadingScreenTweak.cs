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
    // press back during a loading-ish game state and we open a confirm popup; OK calls ReloadGame
    // to drop the player back to the menu. the action id comes straight from RewiredConsts (the
    // generated property returns the live Rewired id for Menu_UICancel — Circle/B/A-cancel across
    // every controller layout) so we don't need to scrape NavigationOverlayManager and we don't
    // have a startup race where the id isn't cached yet.
    //
    // the button goes in the game's real NavigationOverlay row, but the press is read by our own
    // Rewired poll rather than the button's UIButtonSolo: the Menu input category is disabled
    // during loading, so anything routed through the game's own button callback never fires there.
    public class LeaveOnLoadingScreenTweak : BfgTweak
    {
        public LeaveOnLoadingScreenTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "leave_on_loading_screen";
        public override string TweakLabel => "tweak.allow_leaving_during_loading";
        public override bool DefaultEnabled => true;

        private static bool _claimed;

        private const string TitleKey = "bfg_leaveloading_title";
        private const string MessageKey = "bfg_leaveloading_msg";
        private const string LeaveLabelKey = "bfg_leaveloading_label";

        // the four states this tweak lives in are entered a handful of times a match, and the state
        // machine already announces every one of them — so the frame loop no longer asks the game
        // what state it is in, it just reads the answer the last transition left behind.
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

            // eliminated banner is a one-tap exit — OnLeavePressed closes it instead of confirming.
            if (NavPromptCore.PollActionDirect(RewiredConsts.Action.Menu_UICancel, IsBackElementName, false)
                || Input.GetKeyDown(KeyCode.Escape))
                OnLeavePressed();
        }

        // called when StateRewardScreen takes over — kill any active prompt + state.
        public static void OnRewardScreenEntered()
        {
            ActiveEliminatedBannerClose = null;
            OnExternalLeaveTriggerEnd();
            DestroyPrompt();
        }

        // FG's NavigationPromptButtonController handles all glyph switching itself (controller
        // change -> right glyph), so the core just Instantiates + Init's once and the visual stays
        // correct for whatever input the user picks up. element-name filter rejects X-on-PS since
        // across controller layouts the Menu_UICancel binding's elementIdentifierId collides with
        // Cross on DualShock.
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

        // popup title/message are looked up as CMS localisation keys, so register ours before show.
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

        // set by the eliminated-banner OnOpened patch; cleared on OnClosed. when set, the leave
        // confirm closes the banner instead of ReloadGame'ing (banner has its own continue-out path).
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

        // bails out of the current match/loading screen the same way the back-button confirm does.
        // other tweaks (e.g. PlayerNameWarningTweak) that need to boot the player out reuse this
        // instead of re-deriving the ReloadGame call.
        public static void LeaveMatch()
        {
            // every load screen spawns a SNAP_Mute_Ambience instance that's supposed to stop on
            // load complete. bailing mid-load orphans it — BUS_AMBIENCES stays at f=0 forever and
            // we never hear ambience until restart. kill all instances of that snapshot before
            // we reload so the orphan can't survive the transition.
            StopAmbienceMuteSnapshot();
            GlobalGameStateClient.Instance.ReloadGame(true, EnumDisconnectReasonGraceful.ConnectToGameFailed);
        }

        private static void StopAmbienceMuteSnapshot() =>
            FallGuysLib.Audio.FmodUtils.StopSnapshots(path => path == "snapshot:/Mix/SNAP_Mute_Ambience");

        // external triggers (e.g. StateQualificationScreen.Teardown) flip this on so the leave
        // window also covers the post-round transition where we'd otherwise reject the back press.
        // window lasts up to 8s but ends early when the round-reveal carousel fades in — that's
        // the visual signal the next round is about to start and the bail moment is gone.
        private static float _externalWindowUntil;
        private static bool _awaitingPlayingStart;
        private static bool ExternalWindowOpen => _awaitingPlayingStart || Time.realtimeSinceStartup < _externalWindowUntil;
        public static void OnExternalLeaveTrigger() => _externalWindowUntil = Time.realtimeSinceStartup + 8f;
        public static void OnExternalLeaveTriggerEnd() { _externalWindowUntil = 0f; _awaitingPlayingStart = false; }

        // OnStateChanged fires before the tweak is added to the live list on a mid-session toggle, so
        // the first Update after enabling still has to resolve the state once for itself.
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

            // also require the actual LoadingScreen container to have any active child — otherwise
            // we'd offer "Leave" during connect/auth states where no loading UI is on screen.
            var loading = GameObject.Find("UICanvas_Client_V2(Clone)/LoadingScreen");
            if (loading == null) return false;
            var t = loading.transform;
            for (int i = 0; i < t.childCount; i++)
                if (t.GetChild(i).gameObject.activeSelf) return true;
            return false;
        }

        // names Rewired's templates use for the cancel/back face button across the controller
        // layouts the game ships with. Xbox = B, PS = Circle, generic DInput = Button 1 / 2.
        // anything else (Cross/A/X, shoulders, sticks, dpad) is rejected so X on PS doesn't fire.
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
