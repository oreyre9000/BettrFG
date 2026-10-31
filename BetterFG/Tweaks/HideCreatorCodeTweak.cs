using FGClient;
using FGClient.Fraggle;
using HarmonyLib;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class HideCreatorCodeTweak : BfgTweak
    {
        public HideCreatorCodeTweak(System.IntPtr ptr) : base(ptr) { }

        public override string TweakId => "hide_creator_code";
        public override string TweakLabel => "tweak.hide_creator_code";
        public override bool DefaultEnabled => false;

        public static HideCreatorCodeTweak Instance { get; private set; }

        void Awake() => Instance = this;

        public override void EnableTweak() => SetCreatorCodeVisible(false);
        public override void DisableTweak() => SetCreatorCodeVisible(true);

        internal static void SetCreatorCodeVisible(bool visible)
        {
            foreach (var vm in Object.FindObjectsOfType<CreatorIDViewModel>(true))
            {
                if (vm == null) continue;
                vm.gameObject.SetActive(visible);
            }
        }

        public static void OnLoadingScreenShown(LoadingGameScreenViewModel screen)
        {
            var inst = Instance;
            if (inst == null || !inst.IsEnabled) return;
            foreach (var vm in screen.GetComponentsInChildren<CreatorIDViewModel>(true))
            {
                if (vm == null) continue;
                vm.gameObject.SetActive(false);
            }
        }

        public static void OnCreatorIDPopulated(CreatorIDViewModel vm)
        {
            var inst = Instance;
            if (inst == null || !inst.IsEnabled) return;
            vm.gameObject.SetActive(false);
        }
    }

    // reapply on every round countdown so it survives scene reloads
    [HarmonyPatch(typeof(ClientGameManager), nameof(ClientGameManager.CountdownEnds))]
    public class CreatorCodeCountdownPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            var inst = HideCreatorCodeTweak.Instance;
            if (inst == null || !inst.IsEnabled) return;
            HideCreatorCodeTweak.SetCreatorCodeVisible(false);
        }
    }
}