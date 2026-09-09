using System;
using System.Collections.Generic;
using UnityEngine.UI;

namespace BetterFG.Services
{
    // loads localization.bak (tab-separated: header row of language codes, then one row per key)
    // and exposes it as key -> current-language string. the file lives loose next to BetterFG.dll in
    // the plugin folder (not embedded) — the build syncs newly-added ids into it every build, and the
    // WinForms editor edits that same file directly, no rebuild needed. any other .bak sat beside it
    // folds in as an extra language column via MergeSideFiles.
    public static class LocalizationService
    {
        private static readonly string FilePath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
            "localization.bak");
        private const string SETTINGS_KEY = "ui.language";
        private const string DEBUG_LANG = "debug";

        private static Dictionary<string, Dictionary<string, string>> _table = new Dictionary<string, Dictionary<string, string>>();
        private static string[] _languages = new[] { "en", DEBUG_LANG };
        private static string _current = "en";

        public static event Action LanguageChanged;

        private class Binding
        {
            public Text Text;
            public string Key;
            public string LastApplied;
        }
        private static readonly List<Binding> _bindings = new List<Binding>();

        public static void Bind(Text text, string key)
        {
            if (text == null || string.IsNullOrEmpty(key)) return;
            var b = new Binding { Text = text, Key = key };
            _bindings.Add(b);
            ApplyBinding(b);
        }

        public static void Unbind(Text text) => _bindings.RemoveAll(b => b.Text == text);

        private static void ApplyBinding(Binding b)
        {
            if (b.Text == null) return;
            if (b.LastApplied != null && b.Text.text != b.LastApplied) return;
            b.LastApplied = Get(b.Key);
            b.Text.text = b.LastApplied;
        }

        public static string CurrentLanguage => _current;
        public static string[] AvailableLanguages => _languages;

        private static readonly Dictionary<string, string> LangNames = new Dictionary<string, string>
        {
            { "en", "English" },
            { "es", "Español" },
            { "es-419", "Español (Latinoamérica)" },
            { "fr", "Français" },
            { "de", "Deutsch" },
            { "it", "Italiano" },
            { "pt", "Português" },
            { "pt-BR", "Português (Brasil)" },
            { "nl", "Nederlands" },
            { "pl", "Polski" },
            { "tr", "Türkçe" },
            { "ru", "Русский" },
            { "uk", "Українська" },
            { "ja", "日本語" },
            { "ko", "한국어" },
            { "zh", "中文" },
            { "zh-Hans", "简体中文" },
            { "zh-Hant", "繁體中文" },
            { "ar", "العربية" },
            { DEBUG_LANG, "Debug" },
        };

        public static string DisplayName(string code)
            => code != null && LangNames.TryGetValue(code, out var n) ? n : code;

        public static void Init()
        {
            string raw = null;
            try
            {
                if (System.IO.File.Exists(FilePath))
                    raw = System.IO.File.ReadAllText(FilePath, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("couldn't read localization.bak: " + ex.Message);
            }

            Parse(raw);
            MergeSideFiles();

            _current = SettingsService.Get(SETTINGS_KEY, "en");
            if (Array.IndexOf(_languages, _current) < 0)
            {
                Plugin.Log?.LogInfo($"saved language '{_current}' isn't in the table anymore, back to en");
                _current = _languages.Length > 0 ? _languages[0] : "en";
                SettingsService.Set(SETTINGS_KEY, _current);
            }

            Plugin.Log?.LogInfo($"localization loaded from {FilePath}: {_table.Count} keys, lang {_current}");
        }

        private static void Parse(string raw)
        {
            _table.Clear();
            if (string.IsNullOrEmpty(raw))
            {
                _languages = new[] { "en", DEBUG_LANG };
                return;
            }

            var lines = raw.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0) { _languages = new[] { "en", DEBUG_LANG }; return; }

            var fileLangs = lines[0].Split('\t');
            _languages = new string[fileLangs.Length + 1];
            Array.Copy(fileLangs, _languages, fileLangs.Length);
            _languages[fileLangs.Length] = DEBUG_LANG;

            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;
                var cols = lines[i].Split('\t');
                string key = Unescape(cols[0]);
                if (key.Length == 0) continue;
                var perLang = new Dictionary<string, string>();
                for (int c = 0; c < fileLangs.Length; c++)
                    perLang[fileLangs[c]] = c + 1 < cols.Length ? Unescape(cols[c + 1]) : key;
                _table[key] = perLang;
            }
        }

        private static void MergeSideFiles()
        {
            var dir = System.IO.Path.GetDirectoryName(FilePath);
            if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) return;

            var added = new List<string>();
            foreach (var path in System.IO.Directory.GetFiles(dir, "*.bak"))
            {
                if (string.Equals(path, FilePath, StringComparison.OrdinalIgnoreCase)) continue;

                string[] lines;
                try
                {
                    lines = System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8)
                        .Replace("\r\n", "\n").Split('\n');
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogWarning($"skipped {System.IO.Path.GetFileName(path)}: {ex.Message}");
                    continue;
                }
                if (lines.Length < 2) continue;

                var head = lines[0].Split('\t');
                var langs = new string[head.Length - 1];
                for (int c = 1; c < head.Length; c++)
                {
                    var code = head[c].Trim();
                    if (code.Length == 0)
                        code = System.IO.Path.GetFileNameWithoutExtension(path);
                    langs[c - 1] = code;
                }
                if (langs.Length == 0) continue;

                int rows = 0;
                for (int i = 1; i < lines.Length; i++)
                {
                    if (lines[i].Length == 0) continue;
                    var cols = lines[i].Split('\t');
                    string key = Unescape(cols[0]);
                    if (key.Length == 0) continue;
                    if (!_table.TryGetValue(key, out var perLang))
                    {
                        perLang = new Dictionary<string, string>();
                        _table[key] = perLang;
                    }
                    for (int c = 0; c < langs.Length; c++)
                    {
                        if (c + 1 >= cols.Length) continue;
                        var val = Unescape(cols[c + 1]);
                        if (val.Length == 0) continue;
                        perLang[langs[c]] = val;
                    }
                    rows++;
                }

                foreach (var code in langs)
                    if (Array.IndexOf(_languages, code) < 0 && !added.Contains(code))
                        added.Add(code);

                Plugin.Log?.LogInfo($"picked up {System.IO.Path.GetFileName(path)}: {string.Join("/", langs)}, {rows} rows");
            }

            if (added.Count == 0) return;

            var merged = new List<string>(_languages);
            merged.RemoveAll(l => l == DEBUG_LANG);
            merged.AddRange(added);
            merged.Add(DEBUG_LANG);
            _languages = merged.ToArray();
        }

        private static string Unescape(string s) => s.Replace("\\n", "\n").Replace("\\t", "\t");

        public static string Get(string key)
        {
            if (key == null) return "";
            if (_current == DEBUG_LANG) return key;
            if (!_table.TryGetValue(key, out var perLang)) return key;
            if (perLang.TryGetValue(_current, out var v) && v.Length > 0) return v;
            if (perLang.TryGetValue("en", out var en) && en.Length > 0) return en;
            return key;
        }

        // Get(id) with the result run through string.Format — for status/tooltip text with runtime
        // values baked in ("reading replays... {0}/{1}"). the table stores the format string itself.
        public static string Format(string key, params object[] args) => string.Format(Get(key), args);

        public static void SetLanguage(string lang)
        {
            if (lang == _current || Array.IndexOf(_languages, lang) < 0) return;
            _current = lang;
            SettingsService.Set(SETTINGS_KEY, lang);
            _bindings.RemoveAll(b => b.Text == null);
            foreach (var b in _bindings) ApplyBinding(b);
            LanguageChanged?.Invoke();
        }

        public static void CycleLanguage(int delta)
        {
            if (_languages.Length == 0) return;
            int idx = Array.IndexOf(_languages, _current);
            idx = ((idx + delta) % _languages.Length + _languages.Length) % _languages.Length;
            SetLanguage(_languages[idx]);
        }
    }
}
