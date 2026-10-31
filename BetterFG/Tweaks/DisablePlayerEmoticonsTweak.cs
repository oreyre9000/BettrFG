using System;

namespace BetterFG.Tweaks
{
    public class DisablePlayerEmoticonsTweak : BfgTweak
    {
        public DisablePlayerEmoticonsTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "disable_player_emoticons";
        public override string TweakLabel => "tweak.disable_player_emoticons";
        public override bool DefaultEnabled => false;
        public override string TweakTooltip => "ui.drops_every_emoticon_anyone_plays_yours_included";

        internal static bool Active;

        public override void EnableTweak() => Active = true;
        public override void DisableTweak() => Active = false;
    }
}
