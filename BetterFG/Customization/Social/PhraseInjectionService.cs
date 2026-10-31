using System;
using System.Collections.Generic;
using System.IO;
using Character;
using FG.Common.Character;
using FG.Common.CMS;
using FG.Common.Definition;
using FGClient;
using FGClient.Customiser;
using MPG.Utility;
using UnityEngine;
using SysSlotDict = System.Collections.Generic.Dictionary<int, int>; // slot index → original speechId

namespace BetterFG.Customization.Social
{
    public static class PhraseInjectionService
    {
        // slot index → original speechId, so we can always restore
        private static readonly SysSlotDict OriginalSlots = new SysSlotDict();

        // originalSpeechId → customSpeechId, for the play patch
        internal static readonly Dictionary<int, int> Remap = new Dictionary<int, int>();

        // customSpeechId → entry id, so the play patch can read the entry's *current* sounds live
        // (re-reading the entry means in-game sound edits take effect without needing Apply All)
        internal static readonly Dictionary<int, string> SoundOwner = new Dictionary<int, string>();

        private static List<PhraseEntry> _entries = new List<PhraseEntry>();

        public static string LastStatus { get; private set; } = "";

        // ── Public API ────────────────────────────────────────────────────────

        public static void SetEntries(List<PhraseEntry> entries) => _entries = entries;

        /// <summary>
        /// Full apply — rebuilds every option fresh from current entry text.
        /// Safe to call as many times as you want.
        /// </summary>
        public static void ApplyAll(List<PhraseEntry> entries)
        {
            _entries = entries;
            ApplyToWheel(null);
        }

        // called by DisplayWheel patch
        internal static void ReapplyToWheel(SocialPrimeHandler primeHandler) => ApplyToWheel(primeHandler);

        internal static void PlayCustomSound(int speechId, GameObject bean)
        {
            if (!SoundOwner.TryGetValue(speechId, out string ownerId)) return;
            PhraseEntry entry = null;
            foreach (var e in _entries) if (e.id == ownerId) { entry = e; break; }
            string path = SocialSound.Pick(entry?.soundPaths);
            if (path != null) SocialSound.Play(path, bean);
        }

        // ── Core ──────────────────────────────────────────────────────────────

        // handler is the specific wheel to inject into (our Social-tab clone when driven from the
        // DisplayWheel patch); null falls back to first found for the Apply All button.
        private static void ApplyToWheel(SocialPrimeHandler handler = null)
        {
            try
            {
                var speechMgr = SingletonBehaviour<SpeechOptionsManager>.Instance;
                if (speechMgr == null) { LastStatus = "Not in a round yet."; return; }

                var primeHandler = handler ?? UnityEngine.Object.FindObjectOfType<SocialPrimeHandler>();
                if (primeHandler == null) { LastStatus = "SocialPrimeHandler not found."; return; }

                var wheelDict = primeHandler.HighlightedSocialWheel?.SocialItemsDictionary;
                var wheelType = primeHandler._wheelType;
                if (wheelType.ToString() != "Phrases") { LastStatus = $"Open the PHRASES wheel first (current: {wheelType})."; return; }
                if (wheelDict == null || !wheelDict.ContainsKey(wheelType)) { LastStatus = "Phrases wheel not in dict."; return; }

                var slots = wheelDict[wheelType];
                var lookup = speechMgr._speechOptionsLookup;

                // ── Step 1: restore all slots to originals ─────────────────────
                foreach (var kvp in OriginalSlots)
                {
                    int si = kvp.Key;
                    int origId = kvp.Value;
                    if (si >= slots.Count) continue;
                    if (lookup.ContainsKey(origId))
                        slots[si] = lookup[origId];
                }
                OriginalSlots.Clear();
                Remap.Clear();
                SoundOwner.Clear();

                // find a reference phrase option to clone metadata from
                ImageSpeechOption refOpt = SpeechOptionBuilder.FindReferencePhraseOption();
                if (refOpt == null) { LastStatus = "No reference phrase found in lookup."; return; }

                // ── Step 2: inject enabled entries, always rebuilding fresh ────
                int injected = 0;
                foreach (var e in _entries)
                {
                    if (!e.enabled) continue;

                    int slotIndex = Math.Max(0, Math.Min(e.slot, slots.Count - 1));

                    // grab and store original before we overwrite
                    int originalId = slots[slotIndex]?.Cast<SocialOption>()?._speechId ?? -1;
                    if (!OriginalSlots.ContainsKey(slotIndex))
                        OriginalSlots[slotIndex] = originalId;

                    // always build a fresh option so text/id changes take effect immediately
                    int customId = 50000 + slotIndex * 100 + injected;

                    string optId = string.IsNullOrEmpty(e.phraseId) ? $"bfg_phrase_{e.id}" : e.phraseId;
                    var opt = SpeechOptionBuilder.Build(optId, e.phraseText, refOpt);
                    opt._speechId = customId;

                    // custom image — cached sprite, rebuilt only when the file changes
                    if (SocialSpriteCache.TryGet(e.imagePath, out var customSprite, out var cacheableSprite))
                    {
                        opt._sprite = customSprite;
                        opt._cachedAtlasSprite = cacheableSprite;
                    }

                    // overwrite in lookup so play patch can find it
                    lookup[customId] = opt;

                    slots[slotIndex] = opt;

                    bool hasSound = false;
                    if (e.soundPaths != null)
                        foreach (string s in e.soundPaths)
                            if (!string.IsNullOrEmpty(s)) { hasSound = true; break; }
                    if (hasSound) SoundOwner[customId] = e.id;
                    if (originalId >= 0) Remap[originalId] = customId;

                    injected++;
                }

                LastStatus = injected > 0
                    ? $"Applied {injected} phrase(s)."
                    : "No enabled phrases.";
            }
            catch (Exception ex)
            {
                LastStatus = $"Error: {ex.Message}";
                Plugin.Log.LogError($"PhraseInjection: {ex.Message}");
            }
        }

    }
}