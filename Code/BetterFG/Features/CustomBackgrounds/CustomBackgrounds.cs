using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Tweaks;
using BetterFG.UI.Windows.Creative;
using BetterFG.Utilities;
using FG.Common;
using FG.Common.LevelEditor.Serialization;
using FGClient;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LevelEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BetterFG.Features.CustomBackgrounds
{
    internal static class Definers
    {
        internal readonly struct Entry
        {
            public readonly string Title;
            public readonly string Bundle;
            public readonly string Hex;
            public readonly Vector3 Position;
            public readonly Vector3 Rotation;
            public readonly string Skybox;
            public readonly string BaseLabel;
            public readonly bool HideLighting;
            public Entry(string title, string bundle, string hex, Vector3 position, Vector3 rotation, string skybox = null, string baseLabel = null, bool hideLighting = false)
            { Title = title; Bundle = bundle; Hex = hex; Position = position; Rotation = rotation; Skybox = skybox; BaseLabel = baseLabel; HideLighting = hideLighting; }
        }

        internal const string DefinerName = "Placeable_BasicBlocks_QuarterShape_VANILLA";

        private const float DefinerOffsetX = -500f;

        internal static readonly Vector3 NightTerrainSpawn = new Vector3(-15.3f, -22.3f, -29.8f);

        internal static readonly List<Entry> Catalog = new List<Entry>
        {
            new Entry("Beta - Day", "terrain_beta_day", "#3DFF90", new Vector3(DefinerOffsetX, 0f, 0f), Vector3.zero),
            new Entry("Beta - Night", "terrain_beta_night", "#3D90FF", new Vector3(DefinerOffsetX, 10f, 0f), new Vector3(0f, 45f, 0f), "Skybox_GeometricBlack_MAT", "Night", true),
        };

        internal static Entry? ByHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            foreach (var e in Catalog)
                if (string.Equals(e.Hex, hex, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        internal static Entry? ByBundle(string bundle)
        {
            if (string.IsNullOrEmpty(bundle)) return null;
            foreach (var e in Catalog)
                if (string.Equals(e.Bundle, bundle, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        internal static bool TryGetEntry(LevelEditorPlaceableObject lepo, out Entry entry)
        {
            entry = default;
            if (lepo == null || !lepo.name.StartsWith(DefinerName, StringComparison.Ordinal)) return false;
            var colour = lepo.GetComponent<LevelEditorColourChangerParameter>();
            if (colour == null) return false;
            var hit = ByHex(colour.CurrentColourHexcode);
            if (!hit.HasValue) return false;
            entry = hit.Value;
            return true;
        }

        internal static bool IsDefinerObject(LevelEditorPlaceableObject lepo) => TryGetEntry(lepo, out _);

        internal static LevelEditorPlaceableObject FindDefiner(out Entry entry)
        {
            foreach (var lepo in LevelIO.PlaceableObjects)
            {
                if (!TryGetEntry(lepo, out entry)) continue;
                BatchTargets.HideAndDecollide(lepo);
                return lepo;
            }
            entry = default;
            return null;
        }

        internal static bool HasDefiner() => FindDefiner(out _) != null;

        internal static Entry? FindEntry()
        {
            var lepo = FindDefiner(out var entry);
            if (lepo != null) return entry;

            foreach (var m in IdentifierObjects.ReadRound(DefinerName))
            {
                var hit = ByHex(m.Hex);
                if (hit.HasValue) return hit;
            }
            return null;
        }

        private static void ApplyDefiner(Entry? wanted)
        {
            var existingLepo = FindDefiner(out var existingEntry);
            if (existingLepo != null)
            {
                if (wanted.HasValue && existingEntry.Title == wanted.Value.Title) return;
                IdentifierObjects.Remove(existingLepo);
                Teardown();
            }

            if (wanted.HasValue)
            {
                var e = wanted.Value;
                IdentifierObjects.Spawn(DefinerName, e.Position, e.Rotation, Vector3.one, e.Hex);
            }
        }

        internal static string ResolveBaseId(LevelEditorOptionsSingleton singleton, Entry entry)
        {
            var options = singleton?.skyboxOptionsNew?.Options;
            if (options == null) return null;

            if (!string.IsNullOrEmpty(entry.BaseLabel))
                foreach (var opt in options)
                {
                    bool hit = (opt.Label != null && opt.Label.IndexOf(entry.BaseLabel, StringComparison.OrdinalIgnoreCase) >= 0) ||
                               (opt.SkyboxID != null && opt.SkyboxID.IndexOf(entry.BaseLabel, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!hit) continue;
                    Plugin.Log.LogInfo($"'{entry.Title}' base -> '{opt.SkyboxID}' (label '{opt.Label}', matched '{entry.BaseLabel}')");
                    return opt.SkyboxID;
                }

            foreach (var opt in options)
            {
                if (ByHex(opt.SkyboxID).HasValue) continue;
                Plugin.Log.LogInfo($"'{entry.Title}' base fell back to '{opt.SkyboxID}', no option matched '{entry.BaseLabel}' among {options.Length}");
                return opt.SkyboxID;
            }
            return null;
        }

        private static GameObject _spawned;
        private static Transform _hiddenCutout;
        private static Transform _hiddenLighting;
        private static Material _prevSkybox;
        private static bool _skyboxSaved;
        internal static GameObject Spawned => _spawned;
        private static float _prevReflectionIntensity;
        private static AmbientMode _prevAmbientMode;
        private static bool _envSaved;
        private static bool _busy;

        internal static void OnBackgroundRebuilt()
        {
            _skyboxSaved = false;
            _prevSkybox = null;
            Teardown();
        }

        internal static void Teardown()
        {
            if (_spawned != null) { _spawned.SetActive(false); UnityEngine.Object.Destroy(_spawned); }
            _spawned = null;

            if (_hiddenCutout != null) _hiddenCutout.gameObject.SetActive(true);
            _hiddenCutout = null;

            if (_hiddenLighting != null) _hiddenLighting.gameObject.SetActive(true);
            _hiddenLighting = null;

            if (_skyboxSaved) { RenderSettings.skybox = _prevSkybox; _skyboxSaved = false; _prevSkybox = null; }

            if (_envSaved)
            {
                RenderSettings.reflectionIntensity = _prevReflectionIntensity;
                RenderSettings.ambientMode = _prevAmbientMode;
                _envSaved = false;
            }
        }

        internal static bool TryApply(GameObject root)
        {
            if (root == null || GameObjectHelper.IsMainMenuUp()) return false;

            var entry = FindEntry();
            if (entry == null) return false;

            if (_busy || GameObject.Find(entry.Value.Bundle) != null) return true;

            _busy = true;
            Background3dTweak.Instance?.StartCoroutine(ApplyRoutine(root, entry.Value).WrapToIl2Cpp());
            return true;
        }

        internal static void Recheck()
        {
            if (_spawned != null) return;
            if (FindEntry() == null) return;

            Background3dTweak.Cancel();
            TryApply(ThemeManager._sceneBackgroundAndLighting);
        }

        private static IEnumerator ApplyRoutine(GameObject root, Entry entry)
        {
            try
            {
                AssetBundle bundle = BetterFG.Utilities.Bundles.Get(entry.Bundle);
                if (bundle == null)
                {
                    string path = Path.Combine(Background3dTweak.BundleDir, entry.Bundle);
                    if (!File.Exists(path)) yield return Background3dTweak.Fetch(entry.Bundle).WrapToIl2Cpp();
                    if (!File.Exists(path))
                    {
                        Plugin.Log.LogWarning($"custom background '{entry.Title}' wants {entry.Bundle}, download didn't land in {Background3dTweak.BundleDir}");
                        yield break;
                    }

                    AssetBundle loaded = null;
                    yield return BetterFG.Utilities.Bundles.LoadFile(entry.Bundle, path, ab => loaded = ab).WrapToIl2Cpp();
                    bundle = loaded;
                    if (bundle == null) { Plugin.Log.LogWarning($"{entry.Bundle} didn't load as a bundle"); yield break; }
                }

                string prefabPath = null;
                string skyboxPath = null;
                foreach (string name in bundle.GetAllAssetNames())
                {
                    if (prefabPath == null && name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) prefabPath = name;
                    if (entry.Skybox != null && skyboxPath == null && name.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) &&
                        name.IndexOf(entry.Skybox, StringComparison.OrdinalIgnoreCase) >= 0) skyboxPath = name;
                }

                if (prefabPath == null) { Plugin.Log.LogWarning($"{entry.Bundle} holds no prefab"); yield break; }

                var assetReq = bundle.LoadAssetAsync(prefabPath);
                yield return assetReq;

                var prefab = assetReq.asset != null ? assetReq.asset.TryCast<GameObject>() : null;
                if (prefab == null || root == null) yield break;

                if (GameObject.Find(entry.Bundle) != null)
                {
                    Plugin.Log.LogInfo($"{entry.Bundle} landed while we were loading it, not stacking a second one");
                    yield break;
                }

                if (skyboxPath != null)
                {
                    var matReq = bundle.LoadAssetAsync(skyboxPath);
                    yield return matReq;

                    var mat = matReq.asset != null ? matReq.asset.TryCast<Material>() : null;
                    if (mat != null)
                    {
                        if (!_skyboxSaved) { _prevSkybox = RenderSettings.skybox; _skyboxSaved = true; }
                        RenderSettings.skybox = mat;
                    }
                }

                var cutout = root.transform.Find("CutoutSphere");
                if (cutout != null) cutout.gameObject.SetActive(false);
                _hiddenCutout = cutout;

                if (entry.HideLighting)
                {
                    var lighting = root.transform.Find("LIGHTING");
                    if (lighting != null) lighting.gameObject.SetActive(false);
                    _hiddenLighting = lighting;

                    if (!_envSaved) { _prevReflectionIntensity = RenderSettings.reflectionIntensity; _prevAmbientMode = RenderSettings.ambientMode; _envSaved = true; }
                    RenderSettings.reflectionIntensity = 0f;
                    RenderSettings.ambientMode = AmbientMode.Flat;
                }

                var spawned = UnityEngine.Object.Instantiate(prefab, root.transform, true);
                spawned.name = entry.Bundle;
                if (entry.Bundle == "terrain_beta_night") spawned.transform.localPosition = NightTerrainSpawn;
                spawned.SetActive(true);
                _spawned = spawned;
                Plugin.Log.LogInfo($"'{entry.Title}' up under {root.name}");

                if (DisableBackgroundRulebook.IsDisabled()) DisableBackgroundRulebook.ReapplyHide(root);
            }
            finally { _busy = false; }
        }

        private static int _rowVmId = int.MinValue;

        internal static void TrackNativeRow(global::RulebookMenuCollectionBinding binding)
        {
            EnsureOptionsInjected();

            if (binding == null) return;
            Transform parent = null;
            try { parent = binding._itemsParent; } catch { }
            if (parent == null) return;

            foreach (var vm in parent.GetComponentsInChildren<LevelEditorRulebookEntryHorizontalListViewModel>(true))
            {
                string en = null;
                try { en = vm.EntryName; } catch { }
                if (en == "Background") { _rowVmId = vm.GetInstanceID(); return; }
            }
        }

        internal static bool IsNativeRowTarget(int instanceId) => instanceId == _rowVmId;

        internal static void SyncDefiner()
        {
            string currentId = null;
            try { currentId = LevelEditorOptionsSingleton.Instance?.skyboxOptionsNew?.GetValue()?.SkyboxID; } catch { }

            Entry? match = null;
            foreach (var e in Catalog)
                if (e.Hex == currentId) { match = e; break; }

            ApplyDefiner(match);
        }

        private static void EnsureOptionsInjected()
        {
            var set = LevelEditorOptionsSingleton.Instance?.skyboxOptionsNew;
            if (set == null) return;

            var merged = new List<LevelEditorOptionsSingleton.SkyboxSetting>();
            var existing = set.Options;
            if (existing != null) merged.AddRange(existing);

            bool changed = false;
            foreach (var entry in Catalog)
            {
                bool present = false;
                foreach (var opt in merged) if (opt.SkyboxID == entry.Hex) { present = true; break; }
                if (present) continue;

                merged.Add(new LevelEditorOptionsSingleton.SkyboxSetting(entry.Title, entry.Hex));
                changed = true;
            }

            if (!changed) return;
            set.Options = new Il2CppReferenceArray<LevelEditorOptionsSingleton.SkyboxSetting>(merged.ToArray());
        }
    }

    [HarmonyPatch(typeof(LevelEditorThemeLighting), nameof(LevelEditorThemeLighting.Start))]
    internal static class CustomBackgroundThemeLightingPatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorThemeLighting __instance)
        {
            var root = __instance.transform.root.gameObject;
            Definers.OnBackgroundRebuilt();
            DisableBackgroundRulebook.Sync(root);
            Definers.TryApply(root);
        }
    }

    [HarmonyPatch(typeof(UGCSkyboxOverrideHandler), nameof(UGCSkyboxOverrideHandler.SetSkybox))]
    internal static class CustomBackgroundSkyboxOverridePatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref string skyboxId)
        {
            var entry = Definers.ByHex(skyboxId);
            if (!entry.HasValue) return;

            string baseId = Definers.ResolveBaseId(LevelEditorOptionsSingleton.Instance, entry.Value);
            if (!string.IsNullOrEmpty(baseId)) skyboxId = baseId;
        }
    }

    [HarmonyPatch(typeof(LevelEditorOptionsSingleton), nameof(LevelEditorOptionsSingleton.GetSkyboxId))]
    internal static class CustomBackgroundIdSanitizePatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorOptionsSingleton __instance, ref string __result)
        {
            if (string.IsNullOrEmpty(__result)) return;
            var entry = Definers.ByHex(__result);
            if (!entry.HasValue) return;

            __result = Definers.ResolveBaseId(__instance, entry.Value);
        }
    }

    [HarmonyPatch(typeof(LevelEditorOptionsSingleton), nameof(LevelEditorOptionsSingleton.CurrentSkyboxOption), MethodType.Getter)]
    internal static class CustomBackgroundOptionSanitizePatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorOptionsSingleton __instance, ref LevelEditorOptionsSingleton.SkyboxSetting __result)
        {
            if (__result == null) return;
            var entry = Definers.ByHex(__result.SkyboxID);
            if (!entry.HasValue) return;

            string baseId = Definers.ResolveBaseId(__instance, entry.Value);
            if (string.IsNullOrEmpty(baseId)) return;

            foreach (var opt in __instance.skyboxOptionsNew.Options)
            {
                if (opt.SkyboxID != baseId) continue;
                __result = opt;
                break;
            }
        }
    }

    [HarmonyPatch(typeof(LevelSaver), nameof(LevelSaver.PopulateSchema))]
    internal static class CustomBackgroundSchemaSanitizePatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorOptionsSingleton options, UGCLevelDataSchema __result)
        {
            if (__result == null) return;
            var entry = Definers.ByHex(__result.SkyboxID);
            if (!entry.HasValue) return;

            string baseId = Definers.ResolveBaseId(options, entry.Value);
            if (string.IsNullOrEmpty(baseId)) return;

            __result.SkyboxID = baseId;
        }
    }
}
