using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BetterFG.Utilities
{
    public static class Bundles
    {
        private static readonly Dictionary<string, AssetBundle> _byKey = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);

        public static AssetBundle Get(string key)
            => !string.IsNullOrEmpty(key) && _byKey.TryGetValue(key, out var ab) && ab != null ? ab : null;

        public static bool TryGet(string key, out AssetBundle ab) { ab = Get(key); return ab != null; }

        public static void Register(string key, AssetBundle ab)
        {
            if (string.IsNullOrEmpty(key) || ab == null) return;
            _byKey[key] = ab;
        }

        public static IEnumerator LoadFile(string key, string path, Action<AssetBundle> done)
        {
            var cached = Get(key);
            if (cached != null) { done?.Invoke(cached); yield break; }

            var req = AssetBundle.LoadFromFileAsync(path);
            yield return req;
            var ab = req.assetBundle;
            if (ab != null && !string.IsNullOrEmpty(key)) _byKey[key] = ab;
            done?.Invoke(ab);
        }

        public static IEnumerator LoadMemory(string key, byte[] bytes, Action<AssetBundle> done)
        {
            var cached = Get(key);
            if (cached != null) { done?.Invoke(cached); yield break; }

            var req = AssetBundle.LoadFromMemoryAsync(bytes);
            yield return req;
            var ab = req.assetBundle;
            if (ab != null && !string.IsNullOrEmpty(key)) _byKey[key] = ab;
            done?.Invoke(ab);
        }

        public static AssetBundle LoadMemorySync(string key, byte[] bytes)
        {
            var cached = Get(key);
            if (cached != null) return cached;

            AssetBundle ab;
            try { ab = AssetBundle.LoadFromMemory(bytes); } catch { ab = null; }
            if (ab != null && !string.IsNullOrEmpty(key)) _byKey[key] = ab;
            return ab;
        }

        public static void Unload(string key, bool unloadAllObjects)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!_byKey.TryGetValue(key, out var ab)) return;
            try { if (ab != null) ab.Unload(unloadAllObjects); } catch { }
            _byKey.Remove(key);
        }
    }

    internal class EmbeddedResourceandUnity
    {
        static Assembly asm = Assembly.GetExecutingAssembly();

        public static Stream Open(string resourcePath)
        {
            var s = asm.GetManifestResourceStream(resourcePath);
            if (s == null) return null;
            bool gz = s.ReadByte() == 0x1f && s.ReadByte() == 0x8b;
            s.Position = 0;
            if (!gz) return s;
            var ms = new MemoryStream();
            using (var z = new System.IO.Compression.GZipStream(s, System.IO.Compression.CompressionMode.Decompress)) z.CopyTo(ms);
            ms.Position = 0;
            return ms;
        }

        public static byte[] Bytes(string resourcePath)
        {
            using var s = Open(resourcePath);
            if (s == null) return null;
            if (s is MemoryStream m) return m.ToArray();
            var b = new byte[s.Length];
            s.Read(b, 0, b.Length);
            return b;
        }

        public static AssetBundle Bundle(string resourcePath)
        {
            var b = Bytes(resourcePath);
            return b == null ? null : AssetBundle.LoadFromMemory(b);
        }

        public static Texture2D LoadTexture(string resourcePath) => LoadTexture(resourcePath, false);

        public static Texture2D LoadTexture(string resourcePath, bool mips)
        {
            if (Atlas.Has(resourcePath))
            {
                var atlased = Atlas.Texture(resourcePath, mips);
                atlased.filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear;
                atlased.wrapMode = TextureWrapMode.Clamp;
                atlased.hideFlags = HideFlags.HideAndDontSave;
                return atlased;
            }

            using (Stream stream = asm.GetManifestResourceStream(resourcePath))
            {
                if (stream == null)
                {
                    Plugin.Log.LogWarning($"EmbeddedRes: no resource at '{resourcePath}'");
                    return null;
                }

                byte[] data = new byte[stream.Length];
                stream.Read(data, 0, data.Length);

                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, mips);
                tex.LoadImage(data);
                tex.filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear;
                // default Repeat wrap bleeds a 1px sliver of the opposite edge into bilinear-sampled UVs
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.Apply();
                // keep it alive across scene unloads so cached references don't go dead between rounds
                tex.hideFlags = HideFlags.HideAndDontSave;
                return tex;
            }
        }

        public static string LoadText(string resourcePath)
        {
            using (Stream stream = Open(resourcePath))
            {
                if (stream == null)
                {
                    Plugin.Log.LogWarning($"EmbeddedRes: no resource at '{resourcePath}'");
                    return null;
                }

                using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8))
                    return reader.ReadToEnd();
            }
        }

        public static Sprite LoadSprite(string resourcePath, float pixelsPerUnit = 100f)
        {
            if (Atlas.Has(resourcePath)) return Atlas.Sprite(resourcePath, new Vector2(0.5f, 0.5f), pixelsPerUnit);

            Texture2D tex = LoadTexture(resourcePath);
            if (tex == null) return null;

            var sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit,
                0,
                SpriteMeshType.FullRect
            );
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        public static Sprite LoadSprite(string resourcePath, Vector2 pivot, Vector4 border)
        {
            if (Atlas.Has(resourcePath)) return Atlas.Sprite(resourcePath, pivot, 100f, border);

            Texture2D tex = LoadTexture(resourcePath);
            if (tex == null) return null;

            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, 100f, 0, SpriteMeshType.FullRect, border);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static readonly System.Collections.Generic.Dictionary<int, Sprite> _texSprites = new System.Collections.Generic.Dictionary<int, Sprite>();

        public static Sprite SpriteFor(Texture2D tex)
        {
            int id = tex.GetInstanceID();
            if (_texSprites.TryGetValue(id, out var cached) && cached != null) return cached;
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            _texSprites[id] = sprite;
            return sprite;
        }

        public static Sprite LoadSprite(string resourcePath, Rect rect, Vector2 pivot, float pixelsPerUnit = 100f)
        {
            if (Atlas.Has(resourcePath)) return Atlas.Sprite(resourcePath, rect, pivot, pixelsPerUnit, Vector4.zero);

            Texture2D tex = LoadTexture(resourcePath);
            if (tex == null) return null;

            var sprite = Sprite.Create(tex, rect, pivot, pixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}