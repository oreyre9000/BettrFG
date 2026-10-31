using System;
using FGClient;

namespace BetterFG.Tweaks
{
    public class ObjectiveRoundNumberTweak : BfgTweak
    {
        public ObjectiveRoundNumberTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "objective_round_number";
        public override string TweakLabel => "Show round number in objective";
        public override bool DefaultEnabled => true;

        public static ObjectiveRoundNumberTweak Instance { get; private set; }

        void Awake() => Instance = this;

        public static string AppendRoundNumber(string text)
        {
            if (Instance == null || !Instance.IsEnabled) return text;
            if (string.IsNullOrEmpty(text)) return text;

            int round = GlobalGameStateClient.Instance != null ? GlobalGameStateClient.Instance.RoundCounterInPlaySession : 0;
            if (round <= 0) return text;

            return $"{text}\nROUND {round}";
        }
    }
}
