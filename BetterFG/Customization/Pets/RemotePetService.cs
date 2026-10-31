using System;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Network;
using BetterFG.Services;
using BetterFG.Utilities;
using FG.Common;
using UnityEngine;

namespace BetterFG.Customization.Pets
{
    // mirrors PetService but for every OTHER player's equipped pets, read off whatever .bfgprofile
    // RemoteProfileStore already resolved for their playerKey - same local-only overlay every other
    // profile field (skins, nametag, plinth...) already rides.
    public class RemotePetService : MonoBehaviour
    {
        public RemotePetService(IntPtr ptr) : base(ptr) { }

        public static RemotePetService Instance { get; private set; }

        // keyed by "<playerKey>#<pet index in their profile>" - one remote can have several pets out
        readonly Dictionary<string, GameObject> _livePets = new Dictionary<string, GameObject>();
        readonly HashSet<string> _spawning = new HashSet<string>();

        float _scanTimer;

        void Awake() => Instance = this;

        void Update()
        {
            _scanTimer -= Time.deltaTime;
            if (_scanTimer > 0f) return;
            _scanTimer = 1f;

            if (RemoteProfileStore.IsEmpty)
            {
                if (_livePets.Count > 0) DespawnAll();
                return;
            }

            var index = FallGuysLib.Players.PlayerUtils.GetClientPlayerManager()?._playerIdIndex;
            if (index == null) { DespawnAll(); return; }

            var local = BeanMonitorService.LocalPlayerBean;
            IntPtr localPtr = local is null ? IntPtr.Zero : local.m_CachedPtr;
            var wanted = new HashSet<string>();
            foreach (var kvp in index)
            {
                var data = kvp.Value;
                if (data == null) continue;
                string key = data.playerKey;
                var profile = string.IsNullOrEmpty(key) ? null : RemoteProfileStore.TryGet(key);
                if (profile?.pets == null || profile.pets.Count == 0) continue;
                var fgcc = data.fgcc;
                var bean = fgcc != null ? fgcc.gameObject : null;
                if (bean is null || bean.m_CachedPtr == IntPtr.Zero || bean.m_CachedPtr == localPtr) continue;

                for (int i = 0; i < profile.pets.Count; i++)
                {
                    string petKey = key + "#" + i;
                    wanted.Add(petKey);
                    if (_spawning.Contains(petKey)) continue;
                    if (_livePets.TryGetValue(petKey, out var go) && go != null) continue;
                    SpawnOne(petKey, profile.pets[i], bean, i);
                }
            }

            // whoever left, or whose profile stopped carrying pets, loses them here too
            var stale = new List<string>();
            foreach (var k in _livePets.Keys)
                if (!wanted.Contains(k)) stale.Add(k);
            foreach (var k in stale) DespawnOne(k);
        }

        void SpawnOne(string petKey, PetData data, GameObject ownerBean, int slot)
        {
            _spawning.Add(petKey);
            PetService.Instance?.AddPendingName(data.name);
            StartCoroutine(PetBeanBuilder.Build(data, bean =>
            {
                _spawning.Remove(petKey);
                PetService.Instance?.RemovePendingName(data.name);
                if (bean == null) { Plugin.Log.LogWarning($"remote pet '{data.name}' spawn failed, no bean came back"); return; }
                var follow = bean.AddComponent<PetFollowComponent>();
                follow.OwnerOverride = ownerBean;
                follow.SlotIndex = slot;
                if (ownerBean != null)
                    bean.transform.position = PetFollowComponent.FormationSpot(ownerBean.transform, slot);
                _livePets[petKey] = bean;
                PetService.Instance?.PumpRemoteFgcc(bean.GetComponent<FallGuysCharacterController>());
                Plugin.Log.LogInfo($"'{data.name}' tagging along behind {ownerBean.name}");
            }, ownerOverride: ownerBean).WrapToIl2Cpp());
        }

        public IEnumerable<FallGuysCharacterController> LiveFgccsForOwner(GameObject ownerBean)
        {
            if (ownerBean == null) yield break;
            foreach (var go in _livePets.Values)
            {
                if (go == null) continue;
                var follow = go.GetComponent<PetFollowComponent>();
                if (follow == null || follow.OwnerOverride != ownerBean) continue;
                var f = go.GetComponent<FallGuysCharacterController>();
                if (f != null) yield return f;
            }
        }

        void DespawnAll()
        {
            var keys = new List<string>(_livePets.Keys);
            foreach (var k in keys) DespawnOne(k);
        }

        void DespawnOne(string petKey)
        {
            if (_livePets.TryGetValue(petKey, out var live) && live != null)
            {
                var follow = live.GetComponent<PetFollowComponent>();
                if (follow != null) follow.enabled = false;
                var f = live.GetComponent<FallGuysCharacterController>();
                if (!FallGuysLib.Players.BeanNetworkUtils.TryNetworkUnspawn(live)) Destroy(live);
                PetService.Instance?.UnregisterLiveFgcc(f);
            }
            _livePets.Remove(petKey);
        }
    }
}
