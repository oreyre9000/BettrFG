using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using FG.Common;
using FGClient;
using HarmonyLib;
using LevelEditor;

namespace BetterFG.Features.CreativeExpansion
{
    [HarmonyPatch(typeof(LevelEditorStatePlay), nameof(LevelEditorStatePlay.DisableState))]
    internal static class CreativePlaytestExitPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => BeanMonitorService.Instance.StartCoroutine(NextFrame().WrapToIl2Cpp());

        private static IEnumerator NextFrame()
        {
            yield return null;
            CreativeSweep.AfterPlaytest();
        }
    }

    [HarmonyPatch(typeof(LevelEditorColourChangerParameter), nameof(LevelEditorColourChangerParameter.SetVisibility))]
    internal static class CreativeColourGatePatch
    {
        [HarmonyPrefix]
        public static void Prefix(LevelEditorColourChangerParameter __instance, ref bool isVisible)
        {
            if (CreativeSweep.OwnsColour(__instance.GetComponentInParent<LevelEditorPlaceableObject>())) isVisible = true;
        }
    }

    [HarmonyPatch(typeof(LevelEditorColourChangerParameter), nameof(LevelEditorColourChangerParameter.SetColour))]
    internal static class CreativeColourLivePatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorColourChangerParameter __instance)
        {
            CreativeSweep.ColourChanged(__instance.GetComponentInParent<LevelEditorPlaceableObject>());
        }
    }

    [HarmonyPatch(typeof(FG.Common.LevelEditorManager), nameof(FG.Common.LevelEditorManager.RegisterObject))]
    internal static class CreativeRegisterObjectPatch
    {
        [HarmonyPostfix]
        public static void Postfix([HarmonyArgument(0)] LevelEditorPlaceableObject placeableObject,
            [HarmonyArgument(1)] bool clone)
        {
            if (placeableObject != null) CreativeSweep.Registered(placeableObject, clone);
        }
    }

    [HarmonyPatch(typeof(LevelEditorPlaceableObject), nameof(LevelEditorPlaceableObject.OnPreDestroy))]
    internal static class CreativeDestroyPatch
    {
        [HarmonyPrefix]
        public static void Prefix(LevelEditorPlaceableObject __instance)
        {
            CreativeSweep.Destroyed(__instance);
        }
    }

    [HarmonyPatch(typeof(CarryObject), nameof(CarryObject.OnSpawnFinished))]
    internal static class CreativeCarrySpawnPatch
    {
        [HarmonyPostfix]
        public static void Postfix(CarryObject __instance) => CreativeSweep.CarrySpawned(__instance);
    }

    [HarmonyPatch(typeof(LevelEditorCollisionParameter),nameof(LevelEditorCollisionParameter.ApplyCollisionParam))]
    internal static class CustomLightCollisionRefreshPatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorCollisionParameter __instance)
        {
            var lepo = __instance.GetComponentInParent<LevelEditorPlaceableObject>();
            if (CustomLights.IsLight(lepo)) CustomLights.DecollideLight(lepo);
        }
    }
}
