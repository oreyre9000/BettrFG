using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Social;
using BetterFG.Utilities;
using FG.Common.Character;
using FG.Common.Character.MotorSystem;
using UnityEngine;

namespace BetterFG.Customization.Pets
{
    // makes live pets mirror the local player's emote a moment later. rides the same MotorTaskEmote
    // request real players use (same shape as PetFollowComponent's jump/grab tasks) so a vanilla emote
    // plays through the pet's own real animator state, not a hand-rolled clone.
    internal static class PetEmoteEcho
    {
        const float MinDelay = 0f;
        const float MaxDelay = 2f;
        // a real player's emote gets cancelled because MotorFunctionMovement contends for the same
        // resource the moment they walk. a pet's own follow logic drives its Rigidbody directly and
        // never goes through that task system (see PetFollowComponent's header), so vanilla emotes on
        // a pet would never get pre-empted on their own - poll and force it off ourselves instead.
        const float MoveCancelThresholdSqr = 0.5f;
        const float MaxEmoteDuration = 4f;

        public static void Echo(MotorAgent emoterAgent, int emoteIndex, EmotesOption emoteOption, IEnumerable<FallGuysCharacterController> pets)
        {
            if (!PatchGate.RoundLive || pets == null) return;

            MotorFunctionEmote emoterEf = null;
            try { emoterEf = emoterAgent?.GetMotorFunction<MotorFunctionEmote>(); } catch { }

            foreach (var fgcc in pets)
            {
                if (fgcc == null) continue;
                var agent = fgcc.MotorAgent;
                if (agent == null) continue;
                float delay = UnityEngine.Random.Range(MinDelay, MaxDelay);
                fgcc.StartCoroutine(EchoAfterDelay(fgcc, agent, emoteIndex, emoterEf, delay).WrapToIl2Cpp());
            }
        }

        static IEnumerator EchoAfterDelay(FallGuysCharacterController petFgcc, MotorAgent agent, int emoteIndex, MotorFunctionEmote localEf, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (petFgcc == null || petFgcc.m_CachedPtr == IntPtr.Zero || agent == null) yield break;

            MotorFunctionEmote ef;
            try { ef = agent.GetMotorFunction<MotorFunctionEmote>(); } catch { yield break; }
            if (ef == null) yield break;

            // synthetic pet bean may never have had a real emote loadout applied to it - hand it the
            // emoter's own selection so _emoteOptions[emoteIndex] actually resolves to something.
            try { if (localEf != null) ef.UpdateEmoteOption(localEf._emoteOptions); } catch { }

            MotorTaskEmote task;
            try { task = agent.MotorTasks?.GetTask<MotorTaskEmote>(); } catch { task = null; }
            if (task == null) yield break;

            try { ef.emoteVariation = emoteIndex; task.emoteVariation = emoteIndex; task.isRequested = true; }
            catch (Exception ex) { Plugin.Log.LogWarning($"pet emote echo: request failed: {ex.Message}"); yield break; }
            PetService.AllowEmoteAudio(petFgcc, MaxEmoteDuration);

            // a custom clip (EmoteInjectionService.PlayClipOnBean) already tears itself down on
            // movement via its own velocity poll, keyed off this same agent's fgcc - nothing more to
            // do here. only vanilla emotes need our own watchdog.
            if (EmoteInjectionService.CustomClips.ContainsKey(emoteIndex)) yield break;

            float elapsed = 0f;
            while (petFgcc != null && petFgcc.m_CachedPtr != IntPtr.Zero)
            {
                if (!task.isRequested) yield break;
                bool moved = petFgcc.RigidBody != null && petFgcc.RigidBody.velocity.sqrMagnitude > MoveCancelThresholdSqr;
                elapsed += Time.deltaTime;
                if (moved || elapsed >= MaxEmoteDuration)
                {
                    PetService.AllowEmoteAudio(petFgcc, 0f);
                    try
                    {
                        var emoteState = ef._originalStates != null && ef._originalStates.Length > 1
                            ? ef._originalStates[1]?.TryCast<MotorFunctionEmoteStateEmote>() : null;
                        EmoteInjectionService.ResetEmote(agent, emoteState);
                        task.isRequested = false;
                    }
                    catch { }
                    yield break;
                }
                yield return null;
            }
        }
    }
}
