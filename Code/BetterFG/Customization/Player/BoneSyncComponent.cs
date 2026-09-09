using System.Collections.Generic;
using UnityEngine;
using System;

namespace BetterFG.Customization.Player
{
    public class BoneSyncComponent : MonoBehaviour
    {
        public GameObject playerObject;
        public bool isRemote = false;
        public List<BoneOffsetEntry> boneOffsets = new List<BoneOffsetEntry>();

        private Dictionary<string, Vector3> boneOffsetMap = new Dictionary<string, Vector3>(StringComparer.Ordinal);
        private HashSet<string> loggedAppliedBones = new HashSet<string>();

        private Transform playerRoot;
        private Transform customSkinRoot;
        private readonly List<BonePair> cachedBones = new List<BonePair>();
        private BonePair[] _pairs = Array.Empty<BonePair>();

        private readonly List<LodRig> _rigs = new List<LodRig>();
        private int _activeRig = -1;
        private int _stride = 1;
        private int _phase;
        private int _tick;

        private Dictionary<string, Transform> _playerBonesByName = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private Transform _playerTorsoBone;
        private Transform _customTorsoBone;

        void Start()
        {
            _phase = GetInstanceID() & 3;
            RebuildOffsetMap();
            SetupBoneReferences();
        }

        public void Rebind()
        {
            _rigs.Clear();
            _activeRig = -1;
            _playerBonesByName = new Dictionary<string, Transform>(StringComparer.Ordinal);
            SetupBoneReferences();
        }

        public void SetBoneOffsets(List<BoneOffsetEntry> offsets)
        {
            boneOffsets = offsets ?? new List<BoneOffsetEntry>();
            RebuildOffsetMap();
            RebuildCachedBones();
            Plugin.Log.LogInfo($"BoneSync: {boneOffsetMap.Count} offsets registered on '{name}'");
        }

        public void SyncNow()
        {
            for (int i = 0; i < _pairs.Length; i++)
            {
                var custom = _pairs[i].customBone;
                var driver = _pairs[i].playerBone;
                if (custom is null || custom.m_CachedPtr == IntPtr.Zero) continue;
                if (driver is null || driver.m_CachedPtr == IntPtr.Zero) continue;

                driver.GetPositionAndRotation(out var pos, out var rot);
                if (_pairs[i].hasOffset)
                {
                    custom.rotation = rot;
                    custom.localPosition = _pairs[i].offset;
                }
                else
                    custom.SetPositionAndRotation(pos, rot);
            }

            ForceScaledTorsoLocalPos();
        }

        private void RebuildOffsetMap()
        {
            boneOffsetMap.Clear();
            loggedAppliedBones.Clear();
            if (boneOffsets == null) return;
            foreach (var entry in boneOffsets)
            {
                if (string.IsNullOrEmpty(entry.bone)) continue;
                string key = entry.bone.Trim();
                if (!boneOffsetMap.ContainsKey(key))
                    boneOffsetMap[key] = entry.localPosition;
                string lower = key.ToLowerInvariant();
                if (!boneOffsetMap.ContainsKey(lower))
                    boneOffsetMap[lower] = entry.localPosition;
            }
        }

        void LateUpdate()
        {
            if (playerObject is null || playerObject.m_CachedPtr == IntPtr.Zero)
            {
                if (!isRemote) Destroy(gameObject);
                return;
            }

            if ((++_tick + _phase) % _stride != 0) return;

            if (!playerObject.activeInHierarchy)
            {
                if (!isRemote) Destroy(gameObject);
                return;
            }

            SwitchToLiveRig();
            SyncNow();
        }

        private void SwitchToLiveRig()
        {
            if (_rigs.Count < 2) return;

            if (_activeRig >= 0)
            {
                var cur = _rigs[_activeRig];
                if (!(cur.rootGo is null) && cur.rootGo.m_CachedPtr != IntPtr.Zero && cur.rootGo.activeInHierarchy) return;
            }

            for (int i = 0; i < _rigs.Count; i++)
            {
                var rig = _rigs[i];
                if (rig.rootGo is null || rig.rootGo.m_CachedPtr == IntPtr.Zero || !rig.rootGo.activeInHierarchy) continue;
                if (i == _activeRig) return;

                UseRig(i);
                Plugin.Log.LogInfo($"bone sync hopped to {rig.label} on {playerObject.name}, {cachedBones.Count} bones matched");
                return;
            }
        }

        private void UseRig(int index)
        {
            _activeRig = index;
            var rig = _rigs[index];
            _playerBonesByName = rig.bones;
            playerRoot = rig.root;
            _playerTorsoBone = rig.torso;
            _stride = rig.stride;
            RebuildCachedBones();
        }

        private void SetupBoneReferences()
        {
            var seenRoots = new HashSet<int>();
            foreach (var skinned in playerObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bones = skinned.bones;
                if (bones == null || bones.Length == 0) continue;

                Transform root = null;
                foreach (var b in bones)
                {
                    if (b == null) continue;
                    if (b.name == "Root") { root = b; break; }
                }
                if (root == null) root = skinned.rootBone;
                if (root == null) continue;
                if (!seenRoots.Add(root.GetInstanceID())) continue;

                var map = new Dictionary<string, Transform>(StringComparer.Ordinal);
                foreach (var b in bones)
                {
                    if (b == null) continue;
                    if (!map.ContainsKey(b.name)) map[b.name] = b;
                    string lower = b.name.ToLowerInvariant();
                    if (!map.ContainsKey(lower)) map[lower] = b;
                }

                foreach (string alias in new[] { root.name, "Body_LOD0 (merge)", "Body_LOD0", "Main FG/Body_LOD0 (merge)" })
                {
                    if (!map.ContainsKey(alias)) map[alias] = root;
                    string lowerAlias = alias.ToLowerInvariant();
                    if (!map.ContainsKey(lowerAlias)) map[lowerAlias] = root;
                }

                if (!map.TryGetValue("Torso_C_jnt_NoStrechSquash", out var torso))
                    torso = FindBoneDeep(root, "Torso_C_jnt_NoStrechSquash");

                string label = skinned.name;
                int lod = 0;
                int at = label.IndexOf("LOD", StringComparison.Ordinal);
                if (at >= 0 && at + 3 < label.Length && char.IsDigit(label[at + 3]))
                    lod = label[at + 3] - '0';

                _rigs.Add(new LodRig
                {
                    label = label,
                    root = root,
                    rootGo = root.gameObject,
                    torso = torso,
                    bones = map,
                    stride = lod < 2 ? 1 : (lod > 4 ? 4 : lod)
                });
            }

            customSkinRoot = transform.Find("Main FG/Body_LOD0 (merge)");
            if (customSkinRoot == null)
            {
                string[] customPaths = { "Body_LOD0 (merge)", "Main FG/Body_LOD0", "Body_LOD0" };
                foreach (string path in customPaths)
                {
                    customSkinRoot = transform.Find(path);
                    if (customSkinRoot != null) break;
                }
            }

            _customTorsoBone = FindBoneDeep(transform, "Torso_C_jnt_NoStrechSquash");

            if (_rigs.Count > 0)
            {
                int live = 0;
                for (int i = 0; i < _rigs.Count; i++)
                    if (_rigs[i].rootGo.activeInHierarchy) { live = i; break; }
                UseRig(live);
            }

            if (playerRoot == null || customSkinRoot == null)
                Plugin.Log.LogWarning($"BoneSync: setup failed on {playerObject.name} - playerRoot={(playerRoot != null ? playerRoot.name : "null")} customRoot={(customSkinRoot != null ? customSkinRoot.name : "null")}");
            else
                Plugin.Log.LogInfo($"BoneSync: ready on {playerObject.name} - {_rigs.Count} skeletons found, riding '{_rigs[_activeRig].label}' with {_playerBonesByName.Count} bones, cached={cachedBones.Count}");
        }

        private void RebuildCachedBones()
        {
            cachedBones.Clear();
            if (customSkinRoot != null) CollectCachedBones(customSkinRoot);
            _pairs = cachedBones.ToArray();
        }

        private void CollectCachedBones(Transform customBone)
        {
            if (customBone == null) return;

            Transform playerBone = null;
            if (!_playerBonesByName.TryGetValue(customBone.name, out playerBone) || playerBone == null)
                _playerBonesByName.TryGetValue(customBone.name.ToLowerInvariant(), out playerBone);

            if (playerBone != null)
            {
                bool hasOffset = boneOffsetMap.TryGetValue(customBone.name, out Vector3 offset)
                              || boneOffsetMap.TryGetValue(customBone.name.Trim().ToLowerInvariant(), out offset);

                cachedBones.Add(new BonePair
                {
                    customBone = customBone,
                    playerBone = playerBone,
                    hasOffset = hasOffset,
                    offset = offset
                });

                if (hasOffset && !loggedAppliedBones.Contains(customBone.name))
                {
                    loggedAppliedBones.Add(customBone.name);
                    Plugin.Log.LogInfo($"BoneSync: offset on '{customBone.name}': ({offset.x:F3}, {offset.y:F3}, {offset.z:F3})");
                }
            }

            for (int i = 0; i < customBone.childCount; i++)
                CollectCachedBones(customBone.GetChild(i));
        }

        private void ForceScaledTorsoLocalPos()
        {
            if (_playerTorsoBone is null || _playerTorsoBone.m_CachedPtr == IntPtr.Zero) return;
            if (_customTorsoBone is null || _customTorsoBone.m_CachedPtr == IntPtr.Zero) return;
            _playerTorsoBone.GetLocalPositionAndRotation(out var lp, out _);
            _customTorsoBone.localPosition = lp * 0.01f;
        }

        private static Transform FindBoneDeep(Transform root, string boneName)
        {
            if (root == null) return null;
            if (root.name == boneName) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var result = FindBoneDeep(root.GetChild(i), boneName);
                if (result != null) return result;
            }
            return null;
        }

        private class LodRig
        {
            public string label;
            public Transform root;
            public GameObject rootGo;
            public Transform torso;
            public Dictionary<string, Transform> bones;
            public int stride;
        }

        private struct BonePair
        {
            public Transform customBone;
            public Transform playerBone;
            public bool hasOffset;
            public Vector3 offset;
        }
    }
}
