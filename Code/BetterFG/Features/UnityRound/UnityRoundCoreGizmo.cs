using System.Collections.Generic;
using BetterFG.Core;
using LevelEditor;
using UnityEngine;

namespace BetterFG.Features.UnityRound
{
    internal static class UnityRoundCoreGizmo
    {
        private const string PrefabKey = "unity01";
        private const float GizmoScale = 100f;
        private const int NoPostAOQueue = 3000;

        private static readonly Dictionary<int, GameObject> _live = new Dictionary<int, GameObject>();

        public static GameObject Use(LevelEditorPlaceableObject lepo)
        {
            int id = lepo.gameObject.GetInstanceID();
            if (_live.TryGetValue(id, out var existing) && existing != null) return existing;

            var go = AssetManager.SpawnPersistent(PrefabKey);
            if (go == null) { Plugin.Log.LogWarning("no unity01 in the 3d bundle, core gizmo stays blank"); return null; }

            go.name = "BFG_UnityRoundCoreGizmo";
            go.transform.SetParent(lepo.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * GizmoScale;

            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                Object.Destroy(c);

            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null) mats[i].renderQueue = NoPostAOQueue;
            }

            go.AddComponent<UnityRoundCoreBillboard>();

            _live[id] = go;
            return go;
        }

        public static void Return(int instanceId)
        {
            if (!_live.TryGetValue(instanceId, out var go)) return;
            _live.Remove(instanceId);
            if (go != null) Object.Destroy(go);
        }

        public static void ReturnAllExcept(HashSet<int> keep)
        {
            if (_live.Count == 0) return;

            List<int> dead = null;
            foreach (var kv in _live)
                if (!keep.Contains(kv.Key)) (dead ?? (dead = new List<int>())).Add(kv.Key);
            if (dead == null) return;
            foreach (int id in dead) Return(id);
        }
    }
}
