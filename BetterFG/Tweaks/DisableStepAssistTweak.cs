using System;

namespace BetterFG.Tweaks
{
    // MovementStepAssist.CanUpdateStepAssist bails on CMSGlobalSettings.CharacterStepAssistEnabled,
    // so the flag alone kills the step-up without touching MovementStepAssistData (which the comp
    // anticheat checksums). Harmony is no use here — the game calls FixedUpdateStepAssist
    // native->native and never enters the interop wrapper.
    public class DisableStepAssistTweak : BfgTweak
    {
        public DisableStepAssistTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "disable_step_assist";
        public override string TweakLabel => "tweak.disable_step_assist";
        public override string TweakTooltip => "step_assist.tooltip";
        public override bool DefaultEnabled => false;

        public static DisableStepAssistTweak Instance { get; private set; }
        void Awake() => Instance = this;

        private static bool _original = true;
        private static bool _captured;

        public override void EnableTweak() => Apply();
        public override void DisableTweak() => Apply();

        public override void OnRoundStart() => Apply();
        public override void OnMainMenuEntered() => Apply();
        public override void OnLevelEditorPlaytest() => Apply();

        // CMS reloads stamp the flag back, hence the lifecycle re-apply
        internal void Apply()
        {
            if (!_captured) { _original = CMSGlobalSettings.CharacterStepAssistEnabled; _captured = true; }
            CMSGlobalSettings.CharacterStepAssistEnabled = IsEnabled ? false : _original;
        }
    }
}
