using System;
using System.Collections.Generic;
using FG.Common.LevelEditor.Serialization;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace BetterFG.Utilities
{
    public static class UGCSchemaGuids
    {
        public static void ReissueGuids(UGCObjectDataSchema schema)
        {
            var flat = new List<UGCObjectDataSchema>();
            Walk(schema, flat.Add);
            Remap(flat);
        }

        public static void Walk(UGCObjectDataSchema schema, Action<UGCObjectDataSchema> fn)
        {
            if (schema == null) return;
            fn(schema);
            WalkAll(schema.Children, fn);
            WalkAll(schema.Receivers, fn);
            WalkAll(schema.Triggers, fn);
            WalkAll(schema.WallsObjs, fn);
            WalkAll(schema.WaypointObjects, fn);

            var comps = schema.Components;
            if (comps == null) return;
            for (int i = 0; i < comps.Count; i++) Walk(comps[i], fn);
        }

        public static void WalkAll(Il2CppReferenceArray<UGCObjectDataSchema> arr, Action<UGCObjectDataSchema> fn)
        {
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++) Walk(arr[i], fn);
        }

        public static void Remap(List<UGCObjectDataSchema> flat)
        {
            var map = new Dictionary<string, Il2CppSystem.Guid>();
            foreach (var node in flat)
            {
                if (!Read(() => node.GUID, out var had)) continue;
                string key = had.ToString();
                if (!map.ContainsKey(key)) map[key] = Il2CppSystem.Guid.NewGuid();
            }

            int dangling = 0;
            foreach (var node in flat)
            {
                node.GUID = Swap(() => node.GUID, map, ref dangling);
                node.SnapTargetGuid = Swap(() => node.SnapTargetGuid, map, ref dangling);
                node.OtherGuid = Swap(() => node.OtherGuid, map, ref dangling);
                node.PillarAGuid = Swap(() => node.PillarAGuid, map, ref dangling);
                node.PillarBGuid = Swap(() => node.PillarBGuid, map, ref dangling);
            }

            Plugin.Log.LogInfo($"{map.Count} guid(s) reissued across {flat.Count} node(s)"
                + (dangling > 0 ? $", {dangling} reference(s) pointed outside the tree and got cleared" : ""));
        }

        private static bool Read(Func<Il2CppSystem.Nullable<Il2CppSystem.Guid>> get, out Il2CppSystem.Guid guid)
        {
            guid = default;
            try
            {
                var n = get();
                if (n == null || !n.HasValue) return false;
                guid = n.Value;
                return true;
            }
            catch { return false; }
        }

        private static Il2CppSystem.Nullable<Il2CppSystem.Guid> Swap(
            Func<Il2CppSystem.Nullable<Il2CppSystem.Guid>> get, Dictionary<string, Il2CppSystem.Guid> map, ref int dangling)
        {
            var none = new Il2CppSystem.Nullable<Il2CppSystem.Guid>();
            if (!Read(get, out var had)) return none;
            if (map.TryGetValue(had.ToString(), out var fresh))
                return new Il2CppSystem.Nullable<Il2CppSystem.Guid>(fresh);
            dangling++;
            return none;
        }
    }
}
