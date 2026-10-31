using System;
using BetterFG.Utilities;
using FGClient;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class NotifyRoundStartTweak : BfgTweak
    {
        public NotifyRoundStartTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "notify_round_start";
        public override string TweakLabel => "tweak.notify_round_start";
        public override bool DefaultEnabled => false;
        public override string TweakTooltip => "ui.only_works_for_windows_notification_will_appear";

        public override void OnRoundStart()
        {
            if (Application.isFocused) return;
            GlobalGameStateClient.Instance.GameStateView.GetLiveClientGameManager(out ClientGameManager cgm);
            Shell32Util.Toast("Round is starting", $"{cgm._round.DisplayNameUnindented} -- {GlobalGameStateClient.Instance.GameStateView.InitialRoundPlayerCount} players");
        }
    }
}
