using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace BetterFG.Editor
{
    internal static class ShipLoc
    {
        private const string PREF = "BetterFG.Creator.Language";
        private const string DEBUG_LANG = "debug";

        private static Dictionary<string, Dictionary<string, string>> _table;
        private static List<string> _langs;
        private static string _current;

        public static IReadOnlyList<string> Languages { get { Load(); return _langs; } }
        public static string Current { get { Load(); return _current; } }

        private static void Load()
        {
            if (_table != null) return;
            _table = new Dictionary<string, Dictionary<string, string>>();
            _langs = new List<string>();
            string raw;
            using (var s = typeof(ShipLoc).Assembly.GetManifestResourceStream("BetterFG.Creator.localization.bak"))
            using (var r = new StreamReader(s, System.Text.Encoding.UTF8))
                raw = r.ReadToEnd();

            var lines = raw.Replace("\r\n", "\n").Split('\n');
            var head = lines[0].Split('\t');
            _langs.AddRange(head);
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;
                var cols = lines[i].Split('\t');
                var perLang = new Dictionary<string, string>();
                for (int c = 0; c < head.Length; c++)
                    if (c + 1 < cols.Length && cols[c + 1].Length > 0)
                        perLang[head[c]] = cols[c + 1].Replace("\\n", "\n").Replace("\\t", "\t");
                _table[cols[0]] = perLang;
            }
            _langs.Add(DEBUG_LANG);

            _current = EditorPrefs.GetString(PREF, "en");
            if (!_langs.Contains(_current)) _current = "en";
        }

        public static string Get(string key)
        {
            Load();
            if (key == null) return "";
            if (_current == DEBUG_LANG || !_table.TryGetValue(key, out var perLang)) return key;
            if (perLang.TryGetValue(_current, out var v)) return v;
            return perLang.TryGetValue("en", out var en) ? en : key;
        }

        public static string Format(string key, params object[] args) => string.Format(Get(key), args);

        public static string Name(string lang)
        {
            Load();
            if (lang == DEBUG_LANG) return "Debug";
            return _table.TryGetValue("lang.name", out var n) && n.TryGetValue(lang, out var v) ? v : lang;
        }

        public static void Set(string lang)
        {
            Load();
            _current = lang;
            EditorPrefs.SetString(PREF, lang);
        }
    }
}
