using System.Collections.Generic;
using BetterFG.Customization.Pets;
using BetterFG.Features.Replay;
using BetterFG.Network;

namespace BetterFG.Customization.Player
{
    public class BeanLook
    {
        public string name = "";
        public string colour = "";
        public string pattern = "";
        public string top = "";
        public string bottom = "";
        public string faceplate = "";
        public float scale = 1f;
        public string colourId = "";
        public string patternId = "";
        public string topId = "";
        public string bottomId = "";
        public string faceplateId = "";
        public readonly List<RemoteSkinEntry> skins = new List<RemoteSkinEntry>();
        public readonly List<SkinTexEntry> texEntries = new List<SkinTexEntry>();

        public static BeanLook From(ReplayPlayer p)
        {
            var look = new BeanLook
            {
                name = p.name ?? "",
                colour = p.colour ?? "",
                pattern = p.pattern ?? "",
                top = p.costumeTop ?? "",
                bottom = p.costumeBottom ?? "",
                faceplate = p.faceplate ?? "",
                scale = p.bfgScale > 0f ? p.bfgScale : 1f,
                colourId = p.bfgColour ?? "",
                patternId = p.bfgPattern ?? "",
                faceplateId = p.bfgFaceplate ?? "",
            };

            var worn = (p.bfgCosmetics ?? "").Split('|');
            if (worn.Length > 0) look.topId = worn[0];
            if (worn.Length > 1) look.bottomId = worn[1];
            foreach (var entry in p.bfgSkins) look.skins.Add(Copy(entry));
            look.texEntries.AddRange(p.bfgTextures);
            return look;
        }

        public void Into(ReplayPlayer p)
        {
            p.name = name;
            p.colour = colour;
            p.pattern = pattern;
            p.costumeTop = top;
            p.costumeBottom = bottom;
            p.faceplate = faceplate;
            p.bfgScale = scale;
            p.bfgSkins.Clear();
            foreach (var entry in skins) p.bfgSkins.Add(Copy(entry));
            p.bfgTextures.Clear();
            p.bfgTextures.AddRange(texEntries);

            var ids = new List<string>();
            if (!string.IsNullOrEmpty(topId)) ids.Add(topId);
            if (!string.IsNullOrEmpty(bottomId)) ids.Add(bottomId);
            p.bfgCosmetics = string.Join("|", ids);
            p.bfgColour = colourId;
            p.bfgPattern = patternId;
            p.bfgFaceplate = faceplateId;
        }

        public void SetPick(string category, ItemDefinitionSO opt)
        {
            string name = opt != null ? (opt.name ?? "") : "";
            string id = "";
            if (opt != null)
            {
                if (category == SkinTexCategory.Colour)
                {
                    var c = opt.TryCast<ColourOption>();
                    if (c != null) id = SkinApplicationService.GetGameColourOptionId(c);
                }
                else if (category == SkinTexCategory.Pattern)
                {
                    var c = opt.TryCast<SkinPatternOption>();
                    if (c != null) id = SkinApplicationService.GetGamePatternOptionId(c);
                }
                else if (category == SkinTexCategory.Faceplate)
                {
                    var c = opt.TryCast<FaceplateOption>();
                    if (c != null) id = SkinApplicationService.GetGameFaceplateOptionId(c);
                }
                else
                {
                    var c = opt.TryCast<CostumeOption>();
                    if (c != null) id = SkinApplicationService.GetGameCosmeticOptionId(c);
                }
            }

            if (category == SkinTexCategory.Colour) { colour = name; colourId = id; }
            else if (category == SkinTexCategory.Pattern) { pattern = name; patternId = id; }
            else if (category == SkinTexCategory.Faceplate) { faceplate = name; faceplateId = id; }
            else if (category == SkinTexCategory.Upper) { top = name; topId = id; }
            else { bottom = name; bottomId = id; }
        }

        public static BeanLook From(PetData data)
        {
            var look = new BeanLook
            {
                name = data.name ?? "",
                colour = data.colour ?? "",
                pattern = data.pattern ?? "",
                top = data.costumeTop ?? "",
                bottom = data.costumeBottom ?? "",
                faceplate = data.faceplate ?? "",
                scale = data.scale > 0f ? data.scale : 0.6f,
            };
            foreach (var entry in data.skins) look.skins.Add(Copy(entry));
            if (data.skinTexEntries != null) look.texEntries.AddRange(data.skinTexEntries);
            return look;
        }

        public void Into(PetData data)
        {
            data.name = name;
            data.colour = colour;
            data.pattern = pattern;
            data.costumeTop = top;
            data.costumeBottom = bottom;
            data.faceplate = faceplate;
            data.scale = scale;
            data.skins.Clear();
            foreach (var entry in skins) data.skins.Add(Copy(entry));
            data.skinTexEntries = new List<SkinTexEntry>(texEntries);

            data.costume = null;
            foreach (var entry in skins)
            {
                if (SkinTypeParser.FromString(entry.type) != SkinType.Costume) continue;
                data.costume = new SkinInfo
                {
                    name = entry.file,
                    file = entry.file,
                    type = entry.type,
                    sourceRepo = entry.repoUrl,
                    repoFolder = entry.folder,
                    localPath = entry.localPath,
                    isLocalImport = entry.source == "local",
                };
                break;
            }
        }

        public PetData ToPetData() { var d = new PetData(); Into(d); return d; }

        public static RemoteSkinEntry Copy(RemoteSkinEntry e) => new RemoteSkinEntry
        {
            file = e.file,
            type = e.type,
            source = e.source,
            localPath = e.localPath,
            repoUrl = e.repoUrl,
            folder = e.folder,
            hand = e.hand,
        };

        public static RemoteSkinEntry FromSkin(SkinInfo skin) => new RemoteSkinEntry
        {
            file = skin.file,
            type = skin.type,
            source = skin.isLocalImport ? "local" : "remote",
            localPath = skin.localPath ?? "",
            repoUrl = skin.sourceRepo ?? "",
            folder = !string.IsNullOrEmpty(skin.repoFolder)
                ? skin.repoFolder
                : SkinTypeParser.CategoryFolder(skin.type) + "/" + skin.file,
            hand = skin.handOverride,
        };

        public bool HasTexture(string entryName)
        {
            foreach (var entry in texEntries) if (entry.entryName == entryName) return true;
            return false;
        }

        public bool Has(string file)
        {
            foreach (var entry in skins) if (entry.file == file) return true;
            return false;
        }

        public int CountOf(SkinType type)
        {
            int n = 0;
            foreach (var entry in skins) if (SkinTypeParser.FromString(entry.type) == type) n++;
            return n;
        }
    }
}
