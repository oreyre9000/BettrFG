using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Customization.Player;
using BetterFG.Utilities;
using UnityEngine.Networking;

namespace BetterFG.Services
{
    public static class UsageStats
    {
        private const string HitBase = "https://stats.bettrfg.com/hit/";
        private const string CountsBase = "https://stats.bettrfg.com/counts/";
        private const string RawPrefix = "https://raw.githubusercontent.com/";
        public const string KEY_ON = "privacy.usage_stats";
        private static readonly HttpClient Http = new HttpClient();
        private static readonly Dictionary<string, string> _counts = new Dictionary<string, string>();
        private static readonly Dictionary<string, List<Action>> _waiting = new Dictionary<string, List<Action>>();

        public static bool On
        {
            get => SettingsService.Get(KEY_ON, "true") != "false";
            set => SettingsService.Set(KEY_ON, value ? "true" : "false");
        }

        private static bool TryKey(SkinInfo skin, out string repo, out string pack)
        {
            repo = pack = null;
            if (skin.isLocalImport) return false;
            string raw = RepoRegistry.ResolveRaw(skin.sourceRepo);
            if (!raw.StartsWith(RawPrefix, StringComparison.OrdinalIgnoreCase)) return false;
            raw = raw.Substring(RawPrefix.Length).TrimEnd('/');
            if (raw.EndsWith("/main", StringComparison.Ordinal)) raw = raw.Substring(0, raw.Length - 5);
            repo = raw;
            pack = string.IsNullOrEmpty(skin.repoFolder) ? skin.file : skin.repoFolder;
            return repo.Length + pack.Length < 100;
        }

        public static void Applied(SkinInfo skin)
        {
            if (!On || !TryKey(skin, out var repo, out var pack)) return;
            Http.GetAsync(HitBase + string.Join("/", Array.ConvertAll((repo + "/" + pack).Split('/'), Uri.EscapeDataString)))
                .ContinueWith(t => { if (t.IsFaulted) Plugin.Log.LogDebug($"usage ping didn't go out, {t.Exception?.GetBaseException().Message}"); });
        }

        public static void Count(SkinInfo skin, Action<int> got)
        {
            if (!TryKey(skin, out var repo, out var pack)) return;
            if (_counts.TryGetValue(repo, out var have)) { got(JsonUtil.GetInt(have, pack)); return; }
            if (!_waiting.TryGetValue(repo, out var list))
            {
                _waiting[repo] = list = new List<Action>();
                AssetManager.Instance.StartCoroutine(Fetch(repo).WrapToIl2Cpp());
            }
            list.Add(() => { if (_counts.TryGetValue(repo, out var json)) got(JsonUtil.GetInt(json, pack)); });
        }

        public static string Label(int n) =>
            LocalizationService.Format(n == 1 ? "usage.wears_one" : "usage.wears_many",
                n >= 999_950 ? (n / 1_000_000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "M"
                : n >= 1000 ? (n / 1000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "K"
                : n.ToString());

        private static IEnumerator Fetch(string repo)
        {
            var req = UnityWebRequest.Get(CountsBase + repo + ".json");
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success) _counts[repo] = req.downloadHandler.text;
            else Plugin.Log.LogInfo($"no application counts for {repo} ({req.responseCode})");
            req.Dispose();
            var list = _waiting[repo];
            _waiting.Remove(repo);
            foreach (var a in list) a();
        }
    }
}
