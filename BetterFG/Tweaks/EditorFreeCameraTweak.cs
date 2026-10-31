using System;
using System.Collections.Generic;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.Utilities;
using FG.Common;
using FGClient;
using FGClient.UI.Core;
using Rewired;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class EditorFreeCameraTweak : BfgTweak
    {
        public EditorFreeCameraTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "editor_free_camera";
        public override string TweakLabel => "tweak.editor_free_camera";
        public override string TweakTooltip => "tweak.editor_free_camera.tip";

        const float FovSpeed = 25f;
        const float FovNudge = 5f;
        const float MinFov = 20f;
        const float MaxFov = 120f;
        const float PadOrbitSpeed = 110f;
        const float MouseOrbitSpeed = 0.2f;
        const float ScrollZoomStep = 8f;
        const float PadZoomSpeed = 60f;
        const float MoveSpeedPerDistance = 1.2f;
        const float BoostMul = 3f;

        static readonly string[] DpadUpNames = { "D-Pad Up", "DPad Up", "Hat Up" };
        static readonly string[] DpadDownNames = { "D-Pad Down", "DPad Down", "Hat Down" };

        static readonly string[] UiRoots =
        {
            "UICanvas_Client_V2(Clone)",
            "Prefab_UI_NavigationOverlay(Clone)",
            "NavigationHintUI",
        };

        bool _tracking;
        Vector3 _lastOffset;
        float _lastPitch;
        float _lastYaw;
        bool _promptsUp;
        bool _uiHidden;
        bool _playCam;
        bool _mouseOrbiting;
        Vector3 _lastMouse;
        LevelEditorCameraController _controller;
        readonly List<CanvasGroup> _hidden = new List<CanvasGroup>();
        readonly List<Renderer> _hiddenGizmos = new List<Renderer>();
        readonly List<Canvas> _hiddenCanvases = new List<Canvas>();
        NavigationPromptData _fovDownData;
        NavigationPromptData _fovUpData;
        NavigationPromptData _hideUiData;

        public override void DisableTweak()
        {
            _tracking = false;
            SetPrompts(false);
            SetPlayCam(false);
            SetUiHidden(false);
        }

        void LateUpdate()
        {
            var mgr = LevelEditorManager.Instance;
            var state = mgr != null ? mgr.CurrentState : null;
            if (state != null && state.TryCast<LevelEditorStatePlay>() != null)
            {
                _tracking = false;
                SetPrompts(false);
                if (KeybindService.KeyDown(KeyCode.H) || NavPromptCore.ElementDownByName(FreeCamPad.ToggleNames)) SetPlayCam(!_playCam);
                if (_playCam) FlyPlayCam(mgr.Camera.Controller);
                else SetUiHidden(false);
                return;
            }
            SetPlayCam(false);

            if (state == null || state.TryCast<LevelEditorStateReticle>() == null)
            {
                _tracking = false;
                SetPrompts(false);
                SetUiHidden(false);
                return;
            }

            var c = mgr.Camera.Controller;
            if (!c.enabled)
            {
                _tracking = false;
                return;
            }
            _controller = c;
            c.GetPitchAndYawAngles(out float pitch, out float yaw);
            c.ApplyTransforms();
            var offset = c._camera.transform.position - c.transform.position;
            if (_tracking && (pitch != _lastPitch || yaw != _lastYaw) && !c.IsInLerp && !mgr.GetReticleBase().HasSelectedObject())
            {
                c.transform.position += _lastOffset - offset;
                c.ApplyTransforms();
            }
            _lastOffset = offset;
            _lastPitch = pitch;
            _lastYaw = yaw;
            _tracking = true;

            SetPrompts(ReInput.controllers.GetLastActiveController()?.type != ControllerType.Joystick);

            float dir = (KeybindService.KeyHeld(KeyCode.Y) ? 1f : 0f) - (KeybindService.KeyHeld(KeyCode.T) ? 1f : 0f);
            if (dir != 0f) ChangeFov(dir * FovSpeed * Time.unscaledDeltaTime);
            if (KeybindService.KeyDown(KeyCode.H)) ToggleUi();
            if (_uiHidden && LevelEditorWorldSpaceUIManager.Instance._commandBufferAdded)
                LevelEditorWorldSpaceUIManager.Instance.RemoveCameraBuffer(false);
        }

        void ChangeFov(float delta)
        {
            var cam = _controller._camera;
            cam.fieldOfView = Mathf.Clamp(cam.fieldOfView + delta, MinFov, MaxFov);
        }

        void NudgeFovDown() => ChangeFov(-FovNudge);
        void NudgeFovUp() => ChangeFov(FovNudge);
        void ToggleUi() => SetUiHidden(!_uiHidden);

        void SetPlayCam(bool on)
        {
            if (_playCam == on) return;
            _playCam = on;
            _mouseOrbiting = false;
            FGInputLockService.SetPlaytestCamLock(on);
            SetUiHidden(on);
            var state = LevelEditorManager.Instance?.CurrentState;
            if (state != null && state.TryCast<LevelEditorStatePlay>() != null)
                LevelEditorManager.Instance.Camera.Controller._camera.enabled = on;
            Plugin.Log.LogInfo(on ? "playtest editor cam on, bean's frozen" : "playtest editor cam off");
        }

        void FlyPlayCam(LevelEditorCameraController c)
        {
            float dt = Time.unscaledDeltaTime;
            c.GetPitchAndYawAngles(out float pitch, out float yaw);

            var orbit = FreeCamPad.Look * PadOrbitSpeed * dt;
            if (Input.GetMouseButton(1))
            {
                if (_mouseOrbiting) orbit += (Vector2)(Input.mousePosition - _lastMouse) * MouseOrbitSpeed;
                _lastMouse = Input.mousePosition;
                _mouseOrbiting = true;
            }
            else _mouseOrbiting = false;
            if (orbit != Vector2.zero)
                c.SetPitchAndYawAngle(Mathf.Clamp(pitch - orbit.y, c.m_minElevation, c.m_maxElevation), yaw + orbit.x);

            float zoom = -Input.mouseScrollDelta.y * ScrollZoomStep
                         + ((NavPromptCore.ElementHeldByName(FreeCamPad.ZoomOutNames) ? 1f : 0f)
                            - (NavPromptCore.ElementHeldByName(FreeCamPad.ZoomInNames) ? 1f : 0f)) * PadZoomSpeed * dt;
            float dist = Mathf.Clamp(c.GetDistance() + zoom, c.m_minCameraReticleDist, c.m_maxCameraReticleDist);
            if (zoom != 0f) c.SetDistance(dist);

            var stick = FreeCamPad.Move;
            var dir = new Vector3(stick.x, 0f, stick.y);
            if (KeybindService.KeyHeld(KeyCode.W)) dir.z += 1f;
            if (KeybindService.KeyHeld(KeyCode.S)) dir.z -= 1f;
            if (KeybindService.KeyHeld(KeyCode.D)) dir.x += 1f;
            if (KeybindService.KeyHeld(KeyCode.A)) dir.x -= 1f;
            if (KeybindService.KeyHeld(KeyCode.E) || NavPromptCore.ElementHeldByName(DpadUpNames)) dir.y += 1f;
            if (KeybindService.KeyHeld(KeyCode.Q) || NavPromptCore.ElementHeldByName(DpadDownNames)) dir.y -= 1f;
            if (dir != Vector3.zero)
            {
                var fwd = c._camera.transform.forward;
                fwd.y = 0f;
                fwd.Normalize();
                var move = fwd * dir.z + new Vector3(fwd.z, 0f, -fwd.x) * dir.x + Vector3.up * dir.y;
                bool boost = KeybindService.ShiftHeld() || NavPromptCore.ElementHeldByName(FreeCamPad.BoostNames);
                c.transform.position += Vector3.ClampMagnitude(move, 1f) * dist * MoveSpeedPerDistance * (boost ? BoostMul : 1f) * dt;
            }
            c.ApplyTransforms();

            if (_uiHidden && LevelEditorWorldSpaceUIManager.Instance._commandBufferAdded)
                LevelEditorWorldSpaceUIManager.Instance.RemoveCameraBuffer(false);
        }

        void SetUiHidden(bool hide)
        {
            if (_uiHidden == hide) return;
            _uiHidden = hide;
            var ws = LevelEditorWorldSpaceUIManager.Instance;
            if (ws != null)
            {
                ws.gameObject.SetActive(!hide);
                if (hide) ws.RemoveCameraBuffer(false); else ws.UpdateCameraBuffer();
                LevelEditorOutlineManager.Instance.gameObject.SetActive(!hide);
            }
            if (hide)
            {
                foreach (var lepo in LevelIO.PlaceableObjects)
                {
                    if (lepo == null) continue;
                    foreach (var r in lepo.GetComponentsInChildren<Renderer>())
                    {
                        if (!r.enabled) continue;
                        var t = r.transform;
                        while (t != lepo.transform && !t.name.StartsWith("Gizmo_", StringComparison.Ordinal)) t = t.parent;
                        if (t == lepo.transform) continue;
                        r.enabled = false;
                        _hiddenGizmos.Add(r);
                    }
                    foreach (var cv in lepo.GetComponentsInChildren<Canvas>())
                    {
                        if (!cv.enabled || cv.renderMode != RenderMode.WorldSpace) continue;
                        cv.enabled = false;
                        _hiddenCanvases.Add(cv);
                    }
                }
            }
            else
            {
                foreach (var r in _hiddenGizmos) if (r != null) r.enabled = true;
                foreach (var cv in _hiddenCanvases) if (cv != null) cv.enabled = true;
                _hiddenGizmos.Clear();
                _hiddenCanvases.Clear();
            }
            if (hide)
            {
                foreach (var path in UiRoots)
                {
                    var go = GameObject.Find(path);
                    if (go == null) continue;
                    var cg = go.GetComponent<CanvasGroup>();
                    if (cg == null) cg = go.AddComponent<CanvasGroup>();
                    cg.alpha = 0f;
                    _hidden.Add(cg);
                }
                if (_hidden.Count == 0) Plugin.Log.LogWarning("hide ui: none of the ui roots were there, nothing hidden");
                return;
            }
            foreach (var cg in _hidden)
                if (cg != null) cg.alpha = 1f;
            _hidden.Clear();
        }

        void SetPrompts(bool on)
        {
            if (_promptsUp == on) return;
            _promptsUp = on;
            if (!on)
            {
                NavPromptInjection.Remove(NavPromptInjection.EditorFovDown);
                NavPromptInjection.Remove(NavPromptInjection.EditorFovUp);
                NavPromptInjection.Remove(NavPromptInjection.EditorHideUi);
                return;
            }
            if (_fovDownData == null)
            {
                _fovDownData = NavPromptInjection.BuildData(NavPrompt.Report, LocalizationService.Get("editor_free_camera.fov_down"), "bfg_editor_free_camera_fov_down", -1, -1);
                _fovUpData = NavPromptInjection.BuildData(NavPrompt.Report, LocalizationService.Get("editor_free_camera.fov_up"), "bfg_editor_free_camera_fov_up", -1, -1);
                _hideUiData = NavPromptInjection.BuildData(NavPrompt.Report, LocalizationService.Get("editor_free_camera.hide_ui"), "bfg_editor_free_camera_hide_ui", -1, -1);
            }
            NavPromptInjection.Add(NavPromptInjection.EditorFovDown, NudgeFovDown, _fovDownData, PinFovDownGlyph);
            NavPromptInjection.Add(NavPromptInjection.EditorFovUp, NudgeFovUp, _fovUpData, PinFovUpGlyph);
            NavPromptInjection.Add(NavPromptInjection.EditorHideUi, ToggleUi, _hideUiData, PinHideUiGlyph);
        }

        static void PinFovDownGlyph(NavigationPromptButton btn) => NavPromptCore.ApplyOwnGlyphByElement(btn, KeyCode.T, -1);
        static void PinFovUpGlyph(NavigationPromptButton btn) => NavPromptCore.ApplyOwnGlyphByElement(btn, KeyCode.Y, -1);
        static void PinHideUiGlyph(NavigationPromptButton btn) => NavPromptCore.ApplyOwnGlyphByElement(btn, KeyCode.H, -1);
    }
}
