using System;
using System.Collections.Generic;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.UI;
using BetterFG.UI.SideWheel;
using FG.Common;
using FGClient;
using FGClient.UI.Core;
using Rewired;
using UnityEngine;
using FallGuysLib.Camera;
using BetterFG.Utilities;
using Events;
using MPG.Utility;

namespace BetterFG.Tweaks
{
    public enum SpectateCamMode { Cinematic, FreeCam }

    public class CinematicSpectatorTweak : BfgTweak
    {
        public CinematicSpectatorTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "cinematic_spectator";
        public override string TweakLabel => "tweak.custom_spectating";
        public override bool DefaultEnabled => true;
        public override string TweakTooltip => "ui.take_the_camera_over_while_spectating_cinematic";

        public static CinematicSpectatorTweak Instance { get; private set; }
        void Awake() => Instance = this;

        public static bool IsFreeCamActive =>
            Instance != null && Instance._customOn && Instance.Mode == SpectateCamMode.FreeCam;

        public static Transform ActiveFreeCamTransform =>
            IsFreeCamActive && Instance._cam != null ? Instance._cam.transform : null;

        const float FarDistance = 45f;
        const float MinShotDuration = 7f;
        const float FrameHeight = 15f;
        const float MinFov = 15f;
        const float MaxFov = 60f;
        const float EyeHeight = 0.8f;
        const float MinDriftSpeed = 0.25f;
        const float MaxDriftSpeed = 0.7f;

        const float FreeCamSpeed = 16f;
        const float FreeCamBoostMul = 3f;
        const float MouseLookSpeed = 0.18f;
        const float MinZoomFov = 10f;
        const float MaxZoomFov = 100f;
        const float PromptFadeDuration = 5f;

        const string NextRoundButtonPath =
            "UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates/SpectatorState/GameplaySpectatorViewModel/SafeArea/GameplaySpectatorUltimatePartyFlow/BottomCenterContainer/NextRoundButton";
        const string PromptKeyPrefix = "bfg_custom_spectator";
        static readonly string[] ToggleNames = { "X", "Square" };
        static readonly string[] DpadUpNames = { "D-Pad Up", "DPad Up", "Hat Up" };
        static readonly string[] DpadDownNames = { "D-Pad Down", "DPad Down", "Hat Down" };

        string ModeKey => $"tweak.{TweakId}.mode";
        SpectateCamMode Mode
        {
            get => SettingsService.Get(ModeKey, "1") == "0" ? SpectateCamMode.Cinematic : SpectateCamMode.FreeCam;
            set => SettingsService.Set(ModeKey, value == SpectateCamMode.FreeCam ? "1" : "0");
        }

        bool _promptInjected;
        bool _promptShowsOn;
        readonly List<GameObject> _hiddenPrompts = new List<GameObject>();
        CanvasGroup _fadedPrompt;
        float _engagedAt;
        bool _customOn;
        bool _outThisRound;
        EventSystem.Handle _spectatorHandle;
        CameraDirector _director;
        Cinemachine.CinemachineBrain _brain;
        Camera _cam;
        Transform _lastTarget;
        Vector3 _lockedPos;
        Vector3 _drift;
        float _lastAngle;
        float _lastCutTime;
        float _originalFov;
        float _freeYaw;
        float _freePitch;
        Vector3 _lastMouse;
        bool _mouseLooking;
        CanvasGroup _defaultUiGroup;
        CanvasGroup _overlayUiGroup;
        GameObject _nextRoundBtn;

        public override void DisableTweak() => Shutdown();

        public override void EnableTweak()
        {
            enabled = false;
            if (_spectatorHandle == null)
                _spectatorHandle = Broadcaster.Instance.Register<CommonEvents.ClientGameManagerSpectatorModeChanged>(
                    new Action<CommonEvents.ClientGameManagerSpectatorModeChanged>(OnSpectatorModeChanged));
            ClientGameManager cgm = null;
            GlobalGameStateClient.Instance?.GameStateView?.GetLiveClientGameManager(out cgm);
            if (cgm != null) SpectatorChanged(cgm.IsSpectatorMode);
        }

        public static void OnClientGameManagerShutdown()
        {
            if (Instance == null) return;
            Instance._outThisRound = false;
            Instance.Shutdown();
        }

        public static void OnPlayerProgress(ClientGameManager cgm, GameMessageServerPlayerProgress msg)
        {
            if (Instance != null && !msg.succeeded && cgm.IsMyLocalPlayer(msg.playerId)) Instance._outThisRound = true;
        }

        void OnSpectatorModeChanged(CommonEvents.ClientGameManagerSpectatorModeChanged e)
        {
            if (IsEnabled) SpectatorChanged(e.IsSpectatorMode);
        }

        void SpectatorChanged(bool spectating)
        {
            if (!spectating || !_outThisRound) { Shutdown(); return; }
            if (enabled) return;
            ClearCaches();
            enabled = true;
            Plugin.Log.LogInfo("out this round and spectating, custom cam is up for grabs");
        }

        public override void OnRoundStart() => Shutdown();

        public override void OnStateChanged(GameStateMachine.IGameState newState)
        {
            if (newState == null || newState.TryCast<StateGameInProgress>() == null) Shutdown();
        }

        public override void OnMainMenuEntered() => Shutdown();

        void Update()
        {
            if (!_outThisRound) { enabled = false; return; }
            EnsurePrompt();
            if (BfgBinds.CustomCam.Down || NavPromptCore.ElementDownByName(ToggleNames)) Toggle();
        }

        void LateUpdate()
        {
            if (!_customOn || _cam == null) return;

            if (_brain != null && _brain.enabled) _brain.enabled = false;

            SuppressGameUi();

            if (Mode == SpectateCamMode.FreeCam) UpdateFreeCam();
            else UpdateCinematic();
        }

        void UpdateCinematic()
        {
            var target = CurrentTargetTransform();
            if (target == null) return;

            _lockedPos += _drift * Time.deltaTime;

            var look = target.position + Vector3.up * EyeHeight;
            bool switched = target != _lastTarget;
            bool ranOff = Vector3.Distance(_lockedPos, look) > FarDistance
                          && Time.time - _lastCutTime > MinShotDuration;
            if (switched || ranOff)
            {
                Reposition(target);
                look = target.position + Vector3.up * EyeHeight;
            }

            var dir = look - _lockedPos;
            if (dir.sqrMagnitude < 0.0001f) return;

            var t = _cam.transform;
            t.position = _lockedPos;
            t.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            _cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(FrameHeight * 0.5f / dir.magnitude) * Mathf.Rad2Deg, MinFov, MaxFov);
        }

        // mouse+keyboard through KeybindService (Rewired-backed, doesn't go deaf like legacy Input),
        // plus a raw joystick poll for pad players: left stick moves, right stick looks, LB/RB go
        // down/up, A boosts. bypasses the game's action maps entirely so it works whether or not
        // FGInputLockService has the player's controller maps locked.
        void UpdateFreeCam()
        {
            // don't fly the camera (or spin it) while something's actually stealing input focus —
            // the game's own pause/options menu, a popup, or BettrFG's own UI/side wheel being open.
            // NavPromptHandle.ComputeFocused() is the same "is gameplay actually focused" check the
            // nav prompts already gate presses on.
            bool bettrFgUiOpen = (BetterFGUIMan.Instance != null && BetterFGUIMan.Instance.IsVisible)
                                || (SideWheelManager.Instance != null && SideWheelManager.Instance.IsWheelVisible);
            if (bettrFgUiOpen || !NavPromptHandle.ComputeFocused()) return;

            // unlocked (not confined/centered) so it doesn't fight BettrFG's own cursor handling,
            // just hidden — reasserted every frame since opening BettrFG's UI flips visible back on.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = false;

            var t = _cam.transform;

            if (Input.GetMouseButton(1))
            {
                if (_mouseLooking)
                {
                    Vector3 delta = (Vector3)Input.mousePosition - _lastMouse;
                    _freeYaw += delta.x * MouseLookSpeed;
                    _freePitch = Mathf.Clamp(_freePitch - delta.y * MouseLookSpeed, -89f, 89f);
                }
                _lastMouse = Input.mousePosition;
                _mouseLooking = true;
            }
            else _mouseLooking = false;

            t.rotation = Quaternion.Euler(_freePitch, _freeYaw, 0f);
            FreeCamPad.Fly(_cam, ref _freeYaw, ref _freePitch, FreeCamSpeed, FreeCamBoostMul, MinZoomFov, MaxZoomFov);

            float scroll = Input.mouseScrollDelta.y;
            if (scroll != 0f) _cam.fieldOfView = Mathf.Clamp(_cam.fieldOfView - scroll * 2.5f, MinZoomFov, MaxZoomFov);

            var dir = Vector3.zero;
            if (NavPromptCore.ElementHeldByName(DpadUpNames)) dir += Vector3.up;
            if (NavPromptCore.ElementHeldByName(DpadDownNames)) dir -= Vector3.up;
            if (KeybindService.KeyHeld(KeyCode.W)) dir += Vector3.forward;
            if (KeybindService.KeyHeld(KeyCode.S)) dir -= Vector3.forward;
            if (KeybindService.KeyHeld(KeyCode.D)) dir += Vector3.right;
            if (KeybindService.KeyHeld(KeyCode.A)) dir -= Vector3.right;
            if (KeybindService.KeyHeld(KeyCode.E)) dir += Vector3.up;
            if (KeybindService.KeyHeld(KeyCode.Q)) dir -= Vector3.up;
            if (dir == Vector3.zero) return;

            float mul = KeybindService.ShiftHeld() ? FreeCamBoostMul : 1f;
            var world = t.rotation * new Vector3(dir.x, 0f, dir.z) + Vector3.up * dir.y;
            t.position += world.normalized * FreeCamSpeed * mul * Mathf.Clamp01(dir.magnitude) * Time.unscaledDeltaTime;
        }

        // hides the game's own HUD/UI via CanvasGroup rather than SetActive — that keeps whatever's
        // driving spectator/round state underneath still ticking, just not drawn or click-through.
        // reasserted every frame we're in free-cam since screens can flip alpha back on their own.
        // the Explore "next round" button is a straight SetActive(false) instead: its UIButtonSolo
        // reads a bound Rewired action directly in its own Update, so a CanvasGroup alone doesn't
        // stop it firing — only killing the GameObject does.
        void SuppressGameUi()
        {
            var defaultGo = GameObject.Find("UICanvas_Client_V2(Clone)/Default");
            if (defaultGo != null)
            {
                if (_defaultUiGroup == null) _defaultUiGroup = defaultGo.GetComponent<CanvasGroup>() ?? defaultGo.AddComponent<CanvasGroup>();
                _defaultUiGroup.alpha = 0f;
                _defaultUiGroup.interactable = false;
                _defaultUiGroup.blocksRaycasts = false;
            }

            var overlayGo = GameObject.Find("UICanvas_Client_V2(Clone)/Overlay");
            if (overlayGo != null)
            {
                if (_overlayUiGroup == null) _overlayUiGroup = overlayGo.GetComponent<CanvasGroup>() ?? overlayGo.AddComponent<CanvasGroup>();
                _overlayUiGroup.alpha = 0f;
                _overlayUiGroup.interactable = false;
                _overlayUiGroup.blocksRaycasts = false;
            }

            if (_nextRoundBtn == null) _nextRoundBtn = GameObject.Find(NextRoundButtonPath);
            if (_nextRoundBtn != null && _nextRoundBtn.activeSelf) _nextRoundBtn.SetActive(false);

            var row = NavPromptCore.Manager?._navPromptsParent;
            if (row == null) return;
            for (int i = 0; i < row.childCount; i++)
            {
                var child = row.GetChild(i).gameObject;
                if (!child.activeSelf) continue;
                var label = child.GetComponent<NavigationPromptButton>()?._localisedStaticLabel;
                if (label != null && label.Key != null && label.Key.StartsWith(PromptKeyPrefix))
                {
                    var group = child.GetComponent<CanvasGroup>() ?? child.AddComponent<CanvasGroup>();
                    if (_fadedPrompt != null && _fadedPrompt != group) _fadedPrompt.alpha = 1f;
                    _fadedPrompt = group;
                    group.alpha = Mathf.Clamp01(1f - (Time.unscaledTime - _engagedAt) / PromptFadeDuration);
                    continue;
                }
                child.SetActive(false);
                _hiddenPrompts.Add(child);
            }
        }

        void RestoreGameUi()
        {
            if (_defaultUiGroup != null) { _defaultUiGroup.alpha = 1f; _defaultUiGroup.interactable = true; _defaultUiGroup.blocksRaycasts = true; }
            if (_overlayUiGroup != null) { _overlayUiGroup.alpha = 1f; _overlayUiGroup.interactable = true; _overlayUiGroup.blocksRaycasts = true; }
            if (_nextRoundBtn != null && !_nextRoundBtn.activeSelf) _nextRoundBtn.SetActive(true);
            if (_fadedPrompt != null) _fadedPrompt.alpha = 1f;
            _fadedPrompt = null;
            foreach (var go in _hiddenPrompts)
                if (go != null) go.SetActive(true);
            _hiddenPrompts.Clear();
        }

        Transform CurrentTargetTransform()
        {
            if (_director == null) _director = CameraLocator.GetCameraDirector();
            if (_director == null) return null;
            var selected = _director._selectedPlayerSpectatorCameraTarget;
            if (selected != null && selected.Transform != null) return selected.Transform;
            var pc = _director.SpectatedPlayerController;
            return pc != null ? pc.transform : null;
        }

        void Toggle() => SetActive(!_customOn);

        void SetActive(bool on)
        {
            if (!on) { Disengage(); return; }

            if (_director == null) _director = CameraLocator.GetCameraDirector();
            if (_director == null) return;

            _cam = _director.MainNativeCam;
            _brain = _director.Brain;
            if (_cam == null) { Plugin.Log.LogWarning("custom spectate cam: no native camera to drive"); return; }

            var mode = Mode;
            if (mode == SpectateCamMode.Cinematic)
            {
                var target = CurrentTargetTransform();
                if (target == null)
                {
                    Plugin.Log.LogWarning("cinematic cam: nobody selected to spectate, not engaging");
                    _cam = null;
                    _brain = null;
                    return;
                }
                _lastAngle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                Reposition(target);
            }
            else
            {
                var euler = _cam.transform.eulerAngles;
                _freeYaw = euler.y;
                _freePitch = euler.x > 180f ? euler.x - 360f : euler.x;
                _mouseLooking = false;
            }

            _originalFov = _cam.fieldOfView;
            if (_brain != null) _brain.enabled = false;
            _customOn = true;
            _engagedAt = Time.unscaledTime;
            Plugin.Log.LogInfo(mode == SpectateCamMode.FreeCam
                ? "free-cam on, you've got the wheel"
                : $"cinematic cam on, watching {_director._selectedPlayerSpectatorCameraTarget?.Name}");
        }

        void Disengage()
        {
            if (!_customOn) return;
            bool wasFree = Mode == SpectateCamMode.FreeCam;
            _customOn = false;
            if (_cam != null) _cam.fieldOfView = _originalFov;
            if (_brain != null) _brain.enabled = true;
            RestoreGameUi();
            if (wasFree)
            {
                Cursor.visible = true;
                RestoreNametagFadeTarget();
            }
            _lastTarget = null;
            _cam = null;
            _brain = null;
            Plugin.Log.LogInfo("custom spectate cam off, cinemachine has the camera back");
        }

        void RestoreNametagFadeTarget()
        {
            var pc = _director != null ? _director.SpectatedPlayerController : null;
            if (pc == null) return;
            var t = pc.transform;
            var huds = UnityEngine.Object.FindObjectsOfType<PlayerInfoHUDBase>(true);
            if (huds == null) return;
            for (int i = 0; i < huds.Length; i++)
                if (huds[i] != null) huds[i]._currentTargetTransform = t;
        }

        void Reposition(Transform target)
        {
            _lastTarget = target;
            _lastCutTime = Time.time;
            var look = target.position + Vector3.up * EyeHeight;

            float height = UnityEngine.Random.Range(4f, 9f);
            Vector3 chosen = Vector3.zero;
            for (int i = 0; i < 6; i++)
            {
                _lastAngle += UnityEngine.Random.Range(Mathf.PI * 0.55f, Mathf.PI * 1.45f);
                float radius = UnityEngine.Random.Range(14f, 22f);
                chosen = look + new Vector3(Mathf.Sin(_lastAngle) * radius, height, Mathf.Cos(_lastAngle) * radius);
                if (!Physics.Linecast(look, chosen)) break;
            }
            _lockedPos = chosen;

            float driftAngle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            _drift = new Vector3(Mathf.Sin(driftAngle), 0f, Mathf.Cos(driftAngle))
                     * UnityEngine.Random.Range(MinDriftSpeed, MaxDriftSpeed);
        }

        void EnsurePrompt()
        {
            if (_promptInjected && _promptShowsOn == _customOn) return;
            _promptInjected = true;
            _promptShowsOn = _customOn;

            string loc = !_customOn ? "spectate_cam.prompt_off"
                : Mode == SpectateCamMode.FreeCam ? "spectate_cam.prompt_free_on" : "spectate_cam.prompt_cine_on";
            string key = !_customOn ? PromptKeyPrefix + "_off"
                : Mode == SpectateCamMode.FreeCam ? PromptKeyPrefix + "_free_on" : PromptKeyPrefix + "_cine_on";

            var data = NavPromptInjection.BuildData(NavPromptCore.Favourite, LocalizationService.Get(loc), key,
                BfgBinds.CustomCam.ActionId, RewiredConsts.Category.Default);
            NavPromptInjection.Add(NavPromptInjection.CustomCam, Toggle, data, PinStickGlyph);
        }

        static void PinStickGlyph(NavigationPromptButton btn)
        {
            var kb = ReInput.isReady && ReInput.players.playerCount > 0
                ? ReInput.players.GetPlayer(0).controllers.maps.GetFirstButtonMapWithAction(ControllerType.Keyboard, BfgBinds.CustomCam.ActionId, true)
                : null;
            NavPromptCore.ApplyOwnGlyphByElement(btn, kb != null ? kb.keyCode : KeyCode.B, NavPromptCore.CurrentPadElementByName(ToggleNames));
        }

        void Shutdown()
        {
            Disengage();
            if (_promptInjected) NavPromptInjection.Remove(NavPromptInjection.CustomCam);
            _promptInjected = false;
            ClearCaches();
            enabled = false;
        }

        void ClearCaches()
        {
            _director = null;
            _lastTarget = null;
            _defaultUiGroup = null;
            _overlayUiGroup = null;
            _nextRoundBtn = null;
        }

        public override List<TweakSetting> GetSettings() => new List<TweakSetting>
        {
            new TweakSetting
            {
                Label = "ui.mode",
                Options = new[] { "ui.cinematic", "ui.free_cam" },
                Selected = () => (int)Mode,
                OnPick = i => Mode = (SpectateCamMode)i
            }
        };
    }
}
