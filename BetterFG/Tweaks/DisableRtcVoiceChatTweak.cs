using System;
using System.Reflection;
using BetterFG.Services;
using HarmonyLib;

namespace BetterFG.Tweaks
{
    public class DisableRtcVoiceChatTweak : BfgTweak
    {
        public DisableRtcVoiceChatTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "disable_rtc_voice_chat";
        public override string TweakLabel => "Neutralize EOS voice-chat";
        public override bool DefaultEnabled => false;
        public override string TweakTooltip => "This tweak is not recommended for most users. Enable this tweak if you want to prevent EOS from reloading audio devices that keep reconnecting due to a faulty cable, which can cause game freezes. Only enable if you don't use EOS voice-chat ever.";

        private static MethodInfo _refreshHook;
        private static int _blocked;

        void Awake()
        {
            bool on = SettingsService.Get($"tweak.{TweakId}", DefaultEnabled ? "true" : "false") == "true";
            Plugin.Log.LogInfo($"rtc voice-chat tweak awake, saved state = {on}");
            if (on) Hook(true);
        }

        public override void EnableTweak() => Hook(true);
        public override void DisableTweak() => Hook(false);

        private static void Hook(bool on)
        {
            var h = Plugin.HarmonyInstance;

            if (!on)
            {
                if (_refreshHook != null)
                {
                    h.Unpatch(_refreshHook, HarmonyPatchType.Prefix, h.Id);
                    _refreshHook = null;
                    Plugin.Log.LogInfo("eos can go back to re-enumerating audio devices");
                }
                return;
            }

            if (_refreshHook != null) return;

            var target = AccessTools.Method(typeof(EOSVoicePartyManager), "RefreshInputAndOutputDevices");
            if (target == null) { Plugin.Log.LogWarning("EOSVoicePartyManager has no RefreshInputAndOutputDevices anymore, device stalls stay"); return; }

            try
            {
                h.Patch(target, prefix: new HarmonyMethod(AccessTools.Method(typeof(DisableRtcVoiceChatTweak), nameof(BlockDeviceRefresh))));
                _refreshHook = target;
                Plugin.Log.LogInfo("eos audio device refresh is blocked now");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"RefreshInputAndOutputDevices wouldn't take the prefix - {ex.Message}");
            }
        }

        private static bool BlockDeviceRefresh()
        {
            if (++_blocked <= 3) Plugin.Log.LogInfo($"eos wanted to re-enumerate audio devices, blocked it ({_blocked})");
            return false;
        }
    }
}
