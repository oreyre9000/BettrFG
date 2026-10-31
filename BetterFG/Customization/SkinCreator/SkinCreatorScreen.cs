using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Pets;
using BetterFG.Customization.Player;
using BetterFG.Features.TimePlacement;
using BetterFG.Services;
using BetterFG.UI;
using BetterFG.Utilities;
using BettrFG.uGUI;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.EventSystems;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace BetterFG.Customization.SkinCreator
{
    public class SkinCreatorScreen : MonoBehaviour
    {
        public SkinCreatorScreen(IntPtr ptr) : base(ptr) { }

        public static SkinCreatorScreen Instance;

        const int ContentOrder = 501;
        const float EDGE = 22f;
        const float TOP = 70f;
        const float BAR_H = 44f;
        const float BTN_W = 150f;
        const float PANEL_W = 440f;
        const float HEIGHT = 1.98f;

        static readonly Vector3 Stage = new Vector3(0f, -3000f, 0f);
        static readonly Vector3 Parked = new Vector3(600f, -3000f, 0f);
        static readonly Color GREEN = new Color(0.22f, 0.42f, 0.26f, 1f);
        static readonly Color HINT = new Color(1f, 1f, 1f, 0.5f);

        ImportedModel _model;
        string _name;
        int _step;
        bool _closing;

        Canvas _canvas;
        Text _title, _hint, _backLbl, _nextLbl;
        RectTransform _panel;
        GameObject _scroll;
        SkinBoneMapView _view;

        GameObject _camHost, _staticGo, _beanHolder, _custom;
        Camera _cam;
        Material[] _mats;
        Mesh _staticMesh, _customMesh;
        AmbientMode _wasAmbientMode;
        Color _wasAmbient;
        bool _wasFog;
        float _yaw = 160f, _pitch = 8f, _dist = 4.2f;
        bool _dragging;
        Vector3 _lastMouse;

        Transform _bean;
        SkinnedMeshRenderer _body;
        string[] _beanNames, _rawBeanNames;
        string _rigFolder;
        List<(string bean, string bone)> _pairs;
        int[] _map;

        Animator _anim;
        AnimationClip[] _clips;
        string[] _clipNames;
        PlayableGraph _graph;
        bool _graphLive;
        AnimationClip _playing;
        WrapMode _playingWrap;

        public static void Open(string path) => Launch(path, null, null);

        public static void Edit(string rigPath)
        {
            var (modelFile, pairs) = CustomRigStore.Read(rigPath);
            string folder = System.IO.Path.GetDirectoryName(rigPath);
            Launch(System.IO.Path.Combine(folder, modelFile), folder, pairs);
        }

        static void Launch(string path, string rigFolder, List<(string bean, string bone)> pairs)
        {
            if (Instance != null || string.IsNullOrEmpty(path)) return;
            ImportedModel model;
            try { model = SkinImport.Load(path); }
            catch (Exception ex) { Plugin.Log.LogError($"couldn't import {path}: {ex}"); return; }
            if (model.BoneNames.Length == 0) { Plugin.Log.LogWarning($"{path} has no skeleton in it, nothing to map"); return; }

            var go = new GameObject("BettrFG_SkinCreator");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<SkinCreatorScreen>();
            Instance._model = model;
            Instance._rigFolder = rigFolder;
            Instance._pairs = pairs;
            Instance._name = rigFolder != null ? System.IO.Path.GetFileName(rigFolder) : System.IO.Path.GetFileNameWithoutExtension(path);
        }

        void Start() => StartCoroutine(OpenRoutine().WrapToIl2Cpp());

        IEnumerator OpenRoutine()
        {
            LoadingScreenService.Show();
            yield return new WaitForSecondsRealtime(0.35f);

            try
            {
                WorldHideService.HideCanvases(this);
                BetterFGUIMan.Instance?.SetVisibleForced(false);
                if (BetterFGUIMan.Instance != null) BetterFGUIMan.Instance.VisibilityLocked = true;
                BuildStage();
                BuildUi();
                BuildStatic();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"skin creator wouldn't build ({ex}), backing out");
                Finish();
                yield break;
            }

            yield return PetBeanBuilder.Build(new BeanLook().ToPetData(), new Action<GameObject>(OnBean), forPreview: true);
            if (_bean == null)
            {
                Plugin.Log.LogWarning("skin creator got no reference bean back, closing");
                Finish();
                yield break;
            }

            try
            {
                SetupBean(_bean.gameObject);
                if (_pairs != null) _map = CustomRigStore.ToMap(_rawBeanNames, _model, _pairs);
                SetStep(_pairs != null ? 1 : 0);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"skin creator reference bean setup threw: {ex}");
                Finish();
                yield break;
            }

            yield return new WaitForSecondsRealtime(0.15f);
            yield return StartCoroutine(LoadingScreenService.HideRoutine().WrapToIl2Cpp());
        }

        void OnBean(GameObject bean) => _bean = bean == null ? null : bean.transform;

        void Finish()
        {
            if (_closing) return;
            _closing = true;
            StartCoroutine(CloseRoutine().WrapToIl2Cpp());
        }

        IEnumerator CloseRoutine()
        {
            LoadingScreenService.Show();
            yield return new WaitForSecondsRealtime(0.35f);

            try
            {
                StopClip();
                if (_view != null) _view.Destroy();
                RenderSettings.ambientMode = _wasAmbientMode;
                RenderSettings.ambientLight = _wasAmbient;
                RenderSettings.fog = _wasFog;
                if (_staticGo != null) Destroy(_staticGo);
                if (_beanHolder != null) Destroy(_beanHolder);
                if (_camHost != null) Destroy(_camHost);
                if (_canvas != null) Destroy(_canvas.gameObject);
                foreach (var m in _mats) Destroy(m);
                if (_staticMesh != null) Destroy(_staticMesh);
                if (_customMesh != null) Destroy(_customMesh);
            }
            catch (Exception ex) { Plugin.Log.LogError($"skin creator teardown threw: {ex}"); }

            WorldHideService.Show(this);
            RestoreBettrFGUi();
            Instance = null;

            yield return StartCoroutine(LoadingScreenService.HideRoutine().WrapToIl2Cpp());
            Destroy(gameObject);
        }

        static void RestoreBettrFGUi()
        {
            if (BetterFGUIMan.Instance == null) return;
            BetterFGUIMan.Instance.VisibilityLocked = false;
            BetterFGUIMan.Instance.SetVisibleForced(true);
        }

        void OnDestroy()
        {
            WorldHideService.Show(this);
            RestoreBettrFGUi();
        }

        void BuildStage()
        {
            _camHost = new GameObject("BettrFG_SkinCreatorCam");
            _camHost.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(_camHost);

            _cam = _camHost.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.1f, 0.1f, 0.13f);
            _cam.cullingMask = 1 << LeaderboardMugshotScene.Layer;
            _cam.fieldOfView = 40f;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 400f;
            _cam.depth = 1000f;
            _cam.allowHDR = false;
            _cam.useOcclusionCulling = false;
            _cam.ResetAspect();

            AddLight("Key", new Vector3(35f, 18f, 0f), 1.1f);
            AddLight("Fill", new Vector3(12f, -142f, 0f), 0.45f);

            _wasAmbientMode = RenderSettings.ambientMode;
            _wasAmbient = RenderSettings.ambientLight;
            _wasFog = RenderSettings.fog;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.34f, 0.34f, 0.37f);
            RenderSettings.fog = false;

            _mats = SkinRetarget.Materials(_model);
        }

        void AddLight(string name, Vector3 angles, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_camHost.transform, false);
            go.transform.localRotation = Quaternion.Euler(angles);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.cullingMask = 1 << LeaderboardMugshotScene.Layer;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
        }

        void BuildUi()
        {
            _canvas = BetterFGUIMan.CreateCanvas();
            _canvas.gameObject.name = "BettrFG_SkinCreator_Canvas";
            _canvas.sortingOrder = ContentOrder;

            _title = UGUIShip.CreateLabel(_canvas.transform, new Rect(EDGE, EDGE, 900f, 40f), "skin_creator.step_1",
                UIScale.FS_TITLE, UGUIShip.WHITE);
            _title.raycastTarget = false;
            _title.fontStyle = FontStyle.Bold;
            _title.rectTransform.pivot = new Vector2(0f, 1f);
            _title.rectTransform.localScale = new Vector3(1.3f, 1.3f, 1f);
            _hint = UGUIShip.CreateLabel(_canvas.transform, new Rect(0f, 0f, 700f, BAR_H),
                "skin_creator.hint_orbit", UIScale.FS_SM, HINT, TextAnchor.MiddleCenter);
            _hint.raycastTarget = false;
            Pin(_hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, EDGE));

            var back = UGUIShip.CreateButton(_canvas.transform, new Rect(0f, 0f, BTN_W, BAR_H),
                "skin_creator.cancel", UGUIShip.BTN_DARK, UGUIShip.WHITE, UIScale.FS_TITLE, new Action(Back));
            Pin(back.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(EDGE, EDGE));
            _backLbl = back.GetComponentInChildren<Text>();
            var next = UGUIShip.CreateButton(_canvas.transform, new Rect(0f, 0f, BTN_W, BAR_H),
                "skin_creator.next", GREEN, UGUIShip.WHITE, UIScale.FS_TITLE, new Action(Next));
            Pin(next.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-EDGE, EDGE));
            _nextLbl = next.GetComponentInChildren<Text>();

            _panel = UGUIShip.CreatePanel(_canvas.transform, new Rect(0f, 0f, PANEL_W, 100f), Color.white);
            UGUIShip.ApplyDeluxPanelDark(_panel.GetComponent<Image>());
            _panel.anchorMin = new Vector2(1f, 0f);
            _panel.anchorMax = Vector2.one;
            _panel.pivot = new Vector2(1f, 0.5f);
            _panel.offsetMin = new Vector2(-PANEL_W - EDGE, EDGE * 2f + BAR_H);
            _panel.offsetMax = new Vector2(-EDGE, -TOP);
        }

        static void Pin(RectTransform rt, Vector2 anchor, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
        }

        void BuildStatic()
        {
            var p = _model.Pos;
            float k = _model.Fit(HEIGHT, out var origin);
            var verts = new Vector3[p.Length];
            for (int i = 0; i < p.Length; i++) verts[i] = (p[i] - origin) * k;

            _staticMesh = new Mesh { name = "BettrFG_SkinCreatorPreview" };
            _staticMesh.vertices = verts;
            _staticMesh.normals = _model.Nrm;
            _staticMesh.uv = _model.Uv;
            SkinRetarget.SetParts(_staticMesh, _model);
            _staticMesh.RecalculateBounds();

            _staticGo = new GameObject("BettrFG_SkinCreatorPreview");
            _staticGo.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(_staticGo);
            _staticGo.layer = LeaderboardMugshotScene.Layer;
            _staticGo.transform.position = Stage;
            _staticGo.AddComponent<MeshFilter>().sharedMesh = _staticMesh;
            _staticGo.AddComponent<MeshRenderer>().sharedMaterials = _mats;
        }

        void SetupBean(GameObject bean)
        {
            _beanHolder = new GameObject("BettrFG_SkinCreatorBean");
            _beanHolder.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(_beanHolder);
            bean.transform.SetParent(_beanHolder.transform, false);

            foreach (var t in bean.GetComponentsInChildren<TMPro.TMP_Text>(true))
                if (t != null) DestroyImmediate(t.gameObject);

            GameObjectHelper.SetLayerRecursively(bean, LeaderboardMugshotScene.Layer);
            bean.transform.SetPositionAndRotation(Parked, Quaternion.identity);

            foreach (var smr in bean.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.updateWhenOffscreen = true;
                if (_body == null && smr.name.StartsWith("Body_LOD0")) _body = smr;
            }

            var bones = _body.bones;
            _beanNames = new string[bones.Length];
            _rawBeanNames = new string[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                _rawBeanNames[i] = bones[i].name;
                _beanNames[i] = SkinRetarget.Friendly(bones[i].name);
            }
            _map = new int[bones.Length];
            for (int i = 0; i < _map.Length; i++) _map[i] = -1;

            _anim = bean.transform.Find("Character").GetComponent<Animator>();
            var all = _anim.runtimeAnimatorController.animationClips;
            var seen = new HashSet<string>();
            var list = new List<AnimationClip>();
            for (int i = 0; i < all.Length; i++)
                if (all[i].length > 0.2f && seen.Add(all[i].name)) list.Add(all[i]);
            list.Sort(new Comparison<AnimationClip>(CompareClips));
            _clips = list.ToArray();
            _clipNames = new string[_clips.Length + 1];
            _clipNames[0] = "skin_creator.anim_default";
            for (int i = 0; i < _clips.Length; i++) _clipNames[i + 1] = _clips[i].name;
        }

        static int CompareClips(AnimationClip a, AnimationClip b) => string.CompareOrdinal(a.name, b.name);

        bool AnyMapped()
        {
            foreach (int m in _map) if (m >= 0) return true;
            return false;
        }

        void SetStep(int s)
        {
            _step = s;
            _title.text = LocalizationService.Get("skin_creator.step_" + (s + 1));
            _backLbl.text = LocalizationService.Get(s == 0 ? "skin_creator.cancel" : "skin_creator.back");
            _nextLbl.text = LocalizationService.Get(s == 2 ? "skin_creator.finish" : "skin_creator.next");

            StopClip();
            if (_view != null) { _view.Destroy(); _view = null; }
            if (_scroll != null) Destroy(_scroll);
            _panel.gameObject.SetActive(s > 0);
            _staticGo.SetActive(s < 2);
            _bean.position = s == 2 ? Stage : Parked;
            if (s == 0) return;

            if (s == 1)
            {
                _view = new SkinBoneMapView(_model, _beanNames, _map, _cam, _staticGo.transform, _canvas, _panel, HEIGHT);
                return;
            }

            RebuildCustom();
            _scroll = new GameObject("Animations");
            var root = _scroll.AddComponent<RectTransform>();
            root.SetParent(_panel, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            SkinCreatorRows.Header(root, "skin_creator.header_anims", "skin_creator.anim_hint");
            var (sr, content) = SkinCreatorRows.List(root, 0f);
            content.sizeDelta = new Vector2(0f, SkinCreatorRows.Clips(content, PANEL_W - UGUIShip.SCROLLBAR_INSET * 2f, _clipNames, new Action<int>(PlayClip)));
        }

        void RebuildCustom()
        {
            if (_custom != null) Destroy(_custom);
            if (_customMesh != null) Destroy(_customMesh);
            _custom = SkinRig.Build(_model, _body, _map, _bean.gameObject, _mats, LeaderboardMugshotScene.Layer, out _customMesh);

            foreach (var lod in _beanHolder.GetComponentsInChildren<LODGroup>(true)) lod.enabled = false;
            foreach (var r in _beanHolder.GetComponentsInChildren<Renderer>(true))
                if (!r.transform.IsChildOf(_custom.transform)) r.enabled = false;
        }

        void PlayClip(int i)
        {
            StopClip();
            if (i == 0) return;
            _playing = _clips[i - 1];
            _playingWrap = _playing.wrapMode;
            _playing.wrapMode = WrapMode.Loop;
            _graph = PlayableGraph.Create("BettrFG_SkinCreator");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.UnscaledGameTime);
            var output = AnimationPlayableOutput.Create(_graph, "out", _anim);
            output.SetSourcePlayable(AnimationClipPlayable.Create(_graph, _playing));
            _graph.Play();
            _graphLive = true;
        }

        void StopClip()
        {
            if (!_graphLive) return;
            _graph.Destroy();
            _graphLive = false;
            _playing.wrapMode = _playingWrap;
        }

        void Apply()
        {
            string folder = CustomRigStore.Save(_rigFolder, _name, _model, _rawBeanNames, _map);
            Plugin.Log.LogInfo($"custom rig skin saved to {folder}, importing it like any other skin");
            CustomizationServices.LoaderService.ImportSkinFromFolder(folder);
        }

        void Back()
        {
            if (_closing) return;
            if (_step == 0) Finish();
            else SetStep(_step - 1);
        }

        void Next()
        {
            if (_closing) return;
            if (_step == 2) { Apply(); Finish(); return; }
            if (_step == 1 && !AnyMapped()) return;
            SetStep(_step + 1);
        }

        void Update()
        {
            if (_closing || _camHost == null) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Back(); return; }

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (Input.GetMouseButtonDown(0) && !overUi) { _dragging = true; _lastMouse = Input.mousePosition; }
            if (Input.GetMouseButtonUp(0)) _dragging = false;
            if (_dragging)
            {
                var delta = Input.mousePosition - _lastMouse;
                _lastMouse = Input.mousePosition;
                _yaw += delta.x * 0.4f;
                _pitch = Mathf.Clamp(_pitch - delta.y * 0.3f, -60f, 80f);
            }
            float scroll = Input.mouseScrollDelta.y;
            if (scroll != 0f && !overUi) _dist = Mathf.Clamp(_dist - scroll * 0.4f, 1.5f, 9f);

            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            var target = Stage + Vector3.up;
            var pos = target - rot * Vector3.forward * _dist;
            if (_step > 0) pos += rot * Vector3.right * (_dist * 0.33f);
            _camHost.transform.SetPositionAndRotation(pos, rot);
            _view?.Tick();
        }
    }
}
