using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using FGClient;
using UnityEngine;

namespace BetterFG.Customization.Menu
{
    public partial class MenuCustomizationApplication
    {
        public const string KEY_BG3D_SELECTED = "menu.bg3d.selected";

        public static readonly (string Id, string Label)[] Bg3dCatalog =
        {
            ("", "Normal"),
            ("Background_Vanilla_HEIGHT0", "Vanilla"),
            ("Background_S01", "Sky"),
            ("Background_S03", "Medieval 3D"),
            ("Background_S04", "Future"),
            ("Background_S07", "Stadium"),
            ("Background_Beach", "Beach"),
            ("Background_Festival", "Festival"),
            ("Background_Jungle_Ground_NoLake", "Jungle"),
            ("Background_Junkyard_Sunny_Ground", "Junkyard"),
            ("Background_DragonCave_GroundWithLake", "Dragon Cave"),
            ("Background_Medieval_Dusk_GroundNoLake", "Medieval"),
            ("Background_Medieval_Night_GroundNoLake", "Midnight"),
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

        private static bool IsMainMenuUp() =>
            GameObject.Find("MainMenuManager")?.GetComponent<MainMenuManager>() != null;

        private static MainMenuBackgroundViewModel FindMenuBackgroundVm() =>
            GameObject.Find("3D Environment")?.GetComponent<MainMenuBackgroundViewModel>();

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
        private static string _bg3dRequestedId;
        private static System.IntPtr _bg3dRequestedHandler;

        private static void RequestBaseSkybox(string wantId)
        {
            var handler = FindMenuBackgroundVm()?._skyboxHandler;
            if (wantId == null || handler == null || _bg3dRequestPending) return;
            if (handler._loadedSkyboxId == wantId) { _bg3dRequestedId = null; return; }
            if (_bg3dRequestedId == wantId && _bg3dRequestedHandler == handler.Pointer) return;
            _bg3dRequestedId = wantId;
            _bg3dRequestedHandler = handler.Pointer;
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

        public void RequestMenuBackground3D(string id)
        {
            SettingsService.Set(KEY_BG3D_SELECTED, id);
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
            if (!IsMainMenuUp()) return;

            string id = SettingsService.Get(KEY_BG3D_SELECTED, "");
            var vm = FindMenuBackgroundVm();
            if (vm == null) return;

            if (string.IsNullOrEmpty(id))
            {
                if (!_bg3dOverrideActive) return;

                DestroyStrayBackgroundClones(vm._skyboxHandler?.Skybox);
                RestoreBg3dEnv();
                if (vm.CanShow3DBackground && vm._mainMenuBackground != null)
                    RequestBaseSkybox(vm._mainMenuBackground.BackgroundName);
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

        private static bool _bg3dEnvSaved;
        private static Material _bg3dPrevSkybox;
        private static bool _bg3dPrevFog;
        private static float _bg3dPrevFogDensity;
        private static Color _bg3dPrevFogColor;
        private static float _bg3dPrevReflection;
        private static UnityEngine.Rendering.AmbientMode _bg3dPrevAmbientMode;

        internal static void OnBaseLightingApplied(GameObject root)
        {
            if (!IsMainMenuUp()) return;
            if (root.transform.parent != null || !root.name.StartsWith("Background_") || !root.name.EndsWith("(Clone)")) return;
            Instance?.ApplyAmbientFromSettings();
            Instance?.ApplySunFromSettings();
            Instance?.ApplyPatternFromSettings();
            Instance?.ApplyGradientFromSettings();
        }

        internal static void RewriteBaseSkyboxId(ref string skyboxId)
        {
            if (!IsMainMenuUp()) return;
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
            while (gen == _bg3dPollGen && IsMainMenuUp())
            {
                Instance?.ApplyAmbientFromSettings();
                Instance?.ApplySunFromSettings();
                yield return new WaitForSeconds(0.25f);
            }
        }

        internal static void ForgetBg3dEnv() => _bg3dEnvSaved = false;

        internal static void ReassertMenuBackground3D()
        {
            if (!IsMainMenuUp()) return;
            Instance?.ApplyMenuBackground3DFromSettings();
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
            if (!_bg3dEnvSaved) return;
            RenderSettings.skybox = _bg3dPrevSkybox;
            RenderSettings.fog = _bg3dPrevFog;
            RenderSettings.fogDensity = _bg3dPrevFogDensity;
            RenderSettings.fogColor = _bg3dPrevFogColor;
            RenderSettings.reflectionIntensity = _bg3dPrevReflection;
            RenderSettings.ambientMode = _bg3dPrevAmbientMode;

            var sky = FindMenuBackgroundVm()?._skyboxHandler?.Skybox;
            if (sky == null) return;
            var lighting = sky.GetComponent<LevelEditorThemeLighting>();
            if (lighting != null) RenderSettings.skybox = lighting._runtimeSkyboxMaterial != null ? lighting._runtimeSkyboxMaterial : lighting._skyboxMaterial;
        }
    }
}
