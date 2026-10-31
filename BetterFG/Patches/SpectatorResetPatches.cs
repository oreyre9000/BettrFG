using BetterFG.Tweaks;
using FG.Common;
using HarmonyLib;

namespace BetterFG.Patches
{
    [HarmonyPatch(typeof(AbstractSpectatorCameraController), "OnEnable")]
    internal static class SpectatorEnterPatch
    {
        [HarmonyPostfix]
        public static void Postfix(AbstractSpectatorCameraController __instance)
        {
            BetterFG.Nametag.CrownRankFovFix.Forget();
            BetterFG.Nametag.NametagIconApplicator.ForgetIconRows();

            var vfx = __instance._cameraDirector != null ? __instance._cameraDirector._screenVFXController : null;
            if (vfx == null) return;

            vfx.HideScreenVFX(true);
            Plugin.Log.LogInfo("spectate: cleared the leftover speed boost fx + fov override, re-baselined the crown badges");
        }
    }

    [HarmonyPatch(typeof(AbstractSpectatorCameraController), nameof(AbstractSpectatorCameraController.TryCycleCameraRight))]
    internal static class SpectatorCycleRightPatch
    {
        [HarmonyPrefix]
        public static bool Prefix() => !CinematicSpectatorTweak.IsFreeCamActive;
    }

    [HarmonyPatch(typeof(AbstractSpectatorCameraController), nameof(AbstractSpectatorCameraController.TryCycleCameraLeft))]
    internal static class SpectatorCycleLeftPatch
    {
        [HarmonyPrefix]
        public static bool Prefix() => !CinematicSpectatorTweak.IsFreeCamActive;
    }
}
