using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.UI;
using FGClient;
using FGClient.UI;
using HarmonyLib;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class AlwaysShowTimerTweak : BfgTweak
    {
        public AlwaysShowTimerTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "always_show_timer";
        public override string TweakLabel => "tweak.always_show_ingame_timer";
        public override bool DefaultEnabled => false;

        public static AlwaysShowTimerTweak Instance { get; private set; }

        void Awake() => Instance = this;

        // only force the timer while one of these GameState GameObjects is the active one. otherwise the
        // timer leaks into every state (countdown, banners, etc).
        private const string GameStatesPath = "UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates";
        private static Transform _playing, _spectator;

        // the two visibility getters and the per-frame state pass all ask this, so it's several calls a
        // frame. resolving the two state objects by name once and memoising the answer per frame keeps it
        // off the il2cpp string marshalling that a childCount name scan pays on every single call.
        private static int _answerFrame = -1;
        private static bool _answer;

        public static bool ShouldForceNow()
        {
            if (_answerFrame == Time.frameCount) return _answer;
            _answerFrame = Time.frameCount;
            _answer = Resolve();
            return _answer;
        }

        private static bool Resolve()
        {
            var inst = Instance;
            if (inst == null || !inst.IsEnabled) return false;

            if (_playing == null && _spectator == null)
            {
                var go = GameObject.Find(GameStatesPath);
                if (go == null) return false;
                var root = go.transform;
                for (int i = 0; i < root.childCount; i++)
                {
                    var child = root.GetChild(i);
                    string n = child.name;
                    if (n == "PlayingState") _playing = child;
                    else if (n == "SpectatorState") _spectator = child;
                }
                if (_playing == null && _spectator == null) return false;
            }

            return (_playing != null && _playing.gameObject.activeInHierarchy)
                || (_spectator != null && _spectator.gameObject.activeInHierarchy);
        }
    }

    // DetermineTimeRemainingState runs every frame inside Update() and recomputes _shouldShowTimeRemaining.
    // forcing the field/getters true reveals the container but the game skips CreateTimeRemaining (which is
    // what actually writes the TMP text), so the timer shows up blank. fix: after the game's own pass, force
    // the field true AND run CreateTimeRemaining ourselves so the game's own formatting fills the text.
    //
    // CreateTimeRemaining dirties the TMP mesh and its canvas, so calling it every frame cost a full text
    // regen + canvas rebatch at framerate for a readout that only ever changes once a second. it now fires
    // on the second boundary. one postfix, not two — this method is hot enough that a second trampoline
    // dispatch for the threshold check was pure overhead.
    [Utilities.BfgPatchGate("tweak.always_show_timer", roundOnly: true)]
    [HarmonyPatch(typeof(GameplayTimerViewModel), "DetermineTimeRemainingState")]
    internal static class DetermineTimeRemainingStatePatch
    {
        private static int _lastWritten = int.MinValue;

        [HarmonyPostfix]
        public static void Postfix(GameplayTimerViewModel __instance)
        {
            var gsv = GlobalGameStateClient.Instance?.GameStateView;
            if (gsv == null) { TimerThresholdReapply.Reset(); return; }

            float remaining = gsv.GameplayTimeRemaining;
            TimerThresholdReapply.Check(remaining);

            if (!AlwaysShowTimerTweak.ShouldForceNow()) { _lastWritten = int.MinValue; return; }
            __instance._shouldShowTimeRemaining = true;

            int second = Mathf.CeilToInt(remaining);
            if (second == _lastWritten) return;
            _lastWritten = second;
            try { __instance.CreateTimeRemaining(remaining); } catch { }
        }
    }

    // the view binds visibility to these two getters, not the raw field. force them true so the timer text
    // actually renders regardless of round state.
    [Utilities.BfgPatchGate("tweak.always_show_timer", roundOnly: true)]
    [HarmonyPatch(typeof(GameplayTimerViewModel), "get_ShouldShowSmallTimeRemaining")]
    internal static class ShouldShowSmallTimeRemainingPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (AlwaysShowTimerTweak.ShouldForceNow()) __result = true;
        }
    }

    [Utilities.BfgPatchGate("tweak.always_show_timer", roundOnly: true)]
    [HarmonyPatch(typeof(GameplayTimerViewModel), "get_ShouldShowTimeRemaining")]
    internal static class ShouldShowTimeRemainingPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (AlwaysShowTimerTweak.ShouldForceNow()) __result = true;
        }
    }

    // when the timer naturally pops on at 30/15/10s left, the game reassigns the BigTimerContainer
    // sprite, dropping our pink/grey baked swap. detect the threshold crossings off the live
    // remaining time and reapply a frame later — the swap is cached per image+texture so this is a
    // sprite reassign, not a rebuild.
    internal static class TimerThresholdReapply
    {
        private static float _prevRemaining = float.MaxValue;
        private static readonly float[] Thresholds = { 30f, 15f, 10f };

        public static void Reset() => _prevRemaining = float.MaxValue;

        public static void Check(float now)
        {
            float prev = _prevRemaining;
            _prevRemaining = now;

            // round just (re)started — time jumped up. reset edge tracking.
            if (now > prev + 1f) return;

            bool crossed = false;
            foreach (var t in Thresholds)
                if (prev > t && now <= t) { crossed = true; break; }
            if (!crossed) return;

            var app = MenuCustomizationApplication.Instance;
            if (app == null) return;
            app.StartCoroutine(ReapplyNextFrame(app).WrapToIl2Cpp());
        }

        private static IEnumerator ReapplyNextFrame(MenuCustomizationApplication app)
        {
            yield return null;
            app.ReapplyBakedPinkGreyTextures();
        }
    }
}
