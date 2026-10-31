using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.Utilities;
using FG.Common;
using FGClient;
using Il2CppInterop.Runtime;
using Levels.Invisibeans;
using UnityEngine;

namespace BetterFG.Features.CustomizeFallGuys
{
    public static class FeatureCustomizeFallGuys
    {
        public static readonly BfgFeature feature = new BfgFeature(
            "customizefallguys",
            "ui.customize_fall_guys",
            false,
            new List<FeatureSetting>
            {
                new FeatureSetting { id = "lookat", label = "customize_fall_guys.look_at_others", defaultOn = false, section = "customize_fall_guys.section.behaviour" },
                new FeatureSetting { id = "blink", label = "ui.blinking", defaultOn = true, section = "customize_fall_guys.section.eyes" },
                new FeatureSetting { id = "look", label = "ui.look_left_and_right", defaultOn = true, section = "customize_fall_guys.section.eyes" },
                new FeatureSetting { id = "squint", label = "ui.light_squinting", defaultOn = false, section = "customize_fall_guys.section.eyes" },
                new FeatureSetting { id = "localonly", label = "ui.only_my_fall_guy", defaultOn = false, section = "customize_fall_guys.section.eyes" },
            },
            onOpen: OnToggled,
            onClosed: OnToggled,
            onSettingChanged: OnSettingChanged,
            ranges: new List<FeatureRange>
            {
                new FeatureRange { id = "eyedist", label = "ui.eye_distance", min = -0.1f, max = 0.12f, step = 0.01f, defaultValue = 0f, hint = "ui.pushes_the_eyes_apart_or_together_at_0_1_they_ne" },
                new FeatureRange { id = "eyeheight", label = "ui.eye_height", min = -0.1f, max = 0.1f, step = 0.01f, defaultValue = 0f, hint = "ui.slides_both_eyes_up_or_down_the_face" },
                new FeatureRange { id = "eyerot", label = "ui.eye_rotation", min = -45f, max = 45f, step = 1f, defaultValue = 0f, hint = "ui.tilts_the_two_eyes_in_opposite_directions_in_deg" },
                new FeatureRange { id = "eyescale", label = "ui.eye_scale", min = 0.1f, max = 4f, step = 0.05f, defaultValue = 1f, hint = "ui.multiplies_eye_size_1_is_stock" },
                new FeatureRange { id = "eyeyscale", label = "ui.eye_y_scale", min = 0.1f, max = 4f, step = 0.05f, defaultValue = 1f, hint = "ui.height_only_on_top_of_eye_scale_low_squashes_the" },
            },
            onRangeChanged: OnRangeChanged,
            choices: new List<FeatureChoice>
            {
                new FeatureChoice
                {
                    id = "eyemat",
                    label = "ui.eye_material",
                    optionIds = new List<string> { "none", "crownjam" },
                    optionLabels = new List<string> { "ui.none_2", "ui.crown_jam" },
                    defaultId = "none",
                    hint = "ui.draws_a_custom_eye_pass_over_the_bean_its_tint_f",
                },
            },
            onChoiceChanged: OnChoiceChanged);

        const float BakeFps = 30f;
        const int MaxBakeFrames = 512;

        const float LookOffset = 0.03f;
        const float EyeDrawDistanceSqr = 30f * 30f;
        static bool _inRound;
        internal static bool InRound => _inRound;

        static bool _qualScreen;

        public static void SetQualScreen() => _qualScreen = true;

        public static void SetInRound(bool on)
        {
            _qualScreen = false;
            if (_inRound == on) return;
            _inRound = on;
            // leaving a round with beans culled would strand them hidden
            if (!on)
                for (int i = 0; i < _count; i++) { _eyes[i].hiddenSet = false; _eyes[i].invisHidden = false; _eyes[i].cullSet = false; }
        }

        internal static bool On;

        static bool _blink, _look, _squint, _localOnly, _work;
        static float _dist, _height, _rot, _scale, _yscale;
        static string _eyeMat;
        static GameObject _previewPanel, _previewBean;

        static void ReadSettings()
        {
            _blink = feature.Get("blink");
            _look = feature.Get("look");
            _squint = feature.Get("squint");
            _localOnly = feature.Get("localonly");
            _dist = feature.GetRange("eyedist");
            _height = feature.GetRange("eyeheight");
            _rot = feature.GetRange("eyerot");
            _scale = feature.GetRange("eyescale");
            _yscale = feature.GetRange("eyeyscale");
            On = feature.enabled;
            BeanLookAt.SetOn(feature.Get("lookat"));
            Utilities.PatchGate.Request(Customization.Player.InvisibilitySyncComponent.GateKey, "eyes", On);
            _eyeMat = On ? feature.GetChoice("eyemat") : "none";
            _work = On && (_blink || _look || _squint
                || _eyeMat != "none"
                || !Mathf.Approximately(_dist, 0f)
                || !Mathf.Approximately(_height, 0f)
                || !Mathf.Approximately(_rot, 0f)
                || !Mathf.Approximately(_scale, 1f)
                || !Mathf.Approximately(_yscale, 1f));
        }

        struct Eyes
        {
            public Transform l;
            public Transform r;
            public float blinkCursor;
            public float blinkSpeed;
            public float lookFrom;
            public float lookTo;
            public float lookT;
            public float lookRate;
            public float lookHold;
            public float squintPhase;
            public GameObject eyeGo;
            public SkinnedMeshRenderer eyeRenderer;
            public bool hiddenSet;
            public bool hiddenValue;
            public bool cullSet;
            public bool culled;
            public bool invisHidden;
            public SkinnedMeshRenderer body;
            public Material wantMat;
            public Material faceMat;
            public InvisibilityVisualsController invis;
            public GameObject root;
            // the preview clone is parked a thousand units under the map, so the distance cull and the
            // isVisible skip would both throw its eyes away every frame
            public bool preview;
            // each bean's own rig can be a different scale (PlayerScaleService, differently-built
            // clones, etc), so the rest pose has to be captured per-bean, not shared off whichever
            // bean happened to get tracked first.
            public Vector3 restPosL, restPosR, restScaleL, restScaleR;
            public Quaternion restRotL, restRotR;

            // set when this bean belongs to a matched profile owner — their own eye settings
            // override the viewer's globals for this bean specifically, [[see ApplyProfileOverride]]
            public bool hasOverride;
            public bool ovBlink, ovLook, ovSquint;
            public float ovDist, ovHeight, ovRot, ovScale, ovYscale;
        }

        // profile-owner eye settings for a remote bean, keyed by that bean's instance id — kept
        // separate from the viewer's own globals so a profile owner's look survives regardless of
        // whether the viewer even has this feature turned on
        struct EyeOverride
        {
            public GameObject bean;
            public bool blink, look, squint;
            public float dist, height, rot, scale, yscale;
            public string eyeMat;
        }
        static readonly Dictionary<int, EyeOverride> _overrides = new Dictionary<int, EyeOverride>();
        internal static bool HasOverrides => _overrides.Count > 0;

        public static void ApplyProfileOverride(GameObject bean, BetterFG.Customization.Player.BfgProfile profile)
        {
            if (bean == null || profile == null) return;
            int id = bean.GetInstanceID();

            if (!profile.GetBool("feature.customizefallguys", false)) { _overrides.Remove(id); return; }

            _overrides[id] = new EyeOverride
            {
                bean = bean,
                blink = profile.GetBool("feature.customizefallguys.blink", true),
                look = profile.GetBool("feature.customizefallguys.look", true),
                squint = profile.GetBool("feature.customizefallguys.squint", false),
                dist = profile.GetFloat("feature.customizefallguys.eyedist", 0f),
                height = profile.GetFloat("feature.customizefallguys.eyeheight", 0f),
                rot = profile.GetFloat("feature.customizefallguys.eyerot", 0f),
                scale = profile.GetFloat("feature.customizefallguys.eyescale", 1f),
                yscale = profile.GetFloat("feature.customizefallguys.eyeyscale", 1f),
                eyeMat = profile.Get("feature.customizefallguys.eyemat", "none"),
            };
            Apply(bean, isLocalPlayer: false);
        }

        public static void ClearProfileOverride(GameObject bean)
        {
            if (bean != null) _overrides.Remove(bean.GetInstanceID());
        }

        static void ReapplyOverrides()
        {
            foreach (var kv in _overrides)
                if (kv.Value.bean != null) Apply(kv.Value.bean, isLocalPlayer: false);
        }

        static bool Gate(GameObject bean, bool isLocalPlayer)
        {
            if (bean == null) return false;
            if (_overrides.ContainsKey(bean.GetInstanceID())) return true;
            if (!_work) return false;
            if (_localOnly && !isLocalPlayer) return false;
            return true;
        }

        static Eyes[] _eyes = new Eyes[64];
        static int _count;
        static readonly List<GameObject> _pending = new List<GameObject>();

        static int _bakedFrames;
        static float _playbackRate;
        static Vector3[] _lPos, _lScale, _rPos, _rScale;
        static Quaternion[] _lRot, _rRot;

        static void OnToggled() => Refresh(true);

        static void OnSettingChanged(string id, bool on) => Refresh(true);

        static void OnRangeChanged(string id, float value) => Refresh(true);

        static void OnChoiceChanged(string id, string optionId) => Refresh(true);

        public static Texture PreviewTexture => EyePreview.Ensure();

        internal static bool PreviewWanted
        {
            get
            {
                if (!feature.enabled || _previewPanel == null) return false;
                var ui = BetterFG.UI.BetterFGUIMan.Instance;
                return ui != null && ui.IsVisible;
            }
        }

        public static void SetPreviewPanel(GameObject panel)
        {
            _previewPanel = panel;
            Refresh();
            FallGuyEyeDriver.EnsurePreviewLoop();
        }

        public static void Refresh(bool rebuild = false, float delay = 0f)
        {
            if (delay > 0f)
            {
                FallGuyEyeDriver.Instance.StartCoroutine(RefreshLater(rebuild, delay).WrapToIl2Cpp());
                return;
            }

            if (rebuild) RestoreAll();
            ReadSettings();

            var local = BeanMonitorService.LocalPlayerBean;
            var mm = UnityEngine.Object.FindObjectOfType<MainMenuManager>();
            // the preview shows a stock Fall Guy, not the live player - PB_FallGuyBot is always
            // resident (HideAndDontSave), so unlike the local/menu bean it never leaves the preview
            // blank
            _previewBean = GameObjectHelper.FindDefaultBotBean();

            // the clone isn't in the scene sweep below (that's the point of it), so re-queue it by hand
            Apply(EyePreview.Clone);
            // RestoreAll above wiped every tracked bean, profile-owned ones included — get them back
            // regardless of the viewer's own toggle/localOnly, they don't answer to those
            ReapplyOverrides();
            if (!_work) return;

            if (_localOnly)
            {
                Apply(local);
                if (mm != null)
                {
                    Apply(mm._menuFallGuy);
                    Apply(mm._lobbyFallGuy);
                }
                return;
            }

            var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<FallguyCustomisationHandler>());
            for (int i = 0; all != null && i < all.Length; i++)
            {
                var handler = all[i].TryCast<FallguyCustomisationHandler>();
                if (handler == null) continue;
                var go = handler.gameObject;
                if (go.scene.IsValid()) Apply(go);
            }
        }

        public static void Apply(GameObject bean, bool isLocalPlayer = true)
        {
            if (!Gate(bean, isLocalPlayer)) return;
            for (int i = 0; i < _pending.Count; i++)
                if (_pending[i] == bean) return;
            _pending.Add(bean);
        }

        public static void ApplyLater(GameObject bean, float delay, bool isLocalPlayer = true)
        {
            if (!Gate(bean, isLocalPlayer)) return;
            var driver = FallGuyEyeDriver.Instance;
            if (driver == null) { Apply(bean, isLocalPlayer); return; }
            driver.StartCoroutine(ApplyAfter(bean, delay, isLocalPlayer).WrapToIl2Cpp());
        }

        static IEnumerator ApplyAfter(GameObject bean, float delay, bool isLocalPlayer)
        {
            yield return new WaitForSeconds(delay);
            Apply(bean, isLocalPlayer);
        }

        internal static void OnInvisibilityVisuals(Levels.Invisibeans.InvisibilityVisualsController controller, bool hidden)
        {
            if (!_work && !HasOverrides) return;
            for (int i = 0; i < _count; i++)
            {
                var invis = _eyes[i].invis;
                if (invis is null || invis.m_CachedPtr != controller.m_CachedPtr) continue;
                if (_eyes[i].invisHidden == hidden) return;
                _eyes[i].invisHidden = hidden;
                if (hidden) return;
                Apply(_eyes[i].root);
                ApplyLater(_eyes[i].root, 0.5f);
                return;
            }
        }

        public static void ReassertOn(CellBehaviour cell)
        {
            var fg = cell != null ? cell._fallGuy : null;
            if (fg != null) ReassertOn(fg.gameObject);
        }

        public static void ReassertOn(GameObject bean)
        {
            if (bean == null) return;
            for (int i = 0; i < _count; i++)
            {
                if (_eyes[i].root != bean) continue;
                if (_eyes[i].eyeRenderer != null) EyeGeometry.Reassert(_eyes[i].eyeRenderer, _eyes[i].body, _eyes[i].wantMat);
                return;
            }
        }

        public static void OnFaceplateChanged(GameObject bean, FaceplateOption option)
        {
            if (bean == null || option == null) return;
            for (int i = 0; i < _count; i++)
            {
                if (_eyes[i].root != bean) continue;
                if (_eyes[i].eyeRenderer == null) return;
                EyeGeometry.SetTint(_eyes[i].eyeRenderer, option.eyesColour);
                _eyes[i].wantMat = _eyes[i].eyeRenderer.sharedMaterial;
                var fgch = bean.GetComponent<FallguyCustomisationHandler>();
                BlankStockEyes(fgch == null ? null : fgch._matInstance, option.eyesColour);
                _eyes[i].hiddenSet = false;
                return;
            }
        }

        static IEnumerator RefreshLater(bool rebuild, float delay)
        {
            yield return new WaitForSeconds(delay);
            Refresh(rebuild);
        }

        static void RestoreAll()
        {
            for (int i = 0; i < _count; i++)
            {
                var l = _eyes[i].l;
                var r = _eyes[i].r;
                if (l.m_CachedPtr != IntPtr.Zero && r.m_CachedPtr != IntPtr.Zero)
                {
                    l.SetLocalPositionAndRotation(_eyes[i].restPosL, _eyes[i].restRotL);
                    l.localScale = _eyes[i].restScaleL;
                    r.SetLocalPositionAndRotation(_eyes[i].restPosR, _eyes[i].restRotR);
                    r.localScale = _eyes[i].restScaleR;
                }
                SetBodyEyes(_eyes[i].root, _eyes[i].faceMat, true);
                EyeGeometry.Detach(_eyes[i].eyeGo);
                _eyes[i].eyeGo = null;
                _eyes[i].eyeRenderer = null;
                _eyes[i].body = null;
                _eyes[i].wantMat = null;
                _eyes[i].faceMat = null;
                _eyes[i].invis = null;
                _eyes[i].root = null;
                _eyes[i].l = null;
                _eyes[i].r = null;
            }
            _count = 0;
            _pending.Clear();
            RestoreStockEyes();
        }

        static GameObject AttachEyes(GameObject bean, string eyeMat, out SkinnedMeshRenderer body, out Material faceMat)
        {
            body = null;
            faceMat = null;
            if (eyeMat == "none") return null;

            var shared = AssetManager.GetMaterial("bettrfg_mat_eyes_" + eyeMat);
            if (shared == null)
            {
                Plugin.Log.LogWarning($"eye material '{eyeMat}' isn't in any loaded bundle, leaving that bean's face alone");
                return null;
            }

            var attached = EyeGeometry.Attach(bean, shared, Color.white, out body);
            if (attached == null) return null;

            // the preview clone has no customisation handler by design, but it shares the bean's
            // material instance, so the body renderer is the honest place to read the eye colour from
            var fgch = bean.GetComponent<FallguyCustomisationHandler>();
            var mat = fgch == null ? body.sharedMaterial : fgch._matInstance;
            int eyes = FallguyCustomisationHandler.ShaderEyesColor;
            if (mat != null && mat.HasProperty(eyes))
            {
                var tint = StockEyes(mat, eyes);
                EyeGeometry.SetTint(attached.GetComponent<SkinnedMeshRenderer>(), tint);
                BlankStockEyes(mat, tint);
                faceMat = mat;
            }
            return attached;
        }

        static readonly List<Material> _stockMats = new List<Material>();
        static readonly List<Color> _stockEyes = new List<Color>();

        static Color StockEyes(Material mat, int eyes)
        {
            for (int i = 0; i < _stockMats.Count; i++)
                if (_stockMats[i] != null && _stockMats[i].m_CachedPtr == mat.m_CachedPtr) return _stockEyes[i];
            return mat.GetColor(eyes);
        }

        static void BlankStockEyes(Material mat, Color stock)
        {
            if (mat == null) return;
            int eyes = FallguyCustomisationHandler.ShaderEyesColor;
            int face = FallguyCustomisationHandler.ShaderFaceColor;
            if (!mat.HasProperty(eyes) || !mat.HasProperty(face)) return;

            for (int i = 0; i < _stockMats.Count; i++)
                if (_stockMats[i] != null && _stockMats[i].m_CachedPtr == mat.m_CachedPtr)
                {
                    _stockEyes[i] = stock;
                    mat.SetColor(eyes, mat.GetColor(face));
                    return;
                }

            _stockMats.Add(mat);
            _stockEyes.Add(stock);
            mat.SetColor(eyes, mat.GetColor(face));
            Plugin.Log.LogInfo($"sank the painted eye into the face colour on {mat.name}, was {stock}");
        }

        static void SetBodyEyes(GameObject root, Material key, bool stock)
        {
            if (root == null || key is null || key.m_CachedPtr == IntPtr.Zero) return;
            int eyes = FallguyCustomisationHandler.ShaderEyesColor;
            var colour = stock ? StockEyes(key, eyes) : key.GetColor(FallguyCustomisationHandler.ShaderFaceColor);
            var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < smrs.Length; i++)
            {
                if (!smrs[i].name.StartsWith("Body_LOD") || !smrs[i].gameObject.activeInHierarchy) continue;
                var mat = smrs[i].sharedMaterial;
                if (mat == null || !mat.name.EndsWith("(Instance)") || !mat.HasProperty(eyes)) continue;
                mat.SetColor(eyes, colour);
            }
        }

        static void RestoreStockEyes()
        {
            int eyes = FallguyCustomisationHandler.ShaderEyesColor;
            for (int i = 0; i < _stockMats.Count; i++)
                if (_stockMats[i] != null) _stockMats[i].SetColor(eyes, _stockEyes[i]);
            _stockMats.Clear();
            _stockEyes.Clear();
        }

        static void Track(GameObject root)
        {
            if (root == null) return;

            var eyeL = GameObjectHelper.FindBoneOnBean(root, "Eye_L_jnt");
            var eyeR = GameObjectHelper.FindBoneOnBean(root, "Eye_R_jnt");
            if (eyeL == null || eyeR == null) return;

            bool hasOv = _overrides.TryGetValue(root.GetInstanceID(), out var ov);
            string eyeMat = hasOv ? ov.eyeMat : _eyeMat;

            for (int i = 0; i < _count; i++)
            {
                if (_eyes[i].l.m_CachedPtr != eyeL.m_CachedPtr) continue;

                _eyes[i].hasOverride = hasOv;
                if (hasOv)
                {
                    _eyes[i].ovBlink = ov.blink; _eyes[i].ovLook = ov.look; _eyes[i].ovSquint = ov.squint;
                    _eyes[i].ovDist = ov.dist; _eyes[i].ovHeight = ov.height; _eyes[i].ovRot = ov.rot;
                    _eyes[i].ovScale = ov.scale; _eyes[i].ovYscale = ov.yscale;
                }

                if (eyeMat == "none") return;
                var fresh = AttachEyes(root, eyeMat, out var freshBody, out var freshFace);
                if (fresh == null) return;
                EyeGeometry.Detach(_eyes[i].eyeGo);
                _eyes[i].faceMat = freshFace;
                _eyes[i].eyeGo = fresh;
                _eyes[i].eyeRenderer = fresh.GetComponent<SkinnedMeshRenderer>();
                _eyes[i].wantMat = _eyes[i].eyeRenderer != null ? _eyes[i].eyeRenderer.sharedMaterial : null;
                _eyes[i].body = freshBody;
                _eyes[i].hiddenSet = false;
                _eyes[i].cullSet = false;
                return;
            }

            Vector3 restPosL, restPosR, restScaleL, restScaleR;
            Quaternion restRotL, restRotR;
            eyeL.GetLocalPositionAndRotation(out restPosL, out restRotL);
            eyeR.GetLocalPositionAndRotation(out restPosR, out restRotR);
            restScaleL = eyeL.localScale;
            restScaleR = eyeR.localScale;

            if (_bakedFrames == 0 && AssetManager.Instance != null
                && AssetManager.Instance.animClips.TryGetValue("bettrfg_anim_eyes", out var clip) && clip != null)
            {
                var skel = eyeL;
                while (skel != null && skel.name != "SKELETON") skel = skel.parent;
                if (skel != null && skel.parent != null) Bake(clip, skel.parent.gameObject, eyeL, eyeR);
            }

            if (_count == _eyes.Length) Array.Resize(ref _eyes, _count * 2);
            _eyes[_count].eyeGo = AttachEyes(root, eyeMat, out var newBody, out _eyes[_count].faceMat);
            _eyes[_count].eyeRenderer = _eyes[_count].eyeGo != null ? _eyes[_count].eyeGo.GetComponent<SkinnedMeshRenderer>() : null;
            _eyes[_count].wantMat = _eyes[_count].eyeRenderer != null ? _eyes[_count].eyeRenderer.sharedMaterial : null;
            _eyes[_count].body = newBody;
            _eyes[_count].hiddenSet = false;
            _eyes[_count].cullSet = false;
            _eyes[_count].invisHidden = false;
            _eyes[_count].hasOverride = hasOv;
            if (hasOv)
            {
                _eyes[_count].ovBlink = ov.blink; _eyes[_count].ovLook = ov.look; _eyes[_count].ovSquint = ov.squint;
                _eyes[_count].ovDist = ov.dist; _eyes[_count].ovHeight = ov.height; _eyes[_count].ovRot = ov.rot;
                _eyes[_count].ovScale = ov.scale; _eyes[_count].ovYscale = ov.yscale;
            }
            _eyes[_count].invis = root.GetComponentInChildren<InvisibilityVisualsController>();
            _eyes[_count].root = root;
            // preview beans sit parked miles from any game camera - without this the distance/visibility
            // cull in ApplyEyes disables the overlay renderer and freezes its blink/look, so the pet
            // tab's render-texture shot shows only the blanked stock (faceplate-coloured) eyes
            _eyes[_count].preview = root == EyePreview.Clone || root == BetterFG.Customization.Pets.PetPreview.Clone;
            _eyes[_count].l = eyeL;
            _eyes[_count].r = eyeR;
            _eyes[_count].restPosL = restPosL; _eyes[_count].restRotL = restRotL; _eyes[_count].restScaleL = restScaleL;
            _eyes[_count].restPosR = restPosR; _eyes[_count].restRotR = restRotR; _eyes[_count].restScaleR = restScaleR;
            _eyes[_count].blinkSpeed = UnityEngine.Random.Range(0.8f, 1.25f) * _playbackRate;
            _eyes[_count].blinkCursor = UnityEngine.Random.Range(0f, Mathf.Max(1, _bakedFrames));
            _eyes[_count].lookFrom = 0f;
            _eyes[_count].lookTo = 0f;
            _eyes[_count].lookT = 1f;
            _eyes[_count].lookRate = 6f;
            _eyes[_count].lookHold = UnityEngine.Random.Range(0.2f, 2f);
            _eyes[_count].squintPhase = UnityEngine.Random.Range(0f, 6.28f);
            _count++;
        }

        static void Bake(AnimationClip clip, GameObject host, Transform eyeL, Transform eyeR)
        {
            float len = clip.length;
            if (len <= 0f) return;

            int frames = Mathf.Clamp(Mathf.CeilToInt(len * BakeFps), 2, MaxBakeFrames);
            _lPos = new Vector3[frames]; _lRot = new Quaternion[frames]; _lScale = new Vector3[frames];
            _rPos = new Vector3[frames]; _rRot = new Quaternion[frames]; _rScale = new Vector3[frames];

            var t = host.transform;
            t.GetLocalPositionAndRotation(out var hostPos, out var hostRot);
            var hostScale = t.localScale;

            for (int i = 0; i < frames; i++)
            {
                clip.SampleAnimation(host, len * i / frames);
                eyeL.GetLocalPositionAndRotation(out _lPos[i], out _lRot[i]);
                eyeR.GetLocalPositionAndRotation(out _rPos[i], out _rRot[i]);
                _lScale[i] = eyeL.localScale;
                _rScale[i] = eyeR.localScale;
            }

            t.SetLocalPositionAndRotation(hostPos, hostRot);
            t.localScale = hostScale;

            _bakedFrames = frames;
            _playbackRate = frames / len;
            Plugin.Log.LogInfo($"blink clip baked once, {frames} frames off {len:0.00}s, no more per-bean SampleAnimation");
        }

        public static void Tick(float dt)
        {
            if (_pending.Count > 0)
            {
                int last = _pending.Count - 1;
                var next = _pending[last];
                _pending.RemoveAt(last);
                if (next != null) Track(next);
            }

            ApplyEyes(dt);
        }

        public static void TickPreview()
        {
            if (!feature.enabled) return;
            var ui = BetterFG.UI.BetterFGUIMan.Instance;
            if (ui == null || !ui.IsVisible) return;
            if (_previewPanel == null || !_previewPanel.activeInHierarchy || _previewBean == null) return;
            EyePreview.SetBean(_previewBean);
            EyePreview.Render();
        }

        static void ApplyEyes(float dt)
        {
            if (_count == 0) return;

            float now = Time.time;

            // eyes are a couple of centimetres across. past ~30m they're subpixel, but the renderer was
            // still costing a draw call and a skinning pass for every bean on the course — and in a
            // 30-player round most of them are miles away. one camera fetch a tick, one distance test
            // per bean, and everything out of range stops rendering entirely.
            //
            // ROUND ONLY. in the menu/lobby Camera.main isn't the camera framing the plinth bean, so the
            // distance came out huge and the eyes vanished — the menu has at most a couple of beans and
            // nothing to save anyway.
            var cam = _inRound && !_qualScreen ? Camera.main : null;
            bool haveCam = cam != null;
            Vector3 camPos = haveCam ? cam.transform.position : Vector3.zero;
            int frameStagger = Time.frameCount;

            for (int i = 0; i < _count; i++)
            {
                var l = _eyes[i].l;
                var r = _eyes[i].r;
                if (l.m_CachedPtr == IntPtr.Zero || r.m_CachedPtr == IntPtr.Zero)
                {
                    EyeGeometry.Detach(_eyes[i].eyeGo);
                    _eyes[i].eyeGo = null;
                    _count--;
                    _eyes[i] = _eyes[_count];
                    _eyes[_count].l = null;
                    _eyes[_count].r = null;
                    i--;
                    continue;
                }

                var eyeRenderer = _eyes[i].eyeRenderer;
                bool hasRenderer = eyeRenderer is not null && eyeRenderer.m_CachedPtr != IntPtr.Zero;
                if (hasRenderer)
                {
                    bool hidden = _eyes[i].invisHidden;

                    // the range test only has to keep up with a bean walking towards the camera, so it
                    // runs on one bean in eight per frame (staggered, so the work is spread evenly) and
                    // reuses the last answer in between. get_position boxes its return through il2cpp;
                    // the paired getter writes straight into our locals.
                    if (!hidden && !_eyes[i].preview)
                    {
                        if (((i + frameStagger) & 7) == 0 || !_eyes[i].cullSet)
                        {
                            bool cull = BaseHidden(_eyes[i].body);
                            if (!cull && haveCam)
                            {
                                l.GetPositionAndRotation(out var eyePos, out _);
                                float dx = eyePos.x - camPos.x, dy = eyePos.y - camPos.y, dz = eyePos.z - camPos.z;
                                cull = dx * dx + dy * dy + dz * dz > EyeDrawDistanceSqr;
                            }
                            _eyes[i].culled = cull;
                            _eyes[i].cullSet = true;
                        }
                        hidden = _eyes[i].culled;
                    }
                    // re-writing .enabled every frame was a renderer setter per bean for a value that
                    // only moves when a powerup does
                    if (!_eyes[i].hiddenSet || _eyes[i].hiddenValue != hidden)
                    {
                        _eyes[i].hiddenSet = true;
                        _eyes[i].hiddenValue = hidden;
                        eyeRenderer.enabled = !hidden;
                        SetBodyEyes(_eyes[i].root, _eyes[i].faceMat, hidden);
                    }
                }

                bool ov = _eyes[i].hasOverride;
                bool blinkOn = (ov ? _eyes[i].ovBlink : _blink) && _bakedFrames > 0;
                bool lookOn = ov ? _eyes[i].ovLook : _look;
                bool squintOn = ov ? _eyes[i].ovSquint : _squint;
                float dist = ov ? _eyes[i].ovDist : _dist;
                float height = ov ? _eyes[i].ovHeight : _height;
                float rot = ov ? _eyes[i].ovRot : _rot;
                float scale = ov ? _eyes[i].ovScale : _scale;
                float yscale = ov ? _eyes[i].ovYscale : _yscale;

                Vector3 pl = _eyes[i].restPosL, pr = _eyes[i].restPosR, sl = _eyes[i].restScaleL, sr = _eyes[i].restScaleR;
                Quaternion ql = _eyes[i].restRotL, qr = _eyes[i].restRotR;

                if (blinkOn)
                {
                    float c = _eyes[i].blinkCursor + dt * _eyes[i].blinkSpeed;
                    _eyes[i].blinkCursor = c;

                    int idx = (int)Mathf.PingPong(c, _bakedFrames - 1);
                    pl = _lPos[idx]; ql = _lRot[idx]; sl = _lScale[idx];
                    pr = _rPos[idx]; qr = _rRot[idx]; sr = _rScale[idx];
                }

                float glance = lookOn ? StepLook(ref _eyes[i], dt) * LookOffset : 0f;
                float squint = squintOn
                    ? 1f - 0.2f * (0.55f + 0.45f * Mathf.Sin(now * 0.7f + _eyes[i].squintPhase))
                    : 1f;

                // blink/look/squint state above is plain managed maths and keeps running, so a bean
                // that comes back into view is already where it should be. the four writes below are
                // the expensive part — every one boxes a struct through il2cpp — and nobody can see
                // the eyes of a bean that isn't on screen.
                if (hasRenderer && !_eyes[i].preview && !eyeRenderer.isVisible) continue;

                pl.x += glance - dist;
                pr.x += glance + dist;
                pl.y += height;
                pr.y += height;

                float yl = sl.y * scale * yscale * squint;
                float yr = sr.y * scale * yscale * squint;

                bool hasRot = !Mathf.Approximately(rot, 0f);
                if (hasRot)
                {
                    ql *= Quaternion.Euler(0f, 0f, rot);
                    qr *= Quaternion.Euler(0f, 0f, -rot);
                }

                l.SetLocalPositionAndRotation(pl, ql);
                r.SetLocalPositionAndRotation(pr, qr);

                l.localScale = new Vector3(sl.x * scale, yl, sl.z * scale);
                r.localScale = new Vector3(sr.x * scale, yr, sr.z * scale);
            }
        }

        static bool BaseHidden(SkinnedMeshRenderer body)
        {
            if (body is null || body.m_CachedPtr == IntPtr.Zero) return false;
            if (!body.gameObject.activeInHierarchy || !body.enabled) return true;
            var invis = Customization.Player.CostumePollerComponent.PeekInvisibleMat();
            if (invis is null) return false;
            var mat = body.sharedMaterial;
            return mat is not null && mat.m_CachedPtr == invis.m_CachedPtr;
        }

        static float StepLook(ref Eyes e, float dt)
        {
            if (e.lookT < 1f)
            {
                e.lookT = Mathf.Min(1f, e.lookT + dt * e.lookRate);
                if (e.lookT >= 1f) e.lookHold = UnityEngine.Random.Range(0.5f, 2.8f);
                return Mathf.Lerp(e.lookFrom, e.lookTo, Mathf.SmoothStep(0f, 1f, e.lookT));
            }

            float held = e.lookTo;
            e.lookHold -= dt;
            if (e.lookHold <= 0f)
            {
                e.lookFrom = held;
                e.lookTo = UnityEngine.Random.Range(-1f, 1f);
                e.lookT = 0f;
                e.lookRate = 1f / UnityEngine.Random.Range(0.12f, 0.24f);
            }
            return held;
        }
    }

    public class FallGuyEyeDriver : MonoBehaviour
    {
        public FallGuyEyeDriver(IntPtr ptr) : base(ptr) { }

        public static FallGuyEyeDriver Instance { get; private set; }

        void Awake() => Instance = this;

        static bool _previewLoopRunning;

        // was started once in Awake and span forever on WaitForEndOfFrame, whether or not the feature
        // was on and whether or not the panel existed — a per-frame il2cpp coroutine resume for the
        // entire session, menus included, with TickPreview early-outing every time. now it only lives
        // while the BettrFG panel is actually open in front of the preview.
        public static void EnsurePreviewLoop()
        {
            if (_previewLoopRunning || Instance == null) return;
            if (!FeatureCustomizeFallGuys.PreviewWanted) return;
            _previewLoopRunning = true;
            Instance.StartCoroutine(PreviewLoop().WrapToIl2Cpp());
        }

        // runs EVERY frame, deliberately. ticking the eye pass at 30Hz to save the transform writes made
        // the eyes visibly jitter against a body rendering at 165+ — the blink source frames are baked at
        // 30fps but the look/squint drifts and the interpolation between them are not. tried 2026-08-21.
        void LateUpdate()
        {
            if (!FeatureCustomizeFallGuys.On && !FeatureCustomizeFallGuys.HasOverrides) return;
            FeatureCustomizeFallGuys.Tick(Time.deltaTime);
        }

        static IEnumerator PreviewLoop()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (FeatureCustomizeFallGuys.PreviewWanted)
            {
                yield return endOfFrame;
                FeatureCustomizeFallGuys.TickPreview();
            }
            // panel's gone, so is the reason to keep a whole spare bean skinning off-screen
            EyePreview.Invalidate();
            _previewLoopRunning = false;
        }
    }
}
