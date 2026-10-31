using Character;
using FG.Common.CMS;
using FG.Common.Definition;
using FGClient;
using FGClient.Customiser;
using MPG.Utility;
using UnityEngine;

namespace BetterFG.Customization.Social
{
    // synthetic phrase SocialOption construction, shared by the local player's phrase wheel
    // injection (PhraseInjectionService) and pet speech (PetSpeechComponent) - both need a fresh
    // TextAndImageSpeechOption cloning a real phrase's icon/rarity metadata, just with different
    // text and a different id/registration target.
    internal static class SpeechOptionBuilder
    {
        public static ImageSpeechOption FindReferencePhraseOption()
        {
            var speechMgr = SingletonBehaviour<SpeechOptionsManager>.Instance;
            var lookup = speechMgr?._speechOptionsLookup;
            if (lookup == null) return null;
            foreach (var kvp in lookup)
                if (kvp.Value?.CMSGroupID == "cosmetics_phrases")
                    return kvp.Value.Cast<ImageSpeechOption>();
            return null;
        }

        public static TextAndImageSpeechOption Build(string id, string text, ImageSpeechOption refOpt)
        {
            var cms = new CustomiserPhrases();
            cms.Id = id;
            var loc = new LocalisedString { Id = cms.Id + "_text", Text = string.IsNullOrEmpty(text) ? "..." : text };
            cms.Cast<CMSItemDefinition>().Name = loc;
            cms.Cast<CMSItemDefinition>().IconName = refOpt.CMSData.Cast<CMSItemDefinition>().IconName;
            cms.Cast<CMSItemDefinition>().ItemRarity = refOpt.CMSData.Cast<CMSItemDefinition>().ItemRarity;

            var opt = ScriptableObject.CreateInstance<TextAndImageSpeechOption>();
            opt.SetCMSData(cms);
            opt.name = cms.Id;
            opt._speechDuration = 3f;
            opt._speechHasDuration = true;
            opt._audioBank = null;
            opt._audioEvent = null;
            // the wheel UI (SocialWheelOptionViewModel.SetData -> ItemDefinitionSO.GetMenuDisplaySpriteWhenReady)
            // NREs if menuDisplaySpriteAtlasReference/_spriteAtlasLoadableAsset are null - dropping
            // these to dodge an unrelated InvalidKeyException log elsewhere broke the real phrase
            // wheel outright. keep cloning them; the already-resolved sprite fields alone aren't enough.
            opt._cachedAtlasSprite = refOpt._cachedAtlasSprite;
            opt._sprite = refOpt._sprite;
            opt._spriteAtlasLoadableAsset = refOpt._spriteAtlasLoadableAsset;
            opt.menuDisplaySpriteAtlasReference = refOpt.menuDisplaySpriteAtlasReference;
            opt.menuDisplaySpriteReference = refOpt.menuDisplaySpriteReference;
            return opt;
        }
    }
}
