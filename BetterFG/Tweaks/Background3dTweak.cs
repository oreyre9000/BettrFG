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

        private const string RELEASE_URL = OnlineData.BaseUrl + "3DBackground/";
        private const string CATALOGUE_URL = RELEASE_URL + "backgrounds.json";
        internal const string ASKED_KEY = "tweak.background_3d.asked";

        internal static string BundleDir => OnlineData.Dir("3DBackground");

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
                    Hide = new[] { "LIGHTING" },
                    Skybox = "Assets/Material/Skybox_S04_Cyber_MAT.mat",
                    FogDensity = 0.0009f,
                    FogColor = new Color(0.6008f, 0.3314f, 0.7263f, 1f),
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

        internal static readonly string[] HideOnApply = { "CutoutSphere", "LIGHTING/Reflection_Probe" };

        internal static void TintS4Goop(GameObject terrain)
        {
            var goop = terrain.transform.Find("objects/PB_FallGuy_BackGround_Terrain_S04/terrain/ENV_Terrain_NonStatic_S04/Water");
            if (goop == null) { Plugin.Log.LogWarning("terrain_s4 has no goop lake where we expect it, old bundle? leaving it pink-less"); return; }
            var goopMat = goop.GetComponent<MeshRenderer>().material;
            goopMat.SetColor("_Color", new Color(0.4224f, 0.2684f, 0.3685f, 1f));
            goopMat.SetColor("_Color2", new Color(0.4455f, 0.3179f, 0.4026f, 1f));
            goopMat.SetColor("_DepthColor", new Color(0.4719f, 0.1936f, 0.3317f, 1f));
            goop.Find("VFX_GoopLakeEdge")?.GetComponent<MeshRenderer>().material
                .SetColor("_Color", new Color(0.2712f, 0.1348f, 0.4147f, 0.667f));
        }

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
        internal static readonly Dictionary<string, float> SizeMb = new Dictionary<string, float>();
        internal static event Action DownloadsChanged;
        private static readonly HashSet<string> _declined = new HashSet<string>();

        internal static string NameOf(string bundle)
        {
            foreach (var kv in Catalogue) if (kv.Value == bundle) return kv.Key;
            return bundle;
        }

        internal static bool Have(string bundle) => PackageFor(bundle) != null || File.Exists(Path.Combine(BundleDir, bundle));

        internal static bool Downloaded(string id)
        {
            if (PackageFor(id) is string package) return OnlineData.Installed("RoundBackgrounds/" + package);
            return File.Exists(Path.Combine(BundleDir, id));
        }

        internal static bool Outdated(string id) =>
            OnlineData.Outdated(PackageFor(id) is string package ? "RoundBackgrounds/" + package : "3DBackground/" + id);

        internal static void Uninstall(string id)
        {
            if (PackageFor(id) is string package) OnlineData.Uninstall("RoundBackgrounds/" + package);
            else
            {
                Delete(id);
                OnlineData.Uninstall("3DBackground/" + id);
            }
            DownloadSeq++;
            DownloadsChanged?.Invoke();
        }

        internal static string PackageFor(string key) => Customization.Round.CustomBackgroundEntry.ByKey(key)?.Package;

        internal static Vector3 RoundTerrainPos(string bundle) =>
            bundle == "terrain_s3" ? new Vector3(0f, -100f, 0f) : new Vector3(0f, bundle == "terrain_s5" ? -200f : -30f, 0f);

        private static readonly Dictionary<string, Texture2D> _previews = new Dictionary<string, Texture2D>();

        internal static Texture2D Preview(string key)
        {
            if (_previews.TryGetValue(key, out var tex)) return tex;
            if (key == Customization.Round.RoundBackgroundService.SkyBluePreview)
            {
                tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                tex.SetPixel(0, 0, Customization.Round.RoundBackgroundService.SkyBlue);
                tex.Apply();
                return _previews[key] = tex;
            }
            string path = OnlineData.Dir("3DBackground", "previews", key + ".jpg");
            if (!File.Exists(path))
            {
                if (!_previewsRetried && File.Exists(OnlineData.Stamp(PreviewsPackage)))
                {
                    Plugin.Log.LogWarning($"preview {key}.jpg is gone but the stamp says we have them, pulling the previews again");
                    File.Delete(OnlineData.Stamp(PreviewsPackage));
                    _previewsRetried = true;
                    _previewsFetched = false;
                }
                return null;
            }
            tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.LoadImage(File.ReadAllBytes(path));
            return _previews[key] = tex;
        }

        private const string PreviewsPackage = "3DBackground/previews";
        private static bool _previewsFetched;
        private static bool _previewsBusy;
        private static bool _previewsRetried;

        internal static IEnumerator FetchPreviews(Action onDone)
        {
            bool waited = _previewsBusy;
            while (_previewsBusy) yield return null;
            if (_previewsFetched)
            {
                if (waited) onDone?.Invoke();
                yield break;
            }

            _previewsBusy = true;
            string stamp = OnlineData.Stamp(PreviewsPackage);
            string before = File.Exists(stamp) ? File.ReadAllText(stamp) : null;
            try
            {
                for (int attempt = 0; attempt < 3 && !_previewsFetched; attempt++)
                {
                    if (attempt > 0) yield return new WaitForSecondsRealtime(3f);
                    yield return OnlineData.LoadManifest().WrapToIl2Cpp();
                    yield return OnlineData.Fetch(PreviewsPackage).WrapToIl2Cpp();
                    _previewsFetched = OnlineData.Ready(PreviewsPackage);
                }
            }
            finally { _previewsBusy = false; }

            if (!_previewsFetched) Plugin.Log.LogWarning("background previews didn't come down, trying again next time something asks");
            if (File.Exists(stamp) && File.ReadAllText(stamp) != before) onDone?.Invoke();
        }

        internal static void AskDownload(string bundle, string name, Action onReady, bool clicked = false)
        {
            if (clicked) _declined.Remove(bundle);
            if (Instance == null || _declined.Contains(bundle) || Downloading.ContainsKey(bundle) || LevelEditorRulebookViewModel.IsScreenOpen()) return;
            Instance.StartCoroutine(AskRoutine(bundle, name, onReady, false).WrapToIl2Cpp());
        }

        private static bool _rulebookPick;

        internal static void NoteRulebookPick() => _rulebookPick = true;

        internal static void OnRulebookClosed()
        {
            if (!_rulebookPick || Instance == null) return;
            string id = null, name = null;
            Action ready = null;
            if (Features.CreativeExpansion.Definers.FindEntry() is Features.CreativeExpansion.Definers.Entry e)
            {
                id = e.Package != null ? "round:" + e.Package : e.Bundle;
                name = e.Title;
                ready = Features.CreativeExpansion.Definers.Recheck;
            }
            else if (Instance.IsEnabled && _swap != null && !Features.CreativeExpansion.DisableBackgroundRulebook.IsDisabled())
            {
                id = _swap.Bundle;
                name = NameOf(id);
                ready = ApplyIfWanted;
            }
            if (id == null || Downloaded(id) || Downloading.ContainsKey(id)) { _rulebookPick = false; return; }
            Plugin.Log.LogInfo($"rulebook shut on {id}, which isn't downloaded, asking");
            Instance.StartCoroutine(AskRoutine(id, name, ready, true).WrapToIl2Cpp());
        }

        private static void ReopenRulebook() => Events.Broadcaster.Instance.Broadcast(new LevelEditorOpenRulebookEvent());

        private static IEnumerator AskRoutine(string bundle, string name, Action onReady, bool fromRulebook)
        {
            if (!SizeMb.ContainsKey(bundle)) yield return FetchCatalogue().WrapToIl2Cpp();
            string body = SizeMb.TryGetValue(bundle, out float mb)
                ? LocalizationService.Format("bg3d.download.body", name, mb.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
                : LocalizationService.Format("bg3d.download.body_nosize", name);
            Features.Onboarding.FullscreenPrompt.Ask("bg3d.download.title", body, "bg3d.download.confirm",
                () =>
                {
                    _rulebookPick = false;
                    Instance.StartCoroutine(FetchThen(bundle, onReady).WrapToIl2Cpp());
                    Customization.Round.BackgroundLibrary.Open();
                },
                onCancel: fromRulebook ? ReopenRulebook : () => _declined.Add(bundle));
        }

        private static IEnumerator FetchThen(string bundle, Action onReady)
        {
            yield return Fetch(bundle).WrapToIl2Cpp();
            if (Downloaded(bundle)) onReady?.Invoke();
        }

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
                if (string.IsNullOrEmpty(file)) continue;
                listed.Add(new KeyValuePair<string, string>(JsonUtil.GetValue(entry, "name"), file));
                if (float.TryParse(JsonUtil.GetValue(entry, "mb"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float mb))
                    SizeMb[file] = mb;

                if (Have(file) && Utilities.Bundles.Get(file) == null && File.Exists(OnlineData.Stamp("3DBackground/" + file)))
                    yield return OnlineData.Fetch("3DBackground/" + file).WrapToIl2Cpp();
            }

            if (listed.Count == 0) { Plugin.Log.LogWarning("backgrounds.json came down but parsed to nothing?"); onDone?.Invoke(); yield break; }

            Catalogue.Clear();
            Catalogue.AddRange(listed);
            Plugin.Log.LogInfo($"backgrounds.json: {Catalogue.Count} backdrops on the release");
            onDone?.Invoke();
        }

        public override List<TweakButton> GetCustomButtons() => new List<TweakButton>
        {
            new TweakButton { Label = "ui.configure", Width = 30f, OnClick = OpenConfig }
        };

        private void OpenConfig() => Customization.Round.BackgroundLibrary.Open();

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
        private static bool _packaged;
        private static int _gen;

        private static IEnumerator ApplyPackage(GameObject background, Customization.Round.CustomBackgroundEntry entry, int gen)
        {
            foreach (var path in HideOnApply)
            {
                var child = background.transform.Find(path);
                if (child == null || !child.gameObject.activeSelf) continue;
                child.gameObject.SetActive(false);
                _hidden.Add(child.gameObject);
            }
            _packaged = true;
            yield return Customization.Round.RoundBackgroundService.Apply(background.scene, "round:" + entry.Package, Customization.Round.RoundBackgroundService.Context.Creative).WrapToIl2Cpp();
            if (gen == _gen && Customization.Round.RoundBackgroundService.PackageRoot != null)
                Plugin.Log.LogInfo($"{background.name} wears the {entry.Package} package instead of {entry.Bundle}");
        }

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
            NavPromptCore.RegisterCmsString("bfg_bg3d_title", LocalizationService.Get("bg3d.consent_title"));
            NavPromptCore.RegisterCmsString("bfg_bg3d_body", LocalizationService.Get("bg3d.consent_body"));
            NavPromptCore.RegisterCmsString("bfg_bg3d_install", LocalizationService.Get("bg3d.consent_install"));
            NavPromptCore.RegisterCmsString("bfg_bg3d_disable", LocalizationService.Get("bg3d.consent_disable"));

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

        internal static IEnumerator Fetch(string id, bool update = false)
        {
            if ((Downloaded(id) && !(update && Outdated(id))) || Downloading.ContainsKey(id)) yield break;

            string round = PackageFor(id);
            string package = round != null ? "RoundBackgrounds/" + round : "3DBackground/" + id;
            if (round == null && !Downloaded(id) && File.Exists(OnlineData.Stamp(package))) File.Delete(OnlineData.Stamp(package));
            Downloading[id] = 0f;
            DownloadSeq++;
            DownloadsChanged?.Invoke();
            try { yield return OnlineData.Fetch(package, p => Downloading[id] = p).WrapToIl2Cpp(); }
            finally { Downloading.Remove(id); DownloadSeq++; DownloadsChanged?.Invoke(); }
        }

        public override void DisableTweak()
        {
            _gen++;
            if (_spawned != null) Destroy(_spawned);
            _spawned = null;
            if (_packaged) Customization.Round.RoundBackgroundService.Restore();
            _packaged = false;

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
            float far = _swap != null && PackageFor(_swap.Bundle) != null ? 20000f : (bgName != null && bgName.IndexOf("Volcano", StringComparison.Ordinal) >= 0) ? 25000f : 5000f;
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
            if (Customization.Round.RoundBackgroundService.Owns(root)) return;
            string raw = root != null ? root.name.Replace("(Clone)", "") : "";
            if (raw.Length > 0 && _seenBgNames.Add(raw))
                Plugin.Log.LogInfo($"theme background seen: {raw}");

            _envSaved = false;
            _prevSkybox = null;
            Instance?.DisableTweak();

            Resolve(root);
            ApplyFarClip(raw);
            ApplyIfWanted();
        }

        internal static void Cancel() => Instance?.DisableTweak();

        internal static void ApplyIfWanted()
        {
            var inst = Instance;
            if (inst == null || !inst.IsEnabled || _busy || _spawned != null || _packaged) return;
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
            var inner = ApplyRoutine();
            try { while (inner.MoveNext()) yield return inner.Current; }
            finally { _busy = false; }
        }

        private IEnumerator ApplyRoutine()
        {
            int gen = _gen;

            var background = _background;
            var swap = _swap;

            if (Customization.Round.CustomBackgroundEntry.ByKey(swap.Bundle) is Customization.Round.CustomBackgroundEntry entry)
            {
                if (!Downloaded(swap.Bundle))
                {
                    AskDownload(swap.Bundle, NameOf(swap.Bundle), ApplyIfWanted);
                    yield break;
                }
                _hidden.Clear();
                yield return ApplyPackage(background, entry, gen).WrapToIl2Cpp();
                yield break;
            }

            AssetBundle bundle = Utilities.Bundles.Get(swap.Bundle);
            if (bundle == null)
            {
                string path = Path.Combine(BundleDir, swap.Bundle);
                if (!File.Exists(path))
                {
                    AskDownload(swap.Bundle, NameOf(swap.Bundle), ApplyIfWanted);
                    yield break;
                }

                AssetBundle loaded = null;
                yield return Utilities.Bundles.LoadFile(swap.Bundle, path, ab => loaded = ab).WrapToIl2Cpp();
                bundle = loaded;
                if (bundle == null)
                {
                    Plugin.Log.LogWarning($"Background3dTweak: '{swap.Bundle}' didn't load as a bundle");
                    yield break;
                }
            }

            string prefabPath = null;
            foreach (string name in bundle.GetAllAssetNames())
                if (name.EndsWith(".prefab")) { prefabPath = name; break; }

            if (prefabPath == null)
            {
                Plugin.Log.LogWarning($"Background3dTweak: '{swap.Bundle}' holds no prefab");
                yield break;
            }

            var assetReq = bundle.LoadAssetAsync(prefabPath);
            yield return assetReq;

            var prefab = assetReq.asset != null ? assetReq.asset.TryCast<GameObject>() : null;
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"Background3dTweak: '{prefabPath}' isn't a prefab");
                yield break;
            }

            if (background == null || _spawned != null || gen != _gen) yield break;

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
                    if (background == null || gen != _gen) yield break;

                    if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
                        RenderSettings.skybox = handle.Result;
                    else
                        Plugin.Log.LogWarning($"addressables wouldn't hand over {swap.Skybox} ({handle.Status}), keeping the old skybox");
                }
            }

            bool inRound = !GameObjectHelper.IsMainMenuUp();

            var spawnPos = new Vector3(0f, swap.Bundle == "terrain_s5" ? -200f : -30f, 0f);
            var roundPos = RoundTerrainPos(swap.Bundle);

            _spawned = Instantiate(prefab);
            _spawned.name = swap.Bundle;
            _spawned.transform.Find("Directional Light")?.gameObject.SetActive(false);

            if (swap.Bundle == "terrain_s4") TintS4Goop(_spawned);
            if (inRound) _spawned.transform.position = roundPos;
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
        public static void Postfix()
        {
            Background3dTweak.ReapplyFarClip();
            Customization.Round.RoundBackgroundService.ReapplyFarClip();
        }
    }
}
