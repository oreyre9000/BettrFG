using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using BetterFG.Services;

namespace BetterFG.Customization.Presets
{
    public static class PresetService
    {
        private static readonly string[] PREFIXES =
            { "skin.", "skintex.", "allcosmetics.", "nametag.", "menu.", "screen." };

        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BettrFG", "Settings", "presets");

        public static List<string> List()
        {
            var names = new List<string>();
            if (!Directory.Exists(Dir)) return names;
            foreach (var f in Directory.GetFiles(Dir))
            {
                string ext = Path.GetExtension(f);
                if (!ext.Equals(EXT, StringComparison.OrdinalIgnoreCase) && !ext.Equals(".txt", StringComparison.OrdinalIgnoreCase)) continue;
                string n = Path.GetFileNameWithoutExtension(f);
                if (!names.Contains(n)) names.Add(n);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        public static void ToUser(string name, string userDir, string baseUserDir)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            string baseFile = Path.Combine(baseUserDir, "settings.txt");
            if (File.Exists(baseFile))
                foreach (var line in File.ReadAllLines(baseFile))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 1) continue;
                    string k = line.Substring(0, eq).Trim();
                    bool owned = false;
                    foreach (var pre in PREFIXES) if (k.StartsWith(pre, StringComparison.Ordinal)) { owned = true; break; }
                    if (!owned) values[k] = line.Substring(eq + 1).Trim();
                }

            foreach (var line in ReadLines(name, Path.Combine(userDir, "files")))
            {
                int eq = line.IndexOf('=');
                if (eq < 1) continue;
                values[line.Substring(0, eq).Trim()] = BetterFG.Utilities.UntrustedInput.SettingValue(line.Substring(eq + 1).Trim());
            }

            Directory.CreateDirectory(userDir);
            var lines = new List<string>();
            foreach (var kv in values)
                if (SettingsService.IsUserKey(kv.Key)) lines.Add(kv.Key + "=" + kv.Value);
            File.WriteAllLines(Path.Combine(userDir, "settings.txt"), lines);

            foreach (var f in SettingsService.UserFiles)
            {
                string src = Path.Combine(baseUserDir, f), dst = Path.Combine(userDir, f);
                if (File.Exists(src) && !File.Exists(dst)) File.Copy(src, dst);
            }
        }

        private static List<string> ReadLines(string name, string files)
        {
            string zipPath = Path.Combine(Dir, name + EXT);
            if (!File.Exists(zipPath)) return new List<string>(File.ReadAllLines(Path.Combine(Dir, name + ".txt")));

            var lines = new List<string>();
            using (var zip = ZipFile.OpenRead(zipPath))
                foreach (var e in zip.Entries)
                {
                    if (e.FullName == "preset.txt")
                    {
                        using var r = new StreamReader(e.Open());
                        for (string l; (l = r.ReadLine()) != null;) lines.Add(l);
                    }
                    else if (e.FullName.StartsWith(FILES, StringComparison.Ordinal) && e.Name.Length > 0)
                    {
                        Directory.CreateDirectory(files);
                        e.ExtractToFile(Path.Combine(files, Path.GetFileName(e.Name)), true);
                    }
                }

            for (int i = 0; i < lines.Count; i++)
            {
                int eq = lines[i].IndexOf('=');
                if (eq < 1) continue;
                string val = lines[i].Substring(eq + 1).Trim();
                if (val.StartsWith(FILES, StringComparison.Ordinal))
                    lines[i] = lines[i].Substring(0, eq) + "=" + Path.Combine(files, Path.GetFileName(val));
            }
            return lines;
        }

        private const string EXT = ".bfgpreset";
        private const string FILES = "files/";
    }
}
