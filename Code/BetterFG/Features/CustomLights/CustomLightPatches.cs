using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using FG.Common;
using FGClient;
using HarmonyLib;
using LevelEditor;

namespace BetterFG.Features.CustomLights
{
    [HarmonyPatch(typeof(LevelEditorStatePlay), nameof(LevelEditorStatePlay.DisableState))]
    internal static class CustomLightPlaytestExitPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            var host = BeanMonitorService.Instance;
            if (host != null) host.StartCoroutine(NextFrame().WrapToIl2Cpp());
            else CustomLights.Sync();
        }

        private static IEnumerator NextFrame()
        {
            yield return null;
            CustomLights.Sync();
            BetterFG.Features.CustomIntroCams.CustomIntroCams.ShowShotGizmos(true);
            BetterFG.Features.CustomIntroCams.CustomIntroCams.Sync();
        }
    }

    [HarmonyPatch(typeof(LevelEditorColourChangerParameter), nameof(LevelEditorColourChangerParameter.SetVisibility))]
    internal static class CustomLightColourGatePatch
    {
        [HarmonyPrefix]
        public static void Prefix(LevelEditorColourChangerParameter __instance, ref bool isVisible)
        {
            var lepo = __instance.GetComponentInParent<LevelEditorPlaceableObject>();
            if (CustomLights.IsLight(lepo)) isVisible = true;
        }
    }

    [HarmonyPatch(typeof(LevelEditorColourChangerParameter), nameof(LevelEditorColourChangerParameter.SetColour))]
    internal static class CustomLightColourLivePatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorColourChangerParameter __instance)
        {
            var lepo = __instance.GetComponentInParent<LevelEditorPlaceableObject>();
            if (CustomLights.IsLight(lepo)) CustomLights.Sync();
        }
    }

    [HarmonyPatch(typeof(FG.Common.LevelEditorManager), nameof(FG.Common.LevelEditorManager.RegisterObject))]
    internal static class CustomLightRegisterObjectPatch
    {
        [HarmonyPostfix]
        public static void Postfix([HarmonyArgument(0)] LevelEditorPlaceableObject placeableObject,
            [HarmonyArgument(1)] bool clone)
        {
            if (placeableObject == null) return;

            if (clone && BetterFG.Features.CustomIntroCams.CustomIntroCams.IsShot(placeableObject))
                BetterFG.Features.CustomIntroCams.CustomIntroCams.AssignFreeOrder(placeableObject);

            string n = placeableObject.name;
            if (!n.StartsWith(CustomLights.MarkerName, StringComparison.Ordinal)
                && !n.StartsWith(BetterFG.Features.CustomIntroCams.CustomIntroCams.MarkerName, StringComparison.Ordinal)) return;

            // loading a level registers every placeable in it, so this fires hundreds of times in a
            // row - Sync() walks the whole scene, so coalesce into the one delayed ladder instead.
            CustomLights.SyncDelayed();
        }
    }

    [HarmonyPatch(typeof(LevelEditorPlaceableObject), nameof(LevelEditorPlaceableObject.OnPreDestroy))]
    internal static class CustomLightDestroyPatch
    {
        [HarmonyPrefix]
        public static void Prefix(LevelEditorPlaceableObject __instance)
        {
            if (CustomLights.IsLight(__instance)) CustomLights.DropMarker(__instance);
            else BetterFG.Features.CustomIntroCams.CustomIntroCams.DropMarker(__instance);
        }
    }

    [HarmonyPatch(typeof(LevelEditorOutlineManager), nameof(LevelEditorOutlineManager.EnableOutline))]
    internal static class CustomLightNoOutlinePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(UnityEngine.GameObject go)
        {
            BetterFG.Utilities.BfgProbe.Hit("EnableOutline");
            return !CustomLights.IsLightGO(go) && !BetterFG.Features.CustomIntroCams.CustomIntroCams.IsRigObject(go);
        }
    }

    [HarmonyPatch(typeof(LevelEditorCollisionParameter), nameof(LevelEditorCollisionParameter.ApplyCollisionParam))]
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
