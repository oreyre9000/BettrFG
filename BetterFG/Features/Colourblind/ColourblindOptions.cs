using System;
using System.Globalization;
using BetterFG.Core;
using BetterFG.Services;
using BettrFG.uGUI;
using FGClient;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BetterFG.Features.Colourblind
{
    internal static class ColourblindOptions
    {
        private sealed class Row
        {
            public string Id;
            public string[] ValueIds;
            public int Index;
            public int Default;
            public TMP_Text ValueText;
            public int Count => ValueIds != null ? ValueIds.Length : 11;
        }

        private static readonly Row Mode = new Row
        {
            Id = "colourblind.mode",
            ValueIds = new[] { "colourblind.mode.off", "colourblind.mode.protan", "colourblind.mode.deutan", "colourblind.mode.tritan", "colourblind.mode.achroma" }
        };

        private static readonly Row Strength = new Row { Id = "colourblind.strength", Index = 10, Default = 10 };

        private static readonly Row[] Rows = { Mode, Strength };

        private static readonly float[][] Simulation =
        {
            null,
            new[] { 0.567f, 0.433f, 0f, 0.558f, 0.442f, 0f, 0f, 0.242f, 0.758f },
            new[] { 0.625f, 0.375f, 0f, 0.700f, 0.300f, 0f, 0f, 0.300f, 0.700f },
            new[] { 0.950f, 0.050f, 0f, 0f, 0.433f, 0.567f, 0f, 0.475f, 0.525f }
        };

        private static readonly float[] RedGreenShift = { 0f, 0f, 0f, 0.7f, 1f, 0f, 0.7f, 0f, 1f };
        private static readonly float[] BlueYellowShift = { 1f, 0f, 0.7f, 0f, 1f, 0.7f, 0f, 0f, 0f };

        private static bool _loaded;
        private static GameObject _overlay;
        private static Material _mat;

        internal static void Apply()
        {
            Load();

            if (Mode.Index == 0)
            {
                if (_overlay != null) _overlay.SetActive(false);
                return;
            }

            Ensure();
            if (_mat == null) return;

            bool mono = Mode.Index == 4;
            if (!mono)
            {
                var m = Simulation[Mode.Index];
                _mat.SetVector("_M0", new Vector4(m[0], m[1], m[2], 0f));
                _mat.SetVector("_M1", new Vector4(m[3], m[4], m[5], 0f));
                _mat.SetVector("_M2", new Vector4(m[6], m[7], m[8], 0f));

                var s = Mode.Index == 3 ? BlueYellowShift : RedGreenShift;
                _mat.SetVector("_S0", new Vector4(s[0], s[1], s[2], 0f));
                _mat.SetVector("_S1", new Vector4(s[3], s[4], s[5], 0f));
                _mat.SetVector("_S2", new Vector4(s[6], s[7], s[8], 0f));
            }

            _mat.SetFloat("_Mono", mono ? 1f : 0f);
            _mat.SetFloat("_Strength", Strength.Index * 0.1f);
            _overlay.SetActive(true);
        }

        private static void Ensure()
        {
            if (_overlay != null) return;

            var sh = AssetManager.GetShader("colourblind");
            if (sh == null)
            {
                Plugin.Log.LogWarning("colourblind shader never showed up in the bundles, nothing to filter with");
                return;
            }

            _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };

            _overlay = new GameObject("BettrFG_Colourblind");
            _overlay.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(_overlay);

            var canvas = _overlay.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;

            var imgGo = new GameObject("Filter");
            imgGo.transform.SetParent(_overlay.transform, false);
            var img = imgGo.AddComponent<RawImage>();
            img.material = _mat;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Plugin.Log.LogInfo("colourblind overlay up, sitting on top of everything at 32760");
        }

        internal static void ResetAll()
        {
            for (int i = 0; i < Rows.Length; i++)
            {
                Rows[i].Index = Rows[i].Default;
                SettingsService.Set(Rows[i].Id, Rows[i].Index.ToString(CultureInfo.InvariantCulture));
                Refresh(Rows[i]);
            }
            Apply();
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            for (int i = 0; i < Rows.Length; i++)
                if (int.TryParse(SettingsService.Get(Rows[i].Id, Rows[i].Default.ToString(CultureInfo.InvariantCulture)), out int v))
                    Rows[i].Index = Mathf.Clamp(v, 0, Rows[i].Count - 1);
        }

        private static void Step(Row r, int delta)
        {
            int next = Mathf.Clamp(r.Index + delta, 0, r.Count - 1);
            if (next == r.Index) return;
            r.Index = next;
            SettingsService.Set(r.Id, r.Index.ToString(CultureInfo.InvariantCulture));
            Refresh(r);
            Apply();
        }

        private static void Refresh(Row r)
        {
            if (r.ValueText == null) return;
            r.ValueText.text = r.ValueIds != null
                ? LocalizationService.Get(r.ValueIds[r.Index])
                : (r.Index * 10).ToString(CultureInfo.InvariantCulture) + "%";
        }

        private static string RowName(int i) => "BFG_ColourblindRow_" + i;

        internal static void Inject(GraphicsOptionsViewModel vm)
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

                int sibling = parent.childCount;
                int listIndex = elems.Count;

                var holder = new GameObject("BFG_ColourblindHolder");
                holder.SetActive(false);

                for (int i = 0; i < Rows.Length; i++)
                {
                    var go = UnityEngine.Object.Instantiate(template, holder.transform);
                    go.name = RowName(i);

                    var dead = go.GetComponent<OptionViewModel>();
                    if (dead != null) UnityEngine.Object.DestroyImmediate(dead);

                    go.transform.SetParent(parent, false);
                    go.transform.SetSiblingIndex(sibling + i);
                    go.SetActive(true);

                    Wire(go, Rows[i]);

                    var cfg = go.GetComponent<SettingConfigurationViewModel>();
                    if (cfg == null) continue;
                    ih.AddSettingElement(cfg, false);
                    elems.Remove(cfg);
                    elems.Insert(listIndex + i, cfg);
                }

                UnityEngine.Object.Destroy(holder);
                Apply();
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"colourblind rows: {ex.Message}"); }
        }

        private static GameObject FindTemplate(GraphicsOptionsViewModel vm)
        {
            var opts = vm._options;
            if (opts == null) return null;
            for (int i = 0; i < opts.Count; i++)
            {
                var go = opts[i]?.gameObject;
                if (go == null) continue;
                if (go.GetComponentInChildren<Slider>(true) != null) continue;
                var texts = go.GetComponentsInChildren<TextMeshProUGUI>(true);
                for (int t = 0; t < texts.Length; t++)
                    if (texts[t].name == "SettingsValueText") return go;
            }
            Plugin.Log.LogWarning("no carousel row on the graphics screen to clone, colourblind rows skipped");
            return null;
        }

        private static void Rebind(Transform parent)
        {
            for (int i = 0; i < Rows.Length; i++)
            {
                var row = parent.Find(RowName(i));
                if (row == null) continue;
                var texts = row.GetComponentsInChildren<TextMeshProUGUI>(true);
                for (int t = 0; t < texts.Length; t++)
                    if (texts[t].name == "SettingsValueText") Rows[i].ValueText = texts[t];
                Refresh(Rows[i]);
            }
        }

        private static void Wire(GameObject go, Row r)
        {
            var texts = go.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                var t = texts[i];
                if (t.name == "SettingsText")
                {
                    var loc = t.gameObject.GetComponent("LocalisedStaticLabel")?.TryCast<Behaviour>();
                    if (loc != null) loc.enabled = false;
                    UGUIShip.RelabelText(t, r.Id);
                }
                else if (t.name == "SettingsValueText")
                {
                    var bind = t.gameObject.GetComponent("TextBinding")?.TryCast<Behaviour>();
                    if (bind != null) bind.enabled = false;
                    r.ValueText = t;
                }
            }

            var cfg = go.GetComponent<SettingConfigurationViewModel>();
            if (cfg != null)
            {
                cfg._OnMoveLeft.AddListener((UnityAction)(() => Step(r, -1)));
                cfg._OnMoveRight.AddListener((UnityAction)(() => Step(r, 1)));
            }

            Refresh(r);
        }
    }

    [HarmonyPatch(typeof(GraphicsOptionsViewModel), "OnEnable")]
    internal static class ColourblindInjectPatch
    {
        [HarmonyPostfix]
        public static void Postfix(GraphicsOptionsViewModel __instance) => ColourblindOptions.Inject(__instance);
    }

    [HarmonyPatch(typeof(GraphicsOptionsViewModel), nameof(GraphicsOptionsViewModel.ResetToDefaults))]
    internal static class ColourblindResetPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => ColourblindOptions.ResetAll();
    }
}
