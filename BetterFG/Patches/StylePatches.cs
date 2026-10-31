using FGClient.UI;
using FGClient.UI.Core;
using HarmonyLib;
using BetterFG.Customization.UI.Styles;

namespace BetterFG.Patches
{
    [HarmonyPatch(typeof(ScreenViewModel), nameof(ScreenViewModel.Init))]
    internal static class StyleScreenInitPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ScreenViewModel __instance) => UIStyles.OnScreen(__instance);
    }

    [HarmonyPatch(typeof(FGClient.LoadingGameScreenViewModel), nameof(FGClient.LoadingGameScreenViewModel.CurrentScreen), MethodType.Setter)]
    internal static class StyleLoadingImagePatch
    {
        [HarmonyPostfix]
        public static void Postfix(FGClient.LoadingGameScreenViewModel __instance) => UIStyles.OnLoading(__instance.transform);
    }

    [HarmonyPatch(typeof(FGClient.UI.UIButtonMenuTab), nameof(FGClient.UI.UIButtonMenuTab.Select))]
    internal static class StyleTabSelectPatch
    {
        [HarmonyPostfix]
        public static void Postfix(FGClient.UI.UIButtonMenuTab __instance) => StyledUI.OnHighlight(__instance, __instance.IsHighlightedTab);
    }

    [HarmonyPatch(typeof(FGClient.UI.UIButtonMenuTab), nameof(FGClient.UI.UIButtonMenuTab.Deselect))]
    internal static class StyleTabDeselectPatch
    {
        [HarmonyPostfix]
        public static void Postfix(FGClient.UI.UIButtonMenuTab __instance) => StyledUI.OnHighlight(__instance, __instance.IsHighlightedTab);
    }

    [HarmonyPatch(typeof(FGClient.Store.SymphonyStoreInputHandler))]
    internal static class StyleShopMovePatch
    {
        [HarmonyPrefix, HarmonyPatch("OnMoveLeft")]
        public static bool Left(FGClient.Store.SymphonyStoreInputHandler __instance) => !UIStyles.OnShopMove(__instance, 0);

        [HarmonyPrefix, HarmonyPatch("OnMoveRight")]
        public static bool Right(FGClient.Store.SymphonyStoreInputHandler __instance) => !UIStyles.OnShopMove(__instance, 1);

        [HarmonyPrefix, HarmonyPatch("OnMoveUp")]
        public static bool Up(FGClient.Store.SymphonyStoreInputHandler __instance) => !UIStyles.OnShopMove(__instance, 2);

        [HarmonyPrefix, HarmonyPatch("OnMoveDown")]
        public static bool Down(FGClient.Store.SymphonyStoreInputHandler __instance) => !UIStyles.OnShopMove(__instance, 3);
    }

    [HarmonyPatch(typeof(ScreenViewModel), nameof(ScreenViewModel.OnGainFocus))]
    internal static class StyleScreenFocusPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ScreenViewModel __instance)
        {
            if (__instance.TryCast<UIModalMessage>() == null) UIStyles.OnFocus(__instance);
        }
    }

    [HarmonyPatch(typeof(UIModalMessage), nameof(UIModalMessage.OnGainFocus))]
    internal static class StyleModalPatch
    {
        [HarmonyPostfix]
        public static void Postfix(UIModalMessage __instance) => UIStyles.OnModal(__instance);
    }
}
