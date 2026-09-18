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
    [HarmonyPatch(typeof(RewiredPlayerExtensions), nameof(RewiredPlayerExtensions.GetControllerMouseOrTouchDown))]
    public class EditorUiLockRawMouseClickPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result)
        {
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
            if (!BatchEditWindow.AnyOpen) return true;
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
        public static bool Prefix() => !BatchEditWindow.AnyOpen;
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
}
