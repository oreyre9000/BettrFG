using System;
using System.Collections.Generic;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using BetterFG.Customization.Player;

namespace BetterFG.Services
{
    public class BeanVisualRig : MonoBehaviour
    {
        public BeanVisualRig(IntPtr ptr) : base(ptr) { }

        public const string RIG_NAME = "BetterFG_VisualRig";

        private GameObject _bean;
        private readonly List<Transform> _real = new List<Transform>();
        private readonly List<Transform> _clone = new List<Transform>();
        private readonly Dictionary<int, Transform> _toClone = new Dictionary<int, Transform>();
        private readonly Dictionary<int, Transform> _toReal = new Dictionary<int, Transform>();

        public static BeanVisualRig Get(GameObject bean)
        {
            var t = bean.transform.Find(RIG_NAME);
            return t != null ? t.GetComponent<BeanVisualRig>() : null;
        }

        public static BeanVisualRig Create(GameObject bean)
        {
            var skeleton = bean.transform.Find("Character/SKELETON");
            if (skeleton == null)
            {
                Plugin.Log.LogWarning($"{bean.name} has no Character/SKELETON, so it gets no visual rig and no scale");
                return null;
            }

            var go = new GameObject(RIG_NAME);
            go.transform.SetParent(bean.transform, false);
            var rig = go.AddComponent<BeanVisualRig>();
            rig._bean = bean;
            rig.CloneBone(skeleton, go.transform);
            Plugin.Log.LogInfo($"cloned {rig._real.Count} bones off {bean.name} for the visual rig");
            return rig;
        }

        public void SetScale(float scale) => transform.localScale = new Vector3(scale, scale, scale);

        public Transform RealBone(Transform clone)
        {
            if (clone == null) return null;
            return _toReal.TryGetValue(clone.GetInstanceID(), out var real) ? real : clone;
        }

        public void Rebind()
        {
            MoveProps(_real, _clone, _toClone);
            Swap(_toClone);
        }

        public void Teardown()
        {
            Swap(_toReal);
            MoveProps(_clone, _real, _toReal);
            _real.Clear();
            _clone.Clear();
            Plugin.Log.LogInfo($"visual rig off {_bean.name}, meshes back on the real bones");
            Destroy(gameObject);
        }

        private void CloneBone(Transform real, Transform parent)
        {
            var t = new GameObject(real.name).transform;
            t.SetParent(parent, false);
            real.GetLocalPositionAndRotation(out var p, out var r);
            t.SetLocalPositionAndRotation(p, r);
            t.localScale = real.localScale;

            _real.Add(real);
            _clone.Add(t);
            _toClone[real.GetInstanceID()] = t;
            _toReal[t.GetInstanceID()] = real;

            for (int i = 0; i < real.childCount; i++)
                CloneBone(real.GetChild(i), t);
        }

        private void Swap(Dictionary<int, Transform> map)
        {
            int taken = 0;
            foreach (var smr in _bean.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bones = smr.bones;
                if (bones == null || bones.Length == 0) continue;

                var next = new Il2CppReferenceArray<Transform>(bones.Length);
                bool touched = false;
                for (int i = 0; i < bones.Length; i++)
                {
                    var b = bones[i];
                    next[i] = b;
                    if (b != null && map.TryGetValue(b.GetInstanceID(), out var to)) { next[i] = to; touched = true; }
                }
                if (!touched) continue;

                var root = smr.rootBone;
                smr.bones = next;
                if (root != null && map.TryGetValue(root.GetInstanceID(), out var toRoot)) smr.rootBone = toRoot;
                taken++;
            }

            foreach (var sync in _bean.GetComponentsInChildren<BoneSyncComponent>(true))
                sync.Rebind();

            if (taken > 0) Plugin.Log.LogInfo($"{taken} meshes reskinned on {_bean.name}");
        }

        private static void MoveProps(List<Transform> from, List<Transform> to, Dictionary<int, Transform> bones)
        {
            for (int i = 0; i < from.Count; i++)
            {
                var src = from[i];
                for (int c = src.childCount - 1; c >= 0; c--)
                {
                    var child = src.GetChild(c);
                    if (bones.ContainsKey(child.GetInstanceID())) continue;
                    child.SetParent(to[i], false);
                }
            }
        }

        void LateUpdate() => Sync();

        public void Sync()
        {
            if (_real.Count == 0 || _real[0] is null || _real[0].m_CachedPtr == IntPtr.Zero) return;
            for (int i = 0; i < _real.Count; i++)
            {
                _real[i].GetLocalPositionAndRotation(out var p, out var r);
                _clone[i].SetLocalPositionAndRotation(p, r);
                _clone[i].localScale = _real[i].localScale;
            }
        }
    }
}
