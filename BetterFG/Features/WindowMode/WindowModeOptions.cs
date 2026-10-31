using System;
using System.Collections;
using System.Globalization;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.Utilities;
using BettrFG.uGUI;
using FGClient;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace BetterFG.Features.WindowMode
{
    internal static class WindowModeOptions
    {
        private const int Windowed = 0;
        private const int WindowedBorderless = 1;
        private const int FullscreenBorderless = 2;

        private static readonly string[] ValueIds =
        {
            "window_mode.mode.windowed",
            "window_mode.mode.windowed_borderless",
            "window_mode.mode.fullscreen_borderless",
        };

        private static readonly FullScreenMode[] Modes =
        {
            FullScreenMode.Windowed,
            FullScreenMode.Windowed,
            FullScreenMode.FullScreenWindow,
        };

        private const string Id = "window_mode.mode";
        private const string RowName = "BFG_WindowModeRow";

        private const int DragStripPx = 10;

        private static int _index = FullscreenBorderless;
        private static bool _loaded;
        private static TMP_Text _valueText;

        internal static void ApplyResolution(GraphicsSettings gs)
        {
            Load();

            int w = gs.ResolutionW;
            int h = gs.ResolutionH;
            int hz = gs.ResolutionHz;

            if (w < h) { w = 1024; h = 768; }
            if (hz < 0) hz = 0;

            if (w <= 0 || h <= 0)
            {
                var cur = Screen.currentResolution;
                w = cur.width;
                h = cur.height;
            }

            var mode = Modes[_index];
            Screen.SetResolution(w, h, mode, hz);
            ApplyWindowStyle(w, h);

            Plugin.Log.LogInfo($"display set to {w}x{h}@{hz}, {mode}");
        }

        private static void ApplyWindowStyle(int w, int h)
        {
            Shell32Util.SetDragStrip(DragStripPx);
            if (Modes[_index] != FullScreenMode.Windowed) return;

            bool borderless = _index == WindowedBorderless;
            Shell32Util.SetWindowFrame(borderless, w, h);

            var host = AssetManager.Instance;
            if (host != null) host.StartCoroutine(RestyleWhenUnityIsDone(borderless, w, h).WrapToIl2Cpp());
        }

        private static IEnumerator RestyleWhenUnityIsDone(bool borderless, int w, int h)
        {
            yield return null;
            yield return null;
            Shell32Util.SetWindowFrame(borderless, w, h);
        }

        internal static void ResetAll()
        {
            _index = GraphicsSettings.DefaultWindowed ? Windowed : FullscreenBorderless;
            SettingsService.Set(Id, _index.ToString(CultureInfo.InvariantCulture));
            Refresh();
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            if (int.TryParse(SettingsService.Get(Id, ""), out int v))
            {
                _index = Mathf.Clamp(v, 0, Modes.Length - 1);
                return;
            }

            _index = PlayerPrefs.GetString("Windowed", "true") == "true" ? Windowed : FullscreenBorderless;
            Plugin.Log.LogInfo($"no window mode picked yet, inheriting the game's own setting: {ValueIds[_index]}");
        }

        private static void Step(int delta)
        {
            int next = Mathf.Clamp(_index + delta, 0, Modes.Length - 1);
            if (next == _index) return;
            _index = next;
            SettingsService.Set(Id, _index.ToString(CultureInfo.InvariantCulture));
            Refresh();
        }

        private static void Refresh()
        {
            if (_valueText != null) _valueText.text = LocalizationService.Get(ValueIds[_index]);
        }

        internal static void Inject(GraphicsOptionsViewModel vm)
        {
            try
            {
                Load();

                var opts = vm._options;
                if (opts == null) return;

                WindowedOptionViewModel originalVm = null;
                for (int i = 0; i < opts.Count; i++)
                {
                    originalVm = opts[i]?.TryCast<WindowedOptionViewModel>();
                    if (originalVm != null) break;
                }
                if (originalVm == null) return;

                var original = originalVm.gameObject;
                var parent = original.transform.parent;

                var existing = parent.Find(RowName);
                if (existing != null)
                {
                    Rebind(existing);
                    return;
                }

                var ih = vm.ScreenInputHandler;
                var elems = ih != null ? ih._settingElements : null;
                var originalCfg = original.GetComponent<SettingConfigurationViewModel>();

                int listIndex = elems != null ? elems.Count : 0;
                if (elems != null)
                    for (int i = 0; i < elems.Count; i++)
                        if (elems[i] == originalCfg) { listIndex = i; break; }

                int sibling = original.transform.GetSiblingIndex();

                var holder = new GameObject("BFG_WindowModeHolder");
                holder.SetActive(false);

                var go = UnityEngine.Object.Instantiate(original, holder.transform);
                go.name = RowName;

                var dead = go.GetComponent<WindowedOptionViewModel>();
                if (dead != null) UnityEngine.Object.DestroyImmediate(dead);

                go.transform.SetParent(parent, false);
                go.transform.SetSiblingIndex(sibling + 1);
                go.SetActive(true);

                Wire(go);

                original.SetActive(false);
                if (elems != null && originalCfg != null) elems.Remove(originalCfg);

                var cfg = go.GetComponent<SettingConfigurationViewModel>();
                if (cfg != null && elems != null)
                {
                    ih.AddSettingElement(cfg, false);
                    elems.Remove(cfg);
                    elems.Insert(listIndex, cfg);
                }

                UnityEngine.Object.Destroy(holder);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"window mode row: {ex.Message}"); }
        }

        private static void Rebind(Transform row)
        {
            var texts = row.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int t = 0; t < texts.Length; t++)
                if (texts[t].name == "SettingsValueText") _valueText = texts[t];
            Refresh();
        }

        private static void Wire(GameObject go)
        {
            var texts = go.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                var t = texts[i];
                if (t.name == "SettingsText")
                {
                    var loc = t.gameObject.GetComponent("LocalisedStaticLabel")?.TryCast<Behaviour>();
                    if (loc != null) loc.enabled = false;
                    UGUIShip.RelabelText(t, Id);
                }
                else if (t.name == "SettingsValueText")
                {
                    var bind = t.gameObject.GetComponent("TextBinding")?.TryCast<Behaviour>();
                    if (bind != null) bind.enabled = false;
                    _valueText = t;
                }
            }

            var cfg = go.GetComponent<SettingConfigurationViewModel>();
            if (cfg != null)
            {
                cfg._OnMoveLeft.AddListener((UnityAction)(() => Step(-1)));
                cfg._OnMoveRight.AddListener((UnityAction)(() => Step(1)));
            }

            Refresh();
        }
    }

    [HarmonyPatch(typeof(GraphicsSettings), nameof(GraphicsSettings.ApplyResolutionSettings))]
    internal static class WindowModeResolutionPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(GraphicsSettings __instance)
        {
            WindowModeOptions.ApplyResolution(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(GraphicsOptionsViewModel), "OnEnable")]
    internal static class WindowModeInjectPatch
    {
        [HarmonyPostfix]
        public static void Postfix(GraphicsOptionsViewModel __instance) => WindowModeOptions.Inject(__instance);
    }

    [HarmonyPatch(typeof(GraphicsOptionsViewModel), nameof(GraphicsOptionsViewModel.ResetToDefaults))]
    internal static class WindowModeResetPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => WindowModeOptions.ResetAll();
    }
}
