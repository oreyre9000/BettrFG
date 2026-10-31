using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Customization.UI;
using BetterFG.Features.Assets;
using BetterFG.Services;
using BetterFG.Tweaks;
using BetterFG.Utilities;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

namespace BetterFG.Customization.Round
{
    public static class RoundBackgroundService
    {
        public const string KeyPrefix = "round.bg.";
        public const string QualScene = "FallGuy_Qualification40_scene";
        private const int QualBgLayer = 6;
        private const int QualBgMask = (1 << QualBgLayer) | (1 << 4);
        public const string SkyBluePick = "sky:blue";
        public const string VanillaSkyPick = "sky:vanilla";
        public const string SkyBluePreview = "sky_blue";
        public static readonly Color SkyBlue = new Color(0.55f, 0.8f, 1f);

        public struct Option
        {
            public string Key, Label, Preview, Group;
        }

        private class Snapshot
        {
            public Material Skybox;
            public bool Fog;
            public FogMode FogMode;
            public Color FogColor, AmbientSky, AmbientEquator, AmbientGround;
            public float FogDensity, FogStart, FogEnd, AmbientIntensity, ReflectionIntensity, CameraFar, LensFar;
            public Color CameraBg;
            public UnityEngine.Rendering.AmbientMode AmbientMode;
            public UnityEngine.Rendering.DefaultReflectionMode ReflectionMode;
            public Texture CustomReflection;
            public Light Sun;
            public float[] Probe;
            public LightmapData[] Lightmaps;
            public LightProbes LightProbes;
        }

        private const float FarClip = 20000f;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly HashSet<int> _spawnedBases = new HashSet<int>();
        private static readonly List<GameObject> _hidden = new List<GameObject>();
        private static readonly List<GameObject> _spawned = new List<GameObject>();
        private static readonly List<Light> _lightsOff = new List<Light>();
        private static readonly List<(Light Light, int Mask)> _lightsMasked = new List<(Light, int)>();
        private static readonly List<ReflectionProbe> _probesOff = new List<ReflectionProbe>();
        private static Snapshot _snap;
        private static Scene _extraScene;
        private static int _sceneHandle = -1;
        private static int _gen;
        private static float _farClip;
        private static int _qualHandle = -1;

        public const string ModeKey = "round_bg.mode";
        public static readonly string[] Modes = { "off", "manual", "auto" };
        public static string Mode => SettingsService.Get(ModeKey, "manual");
        public static string CurrentScene { get; private set; }
        public static event Action CurrentChanged;
        private static string _autoPick;

        public static string Picked(string scene) => SettingsService.Get(KeyPrefix + scene, "");

        private static string Effective(string scene) => BetaDoorDash.Pick(scene) ?? BetaBlockParty.Pick(scene) ?? (Mode == "off" ? "" : Mode == "auto" ? _autoPick ??= RollAuto() : Picked(scene));

        internal static string IdOf(string key) =>
            key.StartsWith("round:") ? key
            : key.StartsWith("bettrfg:") || key.StartsWith("scene:") ? key.Substring(key.IndexOf(':') + 1)
            : null;

        private static string RollAuto()
        {
            var ready = new List<string>();
            foreach (var o in Options())
                if (o.Key.Length > 0 && (IdOf(o.Key) is not string id || Background3dTweak.Downloaded(id))) ready.Add(o.Key);
            string pick = ready.Count > 0 ? ready[UnityEngine.Random.Range(0, ready.Count)] : "";
            Plugin.Log.LogInfo($"auto background rolled {pick} out of {ready.Count}");
            return pick;
        }

        internal static void OnLoadingStarted(FG.Common.CMS.Round round)
        {
            CurrentScene = round?.GetSceneName();
            BetaDoorDash.Roll(CurrentScene);
            BetaBlockParty.Roll(CurrentScene);
            _autoPick = null;
            Applied = "";
            CurrentChanged?.Invoke();
            if (string.IsNullOrEmpty(CurrentScene) || !SceneExport.IsUnityRound(CurrentScene) || PackageOf(Effective(CurrentScene)) is not string package) return;
            AssetManager.Instance.StartCoroutine(OnlineData.Fetch("RoundBackgrounds/" + package).WrapToIl2Cpp());
        }

        private static string PackageOf(string pick)
        {
            if (pick.StartsWith("bettrfg:") && CustomBackgroundEntry.ByKey(pick.Substring(8)) is CustomBackgroundEntry mapped) return mapped.Package;
            return pick.StartsWith("round:") ? pick.Substring(6) : null;
        }

        internal static void OnShutdown()
        {
            CurrentScene = null;
            CurrentChanged?.Invoke();
        }

        public static void SetMode(string mode)
        {
            SettingsService.Set(ModeKey, mode);
            string active = SceneManager.GetActiveScene().name;
            Reapply(active.StartsWith("FallGuy_Qualification") ? QualScene : active);
        }

        internal static bool Owns(GameObject root) => root != null && _spawnedBases.Contains(root.GetInstanceID());

        internal static GameObject PackageRoot { get; private set; }

        private sealed class Look
        {
            public Material Sky;
            public bool Fog;
            public FogMode FogMode;
            public Color FogColor, AmbientSky, AmbientEquator, AmbientGround;
            public float FogDensity, FogStart, FogEnd, AmbientIntensity, ReflectionIntensity;
            public UnityEngine.Rendering.AmbientMode AmbientMode;
            public float[] AmbientProbe;
            public Light Sun;
            public Texture Reflection;
            public LightmapData[] Lightmaps;
        }

        private static Look _look;

        internal static void ReassertLook()
        {
            if (PackageRoot == null || _look == null) return;
            RenderSettings.skybox = _look.Sky;
            RenderSettings.fog = _look.Fog;
            RenderSettings.fogMode = _look.FogMode;
            RenderSettings.fogColor = _look.FogColor;
            RenderSettings.fogDensity = _look.FogDensity;
            RenderSettings.fogStartDistance = _look.FogStart;
            RenderSettings.fogEndDistance = _look.FogEnd;
            RenderSettings.sun = _look.Sun;
            RenderSettings.ambientMode = _look.AmbientMode;
            RenderSettings.ambientSkyColor = _look.AmbientSky;
            RenderSettings.ambientEquatorColor = _look.AmbientEquator;
            RenderSettings.ambientGroundColor = _look.AmbientGround;
            RenderSettings.ambientIntensity = _look.AmbientIntensity;
            RenderSettings.reflectionIntensity = _look.ReflectionIntensity;
            if (_look.AmbientProbe != null) Ss2RoundLookTweak.WriteAmbientProbe(_look.AmbientProbe);
            if (LightmapSettings.lightmaps.Length != _look.Lightmaps.Length) LightmapSettings.lightmaps = _look.Lightmaps;
            if (_look.Reflection == null) return;
            RenderSettings.customReflectionTexture = _look.Reflection;
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
        }

        public static List<Option> Options()
        {
            var list = new List<Option>
            {
                new Option { Key = "", Label = "round_bg.default", Group = "" },
                new Option { Key = SkyBluePick, Label = "round_bg.sky_blue", Preview = SkyBluePreview, Group = "native" },
                new Option { Key = VanillaSkyPick, Label = "round_bg.sky_vanilla", Preview = "native_Background_Vanilla_HEIGHT0", Group = "native" },
            };
            foreach (var n in MenuCustomizationApplication.Bg3dCatalog)
                if (n.Id.Length > 0) list.Add(new Option { Key = "native:" + n.Id, Label = n.Label, Preview = "native_" + n.Id, Group = "native" });
            foreach (var kv in Background3dTweak.Catalogue)
                list.Add(new Option { Key = "bettrfg:" + kv.Value, Label = kv.Key, Preview = kv.Value, Group = "bettrfg" });
            foreach (var e in Features.CreativeExpansion.Definers.Catalog)
                if (e.SceneName != null) list.Add(new Option { Key = "scene:" + e.Bundle, Label = e.Title, Preview = e.Bundle, Group = "bettrfg" });
            foreach (var e in CustomBackgroundEntry.All)
                if (e.Bundle == null) list.Add(new Option { Key = "round:" + e.Package, Label = e.Label, Preview = "round_" + e.Package, Group = "bettrfg" });
            return list;
        }

        public static void Choose(string scene, string key)
        {
            SettingsService.Set(KeyPrefix + scene, key);
            if (Mode == "manual") Reapply(scene);
        }

        private static void Reapply(string scene)
        {
            var active = SceneManager.GetActiveScene();
            bool qual = scene == QualScene && active.name.StartsWith("FallGuy_Qualification");
            if ((!qual && (active.name != scene || !SceneExport.IsUnityRound(scene))) || !active.isLoaded) return;
            Restore();
            string key = Effective(scene);
            if (key.Length > 0) AssetManager.Instance.StartCoroutine(Apply(active, key, qual ? Context.Qualification : Context.Round).WrapToIl2Cpp());
            Plugin.Log.LogInfo($"{scene} background swapped live to {(key.Length > 0 ? key : "its own")}");
        }

        internal static string Applied { get; private set; } = "";

        internal static void OnRoundLoaded()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.handle != _sceneHandle) Forget();
            if (!SceneExport.IsUnityRound(scene.name)) return;
            string pick = Effective(scene.name);
            Applied = pick;
            if (pick.Length == 0) return;
            AssetManager.Instance.StartCoroutine(Apply(scene, pick).WrapToIl2Cpp());
        }

        internal static void OnReplayLoaded(string pick)
        {
            Forget();
            if (string.IsNullOrEmpty(pick)) return;
            AssetManager.Instance.StartCoroutine(Apply(SceneManager.GetActiveScene(), pick).WrapToIl2Cpp());
        }

        internal static void OnQualScreen(Scene scene)
        {
            if (scene.handle == _qualHandle) return;
            _qualHandle = scene.handle;
            Forget();
            string pick = Effective(QualScene);
            if (pick.Length == 0) return;
            AssetManager.Instance.StartCoroutine(Apply(scene, pick, Context.Qualification).WrapToIl2Cpp());
        }

        private static void Forget()
        {
            BackgroundWire.Raise(BackgroundKind.PackageGone, null);
            _gen++;
            _snap = null;
            _sceneHandle = -1;
            _hidden.Clear();
            _spawned.Clear();
            _lightsOff.Clear();
            _probesOff.Clear();
            _lightsMasked.Clear();
            _farClip = 0f;
            PackageRoot = null;
        }

        private static void TakeSnapshot(Scene scene)
        {
            if (_snap != null && _sceneHandle == scene.handle) return;
            _sceneHandle = scene.handle;
            var cam = Camera.main;
            var lens = FallGuysLib.Camera.CameraLocator.GetCameraDirector()?._closeCamera;
            _snap = new Snapshot
            {
                Skybox = RenderSettings.skybox, Fog = RenderSettings.fog, FogMode = RenderSettings.fogMode,
                FogColor = RenderSettings.fogColor, FogDensity = RenderSettings.fogDensity,
                FogStart = RenderSettings.fogStartDistance, FogEnd = RenderSettings.fogEndDistance,
                AmbientMode = RenderSettings.ambientMode, AmbientSky = RenderSettings.ambientSkyColor,
                AmbientEquator = RenderSettings.ambientEquatorColor, AmbientGround = RenderSettings.ambientGroundColor,
                AmbientIntensity = RenderSettings.ambientIntensity, ReflectionIntensity = RenderSettings.reflectionIntensity,
                ReflectionMode = RenderSettings.defaultReflectionMode, CustomReflection = RenderSettings.customReflectionTexture,
                Sun = RenderSettings.sun, Probe = Ss2RoundLookTweak.ReadAmbientProbe(), Lightmaps = LightmapSettings.lightmaps,
                LightProbes = LightmapSettings.lightProbes,
                CameraFar = cam != null ? cam.farClipPlane : 0f, CameraBg = cam != null ? cam.backgroundColor : Color.black, LensFar = lens != null ? lens.m_Lens.FarClipPlane : 0f,
            };
        }

        public static void Restore()
        {
            BackgroundWire.Raise(BackgroundKind.PackageGone, null);
            _gen++;
            foreach (var go in _spawned) if (go != null) UnityEngine.Object.Destroy(go);
            foreach (var go in _hidden) if (go != null) go.SetActive(true);
            foreach (var l in _lightsOff) if (l != null) l.enabled = true;
            foreach (var p in _probesOff) if (p != null) p.enabled = true;
            foreach (var m in _lightsMasked) if (m.Light != null) m.Light.cullingMask = m.Mask;
            _spawned.Clear();
            _hidden.Clear();
            _lightsOff.Clear();
            _probesOff.Clear();
            _lightsMasked.Clear();
            _farClip = 0f;
            PackageRoot = null;
            if (_extraScene.IsValid() && _extraScene.isLoaded) SceneManager.UnloadSceneAsync(_extraScene);
            _extraScene = default;
            bool alive = false;
            for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).handle == _sceneHandle) alive = true;
            if (_snap == null || !alive) return;

            RenderSettings.skybox = _snap.Skybox;
            RenderSettings.fog = _snap.Fog;
            RenderSettings.fogMode = _snap.FogMode;
            RenderSettings.fogColor = _snap.FogColor;
            RenderSettings.fogDensity = _snap.FogDensity;
            RenderSettings.fogStartDistance = _snap.FogStart;
            RenderSettings.fogEndDistance = _snap.FogEnd;
            RenderSettings.ambientMode = _snap.AmbientMode;
            RenderSettings.ambientSkyColor = _snap.AmbientSky;
            RenderSettings.ambientEquatorColor = _snap.AmbientEquator;
            RenderSettings.ambientGroundColor = _snap.AmbientGround;
            RenderSettings.ambientIntensity = _snap.AmbientIntensity;
            RenderSettings.reflectionIntensity = _snap.ReflectionIntensity;
            RenderSettings.defaultReflectionMode = _snap.ReflectionMode;
            RenderSettings.customReflectionTexture = _snap.CustomReflection;
            RenderSettings.sun = _snap.Sun;
            if (_snap.Probe != null) Ss2RoundLookTweak.WriteAmbientProbe(_snap.Probe);
            LightmapSettings.lightmaps = _snap.Lightmaps;
            LightmapSettings.lightProbes = _snap.LightProbes;
            DynamicGI.UpdateEnvironment();

            var cam = Camera.main;
            if (cam != null && _snap.CameraFar > 0f) cam.farClipPlane = _snap.CameraFar;
            if (cam != null) cam.backgroundColor = _snap.CameraBg;
            var freelook = FallGuysLib.Camera.CameraLocator.GetCameraDirector()?._closeCamera;
            if (freelook != null && _snap.LensFar > 0f)
            {
                var lens = freelook.m_Lens;
                lens.FarClipPlane = _snap.LensFar;
                freelook.m_Lens = lens;
            }
        }

        public enum Context { Round, Creative, Menu, Qualification }

        public static IEnumerator Apply(Scene scene, string pick, Context context = Context.Round)
        {
            int gen = ++_gen;
            if (PackageOf(pick) is string name)
            {
                if (!OnlineData.Ready("RoundBackgrounds/" + name)) yield return OnlineData.Fetch("RoundBackgrounds/" + name).WrapToIl2Cpp();
                string dir = OnlineData.Dir("RoundBackgrounds", name);
                if (!File.Exists(Path.Combine(dir, "layout.txt"))) { Plugin.Log.LogWarning($"round background {name} isn't on disk and didn't download, {scene.name} keeps its own"); yield break; }
                if (gen != _gen || !scene.isLoaded) yield break;
                TakeSnapshot(scene);
                StripHost(scene, context);
                BuildPackage(scene, dir, context != Context.Menu);
                var entry = CustomBackgroundEntry.ByPackage(name);
                if (PackageRoot != null && entry?.For(context) is BackgroundTransform placed) placed.ApplyTo(PackageRoot.transform);
                if (PackageRoot != null && entry?.Hide != null)
                    foreach (var path in entry.Hide) PackageRoot.transform.Find(path)?.gameObject.SetActive(false);
                if (PackageRoot != null && entry != null && entry.NoShadows)
                    foreach (var r in PackageRoot.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var cam = Camera.main;
                if (context == Context.Menu && PackageRoot != null && cam != null)
                    foreach (var t in PackageRoot.GetComponentsInChildren<Transform>(true))
                        if ((cam.cullingMask & (1 << t.gameObject.layer)) == 0) t.gameObject.layer = 0;
                if (context == Context.Qualification && PackageRoot != null) IsolateQual(PackageRoot);
                if (PackageRoot != null) BackgroundWire.Raise(BackgroundKind.Package, name, PackageRoot);
                yield break;
            }

            if (pick.StartsWith("scene:"))
            {
                yield return ApplySceneBundle(scene, pick.Substring(6), gen).WrapToIl2Cpp();
                yield break;
            }

            if (pick == SkyBluePick)
            {
                TakeSnapshot(scene);
                RenderSettings.skybox = null;
                if (Camera.main != null) Camera.main.backgroundColor = SkyBlue;
                Plugin.Log.LogInfo($"{scene.name} sky is plain blue now");
                yield break;
            }

            bool skyOnly = pick == VanillaSkyPick;
            if (skyOnly) pick = "native:Background_Vanilla_HEIGHT0";

            string bundle = pick.StartsWith("bettrfg:") ? pick.Substring(8) : null;
            if (bundle != null && !Background3dTweak.Have(bundle))
            {
                Background3dTweak.AskDownload(bundle, Background3dTweak.NameOf(bundle), OnRoundLoaded);
                yield break;
            }
            string baseId = bundle != null ? MenuCustomizationApplication.BaseIdForBundle(bundle) : pick.Substring(7);
            if (string.IsNullOrEmpty(baseId)) { Plugin.Log.LogWarning($"no game background to sit {bundle} on, leaving {scene.name} alone"); yield break; }

            var op = Addressables.LoadAssetAsync<GameObject>(baseId);
            while (!op.IsDone) yield return null;
            if (op.Status != AsyncOperationStatus.Succeeded || op.Result == null) { Plugin.Log.LogWarning($"addressables wouldn't hand over {baseId}"); yield break; }
            if (gen != _gen || !scene.isLoaded) yield break;

            TakeSnapshot(scene);
            var anchor = StripHost(scene, context);
            var baseGo = UnityEngine.Object.Instantiate(op.Result);
            _spawnedBases.Add(baseGo.GetInstanceID());
            _spawned.Add(baseGo);
            SceneManager.MoveGameObjectToScene(baseGo, scene);
            baseGo.transform.position += anchor;
            var sun = baseGo.GetComponentInChildren<Light>();
            if (sun != null) RenderSettings.sun = sun;
            if (skyOnly) foreach (var r in baseGo.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            if (context == Context.Qualification) IsolateQual(baseGo);
            ReapplyFarClip();

            if (bundle == null)
            {
                yield return null;
                yield return null;
                if (gen != _gen) yield break;
                SkyLighting();
                Plugin.Log.LogInfo($"{scene.name} now sits on {baseId}");
                yield break;
            }

            AssetBundle ab = Bundles.Get(bundle);
            if (ab == null)
            {
                AssetBundle loaded = null;
                yield return Bundles.LoadFile(bundle, Path.Combine(Background3dTweak.BundleDir, bundle), x => loaded = x).WrapToIl2Cpp();
                ab = loaded;
                if (ab == null) { Plugin.Log.LogWarning($"{bundle} didn't load as a bundle, {scene.name} only gets the sky"); yield break; }
            }
            string prefabPath = null;
            foreach (var n in ab.GetAllAssetNames()) if (n.EndsWith(".prefab")) { prefabPath = n; break; }
            var req = ab.LoadAssetAsync(prefabPath);
            yield return req;
            var prefab = req.asset != null ? req.asset.TryCast<GameObject>() : null;
            if (prefab == null || gen != _gen || !scene.isLoaded) yield break;

            var terrain = UnityEngine.Object.Instantiate(prefab);
            terrain.name = bundle;
            _spawned.Add(terrain);
            SceneManager.MoveGameObjectToScene(terrain, scene);
            terrain.transform.position = BetaDoorDash.Owns(scene.name) ? BetaDoorDash.TerrainPos : Background3dTweak.RoundTerrainPos(bundle) + anchor;
            terrain.transform.Find("Directional Light")?.gameObject.SetActive(false);
            if (bundle == "terrain_s4") Background3dTweak.TintS4Goop(terrain);

            yield return null;
            yield return null;
            if (gen != _gen || baseGo == null) yield break;
            Background3dTweak.BackgroundSwap swap = null;
            foreach (var kv in Background3dTweak.SwapFor) if (kv.Value.Bundle == bundle) { swap = kv.Value; break; }
            foreach (var paths in new[] { Background3dTweak.HideOnApply, swap?.Hide })
                if (paths != null)
                    foreach (var path in paths)
                        if (swap?.Keep == null || Array.IndexOf(swap.Keep, path) < 0) baseGo.transform.Find(path)?.gameObject.SetActive(false);
            if (swap != null)
            {
                var sunT = swap.Sun != null ? baseGo.transform.Find(swap.Sun) : null;
                if (sunT != null && swap.SunRotation.HasValue) sunT.localEulerAngles = swap.SunRotation.Value;
                var sunLight = sunT != null ? sunT.GetComponent<Light>() : null;
                if (sunLight != null && swap.SunIntensity.HasValue) sunLight.intensity = swap.SunIntensity.Value;
                if (swap.FogDensity.HasValue) RenderSettings.fogDensity = swap.FogDensity.Value;
                if (swap.FogColor.HasValue) RenderSettings.fogColor = swap.FogColor.Value;
                if (swap.Skybox != null)
                {
                    var sky = Addressables.LoadAssetAsync<Material>(swap.Skybox);
                    while (!sky.IsDone) yield return null;
                    if (gen != _gen) yield break;
                    if (sky.Status == AsyncOperationStatus.Succeeded && sky.Result != null) RenderSettings.skybox = sky.Result;
                }
            }
            BetaDoorDash.Lighting(scene.name);
            BetaBlockParty.Sky(scene.name, ab);
            SkyLighting();
            Plugin.Log.LogInfo($"{scene.name} now has {bundle} over {baseId}");
        }

        private static IEnumerator ApplySceneBundle(Scene scene, string bundle, int gen)
        {
            var entry = Features.CreativeExpansion.Definers.ByBundle(bundle);
            if (!entry.HasValue || scene.name == entry.Value.SceneName) yield break;
            if (!Background3dTweak.Have(bundle))
            {
                Background3dTweak.AskDownload(bundle, entry.Value.Title, OnRoundLoaded);
                yield break;
            }
            if (Bundles.Get(bundle) == null)
            {
                AssetBundle loaded = null;
                yield return Bundles.LoadFile(bundle, Path.Combine(Background3dTweak.BundleDir, bundle), x => loaded = x).WrapToIl2Cpp();
                if (loaded == null) { Plugin.Log.LogWarning($"{bundle} didn't load as a bundle, {scene.name} keeps its own"); yield break; }
            }
            var load = SceneManager.LoadSceneAsync(entry.Value.SceneName, LoadSceneMode.Additive);
            if (load == null) yield break;
            yield return load;
            var extra = SceneManager.GetSceneByName(entry.Value.SceneName);
            if (!extra.IsValid() || !extra.isLoaded) { Plugin.Log.LogWarning($"{entry.Value.SceneName} out of {bundle} didn't load"); yield break; }
            if (gen != _gen || !scene.isLoaded) { SceneManager.UnloadSceneAsync(extra); yield break; }

            TakeSnapshot(scene);
            StripHost(scene);
            _extraScene = extra;

            SceneManager.SetActiveScene(extra);
            var sky = RenderSettings.skybox;
            bool fog = RenderSettings.fog;
            var fogMode = RenderSettings.fogMode;
            var fogColor = RenderSettings.fogColor;
            float fogDensity = RenderSettings.fogDensity, ambientIntensity = RenderSettings.ambientIntensity, reflection = RenderSettings.reflectionIntensity;
            var ambientMode = RenderSettings.ambientMode;
            Color skyCol = RenderSettings.ambientSkyColor, eqCol = RenderSettings.ambientEquatorColor, groundCol = RenderSettings.ambientGroundColor;
            var probe = Ss2RoundLookTweak.ReadAmbientProbe();
            var reflectionTex = ReflectionProbe.defaultTexture;
            var sun = RenderSettings.sun;
            SceneManager.SetActiveScene(scene);

            RenderSettings.skybox = sky;
            RenderSettings.fog = fog;
            RenderSettings.fogMode = fogMode;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientSkyColor = skyCol;
            RenderSettings.ambientEquatorColor = eqCol;
            RenderSettings.ambientGroundColor = groundCol;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.reflectionIntensity = reflection;
            if (probe != null) Ss2RoundLookTweak.WriteAmbientProbe(probe);
            if (reflectionTex != null)
            {
                RenderSettings.customReflectionTexture = reflectionTex;
                RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
            }
            if (sun != null) RenderSettings.sun = sun;
            ReapplyFarClip();
            Plugin.Log.LogInfo($"{scene.name} borrowed {entry.Value.Title} from {bundle}, {extra.rootCount} roots");
        }

        private static void SkyLighting()
        {
            DynamicGI.UpdateEnvironment();
            var go = new GameObject("BettrFG_SkyReflectionCam");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.cullingMask = 0;
            cam.clearFlags = CameraClearFlags.Skybox;
            var cube = new Cubemap(128, TextureFormat.RGBAHalf, true) { name = "BettrFG_roundbg_skyreflection" };
            cam.RenderToCubemap(cube);
            UnityEngine.Object.Destroy(go);
            RenderSettings.customReflectionTexture = cube;
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
        }

        private static void IsolateQual(GameObject root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.gameObject.layer == 0) t.gameObject.layer = QualBgLayer;
            foreach (var go in _spawned)
                if (go != null)
                    foreach (var l in go.GetComponentsInChildren<Light>(true)) l.cullingMask = QualBgMask;
            RenderSettings.sun = _snap.Sun;
        }

        private static Vector3 StripHost(Scene scene, Context context = Context.Round)
        {
            var anchor = Vector3.zero;
            bool found = false;
            bool menu = context == Context.Menu;
            bool qual = context == Context.Qualification;
            int playerUi = 1 << LayerMask.NameToLayer("PlayerUI");
            foreach (var root in scene.GetRootGameObjects())
            {
                if (_spawned.Contains(root)) continue;
                bool menuOwn = menu && !root.name.StartsWith("Background_");
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.gameObject.activeSelf && t.name.ToLowerInvariant().Contains("fallguy_background"))
                    {
                        if (!found) { anchor = t.position; found = true; }
                        t.gameObject.SetActive(false);
                        _hidden.Add(t.gameObject);
                    }
                foreach (var l in root.GetComponentsInChildren<Light>(true))
                    if (l.enabled && l.type == LightType.Directional)
                    {
                        if (menuOwn || qual)
                        {
                            _lightsMasked.Add((l, l.cullingMask));
                            l.cullingMask &= qual ? ~QualBgMask : playerUi;
                            continue;
                        }
                        l.enabled = false;
                        _lightsOff.Add(l);
                    }
                foreach (var p in root.GetComponentsInChildren<ReflectionProbe>(true))
                    if (p.enabled)
                    {
                        p.enabled = false;
                        _probesOff.Add(p);
                    }
            }
            _farClip = FarClip;
            return anchor;
        }

        private static Vector3 V3(string s)
        {
            var p = s.Split(',');
            return new Vector3(float.Parse(p[0], Inv), float.Parse(p[1], Inv), float.Parse(p[2], Inv));
        }

        private static Color Col(string s) { var v = V3(s); return new Color(v.x, v.y, v.z, 1f); }

        private static T Load<T>(string guid) where T : UnityEngine.Object => Addressables.LoadAssetAsync<T>(guid).WaitForCompletion();

        private static void BuildPackage(Scene scene, string dir, bool keepHostLighting)
        {
            var rows = new Dictionary<string, List<string[]>>();
            foreach (var line in File.ReadAllLines(Path.Combine(dir, "lighting.txt")))
            {
                var p = line.Split('\t');
                if (p.Length < 2) continue;
                if (keepHostLighting && (p[0] == "ambient" || p[0] == "probe" || p[0] == "reflectionintensity")) continue;
                if (!rows.TryGetValue(p[0], out var list)) rows[p[0]] = list = new List<string[]>();
                list.Add(p);
            }
            List<string[]> Rows(string key) => rows.TryGetValue(key, out var l) ? l : new List<string[]>();

            var atlases = new SortedDictionary<int, LightmapData>();
            foreach (var p in Rows("lightmap"))
            {
                int source = int.Parse(p[1]);
                var tex = new Texture2D(int.Parse(p[4]), int.Parse(p[5]), (TextureFormat)int.Parse(p[6]), int.Parse(p[7]), true);
                tex.name = "BettrFG_roundbg_" + source + "_" + p[2];
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.LoadRawTextureData((Il2CppStructArray<byte>)File.ReadAllBytes(Path.Combine(dir, p[3])));
                tex.Apply(false, true);
                if (!atlases.TryGetValue(source, out var data)) atlases[source] = data = new LightmapData();
                if (p[2] == "Lightmap") data.lightmapColor = tex;
                else if (p[2] == "DirLightmap") data.lightmapDir = tex;
                else data.shadowMask = tex;
            }
            var old = LightmapSettings.lightmaps;
            var all = new LightmapData[old.Length + atlases.Count];
            for (int i = 0; i < old.Length; i++) all[i] = old[i];
            var slotOf = new Dictionary<int, int>();
            foreach (var kv in atlases)
            {
                slotOf[kv.Key] = old.Length + slotOf.Count;
                all[slotOf[kv.Key]] = kv.Value;
            }
            LightmapSettings.lightmaps = all;

            var meshes = new Dictionary<string, Mesh>();
            foreach (var p in Rows("mesh")) meshes[p[1]] = Load<Mesh>(p[2]);
            var nodeMesh = new Dictionary<int, Mesh>();
            var byGuid = new Dictionary<string, Mesh>();
            foreach (var p in Rows("nodemesh"))
            {
                if (!byGuid.TryGetValue(p[2], out var m)) byGuid[p[2]] = m = Load<Mesh>(p[2]);
                nodeMesh[int.Parse(p[1])] = m;
            }
            var mats = new Dictionary<string, Material>();
            foreach (var p in Rows("material")) mats[p[1]] = Load<Material>(p[2]);
            var matByGuid = new Dictionary<string, Material>();
            var nodeMats = new Dictionary<int, string[]>();
            foreach (var p in Rows("nodemat")) nodeMats[int.Parse(p[1])] = p[2].Split('|');
            var prefabs = new Dictionary<string, string>();
            foreach (var p in Rows("prefab")) prefabs[p[1]] = p[2];
            var skips = new HashSet<string>();
            foreach (var p in Rows("skip")) skips.Add(p[1]);
            var spins = new Dictionary<string, string[]>();
            foreach (var p in Rows("spin")) spins[p[1]] = p;
            var geoms = new Dictionary<int, string>();
            foreach (var p in Rows("geom")) geoms[int.Parse(p[1])] = p[2];

            var nodes = new List<Transform>();
            var under = new HashSet<int>();
            int built = 0;
            foreach (var line in File.ReadAllLines(Path.Combine(dir, "layout.txt")))
            {
                var p = line.Split('\t');
                if (p.Length < 6) continue;
                int parent = int.Parse(p[1]);
                if (parent >= 0 && (nodes[parent] == null || under.Contains(parent)) || skips.Contains(p[2]))
                {
                    if (parent >= 0 && under.Contains(parent)) under.Add(nodes.Count);
                    nodes.Add(null);
                    continue;
                }
                GameObject go;
                if (prefabs.TryGetValue(p[2], out var guid))
                {
                    go = UnityEngine.Object.Instantiate(Load<GameObject>(guid));
                    go.name = p[2];
                    under.Add(nodes.Count);
                }
                else go = new GameObject(nodes.Count == 0 ? "BettrFG_RoundBackground" : p[2]);
                if (parent >= 0) go.transform.SetParent(nodes[parent], false);
                else
                {
                    SceneManager.MoveGameObjectToScene(go, scene);
                    _spawned.Add(go);
                    if (nodes.Count == 0) PackageRoot = go;
                }
                go.transform.localPosition = V3(p[3]);
                var q = p[4].Split(',');
                go.transform.localRotation = new Quaternion(float.Parse(q[0], Inv), float.Parse(q[1], Inv), float.Parse(q[2], Inv), float.Parse(q[3], Inv));
                go.transform.localScale = V3(p[5]);
                nodes.Add(go.transform);

                if (spins.TryGetValue(p[2], out var spin))
                {
                    var c = go.AddComponent<BackgroundSpin>();
                    c.Axis = V3(spin[2]);
                    c.DegreesPerSecond = float.Parse(spin[3], Inv);
                }

                if (p.Length > 10)
                {
                    var state = p[10].Split(',');
                    go.SetActive(state[1] == "1");
                    go.layer = int.Parse(state[2]);
                }

                if (p.Length < 10 || p[6].Length == 0) continue;
                int index = nodes.Count - 1;
                bool baked = geoms.TryGetValue(index, out var geomFile);
                if (baked) nodeMesh[index] = Ss2RoundLookTweak.ReadMesh(File.ReadAllBytes(Path.Combine(dir, geomFile)), 0, go.transform.worldToLocalMatrix, out _);
                if (!nodeMesh.TryGetValue(index, out var mesh))
                {
                    string meshName = p[2];
                    int cut = meshName.IndexOf(" (", StringComparison.Ordinal);
                    if (cut > 0) meshName = meshName.Substring(0, cut);
                    meshes.TryGetValue(meshName, out mesh);
                }
                if (mesh == null) continue;
                var names = p[6].Split('|');
                var slots = new Material[names.Length];
                nodeMats.TryGetValue(index, out var exact);
                for (int i = 0; i < names.Length; i++)
                {
                    string g = exact != null && i < exact.Length ? exact[i] : "";
                    if (g.Length == 0) mats.TryGetValue(names[i], out slots[i]);
                    else if (!matByGuid.TryGetValue(g, out slots[i])) matByGuid[g] = slots[i] = Load<Material>(g);
                }
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = slots;
                if (p.Length > 10) mr.enabled = p[10][0] == '1';
                int source = int.Parse(p[7]);
                mr.lightmapIndex = source < 0 || source >= 65534 ? source : slotOf.TryGetValue(source, out var slot) ? slot : slotOf.Count > 0 ? old.Length : -1;
                var st = p[8].Split(',');
                mr.lightmapScaleOffset = baked ? new Vector4(1f, 1f, 0f, 0f) : new Vector4(float.Parse(st[0], Inv), float.Parse(st[1], Inv), float.Parse(st[2], Inv), float.Parse(st[3], Inv));
                var f = p[9].Split(',');
                mr.shadowCastingMode = (UnityEngine.Rendering.ShadowCastingMode)int.Parse(f[0]);
                mr.receiveShadows = f[1] == "1";
                mr.lightProbeUsage = (UnityEngine.Rendering.LightProbeUsage)int.Parse(f[2]);
                mr.reflectionProbeUsage = (UnityEngine.Rendering.ReflectionProbeUsage)int.Parse(f[3]);
                built++;
            }

            foreach (var p in Rows("sky")) RenderSettings.skybox = Load<Material>(p[1]);
            foreach (var p in Rows("fog"))
            {
                RenderSettings.fog = p[1] != "0";
                RenderSettings.fogMode = (FogMode)int.Parse(p[1]);
                RenderSettings.fogColor = Col(p[2]);
                RenderSettings.fogDensity = float.Parse(p[3], Inv);
                if (p.Length > 5)
                {
                    RenderSettings.fogStartDistance = float.Parse(p[4], Inv);
                    RenderSettings.fogEndDistance = float.Parse(p[5], Inv);
                }
            }
            foreach (var p in Rows("reflectionintensity")) RenderSettings.reflectionIntensity = float.Parse(p[1], Inv);
            foreach (var p in Rows("anchor")) nodes[int.Parse(p[1])].GetComponent<Renderer>().probeAnchor = nodes[int.Parse(p[2])];
            string[] shA = { "unity_SHAr", "unity_SHAg", "unity_SHAb" }, shB = { "unity_SHBr", "unity_SHBg", "unity_SHBb" };
            foreach (var p in Rows("sh"))
            {
                var r = nodes[int.Parse(p[1])]?.GetComponent<Renderer>();
                if (r == null) continue;
                var v = Array.ConvertAll(p[2].Split(','), x => float.Parse(x, Inv));
                var block = new MaterialPropertyBlock();
                for (int c = 0; c < 3; c++)
                {
                    int o = c * 9;
                    block.SetVector(shA[c], new Vector4(v[o + 3], v[o + 1], v[o + 2], v[o] - v[o + 6]));
                    block.SetVector(shB[c], new Vector4(v[o + 4], v[o + 5], v[o + 6] * 3f, v[o + 7]));
                }
                block.SetVector("unity_SHC", new Vector4(v[8], v[17], v[26], 1f));
                r.SetPropertyBlock(block);
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.CustomProvided;
            }
            foreach (var p in Rows("ambient"))
            {
                RenderSettings.ambientMode = (UnityEngine.Rendering.AmbientMode)int.Parse(p[1]);
                RenderSettings.ambientSkyColor = Col(p[2]);
                RenderSettings.ambientEquatorColor = Col(p[3]);
                RenderSettings.ambientGroundColor = Col(p[4]);
                RenderSettings.ambientIntensity = float.Parse(p[5], Inv);
            }
            foreach (var p in Rows("probe"))
            {
                var parts = p[1].Split(',');
                var sh = new float[27];
                for (int i = 0; i < 27; i++) sh[i] = float.Parse(parts[i], Inv);
                Ss2RoundLookTweak.WriteAmbientProbe(sh);
            }
            foreach (var p in Rows("reflection"))
            {
                RenderSettings.customReflectionTexture = LoadCube(dir, p[1], int.Parse(p[2]), int.Parse(p[3]), int.Parse(p[4]));
                RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
            }
            foreach (var p in Rows("refprobe"))
            {
                Transform holder = null;
                foreach (var n in nodes) if (n != null && n.name == p[1]) { holder = n; break; }
                if (holder == null) continue;
                var probeGo = new GameObject("BettrFG_ReflectionProbe");
                probeGo.transform.SetParent(holder, false);
                var probe = probeGo.AddComponent<ReflectionProbe>();
                probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom;
                probe.customBakedTexture = LoadCube(dir, p[2], int.Parse(p[3]), int.Parse(p[4]), int.Parse(p[5]));
                probe.size = V3(p[6]);
                probe.center = V3(p[7]);
                probe.intensity = float.Parse(p[8], Inv);
                probe.boxProjection = p[9] == "1";
                probe.blendDistance = float.Parse(p[10], Inv);
                probe.importance = int.Parse(p[11]);
            }

            Light first = null;
            var lights = new Dictionary<string, Light>();
            foreach (var p in Rows("light"))
            {
                var go = new GameObject("BettrFG_RoundLight_" + p[1]);
                SceneManager.MoveGameObjectToScene(go, scene);
                _spawned.Add(go);
                var l = go.AddComponent<Light>();
                l.type = LightType.Directional;
                l.color = Col(p[2]);
                l.intensity = float.Parse(p[3], Inv);
                l.bounceIntensity = float.Parse(p[4], Inv);
                l.shadows = (LightShadows)int.Parse(p[5]);
                l.shadowStrength = float.Parse(p[6], Inv);
                go.transform.rotation = Quaternion.Euler(V3(p[7]));
                l.cullingMask = int.Parse(p[8]);
                lights[p[1]] = l;
                if (first == null) first = l;
            }
            foreach (var p in Rows("cookie"))
                if (lights.TryGetValue(p[1], out var lit))
                {
                    lit.cookie = Load<Texture2D>(p[2]);
                    lit.cookieSize = float.Parse(p[3], Inv);
                }
            if (first != null) RenderSettings.sun = first;
            DynamicGI.UpdateEnvironment();
            _look = new Look
            {
                Sky = RenderSettings.skybox, Fog = RenderSettings.fog, FogMode = RenderSettings.fogMode, FogColor = RenderSettings.fogColor,
                FogDensity = RenderSettings.fogDensity, FogStart = RenderSettings.fogStartDistance, FogEnd = RenderSettings.fogEndDistance,
                AmbientMode = RenderSettings.ambientMode, AmbientSky = RenderSettings.ambientSkyColor, AmbientEquator = RenderSettings.ambientEquatorColor,
                AmbientGround = RenderSettings.ambientGroundColor, AmbientIntensity = RenderSettings.ambientIntensity,
                ReflectionIntensity = RenderSettings.reflectionIntensity, AmbientProbe = Ss2RoundLookTweak.ReadAmbientProbe(),
                Sun = RenderSettings.sun, Reflection = RenderSettings.customReflectionTexture, Lightmaps = LightmapSettings.lightmaps,
            };

            foreach (var p in Rows("farclip")) _farClip = Math.Max(FarClip, float.Parse(p[1], Inv));
            ReapplyFarClip();
            Plugin.Log.LogInfo($"{scene.name} got the {Path.GetFileName(dir)} background, {built} pieces, far clip {_farClip}");
        }

        private static Cubemap LoadCube(string dir, string file, int size, int format, int mips)
        {
            var cube = new Cubemap(size, (TextureFormat)format, mips) { name = "BettrFG_roundbg_" + Path.GetFileNameWithoutExtension(file) };
            int block = format == (int)TextureFormat.DXT1 || format == (int)TextureFormat.BC4 ? 8 : 16;
            var raw = File.ReadAllBytes(Path.Combine(dir, file));
            int at = 0;
            for (int face = 0; face < 6; face++)
                for (int mip = 0; mip < mips; mip++)
                {
                    int w = Math.Max(1, size >> mip), len = (w + 3) / 4 * ((w + 3) / 4) * block;
                    var slice = new byte[len];
                    Buffer.BlockCopy(raw, at, slice, 0, len);
                    at += len;
                    cube.SetPixelData((Il2CppStructArray<byte>)slice, mip, (CubemapFace)face, 0);
                }
            cube.Apply(false, true);
            return cube;
        }

        internal static void ReapplyFarClip()
        {
            if (_farClip <= 0f) return;
            var cam = Camera.main;
            if (cam != null && cam.farClipPlane < _farClip) cam.farClipPlane = _farClip;
            var director = FallGuysLib.Camera.CameraLocator.GetCameraDirector();
            var freelook = director != null ? director._closeCamera : null;
            if (freelook == null) return;
            var lens = freelook.m_Lens;
            if (lens.FarClipPlane >= _farClip) return;
            lens.FarClipPlane = _farClip;
            freelook.m_Lens = lens;
        }
    }
}
