using System;
using System.Collections.Generic;
using HarmonyLib;
using Levels.Invisibeans;
using UnityEngine;

namespace BetterFG.Customization.Player
{
    public class InvisibilitySyncComponent : MonoBehaviour
    {
        public GameObject playerObject;

        private InvisibilityVisualsController invisController;
        private Il2CppSystem.Collections.Generic.List<SkinnedMeshRenderer> ownSmrs;
        private readonly List<Renderer> otherRenderers = new List<Renderer>();
        private bool? lastHidden;
        private Il2CppSystem.Collections.Generic.List<Il2CppSystem.ValueTuple<Shader, int>> savedSettings;
        private readonly Dictionary<IntPtr, Shader[]> extraShaders = new Dictionary<IntPtr, Shader[]>();
        private readonly Dictionary<IntPtr, int[]> extraQueues = new Dictionary<IntPtr, int[]>();

        internal const string GateKey = "bfg.invis.clones";
        private static readonly Dictionary<IntPtr, List<InvisibilitySyncComponent>> _byController =
            new Dictionary<IntPtr, List<InvisibilitySyncComponent>>();
        private static bool _gateOn;

        private IntPtr _registeredUnder = IntPtr.Zero;

        private static readonly Dictionary<IntPtr, InvisibilitySyncComponent> _rendererOwner =
            new Dictionary<IntPtr, InvisibilitySyncComponent>();
        private readonly List<IntPtr> _claimed = new List<IntPtr>();

        void Start() => Setup();

        public void Setup()
        {
            Unregister();
            invisController = playerObject != null ? playerObject.GetComponentInChildren<InvisibilityVisualsController>() : null;
            lastHidden = null;
            savedSettings = null;
            ownSmrs = new Il2CppSystem.Collections.Generic.List<SkinnedMeshRenderer>();
            otherRenderers.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                IntPtr key = r.m_CachedPtr;
                if (_rendererOwner.TryGetValue(key, out var owner)
                    && owner is not null && owner.m_CachedPtr != IntPtr.Zero && owner != this) continue;
                _rendererOwner[key] = this;
                _claimed.Add(key);

                var smr = r.TryCast<SkinnedMeshRenderer>();
                if (smr != null) ownSmrs.Add(smr);
                else otherRenderers.Add(r);
            }

            if (invisController is null || invisController.m_CachedPtr == IntPtr.Zero) return;
            if (ownSmrs.Count == 0 && otherRenderers.Count == 0) return;

            _registeredUnder = invisController.m_CachedPtr;
            if (!_byController.TryGetValue(_registeredUnder, out var list))
            {
                list = new List<InvisibilitySyncComponent>();
                _byController[_registeredUnder] = list;
            }
            list.Add(this);
            SyncGate();
        }

        void OnDestroy() => Unregister();

        private void Unregister()
        {
            for (int i = 0; i < _claimed.Count; i++)
                if (_rendererOwner.TryGetValue(_claimed[i], out var owner) && owner == this)
                    _rendererOwner.Remove(_claimed[i]);
            _claimed.Clear();

            if (_registeredUnder == IntPtr.Zero) return;
            if (_byController.TryGetValue(_registeredUnder, out var list))
            {
                list.Remove(this);
                if (list.Count == 0) _byController.Remove(_registeredUnder);
            }
            _registeredUnder = IntPtr.Zero;
            SyncGate();
        }

        private static void SyncGate()
        {
            bool want = _byController.Count > 0;
            if (want == _gateOn) return;
            _gateOn = want;
            Utilities.PatchGate.Request(GateKey, "clones", want);
        }

        internal static void PushFrom(InvisibilityVisualsController controller)
        {
            if (!_byController.TryGetValue(controller.m_CachedPtr, out var list)) return;
            float ratio = controller._visibilityRatio;
            bool hidden = controller._ready && ratio < 0.5f;
            for (int i = 0; i < list.Count; i++)
                list[i].Apply(hidden, ratio);
        }

        internal static void PushVisible(InvisibilityVisualsController controller)
        {
            if (!_byController.TryGetValue(controller.m_CachedPtr, out var list)) return;
            for (int i = 0; i < list.Count; i++)
                list[i].Apply(false, 1f);
        }

        private void Apply(bool hidden, float ratio)
        {
            if (lastHidden != hidden)
            {
                lastHidden = hidden;

                for (int i = 0; i < otherRenderers.Count; i++)
                {
                    var r = otherRenderers[i];
                    if (r is null || r.m_CachedPtr == IntPtr.Zero) continue;
                    r.enabled = !hidden;
                }

                for (int i = ownSmrs.Count - 1; i >= 0; i--)
                {
                    var smr = ownSmrs[i];
                    if (smr is null || smr.m_CachedPtr == IntPtr.Zero) { ownSmrs.RemoveAt(i); continue; }
                    if (!hidden) continue;
                    var mat = smr.sharedMaterial;
                    if (mat == null || mat.shader == null) continue;
                    if (mat.shader == invisController._hiderCostumeShader || mat.shader == invisController._hiderBodyShader)
                        ownSmrs.RemoveAt(i);
                }

                if (ownSmrs.Count > 0)
                {
                    if (hidden)
                    {
                        SwapExtraSlots(invisController._hiderCostumeShader, InvisibilityVisualsController._transparentCostumeRenderQueue);
                        invisController.AddOutlineToHiderRenderers(ownSmrs, invisController._hiderCostumeShader,
                            out savedSettings, InvisibilityVisualsController._transparentCostumeRenderQueue);
                    }
                    else if (savedSettings != null)
                    {
                        invisController.ResetCharacterShadersAndColor(ownSmrs, savedSettings);
                        savedSettings = null;
                        RestoreExtraSlots();
                    }
                }
            }

            if (hidden && ownSmrs.Count > 0)
                invisController.UpdateMaterialProperties(ownSmrs);
        }

        private void SwapExtraSlots(Shader hider, int queue)
        {
            extraShaders.Clear();
            extraQueues.Clear();
            for (int i = 0; i < ownSmrs.Count; i++)
            {
                var smr = ownSmrs[i];
                if (smr is null || smr.m_CachedPtr == IntPtr.Zero) continue;
                var mats = smr.materials;
                if (mats == null || mats.Length < 2) continue;

                var shaders = new Shader[mats.Length];
                var queues = new int[mats.Length];
                for (int m = 1; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null || mat.shader == hider) continue;
                    shaders[m] = mat.shader;
                    queues[m] = mat.renderQueue;
                    mat.shader = hider;
                    mat.renderQueue = queue;
                }
                extraShaders[smr.m_CachedPtr] = shaders;
                extraQueues[smr.m_CachedPtr] = queues;
            }
        }

        private void RestoreExtraSlots()
        {
            for (int i = 0; i < ownSmrs.Count; i++)
            {
                var smr = ownSmrs[i];
                if (smr is null || smr.m_CachedPtr == IntPtr.Zero) continue;
                if (!extraShaders.TryGetValue(smr.m_CachedPtr, out var shaders)) continue;
                var queues = extraQueues[smr.m_CachedPtr];
                var mats = smr.materials;
                if (mats == null) continue;
                for (int m = 1; m < mats.Length && m < shaders.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null || shaders[m] == null) continue;
                    mat.shader = shaders[m];
                    mat.renderQueue = queues[m];
                }
            }
            extraShaders.Clear();
            extraQueues.Clear();
        }
    }

    [Utilities.BfgPatchGate(InvisibilitySyncComponent.GateKey)]
    [HarmonyPatch(typeof(InvisibilityVisualsController), "SetVisuals")]
    internal static class InvisibilitySetVisualsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(InvisibilityVisualsController __instance) => InvisibilitySyncComponent.PushFrom(__instance);
    }

    [Utilities.BfgPatchGate(InvisibilitySyncComponent.GateKey)]
    [HarmonyPatch(typeof(InvisibilityVisualsController), nameof(InvisibilityVisualsController.ResetCharacterShadersAndColor), new Type[] { })]
    internal static class InvisibilityResetShadersPatch
    {
        [HarmonyPostfix]
        public static void Postfix(InvisibilityVisualsController __instance) => InvisibilitySyncComponent.PushVisible(__instance);
    }
}
