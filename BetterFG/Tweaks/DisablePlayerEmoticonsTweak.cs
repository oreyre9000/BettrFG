using System;

namespace BetterFG.Tweaks
{
    public class DisablePlayerEmoticonsTweak : BfgTweak
    {
        public DisablePlayerEmoticonsTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "disable_player_emoticons";
        public override string TweakLabel => "Disable Player Emoticons";
        public override bool DefaultEnabled => false;
        public override string TweakTooltip => "Drops every emoticon anyone plays, yours included. No bubble above the bean, no sound, no fall feed entry.";

        internal static bool Active;

        public override void EnableTweak() => Active = true;
        public override void DisableTweak() => Active = false;
    }
}
