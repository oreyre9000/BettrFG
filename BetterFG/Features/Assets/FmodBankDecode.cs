using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace BetterFG.Features.Assets
{
    public class FmodSample
    {
        public string Name;
        public byte[] Pcm;
        public int Channels;
        public int Rate;
        public int Bits;
        public bool Float;

        public float Seconds
        {
            get
            {
                int blockAlign = Channels * Bits / 8;
                return blockAlign <= 0 || Rate <= 0 ? 0f : Pcm.Length / (float)(blockAlign * Rate);
            }
        }
    }

    public static class FmodBankDecode
    {
        private const string DLL = "fmodstudio";
        private const uint FMOD_VERSION = 0x00020209;

        private const uint MODE_LOOP_OFF = 0x00000001;
        private const uint MODE_2D = 0x00000008;
        private const uint MODE_CREATESAMPLE = 0x00000100;
        private const uint MODE_IGNORETAGS = 0x02000000;
        private const uint TIMEUNIT_PCM = 0x00000002;
        private const int OUTPUT_NOSOUND_NRT = 4;

        [DllImport(DLL)] private static extern int FMOD_System_Create(out IntPtr system, uint headerVersion);
        [DllImport(DLL)] private static extern int FMOD_System_SetOutput(IntPtr system, int output);
        [DllImport(DLL)] private static extern int FMOD_System_Init(IntPtr system, int maxChannels, uint flags, IntPtr extra);
        [DllImport(DLL)] private static extern int FMOD_System_Close(IntPtr system);
        [DllImport(DLL)] private static extern int FMOD_System_Release(IntPtr system);
        [DllImport(DLL, CharSet = CharSet.Ansi)]
        private static extern int FMOD_System_CreateSound(IntPtr system, string path, uint mode, IntPtr exinfo, out IntPtr sound);
        [DllImport(DLL)] private static extern int FMOD_Sound_Release(IntPtr sound);
        [DllImport(DLL)] private static extern int FMOD_Sound_GetNumSubSounds(IntPtr sound, out int num);
        [DllImport(DLL)] private static extern int FMOD_Sound_GetSubSound(IntPtr sound, int index, out IntPtr sub);
        [DllImport(DLL)] private static extern int FMOD_Sound_GetFormat(IntPtr sound, out int type, out int format, out int channels, out int bits);
        [DllImport(DLL)] private static extern int FMOD_Sound_GetDefaults(IntPtr sound, out float frequency, out int priority);
        [DllImport(DLL)] private static extern int FMOD_Sound_GetLength(IntPtr sound, out uint length, uint lengthType);
        [DllImport(DLL)] private static extern int FMOD_Sound_Lock(IntPtr sound, uint offset, uint length,
            out IntPtr ptr1, out IntPtr ptr2, out uint len1, out uint len2);
        [DllImport(DLL)] private static extern int FMOD_Sound_Unlock(IntPtr sound, IntPtr ptr1, IntPtr ptr2, uint len1, uint len2);
        [DllImport(DLL, CharSet = CharSet.Ansi)]
        private static extern int FMOD_Sound_GetName(IntPtr sound, StringBuilder name, int nameLen);

        public static int FindFsb(byte[] bank)
        {
            for (int i = 0; i + 4 <= bank.Length; i++)
                if (bank[i] == 'F' && bank[i + 1] == 'S' && bank[i + 2] == 'B' && bank[i + 3] == '5')
                    return i;
            return -1;
        }

        // FMOD owns the FSB5/vorbis format, and the game's own fmodstudio.dll is already in the process,
        // so decoding goes through it instead of shipping a third-party codec. the sound is opened from
        // a temp file rather than memory purely to avoid FMOD_CREATESOUNDEXINFO, whose layout has to
        // match the exact FMOD build or the call is rejected.
        public static List<FmodSample> Read(byte[] bank, string baseName, out string error)
        {
            error = null;
            var samples = new List<FmodSample>();

            int at = FindFsb(bank);
            if (at < 0) { error = "no FSB5 chunk in that bank"; return samples; }

            string temp = Path.Combine(Path.GetTempPath(), "bettrfg_" + Guid.NewGuid().ToString("N") + ".fsb");
            File.WriteAllBytes(temp, Sub(bank, at));

            IntPtr system = IntPtr.Zero, sound = IntPtr.Zero;
            try
            {
                int r = FMOD_System_Create(out system, FMOD_VERSION);
                if (r != 0) { error = "fmod system create failed (" + r + ")"; return samples; }

                FMOD_System_SetOutput(system, OUTPUT_NOSOUND_NRT);
                r = FMOD_System_Init(system, 64, 0u, IntPtr.Zero);
                if (r != 0) { error = "fmod init failed (" + r + ")"; return samples; }

                r = FMOD_System_CreateSound(system, temp, MODE_CREATESAMPLE | MODE_LOOP_OFF | MODE_2D | MODE_IGNORETAGS,
                    IntPtr.Zero, out sound);
                if (r != 0) { error = "fmod couldn't open the fsb (" + r + ")"; return samples; }

                FMOD_Sound_GetNumSubSounds(sound, out int subs);

                if (subs <= 0)
                {
                    var one = ReadOne(sound, baseName);
                    if (one != null) samples.Add(one);
                }
                else
                {
                    for (int i = 0; i < subs; i++)
                    {
                        if (FMOD_Sound_GetSubSound(sound, i, out IntPtr sub) != 0 || sub == IntPtr.Zero) continue;

                        var nameBuf = new StringBuilder(256);
                        string name = FMOD_Sound_GetName(sub, nameBuf, 256) == 0 && nameBuf.Length > 0
                            ? nameBuf.ToString()
                            : baseName + "_" + i;
                        if (subs == 1) name = baseName;

                        var s = ReadOne(sub, Safe(name));
                        if (s != null) samples.Add(s);
                    }
                }
            }
            catch (Exception ex) { error = ex.Message; }
            finally
            {
                if (sound != IntPtr.Zero) FMOD_Sound_Release(sound);
                if (system != IntPtr.Zero) { FMOD_System_Close(system); FMOD_System_Release(system); }
                try { File.Delete(temp); } catch { }
            }

            return samples;
        }

        // TIMEUNIT_PCMBYTES lies on these banks (it reports the same constant for every subsound), so the
        // byte count is derived from the PCM sample count instead. and a CREATESAMPLE sound is already
        // decoded in memory, which is Lock's job to hand over - ReadData only serves OPENONLY sounds.
        private static FmodSample ReadOne(IntPtr sound, string name)
        {
            if (FMOD_Sound_GetFormat(sound, out _, out int format, out int channels, out int bits) != 0) return null;
            if (FMOD_Sound_GetLength(sound, out uint samples, TIMEUNIT_PCM) != 0 || samples == 0) return null;
            FMOD_Sound_GetDefaults(sound, out float freq, out _);

            if (channels <= 0) channels = 1;
            if (bits <= 0) bits = 32;
            long want = (long)samples * channels * (bits / 8);
            if (want <= 0 || want > int.MaxValue) return null;

            if (FMOD_Sound_Lock(sound, 0u, (uint)want, out IntPtr p1, out IntPtr p2, out uint l1, out uint l2) != 0)
                return null;

            byte[] pcm;
            try
            {
                pcm = new byte[l1 + l2];
                if (l1 > 0) Marshal.Copy(p1, pcm, 0, (int)l1);
                if (l2 > 0 && p2 != IntPtr.Zero) Marshal.Copy(p2, pcm, (int)l1, (int)l2);
            }
            finally { FMOD_Sound_Unlock(sound, p1, p2, l1, l2); }

            if (pcm.Length == 0) return null;

            return new FmodSample
            {
                Name = name,
                Pcm = pcm,
                Channels = channels,
                Rate = (int)freq <= 0 ? 48000 : (int)freq,
                Bits = bits,
                Float = format == 5,
            };
        }

        public static string WriteMp3(FmodSample s, string outDir, out string error)
        {
            error = null;
            Directory.CreateDirectory(outDir);
            string wav = Path.Combine(outDir, s.Name + ".wav");
            WriteWav16(wav, s);

            string mp3 = Path.Combine(outDir, s.Name + ".mp3");
            try
            {
                NAudio.MediaFoundation.MediaFoundationApi.Startup();
                using (var reader = new NAudio.Wave.WaveFileReader(wav))
                    NAudio.Wave.MediaFoundationEncoder.EncodeToMp3(reader, mp3, 192000);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Plugin.Log.LogWarning($"mp3 encode of {s.Name} didn't take: {ex.Message}. wav is still there");
                return wav;
            }

            try { File.Delete(wav); } catch { }
            return mp3;
        }

        // 16-bit PCM is what every consumer of these files actually wants: Unity's wav loader and
        // Media Foundation both take it without argument, where float32 is hit and miss.
        public static void WriteWav16(string path, FmodSample s)
        {
            var f = ToFloats(s);
            var pcm = new byte[f.Length * 2];
            for (int i = 0; i < f.Length; i++)
            {
                short v = (short)Mathf.Clamp(Mathf.RoundToInt(f[i] * 32767f), -32768, 32767);
                pcm[i * 2] = (byte)(v & 0xFF);
                pcm[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
            }
            WriteRiff(path, pcm, s.Channels, s.Rate, 16, false);
        }

        public static void WriteWav(string path, FmodSample s)
            => WriteRiff(path, s.Pcm, s.Channels, s.Rate, s.Bits, s.Float);

        private static void WriteRiff(string path, byte[] pcm, int channels, int rate, int bits, bool floatPcm)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var w = new BinaryWriter(fs))
            {
                int blockAlign = channels * bits / 8;
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + pcm.Length);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);
                w.Write((short)(floatPcm ? 3 : 1));
                w.Write((short)channels);
                w.Write(rate);
                w.Write(rate * blockAlign);
                w.Write((short)blockAlign);
                w.Write((short)bits);
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(pcm.Length);
                w.Write(pcm);
            }
        }

        public static float[] ToFloats(FmodSample s)
        {
            if (s.Float)
            {
                var f = new float[s.Pcm.Length / 4];
                Buffer.BlockCopy(s.Pcm, 0, f, 0, f.Length * 4);
                return f;
            }
            if (s.Bits == 8)
            {
                var f = new float[s.Pcm.Length];
                for (int i = 0; i < f.Length; i++) f[i] = (s.Pcm[i] - 128) / 128f;
                return f;
            }
            if (s.Bits == 32)
            {
                var f = new float[s.Pcm.Length / 4];
                for (int i = 0; i < f.Length; i++) f[i] = BitConverter.ToInt32(s.Pcm, i * 4) / 2147483648f;
                return f;
            }
            var o = new float[s.Pcm.Length / 2];
            for (int i = 0; i < o.Length; i++) o[i] = BitConverter.ToInt16(s.Pcm, i * 2) / 32768f;
            return o;
        }

        private static byte[] Sub(byte[] src, int from)
        {
            var dst = new byte[src.Length - from];
            Buffer.BlockCopy(src, from, dst, 0, dst.Length);
            return dst;
        }

        private static string Safe(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' ? c : '_');
            return sb.Length == 0 ? "sound" : sb.ToString();
        }
    }
}
