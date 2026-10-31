using System;
using Character;
using FG.Common.Character;
using FG.Common.Character.MotorSystem;
using FGClient;
using FGClient.UI;
using FallGuysLib.Players;
using HarmonyLib;
using MPG.Utility;
using UnityEngine;
using BetterFG.Customization.Social;
using BetterFG.Features.Replay;
using BetterFG.Tweaks;
using PlayerUtils = FallGuysLib.Players.PlayerUtils;

namespace BetterFG.Patches.Social
{
    // handles phrase/emoticon id remapping and muting ranked emoticons in one prefix
    [HarmonyPatch(typeof(MotorFunctionSpeechStateActive), "PlaySpeechOption")]
    internal static class SpeechPlayPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(MotorFunctionSpeechStateActive __instance, ref int optionId)
        {
            if (BetterFG.Features.CustomGamemodes.CustomGamemodeHost.OnSpeech(__instance)) return false;

            if (MuteSocialSoundsTweak.Active || DisablePlayerEmoticonsTweak.Active || DisablePlayerPhrasesTweak.Active)
            {
                try
                {
                    var mgr = SingletonBehaviour<SpeechOptionsManager>.Instance;
                    var lookup = mgr?._speechOptionsLookup;
                    if (lookup != null && lookup.ContainsKey(optionId))
                    {
                        var opt = lookup[optionId];
                        if (opt != null)
                        {
                            if (MuteSocialSoundsTweak.Active && MuteSocialSoundsTweak.IsRanked(opt))
                            {
                                Plugin.Log?.LogInfo("muteRankSounds: skipped " + opt._audioEvent);
                                return false;
                            }

                            string group = opt.CMSGroupID;
                            if (DisablePlayerPhrasesTweak.Active && group == "cosmetics_phrases") return false;
                            if (DisablePlayerEmoticonsTweak.Active && group == "cosmetics_emoticons") return false;
                        }
                    }
                }
                catch (Exception ex) { Plugin.Log?.LogWarning("muteRankSounds: " + ex.Message); }
            }

            // creative-mode beans never register in the networked player manager, so also accept the
            // editor bean by name as "local"
            var speechBean = __instance.MotorAgent?.gameObject;
            uint localId = PlayerUtils.GetLocalPlayerId();
            bool localSpeech = speechBean != null && speechBean.name == "LevelEditor_FallGuy(Clone)";
            if (!localSpeech && (localId == 0 || speechBean != PlayerUtils.GetPlayerObject(localId)))
            {
                RemoteSocialDisplay.TryRemap(speechBean, ref optionId);
                FeatureReplay.CaptureSpeech(speechBean, optionId);
                return true;
            }

            if (PhraseInjectionService.Remap.TryGetValue(optionId, out int remappedPhrase))
            {
                optionId = remappedPhrase;
                PhraseInjectionService.PlayCustomSound(remappedPhrase, speechBean);
            }
            else if (EmoticonInjectionService.Remap.TryGetValue(optionId, out int remappedEmote))
            {
                optionId = remappedEmote;
                EmoticonInjectionService.PlayCustomSound(remappedEmote, speechBean);
            }

            FeatureReplay.CaptureSpeech(speechBean, optionId);
            return true;
        }
    }

    [HarmonyPatch(typeof(MotorFunctionSpeech), nameof(MotorFunctionSpeech.CanSendSpeech))]
    internal static class SpeechCooldownBypassPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(MotorFunctionSpeech __instance, SocialOption speechOption, ref bool __result)
        {
            if (!PhraseCooldownBypassTweak.TryShowLocally(__instance, speechOption)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(SpeechBubbleViewModel), nameof(SpeechBubbleViewModel.InitSpeechBubble))]
    internal static class SpeechBubbleLocalAlphaPatch
    {
        [HarmonyPostfix]
        public static void Postfix(SpeechBubbleViewModel __instance, SocialOption speechOption, FallGuysCharacterController fgcc)
        {
            PhraseCooldownBypassTweak.ApplyBubbleAlpha(__instance, fgcc);
            BetterFG.Features.TextChat.FeatureTextChat.OnBubbleInit(__instance, speechOption);
        }
    }

    // custom emotes: when an emote slot we injected plays, drive the bean's animator with our own
    // AnimationClip (loaded from a bundle) and suppress the game's addressable-backed playback.
    [HarmonyPatch(typeof(MotorFunctionEmoteStateEmote), nameof(MotorFunctionEmoteStateEmote.PlayEmote))]
    internal static class PlayEmotePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(MotorFunctionEmoteStateEmote __instance, int emoteIndex, EmotesOption emoteOption)
        {
            try
            {
                var emoteBean = __instance.MotorAgent?.gameObject;
                uint localId = PlayerUtils.GetLocalPlayerId();
                bool localBean = emoteBean != null && emoteBean.name == "LevelEditor_FallGuy(Clone)";
                if (!localBean && localId != 0) localBean = emoteBean == PlayerUtils.GetPlayerObject(localId);

                // fire regardless of custom/vanilla - pets should mirror either kind
                bool petBean = BetterFG.Customization.Pets.PetService.IsPetFgcc(emoteBean?.GetComponent<FallGuysCharacterController>());
                if (!petBean)
                {
                    var mirrorPets = localBean
                        ? (System.Collections.Generic.IEnumerable<FallGuysCharacterController>)BetterFG.Customization.Pets.PetService.Instance?.LiveFgccs
                        : BetterFG.Customization.Pets.RemotePetService.Instance?.LiveFgccsForOwner(emoteBean);
                    BetterFG.Customization.Pets.PetEmoteEcho.Echo(__instance.MotorAgent, emoteIndex, emoteOption, mirrorPets);
                }

                AnimationClip clip;
                string sound = null;
                if (localBean || petBean)
                {
                    if (!EmoteInjectionService.CustomClips.TryGetValue(emoteIndex, out clip) || clip == null) return true;
                    EmoteInjectionService.SoundPaths.TryGetValue(emoteIndex, out sound);
                }
                else
                {
                    clip = EmoteInjectionService.RemoteClip(emoteBean, emoteIndex, out sound);
                    if (clip == null) return true;
                }

                var fgcc = localBean ? PlayerUtils.PlayerController : emoteBean.GetComponent<FallGuysCharacterController>();
                if (fgcc != null && fgcc.RigidBody.velocity.sqrMagnitude > 0.5f)
                {
                    // the emote function already flipped to StateEmote before we got here. blocking
                    // playback doesn't undo that, so it'd stay stuck at state 1 and you couldn't emote
                    // again (even after stopping) without doing an action. reset it back to inactive.
                    EmoteInjectionService.ResetEmote(__instance.MotorAgent, __instance);
                    return false;
                }

                var bean = __instance.MotorAgent?.gameObject;
                if (bean == null) return true;

                var animator = bean.GetComponentInChildren<Animator>();
                if (animator == null) return true;

                EmoteInjectionService.PlayClipOnBean(animator, clip, sound, __instance.MotorAgent, __instance);
                return false; // we played it, skip original
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("CustomEmote: PlayEmote prefix failed: " + ex.Message);
                return true;
            }
        }
    }

    // grabbing cancels a playing custom emote (local player only). Begin takes an int.
    [HarmonyPatch(typeof(MotorFunctionGrabStateGrab), "Begin", new[] { typeof(int) })]
    internal static class GrabCancelEmotePatch
    {
        [HarmonyPrefix]
        public static void Prefix(MotorFunctionGrabStateGrab __instance)
            => GrabEmoteCancel.CancelLocalEmote(__instance.MotorAgent);
    }

    // the player grab (attempt-grab) is what actually fires when you press grab — cancel emote on it too
    [HarmonyPatch(typeof(MotorFunctionPlayerGrabStateAttemptGrab), "Begin", new[] { typeof(int) })]
    internal static class PlayerGrabCancelEmotePatch
    {
        [HarmonyPrefix]
        public static void Prefix(MotorFunctionPlayerGrabStateAttemptGrab __instance)
            => GrabEmoteCancel.CancelLocalEmote(__instance.MotorAgent);
    }

    internal static class GrabEmoteCancel
    {
        public static void CancelLocalEmote(MotorAgent agent)
        {
            if (!EmoteInjectionService.AnyGraphsLive) return;

            try
            {
                if (agent == null) return;
                uint localId = PlayerUtils.GetLocalPlayerId();
                bool localBean = agent.gameObject != null && agent.gameObject.name == "LevelEditor_FallGuy(Clone)";
                if (!localBean && (localId == 0 || agent.gameObject != PlayerUtils.GetPlayerObject(localId))) return;
                EmoteInjectionService.StopEmoteForAgent(agent);
            }
            catch { }
        }
    }

    // the game's UpdateHighlightedItem fires for both select+deselect, but on a custom-injected
    // slot the deselect path isn't toggling the "TabContentEmotesWheelAssigned" highlight backing
    // off — so the previous slot stays lit when the cursor moves. force the active state ourselves
    // off the same call.
    [HarmonyPatch(typeof(SocialWheelViewModel), nameof(SocialWheelViewModel.UpdateHighlightedItem))]
    internal static class WheelHighlightFixPatch
    {
        private const string HIGHLIGHT_GO_NAME = "TabContentEmotesWheelAssigned";

        [HarmonyPostfix]
        public static void Postfix(SocialWheelViewModel __instance, int index, bool isHighlighted)
        {
            try
            {
                var vms = __instance?._wheelOptionsViewModels;
                if (vms == null || index < 0 || index >= vms.Count) return;
                var vm = vms[index];
                if (vm == null) return;
                var bgT = vm.transform.Find(HIGHLIGHT_GO_NAME);
                if (bgT == null)
                {
                    // not always a direct child — search the subtree
                    foreach (var t in vm.GetComponentsInChildren<Transform>(true))
                        if (t != null && t.name == HIGHLIGHT_GO_NAME) { bgT = t; break; }
                }
                if (bgT == null) return;
                if (bgT.gameObject.activeSelf != isHighlighted)
                    bgT.gameObject.SetActive(isHighlighted);
            }
            catch { }
        }
    }

    // clicking the wheel calls OnPointerClick, which collapses it. for our visualizer clone in the
    // Social tab that's not wanted — bring the wheel straight back. the tab-side guard makes sure
    // only our clone reacts, so the real in-game wheel is untouched.
    [HarmonyPatch(typeof(SocialPrimeHandler), nameof(SocialPrimeHandler.OnPointerClick))]
    internal static class PrimeClickPatch
    {
        private static int _clickedSlot = -1;

        [HarmonyPrefix]
        public static void Prefix(SocialPrimeHandler __instance)
            => _clickedSlot = __instance.HighlightedSocialWheel?.GetLastHighlitedIndex() ?? -1;

        [HarmonyPostfix]
        public static void Postfix(SocialPrimeHandler __instance)
            => BetterFG.UI.Components.SocialWheelPreview.OnPrimeClicked(__instance.gameObject, _clickedSlot);
    }

    // reapply before wheel renders so first open already has custom slots
    [HarmonyPatch(typeof(SocialPrimeHandler), nameof(SocialPrimeHandler.DisplayWheel), new[] { typeof(WheelType) })]
    internal static class DisplayWheelPatch
    {
        [HarmonyPrefix]
        public static void Prefix(SocialPrimeHandler __instance, WheelType wheelType)
        {
            try
            {
                SocialSpriteCache.DropDeadAtlasSprites();
                PhraseInjectionService.ReapplyToWheel(__instance);
                // emoticon + emote share the EmotesAndEmoticons wheel and both mutate the same slot
                // list. restore BOTH before either injects, so neither snapshots a slot the other
                // has already dirtied — that's what leaves a stale emoticon icon stuck in an old slot.
                EmoteInjectionService.RestoreSlots(__instance);
                EmoticonInjectionService.RestoreSlots(__instance);
                EmoticonInjectionService.InjectSlots(__instance);
                EmoteInjectionService.InjectSlots(__instance);
                Plugin.Log.LogInfo("ts wheel beast");
            }
            catch (Exception ex) { Plugin.Log.LogWarning("DisplayWheel: patch failed: " + ex.Message); }
        }

        // each slot VM has both a PhraseIcon and an EmoteEmojiIcon child; in-game only the last-opened
        // wheel's icon type gets left enabled, so our other wheel's slots look blank. enable the one
        // matching the wheel we're showing on every slot VM.
        [HarmonyPostfix]
        public static void Postfix(SocialPrimeHandler __instance, WheelType wheelType)
        {
            try
            {
                string wantIcon = wheelType == WheelType.Phrases ? "PhraseIcon" : "EmoteEmojiIcon";
                foreach (var vm in __instance.GetComponentsInChildren<SocialWheelOptionViewModel>(true))
                    foreach (var t in vm.GetComponentsInChildren<Transform>(true))
                        if (t.name == wantIcon) t.gameObject.SetActive(true);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("DisplayWheel: icon enable failed: " + ex.Message); }
        }
    }
}