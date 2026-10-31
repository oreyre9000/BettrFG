using System;
using System.Collections.Generic;
using BetterFG.Core;
using BetterFG.Features.MorePlatformIcon;
using BetterFG.Nametag;
using BetterFG.Network;
using BetterFG.Tweaks;
using FGClient.FallFeed;
using FGClient;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace BetterFG.Patches
{
    internal static class FallFeedNameCore
    {
        private static readonly Dictionary<IntPtr, string> _lastRestyleKey = new Dictionary<IntPtr, string>();

        private static string KeyOf(FallFeedNotificationViewModel vm)
            => (vm._playerNameText?.text ?? "") + "" + (vm._playerNameText2?.text ?? "") + "" + (vm._messageBodyText?.text ?? "") + "" + vm._enableTime;

        internal static void RestyleVm(FallFeedNotificationViewModel vm, string src = "?")
        {
            if (vm == null || vm._disposed) return;
            IntPtr id = vm.Pointer;
            string keyBefore = KeyOf(vm);
            if (_lastRestyleKey.TryGetValue(id, out string prev) && prev == keyBefore) return;

            string primaryNameBefore = vm._playerNameText?.text ?? "";
            string secondaryNameBefore = vm._playerNameText2?.text ?? "";
            string bodyBefore = vm._messageBodyText?.text ?? "";
            string localKey = GlobalGameStateClient.Instance?.GetLocalPlayerKey() ?? "";
            Plugin.Log.LogInfo($"ff[{src}] vm={id.ToInt64():x} n1='{primaryNameBefore}' n2='{secondaryNameBefore}' body='{bodyBefore}' local='{LocalPlayerInfo.FGlocalplayerusername}'/'{LocalPlayerInfo.DisplayName}'");

            RestyleSlot(vm._platformIconText, vm._playerNameText, localKey, "n1");
            RestyleSlot(vm._platformIconText2, vm._playerNameText2, localKey, "n2");

            bool primaryIsLocal = IsLocalDisplayName(primaryNameBefore);
            string primaryKey = primaryIsLocal ? localKey : NametagIconApplicator.ResolveKeyByDisplayName(primaryNameBefore);
            string qualKey = NametagIconApplicator.ResolveKeyByDisplayName(primaryNameBefore);
            if (string.IsNullOrEmpty(qualKey) && primaryIsLocal)
                qualKey = FallGuysLib.Players.PlayerUtils.GetLocalBarePlayerKey();
            Plugin.Log.LogInfo($"ff[{src}] qual: primaryIsLocal={primaryIsLocal} primaryKey='{primaryKey}' qualKey='{qualKey}'");
            FallFeedQualTimeTweak.Instance?.Apply(vm._messageBodyText, qualKey);

            vm.RefreshStructuredLayout();

            _lastRestyleKey[id] = KeyOf(vm);
            Plugin.Log.LogInfo($"ff[{src}] post n1='{vm._playerNameText?.text}' n2='{vm._playerNameText2?.text}'");
        }

        // CreateNotification doesn't hand us the ViewModel it just populated, so find it. Body text
        // alone isn't enough — every elimination (or every player sending the same phrase) shares the
        // exact same MessageBody, so a plain text match grabbed whatever row happened to sit at the
        // highest sibling index, not the one this call just populated (that's what was putting the
        // local player's styling on other people's rows, and delaying it onto a later one for our own).
        // _enableTime gets stamped to Time.time the instant a row is shown, and CreateNotification is
        // fully synchronous, so the row it just populated has the LARGEST _enableTime among any text
        // matches — same frame, same Time.time as this postfix.
        internal static FallFeedNotificationViewModel FindShownNotification(Transform container, string messageBody)
        {
            if (container == null) return null;
            FallFeedNotificationViewModel best = null;
            float bestEnableTime = float.NegativeInfinity;
            for (int i = 0; i < container.childCount; i++)
            {
                var vm = container.GetChild(i)?.GetComponent<FallFeedNotificationViewModel>();
                if (vm == null || vm._disposed) continue;
                if (vm._messageBodyText == null || vm._messageBodyText.text != messageBody) continue;
                if (vm._enableTime < bestEnableTime) continue;
                bestEnableTime = vm._enableTime;
                best = vm;
            }
            return best;
        }

        // fall-feed name TMPs we've actually styled — a pooled row we never touched keeps whatever
        // material the game gave it, so a profile-less player never gets scrubbed to the shadow mat.
        private static readonly HashSet<IntPtr> _styledRows = new HashSet<IntPtr>();

        // each row's own font + material as the game first handed it to us. reverting to a single
        // global asap-bold-shadow material stomped CJK / other-language / custom-font rows onto the
        // wrong atlas — that's the garbled bold-shadow text. restore the row's real original instead.
        private static readonly Dictionary<IntPtr, ValueTuple<TMPro.TMP_FontAsset, Material>> _rowOrigin
            = new Dictionary<IntPtr, ValueTuple<TMPro.TMP_FontAsset, Material>>();

        private static void RestoreRowFont(TextMeshProUGUI t)
        {
            t.enableVertexGradient = false;
            NametagIconApplicator.UnregisterGradient(t);
            if (_rowOrigin.TryGetValue(t.m_CachedPtr, out var o))
            {
                if (o.Item1 != null) t.font = o.Item1;
                if (o.Item2 != null) t.fontSharedMaterial = o.Item2;
            }
            else if (AssetManager.DefaultNameMaterial != null)
            {
                t.fontSharedMaterial = AssetManager.DefaultNameMaterial;
            }
            string cur = t.text ?? "";
            if (cur.IndexOf('<') >= 0)
                t.text = System.Text.RegularExpressions.Regex.Replace(cur, "<[^>]*>", "").Trim();
            // rows are pooled - whoever occupied this TMP before may have had an icon attached as a
            // sibling under it (AttachUIIcon), and that icon does NOT get cleared just because the
            // font/material got reset above. left alone it rides along onto whoever reuses this row
            // next, showing the WRONG player with our icon.
            NametagIconApplicator.RemoveInlineUIIcon(t.transform);
        }

        internal static void RestyleSlot(TextMeshProUGUI iconText, TextMeshProUGUI nameText, string localKey, string tag = "")
        {
            if (nameText == null || !nameText.gameObject.activeSelf) return;
            string displayName = nameText.text ?? "";
            if (string.IsNullOrEmpty(displayName)) { Plugin.Log.LogInfo($"ff  slot[{tag}] empty, skip"); return; }
            Color gameColor = nameText.color;

            if (!_rowOrigin.ContainsKey(nameText.m_CachedPtr))
                _rowOrigin[nameText.m_CachedPtr] = new ValueTuple<TMPro.TMP_FontAsset, Material>(nameText.font, nameText.fontSharedMaterial);

            bool isLocal = IsLocalDisplayName(displayName);
            string fullKey = isLocal ? localKey : NametagIconApplicator.ResolveKeyByDisplayName(displayName);
            Plugin.Log.LogInfo($"ff  slot[{tag}] ptr={nameText.m_CachedPtr.ToInt64():x} name='{displayName}' isLocal={isLocal} key='{fullKey}' wasStyled={_styledRows.Contains(nameText.m_CachedPtr)}");

            string spriteName = FeatureMorePlatformIcon.SpriteNameForPlayerKey(fullKey);
            // outline sheet (not the plain one) — that's what fall feed used pre-update, since its
            // icons sit over a varied message background rather than a flat panel. 0.6f compensates
            // for the 1.8x IconScale every glyph in this sprite asset is baked with (BuildAsset),
            // not something tuned for the old inline-string layout specifically.
            if (!string.IsNullOrEmpty(spriteName) && iconText != null && NametagIconApplicator.ApplyInlinePlatformAssetOutline(iconText))
            {
                if (!iconText.gameObject.activeSelf) iconText.gameObject.SetActive(true);
                if (!iconText.enabled) iconText.enabled = true;
                NametagIconApplicator.ApplyInlinePlatform(iconText, spriteName, 0.6f, -0.12f);
            }

            var info = isLocal
                ? FeatureMorePlatformIcon.LocalNametagInfo()
                : (RemoteProfileStore.TryGet(fullKey)?.nametag ?? RemoteProfileStore.TryGet(displayName)?.nametag);
            Plugin.Log.LogInfo($"ff  slot[{tag}] info={(info == null ? "null" : (isLocal ? "LOCAL" : "REMOTE"))} customName='{info?.customName}'");

            if (info == null)
            {
                _styledRows.Remove(nameText.m_CachedPtr);
                RestoreRowFont(nameText);
                return;
            }

            // rows are pooled — the game refreshes .text every show but never resets colour/gradient/
            // material, so whatever a previous occupant's row got styled to (gold gradient, custom
            // colour) sticks around for whoever reuses that TMP next: right name, someone else's look.
            RestoreRowFont(nameText);
            _styledRows.Add(nameText.m_CachedPtr);

            string fallback = displayName;
            if (StripSizeTagsTweak.Active) fallback = StripSizeTagsTweak.Strip(fallback);
            NametagIconApplicator.ApplyRemoteToNameplate(nameText, fallback, info);

            if (gameColor.r < 0.99f || gameColor.g < 0.99f || gameColor.b < 0.99f)
            {
                nameText.enableVertexGradient = false;
                NametagIconApplicator.UnregisterGradient(nameText);
                nameText.text = System.Text.RegularExpressions.Regex.Replace(nameText.text ?? "", "</?color[^>]*>", "");
                nameText.color = gameColor;
            }
        }

        internal static bool IsLocalDisplayName(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return false;
            displayName = NametagIconApplicator.StripRichText(displayName).Trim();
            string a = LocalPlayerInfo.FGlocalplayerusername;
            string b = LocalPlayerInfo.DisplayName;
            return (!string.IsNullOrEmpty(a) && displayName.Equals(a, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(b) && displayName.Equals(b, StringComparison.OrdinalIgnoreCase));
        }

        internal static void ClearStylingOnDispose(FallFeedNotificationViewModel vm)
        {
            if (vm == null) return;
            ClearSlotStyling(vm._playerNameText);
            ClearSlotStyling(vm._playerNameText2);
            _lastRestyleKey.Remove(vm.Pointer);
        }

        private static void ClearSlotStyling(TextMeshProUGUI t)
        {
            if (t == null) return;
            if (!_rowOrigin.ContainsKey(t.m_CachedPtr)) return;
            _styledRows.Remove(t.m_CachedPtr);
            RestoreRowFont(t);
            t.text = "";
        }

        internal static bool IsLocalPlayerKey(string playerKey)
        {
            if (string.IsNullOrEmpty(playerKey)) return false;
            string localBare = FallGuysLib.Players.PlayerUtils.GetLocalBarePlayerKey();
            if (string.IsNullOrEmpty(localBare)) return false;
            if (playerKey.Equals(localBare, StringComparison.OrdinalIgnoreCase)) return true;
            string cleanA = FallGuysLib.Players.PlayerUtils.CleanPlayerName(playerKey);
            string cleanB = FallGuysLib.Players.PlayerUtils.CleanPlayerName(localBare);
            return !string.IsNullOrEmpty(cleanA) && cleanA.Equals(cleanB, StringComparison.OrdinalIgnoreCase);
        }
    }

    [HarmonyPatch(typeof(FallFeedContainer), "CreateNotification")]
    internal static class FallFeedNamePatch
    {
        [HarmonyPostfix]
        public static void Postfix(FallFeedContainer __instance, FallFeedManager.FallFeedMessageData message)
        {
            try
            {
                if (message == null) return;
                var vm = FallFeedNameCore.FindShownNotification(__instance._notificationContainer, message.MessageBody ?? "");
                if (vm == null) return;
                FallFeedNameCore.RestyleVm(vm, "CreateNotification");
                BetterFG.Features.TextChat.FeatureTextChat.OnFallFeedRow(vm, message.MessageBody ?? "");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Fallfeedpatch.cs " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(FallFeedNotificationViewModel), "ShowMessage")]
    internal static class FallFeedShowMessagePatch
    {
        [HarmonyPostfix]
        public static void Postfix(FallFeedNotificationViewModel __instance)
        {
            try
            {
                FallFeedNameCore.RestyleVm(__instance, "ShowMessage");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Fallfeedpatch.cs " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(FallFeedNotificationViewModel), "Update")]
    internal static class FallFeedUpdatePatch
    {
        [HarmonyPostfix]
        public static void Postfix(FallFeedNotificationViewModel __instance)
        {
            try { FallFeedNameCore.RestyleVm(__instance, "Update"); BetterFG.Tweaks.SkipNotificationTweak.ShortenRespawn(__instance); }
            catch (Exception ex) { Plugin.Log.LogWarning("Fallfeedpatch.cs " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(FallFeedNotificationViewModel), "ApplyMultiTMPMode")]
    internal static class FallFeedApplyMultiTMPModePatch
    {
        [HarmonyPostfix]
        public static void Postfix(FallFeedNotificationViewModel __instance)
        {
            try { FallFeedNameCore.RestyleVm(__instance, "MultiTMP"); }
            catch (Exception ex) { Plugin.Log.LogWarning("Fallfeedpatch.cs " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(FallFeedNotificationViewModel), "ApplyDisconnectFullLineMode")]
    internal static class FallFeedApplyDisconnectFullLineModePatch
    {
        [HarmonyPostfix]
        public static void Postfix(FallFeedNotificationViewModel __instance)
        {
            try { FallFeedNameCore.RestyleVm(__instance, "DiscFullLine"); }
            catch (Exception ex) { Plugin.Log.LogWarning("Fallfeedpatch.cs " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(FallFeedNotificationViewModel), "DisposeNotification")]
    internal static class FallFeedDisposeNotificationPatch
    {
        [HarmonyPrefix]
        public static void Prefix(FallFeedNotificationViewModel __instance)
        {
            try
            {
                Plugin.Log.LogInfo($"ff[Dispose] vm={__instance.Pointer.ToInt64():x} n1='{__instance._playerNameText?.text}' n2='{__instance._playerNameText2?.text}'");
                FallFeedNameCore.ClearStylingOnDispose(__instance);
                BetterFG.Features.TextChat.FeatureTextChat.OnFallFeedDispose(__instance);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Fallfeedpatch.cs " + ex.Message); }
        }
    }
}
