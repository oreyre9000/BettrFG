using System;
using System.IO;
using BetterFG.UI.Windows;
using FG.Common;
using FGClient;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using static FG.Common.GameStateMachine;

namespace BetterFG.Features.UnityRound.Editor
{
    // Lets you load a local unity round into the in-game level editor by picking its info.json.
    //
    // Patches GameStateMachine.ReplaceCurrentState (same trick VictoryScreenBean uses): entering
    // StateLevelEditor opens the loader window, leaving it tears everything down + drops caches.
    //
    // Only ever one object lives in the holder. Load wipes the old one first. The editor's own
    // creative-mode background + placeables get disabled while a round is loaded, and the original
    // render settings (ambient/fog/skybox) are cached once so Unload can put them back.

    public static class UnityRoundLoader
    {
        public const string HOLDER_NAME = "BetterFG_UnityRoundEditorHolder";
        public const string PathSettingKey = "unityround.editor.jsonpath";
        public const string ShareUrlSettingKey = "unityround.editor.shareurl";

        private static GameObject _holder;
        private static GameObject _spawned;
        private static AssetBundle _loadedBundle;
        private static bool _ownsBundle;   // false when we reused a bundle the game already had loaded
        private static string _loadedJsonPath;

        private static long _infoFingerprint;
        private static DateTime _bundleWriteTime;
        private static long _bundleLength;

        private static bool _envCached;
        private static AmbientMode _oldAmbientMode;
        private static Color _oldAmbientLight;
        private static float _oldReflectionIntensity;
        private static bool _oldFog;
        private static float _oldFogDensity;
        private static Color _oldFogColor;
        private static Material _oldSkybox;

        public static GameObject Spawned => _spawned;
        public static bool HasSpawned => _spawned != null;
        public static string LoadedJsonPath => _loadedJsonPath;

        // true while we're sat in the level editor (creative). the Creative tab reads this.
        public static bool InLevelEditor;

        public static bool Playtesting { get; private set; }

        public static bool EditorPreview => InLevelEditor && !Playtesting;

        public static void SetPlaytesting(bool on)
        {
            if (Playtesting == on) return;
            Playtesting = on;
            Reapply();
        }

        // live "keep the editor's own placeables/background visible" toggle. normally comes from
        // info.json's keepExistingObjects; the Creative tab's Configuration step flips it at runtime.
        public static bool KeepExistingObjects { get; private set; }

        public static bool HideFinishLines { get; private set; }

        // re-run the creative-object cull with the new keep flag. restore first so a false->true->false
        // walk doesn't leave anything double-disabled or stuck on.
        public static void SetKeepExistingObjects(bool keep)
        {
            KeepExistingObjects = keep;
            Reapply();
        }

        public static bool HideBackground { get; private set; }

        public static bool HideThemeLighting { get; private set; }

        public static void SetHideThemeLighting(bool hide)
        {
            HideThemeLighting = hide;
            Reapply();
        }

        public static void SetHideFinishLines(bool hide)
        {
            HideFinishLines = hide;
            Reapply();
        }

        public static void SetHideBackground(bool hide)
        {
            HideBackground = hide;
            Reapply();
        }

        private static void Reapply()
        {
            if (_spawned == null) return;
            BetterFGRoundPostmodifier.DisableCreativeModeObjects(disablePlaceables: true, keepExisting: KeepExistingObjects,
                hideFinishLines: HideFinishLines, hideBackground: HideBackground, hideThemeLighting: HideThemeLighting);
            BetterFG.Tweaks.Background3dTweak.ApplyIfWanted();
        }

        // ── share code in the level description ──────────────────────────────────
        // the level editor description field rejects raw <color> tags, so we wrap them in LRM marks
        // (U+200E, invisible) which lets them through. format written into the description:
        //   ‎<color=red>‎BettrFG level owner/repo/round_xxx
        // the play-time loader (TryHandleDescription) strips this back to the owner/repo/round_xxx code.
        public const string LRM = "‎";
        public const string DESC_PREFIX = LRM + "<color=red>" + LRM + "BettrFG level ";

        // pull the owner/repo/round_xxx code back out of a wrapped description. returns null if the
        // description isn't one of ours. tolerant of the LRM marks / color tag / whitespace.
        public static string ExtractShareCode(string description)
        {
            if (string.IsNullOrEmpty(description)) return null;
            // drop every LRM mark, then any <...> tags, then look for our marker text.
            string s = description.Replace(LRM, "");
            s = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]*>", "");
            int i = s.IndexOf("BettrFG level", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            string code = s.Substring(i + "BettrFG level".Length).Trim();
            return string.IsNullOrEmpty(code) ? null : code;
        }

        // turn a github link to a level's info.json into the owner/repo/round_xxx description code.
        // accepts both the browser link (github.com/owner/repo/blob/branch/Rounds/round_xxx/info.json)
        // and the raw link (raw.githubusercontent.com/owner/repo/branch/Rounds/round_xxx/info.json).
        public static string ShareCodeFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            url = url.Trim().Trim('"');

            string rest = null;
            foreach (var host in new[] { "raw.githubusercontent.com/", "github.com/" })
            {
                int i = url.IndexOf(host, StringComparison.OrdinalIgnoreCase);
                if (i >= 0) { rest = url.Substring(i + host.Length); break; }
            }
            if (rest == null) return "";

            var parts = rest.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return "";
            string owner = parts[0];
            string repo = parts[1];

            string round = null;
            foreach (var p in parts)
                if (p.StartsWith("round_", StringComparison.OrdinalIgnoreCase)) { round = p; break; }
            if (string.IsNullOrEmpty(round)) return "";

            return $"{owner}/{repo}/{round}";
        }

        // write owner/repo/round_xxx into the current level's description via the editor's IO component.
        public static bool SetLevelDescription(string code, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(code)) { error = "no code"; return false; }
            try
            {
                // the holder object is literally named "----------------SYSTEMS/MANAGERS" (the slash
                // is part of the name), so GameObject.Find can't path to it. grab the IO by type.
                var io = UnityEngine.Object.FindObjectOfType<LevelEditorManagerIO>();
                if (io == null) { error = "not in the level editor"; return false; }

                io.TrySetLevelDescription(DESC_PREFIX + code);
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        // ── window ────────────────────────────────────────────────────────────

        public static void OpenWindow()
        {
            if (UnityRoundLoaderWindow.Instance != null)
            {
                UnityRoundLoaderWindow.Instance.ShowWindow();
                return;
            }

            var go = new GameObject("BetterFG_UnityRoundLoaderWindow");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<UnityRoundLoaderWindow>().Configure();
        }

        private static GameObject GetHolder()
        {
            if (_holder != null) return _holder;
            _holder = GameObject.Find(HOLDER_NAME) ?? new GameObject(HOLDER_NAME);
            _holder.transform.position = Vector3.zero;
            _holder.transform.rotation = Quaternion.identity;
            return _holder;
        }

        // ── load ──────────────────────────────────────────────────────────────

        // Picks the round's info.json, loads the bundle named in "file" sitting next to it,
        // instantiates the prefab into the holder and applies the round's environment.
        public static bool LoadFromInfoJson(string jsonPath, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(jsonPath)) { error = "no path given"; return false; }
            jsonPath = jsonPath.Trim().Trim('"');
            if (!File.Exists(jsonPath)) { error = "info.json not found"; return false; }

            MapInfo info;
            try { info = BetterFGUnityRounds.ParseInfo(File.ReadAllText(jsonPath)); }
            catch (Exception ex) { error = "info.json read failed: " + ex.Message; return false; }

            if (info == null || string.IsNullOrEmpty(info.file) || string.IsNullOrEmpty(info.prefab))
            { error = "invalid info.json"; return false; }

            string folder = Path.GetDirectoryName(jsonPath);
            string bundlePath = Path.Combine(folder, info.file);
            if (!File.Exists(bundlePath)) { error = $"bundle '{info.file}' not next to info.json"; return false; }

            // fully unload our previous bundle first so re-loading the same file doesn't collide
            // with "another AssetBundle with the same files is already loaded". don't touch a bundle
            // the game owns.
            UnloadBundle();

            byte[] bytes;
            try { bytes = File.ReadAllBytes(bundlePath); }
            catch (Exception ex) { error = "bundle read failed: " + ex.Message; return false; }

            AssetBundle bundle = BetterFG.Utilities.Bundles.LoadMemorySync(info.prefab, bytes);
            bool owns = bundle != null;
            if (bundle == null) bundle = BetterFGUnityRounds.FindLoadedBundleWithAsset(info.prefab);
            if (bundle == null) { error = "not a valid asset bundle"; return false; }
            _loadedBundle = bundle;
            _ownsBundle = owns;
            _loadedInfoPrefab = owns ? info.prefab : null;

            GameObject prefab = FindPrefab(bundle, info.prefab);
            if (prefab == null) { error = $"prefab '{info.prefab}' not in bundle"; UnloadBundle(); return false; }

            ClearHolder();
            var holder = GetHolder();

            GameObject instance;
            try
            {
                instance = UnityEngine.Object.Instantiate(prefab, holder.transform);
                instance.name = prefab.name;
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
            }
            catch (Exception ex) { error = "instantiate failed: " + ex.Message; return false; }

            UnityEngine.Object.DontDestroyOnLoad(holder);

            _spawned = instance;
            _loadedJsonPath = jsonPath;
            KeepExistingObjects = info.keepExistingObjects;
            HideFinishLines = info.hideFinishLines;
            HideBackground = info.hideBackground;
            HideThemeLighting = info.hideThemeLighting;
            RememberFingerprint(jsonPath, folder, info);

            // snapshot render settings before Apply clobbers them, so Unload can restore
            CacheEnvironmentOnce();

            // run the exact same postmodify the normal round path runs (physic materials,
            // mantle targets, endgoals, light probes, starting positions, env + skybox).
            Material skybox = LoadSkybox(bundle, info.skybox);
            BetterFGRoundPostmodifier.Apply(instance, info, ref skybox);

            // apply any custom obstacle textures saved in texture.json next to info.json
            ObstacleTextureLoader.LoadAndApplyForRound(jsonPath);

            Plugin.Log.LogInfo($"UnityRoundLoader: loaded '{instance.name}' from {jsonPath}");
            return true;
        }

        private static Material LoadSkybox(AssetBundle bundle, string skyboxName)
        {
            if (string.IsNullOrEmpty(skyboxName)) return null;
            try
            {
                var asset = bundle.LoadAsset(skyboxName);
                var mat = asset != null ? asset.TryCast<Material>() : null;
                if (mat == null) Plugin.Log.LogWarning($"UnityRoundLoader: skybox '{skyboxName}' not in bundle");
                return mat;
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"UnityRoundLoader: skybox load: {ex.Message}"); return null; }
        }

        // Manual unload (the UNLOAD button): tear down AND forget the round for this level so it
        // stays gone next time you open it. Leaving the editor uses plain Unload() instead, which
        // keeps the binding.
        public static void UnloadAndForget()
        {
            CreativeRoundMemory.ForgetForCurrentLevel();
            Unload();
        }

        // Tears the loaded round down and restores the editor to how it was.
        public static void Unload()
        {
            ObstacleTextureLoader.RevertAll();
            ClearHolder();
            UnloadBundle();
            _loadedJsonPath = null;

            BetterFGRoundPostmodifier.RestoreCreativeModeObjects();
            BetterFGRoundPostmodifier.ClearLiveEnvironment();
            RestoreEnvironment();
            BetterFG.Tweaks.Background3dTweak.ApplyIfWanted();
        }

        private static void UnloadBundle()
        {
            if (_loadedBundle != null && _ownsBundle && _loadedInfoPrefab != null)
                BetterFG.Utilities.Bundles.Unload(_loadedInfoPrefab, true);
            else if (_loadedBundle != null && _ownsBundle)
                _loadedBundle.Unload(true);
            _loadedBundle = null;
            _ownsBundle = false;
            _loadedInfoPrefab = null;
        }

        private static string _loadedInfoPrefab;

        private static GameObject FindPrefab(AssetBundle bundle, string prefabName)
        {
            try
            {
                var asset = bundle.LoadAsset(prefabName);
                var go = asset != null ? asset.TryCast<GameObject>() : null;
                if (go != null) return go;

                // fall back to the first GameObject in the bundle
                var all = bundle.LoadAllAssets();
                for (int i = 0; i < all.Count; i++)
                {
                    go = all[i]?.TryCast<GameObject>();
                    if (go != null) return go;
                }
            }
            catch (Exception ex) { Plugin.Log.LogError($"UnityRoundLoader: FindPrefab: {ex.Message}"); }
            return null;
        }

        public static void ClearHolder()
        {
            UnityRoundAnchors.DetachAll();
            if (_spawned != null) { UnityEngine.Object.Destroy(_spawned); _spawned = null; }

            // belt and braces: only ever one object in the holder
            if (_holder != null)
            {
                var t = _holder.transform;
                for (int i = t.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
            }
        }

        // ── auto-refresh ──────────────────────────────────────────────────────────

        private static void RememberFingerprint(string jsonPath, string folder, MapInfo info)
        {
            try { _infoFingerprint = QuickHash(File.ReadAllBytes(jsonPath)); }
            catch { _infoFingerprint = 0; }

            try
            {
                string bundlePath = Path.Combine(folder, info.file);
                var fi = new FileInfo(bundlePath);
                _bundleWriteTime = fi.LastWriteTimeUtc;
                _bundleLength = fi.Length;
            }
            catch { _bundleWriteTime = default; _bundleLength = 0; }
        }

        public static void NoteInfoWritten()
        {
            if (_loadedJsonPath == null) return;
            try { _infoFingerprint = QuickHash(File.ReadAllBytes(_loadedJsonPath)); } catch { }
        }

        private static long QuickHash(byte[] bytes)
        {
            long h = unchecked((long)1469598103934665603);
            foreach (byte b in bytes) { h ^= b; h *= 1099511628211; }
            return h;
        }

        public static void TickAutoRefresh()
        {
            if (_loadedJsonPath == null) return;
            if (!File.Exists(_loadedJsonPath)) return;

            long infoHash;
            try { infoHash = QuickHash(File.ReadAllBytes(_loadedJsonPath)); }
            catch { return; }

            bool changed = infoHash != _infoFingerprint;

            if (!changed)
            {
                try
                {
                    var info = BetterFGUnityRounds.ParseInfo(File.ReadAllText(_loadedJsonPath));
                    string folder = Path.GetDirectoryName(_loadedJsonPath);
                    string bundlePath = info != null && !string.IsNullOrEmpty(info.file) ? Path.Combine(folder, info.file) : null;
                    if (bundlePath != null && File.Exists(bundlePath))
                    {
                        var fi = new FileInfo(bundlePath);
                        changed = fi.LastWriteTimeUtc != _bundleWriteTime || fi.Length != _bundleLength;
                    }
                }
                catch { }
            }

            if (!changed) return;

            string path = _loadedJsonPath;
            Plugin.Log.LogInfo($"unity round core: {Path.GetFileName(path)} changed on disk, auto-reloading");
            if (!LoadFromInfoJson(path, out string error))
                Plugin.Log.LogWarning($"unity round core: auto-reload failed: {error}");
        }

        // ── environment ─────────────────────────────────────────────────────────

        // Snapshot the editor's render settings exactly once per level-editor session.
        private static void CacheEnvironmentOnce()
        {
            if (_envCached) return;
            _oldAmbientMode = RenderSettings.ambientMode;
            _oldAmbientLight = RenderSettings.ambientLight;
            _oldReflectionIntensity = RenderSettings.reflectionIntensity;
            _oldFog = RenderSettings.fog;
            _oldFogDensity = RenderSettings.fogDensity;
            _oldFogColor = RenderSettings.fogColor;
            _oldSkybox = RenderSettings.skybox;
            _envCached = true;
        }

        private static void RestoreEnvironment()
        {
            if (!_envCached) return;
            RenderSettings.ambientMode = _oldAmbientMode;
            RenderSettings.ambientLight = _oldAmbientLight;
            RenderSettings.reflectionIntensity = _oldReflectionIntensity;
            RenderSettings.fog = _oldFog;
            RenderSettings.fogDensity = _oldFogDensity;
            RenderSettings.fogColor = _oldFogColor;
            RenderSettings.skybox = _oldSkybox;
        }

        // called from the shared GameStateMachine.ReplaceCurrentState hub in GameStatePatches.
        // tracks whether we're in the level editor (creative) so the Creative tab can light up;
        // leaving tears the loaded round down.
        public static void OnReplaceCurrentState(GameStateMachine.IGameState newState)
        {
            bool nowEditor = newState != null && newState.TryCast<FGClient.StateLevelEditor>() != null;
            if (nowEditor == InLevelEditor) return;

            InLevelEditor = nowEditor;
            if (!nowEditor) OnLeaveLevelEditor();
        }

        // Called when the level editor state is left: full teardown + drop the env cache.
        public static void OnLeaveLevelEditor()
        {
            Unload();
            _envCached = false;
            _oldSkybox = null;
            UnityRoundLoaderWindow.Instance?.Close();
            UI.Windows.ObstacleTextureWindow.Instance?.Close();
        }

    }

}
