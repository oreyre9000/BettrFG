using System;
using System.Collections.Generic;
using BetterFG.Core;
using BetterFG.Features.QualificationTime;
using BetterFG.Services;
using BetterFG.UI;
using BetterFG.UI.SideWheel;
using FG.Common;
using FGClient;
using Rewired;
using UnityEngine;
using FallGuysLib.Camera;

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
        const float StickLookSpeed = 110f;
        const float StickDeadzone = 0.2f;
        const float PromptFadeDuration = 5f;

        const string NextRoundButtonPath =
            "UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates/SpectatorState/GameplaySpectatorViewModel/SafeArea/GameplaySpectatorUltimatePartyFlow/BottomCenterContainer/NextRoundButton";
        const string SubMenuNavRightPath = "Prefab_UI_NavigationOverlay(Clone)/SafeArea/SubMenuNavigation_Right";

        string ModeKey => $"tweak.{TweakId}.mode";
        SpectateCamMode Mode
        {
            get => SettingsService.Get(ModeKey, "1") == "0" ? SpectateCamMode.Cinematic : SpectateCamMode.FreeCam;
            set => SettingsService.Set(ModeKey, value == SpectateCamMode.FreeCam ? "1" : "0");
        }

        NavPromptHandle _prompt;
        bool _promptShowsOn;
        CanvasGroup _promptGroup;
        float _promptShownAt;
        bool _customOn;
        bool _sessionOver;
        ClientGameManager _cgm;
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
        GameObject _subMenuNavRight;

        public override void DisableTweak() => Shutdown();

        public override void EnableTweak() => OnSpectatorMode();

        public static void OnClientGameManagerShutdown()
        {
            if (Instance == null) return;
            Instance._sessionOver = true;
            Instance.Shutdown();
        }

        // SwitchToSpectatorMode is the moment the manager goes live, so take it here instead of
        // re-asking GlobalGameStateClient on a timer for the rest of the round.
        public override void OnSpectatorMode()
        {
            _sessionOver = false;
            _cgm = null;
            ClearCaches();
            GlobalGameStateClient.Instance?.GameStateView?.GetLiveClientGameManager(out _cgm);
        }

        public override void OnRoundStart()
        {
            _sessionOver = false;
            _cgm = null;
            ShutdownOnce();
        }

        public override void OnStateChanged(GameStateMachine.IGameState newState)
        {
            if (newState == null || newState.TryCast<StateGameInProgress>() == null) Shutdown();
        }

        public override void OnMainMenuEntered()
        {
            _sessionOver = true;
            Shutdown();
        }

        // the manager is taken once from SwitchToSpectatorMode, so a null one here means we simply
        // aren't spectating and the frame costs a field read. teardown only runs on the edge.
        bool _torndown = true;

        void Update()
        {
            if (_sessionOver || _cgm == null) { ShutdownOnce(); return; }
            if (!_cgm.IsSpectatorMode) { ShutdownOnce(); return; }
            if (FeatureQualificationTime.QualPopupOpen) { ShutdownOnce(); return; }

            _torndown = false;
            EnsurePrompt();
            UpdatePromptFade();
            if (_prompt != null && _prompt.IsPressed()) SetActive(!_customOn);
        }

        void ShutdownOnce()
        {
            if (_torndown) return;
            _torndown = true;
            Shutdown();
        }

        void LateUpdate()
        {
            if (!_customOn || _cam == null) return;

            if (_brain != null && _brain.enabled) _brain.enabled = false;

            SuppressGameUi();

            if (Mode == SpectateCamMode.FreeCam) UpdateFreeCam();
            else UpdateCinematic();

            // reasserted here too (Update() already calls it) — while we're engaged the prompt's own
            // prefab logic (button highlight/appear tweens) can write its CanvasGroup back to full
            // alpha on its own Update, and LateUpdate always runs after every script's Update this
            // frame, so this is the one that actually sticks.
            UpdatePromptFade();
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

            ReadPad(out var padMove, out var padLook, out bool padBoost, out float padVertical);

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

            if (padLook != Vector2.zero)
            {
                _freeYaw += padLook.x * StickLookSpeed * Time.unscaledDeltaTime;
                _freePitch = Mathf.Clamp(_freePitch - padLook.y * StickLookSpeed * Time.unscaledDeltaTime, -89f, 89f);
            }

            t.rotation = Quaternion.Euler(_freePitch, _freeYaw, 0f);

            var dir = Vector3.zero;
            if (KeybindService.KeyHeld(KeyCode.W)) dir += Vector3.forward;
            if (KeybindService.KeyHeld(KeyCode.S)) dir -= Vector3.forward;
            if (KeybindService.KeyHeld(KeyCode.D)) dir += Vector3.right;
            if (KeybindService.KeyHeld(KeyCode.A)) dir -= Vector3.right;
            if (KeybindService.KeyHeld(KeyCode.E)) dir += Vector3.up;
            if (KeybindService.KeyHeld(KeyCode.Q)) dir -= Vector3.up;
            dir += Vector3.forward * padMove.y + Vector3.right * padMove.x + Vector3.up * padVertical;
            if (dir == Vector3.zero) return;

            float mul = (KeybindService.ShiftHeld() || padBoost) ? FreeCamBoostMul : 1f;
            var world = t.rotation * new Vector3(dir.x, 0f, dir.z) + Vector3.up * dir.y;
            t.position += world.normalized * FreeCamSpeed * mul * Mathf.Clamp01(dir.magnitude) * Time.unscaledDeltaTime;
        }

        // axes 0/1 = left stick (move), axes 2/3 = right stick (look) — same convention as
        // ControllerManager. button indices per ControllerBindService: 0=A/cross, 4=LB, 5=RB.
        void ReadPad(out Vector2 move, out Vector2 look, out bool boost, out float vertical)
        {
            move = Vector2.zero;
            look = Vector2.zero;
            boost = false;
            vertical = 0f;
            if (!ReInput.isReady || ReInput.players.playerCount == 0) return;

            var p = ReInput.players.GetPlayer(0);
            if (p == null) return;

            float mx = 0f, my = 0f, lx = 0f, ly = 0f;
            bool a = false, lb = false, rb = false;
            var sticks = p.controllers.Joysticks;
            int n = p.controllers.joystickCount;
            for (int i = 0; i < n; i++)
            {
                var j = sticks[i];
                if (j == null) continue;
                if (j.axisCount >= 2) { mx += j.GetAxis(0); my += j.GetAxis(1); }
                if (j.axisCount >= 4) { lx += j.GetAxis(2); ly += j.GetAxis(3); }
                if (j.buttonCount > 0) a |= j.GetButton(0);
                if (j.buttonCount > 4) lb |= j.GetButton(4);
                if (j.buttonCount > 5) rb |= j.GetButton(5);
            }

            move = new Vector2(Mathf.Abs(mx) > StickDeadzone ? mx : 0f, Mathf.Abs(my) > StickDeadzone ? my : 0f);
            look = new Vector2(Mathf.Abs(lx) > StickDeadzone ? lx : 0f, Mathf.Abs(ly) > StickDeadzone ? ly : 0f);
            boost = a;
            vertical = (rb ? 1f : 0f) - (lb ? 1f : 0f);
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

            if (_subMenuNavRight == null) _subMenuNavRight = GameObject.Find(SubMenuNavRightPath);
            if (_subMenuNavRight != null && _subMenuNavRight.activeSelf) _subMenuNavRight.SetActive(false);
        }

        void RestoreGameUi()
        {
            if (_defaultUiGroup != null) { _defaultUiGroup.alpha = 1f; _defaultUiGroup.interactable = true; _defaultUiGroup.blocksRaycasts = true; }
            if (_overlayUiGroup != null) { _overlayUiGroup.alpha = 1f; _overlayUiGroup.interactable = true; _overlayUiGroup.blocksRaycasts = true; }
            if (_nextRoundBtn != null && !_nextRoundBtn.activeSelf) _nextRoundBtn.SetActive(true);
            if (_subMenuNavRight != null && !_subMenuNavRight.activeSelf) _subMenuNavRight.SetActive(true);
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
            if (_prompt != null && _prompt.IsAlive && _promptShowsOn == _customOn) return;
            _prompt?.Destroy();
            _promptShowsOn = _customOn;

            string label = !_customOn ? "Custom Cam: Off"
                : Mode == SpectateCamMode.FreeCam ? "Free-cam: On" : "Cinematic Cam: On";
            string key = !_customOn ? "bfg_custom_spectator_off"
                : Mode == SpectateCamMode.FreeCam ? "bfg_custom_spectator_free_on" : "bfg_custom_spectator_cine_on";

            _prompt = NavPromptCore.From(NavPrompt.Favourite)
                .WithLabel(label, key)
                .AnchoredAt(NavPromptAnchor.TopRight, new Vector2(-360f, -70f))
                .OnOwnCanvas()
                .PollActions(RewiredConsts.Action.Menu_Favourite)
                .AllowWhileUnfocused()
                .SpawnOn(null);

            _promptGroup = null;
            _promptShownAt = Time.unscaledTime;
        }

        // visual-only fade — the prompt stays fully functional (IsPressed polls the bound Rewired
        // action directly, not a click), it just dims out of the way after a few seconds so it's not
        // sitting on screen the whole time you're flying around. only the "on" prompt fades — while
        // off there's no camera/HUD clutter to get out of the way of, so it just stays put.
        void UpdatePromptFade()
        {
            if (_prompt == null || !_prompt.IsAlive) return;
            if (_promptGroup == null)
            {
                var go = _prompt.GameObject;
                if (go == null) return;
                _promptGroup = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();
            }
            if (!_customOn) { _promptGroup.alpha = 1f; return; }
            float elapsed = Time.unscaledTime - _promptShownAt;
            _promptGroup.alpha = Mathf.Clamp01(1f - elapsed / PromptFadeDuration);
        }

        void Shutdown()
        {
            Disengage();
            _prompt?.Destroy();
            _prompt = null;
            _promptGroup = null;
            ClearCaches();
        }

        // _cgm deliberately survives a shutdown — dropping it there meant re-walking
        // GlobalGameStateClient.GameStateView.GetLiveClientGameManager every frame of every round we
        // weren't spectating. the real session changes below clear it, and a destroyed manager reads as
        // null anyway.
        void ClearCaches()
        {
            _director = null;
            _lastTarget = null;
            _defaultUiGroup = null;
            _overlayUiGroup = null;
            _nextRoundBtn = null;
            _subMenuNavRight = null;
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
