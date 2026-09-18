using System;
using System.Collections.Generic;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.Tweaks;
using BetterFG.Utilities;
using FG.Common;
using FGClient.UI;
using FGClient.UI.Core;
using LevelEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Features.CreativeExpansion
{
    public class IntroCamPreview : MonoBehaviour
    {
        public IntroCamPreview(IntPtr ptr) : base(ptr) { }

        public static IntroCamPreview Instance { get; private set; }

        private const int PipWidth = 448;
        private const int PipHeight = 252;
        private const float PipMargin = 26f;
        private const int ExitGraceFrames = 20;

        private static readonly string[] EditorUiRoots =
        {
            "UICanvas_Client_V2(Clone)/Default/Prime_UI_LE_Navigation_Canvas(Clone)",
            "UICanvas_Client_V2(Clone)/Popup/Prime_UI_LE_ParametersMenu_Canvas(Clone)",
            "UICanvas_Client_V2(Clone)/Default/Prime_UI_LE_UndoHistoryList_Prefab_Canvas(Clone)",
            "NavigationHintUI/Prime_UI_LE_HUDMessageManager",
        };

        private Camera _pipCam;
        private RenderTexture _pipRt;
        private Canvas _canvas;
        private RectTransform _pipFrame;
        private Transform _pipTarget;
        private bool _pipShown;

        private bool _playing;
        private int _startFrame;
        private float _t0;
        private float _duration;
        private float _length;
        private Vector3[] _path;
        private Vector3[] _look;
        private float[] _arc;

        private Camera _cam;
        private Cinemachine.CinemachineBrain _brain;
        private LevelEditorCameraController _controller;
        private float _originalFov;
        private NavigationPromptData _exitData;

        private readonly List<CanvasGroup> _dimmed = new List<CanvasGroup>();
        private readonly List<Renderer> _reticleHidden = new List<Renderer>();

        private int _gizmosHidden;
        private GameObject _worldGizmos;
        private Transform _heightHelper;
        private Vector3 _heightHelperScale;

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public static void Play()
        {
            if (Instance != null) Instance.Begin();
        }

        private void Begin()
        {
            if (_playing) return;

            CustomIntroCams.Sync();
            if (!CustomIntroCams.TryBuildEditorPath(out _path, out _look, out _duration))
            {
                Plugin.Log.LogWarning("nothing to preview, this rig has no shots on it yet");
                return;
            }

            var lem = LevelEditorManager.Instance;
            var lec = lem != null ? lem.Camera : null;
            if (lec == null) { Plugin.Log.LogWarning("no level editor camera to borrow, preview is off"); return; }

            _controller = lec.Controller;

            var node = GameObject.Find("LevelEditorCameraNode");
            _cam = node != null ? node.GetComponent<Camera>() : null;
            if (_cam == null && _controller != null) _cam = _controller.GetComponentInChildren<Camera>(true);
            if (_cam == null)
            {
                var director = lec.Director;
                _cam = director != null ? director.MainNativeCam : Camera.main;
            }
            if (_cam == null) { Plugin.Log.LogWarning("no editor camera to drive, preview is off"); return; }
            Plugin.Log.LogInfo($"preview is driving {_cam.name} (depth {_cam.depth})");

            _brain = lec.GetCinemachineBrain();
            _originalFov = _cam.fieldOfView;

            if (_brain != null) _brain.enabled = false;
            if (_controller != null) _controller.enabled = false;
            _cam.fieldOfView = CustomIntroCams.PreviewFov;

            _arc = CreativeIntroCameraTweak.ArcTable(_path, out _length);
            _t0 = Time.time;
            _startFrame = Time.frameCount;
            _playing = true;

            SetPipTarget(null);
            HideEditorChrome();
            HideGizmos();


            if (_exitData == null)
                _exitData = NavPromptInjection.BuildData(NavPrompt.Back, LocalizationService.Get("introcams.exit_preview"),
                    "bfg_introcam_exit", RewiredConsts.Action.Menu_UICancel, RewiredConsts.Category.Menu);
            NavPromptInjection.Add(NavPromptInjection.IntroCamExit, ExitPressed, _exitData);

            Plugin.Log.LogInfo($"intro cam preview rolling, {_path.Length} shot(s) over {_duration:0.0}s");
        }

        private void ExitPressed() => Stop("exit prompt");

        private void Stop(string why)
        {
            if (!_playing) return;
            _playing = false;

            NavPromptInjection.Remove(NavPromptInjection.IntroCamExit);
            RestoreEditorChrome();
            ShowGizmos();

            if (_cam != null) _cam.fieldOfView = _originalFov;
            if (_brain != null) _brain.enabled = true;
            if (_controller != null) _controller.enabled = true;

            _cam = null;
            _brain = null;
            _controller = null;
            _path = null;
            _look = null;
            _arc = null;
            Plugin.Log.LogInfo($"preview over ({why}), the editor has its camera back");
        }

        private void HideEditorChrome()
        {
            _dimmed.Clear();
            foreach (var path in EditorUiRoots)
            {
                var go = GameObject.Find(path);
                if (go == null) continue;
                var cg = go.GetComponent<CanvasGroup>();
                if (cg == null) cg = go.AddComponent<CanvasGroup>();
                cg.alpha = 0f;
                cg.blocksRaycasts = false;
                cg.interactable = false;
                _dimmed.Add(cg);
            }
            if (_dimmed.Count == 0) Plugin.Log.LogWarning("none of the editor ui roots were where I expected, the preview keeps the hud over it");
        }

        private void RestoreEditorChrome()
        {
            for (int i = 0; i < _dimmed.Count; i++)
            {
                var cg = _dimmed[i];
                if (cg == null) continue;
                cg.alpha = 1f;
                cg.blocksRaycasts = true;
                cg.interactable = true;
            }
            _dimmed.Clear();
        }

        private void HideGizmos()
        {
            _gizmosHidden = 1;

            var helper = GameObject.Find("Rule_HeightHelper(Clone)");
            if (helper != null)
            {
                _heightHelper = helper.transform;
                _heightHelperScale = _heightHelper.localScale;
                _heightHelper.localScale = Vector3.zero;
            }
            else Plugin.Log.LogInfo("no Rule_HeightHelper(Clone) in this level, nothing to squash");

            _worldGizmos = GameObject.Find("WorldSpace_Canvas");
            if (_worldGizmos != null) _worldGizmos.SetActive(false);
            else Plugin.Log.LogWarning("WorldSpace_Canvas wasn't there to switch off, marker gizmos will be in shot");

            CustomIntroCams.ShowShotGizmos(false);
            RefreshWorldSpaceUiBuffer();

            _reticleHidden.Clear();
            var lem = LevelEditorManager.Instance;
            var lec = lem != null ? lem.Camera : null;
            var reticle = lec != null ? lec.ReticleGameObject : null;
            if (reticle != null)
                foreach (var r in reticle.GetComponentsInChildren<Renderer>(true))
                    if (r != null && r.enabled) { r.enabled = false; _reticleHidden.Add(r); }
        }

        private void ShowGizmos()
        {
            if (_gizmosHidden == 0) return;
            _gizmosHidden = 0;

            if (_heightHelper != null) _heightHelper.localScale = _heightHelperScale;
            _heightHelper = null;

            if (_worldGizmos != null) _worldGizmos.SetActive(true);
            _worldGizmos = null;

            CustomIntroCams.ShowShotGizmos(true);
            RefreshWorldSpaceUiBuffer();

            for (int i = 0; i < _reticleHidden.Count; i++)
                if (_reticleHidden[i] != null) _reticleHidden[i].enabled = true;
            _reticleHidden.Clear();
        }

        private static void RefreshWorldSpaceUiBuffer()
        {
            var wsui = LevelEditorWorldSpaceUIManager.Instance;
            if (wsui != null) wsui.UpdateCameraBuffer();
        }

        void Update()
        {
            if (!IdentifierObjects.InEditor())
            {
                Stop("left the editor");
                SetPipTarget(null);
                return;
            }

            if (_playing)
            {
                if (Time.frameCount - _startFrame < ExitGraceFrames) return;
                if (KeybindService.KeyDown(KeyCode.Escape) ||
                    NavPromptCore.PollActionDirect(RewiredConsts.Action.Menu_UICancel, null, true))
                    Stop("cancel pressed");
                return;
            }

            var mgr = LevelEditorManager.Instance;
            var reticle = mgr != null ? mgr.GetReticleBase() : null;
            var selected = reticle != null ? reticle.SelectedObject : null;
            SetPipTarget(CustomIntroCams.IsShot(selected) ? selected.transform : null);

            if (_gizmosHidden > 0)
            {
                Plugin.Log.LogWarning("gizmos were still hidden with nothing previewing, putting them back");
                ShowGizmos();
            }
        }

        private void SetPipTarget(Transform target)
        {
            _pipTarget = target;

            if (target == null)
            {
                if (!_pipShown) return;
                _pipShown = false;
                if (_pipFrame != null) _pipFrame.gameObject.SetActive(false);
                if (_pipCam != null) _pipCam.enabled = false;
                return;
            }

            if (_pipShown) return;
            _pipShown = true;
            EnsurePip();
            _pipFrame.gameObject.SetActive(true);
        }

        private void EnsurePip()
        {
            if (_pipCam != null && _canvas != null) return;

            _pipRt = new RenderTexture(PipWidth, PipHeight, 24, RenderTextureFormat.ARGB32);
            _pipRt.name = "BettrFG_IntroCamPip";
            _pipRt.Create();

            var camGo = new GameObject("BettrFG_IntroCamPipCam");
            camGo.transform.SetParent(transform, false);
            _pipCam = camGo.AddComponent<Camera>();
            _pipCam.targetTexture = _pipRt;
            _pipCam.fieldOfView = CustomIntroCams.PreviewFov;
            _pipCam.depth = -50f;
            _pipCam.enabled = false;

            var editorCam = CustomIntroCams.EditorCamera();
            if (editorCam != null) _pipCam.cullingMask = editorCam.cullingMask;

            var canvasGo = new GameObject("BettrFG_IntroCamCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 4000;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            UIScaleService.Register(_canvas);

            var frameGo = new GameObject("Frame");
            _pipFrame = frameGo.AddComponent<RectTransform>();
            _pipFrame.SetParent(_canvas.transform, false);
            _pipFrame.anchorMin = _pipFrame.anchorMax = _pipFrame.pivot = new Vector2(0f, 0f);
            _pipFrame.anchoredPosition = new Vector2(PipMargin, PipMargin);
            _pipFrame.sizeDelta = new Vector2(PipWidth, PipHeight);

            var (_, slot) = BettrFG.uGUI.UGUIShip.CreateFramedImage(frameGo.transform);
            slot.gameObject.AddComponent<RectMask2D>();

            var raw = slot.gameObject.AddComponent<RawImage>();
            raw.texture = _pipRt;
            raw.raycastTarget = false;

            _pipFrame.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (_gizmosHidden > 0)
            {
                var wsui = LevelEditorWorldSpaceUIManager.Instance;
                var buffer = wsui != null ? wsui.WorldSpaceUIBuffer : null;
                if (buffer != null) buffer.Clear();
            }

            if (_pipShown && _pipTarget != null && _pipCam != null)
            {
                _pipCam.transform.SetPositionAndRotation(_pipTarget.position, _pipTarget.rotation);
                CustomIntroCams.SetGizmoRenderersEnabled(false);
                _pipCam.Render();
                CustomIntroCams.SetGizmoRenderersEnabled(true);
            }

            if (CustomIntroCams.HasLabels)
            {
                var viewer = _playing && _cam != null ? _cam : CustomIntroCams.EditorCamera();
                if (viewer != null) CustomIntroCams.BillboardLabels(viewer.transform);
            }

            if (!_playing) return;
            if (_cam == null) { Stop("camera went away"); return; }
            if (_brain != null && _brain.enabled) _brain.enabled = false;

            float raw = _duration > 0f ? Mathf.Clamp01((Time.time - _t0) / _duration) : 1f;
            float eased = raw * raw * (3f - 2f * raw);
            float u = CreativeIntroCameraTweak.ArcParam(_arc, _length, eased);

            var pos = CreativeIntroCameraTweak.Spline(_path, u);
            var look = CreativeIntroCameraTweak.Spline(_look, u);
            var dir = look - pos;
            if (dir.sqrMagnitude < 0.0001f) return;

            _cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir, Vector3.up));
        }
    }
}
