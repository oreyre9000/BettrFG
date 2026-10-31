using System;

namespace BetterFG.Tweaks
{
    public class DisablePlayerPhrasesTweak : BfgTweak
    {
        public DisablePlayerPhrasesTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "disable_player_phrases";
        public override string TweakLabel => "tweak.disable_player_phrases";
        public override bool DefaultEnabled => false;
        public override string TweakTooltip => "ui.drops_every_phrase_anyone_plays_yours_included_n";

        internal static bool Active;

        public override void EnableTweak() => Active = true;
        public override void DisableTweak() => Active = false;
    }
}
