using System.Collections.Generic;
using BetterFG.Features.AllCosmetics;
using BetterFG.Features.CreativeExpansion;
using BetterFG.Features.CustomizeFallGuys;
using BetterFG.Features.MorePlatformIcon;
using BetterFG.Features.QualificationTime;
using BetterFG.Features.Replay;
using BetterFG.Features.Stars;
using BetterFG.Features.TimePlacement;

namespace BetterFG.Features
{
    public static class FeatureRegistry
    {
        static readonly List<BfgFeature> _all = new List<BfgFeature>
        {
            FeatureQualificationTime.feature,
            FeatureStars.feature,
            FeatureMorePlatformIcon.feature,
            FeatureTimePlacement.feature,
            FeatureReplay.feature,
            FeatureCustomizeFallGuys.feature,
            FeatureCreativeExpansion.feature,
            //FeatureAllCosmetics.feature
        };

        static FeatureRegistry() => _all.RemoveAll(f => Utilities.FeatureAvailability.Off(f.id));

        public static IReadOnlyList<BfgFeature> all => _all;

        public static BfgFeature Find(string id)
        {
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].id == id) return _all[i];
            return null;
        }

        public static bool IsOn(string featureId, string settingId)
        {
            var f = Find(featureId);
            return f != null && f.Get(settingId);
        }
    }
}
