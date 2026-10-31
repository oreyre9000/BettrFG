using HarmonyLib;
using BetterFG.Customization.Pets;
using FG.Common.Character;

namespace BetterFG.Patches
{
    // destroying the pet's own GrabController only stops IT from grabbing others - it doesn't gate
    // whether it CAN BE grabbed, and MotorFunctionBeingGrabbed.StartInvulnerablityWindow() (tried
    // first) depends on the same local-tick path that IsControlledLocally=false breaks. this goes
    // straight to the source instead: the grabbing player's own eligibility check. non-byref, plain
    // bool return - not the risky byref-instance-method shape this codebase avoids patching.
    [HarmonyPatch(typeof(MotorFunctionPlayerGrab), "CanBeGrabbed")]
    public static class PetGrabBlockPatch
    {
        [HarmonyPostfix]
        static void Postfix(FallGuysCharacterController grabbedCharacter, ref bool __result)
        {
            if (__result && PetService.IsPetFgcc(grabbedCharacter))
                __result = false;
        }
    }
}
