using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace BetterFG.Editor
{
    public static class SkinInfoJson
    {
        static readonly System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;

        public static void Write(string outputDir, string bundleFileName, string name, string author, string description, string group, string type,
            bool keepBase = false, float skinScale = 0f,
            float itemScale = 1f,
            string leftBoneName = null, string rightBoneName = null,
            Vector3 leftPos = default, Vector3 leftRot = default,
            Vector3 rightPos = default, Vector3 rightRot = default,
            List<(string boneName, Vector3 localPos)> boneOffsets = null)
        {
            var lines = new List<string>
            {
                JStr("name",        name),
                JStr("author",      author),
                JStr("description", description),
                JStr("file",        bundleFileName),
                JStr("type",        type),
            };
            if (!string.IsNullOrWhiteSpace(group)) lines.Add(JStr("group", group));

            if (type == "costume")
            {
                lines.Add(JBool("keepBase", keepBase));
                if (skinScale > 0f) lines.Add(JFloat("skinScale", skinScale));
            }

            if (type == "item")
            {
                lines.Add(JFloat("scale", itemScale));
                lines.Add("  \"left\":  { \"bone\": \"" + EscJ(leftBoneName) + "\", \"position\": [" + Fv(leftPos.x) + ", " + Fv(leftPos.y) + ", " + Fv(leftPos.z) + "], \"rotation\": [" + Fv(leftRot.x) + ", " + Fv(leftRot.y) + ", " + Fv(leftRot.z) + "] }");
                lines.Add("  \"right\": { \"bone\": \"" + EscJ(rightBoneName) + "\", \"position\": [" + Fv(rightPos.x) + ", " + Fv(rightPos.y) + ", " + Fv(rightPos.z) + "], \"rotation\": [" + Fv(rightRot.x) + ", " + Fv(rightRot.y) + ", " + Fv(rightRot.z) + "] }");
            }

            bool hasBones = boneOffsets != null && boneOffsets.Count > 0;

            var sb = new StringBuilder();
            sb.AppendLine("{");

            for (int i = 0; i < lines.Count; i++)
                sb.AppendLine(lines[i] + (i < lines.Count - 1 || hasBones ? "," : ""));

            if (hasBones)
            {
                sb.AppendLine("  \"boneOffsets\": [");
                for (int i = 0; i < boneOffsets.Count; i++)
                {
                    var (boneName, p) = boneOffsets[i];
                    sb.AppendLine("    { \"bone\": \"" + EscJ(boneName) + "\", \"x\": " + Fv(p.x) + ", \"y\": " + Fv(p.y) + ", \"z\": " + Fv(p.z) + " }" + (i < boneOffsets.Count - 1 ? "," : ""));
                }
                sb.AppendLine("  ]");
            }

            sb.AppendLine("}");
            File.WriteAllText(Path.Combine(outputDir, "info.json"), sb.ToString(), Encoding.UTF8);
        }

        public static string ReadStr(string json, string key)
        {
            string search = "\"" + key + "\"";
            int ki = json.IndexOf(search);
            if (ki < 0) return "";
            int colon = json.IndexOf(':', ki + search.Length);
            if (colon < 0) return "";
            int q1 = json.IndexOf('"', colon + 1);
            if (q1 < 0) return "";
            int q2 = json.IndexOf('"', q1 + 1);
            if (q2 < 0) return "";
            return json.Substring(q1 + 1, q2 - q1 - 1).Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        public static bool ReadBool(string json, string key)
        {
            string search = "\"" + key + "\"";
            int ki = json.IndexOf(search);
            if (ki < 0) return false;
            int colon = json.IndexOf(':', ki + search.Length);
            if (colon < 0) return false;
            int vs = colon + 1;
            while (vs < json.Length && json[vs] == ' ') vs++;
            return vs < json.Length && json[vs] == 't';
        }

        public static float ReadFloat(string json, string key, float fallback = 0f)
        {
            string search = "\"" + key + "\"";
            int ki = json.IndexOf(search);
            if (ki < 0) return fallback;
            int colon = json.IndexOf(':', ki + search.Length);
            if (colon < 0) return fallback;
            int vs = colon + 1;
            while (vs < json.Length && (json[vs] == ' ' || json[vs] == '\n' || json[vs] == '\r')) vs++;
            int ve = vs;
            while (ve < json.Length && (char.IsDigit(json[ve]) || json[ve] == '.' || json[ve] == '-' || json[ve] == 'E' || json[ve] == 'e' || json[ve] == '+')) ve++;
            if (ve == vs) return fallback;
            return float.TryParse(json.Substring(vs, ve - vs), System.Globalization.NumberStyles.Float, ci, out float r) ? r : fallback;
        }

        public static string ReadItemBoneName(string json, string handKey)
        {
            string handObj = ReadObject(json, handKey);
            return string.IsNullOrEmpty(handObj) ? "" : ReadStr(handObj, "bone");
        }

        private static string ReadObject(string json, string key)
        {
            string search = "\"" + key + "\"";
            int ki = json.IndexOf(search);
            if (ki < 0) return "";

            int colon = json.IndexOf(':', ki + search.Length);
            if (colon < 0) return "";

            int objStart = json.IndexOf('{', colon + 1);
            if (objStart < 0) return "";

            int depth = 0;
            for (int i = objStart; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return json.Substring(objStart, i - objStart + 1);
                }
            }

            return "";
        }

        static string JStr(string k, string v) => "  \"" + k + "\": \"" + EscJ(v) + "\"";
        static string JBool(string k, bool v) => "  \"" + k + "\": " + (v ? "true" : "false");
        static string JFloat(string k, float v) => "  \"" + k + "\": " + v.ToString("G6", ci);
        static string Fv(float v) => v.ToString("G6", ci);
        static string EscJ(string s) => s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
    }
}
