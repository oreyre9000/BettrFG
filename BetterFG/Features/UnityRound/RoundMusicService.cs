using System;
using System.IO;
using BetterFG.Utilities;
using FGClient;

namespace BetterFG.Features.UnityRound
{
    public static class RoundMusicService
    {
        private static byte[] _pendingBytes;
        private static string _pendingExt = "";
        private static readonly HostedSong _song = new HostedSong();

        public static bool HasPending => _pendingBytes != null;

        public static void SetPaused(bool paused) => _song.SetPaused(paused);

        public static void SetPending(byte[] bytes, string fileNameOrExt)
        {
            _pendingBytes = bytes;
            _pendingExt = Path.GetExtension(fileNameOrExt).ToLowerInvariant();
        }

        public static void ClearPending()
        {
            _pendingBytes = null;
        }

        public static void StartIfPending() => StartIfPending(GameMusicInstance);

        public static void StartIfPending(Func<EventInstanceReference> host)
        {
            if (_pendingBytes == null) return;
            var bytes = _pendingBytes;
            _pendingBytes = null;

            try { _song.Start(bytes, _pendingExt, host); }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"round song start failed: {ex.Message}");
                _song.Stop();
            }
        }

        public static void Tick() => _song.Tick();

        private static EventInstanceReference GameMusicInstance()
        {
            var gs = GlobalGameStateClient.Instance?._gameStateMachine?.CurrentState;
            var cgm = gs.TryCast<StateGameInProgress>()?._clientGameManager;
            return cgm?._musicInstance;
        }

        public static void Stop() => _song.Stop();
    }
}
