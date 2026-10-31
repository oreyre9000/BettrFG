using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BetterFG.Utilities
{
    internal static class UntrustedInput
    {
        private static readonly Regex BadToken = new Regex(
            @"(?<=^|[|,])\s*(?:[\\/]{2}|(?!https://(?:raw\.githubusercontent|github)\.com/)[a-zA-Z][a-zA-Z0-9+.\-]*://)[^|,]*",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool IsGithubUrl(string url) =>
            !string.IsNullOrEmpty(url)
            && (url.StartsWith("https://raw.githubusercontent.com/", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase));

        public static string RepoUrl(string url) => IsGithubUrl(url) ? url : "";

        public static string AssetUrl(string rawBase, string file)
        {
            string path = file.Replace("\\", "/").TrimStart('/');
            return IsGithubUrl(path) ? path : $"{rawBase}/{path.Replace("..", "")}";
        }

        public static string LocalPath(string path) =>
            !string.IsNullOrEmpty(path) && path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':'
            && (path[2] == '\\' || path[2] == '/') ? path : "";

        public static string SettingValue(string value) =>
            string.IsNullOrEmpty(value) ? value : BadToken.Replace(value, "");

        public static void SettingValues(Dictionary<string, string> values)
        {
            foreach (var k in new List<string>(values.Keys)) values[k] = SettingValue(values[k]);
        }
    }
}
