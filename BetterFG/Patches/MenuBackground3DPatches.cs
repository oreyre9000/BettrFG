using BetterFG.Customization.Menu;
using FGClient;
using HarmonyLib;

namespace BetterFG.Patches
{
    [HarmonyPatch(typeof(MainMenuBackgroundViewModel), nameof(MainMenuBackgroundViewModel.Display3DBackground))]
    internal static class MenuBackgroundDisplay3DPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => MenuCustomizationApplication.Instance?.SetMenuBg3D(true);
    }

    [HarmonyPatch(typeof(MainMenuBackgroundViewModel), nameof(MainMenuBackgroundViewModel.Hide3dBackground))]
    internal static class MenuBackgroundHide3DPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => MenuCustomizationApplication.Instance?.SetMenuBg3D(false);
    }
}
