using System;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class HideZoneArchEffectsTweak : BfgTweak
    {
        public HideZoneArchEffectsTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "hide_zone_arch_effects";
        public override string TweakLabel => "tweak.hide_zone_speed_vfx";
        public override bool DefaultEnabled => false;

        private static FG.Common.SpeedBoostCameraScreenVFXPlayer _player;

        public override void EnableTweak() => SetSpeedLinesHidden(true);
        public override void DisableTweak() => SetSpeedLinesHidden(false);

        internal static void HideSpeedLines(FG.Common.SpeedBoostCameraScreenVFXPlayer player)
        {
            _player = player;
            SetSpeedLinesHidden(true);
        }

        private static void SetSpeedLinesHidden(bool hidden)
        {
            if (_player == null) return;
            foreach (var go in new[] { _player._speedBoostVFXGO, _player._speedBoostSecondThresholdVFXGO })
            {
                if (go == null) continue;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = !hidden;
                foreach (var c in go.GetComponentsInChildren<Canvas>(true)) c.enabled = !hidden;
            }
        }
    }
}
