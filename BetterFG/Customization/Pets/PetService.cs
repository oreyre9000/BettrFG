using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using FallGuysLib.Round;
using FG.Common;
using UnityEngine;

namespace BetterFG.Customization.Pets
{
    public class PetService : MonoBehaviour
    {
        public PetService(IntPtr ptr) : base(ptr) { }

        public static PetService Instance { get; private set; }

        public List<PetData> Pets = new List<PetData>();

        // every currently-out pet bean, keyed by pet id
        readonly Dictionary<string, GameObject> _livePets = new Dictionary<string, GameObject>();
        // fgcc pointers we've handed out or been told about - used to identify a bean as ours even
        // during the spawn window before the GameObject lands in _livePets
        readonly HashSet<IntPtr> _petFgccPtrs = new HashSet<IntPtr>();
        readonly HashSet<string> _pendingNames = new HashSet<string>();
        readonly HashSet<string> _spawning = new HashSet<string>();

        GameObject _lastOwner;
        float _reviveTimer;

        // armed at HandleServerStartRound, released a frame after OnPlayingStarted - PetFollowComponent
        // pins velocity to zero the whole time so the round's drop-in reconciliation can't fling a pet
        public bool FrozenForRoundStart { get; private set; }

        // first live pet's fgcc - the single-pet consumers (replay recording, grab block) only ever
        // needed "a" pet, not all of them
        public FallGuysCharacterController LiveFgcc
        {
            get
            {
                foreach (var kv in _livePets)
                    if (kv.Value != null)
                    {
                        var f = kv.Value.GetComponent<FallGuysCharacterController>();
                        if (f != null) return f;
                    }
                return null;
            }
        }

        public IEnumerable<GameObject> LivePetObjects
        {
            get { foreach (var kv in _livePets) if (kv.Value != null) yield return kv.Value; }
        }

        // every live pet's fgcc - PetMotorPatches pumps each one, 4x a frame. cached instead of
        // GetComponent'd fresh every call (that was 4 IL2Cpp GetComponent calls per pet per frame,
        // just to re-fetch a reference that never changes across a pet's lifetime) - kept in sync
        // in SpawnOne/DespawnOne/DespawnAll below.
        readonly List<FallGuysCharacterController> _liveFgccCache = new List<FallGuysCharacterController>();
        // DespawnOne only untracks a pet it destroyed itself - if the game's own network
        // reconciliation kills the bean first (the Update() revive-timer comment below), the fgcc
        // stays in this cache pointing at a destroyed native object, and PetMotorPatches pumping it
        // every frame is what threw the CalculateFloorRaycastOrigin/UpdateGravity NREs. prune here so
        // every caller gets a clean list regardless of how the bean died.
        public List<FallGuysCharacterController> LiveFgccs
        {
            get
            {
                for (int i = _liveFgccCache.Count - 1; i >= 0; i--)
                    if (_liveFgccCache[i] is null || _liveFgccCache[i].m_CachedPtr == IntPtr.Zero) _liveFgccCache.RemoveAt(i);
                return _liveFgccCache;
            }
        }

        void Awake()
        {
            Instance = this;
            Pets = PetStore.Load();
            RoundEvents.OnPlayingStarted += OnPlayingStarted;
        }

        // HandleServerStartRoundPa.Postfix calls this - freeze pets through the round-start turbulence
        public static void OnRoundStart()
        {
            if (Instance != null) Instance.FrozenForRoundStart = true;
        }

        void OnPlayingStarted()
        {
            // the game re-enables the upper body ragdoll on real round entry, after our spawn-time
            // disable already ran - one frame late so it lands after whatever the game just did.
            // same coroutine lifts the round-start freeze a frame after "playing" is truly live.
            StartCoroutine(AfterPlayingStarted().WrapToIl2Cpp());
        }

        IEnumerator AfterPlayingStarted()
        {
            yield return null;
            foreach (var f in LiveFgccs)
                if (f._ragdollController != null) f._ragdollController._upperBodyEnabled = false;
            FrozenForRoundStart = false;
        }

        void Update()
        {
            var owner = BeanMonitorService.LocalPlayerBean;
            if (owner != _lastOwner)
            {
                _lastOwner = owner;
                DespawnAll();
                // leaving to the menu - drop any stale freeze so a menu-spawned pet isn't stuck
                if (owner == null) FrozenForRoundStart = false;
                else
                    foreach (var p in EquippedPets()) SpawnOne(p);
                return;
            }

            // safety net: the game's own network reconciliation occasionally unspawns a synthetic NPC
            // bean mid-round - bring back any equipped pet that's gone missing. throttled so a spawn
            // still mid-flight (costume download) doesn't get stomped by a second attempt.
            if (owner == null) return;
            _reviveTimer -= Time.deltaTime;
            if (_reviveTimer > 0f) return;
            _reviveTimer = 1f;

            foreach (var p in EquippedPets())
            {
                if (_spawning.Contains(p.id)) continue;
                if (_livePets.TryGetValue(p.id, out var go) && go != null) continue;
                Plugin.Log.LogInfo($"pet '{p.name}' vanished on its own, respawning it");
                SpawnOne(p);
            }
        }

        public IEnumerable<PetData> EquippedPets()
        {
            var ids = PetStore.ActivePetIds;
            foreach (var p in Pets)
                if (ids.Contains(p.id)) yield return p;
        }

        // first equipped pet - kept for the replay recorder
        public PetData ActivePet()
        {
            foreach (var p in EquippedPets()) return p;
            return null;
        }

        public bool IsEquipped(string id) => !string.IsNullOrEmpty(id) && PetStore.ActivePetIds.Contains(id);

        public void ToggleEquipped(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            var ids = PetStore.ActivePetIds;
            if (ids.Remove(id))
            {
                PetStore.ActivePetIds = ids;
                DespawnOne(id);
                return;
            }
            ids.Add(id);
            PetStore.ActivePetIds = ids;
            var data = Pets.Find(p => p.id == id);
            if (data != null && BeanMonitorService.LocalPlayerBean != null) SpawnOne(data);
        }

        public void SavePet(PetData data, bool respawnIfActive = true)
        {
            int idx = Pets.FindIndex(p => p.id == data.id);
            if (idx >= 0) Pets[idx] = data;
            else Pets.Add(data);
            PetStore.Save(Pets);
            PetThumb.Invalidate(data.id);

            if (!IsEquipped(data.id)) return;

            if (respawnIfActive)
            {
                DespawnOne(data.id);
                if (BeanMonitorService.LocalPlayerBean != null) SpawnOne(data);
            }
            else SyncLiveSpeech(data);
        }

        void SyncLiveSpeech(PetData data)
        {
            if (!_livePets.TryGetValue(data.id, out var live) || live == null) return;

            bool wantsSpeech = data.phrases != null && data.phrases.Count > 0;
            var speechComp = live.GetComponent<PetSpeechComponent>();
            if (!wantsSpeech) { if (speechComp != null) Destroy(speechComp); return; }

            if (speechComp == null) speechComp = live.AddComponent<PetSpeechComponent>();
            speechComp.PetId = data.id;
            speechComp.Rebuild(data.phrases, data.phraseIntervalMin, data.phraseIntervalMax);
        }

        public void Reload()
        {
            DespawnAll();
            Pets = PetStore.Load();
            if (BeanMonitorService.LocalPlayerBean != null)
                foreach (var p in EquippedPets()) SpawnOne(p);
        }

        public void RemovePet(string id)
        {
            Pets.RemoveAll(p => p.id == id);
            PetStore.Save(Pets);
            var ids = PetStore.ActivePetIds;
            if (ids.Remove(id)) PetStore.ActivePetIds = ids;
            DespawnOne(id);
            PetThumb.Invalidate(id);
        }

        void SpawnOne(PetData data)
        {
            DespawnOne(data.id);
            _spawning.Add(data.id);
            // SpawnBeanUtils raises SpawnPlayerTagEvent before the build coroutine hands us the fgcc -
            // MatchesPendingPet covers that window by the spawn key (carries the pet name)
            _pendingNames.Add(data.name);
            StartCoroutine(PetBeanBuilder.Build(data, bean =>
            {
                _spawning.Remove(data.id);
                _pendingNames.Remove(data.name);
                if (bean == null) { Plugin.Log.LogWarning($"pet '{data.name}' spawn failed, no bean came back"); return; }
                var follow = bean.AddComponent<PetFollowComponent>();
                follow.SlotIndex = _livePets.Count; // pets already out = this one's formation slot
                var ownerBean = BeanMonitorService.LocalPlayerBean;
                if (ownerBean != null)
                    bean.transform.position = PetFollowComponent.FormationSpot(ownerBean.transform, follow.SlotIndex);
                if (data.phrases != null && data.phrases.Count > 0)
                {
                    var speechComp = bean.AddComponent<PetSpeechComponent>();
                    speechComp.PetId = data.id;
                    speechComp.Rebuild(data.phrases, data.phraseIntervalMin, data.phraseIntervalMax);
                }
                _livePets[data.id] = bean;
                var f = bean.GetComponent<FallGuysCharacterController>();
                if (f != null) { _petFgccPtrs.Add(f.m_CachedPtr); _liveFgccCache.Add(f); }
                Plugin.Log.LogInfo($"pet '{data.name}' spawned, following you around now ({_livePets.Count} out)");
            }).WrapToIl2Cpp());
        }

        // RemotePetService rides the same pending-name window while a remote pet's spawn tag event
        // fires ahead of its build coroutine handing back the fgcc - same race MatchesPendingPet
        // covers for your own pets.
        internal void AddPendingName(string name) => _pendingNames.Add(name);
        internal void RemovePendingName(string name) => _pendingNames.Remove(name);

        internal void RegisterLiveFgcc(FallGuysCharacterController fgcc)
        {
            if (fgcc == null || fgcc.m_CachedPtr == IntPtr.Zero) return;
            _petFgccPtrs.Add(fgcc.m_CachedPtr);
            fgcc.VFXController.enabled = false;
            Patches.PetAudioMutePatch.Set(true);
        }

        // must be called AFTER the caller's own network unspawn, not before - PetUnspawnGuardPatch
        // only skips the game's broken unspawn handler for a pet while its pointer is still in this set
        internal void UnregisterLiveFgcc(FallGuysCharacterController fgcc)
        {
            if (fgcc != null && fgcc.m_CachedPtr != IntPtr.Zero) _petFgccPtrs.Remove(fgcc.m_CachedPtr);
            _liveFgccCache.Remove(fgcc);
            Patches.PetAudioMutePatch.Set(_petFgccPtrs.Count > 0);
        }

        internal void PumpRemoteFgcc(FallGuysCharacterController fgcc) => _liveFgccCache.Add(fgcc);

        public static bool IsPetFgcc(FallGuysCharacterController fgcc)
        {
            var inst = Instance;
            return inst != null && fgcc != null && fgcc.m_CachedPtr != IntPtr.Zero
                && inst._petFgccPtrs.Contains(fgcc.m_CachedPtr);
        }

        readonly Dictionary<IntPtr, float> _emoteAudioUntil = new Dictionary<IntPtr, float>();

        internal static void AllowEmoteAudio(FallGuysCharacterController fgcc, float seconds)
        {
            if (Instance != null) Instance._emoteAudioUntil[fgcc.m_CachedPtr] = Time.time + seconds;
        }

        public static bool IsMutedPet(FallGuysCharacterController fgcc) =>
            IsPetFgcc(fgcc) && !(Instance._emoteAudioUntil.TryGetValue(fgcc.m_CachedPtr, out float until) && Time.time < until);

        public static bool MatchesPendingPet(string playerKey)
        {
            var inst = Instance;
            if (inst == null || string.IsNullOrEmpty(playerKey)) return false;
            foreach (var n in inst._pendingNames)
                if (playerKey == n || playerKey.EndsWith("_" + n, StringComparison.Ordinal)) return true;
            return false;
        }

        void DespawnAll()
        {
            var ids = new List<string>(_livePets.Keys);
            foreach (var id in ids) DespawnOne(id);
        }

        void DespawnOne(string id)
        {
            if (_livePets.TryGetValue(id, out var live) && live != null)
            {
                var old = live.GetComponent<PetFollowComponent>();
                if (old != null) old.enabled = false;

                var f = live.GetComponent<FallGuysCharacterController>();

                // this bean went through the real networked spawn path and is registered in the
                // client's net object table - plain Destroy() leaves that entry dangling and the next
                // unspawn message NREs walking the table (that softlocked the game mid-round). tear it
                // down through the game's OWN unspawn path; policy Destroy kills the GameObject too.
                // UnspawnNetObject synchronously fires _handleClientUnspawnNetObject, which
                // PetUnspawnGuardPatch skips only while this fgcc's pointer is still in _petFgccPtrs -
                // so the pointer must come out AFTER the unspawn call, not before.
                if (!FallGuysLib.Players.BeanNetworkUtils.TryNetworkUnspawn(live)) Destroy(live);

                if (f != null) { _petFgccPtrs.Remove(f.m_CachedPtr); _liveFgccCache.Remove(f); }
            }
            _livePets.Remove(id);
        }
    }
}
