using System;
using System.Collections;
using System.Collections.Generic;
using BetterFG.Utilities;
using UnityEngine;

namespace BetterFG.Services
{
    public static class AddressablesCatalogService
    {
        public static List<string> Keys { get; private set; }

        // key -> catalog resource type class name (e.g. "UnityEngine.Texture2D"), built straight from
        // the catalog's own entry table so classification is exact instead of guessed from a filename
        // extension. a lot of real textures (generated sprite atlas pages, mainly) have no path at all
        // and are keyed by their asset GUID - extension guessing can never find those.
        private static Dictionary<string, string> _keyType;

        public static IEnumerator LoadRoutine()
        {
            if (Keys != null) yield break;

            string path = System.IO.Path.Combine(Application.streamingAssetsPath, "aa", "catalog.bundle");
            if (!System.IO.File.Exists(path))
            {
                Plugin.Log.LogWarning("no catalog.bundle at " + path);
                yield break;
            }

            var ab = Bundles.LoadMemorySync("bettrfg_catalog", System.IO.File.ReadAllBytes(path));
            if (ab == null) { Plugin.Log.LogWarning("catalog.bundle wouldn't open"); yield break; }
            yield return null;

            string json = null;
            var names = ab.GetAllAssetNames();
            for (int i = 0; i < names.Length && json == null; i++)
            {
                var ta = ab.LoadAsset(names[i]).TryCast<TextAsset>();
                if (ta != null) json = ta.text;
            }
            Bundles.Unload("bettrfg_catalog", true);
            yield return null;

            if (string.IsNullOrEmpty(json)) { Plugin.Log.LogWarning("catalog bundle had no text asset in it"); yield break; }

            ParseCatalog(json);
            Plugin.Log.LogInfo($"catalog read, {Keys.Count} addressable keys, {_keyType.Count} type-classified");
        }

        private static void ParseCatalog(string json)
        {
            Keys = new List<string>();
            _keyType = new Dictionary<string, string>();

            var keyData = ReadB64Field(json, "m_KeyDataString");
            var bucketData = ReadB64Field(json, "m_BucketDataString");
            if (keyData == null || bucketData == null) return;

            int bucketCount = BitConverter.ToInt32(bucketData, 0);
            int bi = 4;
            var keysByIndex = new string[bucketCount];
            for (int i = 0; i < bucketCount; i++)
            {
                if (bi + 8 > bucketData.Length) break;
                int offset = BitConverter.ToInt32(bucketData, bi); bi += 4;
                int entryCount = BitConverter.ToInt32(bucketData, bi); bi += 4;
                bi += entryCount * 4;

                string key = ReadKeyString(keyData, offset);
                keysByIndex[i] = key;
                if (!string.IsNullOrEmpty(key)) Keys.Add(key);
            }

            var entryData = ReadB64Field(json, "m_EntryDataString");
            var typeNames = ReadResourceTypeNames(json);
            if (entryData == null || typeNames == null) return;

            const int itemsPerEntry = 7, stride = itemsPerEntry * 4;
            int entryTotal = BitConverter.ToInt32(entryData, 0);
            for (int i = 0; i < entryTotal; i++)
            {
                int b = 4 + i * stride;
                if (b + stride > entryData.Length) break;
                int primaryKeyIdx = BitConverter.ToInt32(entryData, b + 20);
                int resourceTypeIdx = BitConverter.ToInt32(entryData, b + 24);
                if (primaryKeyIdx < 0 || primaryKeyIdx >= keysByIndex.Length) continue;
                if (resourceTypeIdx < 0 || resourceTypeIdx >= typeNames.Length) continue;
                string key = keysByIndex[primaryKeyIdx];
                if (string.IsNullOrEmpty(key)) continue;
                if (_keyType.TryGetValue(key, out var already) && already == "UnityEngine.Texture2D") continue;
                _keyType[key] = typeNames[resourceTypeIdx];
            }
        }

        // mirrors JsonContentCatalogData's own compact object encoding (Addressables runtime source):
        // a type byte followed by type-specific data. ascii/unicode are addressable path strings,
        // the rest (uint16/uint32/int32/hash128) are keys Addressables assigned when nothing readable
        // was available - most commonly a Hash128 GUID for a build-generated asset like an atlas page.
        private static string ReadKeyString(byte[] keyData, int offset)
        {
            if (offset < 0 || offset >= keyData.Length) return null;
            byte type = keyData[offset];
            int at = offset + 1;
            switch (type)
            {
                case 0:
                {
                    int len = BitConverter.ToInt32(keyData, at);
                    if (len < 0 || at + 4 + len > keyData.Length) return null;
                    return System.Text.Encoding.ASCII.GetString(keyData, at + 4, len);
                }
                case 1:
                {
                    int len = BitConverter.ToInt32(keyData, at);
                    if (len < 0 || at + 4 + len > keyData.Length) return null;
                    return System.Text.Encoding.Unicode.GetString(keyData, at + 4, len);
                }
                case 2: return at + 2 <= keyData.Length ? BitConverter.ToUInt16(keyData, at).ToString() : null;
                case 3: return at + 4 <= keyData.Length ? BitConverter.ToUInt32(keyData, at).ToString() : null;
                case 4: return at + 4 <= keyData.Length ? BitConverter.ToInt32(keyData, at).ToString() : null;
                case 5:
                {
                    int len = keyData[at];
                    if (at + 1 + len > keyData.Length) return null;
                    return System.Text.Encoding.ASCII.GetString(keyData, at + 1, len);
                }
                default: return null;
            }
        }

        private static string[] ReadResourceTypeNames(string json)
        {
            var objs = JsonUtil.GetArray(json, "m_resourceTypes");
            if (objs.Count == 0) return null;
            var names = new string[objs.Count];
            for (int i = 0; i < objs.Count; i++)
                names[i] = JsonUtil.ReadFlatObject(objs[i]).TryGetValue("m_ClassName", out var c) ? c : "";
            return names;
        }

        private static byte[] ReadB64Field(string json, string field)
        {
            string tag = "\"" + field + "\":\"";
            int at = json.IndexOf(tag, StringComparison.Ordinal);
            if (at < 0) return null;
            at += tag.Length;
            int end = json.IndexOf('"', at);
            if (end < 0) return null;
            try { return Convert.FromBase64String(json.Substring(at, end - at)); }
            catch { return null; }
        }

        public static bool IsImage(string key)
        {
            if (_keyType != null && _keyType.TryGetValue(key, out var t)) return t == "UnityEngine.Texture2D";
            return key.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                key.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                key.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ||
                key.EndsWith(".psd", StringComparison.OrdinalIgnoreCase) ||
                key.EndsWith(".exr", StringComparison.OrdinalIgnoreCase) ||
                IsCubemap(key);
        }

        public static bool IsCubemap(string key) =>
            key.EndsWith(".hdr", StringComparison.OrdinalIgnoreCase);

        public static bool IsBank(string key) => key.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase);

        public static bool IsMaterial(string key) => key.EndsWith(".mat", StringComparison.OrdinalIgnoreCase);
    }
}
