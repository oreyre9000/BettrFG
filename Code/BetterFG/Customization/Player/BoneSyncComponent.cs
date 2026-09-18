using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using FG.Common.Character;
using FallGuysIK;

namespace BetterFG.Customization.Player
{
    public class BoneSyncComponent : MonoBehaviour
    {
        public GameObject playerObject;
        public bool isRemote = false;

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

        private struct RealBoneOffset { public Transform bone; public Vector3 localPosition; }
        private RealBoneOffset[] _realOffsets = Array.Empty<RealBoneOffset>();
        private Vector3[] _savedLocal = Array.Empty<Vector3>();
        private bool _offsetsLive;
        private bool _restoreRunning;

        internal const string GateKey = "bfg.bonesync.ragdoll";
        private static readonly Dictionary<IntPtr, BoneSyncComponent> _byRagdoll = new Dictionary<IntPtr, BoneSyncComponent>();
        private static readonly Dictionary<IntPtr, List<BoneSyncComponent>> _byBean = new Dictionary<IntPtr, List<BoneSyncComponent>>();
        private IntPtr _beanKey = IntPtr.Zero;
        private IntPtr _ragdollKey = IntPtr.Zero;

        void Start()
        {
            _phase = GetInstanceID() & 3;
            SetupBoneReferences();
        }

        public void DriveRealBonesFromOffsets(List<BoneOffsetEntry> offsets)
        {
            if (_playerBonesByName.Count == 0) SetupBoneReferences();

            var list = new List<RealBoneOffset>();
            foreach (var entry in offsets)
            {
                if (string.IsNullOrEmpty(entry.bone)) continue;
                string key = entry.bone.Trim();
                if (!_playerBonesByName.TryGetValue(key, out var real) || real == null || real == playerRoot) continue;
                list.Add(new RealBoneOffset { bone = real, localPosition = entry.localPosition });
            }

            _realOffsets = list.ToArray();
            _savedLocal = new Vector3[_realOffsets.Length];
            _pairs = Array.Empty<BonePair>();
            enabled = _realOffsets.Length > 0;

            if (_realOffsets.Length > 0 && !_restoreRunning)
            {
                _restoreRunning = true;
                StartCoroutine(RestoreAfterRender().WrapToIl2Cpp());
            }
            Plugin.Log.LogInfo($"BoneSync: driving {_realOffsets.Length} skeleton bone positions from the skin's rig on {playerObject.name}, restored after render so physics never sees them");
        }

        private void ApplyRealOffsets()
        {
            if (_savedLocal.Length != _realOffsets.Length) _savedLocal = new Vector3[_realOffsets.Length];
            for (int i = 0; i < _realOffsets.Length; i++)
            {
                var b = _realOffsets[i].bone;
                if (b is null || b.m_CachedPtr == IntPtr.Zero) continue;
                if (!_offsetsLive) _savedLocal[i] = b.localPosition;
                b.localPosition = _realOffsets[i].localPosition;
            }
            _offsetsLive = true;
        }

        private void RestoreRealBones()
        {
            if (!_offsetsLive) return;
            _offsetsLive = false;
            for (int i = 0; i < _realOffsets.Length; i++)
            {
                var b = _realOffsets[i].bone;
                if (b is null || b.m_CachedPtr == IntPtr.Zero) continue;
                b.localPosition = _savedLocal[i];
            }
        }

        private IEnumerator RestoreAfterRender()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;
                RestoreRealBones();
            }
        }

        public void Rebind()
        {
            _rigs.Clear();
            _activeRig = -1;
            _playerBonesByName = new Dictionary<string, Transform>(StringComparer.Ordinal);
            SetupBoneReferences();
        }

        public void SyncNow()
        {
            for (int i = 0; i < _pairs.Length; i++)
            {
                var custom = _pairs[i].customBone;
                var driver = _pairs[i].playerBone;
                if (custom is null || custom.m_CachedPtr == IntPtr.Zero) continue;
                if (driver is null || driver.m_CachedPtr == IntPtr.Zero) continue;

                if (_pairs[i].trackPosition)
                {
                    driver.GetPositionAndRotation(out var pos, out var rot);
                    custom.SetPositionAndRotation(pos, rot);
                }
                else
                {
                    var fkCustom = _pairs[i].customParent.TransformPoint(_pairs[i].customRestLocal);
                    var fkDriver = _pairs[i].driverParent.TransformPoint(_pairs[i].driverRestLocal);
                    custom.SetPositionAndRotation(fkCustom + (driver.position - fkDriver), driver.rotation);
                }
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

            if (_realOffsets.Length > 0)
            {
                ApplyRealOffsets();
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

            int live = PickBodyRig();
            if (live < 0 || live == _activeRig) return;

            UseRig(live);
            Plugin.Log.LogInfo($"bone sync hopped to {_rigs[live].label} on {playerObject.name}, {cachedBones.Count} bones matched");
        }

        private int PickBodyRig()
        {
            for (int i = 0; i < _rigs.Count; i++)
            {
                var rig = _rigs[i];
                if (rig.rootGo is null || rig.rootGo.m_CachedPtr == IntPtr.Zero || !rig.rootGo.activeInHierarchy) continue;
                if (rig.label.IndexOf("Body_LOD", StringComparison.OrdinalIgnoreCase) < 0) continue;
                return i;
            }
            for (int i = 0; i < _rigs.Count; i++)
            {
                var rig = _rigs[i];
                if (rig.rootGo is null || rig.rootGo.m_CachedPtr == IntPtr.Zero || !rig.rootGo.activeInHierarchy) continue;
                return i;
            }
            return -1;
        }

        private void UseRig(int index)
        {
            _activeRig = index;
            var rig = _rigs[index];
            _playerBonesByName = rig.bones;
            playerRoot = rig.root;
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

            if (_rigs.Count > 0)
            {
                int live = PickBodyRig();
                if (live >= 0) UseRig(live);
            }

            if (playerRoot == null || customSkinRoot == null)
                Plugin.Log.LogWarning($"BoneSync: setup failed on {playerObject.name} - playerRoot={(playerRoot != null ? playerRoot.name : "null")} customRoot={(customSkinRoot != null ? customSkinRoot.name : "null")}");
            else
                Plugin.Log.LogInfo($"BoneSync: ready on {playerObject.name} - {_rigs.Count} skeletons found, riding '{_rigs[_activeRig].label}' with {_playerBonesByName.Count} bones, cached={cachedBones.Count}");

            RegisterRagdoll();
            RegisterBean();
        }

        private void RegisterBean()
        {
            UnregisterBean();
            if (playerObject is null || playerObject.m_CachedPtr == IntPtr.Zero) return;
            _beanKey = playerObject.m_CachedPtr;
            if (!_byBean.TryGetValue(_beanKey, out var list)) { list = new List<BoneSyncComponent>(); _byBean[_beanKey] = list; }
            if (!list.Contains(this)) list.Add(this);
        }

        private void UnregisterBean()
        {
            if (_beanKey == IntPtr.Zero) return;
            if (_byBean.TryGetValue(_beanKey, out var list))
            {
                list.Remove(this);
                if (list.Count == 0) _byBean.Remove(_beanKey);
            }
            _beanKey = IntPtr.Zero;
        }

        internal static void AfterIkPose(FallGuyIkController ik)
        {
            if (ik is null || ik.m_CachedPtr == IntPtr.Zero) return;
            var character = ik.transform;
            var bean = character != null ? character.parent : null;
            if (bean is null || bean.m_CachedPtr == IntPtr.Zero) return;
            if (!_byBean.TryGetValue(bean.gameObject.m_CachedPtr, out var list)) return;

            for (int i = 0; i < list.Count; i++)
            {
                var sync = list[i];
                if (sync is null || sync.m_CachedPtr == IntPtr.Zero || !sync.enabled) continue;
                if (sync._realOffsets.Length > 0) sync.ApplyRealOffsets();
                else sync.SyncNow();
            }
        }

        private void RegisterRagdoll()
        {
            UnregisterRagdoll();
            var ragdoll = playerObject != null ? playerObject.GetComponentInChildren<RagdollController>(true) : null;
            if (ragdoll == null) return;
            _ragdollKey = ragdoll.m_CachedPtr;
            _byRagdoll[_ragdollKey] = this;
            SyncGate();
        }

        private void UnregisterRagdoll()
        {
            if (_ragdollKey == IntPtr.Zero) return;
            _byRagdoll.Remove(_ragdollKey);
            _ragdollKey = IntPtr.Zero;
            SyncGate();
        }

        private static void SyncGate() => Utilities.PatchGate.Request(GateKey, "bone offsets", _byRagdoll.Count > 0);

        void OnDestroy()
        {
            RestoreRealBones();
            UnregisterRagdoll();
            UnregisterBean();
        }

        internal static void AfterRagdollPose(RagdollController controller)
        {
            if (!_byRagdoll.TryGetValue(controller.m_CachedPtr, out var sync) || sync is null || sync.m_CachedPtr == IntPtr.Zero) return;
            if (sync._realOffsets.Length > 0) sync.ApplyRealOffsets();
            else sync.SyncNow();
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
            if (playerBone == null && playerRoot != null)
                playerBone = FindBoneDeep(playerRoot, customBone.name);

            if (playerBone != null)
            {
                bool rootLike = customBone == customSkinRoot || customBone.parent == customSkinRoot;
                if (rootLike || (customBone.parent != null && playerBone.parent != null))
                    cachedBones.Add(new BonePair
                    {
                        customBone = customBone,
                        playerBone = playerBone,
                        trackPosition = rootLike,
                        customParent = customBone.parent,
                        driverParent = playerBone.parent,
                        customRestLocal = customBone.localPosition,
                        driverRestLocal = playerBone.localPosition
                    });
            }

            for (int i = 0; i < customBone.childCount; i++)
                CollectCachedBones(customBone.GetChild(i));
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
            public Dictionary<string, Transform> bones;
            public int stride;
        }

        private struct BonePair
        {
            public Transform customBone;
            public Transform playerBone;
            public bool trackPosition;
            public Transform customParent;
            public Transform driverParent;
            public Vector3 customRestLocal;
            public Vector3 driverRestLocal;
        }
    }

    [Utilities.BfgPatchGate(BoneSyncComponent.GateKey)]
    [HarmonyPatch(typeof(FallGuyIkController), "LateUpdate")]
    internal static class BoneSyncAfterIkPatch
    {
        [HarmonyPostfix]
        public static void Postfix(FallGuyIkController __instance) => BoneSyncComponent.AfterIkPose(__instance);
    }

    [Utilities.BfgPatchGate(BoneSyncComponent.GateKey)]
    [HarmonyPatch(typeof(RagdollController), "UpdateJoints")]
    internal static class BoneSyncAfterRagdollPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RagdollController __instance) => BoneSyncComponent.AfterRagdollPose(__instance);
    }

}
