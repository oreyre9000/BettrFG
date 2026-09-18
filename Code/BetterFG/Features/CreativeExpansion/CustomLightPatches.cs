using System;
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
            BetterFG.Features.CreativeExpansion.CustomIntroCams.ShowShotGizmos(true);
            BetterFG.Features.CreativeExpansion.CustomIntroCams.Sync();
            BetterFG.Features.CreativeExpansion.RuleColours.Sync();
            BetterFG.Features.CreativeExpansion.SlimeColours.Sync();
            BetterFG.Features.CreativeExpansion.ObjectThemes.Sync();
            BetterFG.Features.CreativeExpansion.GravityZoneArt.Refresh();
            BetterFG.Features.CreativeExpansion.Thrusters.Sync();
            BetterFG.Features.UnityRound.UnityRoundAnchors.Sync();
            BetterFG.Features.UnityRound.UnityRoundAnchors.Reap();
        }
    }

    [HarmonyPatch(typeof(LevelEditorColourChangerParameter), nameof(LevelEditorColourChangerParameter.SetVisibility))]
    internal static class CustomLightColourGatePatch
    {
        [HarmonyPrefix]
        public static void Prefix(LevelEditorColourChangerParameter __instance, ref bool isVisible)
        {
            var lepo = __instance.GetComponentInParent<LevelEditorPlaceableObject>();
            if (CustomLights.IsLight(lepo) || BetterFG.Features.UnityRound.UnityRoundCoreObject.IsCore(lepo)) isVisible = true;
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
            else if (BetterFG.Features.CreativeExpansion.RuleColours.IsMarker(lepo))
                BetterFG.Features.CreativeExpansion.RuleColours.OnMarkerColourChanged(lepo);
            else if (BetterFG.Features.CreativeExpansion.SlimeColours.IsMarker(lepo))
                BetterFG.Features.CreativeExpansion.SlimeColours.OnMarkerColourChanged(lepo);
            else if (BetterFG.Features.CreativeExpansion.Thrusters.IsThruster(lepo)) BetterFG.Features.CreativeExpansion.Thrusters.Sync();
            else if (BetterFG.Features.UnityRound.UnityRoundCoreObject.IsCore(lepo))
                BetterFG.Features.UnityRound.UnityRoundCoreObject.OnColourChanged(lepo);
        }
    }

    [HarmonyPatch(typeof(FG.Common.LevelEditorManager), nameof(FG.Common.LevelEditorManager.OnLevelLoad))]
    internal static class CreativeLevelLoadPatch
    {
        [HarmonyPostfix]
        public static void Postfix(bool isStart)
        {
            CustomLights.OnEditorLevelLoad(isStart);
            if (!isStart) Plugin.Log.LogInfo("editor says the level is in, running the marker sweep");
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

            BetterFG.Features.UnityRound.BetterFGRoundPostmodifier.ApplyToNewObject(placeableObject.gameObject);

            if (clone && BetterFG.Features.CreativeExpansion.CustomIntroCams.IsShot(placeableObject))
                BetterFG.Features.CreativeExpansion.CustomIntroCams.AssignFreeOrder(placeableObject);

            string n = placeableObject.name;
            if (!n.StartsWith(CustomLights.MarkerName, StringComparison.Ordinal)
                && !n.StartsWith(BetterFG.Features.CreativeExpansion.CustomIntroCams.MarkerName, StringComparison.Ordinal)
                && !n.StartsWith(BetterFG.Features.CreativeExpansion.Thrusters.MarkerName, StringComparison.Ordinal)) return;

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
            else BetterFG.Features.CreativeExpansion.CustomIntroCams.DropMarker(__instance);
            BetterFG.Features.CreativeExpansion.RuleColours.OnDestroyed(__instance);
            BetterFG.Features.CreativeExpansion.SlimeColours.OnDestroyed(__instance);
            BetterFG.Features.CreativeExpansion.Thrusters.DropVisual(__instance);
            BetterFG.Features.UnityRound.UnityRoundAnchors.OnAnchorDestroying(__instance);
        }
    }

    [HarmonyPatch(typeof(LevelEditorOutlineManager), nameof(LevelEditorOutlineManager.EnableOutline))]
    internal static class CustomLightNoOutlinePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(UnityEngine.GameObject go)
        {
            BetterFG.Utilities.BfgProbe.Hit("EnableOutline");
            return !CustomLights.IsLightGO(go) && !BetterFG.Features.CreativeExpansion.CustomIntroCams.IsRigObject(go)
                && !BetterFG.Features.CreativeExpansion.Thrusters.IsThrusterGO(go);
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
