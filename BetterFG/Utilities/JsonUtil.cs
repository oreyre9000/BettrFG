using System.Collections.Generic;

namespace BetterFG.Utilities
{
    internal static class JsonUtil
    {
        public static System.Text.Json.JsonElement Parse(string json) => System.Text.Json.JsonDocument.Parse(json).RootElement;

        public static string GetValue(string json, string key)
        {
            string searchKey = $"\"{key}\":";
            int start = json.IndexOf(searchKey);
            if (start == -1) return "";
            start += searchKey.Length;
            while (start < json.Length && (json[start] == ' ' || json[start] == '\t')) start++;
            if (start >= json.Length || json[start] != '"') return "";
            return ReadString(json, start, out _);
        }

        public static string ReadString(string json, int openQuote, out int afterCloseQuote)
        {
            afterCloseQuote = openQuote;
            if (openQuote >= json.Length || json[openQuote] != '"') return "";
            var sb = new System.Text.StringBuilder();
            for (int i = openQuote + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\')
                {
                    if (++i >= json.Length) break;
                    char e = json[i];
                    if (e == 'n') sb.Append('\n');
                    else if (e == 'r') sb.Append('\r');
                    else if (e == 't') sb.Append('\t');
                    else if (e == 'u' && i + 4 < json.Length
                             && int.TryParse(json.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber,
                                             System.Globalization.CultureInfo.InvariantCulture, out int cp))
                    { sb.Append((char)cp); i += 4; }
                    else sb.Append(e);
                    continue;
                }
                if (c == '"') { afterCloseQuote = i + 1; return sb.ToString(); }
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static Dictionary<string, string> ReadFlatObject(string obj)
        {
            var dict = new Dictionary<string, string>(System.StringComparer.Ordinal);
            if (string.IsNullOrEmpty(obj)) return dict;
            int i = 0;
            while (i < obj.Length)
            {
                int k0 = obj.IndexOf('"', i);
                if (k0 < 0) break;
                string k = ReadString(obj, k0, out int afterKey);
                int colon = obj.IndexOf(':', afterKey);
                if (colon < 0) break;
                int v0 = obj.IndexOf('"', colon + 1);
                if (v0 < 0) break;
                dict[k] = ReadString(obj, v0, out i);
            }
            return dict;
        }

        public static float GetFloat(string json, string key, float def = 0f)
        {
            string sk = $"\"{key}\":";
            int i = json.IndexOf(sk);
            if (i == -1) return def;
            i += sk.Length;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
            int e = i;
            while (e < json.Length && "0123456789+-.eE".IndexOf(json[e]) != -1) e++;
            if (e == i) return def;
            return float.TryParse(json.Substring(i, e - i),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : def;
        }

        public static int GetInt(string json, string key, int def = 0)
        {
            return (int)GetFloat(json, key, def);
        }

        public static bool GetBool(string json, string key, bool def = false)
        {
            string sk = $"\"{key}\":";
            int i = json.IndexOf(sk);
            if (i == -1) return def;
            i += sk.Length;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
            if (i + 3 < json.Length && json.Substring(i, 4) == "true") return true;
            if (i + 4 < json.Length && json.Substring(i, 5) == "false") return false;
            return def;
        }

        public static string GetObject(string json, string key)
        {
            string sk = $"\"{key}\":";
            int idx = 0;
            while (true)
            {
                int ki = json.IndexOf(sk, idx);
                if (ki == -1) return null;
                int start = ki + sk.Length;
                while (start < json.Length && (json[start] == ' ' || json[start] == '\t')) start++;
                if (start < json.Length && json[start] == '{')
                {
                    int objEnd = FindMatchingBrace(json, start, '{', '}');
                    return objEnd == -1 ? null : json.Substring(start, objEnd - start + 1);
                }
                idx = ki + sk.Length;
            }
        }

        public static List<string> GetArray(string json, string key)
        {
            var result = new List<string>();
            string sk = $"\"{key}\":";
            int idx = 0;
            while (true)
            {
                int ki = json.IndexOf(sk, idx);
                if (ki == -1) return result;
                int start = ki + sk.Length;
                while (start < json.Length && (json[start] == ' ' || json[start] == '\t')) start++;
                if (start < json.Length && json[start] == '[')
                {
                    int arrEnd = FindMatchingBrace(json, start, '[', ']');
                    if (arrEnd != -1) ParseObjectsFromArray(json.Substring(start + 1, arrEnd - start - 1), result);
                    return result;
                }
                idx = ki + sk.Length;
            }
        }

        public static List<string> GetRootArray(string json)
        {
            var result = new List<string>();
            int arrStart = json.IndexOf('[');
            if (arrStart == -1) return result;
            int arrEnd = FindMatchingBrace(json, arrStart, '[', ']');
            if (arrEnd == -1) return result;
            ParseObjectsFromArray(json.Substring(arrStart + 1, arrEnd - arrStart - 1), result);
            return result;
        }

        private static void ParseObjectsFromArray(string inner, List<string> result)
        {
            int idx = 0;
            while (idx < inner.Length)
            {
                int os = inner.IndexOf('{', idx);
                if (os == -1) break;
                int oe = FindMatchingBrace(inner, os, '{', '}');
                if (oe == -1) break;
                result.Add(inner.Substring(os, oe - os + 1));
                idx = oe + 1;
            }
        }

        private static int FindMatchingBrace(string s, int start, char open, char close)
        {
            int depth = 0;
            bool inStr = false;
            for (int i = start; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && inStr) { i++; continue; }
                if (c == '"') { inStr = !inStr; continue; }
                if (inStr) continue;
                if (c == open) depth++;
                else if (c == close) { depth--; if (depth == 0) return i; }
            }
            return -1;
        }
    }
}