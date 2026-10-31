using System;
using System.Collections.Generic;
using BetterFG.Customization.Social;
using Character;
using FG.Common.Character;
using FGClient;
using MPG.Utility;
using UnityEngine;

namespace BetterFG.Customization.Pets
{
    // pops a phrase above the pet's head using the game's own speech-bubble system instead of a
    // hand-rolled canvas. reuses PhraseEntry (image + up to 3 sounds) - same shape the player's own
    // Social > Phrases already edits (EmoticonsPhrasesTab/PhraseSettingsService), just registered
    // under the pet's own ids instead of a wheel slot.
    //
    // UpdateRemoteSpeech alone doesn't drive the state machine for a synthetic bean - the working
    // sequence is: stamp the option id straight onto MotorFunctionSpeech, then force the active
    // state to (re)Begin so it actually reads that id and shows the bubble, same as
    // MotorFunctionSpeechStateActive.Begin(prevState) does when the real state-change flow enters it.
    public class PetSpeechComponent : MonoBehaviour
    {
        public PetSpeechComponent(IntPtr ptr) : base(ptr) { }

        public List<PhraseEntry> Phrases = new List<PhraseEntry>();
        public float IntervalMin = 15f, IntervalMax = 45f;
        public string PetId = "";

        static readonly System.Random Rng = new System.Random();

        readonly List<int> _speechIds = new List<int>();
        float _timer;
        bool _built;

        void Awake()
        {
            _timer = UnityEngine.Random.Range(IntervalMin, IntervalMax);
            BuildOptions();
        }

        // called whenever the phrase list/timing is edited on an already-live pet - rebuilds just
        // the registered options and timer, no bean touched, no respawn
        public void Rebuild(List<PhraseEntry> phrases, float intervalMin, float intervalMax)
        {
            Phrases = phrases != null ? new List<PhraseEntry>(phrases) : new List<PhraseEntry>();
            IntervalMin = intervalMin;
            IntervalMax = intervalMax;
            _speechIds.Clear();
            _built = false;
            BuildOptions();
        }

        // builds one synthetic SocialOption per enabled phrase (with its custom image, if any) and
        // registers it into the speech lookup so the trigger in Update() can find it by id - same
        // construction PhraseInjectionService/RemoteSocialDisplay already use for the local player's
        // phrase wheel / other players' shared phrases, just a different id range and no wheel slot
        void BuildOptions()
        {
            if (Phrases == null || Phrases.Count == 0) return;

            var speechMgr = SingletonBehaviour<SpeechOptionsManager>.Instance;
            var lookup = speechMgr?._speechOptionsLookup;
            if (lookup == null) { Plugin.Log.LogWarning("pet speech: no SpeechOptionsManager yet, phrases won't show"); return; }

            var refOpt = SpeechOptionBuilder.FindReferencePhraseOption();
            if (refOpt == null) { Plugin.Log.LogWarning("pet speech: no reference phrase option found, phrases won't show"); return; }

            int idBase = 70000 + Math.Abs(PetId.GetHashCode() % 1000) * 100;
            for (int i = 0; i < Phrases.Count; i++)
            {
                var e = Phrases[i];
                if (!e.enabled) continue;

                int id = idBase + i;
                var opt = SpeechOptionBuilder.Build($"bfg_pet_phrase_{PetId}_{i}", e.phraseText, refOpt);
                opt._speechId = id;
                if (SocialSpriteCache.TryGet(e.imagePath, out var customSprite, out var cacheableSprite))
                {
                    opt._sprite = customSprite;
                    opt._cachedAtlasSprite = cacheableSprite;
                }
                lookup[id] = opt;
                _speechIds.Add(id);
            }
            _built = true;
        }

        void Update()
        {
            if (!_built || _speechIds.Count == 0) return;

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = UnityEngine.Random.Range(IntervalMin, IntervalMax);

            var fgcc = GetComponent<FallGuysCharacterController>();
            var speechFn = fgcc != null ? fgcc.SpeechMotorFunction : null;
            if (speechFn == null) { Plugin.Log.LogWarning("pet speech: this bean has no SpeechMotorFunction, can't show a bubble"); return; }

            int idx = Rng.Next(_speechIds.Count);
            int id = _speechIds[idx];
            var entry = Phrases[idx];
            Plugin.Log.LogInfo($"pet speech: playing {id} ({entry.phraseText})");

            try
            {
                speechFn._currentSpeechOptionId = id;

                // activeStateID indexes into whatever OriginalStates array THIS bean's
                // MotorAgentConfiguration built - a synthetic bean's state list isn't guaranteed to
                // be the same shape/length as a real player's, so activeStateID can point past the
                // end of it. search for the MotorFunctionSpeechStateActive entry instead of trusting
                // that index.
                var states = speechFn.OriginalStates;
                MotorFunctionSpeechStateActive activeState = null;
                if (states != null)
                {
                    for (int i = 0; i < states.Length; i++)
                    {
                        var s = states[i]?.TryCast<MotorFunctionSpeechStateActive>();
                        if (s != null) { activeState = s; break; }
                    }
                }
                if (activeState == null) { Plugin.Log.LogWarning($"pet speech: no MotorFunctionSpeechStateActive in OriginalStates (length {states?.Length ?? -1})"); return; }
                activeState.Begin(-1);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"pet speech: couldn't trigger the bubble: {ex.Message}"); }
        }
    }
}
