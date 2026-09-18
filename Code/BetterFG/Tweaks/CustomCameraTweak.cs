using System;
using System.Globalization;
using BetterFG.Core;
using BetterFG.Services;
using FallGuysLib.Camera;
using FGClient;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Tweaks
{
    public class CustomCameraTweak : BfgTweak
    {
        public CustomCameraTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "custom_camera";
        public override string TweakLabel => "tweak.custom_camera";
        public override bool DefaultEnabled => false;

        public static CustomCameraTweak Instance { get; private set; }
        void Awake() => Instance = this;

        private const float RampMax = 0.5f;
        internal const float DefaultAccel = 0.8f, DefaultDecel = 0.8f;

        internal static float Accel { get => GetF("camera.accel", DefaultAccel); set => SetF("camera.accel", value); }
        internal static float Decel { get => GetF("camera.decel", DefaultDecel); set => SetF("camera.decel", value); }
        internal static bool ControllerOnly
        {
            get => SettingsService.Get("camera.controller_only", "false") == "true";
            set => SettingsService.Set("camera.controller_only", value ? "true" : "false");
        }

        private static float GetF(string key, float def)
            => float.TryParse(SettingsService.Get(key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? Mathf.Clamp01(v) : def;
        private static void SetF(string key, float v)
            => SettingsService.Set(key, Mathf.Clamp01(v).ToString("0.###", CultureInfo.InvariantCulture));

        private static float _stockAccelX = -1f, _stockDecelX, _stockAccelY, _stockDecelY;

        public override void EnableTweak() => Apply();
        public override void DisableTweak() => Apply();
        public override void OnRoundStart() => Apply();
        public override void OnLevelEditorPlaytest() => Apply();

        internal static void Apply()
        {
            var director = CameraLocator.GetCameraDirector();
            var freelook = director?._closeCamera;
            if (freelook == null) return;
            var pcc = director.transform.root.GetComponentInChildren<PlayerCameraController>(true);

            var x = freelook.m_XAxis;
            var y = freelook.m_YAxis;
            if (_stockAccelX < 0f)
            {
                _stockAccelX = x.m_AccelTime; _stockDecelX = x.m_DecelTime;
                _stockAccelY = y.m_AccelTime; _stockDecelY = y.m_DecelTime;
                Plugin.Log.LogInfo($"close cam stock ramps: x accel {_stockAccelX} decel {_stockDecelX}, y accel {_stockAccelY} decel {_stockDecelY}");
            }

            bool on = Instance != null && Instance.IsEnabled && (!ControllerOnly || pcc == null || pcc._usingController);
            float accel = (1f - Accel) * RampMax, decel = (1f - Decel) * RampMax;
            x.m_AccelTime = on ? accel : _stockAccelX;
            x.m_DecelTime = on ? decel : _stockDecelX;
            y.m_AccelTime = on ? accel : _stockAccelY;
            y.m_DecelTime = on ? decel : _stockDecelY;
            freelook.m_XAxis = x;
            freelook.m_YAxis = y;
        }
    }

    public class CameraOptionRow : MonoBehaviour
    {
        public CameraOptionRow(IntPtr ptr) : base(ptr) { }

        internal enum Kind { Toggle, Accel, Decel, ControllerOnly, CameraAssist }

        internal Kind RowKind =>
            name.StartsWith("BFG_CameraAccel") ? Kind.Accel :
            name.StartsWith("BFG_CameraDecel") ? Kind.Decel :
            name.StartsWith("BFG_CameraControllerOnly") ? Kind.ControllerOnly :
            name.StartsWith("BFG_CameraAssist") ? Kind.CameraAssist : Kind.Toggle;

        internal bool Get() => RowKind == Kind.ControllerOnly ? CustomCameraTweak.ControllerOnly
            : RowKind == Kind.CameraAssist ? DisableCameraAssistTweak.Instance != null && DisableCameraAssistTweak.Instance.IsEnabled
            : CustomCameraTweak.Instance != null && CustomCameraTweak.Instance.IsEnabled;

        internal void Set(bool on)
        {
            if (RowKind == Kind.ControllerOnly) { CustomCameraTweak.ControllerOnly = on; CustomCameraTweak.Apply(); }
            else if (RowKind == Kind.CameraAssist) DisableCameraAssistTweak.Instance?.SetEnabled(on);
            else CustomCameraTweak.Instance?.SetEnabled(on);
        }

        internal float SliderDefault => RowKind == Kind.Accel ? CustomCameraTweak.DefaultAccel : CustomCameraTweak.DefaultDecel;

        internal float SliderValue
        {
            get => RowKind == Kind.Accel ? CustomCameraTweak.Accel : CustomCameraTweak.Decel;
            set
            {
                if (RowKind == Kind.Accel) CustomCameraTweak.Accel = value;
                else CustomCameraTweak.Decel = value;
                CustomCameraTweak.Apply();
            }
        }

        internal static void ShowSubRows(InvertHorizontalOptionViewModel toggle, bool on, bool recenter)
        {
            var cfg = toggle._settingConfigurationViewModel;
            var ih = cfg?._inputHandler;
            var elems = ih?._settingElements;
            int at = elems != null ? elems.IndexOf(cfg) : -1;
            int toggleAt = at;
            foreach (var row in toggle.transform.parent.GetComponentsInChildren<CameraOptionRow>(true))
            {
                if (row.RowKind == Kind.Toggle || row.RowKind == Kind.CameraAssist) continue;
                row.gameObject.SetActive(on);
                var rc = row.GetComponent<SettingConfigurationViewModel>();
                if (elems == null || rc == null) continue;
                if (on) { if (!elems.Contains(rc)) elems.Insert(++at, rc); }
                else elems.Remove(rc);
            }
            if (ih == null || toggleAt < 0) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(toggle.transform.parent.GetComponent<RectTransform>());
            if (recenter) ih.SelectSettingIndex(toggleAt, true);
            else if (ih._selectedSettingIndex >= elems.Count) ih._selectedSettingIndex = elems.Count - 1;
        }

        private const string HeaderKey = "camera.category_label";

        private static readonly string[] Names = { "BFG_CameraToggle", "BFG_CameraAccel", "BFG_CameraDecel", "BFG_CameraControllerOnly", "BFG_CameraAssist" };
        private static readonly string[] LabelKeys = { "tweak.custom_camera", "camera.accel_label", "camera.decel_label", "camera.controller_only_label", "tweak.disable_camera_assist" };
        private static GameObject[] _prefabs;

        private static void OnLanguageChanged()
        {
            for (int i = 0; i < LabelKeys.Length; i++) NavPromptCore.SetCmsString(LabelKeys[i], LocalizationService.Get(LabelKeys[i]));
            foreach (var row in Resources.FindObjectsOfTypeAll<CameraOptionRow>())
                foreach (var label in row.GetComponentsInChildren<LocalisedStaticLabel>(true))
                    label.SetLocalisationKey(label.Key);
        }

        private static GameObject _header;

        internal static void AddToSpawner(OptionsSpawner spawner)
        {
            var list = spawner._pcConfig?._options;
            if (list == null) return;
            GameObject toggleDonor = null, sliderDonor = null;
            for (int i = 0; i < list.Count; i++)
            {
                var go = list[i];
                if (go == null) continue;
                if (_prefabs != null && go.Pointer == _prefabs[0].Pointer) return;
                if (toggleDonor == null && go.GetComponent<InvertHorizontalOptionViewModel>() != null) toggleDonor = go;
                if (sliderDonor == null && go.GetComponent<CameraSensitivityXOptionViewModel>() != null) sliderDonor = go;
            }
            if (toggleDonor == null || sliderDonor == null) return;

            for (int i = 0; i < LabelKeys.Length; i++) NavPromptCore.SetCmsString(LabelKeys[i], LocalizationService.Get(LabelKeys[i]));
            NavPromptCore.SetCmsString(HeaderKey, LocalizationService.Get(HeaderKey));

            if (_prefabs == null)
            {
                LocalizationService.LanguageChanged += OnLanguageChanged;
                var holder = new GameObject("BFG_CameraOptionHolder");
                holder.SetActive(false);
                holder.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(holder);
                _prefabs = new GameObject[Names.Length];
                for (int i = 0; i < Names.Length; i++)
                {
                    bool slider = i == 1 || i == 2;
                    var p = UnityEngine.Object.Instantiate(slider ? sliderDonor : toggleDonor, holder.transform);
                    p.name = Names[i];
                    p.AddComponent<CameraOptionRow>();
                    foreach (var label in p.GetComponentsInChildren<LocalisedStaticLabel>(true)) label._key = LabelKeys[i];
                    _prefabs[i] = p;
                }
                _header = MakeHeader(spawner, holder.transform);
            }

            if (_header != null) list.Add(_header);
            for (int i = 0; i < _prefabs.Length; i++) list.Add(_prefabs[i]);
            Plugin.Log.LogInfo($"camera section appended to {spawner.name}, {list.Count} rows in that screen now");
        }

        private static GameObject MakeHeader(OptionsSpawner spawner, Transform holder)
        {
            LocalisedStaticLabel donorLabel = null;
            foreach (var label in spawner.transform.root.GetComponentsInChildren<LocalisedStaticLabel>(true))
                if (label.Key == "controls_binding_label") { donorLabel = label; break; }
            if (donorLabel == null) { Plugin.Log.LogWarning("never found the control-binding header, camera rows will sit bare"); return null; }

            var bar = donorLabel.transform;
            string path = "";
            for (int up = 0; up < 3 && bar.parent != null; up++)
            {
                if (bar.GetComponent<Image>() != null) break;
                path = path.Length == 0 ? bar.name : bar.name + "/" + path;
                bar = bar.parent;
            }

            var go = UnityEngine.Object.Instantiate(bar.gameObject, holder);
            go.name = "BFG_CameraHeader";
            foreach (var rebind in go.GetComponentsInChildren<FallGuysControlRebinding>(true)) UnityEngine.Object.DestroyImmediate(rebind);

            var keep = path.Length == 0 ? go.transform : go.transform.Find(path);
            if (keep != null)
                for (int i = go.transform.childCount - 1; i >= 0; i--)
                {
                    var child = go.transform.GetChild(i);
                    if (!keep.IsChildOf(child)) UnityEngine.Object.DestroyImmediate(child.gameObject);
                }

            foreach (var label in go.GetComponentsInChildren<LocalisedStaticLabel>(true)) label._key = HeaderKey;
            Plugin.Log.LogInfo($"camera header off {bar.name}, kept {path}, {go.transform.childCount} children left");
            return go;
        }
    }

    [HarmonyPatch(typeof(OptionsSpawner), "Awake")]
    internal static class CustomCameraSpawnPatch
    {
        [HarmonyPrefix]
        public static void Prefix(OptionsSpawner __instance)
        {
            try { CameraOptionRow.AddToSpawner(__instance); }
            catch (Exception ex) { Plugin.Log.LogWarning($"camera rows: {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(InvertHorizontalOptionViewModel), "OnEnable")]
    internal static class CustomCameraToggleEnablePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(InvertHorizontalOptionViewModel __instance)
        {
            var row = __instance.GetComponent<CameraOptionRow>();
            if (row == null) return true;
            bool on = row.Get();
            __instance.InvertX = on;
            __instance._currentInvertX = on;
            if (row.RowKind == CameraOptionRow.Kind.Toggle) CameraOptionRow.ShowSubRows(__instance, on, false);
            return false;
        }
    }

    [HarmonyPatch(typeof(InvertHorizontalOptionViewModel), nameof(InvertHorizontalOptionViewModel.ToggleInvertX))]
    internal static class CustomCameraTogglePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(InvertHorizontalOptionViewModel __instance)
        {
            var row = __instance.GetComponent<CameraOptionRow>();
            if (row == null) return true;
            bool on = !__instance.InvertX;
            row.Set(on);
            if (row.RowKind == CameraOptionRow.Kind.Toggle) CameraOptionRow.ShowSubRows(__instance, on, true);
            __instance.InvertX = on;
            __instance._currentInvertX = on;
            return false;
        }
    }

    [HarmonyPatch(typeof(InvertHorizontalOptionViewModel), nameof(InvertHorizontalOptionViewModel.ResetToDefaults))]
    internal static class CustomCameraToggleResetPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(InvertHorizontalOptionViewModel __instance)
        {
            var row = __instance.GetComponent<CameraOptionRow>();
            if (row == null) return true;
            row.Set(false);
            if (row.RowKind == CameraOptionRow.Kind.Toggle) CameraOptionRow.ShowSubRows(__instance, false, true);
            __instance.InvertX = false;
            __instance._currentInvertX = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(CameraSensitivityXOptionViewModel), "OnEnable")]
    internal static class CustomCameraSliderEnablePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(CameraSensitivityXOptionViewModel __instance)
        {
            var row = __instance.GetComponent<CameraOptionRow>();
            if (row == null) return true;
            float v = row.SliderValue;
            __instance.SensitivityX = v;
            __instance._currentSensitivityX = v;
            __instance._sliderSensitivityX?.SetValueWithoutNotify(v);
            return false;
        }
    }

    [HarmonyPatch(typeof(CameraSensitivityXOptionViewModel), nameof(CameraSensitivityXOptionViewModel.OnChangeSensitivityX))]
    internal static class CustomCameraSliderChangePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(CameraSensitivityXOptionViewModel __instance, float value)
        {
            var row = __instance.GetComponent<CameraOptionRow>();
            if (row == null) return true;
            row.SliderValue = value;
            __instance.SensitivityX = value;
            __instance._currentSensitivityX = value;
            return false;
        }
    }

    [HarmonyPatch(typeof(CameraSensitivityXOptionViewModel), nameof(CameraSensitivityXOptionViewModel.AddSensitivityX))]
    internal static class CustomCameraSliderStepPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(CameraSensitivityXOptionViewModel __instance, float amount)
        {
            var row = __instance.GetComponent<CameraOptionRow>();
            if (row == null) return true;
            float v = Mathf.Clamp01(row.SliderValue + amount);
            row.SliderValue = v;
            __instance.SensitivityX = v;
            __instance._currentSensitivityX = v;
            __instance._sliderSensitivityX?.SetValueWithoutNotify(v);
            return false;
        }
    }

    [HarmonyPatch(typeof(CameraSensitivityXOptionViewModel), nameof(CameraSensitivityXOptionViewModel.ResetToDefaults))]
    internal static class CustomCameraSliderResetPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(CameraSensitivityXOptionViewModel __instance)
        {
            var row = __instance.GetComponent<CameraOptionRow>();
            if (row == null) return true;
            float v = row.SliderDefault;
            row.SliderValue = v;
            __instance.SensitivityX = v;
            __instance._currentSensitivityX = v;
            __instance._sliderSensitivityX?.SetValueWithoutNotify(v);
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerCameraController), "CalculateUsingController")]
    internal static class CustomCameraDevicePatch
    {
        [HarmonyPostfix]
        public static void Postfix() => CustomCameraTweak.Apply();
    }
}
