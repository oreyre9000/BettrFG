using FGClient.CatapultServices;
using FGClient.ShowSelector;
using HarmonyLib;

namespace BetterFG.Patches
{
    [Utilities.BfgPatchGate("tweak.multi_show_select")]
    [HarmonyPatch(typeof(MatchmakingService), nameof(MatchmakingService.GetPlayerMatchmakingAuthToken))]
    internal static class MultiShowTokenPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Il2CppSystem.Collections.Generic.List<string> showIds)
            => BetterFG.Tweaks.MultiShowSelectTweak.AugmentShowIds(showIds);
    }

    [Utilities.BfgPatchGate("tweak.multi_show_select")]
    [HarmonyPatch(typeof(MainMenuShowSelectorPlayButtonViewModel), "OnShowSelected", typeof(Il2CppSystem.Collections.Generic.Dictionary<FGClient.ShowData, bool>))]
    internal static class MultiShowPlayButtonPatch
    {
        [HarmonyPostfix]
        public static void Postfix(MainMenuShowSelectorPlayButtonViewModel __instance, Il2CppSystem.Collections.Generic.Dictionary<FGClient.ShowData, bool> showDefs)
            => BetterFG.Tweaks.MultiShowSelectTweak.OnPlayButtonShows(__instance, showDefs);
    }

    [Utilities.BfgPatchGate("tweak.multi_show_select")]
    [HarmonyPatch(typeof(FGClient.UI.CurrentSelectedShowViewModel), "DisplayShowNameFromDict")]
    internal static class MultiShowLobbyTitlePatch
    {
        [HarmonyPostfix]
        public static void Postfix(FGClient.UI.CurrentSelectedShowViewModel __instance)
            => BetterFG.Tweaks.MultiShowSelectTweak.OnLobbyTitle(__instance);
    }

    [Utilities.BfgPatchGate("tweak.multi_show_select")]
    [HarmonyPatch(typeof(ShowSelectorShowTileViewModel), nameof(ShowSelectorShowTileViewModel.SetNestedShowsEntryPointData))]
    internal static class MultiShowNestedTilePatch
    {
        [HarmonyPostfix]
        public static void Postfix(ShowSelectorShowTileViewModel __instance)
            => BetterFG.Tweaks.MultiShowSelectTweak.OnNestedData(__instance);
    }
}
