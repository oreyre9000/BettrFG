using System;
using BetterFG.Services;
using FallGuysLib.UI;
using FG.Common;
using FGClient;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Tweaks
{
    public class LeaveToLobbyTweak : BfgTweak
    {
        public LeaveToLobbyTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "leave_to_lobby";
        public override string TweakLabel => "tweak.leave_to_lobby";
        public override bool DefaultEnabled => true;

        public override void OnMainMenuEntered() => LeaveToLobby.ReturnToLobby();
    }

    internal static class LeaveToLobby
    {
        private const int ReasonLeaveMatch = 4;
        private const int ReasonEpisodeOver = 1;
        private static readonly Color Orange = new Color(1f, 0.69f, 0.05f, 1f);

        internal static bool ForceLobby;

        internal static bool InRealLobby()
        {
            if (!PrivateLobbyUtils.IsInPrivateLobby()) return false;
            var cfg = PrivateLobbyUtils.GetCurrentPrivateLobby().PrivateLobbyConfig;
            return cfg == null || cfg.DiscoveryShowConfig == null;
        }
        private static bool _pending;
        private static bool _wasHost;
        private static string _code;

        internal static void ReturnToLobby()
        {
            if (!_pending) return;
            _pending = false;
            if (!PrivateLobbyUtils.IsInPrivateLobby())
            {
                Plugin.Log.LogWarning($"lobby {_code} didn't survive the leave, staying on the menu");
                return;
            }
            var gsm = GlobalGameStateClient.Instance._gameStateMachine;
            var menu = gsm.CurrentState.TryCast<StateMainMenu>();
            if (menu == null) return;
            var lobby = new StatePrivateLobby(gsm, menu._gameStateData, _wasHost, _wasHost ? null : _code);
            GlobalGameStateClient.Instance.PrivateLobbyOpened = true;
            gsm.ReplaceCurrentState(new GameStateMachine.IGameState(lobby.Pointer));
            Plugin.Log.LogInfo($"back in {_code}, host {_wasHost}");
        }

        internal static bool TakeOverLeave(LeaveMatchPopupManager popup)
        {
            _wasHost = PrivateLobbyUtils.GetPrivateLobbyIsHost();
            _code = PrivateLobbyUtils.GetPrivateLobbyId();
            _pending = true;
            Plugin.Log.LogInfo($"leaving the match but keeping lobby {_code}");
            popup.LeaveMatch();
            PartyStateManager.Instance.HidePartyMenu();
            popup.MatchLeftFromLeaveMatchPopup = true;
            return false;
        }

        internal static void BuildButtons()
        {
            PopUp.SetButton(false, LocalizationService.Get("leave_to_lobby.cancel"));
            PopUp.SetButton(true, LocalizationService.Get("leave_to_lobby.exit_menu"), PopUp.ActionExtra);
            PopUp.BindKey(PopUp.ActionExtra, KeyCode.F, "Square");
            PopUp.AddButton(LocalizationService.Get("leave_to_lobby.exit_lobby"), Orange, PopUp.ActionConfirm, OnLobbyClicked);
        }

        private static void OnLobbyClicked()
        {
            ForceLobby = true;
            PopUp.ActiveButton(true).GetComponent<Button>().onClick.Invoke();
        }

        internal static bool ShouldSpareLobby(StateDisconnectingFromServer s) => _pending && (int)s._reason == ReasonLeaveMatch;

        internal static void SetReason(StateDisconnectingFromServer s, bool episodeOver) =>
            s._reason = (EnumDisconnectReasonGraceful)(episodeOver ? ReasonEpisodeOver : ReasonLeaveMatch);
    }

    [Utilities.BfgPatchGate("tweak.leave_to_lobby", defaultOn: true)]
    [HarmonyPatch(typeof(LeaveMatchPopupManager), "ShowScreen")]
    internal static class LeaveToLobbyShowPatch
    {
        [HarmonyPostfix]
        public static void Postfix(LeaveMatchPopupManager __instance)
        {
            if (!LeaveToLobby.InRealLobby() || __instance._cachedPopupContext != LeaveMatchPopupManager.PopupContext.None) return;
            LeaveToLobby.BuildButtons();
        }
    }

    [Utilities.BfgPatchGate("tweak.leave_to_lobby", defaultOn: true)]
    [HarmonyPatch(typeof(LeaveMatchPopupManager), "OnClose")]
    internal static class LeaveToLobbyClosePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(LeaveMatchPopupManager __instance, bool wasOk)
        {
            var force = LeaveToLobby.ForceLobby;
            LeaveToLobby.ForceLobby = false;
            if (!force || !wasOk || !LeaveToLobby.InRealLobby()) return true;
            return LeaveToLobby.TakeOverLeave(__instance);
        }
    }

    [Utilities.BfgPatchGate("tweak.leave_to_lobby", defaultOn: true)]
    [HarmonyPatch(typeof(StateDisconnectingFromServer), "Initialise")]
    internal static class LeaveToLobbyDisconnectPatch
    {
        [HarmonyPrefix]
        public static void Prefix(StateDisconnectingFromServer __instance, out bool __state)
        {
            __state = LeaveToLobby.ShouldSpareLobby(__instance);
            if (__state) LeaveToLobby.SetReason(__instance, true);
        }

        [HarmonyPostfix]
        public static void Postfix(StateDisconnectingFromServer __instance, bool __state)
        {
            if (__state) LeaveToLobby.SetReason(__instance, false);
        }
    }
}
