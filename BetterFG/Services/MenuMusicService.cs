using System;
using System.IO;
using BetterFG.Utilities;
using FG.Common;
using FGClient;

namespace BetterFG.Services
{
    public static class MenuMusicService
    {
        private const string KEY_PATH = "menu.music.path";
        private const string KEY_ENABLED = "menu.music.enabled";

        private static readonly HostedSong _song = new HostedSong();
        private static MainMenuManager _menu;
        private static string _path = "";
        private static bool _enabled;

        public static string CurrentPath => _path;
        public static bool Enabled => _enabled;
        public static bool IsPlaying => _song.Playing;

        // %APPDATA%\BettrFG\Music — where downloaded tracks land.
        public static string MusicDir
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string dir = Path.Combine(appData, "BettrFG", "Music");
                try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
                return dir;
            }
        }

        public static void Pause() => _song.SetPaused(true);
        public static void Resume() => _song.SetPaused(false);

        public static void Init()
        {
            _path = SettingsService.Get(KEY_PATH, "");
            _enabled = SettingsService.Get(KEY_ENABLED, "false") == "true";
        }

        public static void SetPath(string path)
        {
            bool wasPlaying = IsPlaying;
            _path = path ?? "";
            SettingsService.Set(KEY_PATH, _path);
            if (wasPlaying || _enabled)
            {
                Stop();
                if (!string.IsNullOrEmpty(_path)) Play();
            }
        }

        public static void SetEnabled(bool on)
        {
            _enabled = on;
            SettingsService.Set(KEY_ENABLED, on ? "true" : "false");
            if (on)
            {
                if (!IsPlaying && !string.IsNullOrEmpty(_path)) Play();
            }
            else Stop();
        }

        public static void SetGameMenuMusicPaused(bool paused)
        {
            if (_menu == null) _menu = UnityEngine.Object.FindObjectOfType<MainMenuManager>();
            _menu?._menuMusic?.SetPaused(paused);
        }

        public static void Play(MainMenuManager menu)
        {
            _menu = menu;
            Play();
        }

        public static void Play()
        {
            Stop();
            if (string.IsNullOrEmpty(_path) || !File.Exists(_path))
            {
                Plugin.Log.LogWarning($"MenuMusic: no file at '{_path}'");
                return;
            }
            if (_menu == null) _menu = UnityEngine.Object.FindObjectOfType<MainMenuManager>();

            try { _song.Start(File.ReadAllBytes(_path), Path.GetExtension(_path).ToLowerInvariant(), MenuMusic); }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"MenuMusic: play failed: {ex.Message}");
                _song.Stop();
            }
        }

        private static EventInstanceReference MenuMusic() => _menu != null ? _menu._menuMusic : null;

        public static void Stop() => _song.Stop();

        public static void Tick() => _song.Tick();

        public static void OnReplaceCurrentState(GameStateMachine.IGameState newState)
        {
            if (!IsPlaying) return;
            if (newState == null) return;
            if (newState.TryCast<StateMainMenu>() != null) return;
            if (newState.TryCast<StateMatchmaking>() != null) return;
            if (newState.TryCast<StateDisconnectingFromServer>() != null) return;
            if (newState.TryCast<StatePrivateLobby>() != null) return;
            if (newState.TryCast<StateConnectionAuthentication>() != null) return;
            if (newState.TryCast<StateConnectToGame>() != null) return;
            Stop();
        }
    }
}
