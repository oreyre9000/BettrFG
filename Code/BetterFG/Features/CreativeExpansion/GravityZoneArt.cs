using System;
using System.Collections.Generic;
using BetterFG.Utilities;
using FG.Common;
using FG.Common.Audio;
using FGClient;
using HarmonyLib;
using LevelEditor;
using Levels.Obstacles;
using MPG.Utility;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class GravityZoneArt
    {
        internal const string Low = "gravity_low";
        internal const string High = "gravity_high";

        private const string VisualName = "BettrFG_GravityZone";
        private const string ZoneChildName = "Zone";
        private const string ZonePrefabGuid = "9e58ac7bd3a1e8c42b711edbe10644e8";

        private static GameObject _prefab;
        private static bool _prefabTried;
        private static readonly List<Transform> _visuals = new List<Transform>();

        internal static void Apply(Transform root, string attach)
        {
            if (root == null) return;
            bool high = attach == High;

            if (!Measure(root, out var size, out var centre)) return;

            var visual = root.Find(VisualName);
            var zone = visual != null ? visual.Find(ZoneChildName) : null;
            if (zone != null && StateOf(zone) != high)
            {
                Clear(root);
                visual = null;
            }

            if (visual == null)
            {
                visual = Build(root, size, high, centre);
                if (visual == null) return;
            }
            else Fit(visual, root, size, centre);

            Live(visual, !IdentifierObjects.InEditor() || InPlaytest());
        }

        internal static void Refresh()
        {
            bool live = !IdentifierObjects.InEditor() || InPlaytest();
            for (int i = _visuals.Count - 1; i >= 0; i--)
            {
                if (_visuals[i] == null) { _visuals.RemoveAt(i); continue; }
                Live(_visuals[i], live);
            }
        }

        private static void Live(Transform visual, bool live)
        {
            foreach (var v in visual.GetComponentsInChildren<COMMON_GravityModifierVolume>(true))
            {
                if (v == null) continue;
                if (!live) Quiet(v);
                if (v._collisionVolume != null && v._collisionVolume.enabled != live) v._collisionVolume.enabled = live;
            }
        }

        internal static void Clear(Transform root)
        {
            var visual = root != null ? root.Find(VisualName) : null;
            if (visual == null) return;
            foreach (var v in visual.GetComponentsInChildren<COMMON_GravityModifierVolume>(true)) Quiet(v);
            UnityEngine.Object.Destroy(visual.gameObject);
        }

        private static bool Measure(Transform root, out Vector3 size, out Vector3 centre)
        {
            size = Vector3.zero;
            centre = root.position;

            var visual = root.Find(VisualName);
            foreach (var c in root.GetComponentsInChildren<BoxCollider>(true))
            {
                if (c == null) continue;
                if (visual != null && c.transform.IsChildOf(visual)) continue;
                var b = c.bounds;
                if (b.size.sqrMagnitude <= size.sqrMagnitude) continue;
                size = b.size;
                centre = b.center;
            }
            return size.sqrMagnitude > 0.0001f;
        }

        private static bool StateOf(Transform zone)
        {
            var volume = zone.GetComponent<COMMON_GravityModifierVolume>();
            return volume != null && volume._startState == COMMON_GravityModifierVolume.State.High;
        }

        private static void Fit(Transform visual, Transform root, Vector3 size, Vector3 centre)
        {
            var world = root.lossyScale;
            visual.localScale = new Vector3(1f / world.x, 1f / world.y, 1f / world.z);
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;

            var zone = visual.Find(ZoneChildName);
            if (zone == null) return;
            zone.rotation = Quaternion.identity;
            zone.localScale = size;
            zone.position = centre;
        }

        private static Transform Build(Transform root, Vector3 size, bool high, Vector3 centre)
        {
            var prefab = Prefab();
            if (prefab == null) return null;

            var holder = new GameObject(VisualName);
            holder.SetActive(false);
            holder.transform.SetParent(root, false);

            var go = UnityEngine.Object.Instantiate(prefab, holder.transform, false);
            go.name = ZoneChildName;

            Fit(holder.transform, root, size, centre);

            var volume = go.GetComponent<COMMON_GravityModifierVolume>();
            if (volume != null)
            {
                volume._startState = high ? COMMON_GravityModifierVolume.State.High : COMMON_GravityModifierVolume.State.Low;
                volume._lowGravityScalar = 1f;
                volume._highGravityScalar = 1f;

                var pool = volume._gravityBodyTrailVFXPool;
                var pools = SingletonBehaviour<ObjectPoolManager>.Instance;
                if (pool != null && pools != null)
                    try { pools.ReferencePool(pool); }
                    catch (Exception ex) { Plugin.Log.LogWarning($"gravity trail pool wouldn't register: {ex.Message}"); }

                if (PostprocessGravityVolume._volumesIn <= 0) Quiet(volume);
            }
            else Plugin.Log.LogWarning("gravity zone art came up with no modifier volume, no enter effects on this one");

            foreach (var b in go.GetComponentsInChildren<Behaviour>(true))
                if (b != null && b.GetIl2CppType().Name == "NavMeshModifier") UnityEngine.Object.Destroy(b);

            var trigger = volume != null ? volume._collisionVolume : null;
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                if (c != null && (trigger == null || c.Pointer != trigger.Pointer)) c.enabled = false;

            holder.SetActive(true);
            _visuals.Add(holder.transform);
            return holder.transform;
        }

        private static void Quiet(COMMON_GravityModifierVolume volume)
        {
            if (volume == null) return;
            volume._contributingToGravityZoneEffect = false;
            PostprocessGravityVolume._volumesIn = 0;

            var post = volume._postprocessScript;
            if (post != null)
            {
                post._activeOpacity = 0f;
                post._fadeFinished = false;
                if (post._postProcess != null) post._postProcess.enabled = false;
                if (post._material != null)
                    post._material.SetFloat(PostprocessGravityVolume.TotalOpacityPropertyId, 0f);
            }

            var mix = AudioMixing.Instance;
            if (mix == null) return;
            mix._entitiesWantingGravityZonesEffect = 0;
            if (mix._gravityZoneInstance != null)
                try { mix._gravityZoneInstance.KeyOff(); }
                catch (Exception ex) { Plugin.Log.LogWarning($"gravity zone audio wouldn't key off: {ex.Message}"); }
        }

        private static bool InPlaytest()
        {
            var mgr = LevelEditorManager.Instance;
            var state = mgr != null ? mgr.CurrentState : null;
            return state != null && state.TryCast<LevelEditorStatePlay>() != null;
        }

        private static GameObject Prefab()
        {
            if (_prefab != null || _prefabTried) return _prefab;
            _prefabTried = true;
            try { _prefab = Addressables.LoadAssetAsync<GameObject>(ZonePrefabGuid).WaitForCompletion(); }
            catch (Exception ex) { Plugin.Log.LogWarning($"gravity zone prefab {ZonePrefabGuid} wouldn't load: {ex.Message}"); }
            if (_prefab == null) Plugin.Log.LogWarning("no gravity zone art, the themed zones will look untouched");
            return _prefab;
        }
    }

    [HarmonyPatch(typeof(LevelEditorStatePlay), nameof(LevelEditorStatePlay.EnableState))]
    internal static class GravityZonePlaytestStartPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => GravityZoneArt.Refresh();
    }

    [HarmonyPatch(typeof(PostprocessGravityVolume), nameof(PostprocessGravityVolume.OnLocalPlayerTrigger))]
    internal static class GravityZonePostprocessCountPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (PostprocessGravityVolume._volumesIn < 0) PostprocessGravityVolume._volumesIn = 0;
        }
    }
}
