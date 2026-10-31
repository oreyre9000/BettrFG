using System;
using System.Collections.Generic;
using System.Linq;
using FGClient;
using FGClient.UI;
using FGClient.UI.Core;
using HarmonyLib;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class FasterTransitionsTweak : BfgTweak
    {
        public FasterTransitionsTweak(IntPtr ptr) : base(ptr) { }

        public const string GateKey = "tweak.faster_transitions";
        public override string TweakId => "faster_transitions";
        public override string TweakLabel => "Faster Transitions";
        public override string TweakTooltip => "Screens and popups fade in and out much faster and respond to input sooner. Fading screens stop blocking clicks.";

        internal const float FadeIn = 0.12f;
        internal const float FadeOut = 0.35f;
        internal const float MaxDelay = 0.05f;

        static readonly Dictionary<IntPtr, (ScreenViewModel Vm, float InOut)> _screens = new Dictionary<IntPtr, (ScreenViewModel, float)>();
        static readonly Dictionary<IntPtr, (ScreenMetaData Meta, float Time)> _metas = new Dictionary<IntPtr, (ScreenMetaData, float)>();
        static readonly Dictionary<IntPtr, CanvasGroup> _released = new Dictionary<IntPtr, CanvasGroup>();
        static float _popupFade = -1f;

        internal static void Prepare(ScreenMetaData meta, float max)
        {
            if (meta == null) return;
            if (!_metas.TryGetValue(meta.Pointer, out var m))
            {
                if (meta.TransitionTime <= max) return;
                m = (meta, meta.TransitionTime);
                _metas[meta.Pointer] = m;
            }
            meta.TransitionTime = Mathf.Min(m.Time, max);
        }

        internal static void Clamp(ScreenViewModel vm, float max)
        {
            Prepare(vm.MetaData, max);
            if (!_screens.TryGetValue(vm.Pointer, out var s))
            {
                s = (vm, vm._screenFadeInAndOutTime);
                _screens[vm.Pointer] = s;
            }
            vm._screenFadeInAndOutTime = Mathf.Min(s.InOut, max);
            var meta = vm.MetaData;
            vm._fadeTime = meta != null && meta.TransitionTime >= 0f ? meta.TransitionTime : vm._screenFadeInAndOutTime;
            var loading = vm.TryCast<LoadingScreenViewModel>();
            if (loading != null) loading._fadeDuration = Mathf.Min(loading._fadeDuration, FadeOut);
            var pm = PopupManager.Instance;
            if (pm != null && pm._popupFadeInOutTime > FadeOut)
            {
                _popupFade = pm._popupFadeInOutTime;
                pm._popupFadeInOutTime = FadeOut;
            }
        }

        internal static void ReleaseRaycasts(ScreenViewModel vm)
        {
            var cg = vm.GetComponent<CanvasGroup>();
            if (cg == null || !cg.blocksRaycasts) return;
            cg.blocksRaycasts = false;
            _released[vm.Pointer] = cg;
            if (_released.Count > 64)
                foreach (var k in _released.Where(p => p.Value == null).Select(p => p.Key).ToList()) _released.Remove(k);
        }

        internal static void RestoreRaycasts(ScreenViewModel vm)
        {
            if (_released.Remove(vm.Pointer, out var cg) && cg != null) cg.blocksRaycasts = true;
        }

        public override void DisableTweak()
        {
            foreach (var s in _screens.Values)
                if (s.Vm != null) s.Vm._screenFadeInAndOutTime = s.InOut;
            foreach (var m in _metas.Values) m.Meta.TransitionTime = m.Time;
            foreach (var cg in _released.Values)
                if (cg != null) cg.blocksRaycasts = true;
            _screens.Clear();
            _metas.Clear();
            _released.Clear();
            var pm = PopupManager.Instance;
            if (pm != null && _popupFade > 0f) pm._popupFadeInOutTime = _popupFade;
            _popupFade = -1f;
        }
    }

    [Utilities.BfgPatchGate(FasterTransitionsTweak.GateKey)]
    [HarmonyPatch(typeof(ScreenViewModel), "Init")]
    internal static class FasterTransitionsInitPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ScreenMetaData metaData) => FasterTransitionsTweak.Prepare(metaData, FasterTransitionsTweak.FadeIn);
    }

    [Utilities.BfgPatchGate(FasterTransitionsTweak.GateKey)]
    [HarmonyPatch(typeof(ScreenViewModel), "DoFadeIn")]
    internal static class FasterTransitionsFadeInPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ScreenViewModel __instance)
        {
            FasterTransitionsTweak.Clamp(__instance, FasterTransitionsTweak.FadeIn);
            FasterTransitionsTweak.RestoreRaycasts(__instance);
        }
    }

    [Utilities.BfgPatchGate(FasterTransitionsTweak.GateKey)]
    [HarmonyPatch(typeof(ScreenViewModel), "DoFadeOut")]
    internal static class FasterTransitionsFadeOutPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ScreenViewModel __instance)
        {
            FasterTransitionsTweak.Clamp(__instance, FasterTransitionsTweak.FadeOut);
            FasterTransitionsTweak.ReleaseRaycasts(__instance);
        }
    }

    [Utilities.BfgPatchGate(FasterTransitionsTweak.GateKey)]
    [HarmonyPatch(typeof(ScreenViewModel), "ToggleScreenFromCache")]
    internal static class FasterTransitionsCachePatch
    {
        [HarmonyPrefix]
        public static void Prefix(ScreenViewModel __instance, bool showScreen)
        {
            if (showScreen) FasterTransitionsTweak.RestoreRaycasts(__instance);
        }
    }

    [Utilities.BfgPatchGate(FasterTransitionsTweak.GateKey)]
    [HarmonyPatch(typeof(CanvasGroupFader), "initialiseFader")]
    internal static class FasterTransitionsFaderPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref float delayTime, ref float fadeTime)
        {
            delayTime = Mathf.Min(delayTime, FasterTransitionsTweak.MaxDelay);
            fadeTime = Mathf.Min(fadeTime, FasterTransitionsTweak.FadeIn);
        }
    }
}
