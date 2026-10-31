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
            var vfx = __instance._cameraDirector != null ? __instance._cameraDirector._screenVFXController : null;
            if (vfx == null) return;
            vfx.HideScreenVFX(true);
        }
    }
}
