using System;
using FG.Common;
using FG.Common.LevelEditor.Serialization;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LevelEditor;

namespace BetterFG.UI.Windows.Creative
{
    public static class BatchLink
    {
        public static bool IsController(LevelEditorPlaceableObject obj)
        {
            var ctrl = obj?.LinkController;
            if (ctrl == null) return false;
            return LinkingSystemExtensions.IsMoverType(ctrl.GetTransmitterLinkType());
        }

        public static LevelEditorPlaceableObject Controller() => Controller(out _);

        public static LevelEditorPlaceableObject Controller(out int found)
        {
            found = 0;
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (sel == null) return null;

            var pivot = LevelEditorMultiSelectionHandler.SelectionPivot();
            LevelEditorPlaceableObject first = null, onPivot = null;
            foreach (var obj in sel)
            {
                if (obj == null || !IsController(obj)) continue;
                found++;
                if (first == null) first = obj;
                if (pivot != null && obj.Pointer == pivot.Pointer) onPivot = obj;
            }
            return onPivot ?? first;
        }

        public static string TypeName(LevelEditorPlaceableObject controller)
        {
            var ctrl = controller?.LinkController;
            if (ctrl == null) return "?";
            return ctrl.GetTransmitterLinkType() == ObjectLinkController.TransmitterLinkType.Rotation
                ? "rotation" : "movement";
        }

        public static void Survey(LevelEditorPlaceableObject controller,
            out int receivers, out int linked, out int slotsFree)
        {
            receivers = 0; linked = 0; slotsFree = -1;

            var ctrl = controller?.LinkController;
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (ctrl == null || sel == null) return;

            foreach (var obj in sel)
            {
                if (obj == null || obj.Pointer == controller.Pointer) continue;
                if (obj.LinkController == null) continue;
                receivers++;
                if (ctrl.IsReceiverLinkedTo(obj)) linked++;
            }

            int max = ctrl.GetMaxConnections();
            if (max > 0) slotsFree = max - ctrl.GetConnectedReceiverCount();
        }

        public static int LinkAll(BatchEditHistory.BatchEntry entry,
            LevelEditorPlaceableObject controller, out string note)
        {
            note = null;
            var ctrl = controller?.LinkController;
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (ctrl == null || sel == null)
            {
                Plugin.Log.LogWarning("batch link asked for, but there's no controller in the selection");
                return 0;
            }

            int done = 0, already = 0, budget = 0, incompatible = 0, inert = 0, copies = 0;
            var activeCtrl = ctrl;
            foreach (var obj in sel)
            {
                if (obj == null || obj.Pointer == controller.Pointer) continue;

                var rc = obj.LinkController;
                if (rc == null) { inert++; continue; }
                if (ctrl.IsReceiverLinkedTo(obj)) { already++; continue; }

                bool ok = activeCtrl.CanTransmitterLinkToReceiver(rc, out var why);
                if (why == ObjectLinkController.LinkConnectionCheckResult.NoAvailableSlots)
                {
                    var copy = CloneController(controller);
                    if (copy?.LinkController != null)
                    {
                        copies++;
                        activeCtrl = copy.LinkController;
                        ok = activeCtrl.CanTransmitterLinkToReceiver(rc, out why);
                    }
                }

                if (!ok || why != ObjectLinkController.LinkConnectionCheckResult.ValidConnection)
                {
                    switch (why)
                    {
                        case ObjectLinkController.LinkConnectionCheckResult.AlreadyConnected: already++; break;
                        case ObjectLinkController.LinkConnectionCheckResult.LinkBudgetExceeded: budget++; break;
                        default: incompatible++; break;
                    }
                    continue;
                }

                entry?.Snaps.Add(new BatchEditHistory.ObjectSnap { Obj = obj, LinkTo = activeCtrl.PlaceableObject, Linked = false });
                activeCtrl.LinkReceiver(obj, false);
                done++;
            }

            if (budget > 0) note = budget + " over the level's link budget";
            else if (incompatible + inert > 0 && done == 0) note = "ui.nothing_selected_can_be_driven_by_this";
            else if (already > 0 && done == 0) note = "ui.all_of_them_are_already_linked";
            else if (copies > 0) note = $"added {copies} more controller(s) to fit them all";

            Plugin.Log.LogInfo($"{TypeName(controller)} controller took {done} new link(s)"
                + (copies > 0 ? $", spread over {copies} extra cop{(copies == 1 ? "y" : "ies")}" : "")
                + (already > 0 ? $", {already} already on it" : "")
                + (budget > 0 ? $", {budget} blocked by the link budget" : "")
                + (incompatible + inert > 0 ? $", {incompatible + inert} can't be linked at all" : ""));
            return done;
        }

        public static int UnlinkAll(BatchEditHistory.BatchEntry entry, LevelEditorPlaceableObject controller)
        {
            var ctrl = controller?.LinkController;
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (ctrl == null || sel == null) { Plugin.Log.LogWarning("unlink with no controller selected, ignoring"); return 0; }

            int done = 0;
            foreach (var obj in sel)
            {
                if (obj == null || obj.Pointer == controller.Pointer) continue;
                if (!ctrl.IsReceiverLinkedTo(obj)) continue;

                entry?.Snaps.Add(new BatchEditHistory.ObjectSnap { Obj = obj, LinkTo = controller, Linked = true });
                ctrl.UnlinkReceiver(obj, false, true, true, false);
                done++;
            }
            Plugin.Log.LogInfo($"cut {done} link(s) off the {TypeName(controller)} controller");
            return done;
        }

        private static LevelEditorPlaceableObject CloneController(LevelEditorPlaceableObject source)
        {
            var mgr = LevelEditorManager.Instance;
            if (mgr == null) return null;

            var schema = LevelSaver.GetObjectSchema(source);
            if (schema == null) { Plugin.Log.LogWarning("link controller wouldn't serialise, can't copy it"); return null; }

            schema.Receivers = new Il2CppReferenceArray<UGCObjectDataSchema>(0);
            schema.SnapStatus = new Il2CppSystem.Nullable<bool>(false);
            schema.SnapTargetGuid = new Il2CppSystem.Nullable<Il2CppSystem.Guid>();
            schema.SnappedWith = new Il2CppSystem.Collections.Generic.List<UGCObjectDataSchema.SnapTarget>();
            schema.SnapExceptions = new Il2CppSystem.Collections.Generic.List<UGCObjectDataSchema.SnapTarget>();

            BetterFG.Utilities.UGCSchemaGuids.ReissueGuids(schema);

            var go = LevelLoader.LoadObject(schema, false);
            var lepo = go != null ? go.GetComponent<LevelEditorPlaceableObject>() : null;
            if (lepo == null) { Plugin.Log.LogWarning("controller copy didn't come back as a placeable"); return null; }

            // The game's own clone is GetObjectSchema -> LoadObject -> ConfigureIndividualObjectPostLoad
            // (LevelEditorStateReticleInputHandler.CloneObject). We were skipping the last step, so the
            // copy came out with its rotation parameter never written and never activated.
            try { LevelLoader.ConfigureIndividualObjectPostLoad(go, (LevelEditorPlaceableObject)null, schema); }
            catch (Exception ex) { Plugin.Log.LogWarning($"controller copy post-load threw: {ex.Message}"); }

            try { lepo.OnDeselectInGameEditor(true); }
            catch (Exception ex) { Plugin.Log.LogWarning($"controller copy finalise threw: {ex.Message}"); }

            if (!LevelIO.IsObjectRegistered(lepo)) mgr.RegisterObject(lepo, true, false, false);

            Plugin.Log.LogInfo($"link controller was full, dropped a copy at {lepo.Position}");
            return lepo;
        }

        public static void SetLinked(ObjectLinkController ctrl, LevelEditorPlaceableObject receiver, bool linked)
        {
            if (ctrl.IsReceiverLinkedTo(receiver) == linked) return;

            if (!linked) { ctrl.UnlinkReceiver(receiver, false, true, true, false); return; }

            var rc = receiver.LinkController;
            if (rc == null) return;
            bool ok = ctrl.CanTransmitterLinkToReceiver(rc, out var why);
            if (!ok || why != ObjectLinkController.LinkConnectionCheckResult.ValidConnection)
            {
                Plugin.Log.LogWarning($"couldn't put a link back, editor says {why}");
                return;
            }
            ctrl.LinkReceiver(receiver, false);
        }
    }
}
