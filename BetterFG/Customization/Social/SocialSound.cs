using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using FGClient;
using FMODUnity;
using UnityEngine;

namespace BetterFG.Customization.Social
{
    internal static class SocialSound
    {
        private const float MinDistance = 1f;
        private const float MaxDistance = 20f;
        private static readonly System.Random Rng = new System.Random();

        internal static string Pick(string[] paths)
        {
            if (paths == null) return null;
            var found = new List<string>();
            foreach (string s in paths) if (!string.IsNullOrEmpty(s) && File.Exists(s)) found.Add(s);
            return found.Count == 0 ? null : found[Rng.Next(found.Count)];
        }

        private const string EmoteBus3D = "bus:/MASTER/BUS_SFX_Emote_3D";
        private const string EmoteBus2D = "bus:/MASTER/BUS_AUX/BUS_AUX_Emote_2D";
        private static readonly HashSet<string> _lockedBuses = new HashSet<string>();

        private static FMOD.ChannelGroup Group(string busPath)
        {
            if (busPath != null)
            {
                try
                {
                    var bus = RuntimeManager.GetBus(busPath);
                    if (_lockedBuses.Add(busPath))
                    {
                        bus.lockChannelGroup();
                        RuntimeManager.StudioSystem.flushCommands();
                    }
                    if (bus.getChannelGroup(out var group) == FMOD.RESULT.OK) return group;
                    Plugin.Log.LogWarning($"{busPath} has no channel group yet, emote sound goes to master");
                }
                catch (System.Exception ex) { Plugin.Log.LogWarning($"no {busPath}? {ex.Message}"); }
            }
            RuntimeManager.CoreSystem.getMasterChannelGroup(out var master);
            return master;
        }

        internal static FMOD.Channel Play(string path, GameObject bean, bool emote = false)
        {
            var fgcc = bean != null ? bean.GetComponent<FallGuysCharacterController>() : null;
            bool spatial = fgcc != null && !fgcc.IsLocalPlayer;
            var core = RuntimeManager.CoreSystem;
            var mode = (spatial ? FMOD.MODE._3D | FMOD.MODE._3D_LINEARSQUAREROLLOFF : FMOD.MODE._2D) | FMOD.MODE.LOOP_OFF;
            var r = core.createSound(path, mode, out FMOD.Sound sound);
            if (r != FMOD.RESULT.OK)
            {
                Plugin.Log.LogWarning($"fmod wouldn't take {Path.GetFileName(path)}: {r}");
                return default;
            }
            if (spatial) sound.set3DMinMaxDistance(MinDistance, MaxDistance);
            var group = Group(emote ? (spatial ? EmoteBus3D : EmoteBus2D) : null);
            core.playSound(sound, group, true, out FMOD.Channel ch);
            ch.setVolume(Volume());
            if (spatial) Place(ch, fgcc.transform);
            ch.setPaused(false);
            BeanMonitorService.Instance.StartCoroutine(Track(ch, sound, spatial ? fgcc.transform : null).WrapToIl2Cpp());
            return ch;
        }

        private static IEnumerator Track(FMOD.Channel ch, FMOD.Sound sound, Transform follow)
        {
            while (ch.isPlaying(out bool playing) == FMOD.RESULT.OK && playing)
            {
                if (follow != null) Place(ch, follow);
                yield return null;
            }
            sound.release();
        }

        private static void Place(FMOD.Channel ch, Transform t)
        {
            var p = t.position;
            var pos = new FMOD.VECTOR { x = p.x, y = p.y, z = p.z };
            var vel = new FMOD.VECTOR();
            ch.set3DAttributes(ref pos, ref vel);
        }

        private static float Volume()
        {
            var audio = GlobalGameStateClient.Instance?.PlayerProfile?.AudioSettings;
            return audio != null ? Mathf.Clamp01(audio.MasterVolume) * Mathf.Clamp01(audio.SFXVolume) * 0.6f : 0.6f;
        }
    }
}
