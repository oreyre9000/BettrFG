using System;
using System.Collections.Generic;
using BetterFG.Services;
using FG.Common.CMS;
using FGClient;
using UnityEngine;

namespace BetterFG.Customization.Player
{
    public partial class SkinApplicationService
    {
        public static string GetGameEmoteOptionId(EmotesOption option)
        {
            return "gameemote:" + GetEmoteId(option);
        }

        private static string GetEmoteId(ItemDefinitionSO option)
        {
            try
            {
                var cms = option.CMSData;
                if (cms != null && !string.IsNullOrEmpty(cms.FullItemId)) return cms.FullItemId;
                if (cms != null && !string.IsNullOrEmpty(cms.Id)) return cms.Id;
            }
            catch { }
            try { if (!string.IsNullOrEmpty(option.name)) return option.name; } catch { }
            return option.GetInstanceID().ToString();
        }

        internal static string GetEmoteName(ItemDefinitionSO option, string fallback)
        {
            try { return option.CMSData.Name._text ?? option.name ?? ""; } catch { }
            try { return option.name ?? ""; } catch { }
            return fallback;
        }

        private static EmotesOption ResolveEmoteOption(string id)
        {
            var cm = CustomisationManager.Instance;
            if (cm == null || string.IsNullOrEmpty(id)) return null;
            string bare = StripIdPrefix(id, out string full);
            return cm.GetEmoteOptionWithId(bare, false) ?? cm.GetEmoteOptionWithId(full, false);
        }

        private Dictionary<int, GameCosmeticEntry> activeEmotes;

        public Dictionary<int, GameCosmeticEntry> ActiveGameEmotes(bool resolve = false)
        {
            if (activeEmotes == null)
            {
                activeEmotes = new Dictionary<int, GameCosmeticEntry>();
                for (int s = 0; s < 8; s++)
                {
                    string id = SettingsService.Get($"allcosmetics.emote.{s}", "");
                    if (id != "") activeEmotes[s] = new GameCosmeticEntry { id = id, name = id, kind = "emote", slot = s };
                }
            }
            if (!resolve) return activeEmotes;
            foreach (var e in activeEmotes.Values)
            {
                if (e.option != null) continue;
                e.option = ResolveEmoteOption(e.id);
                if (e.option != null) e.name = GetEmoteName(e.option.Cast<ItemDefinitionSO>(), e.id);
            }
            return activeEmotes;
        }

        public void ApplyGameEmote(EmotesOption option, int slot)
        {
            string id = GetGameEmoteOptionId(option);
            ResolveOptionIconSprite(option);
            ActiveGameEmotes()[slot] = new GameCosmeticEntry { id = id, name = GetEmoteName(option, "emote"), option = option, kind = "emote", slot = slot };
            SettingsService.Set($"allcosmetics.emote.{slot}", id);
            Plugin.Log.LogInfo($"{id} on emote slot {slot}");
        }

        public void RemoveGameEmote(int slot)
        {
            if (!ActiveGameEmotes().Remove(slot)) return;
            SettingsService.Set($"allcosmetics.emote.{slot}", "");
            OnSkinRemoved?.Invoke("Game emote removed");
        }

        public void RemoveAllGameEmotes()
        {
            foreach (int slot in new List<int>(ActiveGameEmotes().Keys)) RemoveGameEmote(slot);
        }

        private static readonly Dictionary<string, Sprite> _iconSpriteCache =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        public static Sprite ResolveOptionIconSprite(UnityEngine.Object option)
        {
            if (option == null) return null;
            string key = option.name;
            if (!string.IsNullOrEmpty(key) && _iconSpriteCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            ItemDefinitionSO def = null;
            try { def = option.Cast<ItemDefinitionSO>(); } catch { }
            if (def == null) return null;

            Sprite src = null;
            try { src = def.MenuDisplaySprite; } catch { }
            if (src == null)
            {
                try { src = def._spriteAtlasLoadableAsset.AssetRef.LoadAsset<Sprite>().Result; } catch { }
            }
            if (src == null || src.texture == null) return null;

            var crop = CropAtlasToTexture2D(src, string.IsNullOrEmpty(key) ? src.name : key);
            if (crop == null) return null;

            var owned = Sprite.Create(crop, new Rect(0f, 0f, crop.width, crop.height),
                new Vector2(src.pivot.x / Mathf.Max(1f, src.rect.width), src.pivot.y / Mathf.Max(1f, src.rect.height)),
                100f, 0, SpriteMeshType.FullRect);
            owned.name = "bfg_icon_" + crop.name;
            owned.hideFlags = HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;
            UnityEngine.Object.DontDestroyOnLoad(crop);

            if (!string.IsNullOrEmpty(key)) _iconSpriteCache[key] = owned;
            return owned;
        }

        public static Texture2D CropAtlasToTexture2D(Sprite spr, string name)
        {
            var atlas = spr.texture;
            if (atlas == null) return null;

            var tr = spr.textureRect;
            int rx = Mathf.Clamp(Mathf.FloorToInt(tr.x), 0, atlas.width);
            int ry = Mathf.Clamp(Mathf.FloorToInt(tr.y), 0, atlas.height);
            int rw = Mathf.Clamp(Mathf.CeilToInt(tr.width), 1, atlas.width - rx);
            int rh = Mathf.Clamp(Mathf.CeilToInt(tr.height), 1, atlas.height - ry);

            var rt = RenderTexture.GetTemporary(atlas.width, atlas.height, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Graphics.Blit(atlas, rt);
            RenderTexture.active = rt;
            var crop = new Texture2D(rw, rh, TextureFormat.RGBA32, false);
            crop.ReadPixels(new Rect(rx, ry, rw, rh), 0, 0);
            crop.Apply();
            crop.hideFlags = HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;
            crop.name = "bfg_iconsrc_" + name;
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return crop;
        }
    }
}
