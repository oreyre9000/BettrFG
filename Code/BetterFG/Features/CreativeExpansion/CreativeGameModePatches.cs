using BetterFG.Features.UnityRound.Editor;
using FG.Common;
using HarmonyLib;

namespace BetterFG.Features.CreativeExpansion
{
    // A level's game mode lives in its JSON. When the game builds a loader from that JSON, swap the
    // mode id if this level has a queued change (set from the rulebook row, kept per share code).
    [HarmonyPatch(typeof(LevelLoader), nameof(LevelLoader.CreateLevelLoaderFromDownloadedJSON))]
    internal static class CreativeGameModeJsonLoadPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref string jsonString, LevelAggregateDto dto)
        {
            if (!UnityRoundLoader.InLevelEditor) return;
            CreativeGameModeRulebook.RewriteJsonForLoad(ref jsonString, dto?.ShareCode);
            BetterFG.Features.CreativeExpansion.LevelPortImport.RewriteJsonForLoad(ref jsonString, dto?.ShareCode);
        }
    }

    // The Rulebook (Settings) screen rebuilds its row list through HandleChanged. Every rebuild we
    // slot our "Game Mode" row in just after the row it was cloned from.
    [HarmonyPatch(typeof(RulebookMenuCollectionBinding), nameof(RulebookMenuCollectionBinding.HandleChanged))]
    internal static class RulebookGameModeRowPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RulebookMenuCollectionBinding __instance)
        {
            CreativeGameModeRulebook.InjectRow(__instance);
            DisableBackgroundRulebook.InjectRow(__instance);
            Definers.TrackNativeRow(__instance);
        }
    }

    // Left/right on a horizontal-list row (mouse arrows or carousel nav) both land here. On our
    // cloned rows we handle the change ourselves and skip the native handler.
    [HarmonyPatch(typeof(LevelEditorRulebookEntryHorizontalListViewModel), nameof(LevelEditorRulebookEntryHorizontalListViewModel.OnIncrement))]
    internal static class RulebookGameModeIncrementPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(LevelEditorRulebookEntryHorizontalListViewModel __instance)
        {
            int id = __instance.GetInstanceID();
            if (CreativeGameModeRulebook.IsOurVm(id)) { CreativeGameModeRulebook.Cycle(1); return false; }
            if (DisableBackgroundRulebook.IsOurVm(id)) { DisableBackgroundRulebook.Toggle(); return false; }
            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(LevelEditorRulebookEntryHorizontalListViewModel __instance)
        {
            if (Definers.IsNativeRowTarget(__instance.GetInstanceID()))
            {
                Definers.SyncDefiner();
                DisableBackgroundRulebook.Sync(ThemeManager._sceneBackgroundAndLighting);
            }
        }
    }

    [HarmonyPatch(typeof(LevelEditorRulebookEntryHorizontalListViewModel), nameof(LevelEditorRulebookEntryHorizontalListViewModel.OnDecrement))]
    internal static class RulebookGameModeDecrementPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(LevelEditorRulebookEntryHorizontalListViewModel __instance)
        {
            int id = __instance.GetInstanceID();
            if (CreativeGameModeRulebook.IsOurVm(id)) { CreativeGameModeRulebook.Cycle(-1); return false; }
            if (DisableBackgroundRulebook.IsOurVm(id)) { DisableBackgroundRulebook.Toggle(); return false; }
            return true;
        }

        [HarmonyPostfix]
        public static void Postfix(LevelEditorRulebookEntryHorizontalListViewModel __instance)
        {
            if (Definers.IsNativeRowTarget(__instance.GetInstanceID()))
            {
                Definers.SyncDefiner();
                DisableBackgroundRulebook.Sync(ThemeManager._sceneBackgroundAndLighting);
            }
        }
    }

    // Rulebook closed - if the game mode row was moved off the level's real mode, ask whether to
    // apply it (queues the swap + leaves the editor). Both the explicit CloseScreen() and the
    // OnClosed() lifecycle hook route here; OnRulebookClosed de-dupes per frame.
    [HarmonyPatch(typeof(LevelEditorRulebookViewModel), nameof(LevelEditorRulebookViewModel.CloseScreen))]
    internal static class RulebookGameModeCloseScreenPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => CreativeGameModeRulebook.OnRulebookClosed("CloseScreen");
    }

    [HarmonyPatch(typeof(LevelEditorRulebookViewModel), nameof(LevelEditorRulebookViewModel.OnClosed))]
    internal static class RulebookGameModeOnClosedPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => CreativeGameModeRulebook.OnRulebookClosed("OnClosed");
    }
}
