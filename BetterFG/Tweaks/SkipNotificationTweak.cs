using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using FG.Common;
using FGClient.FallFeed;
using FallGuysLib.Players;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

namespace BetterFG.Tweaks
{
    public class SkipNotificationTweak : BfgTweak
    {
        public SkipNotificationTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "skip_notification";
        public override string TweakLabel => "More Fall Feed Notifications";
        public override bool DefaultEnabled => true;
        public override string TweakTooltip => "Adds Fall Feed notifications the game doesn't show: a player hitting the Explore skip button, a player leaving mid round, and a player respawning at a checkpoint.";

        public static SkipNotificationTweak Instance { get; private set; }
        void Awake() => Instance = this;

        static readonly Dictionary<string, (string Label, bool Replace)> _pendingNames = new Dictionary<string, (string, bool)>(StringComparer.OrdinalIgnoreCase);
        internal static bool HasPending => _pendingNames.Count > 0;

        public void Notify(string playerKey, GameMessageServerPlayerProgress progressMessage, string labelId, bool replaceBody = false)
        {
            if (!IsEnabled) return;
            if (progressMessage == null || string.IsNullOrEmpty(playerKey)) return;
            if (BetterFG.UI.BetterFGUIMan.Instance == null) return;
            string display = PlayerUtils.CleanPlayerName(playerKey);
            if (string.IsNullOrEmpty(display)) return;
            _pendingNames[display] = (labelId, replaceBody);
            Plugin.Log.LogInfo($"fall feed notif: pending += '{display}' as {labelId} (now {_pendingNames.Count})");
            BetterFG.UI.BetterFGUIMan.Instance.StartCoroutine(FireIfGameDidntEmit(display, progressMessage).WrapToIl2Cpp());
        }

        public void NotifyRespawn(FallGuysCharacterController character)
        {
            if (!IsEnabled || character == null) return;
            var netObject = character.NetObject;
            if (netObject == null) return;
            var npdc = PlayerUtils.GetClientPlayerManager()?.GetClientPlayerDataForNetId(netObject.NetID);
            if (npdc == null) return;
            Notify(npdc.playerKey, new GameMessageServerPlayerProgress { playerId = npdc.remotePlayerID }, "respawned", replaceBody: true);
        }

        internal static bool TryConsumeForRenderedName(string renderedNameText, out string matched, out string labelId, out bool replaceBody)
        {
            matched = null;
            labelId = null;
            replaceBody = false;
            if (string.IsNullOrEmpty(renderedNameText) || _pendingNames.Count == 0) return false;
            string bare = Regex.Replace(renderedNameText, "<[^>]*>", "").Trim();
            if (string.IsNullOrEmpty(bare)) return false;
            foreach (var p in _pendingNames)
            {
                if (bare.Equals(p.Key, StringComparison.OrdinalIgnoreCase))
                {
                    labelId = p.Value.Label;
                    replaceBody = p.Value.Replace;
                    _pendingNames.Remove(p.Key);
                    matched = p.Key;
                    return true;
                }
            }
            return false;
        }

        const string RespawnSprite = "bfg_respawn";
        const float RespawnSeconds = 1.5f;
        static TMP_SpriteAsset _respawnAsset;

        internal static void ShortenRespawn(FallFeedNotificationViewModel vm)
        {
            if (Time.time - vm._enableTime > 0.5f || !vm._messageBodyText.text.Contains(RespawnSprite)) return;
            vm._enableTime -= Mathf.Max(0f, vm._container.NotificationDuration - RespawnSeconds);
        }

        internal static string IconFor(string labelId, TMP_Text body)
        {
            if (labelId != "respawned") return "";
            if (_respawnAsset == null)
            {
                var tex = Utilities.EmbeddedResourceandUnity.LoadTexture("BetterFG.assets.ui.general.icon_respawn.png");
                int s = tex.width;
                var asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
                asset.name = "BettrFGFallFeedIcons";
                asset.version = "1.1.0";
                asset.spriteSheet = tex;
                asset.hashCode = TMP_TextUtilities.GetSimpleHashCode(asset.name);
                var mat = new Material(Shader.Find("TextMeshPro/Sprite")) { name = asset.name + "_Mat", mainTexture = tex };
                asset.material = mat;
                asset.materialHashCode = TMP_TextUtilities.GetSimpleHashCode(mat.name);
                asset.spriteInfoList = new Il2CppSystem.Collections.Generic.List<TMP_Sprite>();
                asset.fallbackSpriteAssets = new Il2CppSystem.Collections.Generic.List<TMP_SpriteAsset>();
                var glyph = new TMP_SpriteGlyph(0, new GlyphMetrics(s, s, 0f, s * 0.8f, s), new GlyphRect(0, 0, s, s), 1.2f, 0);
                var ch = new TMP_SpriteCharacter(0xE7F0, glyph) { name = RespawnSprite, glyphIndex = 0 };
                asset.spriteGlyphTable = new Il2CppSystem.Collections.Generic.List<TMP_SpriteGlyph>();
                asset.spriteGlyphTable.Add(glyph);
                asset.spriteCharacterTable = new Il2CppSystem.Collections.Generic.List<TMP_SpriteCharacter>();
                asset.spriteCharacterTable.Add(ch);
                asset.UpdateLookupTables();
                _respawnAsset = asset;
            }

            var cur = body.spriteAsset;
            if (cur == null) body.spriteAsset = _respawnAsset;
            else if (cur != _respawnAsset && (cur.fallbackSpriteAssets == null || !cur.fallbackSpriteAssets.Contains(_respawnAsset)))
            {
                if (cur.fallbackSpriteAssets == null) cur.fallbackSpriteAssets = new Il2CppSystem.Collections.Generic.List<TMP_SpriteAsset>();
                cur.fallbackSpriteAssets.Add(_respawnAsset);
                cur.UpdateLookupTables();
            }
            return $"<sprite name=\"{RespawnSprite}\"> ";
        }

        IEnumerator FireIfGameDidntEmit(string displayName, GameMessageServerPlayerProgress msg)
        {
            for (int i = 0; i < 8; i++) yield return null;
            if (!_pendingNames.ContainsKey(displayName)) yield break;
            ForceEmit(displayName, msg);
        }

        static FallFeedQualifyEliminateHandler _handler;

        static void ForceEmit(string displayName, GameMessageServerPlayerProgress msg)
        {
            try
            {
                if (_handler is null || _handler.m_CachedPtr == IntPtr.Zero)
                    _handler = UnityEngine.Object.FindObjectOfType<FallFeedQualifyEliminateHandler>();
                var handler = _handler;
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
                if (!SkipNotificationTweak.TryConsumeForRenderedName(nameText.text, out string matched, out string labelId, out bool replaceBody)) return;
                string label = labelId;
                if (curBody.Contains(label)) return;
                label = SkipNotificationTweak.IconFor(labelId, body) + label;
                body.text = replaceBody ? label : curBody + " " + label;
                body.ForceMeshUpdate();
                Plugin.Log.LogInfo($"fall feed append: '{matched}' -> '{body.text}'");
            }
            catch (Exception ex) { Plugin.Log.LogWarning("fall feed append: " + ex.Message); }
        }
    }
}
