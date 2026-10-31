using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BetterFG.Services;
using BetterFG.UI.Windows;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.SideWheel
{
    public class SideWheelManager : MonoBehaviour
    {
        public SideWheelManager(IntPtr ptr) : base(ptr) { }

        public static SideWheelManager Instance { get; private set; }

        // ── Tweakable at runtime ──────────────────────────────────────────────
        public float RingDiameter = 340f;
        public Vector2 RingImageSize = new Vector2(300f, 338f);
        public Vector2 RingImagePivot = new Vector2(117f / 300f, 1f - 164f / 338f);
        public float PeekAmount = 72f;
        public float IconSize = 38f;
        public float IconOrbitR = 137.9549f;
        public float IconScale = 1.2f;
        public float IconHoverScale = 1.5f;
        public float ScrollSpeed = 20f;
        public float ScrollSmooth = 20f;
        public float HitW = 120f;
        public float HitH = 400f;
        public float WindowX = 100f;
        public Color SelTint = new Color(1f, 0.85f, 0.4f, 1f);

        // ── Animation curve for window open ───────────────────────────────────
        private static readonly AnimationCurve OpenCurve = new AnimationCurve(new Keyframe[]
        {
            new Keyframe(0f, 0f, 0f, 2.5f),
            new Keyframe(0.6f, 1.05f, 0.3f, 0.3f),
            new Keyframe(1f, 1f, 0f, 0f),
        });

        // ── Runtime refs ──────────────────────────────────────────────────────
        private Canvas _canvas;
        private RectTransform _wheelRt;
        private RectTransform _ringRt;
        private CanvasGroup _canvasGroup;

        private float _currentAngle = 0f;
        private float _targetAngle = 0f;
        private int _hoveredIdx = -1;
        private bool _pressed = false;
        private bool _dragged = false;
        private float _dragStartY = 0f;
        private float _lastDragY = 0f;
        private float _dragEndTime = -1f;
        private bool _wheelVisible = false;
        // true only when the wheel is actually on screen — the toggle flag alone isn't enough,
        // because hiding the parent BettrFG UI drops the wheel's canvas alpha to 0 while leaving
        // _wheelVisible set. (without this, ControllerManager thinks the wheel is up and keeps
        // driving the cursor even when everything's hidden.)
        public bool IsWheelVisible => _wheelVisible && _canvasGroup != null && _canvasGroup.alpha > 0.01f;


        private readonly List<WheelEntry> _entries = new List<WheelEntry>();
        private readonly List<RectTransform> _iconRts = new List<RectTransform>();
        private readonly List<RawImage> _iconImgs = new List<RawImage>();
        private readonly List<RectTransform> _labelRts = new List<RectTransform>();

        private BetterFGWindow _openWindow;
        private int _selectedIdx = -1;
        private bool _animating = false;
        private int _windowIconIdx = -1;

        private struct WheelEntry
        {
            public string locId;
            public Texture2D icon;
            public Func<BetterFGWindow> createWindow;
        }

        // ── Entry point ───────────────────────────────────────────────────────
        public static SideWheelManager Create()
        {
            var go = new GameObject("BetterFG_SideWheel");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            return go.AddComponent<SideWheelManager>();
        }

        public void RegisterEntry(string locId, Texture2D icon, Func<BetterFGWindow> createWindow)
        {
            _entries.Add(new WheelEntry { locId = locId, icon = icon, createWindow = createWindow });
            RebuildIcons();
        }

        public RectTransform RingRect => _ringRt;
        public float RingLocalRadius => RingImageSize.y * (327f / 676f);

        // the onboarding tutorial holds this so the wheel keybind only works on the steps that ask
        // for it. does not affect clicks on the wheel itself once it's open.
        public static bool InputLocked;

        // pins the wheel's rotation on top of the existing "a window is open" rule. the tutorial
        // holds this so you can't spin the ring away from the entry it's pointing at.
        public static bool ScrollLocked;

        private bool CanRotate => _openWindow == null && !ScrollLocked;

        public BetterFGWindow CurrentWindow => _openWindow;

        public RectTransform GetEntryRect(string locId)
        {
            int idx = _entries.FindIndex(e => e.locId == locId);
            return (idx >= 0 && idx < _iconRts.Count) ? _iconRts[idx] : null;
        }

        public void CenterOn(string locId)
        {
            int idx = _entries.FindIndex(e => e.locId == locId);
            if (idx < 0) return;
            _currentAngle = _targetAngle = ICON_STEP_DEG * idx;
            PositionIcons();
        }

        public void SetVisible(bool visible)
        {
            // Called by TabManager Shift+Z — only shows if wheel is also toggled on
            if (_canvasGroup == null) return;
            bool show = visible && _wheelVisible;
            _canvasGroup.alpha = show ? 1f : 0f;
            _canvasGroup.blocksRaycasts = show;
            _canvasGroup.interactable = show;
            if (_canvas != null) _canvas.enabled = show;
            if (!visible)
            {
                CloseWindow();
                LobbyAutokickConfigWindow.Instance?.Close();
            }
        }

        // controller bind entry point — same toggle the keyboard hotkey path uses.
        public void ToggleFromController() => SetWheelVisible(!_wheelVisible);

        // lets a tab open on top of the wheel instead of refusing — closes the wheel out of the way.
        public void CloseWheel()
        {
            if (_wheelVisible) SetWheelVisible(false);
        }

        // general "jump the user to this window" entry: force the wheel visible, open the entry whose window
        // type is T, and once its open animation lands hand the live window to onOpened (used to flash the
        // controls the user was pointed at). locId must match what SidewheelRegistry registered T under.
        public void OpenWindow<T>(string locId, System.Action<BetterFGWindow> onOpened = null)
            where T : BetterFGWindow
        {
            int idx = _entries.FindIndex(e => e.locId == locId);
            if (idx < 0) { Plugin.Log.LogWarning($"sidewheel: no entry '{locId}' to open"); return; }

            if (!_wheelVisible) SetWheelVisible(true);
            // if this window's already the open one, don't re-open (that would toggle it shut) — just re-run
            // the callback against the existing instance.
            if (_selectedIdx == idx && _openWindow != null) { onOpened?.Invoke(_openWindow); return; }

            OnIconClicked(idx);
            if (onOpened != null && _openWindow != null)
                StartCoroutine(InvokeAfterOpen(_openWindow, onOpened).WrapToIl2Cpp());
        }

        private IEnumerator InvokeAfterOpen(BetterFGWindow window, System.Action<BetterFGWindow> cb)
        {
            while (_animating) yield return null;
            cb(window);
        }

        private void SetWheelVisible(bool visible)
        {
            _wheelVisible = visible;
            if (_canvasGroup == null) return;
            _canvasGroup.alpha = visible ? 1f : 0f;
            _canvasGroup.blocksRaycasts = visible;
            _canvasGroup.interactable = visible;
            if (_canvas != null) _canvas.enabled = visible;
            if (!visible)
            {
                CloseWindow();
                LobbyAutokickConfigWindow.Instance?.Close();
            }
            BetterFGUIMan.Instance?.OnWheelVisibilityChanged(visible);
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        void Awake()
        {
            Instance = this;
            BuildCanvas();
            SetVisible(false);
            BetterFG.Services.LocalizationService.LanguageChanged += RefreshLabels;
        }

        private void RefreshLabels()
        {
            for (int i = 0; i < _labelRts.Count && i < _entries.Count; i++)
            {
                if (_labelRts[i] == null) continue;
                var t = _labelRts[i].GetComponent<Text>();
                if (t != null)
                    t.text = BetterFG.Services.LocalizationService.Get(_entries[i].locId).ToUpperInvariant();
            }
        }

        void Update()
        {
            var wheelKey = BetterFG.Services.KeybindService.Get(BetterFG.Services.KeybindId.ToggleWheel);
            if (_wheelVisible && BetterFG.Features.Replay.ReplayViewer.Instance != null) SetWheelVisible(false);
            if (!InputLocked
                && BetterFG.Features.Replay.ReplayViewer.Instance == null
                && BetterFG.Services.KeybindService.KeyDown(wheelKey)
                && !BetterFG.Services.KeybindService.ShiftHeld()
                && !IsTyping()
                && !BetterFG.Features.TextChat.FeatureTextChat.Typing)
                SetWheelVisible(!_wheelVisible);

            if (!_wheelVisible) return;
            HandleScrollAndClick();
            SmoothRotation();
            PositionIcons();
            UpdateHover();
            ApplyRuntimeLayout();
            if (_openWindow != null) UpdateWindowTransform();

        }

        private static IntPtr _typingSelPtr = IntPtr.Zero;
        private static bool _typingSelIsField;

        private static bool IsTyping()
        {
            var cur = UnityEngine.EventSystems.EventSystem.current;
            if (cur == null || cur.currentSelectedGameObject == null) { _typingSelPtr = IntPtr.Zero; return false; }
            var go = cur.currentSelectedGameObject;
            if (!go.activeInHierarchy) return false;

            IntPtr ptr = go.Pointer;
            if (ptr != _typingSelPtr)
            {
                _typingSelPtr = ptr;
                _typingSelIsField = go.GetComponent<UnityEngine.UI.InputField>() != null
                    || go.GetComponent<TMPro.TMP_InputField>() != null;
            }
            return _typingSelIsField;
        }

        private float _apDiameter = -1f, _apPeek;

        private void ApplyRuntimeLayout()
        {
            // only touch the rect when the tunables actually moved, re-writing the same values every
            // frame dirties the canvas for nothing. UpdateShape already dedupes internally
            if (_wheelRt != null && (_apDiameter != RingDiameter || _apPeek != PeekAmount))
            {
                _apDiameter = RingDiameter;
                _apPeek = PeekAmount;
                _wheelRt.anchoredPosition = new Vector2(-(RingDiameter * 0.5f) + PeekAmount, 0f);
                _wheelRt.sizeDelta = new Vector2(RingDiameter, RingDiameter);
            }
            if (_ringRt != null && (_ringRt.sizeDelta != RingImageSize || _ringRt.pivot != RingImagePivot))
            {
                _ringRt.sizeDelta = RingImageSize;
                _ringRt.pivot = RingImagePivot;
            }
        }

        // ── Build ─────────────────────────────────────────────────────────────

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("SideWheelCanvas");
            canvasGo.hideFlags = HideFlags.HideAndDontSave;
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 998;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            BetterFG.Services.UIScaleService.Register(_canvas);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            _canvasGroup = canvasGo.AddComponent<CanvasGroup>();

            var wheelGo = new GameObject("Wheel");
            wheelGo.transform.SetParent(canvasGo.transform, false);
            _wheelRt = wheelGo.AddComponent<RectTransform>();
            _wheelRt.anchorMin = new Vector2(0f, 0.5f);
            _wheelRt.anchorMax = new Vector2(0f, 0.5f);
            _wheelRt.pivot = new Vector2(0.5f, 0.5f);
            _wheelRt.anchoredPosition = new Vector2(-(RingDiameter * 0.5f) + PeekAmount, 0f);
            _wheelRt.sizeDelta = new Vector2(RingDiameter, RingDiameter);
            _wheelRt.localScale = new Vector2(1.2f, 1.2f);


            BuildRingMesh(wheelGo);
        }

        private void BuildRingMesh(GameObject parent)
        {
            var ringGo = new GameObject("Ring");
            ringGo.transform.SetParent(parent.transform, false);
            _ringRt = ringGo.AddComponent<RectTransform>();
            _ringRt.anchorMin = new Vector2(0.5f, 0.5f);
            _ringRt.anchorMax = new Vector2(0.5f, 0.5f);
            _ringRt.pivot = RingImagePivot;
            _ringRt.sizeDelta = RingImageSize;
            _ringRt.anchoredPosition = Vector2.zero;
            _ringRt.localScale = new Vector3(0.94f, 0.94f, 0.94f);

            var img = ringGo.AddComponent<RawImage>();
            img.texture = LoadEmbedded("BetterFG.assets.ui.side.sidewheel.png");
            img.raycastTarget = false;
        }

        private void RebuildIcons()
        {
            foreach (var rt in _iconRts)
                if (rt != null) UnityEngine.Object.Destroy(rt.gameObject);
            _iconRts.Clear();
            _iconImgs.Clear();
            _labelRts.Clear();

            for (int i = 0; i < _entries.Count; i++)
            {
                int captured = i;
                var entry = _entries[i];

                var iconGo = UGUIShip.CreateIconButton(
                    _wheelRt,
                    new Vector2(IconSize, IconSize),
                    entry.icon,
                    () => OnIconClicked(captured),
                    () => { _hoveredIdx = captured; },
                    () => { if (_hoveredIdx == captured) _hoveredIdx = -1; }
                ).gameObject;

                var rt = iconGo.GetComponent<RectTransform>();
                rt.localScale = new Vector3(IconScale, IconScale, IconScale);

                var raw = iconGo.GetComponent<RawImage>();

                // hover label — child of the icon. CreateLabel gives us the styled Text; we then
                // re-anchor to left-middle pivot so it grows outward, and drive position/rotation/
                // scale per-frame in PositionIcons to keep it on the icon's radial ray.
                var lbl = UGUIShip.CreateLabel(iconGo.transform, new Rect(0, 0, 880f, IconSize / LabelScale),
                    BetterFG.Services.LocalizationService.Get(entry.locId).ToUpperInvariant(), 16);
                lbl.fontStyle = FontStyle.Bold;
                lbl.horizontalOverflow = HorizontalWrapMode.Overflow;
                lbl.verticalOverflow = VerticalWrapMode.Overflow;
                var lblRt = lbl.rectTransform;
                lblRt.anchorMin = new Vector2(0.5f, 0.5f);
                lblRt.anchorMax = new Vector2(0.5f, 0.5f);
                lblRt.pivot = new Vector2(0f, 0.5f);
                lblRt.localScale = new Vector3(LabelScale, LabelScale, 1f);
                var outline = lbl.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                outline.effectDistance = new Vector2(2f, -2f);
                lbl.gameObject.SetActive(false);

                _iconRts.Add(rt);
                _iconImgs.Add(raw);
                _labelRts.Add(lblRt);
            }

            PositionIcons();
        }

        // ── Input ─────────────────────────────────────────────────────────────

        private void HandleScrollAndClick()
        {
            if (_entries.Count == 0) return;
            if (_canvasGroup != null && _canvasGroup.alpha < 0.01f) return;

            var mouse = new Vector2(Input.mousePosition.x, Input.mousePosition.y);

            if (_pressed)
            {
                if (Input.GetMouseButton(0))
                {
                    if (Mathf.Abs(mouse.y - _dragStartY) > DragDeadzonePx) _dragged = true;
                    if (_dragged)
                    {
                        _dragEndTime = Time.unscaledTime;
                        if (CanRotate)
                        {
                            _targetAngle += (mouse.y - _lastDragY) * DragDegPerPixel;
                            _currentAngle = _targetAngle;
                        }
                    }
                    _lastDragY = mouse.y;
                }
                else
                {
                    _pressed = false;
                    _dragged = false;
                }
                return;
            }

            if (!IsInHitRect(mouse)) return;

            float scroll = Input.mouseScrollDelta.y;
            if (CanRotate && Mathf.Abs(scroll) > 0.01f)
                _targetAngle -= scroll * ICON_STEP_DEG;

            if (Input.GetMouseButtonDown(0))
            {
                _pressed = true;
                _dragged = false;
                _dragStartY = _lastDragY = mouse.y;
            }
        }

        private bool IsInHitRect(Vector2 screenPos)
        {
            float cy = Screen.height * 0.5f;
            return screenPos.x >= 0f && screenPos.x <= HitW
                && screenPos.y >= cy - HitH * 0.5f && screenPos.y <= cy + HitH * 0.5f;
        }

        // ── Rotation ─────────────────────────────────────────────────────────

        private void SmoothRotation()
        {
            _currentAngle = Mathf.LerpAngle(_currentAngle, _targetAngle, Time.deltaTime * ScrollSmooth);
        }

        private void UpdateHover()
        {
            for (int i = 0; i < _iconRts.Count; i++)
            {
                if (_iconRts[i] == null) continue;
                bool hov = i == _hoveredIdx;
                float s = hov ? IconHoverScale : IconScale;
                _iconRts[i].localScale = new Vector3(s, s, s);
                // hide labels on the open window's icon and its two ring neighbours (they'd overlap the window)
                bool nearWindow = _windowIconIdx >= 0 && Mathf.Abs(i - _windowIconIdx) <= 1;
                bool showLabel = hov && !nearWindow;
                if (_labelRts[i] != null && _labelRts[i].gameObject.activeSelf != showLabel)
                    _labelRts[i].gameObject.SetActive(showLabel);
            }
        }

        private const float ICON_STEP_DEG = 25f;
        private const float DragDeadzonePx = 4f;
        private const float DragDegPerPixel = 0.42f;
        // hover label renders at a big font size then scales down here so text stays crisp
        private const float LabelScale = 0.68f;
        // how far past the icon center the label sits, along the radial ray (icon-local px)
        private const float LabelGap = 34f;
        // fraction of the ray angle the label tilts by; 1 = fully radial, 0 = always flat
        private const float LabelTiltStrength = 0.3f;

        private void PositionIcons()
        {
            if (_entries.Count == 0) return;
            for (int i = 0; i < _iconRts.Count; i++)
            {
                // wrap before use — _currentAngle accumulates unbounded from scrolling, and the tilt
                // below multiplies by LabelTiltStrength before Quaternion.Euler's own periodicity kicks
                // in, so an unwrapped angle drifts the label rotation further off with every lap scrolled
                float angleDeg = Mathf.Repeat(_currentAngle - ICON_STEP_DEG * i + 180f, 360f) - 180f;
                float angleRad = angleDeg * Mathf.Deg2Rad;
                var pos = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * IconOrbitR;
                _iconRts[i].anchoredPosition = pos;

                float vis = Mathf.Clamp01((pos.x + IconSize) / (IconSize * 2f));
                if (_iconImgs[i] != null) _iconImgs[i].color = new Color(1f, 1f, 1f, vis);

                // keep the hover label on the same radial ray as the icon, pushed further out and
                // rotated to read outward. the icon stays upright; the label pivot is placed along
                // the outward direction in the icon's local space (uniform IconScale, so divide).
                if (_labelRts[i] != null)
                {
                    var outward = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
                    _labelRts[i].anchoredPosition = outward * (LabelGap / IconScale);
                    _labelRts[i].localRotation = Quaternion.Euler(0f, 0f, angleDeg * LabelTiltStrength);
                }
            }
        }

        // Snap target so icon[idx] lands at 0° (3 o'clock, rightmost visible)
        private void RotateToIcon(int idx)
        {
            float iconAngle = (_currentAngle - ICON_STEP_DEG * idx) % 360f;
            if (iconAngle > 180f) iconAngle -= 360f;
            _targetAngle = _currentAngle - iconAngle;
        }

        // ── Window ────────────────────────────────────────────────────────────

        private void OnIconClicked(int idx)
        {
            if (_animating) return;
            if (Time.unscaledTime - _dragEndTime < 0.1f) return;

            if (_selectedIdx == idx && _openWindow != null)
            {
                CloseWindow();
                return;
            }

            CloseWindow();
            _selectedIdx = idx;
            _windowIconIdx = idx;
            RefreshIconTints();

            // Capture icon's current angle on the ring before rotation starts
            float spawnAngleDeg = (_currentAngle - ICON_STEP_DEG * idx) % 360f;
            if (spawnAngleDeg > 180f) spawnAngleDeg -= 360f;

            RotateToIcon(idx);

            var window = _entries[idx].createWindow();
            if (window == null) return;
            _openWindow = window;

            StartCoroutine(AnimateWindowOpen(window, spawnAngleDeg, Vector2.zero).WrapToIl2Cpp());

            if (!BetterFG.Features.Onboarding.OnboardingController.IsRunning)
                BetterFG.Features.Onboarding.HelpPrompt.OfferForWheelEntry(_entries[idx].locId);
        }

        private IEnumerator AnimateWindowOpen(BetterFGWindow window, float startAngleDeg, Vector2 spawnLocalPos)
        {
            _animating = true;
            float elapsed = 0f;
            const float DUR = 0.18f;

            var wheelPos = _wheelRt.anchoredPosition;
            float restOffsetX = WindowX - wheelPos.x;

            // Start position: icon's world position on the ring at click time
            float startRad = startAngleDeg * Mathf.Deg2Rad;
            var startPos = wheelPos + new Vector2(Mathf.Cos(startRad), Mathf.Sin(startRad)) * IconOrbitR;

            // End position: window at rest (angle = 0 after RotateToIcon snaps)
            var endPos = new Vector2(WindowX, 0f);

            window.SetAnchorPosition(startPos);
            window.SetRotation(startAngleDeg);
            window.SetScaleX(0f);
            window.Show();
            AudioService.PlayTabOpen();

            while (elapsed < DUR)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / DUR);
                float curve = OpenCurve.Evaluate(t);

                // Track the icon's live position during ring rotation
                float liveAngle = _currentAngle - ICON_STEP_DEG * _windowIconIdx;
                float liveRad = liveAngle * Mathf.Deg2Rad;
                var liveWheelPos = _wheelRt.anchoredPosition;
                float liveOffsetX = WindowX - liveWheelPos.x;
                var livePos = liveWheelPos + new Vector2(Mathf.Cos(liveRad), Mathf.Sin(liveRad)) * liveOffsetX;

                window.SetAnchorPosition(livePos);
                window.SetRotation(liveAngle);
                window.SetScaleX(curve);
                yield return null;
            }

            window.SetScaleX(1f);
            _animating = false;
        }

        private IEnumerator AnimateWindowClose(BetterFGWindow window)
        {
            float elapsed = 0f;
            const float DUR = 0.12f;
            AudioService.PlayTabClose();

            while (elapsed < DUR)
            {
                elapsed += Time.deltaTime;
                float t = 1f - Mathf.Clamp01(elapsed / DUR);
                window.SetScaleX(t * t);
                yield return null;
            }
            window.SetScaleX(0f);
            UnityEngine.Object.Destroy(window.gameObject);
        }

        private void CloseWindow()
        {
            _selectedIdx = -1;
            _windowIconIdx = -1;
            RefreshIconTints();

            if (_openWindow != null)
            {
                var win = _openWindow;
                _openWindow = null;
                StartCoroutine(AnimateWindowClose(win).WrapToIl2Cpp());
            }
        }

        // swap what's in the open slot for another window without touching the ring selection, so a
        // partial window (and its back link) rides the same icon the parent came out of.
        public void SwapWindow(BetterFGWindow next)
        {
            if (_windowIconIdx < 0) { Plugin.Log.LogWarning("sidewheel: nothing open to swap out of"); return; }

            if (_openWindow != null)
            {
                var old = _openWindow;
                _openWindow = null;
                StartCoroutine(AnimateWindowClose(old).WrapToIl2Cpp());
            }

            _openWindow = next;
            StartCoroutine(AnimateWindowOpen(next, _currentAngle - ICON_STEP_DEG * _windowIconIdx, Vector2.zero).WrapToIl2Cpp());
        }

        private void RefreshIconTints()
        {
            for (int i = 0; i < _iconImgs.Count; i++)
            {
                if (_iconImgs[i] == null) continue;
                _iconImgs[i].color = i == _selectedIdx ? SelTint : Color.white;
            }
        }

        private void UpdateWindowTransform()
        {
            if (_openWindow == null || _windowIconIdx < 0 || _animating) return;
            float angleDeg = _currentAngle - ICON_STEP_DEG * _windowIconIdx;
            float angleRad = angleDeg * Mathf.Deg2Rad;

            // Rotate the rest offset (window-to-ring-center distance at angle 0) around the ring center.
            var wheelPos = _wheelRt.anchoredPosition;
            float restOffsetX = WindowX - wheelPos.x;
            var rotated = new Vector2(
                Mathf.Cos(angleRad) * restOffsetX,
                Mathf.Sin(angleRad) * restOffsetX
            );
            _openWindow.SetAnchorPosition(wheelPos + rotated);
            _openWindow.SetRotation(angleDeg);
        }

        // ── Texture loader ────────────────────────────────────────────────────

        public static Texture2D LoadEmbedded(string resourceName)
            => BetterFG.Utilities.EmbeddedResourceandUnity.LoadTexture(resourceName);
    }

    public class RingHoleGraphic : Graphic
    {
        public RingHoleGraphic(IntPtr ptr) : base(ptr) { }

        private RectTransform _ring;
        private float _ringInner;
        private Vector2 _center;
        private Vector2 _hole;
        private Vector2 _outer;

        public void Init(RectTransform ring, float ringInnerLocal)
        {
            _ring = ring;
            _ringInner = ringInnerLocal;
            color = Color.white;
            raycastTarget = true;
            Sync();
        }

        void LateUpdate() => Sync();

        private void Sync()
        {
            if (_ring == null) return;
            var s = rectTransform.lossyScale;
            float sx = Mathf.Abs(s.x), sy = Mathf.Abs(s.y);
            if (sx < 0.2f || sy < 0.01f) return;

            var c = (Vector2)rectTransform.InverseTransformPoint(_ring.position);
            float world = _ringInner * _ring.lossyScale.y;
            var hole = new Vector2(world / sx, world / sy);
            var outer = new Vector2(4000f / sx, 4000f / sy);
            if ((c - _center).sqrMagnitude < 0.01f && (hole - _hole).sqrMagnitude < 0.01f) return;

            _center = c;
            _hole = hole;
            _outer = outer;
            SetVerticesDirty();
        }

        public override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_hole.x <= 0f || _hole.y <= 0f) return;
            const int SEGMENTS = 96;
            float step = 2f * Mathf.PI / SEGMENTS;
            for (int i = 0; i < SEGMENTS; i++)
            {
                float a0 = step * i;
                float a1 = step * (i + 1);
                var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                int b = i * 4;
                vh.AddVert(_center + Vector2.Scale(d0, _outer), color, Vector2.zero);
                vh.AddVert(_center + Vector2.Scale(d1, _outer), color, Vector2.zero);
                vh.AddVert(_center + Vector2.Scale(d1, _hole), color, Vector2.zero);
                vh.AddVert(_center + Vector2.Scale(d0, _hole), color, Vector2.zero);
                vh.AddTriangle(b, b + 1, b + 2);
                vh.AddTriangle(b, b + 2, b + 3);
            }
        }
    }
}
