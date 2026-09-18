using BetterFG.Customization.TexturePacks;
using HarmonyLib;
using UnityEngine.UI;

namespace BetterFG.Patches
{
    [HarmonyPatch(typeof(Image), nameof(Image.sprite), MethodType.Setter)]
    internal static class TexturePackImageSpritePatch
    {
        [HarmonyPostfix]
        static void Postfix(Image __instance) => TexturePackRuntimeService.TryReplaceSprite(__instance);
    }

    [HarmonyPatch(typeof(RawImage), nameof(RawImage.texture), MethodType.Setter)]
    internal static class TexturePackRawImageTexturePatch
    {
        [HarmonyPostfix]
        static void Postfix(RawImage __instance) => TexturePackRuntimeService.TryReplaceRawTexture(__instance);
    }
}
