using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Features.TimePlacement;

namespace BetterFG.Tweaks
{
    public class FallFeedQualTimeTweak : BfgTweak
    {
        public FallFeedQualTimeTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "fallfeed_qual_time";
        public override string TweakLabel => "Fall Feed Qualification Time";
        public override bool DefaultEnabled => false;

        public static FallFeedQualTimeTweak Instance { get; private set; }
        void Awake() => Instance = this;

        const string RaceSprite = "fallfeed-race";
        const string TimeColor = "#FFFF00";

        readonly HashSet<string> _stampedKeys = new HashSet<string>();

        internal void ResetStampedKeys() => _stampedKeys.Clear();

        static bool IsQualifyRow(string body)
            => !string.IsNullOrEmpty(body) && body.IndexOf(RaceSprite, StringComparison.OrdinalIgnoreCase) >= 0;

        public void Apply(TMPro.TextMeshProUGUI messageBodyText, string primaryPlayerKey)
        {
            if (!IsEnabled || messageBodyText == null) return;
            try
            {
                string cur = messageBodyText.text;
                if (string.IsNullOrEmpty(cur) || !IsQualifyRow(cur)) return;
                if (cur.Contains(TimeColor)) return;
                if (string.IsNullOrEmpty(primaryPlayerKey) || _stampedKeys.Contains(primaryPlayerKey)) return;

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

        static bool TryGetQualTime(string key, out string formatted)
        {
            formatted = null;
            if (!FeatureTimePlacement.TryGetQualTime(key, out float seconds)) return false;
            TimeSpan t = TimeSpan.FromSeconds(seconds);
            formatted = string.Format("{0:D2}:{1:D2}:{2:D3}", t.Minutes, t.Seconds, t.Milliseconds);
            return true;
        }
    }
}
