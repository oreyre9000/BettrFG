using System;
using System.Collections.Generic;
using BetterFG.Services;
using BettrFG.uGUI;
using FallGuysLib.Round;
using FG.Common.LODs;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.UI;

namespace BetterFG.Tweaks
{
    public class PerformanceModeTweak : BfgTweak
    {
        public PerformanceModeTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "performance_mode";
        public override string TweakLabel => "tweak.performance_mode";
        public override string TweakTooltip => "tweak.performance_mode.tip";

        private const string LevelKey = "tweak.performance_mode.level";
        internal static readonly string[] LevelIds = { "perf.level_light", "perf.level_balanced", "perf.level_strong", "perf.level_max" };
        internal static int Level => int.TryParse(SettingsService.Get(LevelKey, "1"), out int v) ? Mathf.Clamp(v, 0, 3) : 1;
        internal static bool Active => SettingsService.Get("tweak.performance_mode", "false") == "true";

        internal static bool Overrides(BfgTweak t) => t is ShadowDistanceTweak || t is ShadowCustomResolutionTweak || t is ShadowCascadeSplitTweak;

        private static readonly float[] LodMedium = { 35f, 25f, 15f, 10f };
        private static readonly float[] LodLow = { 60f, 45f, 30f, 20f };
        private static readonly float[] LodOff = { 90f, 70f, 50f, 40f };
        private static readonly LodLevel[] ShadowLod = { LodLevel.Low, LodLevel.Medium, LodLevel.Medium, LodLevel.Medium };
        private static readonly LodLevel[] ParticleLod = { LodLevel.Off, LodLevel.Low, LodLevel.Medium, LodLevel.Medium };
        private static readonly LodLevel[] RigidbodyLod = { LodLevel.Off, LodLevel.Low, LodLevel.Low, LodLevel.Medium };
        private static readonly AnimatorCullingMode[] Culling = { AnimatorCullingMode.AlwaysAnimate, AnimatorCullingMode.CullUpdateTransforms, AnimatorCullingMode.CullCompletely, AnimatorCullingMode.CullCompletely };
        private static readonly bool[] InnerShadows = { false, true, true, true };
        private static readonly bool[] NoCookies = { false, false, true, true };
        private static readonly float[] LodBias = { 1f, 0.8f, 0.6f, 0.4f };
        private static readonly int[] PixelLights = { 4, 2, 1, 0 };
        private static readonly int[] RaycastBudget = { 512, 256, 128, 64 };
        private static readonly int[] Msaa = { 4, 2, 0, 0 };
        private static readonly int[] Skin = { 4, 2, 2, 1 };
        private static readonly int[] Shadows = { 2, 2, 1, 0 };
        private static readonly bool[] SoftParticles = { true, false, false, false };
        private static readonly float[] ShadowDistance = { 60f, 45f, 30f, 20f };
        private static readonly int[] Cascades = { 2, 2, 1, 1 };
        private static readonly int[] ShadowRes = { 2, 1, 0, 0 };
        private static readonly float[] CullDistance = { 0f, 0f, 300f, 100f };
        private static readonly float[] FarClip = { 0f, 0f, 500f, 150f };
        private static readonly int[] Post = { 0, 0, 1, 2 };
        private static readonly bool[] Forward = { false, false, false, true };
        private static readonly bool[] OptimiseParticles = { false, false, true, true };
        private static readonly float[] ParticleCull = { 200f, 150f, 80f, 50f };
        private static readonly int[] HideBeansFrom = { 9, 9, 9, (int)LodLevel.Low };
        private static readonly int[] NametagEvery = { 1, 1, 2, 3 };

        internal static bool SkipNametagFrame => _level >= 0 && NametagEvery[_level] > 1
            && Time.unscaledDeltaTime < 1f / 150f && Time.frameCount % NametagEvery[_level] != 0;

        private sealed class FarBean
        {
            public FallGuysCharacterController Fgcc;
            public Animator Anim;
            public List<Renderer> Rends;
            public bool Hidden;
        }

        private static readonly List<FarBean> _beans = new List<FarBean>();
        private static int _level = -1;

        private static Camera _cam;
        private static PostProcessLayer _layer;
        private static float _sFar;
        private static bool _farTouched;
        private static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float> _sCull;
        private static RenderingPath _sPath;
        private static bool _sHdr, _sLayerOn;
        private static PostProcessLayer.Antialiasing _sAa;
        private static PostProcessEffectSettings _ao, _bloom;
        private static bool _sAo, _sBloom, _sOptParticles;
        private static float _sParticleCull;

        private static bool _captured;
        private static int _sTarget, _sVsync, _sPixelLights, _sRaycast, _sMsaa, _sCascades;
        private static float _sLodBias, _sShadowDistance;
        private static ShadowResolution _sShadowRes;
        private static SkinWeights _sSkin;
        private static ShadowQuality _sShadows;
        private static bool _sSoftParticles;
        private static PlatformSettings _sPlatform;
        private static AnimatorCullingMode _sCulling;
        private static bool _sInnerShadows, _sNoCookies;
        private static LodLevel _sParticleLod, _sRigidbodyLod;

        public override void EnableTweak()
        {
            foreach (var t in TweakRegistry.All)
                if (Overrides(t) && t.IsEnabled) t.DisableTweak();
            RoundEvents.OnPlayingStarted += CollectBeans;
            Apply(Level);
        }

        public override void DisableTweak()
        {
            RoundEvents.OnPlayingStarted -= CollectBeans;
            ReleaseBeans();
            _level = -1;
            Restore();
            foreach (var t in TweakRegistry.All)
                if (Overrides(t) && t.IsEnabled) t.EnableTweak();
        }

        void Update()
        {
            if (_captured && (Application.targetFrameRate != -1 || QualitySettings.vSyncCount != 0)) Uncap();
            if (_beans.Count == 0) return;
            int l = _level;
            for (int i = _beans.Count - 1; i >= 0; i--)
            {
                var b = _beans[i];
                if (b.Fgcc.m_CachedPtr == IntPtr.Zero || b.Anim.m_CachedPtr == IntPtr.Zero) { _beans.RemoveAt(i); continue; }
                bool hide = (int)b.Fgcc.Lod_ReadOnly >= HideBeansFrom[l];
                if (hide == b.Hidden) continue;
                b.Hidden = hide;
                b.Anim.enabled = !hide;
                foreach (var r in b.Rends) if (r.m_CachedPtr != IntPtr.Zero) r.enabled = !hide;
            }
        }

        private static void CollectBeans()
        {
            ReleaseBeans();
            if (_level < 2) return;
            var index = FallGuysLib.Players.PlayerUtils.GetClientPlayerManager()?._playerIdIndex;
            if (index == null) return;
            var local = Services.BeanMonitorService.LocalPlayerBean;
            IntPtr localPtr = local is null ? IntPtr.Zero : local.m_CachedPtr;
            foreach (var kvp in index)
            {
                var fgcc = kvp.Value?.fgcc;
                if (fgcc == null || fgcc.gameObject.m_CachedPtr == localPtr) continue;
                var anim = fgcc.GetComponentInChildren<Animator>();
                if (anim == null) continue;
                var rends = new List<Renderer>();
                foreach (var r in fgcc.GetComponentsInChildren<Renderer>()) if (r.enabled) rends.Add(r);
                _beans.Add(new FarBean { Fgcc = fgcc, Anim = anim, Rends = rends });
            }
        }

        private static void ReleaseBeans()
        {
            foreach (var b in _beans)
            {
                if (!b.Hidden) continue;
                foreach (var r in b.Rends) if (r.m_CachedPtr != IntPtr.Zero) r.enabled = true;
                if (b.Anim.m_CachedPtr != IntPtr.Zero) b.Anim.enabled = true;
            }
            _beans.Clear();
        }

        private static void Uncap()
        {
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
        }

        public override void OnRoundStart() => Apply(Level);
        public override void OnMainMenuEntered() => Apply(Level);

        private static void Capture()
        {
            if (_captured) return;
            _captured = true;
            _sTarget = Application.targetFrameRate;
            _sVsync = QualitySettings.vSyncCount;
            _sShadowDistance = QualitySettings.shadowDistance;
            _sCascades = QualitySettings.shadowCascades;
            _sShadowRes = QualitySettings.shadowResolution;
            _sLodBias = QualitySettings.lodBias;
            _sPixelLights = QualitySettings.pixelLightCount;
            _sRaycast = QualitySettings.particleRaycastBudget;
            _sMsaa = QualitySettings.antiAliasing;
            _sSkin = QualitySettings.skinWeights;
            _sShadows = QualitySettings.shadows;
            _sSoftParticles = QualitySettings.softParticles;
        }

        private static void CapturePlatform(PlatformSettings ps)
        {
            if (_sPlatform != null) return;
            _sPlatform = ps;
            _sCulling = ps._characterCulling;
            _sInnerShadows = ps._innerShadowsOnly;
            _sNoCookies = ps._removeLightCookies;
            _sParticleLod = ps._disableParticlesLOD;
            _sRigidbodyLod = ps._disableRigidBody;
            _sOptParticles = ps._optimiseParticles;
            _sParticleCull = ps._particleCullDistance;
        }

        private static void ApplyCamera(int l)
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (cam != _cam)
            {
                _cam = cam;
                _layer = cam.GetComponent<PostProcessLayer>();
                _sFar = cam.farClipPlane;
                _farTouched = false;
                _sCull = cam.layerCullDistances;
                _sPath = cam.renderingPath;
                _sHdr = cam.allowHDR;
                if (_layer != null)
                {
                    _sLayerOn = _layer.enabled;
                    _sAa = _layer.antialiasingMode;
                    var vol = cam.GetComponent<PostProcessVolume>();
                    if (_ao == null && vol != null && vol.sharedProfile != null)
                    {
                        foreach (var s in vol.sharedProfile.settings)
                        {
                            var n = s.GetIl2CppType().Name;
                            if (n == "AmbientOcclusion") { _ao = s; _sAo = s.active; }
                            else if (n == "Bloom") { _bloom = s; _sBloom = s.active; }
                        }
                    }
                }
            }

            if (FarClip[l] > 0f)
            {
                cam.farClipPlane = Mathf.Min(_sFar, FarClip[l]);
                _farTouched = true;
            }
            else if (_farTouched)
            {
                cam.farClipPlane = _sFar;
                _farTouched = false;
            }
            if (CullDistance[l] > 0f)
            {
                var cull = new float[32];
                for (int i = 0; i < 32; i++) cull[i] = _sCull[i] > 0f ? Mathf.Min(_sCull[i], CullDistance[l]) : CullDistance[l];
                cam.layerCullDistances = cull;
            }
            else cam.layerCullDistances = _sCull;
            cam.renderingPath = Forward[l] ? RenderingPath.Forward : _sPath;
            cam.allowHDR = _sHdr && !Forward[l];
            if (Post[l] == 2) cam.depthTextureMode = DepthTextureMode.None;

            if (_layer == null) return;
            _layer.enabled = _sLayerOn && Post[l] < 2;
            _layer.antialiasingMode = Post[l] >= 1 ? PostProcessLayer.Antialiasing.None : _sAa;
            if (_ao != null) _ao.active = _sAo && Post[l] < 1;
            if (_bloom != null) _bloom.active = _sBloom && Post[l] < 1;
        }

        private static void RestoreCamera()
        {
            if (_cam != null)
            {
                if (_farTouched) _cam.farClipPlane = _sFar;
                _cam.layerCullDistances = _sCull;
                _cam.renderingPath = _sPath;
                _cam.allowHDR = _sHdr;
                if (_layer != null)
                {
                    _layer.enabled = _sLayerOn;
                    _layer.antialiasingMode = _sAa;
                }
            }
            if (_ao != null) _ao.active = _sAo;
            if (_bloom != null) _bloom.active = _sBloom;
            _cam = null;
            _layer = null;
        }

        private static void Apply(int l)
        {
            bool changed = l != _level;
            _level = l;
            if (changed) CollectBeans();
            Capture();
            Uncap();
            QualitySettings.shadowDistance = Mathf.Min(_sShadowDistance, ShadowDistance[l]);
            QualitySettings.shadowCascades = Mathf.Min(_sCascades, Cascades[l]);
            QualitySettings.shadowResolution = (ShadowResolution)Mathf.Min((int)_sShadowRes, ShadowRes[l]);
            QualitySettings.lodBias = Mathf.Min(_sLodBias, LodBias[l]);
            QualitySettings.pixelLightCount = Mathf.Min(_sPixelLights, PixelLights[l]);
            QualitySettings.particleRaycastBudget = Mathf.Min(_sRaycast, RaycastBudget[l]);
            QualitySettings.antiAliasing = Mathf.Min(_sMsaa, Msaa[l]);
            QualitySettings.skinWeights = (SkinWeights)Mathf.Min((int)_sSkin, Skin[l]);
            QualitySettings.shadows = (ShadowQuality)Mathf.Min((int)_sShadows, Shadows[l]);
            QualitySettings.softParticles = _sSoftParticles && SoftParticles[l];

            var pm = PlatformManager.Instance;
            var ps = pm != null ? pm._currentSettings : null;
            if (ps != null)
            {
                CapturePlatform(ps);
                ps._characterCulling = (AnimatorCullingMode)Mathf.Max((int)_sCulling, (int)Culling[l]);
                ps._innerShadowsOnly = _sInnerShadows || InnerShadows[l];
                ps._removeLightCookies = _sNoCookies || NoCookies[l];
                ps._disableParticlesLOD = (LodLevel)Mathf.Min((int)_sParticleLod, (int)ParticleLod[l]);
                ps._disableRigidBody = (LodLevel)Mathf.Min((int)_sRigidbodyLod, (int)RigidbodyLod[l]);
                ps._optimiseParticles = _sOptParticles || OptimiseParticles[l];
                ps._particleCullDistance = Mathf.Min(_sParticleCull, ParticleCull[l]);
                if (pm._particleManager != null) pm._particleManager._defaultMaxCullDistance = ps._particleCullDistance;
            }

            ApplyCamera(l);

            var lm = LodManager.Instance;
            if (lm != null)
            {
                var s = lm._defaultLodSettings;
                s.mediumDistance = Mathf.Min(s.mediumDistance, LodMedium[l]);
                s.lowDistance = Mathf.Min(s.lowDistance, LodLow[l]);
                s.offDistance = Mathf.Min(s.offDistance, LodOff[l]);
                s.disableShadowsLOD = ShadowLod[l];
                lm.OverrideLodSettings(s);
            }
        }

        private static void Restore()
        {
            if (_captured)
            {
                Application.targetFrameRate = _sTarget;
                QualitySettings.vSyncCount = _sVsync;
                QualitySettings.shadowDistance = _sShadowDistance;
                QualitySettings.shadowCascades = _sCascades;
                QualitySettings.shadowResolution = _sShadowRes;
                QualitySettings.lodBias = _sLodBias;
                QualitySettings.pixelLightCount = _sPixelLights;
                QualitySettings.particleRaycastBudget = _sRaycast;
                QualitySettings.antiAliasing = _sMsaa;
                QualitySettings.skinWeights = _sSkin;
                QualitySettings.shadows = _sShadows;
                QualitySettings.softParticles = _sSoftParticles;
                _captured = false;
            }
            if (_sPlatform != null)
            {
                _sPlatform._characterCulling = _sCulling;
                _sPlatform._innerShadowsOnly = _sInnerShadows;
                _sPlatform._removeLightCookies = _sNoCookies;
                _sPlatform._disableParticlesLOD = _sParticleLod;
                _sPlatform._disableRigidBody = _sRigidbodyLod;
                _sPlatform._optimiseParticles = _sOptParticles;
                _sPlatform._particleCullDistance = _sParticleCull;
                var pm = PlatformManager.Instance;
                if (pm != null && pm._particleManager != null) pm._particleManager._defaultMaxCullDistance = _sParticleCull;
                _sPlatform = null;
            }
            RestoreCamera();
            var lm = LodManager.Instance;
            if (lm != null) lm.OverrideLodSettings(lm._defaultLodSettings);
        }

        public override List<TweakSetting> GetSettings() => new List<TweakSetting>
        {
            new TweakSetting
            {
                Label = "perf.level",
                Options = LevelIds,
                Selected = () => Level,
                OnPick = i =>
                {
                    SettingsService.Set(LevelKey, i.ToString());
                    if (Active) Apply(i);
                },
            },
        };
    }

    public class FpsCounterTweak : BfgTweak
    {
        public FpsCounterTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "fps_counter";
        public override string TweakLabel => "tweak.fps_counter";

        private GameObject _root;
        private Text _label;
        private int _frames;
        private float _since, _worst;

        public override void EnableTweak()
        {
            if (_root != null) return;
            _root = new GameObject("BettrFG_Fps");
            DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32761;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.matchWidthOrHeight = 1f;
            UIScaleService.Register(canvas);
            _label = UGUIShip.CreateLabel(_root.transform, new Rect(12, 72, 420, 30), "", 18, new Color(1f, 0.85f, 0.1f, 1f), TextAnchor.UpperLeft);
            _frames = 0;
            _since = Time.realtimeSinceStartup;
            _worst = 0f;
        }

        public override void DisableTweak()
        {
            if (_root != null) Destroy(_root);
            _root = null;
            _label = null;
        }

        void Update()
        {
            if (_label == null) return;
            _frames++;
            float dt = Time.unscaledDeltaTime;
            if (dt > _worst) _worst = dt;
            float now = Time.realtimeSinceStartup;
            float span = now - _since;
            if (span < 0.5f) return;
            string level = PerformanceModeTweak.Active ? "  " + LocalizationService.Get(PerformanceModeTweak.LevelIds[PerformanceModeTweak.Level]) : "";
            _label.text = $"{Mathf.RoundToInt(_frames / span)} fps  {span * 1000f / _frames:0.00} ms  max {_worst * 1000f:0.0} ms{level}";
            _frames = 0;
            _since = now;
            _worst = 0f;
        }
    }
}
