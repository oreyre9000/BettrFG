using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Features.TimePlacement;

namespace BetterFG.Tweaks
{
    // stamps qualification fallfeeds (the ones with the fallfeed-race sprite) with the time the
    // player qualified at — the server's qualifyTime, captured by FeatureTimePlacement, so the stamp
    // matches the in-game leaderboard's time column exactly. driven from FallFeedNamePatch's postfix
    // on FallFeedNotificationViewModel.ShowMessage so we don't add another patch, but all the
    // behaviour lives here. writes straight to the rendered TMP text — MessageBody on the message
    // struct is a copy by the time ShowMessage returns, editing it does nothing.
    public class FallFeedQualTimeTweak : BfgTweak
    {
        public FallFeedQualTimeTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "fallfeed_qual_time";
        public override string TweakLabel => "tweak.fall_feed_qualification_time";
        public override bool DefaultEnabled => false;

        public static FallFeedQualTimeTweak Instance { get; private set; }
        void Awake() => Instance = this;

        // the qualify-message filter, and the insertion point for the stamp. confirmed live post-update:
        // rows read "fallfeed-race" or "fallfeed-race-first", so the prefix covers both. "has a stored
        // qual time" is NOT a usable signal on its own, a phrase row from someone who qualifies a beat
        // later gets the time stamped onto it instead of their real qualify row.
        const string RaceSprite = "fallfeed-race";
        const string TimeColor = "#FFFF00"; // pure yellow

        static bool IsQualifyRow(string body)
            => !string.IsNullOrEmpty(body) && body.IndexOf(RaceSprite, StringComparison.OrdinalIgnoreCase) >= 0;

        // a qualified player's time sits in FeatureTimePlacement for the REST of the round, so
        // "has a stored qual time" isn't a one-shot qualify signal by itself — a later, unrelated
        // fall-feed row about the same already-qualified player (e.g. a chat-disabled notice) would
        // still hit it and get the qualify time wrongly stamped onto it. one stamp per player per
        // round fixes that; cleared alongside FeatureTimePlacement's own round reset.
        readonly HashSet<string> _stampedKeys = new HashSet<string>();

        internal void ResetStampedKeys() => _stampedKeys.Clear();

        public void Apply(TMPro.TextMeshProUGUI messageBodyText, string primaryPlayerKey)
        {
            if (!IsEnabled) { Plugin.Log.LogInfo("ff  qt: tweak off"); return; }
            if (messageBodyText == null) { Plugin.Log.LogInfo("ff  qt: no body tmp"); return; }
            try
            {
                string cur = messageBodyText.text;
                if (string.IsNullOrEmpty(cur) || !IsQualifyRow(cur)) return;
                Plugin.Log.LogInfo($"ff  qt: key='{primaryPlayerKey}' body='{cur}' stored=[{FeatureTimePlacement.DumpQualTimeKeys()}]");
                if (cur.Contains(TimeColor)) { Plugin.Log.LogInfo("ff  qt: already stamped"); return; }
                if (string.IsNullOrEmpty(primaryPlayerKey)) { Plugin.Log.LogInfo("ff  qt: key empty"); return; }
                if (_stampedKeys.Contains(primaryPlayerKey)) { Plugin.Log.LogInfo("ff  qt: key already stamped this round"); return; }

                // server qualifyTime for this fallfeed's player, captured by FeatureTimePlacement. the
                // row is created off the unspawn message, which lands a frame or two BEFORE the
                // HandleServerPlayerProgress message that actually stores the qual time - so a miss
                // here isn't "no time exists", it's "not yet". retry for a couple seconds instead of
                // giving up on the one shot we get.
                if (!TryGetQualTime(primaryPlayerKey, out string qualTime))
                {
                    StartCoroutine(RetryStamp(messageBodyText, primaryPlayerKey).WrapToIl2Cpp());
                    return;
                }
                _stampedKeys.Add(primaryPlayerKey);
                Stamp(messageBodyText, cur, qualTime);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("FallFeed: qualtime " + ex.Message);
            }
        }

        static void Stamp(TMPro.TextMeshProUGUI messageBodyText, string cur, string qualTime)
        {
            string stamp = $" <color={TimeColor}>{qualTime}</color>";
            int idx = cur.IndexOf("<sprite name=\"" + RaceSprite, StringComparison.OrdinalIgnoreCase);
            messageBodyText.text = idx >= 0 ? cur.Insert(idx, stamp + " ") : cur + stamp;
        }

        IEnumerator RetryStamp(TMPro.TextMeshProUGUI messageBodyText, string primaryPlayerKey)
        {
            for (int i = 0; i < 120 && messageBodyText != null; i++)
            {
                yield return null;
                string cur = messageBodyText.text;
                if (!IsQualifyRow(cur)) yield break;
                if (!TryGetQualTime(primaryPlayerKey, out string qualTime)) continue;
                _stampedKeys.Add(primaryPlayerKey);
                if (!cur.Contains(TimeColor)) Stamp(messageBodyText, cur, qualTime);
                yield break;
            }
        }

        // the server qualifyTime FeatureTimePlacement captured for THIS fallfeed's primary player,
        // formatted mm:ss:ms to match the leaderboard's time column. false if we have no stored time.
        static bool TryGetQualTime(string key, out string formatted)
        {
            formatted = null;
            if (string.IsNullOrEmpty(key)) return false;
            if (!FeatureTimePlacement.TryGetQualTime(key, out float seconds)) return false;

            TimeSpan t = TimeSpan.FromSeconds(seconds);
            formatted = string.Format("{0:D2}:{1:D2}:{2:D3}", t.Minutes, t.Seconds, t.Milliseconds);
            return true;
        }
    }
}
