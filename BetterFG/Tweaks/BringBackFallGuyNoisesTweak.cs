using System;
using System.Reflection;
using BetterFG.Utilities;
using FG.Common;
using FG.Common.Audio;
using FG.Common.Character;
using FGClient;
using FMOD;
using FMODUnity;
using HarmonyLib;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class BringBackFallGuyNoisesTweak : BfgTweak
    {
        public BringBackFallGuyNoisesTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "bring_back_fallguy_noises";
        public override string TweakLabel => "tweak.bring_back_fall_guy_noises";
        public override bool DefaultEnabled => true;

        public static BringBackFallGuyNoisesTweak Instance { get; private set; }

        internal static bool Active;

        void Awake() => Instance = this;

        public override void EnableTweak()
        {
            Active = true;
            var gs = GlobalGameStateClient.Instance?._gameStateMachine?.CurrentState;
            if (gs?.TryCast<StateGameInProgress>() != null) { HookRoundEvents(true); ApplyFix(); }
        }

        public override void DisableTweak()
        {
            Active = false;
            HookRoundEvents(false);
        }

        public override void OnRoundStart()
        {
            HookRoundEvents(true);
            ApplyFix();
        }

        public override void OnBannerShown() => HookRoundEvents(false);
        public override void OnMainMenuEntered() => HookRoundEvents(false);

        // the hooks only exist while a round is actually running, never in menus or lobbies.
        private static readonly System.Collections.Generic.List<(MethodInfo target, MethodInfo patch)> _hooks = new System.Collections.Generic.List<(MethodInfo, MethodInfo)>();

        private static void HookRoundEvents(bool on)
        {
            var h = Plugin.HarmonyInstance;
            if (h == null) return;

            if (!on)
            {
                foreach (var (target, patch) in _hooks) h.Unpatch(target, patch);
                _hooks.Clear();
                return;
            }

            if (_hooks.Count > 0) return;
            Hook(h, AccessTools.Method(typeof(FallGuysCharacterController), nameof(FallGuysCharacterController.HandleVfxCollision)), null, nameof(VfxCollisionPostfix));
            Hook(h, AccessTools.Method(typeof(FallingAudioExtensions), nameof(FallingAudioExtensions.PlayFallingAudio)), nameof(FallingPrefix), nameof(CloseGate));
            Hook(h, AccessTools.Method(typeof(MotorFunctionDiveStateDive), nameof(MotorFunctionDiveStateDive.Begin)), null, nameof(DivePostfix));
        }

        private static void Hook(Harmony h, MethodInfo target, string prefix, string postfix)
        {
            if (target == null) { Plugin.Log.LogWarning($"{prefix ?? postfix} has nothing to hook, that noise stays silent for remote beans"); return; }
            var pre = prefix == null ? null : AccessTools.Method(typeof(BringBackFallGuyNoisesTweak), prefix);
            var post = postfix == null ? null : AccessTools.Method(typeof(BringBackFallGuyNoisesTweak), postfix);
            h.Patch(target, prefix: pre == null ? null : new HarmonyMethod(pre), postfix: post == null ? null : new HarmonyMethod(post));
            if (pre != null) _hooks.Add((target, pre));
            if (post != null) _hooks.Add((target, post));
        }

        private static void DivePostfix(MotorFunctionDiveStateDive __instance)
        {
            var c = __instance.MotorAgent?.Character;
            if (c is null) { Plugin.Log.LogInfo("dive state began with no character on the agent?"); return; }
            Plugin.Log.LogInfo($"dive: {c.name}, local={c.IsLocalPlayer}");
            if (c.IsLocalPlayer || Hidden(c)) return;
            OpenGate();
            try { c.FXController.PlaySoundOnCharacter(AudioManager.EventMasterData.Dive); }
            finally { CloseGate(); }
        }

        // the game already decided this contact was worth a vfx puff, so its own filter replaces the
        // magnitude gate the old OnCollisionEnter hook needed. impactStrength is left alone: the
        // quantities compared against HitThreshold/HardLandingThreshold are re-derived here exactly as
        // the sampled version derived them, so no meaning is inferred from the game's own scale.
        private static void VfxCollisionPostfix(FallGuysCharacterController __instance, UnityEngine.Collision collision, bool landed)
        {
            if (!Active || __instance is null || __instance.m_CachedPtr == IntPtr.Zero || Customization.Pets.PetService.IsPetFgcc(__instance)) return;

            if (landed)
            {
                float landSpeed = -__instance.AnimatedPlayerVelocity.y;
                if (landSpeed >= HardLandingThreshold) Yelp(__instance, landSpeed);
                return;
            }

            HandleRemoteCollision(__instance, collision);
        }

        private const float BaseHardLandingThreshold = 20f;
        private const float BaseHitThreshold = 3f;
        private const float HitVoiceCooldown = 0.3f;

        // multiplies both thresholds so landing and collision noises stay proportional as it moves.
        // 1.0 = default game feel, 0.2 = max sensitivity (nearly every bump yelps). slider's Min/Max
        // are given reversed (2 -> 0.2) so dragging right still reads as "more sensitive".
        private const string SensitivityKey = "tweak.bring_back_fallguy_noises.sensitivity";

        internal static float ThresholdMultiplier
        {
            get => float.TryParse(Services.SettingsService.Get(SensitivityKey, "1"),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)
                ? Mathf.Clamp(v, 0.2f, 2f) : 1f;
            set => Services.SettingsService.Set(SensitivityKey, Mathf.Clamp(value, 0.2f, 2f).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private static float HardLandingThreshold => BaseHardLandingThreshold * ThresholdMultiplier;
        private static float HitThreshold => BaseHitThreshold * ThresholdMultiplier;

        public override System.Collections.Generic.List<TweakSlider> GetSliders() => new System.Collections.Generic.List<TweakSlider>
        {
            new TweakSlider { Label = "ui.sensitivity", Min = 2f, Max = 0.2f, Step = 0.1f, Get = () => ThresholdMultiplier, Set = v => ThresholdMultiplier = v }
        };

        // keyed on the il2cpp pointer, which is a plain field read — GetInstanceID is a runtime_invoke
        private static readonly System.Collections.Generic.Dictionary<IntPtr, float> _last = new System.Collections.Generic.Dictionary<IntPtr, float>();

        internal static void HandleRemoteCollision(FallGuysCharacterController c, UnityEngine.Collision collision)
        {
            if (c is null || collision is null) return;
            try
            {
                float rel = collision.relativeVelocity.magnitude;
                if (rel < HitThreshold) return;

                Yelp(c, rel);

                var rb = collision.rigidbody;
                var otherBean = rb != null ? rb.GetComponent<FallGuysCharacterController>() : null;
                if (otherBean is null) otherBean = collision.gameObject.GetComponentInParent<FallGuysCharacterController>();
                if (otherBean is null || otherBean.Pointer == c.Pointer) return;

                Yelp(otherBean, rel);
            }
            catch { }
        }

        private static bool _probedHit;

        private static void ProbeHitEvent()
        {
            _probedHit = true;
            var master = AudioManager.EventMasterData;

            foreach (string key in new[] { master.VO_Hit.audioEvent2D, master.VO_Hit.audioEvent3D, master.VO_Effort.audioEvent3D })
            {
                var desc = RuntimeManager.GetEventDescription(AudioManager.GetGuidForKey(key));
                if (!desc.isValid()) { Plugin.Log.LogWarning($"{key}: no description, bank not loaded"); continue; }

                desc.getLength(out int len);
                desc.is3D(out bool spatial);

                string ps = "";
                if (desc.getParameterDescriptionCount(out int n) == RESULT.OK)
                    for (int i = 0; i < n; i++)
                        if (desc.getParameterDescriptionByIndex(i, out var pd) == RESULT.OK)
                            ps += $" {(string)pd.name}[{pd.minimum}..{pd.maximum} default {pd.defaultvalue}]";

                Plugin.Log.LogInfo($"{key}: {len}ms, 3d={spatial}, params:{(ps.Length == 0 ? " none" : ps)}");
            }
        }

        private static bool Hidden(FallGuysCharacterController c)
        {
            if (c.IsLocalPlayer) return false;
            var v = c._invisibilityVisualsController;
            if (v is null || v.m_CachedPtr == IntPtr.Zero) return false;
            return v._activatingInvisible
                || (v.enabled && v._ready && v._invisibilityMode == Levels.Invisibeans.InvisibilityVisualsController.InvisibilityMode.Invisibeans);
        }

        private static void FallingPrefix(FallGuysCharacterController character)
        {
            if (!Hidden(character)) OpenGate();
        }

        private static void Yelp(FallGuysCharacterController c, float strength)
        {
            if (Hidden(c)) return;
            IntPtr key = c.Pointer;
            _last.TryGetValue(key, out float last);
            if (Time.time - last < HitVoiceCooldown) return;
            _last[key] = Time.time;

            if (!_probedHit) ProbeHitEvent();

            var master = AudioManager.EventMasterData;
            var p = new AudioParamContainer(2);
            p.AddBool(true);
            p.AddFloat(master.ImpactStrengthParam, Mathf.Clamp(strength * 25f, 60f, 500f));
            PlayThroughGate(master.VO_Hit, c, p);
        }

        // RemovePlayersAudioActive is the game's own cull of every remote bean's audio. it used to be
        // pinned false for the whole round just so our PlayCharacterAudio calls would clear
        // CanCharacterPlayAudio — which also handed every other player's footsteps, foley and voice
        // back to FMOD as live 3D events, a cost that grows with the crowd. PlayCharacterAudio does
        // that check inside the call, so the flag only has to be down across the call itself and the
        // player's own audio setting is back before anything else can read it.
        private static AudioManager _am;
        private static bool _stockRemovePlayersAudio;

        private static void OpenGate()
        {
            if (_am is not null && _am.m_CachedPtr != IntPtr.Zero) _am.RemovePlayersAudioActive = false;
        }

        private static void CloseGate()
        {
            if (_am is not null && _am.m_CachedPtr != IntPtr.Zero) _am.RemovePlayersAudioActive = _stockRemovePlayersAudio;
        }

        private static void PlayThroughGate(AudioEvent2D3DPairSO pair, FallGuysCharacterController c, AudioParamContainer p)
        {
            OpenGate();
            try { AudioManager.PlayCharacterAudio(pair, c, c.transform.position, p); }
            finally { CloseGate(); }
        }

        internal static void ApplyFix()
        {
            try
            {
                if (_am is null || _am.m_CachedPtr == IntPtr.Zero)
                    _am = UnityEngine.Object.FindObjectOfType<AudioManager>();
                if (_am != null)
                {
                    _stockRemovePlayersAudio = _am.RemovePlayersAudioActive;
                    Plugin.Log.LogInfo($"fallguy noises: game's remote-audio culling is {(_stockRemovePlayersAudio ? "ON" : "off")}, gate opens only around our own plays");
                }
            }
            catch { }

            FallGuysLib.Audio.FmodUtils.UnmuteBus("bus:/MASTER/BUS_VO");
            FallGuysLib.Audio.FmodUtils.UnmuteBus("bus:/MASTER/BUS_VO/BUS_VO_3D");
            FallGuysLib.Audio.FmodUtils.UnmuteBus("bus:/MASTER/BUS_VO/BUS_VO_2D");
        }
    }
}
