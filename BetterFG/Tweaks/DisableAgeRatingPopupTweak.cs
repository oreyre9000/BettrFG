using System;
using HarmonyLib;

namespace BetterFG.Tweaks
{
    public class DisableAgeRatingPopupTweak : BfgTweak
    {
        public DisableAgeRatingPopupTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "disable_age_rating_popup";
        public override string TweakLabel => "tweak.disable_age_rating_popup";
        public override bool DefaultEnabled => true;
        public override string TweakTooltip => "ui.stops_region_gated_korean_age_rating_compliance";
    }

    internal static class NotKorea
    {
        public const string RegionCode = "KR";
        public const string Replacement = "US";

        public static string Fix(string region) => region == RegionCode ? Replacement : region;
    }

    [Utilities.BfgPatchGate("tweak.disable_age_rating_popup", defaultOn: true)]
    [HarmonyPatch(typeof(RegionLockingManager), "SetRegion")]
    internal static class ForceNotKoreaRegionPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RegionLockingManager __instance)
        {
            __instance._regionCode = NotKorea.Fix(__instance._regionCode);
            __instance._overrideRegionCode = NotKorea.Fix(__instance._overrideRegionCode);
        }
    }

    [Utilities.BfgPatchGate("tweak.disable_age_rating_popup", defaultOn: true)]
    [HarmonyPatch(typeof(FGClient.Requirements.CountryRequirement), "GetCountry")]
    internal static class ForceNotKoreaCountryPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref string __result) => __result = NotKorea.Fix(__result);
    }

    [Utilities.BfgPatchGate("tweak.disable_age_rating_popup", defaultOn: true)]
    [HarmonyPatch(typeof(FGClient.Requirements.RegionRequirement), "GetRegion")]
    internal static class ForceNotKoreaRegionRequirementPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref string __result) => __result = NotKorea.Fix(__result);
    }
}
