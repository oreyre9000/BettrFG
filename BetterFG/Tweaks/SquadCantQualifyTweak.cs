using System;
using System.Collections.Generic;
using DG.Tweening;
using FG.Common;
using FGClient;
using FMODUnity;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Tweaks
{
    public class SquadCantQualifyTweak : BfgTweak
    {
        public SquadCantQualifyTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "squad_cant_qualify";
        public override string TweakLabel => "tweak.squad_cant_qualify";
        public override string TweakTooltip => "tweak.squad_cant_qualify.tip";
        public override bool DefaultEnabled => true;

        public static SquadCantQualifyTweak Instance { get; private set; }

        const string Sound = "UI_RewardScreen_Medal_Lost";
        const string IconName = "fallfeed-eliminate";
        const string MarkName = "BettrFG_CantQualify";
        const string GameStatesPath = "UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates";
        const string CautionPath = "/GameplayScoringViewModel/TopMiddleContainer/SquadPosition/SquadScoreContainer/SquadScoreBox/CautionIcon";

        static List<string> _banks;
        static Sprite _icon;
        static Transform _states;
        static bool _announced;

        void Awake() => Instance = this;

        public override void OnRoundStart() => _announced = false;

        public override void OnMainMenuEntered() => UnloadBanks();

        public override void DisableTweak() => UnloadBanks();

        static void UnloadBanks()
        {
            if (_banks == null) return;
            Utilities.GameSoundBanks.Unload(_banks);
            _banks = null;
        }

        public static void OnSquadScoresChanged()
        {
            if (Instance == null || !Instance.IsEnabled) return;
            var sm = ClientSquadManager.Instance;
            var rules = sm.GameRules;
            uint squadId = sm.LocalPlayerSquadId;
            if (squadId == 0 || rules == null || rules.IsScoringGame) return;
            if (_banks == null)
            {
                _banks = Utilities.GameSoundBanks.For(new[] { Sound });
                Utilities.GameSoundBanks.Load(_banks);
            }

            bool show = CantQualify(sm, rules, squadId);
            if (_states == null) _states = GameObject.Find(GameStatesPath)?.transform;
            if (_states == null) return;
            bool popped = SetMark(_states.Find("PlayingState" + CautionPath), show);
            popped |= SetMark(_states.Find("SpectatorState" + CautionPath), show);
            if (!popped || _announced) return;
            _announced = true;
            Plugin.Log.LogInfo($"squad {squadId} can't reach the qualifying spots anymore");
            try { RuntimeManager.PlayOneShot(AudioManager.GetGuidForKey(Sound), Vector3.zero); }
            catch (Exception ex) { Plugin.Log.LogWarning("no sting for the can't-qualify mark, bank not in yet? " + ex.Message); }
        }

        static bool SetMark(Transform caution, bool show)
        {
            var mark = caution.Find(MarkName);
            if (mark == null)
            {
                if (!show) return false;
                mark = MakeMark(caution);
            }
            if (mark.gameObject.activeSelf == show) return false;
            mark.gameObject.SetActive(show);
            if (!show) return false;
            mark.localScale = Vector3.zero;
            TweenSettingsExtensions.SetEase(ShortcutExtensions.DOScale(mark, 1f, 0.4f), Ease.OutBack);
            ShortcutExtensions.DOPunchRotation(mark, new Vector3(0f, 0f, 25f), 0.5f, 8, 0.6f);
            return true;
        }

        static bool CantQualify(ClientSquadManager sm, GameRules rules, uint squadId)
        {
            bool race = rules.RoundEndCondition == RoundEndCondition.SuccessQuota;
            if (!race && (rules.RoundEndCondition != RoundEndCondition.EliminatedQuota || rules.IsFinalRound)) return false;

            int started = rules.ParticipantsThatStartedTheRound;
            int next = started - rules.NumPlayersHavingSatisfiedObjective;
            int spotsLeft = rules.GetNumQualifiersNeededToCompleteGame(started, started, false, 0) - rules.NumPlayersHavingSatisfiedObjective;
            int left = Mathf.CeilToInt(GlobalGameStateClient.Instance.GameStateView.GameplayTimeRemaining);
            var squads = sm._sortedSquadScoresList;
            var rivals = new List<int>();
            int best = int.MaxValue;
            for (int i = 0; i < squads.Count; i++)
            {
                var sq = squads[i];
                int score = 0, pending = 0;
                foreach (var ps in sq.playerScores.Values)
                {
                    score += ps.Score;
                    if (!ps.IsFinal) pending++;
                }
                if (sq.squadID != squadId) { rivals.Add(score); continue; }
                best = score;
                if (race) pending = Math.Min(pending, Math.Max(0, spotsLeft));
                for (int k = 0; k < pending; k++) best += race ? Math.Max(0, next - k) : left;
            }

            int spots = sm.GetNumQualifyingSquads();
            if (best == int.MaxValue || spots <= 0 || rivals.Count < spots) return false;
            rivals.Sort();
            return best <= rivals[rivals.Count - spots];
        }

        static Transform MakeMark(Transform caution)
        {
            if (_icon == null)
                foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                    if (s.name == IconName) { _icon = s; break; }
            var img = new GameObject(MarkName).AddComponent<Image>();
            img.transform.SetParent(caution, false);
            img.sprite = _icon;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.rectTransform.sizeDelta = new Vector2(48f, 48f);
            img.gameObject.SetActive(false);
            return img.transform;
        }
    }
}
