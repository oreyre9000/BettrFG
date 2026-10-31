using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BetterFG.Customization.Pets;
using BetterFG.Nametag;
using BetterFG.Network;
using BetterFG.Services;
using BetterFG.Utilities;
using FallGuysLib.Players;
using UnityEngine;

namespace BetterFG.Customization.Player
{
    public class TexEmbed
    {
        public string fileName;
        public string b64;
    }

    public class PlinthEmbed
    {
        public string file, repoUrl, bundleB64, source, folder;
    }

    public class SocialEmbed
    {
        public bool phrase;
        public bool emote;
        public int slot;
        public string text;
        public string imageB64, imageExt;
        public string bundleB64, clipName;
        public string[] soundB64 = new string[3];
        public string[] soundExt = new string[3];
    }

    public class PbEntry
    {
        public string round;
        public string rawId;
        public string type;
        public float time;
        public string date;
        public string ghostB64;
    }

    public class BfgProfile
    {
        public const string CosmeticIds = "allcosmetics.ids";
        public const string CosmeticColour = "allcosmetics.colour";
        public const string CosmeticPattern = "allcosmetics.pattern";
        public const string CosmeticFaceplate = "allcosmetics.faceplate";

        public static readonly string[] Prefixes =
            { "skin.", "skintex.", "allcosmetics.", "nametag.", "crownrank.", "iteml", "itemr", "feature.customizefallguys" };

        public string name;
        public string username;
        public string displayName;
        public string platformName;
        public string exportedAt;
        public float scale = 1f;
        public int teamId = -1;

        public uint playerID;
        public string episodeGUID;
        public bool requireKeyMatch;
        public string resolvedPlayerKey;

        public Dictionary<string, string> settings = new Dictionary<string, string>(StringComparer.Ordinal);
        public List<RemoteSkinEntry> skins = new List<RemoteSkinEntry>();
        public List<PetData> pets = new List<PetData>();
        public List<TexEmbed> textures = new List<TexEmbed>();
        public string iconB64;
        public string backingB64;
        public string previewB64;
        public PlinthEmbed plinth;
        public List<SocialEmbed> socials = new List<SocialEmbed>();
        public List<PbEntry> pbs = new List<PbEntry>();

        public RemoteNametagInfo nametag;
        public string texDir;

        public string Get(string key, string def = "") =>
            settings.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : def;

        public float GetFloat(string key, float def) =>
            settings.TryGetValue(key, out var v) &&
            float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : def;

        public bool GetBool(string key, bool def = false) =>
            settings.TryGetValue(key, out var v) ? v == "true" || v == "1" : def;

        public string CleanName => PlayerUtils.CleanPlayerName(username ?? "");

        public IEnumerable<string> AliasKeys()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in new[] { username, displayName, platformName, nametag?.customName })
            {
                string c = PlayerUtils.CleanPlayerName(n ?? "");
                if (!string.IsNullOrEmpty(c) && seen.Add(c)) yield return c;
            }
        }

        public bool KeyMatches(string key)
        {
            string c = PlayerUtils.CleanPlayerName(key ?? "");
            if (string.IsNullOrEmpty(c)) return false;
            foreach (var k in AliasKeys())
                if (k.Equals(c, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public List<SkinTexEntry> TexEntries()
        {
            var entries = SkinApplicationService.ReadTexEntries(Get);
            if (string.IsNullOrEmpty(texDir)) return entries;
            foreach (var e in entries)
                foreach (var ov in e.overrides)
                {
                    if (string.IsNullOrEmpty(ov.texPath)) continue;
                    string local = Path.Combine(texDir, Path.GetFileName(ov.texPath));
                    ov.texPath = File.Exists(local) ? local : UntrustedInput.LocalPath(ov.texPath);
                }
            return entries;
        }

        public CrownRankService.CrownCfg Crown() => CrownRankService.CfgFrom(Get);

        public RemoteNametagInfo BuildNametag()
        {
            bool wantsBacking = Get("nametag.backing.enabled") == "true";
            if (Get("nametag.enabled") != "true" && string.IsNullOrEmpty(Get("nametag.customname"))
                && !wantsBacking && Get("nametag.nickname.enabled") != "true")
                return null;

            return new RemoteNametagInfo
            {
                r = GetFloat("nametag.color.r", 1f),
                g = GetFloat("nametag.color.g", 1f),
                b = GetFloat("nametag.color.b", 1f),
                bold = Get("nametag.bold") == "true",
                italic = Get("nametag.italic") == "true",
                customName = Get("nametag.customname"),
                iconMode = Get("nametag.icon.mode"),
                iconCountry = Get("nametag.icon.country"),
                iconPath = Get("nametag.icon.path"),
                iconScale = GetFloat("nametag.icon.scale", 1f),
                iconOffX = GetFloat("nametag.icon.offset.x", 0f),
                iconOffY = GetFloat("nametag.icon.offset.y", 0f),
                platformHide = Get("nametag.platform.hide"),
                platformCustom = Get("nametag.platform.custom"),
                nameStyle = Get("nametag.namestyle"),
                gradientColors = Get("nametag.gradient.colors"),
                gradientAngle = GetFloat("nametag.gradient.angle", 0f),
                outlineEnabled = Get("nametag.outline.enabled") == "true",
                outlineR = GetFloat("nametag.outline.r", 0f),
                outlineG = GetFloat("nametag.outline.g", 0f),
                outlineB = GetFloat("nametag.outline.b", 0f),
                outlineWidth = GetFloat("nametag.outline.width", 0.2f),
                outlineMode = Get("nametag.outline.mode"),
                backingEnabled = wantsBacking,
                backingPath = Get("nametag.backing.path"),
                backingOffX = GetFloat("nametag.backing.offset.x", 0f),
                backingOffY = GetFloat("nametag.backing.offset.y", 0f),
                backingScale = GetFloat("nametag.backing.scale", 1f),
                nickname = Get("nametag.nickname.enabled") == "true" ? Get("nametag.nickname.text") : "",
                hudType = Get("nametag.hudtype"),
            };
        }

        public static BfgProfile FromLocal()
        {
            var p = new BfgProfile
            {
                settings = SettingsService.Snapshot(Prefixes),
                scale = PlayerScaleService.GetPlayerScale(),
                requireKeyMatch = true,
            };

            foreach (var key in NametagDefaults)
                if (!p.settings.ContainsKey(key.Key)) p.settings[key.Key] = SettingsService.Get(key.Key, key.Value);

            var graphics = FGClient.GlobalGameStateClient.Instance?.PlayerProfile?.GraphicsSettings;
            if (graphics != null)
                p.settings[NametagIconApplicator.KEY_HUD_TYPE] = graphics.UsingMotionBlur() || graphics.UsingDepthOfField() ? "canvas" : "gameobject";

            p.skins = ReadLoadout(p.Get);
            p.nametag = p.BuildNametag();
            if (PetService.Instance != null)
                foreach (var pet in PetService.Instance.EquippedPets())
                    p.pets.Add(pet.CloneForExport());
            return p;
        }

        public void StampLocalIdentity()
        {
            try { username = PlayerUtils.CleanPlayerName(FGClient.GlobalGameStateClient.Instance?.GetLocalPlayerKey() ?? ""); }
            catch { username = ""; }
            try { displayName = PlayerUtils.CleanPlayerName(FGClient.GlobalGameStateClient.Instance?.GetLocalPlayerName() ?? ""); }
            catch { displayName = ""; }
            try { platformName = PlayerUtils.CleanPlayerName(FGClient.GlobalGameStateClient.Instance?.PlayerProfile?.PlatformAccountName ?? ""); }
            catch { platformName = ""; }
        }

        static readonly KeyValuePair<string, string>[] NametagDefaults =
        {
            new KeyValuePair<string, string>("nametag.enabled", "false"),
            new KeyValuePair<string, string>("nametag.bold", "false"),
            new KeyValuePair<string, string>("nametag.italic", "false"),
            new KeyValuePair<string, string>("nametag.customname", ""),
            new KeyValuePair<string, string>("nametag.namestyle", "default"),
            new KeyValuePair<string, string>("nametag.gradient.colors", ""),
            new KeyValuePair<string, string>("nametag.gradient.angle", "0"),
            new KeyValuePair<string, string>("nametag.outline.enabled", "false"),
            new KeyValuePair<string, string>("nametag.outline.r", "0"),
            new KeyValuePair<string, string>("nametag.outline.g", "0"),
            new KeyValuePair<string, string>("nametag.outline.b", "0"),
            new KeyValuePair<string, string>("nametag.outline.width", "0.2"),
            new KeyValuePair<string, string>("nametag.outline.mode", "outset"),
            new KeyValuePair<string, string>("nametag.color.r", "1"),
            new KeyValuePair<string, string>("nametag.color.g", "1"),
            new KeyValuePair<string, string>("nametag.color.b", "1"),
            new KeyValuePair<string, string>("nametag.icon.mode", "none"),
            new KeyValuePair<string, string>("nametag.icon.country", ""),
            new KeyValuePair<string, string>("nametag.icon.path", ""),
            new KeyValuePair<string, string>("nametag.icon.scale", "1"),
            new KeyValuePair<string, string>("nametag.icon.offset.x", "0"),
            new KeyValuePair<string, string>("nametag.icon.offset.y", "0"),
            new KeyValuePair<string, string>("nametag.platform.hide", "false"),
            new KeyValuePair<string, string>("nametag.platform.custom", ""),
            new KeyValuePair<string, string>("nametag.backing.enabled", "false"),
            new KeyValuePair<string, string>("nametag.backing.path", ""),
            new KeyValuePair<string, string>("nametag.backing.offset.x", "0"),
            new KeyValuePair<string, string>("nametag.backing.offset.y", "0"),
            new KeyValuePair<string, string>("nametag.backing.scale", "1"),
            new KeyValuePair<string, string>("nametag.nickname.enabled", "false"),
            new KeyValuePair<string, string>("nametag.nickname.text", ""),
            new KeyValuePair<string, string>("menu.plinth.col.on", "false"),
            new KeyValuePair<string, string>("menu.plinth.col.r", "1"),
            new KeyValuePair<string, string>("menu.plinth.col.g", "1"),
            new KeyValuePair<string, string>("menu.plinth.col.b", "1"),
        };

        public static List<RemoteSkinEntry> ReadLoadout(Dictionary<string, string> settings)
            => ReadLoadout((k, d) => settings.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v) ? v : d);

        public static List<RemoteSkinEntry> ReadLoadout(System.Func<string, string, string> get)
        {
            string G(string k) => get(k, "");

            string files = G("skin.multi.files");
            string sources = G("skin.multi.sources");
            string paths = G("skin.multi.paths");
            string repos = G("skin.multi.repos");
            string types = G("skin.multi.types");
            string folders = G("skin.multi.folders");

            if (string.IsNullOrEmpty(files))
            {
                files = G("skin.file");
                sources = G("skin.source");
                paths = G("skin.localPath");
                repos = types = folders = "";
            }

            var list = new List<RemoteSkinEntry>();
            if (string.IsNullOrEmpty(files)) return list;

            var hands = ReadHandOverrides(G("skin.hand.overrides"));
            string[] f = files.Split(',');
            string[] src = sources.Split(',');
            string[] pth = paths.Split(',');
            string[] rp = repos.Split(',');
            string[] ty = types.Split(',');
            string[] fo = folders.Split(',');

            for (int i = 0; i < f.Length; i++)
            {
                string file = f[i].Trim();
                if (string.IsNullOrEmpty(file)) continue;
                list.Add(new RemoteSkinEntry
                {
                    file = file,
                    source = i < src.Length ? src[i].Trim() : "remote",
                    localPath = i < pth.Length ? pth[i].Trim() : "",
                    repoUrl = i < rp.Length ? rp[i].Trim() : "",
                    type = i < ty.Length ? ty[i].Trim() : "",
                    folder = i < fo.Length ? fo[i].Trim() : "",
                    hand = hands.TryGetValue(file, out int ov) ? ov : 0,
                });
            }
            return list;
        }

        public static Dictionary<string, int> ReadHandOverrides(string raw)
        {
            var map = new Dictionary<string, int>();
            if (string.IsNullOrEmpty(raw)) return map;
            foreach (string part in raw.Split(','))
            {
                int colon = part.LastIndexOf(':');
                if (colon < 1) continue;
                if (int.TryParse(part.Substring(colon + 1), out int ov))
                    map[part.Substring(0, colon)] = ov;
            }
            return map;
        }

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            Field(sb, "username", username);
            Field(sb, "displayName", displayName);
            Field(sb, "platformName", platformName);
            Field(sb, "exportedAt", exportedAt);
            sb.Append("  \"scale\": ").Append(scale.ToString("R", CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"playerID\": ").Append(playerID).Append(",\n");
            Field(sb, "episodeGUID", episodeGUID);

            sb.Append("  \"settings\": {");
            bool first = true;
            foreach (var kv in settings)
            {
                sb.Append(first ? "\n" : ",\n");
                sb.Append("    \"").Append(JsonUtil.Escape(kv.Key)).Append("\": \"").Append(JsonUtil.Escape(kv.Value)).Append('"');
                first = false;
            }
            sb.Append(first ? "},\n" : "\n  },\n");

            sb.Append("  \"skins\": [");
            for (int i = 0; i < skins.Count; i++)
            {
                sb.Append(i == 0 ? "\n    " : ",\n    ");
                AppendSkin(sb, skins[i]);
            }
            sb.Append(skins.Count == 0 ? "],\n" : "\n  ],\n");

            sb.Append("  \"pets\": [");
            for (int i = 0; i < pets.Count; i++)
            {
                var pd = pets[i];
                sb.Append(i == 0 ? "\n" : ",\n");
                sb.Append("    { \"name\": \"").Append(JsonUtil.Escape(pd.name))
                  .Append("\", \"top\": \"").Append(JsonUtil.Escape(pd.costumeTop))
                  .Append("\", \"bottom\": \"").Append(JsonUtil.Escape(pd.costumeBottom))
                  .Append("\", \"pattern\": \"").Append(JsonUtil.Escape(pd.pattern))
                  .Append("\", \"faceplate\": \"").Append(JsonUtil.Escape(pd.faceplate))
                  .Append("\", \"colour\": \"").Append(JsonUtil.Escape(pd.colour))
                  .Append("\", \"scale\": ").Append(pd.scale.ToString("R", CultureInfo.InvariantCulture));
                if (pd.costume != null && !string.IsNullOrEmpty(pd.costume.file))
                    sb.Append(", \"costumeFile\": \"").Append(JsonUtil.Escape(pd.costume.file))
                      .Append("\", \"costumeRepo\": \"").Append(JsonUtil.Escape(pd.costume.sourceRepo))
                      .Append("\", \"costumeFolder\": \"").Append(JsonUtil.Escape(pd.costume.repoFolder)).Append('"');
                sb.Append(", \"skins\": [");
                for (int j = 0; j < pd.skins.Count; j++)
                {
                    if (j > 0) sb.Append(", ");
                    AppendSkin(sb, pd.skins[j]);
                }
                sb.Append("] }");
            }
            sb.Append(pets.Count == 0 ? "],\n" : "\n  ],\n");

            sb.Append("  \"textures\": [");
            for (int i = 0; i < textures.Count; i++)
            {
                sb.Append(i == 0 ? "\n" : ",\n");
                sb.Append("    { \"fileName\": \"").Append(JsonUtil.Escape(textures[i].fileName))
                  .Append("\", \"b64\": \"").Append(textures[i].b64).Append("\" }");
            }
            sb.Append(textures.Count == 0 ? "],\n" : "\n  ],\n");

            sb.Append("  \"socials\": [");
            for (int i = 0; i < socials.Count; i++)
            {
                var s = socials[i];
                sb.Append(i == 0 ? "\n" : ",\n");
                sb.Append("    { \"phrase\": ").Append(s.phrase ? "true" : "false")
                  .Append(", \"emote\": ").Append(s.emote ? "true" : "false")
                  .Append(", \"clipName\": \"").Append(JsonUtil.Escape(s.clipName))
                  .Append("\", \"bundleB64\": \"").Append(s.bundleB64 ?? "").Append('"')
                  .Append(", \"slot\": ").Append(s.slot)
                  .Append(", \"text\": \"").Append(JsonUtil.Escape(s.text))
                  .Append("\", \"imageB64\": \"").Append(s.imageB64 ?? "")
                  .Append("\", \"imageExt\": \"").Append(JsonUtil.Escape(s.imageExt)).Append('"');
                for (int j = 0; j < 3; j++)
                    sb.Append(", \"s").Append(j).Append("B64\": \"").Append(s.soundB64[j] ?? "")
                      .Append("\", \"s").Append(j).Append("Ext\": \"").Append(JsonUtil.Escape(s.soundExt[j])).Append('"');
                sb.Append(" }");
            }
            sb.Append(socials.Count == 0 ? "],\n" : "\n  ],\n");

            sb.Append("  \"pbs\": [");
            for (int i = 0; i < pbs.Count; i++)
            {
                var e = pbs[i];
                sb.Append(i == 0 ? "\n" : ",\n");
                sb.Append("    { \"round\": \"").Append(JsonUtil.Escape(e.round))
                  .Append("\", \"rawId\": \"").Append(JsonUtil.Escape(e.rawId))
                  .Append("\", \"type\": \"").Append(JsonUtil.Escape(e.type))
                  .Append("\", \"time\": ").Append(e.time.ToString("R", CultureInfo.InvariantCulture))
                  .Append(", \"date\": \"").Append(JsonUtil.Escape(e.date))
                  .Append("\", \"ghostB64\": \"").Append(e.ghostB64 ?? "").Append("\" }");
            }
            sb.Append(pbs.Count == 0 ? "],\n" : "\n  ],\n");

            Field(sb, "iconB64", iconB64, raw: true);
            Field(sb, "backingB64", backingB64, raw: true);
            Field(sb, "previewB64", previewB64, raw: true);

            sb.Append("  \"plinth\": ");
            if (plinth == null) sb.Append("{}\n");
            else
                sb.Append("{ \"file\": \"").Append(JsonUtil.Escape(plinth.file))
                  .Append("\", \"repoUrl\": \"").Append(JsonUtil.Escape(plinth.repoUrl))
                  .Append("\", \"source\": \"").Append(JsonUtil.Escape(plinth.source))
                  .Append("\", \"folder\": \"").Append(JsonUtil.Escape(plinth.folder))
                  .Append("\", \"bundleB64\": \"").Append(plinth.bundleB64 ?? "").Append("\" }\n");

            sb.Append("}\n");
            return sb.ToString();
        }

        static void AppendSkin(StringBuilder sb, RemoteSkinEntry s)
            => sb.Append("{ \"file\": \"").Append(JsonUtil.Escape(s.file))
                 .Append("\", \"type\": \"").Append(JsonUtil.Escape(s.type))
                 .Append("\", \"source\": \"").Append(JsonUtil.Escape(s.source))
                 .Append("\", \"repoUrl\": \"").Append(JsonUtil.Escape(s.repoUrl))
                 .Append("\", \"folder\": \"").Append(JsonUtil.Escape(s.folder))
                 .Append("\", \"hand\": ").Append(s.hand)
                 .Append(", \"bundleB64\": \"").Append(s.bundleB64 ?? "").Append("\" }");

        static RemoteSkinEntry ReadSkin(string s) => new RemoteSkinEntry
        {
            file = JsonUtil.GetValue(s, "file"),
            type = JsonUtil.GetValue(s, "type"),
            source = JsonUtil.GetValue(s, "source"),
            localPath = UntrustedInput.LocalPath(JsonUtil.GetValue(s, "localPath")),
            repoUrl = UntrustedInput.RepoUrl(JsonUtil.GetValue(s, "repoUrl")),
            folder = JsonUtil.GetValue(s, "folder"),
            bundleB64 = JsonUtil.GetValue(s, "bundleB64"),
            hand = JsonUtil.GetInt(s, "hand"),
        };

        static void Field(StringBuilder sb, string key, string value, bool raw = false)
            => sb.Append("  \"").Append(key).Append("\": \"")
                 .Append(raw ? value ?? "" : JsonUtil.Escape(value)).Append("\",\n");

        public static BfgProfile FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var p = new BfgProfile
            {
                username = JsonUtil.GetValue(json, "username"),
                displayName = JsonUtil.GetValue(json, "displayName"),
                platformName = JsonUtil.GetValue(json, "platformName"),
                scale = JsonUtil.GetFloat(json, "scale", 1f),
                playerID = (uint)JsonUtil.GetInt(json, "playerID"),
                episodeGUID = JsonUtil.GetValue(json, "episodeGUID"),
                iconB64 = JsonUtil.GetValue(json, "iconB64"),
                backingB64 = JsonUtil.GetValue(json, "backingB64"),
                previewB64 = JsonUtil.GetValue(json, "previewB64"),
            };

            if (string.IsNullOrEmpty(p.username))
                p.username = JsonUtil.GetValue(json, "playerName");
            if (string.IsNullOrEmpty(p.username))
                p.username = JsonUtil.GetValue(json, "playerKey");
            if (p.scale <= 0f) p.scale = JsonUtil.GetFloat(json, "playerScale", 1f);

            string settingsObj = JsonUtil.GetObject(json, "settings");
            if (!string.IsNullOrEmpty(settingsObj))
            {
                p.settings = JsonUtil.ReadFlatObject(settingsObj);
                UntrustedInput.SettingValues(p.settings);
            }

            foreach (var s in JsonUtil.GetArray(json, "skins"))
                p.skins.Add(ReadSkin(s));

            foreach (var s in JsonUtil.GetArray(json, "pets"))
            {
                var pd = new PetData
                {
                    name = JsonUtil.GetValue(s, "name"),
                    costumeTop = JsonUtil.GetValue(s, "top"),
                    costumeBottom = JsonUtil.GetValue(s, "bottom"),
                    pattern = JsonUtil.GetValue(s, "pattern"),
                    faceplate = JsonUtil.GetValue(s, "faceplate"),
                    colour = JsonUtil.GetValue(s, "colour"),
                    scale = JsonUtil.GetFloat(s, "scale", 0.6f),
                };
                string costumeFile = JsonUtil.GetValue(s, "costumeFile");
                if (!string.IsNullOrEmpty(costumeFile))
                    pd.costume = new SkinInfo
                    {
                        name = costumeFile,
                        file = costumeFile,
                        type = "costume",
                        sourceRepo = UntrustedInput.RepoUrl(JsonUtil.GetValue(s, "costumeRepo")),
                        repoFolder = JsonUtil.GetValue(s, "costumeFolder"),
                    };
                foreach (var e in JsonUtil.GetArray(s, "skins"))
                    pd.skins.Add(ReadSkin(e));
                if (pd.skins.Count == 0 && pd.costume != null)
                    pd.skins.Add(BeanLook.FromSkin(pd.costume));
                p.pets.Add(pd);
            }

            foreach (var t in JsonUtil.GetArray(json, "textures"))
                p.textures.Add(new TexEmbed { fileName = JsonUtil.GetValue(t, "fileName"), b64 = JsonUtil.GetValue(t, "b64") });

            foreach (var s in JsonUtil.GetArray(json, "socials"))
                p.socials.Add(new SocialEmbed
                {
                    phrase = JsonUtil.GetBool(s, "phrase"),
                    emote = JsonUtil.GetBool(s, "emote"),
                    clipName = JsonUtil.GetValue(s, "clipName"),
                    bundleB64 = JsonUtil.GetValue(s, "bundleB64"),
                    slot = JsonUtil.GetInt(s, "slot"),
                    text = JsonUtil.GetValue(s, "text"),
                    imageB64 = JsonUtil.GetValue(s, "imageB64"),
                    imageExt = JsonUtil.GetValue(s, "imageExt"),
                    soundB64 = new[] { JsonUtil.GetValue(s, "s0B64"), JsonUtil.GetValue(s, "s1B64"), JsonUtil.GetValue(s, "s2B64") },
                    soundExt = new[] { JsonUtil.GetValue(s, "s0Ext"), JsonUtil.GetValue(s, "s1Ext"), JsonUtil.GetValue(s, "s2Ext") },
                });

            string plinthObj = JsonUtil.GetObject(json, "plinth");
            if (!string.IsNullOrEmpty(plinthObj))
            {
                string pf = JsonUtil.GetValue(plinthObj, "file");
                if (!string.IsNullOrEmpty(pf))
                    p.plinth = new PlinthEmbed
                    {
                        file = pf,
                        repoUrl = UntrustedInput.RepoUrl(JsonUtil.GetValue(plinthObj, "repoUrl")),
                        source = JsonUtil.GetValue(plinthObj, "source"),
                        folder = JsonUtil.GetValue(plinthObj, "folder"),
                        bundleB64 = JsonUtil.GetValue(plinthObj, "bundleB64"),
                    };
            }

            foreach (var e in JsonUtil.GetArray(json, "pbs"))
                p.pbs.Add(new PbEntry
                {
                    round = JsonUtil.GetValue(e, "round"),
                    rawId = JsonUtil.GetValue(e, "rawId"),
                    type = JsonUtil.GetValue(e, "type"),
                    time = JsonUtil.GetFloat(e, "time"),
                    date = JsonUtil.GetValue(e, "date"),
                    ghostB64 = JsonUtil.GetValue(e, "ghostB64"),
                });

            p.nametag = p.BuildNametag();
            if (p.nametag != null)
            {
                p.nametag.iconPath = UntrustedInput.LocalPath(p.nametag.iconPath);
                p.nametag.backingPath = UntrustedInput.LocalPath(p.nametag.backingPath);
            }
            return p;
        }

        public static string PeekUsername(string path)
        {
            string head = Head(path);
            string u = JsonUtil.GetValue(head, "username");
            return string.IsNullOrEmpty(u) ? JsonUtil.GetValue(head, "playerName") : u;
        }

        public static DateTime PeekExportedAt(string path)
        {
            return long.TryParse(JsonUtil.GetValue(Head(path), "exportedAt"), out long secs)
                ? DateTimeOffset.FromUnixTimeSeconds(secs).UtcDateTime
                : File.GetLastWriteTimeUtc(path);
        }

        static string Head(string path)
        {
            try
            {
                using (var r = new StreamReader(path))
                {
                    var buf = new char[512];
                    int n = r.Read(buf, 0, buf.Length);
                    return new string(buf, 0, n);
                }
            }
            catch { return ""; }
        }
    }
}
