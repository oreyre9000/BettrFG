using System;
using BetterFG.Utilities;
using FGClient;
using FGClient.UI.PrivateLobby;
using HarmonyLib;
using UnityEngine;
using FgAudioSettings = FGClient.AudioSettings;

namespace BetterFG.Tweaks
{
    public class SpectatorMusicTweak : BfgTweak
    {
        public SpectatorMusicTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "spectator_music";
        public override string TweakLabel => "tweak.spectator_music";
        public override bool DefaultEnabled => true;

        public static SpectatorMusicTweak Instance { get; private set; }
        private static ClientGameManager _lastGameManager;
        private static bool _lastSpectatorMode;

        internal static bool WantsMusic => Instance != null && Instance.IsEnabled;

        void Awake()
        {
            Instance = this;
        }

        public override void EnableTweak()
        {
            Apply(true);
            ForceSpectatorMix(_lastSpectatorMode);
        }

        public override void DisableTweak()
        {
            Apply(false);
            ForceSpectatorMix(false);
        }

        public override void OnMainMenuEntered()
        {
            _lastGameManager = null;
            ForceSpectatorMix(false);
        }

        internal static void ApplyIfWanted()
        {
            var inst = Instance;
            if (inst != null && inst.IsEnabled) Apply(true);
        }

        internal static void ApplyIfWanted(ClientGameManager cgm)
        {
            if (cgm != null) _lastGameManager = cgm;
            var inst = Instance;
            if (inst != null && inst.IsEnabled)
            {
                ForceSpectatorMix(cgm != null && cgm.IsSpectatorMode);
            }
        }

        private static void Apply(bool enabled)
        {
        }


        internal static void ForceSpectatorMix(bool spectating)
        {
            _lastSpectatorMode = spectating;
            if (!WantsMusic && spectating) return;

            try
            {
                var audioManager = UnityEngine.Object.FindObjectOfType<AudioManager>();
                if (audioManager != null)
                    audioManager._spectatorMusic = WantsMusic;

                var master = AudioManager.EventMasterData;
                var param = master == null ? null : master.SpectatorMusicParam;
                if (string.IsNullOrEmpty(param)) param = "SpectatorMusic";

                float value = WantsMusic && spectating ? 1f : 0f;
                AudioManager.SetGlobalParam(param, value);

                var cgm = _lastGameManager;
                FMOD.Studio.EventInstance raw = default;
                if (cgm != null && cgm._musicInstance != null)
                {
                    var music = cgm._musicInstance;
                    music.SetParameterByName(param, value);

                    raw = music._eventInstance;
                    if (raw.isValid())
                        raw.setParameterByName(param, value, true);
                }
                ApplySpectatorFx(raw, WantsMusic && spectating);
            }
            catch (Exception ex) { Plugin.Log?.LogWarning("SpectatorMusicTweak: param failed " + ex.Message); }
        }

        private static FMOD.DSP _convolution;
        private static FMOD.ChannelGroup _convolutionGroup;

        private static void ApplySpectatorFx(FMOD.Studio.EventInstance raw, bool spectating)
        {
            try
            {
                if (_convolutionGroup.hasHandle()) _convolutionGroup.removeDSP(_convolution);
                _convolutionGroup = default;
                if (!spectating || !raw.isValid()) return;

                FMOD.ChannelGroup group;
                var groupResult = raw.getChannelGroup(out group);
                if (groupResult != FMOD.RESULT.OK)
                {
                    Plugin.Log?.LogWarning("spectator music has no channel group yet (" + groupResult + "), no muffle this time");
                    return;
                }

                if (!_convolution.hasHandle())
                {
                    FMODUnity.RuntimeManager.CoreSystem.createDSPByType(FMOD.DSP_TYPE.CONVOLUTIONREVERB, out _convolution);
                    byte[] wav = EmbeddedResourceandUnity.Bytes("BetterFG.assets.audio.spectator_ir.wav");
                    var ir = new byte[wav.Length - 42];
                    ir[0] = 2;
                    Buffer.BlockCopy(wav, 44, ir, 2, wav.Length - 44);
                    _convolution.setParameterData((int)FMOD.DSP_CONVOLUTION_REVERB.IR, ir);
                    _convolution.setParameterFloat((int)FMOD.DSP_CONVOLUTION_REVERB.WET, -20.8f);
                    _convolution.setParameterFloat((int)FMOD.DSP_CONVOLUTION_REVERB.DRY, -80f);
                    _convolution.setParameterBool((int)FMOD.DSP_CONVOLUTION_REVERB.LINKED, true);
                }

                var addResult = group.addDSP(0, _convolution);
                if (addResult == FMOD.RESULT.OK) _convolutionGroup = group;
                else Plugin.Log?.LogWarning("couldn't hang the spectator convolution on the music: " + addResult);
            }
            catch (Exception ex) { Plugin.Log?.LogWarning("SpectatorMusicTweak: spectator convolution failed " + ex.Message); }
        }
    }


    // SwitchToSpectatorMode is patched in GameStatePatches (Patch_SwitchToSpectatorMode) — that hub
    // routes into ApplyIfWanted / ForceSpectatorMix here alongside the other tweaks that care.
}
