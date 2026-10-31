using System;
using System.Collections.Generic;
using System.Reflection;
using BetterFG.Utilities;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using HarmonyLib;
using UnityEngine;

namespace BetterFG.Features.Replay
{
    // every capture hook below sits on something the game does constantly — each FMOD event start/stop,
    // each pooled spawn, each animator audio state — and they all did nothing unless a recording was
    // live. the early-out was cheap but the trampoline in front of it was not: an il2cpp-to-managed
    // transition plus wrapper allocations, on all of that traffic, permanently. so the hooks go in when
    // recording starts and come back out when it stops.
    internal static class ReplayCaptureHooks
    {
        private static readonly List<(MethodBase target, MethodInfo patch)> _installed =
            new List<(MethodBase, MethodInfo)>();

        public static void SetActive(bool on)
        {
            if (on == _installed.Count > 0) return;
            if (on) Install(); else Remove();
        }

        private static void Install()
        {
            Hook(AccessTools.Method(typeof(EventInstance), nameof(EventInstance.start)), typeof(ReplaySlideAudioPatch), false);
            Hook(AccessTools.Method(typeof(EventInstance), nameof(EventInstance.stop)), typeof(ReplaySlideAudioStopPatch), false);
            Hook(AccessTools.Method(typeof(AudioManager), nameof(AudioManager.PlayCharacterAudio)), typeof(ReplayCharacterAudioPatch), false);
            Hook(AccessTools.Method(typeof(AudioManager), nameof(AudioManager.PlaySpeechBubbleAudio)), typeof(ReplaySpeechBubbleAudioPatch), false);
            Hook(AccessTools.Method(typeof(AudioManager), nameof(AudioManager.PlayOneShot), new[] { typeof(string), typeof(Vector3) }), typeof(ReplayObjectAudioPatch), false);
            Hook(AccessTools.Method(typeof(AudioManager), nameof(AudioManager.PlayOneShotAttached), new[] { typeof(string), typeof(GameObject) }), typeof(ReplayAttachedAudioPatch), false);
            Hook(ReplayCallbackAudioPatch.Target(), typeof(ReplayCallbackAudioPatch), false);
            foreach (var m in ReplayCreateAudioPatch.Targets()) Hook(m, typeof(ReplayCreateAudioPatch), true);
            Hook(AccessTools.Method(typeof(AudioManager), nameof(AudioManager.StopAndReleaseAudioEvent)), typeof(ReplayStopAudioPatch), false);
            Hook(AccessTools.Method(typeof(PlayAudioStateBehaviour), nameof(PlayAudioStateBehaviour.OnStateEnter), new[] { typeof(Animator), typeof(AnimatorStateInfo), typeof(int) }), typeof(ReplayAnimatorAudioPatch), false);
            Hook(AccessTools.Method(typeof(Levels.Obstacles.COMMON_PrefabSpawnerBase), nameof(Levels.Obstacles.COMMON_PrefabSpawnerBase.OnInstantiateObject)), typeof(ReplaySpawnerPatch), true);
            Hook(AccessTools.Method(typeof(FG.Common.GameObjectPool), nameof(FG.Common.GameObjectPool.PrepareObjectForUse)), typeof(ReplayPoolGetPatch), true);
            Hook(AccessTools.Method(typeof(FG.Common.GameObjectPool), nameof(FG.Common.GameObjectPool.PrepareObjectForStorage)), typeof(ReplayPoolReturnPatch), false);
            Hook(AccessTools.Method(typeof(FG.Common.Character.FallGuyVFXController), nameof(FG.Common.Character.FallGuyVFXController.HandleOnDive)), typeof(ReplayDiveSlideVfxPatch), false);
            Hook(AccessTools.Method(typeof(Levels.Tag.TailTagAccessory), nameof(Levels.Tag.TailTagAccessory.OnAccessoryEnabled)), typeof(ReplayTailEnablePatch), true);
            Hook(AccessTools.Method(typeof(Levels.Tag.TailTagAccessory), nameof(Levels.Tag.TailTagAccessory.OnAccessoryDisabled)), typeof(ReplayTailDisablePatch), true);
            Hook(AccessTools.Method(typeof(Levels.Starlink.StarlinkNode), nameof(Levels.Starlink.StarlinkNode.OnButtonPress)), typeof(ReplayStarchartButtonPatch), true);
            Plugin.Log.LogInfo($"replay capture hooks in, {_installed.Count} methods");
        }

        private static void Hook(MethodBase target, Type owner, bool postfix)
        {
            if (target == null) { Plugin.Log.LogWarning($"replay: nothing to hook for {owner.Name}"); return; }
            var patch = AccessTools.Method(owner, postfix ? "Postfix" : "Prefix");
            if (patch == null) return;
            var hm = new HarmonyMethod(patch);
            Plugin.HarmonyInstance.Patch(target, prefix: postfix ? null : hm, postfix: postfix ? hm : null);
            _installed.Add((target, patch));
        }

        private static void Remove()
        {
            foreach (var (target, patch) in _installed)
                Plugin.HarmonyInstance.Unpatch(target, patch);
            _installed.Clear();
            Plugin.Log.LogInfo("recording stopped, capture hooks back out of the audio + pool paths");
        }
    }

    // fmod paths end in the same short key AudioManager uses (".../F_Slide" -> "F_Slide"), so the last
    // segment is enough to identify anything that starts, whoever started it
    internal static class ReplaySlideEvent
    {
        struct Info
        {
            public string key;
            public bool slide;
        }

        static readonly Dictionary<IntPtr, Info> _known = new Dictionary<IntPtr, Info>();

        public static void Reset() => _known.Clear();

        public static bool Resolve(EventInstance instance, out EventDescription desc, out string key, out bool slide)
        {
            key = null;
            slide = false;
            desc = default;
            if (!instance.isValid() || instance.getDescription(out desc) != RESULT.OK) return false;

            if (!_known.TryGetValue(desc.handle, out var info))
            {
                if (desc.getPath(out string path) == RESULT.OK && !string.IsNullOrEmpty(path))
                {
                    int cut = path.LastIndexOf('/');
                    info.key = cut >= 0 ? path.Substring(cut + 1) : path;
                    info.slide = string.Equals(info.key, "F_Slide", StringComparison.OrdinalIgnoreCase);
                }
                _known[desc.handle] = info;
            }

            key = info.key;
            slide = info.slide;
            return !string.IsNullOrEmpty(key);
        }
    }

    internal static class ReplaySlideAudioPatch
    {
        public static void Prefix(EventInstance __instance)
        {
            if (FeatureReplay.Live == null) return;
            if (!ReplaySlideEvent.Resolve(__instance, out var desc, out string key, out bool slide)) return;

            // blast ball explosions (and anything else a pooled vfx prefab plays for itself) never touch
            // AudioManager, so the key-based hooks above can't see them. this is where they all end up
            if (!slide)
            {
                var pos = Vector3.zero;
                if (__instance.get3DAttributes(out var attributes) == RESULT.OK)
                    pos = new Vector3(attributes.position.x, attributes.position.y, attributes.position.z);

                // a looping event (not one-shot) needs its stop recorded or playback loops it forever
                bool oneshot = true;
                desc.isOneshot(out oneshot);
                FeatureReplay.CaptureStrayAudio(key, pos, oneshot ? System.IntPtr.Zero : __instance.handle);
                return;
            }

            var pairs = new List<(string, float)>();
            if (desc.getParameterDescriptionCount(out int count) == RESULT.OK)
            {
                for (int i = 0; i < count; i++)
                {
                    if (desc.getParameterDescriptionByIndex(i, out var pd) != RESULT.OK) continue;
                    if (__instance.getParameterByID(pd.id, out float value, out _) != RESULT.OK) continue;
                    pairs.Add(((string)pd.name, value));
                }
            }

            FeatureReplay.CaptureSlideAudio(pairs, __instance.handle);
        }
    }

    internal static class ReplaySlideAudioStopPatch
    {
        public static void Prefix(EventInstance __instance)
        {
            if (FeatureReplay.Live == null) return;
            if (!ReplaySlideEvent.Resolve(__instance, out _, out _, out bool slide)) return;
            if (slide) FeatureReplay.CloseSlideAudio(__instance.handle);
            else FeatureReplay.CloseStrayAudio(__instance.handle);
        }
    }

    internal static class ReplayCharacterAudioPatch
    {
        public static void Prefix(AudioEvent2D3DPairSO pair, FallGuysCharacterController characterController, Vector3 pos, AudioParamContainer paramContainer)
        {
            if (FeatureReplay.Live == null) return;
            FeatureReplay.CaptureAudio(pair, characterController, pos, paramContainer);
        }
    }

    internal static class ReplaySpeechBubbleAudioPatch
    {
        public static void Prefix(string audioEvent, FallGuysCharacterController characterController)
        {
            if (FeatureReplay.Live == null || characterController == null) return;
            FeatureReplay.CaptureAudio(audioEvent, characterController, characterController.transform.position);
        }
    }

    internal static class ReplayObjectAudioPatch
    {
        public static void Prefix(string __0, Vector3 __1)
        {
            if (FeatureReplay.Live == null) return;
            FeatureReplay.CaptureAudio(__0, null, __1);
        }
    }

    internal static class ReplayAttachedAudioPatch
    {
        public static void Prefix(string __0, GameObject __1)
        {
            if (FeatureReplay.Live == null || __1 == null) return;
            FeatureReplay.CaptureAudio(__0, null, __1.transform.position);
        }
    }

    // PlayOneShot has a second overload that takes a completion callback, and level-element sounds
    // like the blast ball's explosion come through that one, not the plain (key, pos) pair
    internal static class ReplayCallbackAudioPatch
    {
        public static MethodBase Target()
        {
            foreach (var m in typeof(AudioManager).GetMethods())
                if (m.Name == nameof(AudioManager.PlayOneShot) && m.GetParameters().Length == 3) return m;
            return null;
        }

        public static void Prefix(string __0, Vector3 __2)
        {
            if (FeatureReplay.Live == null) return;
            FeatureReplay.CaptureAudio(__0, null, __2);
        }
    }

    internal static class ReplayCreateAudioPatch
    {
        public static IEnumerable<MethodBase> Targets()
        {
            foreach (var m in typeof(AudioManager).GetMethods())
            {
                if (m.Name != nameof(AudioManager.CreateAudio)) continue;
                var ps = m.GetParameters();
                if (ps.Length > 1 && (ps[1].ParameterType == typeof(Vector3) || ps[1].ParameterType == typeof(Transform)))
                    yield return m;
            }
        }

        public static void Postfix(object[] __args, EventInstanceReference __result)
        {
            if (FeatureReplay.Live == null || __args == null || __args.Length < 2) return;

            Vector3 pos;
            if (__args[1] is Vector3 at) pos = at;
            else if (__args[1] is Transform tf && tf != null) pos = tf.position;
            else return;

            FeatureReplay.CaptureHeldAudio(__result, __args[0] as string, pos);
        }
    }

    internal static class ReplayStopAudioPatch
    {
        public static void Prefix(ref EventInstanceReference instanceReference)
        {
            if (FeatureReplay.Live == null) return;
            FeatureReplay.CloseHeldAudio(instanceReference);
        }
    }

    internal static class ReplayAnimatorAudioPatch
    {
        public static void Prefix(PlayAudioStateBehaviour __instance, Animator animator)
        {
            if (FeatureReplay.Live == null || animator == null) return;

            var controller = FeatureReplay.BeanFor(animator);
            if (controller == null) return;

            var source = __instance.TransformOverride != null ? __instance.TransformOverride : controller.transform;
            FeatureReplay.CaptureAudio(__instance._audioToPlay, controller, source.position);
        }
    }

    internal static class ReplaySpawnerPatch
    {
        public static void Postfix(GameObject go)
        {
            if (FeatureReplay.Live == null || go == null) return;
            ReplayWorldRecorder.OnLocalSpawn(go, FeatureReplay.GameplayTime);
        }
    }

    internal static class ReplayPoolGetPatch
    {
        public static void Postfix(GameObject go)
        {
            if (FeatureReplay.Live == null || go == null) return;
            ReplayWorldRecorder.OnLocalSpawn(go, FeatureReplay.GameplayTime);
        }
    }

    internal static class ReplayPoolReturnPatch
    {
        public static void Prefix(GameObject go)
        {
            if (FeatureReplay.Live == null || go == null) return;
            ReplayWorldRecorder.OnLocalDespawn(go, FeatureReplay.GameplayTime);
        }
    }

    internal static class ReplayDiveSlideVfxPatch
    {
        public static void Prefix(FG.Common.Character.FallGuyVFXController __instance)
        {
            if (FeatureReplay.Live == null || __instance == null) return;
            FeatureReplay.CaptureDiveSlideVfx(__instance);
        }
    }

    internal static class ReplayTailEnablePatch
    {
        public static void Postfix(Levels.Tag.TailTagAccessory __instance)
        {
            if (FeatureReplay.Live == null) return;
            FeatureReplay.CaptureTailState(__instance, true);
        }
    }

    internal static class ReplayTailDisablePatch
    {
        public static void Postfix(Levels.Tag.TailTagAccessory __instance)
        {
            if (FeatureReplay.Live == null) return;
            FeatureReplay.CaptureTailState(__instance, false);
        }
    }

    internal static class ReplayStarchartButtonPatch
    {
        public static void Postfix(Levels.Starlink.StarlinkNode __instance)
        {
            if (FeatureReplay.Live == null) return;
            FeatureReplay.CaptureStarchartButtonPress(__instance);
        }
    }

    internal static class ReplayMenuReloadGuard
    {
        static MethodBase _target;
        static MethodInfo _patch;

        public static void Arm()
        {
            if (_target != null) return;
            _target = AccessTools.Method(typeof(FGClient.MainMenuManager), nameof(FGClient.MainMenuManager.HideChallenges));
            _patch = AccessTools.Method(typeof(ReplayMenuReloadGuard), nameof(Skip));
            Plugin.HarmonyInstance.Patch(_target, prefix: new HarmonyMethod(_patch));
        }

        public static void Disarm()
        {
            if (_target == null) return;
            Plugin.HarmonyInstance.Unpatch(_target, _patch);
            _target = null;
            _patch = null;
        }

        static bool Skip()
        {
            Plugin.Log.LogInfo("held the menu's challenges teardown off the exit reload — it NREs when the reload starts from the menu, and that throw is what kills StateMainMenu.Initialise");
            return false;
        }
    }
}
