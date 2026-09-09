using System;
using System.Collections.Generic;
using BetterFG.Customization.Player;
using BetterFG.Nametag;
using BetterFG.Services;
using FallGuysLib.Players;
using UnityEngine;

namespace BetterFG.Network
{
    // the live bean-name -> profile map for a round. profiles come in from .bfgprofile files and
    // debug_profiles.json; whoever asks "what is this player wearing" asks here.
    public static class RemoteProfileStore
    {
        private static readonly Dictionary<string, BfgProfile> _byKey
            = new Dictionary<string, BfgProfile>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<BfgProfile> _pending = new List<BfgProfile>();

        public static BfgProfile LocalLoadout()
        {
            var skins = BfgProfile.ReadLoadout(SettingsService.Get);
            return skins.Count > 0 ? new BfgProfile { skins = skins } : null;
        }

        public static Dictionary<string, int> LocalHandOverrides()
            => BfgProfile.ReadHandOverrides(SettingsService.Get("skin.hand.overrides", ""));

        public static string LocalAppliedSummary(SkinRepo repo)
        {
            if (repo == null) return null;
            var local = LocalLoadout();
            if (local == null) return null;

            int costumes = 0, accessories = 0, items = 0, plinths = 0, emotes = 0, others = 0;
            foreach (var e in local.skins)
            {
                if (string.IsNullOrEmpty(e.repoUrl)) continue;
                if (!string.Equals(e.repoUrl, repo.githubUrl, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(e.repoUrl, repo.RawBase, StringComparison.OrdinalIgnoreCase)) continue;

                switch (SkinTypeParser.FromString(e.type))
                {
                    case SkinType.Costume: costumes++; break;
                    case SkinType.Accessory: accessories++; break;
                    case SkinType.Item: items++; break;
                    case SkinType.Plinth: plinths++; break;
                    case SkinType.Emote: emotes++; break;
                    default: others++; break;
                }
            }

            var parts = new List<string>();
            if (costumes > 0) parts.Add(costumes + (costumes == 1 ? " costume" : " costumes"));
            if (accessories > 0) parts.Add(accessories + (accessories == 1 ? " accessory" : " accessories"));
            if (items > 0) parts.Add(items + (items == 1 ? " item" : " items"));
            if (plinths > 0) parts.Add(plinths + (plinths == 1 ? " plinth" : " plinths"));
            if (emotes > 0) parts.Add(emotes + (emotes == 1 ? " emote" : " emotes"));
            if (others > 0) parts.Add(others + (others == 1 ? " other" : " others"));

            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        public static void Clear()
        {
            _byKey.Clear();
            _pending.Clear();
        }

        public static void Register(BfgProfile profile, string playerKey = null)
        {
            string key = !string.IsNullOrEmpty(playerKey) ? playerKey : profile.username;
            if (string.IsNullOrEmpty(key)) { _pending.Add(profile); return; }

            Add(profile, key);
            foreach (var alias in profile.AliasKeys()) _byKey[alias] = profile;
            profile.resolvedPlayerKey = key;
            NametagPatchHub.RefreshRemoteNametags();
        }

        private static void Add(BfgProfile profile, string key)
        {
            _byKey[key] = profile;
            string clean = PlayerUtils.CleanPlayerName(key);
            if (!string.IsNullOrEmpty(clean))
                _byKey[clean] = profile;
        }

        public static bool IsEmpty => _byKey.Count == 0;

        /// <summary>
        /// Looks up by full key (e.g. xb1_oreyre9000) or clean name (e.g. oreyre9000).
        /// </summary>
        public static BfgProfile TryGet(string key)
        {
            if (_byKey.Count == 0 || string.IsNullOrEmpty(key)) return null;
            if (_byKey.TryGetValue(key, out var p)) return p;
            string cleaned = PlayerUtils.CleanPlayerName(key);
            if (cleaned != key && _byKey.TryGetValue(cleaned, out var pc)) return pc;
            foreach (var kv in _byKey)
                if (PlayerUtils.CleanPlayerName(kv.Key).Equals(cleaned, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            return null;
        }

        public static void ResolvePending(GameObject localBean)
        {
            if (_pending.Count == 0) return;

            try
            {
                var cpm = PlayerUtils.GetClientPlayerManager();
                if (cpm?._playerIdIndex == null) return;

                var remotes = new List<(uint id, string key)>();
                foreach (var kvp in cpm._playerIdIndex)
                {
                    var go = kvp.Value?.fgcc?.gameObject;
                    if (go == null || go == localBean) continue;
                    remotes.Add((kvp.Key, kvp.Value.playerKey ?? ""));
                }
                remotes.Sort((a, b) => a.id.CompareTo(b.id));

                for (int i = 0; i < _pending.Count && i < remotes.Count; i++)
                {
                    var profile = _pending[i];

                    if (profile.playerID != 0)
                    {
                        bool matched = false;
                        foreach (var r in remotes)
                        {
                            if (r.id != profile.playerID) continue;
                            Add(profile, r.key);
                            profile.resolvedPlayerKey = r.key;
                            matched = true;
                            break;
                        }
                        if (matched) continue;
                    }

                    string fullKey = remotes[i].key;
                    if (!string.IsNullOrEmpty(fullKey))
                    {
                        Add(profile, fullKey);
                        profile.resolvedPlayerKey = fullKey;
                        Plugin.Log.LogInfo($"positional match profile[{i}] -> {fullKey}");
                    }
                }

                _pending.Clear();
                NametagPatchHub.RefreshRemoteNametags();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("ResolvePending: " + ex.Message);
            }
        }
    }
}
