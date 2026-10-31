using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Features.UnityRound;
using BetterFG.Services;
using BetterFG.Utilities;
using LevelEditor;
using UnityEngine;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class CreativeSweep
    {
        private const int QuietFrames = 20;
        private const int SettleFrames = 30;
        private static int _lastQueued;
        private static bool _waiting;

        private static readonly (string Name, Action Sync)[] Features =
        {
            ("lights", CustomLights.Sync),
            ("intro cams", CustomIntroCams.Sync),
            ("neon", NeonMaterial.Sync),
            ("rule colours", RuleColours.Sync),
            ("object themes", ObjectThemes.Sync),
            ("slime colour", SlimeColours.Sync),
            ("thrusters", Thrusters.Sync),
            ("foliage", Foliage.Sync),
            ("trigger volumes", TriggerVolumes.Sync),
            ("surface finish", SurfaceFinish.Sync),
            ("surface materials", SurfaceMaterials.Sync),
            ("object colours", ObjectColours.Sync),
            ("unity round core", UnityRoundCoreObject.Sync),
            ("unity round anchors", UnityRoundAnchors.Sync),
            ("custom game modes", BetterFG.Features.CustomGamemodes.CustomGamemodeHost.Sync),
        };

        internal static void Registered(LevelEditorPlaceableObject lepo, bool clone)
        {
            BetterFGRoundPostmodifier.ApplyToNewObject(lepo.gameObject);
            if (clone && CustomIntroCams.IsShot(lepo)) CustomIntroCams.AssignFreeOrder(lepo);
            if (!NeonMaterial.IsMarkerObject(lepo)) NeonMaterial.OnPlaced(lepo);

            string n = lepo.name;
            if (n.StartsWith(CustomLights.MarkerName, StringComparison.Ordinal)
                || n.StartsWith(CustomIntroCams.MarkerName, StringComparison.Ordinal)
                || NeonMaterial.IsMarkerObject(lepo) || ObjectThemes.IsThemable(lepo) || TriggerVolumes.IsVolume(lepo)) Queue();
        }

        internal static bool OwnsColour(LevelEditorPlaceableObject lepo)
            => CustomLights.IsLight(lepo) || Foliage.IsFoliage(lepo) || UnityRoundCoreObject.IsCore(lepo);

        internal static void ColourChanged(LevelEditorPlaceableObject lepo)
        {
            ColourSwatches.Refresh();
            SurfaceFinish.OnColourChanged(lepo);
            SurfaceMaterials.OnColourChanged(lepo);
            if (CustomLights.IsLight(lepo)) CustomLights.Sync();
            else if (RuleColours.IsMarker(lepo)) RuleColours.OnMarkerColourChanged(lepo);
            else if (SlimeColours.IsMarker(lepo)) SlimeColours.OnMarkerColourChanged(lepo);
            else if (ObjectColours.IsMarker(lepo)) ObjectColours.OnMarkerColourChanged(lepo);
            else if (Thrusters.IsThruster(lepo) || Thrusters.IsCore(lepo)) Thrusters.Sync();
            else if (Foliage.IsFoliage(lepo)) Foliage.Sync();
            else if (UnityRoundCoreObject.IsCore(lepo)) UnityRoundCoreObject.OnColourChanged(lepo);
        }

        internal static void Placed(LevelEditorPlaceableObject lepo)
        {
            NeonMaterial.OnPlaced(lepo);
            RuleColours.OnPlaced(lepo);
            SlimeColours.OnPlaced(lepo);
            ObjectThemes.OnPlaced(lepo);
            Thrusters.OnPlaced(lepo);
            SurfaceFinish.OnPlaced(lepo);
            SurfaceMaterials.OnPlaced(lepo);
            ObjectColours.OnPlaced(lepo);
        }

        internal static void Cloned(LevelEditorPlaceableObject clone, LevelEditorPlaceableObject source)
        {
            NeonMaterial.OnCloned(clone, source);
            SurfaceFinish.OnCloned(clone, source);
            SurfaceMaterials.OnCloned(clone, source);
            RuleColours.OnCloned(clone, source);
            SlimeColours.OnCloned(clone, source);
            ObjectThemes.OnCloned(clone, source);
            ObjectColours.OnCloned(clone, source);
            Thrusters.OnCloned(clone, source);
        }

        internal static void CarrySpawned(FG.Common.CarryObject co) => CarryLooks.OnSpawned(co);

        internal static void Destroyed(LevelEditorPlaceableObject lepo)
        {
            if (CustomLights.IsLight(lepo)) CustomLights.DropMarker(lepo);
            else CustomIntroCams.DropMarker(lepo);
            RuleColours.OnDestroyed(lepo);
            SlimeColours.OnDestroyed(lepo);
            Thrusters.DropVisual(lepo);
            SurfaceFinish.OnDestroyed(lepo);
            SurfaceMaterials.OnDestroyed(lepo);
            ObjectColours.OnDestroyed(lepo);
            UnityRoundAnchors.OnAnchorDestroying(lepo);
        }

        internal static void NewLevel()
        {
            Definers.Teardown();
            CustomLights.InvalidateRoundLights();
            NeonMaterial.ResetEditorState();
            SurfaceMaterials.ResetEditorState();
            RuleColours.ResetEditorState();
            SlimeColours.ResetEditorState();
            ObjectThemes.ResetEditorState();
            ObjectColours.ResetEditorState();
            IdentifierObjects.InvalidateLevelJson();
            DisableBackgroundRulebook.Invalidate();
            _waiting = false;
        }

        internal static void All()
        {
            foreach (var (name, sync) in Features)
                try { sync(); }
                catch (Exception ex) { Plugin.Log.LogWarning($"{name} sweep fell over: {ex.Message}"); }
        }

        internal static void Queue()
        {
            _lastQueued = Time.frameCount;
            if (_waiting) return;
            _waiting = true;
            BeanMonitorService.Instance.StartCoroutine(AfterQuiet().WrapToIl2Cpp());
        }

        private static IEnumerator AfterQuiet()
        {
            while (Time.frameCount - _lastQueued < QuietFrames) yield return null;
            _waiting = false;
            All();
        }

        internal static void OnEditorLevelLoad(bool isStart)
        {
            if (!isStart) BeanMonitorService.Instance.StartCoroutine(Settle().WrapToIl2Cpp());
        }

        private static IEnumerator Settle()
        {
            yield return null;
            All();
            for (int i = 0; i < SettleFrames; i++) yield return null;
            All();
        }

        internal static void AfterPlaytest()
        {
            CustomIntroCams.ShowShotGizmos(true);
            All();
            GravityZoneArt.Refresh();
            UnityRoundAnchors.Reap();
        }

        internal static void LevelBuilt()
        {
            NewLevel();
            Definers.Recheck();
            All();
        }
    }
}
