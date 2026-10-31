using System;
using FG.Common;
using FG.Common.CMS;
using FGClient;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class ShowMedalOnQualifyTweak : BfgTweak
    {
        public ShowMedalOnQualifyTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "show_medal_on_qualify";
        public override string TweakLabel => "Show medal on qualify";
        public override string TweakTooltip => "Shows your medal on the Qualified banner as soon as you qualify, picked from the round's own medal tiers by your finish position.";
        public override bool DefaultEnabled => false;

        public static ShowMedalOnQualifyTweak Instance { get; private set; }

        private int _finished;
        private string _badge;
        private string _levelId;

        void Awake() => Instance = this;

        public override void OnRoundStart()
        {
            _finished = 0;
            _badge = null;
        }

        public static void OnPlayerProgress(GameMessageServerPlayerProgress msg)
        {
            if (Instance == null || !Instance.IsEnabled || !msg.succeeded || msg.isSkipping) return;
            Instance._finished++;
            if (!GlobalGameStateClient.Instance.GameStateView.GetLiveClientGameManager(out ClientGameManager cgm) || !cgm.IsMyLocalPlayer(msg.playerId)) return;

            var bonus = cgm._round.Bonus;
            uint players = GlobalGameStateClient.Instance.GameStateView.InitialRoundPlayerCount;
            Instance._levelId = cgm._round.Id;
            Instance._badge = null;
            if (bonus == null) return;
            if (bonus.Type == RoundBonus.BonusType.qualification)
                Instance._badge = bonus.QualificationTier?.Badge?.Id;
            else if (bonus.Type == RoundBonus.BonusType.position)
            {
                float pct = (Instance._finished - 1) * 100f / players;
                var tiers = bonus.PositionTiers;
                for (int i = 0; i < tiers.Length && Instance._badge == null; i++)
                    if (pct < tiers[i].PositionPercentage) Instance._badge = tiers[i].Badge.Id;
                Instance._badge ??= "pink";
            }
            Plugin.Log.LogInfo($"qualified {Instance._finished}/{players} on {Instance._levelId}, medal {Instance._badge ?? "none (team round?)"}");
        }

        public static void OnBannerOpened(Component banner)
        {
            if (Instance == null || !Instance.IsEnabled || Instance._badge == null) return;
            if (banner.TryCast<FGClient.UI.QualifiedScreenViewModel>() == null) return;
            var vm = banner.GetComponent<EndRoundBadgeProcessor>()._badgeViewModel;
            vm.gameObject.SetActive(true);
            vm.SetBadgeData(new BadgeData(Instance._badge, Instance._levelId, 0));
        }
    }
}
