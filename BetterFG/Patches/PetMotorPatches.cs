using HarmonyLib;
using BetterFG.Customization.Pets;
using FGClient;
using UnityEngine;

namespace BetterFG.Patches
{
    // SpawnBeanUtils-spawned beans aren't in ClientPlayerUpdateManager's real player roster, so
    // their FallGuysCharacterController never gets its own per-frame Update/FixedUpdate/LateUpdate
    // ticks - without this the animator never advances at all (confirmed: removing this patch when
    // PetFollowComponent stopped needing the real motor for movement also killed animation, since
    // the tick was still what drove it). chaosmod's own NPC spawn needs the identical fix
    // (Patches/ClonedBeanPatches.cs there): pump each orphaned fgcc through the same calls the
    // manager makes for every real player, once a frame, right alongside them.
    [HarmonyPatch(typeof(ClientPlayerUpdateManager))]
    public static class PetMotorPatches
    {
        // while FrozenForRoundStart, PetFollowComponent already pins each pet's rigidbody to zero -
        // but pumping the fgcc's own managed motor update here would drive it right back off that
        // pin (the round-start drop-in reconciliation feeds the motor garbage state), which is what
        // flung pets across the map at round start. skip the pump for the same short window.
        [HarmonyPatch("OnManagedFixedUpdate")]
        [HarmonyPostfix]
        static void FixedUpdatePostfix(ClientPlayerUpdateManager __instance)
        {
            var svc = PetService.Instance;
            if (svc == null || svc.FrozenForRoundStart) return;
            foreach (var fgcc in svc.LiveFgccs)
                fgcc.OnManagedFixedUpdate_Local(!Physics.autoSimulation, Physics.gravity, __instance.GameState.SimulationFixedTime);
        }

        [HarmonyPatch("OnManagedLateFixedUpdate")]
        [HarmonyPostfix]
        static void LateFixedUpdatePostfix(ClientPlayerUpdateManager __instance)
        {
            var svc = PetService.Instance;
            if (svc == null || svc.FrozenForRoundStart) return;
            foreach (var fgcc in svc.LiveFgccs)
                fgcc.OnManagedLateFixedUpdate_LocalOrServer();
        }

        [HarmonyPatch("OnManagedUpdate")]
        [HarmonyPostfix]
        static void UpdatePostfix(ClientPlayerUpdateManager __instance)
        {
            var svc = PetService.Instance;
            if (svc == null || svc.FrozenForRoundStart) return;
            foreach (var fgcc in svc.LiveFgccs)
                fgcc.OnManagedUpdate_Local(__instance.GameState.SimulationTime, __instance.GameState.SimulationFixedTime, Time.deltaTime);
        }

        [HarmonyPatch("OnManagedLateUpdate")]
        [HarmonyPostfix]
        static void LateUpdatePostfix(ClientPlayerUpdateManager __instance)
        {
            var svc = PetService.Instance;
            if (svc == null || svc.FrozenForRoundStart) return;
            foreach (var fgcc in svc.LiveFgccs)
                fgcc.OnManagedLateUpdate_Local(Physics.autoSimulation, Time.time, __instance.GameState.SimulationFixedTime);
        }
    }
}
