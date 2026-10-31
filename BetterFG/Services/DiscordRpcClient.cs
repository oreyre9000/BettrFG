using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace BetterFG.Services
{
    internal static class DiscordRpcClient
    {
        public readonly struct Activity : IEquatable<Activity>
        {
            public readonly string Details;
            public readonly string State;
            public readonly string LargeImage;
            public readonly string LargeText;
            public readonly string SmallImage;
            public readonly string SmallText;
            public readonly long StartUnix;
            public readonly int PartySize;
            public readonly int PartyMax;

            public Activity(string details, string state, string largeImage, string largeText,
                string smallImage, string smallText, long startUnix, int partySize, int partyMax)
            {
                Details = details;
                State = state;
                LargeImage = largeImage;
                LargeText = largeText;
                SmallImage = smallImage;
                SmallText = smallText;
                StartUnix = startUnix;
                PartySize = partySize;
                PartyMax = partyMax;
            }

            public bool Equals(Activity o) =>
                Details == o.Details && State == o.State && LargeImage == o.LargeImage &&
                LargeText == o.LargeText && SmallImage == o.SmallImage && SmallText == o.SmallText &&
                StartUnix == o.StartUnix && PartySize == o.PartySize && PartyMax == o.PartyMax;

            public override bool Equals(object o) => o is Activity a && Equals(a);
            public override int GetHashCode() => HashCode.Combine(Details, State, LargeImage, LargeText,
                SmallImage, SmallText, StartUnix, HashCode.Combine(PartySize, PartyMax));
        }

        private const int OpHandshake = 0;
        private const int OpFrame = 1;
        private const int OpClose = 2;

        private const long RateLimitMs = 4000;
        private const long ReconnectMs = 60000;

        private static readonly object _gate = new object();
        private static readonly object _writeGate = new object();
        private static readonly ManualResetEventSlim _wake = new ManualResetEventSlim(false);
        private static readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);

        private static string _clientId;
        private static NamedPipeClientStream _pipe;
        private static Thread _writer;
        private static Thread _reader;
        private static volatile bool _stop;

        private static Activity _pending;
        private static bool _hasPending;
        private static Activity _lastSent;
        private static bool _hasLastSent;
        private static long _lastSendTick;
        private static long _lastConnectTick = long.MinValue / 2;

        public static void Start(string clientId)
        {
            lock (_gate)
            {
                if (_writer != null && _writer.IsAlive) return;
                _clientId = clientId;
                _stop = false;
                _hasLastSent = false;
                _writer = new Thread(WriterLoop) { IsBackground = true, Name = "BettrFG Discord RPC" };
                _writer.Start();
            }
        }

        public static void Stop()
        {
            lock (_gate)
            {
                _stop = true;
                _hasPending = false;
            }
            _wake.Set();

            var pipe = _pipe;
            if (pipe != null && pipe.IsConnected)
            {
                using var buffer = new MemoryStream();
                using (var w = new Utf8JsonWriter(buffer))
                {
                    w.WriteStartObject();
                    w.WriteString("cmd", "SET_ACTIVITY");
                    w.WriteString("nonce", Guid.NewGuid().ToString());
                    w.WriteStartObject("args");
                    w.WriteNumber("pid", Environment.ProcessId);
                    w.WriteEndObject();
                    w.WriteEndObject();
                }
                Write(OpFrame, buffer.ToArray());
                Plugin.Log.LogInfo("told discord to drop the presence");
            }

            Teardown();
        }

        public static void Set(Activity activity)
        {
            lock (_gate)
            {
                if (_stop) return;
                _pending = activity;
                _hasPending = true;
            }
            _wake.Set();
        }

        private static void WriterLoop()
        {
            while (!_stop)
            {
                _wake.Wait();
                _wake.Reset();
                if (_stop) break;

                long wait = RateLimitMs - (Environment.TickCount64 - _lastSendTick);
                if (wait > 0) Thread.Sleep((int)wait);
                if (_stop) break;

                Activity activity;
                lock (_gate)
                {
                    if (!_hasPending) continue;
                    activity = _pending;
                    _hasPending = false;
                }
                if (_hasLastSent && activity.Equals(_lastSent)) continue;

                if (!EnsureConnected())
                {
                    lock (_gate) { if (!_hasPending) { _pending = activity; _hasPending = true; } }
                    continue;
                }

                if (!Write(OpFrame, SetActivityFrame(activity)))
                {
                    Teardown();
                    continue;
                }

                _lastSent = activity;
                _hasLastSent = true;
                _lastSendTick = Environment.TickCount64;
            }

            Teardown();
        }

        private static bool EnsureConnected()
        {
            if (_pipe != null && _pipe.IsConnected) return true;
            Teardown();

            if (Environment.TickCount64 - _lastConnectTick < ReconnectMs) return false;
            _lastConnectTick = Environment.TickCount64;

            for (int i = 0; i < 10; i++)
            {
                NamedPipeClientStream pipe = null;
                try
                {
                    pipe = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                    pipe.Connect(200);
                    _pipe = pipe;

                    if (!Write(OpHandshake, Encoding.UTF8.GetBytes($"{{\"v\":1,\"client_id\":\"{_clientId}\"}}")))
                    {
                        Teardown();
                        continue;
                    }

                    _reader = new Thread(ReaderLoop) { IsBackground = true, Name = "BettrFG Discord RPC reader" };
                    _reader.Start();

                    if (!_ready.Wait(3000))
                    {
                        Plugin.Log.LogWarning($"pipe {i} took the handshake but never said READY, is the client id right?");
                        Teardown();
                        continue;
                    }

                    Plugin.Log.LogInfo($"discord rpc up on pipe {i}");
                    return true;
                }
                catch
                {
                    try { pipe?.Dispose(); } catch { }
                    if (_pipe == pipe) _pipe = null;
                }
            }

            Plugin.Log.LogInfo("no discord-ipc pipe answered, presence is off until the next round or menu");
            return false;
        }

        private static bool Write(int opcode, byte[] payload)
        {
            var pipe = _pipe;
            if (pipe == null) return false;
            try
            {
                var frame = new byte[8 + payload.Length];
                BitConverter.GetBytes(opcode).CopyTo(frame, 0);
                BitConverter.GetBytes(payload.Length).CopyTo(frame, 4);
                payload.CopyTo(frame, 8);
                lock (_writeGate)
                {
                    pipe.Write(frame, 0, frame.Length);
                    pipe.Flush();
                }
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"discord pipe write died: {ex.Message}");
                return false;
            }
        }

        private static void ReaderLoop()
        {
            var pipe = _pipe;
            var header = new byte[8];
            while (!_stop && pipe != null && pipe.IsConnected)
            {
                try
                {
                    if (!ReadExact(pipe, header, 8)) break;
                    int opcode = BitConverter.ToInt32(header, 0);
                    int length = BitConverter.ToInt32(header, 4);
                    if (length < 0 || length > 1 << 20) break;

                    var payload = new byte[length];
                    if (!ReadExact(pipe, payload, length)) break;

                    if (opcode == OpClose)
                    {
                        Plugin.Log.LogWarning("discord closed the pipe: " + Encoding.UTF8.GetString(payload));
                        break;
                    }

                    using var doc = JsonDocument.Parse(payload);
                    if (doc.RootElement.TryGetProperty("evt", out var evt) && evt.GetString() == "READY")
                        _ready.Set();
                }
                catch { break; }
            }

            if (_pipe == pipe) Teardown();
        }

        private static bool ReadExact(Stream stream, byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = stream.Read(buffer, read, count - read);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        private static byte[] SetActivityFrame(Activity a)
        {
            using var buffer = new MemoryStream();
            using (var w = new Utf8JsonWriter(buffer))
            {
                w.WriteStartObject();
                w.WriteString("cmd", "SET_ACTIVITY");
                w.WriteString("nonce", Guid.NewGuid().ToString());

                w.WriteStartObject("args");
                w.WriteNumber("pid", Environment.ProcessId);

                w.WriteStartObject("activity");
                if (!string.IsNullOrEmpty(a.Details)) w.WriteString("details", a.Details);
                if (!string.IsNullOrEmpty(a.State)) w.WriteString("state", a.State);

                if (a.StartUnix > 0)
                {
                    w.WriteStartObject("timestamps");
                    w.WriteNumber("start", a.StartUnix);
                    w.WriteEndObject();
                }

                if (!string.IsNullOrEmpty(a.LargeImage) || !string.IsNullOrEmpty(a.SmallImage))
                {
                    w.WriteStartObject("assets");
                    if (!string.IsNullOrEmpty(a.LargeImage)) w.WriteString("large_image", a.LargeImage);
                    if (!string.IsNullOrEmpty(a.LargeText)) w.WriteString("large_text", a.LargeText);
                    if (!string.IsNullOrEmpty(a.SmallImage)) w.WriteString("small_image", a.SmallImage);
                    if (!string.IsNullOrEmpty(a.SmallText)) w.WriteString("small_text", a.SmallText);
                    w.WriteEndObject();
                }

                if (a.PartySize > 0 && a.PartyMax > 0)
                {
                    w.WriteStartObject("party");
                    w.WriteString("id", "bettrfg-party");
                    w.WriteStartArray("size");
                    w.WriteNumberValue(a.PartySize);
                    w.WriteNumberValue(a.PartyMax);
                    w.WriteEndArray();
                    w.WriteEndObject();
                }

                w.WriteEndObject();
                w.WriteEndObject();
                w.WriteEndObject();
            }
            return buffer.ToArray();
        }

        private static void Teardown()
        {
            _ready.Reset();
            _hasLastSent = false;

            var pipe = _pipe;
            _pipe = null;
            if (pipe == null) return;
            try { pipe.Dispose(); } catch { }
        }
    }
}
