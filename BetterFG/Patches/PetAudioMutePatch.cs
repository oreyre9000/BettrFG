using System.Reflection;
using BetterFG.Customization.Pets;
using FG.Common;
using FG.Common.Audio;
using FGClient;
using HarmonyLib;

namespace BetterFG.Patches
{
    internal static class PetAudioMutePatch
    {
        static MethodInfo _target, _prefix;

        internal static void Set(bool on)
        {
            var h = Plugin.HarmonyInstance;
            if (h == null || on == (_target != null)) return;
            if (on)
            {
                _target = AccessTools.Method(typeof(AudioManager), nameof(AudioManager.CanCharacterPlayAudio));
                _prefix = AccessTools.Method(typeof(PetAudioMutePatch), nameof(Prefix));
                h.Patch(_target, prefix: new HarmonyMethod(_prefix));
                return;
            }
            h.Unpatch(_target, _prefix);
            _target = null;
        }

        static bool Prefix(FallGuysCharacterController __0, ref bool __result)
        {
            if (!PetService.IsMutedPet(__0)) return true;
            __result = false;
            return false;
        }
    }
}
