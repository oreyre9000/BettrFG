using FG.Common;
using HarmonyLib;
using UnityEngine;

namespace BetterFG.Patches
{
    [Utilities.BfgPatchGate("tweak.hide_zone_arch_effects", roundOnly: true)]
    [HarmonyPatch(typeof(PostprocessGravityVolume), nameof(PostprocessGravityVolume.OnLocalPlayerTrigger))]
    internal static class GravityZonePostprocessPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(bool entered) => !entered;
    }

    [Utilities.BfgPatchGate("tweak.hide_zone_arch_effects", roundOnly: true)]
    [HarmonyPatch(typeof(CameraScreenVFXPlayer), nameof(CameraScreenVFXPlayer.TogglePostProcessScript))]
    internal static class SpeedArchPostProcessPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(bool enabled) => !enabled;
    }

    [Utilities.BfgPatchGate("tweak.hide_zone_arch_effects")]
    [HarmonyPatch(typeof(SpeedBoostCameraScreenVFXPlayer), nameof(SpeedBoostCameraScreenVFXPlayer.ShowScreenVFX))]
    internal static class SpeedLinesShowPatch
    {
        [HarmonyPostfix]
        public static void Postfix(SpeedBoostCameraScreenVFXPlayer __instance) => Tweaks.HideZoneArchEffectsTweak.HideSpeedLines(__instance);
    }
}
