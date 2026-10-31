using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine.Networking;

namespace BetterFG.Utilities
{
    public static class OnlineData
    {
        public const string BaseUrl = "https://assets.bettrfg.com/";

        public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BettrFG", "OnlineData");

        public static string Dir(params string[] parts) => Path.Combine(Root, Path.Combine(parts));

        public static string Stamp(string package) => Path.Combine(Root, ".stamps", package.Replace('/', '_'));

        static Dictionary<string, string> _manifest;
        static UnityWebRequest _manifestReq;
        static UnityWebRequestAsyncOperation _manifestOp;
        static readonly HashSet<string> _busy = new HashSet<string>();

        public static bool IsCurrent(string package)
        {
            string stamp = Stamp(package);
            if (!File.Exists(stamp)) return false;
            return _manifest == null || !_manifest.TryGetValue(package, out var hashes) || File.ReadAllText(stamp) == hashes;
        }

        public static bool Installed(string package) => File.Exists(Stamp(package));

        public static bool Outdated(string package) =>
            Installed(package) && _manifest != null && _manifest.TryGetValue(package, out var hashes) && File.ReadAllText(Stamp(package)) != hashes;

        public static long DiskBytes(string package)
        {
            string path = Dir(package);
            if (File.Exists(path)) return new FileInfo(path).Length;
            return Directory.Exists(path) ? new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0L;
        }

        public static bool Uninstall(string package)
        {
            try
            {
                string path = Dir(package);
                if (File.Exists(path)) File.Delete(path);
                else if (Directory.Exists(path)) Directory.Delete(path, true);
                File.Delete(Stamp(package));
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"{package} wouldn't uninstall: {ex.Message}");
                return false;
            }
        }

        public static bool Ready(string package)
        {
            if (_manifest == null) return false;
            if (!_manifest.TryGetValue(package, out var hashes)) return true;
            string stamp = Stamp(package);
            return File.Exists(stamp) && File.ReadAllText(stamp) == hashes;
        }

        public static List<string> Listed(string prefix)
        {
            var list = new List<string>();
            if (_manifest != null)
                foreach (var key in _manifest.Keys)
                    if (key.StartsWith(prefix, StringComparison.Ordinal)) list.Add(key);
            return list;
        }

        public static IEnumerator Fetch(string package, Action<float> progress = null)
        {
            yield return LoadManifest().WrapToIl2Cpp();
            if (_manifest == null || !_manifest.TryGetValue(package, out var hashes)) yield break;
            string stamp = Stamp(package);
            if (File.Exists(stamp) && File.ReadAllText(stamp) == hashes) yield break;
            if (!_busy.Add(package)) yield break;

            try
            {
                var parts = hashes.Split('\t');
                for (int i = 0; i < parts.Length; i++)
                {
                    string url = BaseUrl + package + (i == 0 ? ".zip" : $".{i + 1}.zip");
                    var req = UnityWebRequest.Get(url);
                    byte[] data;
                    string error;
                    try
                    {
                        var op = req.SendWebRequest();
                        while (!op.isDone)
                        {
                            progress?.Invoke((i + req.downloadProgress) / parts.Length);
                            yield return null;
                        }
                        data = req.result == UnityWebRequest.Result.Success ? req.downloadHandler.data : null;
                        error = req.error;
                    }
                    finally { req.Dispose(); }
                    if (data == null || data.Length == 0) { Plugin.Log.LogWarning($"{url} didn't come down ({error})"); yield break; }

                    string want = parts[i];
                    var unpack = Task.Run(() => Unpack(data, want));
                    while (!unpack.IsCompleted) yield return null;
                    if (unpack.Result != null) { Plugin.Log.LogWarning($"{url}: {unpack.Result}"); yield break; }
                }

                Directory.CreateDirectory(Path.GetDirectoryName(stamp));
                File.WriteAllText(stamp, hashes);
                progress?.Invoke(1f);
                Plugin.Log.LogInfo($"{package} is in, {parts.Length} zip(s)");
            }
            finally { _busy.Remove(package); }
        }

        public static IEnumerator LoadManifest()
        {
            if (_manifest == null)
            {
                if (_manifestReq == null)
                {
                    _manifestReq = UnityWebRequest.Get(BaseUrl + "manifest.txt");
                    _manifestOp = _manifestReq.SendWebRequest();
                }
                yield return _manifestOp;
                if (_manifest == null && _manifestReq != null)
                {
                    if (_manifestReq.result == UnityWebRequest.Result.Success)
                    {
                        var parsed = new Dictionary<string, string>();
                        foreach (var line in _manifestReq.downloadHandler.text.Split('\n'))
                        {
                            int tab = line.IndexOf('\t');
                            if (tab > 0) parsed[line.Substring(0, tab)] = line.Substring(tab + 1).TrimEnd('\r');
                        }
                        _manifest = parsed;
                        Plugin.Log.LogInfo($"asset manifest up, {parsed.Count} packages");
                    }
                    else Plugin.Log.LogWarning($"asset manifest wouldn't load ({_manifestReq.error}), sticking with what's on disk");
                    _manifestReq.Dispose();
                    _manifestReq = null;
                }
            }
        }

        static string Unpack(byte[] data, string sha256)
        {
            try
            {
                if (!Convert.ToHexString(SHA256.HashData(data)).Equals(sha256, StringComparison.OrdinalIgnoreCase))
                    return "hash doesn't match the manifest, not unpacking it";

                string root = Path.GetFullPath(Root) + Path.DirectorySeparatorChar;
                using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
                foreach (var entry in zip.Entries)
                {
                    if (entry.Name.Length == 0) continue;
                    string dest = Path.GetFullPath(Path.Combine(root, entry.FullName));
                    if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return $"{entry.FullName} points outside OnlineData, refusing the whole zip";
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    string tmp = dest + ".dl";
                    entry.ExtractToFile(tmp, true);
                    File.Move(tmp, dest, true);
                }
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }
    }
}
