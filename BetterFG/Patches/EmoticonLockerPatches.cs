using System;
using BetterFG.Customization.Social;
using Character;
using FGClient.Customiser;
using FGClient;
using FGClient.UI;
using HarmonyLib;

namespace BetterFG.Patches.Social
{
    [HarmonyPatch(typeof(PooledGridLayout), nameof(PooledGridLayout.SetCollection))]
    internal static class EmoticonGridCollectionPatch
    {
        [HarmonyPrefix]
        public static void Prefix(PooledGridLayout __instance, Il2CppSystem.Collections.IEnumerable value)
        {
            try
            {
                var section = __instance.GetComponent<CustomiserEmoticonsSection>();
                if (section != null)
                {
                    var opts = value?.TryCast<Il2CppSystem.Collections.Generic.List<ItemDefinitionSO>>();
                    if (opts != null) EmoticonLockerService.InjectTiles(section, opts);
                    return;
                }
                var phrases = __instance.GetComponent<CustomiserPhrasesSection>();
                if (phrases == null) return;
                var phraseOpts = value?.TryCast<Il2CppSystem.Collections.Generic.List<SocialOption>>();
                if (phraseOpts != null) PhraseLockerService.InjectTiles(phrases, phraseOpts);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"custom emoticon tiles didn't make it into the grid: {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(SocialWheelViewModel), nameof(SocialWheelViewModel.RefreshSocialWheel))]
    internal static class EmoticonMenuWheelPatch
    {
        [HarmonyPrefix]
        public static void Prefix(SocialWheelViewModel __instance, ref Il2CppSystem.Collections.Generic.List<ItemDefinitionSO> itemsList)
        {
            try
            {
                EmoticonLockerService.DecorateWheel(__instance, ref itemsList);
                PhraseLockerService.DecorateWheel(__instance, ref itemsList);
                if (itemsList != null && itemsList.Count > 0 && LockerEditorUtil.IsMenuWheel(__instance))
                    EmoteInjectionService.InjectInto(itemsList);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"custom emoticons didn't paint onto the locker wheel: {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(CustomiserEmoticonsSection), "SelectOptionImplementation")]
    internal static class EmoticonGridSelectPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ItemDefinitionSO option) => !EmoticonLockerService.HandleSelect(option);
    }

    [HarmonyPatch(typeof(ReportUGCPopupViewModel), nameof(ReportUGCPopupViewModel.CloseWindow))]
    internal static class ReportPopupCloseWindowPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ReportUGCPopupViewModel __instance)
        {
            if (!BetterFG.UI.ReportMenuShell.InputBlocked) return true;
            foreach (var b in __instance.GetComponentsInChildren<DelayedButton>(true)) b.ResetElements();
            return false;
        }
    }

    [HarmonyPatch(typeof(CustomiserEmoticonsSection), "OnGridOptionHighlighted")]
    internal static class EmoticonGridHighlightPatch
    {
        [HarmonyPostfix]
        public static void Postfix(CustomiserEmoticonsSection __instance, int idx)
        {
            try { EmoticonLockerService.OnHighlighted(__instance, idx); }
            catch (Exception ex) { Plugin.Log.LogWarning($"emoticon highlight tracking: {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(CustomiserEmoticonsSection), nameof(CustomiserEmoticonsSection.BindEmoticonsToDirectionInPlayerProfile))]
    internal static class EmoticonGridBindPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(SocialWheelItemDirection direction, SocialOption emoticon)
            => !EmoticonLockerService.HandleBind(direction, emoticon);
    }

    [HarmonyPatch(typeof(CustomiserPhrasesSection), "SelectOptionImplementation")]
    internal static class PhraseGridSelectPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(SocialOption option) => !PhraseLockerService.HandleSelect(option);
    }

    [HarmonyPatch(typeof(CustomiserPhrasesSection), "OnGridOptionHighlighted")]
    internal static class PhraseGridHighlightPatch
    {
        [HarmonyPostfix]
        public static void Postfix(CustomiserPhrasesSection __instance, int idx)
        {
            try { PhraseLockerService.OnHighlighted(__instance, idx); }
            catch (Exception ex) { Plugin.Log.LogWarning($"phrase highlight tracking: {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(CustomiserPhrasesSection), nameof(CustomiserPhrasesSection.BindPhrasesToDirectionInPlayerProfile))]
    internal static class PhraseGridBindPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(SocialWheelItemDirection direction, SocialOption phrase)
            => !PhraseLockerService.HandleBind(direction, phrase);
    }
}
