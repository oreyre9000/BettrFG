using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine;
using BetterFG.Services;
using BetterFG.Tweaks;
using FGClient;

namespace BetterFG.Customization.UI
{
    public partial class MenuCustomizationApplication
    {
        // ── Main menu 3D background ─────────────────────────────────────────────
        // two independent sources, picked by mode:
        //  - native: the game's own MainMenuBackgroundViewModel/MainMenuSkyboxHandler skybox swap
        //    (already has its own on/off in the game's Options — we never duplicate that toggle).
        //    "" means real/native: leave it alone entirely, or hand it back if we'd forced something.
        //  - custom: Background3dTweak's own downloaded terrain bundles, spawned world-anchored the
        //    same way the native skybox clones already are.

        public const string KEY_BG3D_MODE = "menu.bg3d.mode"; // "native" | "custom"
        public const string KEY_BG3D_SELECTED = "menu.bg3d.selected";
        public const string KEY_BG3D_CUSTOM_BUNDLE = "menu.bg3d.custom.bundle";

        // every Background_* addressable the currently-installed build actually ships, pulled off a
        // live catalog dump — same full 3D environments the native seasonal system swaps in.
        public static readonly (string Id, string Label)[] Bg3dCatalog =
        {
            ("", "bg3d.normal"),
            ("Background_Vanilla_HEIGHT0", "Vanilla"),
            ("Background_S01", "Season 1"),
            ("Background_S03", "Season 3"),
            ("Background_S04", "Season 4"),
            ("Background_S07", "Season 7"),
            ("Background_Beach", "Beach"),
            ("Background_Festival", "Festival"),
            ("Background_Jungle_Ground_NoLake", "Jungle"),
            ("Background_Junkyard_Sunny_Ground", "Junkyard"),
            ("Background_DragonCave_GroundWithLake", "Dragon Cave"),
            ("Background_Medieval_Dusk_GroundNoLake", "Medieval Dusk"),
            ("Background_Medieval_Night_GroundNoLake", "Medieval Night"),
            ("Background_Medieval_HEIGHT2", "Medieval"),
            ("Background_Winter_Snowy_GroundNoLake", "Winter Snowy"),
            ("Background_Winter_HEIGHT2", "Winter"),
            ("Background_Halloween_Night_GroundWithLake", "Halloween Night"),
            ("Background_Volcano_Sunset_RespawnSky", "Volcano Sunset"),
            ("Background_Space_Night", "Space"),
            ("Background_Neo_Tokyo_Ground", "Neo Tokyo"),
        };

        public static int Bg3dIndexOf(string id)
        {
            for (int i = 0; i < Bg3dCatalog.Length; i++)
                if (Bg3dCatalog[i].Id == id) return i;
            return 0;
        }

        private static MainMenuBackgroundViewModel FindMenuBackgroundVm() =>
            GameObject.Find("3D Environment")?.GetComponent<MainMenuBackgroundViewModel>();

        // stray Addressable clones can get orphaned if SetSkybox is called again before its own async
        // load finishes (cycling the carousel fast enough outruns MainMenuSkyboxHandler's own cleanup)
        // — sweep them before every apply so browsing quickly never leaks a live GameObject behind.
        private static void DestroyStrayBackgroundClones(GameObject keep)
        {
            foreach (var t in Object.FindObjectsOfType<Transform>())
            {
                if (t == null || t.parent != null || t.gameObject == keep) continue;
                if (t.name.StartsWith("Background_") && t.name.EndsWith("(Clone)"))
                    Object.Destroy(t.gameObject);
            }
        }

        private static bool _bg3dRequestPending;

        private static void RequestBaseSkybox(string wantId)
        {
            var handler = FindMenuBackgroundVm()?._skyboxHandler;
            if (wantId == null || handler == null || _bg3dRequestPending) return;
            if (handler._loadedSkyboxId == wantId) return;
            _bg3dRequestPending = true;
            Instance?.StartCoroutine(IssueBaseSkybox(wantId).WrapToIl2Cpp());
        }

        private static IEnumerator IssueBaseSkybox(string wantId)
        {
            yield return null;
            var handler = FindMenuBackgroundVm()?._skyboxHandler;
            if (handler != null && handler._loadedSkyboxId != wantId) handler.SetSkybox(wantId);
            _bg3dRequestPending = false;
        }

        private bool _bg3dOverrideActive;
        private Coroutine _bg3dApplyRoutine;
        private int _bg3dGen;
        private string _bg3dLoadingBundle;
        private static Material _bg3dSkyboxMat;

        public void RequestMenuBackground3D(string id)
        {
            SettingsService.Set(KEY_BG3D_MODE, "native");
            SettingsService.Set(KEY_BG3D_SELECTED, id);
            if (_bg3dApplyRoutine != null) StopCoroutine(_bg3dApplyRoutine);
            _bg3dApplyRoutine = StartCoroutine(DebouncedApplyBg3d().WrapToIl2Cpp());
        }

        public void RequestMenuBackgroundCustomBundle(string bundle)
        {
            SettingsService.Set(KEY_BG3D_MODE, "custom");
            SettingsService.Set(KEY_BG3D_CUSTOM_BUNDLE, bundle);
            if (_bg3dApplyRoutine != null) StopCoroutine(_bg3dApplyRoutine);
            _bg3dApplyRoutine = StartCoroutine(DebouncedApplyBg3d().WrapToIl2Cpp());
        }

        private IEnumerator DebouncedApplyBg3d()
        {
            yield return new WaitForSeconds(0.2f);
            _bg3dApplyRoutine = null;
            ApplyMenuBackground3DFromSettings();
        }

        public void ApplyMenuBackground3DFromSettings()
        {
            // a round's own backdrop root is a Background_*(Clone) too, and its loader owns
            // RenderSettings — none of this may run outside the menu.
            if (!BetterFG.Utilities.GameObjectHelper.IsMainMenuUp()) return;

            if (SettingsService.Get(KEY_BG3D_MODE, "native") == "custom")
            {
                string bundle = SettingsService.Get(KEY_BG3D_CUSTOM_BUNDLE, "");
                if (string.IsNullOrEmpty(bundle)) return;

                if (FindMenuBackgroundVm()?.CanShow3DBackground == false)
                {
                    _bg3dGen++;
                    DestroyCustomBg3d();
                    RestoreBg3dEnv();
                    ApplyPatternFromSettings();
                    ApplyGradientFromSettings();
                    return;
                }

                if (_bg3dLoadingBundle == bundle) return;

                if (_customBg3d != null && _customBg3d.name == bundle)
                {
                    var live = FindMenuBackgroundVm();
                    if (live == null) return;

                    var def = BetterFG.Features.CreativeExpansion.Definers.ByBundle(bundle);
                    string wantId = def.HasValue ? MenuBaseForDefiner(def.Value) : NativeIdForBundle(bundle);

                    RequestBaseSkybox(wantId);

                    ReapplyBg3dOverrides();
                    ApplyPatternFromSettings();
                    ApplyGradientFromSettings();
                    return;
                }

                StartCoroutine(ApplyCustomBundleRoutine(bundle, ++_bg3dGen).WrapToIl2Cpp());
                return;
            }

            _bg3dGen++;
            DestroyCustomBg3d();

            string id = SettingsService.Get(KEY_BG3D_SELECTED, "");
            var vm = FindMenuBackgroundVm();
            if (vm == null) return;

            if (string.IsNullOrEmpty(id))
            {
                if (!_bg3dOverrideActive) return; // never touched it this session — leave native alone

                DestroyStrayBackgroundClones(vm._skyboxHandler?.Skybox);
                RestoreBg3dEnv();
                if (vm.CanShow3DBackground && vm._mainMenuBackground != null)
                {
                    RequestBaseSkybox(vm._mainMenuBackground.BackgroundName);
                }
                else vm.Show3DBackground = false;
                _bg3dOverrideActive = false;
            }
            else
            {
                SaveBg3dEnv();
                DestroyStrayBackgroundClones(vm._skyboxHandler?.Skybox);

                if (!vm.CanShow3DBackground)
                {
                    vm.Show3DBackground = false;
                    _bg3dOverrideActive = false;
                }
                else
                {
                    if (vm._skyboxHandler._loadedSkyboxId != id)
                    {
                        RestoreBg3dEnv();
                        RequestBaseSkybox(id);
                    }
                    _bg3dOverrideActive = true;
                }
            }

            ApplyPatternFromSettings();
            ApplyGradientFromSettings();
        }

        // ── Custom (Background3dTweak bundle) source ────────────────────────────

        private static GameObject _customBg3d;

        public static bool IsCustomBg3dActive => _customBg3d != null;

        private static void DestroyCustomBg3d()
        {
            if (_customBg3d != null) Object.Destroy(_customBg3d);
            _customBg3d = null;
        }

        private static bool _bg3dEnvSaved;
        private static Material _bg3dPrevSkybox;
        private static bool _bg3dPrevFog;
        private static float _bg3dPrevFogDensity;
        private static Color _bg3dPrevFogColor;
        private static float _bg3dPrevReflection;
        private static UnityEngine.Rendering.AmbientMode _bg3dPrevAmbientMode;
        private static readonly List<GameObject> _bg3dHiddenBaseParts = new List<GameObject>();

        private static void HideBaseParts(Transform root, bool hideLighting)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "CutoutSphere" && !(hideLighting && t.name == "LIGHTING")) continue;
                if (!t.gameObject.activeSelf) continue;
                t.gameObject.SetActive(false);
                _bg3dHiddenBaseParts.Add(t.gameObject);
            }
        }

        internal static void OnBaseLightingApplied(GameObject root)
        {
            if (!BetterFG.Utilities.GameObjectHelper.IsMainMenuUp()) return;
            if (root.transform.parent != null || !root.name.StartsWith("Background_") || !root.name.EndsWith("(Clone)")) return;
            ReapplyBg3dOverrides(root);
            Instance?.ApplyPatternFromSettings();
            Instance?.ApplyGradientFromSettings();
        }

        internal static void RewriteBaseSkyboxId(ref string skyboxId)
        {
            if (!BetterFG.Utilities.GameObjectHelper.IsMainMenuUp()) return;

            if (SettingsService.Get(KEY_BG3D_MODE, "native") == "custom")
            {
                if (_customBg3d == null) return;
                var definer = BetterFG.Features.CreativeExpansion.Definers.ByBundle(_customBg3d.name);
                string wantId = definer.HasValue ? MenuBaseForDefiner(definer.Value) : NativeIdForBundle(_customBg3d.name);
                if (wantId != null) skyboxId = wantId;
                return;
            }

            string selected = SettingsService.Get(KEY_BG3D_SELECTED, "");
            if (!string.IsNullOrEmpty(selected)) skyboxId = selected;
        }

        private static int _bg3dPollGen;

        internal static void StartBg3dPoll()
        {
            Instance?.StartCoroutine(Bg3dPollLoop(++_bg3dPollGen).WrapToIl2Cpp());
        }

        private static IEnumerator Bg3dPollLoop(int gen)
        {
            while (gen == _bg3dPollGen && BetterFG.Utilities.GameObjectHelper.IsMainMenuUp())
            {
                if (_customBg3d == null && SettingsService.Get(KEY_BG3D_MODE, "native") == "custom")
                    Instance?.ApplyMenuBackground3DFromSettings();
                else
                    ReapplyBg3dOverrides();
                yield return new WaitForSeconds(0.25f);
            }
        }

        internal static void ReapplyBg3dOverrides(GameObject root = null)
        {
            if (!BetterFG.Utilities.GameObjectHelper.IsMainMenuUp()) return;

            if (_customBg3d != null && SettingsService.Get(KEY_BG3D_MODE, "native") == "custom")
            {
                var definer = BetterFG.Features.CreativeExpansion.Definers.ByBundle(_customBg3d.name);
                string wantId = definer.HasValue ? MenuBaseForDefiner(definer.Value) : NativeIdForBundle(_customBg3d.name);
                var bg = root != null ? root : GameObject.Find(wantId + "(Clone)");

                if (bg != null && bg.name == wantId + "(Clone)")
                {
                    HideBaseParts(bg.transform, definer.HasValue && definer.Value.HideLighting);
                    if (definer.HasValue) ApplyDefinerOverrides(definer.Value, _bg3dSkyboxMat);
                }
                else RequestBaseSkybox(wantId);
            }

            Instance?.ApplyAmbientFromSettings();
            Instance?.ApplySunFromSettings();
        }

        internal static void ForgetBg3dEnv()
        {
            _bg3dEnvSaved = false;
            _bg3dHiddenBaseParts.Clear();
        }

        internal static void ReassertMenuBackground3D()
        {
            if (!BetterFG.Utilities.GameObjectHelper.IsMainMenuUp()) return;
            Instance?.ApplyMenuBackground3DFromSettings();
        }

        private static string MenuBaseForDefiner(BetterFG.Features.CreativeExpansion.Definers.Entry e)
        {
            if (!string.IsNullOrEmpty(e.BaseLabel))
                foreach (var entry in Bg3dCatalog)
                    if (entry.Id.Length > 0 && entry.Id.IndexOf(e.BaseLabel, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return entry.Id;
            return Bg3dCatalog[1].Id;
        }

        private static string NativeIdForBundle(string bundle)
        {
            foreach (var kv in Background3dTweak.SwapFor)
            {
                if (kv.Value.Bundle != bundle) continue;
                var want = kv.Key.Split('_');
                foreach (var entry in Bg3dCatalog)
                {
                    if (entry.Id.Length == 0) continue;
                    var tokens = entry.Id.Substring("Background_".Length).Split('_');
                    int hits = 0;
                    foreach (var w in want) if (System.Array.IndexOf(tokens, w) >= 0) hits++;
                    if (hits == want.Length) return entry.Id;
                }
            }
            return null;
        }

        private static void SaveBg3dEnv()
        {
            if (_bg3dEnvSaved) return;
            _bg3dPrevSkybox = RenderSettings.skybox;
            _bg3dPrevFog = RenderSettings.fog;
            _bg3dPrevFogDensity = RenderSettings.fogDensity;
            _bg3dPrevFogColor = RenderSettings.fogColor;
            _bg3dPrevReflection = RenderSettings.reflectionIntensity;
            _bg3dPrevAmbientMode = RenderSettings.ambientMode;
            _bg3dEnvSaved = true;
        }

        private static void RestoreBg3dEnv()
        {
            foreach (var go in _bg3dHiddenBaseParts)
                if (go != null) go.SetActive(true);
            _bg3dHiddenBaseParts.Clear();

            if (!_bg3dEnvSaved) return;
            RenderSettings.skybox = _bg3dPrevSkybox;
            RenderSettings.fog = _bg3dPrevFog;
            RenderSettings.fogDensity = _bg3dPrevFogDensity;
            RenderSettings.fogColor = _bg3dPrevFogColor;
            RenderSettings.reflectionIntensity = _bg3dPrevReflection;
            RenderSettings.ambientMode = _bg3dPrevAmbientMode;
        }

        private static void ApplyDefinerOverrides(BetterFG.Features.CreativeExpansion.Definers.Entry e, Material skyboxMat)
        {
            if (skyboxMat != null) RenderSettings.skybox = skyboxMat;
            if (!e.HideLighting) return;

            RenderSettings.reflectionIntensity = 0f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        }

        private IEnumerator ApplyCustomBundleRoutine(string bundle, int gen)
        {
            _bg3dLoadingBundle = bundle;
            try
            {
                string path = Path.Combine(Background3dTweak.BundleDir, bundle);
                if (!File.Exists(path)) yield return Background3dTweak.Fetch(bundle).WrapToIl2Cpp();
                if (gen != _bg3dGen) yield break;
                if (!File.Exists(path))
                {
                    Plugin.Log.LogWarning($"menu 3d background: {bundle} didn't download, leaving it as-is");
                    yield break;
                }

                AssetBundle ab = BetterFG.Utilities.Bundles.Get(bundle);
                if (ab == null)
                {
                    AssetBundle loaded = null;
                    yield return BetterFG.Utilities.Bundles.LoadFile(bundle, path, x => loaded = x).WrapToIl2Cpp();
                    ab = loaded;
                    if (ab == null) { Plugin.Log.LogWarning($"menu 3d background: '{bundle}' didn't load as a bundle"); yield break; }
                    if (gen != _bg3dGen) yield break;
                }

                var definer = BetterFG.Features.CreativeExpansion.Definers.ByBundle(bundle);

                string prefabPath = null, skyboxPath = null;
                foreach (var name in ab.GetAllAssetNames())
                {
                    if (prefabPath == null && name.EndsWith(".prefab")) prefabPath = name;
                    if (definer.HasValue && definer.Value.Skybox != null && skyboxPath == null &&
                        name.EndsWith(".mat", System.StringComparison.OrdinalIgnoreCase) &&
                        name.IndexOf(definer.Value.Skybox, System.StringComparison.OrdinalIgnoreCase) >= 0) skyboxPath = name;
                }
                if (prefabPath == null) { Plugin.Log.LogWarning($"menu 3d background: '{bundle}' holds no prefab"); yield break; }

                var assetReq = ab.LoadAssetAsync(prefabPath);
                yield return assetReq;
                if (gen != _bg3dGen) { Plugin.Log.LogInfo($"dropped stale menu backdrop {bundle}, something else got picked while it loaded"); yield break; }
                var prefab = assetReq.asset != null ? assetReq.asset.TryCast<GameObject>() : null;
                if (prefab == null) yield break;

                Material skyboxMat = null;
                if (skyboxPath != null)
                {
                    var matReq = ab.LoadAssetAsync(skyboxPath);
                    yield return matReq;
                    if (gen != _bg3dGen) yield break;
                    skyboxMat = matReq.asset.TryCast<Material>();
                }

                SaveBg3dEnv();
                RestoreBg3dEnv();
                DestroyCustomBg3d();

                string nativeId = definer.HasValue ? MenuBaseForDefiner(definer.Value) : NativeIdForBundle(bundle);
                var vm = FindMenuBackgroundVm();
                if (vm != null)
                {
                    DestroyStrayBackgroundClones(vm._skyboxHandler?.Skybox);
                    if (nativeId != null) RequestBaseSkybox(nativeId);
                    else
                    {
                        vm._skyboxHandler?.ForceStopLoadingCoroutine();
                        vm._skyboxHandler?.ForceClearLoadedSkybox();
                        Plugin.Log.LogInfo($"{bundle} has no matching game background, terrain goes on the default sky");
                    }
                }

                _customBg3d = Instantiate(prefab);
                _customBg3d.name = bundle;
                if (bundle == "terrain_s5")
                {
                    _customBg3d.transform.position = new Vector3(180.6322f, -184.2911f, 255.3165f);
                    _customBg3d.transform.eulerAngles = new Vector3(351.9116f, 0f, 0f);
                }
                else if (bundle == "terrain_s4")
                {
                    _customBg3d.transform.position = new Vector3(40.3547f, -9.0254f, 161.61f);
                    _customBg3d.transform.eulerAngles = new Vector3(352.0271f, 0f, 0f);
                    _customBg3d.transform.localScale = Vector3.one;
                    Object.Destroy(_customBg3d.transform.Find("Clouds_GRP3/ENV_RoundCloud_Large01").gameObject);
                    foreach (var light in _customBg3d.GetComponentsInChildren<Light>(true))
                        Object.Destroy(light);
                    foreach (var probe in _customBg3d.GetComponentsInChildren<ReflectionProbe>(true))
                        Object.Destroy(probe);
                }
                else if (bundle.StartsWith("terrain_beta"))
                {
                    _customBg3d.transform.localPosition = bundle == "terrain_beta_night"
                        ? BetterFG.Features.CreativeExpansion.Definers.NightTerrainSpawn
                        : new Vector3(-7.4413f, 135.4576f, 108.0769f);
                    _customBg3d.transform.Find("Plane").gameObject.SetActive(false);
                }
                _customBg3d.SetActive(true);
                _bg3dOverrideActive = true;

                ApplyPatternFromSettings();
                ApplyGradientFromSettings();
                Plugin.Log.LogInfo($"menu backdrop {bundle} up, base {nativeId ?? "none"}");

                _bg3dSkyboxMat = skyboxMat;
                ReapplyBg3dOverrides();
            }
            finally
            {
                if (_bg3dLoadingBundle == bundle) _bg3dLoadingBundle = null;
            }
        }
    }
}
