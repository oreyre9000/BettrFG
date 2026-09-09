using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BetterFG.Core;
using BetterFG.Features.MorePlatformIcon;
using BetterFG.Network;
using BetterFG.Services;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine;
using UnityEngine.UI;
using FGClient;
using PlayerUtils = FallGuysLib.Players.PlayerUtils;
using PartyMenu;
using BetterFG.UI.Tabs;
using TMPro;
using Rect = UnityEngine.Rect;

namespace BetterFG.Nametag
{
    public static class NametagIconApplicator
    {
        private const string KEY_ICON_MODE = "nametag.icon.mode";
        private const string KEY_ICON_COUNTRY = "nametag.icon.country";
        private const string KEY_ICON_PATH = "nametag.icon.path";
        private const string KEY_COLOR_R = "nametag.color.r";
        private const string KEY_COLOR_G = "nametag.color.g";
        private const string KEY_COLOR_B = "nametag.color.b";
        private const string KEY_BOLD = "nametag.bold";
        private const string KEY_ITALIC = "nametag.italic";
        private const string KEY_ENABLED = "nametag.enabled";
        private const string KEY_NAME_STYLE = "nametag.namestyle";
        private const string KEY_GRADIENT_COLORS = "nametag.gradient.colors";
        private const string KEY_GRADIENT_ANGLE = "nametag.gradient.angle";
        private const string KEY_OUTLINE_ENABLED = "nametag.outline.enabled";
        private const string KEY_OUTLINE_R = "nametag.outline.r";
        private const string KEY_OUTLINE_G = "nametag.outline.g";
        private const string KEY_OUTLINE_B = "nametag.outline.b";
        private const string KEY_OUTLINE_WIDTH = "nametag.outline.width";
        private const string KEY_OUTLINE_MODE = "nametag.outline.mode";
        private const string KEY_CUSTOM_NAME = "nametag.customname";
        private const string KEY_PLATFORM_HIDE = "nametag.platform.hide";
        private const string KEY_PLATFORM_CUSTOM = "nametag.platform.custom";
        private const string KEY_BACKING_ENABLED = "nametag.backing.enabled";
        private const string KEY_BACKING_PATH = "nametag.backing.path";
        private const string KEY_BACKING_OFFSET_X = "nametag.backing.offset.x";
        private const string KEY_BACKING_OFFSET_Y = "nametag.backing.offset.y";
        private const string KEY_BACKING_SCALE = "nametag.backing.scale";
        private const string KEY_NICKNAME_ENABLED = "nametag.nickname.enabled";
        private const string KEY_NICKNAME_TEXT = "nametag.nickname.text";

        private const string NICKNAME_REGULAR_NAME = "NameTagUserNicknameText";
        private const string NICKNAME_PARTY_NAME = "Nickname_Label";

        private const string BACKING_OBJECT_NAME = "NameTagBacking";

        private const string FLAG_OBJECT_NAME = "BetterFG_NametagIcon";
        private const string UI_ICON_NAME = "BetterFG_UINametagIcon";
        private const float BASE_SCALE = 0.3f;
        private const float FLAG_NUDGE_X = 0.01f;
        private const float UI_ICON_SIZE_REGULAR = 2f * 0.37f * 0.18f;
        private const float UI_ICON_SIZE_PARTY = 18f * 0.37f * 3f;
        private const float UI_ICON_GAP = 4f;

        private static string _cachedRawName = null;

        // everything the name+icon apply needs, so the pipeline takes values instead of reading globals. build
        // one from saved settings (in-game) or from the config tab's live fields (preview).
        public struct NametagCfg
        {
            public bool enabled;
            public float r, g, b;
            public bool bold, italic;
            public string style;      // "none" | "default" | "gold" | "goldcolored"
            public string gradientColors;
            public float gradientAngle;
            public bool outlineEnabled;
            public float outlineR, outlineG, outlineB;
            public float outlineWidth;
            public string outlineMode;
            public string iconMode;   // "none" | "flag" | "custom"
            public string iconCountry, iconPath;
        }

        public static NametagCfg CfgFromSettings()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float F(string k) => float.TryParse(SettingsService.Get(k, "1"), System.Globalization.NumberStyles.Float, ci, out float v) ? v : 1f;
            return new NametagCfg
            {
                enabled = SettingsService.Get(KEY_ENABLED, "false") == "true",
                r = F(KEY_COLOR_R), g = F(KEY_COLOR_G), b = F(KEY_COLOR_B),
                bold = SettingsService.Get(KEY_BOLD, "false") == "true",
                italic = SettingsService.Get(KEY_ITALIC, "false") == "true",
                style = SettingsService.Get(KEY_NAME_STYLE, "default"),
                gradientColors = SettingsService.Get(KEY_GRADIENT_COLORS, ""),
                gradientAngle = float.TryParse(SettingsService.Get(KEY_GRADIENT_ANGLE, "0"), System.Globalization.NumberStyles.Float, ci, out float ga) ? ga : 0f,
                outlineEnabled = SettingsService.Get(KEY_OUTLINE_ENABLED, "false") == "true",
                outlineR = float.TryParse(SettingsService.Get(KEY_OUTLINE_R, "0"), System.Globalization.NumberStyles.Float, ci, out float orv) ? orv : 0f,
                outlineG = float.TryParse(SettingsService.Get(KEY_OUTLINE_G, "0"), System.Globalization.NumberStyles.Float, ci, out float ogv) ? ogv : 0f,
                outlineB = float.TryParse(SettingsService.Get(KEY_OUTLINE_B, "0"), System.Globalization.NumberStyles.Float, ci, out float obv) ? obv : 0f,
                outlineWidth = float.TryParse(SettingsService.Get(KEY_OUTLINE_WIDTH, "0.2"), System.Globalization.NumberStyles.Float, ci, out float owv) ? owv : 0.2f,
                outlineMode = SettingsService.Get(KEY_OUTLINE_MODE, "outset"),
                iconMode = SettingsService.Get(KEY_ICON_MODE, "none"),
                iconCountry = SettingsService.Get(KEY_ICON_COUNTRY, ""),
                iconPath = SettingsService.Get(KEY_ICON_PATH, ""),
            };
        }

        public static bool PlatformHideFromSettings()
        {
            string s = SettingsService.Get(KEY_PLATFORM_HIDE, "none");
            return s == "self" || s == "everyone";
        }

        public static bool PlatformHideEveryoneFromSettings() => SettingsService.Get(KEY_PLATFORM_HIDE, "none") == "everyone";

        public static void SetPlatformHideEveryone(bool on)
        {
            if (on) SettingsService.Set(KEY_PLATFORM_HIDE, "everyone");
            else if (SettingsService.Get(KEY_PLATFORM_HIDE, "none") == "everyone") SettingsService.Set(KEY_PLATFORM_HIDE, "none");
            ApplyKnownPlatformIcons();
        }

        public static string PlatformCustomFromSettings() => SettingsService.Get(KEY_PLATFORM_CUSTOM, "");

        private static readonly Dictionary<int, SpriteRenderer> _icon3d = new Dictionary<int, SpriteRenderer>();
        private static readonly Dictionary<int, UnityEngine.UI.Image> _iconUI = new Dictionary<int, UnityEngine.UI.Image>();
        private static readonly Dictionary<int, Sprite> _knownPlatform3dOriginals = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> _knownPlatformUiOriginals = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> _backingOriginals = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, (string text, Vector3 pos)> _nicknameOriginals = new Dictionary<int, (string, Vector3)>();

        private static readonly HashSet<IntPtr> _iconPtrs = new HashSet<IntPtr>();

        // bumped whenever an icon is registered or dropped, so the per-HUD row cache below knows to
        // rebuild instead of holding a reference to a dead icon
        private static int _iconVersion;

        private static void SyncIconGate() => _iconVersion++;

        public static void ClearIconRegistry() { _icon3d.Clear(); _iconUI.Clear(); _iconPtrs.Clear(); _placement.Clear(); SyncIconGate(); }

        // ── per-frame icon alpha ──────────────────────────────────────────────
        //
        // the game fades each nametag by distance every frame and used to tell us through a prefix on
        // PlayerInfoDisplay.SetTextAlpha — one native->managed crossing per visible tag per frame, on
        // both display types. this reads the alpha the game just wrote instead, off the single
        // PlayerInfoHUDBase.LateUpdate postfix, and only for the handful of tags that actually carry an
        // icon (usually just yours). rows are resolved once and held until the tag count or the icon
        // registry changes, so a steady frame is one colour read per iconned tag and nothing else.
        private class IconRow
        {
            public TMP_Text text;
            public SpriteRenderer sr;
            public UnityEngine.UI.Image img;
            public float last = -1f;
        }

        private class IconRows
        {
            public int count = -1;
            public int version = -1;
            public readonly List<IconRow> rows = new List<IconRow>();
        }

        private static readonly Dictionary<IntPtr, IconRows> _iconRowsByHud = new Dictionary<IntPtr, IconRows>();

        public static void ForgetIconRows() => _iconRowsByHud.Clear();

        public static void TickIconAlpha(PlayerInfoHUDBase hud)
        {
            if (_iconPtrs.Count == 0) return;

            var spawned = hud._spawnedInfoObjects;
            if (spawned == null) return;
            int count = spawned.Count;

            IntPtr hudId = hud.Pointer;
            if (!_iconRowsByHud.TryGetValue(hudId, out var cache))
            {
                cache = new IconRows();
                _iconRowsByHud[hudId] = cache;
            }

            if (cache.count != count || cache.version != _iconVersion)
            {
                cache.count = count;
                cache.version = _iconVersion;
                cache.rows.Clear();
                for (int i = 0; i < count; i++)
                {
                    var display = spawned[i].playerInfo;
                    if (display == null) continue;
                    var tmp = TryGetNameText(display);
                    if (tmp is null || tmp.m_CachedPtr == IntPtr.Zero) continue;
                    // the sink-side SetText prefixes used to do this; the names themselves are already
                    // stripped at PlayerNameManager.GetNameToDisplayForPlayer
                    if (BetterFG.Tweaks.StripSizeTagsTweak.Active) tmp.parseCtrlCharacters = false;
                    if (!_iconPtrs.Contains(tmp.m_CachedPtr)) continue;

                    int id = tmp.GetInstanceID();
                    var row = new IconRow { text = tmp };
                    _icon3d.TryGetValue(id, out row.sr);
                    _iconUI.TryGetValue(id, out row.img);
                    if (row.sr == null && row.img == null) continue;
                    cache.rows.Add(row);
                }
            }

            var list = cache.rows;
            for (int i = 0; i < list.Count; i++)
            {
                var row = list[i];
                if (row.text.m_CachedPtr == IntPtr.Zero) { cache.count = -1; return; }

                float alpha = row.text.color.a;
                if (alpha == row.last) continue;
                row.last = alpha;

                if (row.sr != null) { var c = row.sr.color; c.a = alpha; row.sr.color = c; }
                if (row.img != null) { var c = row.img.color; c.a = alpha; row.img.color = c; }
            }
        }

        // replay drives its own nametags off a track list, not the live HUD, so it still pushes alpha
        // in directly rather than going through TickIconAlpha's row cache
        public static void SetIconAlphaForDisplay(PlayerInfoDisplayGameObject display, float alpha)
        {
            if (display is null || display.m_CachedPtr == IntPtr.Zero) return;
            var txt = display._text;
            if (txt is null || txt.m_CachedPtr == IntPtr.Zero) return;
            SetIconAlphaForText(txt, alpha);
        }

        private static void SetIconAlphaForText(TMP_Text txt, float alpha)
        {
            // the game fades nametag alpha by distance, so this runs for every visible tag every frame.
            // GetInstanceID is an il2cpp round trip, and almost nobody has a custom icon — so when the
            // registries are empty there's nothing to look up and we get out before paying for it.
            if (_iconPtrs.Count == 0 || !_iconPtrs.Contains(txt.m_CachedPtr)) return;

            int id = txt.GetInstanceID();

            if (_icon3d.TryGetValue(id, out var sr) && sr != null)
            { var c = sr.color; c.a = alpha; sr.color = c; }

            if (_iconUI.TryGetValue(id, out var img) && img != null)
            { var c = img.color; c.a = alpha; img.color = c; }
        }

        // ── Local nametag ─────────────────────────────────────────────────────

        // in-game entry: local display + saved settings.
        public static void ApplyNametag() => ApplyNametagTo(NametagFinder.FindLocalDisplay(), CfgFromSettings());

        // called from patch_SpawnPlayerTag once the local row actually exists — no more blind polling.
        public static void OnLocalTagSpawned()
        {
            ApplyNametag();
            ApplyPlatformIcon();
            CrownRankService.ApplyLocal();
        }

        public static RemoteNametagInfo BuildLocalNametagInfo()
        {
            var cfg = CfgFromSettings();
            string customName = LocalPlayerInfo.CustomName;
            string hideMode = SettingsService.Get(KEY_PLATFORM_HIDE, "none");
            string platformHide = hideMode == "self" ? "true" : "";
            string platformCustom = SettingsService.Get(KEY_PLATFORM_CUSTOM, "");

            bool useDisplay = cfg.enabled || !string.IsNullOrEmpty(customName)
                || !string.IsNullOrEmpty(platformHide) || !string.IsNullOrEmpty(platformCustom);
            if (!useDisplay) return null;

            bool nicknameOn = SettingsService.Get(KEY_NICKNAME_ENABLED, "false") == "true";
            bool backingOn = SettingsService.Get(KEY_BACKING_ENABLED, "false") == "true";
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float F(string k, float def) => float.TryParse(SettingsService.Get(k, def.ToString(ci)), System.Globalization.NumberStyles.Float, ci, out float v) ? v : def;

            return new RemoteNametagInfo
            {
                customName = LocalPlayerInfo.DisplayName,
                r = cfg.enabled ? cfg.r : 1f,
                g = cfg.enabled ? cfg.g : 1f,
                b = cfg.enabled ? cfg.b : 1f,
                bold = cfg.enabled && cfg.bold,
                italic = cfg.enabled && cfg.italic,
                nameStyle = cfg.enabled ? cfg.style : "",
                gradientColors = cfg.enabled ? cfg.gradientColors : "",
                gradientAngle = cfg.enabled ? cfg.gradientAngle : 0f,
                outlineEnabled = cfg.enabled && cfg.outlineEnabled,
                outlineR = cfg.outlineR,
                outlineG = cfg.outlineG,
                outlineB = cfg.outlineB,
                outlineWidth = cfg.outlineWidth,
                outlineMode = cfg.outlineMode,
                iconMode = cfg.enabled ? cfg.iconMode : "none",
                iconCountry = cfg.iconCountry,
                iconPath = cfg.iconPath,
                iconScale = 1f,
                nickname = nicknameOn ? SettingsService.Get(KEY_NICKNAME_TEXT, "") : "",
                backingEnabled = backingOn,
                backingPath = backingOn ? SettingsService.Get(KEY_BACKING_PATH, "") : "",
                backingOffX = F(KEY_BACKING_OFFSET_X, 0f),
                backingOffY = F(KEY_BACKING_OFFSET_Y, 0f),
                backingScale = F(KEY_BACKING_SCALE, 1f),
                platformHide = platformHide,
                platformCustom = platformCustom,
            };
        }

        // apply the name (+ icon) to a specific display with explicit values. used both in-game and by the
        // config-tab preview (which passes a cloned display + its live field values).
        public static void ApplyNametagTo(PlayerInfoDisplay display, NametagCfg cfg)
        {
            var anyTmp = display != null ? TryGetNameText(display) : null;
            if (anyTmp == null) return;

            var uiTmp = anyTmp.TryCast<TextMeshProUGUI>();
            if (uiTmp != null) { ApplyToNameplate(uiTmp, LocalPlayerInfo.DisplayName, cfg, NameplateType.Regular); return; }

            var tmp = anyTmp.TryCast<TextMeshPro>();
            if (tmp == null) return;

            string stripped = System.Text.RegularExpressions.Regex.Replace(tmp.text, "<[^>]*>", "").Trim();
            string customName = LocalPlayerInfo.CustomName;
            if (string.IsNullOrEmpty(_cachedRawName) && stripped != customName)
                _cachedRawName = stripped;

            if (!cfg.enabled)
            {
                // nametag styling off — but font replacement is independent, so still apply the custom
                // font to the 3D nameplate (keeps whatever material/outline it already has, on our atlas).
                BetterFG.Customization.UI.FontReplacementService.ApplyToNametag(tmp);
                return;
            }

            string raw = string.IsNullOrEmpty(customName) ? _cachedRawName : customName;
            if (string.IsNullOrEmpty(raw))
                raw = System.Text.RegularExpressions.Regex.Replace(tmp.text, "<[^>]*>", "").Trim();

            ApplyTextStyle(tmp, raw, ArgsFromCfg(cfg));
            ApplyIconTo(display, cfg);
        }

        public static void RemoveNametag()
        {
            SettingsService.Set(KEY_ENABLED, "false");
            SettingsService.Set(KEY_NAME_STYLE, "default");

            var anyTmp = NametagFinder.FindLocalNameTextAny();
            var uiTmp = anyTmp?.TryCast<TextMeshProUGUI>();
            if (uiTmp != null)
                ApplyToNameplate(uiTmp, LocalPlayerInfo.FGlocalplayerusername, NameplateType.Regular);

            var tmp = anyTmp?.TryCast<TextMeshPro>();
            if (tmp != null)
            {
                string raw = _cachedRawName ?? System.Text.RegularExpressions.Regex.Replace(tmp.text, "<[^>]+>", "").Trim();
                tmp.text = raw;
                if (AssetManager.DefaultNameMaterial != null) tmp.fontSharedMaterial = AssetManager.DefaultNameMaterial;
                tmp.color = Color.white;
            }

            _cachedRawName = null;

            RemoveIcon();
        }

        // ── Local platform icon ───────────────────────────────────────────────

        public static void ApplyPlatformIcon()
        {
            string modeStr = SettingsService.Get(KEY_PLATFORM_HIDE, "none");
            string customId = SettingsService.Get(KEY_PLATFORM_CUSTOM, "");

            var localTag = NametagFinder.FindLocalNameTagSprite();
            if (localTag == null) return;

            ApplyPlatformIcon(localTag.gameObject, modeStr == "self" || modeStr == "everyone", customId);
        }

        // ── Remote platform icon ──────────────────────────────────────────────

        public static void ApplyRemotePlatformIcon(NameTagViewModel vm, RemoteNametagInfo info)
        {
            if (vm == null || info == null) return;

            bool hide = info.platformHide == "true";
            string customSprite = info.platformCustom ?? "";
            if (!hide && string.IsNullOrEmpty(customSprite)) return;

            var huds = UnityEngine.Object.FindObjectsOfType<PlayerInfoHUDBase>(true);
            if (huds == null) return;

            for (int h = 0; h < huds.Length; h++)
            {
                var spawned = huds[h]?._spawnedInfoObjects;
                if (spawned == null) continue;

                for (int i = 0; i < spawned.Count; i++)
                {
                    var pi = spawned[i]?.playerInfo;
                    if (!MatchesText(pi, vm._playerNameText)) continue;

                    ApplyPlatformIcon(pi?.gameObject, hide, customSprite);
                    return;
                }
            }
        }

        // nametags need to render at queue 4000 to show up on top of water/ice (a real vanilla FG bug),
        // but touching TMP_Text.fontMaterial to do it force-instances a brand new Material PER TEXT —
        // do that across a full lobby's worth of remote nametags at round start and every one of them
        // stops batching with every other nametag, permanently, for the rest of the round. this is what
        // was silently costing fps that scaled with player count and only recovered as players left.
        // fix: bump the render queue on ONE shared clone per distinct base material (cached), and point
        // every nametag using that base at the SAME clone via fontSharedMaterial instead of fontMaterial
        // — fontSharedMaterial never instances, so nametags sharing a base material keep batching.
        private static readonly Dictionary<int, Material> _queueBumpedShared = new Dictionary<int, Material>();

        private static readonly Dictionary<IntPtr, IntPtr> _queueSeen = new Dictionary<IntPtr, IntPtr>();

        public static void EnsureNametagRenderQueue(TMP_Text txt)
        {
            if (txt == null) return;
            var shared = txt.fontSharedMaterial;
            if (shared == null) return;

            IntPtr tp = txt.Pointer, sp = shared.Pointer;
            if (_queueSeen.TryGetValue(tp, out var prev) && prev == sp) return;

            if (shared.renderQueue == 4000) { _queueSeen[tp] = sp; return; }

            int key = shared.GetInstanceID();
            if (!_queueBumpedShared.TryGetValue(key, out var bumped) || bumped == null)
            {
                bumped = new Material(shared) { hideFlags = HideFlags.HideAndDontSave };
                bumped.renderQueue = 4000;
                _queueBumpedShared[key] = bumped;
            }
            txt.fontSharedMaterial = bumped;
            _queueSeen[tp] = bumped.Pointer;
        }

        public static void ApplyRenderQueueToAllNametags()
        {
            try
            {
                var huds = UnityEngine.Object.FindObjectsOfType<PlayerInfoHUDBase>(true);
                if (huds == null) return;

                for (int h = 0; h < huds.Length; h++)
                {
                    var spawned = huds[h]?._spawnedInfoObjects;
                    if (spawned == null) continue;

                    for (int i = 0; i < spawned.Count; i++)
                    {
                        var txt = TryGetNameText(spawned[i]?.playerInfo);
                        EnsureNametagRenderQueue(txt);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("NametagIcon: renderqueue pass " + ex.Message);
            }
        }

        public static void ApplyKnownPlatformIcons()
        {
            try
            {
                bool hideEveryone = SettingsService.Get(KEY_PLATFORM_HIDE, "none") == "everyone";

                if (!FeatureMorePlatformIcon.Enabled && !hideEveryone)
                {
                    RestoreKnownPlatformIcons();
                    return;
                }

                var huds = UnityEngine.Object.FindObjectsOfType<PlayerInfoHUDBase>(true);
                if (huds == null) return;

                for (int h = 0; h < huds.Length; h++)
                {
                    var spawned = huds[h]?._spawnedInfoObjects;
                    if (spawned == null) continue;

                    for (int i = 0; i < spawned.Count; i++)
                    {
                        var row = spawned[i];
                        if (row == null) continue;
                        ApplyKnownPlatformIcon(row.playerInfo, PlayerKeyForFgcc(row.fgcc));
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("NametagIcon: platform pass " + ex.Message);
            }
        }

        public static void ApplyKnownPlatformIcon(PlayerInfoDisplay display)
        {
            if (display == null) return;
            if (!FeatureMorePlatformIcon.Enabled) { RestoreKnownPlatformIcon(display); return; }

            try
            {
                var huds = UnityEngine.Object.FindObjectsOfType<PlayerInfoHUDBase>(true);
                if (huds == null) return;

                for (int h = 0; h < huds.Length; h++)
                {
                    var spawned = huds[h]?._spawnedInfoObjects;
                    if (spawned == null) continue;

                    for (int i = 0; i < spawned.Count; i++)
                    {
                        var row = spawned[i];
                        if (row == null || row.playerInfo != display) continue;
                        ApplyKnownPlatformIcon(display, PlayerKeyForFgcc(row.fgcc));
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("NametagIcon: platform row " + ex.Message);
            }
        }

        public static void ApplyKnownPlatformIcon(TMP_Text target)
        {
            if (target == null) return;
            if (!FeatureMorePlatformIcon.Enabled) { RestoreKnownPlatformIcons(); return; }

            try
            {
                var huds = UnityEngine.Object.FindObjectsOfType<PlayerInfoHUDBase>(true);
                if (huds == null) return;

                for (int h = 0; h < huds.Length; h++)
                {
                    var spawned = huds[h]?._spawnedInfoObjects;
                    if (spawned == null) continue;

                    for (int i = 0; i < spawned.Count; i++)
                    {
                        var row = spawned[i];
                        if (row == null || !MatchesText(row.playerInfo, target)) continue;
                        ApplyKnownPlatformIcon(row.playerInfo, PlayerKeyForFgcc(row.fgcc));
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("NametagIcon: platform text " + ex.Message);
            }
        }

        public static void ApplyKnownPlatformIcon(PlayerInfoDisplay display, string playerKey)
        {
            if (display == null) return;

            string hideMode = SettingsService.Get(KEY_PLATFORM_HIDE, "none");
            var local = display.gameObject.name.IndexOf("Local", StringComparison.OrdinalIgnoreCase) >= 0;
            if (local)
            {
                string custom = SettingsService.Get(KEY_PLATFORM_CUSTOM, "");
                if (hideMode == "self" || hideMode == "everyone" || !string.IsNullOrEmpty(custom))
                {
                    ApplyPlatformIcon(display.gameObject, hideMode == "self" || hideMode == "everyone", custom);
                    return;
                }
            }
            else if (hideMode == "everyone")
            {
                ApplyPlatformIcon(display.gameObject, true, "");
                return;
            }

            if (string.IsNullOrEmpty(playerKey)) return;
            if (!FeatureMorePlatformIcon.Enabled) { RestoreKnownPlatformIcon(display); return; }

            var sprite = FeatureMorePlatformIcon.SpriteForPlayerKey(playerKey);
            if (sprite == null) return;
            if (HasPlatformOverride(display, playerKey)) return;

            SetPlatformIconSprite(display, sprite);
        }

        public static void ApplyPlatformIconByName(PlayerInfoDisplay display, string platformName)
        {
            if (display == null || !FeatureMorePlatformIcon.Enabled) return;

            var sprite = FeatureMorePlatformIcon.SpriteForName(platformName);
            if (sprite == null) return;

            SetPlatformIconSprite(display, sprite);
        }

        static void SetPlatformIconSprite(PlayerInfoDisplay display, Sprite sprite)
        {
            var goDisplay = display.TryCast<PlayerInfoDisplayGameObject>();
            if (goDisplay != null)
            {
                var r = goDisplay._platformIconRenderer;
                if (r != null)
                {
                    int id = r.GetInstanceID();
                    if (!_knownPlatform3dOriginals.ContainsKey(id))
                        _knownPlatform3dOriginals[id] = r.sprite;
                    r.sprite = sprite;
                    var rp = r.transform.localPosition;
                    r.transform.localPosition = new Vector3(0.0294f, rp.y, rp.z);
                    r.gameObject.SetActive(true);
                }
                return;
            }

            var canvasDisplay = display.TryCast<PlayerInfoDisplayCanvas>();
            if (canvasDisplay != null)
            {
                var img = canvasDisplay._platformIconImage;
                if (img != null)
                {
                    int id = img.GetInstanceID();
                    if (!_knownPlatformUiOriginals.ContainsKey(id))
                        _knownPlatformUiOriginals[id] = img.sprite;
                    img.sprite = sprite;
                    img.preserveAspect = true;
                    var ip = img.transform.localPosition;
                    img.transform.localPosition = new Vector3(0.0294f, ip.y, ip.z);
                    img.gameObject.SetActive(true);
                }
            }
        }

        public static void RestoreKnownPlatformIcons()
        {
            try
            {
                var huds = UnityEngine.Object.FindObjectsOfType<PlayerInfoHUDBase>(true);
                if (huds == null) return;

                for (int h = 0; h < huds.Length; h++)
                {
                    var spawned = huds[h]?._spawnedInfoObjects;
                    if (spawned == null) continue;

                    for (int i = 0; i < spawned.Count; i++)
                    {
                        var row = spawned[i];
                        if (row != null)
                            RestoreKnownPlatformIcon(row.playerInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("NametagIcon: platform restore " + ex.Message);
            }
        }

        private static void RestoreKnownPlatformIcon(PlayerInfoDisplay display)
        {
            if (display == null) return;

            var goDisplay = display.TryCast<PlayerInfoDisplayGameObject>();
            if (goDisplay != null)
            {
                var r = goDisplay._platformIconRenderer;
                if (r != null && _knownPlatform3dOriginals.TryGetValue(r.GetInstanceID(), out var sprite))
                    r.sprite = sprite;
                return;
            }

            var canvasDisplay = display.TryCast<PlayerInfoDisplayCanvas>();
            if (canvasDisplay != null)
            {
                var img = canvasDisplay._platformIconImage;
                if (img != null && _knownPlatformUiOriginals.TryGetValue(img.GetInstanceID(), out var sprite))
                    img.sprite = sprite;
            }
        }

        // fall feed's own PlayerSlot stopped carrying a usable key post-update (empty PlayerKey,
        // and StaticDisplayName crashes the process to even read — see FallfeedPatch.cs). A qualify/
        // eliminate notification shows up the instant the game despawns that player's bean, too late
        // for the HUD-row scan below to find them. FeatureTimePlacement.OnServerPlayerProgress has
        // the real key at the moment the server message lands, so it seeds name->key here as each
        // player finishes/dies — fall feed then just looks the name up. Keyed by name, not by
        // arrival order, so a missing or extra notification can't shift every later row onto the
        // wrong player.
        private static readonly Dictionary<string, string> _recentKeyByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void SeedKeyForName(string displayName, string playerKey)
        {
            if (string.IsNullOrEmpty(displayName) || string.IsNullOrEmpty(playerKey)) return;
            _recentKeyByName[StripRichText(displayName).Trim()] = playerKey;
        }

        // stale keys from a previous lobby would otherwise stick to a returning name — clear at round start.
        public static void ForgetRecentKeys() => _recentKeyByName.Clear();

        public static string ResolveKeyByDisplayName(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return "";
            displayName = StripRichText(displayName).Trim();
            if (_recentKeyByName.TryGetValue(displayName, out var cached) && !string.IsNullOrEmpty(cached))
                return cached;

            try
            {
                var huds = UnityEngine.Object.FindObjectsOfType<PlayerInfoHUDBase>(true);
                if (huds == null) return "";

                for (int h = 0; h < huds.Length; h++)
                {
                    var spawned = huds[h]?._spawnedInfoObjects;
                    if (spawned == null) continue;

                    for (int i = 0; i < spawned.Count; i++)
                    {
                        var row = spawned[i];
                        var tmp = TryGetNameText(row?.playerInfo);
                        if (tmp == null) continue;
                        if (!StripRichText(tmp.text ?? "").Trim().Equals(displayName, StringComparison.OrdinalIgnoreCase)) continue;
                        string key = PlayerKeyForFgcc(row.fgcc);
                        if (!string.IsNullOrEmpty(key)) _recentKeyByName[displayName] = key;
                        return key;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("NametagIcon: resolvekey " + ex.Message);
            }
            return "";
        }

        private static string PlayerKeyForFgcc(FallGuysCharacterController fgcc)
        {
            if (fgcc == null) return "";

            try
            {
                var cpm = PlayerUtils.GetClientPlayerManager();
                if (cpm?._playerIdIndex == null) return "";

                foreach (var kvp in cpm._playerIdIndex)
                {
                    var data = kvp.Value;
                    if (data == null || data.fgcc != fgcc) continue;
                    return data.playerKey ?? "";
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("NametagIcon: playerkey " + ex.Message);
            }

            return "";
        }

        private static bool HasPlatformOverride(PlayerInfoDisplay display, string playerKey)
        {
            var profile = RemoteProfileStore.TryGet(playerKey);
            var remote = profile?.nametag;
            if (remote != null && (remote.platformHide == "true" || !string.IsNullOrEmpty(remote.platformCustom)))
                return true;

            var local = display.gameObject.name.IndexOf("Local", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!local) return false;

            string hide = SettingsService.Get(KEY_PLATFORM_HIDE, "none");
            string custom = SettingsService.Get(KEY_PLATFORM_CUSTOM, "");
            return hide == "self" || hide == "everyone" || !string.IsNullOrEmpty(custom);
        }

        // ── Nameplate backing ─────────────────────────────────────────────────

        private const string BACKING_FILL_RES = "BetterFG.assets.features.customnameplate.nameplate_fill.png";
        private const string BACKING_OUTLINE_RES = "BetterFG.assets.features.customnameplate.nameplate_outline.png";

        private static readonly Dictionary<string, Sprite> _backingSpriteCache =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static Texture2D _backingFillTex;
        private static Texture2D _backingOutlineTex;

        private static Image FindBackingImage(Transform vmTransform)
        {
            if (vmTransform == null) return null;
            var children = vmTransform.GetComponentsInChildren<Image>(true);
            if (children == null) return null;
            for (int i = 0; i < children.Length; i++)
            {
                var img = children[i];
                if (img != null && img.gameObject.name == BACKING_OBJECT_NAME)
                    return img;
            }
            return null;
        }

        private static Texture2D LoadEmbeddedReadable(string resourceName, ref Texture2D cache)
        {
            if (cache != null) return cache;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream == null) { Plugin.Log.LogError($"NametagIcon: missing resource {resourceName}"); return null; }
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);
                tex.Apply();
                cache = tex;
            }
            catch (Exception ex) { Plugin.Log.LogError($"NametagIcon: LoadEmbeddedReadable {resourceName}: {ex.Message}"); }
            return cache;
        }

        private static Texture2D LoadReadableFile(string path)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(path));
            tex.Apply();
            return tex;
        }

        private static Sprite BuildBackingSprite(string path, float offX, float offY, float scale)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            string key = $"{path}|{offX:0.###}|{offY:0.###}|{scale:0.###}";
            if (_backingSpriteCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var fill = LoadEmbeddedReadable(BACKING_FILL_RES, ref _backingFillTex);
            var outline = LoadEmbeddedReadable(BACKING_OUTLINE_RES, ref _backingOutlineTex);
            if (fill == null) return null;

            try
            {
                int W = fill.width, H = fill.height;
                var user = LoadReadableFile(path);

                // Fit user texture to canvas height at scale=1, centered, then apply scale + normalized offset.
                float userAspect = (float)user.width / user.height;
                float drawH = H * Mathf.Max(0.01f, scale);
                float drawW = drawH * userAspect;
                float drawX = (W - drawW) * 0.5f + offX * W;
                float drawY = (H - drawH) * 0.5f + offY * H;

                var fillPx = fill.GetPixels();
                var outPx = outline != null ? outline.GetPixels() : null;
                bool outMatches = outPx != null && outline.width == W && outline.height == H;
                var dst = new Color[W * H];

                for (int y = 0; y < H; y++)
                {
                    for (int x = 0; x < W; x++)
                    {
                        int idx = y * W + x;
                        Color c = Color.clear;

                        float maskA = fillPx[idx].a;
                        if (maskA > 0.001f)
                        {
                            float u = (x - drawX) / drawW;
                            float v = (y - drawY) / drawH;
                            if (u >= 0f && u <= 1f && v >= 0f && v <= 1f)
                            {
                                var up = user.GetPixelBilinear(u, v);
                                up.a *= maskA;
                                c = up;
                            }
                        }

                        if (outMatches)
                        {
                            var o = outPx[idx];
                            float oa = o.a;
                            if (oa > 0.001f)
                            {
                                c.r = o.r * oa + c.r * (1f - oa);
                                c.g = o.g * oa + c.g * (1f - oa);
                                c.b = o.b * oa + c.b * (1f - oa);
                                c.a = oa + c.a * (1f - oa);
                            }
                        }

                        dst[idx] = c;
                    }
                }

                var result = new Texture2D(W, H, TextureFormat.RGBA32, false);
                result.wrapMode = TextureWrapMode.Clamp;
                result.filterMode = FilterMode.Point;
                result.SetPixels(dst);
                result.Apply();
                // applied to nameplate rows that survive scene loads — keep it alive across transitions.
                result.hideFlags = HideFlags.HideAndDontSave;

                var spr = Sprite.Create(result, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f));
                spr.hideFlags = HideFlags.HideAndDontSave;
                _backingSpriteCache[key] = spr;
                return spr;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"NametagIcon: BuildBackingSprite {path}: {ex.Message}");
                return null;
            }
        }

        public static Vector2Int BackingCanvasSize()
        {
            var fill = LoadEmbeddedReadable(BACKING_FILL_RES, ref _backingFillTex);
            return fill != null ? new Vector2Int(fill.width, fill.height) : new Vector2Int(606, 166);
        }

        public static Sprite GetBackingPreviewSprite(string path, float offX, float offY, float scale)
            => BuildBackingSprite(path, offX, offY, scale);

        // ── Nickname subtext ──────────────────────────────────────────────────

        public static void ApplyLocalNickname(Transform vmTransform, bool party)
        {
            bool enabled = SettingsService.Get(KEY_NICKNAME_ENABLED, "false") == "true";
            string text = SettingsService.Get(KEY_NICKNAME_TEXT, "");
            ApplyNickname(vmTransform, party, enabled, text);
        }

        private static readonly Vector3 NICKNAME_CONTAINER_POS_EMPTY = new Vector3(28.8173f, -8.9213f, 0f);
        private static readonly Vector3 NICKNAME_CONTAINER_POS_FILLED = new Vector3(28.8001f, 1.9f, 0f);

        public static bool ApplyNickname(Transform vmTransform, bool party, bool enabled, string text)
        {
            if (vmTransform == null) return false;

            string childName = party ? NICKNAME_PARTY_NAME : NICKNAME_REGULAR_NAME;
            var t = FindDescendant(vmTransform, childName);
            if (t == null) return false;

            var tmp = t.GetComponent<TMPro.TextMeshProUGUI>();
            if (tmp == null) return false;

            int id = tmp.GetInstanceID();
            var container = party ? null : t.parent;

            if (!enabled)
            {
                if (_nicknameOriginals.TryGetValue(id, out var orig))
                {
                    tmp.text = orig.text;
                    if (container != null) container.localPosition = orig.pos;
                    _nicknameOriginals.Remove(id);
                }
                return true;
            }

            if (!_nicknameOriginals.ContainsKey(id))
                _nicknameOriginals[id] = (tmp.text, container != null ? container.localPosition : Vector3.zero);

            string applied = text ?? "";
            tmp.text = applied;
            if (container != null)
                container.localPosition = string.IsNullOrEmpty(applied)
                    ? NICKNAME_CONTAINER_POS_EMPTY
                    : NICKNAME_CONTAINER_POS_FILLED;
            return true;
        }

        public static void ApplyLocalPartyBacking(Transform partyVmTransform)
        {
            bool enabled = SettingsService.Get(KEY_BACKING_ENABLED, "false") == "true";
            string path = SettingsService.Get(KEY_BACKING_PATH, "");
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float offX = float.TryParse(SettingsService.Get(KEY_BACKING_OFFSET_X, "0"), System.Globalization.NumberStyles.Float, ci, out float ox) ? ox : 0f;
            float offY = float.TryParse(SettingsService.Get(KEY_BACKING_OFFSET_Y, "0"), System.Globalization.NumberStyles.Float, ci, out float oy) ? oy : 0f;
            float scale = float.TryParse(SettingsService.Get(KEY_BACKING_SCALE, "1"), System.Globalization.NumberStyles.Float, ci, out float sv) ? sv : 1f;
            ApplyPartyBacking(partyVmTransform, enabled, path, offX, offY, scale);
        }

        // party backing with explicit values (remote profiles pass their own)
        public static void ApplyPartyBacking(Transform partyVmTransform, bool enabled, string path, float offX, float offY, float scale)
        {
            if (partyVmTransform == null) return;

            var container = partyVmTransform.Find("Container_Nameplate/Container")
                          ?? FindDescendant(partyVmTransform, "Container_Nameplate")?.Find("Container")
                          ?? FindDescendant(partyVmTransform, "Container");
            if (container == null) return;

            Sprite sprite = enabled && !string.IsNullOrEmpty(path)
                ? BuildBackingSprite(path, offX, offY, scale)
                : null;

            ApplyPartyBackingImage(container.Find("Nameplate_Unselected") ?? FindDescendant(container, "Nameplate_Unselected"), enabled, sprite);
            ApplyPartyBackingImage(container.Find("Nameplate_Selected") ?? FindDescendant(container, "Nameplate_Selected"), enabled, sprite);
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null) return null;
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].name == name)
                    return all[i];
            return null;
        }

        private static void ApplyPartyBackingImage(Transform t, bool enabled, Sprite sprite)
        {
            if (t == null) return;
            var img = t.GetComponent<Image>();
            if (img == null) return;

            int id = img.GetInstanceID();

            if (!enabled || sprite == null)
            {
                if (_backingOriginals.TryGetValue(id, out var orig))
                {
                    img.sprite = orig;
                    _backingOriginals.Remove(id);
                }
                return;
            }

            if (!_backingOriginals.ContainsKey(id))
                _backingOriginals[id] = img.sprite;
            img.sprite = sprite;
        }

        public static void ApplyLocalBacking(Transform vmTransform)
        {
            bool enabled = SettingsService.Get(KEY_BACKING_ENABLED, "false") == "true";
            string path = SettingsService.Get(KEY_BACKING_PATH, "");
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float offX = float.TryParse(SettingsService.Get(KEY_BACKING_OFFSET_X, "0"), System.Globalization.NumberStyles.Float, ci, out float ox) ? ox : 0f;
            float offY = float.TryParse(SettingsService.Get(KEY_BACKING_OFFSET_Y, "0"), System.Globalization.NumberStyles.Float, ci, out float oy) ? oy : 0f;
            float scale = float.TryParse(SettingsService.Get(KEY_BACKING_SCALE, "1"), System.Globalization.NumberStyles.Float, ci, out float sv) ? sv : 1f;
            ApplyBacking(vmTransform, enabled, path, offX, offY, scale);
        }

        public static void ApplyBacking(Transform vmTransform, bool enabled, string path, float offX, float offY, float scale)
        {
            var img = FindBackingImage(vmTransform);
            if (img == null) return;

            int id = img.GetInstanceID();

            if (!enabled || string.IsNullOrEmpty(path))
            {
                if (_backingOriginals.TryGetValue(id, out var orig))
                {
                    img.sprite = orig;
                    _backingOriginals.Remove(id);
                }
                return;
            }

            var sprite = BuildBackingSprite(path, offX, offY, scale);
            if (sprite == null) return;

            if (!_backingOriginals.ContainsKey(id))
                _backingOriginals[id] = img.sprite;
            img.sprite = sprite;
        }

        // ── 3D nametag icon ───────────────────────────────────────────────────

        public static void ApplyIcon() => ApplyIconTo(NametagFinder.FindLocalDisplay(), CfgFromSettings());

        public static void ApplyIconTo(PlayerInfoDisplay display, NametagCfg cfg)
        {
            var nameAndCrown = NameAndCrownOf(display);
            if (cfg.iconMode == "none" || string.IsNullOrEmpty(cfg.iconMode))
            {
                if (nameAndCrown != null) RemoveIconUnder(nameAndCrown);
                return;
            }

            Sprite sprite = ResolveIconSprite(cfg.iconMode, cfg.iconCountry ?? "", cfg.iconPath ?? "");
            if (sprite == null) return;
            if (nameAndCrown == null) { Plugin.Log.LogInfo("NametagIcon: NameAndCrownParent not found"); return; }
            AttachIcon(nameAndCrown, sprite);
        }

        // NameAndCrownParent for a given display via its crown layout helper (works for both subtypes), which
        // is the same _crownParentTransform the crown code uses — no hardcoded child-path lookup.
        internal static Transform NameAndCrownOf(PlayerInfoDisplay display)
        {
            if (display == null) return null;
            var go = display.TryCast<PlayerInfoDisplayGameObject>();
            var canvas = go == null ? display.TryCast<PlayerInfoDisplayCanvas>() : null;
            var helper = go != null ? go._crownRankPlayerTagLayoutHelper : canvas?._crownRankPlayerTagLayoutHelper;
            if (helper != null && helper._crownParentTransform != null) return helper._crownParentTransform;
            // fallback: a NameAndCrownParent child under the display transform.
            return display.transform.Find("NameAndCrownParent");
        }

        public static void RemoveIcon()
        {
            var nameAndCrown = NametagFinder.FindNameAndCrownParent();
            if (nameAndCrown != null) RemoveIconUnder(nameAndCrown);
        }

        private static void RemoveIconUnder(Transform nameAndCrown)
        {
            RescueAndDestroyWrapper(nameAndCrown);
            var existing = nameAndCrown.Find(FLAG_OBJECT_NAME);
            if (existing != null) UnityEngine.Object.Destroy(existing.gameObject);
        }

        // ── UI nameplate — local ──────────────────────────────────────────────

        public enum NameplateType { Regular, Party }

        public static void ApplyToNameplate(TMPro.TextMeshProUGUI tmp, string displayName, NameplateType type = NameplateType.Regular)
            => ApplyToNameplate(tmp, displayName, CfgFromSettings(), type);

        public static void ApplyToNameplate(TMPro.TextMeshProUGUI tmp, string displayName, NametagCfg cfg, NameplateType type = NameplateType.Regular)
        {
            if (tmp == null) return;
            BetterFG.Customization.UI.FontReplacementService.ProtectText(tmp);

            if (!cfg.enabled)
            {
                tmp.text = displayName;
                tmp.color = UnityEngine.Color.white;
                if (AssetManager.DefaultNameMaterial != null) tmp.fontSharedMaterial = AssetManager.DefaultNameMaterial;
                RemoveUIIcon(tmp.transform);
                // font replacement is independent of the nametag feature — still apply the custom font
                // (with the default shadow material's outline) even when nametag styling is off.
                BetterFG.Customization.UI.FontReplacementService.ApplyToNametag(tmp);
                return;
            }

            ApplyTextStyle(tmp, displayName, ArgsFromCfg(cfg));
            MarkStyled(tmp, cfg.style);

            if (cfg.iconMode == "none" || string.IsNullOrEmpty(cfg.iconMode)) { RemoveUIIcon(tmp.transform); return; }

            Sprite sprite = ResolveIconSprite(cfg.iconMode, cfg.iconCountry ?? "", cfg.iconPath ?? "");
            if (sprite == null) { RemoveUIIcon(tmp.transform); return; }
            AttachUIIcon(tmp, sprite, type);
        }

        // ── UI nameplate — remote ─────────────────────────────────────────────

        public static void ApplyRemoteToNameplate(TMPro.TextMeshPro tmp3d, string fallbackName, RemoteNametagInfo info)
        {
            if (tmp3d == null || info == null) return;
            BetterFG.Customization.UI.FontReplacementService.ProtectText(tmp3d);

            string name = string.IsNullOrEmpty(info.customName) ? StripRichText(fallbackName) : info.customName;
            ApplyTextStyle(tmp3d, name, ArgsFromRemote(info));
            MarkStyled(tmp3d, info.nameStyle);

            // after the first apply the text lives inside our wrapper, so step up to the real parent
            var nameAndCrown = tmp3d.transform.parent;
            if (nameAndCrown != null && nameAndCrown.name == "BetterFG_NameWrapper")
                nameAndCrown = nameAndCrown.parent;
            if (nameAndCrown == null) return;

            if (nameAndCrown.Find(FLAG_OBJECT_NAME) != null) return;

            if (string.IsNullOrEmpty(info.iconMode) || info.iconMode == "none") return;

            Sprite sprite = ResolveIconSprite(info.iconMode, info.iconCountry ?? "", info.iconPath ?? "");
            if (sprite == null) return;

            bool isCustom = info.iconMode == "custom";
            float scaleMul = isCustom ? info.iconScale : 1f;
            float offX = isCustom ? info.iconOffX : 0f;
            float offY = isCustom ? info.iconOffY : 0f;
            float finalScale = BASE_SCALE * scaleMul;
            float iconWorldW = sprite.texture.width / sprite.pixelsPerUnit * finalScale;
            float crownWorldW = GetCrownWorldWidth(nameAndCrown);

            var wrapperT = tmp3d.transform.parent.name == "BetterFG_NameWrapper" ? tmp3d.transform.parent : null;
            if (wrapperT == null)
            {
                var wrapperGo = new GameObject("BetterFG_NameWrapper");
                wrapperT = wrapperGo.transform;
                wrapperT.SetParent(nameAndCrown, false);
                wrapperT.localPosition = Vector3.zero;
                wrapperT.localRotation = Quaternion.identity;
                wrapperT.localScale = Vector3.one;
                tmp3d.transform.SetParent(wrapperT, true);
            }

            var iconGo = new GameObject(FLAG_OBJECT_NAME);
            iconGo.transform.SetParent(nameAndCrown, false);
            iconGo.transform.localScale = new Vector3(finalScale, finalScale, finalScale);
            iconGo.transform.localRotation = Quaternion.identity;
            var sr = iconGo.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 1;
            // start at the name's current alpha — the game fades nametags out (hide-names / distance) and the
            // icon should be just as faded on first show, not pop in fully opaque until the next alpha sync.
            sr.color = new Color(1f, 1f, 1f, tmp3d.color.a);
            _icon3d[tmp3d.GetInstanceID()] = sr;
            _iconPtrs.Add(tmp3d.m_CachedPtr);
            SyncIconGate();

            MaybeAttachGifAnimator(iconGo, sr, null, info.iconMode, info.iconPath);
            RememberPlacement(nameAndCrown, tmp3d, wrapperT, iconGo.transform, iconWorldW, isCustom, offX, offY);

            var host = BeanMonitorService.Instance;
            if (host != null)
                host.StartCoroutine(
                    RepositionNextFrame(tmp3d, wrapperT, iconGo.transform, iconWorldW, crownWorldW, isCustom, offX, offY, nameAndCrown)
                    .WrapToIl2Cpp());
        }

        public static void ApplyRemoteToNameplate(TMPro.TextMeshProUGUI tmp, string fallbackName, RemoteNametagInfo info)
        {
            if (tmp == null || info == null) return;
            BetterFG.Customization.UI.FontReplacementService.ProtectText(tmp);

            string name = string.IsNullOrEmpty(info.customName) ? StripRichText(fallbackName) : info.customName;

            ApplyTextStyle(tmp, name, ArgsFromRemote(info));
            MarkStyled(tmp, info.nameStyle);

            if (string.IsNullOrEmpty(info.iconMode) || info.iconMode == "none")
            {
                RemoveUIIcon(tmp.transform);
                return;
            }

            Sprite sprite = ResolveIconSprite(info.iconMode, info.iconCountry ?? "", info.iconPath ?? "");
            if (sprite == null) { RemoveUIIcon(tmp.transform); return; }
            AttachUIIcon(tmp, sprite, NameplateType.Regular, info.iconMode, info.iconPath, info.iconScale, info.iconOffX, info.iconOffY);
        }

        public static void ApplyRemoteToNameplate(TMP_Text tmp, string fallbackName, RemoteNametagInfo info)
        {
            if (tmp == null || info == null) return;
            var ui = tmp.TryCast<TextMeshProUGUI>();
            if (ui != null) { ApplyRemoteToNameplate(ui, fallbackName, info); return; }
            var world = tmp.TryCast<TextMeshPro>();
            if (world != null) { ApplyRemoteToNameplate(world, fallbackName, info); return; }
        }

        public static void ApplyRemoteToDisplay(PlayerInfoDisplay display, string fallbackName, RemoteNametagInfo info)
        {
            if (display == null || info == null) return;
            var tmp = TryGetNameText(display);
            if (tmp == null) return;
            ApplyRemoteToNameplate(tmp, fallbackName, info);
        }

        private static readonly Dictionary<IntPtr, IntPtr> _styled = new Dictionary<IntPtr, IntPtr>();

        internal static void MarkStyled(TMP_Text tmp, string nameStyle)
        {
            if (tmp == null) return;
            var mat = nameStyle == "none" ? null : tmp.fontSharedMaterial;
            _styled[tmp.m_CachedPtr] = mat != null ? mat.m_CachedPtr : IntPtr.Zero;
        }

        public static void RevertRemote(NameTagViewModel vm)
        {
            if (vm == null) return;

            var tmp3d = vm._playerNameText;
            if (tmp3d == null || !_styled.TryGetValue(tmp3d.m_CachedPtr, out var ourMat)) return;
            _styled.Remove(tmp3d.m_CachedPtr);

            UnregisterGradient(tmp3d);
            _icon3d.Remove(tmp3d.GetInstanceID());
            _iconUI.Remove(tmp3d.GetInstanceID());
            _iconPtrs.Remove(tmp3d.m_CachedPtr);
            SyncIconGate();

            var curMat = tmp3d.fontSharedMaterial;
            if (ourMat != IntPtr.Zero && curMat != null && curMat.m_CachedPtr == ourMat)
            {
                tmp3d.enableVertexGradient = false;
                if (AssetManager.DefaultNameMaterial != null) tmp3d.fontSharedMaterial = AssetManager.DefaultNameMaterial;
                tmp3d.color = new UnityEngine.Color(1f, 1f, 1f, tmp3d.color.a);
            }
            tmp3d.text = System.Text.RegularExpressions.Regex.Replace(tmp3d.text ?? "", "<[^>]*>", "").Trim();

            var nameAndCrown = tmp3d.transform.parent;
            if (nameAndCrown != null && nameAndCrown.name == "BetterFG_NameWrapper")
                nameAndCrown = nameAndCrown.parent;
            if (nameAndCrown != null)
            {
                var flag = nameAndCrown.Find(FLAG_OBJECT_NAME);
                if (flag != null) UnityEngine.Object.Destroy(flag.gameObject);
                RescueAndDestroyWrapper(nameAndCrown);
            }

            RemoveUIIcon(tmp3d.transform);
            ApplyBacking(vm.transform, false, "", 0f, 0f, 1f);
            ApplyNickname(vm.transform, party: false, enabled: false, "");

            var huds = UnityEngine.Object.FindObjectsOfType<PlayerInfoHUDBase>(true);
            if (huds == null) return;
            for (int h = 0; h < huds.Length; h++)
            {
                var spawned = huds[h]?._spawnedInfoObjects;
                if (spawned == null) continue;
                for (int i = 0; i < spawned.Count; i++)
                {
                    var pi = spawned[i]?.playerInfo;
                    if (!MatchesText(pi, tmp3d)) continue;
                    ApplyPlatformIcon(pi?.gameObject, false, "");
                    return;
                }
            }
        }

        public static void ApplyLocalInlineName(TMPro.TextMeshProUGUI tmp, string displayName, string spriteName = "", float iconScale = 1f, float iconVoffset = 0.04f)
        {
            if (tmp == null) return;

            if (SettingsService.Get(KEY_ENABLED, "false") != "true")
            {
                tmp.text = InlinePlatformTag(tmp.text ?? "", spriteName, iconScale, iconVoffset) + displayName;
                tmp.color = UnityEngine.Color.white;
                return;
            }

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float r = float.TryParse(SettingsService.Get(KEY_COLOR_R, "1"), System.Globalization.NumberStyles.Float, ci, out float rv) ? rv : 1f;
            float g = float.TryParse(SettingsService.Get(KEY_COLOR_G, "1"), System.Globalization.NumberStyles.Float, ci, out float gv) ? gv : 1f;
            float b = float.TryParse(SettingsService.Get(KEY_COLOR_B, "1"), System.Globalization.NumberStyles.Float, ci, out float bv) ? bv : 1f;
            bool bold = SettingsService.Get(KEY_BOLD, "false") == "true";
            bool italic = SettingsService.Get(KEY_ITALIC, "false") == "true";

            tmp.richText = true;
            tmp.color = UnityEngine.Color.white;
            tmp.text = BuildInlineStyledName(tmp.text ?? "", displayName, r, g, b, bold, italic, "", false, spriteName, "", iconScale, iconVoffset);
        }

        public static void ApplyInlinePlatform(TMPro.TextMeshProUGUI tmp, string spriteName, float iconScale = 1f, float iconVoffset = 0.04f)
        {
            if (tmp == null || string.IsNullOrEmpty(spriteName)) return;

            tmp.richText = true;
            tmp.text = ReplaceInlinePlatform(tmp.text ?? "", spriteName, iconScale, iconVoffset);
        }

        public static string BuildInlinePlatformName(string original, string playerKey, bool hidePlatform = false, string platformCustom = "", float iconScale = 1f, float iconVoffset = 0.04f)
        {
            if (hidePlatform) return StripInlinePlatform(original);

            string sprite = ResolveInlineSprite("", playerKey, platformCustom);
            if (string.IsNullOrEmpty(sprite)) return original ?? "";
            return ReplaceInlinePlatform(original ?? "", sprite, iconScale, iconVoffset);
        }

        public static string BuildInlineStyledName(
            string original,
            string displayName,
            float r = 1f, float g = 1f, float b = 1f,
            bool bold = false, bool italic = false,
            string platformCustom = "",
            bool hidePlatform = false,
            string spriteName = "",
            string playerKey = "",
            float iconScale = 1f,
            float iconVoffset = 0.04f)
        {
            string platform = hidePlatform ? "" : InlinePlatformTag(original, ResolveInlineSprite(spriteName, playerKey, platformCustom), iconScale, iconVoffset);

            string style = "", close = "";
            if (bold) { style += "<b>"; close = "</b>" + close; }
            if (italic) { style += "<i>"; close = "</i>" + close; }

            int ri = Mathf.Clamp(Mathf.RoundToInt(r * 255), 0, 255);
            int gi = Mathf.Clamp(Mathf.RoundToInt(g * 255), 0, 255);
            int bi = Mathf.Clamp(Mathf.RoundToInt(b * 255), 0, 255);

            return $"{platform}<color=#{ri:X2}{gi:X2}{bi:X2}>{style}{displayName}{close}</color>";
        }

        public static bool ApplyInlinePlatformAsset(TMPro.TextMeshProUGUI tmp)
            => ApplyInlinePlatformAsset(tmp, FeatureMorePlatformIcon.SpriteAsset());

        public static bool ApplyInlinePlatformAssetOutline(TMPro.TextMeshProUGUI tmp)
            => ApplyInlinePlatformAsset(tmp, FeatureMorePlatformIcon.SpriteAssetOutline());

        private static bool ApplyInlinePlatformAsset(TMPro.TextMeshProUGUI tmp, TMP_SpriteAsset asset)
        {
            if (tmp == null) return false;
            if (!FeatureMorePlatformIcon.Enabled) return false;

            if (asset == null) return false;

            if (tmp.spriteAsset == null)
            {
                tmp.spriteAsset = asset;
                tmp.richText = true;
                return true;
            }

            if (tmp.spriteAsset == asset)
            {
                tmp.richText = true;
                return true;
            }

            if (tmp.spriteAsset.fallbackSpriteAssets == null)
                tmp.spriteAsset.fallbackSpriteAssets = new Il2CppSystem.Collections.Generic.List<TMP_SpriteAsset>();

            for (int i = 0; i < tmp.spriteAsset.fallbackSpriteAssets.Count; i++)
                if (tmp.spriteAsset.fallbackSpriteAssets[i] == asset)
                {
                    tmp.richText = true;
                    return true;
                }

            tmp.spriteAsset.fallbackSpriteAssets.Add(asset);
            tmp.spriteAsset.UpdateLookupTables();
            tmp.richText = true;
            return true;
        }

        private static string ResolveInlineSprite(string spriteName, string playerKey, string platformCustom)
        {
            if (!string.IsNullOrEmpty(spriteName)) return spriteName;

            string fromKey = FeatureMorePlatformIcon.SpriteNameForPlayerKey(playerKey);
            if (!string.IsNullOrEmpty(fromKey)) return fromKey;

            if (string.IsNullOrEmpty(platformCustom)) return "";
            return PlatformKeyToSpriteName(platformCustom);
        }

        private static string InlinePlatformTag(string original, string spriteName, float iconScale = 1f, float iconVoffset = 0.04f)
        {
            if (!string.IsNullOrEmpty(spriteName))
            {
                if (iconScale > 0f && Math.Abs(iconScale - 1f) > 0.001f)
                    return $"<size={Mathf.RoundToInt(iconScale * 100f)}%><voffset={iconVoffset:0.###}em><sprite name=\"{spriteName}\"></voffset></size> ";
                return $"<voffset={iconVoffset:0.###}em><sprite name=\"{spriteName}\"></voffset> ";
            }

            return KeepInlinePlatform(original);
        }

        private static string ReplaceInlinePlatform(string original, string spriteName, float iconScale = 1f, float iconVoffset = 0.04f)
            => InlinePlatformTag(original, spriteName, iconScale, iconVoffset) + StripInlinePlatform(original);

        private static string StripInlinePlatform(string original)
        {
            if (string.IsNullOrEmpty(original)) return "";

            int spriteStart = original.IndexOf("<sprite", StringComparison.Ordinal);
            if (spriteStart < 0) return original;

            int spriteEnd = original.IndexOf('>', spriteStart);
            if (spriteEnd < 0) return original;

            int start = spriteStart;
            int sizeStart = original.LastIndexOf("<size", spriteStart, StringComparison.Ordinal);
            int voffsetStart = original.LastIndexOf("<voffset", spriteStart, StringComparison.Ordinal);
            if (sizeStart >= 0 && (voffsetStart < 0 || sizeStart < voffsetStart))
                start = sizeStart;
            else if (voffsetStart >= 0)
                start = voffsetStart;

            while (start > 0 && original[start - 1] == ' ') start--;

            int afterSprite = spriteEnd + 1;
            if (afterSprite < original.Length && original.IndexOf("</voffset>", afterSprite, StringComparison.Ordinal) == afterSprite)
                afterSprite += "</voffset>".Length;
            if (afterSprite < original.Length && original.IndexOf("</size>", afterSprite, StringComparison.Ordinal) == afterSprite)
                afterSprite += "</size>".Length;
            while (afterSprite < original.Length && original[afterSprite] == ' ') afterSprite++;

            return original.Substring(0, start) + original.Substring(afterSprite);
        }

        private static string KeepInlinePlatform(string original)
        {
            int spriteStart = original.IndexOf("<sprite", StringComparison.Ordinal);
            if (spriteStart < 0) return "";

            int spriteEnd = original.IndexOf('>', spriteStart);
            if (spriteEnd < 0) return "";

            int afterSprite = spriteEnd + 1;
            while (afterSprite < original.Length && original[afterSprite] == ' ') afterSprite++;
            return original.Substring(0, afterSprite);
        }

        private static string PlatformKeyToSpriteName(string platformKey)
        {
            if (string.IsNullOrEmpty(platformKey)) return "";
            string k = platformKey.ToLowerInvariant();
            if (FeatureMorePlatformIcon.Enabled)
            {
                foreach (var id in FeatureMorePlatformIcon.PlatformIconIds())
                    if (string.Equals(k, id, StringComparison.OrdinalIgnoreCase))
                        return id;
            }
            if (k.StartsWith("android_") || k.StartsWith("ios_")) return "platform-mobile";
            if (k.StartsWith("ps4_") || k.StartsWith("ps5_") ||
                k.StartsWith("xb1_") || k.StartsWith("xsx_") || k.StartsWith("switch_"))
                return "platform-console";
            return "";
        }

        private static readonly Dictionary<IntPtr, TMP_Text> _nameTextByDisplay = new Dictionary<IntPtr, TMP_Text>();

        public static TMP_Text TryGetNameText(PlayerInfoDisplay display)
        {
            if (display == null) return null;

            IntPtr id = display.Pointer;
            if (_nameTextByDisplay.TryGetValue(id, out var cached) && cached != null) return cached;

            TMP_Text found = null;
            var goDisplay = display.TryCast<PlayerInfoDisplayGameObject>();
            if (goDisplay != null && goDisplay._text != null) found = goDisplay._text;

            if (found == null)
            {
                var canvasDisplay = display.TryCast<PlayerInfoDisplayCanvas>();
                if (canvasDisplay != null && canvasDisplay._text != null) found = canvasDisplay._text;
            }

            if (found == null) found = display.gameObject.GetComponentInChildren<TMP_Text>(true);
            if (found != null) _nameTextByDisplay[id] = found;
            return found;
        }

        public static bool ApplyPlatformIcon(GameObject displayObject, bool hide, string customSprite)
        {
            if (displayObject == null) return false;

            var custom = FeatureMorePlatformIcon.Enabled && !hide && !string.IsNullOrEmpty(customSprite)
                ? FeatureMorePlatformIcon.SpriteForName(customSprite)
                : null;

            var goDisplay = displayObject.GetComponent<PlayerInfoDisplayGameObject>();
            if (goDisplay != null)
            {
                var r = goDisplay._platformIconRenderer;
                if (r != null) r.gameObject.SetActive(!hide);
                if (r != null && custom != null)
                {
                    // stash the game's real sprite once so clearing the custom pick puts it back
                    if (!_knownPlatform3dOriginals.ContainsKey(r.GetInstanceID()))
                        _knownPlatform3dOriginals[r.GetInstanceID()] = r.sprite;
                    r.sprite = custom;
                }
                else if (!hide && !string.IsNullOrEmpty(customSprite)) goDisplay.SetPlatformIcon(customSprite);
                if (r != null && !hide) { var rp = r.transform.localPosition; r.transform.localPosition = new Vector3(0.0294f, rp.y, rp.z); }
                return true;
            }

            var canvasDisplay = displayObject.GetComponent<PlayerInfoDisplayCanvas>();
            if (canvasDisplay != null)
            {
                var img = canvasDisplay._platformIconImage;
                if (img != null) img.gameObject.SetActive(!hide);
                if (img != null && custom != null)
                {
                    if (!_knownPlatformUiOriginals.ContainsKey(img.GetInstanceID()))
                        _knownPlatformUiOriginals[img.GetInstanceID()] = img.sprite;
                    img.sprite = custom; img.preserveAspect = true;
                }
                else if (!hide && !string.IsNullOrEmpty(customSprite)) canvasDisplay.SetPlatformIcon(customSprite);
                if (img != null && !hide) { var ip = img.transform.localPosition; img.transform.localPosition = new Vector3(0.0294f, ip.y, ip.z); }
                return true;
            }

            return false;
        }

        public static bool MatchesText(PlayerInfoDisplay display, TMP_Text target)
        {
            if (display == null || target == null) return false;
            var txt = TryGetNameText(display);
            return txt != null && txt.GetInstanceID() == target.GetInstanceID();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private struct TextStyleArgs
        {
            public float r, g, b;
            public bool bold, italic;
            public string style;
            public string gradientColors;
            public float gradientAngle;
            public bool outlineEnabled;
            public float outlineR, outlineG, outlineB, outlineWidth;
            public string outlineMode;
        }

        private static TextStyleArgs ArgsFromCfg(NametagCfg cfg) => new TextStyleArgs
        {
            r = cfg.r, g = cfg.g, b = cfg.b,
            bold = cfg.bold, italic = cfg.italic,
            style = cfg.style, gradientColors = cfg.gradientColors, gradientAngle = cfg.gradientAngle,
            outlineEnabled = cfg.outlineEnabled,
            outlineR = cfg.outlineR, outlineG = cfg.outlineG, outlineB = cfg.outlineB,
            outlineWidth = cfg.outlineWidth,
            outlineMode = cfg.outlineMode,
        };

        private static TextStyleArgs ArgsFromRemote(RemoteNametagInfo info) => new TextStyleArgs
        {
            r = info.r, g = info.g, b = info.b,
            bold = info.bold, italic = info.italic,
            style = info.nameStyle ?? "",
            gradientColors = info.gradientColors ?? "",
            gradientAngle = info.gradientAngle,
            outlineEnabled = info.outlineEnabled,
            outlineR = info.outlineR, outlineG = info.outlineG, outlineB = info.outlineB,
            outlineWidth = info.outlineWidth,
            outlineMode = info.outlineMode,
        };

        private static void ApplyTextStyle(TMPro.TextMeshProUGUI tmp, string name, TextStyleArgs args)
            => ApplyTextStyle((TMP_Text)tmp, name, args);

        private static void ApplyTextStyle(TMP_Text tmp, string name, TextStyleArgs args)
        {
            if (tmp == null) return;
            float r = args.r, g = args.g, b = args.b;
            bool bold = args.bold, italic = args.italic;
            string styleStr = args.style;
            string gradientColors = args.gradientColors;
            float gradientAngle = args.gradientAngle;
            if (styleStr != "gradient") UnregisterGradient(tmp);
            // this TMP is a nameplate — tell the font replacement system to never touch it, so the
            // gold/fame material stays on its original font and never corrupts.
            BetterFG.Customization.UI.FontReplacementService.ProtectText(tmp);
            tmp.richText = true;
            // the game fades this text's alpha to 0 for its hide-names setting (and on respawn/round-start
            // re-shows it via SetTextAlpha). we rewrite text/colour from scratch below, so grab the current
            // alpha and carry it through — otherwise every reapply snaps the name back to fully visible.
            float a = tmp.color.a;
            string style = "", close = "";
            if (bold) { style += "<b>"; close = "</b>" + close; }
            if (italic) { style += "<i>"; close = "</i>" + close; }

            if (styleStr == "none")
            {
                // "None" — leave the name's material/gradient exactly as the game set it.
                // Only apply the colour tag + bold/italic so colour sliders still work,
                // and skip the font-replacement material handoff below.
                int rn = Mathf.Clamp(Mathf.RoundToInt(r * 255), 0, 255);
                int gn = Mathf.Clamp(Mathf.RoundToInt(g * 255), 0, 255);
                int bn = Mathf.Clamp(Mathf.RoundToInt(b * 255), 0, 255);
                tmp.text = $"<color=#{rn:X2}{gn:X2}{bn:X2}>{style}{name}{close}</color>";
                var cn = tmp.color; cn.a = a; tmp.color = cn;
                // "none" keeps the game's material untouched, but font replacement is still independent —
                // hand off so the custom font lands on whatever material the game already set.
                BetterFG.Customization.UI.FontReplacementService.ApplyToNametag(tmp);
                return;
            }

            if (styleStr == "goldcolored")
            {
                var mat = AssetManager.GoldNameMaterial;
                if (mat != null)
                {
                    // Use an instance copy so we never mutate the shared gold material
                    // (the plain "gold" style reuses that same shared asset).
                    var inst = new UnityEngine.Material(mat);
                    inst.SetTexture("_FaceTex", null);
                    // Face color comes from the per-vertex gradient below, so keep the
                    // material face white (vertex color multiplies the white face).
                    inst.SetColor("_FaceColor", UnityEngine.Color.white);
                    inst.SetColor("_OutlineColor", DarkerSaturated(r, g, b, 0.70f));
                    if (inst.GetFloat("_OutlineWidth") <= 0f)
                        inst.SetFloat("_OutlineWidth", 0.2f);
                    tmp.fontMaterial = inst;
                }
                var face = new UnityEngine.Color(r, g, b, 1f);
                tmp.color = new UnityEngine.Color(1f, 1f, 1f, a);
                tmp.enableVertexGradient = true;
                tmp.colorGradient = new VertexGradient(
                    face, face, UnityEngine.Color.white, UnityEngine.Color.white);
                tmp.text = $"{style}{name}{close}";
            }
            else if (styleStr == "gradient")
            {
                tmp.enableVertexGradient = false;
                var plainMat = AssetManager.NameFontAsset?.material;
                if (plainMat != null) tmp.fontSharedMaterial = plainMat;
                tmp.color = new UnityEngine.Color(1f, 1f, 1f, a);
                var stops = ParseGradientColors(gradientColors);
                if (stops.Count == 0) stops.Add(new UnityEngine.Color(r, g, b, 1f));
                var sb = new System.Text.StringBuilder(name.Length + 16);
                if (bold) sb.Append("<b>");
                if (italic) sb.Append("<i>");
                sb.Append(name);
                if (italic) sb.Append("</i>");
                if (bold) sb.Append("</b>");
                tmp.text = sb.ToString();
                EnsureNametagRenderQueue(tmp);
                BetterFG.Customization.UI.FontReplacementService.ApplyToNametag(tmp);
                EnsureNametagRenderQueue(tmp);
                ApplyOutlineIfEnabled(tmp, args);
                RegisterGradient(tmp, stops, gradientAngle);
                ApplyGradientVertexColors(tmp, stops, gradientAngle);
                return;
            }
            else if (styleStr == "gold")
            {
                tmp.enableVertexGradient = false;
                var mat = AssetManager.GoldNameMaterial;
                // no per-player state on this one — every "gold" nametag can share the same material,
                // so assign via fontSharedMaterial (not fontMaterial, which force-instances a clone
                // per text and silently breaks batching across every gold nametag on screen).
                if (mat != null) tmp.fontSharedMaterial = mat;
                tmp.color = new UnityEngine.Color(1f, 1f, 1f, a);
                tmp.text = $"{style}{name}{close}";
            }
            else
            {
                tmp.enableVertexGradient = false;
                if (AssetManager.DefaultNameMaterial != null) tmp.fontSharedMaterial = AssetManager.DefaultNameMaterial;
                tmp.color = new UnityEngine.Color(1f, 1f, 1f, a);
                int ri = Mathf.Clamp(Mathf.RoundToInt(r * 255), 0, 255);
                int gi = Mathf.Clamp(Mathf.RoundToInt(g * 255), 0, 255);
                int bi = Mathf.Clamp(Mathf.RoundToInt(b * 255), 0, 255);
                tmp.text = $"<color=#{ri:X2}{gi:X2}{bi:X2}>{style}{name}{close}</color>";
            }

            EnsureNametagRenderQueue(tmp);

            // now that the final material (shadow/gold/default) is set with its outline, hand off to the
            // font replacement system: if an override targets this nametag's font it swaps to our atlas
            // and rebuilds THIS material onto it, so the custom font lands WITH the outline. no-op when
            // font replacement is off or nothing targets this font.
            BetterFG.Customization.UI.FontReplacementService.ApplyToNametag(tmp);
            EnsureNametagRenderQueue(tmp);
            ApplyOutlineIfEnabled(tmp, args);
        }

        public static List<UnityEngine.Color> ParseGradientColors(string s)
        {
            var list = new List<UnityEngine.Color>();
            if (string.IsNullOrEmpty(s)) return list;
            foreach (var part in s.Split(','))
            {
                if (BettrFG.uGUI.UGUIShip.HexToColor(part, out float rr, out float gg, out float bb))
                    list.Add(new UnityEngine.Color(rr, gg, bb, 1f));
            }
            return list;
        }

        public static string SerializeGradientColors(List<UnityEngine.Color> stops)
        {
            if (stops == null || stops.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < stops.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(BettrFG.uGUI.UGUIShip.ColorToHex(stops[i].r, stops[i].g, stops[i].b));
            }
            return sb.ToString();
        }

        private static UnityEngine.Color SampleGradient(List<UnityEngine.Color> stops, float t)
        {
            if (stops.Count == 1) return stops[0];
            float pos = Mathf.Clamp01(t) * (stops.Count - 1);
            int i = (int)pos;
            if (i >= stops.Count - 1) return stops[stops.Count - 1];
            return UnityEngine.Color.Lerp(stops[i], stops[i + 1], pos - i);
        }

        private class GradientBinding
        {
            public TMP_Text tmp;
            public List<UnityEngine.Color> stops;
            public float angle;
        }
        private static readonly Dictionary<IntPtr, GradientBinding> _gradientTmps = new Dictionary<IntPtr, GradientBinding>();

        private static bool _tmpEventHooked;

        private static void EnsureTMPEventHooked()
        {
            if (_tmpEventHooked) return;
            try
            {
                TMPro.TMPro_EventManager.TEXT_CHANGED_EVENT.Add(new Action<UnityEngine.Object>(OnTMPTextChanged));
                _tmpEventHooked = true;
            }
            catch (Exception ex) { Plugin.Log.LogWarning("NametagIcon: TMP event hook: " + ex.Message); }
        }

        private static void OnTMPTextChanged(UnityEngine.Object obj)
        {
            if (obj == null) return;
            var tmp = obj.TryCast<TMP_Text>();
            if (tmp == null || tmp.m_CachedPtr == IntPtr.Zero) return;
            if (!_gradientTmps.TryGetValue(tmp.Pointer, out var b)) return;
            WriteGradientVertexColors(tmp, b.stops, b.angle);
        }

        public static void RegisterGradient(TMP_Text tmp, List<UnityEngine.Color> stops, float angleDeg)
        {
            if (tmp == null || tmp.m_CachedPtr == IntPtr.Zero) return;
            EnsureTMPEventHooked();
            _gradientTmps[tmp.Pointer] = new GradientBinding { tmp = tmp, stops = new List<UnityEngine.Color>(stops), angle = angleDeg };
        }

        public static void UnregisterGradient(TMP_Text tmp)
        {
            if (tmp == null || tmp.m_CachedPtr == IntPtr.Zero) return;
            _gradientTmps.Remove(tmp.Pointer);
        }

        public static void TickGradients()
        {
            if (_gradientTmps.Count == 0) return;
            List<IntPtr> dead = null;
            foreach (var kvp in _gradientTmps)
            {
                var b = kvp.Value;
                if (b.tmp == null || b.tmp.m_CachedPtr == IntPtr.Zero) { (dead ??= new List<IntPtr>()).Add(kvp.Key); continue; }
                WriteGradientVertexColors(b.tmp, b.stops, b.angle);
            }
            if (dead != null) foreach (var p in dead) _gradientTmps.Remove(p);
        }

        private static void WriteGradientVertexColors(TMP_Text tmp, List<UnityEngine.Color> stops, float angleDeg)
        {
            try
            {
                var info = tmp.textInfo;
                if (info == null || info.characterCount == 0) return;
                WriteColorsCore(tmp, info, stops, angleDeg);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("NametagIcon: gradient write: " + ex.Message); }
        }

        private static void ApplyGradientVertexColors(TMP_Text tmp, List<UnityEngine.Color> stops, float angleDeg)
        {
            try
            {
                tmp.ForceMeshUpdate(false, false);
                var info = tmp.textInfo;
                if (info == null || info.characterCount == 0) return;
                WriteColorsCore(tmp, info, stops, angleDeg);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("NametagIcon: gradient apply: " + ex.Message); }
        }

        private static void WriteColorsCore(TMP_Text tmp, TMPro.TMP_TextInfo info, List<UnityEngine.Color> stops, float angleDeg)
        {
            try
            {
                int count = info.characterCount;
                if (count == 0) return;

                float rad = angleDeg * Mathf.Deg2Rad;
                float ux = Mathf.Cos(rad), uy = Mathf.Sin(rad);

                float minP = float.MaxValue, maxP = float.MinValue;
                for (int i = 0; i < count; i++)
                {
                    var ch = info.characterInfo[i];
                    if (!ch.isVisible) continue;
                    float p;
                    p = ch.bottomLeft.x * ux + ch.bottomLeft.y * uy; if (p < minP) minP = p; if (p > maxP) maxP = p;
                    p = ch.topLeft.x     * ux + ch.topLeft.y     * uy; if (p < minP) minP = p; if (p > maxP) maxP = p;
                    p = ch.topRight.x    * ux + ch.topRight.y    * uy; if (p < minP) minP = p; if (p > maxP) maxP = p;
                    p = ch.bottomRight.x * ux + ch.bottomRight.y * uy; if (p < minP) minP = p; if (p > maxP) maxP = p;
                }
                float span = maxP - minP;
                if (span < 1e-4f) span = 1f;

                for (int i = 0; i < count; i++)
                {
                    var ch = info.characterInfo[i];
                    if (!ch.isVisible) continue;
                    int matIdx = ch.materialReferenceIndex;
                    int vi = ch.vertexIndex;
                    var colors = info.meshInfo[matIdx].colors32;

                    var cBL = SampleGradient(stops, ((ch.bottomLeft.x * ux + ch.bottomLeft.y * uy) - minP) / span);
                    var cTL = SampleGradient(stops, ((ch.topLeft.x     * ux + ch.topLeft.y     * uy) - minP) / span);
                    var cTR = SampleGradient(stops, ((ch.topRight.x    * ux + ch.topRight.y    * uy) - minP) / span);
                    var cBR = SampleGradient(stops, ((ch.bottomRight.x * ux + ch.bottomRight.y * uy) - minP) / span);
                    cBL.a = tmp.color.a; cTL.a = tmp.color.a; cTR.a = tmp.color.a; cBR.a = tmp.color.a;
                    colors[vi + 0] = cBL;
                    colors[vi + 1] = cTL;
                    colors[vi + 2] = cTR;
                    colors[vi + 3] = cBR;
                }

                tmp.UpdateVertexData(TMPro.TMP_VertexDataUpdateFlags.Colors32);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("NametagIcon: gradient vertex: " + ex.Message); }
        }

        private static string BuildGradientText(string name, List<UnityEngine.Color> stops, bool bold, bool italic)
        {
            int n = name?.Length ?? 0;
            if (n == 0) return name ?? "";
            var sb = new System.Text.StringBuilder(n * 24);
            if (bold) sb.Append("<b>");
            if (italic) sb.Append("<i>");
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 0f : (float)i / (n - 1);
                var c = SampleGradient(stops, t);
                int ri = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
                int gi = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
                int bi = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
                sb.Append("<color=#").Append(ri.ToString("X2")).Append(gi.ToString("X2")).Append(bi.ToString("X2")).Append('>');
                sb.Append(name[i]);
                sb.Append("</color>");
            }
            if (italic) sb.Append("</i>");
            if (bold) sb.Append("</b>");
            return sb.ToString();
        }

        private static void ApplyOutlineIfEnabled(TMP_Text tmp, TextStyleArgs args)
        {
            if (!args.outlineEnabled) return;
            if (args.style != "default" && args.style != "gradient" && args.style != "") return;
            try
            {
                var mat = tmp.fontMaterial;
                if (mat == null) return;
                float w = Mathf.Clamp01(args.outlineWidth);
                var col = new UnityEngine.Color(args.outlineR, args.outlineG, args.outlineB, 1f);
                bool inset = args.outlineMode == "inset";
                if (inset)
                {
                    mat.SetColor("_OutlineColor", col);
                    mat.SetFloat("_OutlineWidth", w);
                    if (w > 0f) mat.EnableKeyword("OUTLINE_ON");
                    else mat.DisableKeyword("OUTLINE_ON");
                    mat.SetFloat("_UnderlayDilate", 0f);
                    mat.DisableKeyword("UNDERLAY_ON");
                }
                else
                {
                    mat.SetFloat("_OutlineWidth", 0f);
                    mat.DisableKeyword("OUTLINE_ON");
                    mat.SetColor("_UnderlayColor", col);
                    mat.SetFloat("_UnderlayOffsetX", 0f);
                    mat.SetFloat("_UnderlayOffsetY", 0f);
                    mat.SetFloat("_UnderlayDilate", w);
                    mat.SetFloat("_UnderlaySoftness", 0f);
                    if (w > 0f) mat.EnableKeyword("UNDERLAY_ON");
                    else mat.DisableKeyword("UNDERLAY_ON");
                }
                tmp.havePropertiesChanged = true;
                TMPro.TMPro_EventManager.ON_MATERIAL_PROPERTY_CHANGED(true, mat);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("NametagIcon: outline: " + ex.Message); }
        }

        // Returns a darker, more saturated version of an RGB color for use as an outline.
        // darken: fraction to reduce value by (0.70 = 70% darker).
        private static UnityEngine.Color DarkerSaturated(float r, float g, float b, float darken)
        {
            UnityEngine.Color.RGBToHSV(new UnityEngine.Color(r, g, b, 1f), out float h, out float s, out float v);
            s = Mathf.Clamp01(s + (1f - s) * 0.5f);
            v = Mathf.Clamp01(v * (1f - darken));
            var c = UnityEngine.Color.HSVToRGB(h, s, v);
            c.a = 1f;
            return c;
        }

        private static Sprite ResolveIconSprite(string iconMode, string country, string path)
        {
            if (iconMode == "flag" && !string.IsNullOrEmpty(country))
                return LoadEmbeddedFlag(country);
            if (iconMode == "custom" && !string.IsNullOrEmpty(path) && File.Exists(path))
                return LoadFileSprite(path);
            return null;
        }

        private static string StripRichText(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return System.Text.RegularExpressions.Regex.Replace(s, "<[^>]*>", "").Trim();
        }

        // public seam for callers outside this file that need to strip an inline UI icon off a
        // reverted/pooled row without going through a full ApplyRemoteToNameplate pass.
        public static void RemoveInlineUIIcon(Transform t) => RemoveUIIcon(t);

        private static void RemoveUIIcon(Transform t)
        {
            if (t == null) return;
            var icon = t.Find(UI_ICON_NAME) ?? t.parent?.Find(UI_ICON_NAME);
            if (icon != null) UnityEngine.Object.DestroyImmediate(icon.gameObject);
        }

        private static void AttachUIIcon(TMPro.TextMeshProUGUI tmp, Sprite sprite, NameplateType type, string iconMode = null, string iconPath = null, float iconScale = -1f, float iconOffX = float.NaN, float iconOffY = float.NaN)
        {
            var parent = tmp.transform.parent;
            RemoveUIIcon(tmp.transform);
            tmp.richText = true;

            var tmpRt = tmp.GetComponent<RectTransform>();

            var iconGo = new GameObject(UI_ICON_NAME);
            iconGo.transform.SetParent(parent, false);
            var iconRt = iconGo.AddComponent<RectTransform>();
            var layout = parent != null ? parent.GetComponent<HorizontalLayoutGroup>() : null;

            if (layout != null)
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                bool isCustom = (iconMode ?? SettingsService.Get("nametag.icon.mode", "none")) == "custom";
                float scale = iconScale >= 0f ? iconScale : (isCustom && float.TryParse(SettingsService.Get("nametag.icon.scale", "1"), System.Globalization.NumberStyles.Float, ci, out float sv) ? sv : 1f);
                float offX = !float.IsNaN(iconOffX) ? iconOffX : (isCustom && float.TryParse(SettingsService.Get("nametag.icon.offset.x", "0"), System.Globalization.NumberStyles.Float, ci, out float ox) ? ox : 0f);
                float offY = !float.IsNaN(iconOffY) ? iconOffY : (isCustom && float.TryParse(SettingsService.Get("nametag.icon.offset.y", "0"), System.Globalization.NumberStyles.Float, ci, out float oy) ? oy : 0f);

                float h = tmpRt != null && tmpRt.rect.height > 1f ? tmpRt.rect.height : tmp.fontSize;
                float size = Mathf.Clamp(h, 12f, 32f) * scale;
                iconRt.sizeDelta = new Vector2(size, size);
                var layoutIcon = iconGo.AddComponent<LayoutElement>();
                layoutIcon.ignoreLayout = false;
                layoutIcon.preferredWidth = size;
                layoutIcon.preferredHeight = size;
                layoutIcon.flexibleWidth = 0f;
                layoutIcon.flexibleHeight = 0f;
                iconGo.transform.SetSiblingIndex(tmp.transform.GetSiblingIndex() + 1);

                var layoutImg = iconGo.AddComponent<Image>();
                layoutImg.sprite = sprite;
                layoutImg.preserveAspect = true;
                layoutImg.raycastTarget = false;
                layoutImg.color = new Color(1f, 1f, 1f, tmp.color.a);
                _iconUI[tmp.GetInstanceID()] = layoutImg;
            _iconPtrs.Add(tmp.m_CachedPtr);
            SyncIconGate();
                MaybeAttachGifAnimator(iconGo, null, layoutImg, iconMode, iconPath);
                // canvas tags hide a no-rank crown by disabling its Container while the badge root stays an
                // active layout child, so the rebuild would still reserve its width. mirror the hide into
                // ignoreLayout (both ways, so a crown gained later takes its space back)
                var crownVm = parent.GetComponentInChildren<CrownRankBadgeViewModel>(true);
                var crownCont = crownVm != null ? crownVm.transform.Find("Container") : null;
                var crownLe = crownVm != null ? crownVm.GetComponent<LayoutElement>() : null;
                if (crownCont != null && crownLe != null) crownLe.ignoreLayout = !crownCont.gameObject.activeSelf;
                LayoutRebuilder.ForceRebuildLayoutImmediate(parent.GetComponent<RectTransform>());
                var layoutHost = BeanMonitorService.Instance;
                if (layoutHost != null)
                    layoutHost.StartCoroutine(RepositionUILayoutNextFrame(iconRt, parent.GetComponent<RectTransform>(), offX, offY).WrapToIl2Cpp());
                return;
            }

            iconRt.anchorMin = tmpRt != null ? tmpRt.anchorMin : new Vector2(0.5f, 0.5f);
            iconRt.anchorMax = tmpRt != null ? tmpRt.anchorMax : new Vector2(0.5f, 0.5f);
            iconRt.pivot = new Vector2(0f, 0.5f);
            iconRt.sizeDelta = new Vector2(UI_ICON_SIZE_REGULAR, UI_ICON_SIZE_REGULAR);
            var le = iconGo.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            var img = iconGo.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, tmp.color.a);
            _iconUI[tmp.GetInstanceID()] = img;
            _iconPtrs.Add(tmp.m_CachedPtr);
            SyncIconGate();
            MaybeAttachGifAnimator(iconGo, null, img, iconMode, iconPath);

            var host = BeanMonitorService.Instance;
            if (host != null)
                host.StartCoroutine(RepositionUINextFrame(tmp, iconRt, type, iconMode, iconScale, iconOffX, iconOffY).WrapToIl2Cpp());
        }

        private static IEnumerator RepositionUINextFrame(TMPro.TextMeshProUGUI tmp, RectTransform iconRt, NameplateType type, string iconMode = null, float iconScale = -1f, float iconOffX = float.NaN, float iconOffY = float.NaN)
        {
            yield return null;
            try { _ = iconRt.name; } catch { yield break; }

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            bool isCustom = (iconMode ?? SettingsService.Get("nametag.icon.mode", "none")) == "custom";
            float scale = iconScale >= 0f ? iconScale : (isCustom && float.TryParse(SettingsService.Get("nametag.icon.scale", "1"), System.Globalization.NumberStyles.Float, ci, out float sv) ? sv : 1f);
            float offX = !float.IsNaN(iconOffX) ? iconOffX : (isCustom && float.TryParse(SettingsService.Get("nametag.icon.offset.x", "0"), System.Globalization.NumberStyles.Float, ci, out float ox) ? ox : 0f);
            float offY = !float.IsNaN(iconOffY) ? iconOffY : (isCustom && float.TryParse(SettingsService.Get("nametag.icon.offset.y", "0"), System.Globalization.NumberStyles.Float, ci, out float oy) ? oy : 0f);

            float baseSize = type == NameplateType.Party ? UI_ICON_SIZE_PARTY : UI_ICON_SIZE_REGULAR;
            float size = baseSize * (isCustom ? scale : 1f);
            iconRt.sizeDelta = new Vector2(size, size);

            var tmpRt = tmp.GetComponent<RectTransform>();
            if (tmpRt == null) yield break;

            float textW = 0f;
            try { textW = tmp.GetPreferredValues(tmp.text).x; } catch { }

            float iconX, iconY;
            if (type == NameplateType.Regular)
            {
                iconX = tmpRt.anchoredPosition.x + textW * 0.1158f + 112.86f + (offX * 20);
                iconY = offY * 20;
            }
            else
            {
                iconX = tmpRt.anchoredPosition.x + (textW - 2f) * 0.5f + UI_ICON_GAP + (offX * 20);
                iconY = tmpRt.anchoredPosition.y + offY * 20;
            }

            iconRt.anchoredPosition = new Vector2(iconX, iconY);
        }

        private static IEnumerator RepositionUILayoutNextFrame(RectTransform iconRt, RectTransform parentRt, float offX, float offY)
        {
            yield return null;
            try { _ = iconRt.name; } catch { yield break; }

            if (parentRt != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(parentRt);

            iconRt.anchoredPosition += new Vector2(offX * 20f, offY * 20f);
        }

        // ── 3D icon internals ─────────────────────────────────────────────────

        private static void RescueAndDestroyWrapper(Transform nameAndCrown)
        {
            var wrapper = nameAndCrown.Find("BetterFG_NameWrapper");
            if (wrapper == null) return;
            var nameTextTransform = wrapper.Find("NameText");
            var tmp = nameTextTransform != null ? nameTextTransform.GetComponent<TMPro.TextMeshPro>() : null;
            if (tmp != null)
            {
                tmp.transform.SetParent(nameAndCrown, true);
                tmp.transform.localPosition = Vector3.zero;
            }
            UnityEngine.Object.Destroy(wrapper.gameObject);
        }

        private static float GetCrownWorldWidth(Transform nameAndCrown)
        {
            var crown = nameAndCrown.GetComponentInChildren<CrownRankBadgeViewModel>(true)?.transform;
            if (crown == null) return 0f;
            // a badge that's actually part of the tag sits at local y 0. with no crown rank the game parks
            // it off the tag (y != 0) while it's still active, so don't reserve layout space for it
            if (Mathf.Abs(crown.localPosition.y) > 0.0001f) return 0f;
            var container = crown.Find("Container");
            if (container == null || !container.gameObject.activeInHierarchy) return 0f;
            var rt = crown.GetComponent<RectTransform>();
            if (rt == null) return 0f;
            return rt.rect.width * Mathf.Abs(crown.lossyScale.x);
        }

        private static void AttachIcon(Transform nameAndCrown, Sprite sprite)
        {
            // destroy EVERY existing icon immediately, not just the first with deferred Destroy — a same-frame
            // re-attach (crown apply + deferred re-pin + name change can all fire in one frame) would otherwise
            // find the still-alive pending-destroy icon, no-op, and stack a second one. that's the duplicate.
            for (var existingIcon = nameAndCrown.Find(FLAG_OBJECT_NAME); existingIcon != null; existingIcon = nameAndCrown.Find(FLAG_OBJECT_NAME))
                UnityEngine.Object.DestroyImmediate(existingIcon.gameObject);
            RescueAndDestroyWrapper(nameAndCrown);

            var nameTextTransform = nameAndCrown.Find("NameText") ?? nameAndCrown.Find("BetterFG_NameWrapper/NameText");
            var tmp = nameTextTransform != null ? nameTextTransform.GetComponent<TMPro.TextMeshPro>() : null;

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float scaleMul = float.TryParse(SettingsService.Get("nametag.icon.scale", "1"), System.Globalization.NumberStyles.Float, ci, out float sm) ? sm : 1f;
            float offX = float.TryParse(SettingsService.Get("nametag.icon.offset.x", "0"), System.Globalization.NumberStyles.Float, ci, out float ox) ? ox : 0f;
            float offY = float.TryParse(SettingsService.Get("nametag.icon.offset.y", "0"), System.Globalization.NumberStyles.Float, ci, out float oy) ? oy : 0f;

            bool isCustom = SettingsService.Get("nametag.icon.mode", "none") == "custom";
            float finalScale = BASE_SCALE * (isCustom ? scaleMul : 1f);
            float finalOffY = isCustom ? offY : 0f;
            float iconWorldW = sprite != null ? (sprite.texture.width / sprite.pixelsPerUnit) * finalScale : 0f;
            float crownWorldW = GetCrownWorldWidth(nameAndCrown);

            var wrapperGo = new GameObject("BetterFG_NameWrapper");
            wrapperGo.transform.SetParent(nameAndCrown, false);
            wrapperGo.transform.localPosition = Vector3.zero;
            wrapperGo.transform.localRotation = Quaternion.identity;
            wrapperGo.transform.localScale = Vector3.one;
            if (tmp != null) tmp.transform.SetParent(wrapperGo.transform, true);

            var iconGo = new GameObject(FLAG_OBJECT_NAME);
            iconGo.transform.SetParent(nameAndCrown, false);
            iconGo.transform.localScale = new Vector3(finalScale, finalScale, finalScale);
            iconGo.transform.localRotation = Quaternion.identity;
            var sr = iconGo.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 1;
            // inherit the name's current alpha so a faded nametag's icon comes up faded too, not fully opaque
            sr.color = new Color(1f, 1f, 1f, tmp != null ? tmp.color.a : 1f);
            if (tmp != null) { _icon3d[tmp.GetInstanceID()] = sr; _iconPtrs.Add(tmp.m_CachedPtr); SyncIconGate(); }

            MaybeAttachGifAnimator(iconGo, sr, null);
            RememberPlacement(nameAndCrown, tmp, wrapperGo.transform, iconGo.transform, iconWorldW, isCustom, offX, finalOffY);

            var host = BeanMonitorService.Instance;
            if (host != null)
                host.StartCoroutine(
                    RepositionNextFrame(tmp, wrapperGo.transform, iconGo.transform, iconWorldW, crownWorldW, isCustom, offX, finalOffY, nameAndCrown)
                    .WrapToIl2Cpp());
        }

        private struct IconPlacement
        {
            public TMPro.TextMeshPro tmp;
            public Transform wrapper, icon;
            public float iconW, offX, offY;
            public bool custom;
        }

        private static readonly Dictionary<int, IconPlacement> _placement = new Dictionary<int, IconPlacement>();

        private static void RememberPlacement(Transform nameAndCrown, TMPro.TextMeshPro tmp, Transform wrapper,
            Transform iconT, float iconWorldW, bool isCustom, float offX, float finalOffY)
        {
            if (nameAndCrown == null) return;
            _placement[nameAndCrown.GetInstanceID()] = new IconPlacement
            {
                tmp = tmp, wrapper = wrapper, icon = iconT,
                iconW = iconWorldW, custom = isCustom, offX = offX, offY = finalOffY,
            };
        }

        // re-runs the whole name+icon+crown placement with today's widths, so a crown that came or went
        // after the icon was attached doesn't leave the group off-centre
        public static bool Relayout(Transform nameAndCrown)
        {
            if (nameAndCrown == null || !_placement.TryGetValue(nameAndCrown.GetInstanceID(), out var p)) return false;
            if (p.wrapper == null || p.icon == null)
            {
                _placement.Remove(nameAndCrown.GetInstanceID());
                return false;
            }
            PlaceIconGroup(p.tmp, p.wrapper, p.icon, p.iconW, 0f, p.custom, p.offX, p.offY, nameAndCrown);
            return true;
        }

        private static IEnumerator RepositionNextFrame(
            TMPro.TextMeshPro tmp, Transform wrapper, Transform iconT,
            float iconWorldW, float crownWorldW, bool isCustom, float offX, float finalOffY,
            Transform nameAndCrown)
        {
            yield return null;
            try { _ = wrapper.name; _ = iconT.name; } catch { yield break; }
            PlaceIconGroup(tmp, wrapper, iconT, iconWorldW, crownWorldW, isCustom, offX, finalOffY, nameAndCrown);
        }

        private static void PlaceIconGroup(
            TMPro.TextMeshPro tmp, Transform wrapper, Transform iconT,
            float iconWorldW, float crownWorldW, bool isCustom, float offX, float finalOffY,
            Transform nameAndCrown)
        {
            float resolvedCrownW = crownWorldW > 0f ? crownWorldW : GetCrownWorldWidth(nameAndCrown);
            float textWorldW = 0f;
            try { textWorldW = tmp != null ? tmp.preferredWidth : 0f; } catch { }

            try
            {
                float gap = 0.02f;
                float crownGap = resolvedCrownW > 0f ? gap : 0f;
                float totalW = textWorldW + gap + iconWorldW + crownGap + resolvedCrownW;
                float leftEdge = -totalW * 0.5f;

                // two layouts, both centred. the name+icon keep their order (name then icon) either way — only
                // the crown changes sides. NOT swapped: [name][icon][crown]. swapped: [crown][name][icon] — the
                // crown moves to the far left, name+icon just shift right to make room. the icon must NOT flip
                // to the left of the name.
                bool swap = BetterFG.Nametag.CrownRankService.SwapSide;
                float nameX, iconX, crownX;
                if (swap)
                {
                    crownX = leftEdge + resolvedCrownW * 0.5f;
                    nameX = leftEdge + resolvedCrownW + crownGap + textWorldW * 0.5f;
                    iconX = leftEdge + resolvedCrownW + crownGap + textWorldW + gap + iconWorldW * 0.5f;
                }
                else
                {
                    nameX = leftEdge + textWorldW * 0.5f;
                    iconX = leftEdge + textWorldW + gap + iconWorldW * 0.5f;
                    crownX = leftEdge + textWorldW + gap + iconWorldW + crownGap + resolvedCrownW * 0.5f;
                }
                iconX += isCustom ? offX : FLAG_NUDGE_X;

                if (tmp != null) tmp.transform.localPosition = Vector3.zero;
                wrapper.localPosition = new Vector3(nameX, 0f, wrapper.localPosition.z);

                if (tmp != null)
                {
                    tmp.enabled = true;
                    var csf = tmp.gameObject.GetComponent<ContentSizeFitter>();
                    if (csf != null) csf.enabled = true;
                }

                iconT.localPosition = new Vector3(iconX, finalOffY, iconT.localPosition.z);

                if (resolvedCrownW > 0f)
                {
                    var crown = nameAndCrown.GetComponentInChildren<CrownRankBadgeViewModel>(true)?.transform;
                    if (crown != null)
                    {
                        var le = crown.GetComponent<LayoutElement>();
                        if (le != null) le.ignoreLayout = true;
                        var ccsf = crown.GetComponent<ContentSizeFitter>();
                        if (ccsf != null) ccsf.enabled = false;
                        crown.localPosition = new Vector3(crownX, crown.localPosition.y, crown.localPosition.z);
                    }
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"NametagIcon: reposition aborted: {ex.Message}"); }
        }

        // re-place the local crown counter using the same centred formula the icon reposition uses (name +
        // icon + crown laid left-to-right, group centred on the parent origin). this is the accurate one —
        // it accounts for the icon width, so crown-only or crown+icon both land right. pins the crown out of
        // the layout so the game's respawn re-layout can't drag it back to the middle. safe to call any time.
        public static void RepositionLocalCrown() => RepositionCrownUnder(NametagFinder.FindNameAndCrownParent());

        public static bool HasIcon(Transform nameAndCrown) =>
            nameAndCrown != null && nameAndCrown.Find(FLAG_OBJECT_NAME) != null;

        public static void RepositionCrownUnder(Transform nameAndCrown)
        {
            if (nameAndCrown == null) return;
            var crown = nameAndCrown.GetComponentInChildren<CrownRankBadgeViewModel>(true)?.transform;
            if (crown == null) return;

            var le = crown.GetComponent<LayoutElement>();
            if (le != null) le.ignoreLayout = true;
            var csf = crown.GetComponent<ContentSizeFitter>();
            if (csf != null) csf.enabled = false;

            var nameT = nameAndCrown.Find("NameText") ?? nameAndCrown.Find("BetterFG_NameWrapper/NameText");
            var tmp = nameT != null ? nameT.GetComponent<TMPro.TextMeshPro>() : null;
            float textW = 0f;
            try { textW = tmp != null ? tmp.preferredWidth : 0f; } catch { }

            float crownW = GetCrownWorldWidth(nameAndCrown);

            float iconW = 0f;
            var iconT = nameAndCrown.Find(FLAG_OBJECT_NAME);
            var iconSr = iconT != null ? iconT.GetComponent<SpriteRenderer>() : null;
            if (iconSr != null && iconSr.sprite != null)
                iconW = iconSr.sprite.texture.width / iconSr.sprite.pixelsPerUnit * Mathf.Abs(iconT.localScale.x);

            const float gap = 0.02f;
            float crownGap = crownW > 0f ? gap : 0f;
            float iconGap = iconW > 0f ? gap : 0f;
            float totalW = textW + iconGap + iconW + crownGap + crownW;
            float leftEdge = -totalW * 0.5f;
            // match RepositionNextFrame's layout: swapped → crown on the far LEFT; otherwise far right. only the
            // crown moves here (name+icon keep their order and are placed by the full reposition).
            float crownX = BetterFG.Nametag.CrownRankService.SwapSide
                ? leftEdge + crownW * 0.5f
                : leftEdge + textW + iconGap + iconW + crownGap + crownW * 0.5f;
            crown.localPosition = new Vector3(crownX, crown.localPosition.y, crown.localPosition.z);
        }

        // ── Sprite loading ────────────────────────────────────────────────────

        internal static Sprite LoadEmbeddedFlag(string isoCode)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using Stream stream = asm.GetManifestResourceStream($"BetterFG.assets.flags.{isoCode.ToUpper()}.png");
                if (stream == null) return null;
                byte[] data = new byte[stream.Length];
                stream.Read(data, 0, data.Length);
                return BytesToSprite(data, resize: false);
            }
            catch (Exception ex) { Plugin.Log.LogError($"NametagIcon: LoadEmbeddedFlag {isoCode}: {ex.Message}"); return null; }
        }

        internal static Sprite LoadFileSprite(string path)
        {
            try
            {
                if (GifDecoder.IsGifPath(path))
                {
                    var frames = LoadGifSprites(path);
                    if (frames != null && frames.Length > 0) return frames[0];
                    // decode failed — fall through to Unity's loader so the icon still shows
                }
                return BytesToSprite(File.ReadAllBytes(path), resize: true);
            }
            catch (Exception ex) { Plugin.Log.LogError($"NametagIcon: LoadFileSprite {path}: {ex.Message}"); return null; }
        }

        // Per-path cache of decoded GIF frame sprites + delays (decoding is expensive).
        private static readonly Dictionary<string, (Sprite[] frames, float[] delays)> _gifCache =
            new Dictionary<string, (Sprite[], float[])>(StringComparer.OrdinalIgnoreCase);

        internal static Sprite[] LoadGifSprites(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            if (_gifCache.TryGetValue(path, out var cached) && cached.frames != null
                && cached.frames.Length > 0 && cached.frames[cached.frames.Length - 1] != null)
                return cached.frames;

            var result = GifDecoder.Decode(File.ReadAllBytes(path));
            if (result == null || result.Frames == null || result.Frames.Length == 0) return null;

            var sprites = new Sprite[result.Frames.Length];
            for (int i = 0; i < result.Frames.Length; i++)
            {
                var tex = ResizeIcon(result.Frames[i]);
                if (tex != result.Frames[i]) UnityEngine.Object.Destroy(result.Frames[i]);
                // Survive scene loads (menu → in-game): without this the frame textures
                // and sprites get destroyed on scene transition and the animation freezes.
                tex.hideFlags = HideFlags.HideAndDontSave;
                var spr = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                spr.hideFlags = HideFlags.HideAndDontSave;
                sprites[i] = spr;
            }

            _gifCache[path] = (sprites, result.Delays);
            return sprites;
        }

        private static float[] GetGifDelays(string path)
            => _gifCache.TryGetValue(path, out var c) ? c.delays : null;

        private static void MaybeAttachGifAnimator(GameObject iconGo, SpriteRenderer sr, Image img, string iconMode = null, string iconPath = null)
        {
            try
            {
                if (iconGo == null) return;
                string mode = iconMode ?? SettingsService.Get(KEY_ICON_MODE, "none");
                if (mode != "custom") return;

                string path = iconPath ?? SettingsService.Get(KEY_ICON_PATH, "");
                if (!GifDecoder.IsGifPath(path)) return;

                var frames = LoadGifSprites(path);
                if (frames == null || frames.Length < 2) return;

                var anim = iconGo.AddComponent<GifAnimator>();
                anim.SourcePath = path;
                anim.Init(frames, GetGifDelays(path), sr, img);
            }
            catch (Exception ex) { Plugin.Log.LogError($"NametagIcon: gif animator: {ex.Message}"); }
        }

        private static Sprite BytesToSprite(byte[] data, bool resize)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(data);
            tex.Apply();
            if (resize) tex = ResizeIcon(tex);
            var spr = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            // these sprites get applied to player rows that live across scene loads (menu → in-game).
            // without DontSave the texture+sprite get destroyed on scene transition and the row goes blank.
            tex.hideFlags = HideFlags.HideAndDontSave;
            spr.hideFlags = HideFlags.HideAndDontSave;
            return spr;
        }

        private static Texture2D ResizeIcon(Texture2D src)
        {
            const int MAX = 256;
            // Never upscale: small source icons keep their native resolution.
            if (src.width <= MAX && src.height <= MAX) return src;
            int dstW, dstH;
            if (src.width >= src.height) { dstW = MAX; dstH = Mathf.Max(1, Mathf.RoundToInt(MAX * (float)src.height / src.width)); }
            else { dstH = MAX; dstW = Mathf.Max(1, Mathf.RoundToInt(MAX * (float)src.width / src.height)); }
            var rt = RenderTexture.GetTemporary(dstW, dstH, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;
            RenderTexture.active = rt;
            Graphics.Blit(src, rt);
            var dst = new Texture2D(dstW, dstH, TextureFormat.RGBA32, false);
            dst.ReadPixels(new Rect(0, 0, dstW, dstH), 0, 0);
            dst.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }
    }
}
