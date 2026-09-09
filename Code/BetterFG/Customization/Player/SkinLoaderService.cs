using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using BetterFG.Customization.Player;
using BetterFG.Network;
using BetterFG.Services;
using BetterFG.Utilities;
using BepInEx.Unity.IL2CPP.Utils.Collections;

namespace BetterFG.Customization.Player
{
    public class SkinLoaderService : MonoBehaviour
    {
        public event Action<SkinInfo, AssetBundle> OnSkinLoaded;
        public event Action<SkinInfo, AssetBundle, Texture2D> OnSkinImported;
        public event Action<string> OnProgress;
        public event Action<string> OnError;

        public SkinApplicationService skinApp;

        private bool isBusy = false;
        public bool IsBusy => isBusy;

        // ── Disk cache ────────────────────────────────────────────────────────
        // downloaded bundles used to be re-fetched + LZMA-decompressed from a managed byte[]
        // every boot — 0.5-1.3s main-thread stall per skin, landing right at menu enter. instead:
        // first download writes the bundle to disk and recompresses it to LZ4, every later boot
        // memory-maps it via LoadFromFileAsync (LZ4 chunks decompress lazily — no big stall) and
        // refreshes the cache file in the background so repo updates land next boot.

        public static string SkinCacheDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BettrFG", "SkinCache");

        public static string CachePathFor(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            foreach (char c in Path.GetInvalidFileNameChars()) if (file.IndexOf(c) >= 0) return null;
            try
            {
                if (!Directory.Exists(SkinCacheDir)) Directory.CreateDirectory(SkinCacheDir);
                return Path.Combine(SkinCacheDir, file);
            }
            catch { return null; }
        }

        // write downloaded bytes to disk off-thread (tmp + move so a mid-write crash never leaves
        // a truncated file the next boot would try to load)
        public IEnumerator SaveCacheCoroutine(string cachePath, byte[] data)
        {
            string tmp = cachePath + ".dl";
            var writeTask = System.Threading.Tasks.Task.Run(() => { try { File.WriteAllBytes(tmp, data); return true; } catch { return false; } });
            while (!writeTask.IsCompleted) yield return null;
            if (!writeTask.Result) yield break;
            try { if (File.Exists(cachePath)) File.Delete(cachePath); File.Move(tmp, cachePath); } catch { }
        }

        // cache-hit boots still re-download quietly in the background so the cache never goes
        // more than one boot stale
        private IEnumerator RefreshCacheCoroutine(string url, string cachePath)
        {
            var req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();
            byte[] data = req.result == UnityWebRequest.Result.Success ? req.downloadHandler.data : null;
            req.Dispose();
            if (data != null && data.Length > 0)
                yield return SaveCacheCoroutine(cachePath, data).WrapToIl2Cpp();
        }

        // ── GitHub download ───────────────────────────────────────────────────

        // multiple boot-time triggers can all ask for the same file while the first request is
        // still in flight (early restore, preset restore, OnBeansFound's TriggerReload, profile
        // sync) — without this the same skin downloads several times in parallel
        private readonly HashSet<string> _downloading = new HashSet<string>();

        public void DownloadSkin(string skinName, string url)
        {
            DownloadSkinWithInfo(skinName, url, null);
        }

        public void DownloadSkinWithInfo(string skinName, string url, string infoUrl)
        {
            if (!_downloading.Add(skinName))
            {
                Plugin.Log.LogInfo($"SkinLoader: '{skinName}' already downloading, skipping duplicate request");
                return;
            }
            StartCoroutine(DownloadOuterCoroutine(skinName, url, infoUrl).WrapToIl2Cpp());
        }

        private IEnumerator DownloadOuterCoroutine(string skinName, string url, string infoUrl)
        {
            yield return DownloadCoroutine(skinName, url, infoUrl).WrapToIl2Cpp();
            _downloading.Remove(skinName);
        }

        private IEnumerator DownloadCoroutine(string skinName, string url, string infoUrl)
        {
            isBusy = true;
            OnProgress?.Invoke($"Downloading {skinName}...");

            // HARD reuse first — never redownload if already in SkinApplication
            if (skinApp != null && skinApp.TryGetLoadedBundle(skinName, out var alreadyLoaded) && alreadyLoaded != null)
            {
                OnProgress?.Invoke($"Using cached {skinName}");
                isBusy = false;

                SkinInfo cachedInfo = null;
                if (!string.IsNullOrEmpty(infoUrl))
                {
                    UnityWebRequest infoReq2 = UnityWebRequest.Get(infoUrl);
                    yield return infoReq2.SendWebRequest();
                    if (infoReq2.result == UnityWebRequest.Result.Success)
                        cachedInfo = ParseSkinInfoWithOffsets(infoReq2.downloadHandler.text);
                    infoReq2.Dispose();
                }

                if (cachedInfo == null)
                    cachedInfo = new SkinInfo { name = skinName, file = skinName };

                try { cachedInfo.sourceRepo = url; } catch { }
                try { cachedInfo.lastCachedUtc = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(); } catch { }

                OnSkinLoaded?.Invoke(cachedInfo, alreadyLoaded);
                yield break;
            }

            string cachePath = CachePathFor(skinName);
            AssetBundle bundle = null;

            if (cachePath != null && File.Exists(cachePath))
            {
                OnProgress?.Invoke($"Loading cached {skinName}...");
                AssetBundle fileLoaded = null;
                yield return Bundles.LoadFile(skinName, cachePath, ab => fileLoaded = ab).WrapToIl2Cpp();
                bundle = fileLoaded;
                if (bundle != null)
                    StartCoroutine(RefreshCacheCoroutine(url, cachePath).WrapToIl2Cpp());
                else
                {
                    // corrupt/stale cache file — toss it and download fresh below
                    try { File.Delete(cachePath); } catch { }
                }
            }

            if (bundle == null)
            {
                UnityWebRequest req = UnityWebRequest.Get(url);
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    OnError?.Invoke($"Download failed: {req.error}");
                    isBusy = false;
                    req.Dispose();
                    yield break;
                }

                byte[] bundleBytes = req.downloadHandler.data;
                req.Dispose();

                // DO NOT unload anything here anymore

                if (cachePath != null)
                    StartCoroutine(SaveCacheCoroutine(cachePath, bundleBytes).WrapToIl2Cpp());
                AssetBundle memLoaded = null;
                yield return Bundles.LoadMemory(skinName, bundleBytes, ab => memLoaded = ab).WrapToIl2Cpp();
                bundle = memLoaded;
            }

            if (bundle == null)
            {
                OnError?.Invoke("Failed to load AssetBundle");
                isBusy = false;
                yield break;
            }

            // register into shared system immediately
            try { skinApp?.RegisterRemoteBundle(skinName, bundle); } catch { }

            OnProgress?.Invoke($"Downloaded {skinName}");
            isBusy = false;

            SkinInfo skinInfo = null;
            if (!string.IsNullOrEmpty(infoUrl))
            {
                UnityWebRequest infoReq = UnityWebRequest.Get(infoUrl);
                yield return infoReq.SendWebRequest();
                if (infoReq.result == UnityWebRequest.Result.Success)
                    skinInfo = ParseSkinInfoWithOffsets(infoReq.downloadHandler.text);
                infoReq.Dispose();
            }

            if (skinInfo == null)
                skinInfo = new SkinInfo { name = skinName, file = skinName };

            try { skinInfo.sourceRepo = url; } catch { }
            try { skinInfo.lastCachedUtc = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(); } catch { }

            OnSkinLoaded?.Invoke(skinInfo, bundle);
        }
        // one entry off a profile -> a slot ready for ApplySkinToBean, riding the same disk cache
        // as the local loadout instead of refetching the bundle + info.json every round
        public IEnumerator ResolveProfileSlot(RemoteSkinEntry entry, Action<ActiveSkinSlot> done, Dictionary<string, string> ownerSettings = null)
        {
            SkinType type = SkinTypeParser.FromString(entry.type);
            // plinths never bind to a bean (the profile plinth has its own path) — resolving one
            // here builds a Costumes/ url that 404s and eats the whole 20s timeout, stalling every
            // skin/cosmetic/texture queued behind it
            if (type == SkinType.Unknown || type == SkinType.Plinth) yield break;

            // embedded local skins already sit unpacked on disk — no repo has ever heard of them
            if (entry.source == "local" && !string.IsNullOrEmpty(entry.localPath) && File.Exists(entry.localPath))
            {
                AssetBundle local = null;
                if (skinApp != null && skinApp.TryGetLoadedBundle(entry.file, out var already) && already != null)
                    local = already;
                else
                {
                    byte[] bytes = null;
                    try { bytes = File.ReadAllBytes(entry.localPath); }
                    catch (Exception ex) { Plugin.Log.LogWarning($"local profile skin {entry.file}: {ex.Message}"); }
                    if (bytes != null)
                    {
                        local = skinApp?.GetOrLoadBundle(entry.file, bytes);
                        if (local != null) skinApp.RegisterRemoteBundle(entry.file, local);
                    }
                }
                if (local != null)
                {
                    SkinInfo skinInfo = null;
                    string infoPath = Path.Combine(Path.GetDirectoryName(entry.localPath) ?? "", "info.json");
                    if (File.Exists(infoPath))
                    {
                        try { skinInfo = ParseSkinInfoWithOffsets(File.ReadAllText(infoPath)); }
                        catch (Exception ex) { Plugin.Log.LogWarning($"local profile skin {entry.file}: bad info.json ({ex.Message})"); }
                    }
                    if (skinInfo == null) skinInfo = new SkinInfo { name = entry.file, file = entry.file, type = entry.type };
                    skinInfo.isLocalImport = true;
                    skinInfo.localPath = entry.localPath;

                    skinInfo.handOverride = entry.hand;
                    skinInfo.ownerSettings = ownerSettings;
                    done(new ActiveSkinSlot { skinInfo = skinInfo, bundle = local, type = type });
                }
                yield break;
            }

            string category = type == SkinType.Accessory ? "Accessories" : type == SkinType.Item ? "Items" : "Costumes";
            string repoRaw = RepoRegistry.ResolveRaw(entry.repoUrl);
            string folder = !string.IsNullOrEmpty(entry.folder) ? entry.folder : $"{category}/{entry.file}";

            SkinInfo info = null;
            AssetBundle bundle = null;
            Action<SkinInfo, AssetBundle> onLoaded = (i, b) => { if (b != null && i != null && i.file == entry.file) { info = i; bundle = b; } };

            OnSkinLoaded += onLoaded;
            DownloadSkinWithInfo(entry.file, $"{repoRaw}/{folder}/{entry.file}", $"{repoRaw}/{folder}/info.json");

            float waited = 0f;
            while (bundle == null && waited < 20f)
            {
                if (!_downloading.Contains(entry.file))
                {
                    if (skinApp != null && skinApp.TryGetLoadedBundle(entry.file, out var cached) && cached != null) bundle = cached;
                    break;
                }
                yield return null;
                waited += Time.deltaTime;
            }
            OnSkinLoaded -= onLoaded;

            if (bundle == null)
            {
                Plugin.Log.LogWarning($"profile skin '{entry.file}' never turned up — {repoRaw}/{folder} is probably a dead link, gave up after {waited:F1}s");
                yield break;
            }

            if (info == null)
            {
                var infoReq = UnityWebRequest.Get($"{repoRaw}/{folder}/info.json");
                yield return infoReq.SendWebRequest();
                if (infoReq.result == UnityWebRequest.Result.Success)
                    info = ParseSkinInfoWithOffsets(infoReq.downloadHandler.text);
                infoReq.Dispose();
                if (info == null) info = new SkinInfo { name = entry.file, file = entry.file };
                info.infoFetched = true;
            }

            info = info.Copy();
            info.type = entry.type;
            info.sourceRepo = repoRaw;
            info.repoFolder = folder;
            info.handOverride = entry.hand;
            info.ownerSettings = ownerSettings;
            done(new ActiveSkinSlot { skinInfo = info, bundle = bundle, type = type });
        }

        // ── Local folder import ───────────────────────────────────────────────

        public void ImportSkinFromFolder(string folderPath)
        {
            if (isBusy) return;
            StartCoroutine(ImportCoroutine(folderPath).WrapToIl2Cpp());
        }

        private IEnumerator ImportCoroutine(string folderPath)
        {
            isBusy = true;
            OnProgress?.Invoke($"Importing {Path.GetFileName(folderPath)}...");

            if (!Directory.Exists(folderPath))
            {
                OnError?.Invoke("Folder does not exist");
                isBusy = false;
                yield break;
            }

            string infoPath = Path.Combine(folderPath, "info.json");
            if (!File.Exists(infoPath))
            {
                OnError?.Invoke("info.json not found");
                isBusy = false;
                yield break;
            }

            // the bundle read is the expensive chunk (big file + first-boot AV scan can take
            // hundreds of ms), and StartCoroutine runs synchronously to the first yield — so the
            // old sync reads here landed entirely inside the caller's frame (RestoreFromSettings
            // on boot). do all the file IO + parsing on a worker thread instead; it's pure
            // .NET string/file work, no Unity API.
            SkinInfo skinInfo = null;
            byte[] bundleBytes = null;
            string readError = null;
            var readTask = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    string json = File.ReadAllText(infoPath);
                    skinInfo = ParseSkinInfo(json);
                    if (skinInfo == null || string.IsNullOrEmpty(skinInfo.file)) { readError = "Invalid info.json"; return; }
                    skinInfo.boneOffsets = SkinCatalogService.ParseBoneOffsets(json);

                    string bundlePath = Path.Combine(folderPath, skinInfo.file);
                    if (!File.Exists(bundlePath)) { readError = $"Bundle '{skinInfo.file}' not found"; return; }

                    skinInfo.isLocalImport = true;
                    skinInfo.localPath = bundlePath;
                    bundleBytes = File.ReadAllBytes(bundlePath);
                }
                catch (Exception ex) { readError = $"Failed to read bundle: {ex.Message}"; }
            });
            while (!readTask.IsCompleted) yield return null;

            if (readError != null || bundleBytes == null)
            {
                OnError?.Invoke(readError ?? "Failed to read bundle");
                isBusy = false;
                yield break;
            }

            OnProgress?.Invoke($"Loading {skinInfo.file}...");

            skinApp?.UnloadBundleForFile(skinInfo.file, force: true);
            Bundles.Unload(skinInfo.file, true);

            yield return null;
            AssetBundle importLoaded = null;
            yield return Bundles.LoadMemory(skinInfo.file, bundleBytes, ab => importLoaded = ab).WrapToIl2Cpp();
            AssetBundle bundle = importLoaded;

            if (bundle == null)
            {
                OnError?.Invoke("Failed to load AssetBundle");
                isBusy = false;
                yield break;
            }

            Texture2D cover = null;
            foreach (string coverName in new[] { "cover.png", "cover.jpg", "Cover.png", "Cover.jpg" })
            {
                string coverPath = Path.Combine(folderPath, coverName);
                if (!File.Exists(coverPath)) continue;
                try
                {
                    byte[] data = File.ReadAllBytes(coverPath);
                    var tex = new Texture2D(2, 2);
                    if (tex.LoadImage(data))
                    {
                        UnityEngine.Object.DontDestroyOnLoad(tex);
                        cover = tex;
                        break;
                    }
                }
                catch { }
            }

            OnProgress?.Invoke($"Imported {skinInfo.name}");
            isBusy = false;
            try { skinInfo.sourceRepo = folderPath; } catch { }
            try { skinInfo.lastCachedUtc = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(); } catch { }
            OnSkinImported?.Invoke(skinInfo, bundle, cover);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private SkinInfo ParseSkinInfoWithOffsets(string json)
        {
            var skin = ParseSkinInfo(json);
            if (skin == null) return null;
            skin.boneOffsets = SkinCatalogService.ParseBoneOffsets(json);
            skin.infoFetched = true;
            SkinCatalogService.FillItemInfoFromJson(skin, json);
            return skin;
        }


        private SkinInfo ParseSkinInfo(string json)
        {
            var skin = new SkinInfo
            {
                name = JsonUtil.GetValue(json, "name"),
                author = JsonUtil.GetValue(json, "author"),
                description = JsonUtil.GetValue(json, "description"),
                group = JsonUtil.GetValue(json, "group"),
                type = JsonUtil.GetValue(json, "type"),
                file = JsonUtil.GetValue(json, "file"),
                skinScale = JsonUtil.GetFloat(json, "skinScale"),
                keepBase = JsonUtil.GetBool(json, "keepBase")
            };
            return string.IsNullOrEmpty(skin.name) ? null : skin;
        }
    }
}
