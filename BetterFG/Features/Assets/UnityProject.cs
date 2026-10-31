using System;
using System.IO;
using BetterFG.Services;

namespace BetterFG.Features.Assets
{
    public static class UnityProject
    {
        public const string Key = "asset_export.project_assets";

        public static string AssetsPath => SettingsService.Get(Key, "");

        public static bool Configured
        {
            get
            {
                string p = AssetsPath;
                return !string.IsNullOrEmpty(p) && Directory.Exists(p);
            }
        }

        public static void Set(string assetsPath) => SettingsService.Set(Key, assetsPath ?? "");

        public static string Resolve(string picked)
        {
            if (string.IsNullOrEmpty(picked)) return null;
            string dir = picked.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(dir)) return null;

            if (string.Equals(Path.GetFileName(dir), "Assets", StringComparison.OrdinalIgnoreCase))
                return dir;

            string nested = Path.Combine(dir, "Assets");
            if (Directory.Exists(nested)) return nested;

            return null;
        }
    }
}
