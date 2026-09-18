using System.Collections.Generic;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class FeatureCreativeExpansion
    {
        public static readonly BfgFeature feature = new BfgFeature("creativeexpansion", "creativeexpansion.title", true, new List<FeatureSetting>
        {
            new FeatureSetting { id = "custombackgrounds", label = "creativeexpansion.custombackgrounds", defaultOn = true },
            new FeatureSetting { id = "objectthemes", label = "creativeexpansion.objectthemes", defaultOn = true },
            new FeatureSetting { id = "colouring", label = "creativeexpansion.colouring", defaultOn = true },
            new FeatureSetting { id = "customobjects", label = "creativeexpansion.customobjects", defaultOn = true },
            new FeatureSetting { id = "creativegroups", label = "creativeexpansion.creativegroups", defaultOn = true },
            new FeatureSetting { id = "creativegamemode", label = "creativeexpansion.creativegamemode", defaultOn = true },
            new FeatureSetting { id = "creativethumbnail", label = "creativeexpansion.creativethumbnail", defaultOn = true },
            new FeatureSetting { id = "publishwarning", label = "creativeexpansion.publishwarning", defaultOn = true },
            new FeatureSetting { id = "levelbrowse", label = "creativeexpansion.levelbrowse", defaultOn = true },
            new FeatureSetting { id = "levelport", label = "creativeexpansion.levelport", defaultOn = true },
            new FeatureSetting { id = "levelcode", label = "creativeexpansion.levelcode", defaultOn = true },
        });

        public static bool On(string id) => FeatureRegistry.IsOn("creativeexpansion", id);
    }
}
