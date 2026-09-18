using System.IO;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BetterFG.Features.CreativeExpansion
{
    [HarmonyPatch(typeof(LevelEditorPublishPopupViewModel), "ConfirmPublish")]
    internal static class PublishConfirmPatch
    {
        private static bool _bypassWarnings;

        [HarmonyPrefix]
        public static bool Prefix(LevelEditorPublishPopupViewModel __instance)
        {
            if (!_bypassWarnings && PublishWarnings.TryCollect(__instance, out var body))
            {
                PublishWarnings.Show(body, () => { _bypassWarnings = true; __instance.ConfirmPublish(); });
                return false;
            }
            _bypassWarnings = false;

            PublishThumbnail.Armed = PublishThumbnail.HasChoice;
            if (!PublishThumbnail.Armed) return true;

            PublishThumbnail.PushToEditor();
            Plugin.Log.LogInfo($"publishing with {Path.GetFileName(PublishThumbnail.ChosenPath)} as the thumbnail");
            return true;
        }
    }

    [HarmonyPatch(typeof(LevelSaver), "CaptureThumbnail")]
    internal static class CaptureThumbnailBytesPatch
    {
        [HarmonyPostfix]
        public static void Postfix(bool isAutoSave, ref Il2CppStructArray<byte> __result)
        {
            if (isAutoSave || !PublishThumbnail.ShouldSwap) return;

            var probe = new Texture2D(2, 2, TextureFormat.RGB24, false);
            probe.LoadImage(__result);
            int w = probe.width;
            int h = probe.height;
            Object.Destroy(probe);

            byte[] mine = PublishThumbnail.EncodeFor(w, h, __result.Length);
            Plugin.Log.LogInfo($"thumbnail swapped for {Path.GetFileName(PublishThumbnail.ChosenPath)} at {w}x{h} — theirs was {__result.Length / 1024}kb, ours is {mine.Length / 1024}kb");
            __result = mine;
        }
    }

    [HarmonyPatch(typeof(LevelSaver), "CaptureThumbnailAsTexture")]
    internal static class CaptureThumbnailTexturePatch
    {
        [HarmonyPostfix]
        public static void Postfix(bool isAutoSave, ref Texture2D __result)
        {
            if (isAutoSave || !PublishThumbnail.ShouldSwap) return;
            __result = PublishThumbnail.Fit(__result.width, __result.height);
        }
    }
}
