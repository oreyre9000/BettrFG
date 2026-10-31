using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BetterFG.Services
{
    public static class SettingsService
    {
        private static readonly string OldSettingsDir;
        private static readonly string SettingsDir;
        public static readonly string ExportsDir;
        public static readonly string TexturePacksDir;
        public static readonly string LayoutImagesDir;
        public static readonly string UsersDir;
        private static readonly string ActiveUserFile;
        private static readonly string BackupDir;
        private static readonly string OldFilePath;
        private static readonly string LegacyFilePath;
        private static readonly string FilePath;
        private static readonly string OldPbFilePath;
        private static readonly string PbFilePath;

        public const string MainUser = "Main";
        public static readonly string[] UserPrefixes =
            { "skin.", "skintex.", "allcosmetics.", "nametag.", "crownrank.", "menu.", "screen.", "pets.", "ui.canvas." };
        public static readonly string[] UserFiles = { "emoticons.txt", "phrases.txt", "emotes.txt", "skinScales.txt" };

        public static string ActiveUser { get; private set; } = MainUser;
        public static bool MigratedThisRun { get; private set; }
        public static string UserDir { get { EnsureInit(); return Path.Combine(UsersDir, ActiveUser); } }
        private static string UserFilePath => Path.Combine(UserDir, "settings.txt");
        private static string SkinScaleFilePath => Path.Combine(UserDir, "skinScales.txt");

        private static Dictionary<string, string> _data = new Dictionary<string, string>(StringComparer.Ordinal);
        private static Dictionary<string, string> _user = new Dictionary<string, string>(StringComparer.Ordinal);
        private static bool _loaded = false;
        private static bool _initDone = false;
        private static bool _frozen;
        private static bool _pendingUserWrite;

        // periodic backup gating — only writes a new backup if Set was called since the last one.
        private static bool _dirtySinceBackup;
        private static bool _pendingWrite;
        private static float _lastBackupTime;
        public const string KEY_BACKUP_ENABLED = "backup.enabled";
        public const string KEY_BACKUP_INTERVAL_MIN = "backup.intervalMin";
        public const int DEFAULT_BACKUP_INTERVAL_MIN = 5;

        private static Dictionary<string, float> _pbs = new Dictionary<string, float>(StringComparer.Ordinal);
        private static bool _pbsLoaded = false;

        private static Dictionary<string, float> _skinScales = new Dictionary<string, float>(StringComparer.Ordinal);
        private static bool _skinScalesLoaded = false;

        static SettingsService()
        {
            string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            OldSettingsDir = Path.Combine(dllDir, "Settings");
            SettingsDir = Path.Combine(appData, "BettrFG", "Settings");
            ExportsDir = Path.Combine(appData, "BettrFG", "Exports");
            TexturePacksDir = Path.Combine(appData, "BettrFG", "TexturePacks");
            LayoutImagesDir = Path.Combine(appData, "BettrFG", "LayoutImages");
            UsersDir = Path.Combine(appData, "BettrFG", "Users");
            ActiveUserFile = Path.Combine(appData, "BettrFG", "user.txt");
            BackupDir = Path.Combine(SettingsDir, "Backup");
            OldFilePath = Path.Combine(OldSettingsDir, "last.txt");
            LegacyFilePath = Path.Combine(SettingsDir, "last.txt");
            FilePath = Path.Combine(SettingsDir, "global.txt");
            OldPbFilePath = Path.Combine(OldSettingsDir, "pb.txt");
            PbFilePath = Path.Combine(SettingsDir, "pb.txt");
        }

        public static void Init()
        {
            EnsureInit();
        }

        public static bool IsUserKey(string key)
        {
            foreach (var pre in UserPrefixes)
                if (key.StartsWith(pre, StringComparison.Ordinal)) return true;
            return false;
        }

        private static Dictionary<string, string> StoreFor(string key) => IsUserKey(key) ? _user : _data;

        public static string Get(string key, string defaultValue = "")
        {
            EnsureLoaded();
            return StoreFor(key).TryGetValue(key, out var v) ? v : defaultValue;
        }

        public static int Version { get; private set; }

        public static void Set(string key, string value)
        {
            EnsureLoaded();
            StoreFor(key)[key] = value ?? "";
            MarkDirty(key);
            _dirtySinceBackup = true;
            Version++;
        }

        public static void Remove(string key)
        {
            EnsureLoaded();
            if (StoreFor(key).Remove(key)) { MarkDirty(key); Version++; }
        }

        private static void MarkDirty(string key)
        {
            if (IsUserKey(key)) _pendingUserWrite = true;
            else _pendingWrite = true;
        }

        public static string SanitizeUserName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim().Trim('.');
        }

        public static List<string> ListUsers()
        {
            EnsureInit();
            var names = new List<string>();
            foreach (var d in Directory.GetDirectories(UsersDir)) names.Add(Path.GetFileName(d));
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        public static Dictionary<string, string> ReadUserSettings(string user) =>
            ReadKeyFile(Path.Combine(UsersDir, SanitizeUserName(user), "settings.txt"));

        public static void SwitchUser(string name)
        {
            name = SanitizeUserName(name);
            if (name.Length == 0 || !Directory.Exists(Path.Combine(UsersDir, name))) return;
            Flush();
            ActiveUser = name;
            File.WriteAllText(ActiveUserFile, name);
            _user = ReadKeyFile(UserFilePath);
            _skinScalesLoaded = false;
            Version++;
            Plugin.Log.LogInfo($"now playing as {name}");
        }

        // Set used to rewrite the whole file on every call, so anything that saved a LIST paid one
        // full write per element — typing a single character into a name-warning rule rewrote ~500
        // lines thirteen times over, on the main thread, which is exactly as slow as it sounds.
        // writes are coalesced now and flushed from the UI housekeeping tick.
        public static void Flush()
        {
            if (!_pendingWrite && !_pendingUserWrite) return;
            Save();
        }

        // for presets: grab every key under these prefixes (snapshot), or wipe those keys and write
        // a new set (values=null just wipes). one save either way.
        public static Dictionary<string, string> Snapshot(string[] prefixes)
        {
            EnsureLoaded();
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var store in new[] { _data, _user })
                foreach (var kv in store)
                    foreach (var pre in prefixes)
                        if (kv.Key.StartsWith(pre, StringComparison.Ordinal)) { result[kv.Key] = kv.Value; break; }
            return result;
        }

        public static void ReplacePrefixed(string[] prefixes, Dictionary<string, string> values)
        {
            EnsureLoaded();
            foreach (var store in new[] { _data, _user })
            {
                var toRemove = new List<string>();
                foreach (var key in store.Keys)
                    foreach (var pre in prefixes)
                        if (key.StartsWith(pre, StringComparison.Ordinal)) { toRemove.Add(key); break; }
                foreach (var key in toRemove) store.Remove(key);
            }
            if (values != null)
                foreach (var kv in values) StoreFor(kv.Key)[kv.Key] = kv.Value ?? "";
            Version++;
            _pendingWrite = _pendingUserWrite = true;
            Save();
        }

        public static bool TryGetPb(string levelName, out float pb)
        {
            EnsurePbsLoaded();
            return _pbs.TryGetValue(levelName, out pb);
        }

        public static void SetPb(string levelName, float time)
        {
            EnsurePbsLoaded();
            _pbs[levelName] = time;
            SavePbs();
        }

        public static bool IsNewPb(string levelName, float time) =>
            !TryGetPb(levelName, out float pb) || time < pb;

        // "Don't show again" for a specific update version. Persists to last.txt as a
        // comma-separated list under updateWindow.ignored.
        public const string KEY_IGNORED_UPDATES = "updateWindow.ignored";

        public static bool IsUpdateIgnored(string version)
        {
            if (string.IsNullOrEmpty(version)) return false;
            string raw = Get(KEY_IGNORED_UPDATES, "");
            if (string.IsNullOrEmpty(raw)) return false;
            foreach (var part in raw.Split(','))
                if (part.Trim() == version) return true;
            return false;
        }

        public static void IgnoreUpdate(string version)
        {
            if (string.IsNullOrEmpty(version) || IsUpdateIgnored(version)) return;
            string raw = Get(KEY_IGNORED_UPDATES, "");
            Set(KEY_IGNORED_UPDATES, string.IsNullOrEmpty(raw) ? version : raw + "," + version);
        }

        public static Dictionary<string, float> GetAllPbs()
        {
            EnsurePbsLoaded();
            return _pbs;
        }

        public static bool TryGetSkinScale(string file, out float scale)
        {
            EnsureSkinScalesLoaded();
            return _skinScales.TryGetValue(file, out scale);
        }

        public static void SetSkinScale(string file, float scale)
        {
            EnsureSkinScalesLoaded();
            if (Mathf.Abs(scale - 1f) < 0.01f) { RemoveSkinScale(file); return; }
            _skinScales[file] = scale;
            SaveSkinScales();
        }

        public static void RemoveSkinScale(string file)
        {
            EnsureSkinScalesLoaded();
            if (_skinScales.Remove(file)) SaveSkinScales();
        }

        private static void EnsureInit()
        {
            if (_initDone) return;
            _initDone = true;

            try
            {
                if (!Directory.Exists(SettingsDir))
                    Directory.CreateDirectory(SettingsDir);

                if (!File.Exists(LegacyFilePath) && File.Exists(OldFilePath))
                {
                    File.Copy(OldFilePath, LegacyFilePath, false);
                    Plugin.Log.LogInfo("Settings: moved last.txt to appdata");
                }

                if (!File.Exists(PbFilePath) && File.Exists(OldPbFilePath))
                {
                    File.Copy(OldPbFilePath, PbFilePath, false);
                    Plugin.Log.LogInfo("Settings: moved pb.txt to appdata");
                }

                if (!File.Exists(FilePath)) SplitLegacySettings();

                string want = File.Exists(ActiveUserFile) ? SanitizeUserName(File.ReadAllText(ActiveUserFile)) : "";
                if (want.Length == 0 || !Directory.Exists(Path.Combine(UsersDir, want)))
                {
                    if (want.Length > 0) Plugin.Log.LogWarning($"user.txt says '{want}' but there's no such user folder, falling back to {MainUser}");
                    want = MainUser;
                }
                Directory.CreateDirectory(Path.Combine(UsersDir, want));
                ActiveUser = want;

                if (BackupEnabled) BackupNow("startup");
                Application.quitting += (Il2CppSystem.Action)(() => { if (BackupEnabled) BackupNow("quit"); });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Settings: Init failed: {ex.Message}");
            }
        }

        // call every frame; takes a backup at most every 5 minutes, and only if something was
        // actually changed since the last one. wired into BetterFGUIMan.Update.
        public static void TickBackup()
        {
            if (!_dirtySinceBackup) return;
            if (!BackupEnabled) return;
            float now = Time.realtimeSinceStartup;
            if (now - _lastBackupTime < BackupIntervalMinutes * 60f) return;
            BackupNow("auto");
            _lastBackupTime = now;
            _dirtySinceBackup = false;
        }

        public static bool BackupEnabled
        {
            get => Get(KEY_BACKUP_ENABLED, "true") != "false";
            set => Set(KEY_BACKUP_ENABLED, value ? "true" : "false");
        }

        public static int BackupIntervalMinutes
        {
            get => int.TryParse(Get(KEY_BACKUP_INTERVAL_MIN, DEFAULT_BACKUP_INTERVAL_MIN.ToString()), out int v)
                ? Mathf.Clamp(v, 1, 120) : DEFAULT_BACKUP_INTERVAL_MIN;
            set => Set(KEY_BACKUP_INTERVAL_MIN, Mathf.Clamp(value, 1, 120).ToString());
        }

        private static void SplitLegacySettings()
        {
            var global = new List<string>();
            var user = new List<string>();
            foreach (var kv in ReadKeyFile(LegacyFilePath))
                (IsUserKey(kv.Key) ? user : global).Add($"{kv.Key}={kv.Value}");

            string main = Path.Combine(UsersDir, MainUser);
            Directory.CreateDirectory(main);
            File.WriteAllLines(Path.Combine(main, "settings.txt"), user);
            foreach (var f in UserFiles)
            {
                string src = Path.Combine(SettingsDir, f), dst = Path.Combine(main, f);
                if (File.Exists(src) && !File.Exists(dst)) File.Copy(src, dst);
            }
            File.WriteAllText(ActiveUserFile, MainUser);
            File.WriteAllLines(FilePath, global);

            MigratedThisRun = File.Exists(LegacyFilePath);
            if (MigratedThisRun)
                Plugin.Log.LogInfo($"split last.txt: {global.Count} global keys, {user.Count} went to {MainUser}. last.txt itself left alone");
        }

        public static void ResetAll()
        {
            BackupNow("reset");
            _data.Clear();
            _user.Clear();
            _pendingWrite = _pendingUserWrite = true;
            Save();
            _frozen = true;
            Plugin.Log.LogWarning("settings wiped back to defaults, old copy is in Backup. closing the game");
        }

        public static string BackupFolderPath => BackupDir;

        public static string SettingsFilePath => FilePath;

        public static string UserSettingsFilePath => UserFilePath;

        public static string SettingsFolderPath => SettingsDir;

        // copies last.txt into Settings/Backup with a timestamped name. cheap insurance against
        // the file getting clobbered mid-session.
        private static void BackupNow(string reason)
        {
            try
            {
                Flush();
                string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                if (File.Exists(FilePath))
                {
                    Directory.CreateDirectory(BackupDir);
                    File.Copy(FilePath, Path.Combine(BackupDir, $"global_{stamp}_{reason}.txt"), true);
                }
                if (File.Exists(UserFilePath))
                {
                    string userBackup = Path.Combine(UserDir, "Backup");
                    Directory.CreateDirectory(userBackup);
                    File.Copy(UserFilePath, Path.Combine(userBackup, $"settings_{stamp}_{reason}.txt"), true);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Settings: backup ({reason}) failed: {ex.Message}");
            }
        }

        private static void EnsureLoaded()
        {
            EnsureInit();
            if (_loaded) return;
            _loaded = true;
            _data = ReadKeyFile(FilePath);
            _user = ReadKeyFile(UserFilePath);
            PromoteUserKeys();
        }

        private static void PromoteUserKeys()
        {
            var moved = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in _data)
                if (IsUserKey(kv.Key)) moved[kv.Key] = kv.Value;
            if (moved.Count == 0) return;

            foreach (var user in ListUsers())
            {
                string path = Path.Combine(UsersDir, user, "settings.txt");
                var keys = ReadKeyFile(path);
                foreach (var kv in moved)
                    if (!keys.ContainsKey(kv.Key)) keys[kv.Key] = kv.Value;
                WriteKeyFile(path, keys);
            }
            foreach (var kv in moved)
            {
                _data.Remove(kv.Key);
                if (!_user.ContainsKey(kv.Key)) _user[kv.Key] = kv.Value;
            }
            _pendingWrite = true;
            Save();
            Plugin.Log.LogInfo($"{moved.Count} shared settings are per-user now, copied into every user");
        }

        private static Dictionary<string, string> ReadKeyFile(string path)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(path)) return d;

            try
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 1) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    d[k] = BetterFG.Utilities.UntrustedInput.SettingValue(v);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Settings: Load failed for {path}: {ex.Message}");
            }
            return d;
        }

        private static void WriteKeyFile(string path, Dictionary<string, string> d)
        {
            var lines = new List<string>();
            foreach (var kv in d)
                lines.Add($"{kv.Key}={kv.Value}");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, lines);
        }

        private static void Save()
        {
            if (_frozen) return;
            EnsureInit();
            try
            {
                if (_pendingWrite) { _pendingWrite = false; WriteKeyFile(FilePath, _data); }
                if (_pendingUserWrite) { _pendingUserWrite = false; WriteKeyFile(UserFilePath, _user); }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Settings: Save failed: {ex.Message}");
            }
        }

        private static void EnsurePbsLoaded()
        {
            EnsureInit();
            if (_pbsLoaded) return;
            _pbsLoaded = true;
            _pbs.Clear();

            if (!File.Exists(PbFilePath)) return;

            try
            {
                foreach (var line in File.ReadAllLines(PbFilePath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 1) continue;
                    string k = line.Substring(0, eq).Trim();
                    if (float.TryParse(line.Substring(eq + 1).Trim(), out float v))
                        _pbs[k] = v;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Settings: PB load failed: {ex.Message}");
            }
        }

        private static void SavePbs()
        {
            EnsureInit();
            try
            {
                if (!Directory.Exists(SettingsDir))
                    Directory.CreateDirectory(SettingsDir);

                var lines = new List<string>();
                foreach (var kv in _pbs)
                    lines.Add($"{kv.Key}={kv.Value:F3}");

                File.WriteAllLines(PbFilePath, lines);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Settings: PB save failed: {ex.Message}");
            }
        }

        private static void EnsureSkinScalesLoaded()
        {
            EnsureInit();
            if (_skinScalesLoaded) return;
            _skinScalesLoaded = true;
            _skinScales.Clear();

            if (!File.Exists(SkinScaleFilePath)) return;

            try
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                foreach (var line in File.ReadAllLines(SkinScaleFilePath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 1) continue;
                    string k = line.Substring(0, eq).Trim();
                    if (float.TryParse(line.Substring(eq + 1).Trim(),
                        System.Globalization.NumberStyles.Float, ci, out float v) && Mathf.Abs(v - 1f) >= 0.01f)
                        _skinScales[k] = v;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Settings: SkinScale load failed: {ex.Message}");
            }
        }

        private static void SaveSkinScales()
        {
            EnsureInit();
            try
            {
                if (!Directory.Exists(SettingsDir))
                    Directory.CreateDirectory(SettingsDir);

                var ci = System.Globalization.CultureInfo.InvariantCulture;
                var lines = new List<string>();
                foreach (var kv in _skinScales)
                    lines.Add($"{kv.Key}={kv.Value.ToString("F3", ci)}");

                File.WriteAllLines(SkinScaleFilePath, lines);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Settings: SkinScale save failed: {ex.Message}");
            }
        }
    }
}
