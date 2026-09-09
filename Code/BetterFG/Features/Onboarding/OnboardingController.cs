using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using BetterFG.UI;
using BetterFG.UI.Components;
using BetterFG.Utilities;
using BetterFG.UI.SideWheel;
using BetterFG.UI.Windows;
using BettrFG.uGUI;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Features.Onboarding
{
    // first-boot tutorial. the BettrFG UI stays hidden until you accept or skip; while it runs the
    // game is covered by an opaque backdrop and every click outside the one thing the current step
    // asks for is eaten, so there is exactly one way forward.
    //
    // runs once ever, finishing or skipping writes seen.onboarding. deliberately NOT tied to
    // startup.seen, so players who updated from an older build still get it the once.
    public class OnboardingController : PromptHost
    {
        public OnboardingController(IntPtr ptr) : base(ptr) { }

        public const string KEY_SEEN = "seen.onboarding";

        public static OnboardingController Instance { get; private set; }

        // first-open help prompts stay out of the way while the tour is driving
        public static bool IsRunning => Instance != null && Instance._running;

        public static bool StartupDone;

        // BettrFG's canvases live at 996-1001, and the game draws somewhere in that band, a backdrop
        // that sits under our UI ends up under the game too. So while the tutorial runs we lift every
        // BettrFG canvas by this much and slot the backdrop just below the lifted block: the game is
        // then far below all of it, and our own relative order is untouched.
        private const int Lift = 2000;
        private const int BackdropOrder = 996 + Lift - 1;
        private const int BlockerOrder = 1001 + Lift + 5;
        private const int PopupOrder = 1001 + Lift + 10;

        // the wheel plus whatever window it opens; the window is the wide part
        private const float WheelStripWidth = 700f;

        // how long the loading cover needs to sweep in and fully own the screen
        private const float CoverSettle = 0.5f;

        private Canvas _backdropCanvas;
        private GameObject _pulse;

        private bool _running;
        private int _step;
        private int _goalSlot;

        // the click-through hole tracks its target every frame, tab titles slide as their tab opens
        // and closes, and a hole captured once ends up somewhere the target no longer is.
        private RectTransform _holeTarget;
        private float _holePad;

        private readonly List<Canvas> _lifted = new List<Canvas>();
        private readonly List<int> _liftedOrder = new List<int>();

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Start() => StartCoroutine(WaitThenStart().WrapToIl2Cpp());

        private IEnumerator WaitThenStart()
        {
            if (SettingsService.Get(KEY_SEEN, "false") == "true") yield break;
            while (BetterFGUIMan.Instance == null) yield return null;
            while (BetterFGUIMan.Instance.SlotCount == 0
                   || BetterFGUIMan.Instance.GetSlotTab(0)?.TitleRt == null)
                yield return null;
            for (int i = 0; i < 3; i++) yield return null;
            while (!StartupDone) yield return null;
            Begin();
            // the canvases were created this frame, so their CanvasScaler hasn't sized them yet and
            // rect still reads raw screen pixels; anything placed off it lands too far out
            yield return null;
            Step(0);
        }

        private void Begin()
        {
            if (_running) return;
            _running = true;

            DumpCanvases();

            var uim = BetterFGUIMan.Instance;
            uim.SetVisibleForced(false);
            uim.VisibilityLocked = true;
            SideWheelManager.InputLocked = true;

            LiftCanvases();

            _backdropCanvas = UGUIShip.CreateCanvas("BetterFG_OnboardingBackdrop");
            _backdropCanvas.sortingOrder = BackdropOrder;
            var bg = new GameObject("Backdrop");
            bg.transform.SetParent(_backdropCanvas.transform, false);
            var brt = bg.AddComponent<RectTransform>();
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            var bimg = bg.AddComponent<Image>();
            bimg.color = new Color(0.10f, 0.11f, 0.13f, 1f);
            bimg.raycastTarget = true;

            _blockerCanvas = UGUIShip.CreateCanvas("BetterFG_OnboardingBlocker");
            _blockerCanvas.sortingOrder = BlockerOrder;
            _blocker = new OnboardingBlocker(_blockerCanvas);

            _canvas = UGUIShip.CreateCanvas("BetterFG_OnboardingCanvas");
            _canvas.sortingOrder = PopupOrder;
        }

        // one-shot inventory of who is drawing where, so the next ordering problem is answered by the
        // log instead of by guessing.
        private static void DumpCanvases()
        {
            var seen = UnityEngine.Object.FindObjectsOfType<Canvas>();
            Plugin.Log.LogInfo($"onboarding starting, {seen.Length} canvases live right now:");
            foreach (var c in seen)
            {
                if (c == null) continue;
                Plugin.Log.LogInfo($"  {c.name}, order {c.sortingOrder}, layer '{c.sortingLayerName}', {c.renderMode}, enabled={c.enabled}");
            }
        }

        private void LiftCanvases()
        {
            foreach (var c in UIScaleService.All)
            {
                if (c == null) continue;
                if (c == _canvas || c == _blockerCanvas || c == _backdropCanvas) continue;
                if (_lifted.Contains(c)) continue;
                _lifted.Add(c);
                _liftedOrder.Add(c.sortingOrder);
                c.sortingOrder += Lift;
            }
        }

        private void DropCanvases()
        {
            for (int i = 0; i < _lifted.Count; i++)
                if (_lifted[i] != null) _lifted[i].sortingOrder = _liftedOrder[i];
            _lifted.Clear();
            _liftedOrder.Clear();
        }

        void Update()
        {
            if (!_running) return;

            // windows build their canvas lazily the first time they open (Tweaks does), so anything
            // that appeared since the last tick has to be lifted too or it spawns under the backdrop.
            LiftCanvases();

            if (_holeTarget != null && _blocker != null)
                _blocker.SetHole(CanvasAabbOf(_holeTarget, _holePad));

            if (!GoalMet()) return;

            int next = _step + 1;
            // let the tab's open/close slide settle before the next step measures anything
            float pause = _step == StepCloseTab ? 0.4f
                        : _step == StepPickTab ? 0.35f
                        : 0f;

            _step = StepHandover;
            ClearPopup();
            ClearPulse();

            if (pause > 0f) StartCoroutine(StepAfter(pause, next).WrapToIl2Cpp());
            else Step(next);
        }

        private IEnumerator StepAfter(float seconds, int next)
        {
            float e = 0f;
            while (e < seconds) { e += Time.unscaledDeltaTime; yield return null; }
            if (_running) Step(next);
        }

        private bool GoalMet()
        {
            var uim = BetterFGUIMan.Instance;
            switch (_step)
            {
                case StepToggleUiId: return uim?.IsVisible == true;
                // the slot holds a different Tab instance after a swap, so always re-read it
                case StepOpenTab: return uim?.GetSlotTab(_goalSlot)?.IsOpen == true;
                case StepRightClick: return uim?.SlotDropdownOpen == true;
                case StepPickTab: return uim?.SlotDropdownOpen == false;
                case StepCloseTab: return uim?.GetSlotTab(_goalSlot)?.IsOpen == false;
                case StepWheelId: return SideWheelManager.Instance?.IsWheelVisible == true;
                case StepTweaksId: return SideWheelManager.Instance?.CurrentWindow is TweaksWindow;
                case StepTweaksCloseId: return SideWheelManager.Instance?.CurrentWindow == null;
                default: return false;
            }
        }

        private void Finish()
        {
            if (!_running) return;
            _running = false;
            _step = -1;
            // written here so it covers both finishing and skipping, but not a crash mid-tour
            SettingsService.Set(KEY_SEEN, "true");
            Tweaks.StartupTitleScreenTweak.ForceOn = false;
            StartCoroutine(FinishRoutine().WrapToIl2Cpp());
        }

        // hand back to the game behind the shared loading cover, so the teardown (canvases dropping
        // 2000 places, the grey going, the UI coming back) never shows.
        private IEnumerator FinishRoutine()
        {
            LoadingScreenService.Show();
            var cover = LoadingScreenService.Canvas;
            if (cover != null) cover.sortingOrder = PopupOrder + 50;

            // the cover animates ITSELF in, tearing down while that's still sweeping across just
            // shows the seam we're covering. wait for it to actually own the screen first.
            float total = UnityEngine.Random.Range(1f, 2f);
            yield return new WaitForSecondsRealtime(CoverSettle);

            if (_popup != null) { UnityEngine.Object.Destroy(_popup); _popup = null; }
            ClearPulse();
            if (_blockerCanvas != null) { UnityEngine.Object.Destroy(_blockerCanvas.gameObject); _blockerCanvas = null; }
            if (_canvas != null) { UnityEngine.Object.Destroy(_canvas.gameObject); _canvas = null; }
            _blocker = null;
            _holeTarget = null;

            DropCanvases();
            SideWheelManager.InputLocked = false;
            SideWheelManager.ScrollLocked = false;

            var uim = BetterFGUIMan.Instance;
            if (uim != null)
            {
                RestoreAllSlots();
                uim.VisibilityLocked = false;
                uim.ChromeSuppressed = false;
                uim.SetVisibleForced(true);
            }

            // the grey goes last, it's the fallback cover if the loading prefab never loaded
            if (_backdropCanvas != null) { UnityEngine.Object.Destroy(_backdropCanvas.gameObject); _backdropCanvas = null; }

            // ride out the rest of the covered stretch before the outro pulls it back
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, total - CoverSettle));

            if (cover != null) cover.sortingOrder = LoadingScreenService.DefaultOrder;
            yield return LoadingScreenService.HideRoutine();
        }

        // roll the old popup shut before it goes, so steps hand over instead of snapping
        private void ClearPopup()
        {
            if (_popup == null) return;
            StartCoroutine(CloseThenDestroy(_popup).WrapToIl2Cpp());
            _popup = null;
        }

        private void ShowPopup(GameObject go)
        {
            _popup = go;
            StartCoroutine(OpenPopup(go).WrapToIl2Cpp());
        }

        private void ClearPulse()
        {
            if (_pulse != null) { UnityEngine.Object.Destroy(_pulse); _pulse = null; }
        }

        // only the pieces the current step actually points at stay on screen
        private void ShowOnlySlot(int slot)
        {
            var uim = BetterFGUIMan.Instance;
            for (int i = 0; i < uim.SlotCount; i++) uim.SetSlotActive(i, i == slot);
        }

        private void CloseOpenTabs()
        {
            var uim = BetterFGUIMan.Instance;
            for (int i = 0; i < uim.SlotCount; i++)
            {
                var t = uim.GetSlotTab(i);
                if (t != null && t.IsOpen) uim.ToggleTab(t);
            }
        }

        // ── Steps ─────────────────────────────────────────────────────────────
        private const int StepWelcomeId = 0;
        private const int StepTabsIntroId = 1;
        private const int StepToggleUiId = 2;
        private const int StepOpenTab = 3;
        private const int StepRightClick = 4;
        private const int StepPickTab = 5;
        private const int StepCloseTab = 6;
        private const int StepWheelId = 7;
        private const int StepTweaksId = 8;
        private const int StepTweaksInfoId = 9;
        private const int StepTweaksCloseId = 10;
        private const int StepDoneId = 11;
        // parked between steps so a met goal can't fire twice while the handover runs
        private const int StepHandover = -2;

        private void Step(int idx)
        {
            ClearPopup();
            ClearPulse();
            _step = idx;
            switch (idx)
            {
                case StepWelcomeId: StepWelcome(); break;
                case StepTabsIntroId: StepTabsIntro(); break;
                case StepToggleUiId: StepToggleUi(); break;
                case StepOpenTab: StepTabs(); break;
                case StepRightClick: StepSwitchTab(); break;
                case StepPickTab: StepPickFromDropdown(); break;
                case StepCloseTab: StepCloseTheTab(); break;
                case StepWheelId: StepWheel(); break;
                case StepTweaksId: StepTweaks(); break;
                case StepTweaksInfoId: StepTweaksInfo(); break;
                case StepTweaksCloseId: StepTweaksClose(); break;
                case StepDoneId: StepDone(); break;
                default: Finish(); break;
            }
        }

        // clicks land only on `target`, and the hole rides it from here on
        private Rect HoleOn(RectTransform target, float pad)
        {
            _holeTarget = target;
            _holePad = pad;
            var box = CanvasAabbOf(target, pad);
            _blocker.SetHole(box);
            return box;
        }

        private void HoleRect(Rect r)
        {
            _holeTarget = null;
            _blocker.SetHole(r);
        }

        private void HoleNone()
        {
            _holeTarget = null;
            _blocker.SetHole(null);
        }

        private OnboardingPopup.ButtonSpec SkipButton()
            => new OnboardingPopup.ButtonSpec("onboarding.btn.skip", () => Finish(), OnboardingPopup.BtnGhost);

        private OnboardingPopup.ButtonSpec NextButton(int next)
            => new OnboardingPopup.ButtonSpec("onboarding.btn.next", () => Step(next), OnboardingPopup.BtnPrimary);

        private void StepWelcome()
        {
            var uim = BetterFGUIMan.Instance;
            uim.ChromeSuppressed = true;
            uim.SetVisibleForced(false);
            SideWheelManager.InputLocked = true;
            SideWheelManager.ScrollLocked = true;
            HoleNone();

            ShowPopup(OnboardingPopup.Show(_canvas,
                "onboarding.intro.title", "onboarding.intro.body",
                Vector2.zero, new Vector2(0.5f, 0.5f),
                new[]
                {
                    SkipButton(),
                    new OnboardingPopup.ButtonSpec("onboarding.btn.lets_go", () => Step(StepTabsIntroId), OnboardingPopup.BtnPrimary),
                }));
        }

        private void StepTabsIntro()
        {
            var uim = BetterFGUIMan.Instance;
            SideWheelManager.InputLocked = true;
            SideWheelManager.ScrollLocked = true;

            _goalSlot = 0;
            uim.SetVisibleForced(false);
            ShowOnlySlot(_goalSlot);
            HoleNone();

            ShowPopup(OnboardingPopup.Show(_canvas,
                "onboarding.tabsintro.title", "onboarding.tabsintro.body",
                Vector2.zero, new Vector2(0.5f, 0.5f),
                new[] { SkipButton(), NextButton(StepToggleUiId) }));
        }

        private void StepToggleUi()
        {
            var uim = BetterFGUIMan.Instance;
            SideWheelManager.InputLocked = true;
            SideWheelManager.ScrollLocked = true;

            _goalSlot = 0;
            uim.SetVisibleForced(false);
            ShowOnlySlot(_goalSlot);
            uim.VisibilityLocked = false;
            HoleNone();

            ShowPopup(OnboardingPopup.Show(_canvas,
                "onboarding.toggleui.title",
                LocalizationService.Format("onboarding.toggleui.body", KeybindService.Label(KeybindId.ToggleUI)),
                Vector2.zero, new Vector2(0.5f, 0.5f),
                new[] { SkipButton() }));
        }

        private void StepTabs()
        {
            var uim = BetterFGUIMan.Instance;
            SideWheelManager.InputLocked = true;
            SideWheelManager.ScrollLocked = true;

            _goalSlot = 0;
            uim.VisibilityLocked = true;
            uim.SetVisibleForced(true);
            ShowOnlySlot(_goalSlot);

            if (!TabStep("onboarding.tabs.title", "onboarding.tabs.body")) Step(StepWheelId);
        }

        // right-click opens the switch-tab dropdown. TabHoverTint reads the mouse directly rather
        // than through the raycaster, so the hole here only governs left-clicks.
        private void StepSwitchTab()
        {
            if (!TabStep("onboarding.switch.title", "onboarding.switch.body")) Step(StepWheelId);
        }

        private void StepPickFromDropdown()
        {
            // the dropdown is parented into the slot's column and animates its height open, so hole
            // out the whole column rather than the dropdown's rect mid-animation
            var rootRt = BetterFGUIMan.Instance.GetSlotRoot(_goalSlot);
            var box = rootRt != null ? HoleOn(rootRt, 8f) : LeftStrip();
            if (rootRt == null) HoleRect(box);

            ShowPopup(OnboardingPopup.Show(_canvas,
                "onboarding.picktab.title", "onboarding.picktab.body",
                Beside(box), new Vector2(0f, 0.5f),
                new[] { SkipButton() }));
        }

        private void StepCloseTheTab()
        {
            if (!TabStep("onboarding.closetab.title", "onboarding.closetab.body")) Step(StepWheelId);
        }

        // the three steps that point at the slot's title are the same shape: hole + pulse on the
        // title, popup beside it.
        private bool TabStep(string titleId, string bodyId)
        {
            var titleRt = BetterFGUIMan.Instance.GetSlotTab(_goalSlot)?.TitleRt;
            if (titleRt == null) return false;

            var box = HoleOn(titleRt, 6f);
            SpawnPulse(box.center, Mathf.Max(80f, box.height * 2.4f), titleRt);

            ShowPopup(OnboardingPopup.Show(_canvas,
                titleId, bodyId, Beside(box), new Vector2(0f, 0.5f),
                new[] { SkipButton() }));
            return true;
        }

        // tabs fill their column downward, so sitting to the side keeps the popup out of the content
        // without pinning every step to the same spot
        private static Vector2 Beside(Rect box) => CanvasGeom.Beside(box);

        private void StepWheel()
        {
            // the tab the last step opened is tall and sits right where the wheel is about to be,
            // put it away and hide every slot so only the wheel is on screen.
            CloseOpenTabs();
            HideAllSlots();

            SideWheelManager.InputLocked = false;
            SideWheelManager.ScrollLocked = true;
            HoleRect(LeftStrip());

            float halfW = _canvas.GetComponent<RectTransform>().rect.width * 0.5f;
            SpawnPulse(new Vector2(-halfW + 40f, 0f), 110f, null);

            string key = KeybindService.Get(KeybindId.ToggleWheel).ToString();
            ShowPopup(OnboardingPopup.Show(_canvas,
                "onboarding.wheel.title",
                LocalizationService.Format("onboarding.wheel.body", key),
                new Vector2(-halfW + 130f, 0f), new Vector2(0f, 0.5f),
                new[] { SkipButton() }));
        }

        private void StepTweaks()
        {
            HideAllSlots();
            SideWheelManager.InputLocked = false;
            SideWheelManager.ScrollLocked = true;
            HoleRect(LeftStrip());

            float halfW = _canvas.GetComponent<RectTransform>().rect.width * 0.5f;

            var iconRt = SideWheelManager.Instance?.GetEntryRect("sidewheel.tweaks");
            float anchorY = 0f;
            if (iconRt != null)
            {
                var box = CanvasAabbOf(iconRt, 4f);
                anchorY = box.center.y;
                SpawnPulse(box.center, Mathf.Max(70f, box.height * 1.8f), iconRt);
            }

            ShowPopup(OnboardingPopup.Show(_canvas,
                "onboarding.tweaks.title", "onboarding.tweaks.body",
                new Vector2(-halfW + 300f, anchorY), new Vector2(0f, 0.5f),
                new[] { SkipButton() }));
        }

        // window's open and there's nothing to click but Next, so everything else stays dead
        private void StepTweaksInfo()
        {
            HideAllSlots();
            SideWheelManager.InputLocked = true;
            SideWheelManager.ScrollLocked = true;
            HoleNone();

            float halfW = _canvas.GetComponent<RectTransform>().rect.width * 0.5f;
            ShowPopup(OnboardingPopup.Show(_canvas,
                "onboarding.tweaksopen.title", "onboarding.tweaksopen.body",
                new Vector2(-halfW + WheelStripWidth + 40f, 0f), new Vector2(0f, 0.5f),
                new[] { SkipButton(), NextButton(StepTweaksCloseId) }));
        }

        // clicking the same wheel icon toggles its window shut, so keep pointing at it
        private void StepTweaksClose()
        {
            HideAllSlots();
            SideWheelManager.InputLocked = false;
            SideWheelManager.ScrollLocked = true;
            HoleRect(LeftStrip());

            float halfW = _canvas.GetComponent<RectTransform>().rect.width * 0.5f;

            var iconRt = SideWheelManager.Instance?.GetEntryRect("sidewheel.tweaks");
            float anchorY = 0f;
            if (iconRt != null)
            {
                var box = CanvasAabbOf(iconRt, 4f);
                anchorY = box.center.y;
                SpawnPulse(box.center, Mathf.Max(70f, box.height * 1.8f), iconRt);
            }

            // clear to the right of the open window rather than on top of it
            ShowPopup(OnboardingPopup.Show(_canvas,
                "onboarding.tweaksclose.title", "onboarding.tweaksclose.body",
                new Vector2(-halfW + WheelStripWidth + 40f, anchorY), new Vector2(0f, 0.5f),
                new[] { SkipButton() }));
        }

        private void StepDone()
        {
            SideWheelManager.InputLocked = true;
            SideWheelManager.ScrollLocked = true;
            BetterFGUIMan.Instance.SetVisibleForced(false);
            HoleNone();

            ShowPopup(OnboardingPopup.Show(_canvas,
                "onboarding.done.title", "onboarding.done.body",
                Vector2.zero, new Vector2(0.5f, 0.5f),
                new[] { new OnboardingPopup.ButtonSpec("onboarding.btn.finish", () => Finish(), OnboardingPopup.BtnPrimary) }));
        }

        private void HideAllSlots()
        {
            var uim = BetterFGUIMan.Instance;
            for (int i = 0; i < uim.SlotCount; i++) uim.SetSlotActive(i, false);
        }

        private void RestoreAllSlots()
        {
            var uim = BetterFGUIMan.Instance;
            for (int i = 0; i < uim.SlotCount; i++) uim.SetSlotActive(i, true);
        }

        // the wheel and whatever window it opens both live down the left side
        private Rect LeftStrip()
        {
            var r = _canvas.GetComponent<RectTransform>().rect;
            return Rect.MinMaxRect(-r.width * 0.5f, -r.height * 0.5f,
                                   -r.width * 0.5f + WheelStripWidth, r.height * 0.5f);
        }

        private Rect CanvasAabbOf(RectTransform target, float pad)
            => CanvasGeom.AabbOf(_canvas, target, pad);

        // pass `follow` for anything that moves under us (a tab title sliding open, a wheel icon
        // riding its orbit); null pins it where it spawned.
        private void SpawnPulse(Vector2 canvasPos, float size, RectTransform follow)
        {
            var go = new GameObject("Onboarding_Pulse");
            go.transform.SetParent(_canvas.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1f, 1f);
            rt.anchoredPosition = canvasPos;
            var pulse = go.AddComponent<HollowCirclePulse>();
            pulse.BaseSize = size;
            pulse.Follow = follow;
            pulse.HostCanvas = _canvas;
            _pulse = go;
        }
    }
}
