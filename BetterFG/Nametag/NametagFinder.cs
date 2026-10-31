using BetterFG.Core;
using FGClient;
using PartyMenu;
using TMPro;
using UnityEngine;

namespace BetterFG.Nametag
{
    public static class NametagFinder
    {
        private const string NAME_AND_CROWN = "NameAndCrownParent";
        private const string NAME_TEXT_PATH = "NameAndCrownParent/NameText";
        private const string NAME_WRAP_PATH = "NameAndCrownParent/BetterFG_NameWrapper/NameText";

        private static readonly string[] NAMETAG_NAMES =
        {
            "LocalPlayerNameTagSprite(Clone)",
            "LocalPlayerNameTag(Clone)",
        };

        private static Transform _nameTagParent;

        private static Transform LocalTagUnder(Transform parent)
        {
            foreach (var name in NAMETAG_NAMES)
            {
                var t = parent.Find(name) ?? parent.Find("Parent/" + name);
                if (t != null) return t;
            }
            return null;
        }

        public static Transform FindLocalNameTagSprite()
        {
            if (_nameTagParent != null)
            {
                var cached = LocalTagUnder(_nameTagParent);
                if (cached != null) return cached;
            }

            foreach (var hud in UnityEngine.Object.FindObjectsOfType<PlayerInfoHUDBase>(true))
            {
                if ((hud.gameObject.hideFlags & HideFlags.HideAndDontSave) != 0) continue;
                var parent = hud._nameTagParent;
                if (parent == null) continue;
                var t = LocalTagUnder(parent.transform);
                if (t == null) continue;
                _nameTagParent = parent.transform;
                return t;
            }

            return null;
        }

        public static PlayerInfoDisplay FindLocalDisplay()
            => FindLocalNameTagSprite()?.GetComponent<PlayerInfoDisplay>();

        public static Transform FindNameAndCrownParent()
        {
            var tag = FindLocalNameTagSprite();
            return tag != null ? tag.Find(NAME_AND_CROWN) : null;
        }

        public static TMPro.TextMeshPro FindLocalNameText()
            => FindLocalNameTextAny()?.TryCast<TMPro.TextMeshPro>();

        public static TMP_Text FindLocalNameTextAny()
        {
            var tag = FindLocalNameTagSprite();
            if (tag == null) return null;

            var goDisplay = tag.GetComponent<PlayerInfoDisplayGameObject>();
            if (goDisplay != null && goDisplay._text != null) return goDisplay._text;

            var canvasDisplay = tag.GetComponent<PlayerInfoDisplayCanvas>();
            if (canvasDisplay != null && canvasDisplay._text != null) return canvasDisplay._text;

            var go = tag.Find(NAME_TEXT_PATH);
            if (go != null)
            {
                var tmp = go.GetComponent<TMP_Text>()
                       ?? go.GetComponentInChildren<TMP_Text>(true);
                if (tmp != null) return tmp;
            }

            var wrapped = tag.Find(NAME_WRAP_PATH);
            if (wrapped != null)
            {
                var tmp = wrapped.GetComponent<TMP_Text>()
                       ?? wrapped.GetComponentInChildren<TMP_Text>(true);
                if (tmp != null) return tmp;
            }

            var any = tag.GetComponentInChildren<TMP_Text>(true);
            if (any != null) return any;

            Plugin.Log.LogWarning($"NametagFinder: NameText not found under '{tag.name}'");
            return null;
        }

        // ── Public reapply ────────────────────────────────────────────────────

        public static void ReapplyAllNameplates()
        {
            var vms = UnityEngine.Object.FindObjectsOfType<NameTagViewModel>(true);
            if (vms != null)
                for (int i = 0; i < vms.Length; i++)
                    NametagPatchHub.HandleViewModel(vms[i], null);

            var partyVms = UnityEngine.Object.FindObjectsOfType<ExpandedMemberItemViewModel>(true);
            if (partyVms != null)
                for (int i = 0; i < partyVms.Length; i++)
                    ApplyToExpandedMember(partyVms[i]);
        }

        // scoped variant for view switches — only the rebuilt view's nameplates, not a whole-scene
        // FindObjectsOfType. anything outside this view kept its nameplate through the switch.
        public static void ReapplyNameplatesInScope(UnityEngine.Transform scope)
        {
            if (scope == null) return;
            foreach (var vm in scope.GetComponentsInChildren<NameTagViewModel>(true))
                NametagPatchHub.HandleViewModel(vm, null);
            foreach (var vm in scope.GetComponentsInChildren<ExpandedMemberItemViewModel>(true))
                ApplyToExpandedMember(vm);
        }

        // ── Nameplate helpers ─────────────────────────────────────────────────

        internal static void ApplyToExpandedMember(ExpandedMemberItemViewModel vm)
        {
            if (vm == null) return;
            var tmp = vm._nameplate?.Username;
            if (tmp == null) return;

            string memberName = vm._item?.Username ?? "";
            string realName = LocalPlayerInfo.FGlocalplayerusername;

            // local player — use live local settings
            if (!string.IsNullOrEmpty(realName) && memberName == realName)
            {
                bool useDisplay = BetterFG.Services.SettingsService.Get("nametag.enabled", "false") == "true"
                                  || !string.IsNullOrEmpty(LocalPlayerInfo.CustomName);
                string name = useDisplay ? LocalPlayerInfo.DisplayName : realName;
                NametagIconApplicator.ApplyToNameplate(tmp, name, NametagIconApplicator.NameplateType.Party);
                NametagIconApplicator.ApplyLocalPartyBacking(vm.transform);
                NametagIconApplicator.ApplyLocalNickname(vm.transform, party: true);
                return;
            }

#if PROFILES
            // remote member — match to an enabled profile by clean name and apply its nametag
            BetterFG.Network.NetworkClient.PrimeProfilesForLobby();
            string clean = FallGuysLib.Players.PlayerUtils.CleanPlayerName(memberName);
            var rp = BetterFG.Customization.Profiles.ProfileService.GetRemoteProfileForName(clean);
            var nt = rp?.nametag;
            if (nt == null) return;

            NametagIconApplicator.ApplyRemoteToNameplate(tmp, memberName, nt);
            NametagIconApplicator.ApplyPartyBacking(vm.transform, nt.backingEnabled, nt.backingPath, nt.backingOffX, nt.backingOffY, nt.backingScale);
            NametagIconApplicator.ApplyNickname(vm.transform, party: true, enabled: !string.IsNullOrEmpty(nt.nickname), text: nt.nickname ?? "");
#endif
        }

        // ── Patches ───────────────────────────────────────────────────────────
        // NOTE: NameTagViewModel patches are in NametagPatchHub — don't add them here too

        [HarmonyLib.HarmonyPatch(typeof(PartyMenuMemberItem), "UpdateGraphics")]
        internal static class patch_PartyMember_UpdateGraphics
        {
            [HarmonyLib.HarmonyPostfix]
            public static void Postfix(PartyMenuMemberItem __instance) => ApplyToExpandedMember(__instance._expandedModel);
        }

        [HarmonyLib.HarmonyPatch(typeof(PartyMenuMemberItem), "Select", new[] { typeof(bool) })]
        internal static class patch_PartyMember_Select
        {
            [HarmonyLib.HarmonyPostfix]
            public static void Postfix(PartyMenuMemberItem __instance) => ApplyToExpandedMember(__instance._expandedModel);
        }
    }
}
