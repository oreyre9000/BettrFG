using BetterFG.Features.UnityRound.Editor;
using BetterFG.Services;
using FGClient;
using UnityEngine;
using PlayerUtils = FallGuysLib.Players.PlayerUtils;

namespace BetterFG.Features.CustomGamemodes
{
    public abstract class CustomGamemode
    {
        public abstract string Id { get; }
        public abstract string ShareCode { get; }

        public bool Built { get; private set; }
        public bool Playing { get; private set; }
        public uint Seed { get; private set; }
        public string SeedSource { get; private set; }

        float _localStart;

        internal static FallGuysCharacterController LocalFgcc => UnityRoundLoader.InLevelEditor
            ? BeanMonitorService.LocalPlayerBean?.GetComponent<FallGuysCharacterController>()
            : PlayerUtils.PlayerController;

        public void Build()
        {
            if (Built) return;
            Built = true;
            OnBuild();
        }

        public void Begin()
        {
            End();
            Build();
            Playing = true;
            _localStart = Time.time;
            Reseed();
            OnBegin();
        }

        public void Tick()
        {
            if (!Playing) return;
            string guid = GlobalGameStateClient.Instance.CachedEpisodeGuid;
            if ((string.IsNullOrEmpty(guid) ? "editor" : guid) != SeedSource)
            {
                Reseed();
                OnReseeded();
            }
            OnTick(Clock());
        }

        public void End()
        {
            if (Playing) OnEnd();
            Playing = false;
        }

        public void Teardown()
        {
            End();
            if (Built) OnTeardown();
            Built = false;
        }

        void Reseed()
        {
            string guid = GlobalGameStateClient.Instance.CachedEpisodeGuid;
            SeedSource = string.IsNullOrEmpty(guid) ? "editor" : guid;
            Seed = Fnv(SeedSource);
        }

        protected float Clock()
        {
            var view = GlobalGameStateClient.Instance.GameStateView;
            float server = view != null ? view.GameplayTimeElapsed : 0f;
            return server > 0f ? server : Time.time - _localStart;
        }

        protected abstract void OnBuild();
        protected abstract void OnBegin();
        protected abstract void OnReseeded();
        protected abstract void OnTick(float clock);
        protected abstract void OnEnd();
        protected abstract void OnTeardown();
        internal virtual void OnNametags(PlayerInfoHUDBase hud) { }
        internal virtual bool OnSpeech(FG.Common.Character.MotorFunctionSpeechStateActive state) => false;

        static readonly System.Collections.Generic.HashSet<string> _missingEvents = new System.Collections.Generic.HashSet<string>();

        internal static GameObject PoolParticle(string pool)
            => UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<GameObject>("ParticleSystemPools/" + pool + ".prefab").WaitForCompletion().GetComponent<FG.Common.ParticleSystemPool>()._object.gameObject;

        internal static void Fx(GameObject prefab, Vector3 pos, float scale)
        {
            var g = Object.Instantiate(prefab, pos, Quaternion.identity);
            g.transform.localScale *= scale;
            g.GetComponent<ParticleSystem>().Play(true);
            Object.Destroy(g, 4f);
        }

        internal static void Sfx(string ev, Vector3 pos)
        {
            if (!TryEvent(ev, out var desc)) return;
            desc.createInstance(out var inst);
            inst.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(pos));
            inst.start();
            inst.release();
        }

        internal static void Ui(string ev)
        {
            if (!TryEvent(ev, out var desc)) return;
            desc.createInstance(out var inst);
            inst.start();
            inst.release();
        }

        internal static bool TryEvent(string ev, out FMOD.Studio.EventDescription desc)
        {
            desc = default;
            try
            {
                desc = AudioManager.GetEventDescription(ev);
                return true;
            }
            catch (System.Exception ex)
            {
                if (_missingEvents.Add(ev)) Plugin.Log.LogWarning($"custom game mode: {ev} isn't loaded (yet), skipping it ({ex.GetType().Name})");
                return false;
            }
        }

        public static uint Fnv(string s)
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return h;
        }

        public static uint Mix(uint s)
        {
            s += 0x6D2B79F5;
            uint z = s;
            z = (z ^ (z >> 15)) * (z | 1);
            z ^= z + (z ^ (z >> 7)) * (z | 61);
            return z ^ (z >> 14);
        }

        public sealed class Prng
        {
            uint _s;
            public Prng(uint seed) => _s = seed;
            public uint Next() { uint r = Mix(_s); _s += 0x6D2B79F5; return r; }
            public int Range(int n) => (int)(Next() % (uint)n);
            public float Float() => (Next() >> 8) / 16777216f;
        }
    }
}
