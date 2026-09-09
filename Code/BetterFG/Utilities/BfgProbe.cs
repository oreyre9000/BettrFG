using System.Collections.Generic;
using BetterFG.Services;
using UnityEngine;

namespace BetterFG.Utilities
{
    public static class BfgProbe
    {
        private static bool _on;
        private static float _next;
        private static readonly Dictionary<string, int> _calls = new Dictionary<string, int>();

        public static void Hit(string name)
        {
            if (!_on) return;
            _calls.TryGetValue(name, out int n);
            _calls[name] = n + 1;
        }

        public static void Flush()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;

            bool want = SettingsService.Get("debug.probe", "false") == "true";
            if (want != _on)
            {
                _on = want;
                Plugin.Log.LogWarning(want
                    ? "patch probe on, one line a second until debug.probe goes false"
                    : "patch probe off");
                _calls.Clear();
                return;
            }
            if (!_on || _calls.Count == 0) return;

            var rows = new List<string>();
            foreach (var kv in _calls) rows.Add($"{kv.Key} {kv.Value}");
            rows.Sort();
            Plugin.Log.LogWarning("calls/sec: " + string.Join(", ", rows));
            _calls.Clear();
        }
    }
}
