using System;
using System.Collections.Generic;
using BetterFG.Customization.Player;
using BetterFG.Customization.Social;
using BetterFG.Services;

namespace BetterFG.Customization.Pets
{
    public class PetData
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name = "Pet";
        public string costumeTop = "";
        public string costumeBottom = "";
        public string pattern = "";
        public string faceplate = "";
        public string colour = "";
        public float scale = 0.6f;

        // optional full costume skin from the existing catalog - overrides the base look above.
        // legacy: the bean creator writes `skins` now, this is only still read for pets saved before it
        public SkinInfo costume;

        // UGC costume/accessory/item/emote bundles worn by this pet, resolved through the same
        // profile-skin path every other dressed bean uses
        public List<BetterFG.Network.RemoteSkinEntry> skins = new List<BetterFG.Network.RemoteSkinEntry>();

        // this pet's OWN skin texture overrides - never the local player's global catalog
        public List<SkinTexEntry> skinTexEntries = new List<SkinTexEntry>();

        // phrases the pet can pop up above its head while it's out, at a random interval in
        // [phraseIntervalMin, phraseIntervalMax] seconds - display duration is owned by the
        // game's own MotorFunctionSpeech state machine, not us. reuses the same PhraseEntry shape
        // (image + up to 3 sounds) the player's own Social > Phrases already has - phraseId/slot
        // are unused here (pets have no wheel slot)
        public List<PhraseEntry> phrases = new List<PhraseEntry>();
        public float phraseIntervalMin = 15f;
        public float phraseIntervalMax = 45f;
    }

    public static class PetStore
    {
        const string KEY_COUNT = "pets.count";
        const string KEY_ACTIVE = "pets.active";
        static string EK(int i, string f) => $"pets.{i}.{f}";

        // legacy single-value accessor - still read by the replay recorder
        public static string ActivePetId
        {
            get { var l = ActivePetIds; return l.Count > 0 ? l[0] : ""; }
            set => ActivePetIds = string.IsNullOrEmpty(value) ? new List<string>() : new List<string> { value };
        }

        // comma-joined set of equipped pet ids. a pre-existing single id parses fine as one element.
        public static List<string> ActivePetIds
        {
            get
            {
                var raw = SettingsService.Get(KEY_ACTIVE, "");
                var list = new List<string>();
                if (string.IsNullOrEmpty(raw)) return list;
                foreach (var part in raw.Split(','))
                {
                    var id = part.Trim();
                    if (id.Length > 0 && !list.Contains(id)) list.Add(id);
                }
                return list;
            }
            set => SettingsService.Set(KEY_ACTIVE, value == null ? "" : string.Join(",", value));
        }

        public static List<PetData> Load()
        {
            var list = new List<PetData>();
            int count = int.TryParse(SettingsService.Get(KEY_COUNT, "0"), out int c) ? c : 0;
            for (int i = 0; i < count; i++)
            {
                var p = new PetData
                {
                    id = SettingsService.Get(EK(i, "id"), Guid.NewGuid().ToString("N")),
                    name = SettingsService.Get(EK(i, "name"), "Pet"),
                    costumeTop = SettingsService.Get(EK(i, "top"), ""),
                    costumeBottom = SettingsService.Get(EK(i, "bottom"), ""),
                    pattern = SettingsService.Get(EK(i, "pattern"), ""),
                    faceplate = SettingsService.Get(EK(i, "faceplate"), ""),
                    colour = SettingsService.Get(EK(i, "colour"), ""),
                };
                float.TryParse(SettingsService.Get(EK(i, "scale"), "0.6"),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out p.scale);
                if (p.scale <= 0f) p.scale = 0.6f;

                p.skinTexEntries = SkinApplicationService.ReadTexEntries(SettingsService.Get, EK(i, "skintex."), EK(i, "skintex.count"));

                int phraseCount = int.TryParse(SettingsService.Get(EK(i, "phrase.count"), "0"), out int pc) ? pc : 0;
                for (int j = 0; j < phraseCount; j++)
                {
                    string raw = SettingsService.Get(EK(i, $"phrase.{j}"), "");
                    if (string.IsNullOrEmpty(raw)) continue;
                    string[] parts = raw.Split('|');
                    if (parts.Length < 3) continue;
                    p.phrases.Add(new PhraseEntry
                    {
                        id = parts[0],
                        phraseText = parts[1],
                        enabled = parts[2] == "1",
                        imagePath = parts.Length > 3 ? parts[3] : "",
                        soundPaths = new[]
                        {
                            parts.Length > 4 ? parts[4] : "",
                            parts.Length > 5 ? parts[5] : "",
                            parts.Length > 6 ? parts[6] : "",
                        },
                    });
                }
                float.TryParse(SettingsService.Get(EK(i, "phrase.min"), "15"),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out p.phraseIntervalMin);
                float.TryParse(SettingsService.Get(EK(i, "phrase.max"), "45"),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out p.phraseIntervalMax);

                string costumeFile = SettingsService.Get(EK(i, "costume.file"), "");
                if (!string.IsNullOrEmpty(costumeFile))
                {
                    p.costume = new SkinInfo
                    {
                        name = SettingsService.Get(EK(i, "costume.name"), costumeFile),
                        file = costumeFile,
                        type = "costume",
                        author = SettingsService.Get(EK(i, "costume.author"), ""),
                        sourceRepo = SettingsService.Get(EK(i, "costume.repo"), ""),
                        repoFolder = SettingsService.Get(EK(i, "costume.folder"), ""),
                    };
                }

                int skinCount = int.TryParse(SettingsService.Get(EK(i, "skin.count"), "0"), out int sc) ? sc : 0;
                for (int j = 0; j < skinCount; j++)
                {
                    string[] parts = SettingsService.Get(EK(i, $"skin.{j}"), "").Split('|');
                    if (parts.Length < 6 || string.IsNullOrEmpty(parts[0])) continue;
                    p.skins.Add(new BetterFG.Network.RemoteSkinEntry
                    {
                        file = parts[0],
                        type = parts[1],
                        source = parts[2],
                        repoUrl = parts[3],
                        folder = parts[4],
                        localPath = parts[5],
                        hand = parts.Length > 6 && int.TryParse(parts[6], out int hv) ? hv : 0,
                    });
                }

                // pets saved before the bean creator kept their one costume in its own keys
                if (skinCount == 0 && p.costume != null)
                    p.skins.Add(BetterFG.Customization.Player.BeanLook.FromSkin(p.costume));

                list.Add(p);
            }
            return list;
        }

        public static void Save(List<PetData> pets)
        {
            SettingsService.Set(KEY_COUNT, pets.Count.ToString());
            for (int i = 0; i < pets.Count; i++)
            {
                var p = pets[i];
                SettingsService.Set(EK(i, "id"), p.id);
                SettingsService.Set(EK(i, "name"), p.name);
                SettingsService.Set(EK(i, "top"), p.costumeTop ?? "");
                SettingsService.Set(EK(i, "bottom"), p.costumeBottom ?? "");
                SettingsService.Set(EK(i, "pattern"), p.pattern ?? "");
                SettingsService.Set(EK(i, "faceplate"), p.faceplate ?? "");
                SettingsService.Set(EK(i, "colour"), p.colour ?? "");
                SettingsService.Set(EK(i, "scale"), p.scale.ToString(System.Globalization.CultureInfo.InvariantCulture));

                SkinApplicationService.SaveEntries(p.skinTexEntries, EK(i, "skintex."), EK(i, "skintex.count"));

                SettingsService.Set(EK(i, "phrase.count"), p.phrases.Count.ToString());
                for (int j = 0; j < p.phrases.Count; j++)
                {
                    var e = p.phrases[j];
                    string Safe(string s) => (s ?? "").Replace("|", "");
                    string s0 = e.soundPaths != null && e.soundPaths.Length > 0 ? e.soundPaths[0] : "";
                    string s1 = e.soundPaths != null && e.soundPaths.Length > 1 ? e.soundPaths[1] : "";
                    string s2 = e.soundPaths != null && e.soundPaths.Length > 2 ? e.soundPaths[2] : "";
                    string line = $"{Safe(e.id)}|{Safe(e.phraseText)}|{(e.enabled ? "1" : "0")}|{Safe(e.imagePath)}|{Safe(s0)}|{Safe(s1)}|{Safe(s2)}";
                    SettingsService.Set(EK(i, $"phrase.{j}"), line);
                }
                SettingsService.Set(EK(i, "phrase.min"), p.phraseIntervalMin.ToString(System.Globalization.CultureInfo.InvariantCulture));
                SettingsService.Set(EK(i, "phrase.max"), p.phraseIntervalMax.ToString(System.Globalization.CultureInfo.InvariantCulture));

                SettingsService.Set(EK(i, "skin.count"), p.skins.Count.ToString());
                for (int j = 0; j < p.skins.Count; j++)
                {
                    var e = p.skins[j];
                    string Clean(string v) => (v ?? "").Replace("|", "");
                    SettingsService.Set(EK(i, $"skin.{j}"),
                        $"{Clean(e.file)}|{Clean(e.type)}|{Clean(e.source)}|{Clean(e.repoUrl)}|{Clean(e.folder)}|{Clean(e.localPath)}|{e.hand}");
                }

                SettingsService.Set(EK(i, "costume.file"), p.costume?.file ?? "");
                if (p.costume != null)
                {
                    SettingsService.Set(EK(i, "costume.name"), p.costume.name ?? "");
                    SettingsService.Set(EK(i, "costume.author"), p.costume.author ?? "");
                    SettingsService.Set(EK(i, "costume.repo"), p.costume.sourceRepo ?? "");
                    SettingsService.Set(EK(i, "costume.folder"), p.costume.repoFolder ?? "");
                }
            }
        }
    }
}
