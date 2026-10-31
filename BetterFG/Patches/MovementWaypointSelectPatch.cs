using FG.Common;
using HarmonyLib;
using LevelEditor;

namespace BetterFG.Patches
{
    internal static class MovementWaypoint
    {
        private static LevelEditorPlaceableObject _lastIndependent;
        private static LevelEditorPlaceableObject _lastOwner;

        internal static LevelEditorPlaceableObject Hovered()
        {
            var hovered = LevelEditorManager.Instance?.GetReticleBase()?.ObjectCurrentlyInReticle;
            if (hovered == null || hovered.ParentObject == null) return null;
            if (hovered.GetComponentInChildren<LevelEditorMovementWaypointActiveBase>() == null) return null;
            if (hovered.GetComponentInChildren<LevelEditorMovementActiveBase>() != null) return null;
            return hovered;
        }

        internal static void Remember(LevelEditorPlaceableObject waypoint, LevelEditorPlaceableObject owner)
        {
            _lastIndependent = waypoint;
            _lastOwner = owner;
        }

        internal static void FixIfDisowned()
        {
            var w = _lastIndependent;
            var owner = _lastOwner;
            if (w == null || owner == null) return;

            var mover = owner.GetComponentInChildren<LevelEditorMovementActiveBase>();
            var target = mover != null ? mover._waypointsParent : null;
            if (target == null) return;

            bool needsFix = w.ParentObject == null || w.transform.parent != target;
            if (!needsFix)
            {
                _lastIndependent = null;
                _lastOwner = null;
                return;
            }

            if (w.ParentObject == null) w.ParentObject = owner;
            w.transform.SetParent(target, true);
            Plugin.Log.LogInfo($"waypoint {w.name} back under {owner.name}");
            _lastIndependent = null;
            _lastOwner = null;
        }
    }

    [HarmonyPatch(typeof(LevelEditorMultiSelectionHandler), nameof(LevelEditorMultiSelectionHandler.HandleInput))]
    public static class MovementWaypointSelectPatch
    {
        private static LevelEditorPlaceableObject _detached;
        private static LevelEditorPlaceableObject _parent;

        [HarmonyPrefix]
        public static void Prefix()
        {
            MovementWaypoint.FixIfDisowned();

            var hovered = MovementWaypoint.Hovered();
            if (hovered == null) return;

            _detached = hovered;
            _parent = hovered.ParentObject;
            MovementWaypoint.Remember(hovered, _parent);
            hovered.ParentObject = null;
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            if (_detached == null) return;
            if (_detached.ParentObject == null) _detached.ParentObject = _parent;
            _detached = null;
            _parent = null;
        }
    }

    [HarmonyPatch(typeof(LevelEditorMultiSelectionHandler), nameof(LevelEditorMultiSelectionHandler.AddToSelection))]
    public static class MovementWaypointAddPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(LevelEditorPlaceableObject obj)
        {
            if (obj == null) return true;
            var hovered = MovementWaypoint.Hovered();
            if (hovered == null) return true;
            return hovered.ParentObject.Pointer != obj.Pointer;
        }
    }

    [HarmonyPatch(typeof(LevelEditorMultiSelectionHandler), nameof(LevelEditorMultiSelectionHandler.DisownAllMultiSelectRigidBodyTransforms))]
    internal static class MovementWaypointDisownAllPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => MovementWaypoint.FixIfDisowned();
    }

    [HarmonyPatch(typeof(LevelEditorMultiSelectionHandler), nameof(LevelEditorMultiSelectionHandler.DisownMultiSelectRigidBodyTransforms))]
    internal static class MovementWaypointDisownSomePatch
    {
        [HarmonyPostfix]
        public static void Postfix() => MovementWaypoint.FixIfDisowned();
    }

    [HarmonyPatch(typeof(LevelEditorMultiSelectionHandler), nameof(LevelEditorMultiSelectionHandler.OnLeaveMultiselect))]
    internal static class MovementWaypointLeavePatch
    {
        [HarmonyPostfix]
        public static void Postfix() => MovementWaypoint.FixIfDisowned();
    }
}
