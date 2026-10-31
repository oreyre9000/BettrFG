using System;
using System.Collections.Generic;
using BetterFG.Features.UnityRound.Editor;
using FGClient;
using UnityEngine;

namespace BetterFG.Features.CustomGamemodes
{
    public class CustomGamemodeHost : MonoBehaviour
    {
        public CustomGamemodeHost(IntPtr ptr) : base(ptr) { }

        internal static readonly List<CustomGamemode> Modes = Discover();

        static List<CustomGamemode> Discover()
        {
            var list = new List<CustomGamemode>();
            foreach (var t in typeof(CustomGamemode).Assembly.GetTypes())
                if (!t.IsAbstract && t.IsSubclassOf(typeof(CustomGamemode))) list.Add((CustomGamemode)Activator.CreateInstance(t));
            return list;
        }

        internal static void OnNametags(PlayerInfoHUDBase hud)
        {
            foreach (var mode in Modes) mode.OnNametags(hud);
        }

        internal static bool OnSpeech(FG.Common.Character.MotorFunctionSpeechStateActive state)
        {
            foreach (var mode in Modes) if (mode.OnSpeech(state)) return true;
            return false;
        }

        static CustomGamemode _active;

        void Update()
        {
            CustomGamemodesLobby.Tick();
            if (_active == null || !_active.Playing) return;
            try { _active.Tick(); }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"{_active.Id} tick blew up, stopping the mode so it doesn't spam every frame: {ex}");
                _active.End();
            }
        }

        internal static string CurrentShareCode()
        {
            if (UnityRoundLoader.InLevelEditor) return CreativeRoundMemory.GetCurrentShareCode();
            string id = GlobalGameStateClient.Instance?.GameStateView?.CurrentGameLevelName;
            if (string.IsNullOrEmpty(id) || !id.StartsWith("ugc-", StringComparison.Ordinal)) return null;
            id = id.Substring(4);
            int version = id.IndexOf('_');
            return version < 0 ? id : id.Substring(0, version);
        }

        internal static void Sync()
        {
            string code = CurrentShareCode();
            CustomGamemode want = null;
            foreach (var mode in Modes) if (mode.ShareCode == code) want = mode;
            if (want != _active)
            {
                _active?.Teardown();
                _active = want;
                if (want != null) Plugin.Log.LogInfo($"{code} is {want.Id}, setting it up");
            }
            _active?.Build();
        }

        internal static void OnPlayingStarted()
        {
            Sync();
            _active?.Begin();
        }

        internal static void OnPlaytestEnd() => _active?.End();

        internal static void OnRoundTeardown()
        {
            _active?.Teardown();
            _active = null;
        }
    }
}
