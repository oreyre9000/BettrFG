using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using BetterFG.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Customization.TexturePacks
{
    public static class TexturePackRuntimeService
    {
        private const string OUR_PREFIX = "bfgtp_";

        private static readonly Dictionary<string, (byte[] colour, byte[] alpha)> _overrides =
            new Dictionary<string, (byte[], byte[])>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Texture2D> _sources =
            new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Texture2D> _sourcesMipped =
            new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Texture2D> _builtTextures =
            new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private static readonly Dictionary<int, Sprite> _builtSprites =
            new Dictionary<int, Sprite>();

        private static readonly Dictionary<string, Sprite> _originalSpritesByName =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Texture> _originalTexturesByName =
            new Dictionary<string, Texture>(StringComparer.Ordinal);
        private static readonly HashSet<int> _seen = new HashSet<int>();
        private static bool _ticking;

        public static bool Active => _overrides.Count > 0;
        public static string ActivePath { get; private set; } = "";

        public static void Clear()
        {
            Revert();
            ActivePath = "";
            SettingsService.Set("texpack.active", "");
        }

        private static void Revert()
        {
            _overrides.Clear();

            foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (img == null || img.sprite == null || !img.sprite.name.StartsWith(OUR_PREFIX, StringComparison.Ordinal)) continue;
                if (_originalSpritesByName.TryGetValue(img.sprite.name.Substring(OUR_PREFIX.Length), out var origSpr))
                    img.sprite = origSpr;
            }

            foreach (var raw in Resources.FindObjectsOfTypeAll<RawImage>())
            {
                if (raw == null || raw.texture == null || !raw.texture.name.StartsWith(OUR_PREFIX, StringComparison.Ordinal)) continue;
                if (_originalTexturesByName.TryGetValue(raw.texture.name.Substring(OUR_PREFIX.Length), out var origTex))
                    raw.texture = origTex;
            }

            foreach (var mat in Resources.FindObjectsOfTypeAll<Material>())
                foreach (int prop in mat.GetTexturePropertyNameIDs())
                {
                    var t = mat.GetTexture(prop);
                    if (t == null || !t.name.StartsWith(OUR_PREFIX, StringComparison.Ordinal)) continue;
                    if (_originalTexturesByName.TryGetValue(t.name.Substring(OUR_PREFIX.Length), out var origTex))
                        mat.SetTexture(prop, origTex);
                }

            foreach (var s in _builtSprites.Values) UnityEngine.Object.Destroy(s);
            foreach (var t in _builtTextures.Values) UnityEngine.Object.Destroy(t);
            foreach (var t in _sources.Values) UnityEngine.Object.Destroy(t);
            foreach (var t in _sourcesMipped.Values) UnityEngine.Object.Destroy(t);
            _builtSprites.Clear();
            _builtTextures.Clear();
            _sources.Clear();
            _sourcesMipped.Clear();
            _originalSpritesByName.Clear();
            _originalTexturesByName.Clear();
            _seen.Clear();
        }

        public static bool Load(string bfgtpPath, out string error)
        {
            error = null;
            var loaded = new Dictionary<string, (byte[] colour, byte[] alpha)>(StringComparer.Ordinal);
            try
            {
                using var zip = ZipFile.OpenRead(bfgtpPath);
                var texNameByBase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var manifest = zip.GetEntry("manifest.json");
                if (manifest != null)
                {
                    using var mr = new StreamReader(manifest.Open());
                    foreach (var obj in JsonUtil.GetArray(mr.ReadToEnd(), "files"))
                    {
                        var d = JsonUtil.ReadFlatObject(obj);
                        if (d.TryGetValue("texName", out var tn) && d.TryGetValue("fileName", out var fn) && !string.IsNullOrEmpty(tn))
                            texNameByBase[Path.GetFileNameWithoutExtension(fn)] = tn;
                    }
                }
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.StartsWith("Textures/", StringComparison.OrdinalIgnoreCase)) continue;
                    string baseName = Path.GetFileNameWithoutExtension(entry.Name);
                    bool isAlpha = baseName.EndsWith(".a", StringComparison.OrdinalIgnoreCase);
                    if (isAlpha) baseName = baseName.Substring(0, baseName.Length - 2);
                    string texName = texNameByBase.TryGetValue(baseName, out var mapped) ? mapped : baseName;
                    using var ms = new MemoryStream();
                    using (var s = entry.Open()) s.CopyTo(ms);
                    loaded.TryGetValue(texName, out var pair);
                    if (isAlpha) pair.alpha = ms.ToArray(); else pair.colour = ms.ToArray();
                    loaded[texName] = pair;
                }
            }
            catch (Exception ex) { error = ex.Message; return false; }

            int usable = 0;
            foreach (var kv in loaded) if (kv.Value.colour != null) usable++;
            if (usable == 0) { error = "pack had no textures in it"; return false; }

            Revert();
            foreach (var kv in loaded) if (kv.Value.colour != null) _overrides[kv.Key] = kv.Value;
            ActivePath = bfgtpPath;
            SettingsService.Set("texpack.active", bfgtpPath);
            Plugin.Log.LogInfo($"texture pack enabled, {_overrides.Count} textures overridden");
            SweepLoadedTextures();
            return true;
        }

        public static void LoadFromSettingsOnStartup()
        {
            string path = SettingsService.Get("texpack.active", "");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            if (!Load(path, out string err))
                Plugin.Log.LogWarning($"saved texture pack ({path}) wouldn't load: {err}");
        }

        public static void SweepLoadedTextures() => Sweep(true);

        private static void Sweep(bool full)
        {
            if (!Active) return;
            int patched = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            foreach (var mat in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (!_seen.Add(mat.GetInstanceID()) && !full) continue;
                foreach (int prop in mat.GetTexturePropertyNameIDs())
                {
                    var t = mat.GetTexture(prop);
                    if (t == null) continue;
                    string name = t.name;
                    if (string.IsNullOrEmpty(name) || name.StartsWith(OUR_PREFIX, StringComparison.Ordinal) || !_overrides.ContainsKey(name)) continue;
                    if (!_originalTexturesByName.ContainsKey(name)) _originalTexturesByName[name] = t;
                    mat.SetTexture(prop, Source(name, true));
                    patched++;
                }
            }

            foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (!_seen.Add(img.GetInstanceID()) && !full) continue;
                if (TryReplaceSprite(img)) patched++;
            }

            foreach (var raw in Resources.FindObjectsOfTypeAll<RawImage>())
            {
                if (!_seen.Add(raw.GetInstanceID()) && !full) continue;
                if (TryReplaceRawTexture(raw)) patched++;
            }

            if (patched > 0) Plugin.Log.LogInfo($"texture pack: swapped {patched} references in {sw.ElapsedMilliseconds}ms{(full ? "" : " (background pass)")}");

            if (!_ticking && BeanMonitorService.Instance != null)
            {
                _ticking = true;
                BeanMonitorService.Instance.StartCoroutine(Tick().WrapToIl2Cpp());
            }
        }

        private static System.Collections.IEnumerator Tick()
        {
            while (Active)
            {
                yield return new WaitForSeconds(1f);
                if (Active) Sweep(false);
            }
            _ticking = false;
        }

        public static bool TryReplaceSprite(Image img)
        {
            if (!Active || img == null) return false;
            var spr = img.sprite;
            if (spr == null || string.IsNullOrEmpty(spr.name) || spr.texture == null) return false;
            if (spr.name.StartsWith(OUR_PREFIX, StringComparison.Ordinal)) return false;
            string key = _overrides.ContainsKey(spr.name) ? spr.name
                       : _overrides.ContainsKey(spr.texture.name) ? spr.texture.name
                       : null;
            if (key == null) return false;

            var built = BuildSprite(spr, key);
            if (built == null) return false;
            if (!_originalSpritesByName.ContainsKey(spr.name)) _originalSpritesByName[spr.name] = spr;
            img.sprite = built;
            return true;
        }

        public static bool TryReplaceRawTexture(RawImage raw)
        {
            if (!Active || raw == null || raw.texture == null || string.IsNullOrEmpty(raw.texture.name)) return false;
            if (raw.texture.name.StartsWith(OUR_PREFIX, StringComparison.Ordinal)) return false;
            if (!_overrides.TryGetValue(raw.texture.name, out _)) return false;

            if (!_originalTexturesByName.ContainsKey(raw.texture.name)) _originalTexturesByName[raw.texture.name] = raw.texture;
            raw.texture = Source(raw.texture.name, false);
            return true;
        }

        private static Texture2D Source(string key, bool mips)
        {
            var cache = mips ? _sourcesMipped : _sources;
            if (cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var (colour, alpha) = _overrides[key];
            var tex = TexturePackProject.Decode(colour, alpha, mips, false);
            tex.name = OUR_PREFIX + key;
            UnityEngine.Object.DontDestroyOnLoad(tex);
            cache[key] = tex;
            return tex;
        }

        private static Texture2D BuildTexture(string key, int w, int h)
        {
            var src = Source(key, false);
            if (src.width == w && src.height == h) return src;
            string sized = $"{key}|{w}x{h}";
            if (_builtTextures.TryGetValue(sized, out var cached) && cached != null) return cached;

            var tex = TexturePackProject.Blit(src, w, h, false, false);
            tex.name = OUR_PREFIX + key;
            UnityEngine.Object.DontDestroyOnLoad(tex);
            _builtTextures[sized] = tex;
            return tex;
        }

        private static Sprite BuildSprite(Sprite original, string key)
        {
            int sid = original.GetInstanceID();
            if (_builtSprites.TryGetValue(sid, out var cached) && cached != null) return cached;

            int w = Mathf.Max(1, Mathf.RoundToInt(original.rect.width));
            int h = Mathf.Max(1, Mathf.RoundToInt(original.rect.height));
            Texture2D tex;
            if (key == original.name)
            {
                tex = BuildTexture(key, w, h);
            }
            else
            {
                Rect used;
                Vector2 offset;
                try { used = original.textureRect; offset = original.textureRectOffset; }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"{original.name} is packed too tightly to lift off {key}, skipping ({ex.Message})");
                    return null;
                }
                var atlas = original.texture;
                var packAtlas = BuildTexture(key, atlas.width, atlas.height);
                tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = OUR_PREFIX + original.name, hideFlags = HideFlags.HideAndDontSave };
                tex.SetPixels32(new Color32[w * h]);
                tex.Apply(false, false);
                Graphics.CopyTexture(packAtlas, 0, 0, Mathf.RoundToInt(used.x), Mathf.RoundToInt(used.y), Mathf.RoundToInt(used.width), Mathf.RoundToInt(used.height),
                    tex, 0, 0, Mathf.RoundToInt(offset.x), Mathf.RoundToInt(offset.y));
                UnityEngine.Object.DontDestroyOnLoad(tex);
                _builtTextures[OUR_PREFIX + sid] = tex;
            }

            var pivot = new Vector2(original.pivot.x / w, original.pivot.y / h);

            var sprite = Sprite.Create(tex, new Rect(0f, 0f, w, h), pivot, original.pixelsPerUnit, 0, SpriteMeshType.FullRect, original.border);
            sprite.name = OUR_PREFIX + original.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            _builtSprites[sid] = sprite;
            return sprite;
        }
    }
}
