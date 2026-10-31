using System;
using System.Collections.Generic;
using HarmonyLib;
using Levels.Invisibeans;
using UnityEngine;

namespace BetterFG.Customization.Player
{
    // keeps a UGC skin/accessory/item clone's own renderers in lockstep with the wearer's
    // hide-and-seek/invisibility powerup state, since our clones sit outside the renderer
    // lists InvisibilityVisualsController manages on the base body/costume.
    //
    // driven entirely off the game's own SetVisuals / ResetCharacterShadersAndColor calls (see the
    // patches below) — the controller only makes those while an invisibility effect is actually
    // live, so a normal round costs nothing. clones are indexed by their wearer's controller
    // pointer, so a bean with no clone on it is rejected by one dictionary miss.
    public class InvisibilitySyncComponent : MonoBehaviour
    {
        public GameObject playerObject;

        private InvisibilityVisualsController invisController;
        // the game's hide/restore pair is typed on SkinnedMeshRenderer, so plain mesh renderers
        // (hand items, props) still ride the enable/disable path
        private Il2CppSystem.Collections.Generic.List<SkinnedMeshRenderer> ownSmrs;
        private readonly List<Renderer> otherRenderers = new List<Renderer>();
        private bool? lastHidden;
        private Il2CppSystem.Collections.Generic.List<Il2CppSystem.ValueTuple<Shader, int>> savedSettings;
        // the controller's pair only ever swaps material slot 0 (game costumes are single-material),
        // so a UGC skin's extra slots stayed solid — keyed by renderer so a mesh dying mid-powerup
        // can't shift what we put back
        private readonly Dictionary<IntPtr, Shader[]> extraShaders = new Dictionary<IntPtr, Shader[]>();
        private readonly Dictionary<IntPtr, int[]> extraQueues = new Dictionary<IntPtr, int[]>();

        // the patch pair is held out of the game until a clone actually exists to sync
        internal const string GateKey = "bfg.invis.clones";
        private static readonly Dictionary<IntPtr, List<InvisibilitySyncComponent>> _byController =
            new Dictionary<IntPtr, List<InvisibilitySyncComponent>>();
        private static bool _gateOn;

        private IntPtr _registeredUnder = IntPtr.Zero;

        // a renderer can be reachable from two of these at once — the clone's component collects its
        // children before BindMeshToFallguy reparents a mesh away, and that mesh then gets its own.
        // both would hand the same renderer to AddOutlineToHiderRenderers, so the second one records
        // the HIDER shader as the original and the restore leaves it transparent for good.
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

        // the dictionary miss is the fast reject: a bean with no clone on it costs one hash lookup
        // and never touches the controller's fields at all.
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

        // straight through the controller's own hide/restore pair, so our renderers get the same
        // shader swap, render queue, property block and colour restore the game gives its own
        private void Apply(bool hidden, float ratio)
        {
            if (lastHidden != hidden)
            {
                lastHidden = hidden;
                Plugin.Log.LogDebug($"{name} going {(hidden ? "ghost" : "solid")} with its wearer, ratio={ratio:0.00}");

                for (int i = 0; i < otherRenderers.Count; i++)
                {
                    var r = otherRenderers[i];
                    if (r is null || r.m_CachedPtr == IntPtr.Zero) continue;
                    r.enabled = !hidden;
                }

                // SetVisuals has already run by the time this postfix fires, so anything the game
                // manages itself is wearing a hider shader right now — a mesh BindMeshToFallguy
                // registered with the handler is one of those, and swapping it a second time would
                // save the hider shader as its original and strand it invisible on the way back
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
                        // ours first: reading .materials instantiates, and the controller must land on
                        // the same instances or its restore writes to materials nothing is using
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

    // the game's per-effect visual push. carries the live fade ratio, so the clone crosses at the
    // same halfway point the body does instead of popping at the start of the fade.
    [Utilities.BfgPatchGate(InvisibilitySyncComponent.GateKey)]
    [HarmonyPatch(typeof(InvisibilityVisualsController), "SetVisuals")]
    internal static class InvisibilitySetVisualsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(InvisibilityVisualsController __instance)
        {
            InvisibilitySyncComponent.PushFrom(__instance);
            Features.CustomizeFallGuys.FeatureCustomizeFallGuys.OnInvisibilityVisuals(
                __instance, __instance._ready && __instance._visibilityRatio < 0.5f);
        }
    }

    // the canonical "put this character back to normal" call — powerup end, controller disable and
    // round teardown all land here. without it a clone that stopped getting SetVisuals mid-fade
    // would stay hidden for good.
    [Utilities.BfgPatchGate(InvisibilitySyncComponent.GateKey)]
    [HarmonyPatch(typeof(InvisibilityVisualsController), nameof(InvisibilityVisualsController.ResetCharacterShadersAndColor), new Type[] { })]
    internal static class InvisibilityResetShadersPatch
    {
        [HarmonyPostfix]
        public static void Postfix(InvisibilityVisualsController __instance)
        {
            InvisibilitySyncComponent.PushVisible(__instance);
            Features.CustomizeFallGuys.FeatureCustomizeFallGuys.OnInvisibilityVisuals(__instance, false);
        }
    }
}
