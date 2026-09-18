using System;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace BetterFG.Utilities
{
    internal static class FmodUtil
    {
        public static int StopSnapshots(Func<string, bool> match)
            => StopInstances((_, path) => path.StartsWith("snapshot:/") && (match == null || match(path)), FMOD.Studio.STOP_MODE.IMMEDIATE);

        public static int StopLoopingEvents()
            => StopInstances((description, path) => path.StartsWith("event:/") && description.isOneshot(out bool oneshot) == RESULT.OK && !oneshot, FMOD.Studio.STOP_MODE.ALLOWFADEOUT);

        private static int StopInstances(Func<EventDescription, string, bool> match, FMOD.Studio.STOP_MODE mode)
        {
            int stopped = 0;
            try
            {
                var studio = RuntimeManager.StudioSystem;
                if (!studio.isValid()) return 0;
                if (studio.getBankList(out Il2CppStructArray<Bank> banks) != RESULT.OK || banks == null) return 0;

                foreach (var bank in banks)
                {
                    if (!bank.isValid()) continue;
                    if (bank.getEventList(out Il2CppStructArray<EventDescription> descriptions) != RESULT.OK || descriptions == null) continue;

                    foreach (var description in descriptions)
                    {
                        if (!description.isValid()) continue;
                        if (description.getPath(out string path) != RESULT.OK) continue;
                        if (!match(description, path)) continue;
                        if (description.getInstanceList(out Il2CppStructArray<EventInstance> instances) != RESULT.OK || instances == null) continue;

                        foreach (var instance in instances)
                        {
                            if (!instance.isValid()) continue;
                            instance.getPlaybackState(out PLAYBACK_STATE state);
                            if (state == PLAYBACK_STATE.STOPPED || state == PLAYBACK_STATE.STOPPING) continue;

                            instance.stop(mode);
                            stopped++;
                        }
                    }
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"couldn't sweep fmod instances: {ex.Message}"); }
            return stopped;
        }

        public static void SetBusVolume(string path, float volume)
        {
            try
            {
                var bus = RuntimeManager.GetBus(path);
                if (bus.isValid()) bus.setVolume(volume);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"couldn't set volume on {path}: {ex.Message}"); }
        }

        public static void UnmuteBus(string path)
        {
            try
            {
                var bus = RuntimeManager.GetBus(path);
                if (!bus.isValid()) return;

                bus.getMute(out bool muted);
                bus.getPaused(out bool paused);
                bus.getVolume(out float volume, out float _);

                if (muted) bus.setMute(false);
                if (paused) bus.setPaused(false);
                if (volume <= 0.001f) bus.setVolume(1f);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"couldn't unmute {path}: {ex.Message}"); }
        }
    }
}
