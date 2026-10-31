using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace BetterFG.Features.Replay
{
    internal class ReplayProcessAudioCapture
    {
        public const int SampleRate = 48000;
        public const int Channels = 2;
        public const int Bits = 16;
        const int BytesPerSecond = SampleRate * Channels * (Bits / 8);

        const string VirtualDevice = "VAD\\Process_Loopback";
        const int ShareModeShared = 0;
        const int StreamFlagsLoopback = 0x00020000;
        const int StreamFlagsEventCallback = 0x00040000;
        const long BufferHns = 2000000;

        static readonly Guid IID_IAudioClient = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
        static readonly Guid IID_IAudioCaptureClient = new Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317");

        [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IActivateAudioInterfaceAsyncOperation
        {
            void GetActivateResult([MarshalAs(UnmanagedType.Error)] out int activateResult,
                [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
        }

        [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IActivateAudioInterfaceCompletionHandler
        {
            void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation);
        }

        [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioClient
        {
            [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
            [PreserveSig] int GetBufferSize(out uint bufferFrames);
            [PreserveSig] int GetStreamLatency(out long latency);
            [PreserveSig] int GetCurrentPadding(out uint padding);
            [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, IntPtr closest);
            [PreserveSig] int GetMixFormat(out IntPtr format);
            [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
            [PreserveSig] int Start();
            [PreserveSig] int Stop();
            [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr handle);
            [PreserveSig] int GetService([MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        }

        [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioCaptureClient
        {
            [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out long devicePosition, out long qpcPosition);
            [PreserveSig] int ReleaseBuffer(uint frames);
            [PreserveSig] int GetNextPacketSize(out uint frames);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ActivationParams
        {
            public int ActivationType;
            public uint TargetProcessId;
            public int LoopbackMode;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BlobPropVariant
        {
            public short vt;
            public short reserved1;
            public short reserved2;
            public short reserved3;
            public int cbSize;
            public int padding;
            public IntPtr pBlobData;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct WaveFormatEx
        {
            public short wFormatTag;
            public short nChannels;
            public int nSamplesPerSec;
            public int nAvgBytesPerSec;
            public short nBlockAlign;
            public short wBitsPerSample;
            public short cbSize;
        }

        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
        static extern void ActivateAudioInterfaceAsync(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            IntPtr activationParams,
            IActivateAudioInterfaceCompletionHandler completionHandler,
            out IActivateAudioInterfaceAsyncOperation operation);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateEventW(IntPtr attributes, bool manualReset, bool initialState, IntPtr name);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr handle);

        class Handler : IActivateAudioInterfaceCompletionHandler
        {
            public readonly ManualResetEvent Done = new ManualResetEvent(false);
            public IActivateAudioInterfaceAsyncOperation Operation;

            public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
            {
                Operation = operation;
                Done.Set();
            }
        }

        Thread _thread;
        FileStream _file;
        volatile bool _stop;
        volatile bool _finished;
        volatile bool _started;
        volatile string _error;
        long _bytes;

        public bool Finished => _finished;
        public bool Started => _started;
        public string Error => _error;
        public float Seconds => _bytes / (float)BytesPerSecond;

        public bool Start(string path)
        {
            try
            {
                _file = new FileStream(path, FileMode.Create, FileAccess.Write);
                WriteHeader(0);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"couldn't open {path} for the audio pass: {ex.Message}");
                return false;
            }

            _thread = new Thread(Run) { IsBackground = true, Name = "BettrFG_ReplayAudio" };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();

            for (int i = 0; i < 200 && !_started && _error == null && !_finished; i++) Thread.Sleep(10);
            if (_error == null) return true;

            Plugin.Log.LogWarning($"process loopback wouldn't start ({_error}), the export goes out silent");
            return false;
        }

        public void Stop() => _stop = true;

        void Run()
        {
            IntPtr blob = IntPtr.Zero;
            IntPtr variant = IntPtr.Zero;
            IntPtr format = IntPtr.Zero;
            IntPtr sampleReady = IntPtr.Zero;
            IAudioClient client = null;
            IAudioCaptureClient capture = null;

            try
            {
                var parameters = new ActivationParams
                {
                    ActivationType = 1,
                    TargetProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id,
                    LoopbackMode = 0,
                };
                blob = Marshal.AllocHGlobal(Marshal.SizeOf<ActivationParams>());
                Marshal.StructureToPtr(parameters, blob, false);

                var propVariant = new BlobPropVariant
                {
                    vt = 65,
                    cbSize = Marshal.SizeOf<ActivationParams>(),
                    pBlobData = blob,
                };
                variant = Marshal.AllocHGlobal(Marshal.SizeOf<BlobPropVariant>());
                Marshal.StructureToPtr(propVariant, variant, false);

                var handler = new Handler();
                ActivateAudioInterfaceAsync(VirtualDevice, IID_IAudioClient, variant, handler, out _);

                if (!handler.Done.WaitOne(4000)) throw new Exception("activation timed out");
                handler.Operation.GetActivateResult(out int hr, out object activated);
                if (hr != 0 || activated == null) throw new Exception("activation returned 0x" + hr.ToString("X8"));

                client = (IAudioClient)activated;

                var wave = new WaveFormatEx
                {
                    wFormatTag = 1,
                    nChannels = Channels,
                    nSamplesPerSec = SampleRate,
                    nAvgBytesPerSec = BytesPerSecond,
                    nBlockAlign = Channels * (Bits / 8),
                    wBitsPerSample = Bits,
                    cbSize = 0,
                };
                format = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
                Marshal.StructureToPtr(wave, format, false);

                Check(client.Initialize(ShareModeShared, StreamFlagsLoopback | StreamFlagsEventCallback,
                    BufferHns, 0, format, IntPtr.Zero), "Initialize");

                sampleReady = CreateEventW(IntPtr.Zero, false, false, IntPtr.Zero);
                Check(client.SetEventHandle(sampleReady), "SetEventHandle");
                Check(client.GetService(IID_IAudioCaptureClient, out object service), "GetService");
                capture = (IAudioCaptureClient)service;

                Check(client.Start(), "Start");
                _started = true;
                Plugin.Log.LogInfo($"capturing the game's own mix, {SampleRate}Hz {Channels}ch");

                var scratch = new byte[BytesPerSecond];
                while (!_stop)
                {
                    WaitForSingleObject(sampleReady, 200);

                    while (true)
                    {
                        if (capture.GetNextPacketSize(out uint packet) != 0 || packet == 0) break;
                        if (capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _) != 0) break;

                        int size = (int)frames * Channels * (Bits / 8);
                        if (size > 0 && size <= scratch.Length)
                        {
                            if ((flags & 2) != 0 || data == IntPtr.Zero) Array.Clear(scratch, 0, size);
                            else Marshal.Copy(data, scratch, 0, size);

                            _file.Write(scratch, 0, size);
                            _bytes += size;
                        }
                        capture.ReleaseBuffer(frames);
                    }
                }

                client.Stop();
            }
            catch (Exception ex) { _error = ex.Message; }
            finally
            {
                if (capture != null) Marshal.ReleaseComObject(capture);
                if (client != null) Marshal.ReleaseComObject(client);
                if (sampleReady != IntPtr.Zero) CloseHandle(sampleReady);
                if (format != IntPtr.Zero) Marshal.FreeHGlobal(format);
                if (variant != IntPtr.Zero) Marshal.FreeHGlobal(variant);
                if (blob != IntPtr.Zero) Marshal.FreeHGlobal(blob);

                try
                {
                    if (_file != null)
                    {
                        _file.Flush();
                        _file.Seek(0, SeekOrigin.Begin);
                        WriteHeader((int)_bytes);
                        _file.Dispose();
                        _file = null;
                    }
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"the capture wav wouldn't close cleanly: {ex.Message}"); }

                _finished = true;
            }
        }

        static void Check(int hr, string what)
        {
            if (hr != 0) throw new Exception(what + " returned 0x" + hr.ToString("X8"));
        }

        void WriteHeader(int dataBytes)
        {
            var w = new BinaryWriter(_file, System.Text.Encoding.ASCII, true);
            w.Write(new char[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + dataBytes);
            w.Write(new char[] { 'W', 'A', 'V', 'E' });
            w.Write(new char[] { 'f', 'm', 't', ' ' });
            w.Write(16);
            w.Write((short)1);
            w.Write((short)Channels);
            w.Write(SampleRate);
            w.Write(BytesPerSecond);
            w.Write((short)(Channels * (Bits / 8)));
            w.Write((short)Bits);
            w.Write(new char[] { 'd', 'a', 't', 'a' });
            w.Write(dataBytes);
            w.Flush();
        }
    }
}
