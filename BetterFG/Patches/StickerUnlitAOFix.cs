using HarmonyLib;
using UnityEngine;

namespace BetterFG.Patches
{
    // Epic's "unlit" decal material is genuinely unlit (no light/AO sampling in the shader at all) but
    // its RenderType tag is still "TransparentCutout" - one of the tags Unity's automatic depth+normals
    // replacement pass matches, so the flat decal quad still feeds the camera's depth/normal buffer that
    // the global Ambient Occlusion post-process reads from, and picks up AO darkening it was never meant
    // to receive. Overriding the tag to something no built-in replacement shader matches drops it out of
    // that pass (and shadow/motion-vector replacement passes) entirely, with no material edits needed.
    [HarmonyPatch(typeof(LevelEditorDecal), nameof(LevelEditorDecal.SetUnlitMode))]
    internal static class StickerUnlitAOFix
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorDecal __instance, bool unlitEnabled)
        {
            if (!unlitEnabled) return;
            var mat = __instance?._renderer != null ? __instance._renderer.sharedMaterial : null;
            if (mat == null) return;
            mat.SetOverrideTag("RenderType", "BettrFGUnlitNoAO");
            mat.renderQueue = 3000;
            Plugin.Log.LogInfo($"unlit sticker AO fix applied to {mat.name}, queue now {mat.renderQueue}");
        }
    }
}
