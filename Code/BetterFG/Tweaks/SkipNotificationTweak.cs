using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using FG.Common;
using FGClient.FallFeed;
using FallGuysLib.Players;
using HarmonyLib;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class SkipNotificationTweak : BfgTweak
    {
        public SkipNotificationTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "skip_notification";
        public override string TweakLabel => "tweak.more_fall_feed_notifications";
        public override bool DefaultEnabled => false;
        public override string TweakTooltip => "tweak.more_fall_feed_notifications.tip";

        public static SkipNotificationTweak Instance { get; private set; }
        void Awake() => Instance = this;

        static readonly Dictionary<string, string> _pendingNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        internal static bool HasPending => _pendingNames.Count > 0;

        public void Notify(string playerKey, GameMessageServerPlayerProgress progressMessage, string labelId)
        {
            if (!IsEnabled) return;
            if (progressMessage == null || string.IsNullOrEmpty(playerKey)) return;
            if (BetterFG.UI.BetterFGUIMan.Instance == null) return;
            string display = PlayerUtils.CleanPlayerName(playerKey);
            if (string.IsNullOrEmpty(display)) return;
            _pendingNames[display] = labelId;
            Plugin.Log.LogInfo($"fall feed notif: pending += '{display}' as {labelId} (now {_pendingNames.Count})");
            BetterFG.UI.BetterFGUIMan.Instance.StartCoroutine(FireIfGameDidntEmit(display, progressMessage).WrapToIl2Cpp());
        }

        internal static bool TryConsumeForRenderedName(string renderedNameText, out string matched, out string labelId)
        {
            matched = null;
            labelId = null;
            if (string.IsNullOrEmpty(renderedNameText) || _pendingNames.Count == 0) return false;
            string bare = Regex.Replace(renderedNameText, "<[^>]*>", "").Trim();
            if (string.IsNullOrEmpty(bare)) return false;
            foreach (var p in _pendingNames)
            {
                if (bare.Equals(p.Key, StringComparison.OrdinalIgnoreCase))
                {
                    labelId = p.Value;
                    _pendingNames.Remove(p.Key);
                    matched = p.Key;
                    return true;
                }
            }
            return false;
        }

        IEnumerator FireIfGameDidntEmit(string displayName, GameMessageServerPlayerProgress msg)
        {
            for (int i = 0; i < 8; i++) yield return null;
            if (!_pendingNames.ContainsKey(displayName)) yield break;
            ForceEmit(displayName, msg);
        }

        static void ForceEmit(string displayName, GameMessageServerPlayerProgress msg)
        {
            try
            {
                var handler = UnityEngine.Object.FindObjectOfType<FallFeedQualifyEliminateHandler>();
                if (handler == null) { Plugin.Log.LogInfo("skip notif: no QualifyEliminate handler live"); return; }

                var mi = AccessTools.Method(typeof(FallFeedQualifyEliminateHandler), "HandleElimination")
                       ?? AccessTools.Method(typeof(FallFeedHandlerBase), "HandleElimination");
                if (mi == null) { Plugin.Log.LogWarning("skip notif: HandleElimination not found via AccessTools"); return; }

                var elimProp = AccessTools.Property(typeof(FallFeedQualifyEliminateHandler), "_eliminationEnabled");
                var qualProp = AccessTools.Property(typeof(FallFeedQualifyEliminateHandler), "_roundQualificationEnabled");
                bool origElim = elimProp != null && (bool)elimProp.GetValue(handler);
                bool origQual = qualProp != null && (bool)qualProp.GetValue(handler);
                bool wasSkipping = msg.isSkipping;
                bool wasDisconnected = msg.isDisconnected;
                elimProp?.SetValue(handler, true);
                qualProp?.SetValue(handler, true);
                msg.isSkipping = false;
                msg.isDisconnected = false;
                try { mi.Invoke(handler, new object[] { msg }); }
                finally
                {
                    msg.isSkipping = wasSkipping;
                    msg.isDisconnected = wasDisconnected;
                    elimProp?.SetValue(handler, origElim);
                    qualProp?.SetValue(handler, origQual);
                }
                Plugin.Log.LogInfo($"fall feed notif: force-emit for '{displayName}' (elimEnabled was {origElim}, qualEnabled was {origQual})");
            }
            catch (Exception ex) { Plugin.Log.LogWarning("fall feed notif: " + ex.Message); }
        }
    }

    // ApplyMultiTMPMode is where the game actually pushes name + body into the TMPs. ShowMessage
    // itself returns before those are set. Priority.First to run ahead of the styling postfix.
    [HarmonyPatch(typeof(FallFeedNotificationViewModel), "ApplyMultiTMPMode")]
    internal static class SkipBodyAppendPatch
    {
        [HarmonyPostfix, HarmonyPriority(Priority.First)]
        public static void Postfix(FallFeedNotificationViewModel __instance)
        {
            try
            {
                if (__instance == null || !SkipNotificationTweak.HasPending) return;
                var body = __instance._messageBodyText;
                var nameText = __instance._playerNameText;
                if (body == null || nameText == null) return;
                string curBody = body.text ?? "";
                if (curBody.IndexOf("fallfeed-eliminate", StringComparison.Ordinal) < 0) return;
                if (!SkipNotificationTweak.TryConsumeForRenderedName(nameText.text, out string matched, out string labelId)) return;
                string label = Services.LocalizationService.Get(labelId);
                if (curBody.Contains(label)) return;
                body.text = curBody + " " + label;
                body.ForceMeshUpdate();
                Plugin.Log.LogInfo($"fall feed append: '{matched}' -> '{body.text}'");
            }
            catch (Exception ex) { Plugin.Log.LogWarning("fall feed append: " + ex.Message); }
        }
    }
}
