using System;
using System.Collections.Generic;
using FG.Common;
using FGClient;

namespace BetterFG.Services
{
    public static class PartyRoundStatus
    {
        private const string Key = "bfg.round";
        private const string Prefix = "bfgr1:";
        private const long MaxAgeSeconds = 900;

        private static readonly HashSet<IntPtr> _overwritten = new();

        public static void Publish()
        {
            int round = GlobalGameStateClient.Instance.RoundCounterInPlaySession;
            if (round > 0) Send($"{Prefix}{round}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
        }

        public static void Clear() => Send("");

        private static void Send(string value)
        {
            var psm = PartyStateManager.Instance;
            if (psm == null || !psm.IsInParty()) return;
            psm.SetStringPartyMemberData(Key, value);
            Plugin.Log.LogInfo($"party round attr -> '{value}'");
        }

        public static void Apply(PartyNameTag tag, IPartyMember member)
        {
            int round = member.GameState == PartyService.MemberGameState.Playing && !member.IsLocal ? ReadRound(member.Id) : 0;
            if (round > 0)
            {
                tag._statusText.text = LocalizationService.Format("party_tag.round_fmt", round);
                _overwritten.Add(tag.Pointer);
            }
            else if (_overwritten.Remove(tag.Pointer) && member.GameState == PartyService.MemberGameState.Playing)
                tag._statusText.text = tag._localisedStrings.GetString("party_playing_text");
        }

        private static int ReadRound(CrossPlatformMemberID id)
        {
            string raw = PartyStateManager.Instance?.GetStringPartyMemberData(id, Key);
            if (raw == null || !raw.StartsWith(Prefix, StringComparison.Ordinal)) return 0;
            var parts = raw.Substring(Prefix.Length).Split(':');
            if (parts.Length != 2
                || !int.TryParse(parts[0], out int round) || round < 1 || round > 99
                || !long.TryParse(parts[1], out long stamp)
                || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - stamp) > MaxAgeSeconds) return 0;
            return round;
        }
    }
}
