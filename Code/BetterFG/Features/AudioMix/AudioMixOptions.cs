using System;
using System.Collections;
using System.Globalization;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using BetterFG.Utilities;
using BettrFG.uGUI;
using FG.Common.Audio;
using FGClient;
using FMODUnity;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BetterFG.Features.AudioMix
{
    internal static class AudioMixOptions
    {
        private sealed class Channel
        {
            public string Id;
            public string[] Buses;
            public float Value;
            public Slider Slider;
            public TMP_Text ValueText;
        }

        private const float Step = 0.05f;

        private static readonly Channel Ambience = new Channel { Id = "audio_mix.ambience", Value = 1f, Buses = new[] { "bus:/MASTER/BUS_AMBIENCES" } };
        private static readonly Channel Voice = new Channel { Id = "audio_mix.voice", Value = 1f, Buses = new[] { "bus:/MASTER/BUS_VO" } };
        private static readonly Channel Emotes = new Channel { Id = "audio_mix.emotes", Value = 1f, Buses = new[] { "bus:/MASTER/BUS_SFX_Emote_3D", "bus:/MASTER/BUS_AUX/BUS_AUX_Emote_2D" } };

        private static readonly Channel[] Channels = { Ambience, Voice, Emotes };

        internal static float VoiceVolume => Voice.Value;

        private static bool _loaded;

        internal static void Apply()
        {
            Load();
            try { if (!RuntimeManager.GetBus("bus:/MASTER").isValid()) return; }
            catch { return; }
            for (int i = 0; i < Channels.Length; i++)
                for (int b = 0; b < Channels[i].Buses.Length; b++)
                    FmodUtil.SetBusVolume(Channels[i].Buses[b], Channels[i].Value);
        }

        internal static void ResetAll()
        {
            for (int i = 0; i < Channels.Length; i++) Set(Channels[i], 1f);
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            for (int i = 0; i < Channels.Length; i++)
                if (float.TryParse(SettingsService.Get(Channels[i].Id, "1"), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    Channels[i].Value = Mathf.Clamp01(v);
        }

        private static void Set(Channel c, float v)
        {
            c.Value = Mathf.Clamp01(v);
            SettingsService.Set(c.Id, c.Value.ToString("0.###", CultureInfo.InvariantCulture));
            for (int b = 0; b < c.Buses.Length; b++) FmodUtil.SetBusVolume(c.Buses[b], c.Value);
            Refresh(c);
        }

        private static void Refresh(Channel c)
        {
            if (c.Slider != null) c.Slider.SetValueWithoutNotify(c.Value);
            if (c.ValueText != null) c.ValueText.text = Mathf.RoundToInt(c.Value * 100f).ToString(CultureInfo.InvariantCulture);
        }

        private static string RowName(int i) => "BFG_AudioMixRow_" + i;

        internal static void Inject(AudioOptionsViewModel vm)
        {
            try
            {
                Load();

                var template = FindTemplate(vm);
                if (template == null) return;

                var parent = template.transform.parent;
                var ih = vm.ScreenInputHandler;
                var elems = ih != null ? ih._settingElements : null;
                if (elems == null) return;

                if (parent.Find(RowName(0)) != null)
                {
                    Rebind(parent);
                    Apply();
                    return;
                }

                var templateCfg = template.GetComponent<SettingConfigurationViewModel>();
                int listIndex = elems.Count;
                for (int i = 0; i < elems.Count; i++)
                    if (elems[i] == templateCfg) { listIndex = i + 1; break; }

                int sibling = template.transform.GetSiblingIndex() + 1;

                var holder = new GameObject("BFG_AudioMixHolder");
                holder.SetActive(false);

                for (int i = 0; i < Channels.Length; i++)
                {
                    var go = UnityEngine.Object.Instantiate(template.gameObject, holder.transform);
                    go.name = RowName(i);

                    var dead = go.GetComponent<EffectsVolumeOptionViewModel>();
                    if (dead != null) UnityEngine.Object.DestroyImmediate(dead);

                    go.transform.SetParent(parent, false);
                    go.transform.SetSiblingIndex(sibling + i);
                    go.SetActive(true);

                    Wire(go, Channels[i]);

                    var cfg = go.GetComponent<SettingConfigurationViewModel>();
                    if (cfg == null) continue;
                    ih.AddSettingElement(cfg, false);
                    elems.Remove(cfg);
                    elems.Insert(listIndex + i, cfg);
                }

                UnityEngine.Object.Destroy(holder);
                Apply();
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"audio mix rows: {ex.Message}"); }
        }

        private static GameObject FindTemplate(AudioOptionsViewModel vm)
        {
            var opts = vm._options;
            if (opts == null) return null;
            for (int i = 0; i < opts.Count; i++)
            {
                var sfx = opts[i]?.TryCast<EffectsVolumeOptionViewModel>();
                if (sfx != null) return sfx.gameObject;
            }
            return null;
        }

        private static void Rebind(Transform parent)
        {
            for (int i = 0; i < Channels.Length; i++)
            {
                var row = parent.Find(RowName(i));
                if (row == null) continue;
                Channels[i].Slider = row.Find("InputsContainer/Slider")?.GetComponent<Slider>();
                Channels[i].ValueText = row.Find("InputsContainer/SettingsValue/SettingsValueText")?.GetComponent<TextMeshProUGUI>();
                Refresh(Channels[i]);
            }
        }

        private static void Wire(GameObject go, Channel c)
        {
            var label = go.transform.Find("SettingsText");
            if (label != null)
            {
                var loc = label.gameObject.GetComponent("LocalisedStaticLabel")?.TryCast<Behaviour>();
                if (loc != null) loc.enabled = false;
                UGUIShip.RelabelText(label.GetComponent<TextMeshProUGUI>(), c.Id);
            }

            var value = go.transform.Find("InputsContainer/SettingsValue/SettingsValueText");
            if (value != null)
            {
                var bind = value.gameObject.GetComponent("TextBinding")?.TryCast<Behaviour>();
                if (bind != null) bind.enabled = false;
                c.ValueText = value.GetComponent<TextMeshProUGUI>();
            }

            c.Slider = go.transform.Find("InputsContainer/Slider")?.GetComponent<Slider>();
            if (c.Slider != null) c.Slider.onValueChanged.AddListener((UnityAction<float>)(v => Set(c, v)));

            var cfg = go.GetComponent<SettingConfigurationViewModel>();
            if (cfg != null)
            {
                cfg._OnMoveLeft.AddListener((UnityAction)(() => Set(c, c.Value - Step)));
                cfg._OnMoveRight.AddListener((UnityAction)(() => Set(c, c.Value + Step)));
            }

            Refresh(c);
        }
    }

    [HarmonyPatch(typeof(AudioOptionsViewModel), "OnEnable")]
    internal static class AudioMixInjectPatch
    {
        [HarmonyPostfix]
        public static void Postfix(AudioOptionsViewModel __instance) => AudioMixOptions.Inject(__instance);
    }

    [HarmonyPatch(typeof(AudioOptionsViewModel), nameof(AudioOptionsViewModel.ResetToDefaults))]
    internal static class AudioMixResetPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => AudioMixOptions.ResetAll();
    }

    [HarmonyPatch(typeof(FGClient.AudioSettings), nameof(FGClient.AudioSettings.ApplySettings))]
    internal static class AudioMixApplyPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => AudioMixOptions.Apply();
    }

    [HarmonyPatch(typeof(FallGuysAudioAnimationCallbacks), nameof(FallGuysAudioAnimationCallbacks.PlayIdleEvent2DLocal3DRemote))]
    internal static class AudioMixIdleNoisePatch
    {
        [HarmonyPostfix]
        public static void Postfix(FallGuysAudioAnimationCallbacks __instance)
        {
            var reference = __instance._lastEventInstance;
            if (reference == null) { Plugin.Log.LogInfo("bean idle noise, but no event reference came back to scale"); return; }

            var instance = reference._eventInstance;
            if (instance.isValid())
            {
                instance.setVolume(AudioMixOptions.VoiceVolume);
                Plugin.Log.LogInfo($"idle noise {reference.EventName} down to {AudioMixOptions.VoiceVolume}");
                return;
            }

            Plugin.Log.LogInfo($"idle noise {reference.EventName} isn't built yet, chasing it");
            __instance.StartCoroutine(ScaleWhenReady(reference).WrapToIl2Cpp());
        }

        private static IEnumerator ScaleWhenReady(EventInstanceReference reference)
        {
            for (int i = 0; i < 60; i++)
            {
                var instance = reference._eventInstance;
                if (instance.isValid())
                {
                    instance.setVolume(AudioMixOptions.VoiceVolume);
                    yield break;
                }
                yield return null;
            }
            Plugin.Log.LogWarning($"gave up chasing idle noise {reference.EventName}, it never got an instance");
        }
    }

    [HarmonyPatch(typeof(AudioManager), nameof(AudioManager.DeregisterLoadingBank))]
    internal static class AudioMixBankLoadedPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => AudioMixOptions.Apply();
    }
}
