using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Player;
using BetterFG.Nametag;
using BetterFG.Network;
using BetterFG.Services;
using BetterFG.UI;
using BetterFG.UI.Tabs;
using BetterFG.Utilities;
using Cinemachine;
using FallGuysLib.Camera;
using FallGuysLib.NPC;
using FG.Common;
using FG.Common.Fraggle;
using FGClient;
using MPG.Utility;
using UnityEngine;
using Wushu.Integration;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.Features.Replay
{
    public partial class ReplayViewer
    {
        bool _rightMoved;

        void SetupCamera()
        {
            var existing = CameraUtils.GetMainCamera();
            if (existing != null)
            {
                TakeOverCamera(existing);
                return;
            }

            Plugin.Log.LogWarning("level had no main camera, falling back to a bare one");
            MakeBareCamera(new Vector3(0f, 8f, -18f), Quaternion.identity, 60f);
            _pitch = 12f;
            ApplyPostProcessing();
        }

        void MakeBareCamera(Vector3 pos, Quaternion rot, float fov)
        {
            var camGo = new GameObject("BettrFG_ReplayCam");
            camGo.transform.SetParent(transform, false);
            camGo.transform.SetPositionAndRotation(pos, rot);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = _sceneLoaded ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 4000f;
            _cam.cullingMask = ~0;
            _cam.fieldOfView = fov;
            SeedLook();
        }

        void TakeOverCamera(Camera cam)
        {
            _cam = cam;
            foreach (var brain in _cam.GetComponentsInChildren<CinemachineBrain>(true))
            {
                if (!brain.enabled) continue;
                brain.enabled = false;
                _brains.Add(brain);
            }

            foreach (var child in _cam.GetComponentsInChildren<Camera>(true))
            {
                if (child == _cam || child.orthographic) continue;
                _childCams.Add(child);
                _childFovs.Add(child.fieldOfView);
            }
            _childFov = _cam.fieldOfView;

            var t = _cam.transform;
            _camParent = t.parent;
            _camLocalPos = t.localPosition;
            _camLocalRot = t.localRotation;
            _camFov = _cam.fieldOfView;
            _tookCamera = true;

            t.SetParent(null, true);
            SeedLook();

            var ppl = _cam.GetComponent<PostProcessLayer>();
            if (ppl != null) ppl.volumeLayer |= 1 << ReplayPostFx.VolumeLayer;

            Plugin.Log.LogInfo($"freecam took over {_cam.gameObject.name}, its post-processing comes along");
        }

        void SeedLook()
        {
            var euler = _cam.transform.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
        }

        void PositionCameraOnPlayer()
        {
            if (_rec.players.Count == 0) return;

            ReplayPlayer target = null;
            foreach (var p in _rec.players)
                if (p.isLocal) { target = p; break; }
            if (target == null) target = _rec.players[0];
            if (target.frames.Count == 0) return;

            var frame = target.frames[0];
            var facing = frame.rot * Vector3.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.0001f) facing = Vector3.forward;
            facing.Normalize();

            var focus = frame.pos + Vector3.up * 1.2f;
            var camPos = frame.pos - facing * 6f + Vector3.up * 4f;

            _cam.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(focus - camPos, Vector3.up));
            SeedLook();
        }

        bool FromLoadedLevel(GameObject go)
        {
            var scene = go.scene;
            foreach (var loaded in _loadedScenes)
                if (loaded == scene) return true;
            return false;
        }

        void AdoptLevelCamera()
        {
            if (FromLoadedLevel(_cam.gameObject)) return;

            var level = CameraUtils.GetMainCamera();
            if (level == null || level == _cam || !FromLoadedLevel(level.gameObject))
            {
                ApplyPostProcessing();
                return;
            }

            var pos = _cam.transform.position;
            var rot = _cam.transform.rotation;
            float fov = _cam.fieldOfView;

            if (_tookCamera) RestoreCamera();
            else Destroy(_cam.gameObject);

            TakeOverCamera(level);
            _cam.transform.SetPositionAndRotation(pos, rot);
            _cam.fieldOfView = fov;
            SeedLook();
        }

        void ApplyPostProcessing()
        {
            PostProcessLayer template = null;
            foreach (var layer in Resources.FindObjectsOfTypeAll<PostProcessLayer>())
            {
                if (layer == null || layer.m_Resources == null || layer.gameObject == _cam.gameObject) continue;
                if (template == null) template = layer;
                if (layer.GetComponent<Camera>() == null) continue;
                if (FromLoadedLevel(layer.gameObject)) { template = layer; break; }
                if (layer.gameObject.scene.IsValid()) template = layer;
            }
            if (template == null)
            {
                Plugin.Log.LogWarning("nothing to copy post-processing from, replay cam renders raw");
                return;
            }

            int mask = template.volumeLayer.value;
            if (mask == 0) mask = ~0;
            mask |= 1 << ReplayPostFx.VolumeLayer;

            _cam.allowHDR = true;

            var ppl = _cam.GetComponent<PostProcessLayer>();
            if (ppl == null) ppl = _cam.gameObject.AddComponent<PostProcessLayer>();
            ppl.enabled = false;
            ppl.Init(template.m_Resources);
            ppl.volumeLayer = mask;
            ppl.volumeTrigger = _cam.transform;
            ppl.antialiasingMode = template.antialiasingMode;
            ppl.stopNaNPropagation = template.stopNaNPropagation;
            ppl.enabled = true;

            int volumes = Resources.FindObjectsOfTypeAll<PostProcessVolume>().Length;
            Plugin.Log.LogInfo($"post-processing off {template.gameObject.name} (scene '{template.gameObject.scene.name}'), volumeLayer {mask}, AA {template.antialiasingMode}, {volumes} volumes around");
        }

        void HandleLook()
        {
            if (Input.GetMouseButtonDown(1))
            {
                CloseContextMenu();
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
                _looking = true;
                _rightMoved = false;
                _lastMouse = Input.mousePosition;
                SeedLook();
            }
            if (Input.GetMouseButtonUp(1))
            {
                bool tap = _looking && !_rightMoved;
                _looking = false;
                _rightMoved = false;
#if MACHINIMA
                if (tap && Machinima && _solo == null) ShowWorldMenu();
#endif
            }
            if (!_looking) return;

            Vector3 now = Input.mousePosition;
            Vector3 delta = now - _lastMouse;
            _lastMouse = now;

            if (delta.sqrMagnitude > 0f) { _freeLook = true; _rightMoved = true; }
            _yaw += delta.x * 0.18f;
            _pitch = Mathf.Clamp(_pitch - delta.y * 0.18f, -89f, 89f);
            _cam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        void HandleMove()
        {
            if (TypingInField()) return;

            var dir = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) dir += Vector3.forward;
            if (Input.GetKey(KeyCode.S)) dir -= Vector3.forward;
            if (Input.GetKey(KeyCode.D)) dir += Vector3.right;
            if (Input.GetKey(KeyCode.A)) dir -= Vector3.right;
            if (Input.GetKey(KeyCode.E)) dir += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) dir -= Vector3.up;
            if (_worldHeld)
            {
                var stick = ControllerManager.Stick;
                dir += Vector3.right * stick.x + Vector3.forward * stick.y;
            }
            if (dir == Vector3.zero) return;

            _freeLook = true;
            float mul = ShiftHeld() ? 3.5f : 1f;
            var t = _cam.transform;
            var world = t.rotation * new Vector3(dir.x, 0f, dir.z) + Vector3.up * dir.y;
            t.position += world.normalized * _speed * mul * Mathf.Clamp01(dir.magnitude) * Time.unscaledDeltaTime;
        }

        public void BeginCameraEdit(ReplayKeyframe kf)
        {
            _editKeyframe = kf;
            _editingCam = true;
            _freeLook = false;
            ClearPlayerHighlight();
            SeekTo(kf.time);

            _hiddenUi.Clear();
            for (int i = 0; i < _canvas.transform.childCount; i++)
            {
                var child = _canvas.transform.GetChild(i).gameObject;
                if (child == _doneBtn.gameObject || !child.activeSelf) continue;
                child.SetActive(false);
                _hiddenUi.Add(child);
            }
            _doneBtn.gameObject.SetActive(true);

            _camEditHint = UGUIShip.CreateLabel(_canvas.transform, new Rect(0f, 12f, Screen.width, 26f),
                "ui.you_re_editing_the_camera_keyframe_s_angle_and_p",
                UIScale.FS, Color.white, TextAnchor.MiddleCenter);
        }

        public void BeginTargetPick(ReplayKeyframe kf, bool lookTarget, bool objects, List<int> allowed)
        {
            _pickKeyframe = kf;
            _pickLookTarget = lookTarget;
            StartPick(objects, allowed,
                objects ? "ui.click_the_object_you_want_right_click_cancels" : "ui.click_the_player_you_want_right_click_cancels");
        }

        public void BeginPlayerPick(ReplayVisibilityKeyframe kf)
        {
            _pickVisKeyframe = kf;
            _pickList = kf.onlyPlayers;
            StartPick(false, null, "ui.click_the_player_to_add_right_click_cancels", false);
        }

        public void BeginNamePick(ReplayVisibilityKeyframe kf)
        {
            _pickVisKeyframe = kf;
            _pickList = kf.nameOnlyPlayers;
            StartPick(false, null, "ui.click_the_player_to_add_right_click_cancels", true);
        }

        public void BeginCrownPick(ReplayVisibilityKeyframe kf)
        {
            _pickVisKeyframe = kf;
            _pickList = kf.crownOnlyPlayers;
            StartPick(false, null, "ui.click_the_player_to_add_right_click_cancels", true);
        }

        void StartPick(bool objects, List<int> allowed, string hint, bool visibleOnly = false)
        {
            _pickObjects = objects;
            _pickAllowed = allowed;
            _pickVisibleOnly = visibleOnly;
            _pickHover = -1;
            _pickHoverPlayer = 0;
            _picking = true;
            _pickWasFree = _freeLook;

            _hiddenUi.Clear();
            for (int i = 0; i < _canvas.transform.childCount; i++)
            {
                var child = _canvas.transform.GetChild(i).gameObject;
                if (!child.activeSelf) continue;
                child.SetActive(false);
                _hiddenUi.Add(child);
            }

            _pickHint = UGUIShip.CreateLabel(_canvas.transform, new Rect(0f, 12f, Screen.width, 26f),
                hint, UIScale.FS, Color.white, TextAnchor.MiddleCenter);

            if (!objects && !visibleOnly)
                foreach (var track in _tracks)
                    if (track.bean != null) track.bean.SetActive(true);
        }

        void UpdateTargetPick()
        {
            var mouse = (Vector2)Input.mousePosition;
            float best = 140f * 140f;
            int bestObject = -1;
            uint bestPlayer = 0;

            if (_pickObjects)
            {
                for (int i = 0; i < _pickAllowed.Count; i++)
                {
                    var tf = _rec.worldObjects[_pickAllowed[i]].live;
                    if (tf == null) continue;

                    var screen = _cam.WorldToScreenPoint(tf.position);
                    if (screen.z <= 0f) continue;

                    float d = ((Vector2)screen - mouse).sqrMagnitude;
                    if (d >= best) continue;
                    best = d;
                    bestObject = _pickAllowed[i];
                }
            }
            else
            {
                for (int i = 0; i < _tracks.Count; i++)
                {
                    if (_tracks[i].bean == null) continue;
                    if (_pickVisibleOnly && !_tracks[i].bean.activeSelf) continue;

                    var screen = _cam.WorldToScreenPoint(_tracks[i].bean.transform.position);
                    if (screen.z <= 0f) continue;

                    float d = ((Vector2)screen - mouse).sqrMagnitude;
                    if (d >= best) continue;
                    best = d;
                    bestPlayer = _tracks[i].player.playerId;
                }
            }

            if (bestObject != _pickHover || bestPlayer != _pickHoverPlayer)
            {
                _pickHover = bestObject;
                _pickHoverPlayer = bestPlayer;

                if (_pickObjects && bestObject >= 0) HighlightObject(bestObject);
                else if (!_pickObjects && bestPlayer != 0) HighlightPlayer(bestPlayer);
                else ClearPlayerHighlight();
            }
            else UpdateHighlight();

            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) { EndTargetPick(); return; }
            if (!Input.GetMouseButtonDown(0)) return;

            if (_pickObjects && _pickHover >= 0)
            {
                if (_pickLookTarget) { _pickKeyframe.lookAt = ReplayLookAt.Object; _pickKeyframe.lookAtObject = _pickHover; }
                else
                {
                    var world = KeyframePosition(_pickKeyframe);
                    _pickKeyframe.cameraType = ReplayCameraType.FixedObject;
                    _pickKeyframe.targetObject = _pickHover;
                    _pickKeyframe.position = world - TargetPositionAt(_pickHover, 0, _pickKeyframe.time);
                }
            }
            else if (!_pickObjects && _pickHoverPlayer != 0)
            {
                if (_pickList != null)
                {
                    if (!_pickList.Contains(_pickHoverPlayer)) _pickList.Add(_pickHoverPlayer);
                }
                else if (_pickLookTarget) { _pickKeyframe.lookAt = ReplayLookAt.Player; _pickKeyframe.lookAtPlayerId = _pickHoverPlayer; }
                else
                {
                    var world = KeyframePosition(_pickKeyframe);
                    _pickKeyframe.cameraType = ReplayCameraType.Fixed;
                    _pickKeyframe.targetPlayerId = _pickHoverPlayer;
                    _pickKeyframe.position = world - PlayerPositionAt(_pickHoverPlayer, _pickKeyframe.time);
                }
            }
            EndTargetPick();
        }

        void EndTargetPick()
        {
            _picking = false;
            ClearPlayerHighlight();

            if (_pickHint != null) Destroy(_pickHint.gameObject);
            _pickHint = null;

            foreach (var go in _hiddenUi)
                if (go != null) go.SetActive(true);
            _hiddenUi.Clear();

            var kf = _pickKeyframe;
            var visKf = _pickVisKeyframe;
            _pickKeyframe = null;
            _pickVisKeyframe = null;
            _pickList = null;
            _pickAllowed = null;

            if (kf != null) OpenKeyframeWindow(kf);
            else if (visKf != null) OpenVisibilityKeyframeWindow(visKf);

            _freeLook = _pickWasFree;
            ApplyTime();
            SyncUi();
        }

        void EndCameraEdit()
        {
            _editingCam = false;
            _freeLook = false;
            _doneBtn.gameObject.SetActive(false);

            if (_camEditHint != null) Destroy(_camEditHint.gameObject);
            _camEditHint = null;

            foreach (var go in _hiddenUi)
                if (go != null) go.SetActive(true);
            _hiddenUi.Clear();

            Plugin.Log.LogInfo($"shot set on the {Stamp(_editKeyframe.time)} keyframe");
            _editKeyframe = null;
            ApplyTime();
            SyncUi();
        }

        public void HighlightObject(int index)
        {
            _highlightObject = index;
            _highlightPlayer = 0;
            _highlightOn = true;

            if (_highlight == null) BuildHighlight();
            if (_highlight != null) _highlight.SetActive(true);
            UpdateHighlight();
        }

        public void HighlightPlayer(uint playerId)
        {
            _highlightPlayer = playerId;
            _highlightObject = -1;
            _highlightOn = true;

            if (_highlight == null) BuildHighlight();
            if (_highlight != null) _highlight.SetActive(true);
            UpdateHighlight();
        }

        public void ClearPlayerHighlight()
        {
            _highlightOn = false;
            if (_highlight != null) _highlight.SetActive(false);
        }

        void BuildHighlight()
        {
            var mat = AdditiveMaterial();
            if (mat == null)
            {
                Plugin.Log.LogWarning("nothing to draw the player marker with, skipping it");
                return;
            }

            if (_highlightTex == null)
                _highlightTex = EmbeddedResourceandUnity.LoadTexture("BetterFG.assets.ui.replay.highlight_player.png");

            mat.mainTexture = _highlightTex;
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", _highlightTex);
            if (mat.HasProperty("_Color")) mat.color = Color.white;
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", Color.white);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.renderQueue = 3100;

            _highlight = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _highlight.name = "BettrFG_ReplayPlayerMarker";
            _highlight.transform.SetParent(transform, false);
            _highlight.transform.localScale = new Vector3(HIGHLIGHT_SIZE, HIGHLIGHT_SIZE, 1f);
            Destroy(_highlight.GetComponent<Collider>());

            var renderer = _highlight.GetComponent<Renderer>();
            renderer.material = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        static Material AdditiveMaterial()
        {
            foreach (var name in new[] { "Particles/Additive", "Legacy Shaders/Particles/Additive", "Mobile/Particles/Additive" })
            {
                var shader = Shader.Find(name);
                if (shader == null) continue;
                Plugin.Log.LogInfo($"player marker on {name}");
                return new Material(shader);
            }

            Material best = null;
            int bestScore = 0;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (candidate == null || candidate.shader == null) continue;

                string shaderName = candidate.shader.name;
                if (string.IsNullOrEmpty(shaderName) || shaderName.StartsWith("Hidden/")) continue;
                if (!candidate.HasProperty("_MainTex")) continue;
                if (!candidate.HasProperty("_SrcBlend") || !candidate.HasProperty("_DstBlend")) continue;
                if ((int)candidate.GetFloat("_DstBlend") != (int)UnityEngine.Rendering.BlendMode.One) continue;

                int src = (int)candidate.GetFloat("_SrcBlend");
                if (src != (int)UnityEngine.Rendering.BlendMode.SrcAlpha && src != (int)UnityEngine.Rendering.BlendMode.One) continue;

                string lower = shaderName.ToLowerInvariant();
                int score = 1;
                if (lower.Contains("particle")) score += 4;
                if (lower.Contains("additive")) score += 4;
                if (lower.Contains("unlit")) score += 2;
                if (lower.Contains("vfx") || lower.Contains("glow") || lower.Contains("sprite")) score += 2;

                if (score <= bestScore) continue;
                bestScore = score;
                best = candidate;
                if (bestScore >= 8) break;
            }

            if (best != null)
            {
                Plugin.Log.LogInfo($"player marker borrowing {best.name} ({best.shader.name}) for its additive blend");
                return new Material(best);
            }

            var sprites = Shader.Find("Sprites/Default");
            if (sprites == null) return null;

            Plugin.Log.LogWarning("no additive material anywhere in the build, the player marker falls back to alpha blending");
            return new Material(sprites);
        }

        void UpdateCameraGizmo()
        {
            if (!_freeLook || _exporting || _editingCam || !PreviewPose(out var pos, out var rot, out float fov))
            {
                ShowGizmo(false);
                return;
            }

            float half = (fov > 0f ? fov : _cam.fieldOfView) * 0.5f * Mathf.Deg2Rad;
            var reach = rot * Vector3.forward * Mathf.Cos(half) * GIZMO_REACH;
            var spread = rot * Vector3.right * Mathf.Sin(half) * GIZMO_REACH;

            if (!CanvasPoint(pos, out var origin))
            {
                ShowGizmo(false);
                return;
            }

            if (_gizmoRoot == null) BuildGizmo();
            ShowGizmo(true);

            _gizmoIcon.anchoredPosition = new Vector2(origin.x, -origin.y);

            if (CanvasPoint(pos - spread + reach, out var left)) StretchLine(_gizmoLineL, origin, left);
            else _gizmoLineL.gameObject.SetActive(false);

            if (CanvasPoint(pos + spread + reach, out var right)) StretchLine(_gizmoLineR, origin, right);
            else _gizmoLineR.gameObject.SetActive(false);
        }

        bool CanvasPoint(Vector3 world, out Vector2 point)
        {
            var screen = _cam.WorldToScreenPoint(world);
            float sf = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            point = new Vector2(screen.x / sf, (Screen.height - screen.y) / sf);
            return screen.z > 0f;
        }

        static void StretchLine(RectTransform line, Vector2 from, Vector2 to)
        {
            if (!line.gameObject.activeSelf) line.gameObject.SetActive(true);

            var delta = to - from;
            line.anchoredPosition = new Vector2(from.x, -from.y);
            line.sizeDelta = new Vector2(delta.magnitude, GIZMO_LINE_W);
            line.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(-delta.y, delta.x) * Mathf.Rad2Deg);
        }

        void ShowGizmo(bool show)
        {
            if (_gizmoRoot == null || _gizmoRoot.gameObject.activeSelf == show) return;
            _gizmoRoot.gameObject.SetActive(show);
        }

        void BuildGizmo()
        {
            var rootGo = new GameObject("CameraGizmo");
            _gizmoRoot = rootGo.AddComponent<RectTransform>();
            _gizmoRoot.SetParent(_canvas.transform, false);
            _gizmoRoot.anchorMin = _gizmoRoot.anchorMax = new Vector2(0f, 1f);
            _gizmoRoot.pivot = new Vector2(0f, 1f);
            _gizmoRoot.anchoredPosition = Vector2.zero;
            _gizmoRoot.sizeDelta = Vector2.zero;
            _gizmoRoot.SetAsFirstSibling();

            _gizmoLineL = BuildGizmoLine();
            _gizmoLineR = BuildGizmoLine();

            if (_gizmoSprite == null)
                _gizmoSprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.camera_gizmo.png");

            var iconGo = new GameObject("Icon");
            _gizmoIcon = iconGo.AddComponent<RectTransform>();
            _gizmoIcon.SetParent(_gizmoRoot, false);
            _gizmoIcon.anchorMin = _gizmoIcon.anchorMax = new Vector2(0f, 1f);
            _gizmoIcon.pivot = new Vector2(0.5f, 0.5f);
            _gizmoIcon.sizeDelta = new Vector2(GIZMO_ICON, GIZMO_ICON);

            var img = iconGo.AddComponent<Image>();
            img.sprite = _gizmoSprite;
            img.color = GIZMO_TINT;
            img.preserveAspect = true;
            img.raycastTarget = false;
        }

        void UpdateHighlight()
        {
            if (!_highlightOn || _highlight == null) return;

            var pos = TargetPositionAt(_highlightObject, _highlightPlayer, _time) + Vector3.up * HIGHLIGHT_HEIGHT;
            _highlight.transform.position = pos;
            _highlight.transform.rotation = Quaternion.LookRotation(pos - _cam.transform.position, Vector3.up);
        }

        public Vector3 TargetPositionAt(int objectIndex, uint playerId, float time)
        {
            if (objectIndex >= 0 && objectIndex < _rec.worldObjects.Count)
            {
                var tf = _rec.worldObjects[objectIndex].live;
                if (tf != null) return tf.position;
            }
            return PlayerPositionAt(playerId, time);
        }

        public Vector3 PlayerPositionAt(uint playerId, float time)
        {
            for (int i = 0; i < _tracks.Count; i++)
            {
                if (_tracks[i].player.playerId != playerId) continue;
                if (_tracks[i].bean != null) return _tracks[i].bean.transform.position;
                SampleFrames(_tracks[i].player.frames, time, out var p, out _);
                return p;
            }
            return Vector3.zero;
        }

        public Vector3 KeyframePosition(ReplayKeyframe k)
        {
            if (k.cameraType == ReplayCameraType.Gameplay)
            {
                SampleFrames(_rec.cameraFrames, _time, out var camPos, out _);
                return camPos;
            }
            if (k.cameraType == ReplayCameraType.Fixed || k.cameraType == ReplayCameraType.FixedObject)
                return TargetPositionAt(k.targetObject, k.targetPlayerId, _time) + k.position;
            return k.position;
        }

        bool KeyframeSpan(out ReplayKeyframe a, out ReplayKeyframe b, out float raw)
        {
            a = null;
            b = null;
            raw = 0f;

            var keys = _rec.keyframes;
            if (keys.Count == 0) return false;

            int i = 0;
            while (i + 1 < keys.Count && keys[i + 1].time <= _time) i++;

            a = keys[i];
            b = i + 1 < keys.Count ? keys[i + 1] : null;
            if (b == null || b.cut) return true;

            float den = b.time - a.time;
            raw = den > 0f ? Mathf.Clamp01((_time - a.time) / den) : 0f;
            return true;
        }

        float PlaybackSpeed()
        {
            if (!KeyframeSpan(out var a, out var b, out float raw)) return 1f;
            if (b == null || b.cutToNext) return a.speed;
            return Mathf.Lerp(a.speed, b.speed, raw);
        }

        bool PreviewPose(out Vector3 pos, out Quaternion rot, out float fov)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;
            fov = 0f;

            if (!KeyframeSpan(out var a, out var b, out float raw)) return false;
            float f = ReplayEase.Apply(a.easingCurve, a.easingDirection, raw);

            pos = KeyframePosition(a);
            if (b != null) pos = Vector3.Lerp(pos, KeyframePosition(b), f);

            rot = KeyframeRotation(a, pos);
            if (b != null) rot = Quaternion.Slerp(rot, KeyframeRotation(b, pos), f);

            if (a.fov > 0f) fov = b != null && b.fov > 0f ? Mathf.Lerp(a.fov, b.fov, f) : a.fov;
            return true;
        }

        void EvaluateKeyframeCamera()
        {
            if (!PreviewPose(out var pos, out var rot, out float fov)) return;

            if (!_editingCam && KeyframeSpan(out var a, out var b, out float raw))
            {
                ReplayShake.Evaluate(a, b, raw, _time, _shakeTime, out var shakeRot, out float shakeFov);
                if (shakeRot != Vector3.zero) rot *= Quaternion.Euler(shakeRot);
                if (fov > 0f) fov += shakeFov;
            }

            _cam.transform.position = pos;
            _cam.transform.rotation = rot;
            if (fov > 0f) _cam.fieldOfView = fov;
        }

        void HandleFov()
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) < 0.01f) return;
            if (!ShiftHeld()) return;

            _cam.fieldOfView = Mathf.Clamp(_cam.fieldOfView - scroll * 2.5f, FOV_MIN, FOV_MAX);
            _freeLook = true;
            SyncChildCameraFov();
        }

        void SyncChildCameraFov()
        {
            if (_childCams.Count == 0 || Mathf.Approximately(_cam.fieldOfView, _childFov)) return;
            _childFov = _cam.fieldOfView;
            for (int i = 0; i < _childCams.Count; i++)
                if (_childCams[i] != null) _childCams[i].fieldOfView = _childFov;
        }

    }
}
