using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Customization.UI;
using BetterFG.Nametag;
using BetterFG.Utilities;
using FGClient;
using FGClient.UI.PrivateLobby;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

namespace BetterFG.Features.MorePlatformIcon
{
    internal static class FeatureMorePlatformIcon
    {
        public static readonly BfgFeature feature = new BfgFeature("moreplatformicon", "ui.more_platform_icons", true, new List<FeatureSetting>
        {
            new FeatureSetting { id = "privatelobby", label = "ui.private_lobby_player_list", defaultOn = true },
        }, onOpen: NametagIconApplicator.ApplyKnownPlatformIcons, onClosed: NametagIconApplicator.RestoreKnownPlatformIcons);

        const string Res = "BetterFG.assets.ui.feature.moreplatformicon.featuremoreplatformicon_platformicons.png";
        const string ResOutline = "BetterFG.assets.ui.feature.moreplatformicon.featuremoreplatformicon_platformicons_outline.png";
        const int Cell = 134;
        const float IconScale = 1.8f;
        const float Drop = 0.2f;

        static TMP_SpriteAsset _asset;
        static TMP_SpriteAsset _assetOutline;
        static Sprite[] _sprites;
        static Sprite[] _spritesOutline;
        static PrivateLobbyPlayerListViewModel _queuedVm;
        static bool _queued;
        static readonly string[] _names =
        {
            "pc_egs", "pc_steam", "xb1", "xsx",
            "xbs", "ps4", "ps5", "android_ega",
            "ios_ega", "switch", "win", "linux"
        };

        public static bool On(string setting) => FeatureRegistry.IsOn("moreplatformicon", setting);
        public static bool Enabled => feature.enabled;
        public static string[] PlatformIconIds() => (string[])_names.Clone();

        public static bool HasOwnSprite(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < _names.Length; i++)
                if (text.IndexOf("name=\"" + _names[i] + "\"", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        public static Sprite SpriteForName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            SpriteAsset();
            if (_sprites == null) return null;

            for (int i = 0; i < _names.Length; i++)
                if (string.Equals(_names[i], name, StringComparison.OrdinalIgnoreCase))
                    return i < _sprites.Length ? _sprites[i] : null;

            return null;
        }

        public static string SpriteNameForPlayerKey(string playerKey)
        {
            if (!Enabled) return "";
            if (string.IsNullOrEmpty(playerKey)) return "";

            for (int i = 0; i < _names.Length; i++)
            {
                string n = _names[i];
                if (playerKey.StartsWith(n + "_", StringComparison.OrdinalIgnoreCase))
                    return n;
            }

            return "";
        }

        public static Sprite SpriteForPlayerKey(string playerKey)
        {
            if (!Enabled) return null;
            string name = SpriteNameForPlayerKey(playerKey);
            if (string.IsNullOrEmpty(name)) return null;

            SpriteAsset();
            if (_sprites == null) return null;

            for (int i = 0; i < _names.Length; i++)
                if (_names[i] == name)
                    return i < _sprites.Length ? _sprites[i] : null;

            return null;
        }

        public static TMP_SpriteAsset SpriteAsset()
        {
            if (_asset != null) return _asset;
            _asset = BuildAsset(Res, "BetterFGMorePlatformIcons", "betterfg_moreplatformicons", ref _sprites);
            return _asset;
        }

        public static TMP_SpriteAsset SpriteAssetOutline()
        {
            if (_assetOutline != null) return _assetOutline;
            _assetOutline = BuildAsset(ResOutline, "BetterFGMorePlatformIconsOutline", "betterfg_moreplatformicons_outline", ref _spritesOutline);
            return _assetOutline;
        }

        static TMP_SpriteAsset BuildAsset(string res, string assetName, string texName, ref Sprite[] spriteStore)
        {
            byte[] bytes;
            using (var stream = typeof(FeatureMorePlatformIcon).Assembly.GetManifestResourceStream(res))
            {
                if (stream == null) return null;
                bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            tex.LoadImage(bytes);
            tex.Apply(true, false);
            tex.name = texName;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 8;
            tex.hideFlags = HideFlags.HideAndDontSave;

            var asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            asset.name = assetName;
            asset.version = "1.1.0";
            asset.spriteSheet = tex;
            asset.hashCode = TMP_TextUtilities.GetSimpleHashCode(asset.name);

            var mat = new Material(Shader.Find("TextMeshPro/Sprite"));
            mat.name = assetName + "_Mat";
            mat.mainTexture = tex;
            asset.material = mat;
            asset.materialHashCode = TMP_TextUtilities.GetSimpleHashCode(mat.name);

            var glyphs = new Il2CppSystem.Collections.Generic.List<TMP_SpriteGlyph>();
            var chars = new Il2CppSystem.Collections.Generic.List<TMP_SpriteCharacter>();
            spriteStore = new Sprite[_names.Length];
            asset.spriteInfoList = new Il2CppSystem.Collections.Generic.List<TMP_Sprite>();
            asset.fallbackSpriteAssets = new Il2CppSystem.Collections.Generic.List<TMP_SpriteAsset>();

            for (int i = 0; i < _names.Length; i++)
            {
                int col = i % 4;
                int row = i / 4;
                int x = col * Cell;
                int y = tex.height - ((row + 1) * Cell);

                var rect = new Rect(x, y, Cell, Cell);
                var sprite = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), Cell, 0, SpriteMeshType.FullRect);
                sprite.name = _names[i];
                sprite = Sprite.Create(tex, rect, new Vector2(0.7f, 0.7f), Cell * 2f, 0, SpriteMeshType.FullRect);
                sprite.name = _names[i];
                spriteStore[i] = sprite;

                float bearingY = Cell * (1f - Drop);
                uint unicode = (uint)(0xE700 + i);
                var glyph = new TMP_SpriteGlyph(
                    (uint)i,
                    new GlyphMetrics(Cell, Cell, 0f, bearingY, Cell),
                    new GlyphRect(x, y, Cell, Cell),
                    IconScale,
                    0,
                    sprite
                );
                glyphs.Add(glyph);

                var ch = new TMP_SpriteCharacter(unicode, glyph);
                ch.name = _names[i];
                ch.glyphIndex = (uint)i;
                chars.Add(ch);
            }

            asset.spriteGlyphTable = glyphs;
            asset.spriteCharacterTable = chars;
            asset.UpdateLookupTables();
            return asset;
        }

        public static void QueuePrivateLobbyApply(PrivateLobbyPlayerListViewModel vm)
        {
            var app = MenuCustomizationApplication.Instance;
            if (vm == null || app == null) return;

            _queuedVm = vm;
            if (_queued) return;

            _queued = true;
            app.StartCoroutine(ApplyPrivateLobbyAfterRefresh().WrapToIl2Cpp());
        }

        public static IEnumerator ApplyPrivateLobbyAfterOpen(PrivateLobbyPlayerListViewModel vm)
        {
            yield return null;
            ApplyPrivateLobby(vm);
            yield return new WaitForSeconds(0.15f);
            ApplyPrivateLobby(vm);
        }

        static IEnumerator ApplyPrivateLobbyAfterRefresh()
        {
            yield return null;
            yield return null;
            ApplyPrivateLobby(_queuedVm);
            yield return new WaitForSeconds(0.05f);
            ApplyPrivateLobby(_queuedVm);
            yield return new WaitForSeconds(0.2f);
            ApplyPrivateLobby(_queuedVm);
            yield return new WaitForSeconds(0.5f);
            ApplyPrivateLobby(_queuedVm);
            _queued = false;
        }

        public static void ApplyPrivateLobby(PrivateLobbyPlayerListViewModel vm)
        {
            try
            {
                if (vm == null || vm.PlayerList == null || vm._cachedPlayersGameObjectList == null)
                {
                    Plugin.Log.LogInfo($"PrivateLobbyName: DIAG bail, vm null={vm == null} playerlist null={vm?.PlayerList == null} cache null={vm?._cachedPlayersGameObjectList == null}");
                    return;
                }
                string localKey = GlobalGameStateClient.Instance?.GetLocalPlayerKey() ?? "";

                int count = Math.Min(vm.PlayerList.Count, vm._cachedPlayersGameObjectList.Count);
                Plugin.Log.LogInfo($"PrivateLobbyName: DIAG applying to {count} rows (playerlist={vm.PlayerList.Count}, cache={vm._cachedPlayersGameObjectList.Count}), privatelobby setting on={On("privatelobby")}");
                for (int i = 0; i < count; i++)
                {
                    var data = vm.PlayerList[i];
                    var row = vm._cachedPlayersGameObjectList[i];
                    if (data == null || row == null)
                    {
                        Plugin.Log.LogInfo($"PrivateLobbyName: DIAG row {i} null (data={data == null}, row={row == null})");
                        continue;
                    }

                    var textNode = row.transform.Find("Panel/Content/PlayerName");
                    if (textNode == null)
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var t in row.transform.GetComponentsInChildren<Transform>(true))
                            sb.Append(GetPath(t, row.transform)).Append(" [").Append(t.GetComponent<TMPro.TextMeshProUGUI>() != null ? "TMP" : "").Append("]; ");
                        Plugin.Log.LogInfo($"PrivateLobbyName: DIAG row {i} hierarchy: {sb}");
                    }
                    Plugin.Log.LogInfo($"PrivateLobbyName: DIAG row {i} key='{data.PlayerKey}' textFound={textNode != null} spriteName='{SpriteNameForPlayerKey(data.PlayerKey)}'");

                    ApplyPrivateLobbyPlatform(row.transform, data.PlayerKey);
                    ApplyPrivateLobbyCustomName(row.transform, data.PlayerKey);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("PrivateLobbyName: list " + ex.Message);
            }
        }

        static string GetPath(Transform t, Transform root)
        {
            if (t == root) return ".";
            var names = new List<string>();
            while (t != null && t != root) { names.Add(t.name); t = t.parent; }
            names.Reverse();
            return string.Join("/", names);
        }

        // scene-based driver (foreground timing): each row's name is what it currently shows, so match
        // on that. used on first open where the VM cache isn't filled yet.
        public static void ApplyPrivateLobbyNamesFromScene(Transform listRoot)
        {
            if (listRoot == null) return;
            foreach (var text in listRoot.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (text == null || text.transform.parent == null) continue;
                if (text.gameObject.name != "Text" || text.transform.parent.name != "Content") continue;

                string visible = NametagIconApplicator.BuildInlinePlatformName(text.text ?? "", "", true);
                ApplyPrivateLobbyCustomName(text.transform.parent, BetterFG.Tweaks.StripSizeTagsTweak.Strip(visible).Trim());
            }
        }

        // one per-row pass: set the row's name + full style. local uses live settings, remote uses
        // the matched profile. keeps the inline platform sprite the platform pass set. row may be the
        // VM's row transform OR the Content node; we resolve the Text under it either way.
        public static void ApplyPrivateLobbyCustomName(Transform row, string playerKey)
        {
            if (row == null) return;
            // row may be the VM row (Panel/Content/PlayerName under it) or the Content node directly
            ApplyPrivateLobbyCustomName((row.Find("Panel/Content/PlayerName") ?? row.Find("PlayerName"))?.GetComponent<TextMeshProUGUI>(), playerKey);
        }

        public static void ApplyPrivateLobbyCustomName(TextMeshProUGUI text, string playerKey)
        {
            try
            {
                if (text == null || string.IsNullOrEmpty(playerKey)) return;

                string localKey = GlobalGameStateClient.Instance?.GetLocalPlayerKey() ?? "";
                string cleanKey = FallGuysLib.Players.PlayerUtils.CleanPlayerName(playerKey);
                // match local by real name, real key, OR the custom name we may have already set the row to
                bool isLocal = !string.IsNullOrEmpty(localKey) &&
                    (string.Equals(cleanKey, FallGuysLib.Players.PlayerUtils.CleanPlayerName(localKey), StringComparison.OrdinalIgnoreCase)
                     || string.Equals(cleanKey, FallGuysLib.Players.PlayerUtils.CleanPlayerName(LocalPlayerInfo.FGlocalplayerusername), StringComparison.OrdinalIgnoreCase)
                     || (!string.IsNullOrEmpty(LocalPlayerInfo.CustomName) && string.Equals(playerKey, LocalPlayerInfo.CustomName, StringComparison.OrdinalIgnoreCase)));

                var info = isLocal
                    ? LocalNametagInfo()
#if PROFILES
                    : BetterFG.Customization.Profiles.ProfileService.GetRemoteProfileForName(
                        FallGuysLib.Players.PlayerUtils.CleanPlayerName(playerKey))?.nametag;
#else
                    : null;
#endif
                if (info == null) return;

                // keep the inline platform sprite tag the platform pass prepended, restyle the rest
                string cur = text.text ?? "";
                string visibleName = NametagIconApplicator.BuildInlinePlatformName(cur, "", true);
                string platformTag = cur.EndsWith(visibleName, StringComparison.Ordinal)
                    ? cur.Substring(0, cur.Length - visibleName.Length)
                    : "";
                NametagIconApplicator.ApplyRemoteToNameplate(text, visibleName, info);
                if (!string.IsNullOrEmpty(platformTag))
                    text.text = platformTag + text.text;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("PrivateLobbyName: customname " + ex.Message);
            }
        }

        // build a RemoteNametagInfo from your own live nametag settings so the local row gets the
        // same name+colour+style path as everyone else.
        internal static BetterFG.Network.RemoteNametagInfo LocalNametagInfo()
            => BetterFG.Nametag.NametagIconApplicator.BuildLocalNametagInfo();

        public static void ApplyPrivateLobbyPlatform(Transform row, string playerKey)
        {
            if (row == null) return;
            ApplyPrivateLobbyPlatform(row.Find("Panel/Content/PlayerName")?.GetComponent<TextMeshProUGUI>(), playerKey);
        }

        public static void ApplyPrivateLobbyPlatform(TextMeshProUGUI text, string playerKey)
        {
            try
            {
                if (text == null) return;

                string spriteName = On("privatelobby") ? SpriteNameForPlayerKey(playerKey) : "";
                if (string.IsNullOrEmpty(spriteName))
                {
                    if (HasOwnSprite(text.text))
                        text.text = NametagIconApplicator.BuildInlinePlatformName(text.text, "", true);
                    return;
                }

                if (!NametagIconApplicator.ApplyInlinePlatformAsset(text)) return;
                NametagIconApplicator.ApplyInlinePlatform(text, spriteName, 0.7f, 0.08f);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("PrivateLobbyName: platform " + ex.Message);
            }
        }

        public static void ApplyPrivateLobbyLocalName(Transform row, string playerKey = "", string matchText = "")
        {
            try
            {
                string localName = GlobalGameStateClient.Instance?.GetLocalPlayerName() ?? "";
                string localKey = GlobalGameStateClient.Instance?.GetLocalPlayerKey() ?? "";
                string displayName = BetterFG.Tweaks.StripSizeTagsTweak.Strip(LocalPlayerInfo.DisplayName);
                if (row == null || string.IsNullOrEmpty(localName) || string.IsNullOrEmpty(displayName)) return;

                var text = row.Find("Panel/Content/PlayerName")?.GetComponent<TextMeshProUGUI>();
                if (text == null) return;

                string visible = text.text ?? "";
                string m = matchText ?? "";
                if (!string.IsNullOrEmpty(playerKey))
                {
                    if (string.IsNullOrEmpty(localKey) ||
                        !string.Equals(playerKey, localKey, StringComparison.OrdinalIgnoreCase))
                        return;
                }
                else
                {
                    string cleanVisible = BetterFG.Tweaks.StripSizeTagsTweak.Strip(
                        NametagIconApplicator.BuildInlinePlatformName(visible, "", true)).Trim();
                    string cleanMatch = BetterFG.Tweaks.StripSizeTagsTweak.Strip(
                        NametagIconApplicator.BuildInlinePlatformName(m, "", true)).Trim();
                    string cleanLocal = BetterFG.Tweaks.StripSizeTagsTweak.Strip(localName).Trim();
                    if (!string.Equals(cleanMatch, cleanLocal, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(cleanVisible, cleanLocal, StringComparison.OrdinalIgnoreCase))
                        return;
                }

                string spriteName = "";
                if (On("privatelobby"))
                {
                    spriteName = SpriteNameForPlayerKey(playerKey);
                    if (!string.IsNullOrEmpty(spriteName))
                        NametagIconApplicator.ApplyInlinePlatformAsset(text);
                }
                if (string.IsNullOrEmpty(spriteName) && HasOwnSprite(text.text))
                    text.text = NametagIconApplicator.BuildInlinePlatformName(text.text, "", true);

                NametagIconApplicator.ApplyLocalInlineName(text, displayName, spriteName, 0.7f, -0.2f);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("PrivateLobbyName: row " + ex.Message);
            }
        }
    }
}
