using UnityEngine;
using LevelEditor;

namespace BetterFG.UI.Windows.Creative
{
    // Shared lookup + snapshot/restore over a placeable's parameter components. All three batch ops
    // (recolour/scale/material) and the undo stack go through here so the il2cpp component lookup (which
    // is finicky — the typed GetComponentInChildren<T> returns null on these, so we enumerate the base
    // handler and TryCast) lives in one place.
    public static class BatchTargets
    {
        public static LevelEditorColourChangerParameter GetColourParam(LevelEditorPlaceableObject obj)
        {
            if (obj == null) return null;
            var handlers = obj.GetComponentsInChildren<LevelEditorPlaceableParameterHandler>(true);
            if (handlers == null) return null;
            for (int i = 0; i < handlers.Length; i++)
            {
                var c = handlers[i]?.TryCast<LevelEditorColourChangerParameter>();
                if (c != null) return c;
            }
            return null;
        }

        public static LevelEditorSurfaceDefinitionParameter GetSurfaceParam(LevelEditorPlaceableObject obj)
        {
            if (obj == null) return null;
            var handlers = obj.GetComponentsInChildren<LevelEditorPlaceableParameterHandler>(true);
            if (handlers == null) return null;
            for (int i = 0; i < handlers.Length; i++)
            {
                var s = handlers[i]?.TryCast<LevelEditorSurfaceDefinitionParameter>();
                if (s != null) return s;
            }
            return null;
        }

        // LevelEditorVisibilityAndCollisionCombinedParameter : LevelEditorVisibilityParameter, so
        // this cast picks up combined objects too.
        public static LevelEditorVisibilityParameter GetVisibilityParam(LevelEditorPlaceableObject obj)
        {
            if (obj == null) return null;
            var handlers = obj.GetComponentsInChildren<LevelEditorPlaceableParameterHandler>(true);
            if (handlers == null) return null;
            for (int i = 0; i < handlers.Length; i++)
            {
                var v = handlers[i]?.TryCast<LevelEditorVisibilityParameter>();
                if (v != null) return v;
            }
            return null;
        }

        public static LevelEditorCollisionParameter GetCollisionParam(LevelEditorPlaceableObject obj)
        {
            if (obj == null) return null;
            var handlers = obj.GetComponentsInChildren<LevelEditorPlaceableParameterHandler>(true);
            if (handlers == null) return null;
            for (int i = 0; i < handlers.Length; i++)
            {
                var c = handlers[i]?.TryCast<LevelEditorCollisionParameter>();
                if (c != null) return c;
            }
            return null;
        }

        public static LevelEditorPhysicsObjectParameter GetPhysicsParam(LevelEditorPlaceableObject obj)
        {
            if (obj == null) return null;
            var handlers = obj.GetComponentsInChildren<LevelEditorPlaceableParameterHandler>(true);
            if (handlers == null) return null;
            for (int i = 0; i < handlers.Length; i++)
            {
                var p = handlers[i]?.TryCast<LevelEditorPhysicsObjectParameter>();
                if (p != null) return p;
            }
            return null;
        }

        // only decals/stickers carry this one, so its presence is also how the window decides whether
        // to offer the lighting row at all.
        public static LevelEditorUnlitModeParameter GetUnlitParam(LevelEditorPlaceableObject obj)
        {
            if (obj == null) return null;
            var handlers = obj.GetComponentsInChildren<LevelEditorPlaceableParameterHandler>(true);
            if (handlers == null) return null;
            for (int i = 0; i < handlers.Length; i++)
            {
                var u = handlers[i]?.TryCast<LevelEditorUnlitModeParameter>();
                if (u != null) return u;
            }
            return null;
        }

        public static LevelEditorRangeParameter GetRangeParam(LevelEditorPlaceableObject obj)
        {
            if (obj == null) return null;
            var handlers = obj.GetComponentsInChildren<LevelEditorPlaceableParameterHandler>(true);
            if (handlers == null) return null;
            for (int i = 0; i < handlers.Length; i++)
            {
                var r = handlers[i]?.TryCast<LevelEditorRangeParameter>();
                if (r != null) return r;
            }
            return null;
        }

        public static LevelEditorDecalScaleParameter GetDecalScaleParam(LevelEditorPlaceableObject obj)
        {
            if (obj == null) return null;
            var handlers = obj.GetComponentsInChildren<LevelEditorPlaceableParameterHandler>(true);
            if (handlers == null) return null;
            for (int i = 0; i < handlers.Length; i++)
            {
                var d = handlers[i]?.TryCast<LevelEditorDecalScaleParameter>();
                if (d != null) return d;
            }
            return null;
        }

        // read the object's CURRENT colour/surface/visibility/collision into `into`, but only for the
        // fields `template` carries — used to build an inverse snapshot for redo (see BatchEditHistory).
        public static void SnapCurrent(LevelEditorPlaceableObject obj,
            BatchEditHistory.ObjectSnap template, BatchEditHistory.ObjectSnap into)
        {
            if (template.Colour.HasValue)
            {
                var rgb = GetColourParam(obj);
                if (rgb != null) { into.Colour = rgb.CurrentColour; into.Palette = rgb.CurrentPalette; }
            }
            if (template.SurfaceIndex.HasValue)
            {
                var surf = GetSurfaceParam(obj);
                if (surf != null) into.SurfaceIndex = surf.SelectedParameterIndex;
            }
            if (template.VisibilityParam.HasValue)
            {
                var vis = GetVisibilityParam(obj);
                if (vis != null) into.VisibilityParam = vis.VisibilityParam;
            }
            if (template.CollisionEnabled.HasValue)
            {
                var col = GetCollisionParam(obj);
                if (col != null) into.CollisionEnabled = col.GetCollisionParam();
            }
            if (template.Unlit.HasValue)
            {
                var unlit = GetUnlitParam(obj);
                if (unlit != null) into.Unlit = unlit._unlitMode;
            }
            if (template.PhysicsEnabled.HasValue || template.WeightIndex.HasValue || template.Draggable.HasValue)
            {
                var phys = GetPhysicsParam(obj);
                if (phys != null)
                {
                    if (template.PhysicsEnabled.HasValue) into.PhysicsEnabled = phys._isPhysicsEnabled;
                    if (template.WeightIndex.HasValue) into.WeightIndex = phys._selectedWeightIndex;
                    if (template.Draggable.HasValue) into.Draggable = phys._isDraggable;
                }
            }
        }

        // put back whatever colour/surface fields the snap captured.
        public static void Restore(LevelEditorPlaceableObject obj, BatchEditHistory.ObjectSnap snap)
        {
            if (snap.Colour.HasValue)
            {
                var rgb = GetColourParam(obj);
                if (rgb != null) rgb.SetPaletteAndColour(snap.Palette, snap.Colour.Value);
            }
            if (snap.SurfaceIndex.HasValue)
            {
                var surf = GetSurfaceParam(obj);
                if (surf != null) ApplySurfaceIndex(surf, snap.SurfaceIndex.Value);
            }
            if (snap.VisibilityParam.HasValue)
            {
                var vis = GetVisibilityParam(obj);
                if (vis != null) { vis.VisibilityParam = snap.VisibilityParam.Value; vis.ApplyVisibilityParam(true); }
            }
            if (snap.CollisionEnabled.HasValue)
            {
                var col = GetCollisionParam(obj);
                if (col != null) { col._collisionEnabled = snap.CollisionEnabled.Value; col.ApplyCollisionParam(true); }
            }
            if (snap.Unlit.HasValue)
            {
                var unlit = GetUnlitParam(obj);
                if (unlit != null) ApplyUnlit(unlit, snap.Unlit.Value);
            }
            if (snap.PhysicsEnabled.HasValue || snap.WeightIndex.HasValue || snap.Draggable.HasValue)
            {
                var phys = GetPhysicsParam(obj);
                if (phys != null)
                {
                    if (snap.PhysicsEnabled.HasValue) phys.TogglePhysics(snap.PhysicsEnabled.Value, true);
                    if (snap.WeightIndex.HasValue) ApplyWeight(phys, snap.WeightIndex.Value);
                    if (snap.Draggable.HasValue) phys.ToggleDraggable(snap.Draggable.Value);
                }
            }
        }

        public static void HideAndDecollide(LevelEditorPlaceableObject lepo)
        {
            var vis = GetVisibilityParam(lepo);
            if (vis != null) { vis.VisibilityParam = 0; vis.ApplyVisibilityParam(true); }

            var col = GetCollisionParam(lepo);
            if (col != null) { col._collisionEnabled = false; col.ApplyCollisionParam(false); }
        }

        public static void ApplyWeight(LevelEditorPhysicsObjectParameter phys, int index)
        {
            phys._selectedWeightIndex = index;
            phys.UpdateSelectedWeight(index);
            phys.UpdatePhysicsParameters();
        }

        // same trick as the surface param: write the state fields ourselves and push straight to the
        // responders (the LevelEditorDecal), instead of going through SetValue and its UI refresh.
        // _currentVal is what Write() persists into the UGC schema, _unlitMode is what the param reads back.
        public static void ApplyUnlit(LevelEditorUnlitModeParameter unlit, bool enabled)
        {
            unlit._currentVal = enabled;
            unlit._unlitMode = enabled;
            unlit.SetUnlitMode(enabled);
        }

        public static void ApplySurfaceIndex(LevelEditorSurfaceDefinitionParameter surf, int index)
        {
            try { surf.SetParameterIndex(index, true); }
            catch (System.Exception ex) { Plugin.Log.LogWarning($"surface index {index} via SetParameterIndex threw: {ex.Message}"); }
        }
    }
}
