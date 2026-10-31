using System;
using HarmonyLib;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class DisableInstructionControlsTweak : BfgTweak
    {
        public DisableInstructionControlsTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "disable_instruction_controls";
        public override string TweakLabel => "Disable Instruction Controls";
        public override bool DefaultEnabled => false;
        public override string TweakTooltip => "Disables the controls written at the top left of the screen in-game.";
    }

    [Utilities.BfgPatchGate("tweak.disable_instruction_controls")]
    [HarmonyPatch(typeof(FGClient.UI.InGameUiManager), nameof(FGClient.UI.InGameUiManager.Initialise))]
    internal static class HideInstructionControlsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(FGClient.UI.InGameUiManager __instance)
        {
            foreach (var t in __instance.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "TipsContainer") continue;
                for (int i = 0; i < t.childCount; i++)
                {
                    var child = t.GetChild(i);
                    if (child.name != "SkipRound") child.gameObject.SetActive(false);
                }
            }
        }
    }
}
