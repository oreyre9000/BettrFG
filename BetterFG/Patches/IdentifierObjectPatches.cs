using BetterFG.Utilities;
using HarmonyLib;
using LevelEditor;

namespace BetterFG.Patches
{
    [HarmonyPatch(typeof(LevelEditorObjectInfoViewModel), "get_ObjectName")]
    internal static class IdentifierObjectNamePatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorObjectInfoViewModel __instance, ref string __result)
        {
            BfgProbe.Hit("get_ObjectName");
            var name = IdentifierObjectRegistry.ResolveDisplayName(__instance._lepo);
            if (name != null) __result = name;
        }
    }

    [HarmonyPatch(typeof(LevelEditorObjectInfoViewModel), "get_ObjectDescription")]
    internal static class IdentifierObjectDescriptionPatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorObjectInfoViewModel __instance, ref string __result)
        {
            BfgProbe.Hit("get_ObjectDescription");
            var description = IdentifierObjectRegistry.ResolveDescription(__instance._lepo);
            if (description != null) __result = description;
        }
    }

    [HarmonyPatch(typeof(LevelEditorParameterMenuViewModel), "BuildParameterEntries")]
    internal static class IdentifierObjectParamMenuPatch
    {
        [HarmonyPrefix]
        public static void Prefix(LevelEditorParameterMenuViewModel __instance)
        {
            var target = __instance._menuData != null ? __instance._menuData.ParamTarget : null;
            BetterFG.Features.CreativeExpansion.ColourSwatches.Clear();
            IdentifierObjectRegistry.OnBuildParameterEntriesPrefix(target);
            BetterFG.Features.CreativeExpansion.SurfaceFinish.OnParamMenu(target);
            BetterFG.Features.CreativeExpansion.ObjectColours.OnParamMenu(target);
        }

        [HarmonyPostfix]
        public static void Postfix(LevelEditorParameterMenuViewModel __instance)
        {
            var rows = IdentifierObjectRegistry.OnBuildParameterEntriesPostfix(__instance);
            if (rows != null)
            {
                __instance.NodeEntries = rows;
                __instance._selectedIndex = 0;
            }
            BetterFG.Features.CreativeExpansion.ParamCategories.Sort(__instance);
        }
    }
}
