using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace BetterFG.Services
{
    // fetches the github release asset list for the "music" tag, and downloads individual
    // tracks on demand into MenuMusicService.MusicDir. caches the parsed track list in memory.
    public static class MenuMusicCatalog
    {
        private const string RELEASE_API =
            "https://api.github.com/repos/oreyre9000/BettrFG/releases/tags/music";

        public struct Track
        {
            public string name;   // filename (e.g. "song.mp3")
            public string url;    // browser_download_url
        }

        private static readonly List<Track> _tracks = new List<Track>();
        public static IReadOnlyList<Track> Tracks => _tracks;
        public static bool Loaded { get; private set; }

        // matches each {"name":"x.mp3", ... "browser_download_url":"https://..."} asset entry.
        private static readonly Regex AssetRegex = new Regex(
            "\"name\"\\s*:\\s*\"(?<name>[^\"]+\\.(?:mp3|wav))\"[\\s\\S]*?\"browser_download_url\"\\s*:\\s*\"(?<url>[^\"]+)\"",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static IEnumerator Fetch(Action onDone = null)
        {
            var req = UnityWebRequest.Get(RELEASE_API);
            req.SetRequestHeader("User-Agent", "BettrFG");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Plugin.Log.LogWarning($"MenuMusicCatalog: fetch failed: {req.error}");
                req.Dispose();
                onDone?.Invoke();
                yield break;
            }

            string json = req.downloadHandler.text ?? "";
            req.Dispose();

            _tracks.Clear();
            foreach (Match m in AssetRegex.Matches(json))
                _tracks.Add(new Track { name = m.Groups["name"].Value, url = m.Groups["url"].Value });
            Loaded = true;
            onDone?.Invoke();
        }

        private static readonly string[] SongExts = { ".mp3", ".wav", ".ogg", ".flac", ".m4a" };

        public static string CustomDir => Directory.CreateDirectory(Path.Combine(MenuMusicService.MusicDir, "custom")).FullName;

        public static List<string> CustomSongs()
        {
            var songs = new List<string>();
            foreach (var f in Directory.GetFiles(CustomDir))
                if (Array.IndexOf(SongExts, Path.GetExtension(f).ToLowerInvariant()) >= 0) songs.Add(f);
            songs.Sort(StringComparer.OrdinalIgnoreCase);
            return songs;
        }

        public static string AddCustom(string src)
        {
            string name = Path.GetFileNameWithoutExtension(src), ext = Path.GetExtension(src);
            string dest = Path.Combine(CustomDir, name + ext);
            for (int n = 2; File.Exists(dest); n++) dest = Path.Combine(CustomDir, $"{name} ({n}){ext}");
            File.Copy(src, dest);
            Plugin.Log.LogInfo($"custom menu song added: {Path.GetFileName(dest)}");
            return dest;
        }

        public static void DeleteCustom(string song)
        {
            if (string.Equals(MenuMusicService.CurrentPath, song, StringComparison.OrdinalIgnoreCase)) MenuMusicService.SetPath("");
            File.Delete(song);
            Plugin.Log.LogInfo($"custom menu song gone: {Path.GetFileName(song)}");
        }

        public static void ReplaceCustom(string old, string src)
        {
            if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(old), StringComparison.OrdinalIgnoreCase)) return;
            bool inUse = string.Equals(MenuMusicService.CurrentPath, old, StringComparison.OrdinalIgnoreCase);
            File.Delete(old);
            string dest = AddCustom(src);
            if (inUse) MenuMusicService.SetPath(dest);
        }

        public static string CachedPath(Track t) =>
            Path.Combine(MenuMusicService.MusicDir, t.name);

        public static bool IsCached(Track t) => File.Exists(CachedPath(t));

        public static IEnumerator Download(Track t, Action<bool> onDone = null)
        {
            string dest = CachedPath(t);
            var req = UnityWebRequest.Get(t.url);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Plugin.Log.LogWarning($"MenuMusicCatalog: download {t.name} failed: {req.error}");
                req.Dispose();
                onDone?.Invoke(false);
                yield break;
            }

            try { File.WriteAllBytes(dest, req.downloadHandler.data); }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"MenuMusicCatalog: write {t.name} failed: {ex.Message}");
                req.Dispose();
                onDone?.Invoke(false);
                yield break;
            }
            req.Dispose();
            onDone?.Invoke(true);
        }
    }
}
