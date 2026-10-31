using HarmonyLib;
using UnityEngine;

namespace BetterFG.Features.CreativeExpansion
{
    [HarmonyPatch(typeof(JumpFloodOutlineRenderer), nameof(JumpFloodOutlineRenderer.IsSupportedRenderer))]
    internal static class ThemeHiddenOutlinePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Renderer r, ref bool __result)
        {
            if (__result && r != null && r.forceRenderingOff) __result = false;
        }
    }
}
