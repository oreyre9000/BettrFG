using BetterFG.Customization.TexturePacks;
using HarmonyLib;
using UnityEngine.UI;

namespace BetterFG.Patches
{
    [Utilities.BfgPatchGate(TexturePackRuntimeService.GateKey)]
    [HarmonyPatch(typeof(Image), nameof(Image.sprite), MethodType.Setter)]
    internal static class TexturePackImageSpritePatch
    {
        [HarmonyPostfix]
        static void Postfix(Image __instance) => TexturePackRuntimeService.TryReplaceSprite(__instance);
    }

    [Utilities.BfgPatchGate(TexturePackRuntimeService.GateKey)]
    [HarmonyPatch(typeof(Image), "OnEnable")]
    internal static class TexturePackImageEnablePatch
    {
        [HarmonyPostfix]
        static void Postfix(Image __instance) => TexturePackRuntimeService.TryReplaceSprite(__instance);
    }

    [Utilities.BfgPatchGate(TexturePackRuntimeService.GateKey)]
    [HarmonyPatch(typeof(RawImage), nameof(RawImage.texture), MethodType.Setter)]
    internal static class TexturePackRawImageTexturePatch
    {
        [HarmonyPostfix]
        static void Postfix(RawImage __instance) => TexturePackRuntimeService.TryReplaceRawTexture(__instance);
    }
}
