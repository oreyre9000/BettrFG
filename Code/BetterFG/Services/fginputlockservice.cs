using System.Collections.Generic;
using FGClient;
using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;
using BetterFG.Utilities;

namespace BetterFG.Services
{
    // when any of our input fields is focused, disable rewired player maps
    // so wasd/jump don't leak into the game. leaves the UI input module alone
    // so our overlay clicks still work. Tick() must be called every frame
    // because the game re-enables maps under us.
    //
    // IMPORTANT: this must ONLY engage while typing in BettrFG's own UI. there are
    // two independent sources of "typing" in our ui and they must not stomp each
    // other, so each gets its own latch and we lock if either is set:
    //   - real unity InputFields owned by our canvas (driven by BetterFGUIMan,
    //     which verifies the selected field lives under our canvas so a focused
    //     Fall Guys input field never trips it)
    //   - our fake text fields (custom carets, no real InputField) driven by the tabs
    //   - CreativeTypeValueTweak, typing into the GAME's parameter node rather than our own ui
    public static class FGInputLockService
    {
        // kill switch for the whole background-input block (loading-screen lock, foreground check,
        // EventSystem/device disable) so a build can go out with none of it active for A/B testing.
        private const bool BackgroundGateEnabled = true;

        private static bool _realFieldLock;
        private static bool _fakeFieldLock;
        private static bool _editorUiLock;
        private static bool _screenLock;
        private static bool _controllerLock;
        private static bool _paramTypeLock;
        private static bool _loadingScreenLock;
        private static float _loadingScreenLockUntil;

        // Application.isFocused/Rewired's own focus tracking sticks "focused" from process start on
        // this build, so a boot that lands without real OS focus keeps servicing input regardless.
        // Ground-truth check against the actual foreground window catches that and plain alt-tab too.
        private static bool _outOfForegroundCached;
        private static int _foregroundFrame = -1;

        private static bool OutOfForeground
        {
            get
            {
                if (!BackgroundGateEnabled) return false;
                int frame = Time.frameCount;
                if (_foregroundFrame != frame)
                {
                    _foregroundFrame = frame;
                    _outOfForegroundCached = !Shell32Util.IsGameWindowForeground();
                }
                return _outOfForegroundCached;
            }
        }

        public static bool IsLocked => _realFieldLock || _fakeFieldLock || _editorUiLock || _screenLock || _controllerLock || _paramTypeLock || _loadingScreenLock || OutOfForeground;

        private static bool _musicMuted;

        // back-compat: callers that used SetLocked were the fake-field caret paths
        public static void SetLocked(bool locked) => _fakeFieldLock = locked;

        public static void SetFakeFieldLock(bool locked) => _fakeFieldLock = locked;

        public static void SetRealFieldLock(bool locked) => _realFieldLock = locked;

        // held while a number is being typed into a level editor parameter node
        public static void SetParamTypeLock(bool locked) => _paramTypeLock = locked;

        // locks the whole game input while our UI is up in the level editor — even when not
        // typing in a field — so editor hotkeys / movement don't fire under the open overlay.
        public static void SetEditorUiLock(bool locked) => _editorUiLock = locked;
        public static bool EditorUiLocked => _editorUiLock;

        // held while one of our fullscreen screens (bean creator, assets, replay) owns the display.
        // the game underneath is still alive and still listening, so without this its menu navigates
        // to whatever you press at our UI. own latch, not the editor one, so closing a screen can't
        // clear a lock the level editor overlay is holding.
        public static void SetScreenLock(bool locked) => _screenLock = locked;

        // locks game input while the player is steering our UI cursor with a controller, so the
        // same stick/buttons don't also drive menus or the camera underneath. ControllerManager
        // sets this on stick/button activity and clears it after a short idle.
        public static void SetControllerLock(bool locked) => _controllerLock = locked;

        // while ControllerManager moves the OS cursor with the stick, Rewired sees that synthetic
        // mouse motion and flips "last active controller" to Mouse every frame, fighting the stick —
        // which makes the game's nav-prompt glyphs strobe between pad and kbm. turning Rewired's Mouse
        // controller off keeps it from counting as active; our clicks/hover/scroll ride Unity's own
        // Input, not Rewired's Mouse, so nothing we do breaks.
        private static bool _rewiredMouseSuppressed;
        public static void SetRewiredMouseSuppressed(bool on) => _rewiredMouseSuppressed = on;

        // the game's own boot/menu loading veil doesn't stop clicks from reaching whatever's
        // already loaded underneath it, so a click during that window can land on a real menu
        // button (shop, etc). GameStatePatches pings this every LoadingScreenViewModel tick
        // while the general loading screen is up; no matching "hide" event exists so we just
        // let the lock expire if the pings stop for a moment.
        public static void RefreshLoadingScreenLock()
        {
            if (!BackgroundGateEnabled) return;
            _loadingScreenLock = true;
            _loadingScreenLockUntil = Time.unscaledTime + 0.3f;
        }

        // re-enable ONLY the maps we turned off, a blanket one force-ons maps the game meant to be off
        // and scrambles the bindings.
        private static readonly HashSet<int> _selfDisabled = new HashSet<int>();
        private static bool _selfWasLocked;
        // reused across ticks — the controller lock holds every frame the UI is up with a pad, and a
        // fresh interop list per player per frame was constant GC churn
        private static Il2CppSystem.Collections.Generic.List<ControllerMap> _mapsBuf;

        // movement/jump/etc also don't all go through mapped Actions — some read the Keyboard/Mouse
        // Controller directly (GetKey/GetButton), which ControllerMap.enabled=false never touches.
        // Controller itself has an enabled flag that kills raw polling too, so hit that as well.
        // kb tracked here; mouse gets its own flag since the cursor-suppress path drives it alone.
        private static bool _devicesDisabledByUs;
        private static bool _mouseDisabledByUs;

        public static void Tick()
        {
            bool outOfForeground = OutOfForeground;

            bool shouldMute = false;
            if (outOfForeground)
            {
                try { shouldMute = GlobalGameStateClient.Instance?.PlayerProfile?.AudioSettings?.MuteAudioOnFocusLost ?? false; } catch { }
            }
            if (shouldMute != _musicMuted)
            {
                _musicMuted = shouldMute;
                if (shouldMute)
                {
                    MenuMusicService.Pause();
                    BetterFG.Features.UnityRound.RoundMusicService.SetPaused(true);
                }
                else
                {
                    MenuMusicService.Resume();
                    BetterFG.Features.UnityRound.RoundMusicService.SetPaused(false);
                }
            }

            if (_loadingScreenLock && Time.unscaledTime > _loadingScreenLockUntil)
                _loadingScreenLock = false;

            bool blockBackground = _loadingScreenLock || outOfForeground;

            if (blockBackground && !_devicesDisabledByUs && Input.anyKey) blockBackground = false;

            bool locked = _realFieldLock || _fakeFieldLock || _editorUiLock || _screenLock || _controllerLock
                          || _paramTypeLock || _loadingScreenLock || outOfForeground;

            if (!locked && !blockBackground && !_selfWasLocked
                && !_devicesDisabledByUs && !_mouseDisabledByUs && !_rewiredMouseSuppressed)
                return;

            if (ReInput.isReady)
            {
                var kb = ReInput.controllers.Keyboard;
                var mouse = ReInput.controllers.Mouse;
                // keyboard: off only during a background block
                if (blockBackground)
                {
                    if (kb != null) kb.enabled = false;
                    _devicesDisabledByUs = true;
                }
                else if (_devicesDisabledByUs)
                {
                    if (kb != null) kb.enabled = true;
                    _devicesDisabledByUs = false;
                }
                // mouse: off during a background block OR while we're suppressing it for cursor-drive
                bool mouseOff = blockBackground || _rewiredMouseSuppressed;
                if (mouseOff)
                {
                    if (mouse != null) mouse.enabled = false;
                    _mouseDisabledByUs = true;
                }
                else if (_mouseDisabledByUs)
                {
                    if (mouse != null) mouse.enabled = true;
                    _mouseDisabledByUs = false;
                }
            }

            if (!ReInput.isReady) return;
            int n = ReInput.players.playerCount;

            // must run BEFORE we re-evaluate IsLocked: a typing lock starting the same frame a stick
            // lock ends would otherwise never restore, leaving those maps off for good (and any later
            // re-enable doubles the input path, so presses register twice).
            if (_selfWasLocked && !locked)
            {
                _selfWasLocked = false;
                if (_selfDisabled.Count > 0)
                {
                    for (int i = 0; i < n; i++)
                    {
                        var p = ReInput.players.GetPlayer(i);
                        if (p == null) continue;
                        var maps = _mapsBuf ??= new Il2CppSystem.Collections.Generic.List<ControllerMap>();
                        maps.Clear();
                        p.controllers.maps.GetAllMaps(maps);
                        for (int m = 0; m < maps.Count; m++)
                        {
                            var map = maps[m];
                            if (map != null && _selfDisabled.Contains(map.id))
                                map.enabled = true;
                        }
                    }
                    _selfDisabled.Clear();
                }
            }

            if (locked)
            {
                // fresh snapshot so we don't carry stale ids from a previous lock session
                if (!_selfWasLocked) _selfDisabled.Clear();

                for (int i = 0; i < n; i++)
                {
                    var p = ReInput.players.GetPlayer(i);
                    if (p == null) continue;
                    var maps = _mapsBuf ??= new Il2CppSystem.Collections.Generic.List<ControllerMap>();
                    maps.Clear();
                    p.controllers.maps.GetAllMaps(maps);
                    for (int m = 0; m < maps.Count; m++)
                    {
                        var map = maps[m];
                        if (map == null || !map.enabled) continue;
                        _selfDisabled.Add(map.id);
                        map.enabled = false;
                    }
                }
                _selfWasLocked = true;
                return;
            }
        }
    }
}
