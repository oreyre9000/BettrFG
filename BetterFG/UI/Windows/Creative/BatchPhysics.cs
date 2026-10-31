using LevelEditor;

namespace BetterFG.UI.Windows.Creative
{
    public static class BatchPhysics
    {
        public static string[] WeightNames()
        {
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (sel == null) return new string[0];
            foreach (var obj in sel)
            {
                var so = BatchTargets.GetPhysicsParam(obj)?._weightsSO;
                if (so == null) continue;
                int count = so.Count();
                if (count <= 0) continue;
                var names = new string[count];
                for (int i = 0; i < count; i++)
                {
                    string n = so.GetEditorName(i);
                    names[i] = string.IsNullOrEmpty(n) ? so.GetGenericName(i) : n;
                }
                return names;
            }
            return new string[0];
        }

        public static bool AnyPhysics()
        {
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (sel == null) return false;
            foreach (var obj in sel)
            {
                var phys = BatchTargets.GetPhysicsParam(obj);
                if (phys != null && phys._isPhysicsFeatureEnabled) return true;
            }
            return false;
        }

        public static bool AnyDraggable()
        {
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (sel == null) return false;
            foreach (var obj in sel)
            {
                var phys = BatchTargets.GetPhysicsParam(obj);
                if (phys != null && phys._isPhysicsFeatureEnabled && phys.CanMakeDraggable) return true;
            }
            return false;
        }

        public static int SetPhysicsEnabled(bool enabled)
        {
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (sel == null) { Plugin.Log.LogWarning("BatchPhysics: no selection"); return 0; }

            var entry = BatchEditHistory.Begin(enabled ? "physics on" : "physics off");
            int touched = 0, skipped = 0;
            foreach (var obj in sel)
            {
                var phys = BatchTargets.GetPhysicsParam(obj);
                if (phys == null || !phys._isPhysicsFeatureEnabled) { skipped++; continue; }

                entry.Snaps.Add(new BatchEditHistory.ObjectSnap { Obj = obj, PhysicsEnabled = phys._isPhysicsEnabled });
                phys.TogglePhysics(enabled, true);
                touched++;
            }
            BatchEditHistory.Push(entry);
            Plugin.Log.LogInfo($"physics {(enabled ? "on" : "off")} for {touched}, {skipped} in the selection can't do physics");
            return touched;
        }

        public static int SetWeight(int index)
        {
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (sel == null) { Plugin.Log.LogWarning("BatchPhysics: no selection"); return 0; }

            var entry = BatchEditHistory.Begin("weight " + index);
            int touched = 0, skipped = 0, clamped = 0;
            foreach (var obj in sel)
            {
                var phys = BatchTargets.GetPhysicsParam(obj);
                if (phys == null || !phys._isPhysicsFeatureEnabled) { skipped++; continue; }

                int target = index;
                var so = phys._weightsSO;
                if (so != null)
                {
                    int count = so.Count();
                    if (count > 0 && target >= count) { target = count - 1; clamped++; }
                }

                entry.Snaps.Add(new BatchEditHistory.ObjectSnap { Obj = obj, WeightIndex = phys._selectedWeightIndex });
                BatchTargets.ApplyWeight(phys, target);
                touched++;
            }
            BatchEditHistory.Push(entry);
            Plugin.Log.LogInfo($"weight {index} -> {touched} object(s)"
                + (clamped > 0 ? $", {clamped} clamped to their own shorter weight list" : "")
                + (skipped > 0 ? $", skipped {skipped}" : ""));
            return touched;
        }

        public static int SetDraggable(bool enabled)
        {
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (sel == null) { Plugin.Log.LogWarning("BatchPhysics: no selection"); return 0; }

            var entry = BatchEditHistory.Begin(enabled ? "grabbable on" : "grabbable off");
            int touched = 0, skipped = 0;
            foreach (var obj in sel)
            {
                var phys = BatchTargets.GetPhysicsParam(obj);
                if (phys == null || !phys._isPhysicsFeatureEnabled || !phys.CanMakeDraggable) { skipped++; continue; }

                entry.Snaps.Add(new BatchEditHistory.ObjectSnap { Obj = obj, Draggable = phys._isDraggable });
                phys.ToggleDraggable(enabled);
                touched++;
            }
            BatchEditHistory.Push(entry);
            Plugin.Log.LogInfo($"grabbable {(enabled ? "on" : "off")}, {touched} done, {skipped} don't offer it");
            return touched;
        }
    }
}
