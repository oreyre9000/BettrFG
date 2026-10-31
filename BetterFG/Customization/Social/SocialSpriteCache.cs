using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using BetterFG.Utilities;
using UnityEngine;

namespace BetterFG.Customization.Social
{
    // the wheel re-applies on every open, and decoding a PNG into a Texture2D + Sprite each time is
    // what made the wheel hitch. cache the EXPENSIVE bits (the decoded Texture2D + Sprite) keyed by
    // path, rebuilt only when the file on disk changes (last-write-time).
    //
    // we do NOT cache the CacheableAtlasSprite wrapper across calls: it builds an internal atlas that
    // UnloadUnusedAssets reclaims on a scene/round change, so a wrapper made last round renders blank
    // on the new round's first wheel open. it's cheap to rebuild from the cached sprite, so we make a
    // fresh one every call — that's the wrapper the game's first render actually reads.
    internal static class SocialSpriteCache
    {
        private struct Entry { public long ticks; public Sprite sprite; }
        private static readonly Dictionary<string, Entry> _cache = new Dictionary<string, Entry>();

        // returns false if the path is empty/missing or decode failed. on success fills sprite+cached.
        internal static bool TryGet(string path, out Sprite sprite, out ItemDefinitionSO.CacheableAtlasSprite cached)
        {
            sprite = null; cached = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

            long ticks = File.GetLastWriteTimeUtc(path).Ticks;
            if (_cache.TryGetValue(path, out var hit) && hit.ticks == ticks && hit.sprite != null)
            {
                sprite = hit.sprite;
            }
            else
            {
                try
                {
                    var decoded = AnimatedImage.Decode(File.ReadAllBytes(path));
                    if (decoded == null) return false;
                    var tex = UnityEngine.Object.Instantiate(decoded.Frames[0]);
                    // keep these alive across scene loads / UnloadUnusedAssets — nothing else roots them
                    // as real assets, so without this the wheel icon goes blank after a round change.
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                    sprite.hideFlags = HideFlags.HideAndDontSave;
                    _cache[path] = new Entry { ticks = ticks, sprite = sprite };
                    if (decoded.Frames.Length > 1)
                    {
                        foreach (var f in decoded.Frames) f.hideFlags = HideFlags.HideAndDontSave;
                        BeanMonitorService.Instance.StartCoroutine(Animate(path, sprite, decoded).WrapToIl2Cpp());
                    }
                    else UnityEngine.Object.Destroy(decoded.Frames[0]);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"SocialSpriteCache: failed to load {path}: {ex.Message}");
                    return false;
                }
            }

            // fresh wrapper every call — see class note
            cached = new ItemDefinitionSO.CacheableAtlasSprite();
            cached.Cache(sprite, true);
            return true;
        }

        internal static void DropDeadAtlasSprites()
        {
            var lookup = MPG.Utility.SingletonBehaviour<Character.SpeechOptionsManager>.Instance?._speechOptionsLookup;
            if (lookup == null) return;
            int dropped = 0;
            foreach (var kv in lookup)
            {
                var cache = kv.Value?.TryCast<ImageSpeechOption>()?._cachedAtlasSprite;
                if (cache == null || !cache.IsCached || (cache.CachedSprite != null && cache.CachedSprite.texture != null)) continue;
                kv.Value.UnloadCachedSprite();
                dropped++;
            }
            if (dropped > 0) Plugin.Log.LogInfo($"{dropped} social icons still held a sprite from an unloaded atlas, dropped them");
        }

        internal static string SourceFile(Sprite sprite)
        {
            foreach (var kv in _cache) if (kv.Value.sprite == sprite) return kv.Key;
            return null;
        }

        private static IEnumerator Animate(string path, Sprite sprite, AnimatedImage.Result anim)
        {
            var tex = sprite.texture;
            for (int i = 0; _cache.TryGetValue(path, out var e) && e.sprite == sprite && tex != null; i = (i + 1) % anim.Frames.Length)
            {
                Graphics.CopyTexture(anim.Frames[i], tex);
                yield return new WaitForSecondsRealtime(anim.Delays[i]);
            }
            foreach (var f in anim.Frames) UnityEngine.Object.Destroy(f);
        }
    }
}
