using System;
using System.Runtime.InteropServices;
using Rewired;
using BetterFG.Features.Replay;
using BetterFG.Services;
using UnityEngine;

namespace BetterFG.UI
{
    // moves the OS cursor with the stick while the BettrFG UI is open, and lets a button
    // left-click. reads the pad through Rewired (which the game already runs) so deadzones
    // and connected-controller detection are handled for us. cursor pos is what unity's
    // Input.mousePosition reads back, so all the existing hover/click code just works.
    public class ControllerManager : MonoBehaviour
    {
        public ControllerManager(IntPtr ptr) : base(ptr) { }

        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;

        private const float DEADZONE = 0.2f;

        private bool _rightHeld, _leftHeld;
        private float _scrollAccum;

        public static Vector2 Stick { get; private set; }

        public static ControllerManager Create()
        {
            var go = new GameObject("BetterFG_Controller");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            return go.AddComponent<ControllerManager>();
        }

        private float _nextPadProbe;
        private bool _padPresent;

        void Update()
        {
            Stick = Vector2.zero;

            // three rewired interop calls a frame just to find out nothing is plugged in. a pad
            // appearing can wait a quarter second, so probe on a timer and keep the answer.
            if (Time.unscaledTime >= _nextPadProbe)
            {
                _nextPadProbe = Time.unscaledTime + 0.25f;
                _padPresent = ReInput.isReady && ReInput.players.playerCount > 0
                              && ReInput.controllers.joystickCount > 0;
            }
            if (!_padPresent) { FGInputLockService.SetRewiredMouseSuppressed(false); return; }

            var p = ReInput.players.GetPlayer(0);
            if (p == null) { FGInputLockService.SetRewiredMouseSuppressed(false); return; }

            // toggle binds run even while the UI is hidden so a controller-only player can open it.
            // skip while a bind is being recorded so the press being captured doesn't also toggle.
            if (Windows.KeybindRecorder.IsRecording) return;
            int uiBtn = ControllerBindService.GetButton(ControllerBindId.ToggleUI);
            int wheelBtn = ControllerBindService.GetButton(ControllerBindId.ToggleWheel);
            bool toggledUI = uiBtn >= 0 && ControllerBindService.ButtonDownThisFrame(uiBtn) && BetterFGUIMan.Instance != null;
            bool toggledWheel = wheelBtn >= 0 && wheelBtn != uiBtn && ControllerBindService.ButtonDownThisFrame(wheelBtn);
            if (toggledUI) BetterFGUIMan.Instance.SetVisible(!BetterFGUIMan.Instance.IsVisible);
            if (toggledWheel) SideWheel.SideWheelManager.Instance?.ToggleFromController();

            // the on-screen keyboard owns the pad outright while it's up: same A button would
            // otherwise both press a key and fire a real click wherever the cursor was parked.
            if (OnScreenKeyboard.Tick(this))
            {
                if (_leftHeld) { mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero); _leftHeld = false; }
                if (_rightHeld) { mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, IntPtr.Zero); _rightHeld = false; }
                _scrollAccum = 0f;
                return;
            }

            bool editorUp = Windows.Creative.BatchEditWindow.AnyOpen
                            || Windows.Creative.PublishThumbnailWindow.StepActive
                            || ReplayViewer.Instance != null
                            || Features.Assets.AssetsScreen.Instance != null;

            // closing the UI with the pad leaves the OS cursor stranded on screen with no stick to move
            // it. once EVERYTHING is closed (main UI AND wheel), hide+lock the cursor — it comes back when
            // they reopen the UI (SetVisible unlocks it) or hit F1.
            if (toggledUI || toggledWheel)
            {
                bool anyUp = (BetterFGUIMan.Instance != null && BetterFGUIMan.Instance.IsVisible)
                          || (SideWheel.SideWheelManager.Instance != null && SideWheel.SideWheelManager.Instance.IsWheelVisible)
                          || editorUp;
                if (!anyUp) BetterFGUIMan.Instance?.SetCursorFree(false);
            }

            // drive the cursor whenever EITHER our main UI or the sidewheel is showing — both put a
            // mouse cursor on screen, so the stick should move it in both.
            bool uiUp = (BetterFGUIMan.Instance != null && BetterFGUIMan.Instance.IsVisible) || editorUp;
            bool wheelUp = SideWheel.SideWheelManager.Instance != null && SideWheel.SideWheelManager.Instance.IsWheelVisible;
            // the PB top-bar tab wants a cursor too, but we DON'T lock the pad there — the tab-switch
            // bumpers have to stay live so you can navigate off it (the tab's Customiser backdrop is
            // de-focused separately so its stick input doesn't fight the cursor).
            bool pbUp = BetterFG.Features.QualificationTime.PBTabView.IsOpen;
            if (!uiUp && !wheelUp && !pbUp)
            {
                if (_leftHeld) { mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero); _leftHeld = false; }
                if (_rightHeld) { mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, IntPtr.Zero); _rightHeld = false; }
                _scrollAccum = 0f;
                FGInputLockService.SetControllerLock(false);
                FGInputLockService.SetRewiredMouseSuppressed(false);
                return;
            }

            // we're about to move the OS cursor from the stick. keep Rewired's Mouse controller off so
            // it doesn't register that as activity and strobe the game's glyphs pad<->kbm every frame.
            FGInputLockService.SetRewiredMouseSuppressed(true);

            // pull the raw sticks straight off any connected joystick so we don't depend on the game's
            // action-map names. axes 0/1 = left stick X/Y (cursor), axes 2/3 = right stick X/Y (scroll).
            // (the interop IList has no Count, so loop on joystickCount and index it.) the click buttons
            // come from the user's binds via element id, not a fixed position.
            float mx = 0f, my = 0f, sy = 0f;
            var sticks = p.controllers.Joysticks;
            int n = p.controllers.joystickCount;
            for (int i = 0; i < n; i++)
            {
                var j = sticks[i];
                if (j == null) continue;
                if (j.axisCount >= 2) { mx += j.GetAxis(0); my += j.GetAxis(1); }
                if (j.axisCount >= 4) sy += j.GetAxis(3);
            }
            bool clickNow = ControllerBindService.ButtonHeldById(ControllerBindService.GetButton(ControllerBindId.LeftClick));
            bool rightNow = ControllerBindService.ButtonHeldById(ControllerBindService.GetButton(ControllerBindId.RightClick));
            mx = Mathf.Clamp(mx, -1f, 1f);
            my = Mathf.Clamp(my, -1f, 1f);
            sy = Mathf.Clamp(sy, -1f, 1f);

            Stick = new Vector2(Mathf.Abs(mx) > DEADZONE ? mx : 0f, Mathf.Abs(my) > DEADZONE ? my : 0f);

            if (Stick != Vector2.zero && !ReplayViewer.PadFlight)
            {
                GetCursorPos(out var c);
                float step = ControllerBindService.CursorSpeed * Time.unscaledDeltaTime;
                int nx = c.X + (int)Math.Round(Stick.x * step);
                int ny = c.Y - (int)Math.Round(Stick.y * step);
                SetCursorPos(nx, ny);
            }

            // right stick Y = scroll wheel. accumulate fractional deltas so slow tilts still scroll.
            bool scrolling = Mathf.Abs(sy) > DEADZONE;
            if (scrolling)
            {
                _scrollAccum += sy * ControllerBindService.ScrollSpeed * Time.unscaledDeltaTime;
                int notches = (int)_scrollAccum;
                if (notches != 0)
                {
                    _scrollAccum -= notches;
                    mouse_event(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)notches), IntPtr.Zero);
                }
            }
            else _scrollAccum = 0f;

            // A/cross = press-and-hold left mouse: down on press, up on release. that gives a plain
            // click on a quick tap, and a drag when you hold it and move the left stick.
            if (clickNow && !_leftHeld) { mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero); _leftHeld = true; AudioService.PlayControllerClick(); }
            else if (!clickNow && _leftHeld) { mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero); _leftHeld = false; }

            if (rightNow && !_rightHeld) { mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, IntPtr.Zero); _rightHeld = true; }
            else if (!rightNow && _rightHeld) { mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, IntPtr.Zero); _rightHeld = false; }

            // lock the pad whenever OUR full UI/wheel owns the screen. the PB tab is the exception —
            // it leaves the pad live so the top-bar bumpers can still change tabs.
            FGInputLockService.SetControllerLock(uiUp || wheelUp);
        }
    }
}
