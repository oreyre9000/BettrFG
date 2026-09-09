using FG.Common;
using FG.Common.Fraggle;
using HarmonyLib;
using LevelEditor;

namespace BetterFG.Features.CreativeGroups
{
    [HarmonyPatch(typeof(LevelEditorPlaceableObject), nameof(LevelEditorPlaceableObject.OnHoverInGameEditor))]
    internal static class CreativeGroupHoverPatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorPlaceableObject __instance) => CreativeGroups.OnHover(__instance);
    }

    [HarmonyPatch(typeof(LevelEditorPlaceableObject), nameof(LevelEditorPlaceableObject.OnHoverOffInGameEditor))]
    internal static class CreativeGroupHoverOffPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => CreativeGroups.ClearOutline();
    }

    [HarmonyPatch(typeof(LevelEditorMultiSelectionHandler), nameof(LevelEditorMultiSelectionHandler.AddToSelection))]
    internal static class CreativeGroupSelectPatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorMultiSelectionHandler __instance,
            [HarmonyArgument(0)] LevelEditorPlaceableObject obj,
            [HarmonyArgument(1)] int options,
            [HarmonyArgument(2)] bool record,
            [HarmonyArgument(3)] bool unselect)
            => CreativeGroups.ExpandAdd(__instance, obj, options, record, unselect);
    }

    [HarmonyPatch(typeof(LevelEditorMultiSelectionHandler), nameof(LevelEditorMultiSelectionHandler.RemoveFromSelection))]
    internal static class CreativeGroupDeselectPatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorMultiSelectionHandler __instance,
            [HarmonyArgument(0)] LevelEditorPlaceableObject obj,
            [HarmonyArgument(1)] int options,
            [HarmonyArgument(2)] bool record,
            [HarmonyArgument(3)] bool unselect)
            => CreativeGroups.ExpandRemove(__instance, obj, options, record, unselect);
    }

    [HarmonyPatch(typeof(LevelEditorObjectInfoViewModel), "get_ObjectName")]
    internal static class CreativeGroupObjectNamePatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorObjectInfoViewModel __instance, ref string __result)
            => CreativeGroups.DecorateName(__instance, ref __result);
    }

    [HarmonyPatch(typeof(LevelEditorObjectInfoViewModel), "get_ObjectsSelectedText")]
    internal static class CreativeGroupObjectCountPatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorObjectInfoViewModel __instance, ref string __result)
            => CreativeGroups.DecorateCount(__instance, ref __result);
    }

    [HarmonyPatch(typeof(LevelEditorRecordChanges), nameof(LevelEditorRecordChanges.UndoRedoEpilogue))]
    internal static class CreativeGroupUndoPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            CreativeGroups.OnUndoRedoSettled();
            BetterFG.Features.CustomBackgrounds.DisableBackgroundRulebook.Invalidate();
        }
    }

    [HarmonyPatch(typeof(LevelEditorRecordChanges), nameof(LevelEditorRecordChanges.GetNameForDisplay))]
    internal static class CreativeGroupHistoryNamePatch
    {
        [HarmonyPostfix]
        public static void Postfix([HarmonyArgument(0)] LevelEditorPlaceableObject lepo, ref string __result)
            => CreativeGroups.DecorateHistoryName(lepo, ref __result);
    }

    [HarmonyPatch(typeof(LevelEditorManagerUI), nameof(LevelEditorManagerUI.OnSavedLevelEvent))]
    internal static class CreativeGroupLevelSavedPatch
    {
        [HarmonyPostfix]
        public static void Postfix([HarmonyArgument(0)] LevelEditorLevelSavedEvent saveLevelEvent)
            => CreativeGroups.OnLevelSaved(saveLevelEvent);
    }

    [HarmonyPatch(typeof(LevelEditorParameterMenuViewModel), "BuildParameterEntries")]
    internal static class CreativeGroupScaleParamPatch
    {
        [HarmonyPrefix]
        public static void Prefix(LevelEditorParameterMenuViewModel __instance)
            => CreativeGroups.AddGroupRows(__instance._menuData.ParamTarget);
    }
}
