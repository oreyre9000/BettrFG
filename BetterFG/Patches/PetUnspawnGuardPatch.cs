using HarmonyLib;
using BetterFG.Customization.Pets;
using FG.Common.Character;

namespace BetterFG.Patches
{
    // the pet spawns through the real networked path, so it's a registered net object - but its fgcc
    // is deliberately gutted (no _netMotorAgentState, no ConnectionToServer, input + GrabController
    // stripped). when the server's own reconciliation unspawns the pet mid-round, the game's
    // per-character _handleClientUnspawnNetObject walks that missing state and NREs. it throws before
    // MPGNetObjectManager finishes, so the net object table keeps a dangling entry and then every
    // later unspawn - a player qualifying, you leaving, a respawn - NREs on that same entry and the
    // round softlocks. skip the character-side handler for our own pet; the manager's table cleanup
    // after it still runs.
    [HarmonyPatch(typeof(FallGuysCharacterController), "_handleClientUnspawnNetObject")]
    public static class PetUnspawnGuardPatch
    {
        [HarmonyPrefix]
        static bool Prefix(FallGuysCharacterController __instance)
        {
            return !PetService.IsPetFgcc(__instance);
        }
    }
}
