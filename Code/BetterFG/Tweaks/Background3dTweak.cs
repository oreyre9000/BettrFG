using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.Utilities;
using FallGuysLib.UI;
using FGClient.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace BetterFG.Tweaks
{
    public class Background3dTweak : BfgTweak
    {
        public Background3dTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "background_3d";
        public override string TweakLabel => "tweak.2d_to_3d_background";
        public override string TweakTooltip => "ui.swaps_the_flat_backdrop_of_a_creative_level_for";
        public override bool DefaultEnabled => true;

        private const string RELEASE_URL = "https://github.com/oreyre9000/BettrFG/releases/download/3dbackgrounds/";
        private const string CATALOGUE_URL = RELEASE_URL + "backgrounds.json";
        internal const string ASKED_KEY = "tweak.background_3d.asked";

        internal static string BundleDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BettrFG", "Assets", "Bundles");

        internal class BackgroundSwap
        {
            public string Bundle;
            public string Sun;
            public Vector3? SunRotation;
            public float? SunIntensity;
            public string Skybox;
            public float? FogDensity;
            public Color? FogColor;
            public float? ProbeSize;
            public string[] Hide;
            public string[] Keep;
        }

        internal static readonly Dictionary<string, BackgroundSwap> SwapFor = new Dictionary<string, BackgroundSwap>
        {
            { "Vanilla", new BackgroundSwap { Bundle = "terrain_s1" } },
            { "Medieval_Dusk", new BackgroundSwap { Bundle = "terrain_s2" } },
            {
                "Winter", new BackgroundSwap
                {
                    Bundle = "terrain_s3",
                    Sun = "LIGHTING/Lights_Winter/SUN_RT_Winter",
                    SunRotation = new Vector3(89.1542f, 347.1031f, 240.518f),
                    Hide = new[] { "LIGHTING/Lights_Winter/DL_BL_RT_Winter" },
                }
            },
            {
                "S04", new BackgroundSwap
                {
                    Bundle = "terrain_s4",
                    Sun = "LIGHTING/SUN_RT_Future",
                    SunIntensity = 0.7f,
                    Skybox = "Skybox/Materials/Skybox_Respawn_S04.mat",
                    FogDensity = 0.0007f,
                    FogColor = new Color(0.6008f, 0.3314f, 0.7263f, 1f),
                    ProbeSize = 4000f,
                }
            },
            {
                "Halloween", new BackgroundSwap
                {
                    Bundle = "terrain_ss",
                    Sun = "LIGHTING/SUN_RT_Halloween",
                    FogColor = new Color(0.329f, 0.2553f, 0.4808f, 1),
                    FogDensity = 0.001f,
                    SunIntensity = 0.7f
                }
            },
            {
                "Junkyard", new BackgroundSwap
                {
                    Bundle = "terrain_sc",
                    FogColor = new Color(0.9132f, 0.6164f, 0.347f, 1),
                    FogDensity = 0.001f
                }
            },
            {
                "Night", new BackgroundSwap
                {
                    Bundle = "terrain_sn",
                    Sun = "LIGHTING/SUN_RT_Halloween",
                    SunIntensity = 0.6f
                }
            },
            {
                "Jungle", new BackgroundSwap
                {
                    Bundle = "terrain_s5",
                    Keep = new[] { "LIGHTING/Reflection_Probe" },
                    FogDensity = 0.0009f,
                }
            },
            {
                "Volcano", new BackgroundSwap
                {
                    Bundle = "terrain_sv",
                    FogColor = new Color(0.969f, 0.309f, 0.769f, 1),
                    FogDensity = 0.0001f
                }
            },
        };

        private static readonly string[] HideOnApply = { "CutoutSphere", "LIGHTING/Reflection_Probe" };

        public static Background3dTweak Instance { get; private set; }

        // bundle -> 0..1 while it's coming down. absent means idle, which is also what the config
        // window keys its per-row progress bar off.
        internal static readonly Dictionary<string, float> Downloading = new Dictionary<string, float>();
        // bumped whenever a bundle enters or leaves that table, so the config window can redraw its
        // rows when FetchAll walks the list on its own with nothing of ours behind it
        internal static int DownloadSeq;

        // display name -> bundle filename, as listed by backgrounds.json on the release. seeded from
        // SwapFor so the config window is never empty, then overwritten by whatever the release says.
        internal static readonly List<KeyValuePair<string, string>> Catalogue = new List<KeyValuePair<string, string>>();

        internal static IEnumerator FetchCatalogue(Action onDone = null)
        {
            Catalogue.Clear();
            var seen = new HashSet<string>();
            foreach (var kv in SwapFor)
                if (seen.Add(kv.Value.Bundle))
                    Catalogue.Add(new KeyValuePair<string, string>(kv.Key.Replace("_", " "), kv.Value.Bundle));

            var req = UnityWebRequest.Get(CATALOGUE_URL);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Plugin.Log.LogWarning($"backgrounds.json wouldn't load ({req.error}) — showing the {Catalogue.Count} baked into this build");
                req.Dispose();
                onDone?.Invoke();
                yield break;
            }

            string json = req.downloadHandler.text ?? "";
            req.Dispose();

            var listed = new List<KeyValuePair<string, string>>();
            foreach (var entry in JsonUtil.GetRootArray(json))
            {
                string file = JsonUtil.GetValue(entry, "file");
                if (!string.IsNullOrEmpty(file))
                    listed.Add(new KeyValuePair<string, string>(JsonUtil.GetValue(entry, "name"), file));
            }

            if (listed.Count == 0) { Plugin.Log.LogWarning("backgrounds.json came down but parsed to nothing?"); onDone?.Invoke(); yield break; }

            Catalogue.Clear();
            Catalogue.AddRange(listed);
            Plugin.Log.LogInfo($"backgrounds.json: {Catalogue.Count} backdrops on the release");
            onDone?.Invoke();
        }

        public override List<TweakButton> GetCustomButtons() => new List<TweakButton>
        {
            new TweakButton { Label = "ui.cfg", Width = 30f, OnClick = OpenConfig }
        };

        // the config window rides the Tweaks sidewheel slot, so its back link lands somewhere sane.
        // going through OpenWindow covers the popup case too, where nothing's open to swap out of yet.
        private void OpenConfig()
        {
            var wheel = BetterFG.UI.SideWheel.SideWheelManager.Instance;
            if (wheel == null) return;
            wheel.OpenWindow<BetterFG.UI.Windows.TweaksWindow>("sidewheel.tweaks",
                _ => wheel.SwapWindow(BetterFG.UI.Windows.BetterFGWindow.Spawn<BetterFG.UI.Windows.Background3dWindow>()));
        }

        private static GameObject _background;
        private static BackgroundSwap _swap;
        private static GameObject _spawned;
        private static readonly List<GameObject> _hidden = new List<GameObject>();
        private static Transform _sun;
        private static Quaternion _sunRotation;
        private static Light _sunLight;
        private static float _sunIntensity;
        private static bool _envSaved;
        private static Material _prevSkybox;
        private static float _prevFogDensity;
        private static Color _prevFogColor;
        private static bool _busy;
        private static int _gen;

        void Awake() => Instance = this;

        // no blanket top-up here — that would quietly pull back anything binned from the config
        // window. Apply still fetches the one bundle a level actually needs.
        public override void EnableTweak() => ApplyIfWanted();

        internal static void Delete(string bundle)
        {
            if (_spawned != null && _spawned.name == bundle) Instance?.DisableTweak();

            Utilities.Bundles.Unload(bundle, true);

            try { File.Delete(Path.Combine(BundleDir, bundle)); Plugin.Log.LogInfo($"binned {bundle}"); }
            catch (Exception ex) { Plugin.Log.LogWarning($"{bundle} wouldn't delete: {ex.Message}"); }
        }

        public override void OnMainMenuEntered()
        {
            if (SettingsService.Get(ASKED_KEY, "false") == "true") return;
            if (!IsInvoking("AskConsent")) Invoke("AskConsent", 2f);
        }

        public void AskConsent()
        {
            NavPromptCore.RegisterCmsString("bfg_bg3d_title", "2D To 3D Background");
            NavPromptCore.RegisterCmsString("bfg_bg3d_body",
                "The \"2D To 3D Background\" tweak is automatically enabled and requires about 40-50 MB of files to be downloaded, would you like to proceed or disable the tweak for now?\n\nThe tweak turns the creative 2D backgrounds into 3D ones.");
            NavPromptCore.RegisterCmsString("bfg_bg3d_install", "Install");
            NavPromptCore.RegisterCmsString("bfg_bg3d_disable", "Disable");

            PopUp.ShowPopup("bfg_bg3d_title", "bfg_bg3d_body",
                PopupInteractionType.Query, UIModalMessage.ModalType.MT_OK_CANCEL, UIModalMessage.OKButtonType.CallToAction,
                ok =>
                {
                    SettingsService.Set(ASKED_KEY, "true");
                    if (ok) { StartCoroutine(FetchAll().WrapToIl2Cpp()); OpenConfig(); }
                    else { SetEnabled(false); Plugin.Log.LogInfo("3d backdrops turned down, nothing downloaded"); }
                },
                "bfg_bg3d_install", "bfg_bg3d_disable");
        }

        private IEnumerator FetchAll()
        {
            var seen = new HashSet<string>();
            foreach (var swap in SwapFor.Values)
            {
                if (!seen.Add(swap.Bundle)) continue;
                yield return Fetch(swap.Bundle).WrapToIl2Cpp();
            }
            ApplyIfWanted();
        }

        internal static IEnumerator Fetch(string bundle)
        {
            string dir = BundleDir;
            Directory.CreateDirectory(dir);

            string path = Path.Combine(dir, bundle);
            if (File.Exists(path) || Downloading.ContainsKey(bundle)) yield break;

            Downloading[bundle] = 0f;
            DownloadSeq++;
            try
            {
                var req = UnityWebRequest.Get(RELEASE_URL + bundle);
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    Downloading[bundle] = req.downloadProgress;
                    yield return null;
                }

                var result = req.result;
                byte[] data = result == UnityWebRequest.Result.Success ? req.downloadHandler.data : null;
                req.Dispose();

                if (data == null || data.Length == 0)
                {
                    Plugin.Log.LogWarning($"{bundle} wouldn't come down from the release ({result})");
                    yield break;
                }

                Downloading[bundle] = 1f;
                string tmp = path + ".dl";
                var write = System.Threading.Tasks.Task.Run(() => { try { File.WriteAllBytes(tmp, data); return true; } catch { return false; } });
                while (!write.IsCompleted) yield return null;

                if (!write.Result) { Plugin.Log.LogWarning($"{bundle} downloaded but wouldn't write into {dir}"); yield break; }

                try { if (File.Exists(path)) File.Delete(path); File.Move(tmp, path); } catch { }
                Plugin.Log.LogInfo($"{bundle} down, {data.Length / 1048576f:0.#}MB");
            }
            finally { Downloading.Remove(bundle); DownloadSeq++; }
        }

        public override void DisableTweak()
        {
            _gen++;
            if (_spawned != null) Destroy(_spawned);
            _spawned = null;

            foreach (var go in _hidden)
                if (go != null) go.SetActive(true);
            _hidden.Clear();

            if (_sun != null) _sun.localRotation = _sunRotation;
            _sun = null;

            if (_sunLight != null) _sunLight.intensity = _sunIntensity;
            _sunLight = null;

            if (!_envSaved) return;
            RenderSettings.skybox = _prevSkybox;
            RenderSettings.fogDensity = _prevFogDensity;
            RenderSettings.fogColor = _prevFogColor;
            _prevSkybox = null;
            _envSaved = false;
        }

        private static readonly HashSet<string> _blockTokens = new HashSet<string>(StringComparer.Ordinal)
        {
            "Space", "Midnight"
        };

        private static bool Resolve(GameObject root)
        {
            _background = null;
            _swap = null;
            if (root == null) return false;

            string bgName = root.name.Replace("(Clone)", "");
            if (!bgName.StartsWith("Background_"))
            {
                Plugin.Log.LogInfo($"'{root.name}' isn't a Background_ root, no 3d terrain to map onto it");
                return false;
            }

            var tokens = bgName.Substring("Background_".Length).Split('_');

            foreach (var t in tokens)
                if (_blockTokens.Contains(t))
                {
                    Plugin.Log.LogInfo($"3d backdrop skipped for {bgName} (blocked token '{t}')");
                    return false;
                }

            int best = 0;
            foreach (var kv in SwapFor)
            {
                var want = kv.Key.Split('_');
                int hits = 0;
                foreach (var w in want) if (Array.IndexOf(tokens, w) >= 0) hits++;
                if (hits == want.Length && hits > best) { best = hits; _swap = kv.Value; }
            }

            if (_swap == null)
            {
                Plugin.Log.LogInfo($"no 3d backdrop mapped for {bgName}");
                return false;
            }

            _background = root;
            return true;
        }

        private static readonly HashSet<string> _seenBgNames = new HashSet<string>();

        private static string _cachedBgName;

        internal static void ApplyFarClip(string bgName)
        {
            _cachedBgName = bgName;
            float far = (bgName != null && bgName.IndexOf("Volcano", StringComparison.Ordinal) >= 0) ? 25000f : 5000f;
            Instance?.StartCoroutine(ApplyFarClipRoutine(far, bgName).WrapToIl2Cpp());
        }

        internal static void ReapplyFarClip() { if (_cachedBgName != null) ApplyFarClip(_cachedBgName); }

        private static IEnumerator ApplyFarClipRoutine(float far, string bgName)
        {
            yield return null;

            var node = GameObject.Find("LevelEditorCameraNode");
            if (node != null) node.GetComponentInChildren<Camera>().farClipPlane = far;

            var director = FallGuysLib.Camera.CameraLocator.GetCameraDirector();
            var freelook = director != null ? director._closeCamera : null;
            if (freelook == null) { Plugin.Log.LogWarning($"far clip: no close camera freelook for {bgName}"); yield break; }

            var lens = freelook.m_Lens;
            lens.FarClipPlane = far;
            freelook.m_Lens = lens;

            Plugin.Log.LogInfo($"far clip -> {far} for {bgName} (readback={freelook.m_Lens.FarClipPlane})");
        }

        internal static void OnThemeLighting(LevelEditorThemeLighting settings)
        {
            if (GameObjectHelper.IsMainMenuUp()) return;

            var root = settings.transform.root.gameObject;
            string raw = root != null ? root.name.Replace("(Clone)", "") : "";
            if (raw.Length > 0 && _seenBgNames.Add(raw))
                Plugin.Log.LogInfo($"theme background seen: {raw}");

            ApplyFarClip(raw);

            _envSaved = false;
            _prevSkybox = null;
            Instance?.DisableTweak();

            Resolve(root);
            ApplyIfWanted();
        }

        internal static void Cancel() => Instance?.DisableTweak();

        internal static void ApplyIfWanted()
        {
            var inst = Instance;
            if (inst == null || !inst.IsEnabled || _busy || _spawned != null) return;
            if (GameObjectHelper.IsMainMenuUp()) return;

            if (BetterFG.Features.UnityRound.BetterFGUnityRounds.RoundLoadingOrActive) return;
            if (Features.UnityRound.Editor.UnityRoundLoader.HasSpawned && Features.UnityRound.Editor.UnityRoundLoader.HideBackground) return;

            if (BetterFG.Features.CreativeExpansion.Definers.HasDefiner())
            {
                Plugin.Log.LogInfo("a custom theme owns this level, our 3d terrain stays out of it");
                return;
            }
            if (BetterFG.Features.CreativeExpansion.DisableBackgroundRulebook.IsDisabled()) return;
            if (_background == null && ThemeManager._sceneBackgroundAndLighting == null)
            {
                Plugin.Log.LogWarning("3d backdrop wanted but there's no background root to hang it on");
                return;
            }
            if (_background == null && !Resolve(ThemeManager._sceneBackgroundAndLighting)) return;

            inst.StartCoroutine(inst.Apply().WrapToIl2Cpp());
        }

        private IEnumerator Apply()
        {
            _busy = true;
            int gen = _gen;

            var background = _background;
            var swap = _swap;

            AssetBundle bundle = Utilities.Bundles.Get(swap.Bundle);
            if (bundle == null)
            {
                string path = Path.Combine(BundleDir, swap.Bundle);
                if (!File.Exists(path)) yield return Fetch(swap.Bundle).WrapToIl2Cpp();
                if (!File.Exists(path))
                {
                    Plugin.Log.LogWarning($"no {swap.Bundle} in {BundleDir} and the download didn't land, backdrop stays flat");
                    _busy = false;
                    yield break;
                }

                AssetBundle loaded = null;
                yield return Utilities.Bundles.LoadFile(swap.Bundle, path, ab => loaded = ab).WrapToIl2Cpp();
                bundle = loaded;
                if (bundle == null)
                {
                    Plugin.Log.LogWarning($"Background3dTweak: '{swap.Bundle}' didn't load as a bundle");
                    _busy = false;
                    yield break;
                }
            }

            string prefabPath = null;
            foreach (string name in bundle.GetAllAssetNames())
                if (name.EndsWith(".prefab")) { prefabPath = name; break; }

            if (prefabPath == null)
            {
                Plugin.Log.LogWarning($"Background3dTweak: '{swap.Bundle}' holds no prefab");
                _busy = false;
                yield break;
            }

            var assetReq = bundle.LoadAssetAsync(prefabPath);
            yield return assetReq;

            var prefab = assetReq.asset != null ? assetReq.asset.TryCast<GameObject>() : null;
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"Background3dTweak: '{prefabPath}' isn't a prefab");
                _busy = false;
                yield break;
            }

            if (background == null || _spawned != null || gen != _gen) { _busy = false; yield break; }

            _hidden.Clear();
            foreach (var paths in new[] { HideOnApply, swap.Hide })
            {
                if (paths == null) continue;
                foreach (var path in paths)
                {
                    if (swap.Keep != null && Array.IndexOf(swap.Keep, path) >= 0) continue;

                    var child = background.transform.Find(path);
                    if (child == null || !child.gameObject.activeSelf) continue;
                    child.gameObject.SetActive(false);
                    _hidden.Add(child.gameObject);
                }
            }

            if (swap.Sun != null)
            {
                _sun = background.transform.Find(swap.Sun);
                if (_sun == null) Plugin.Log.LogWarning($"no {swap.Sun} under {background.name}, leaving the sun where it is");
                else
                {
                    _sunRotation = _sun.localRotation;
                    if (swap.SunRotation.HasValue) _sun.localEulerAngles = swap.SunRotation.Value;

                    if (swap.SunIntensity.HasValue)
                    {
                        _sunLight = _sun.GetComponent<Light>();
                        if (_sunLight != null)
                        {
                            _sunIntensity = _sunLight.intensity;
                            _sunLight.intensity = swap.SunIntensity.Value;
                        }
                    }
                }
            }

            if (swap.FogDensity.HasValue || swap.FogColor.HasValue || swap.Skybox != null)
            {
                if (!_envSaved)
                {
                    _prevSkybox = RenderSettings.skybox;
                    _prevFogDensity = RenderSettings.fogDensity;
                    _prevFogColor = RenderSettings.fogColor;
                    _envSaved = true;
                }

                if (swap.FogDensity.HasValue) RenderSettings.fogDensity = swap.FogDensity.Value;
                if (swap.FogColor.HasValue) RenderSettings.fogColor = swap.FogColor.Value;

                if (swap.Skybox != null)
                {
                    var handle = Addressables.LoadAssetAsync<Material>(swap.Skybox);
                    while (!handle.IsDone) yield return null;

                    if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
                        RenderSettings.skybox = handle.Result;
                    else
                        Plugin.Log.LogWarning($"addressables wouldn't hand over {swap.Skybox} ({handle.Status}), keeping the old skybox");
                }
            }

            bool inRound = !GameObjectHelper.IsMainMenuUp();

            var spawnPos = new Vector3(0f, swap.Bundle == "terrain_s5" ? -200f : -30f, 0f);

            _spawned = Instantiate(prefab);
            _spawned.name = swap.Bundle;
            if (inRound) _spawned.transform.position = spawnPos;
            else { _spawned.transform.SetParent(background.transform, false); _spawned.transform.localPosition = spawnPos; }
            _spawned.SetActive(true);

            if (swap.ProbeSize.HasValue)
            {
                var probeGo = new GameObject("BFG_Probe");
                probeGo.transform.SetParent(_spawned.transform, false);

                var probe = probeGo.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Realtime;
                probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
                probe.clearFlags = ReflectionProbeClearFlags.Skybox;
                probe.resolution = 256;
                probe.size = Vector3.one * swap.ProbeSize.Value;
                probe.RenderProbe();

                Plugin.Log.LogInfo($"baked a {swap.ProbeSize.Value} probe off the new skybox, texture {(probe.texture != null ? probe.texture.name : "null")}");
            }

            Plugin.Log.LogInfo($"Background3dTweak: {swap.Bundle} in for {background.name}{(inRound ? ", loose in the scene since we're in a round" : "")}");
            _busy = false;
        }
    }

    [Utilities.BfgPatchGate("tweak.background_3d", defaultOn: true)]
    [HarmonyPatch(typeof(LevelEditorThemeLighting), nameof(LevelEditorThemeLighting.Start))]
    public class Background3dThemeLightingPatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorThemeLighting __instance) => Background3dTweak.OnThemeLighting(__instance);
    }

    [HarmonyPatch(typeof(FG.Common.CameraDirector), nameof(FG.Common.CameraDirector.UseCloseShot))]
    public class Background3dCloseShotPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => Background3dTweak.ReapplyFarClip();
    }
}
