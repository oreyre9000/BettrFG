using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BetterFG.Features.CreativeExpansion
{
    // Queued imports, keyed by level share code. The Import flow drops the file's JSON here; the
    // prefix on LevelLoader.CreateLevelLoaderFromDownloadedJSON (routed through
    // CreativeGameModePatches, same as the game-mode swap) substitutes it when that level next
    // loads. In memory only — a restart clears it, and once the user saves the level for real the
    // imported content is baked into its actual JSON so the swap just re-applies the same bytes.
    internal static class LevelPortImport
    {
        private static readonly Dictionary<string, string> _pending = new Dictionary<string, string>();

        internal static void Queue(string shareCode, string json)
        {
            if (!FeatureCreativeExpansion.On("levelport") || string.IsNullOrEmpty(shareCode) || string.IsNullOrEmpty(json)) return;
            _pending[shareCode] = json;
        }

        internal static bool HasPending(string shareCode)
            => !string.IsNullOrEmpty(shareCode) && _pending.ContainsKey(shareCode);

        internal static void RewriteJsonForLoad(ref string json, string dtoShareCode)
        {
            if (_pending.Count == 0) return;

            string code = dtoShareCode;
            if (string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(json))
            {
                var m = Regex.Match(json, "\"[Ss]hare[Cc]ode\"\\s*:\\s*\"([^\"]+)\"");
                if (m.Success) code = m.Groups[1].Value;
            }
            if (string.IsNullOrEmpty(code)) return;

            if (_pending.TryGetValue(code, out var imported) && !string.IsNullOrEmpty(imported))
            {
                Plugin.Log.LogInfo($"level import: swapping in {imported.Length} chars of JSON for {code}");
                json = imported;
            }
        }
    }
}
