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

        private static readonly Dictionary<int, Sprite> _originalSprite = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Texture> _originalTexture = new Dictionary<int, Texture>();
        private static readonly HashSet<IntPtr> _seen = new HashSet<IntPtr>();
        private static readonly HashSet<IntPtr> _misses = new HashSet<IntPtr>();
        private static string _cachedPack = "";
        private static bool _ticking;

        public const string GateKey = "bfg.texpack.runtime";
        private const float BackgroundPassInterval = 5f;

        public static bool Active => _overrides.Count > 0;
        public static string ActivePath { get; private set; } = "";

        public static void Clear()
        {
            Revert(false);
            ActivePath = "";
            SettingsService.Set("texpack.active", "");
            PatchGate.Request(GateKey, "texture pack", false);
        }

        private static void Revert(bool dropCaches)
        {
            _overrides.Clear();
            _seen.Clear();
            _misses.Clear();
            var stranded = new HashSet<int>();

            foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (img == null || img.sprite == null) continue;
                int id = img.sprite.GetInstanceID();
                if (_originalSprite.TryGetValue(id, out var origSpr) && origSpr != null) img.sprite = origSpr;
                else if (img.sprite.name.StartsWith(OUR_PREFIX, StringComparison.Ordinal)) stranded.Add(id);
            }

            foreach (var raw in Resources.FindObjectsOfTypeAll<RawImage>())
            {
                if (raw == null || raw.texture == null) continue;
                int id = raw.texture.GetInstanceID();
                if (_originalTexture.TryGetValue(id, out var origTex) && origTex != null) raw.texture = origTex;
                else if (raw.texture.name.StartsWith(OUR_PREFIX, StringComparison.Ordinal)) stranded.Add(id);
            }

            foreach (var mat in Resources.FindObjectsOfTypeAll<Material>())
                foreach (int prop in mat.GetTexturePropertyNameIDs())
                {
                    var t = mat.GetTexture(prop);
                    if (t == null) continue;
                    int id = t.GetInstanceID();
                    if (_originalTexture.TryGetValue(id, out var origTex) && origTex != null) mat.SetTexture(prop, origTex);
                    else if (t.name.StartsWith(OUR_PREFIX, StringComparison.Ordinal)) stranded.Add(id);
                }

            if (stranded.Count > 0)
                Plugin.Log.LogWarning($"texture pack: {stranded.Count} references had no live original left, leaving pack art on them");
            if (!dropCaches) return;

            foreach (var s in _builtSprites.Values)
            {
                if (s == null) continue;
                if (stranded.Contains(s.GetInstanceID()))
                {
                    if (s.texture != null) stranded.Add(s.texture.GetInstanceID());
                    continue;
                }
                UnityEngine.Object.Destroy(s);
            }
            DestroyUnstranded(_builtTextures, stranded);
            DestroyUnstranded(_sources, stranded);
            DestroyUnstranded(_sourcesMipped, stranded);

            _builtSprites.Clear();
            _builtTextures.Clear();
            _sources.Clear();
            _sourcesMipped.Clear();
            _originalSprite.Clear();
            _originalTexture.Clear();
        }

        private static void DestroyUnstranded(Dictionary<string, Texture2D> cache, HashSet<int> stranded)
        {
            foreach (var t in cache.Values)
                if (t != null && !stranded.Contains(t.GetInstanceID())) UnityEngine.Object.Destroy(t);
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

            string stamp = bfgtpPath + "|" + File.GetLastWriteTimeUtc(bfgtpPath).Ticks;
            Revert(!string.Equals(_cachedPack, stamp, StringComparison.OrdinalIgnoreCase));
            _cachedPack = stamp;
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

        public static void SweepNewTextures() => Sweep(false);

        private static void Sweep(bool full)
        {
            if (!Active) return;
            PatchGate.Request(GateKey, "texture pack", true);
            int patched = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (full) _misses.Clear();

            foreach (var mat in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (!_seen.Add(mat.m_CachedPtr) && !full) continue;
                foreach (int prop in mat.GetTexturePropertyNameIDs())
                {
                    var t = mat.GetTexture(prop);
                    if (t == null) continue;
                    string name = t.name;
                    if (string.IsNullOrEmpty(name) || name.StartsWith(OUR_PREFIX, StringComparison.Ordinal) || !_overrides.ContainsKey(name)) continue;
                    var ours = Source(name, true);
                    if (!_originalTexture.TryGetValue(ours.GetInstanceID(), out var known) || known == null)
                        _originalTexture[ours.GetInstanceID()] = t;
                    mat.SetTexture(prop, ours);
                    patched++;
                }
            }

            if (full)
                foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
                    if (TryReplaceSprite(img)) patched++;

            foreach (var raw in Resources.FindObjectsOfTypeAll<RawImage>())
            {
                if (!_seen.Add(raw.m_CachedPtr) && !full) continue;
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
                yield return new WaitForSeconds(BackgroundPassInterval);
                if (Active && !PatchGate.RoundLive) Sweep(false);
            }
            _ticking = false;
        }

        public static bool TryReplaceSprite(Image img)
        {
            if (!Active) return false;
            var spr = img.sprite;
            if (spr is null) return false;
            var sprPtr = spr.m_CachedPtr;
            if (sprPtr == IntPtr.Zero || _misses.Contains(sprPtr)) return false;
            string sprName = spr.name;
            var sprTex = spr.texture;
            if (string.IsNullOrEmpty(sprName) || sprTex is null || sprName.StartsWith(OUR_PREFIX, StringComparison.Ordinal))
            {
                _misses.Add(sprPtr);
                return false;
            }
            string texName = sprTex.name;
            string key = _overrides.ContainsKey(sprName) ? sprName
                       : _overrides.ContainsKey(texName) ? texName
                       : null;
            if (key == null) { _misses.Add(sprPtr); return false; }

            var built = BuildSprite(spr, key);
            if (built == null) { _misses.Add(sprPtr); return false; }
            if (!_originalSprite.TryGetValue(built.GetInstanceID(), out var known) || known == null)
                _originalSprite[built.GetInstanceID()] = spr;
            img.sprite = built;
            return true;
        }

        public static bool TryReplaceRawTexture(RawImage raw)
        {
            if (!Active || raw == null || raw.texture == null || string.IsNullOrEmpty(raw.texture.name)) return false;
            if (raw.texture.name.StartsWith(OUR_PREFIX, StringComparison.Ordinal)) return false;
            if (!_overrides.TryGetValue(raw.texture.name, out _)) return false;

            var ours = Source(raw.texture.name, false);
            if (!_originalTexture.TryGetValue(ours.GetInstanceID(), out var known) || known == null)
                _originalTexture[ours.GetInstanceID()] = raw.texture;
            raw.texture = ours;
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
