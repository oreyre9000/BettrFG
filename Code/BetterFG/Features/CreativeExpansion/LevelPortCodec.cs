using System;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class LevelPortCodec
    {
        private const string Header = "BFGLEVEL/1";

        internal static string Encode(string json)
            => Header + "\n" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));

        internal static bool TryDecode(string text, out string json)
        {
            json = null;
            if (string.IsNullOrEmpty(text)) return false;

            int nl = text.IndexOf('\n');
            if (nl < 0 || text.Substring(0, nl).Trim() != Header) return false;

            try { json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(text.Substring(nl + 1).Trim())); }
            catch { return false; }

            return json.TrimStart().StartsWith("{", StringComparison.Ordinal);
        }
    }
}
