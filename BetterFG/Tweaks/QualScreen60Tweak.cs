using System;
using FGClient;
using HarmonyLib;

namespace BetterFG.Tweaks
{
    public class QualScreen60Tweak : BfgTweak
    {
        public QualScreen60Tweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "qual_screen_60";
        public override string TweakLabel => "tweak.qual_screen_60";
        public override bool DefaultEnabled => false;

        public static QualScreen60Tweak Instance { get; private set; }
        void Awake() => Instance = this;
    }

    [HarmonyPatch(typeof(StateQualificationScreen), "GetQualificationScreenName")]
    internal static class QualScreen60Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ref string __result)
        {
            if (QualScreen60Tweak.Instance == null || !QualScreen60Tweak.Instance.IsEnabled) return;
            Plugin.Log.LogInfo($"qual screen: {__result} swapped for the 60 one");
            __result = "FallGuy_Qualification_Scene";
        }
    }
}
