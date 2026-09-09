using System;
using System.Collections.Generic;
using System.Globalization;
using BetterFG.Services;
using HarmonyLib;
using UnityEngine;

namespace BetterFG.Customization.UI
{
    internal static class CreativeGizmoScale
    {
        private const string Key = "creative.gizmo.scale";
        internal const float Min = 0.4f, Max = 3f;

        private static float _factor = 1f;
        private static float? _stockLineWidth;
        private static readonly Dictionary<IntPtr, float> _written = new Dictionary<IntPtr, float>();

        internal static float Scale
        {
            get => float.TryParse(SettingsService.Get(Key, "1"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float v) && v > 0f ? v : 1f;
            set => SettingsService.Set(Key, value.ToString(CultureInfo.InvariantCulture));
        }

        internal static void Apply()
        {
            _factor = Scale;
            _written.Clear();

            if (_factor == 1f) Remove(); else Install();

            var mgr = LevelEditorWorldSpaceUIManager.Instance;
            var live = mgr != null ? mgr._activeWorldSpaceUIElements : null;
            if (live == null) return;
            for (int i = 0; i < live.Count; i++) Rescale(live[i]);
        }

        private static Harmony _harmony;

        public static void Postfix(LevelEditorWorldSpaceUI __instance) => Rescale(__instance);

        private static void Install()
        {
            if (_harmony != null) return;
            var target = AccessTools.Method(typeof(LevelEditorWorldSpaceUI), nameof(LevelEditorWorldSpaceUI.Update));
            if (target == null) { Plugin.Log.LogWarning("gizmo scale: no LevelEditorWorldSpaceUI.Update to hook"); return; }
            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID + ".creative.gizmoscale");
            _harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(CreativeGizmoScale), nameof(Postfix))));
        }

        private static void Remove()
        {
            if (_harmony == null) return;
            try { _harmony.UnpatchSelf(); }
            catch (Exception ex) { Plugin.Log.LogWarning($"gizmo scale: unpatch threw, moving on: {ex.Message}"); }
            _harmony = null;
        }

        internal static void Rescale(LevelEditorWorldSpaceUI marker)
        {
            if (_factor == 1f || marker == null) return;

            var t = marker.transform;
            var s = t.localScale;
            if (!_written.TryGetValue(marker.Pointer, out float ours) || !Mathf.Approximately(s.x, ours))
            {
                s *= _factor;
                t.localScale = s;
                _written[marker.Pointer] = s.x;
            }

            var line = marker._lineRenderer;
            if (line == null) return;
            if (_stockLineWidth == null) _stockLineWidth = line.widthMultiplier;
            float want = _stockLineWidth.Value * _factor;
            if (!Mathf.Approximately(line.widthMultiplier, want)) line.widthMultiplier = want;
        }
    }
}
