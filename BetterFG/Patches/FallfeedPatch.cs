using System;
using System.Collections.Generic;
using BetterFG.Core;
using BetterFG.Features.MorePlatformIcon;
using BetterFG.Features.TimePlacement;
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
        private static readonly HashSet<IntPtr> _styledRows = new HashSet<IntPtr>();
        private static readonly Dictionary<IntPtr, ValueTuple<TMP_FontAsset, Material>> _rowOrigin
            = new Dictionary<IntPtr, ValueTuple<TMP_FontAsset, Material>>();

        private static string KeyOf(FallFeedNotificationViewModel vm)
            => (vm._playerNameText?.text ?? "") + "" + (vm._playerNameText2?.text ?? "") + "" + (vm._messageBodyText?.text ?? "") + "" + vm._enableTime;

        internal static void RestyleVm(FallFeedNotificationViewModel vm)
        {
            if (vm == null || vm._disposed) return;
            IntPtr id = vm.Pointer;
            string keyBefore = KeyOf(vm);
            if (_lastRestyleKey.TryGetValue(id, out string prev) && prev == keyBefore) return;

            string primaryNameBefore = vm._playerNameText?.text ?? "";
            string localKey = GlobalGameStateClient.Instance?.GetLocalPlayerKey() ?? "";

            RestyleSlot(vm._platformIconText, vm._playerNameText, localKey);
            RestyleSlot(vm._platformIconText2, vm._playerNameText2, localKey);

            string qualKey = NametagIconApplicator.ResolveKeyByDisplayName(primaryNameBefore);
            if (string.IsNullOrEmpty(qualKey) && IsLocalDisplayName(primaryNameBefore))
                qualKey = FeatureTimePlacement.LocalPlayerKey();
            FallFeedQualTimeTweak.Instance?.Apply(vm._messageBodyText, qualKey);

            vm.RefreshStructuredLayout();

            _lastRestyleKey[id] = KeyOf(vm);
        }

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

        private static void RestoreRowFont(TextMeshProUGUI t)
        {
            t.enableVertexGradient = false;
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
            NametagIconApplicator.RemoveInlineUIIcon(t.transform);
        }

        internal static void RestyleSlot(TextMeshProUGUI iconText, TextMeshProUGUI nameText, string localKey)
        {
            if (nameText == null || !nameText.gameObject.activeSelf) return;
            string displayName = nameText.text ?? "";
            if (string.IsNullOrEmpty(displayName)) return;
            Color gameColor = nameText.color;

            if (!_rowOrigin.ContainsKey(nameText.m_CachedPtr))
                _rowOrigin[nameText.m_CachedPtr] = new ValueTuple<TMP_FontAsset, Material>(nameText.font, nameText.fontSharedMaterial);

            bool isLocal = IsLocalDisplayName(displayName);
            string fullKey = isLocal ? localKey : NametagIconApplicator.ResolveKeyByDisplayName(displayName);

            string spriteName = FeatureMorePlatformIcon.SpriteNameForPlayerKey(fullKey);
            if (!string.IsNullOrEmpty(spriteName) && iconText != null && NametagIconApplicator.ApplyInlinePlatformAssetOutline(iconText))
            {
                if (!iconText.gameObject.activeSelf) iconText.gameObject.SetActive(true);
                if (!iconText.enabled) iconText.enabled = true;
                NametagIconApplicator.ApplyInlinePlatform(iconText, spriteName, 0.6f, -0.12f);
            }

            var info = isLocal
                ? FeatureMorePlatformIcon.LocalNametagInfo()
                : (RemoteProfileStore.TryGet(fullKey)?.nametag ?? RemoteProfileStore.TryGet(displayName)?.nametag);

            if (info == null)
            {
                _styledRows.Remove(nameText.m_CachedPtr);
                RestoreRowFont(nameText);
                return;
            }

            RestoreRowFont(nameText);
            _styledRows.Add(nameText.m_CachedPtr);

            string fallback = displayName;
            if (StripSizeTagsTweak.Active) fallback = StripSizeTagsTweak.Strip(fallback);
            NametagIconApplicator.ApplyRemoteToNameplate(nameText, fallback, info);

            string nameStyle = info.nameStyle ?? "";
            if (nameStyle != "gold" && nameStyle != "goldcolored"
                && _rowOrigin.TryGetValue(nameText.m_CachedPtr, out var origin)
                && origin.Item2 != null && nameText.font == origin.Item1)
                nameText.fontSharedMaterial = origin.Item2;

            if (gameColor.r < 0.99f || gameColor.g < 0.99f || gameColor.b < 0.99f)
            {
                nameText.enableVertexGradient = false;
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
            if (t == null || !_rowOrigin.ContainsKey(t.m_CachedPtr)) return;
            _styledRows.Remove(t.m_CachedPtr);
            RestoreRowFont(t);
            t.text = "";
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
                FallFeedNameCore.RestyleVm(vm);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Fallfeedpatch.cs " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(FallFeedNotificationViewModel), "ShowMessage")]
    internal static class FallFeedShowMessagePatch
    {
        [HarmonyPostfix]
        public static void Postfix(FallFeedNotificationViewModel __instance)
        {
            try { FallFeedNameCore.RestyleVm(__instance); }
            catch (Exception ex) { Plugin.Log.LogWarning("Fallfeedpatch.cs " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(FallFeedNotificationViewModel), "Update")]
    internal static class FallFeedUpdatePatch
    {
        [HarmonyPostfix]
        public static void Postfix(FallFeedNotificationViewModel __instance)
        {
            try { FallFeedNameCore.RestyleVm(__instance); BetterFG.Tweaks.SkipNotificationTweak.ShortenRespawn(__instance); }
            catch (Exception ex) { Plugin.Log.LogWarning("Fallfeedpatch.cs " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(FallFeedNotificationViewModel), "ApplyMultiTMPMode")]
    internal static class FallFeedApplyMultiTMPModePatch
    {
        [HarmonyPostfix]
        public static void Postfix(FallFeedNotificationViewModel __instance)
        {
            try { FallFeedNameCore.RestyleVm(__instance); }
            catch (Exception ex) { Plugin.Log.LogWarning("Fallfeedpatch.cs " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(FallFeedNotificationViewModel), "ApplyDisconnectFullLineMode")]
    internal static class FallFeedApplyDisconnectFullLineModePatch
    {
        [HarmonyPostfix]
        public static void Postfix(FallFeedNotificationViewModel __instance)
        {
            try { FallFeedNameCore.RestyleVm(__instance); }
            catch (Exception ex) { Plugin.Log.LogWarning("Fallfeedpatch.cs " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(FallFeedNotificationViewModel), "DisposeNotification")]
    internal static class FallFeedDisposeNotificationPatch
    {
        [HarmonyPrefix]
        public static void Prefix(FallFeedNotificationViewModel __instance)
        {
            try { FallFeedNameCore.ClearStylingOnDispose(__instance); }
            catch (Exception ex) { Plugin.Log.LogWarning("Fallfeedpatch.cs " + ex.Message); }
        }
    }
}
