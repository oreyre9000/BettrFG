using System;

namespace BetterFG.Tweaks
{
    public class DisablePlayerPhrasesTweak : BfgTweak
    {
        public DisablePlayerPhrasesTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "disable_player_phrases";
        public override string TweakLabel => "Disable Player Phrases";
        public override bool DefaultEnabled => false;
        public override string TweakTooltip => "Drops every phrase anyone plays, yours included. No bubble above the bean, no sound, no fall feed entry.";

        internal static bool Active;

        public override void EnableTweak() => Active = true;
        public override void DisableTweak() => Active = false;
    }
}
