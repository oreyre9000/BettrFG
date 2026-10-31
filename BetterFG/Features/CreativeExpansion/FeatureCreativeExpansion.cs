using System.Collections.Generic;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class FeatureCreativeExpansion
    {
        public static readonly BfgFeature feature = new BfgFeature("creativeexpansion", "creativeexpansion.title", true, new List<FeatureSetting>
        {
            new FeatureSetting { id = "creativegroups", label = "creativeexpansion.creativegroups", defaultOn = true },
            new FeatureSetting { id = "creativegamemode", label = "creativeexpansion.creativegamemode", defaultOn = true },
            new FeatureSetting { id = "creativethumbnail", label = "creativeexpansion.creativethumbnail", defaultOn = true },
            new FeatureSetting { id = "publishwarning", label = "creativeexpansion.publishwarning", defaultOn = true },
            new FeatureSetting { id = "levelbrowse", label = "creativeexpansion.levelbrowse", defaultOn = true },
            new FeatureSetting { id = "levelport", label = "creativeexpansion.levelport", defaultOn = true },
            new FeatureSetting { id = "levelcode", label = "creativeexpansion.levelcode", defaultOn = true },
            new FeatureSetting { id = "youtubemusic", label = "creativeexpansion.youtubemusic", defaultOn = true },
        });

        public static bool On(string id) => FeatureRegistry.IsOn("creativeexpansion", id);
    }
}
