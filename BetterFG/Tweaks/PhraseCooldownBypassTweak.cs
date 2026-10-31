using System;
using FG.Common.Character;
using FGClient;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class PhraseCooldownBypassTweak : BfgTweak
    {
        public PhraseCooldownBypassTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "phrase_cooldown_bypass";
        public override string TweakLabel => "tweak.phrase_cooldown_bypass";
        public override bool DefaultEnabled => true;
        public override string TweakTooltip => "phrase_cooldown.tooltip";

        internal static bool Active;
        private static GameObject _pendingBean;

        public override void EnableTweak() => Active = true;
        public override void DisableTweak() => Active = false;

        internal static bool TryShowLocally(MotorFunctionSpeech fn, SocialOption option)
        {
            if (!Active) return false;
            var agent = fn.MotorAgent;
            if (!agent.IsLocalPlayer || option?.TryCast<TextAndImageSpeechOption>() == null) return false;
            var gameState = agent.GameState;
            if (gameState == null || fn._spammingLockTimeEnd <= gameState.SimulationTime) return false;

            var states = fn.OriginalStates;
            for (int i = 0; i < states.Length; i++)
            {
                var active = states[i]?.TryCast<MotorFunctionSpeechStateActive>();
                if (active == null) continue;
                _pendingBean = agent.gameObject;
                active.PlaySpeechOption(option.SpeechId);
                return true;
            }
            return false;
        }

        internal static void ApplyBubbleAlpha(SpeechBubbleViewModel bubble, FallGuysCharacterController fgcc)
        {
            var host = bubble._uiTweenTrigger?.gameObject;
            if (host == null) return;
            var group = host.GetComponent<CanvasGroup>();
            if (_pendingBean != null && fgcc.gameObject == _pendingBean)
            {
                _pendingBean = null;
                (group ?? host.AddComponent<CanvasGroup>()).alpha = 0.9f;
            }
            else if (group != null) group.alpha = 1f;
        }
    }
}
