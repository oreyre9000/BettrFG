using BetterFG.UI.Windows.Creative;
using FG.Common;
using HarmonyLib;
using LevelEditor;
using UnityEngine;

namespace BetterFG.Patches
{
    // While the Batch Edit window is open, block the editor from confirming/placing the multi-selection
    // on a left-click over the object you're looking at. Diagnostic probing (we can't read the IL2CPP
    // bodies) showed the click fires PlaceMultiSelection exactly once — that's the confirm mutation; the
    // other selection methods only tick per-frame. Prefix-skip it while the window's up. Editor-only,
    // dead the moment the window closes. BettrFG never calls PlaceMultiSelection itself.
    //
    // We also stash the live handler so the window can fire one PlaceMultiSelection a frame after it
    // closes — commits the selection cleanly instead of leaving you to work off the click backlog.
    [Utilities.BfgPatchGate(Services.FGInputLockService.EditorUiLockGateKey)]
    [HarmonyPatch(typeof(RewiredPlayerExtensions), nameof(RewiredPlayerExtensions.GetControllerMouseOrTouchDown))]
    public class EditorUiLockRawMouseClickPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result)
        {
            if (CreativeResizeBubble.ForceSelect)
            {
                __result = true;
                return false;
            }
            if (!Services.FGInputLockService.EditorUiLocked) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(LevelEditorMultiSelectionHandler), "PlaceMultiSelection")]
    public class BatchEditBlockPlacePatch
    {
        public static LevelEditorMultiSelectionHandler LiveHandler { get; private set; }
        public static bool BlockedAPlace { get; set; }

        [HarmonyPrefix]
        public static bool Prefix(LevelEditorMultiSelectionHandler __instance)
        {
            LiveHandler = __instance;
            if (__instance._isPlacingClone) return true;
            if (!BatchEditMenu.AnyOpen) return true;
            BlockedAPlace = true;
            return false;
        }
    }

    // Batch edits go through the game's own parameter API (SetColour/ApplySurface/TogglePhysics/...), so the
    // editor records each one as an EditedParameters change. That record starts AND finishes, flushing the
    // _currentChain the multiselect hold had open — the later PlaceMultiSelection then runs its FinishRecord
    // against an empty chain and CleanRepeatedChangesInCurrentSnapshot indexes past the end of _historyList.
    // It throws before the place body clears the placing state, so the selection stays stuck to the reticle.
    // Our edits carry their own undo stack (BatchEditHistory), so keep them out of the game's list entirely.
    [HarmonyPatch(typeof(LevelEditorRecordChanges), "RecordAction")]
    public class BatchEditBlockRecordPatch
    {
        [HarmonyPrefix]
        public static bool Prefix() => !BatchEditMenu.AnyOpen;
    }

    // Multiselect's rigidbody wakes the collider, so the destroy volume eats objects parked under the border
    [HarmonyPatch(typeof(LevelEditorDestroyVolume), "OnTriggerEnter")]
    public class CreativeDestroyVolumePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Collider other)
        {
            var handler = LevelEditorManager.Instance?.GetMultiselectHandler();
            if (handler == null) return true;
            if (!LevelEditorPlaceableObject.ColliderToPlaceable.TryGetValue(other, out var lepo)) return true;
            if (handler.IsPlacingOrCloning) return false;

            var sel = handler._selectedGameObjects;
            if (sel != null && sel.Contains(lepo)) return false;
            var group = handler._selectedGameObjectsGroupMembers;
            if (group != null && group.Contains(lepo)) return false;
            var cloned = handler._clonedSelection;
            return cloned == null || !cloned.Contains(lepo);
        }
    }

    // Placement floor is MapPlacementBounds.min.y (-75, not 0); below it KeepObjectInMapBounds zeroes the move
    [HarmonyPatch(typeof(LevelEditorManager), "SetupMapBoundsAndVisuals")]
    public class CreativePlacementFloorPatch
    {
        private const float FloorDrop = 10000f;

        [HarmonyPostfix]
        public static void Postfix(LevelEditorManager __instance)
        {
            var b = __instance.MapPlacementBounds;
            var min = b.min;
            var max = b.max;
            min.y -= FloorDrop;
            __instance.MapPlacementBounds = new Bounds((min + max) * 0.5f, max - min);

            BetterFG.Customization.UI.CreativeGizmoScale.Apply();
        }
    }

    // The radial menu keeps Object Snapping and Precision Movement mutually exclusive by hand:
    // LevelEditor_RadialMenuButtonDefinition.SetToggleValue's ToggleSnap case clears PrecisionMovement,
    // and its PrecisionMovement case clears ObjectSnappingOn. Nothing downstream needs that - magnetism
    // runs through LevelEditorSnappableBase.SnapToObject, the finer vertical step only swaps the divisor
    // inside SnapToGrid/HandleReticleMovement, and the two never read each other. Snapshot the pair
    // around the call and put back whichever one it clobbered. IsToggleOn reads the config fields
    // straight back, so both buttons then render as on.
    [HarmonyPatch(typeof(LevelEditor_RadialMenuButtonDefinition), nameof(LevelEditor_RadialMenuButtonDefinition.SetToggleValue))]
    public class CreativeRadialTogglesPatch
    {
        private static AdvancedOptionsMenuConfig _config;
        private static bool _snapping;
        private static bool _precision;

        [HarmonyPrefix]
        public static void Prefix(LevelEditor_RadialMenuButtonDefinition __instance)
        {
            _config = null;

            var action = __instance.RadialAction;
            if (action != RadialAction.ToggleSnap && action != RadialAction.PrecisionMovement) return;

            var manager = LevelEditorManager.Instance;
            if (manager == null) return;

            _config = manager.GetAdvancedConfig();
            if (_config == null) return;
            _snapping = _config.ObjectSnappingOn;
            _precision = _config.PrecisionMovement;
        }

        // the setters broadcast the advanced-options data the radial UI binds to, so only touch the one
        // the game actually moved - a redundant set would spam that broadcast on every click.
        [HarmonyPostfix]
        public static void Postfix(LevelEditor_RadialMenuButtonDefinition __instance)
        {
            var config = _config;
            _config = null;
            if (config == null) return;

            if (__instance.RadialAction == RadialAction.ToggleSnap)
            {
                if (config.PrecisionMovement != _precision) config.PrecisionMovement = _precision;
            }
            else if (config.ObjectSnappingOn != _snapping) config.ObjectSnappingOn = _snapping;
        }
    }

    // Rotating an object re-snaps it vertically. Traced live, one frame:
    //   BuildColliderBounds > TryToRotateObject > SnapAfterOperation > PerformSnapping
    //   > SnapToGrid(in 15.800) > SnapToGrid(out 16.000) > set_Position 15.8 -> 16.0
    // The object was on 15.8, a spot only Precision Movement reaches, and the 0.5 grid rounded it off.
    // SnapAfterRotation is never called - the rotate goes through the shared SnapAfterOperation tail -
    // and the y handed to SnapToGrid is already the right one, so the grid round is the entire bug.
    //
    // TryToRotateObject / TryToTiltObject only run when a rotation actually happens (verified: nothing
    // pushes to the trace ring on any other frame), so they make an exact gate. On those frames the
    // vertical snap hands back the y it was given and nothing else changes. Every other frame - which
    // is every frame you are moving something - never enters this code, so the movement step, the grid
    // and the returned position are all left exactly as the game computed them.
    public static class CreativeRotateKeepsPosition
    {
        private static int _frame = -1;

        internal static void Rotated() => _frame = Time.frameCount;
        internal static bool RotatingThisFrame => _frame == Time.frameCount;
    }

    [HarmonyPatch(typeof(FGClient.LevelEditorStateReticleInputHandler), "TryToRotateObject")]
    public class CreativeRotateFlagPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => CreativeRotateKeepsPosition.Rotated();
    }

    [HarmonyPatch(typeof(FGClient.LevelEditorStateReticleInputHandler), "TryToTiltObject")]
    public class CreativeTiltFlagPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => CreativeRotateKeepsPosition.Rotated();
    }

    [HarmonyPatch(typeof(FGClient.LevelEditorStateReticleBase), nameof(FGClient.LevelEditorStateReticleBase.SnapToGrid),
        new System.Type[] { typeof(Vector3), typeof(Matrix4x4), typeof(LevelEditorPlaceableObject), typeof(bool) })]
    public class CreativeRotateNoVerticalSnapPatch
    {
        private static bool _hold;
        private static float _y;

        [HarmonyPrefix]
        public static void Prefix(Vector3 __0, bool __3)
        {
            _hold = __3 && CreativeRotateKeepsPosition.RotatingThisFrame;
            if (_hold) _y = __0.y;
        }

        [HarmonyPostfix]
        public static void Postfix(ref Vector3 __result)
        {
            if (!_hold) return;
            _hold = false;
            __result.y = _y;
        }
    }
}
