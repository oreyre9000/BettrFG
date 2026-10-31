using System;
using System.IO;
using System.Runtime.InteropServices;
using FMODUnity;
using NAudio.Wave;

namespace BetterFG.Utilities
{
    public sealed class HostedSong
    {
        private Func<EventInstanceReference> _host;
        private FMOD.Sound _sound;
        private FMOD.Channel _channel;
        private FMOD.ChannelGroup _group;
        private IntPtr _hostHandle;
        private IntPtr _data;

        public bool Playing { get; private set; }

        public bool Start(byte[] bytes, string ext, Func<EventInstanceReference> host)
        {
            Stop();
            if (ext == ".m4a") bytes = DecodeToWav(bytes);
            _data = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, _data, bytes.Length);
            var info = new FMOD.CREATESOUNDEXINFO { cbsize = Marshal.SizeOf<FMOD.CREATESOUNDEXINFO>(), length = (uint)bytes.Length };
            var r = RuntimeManager.CoreSystem.createSound(_data, FMOD.MODE.OPENMEMORY_POINT | FMOD.MODE.CREATESTREAM | FMOD.MODE.LOOP_NORMAL | FMOD.MODE._2D, ref info, out _sound);
            if (r != FMOD.RESULT.OK)
            {
                Plugin.Log.LogWarning($"fmod wouldn't take the song ({ext}): {r}");
                Stop();
                return false;
            }
            _host = host;
            Playing = true;
            Tick();
            return true;
        }

        private static byte[] DecodeToWav(byte[] m4a)
        {
            NAudio.MediaFoundation.MediaFoundationApi.Startup();
            using var reader = new StreamMediaFoundationReader(new MemoryStream(m4a));
            using var ms = new MemoryStream();
            WaveFileWriter.WriteWavFileToStream(ms, reader);
            return ms.ToArray();
        }

        public void SetPaused(bool paused)
        {
            if (Playing && _channel.hasHandle()) _channel.setPaused(paused);
        }

        public void Tick()
        {
            if (!Playing) return;
            var host = _host();
            var ev = host?._eventInstance ?? default;
            bool live = host != null && ev.getPlaybackState(out var state) == FMOD.RESULT.OK && state != FMOD.Studio.PLAYBACK_STATE.STOPPED;

            if (_channel.hasHandle())
            {
                if (live && ev.handle == _hostHandle) { MuteHost(true); return; }
                _channel.stop();
                _channel.clearHandle();
            }

            if (!live || ev.getChannelGroup(out _group) != FMOD.RESULT.OK) return;
            _hostHandle = ev.handle;
            RuntimeManager.CoreSystem.playSound(_sound, _group, false, out _channel);
            MuteHost(true);
            Plugin.Log.LogInfo($"song riding inside {host.EventName}");
        }

        private void MuteHost(bool mute)
        {
            _group.getNumGroups(out int groups);
            for (int i = 0; i < groups; i++) { _group.getGroup(i, out var g); g.setMute(mute); }
            _group.getNumChannels(out int channels);
            for (int i = 0; i < channels; i++)
            {
                _group.getChannel(i, out var c);
                if (c.handle != _channel.handle) c.setMute(mute);
            }
        }

        public void Stop()
        {
            if (_channel.hasHandle()) _channel.stop();
            if (_group.hasHandle()) MuteHost(false);
            if (_sound.hasHandle()) _sound.release();
            if (_data != IntPtr.Zero) Marshal.FreeHGlobal(_data);
            _data = IntPtr.Zero;
            _channel.clearHandle();
            _group.clearHandle();
            _sound.clearHandle();
            _hostHandle = IntPtr.Zero;
            _host = null;
            Playing = false;
        }
    }
}
