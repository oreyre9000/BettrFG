using System;
using System.Collections.Generic;
using FallGuysLib.UI;
using FG.Common.CMS;
using FG.Common.UI;
using FGClient.UI.Core;
using MPG.Utility;
using Rewired;
using UnityEngine;
using BettrFG.uGUI;
using Events;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppAemList = Il2CppSystem.Collections.Generic.List<Rewired.ActionElementMap>;
using Il2CppNavPromptDict = Il2CppSystem.Collections.Generic.Dictionary<NavPrompt, Il2CppSystem.Action>;

namespace BetterFG.Core
{
    // shared core for "spawn an FG nav-prompt + listen for its input" — used by Leave-on-loading,
    // the qual-screen Set-As-PB prompt, and anything else we add later. before this every caller
    // was hand-rolling: clone NavigationPromptData, register a CMS string, find NavigationOverlayManager,
    // pull _navPromptPrefab, instantiate, anchor + position the RectTransform, set up auto-resize for
    // long labels, and walk every joystick's action-element-map to bypass disabled Rewired categories.
    //
    // The single entry point is NavPromptCore.From(...). Chain options, finish with SpawnOn(parent).
    // The returned NavPromptHandle is the source of truth: poll IsPressed (or set OnPressed), call
    // Destroy when done. Don't go around it.
    public static class NavPromptCore
    {
        // built-in glyph sources we know about. NavPrompt is the in-game enum; cast int for any
        // value not in the enum (Favourite = 22 etc).
        public const NavPrompt Favourite = (NavPrompt)22;

        public static NavPromptBuilder From(NavPrompt source) => new NavPromptBuilder(source);

        // shared CMS-strings table lookup. keys are registered once per process, value-or-overwrite.
        internal static void RegisterCmsString(string key, string value)
        {
            var strings = CMSLoader.Instance._localisedStrings._localisedStrings;
            if (!strings.ContainsKey(key)) strings.Add(key, value);
        }

        // overwrite variant. use only for values that legitimately change between reads (a popup
        // body that names the current item, for example). nav prompt labels must NOT go through
        // this - the manager caches them, churning the value there breaks the row.
        internal static void SetCmsString(string key, string value)
        {
            var strings = CMSLoader.Instance._localisedStrings._localisedStrings;
            strings[key] = value;
        }

        // clone cache so we don't churn NavigationPromptData instances across re-spawns. keyed by
        // (source-glyph, cms-key) since two callers using the same glyph but different labels are
        // distinct clones.
        private static readonly Dictionary<(NavPrompt, string), NavigationPromptData> _cloneCache
            = new Dictionary<(NavPrompt, string), NavigationPromptData>();

        private static NavigationOverlayManager _manager;

        internal static NavigationOverlayManager Manager
        {
            get
            {
                if (_manager != null) return _manager;
                foreach (var mgr in Resources.FindObjectsOfTypeAll<NavigationOverlayManager>())
                    if (mgr != null && mgr.gameObject.scene.IsValid()) { _manager = mgr; break; }
                return _manager;
            }
        }

        internal static NavigationPromptData GetOrCloneData(NavPrompt source, string labelKey, string labelText)
        {
            var key = (source, labelKey);
            if (_cloneCache.TryGetValue(key, out var existing) && existing != null) return existing;

            var mgr = Manager;
            if (mgr == null) return null;
            var dict = mgr._navPromptsDictionary;
            if (dict == null || !dict.TryGetValue(source, out var srcData) || srcData == null) return null;

            RegisterCmsString(labelKey, labelText);
            var clone = UnityEngine.Object.Instantiate(srcData);
            clone.LocalisationKey = labelKey;
            UnityEngine.Object.DontDestroyOnLoad(clone);
            _cloneCache[key] = clone;
            return clone;
        }

        public struct OverlayClaim
        {
            public NavPrompt Key;
            public string LabelKey;
            public string LabelText;
            public Action OnPressed;
            public int IconAction;
            public int IconCategory;
            // same shape as NavPromptInjection's postBuild - set when the source prompt has no
            // keyboard binding of its own (Favourite, notably), so the caller pins one itself via
            // NavPromptCore.ApplyOwnGlyph once the manager has actually built the button.
            public Action<NavigationPromptButton> PostBuild;
        }

        internal static void ClaimOverlayRow(NavPrompt key, string labelKey, string labelText, Action onPressed,
            int iconAction = -1, int iconCategory = -1)
            => ClaimOverlayRow(new OverlayClaim
            {
                Key = key, LabelKey = labelKey, LabelText = labelText, OnPressed = onPressed,
                IconAction = iconAction, IconCategory = iconCategory,
            });

        // claim the real overlay row the way every FG screen does: broadcast a NavPromptChanged.
        // NavigationOverlayManager handles it inline, switches SubMenuNavigation_Right on and builds
        // the real buttons itself - which is the ONLY thing that turns that row on, so parenting a
        // button into it without claiming can never work. multi-entry form so a single tweak can
        // own two adjacent prompts (respawn button + menu-opener) in one row.
        internal static void ClaimOverlayRow(params OverlayClaim[] claims)
        {
            var mgr = Manager;
            if (mgr?._navPromptsDictionary == null || claims == null || claims.Length == 0) return;

            RestoreInstalledData();

            var dict = new Il2CppNavPromptDict();
            foreach (var c in claims)
            {
                var clone = GetOrCloneData(c.Key, c.LabelKey, c.LabelText);
                if (clone != null && c.IconAction >= 0)
                {
                    clone.IconAction = c.IconAction;
                    clone.IconCategory = c.IconCategory;
                    // drop the source prompt's own actions so the game's button never self-fires on
                    // them (Report's action would otherwise trigger us). we read the press ourselves.
                    clone.InputActions = new Il2CppStructArray<int>(0);
                }

                // the clone has to STAY in the dictionary while we hold the row. the manager re-looks
                // the data up whenever it rebuilds the prompts - notably on every active-controller
                // change - so we restore the originals only when we release the row.
                if (clone != null && mgr._navPromptsDictionary.TryGetValue(c.Key, out var original) && original != null)
                {
                    _installedList.Add((c.Key, original));
                    mgr._navPromptsDictionary[c.Key] = clone;
                }

                dict[c.Key] = (Il2CppSystem.Action)c.OnPressed;
                ClaimedKeys.Add(c.Key);
            }

            Broadcaster.Instance?.Broadcast(new NavPromptChanged(dict));

            // Broadcast runs its handlers synchronously, so the manager has already built the row's
            // buttons by the time we get here - same button-by-LocalisationKey lookup
            // NavPromptInjection's postfix uses.
            var parent = mgr._navPromptsParent;
            if (parent == null) return;
            foreach (var c in claims)
            {
                if (c.PostBuild == null) continue;
                for (int i = 0; i < parent.childCount; i++)
                {
                    var btn = parent.GetChild(i).GetComponent<NavigationPromptButton>();
                    var label = btn != null ? btn._localisedStaticLabel : null;
                    if (label != null && label.Key == c.LabelKey)
                    {
                        c.PostBuild(btn);
                        _pinnedButtons.Add(btn);
                        break;
                    }
                }
            }
        }

        private static readonly List<(NavPrompt key, NavigationPromptData original)> _installedList
            = new List<(NavPrompt, NavigationPromptData)>();
        internal static readonly HashSet<NavPrompt> ClaimedKeys = new HashSet<NavPrompt>();
        // buttons whose glyphs we pinned via PostBuild. NavigationOverlayManager pools/reuses these
        // GameObjects across rebuilds - when the row goes back to a game screen, the same button
        // gets a new prompt but keeps whatever _mappeable=false / notMappeableSprite we set here.
        // reset them on release/yield so the reassigned button rebuilds glyphs from its RewiredAction.
        private static readonly List<NavigationPromptButton> _pinnedButtons = new List<NavigationPromptButton>();

        private static void ResetPinnedGlyphs()
        {
            if (_pinnedButtons.Count == 0) return;
            foreach (var btn in _pinnedButtons)
            {
                if (btn == null) continue;
                var ctrl = btn.TryCast<NavigationPromptButtonController>();
                var acu = ctrl != null ? ctrl._activeControllerUI : null;
                if (acu == null) continue;
                acu._mappeable = true;
                acu.UpdateGlyphsWithActiveController();
            }
            _pinnedButtons.Clear();
        }

        private static void RestoreInstalledData()
        {
            ResetPinnedGlyphs();
            ClaimedKeys.Clear();
            if (_installedList.Count == 0) return;
            var dict = Manager?._navPromptsDictionary;
            if (dict != null)
                foreach (var (k, orig) in _installedList) dict[k] = orig;
            _installedList.Clear();
        }

        // give the prompt slot's real data back but leave the row alone, for when another screen has
        // just claimed it - broadcasting an empty set here would wipe the prompts it only just put up.
        internal static void YieldOverlayRow() => RestoreInstalledData();

        // full teardown: hand the data back AND clear the row. only for when we're actually done
        // (round over, tweak switched off), never to step aside for another screen.
        internal static void ReleaseOverlayRow()
        {
            RestoreInstalledData();
            Broadcaster.Instance?.Broadcast(new NavPromptChanged());
        }

        // true while the row is switched on. used to notice another screen has taken it back off us.
        internal static bool OverlayRowActive
        {
            get
            {
                var parent = Manager?._navPromptsParent;
                return parent != null && parent.gameObject.activeInHierarchy;
            }
        }

        private static readonly Il2CppAemList _sharedAemBuf = new Il2CppAemList();

        // one copy of the "did this action fire, even with its category disabled" walk. the plain
        // GetButtonDown only reports while the action's input category is enabled - during loading
        // and gameplay the Menu category isn't - so fall through to walking each joystick's own
        // button maps. the element filter exists because the same elementIdentifierId is Circle on
        // one layout and Cross on another.
        internal static bool PollActionDirect(int actionId, Func<string, bool> elementFilter, bool joystickOnly)
        {
            if (!ReInput.isReady || ReInput.players.playerCount == 0) return false;
            var p = ReInput.players.GetPlayer(0);

            if (elementFilter == null && !joystickOnly && p.GetButtonDown(actionId)) return true;

            var sticks = p.controllers.Joysticks;
            int n = p.controllers.joystickCount;
            for (int i = 0; i < n; i++)
            {
                var j = sticks[i];
                if (j == null || !j.GetAnyButtonDown()) continue;
                _sharedAemBuf.Clear();
                int got = p.controllers.maps.GetButtonMapsWithAction(j, actionId, false, _sharedAemBuf);
                int layout = -1;
                for (int k = 0; k < got; k++)
                {
                    var aem = _sharedAemBuf[k];
                    if (aem == null) continue;
                    var cm = aem.controllerMap;
                    if (cm != null)
                    {
                        if (layout < 0) layout = cm.layoutId;
                        else if (cm.layoutId != layout) continue;
                    }
                    if (!j.GetButtonDownById(aem.elementIdentifierId)) continue;
                    if (elementFilter != null)
                    {
                        string elName = null;
                        try { elName = j.GetElementIdentifierById(aem.elementIdentifierId)?.name; } catch { }
                        if (!elementFilter(elName)) continue;
                    }
                    return true;
                }
            }

            if (joystickOnly) return false;
            var kb = p.controllers.Keyboard;
            if (kb == null || !kb.GetAnyButtonDown()) return false;
            _sharedAemBuf.Clear();
            int kgot = p.controllers.maps.GetButtonMapsWithAction(kb, actionId, false, _sharedAemBuf);
            int kbLayout = -1;
            for (int k = 0; k < kgot; k++)
            {
                var aem = _sharedAemBuf[k];
                if (aem == null) continue;
                var cm = aem.controllerMap;
                if (cm != null)
                {
                    if (kbLayout < 0) kbLayout = cm.layoutId;
                    else if (cm.layoutId != kbLayout) continue;
                }
                if (kb.GetButtonDownById(aem.elementIdentifierId)) return true;
            }
            return false;
        }

        internal static Sprite KeyboardGlyph(KeyCode key)
        {
            if (key == KeyCode.None || !ReInput.isReady) return null;
            var sm = SingletonBehaviour<SpriteManager>.Instance;
            var cg = sm != null ? sm._controllerGlyphs : null;
            if (cg == null || cg.Keyboard == null) return null;
            var kb = ReInput.controllers.Keyboard;
            var ident = kb != null ? kb.GetElementIdentifierByKeyCode(key) : null;
            if (ident == null) return null;
            var info = cg.Keyboard.GetGlyph(ident.id, AxisRange.Full);
            return info != null ? info.sprite : null;
        }

        // pin custom keyboard + pad glyphs on the ActiveControllerUI of an already-built button.
        // Same shape as .OwnGlyph() from the raw-spawn path, hoisted so the injection path can reuse
        // it in a post-build callback. Either sprite null = leave that side alone.
        public static void ApplyOwnGlyph(NavigationPromptButton btn, KeyCode kbKey, int padActionId)
            => ApplyGlyph(btn, KeyboardGlyph(kbKey), padActionId >= 0 ? ResolvePadGlyph(SingletonBehaviour<SpriteManager>.Instance, padActionId) : null);

        // element-id variant: skips the action lookup entirely, resolves the pad glyph directly off
        // whichever joystick is connected. use when we own the input ourselves (raw element poll)
        // instead of piggybacking on a game action.
        public static void ApplyOwnGlyphByElement(NavigationPromptButton btn, KeyCode kbKey, int padElementId)
        {
            Sprite pad = null;
            if (padElementId >= 0 && ReInput.isReady && ReInput.players.playerCount > 0)
            {
                var p = ReInput.players.GetPlayer(0);
                var sticks = p.controllers.Joysticks;
                for (int i = 0; i < p.controllers.joystickCount && pad == null; i++)
                    pad = JoystickElementGlyph(sticks[i], padElementId);
            }
            ApplyGlyph(btn, KeyboardGlyph(kbKey), pad);
        }

        internal static int ResolveElementIdByName(Joystick j, params string[] names)
        {
            if (j == null) return -1;
            var ids = j.ButtonElementIdentifiers;
            int n = j.buttonCount;
            for (int b = 0; b < n; b++)
            {
                var id = ids[b];
                if (id == null) continue;
                foreach (var name in names)
                    if (id.name == name) return id.id;
            }
            return -1;
        }

        internal static int CurrentPadElementByName(params string[] names)
        {
            if (!ReInput.isReady || ReInput.players.playerCount == 0) return -1;
            var p = ReInput.players.GetPlayer(0);
            var sticks = p.controllers.Joysticks;
            for (int i = 0; i < p.controllers.joystickCount; i++)
            {
                int id = ResolveElementIdByName(sticks[i], names);
                if (id >= 0) return id;
            }
            return -1;
        }

        internal static bool ElementDownByName(params string[] names)
        {
            if (!ReInput.isReady || ReInput.players.playerCount == 0) return false;
            var p = ReInput.players.GetPlayer(0);
            var sticks = p.controllers.Joysticks;
            for (int i = 0; i < p.controllers.joystickCount; i++)
            {
                var j = sticks[i];
                if (j == null || !j.GetAnyButtonDown()) continue;
                int id = ResolveElementIdByName(j, names);
                if (id >= 0 && j.GetButtonDownById(id)) return true;
            }
            return false;
        }

        internal static bool ElementHeldByName(params string[] names)
        {
            if (!ReInput.isReady || ReInput.players.playerCount == 0) return false;
            var p = ReInput.players.GetPlayer(0);
            var sticks = p.controllers.Joysticks;
            for (int i = 0; i < p.controllers.joystickCount; i++)
            {
                var j = sticks[i];
                if (j == null) continue;
                int id = ResolveElementIdByName(j, names);
                if (id >= 0 && j.GetButtonById(id)) return true;
            }
            return false;
        }

        internal static int ResolveAxisIdByName(Joystick j, params string[] names)
        {
            if (j == null) return -1;
            var ids = j.AxisElementIdentifiers;
            int n = j.axisCount;
            foreach (var name in names)
                for (int a = 0; a < n; a++)
                {
                    var id = ids[a];
                    if (id != null && id.name == name) return id.id;
                }
            return -1;
        }

        // rewired reports a stick axis in its own orientation per hardware layout, and GetAxisById
        // already applies the layout's calibration/invert — so this is the only read that agrees
        // across pads. raw GetAxis(index) does not.
        internal static float AxisByName(params string[] names)
        {
            if (!ReInput.isReady || ReInput.players.playerCount == 0) return 0f;
            var p = ReInput.players.GetPlayer(0);
            var sticks = p.controllers.Joysticks;
            for (int i = 0; i < p.controllers.joystickCount; i++)
            {
                var j = sticks[i];
                if (j == null) continue;
                int id = ResolveAxisIdByName(j, names);
                if (id >= 0)
                {
                    float v = j.GetAxisById(id);
                    if (Mathf.Abs(v) > 0.001f) return v;
                }
            }
            return 0f;
        }

        private static void ApplyGlyph(NavigationPromptButton btn, Sprite kbSprite, Sprite padSprite)
        {
            var ctrl = btn.TryCast<NavigationPromptButtonController>();
            var acu = ctrl != null ? ctrl._activeControllerUI : null;
            if (acu == null) return;
            acu._mappeable = false;
            if (kbSprite != null) acu._notMappeableKeyboardSprite = kbSprite;
            if (padSprite != null) acu._notMappeableJoystickSprite = padSprite;
            acu._notMappeableKeyboardGlyphText = "";
            acu._notMappeableJoystickGlyphText = "";
            if (acu._text != null) UGUIShip.RelabelText(acu._text, "");
            acu.UpdateGlyphsWithActiveController();
        }

        // ask SpriteManager for the polled action's glyph ON EACH JOYSTICK explicitly. the overload
        // without a Controller resolves for the ACTIVE device, so on keyboard it returns "Esc" and
        // that gets pinned as the pad sprite — pass the joystick so it resolves the pad binding.
        internal static Sprite ResolvePadGlyph(SpriteManager sm, int actionId)
        {
            if (actionId < 0 || !ReInput.isReady || ReInput.players.playerCount == 0) return null;
            var action = ReInput.mapping.GetAction(actionId);
            if (action == null) return null;

            var p = ReInput.players.GetPlayer(0);
            var sticks = p.controllers.Joysticks;
            int n = p.controllers.joystickCount;
            var buf = new Il2CppAemList();
            for (int i = 0; i < n; i++)
            {
                var j = sticks[i];
                buf.Clear();
                int got = p.controllers.maps.GetButtonMapsWithAction(j, actionId, false, buf);
                if (got == 0) continue;
                int layoutId = 0;
                for (int k = 0; k < got; k++)
                {
                    var cm = buf[k] != null ? buf[k].controllerMap : null;
                    if (cm != null) { layoutId = cm.layoutId; break; }
                }
                var info = sm.GetActionControllerSprite(j, action, layoutId, Pole.Positive, false);
                if (info != null && info.sprite != null) return info.sprite;
            }
            return null;
        }

        internal static Sprite JoystickElementGlyph(Joystick j, int elementId)
        {
            if (j == null || elementId < 0) return null;
            var sm = SingletonBehaviour<SpriteManager>.Instance;
            var cg = sm != null ? sm._controllerGlyphs : null;
            if (cg == null) return null;
            try
            {
                string msg = null;
                var info = cg.GetGlyphFromHardwareGuid(j.hardwareTypeGuid, j.hardwareName,
                    ControllerType.Joystick, elementId, AxisRange.Full, ref msg);
                return info != null ? info.sprite : null;
            }
            catch { return null; }
        }

        internal static GameObject GetPromptPrefab()
        {
            var mgr = Manager;
            if (mgr == null) return null;
            var prefab = mgr._navPromptPrefab;
            return prefab != null ? prefab.gameObject : null;
        }

        // shared full-screen container under the game's UICanvas_Client_V2 root. lives there so
        // every prompt parented to it inherits the canvas scaleFactor BettrFG sets. recreated per
        // scene since the canvas itself is re-instantiated; find-or-create.
        internal static Transform GetCustomNavPromptRoot()
        {
            var canvas = GameObject.Find("UICanvas_Client_V2(Clone)");
            if (canvas == null) return null;

            var existing = canvas.transform.Find("CustomNavPrompt");
            if (existing != null) return existing;

            var go = new GameObject("CustomNavPrompt");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(canvas.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetAsLastSibling();
            return rt;
        }
    }

    // anchor presets for the four corners + center-bottom. callers can override with raw anchor/
    // pivot/offset on the builder if they need something exotic.
    public enum NavPromptAnchor
    {
        BottomRight,
        BottomLeft,
        BottomCenter,
        TopRight,
        TopLeft,
        Custom,
    }

    public sealed class NavPromptBuilder
    {
        internal readonly NavPrompt Source;
        internal string LabelText = "Action";
        internal string LabelKey;
        internal NavPromptAnchor Anchor = NavPromptAnchor.BottomRight;
        internal Vector2? Offset;       // null = use the anchor's default edge margin
        internal Vector2? CustomAnchorMin;
        internal Vector2? CustomAnchorMax;
        internal Vector2? CustomPivot;
        internal Action OnPressed;
        internal bool OwnCanvas;       // spawn under the shared CustomNavPrompt container on the game UICanvas
        internal bool ResizeForLongLabel = true;
        internal float Width = 360f;
        internal bool AcceptEscapeKey;          // also fires when keyboard Escape is pressed
        // default on: ignore presses while the top surface on UICanvas_Client_V2/Default isn't
        // focused (menu open, popup up, etc.). opt out with .AllowWhileUnfocused() for prompts
        // that need to fire from a covering UI (LeaveOnLoadingScreen etc).
        internal bool RequireGameplayFocus = true;
        internal Func<string, bool> ElementNameFilter; // optional gate: only accept buttons whose Rewired name matches
        internal int[] PollActionIdOverride;    // poll these Rewired action ids instead of the NavigationPromptData's own
        internal bool InGameOverlayParent;      // parent the button under the game's NavigationOverlay _navPromptsParent
        internal KeyCode ExtraKey = KeyCode.None; // also fires on this key (read via the Rewired keyboard)
        internal bool UseOwnGlyph;              // swap the button's glyph for BettrFG's own key/pad pair
        internal bool JoystickPollOnly;         // PollActions: only the joystick element walk, not keyboard/any-device
        internal BfgBind Bound;

        internal NavPromptBuilder(NavPrompt source) { Source = source; }

        public NavPromptBuilder Bind(BfgBind bind)
        {
            Bound = bind;
            PollActionIdOverride = new[] { bind.ActionId };
            return this;
        }

        // labelKey doubles as both the CMS dictionary key (must be unique per text) and the cache key.
        // If you reuse a key, you reuse the cloned data — fine for the same label, surprising otherwise.
        public NavPromptBuilder WithLabel(string text, string cmsKey)
        {
            LabelText = text;
            LabelKey = cmsKey;
            return this;
        }

        // offset optional — leave it null and the anchor's default edge margin is used.
        public NavPromptBuilder AnchoredAt(NavPromptAnchor anchor, Vector2? offset = null)
        {
            Anchor = anchor;
            Offset = offset;
            return this;
        }

        // raw RectTransform setup for callers who need something the presets don't cover.
        public NavPromptBuilder CustomAnchors(Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 offset)
        {
            Anchor = NavPromptAnchor.Custom;
            CustomAnchorMin = anchorMin;
            CustomAnchorMax = anchorMax;
            CustomPivot = pivot;
            Offset = offset;
            return this;
        }

        public NavPromptBuilder OnPress(Action cb) { OnPressed = cb; return this; }

        // spawn under the shared CustomNavPrompt container on the game's UICanvas instead of the
        // caller-provided parent, so the prompt inherits BettrFG's canvas scaling and survives the
        // UI it floats over (e.g. the leave prompt during loading screens).
        public NavPromptBuilder OnOwnCanvas()
        {
            OwnCanvas = true;
            return this;
        }

        public NavPromptBuilder Width_(float width) { Width = width; return this; }

        public NavPromptBuilder NoAutoResize() { ResizeForLongLabel = false; return this; }

        public NavPromptBuilder AlsoAcceptEscape() { AcceptEscapeKey = true; return this; }

        // parent the spawned button under the game's own NavigationOverlayManager._navPromptsParent
        // (inside Prefab_UI_NavigationOverlay(Clone)), so it sits in the real prompt row instead of
        // our floating CustomNavPrompt container. the game's layout group owns placement, so the
        // anchor + auto-resize options are skipped in this mode.
        public NavPromptBuilder InGameOverlay() { InGameOverlayParent = true; return this; }

        // also fire when this key is pressed. read through the Rewired keyboard (KeybindService),
        // never UnityEngine.Input — legacy Input goes deaf on us intermittently.
        public NavPromptBuilder AlsoAcceptKey(KeyCode key) { ExtraKey = key; return this; }

        // pull the glyph straight from the game's atlas by ELEMENT rather than by action: the O-key
        // sprite (from AlsoAcceptKey) when on keyboard, the pad button bound to the first PollActions
        // id when on a controller. use when the trigger action has no nav glyph of its own / resolves
        // to the wrong key (Default_OpenInGameMenu is Escape on keyboard).
        public NavPromptBuilder OwnGlyph() { UseOwnGlyph = true; return this; }

        // with PollActions: only walk each joystick's button maps — skip the any-device
        // GetButtonDown and the keyboard maps. needed when the polled action is bound to a keyboard
        // key you don't want firing the prompt (Default_OpenInGameMenu is Escape on keyboard).
        public NavPromptBuilder JoystickOnly() { JoystickPollOnly = true; return this; }

        // opt out of the default focus gate. only for prompts that must fire while another surface
        // has focus (loading-screen leave prompt etc).
        public NavPromptBuilder AllowWhileUnfocused() { RequireGameplayFocus = false; return this; }

        // filter joystick button presses by Rewired element name. used by Leave-on-loading to reject
        // X-on-PS (whose elementIdentifierId collides with B-on-Xbox across layouts) and only accept
        // Circle / B / generic-Button-1.
        public NavPromptBuilder FilterElement(Func<string, bool> predicate)
        {
            ElementNameFilter = predicate;
            return this;
        }

        // poll these Rewired action ids instead of the NavigationPromptData's own. needed when the
        // glyph's source data doesn't map cleanly to a controller binding in the disabled-category
        // poll path — e.g. NavPrompt.Back's InputActions don't include Menu_UICancel, so on
        // controller no joystick map fires. callers know which action they actually want.
        public NavPromptBuilder PollActions(params int[] actionIds)
        {
            PollActionIdOverride = actionIds;
            return this;
        }

        public NavPromptHandle SpawnOn(Transform parent)
        {
            if (string.IsNullOrEmpty(LabelKey))
                LabelKey = "bfg_navprompt_" + LabelText.ToLowerInvariant().Replace(' ', '_');

            var data = NavPromptCore.GetOrCloneData(Source, LabelKey, LabelText);
            if (data == null) return null;
            if (Bound != null)
            {
                data.IconAction = Bound.ActionId;
                data.IconCategory = RewiredConsts.Category.Default;
                data.InputActions = new Il2CppStructArray<int>(0);
            }

            var prefab = NavPromptCore.GetPromptPrefab();
            if (prefab == null) return null;

            Transform actualParent = parent;
            if (InGameOverlayParent)
            {
                var mgr = NavPromptCore.Manager;
                actualParent = mgr != null ? mgr._navPromptsParent : null;
                // an inactive row means anything Instantiate'd under it starts inactive too and
                // skips Awake() before Init() runs below, NREing inside the game's own label code.
                if (actualParent == null || !actualParent.gameObject.activeInHierarchy) return null;

                string targetName = "BettrFG_NavPrompt_" + LabelKey;
                for (int i = actualParent.childCount - 1; i >= 0; i--)
                {
                    var child = actualParent.GetChild(i);
                    if (child != null && child.name == targetName)
                        UnityEngine.Object.Destroy(child.gameObject);
                }
            }
            else if (OwnCanvas)
            {
                // sit under the game's own UICanvas so we inherit its scaleFactor (BettrFG canvas
                // scaling lives there). A standalone overlay canvas would ignore that and stay at
                // stock size. CustomNavPrompt is a shared stretch-to-fill container we create once.
                actualParent = NavPromptCore.GetCustomNavPromptRoot();
                if (actualParent == null) return null;
            }

            var go = UnityEngine.Object.Instantiate(prefab, actualParent);
            go.name = "BettrFG_NavPrompt_" + LabelKey;

            var rt = go.GetComponent<RectTransform>();
            if (rt != null && !InGameOverlayParent) ApplyAnchors(rt);

            if (ResizeForLongLabel && rt != null && !InGameOverlayParent)
            {
                rt.sizeDelta = new Vector2(Width, rt.sizeDelta.y);
                foreach (var le in go.GetComponentsInChildren<UnityEngine.UI.LayoutElement>(true))
                {
                    le.minWidth = -1f;
                    le.preferredWidth = -1f;
                }
                foreach (var tmp in go.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                {
                    tmp.enableWordWrapping = false;
                    tmp.overflowMode = TMPro.TextOverflowModes.Overflow;
                }
            }

            // hand the button's own callback a flag flip — NavigationPromptButton wires Rewired
            // resolution internally, so whatever binding the glyph shows on THIS controller layout is
            // the same input it fires on. we set _gamePressed; the handle drains it in IsPressed().
            // ALSO stash the button on the handle so IsPressed can read its live elementIdentifierId
            // and poll that raw controller button directly — needed for glyphs like LE_Down whose
            // NavigationPromptData has no InputActions so the callback never fires on its own.
            var handle = new NavPromptHandle(go, data, OnPressed, AcceptEscapeKey, ElementNameFilter, PollActionIdOverride, RequireGameplayFocus, ExtraKey, JoystickPollOnly);
            var btn = go.GetComponent<NavigationPromptButton>();
            if (btn != null) btn.Init(data, (Il2CppSystem.Action)(() => handle.MarkGamePressed()));
            handle.AttachPromptButton(btn);

            if (UseOwnGlyph && btn != null) ApplyOwnGlyph(btn);

            go.SetActive(true);

            return handle;
        }

        // resolve the real per-element glyphs from the game's SpriteManager atlas and pin them as
        // the ActiveControllerUI's fixed (non-mappeable) sprites. _mappeable=false makes the ACU
        // pick the keyboard vs joystick one by active device on its own.
        private void ApplyOwnGlyph(NavigationPromptButton btn)
            => NavPromptCore.ApplyOwnGlyph(btn, ExtraKey,
                PollActionIdOverride != null && PollActionIdOverride.Length > 0 ? PollActionIdOverride[0] : -1);


        private void ApplyAnchors(RectTransform rt)
        {
            if (Anchor == NavPromptAnchor.Custom && CustomAnchorMin.HasValue)
            {
                rt.anchorMin = CustomAnchorMin.Value;
                rt.anchorMax = CustomAnchorMax.Value;
                rt.pivot = CustomPivot.Value;
                rt.anchoredPosition = Offset ?? Vector2.zero;
                return;
            }
            // default edge margin per anchor when the caller didn't pass an offset. sign points
            // inward from whichever corner/edge the anchor sits on.
            const float M = 70f;
            Vector2 a; Vector2 p; Vector2 def;
            switch (Anchor)
            {
                case NavPromptAnchor.BottomLeft:   a = new Vector2(0f, 0f);   p = new Vector2(0f, 0f);   def = new Vector2( M,  M); break;
                case NavPromptAnchor.BottomCenter: a = new Vector2(0.5f, 0f); p = new Vector2(0.5f, 0f); def = new Vector2( 0f,  M); break;
                case NavPromptAnchor.TopRight:     a = new Vector2(1f, 1f);   p = new Vector2(1f, 1f);   def = new Vector2(-M, -M); break;
                case NavPromptAnchor.TopLeft:      a = new Vector2(0f, 1f);   p = new Vector2(0f, 1f);   def = new Vector2( M, -M); break;
                default:                           a = new Vector2(1f, 0f);   p = new Vector2(1f, 0f);   def = new Vector2(-M,  M); break; // BottomRight
            }
            rt.anchorMin = a;
            rt.anchorMax = a;
            rt.pivot = p;
            rt.anchoredPosition = Offset ?? def;
        }
    }

    // handle is the source of truth for the spawned prompt. callers either poll IsPressed each
    // frame, or set OnPress at build time and let the handle invoke it via Tick().
    public sealed class NavPromptHandle
    {
        public GameObject GameObject { get; private set; }
        private readonly NavigationPromptData _data;
        private readonly Action _onPressed;
        private readonly bool _acceptEscape;
        private readonly Func<string, bool> _elementFilter;
        private readonly int[] _pollActionIds;
        private readonly bool _requireGameplayFocus;
        private readonly KeyCode _extraKey;
        private readonly bool _joystickPollOnly;

        internal NavPromptHandle(GameObject go, NavigationPromptData data,
            Action onPressed, bool acceptEscape, Func<string, bool> elementFilter, int[] pollActionIds,
            bool requireGameplayFocus, KeyCode extraKey, bool joystickPollOnly)
        {
            GameObject = go;
            _data = data;
            _onPressed = onPressed;
            _acceptEscape = acceptEscape;
            _elementFilter = elementFilter;
            _pollActionIds = pollActionIds;
            _requireGameplayFocus = requireGameplayFocus;
            _extraKey = extraKey;
            _joystickPollOnly = joystickPollOnly;
        }

        // true when whatever surface currently owns input on the UI canvas is focused. shared
        // across every prompt in the process — GameObject.Find + GetComponentInChildren are heap
        // scans, so doing them per-handle per-frame builds cost linearly with prompt count. cache
        // the resolved refs and re-resolve only when they've been destroyed. we also throttle to
        // once per frame via Time.frameCount.
        private static int _focusCachedFrame = -1;
        private static bool _focusCachedResult;
        private static GameObject _focusBanners;
        private static GameObject _focusScoring;
        private static readonly Transform[] _focusRootTransforms = new Transform[2];
        private static readonly FocusableViewModel[] _focusVms = new FocusableViewModel[2];
        private static readonly string[] _focusRootPaths = { "UICanvas_Client_V2(Clone)/Default", "UICanvas_Client_V2(Clone)/LoadingScreen" };

        private bool GameplayFocused()
        {
            int f = Time.frameCount;
            if (f == _focusCachedFrame) return _focusCachedResult;
            _focusCachedFrame = f;
            _focusCachedResult = ComputeFocused();
            return _focusCachedResult;
        }

        // exposed so callers outside a NavPromptHandle (free-cam movement, etc.) can gate raw
        // input on the same "is the gameplay view actually focused" check the prompts use.
        internal static bool ComputeFocused()
        {
            // BannersState = elimination/qualification banner is playing. game is fully in gameplay
            // input mode here regardless of what any FocusableViewModel says. short-circuit true.
            if (_focusBanners == null)
                _focusBanners = GameObject.Find("UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates/BannersState");
            if (_focusBanners != null && _focusBanners.activeInHierarchy) return true;

            // spectating: the scoring-feedback surface owns focus, and its own FocusableViewModel
            // isn't the one the child walk below latches onto, so prompts got gated out
            if (_focusScoring == null)
                _focusScoring = GameObject.Find("UICanvas_Client_V2(Clone)/Default/Generic_UI_ScoringFeedback(Clone)");
            if (_focusScoring != null && _focusScoring.activeInHierarchy) return true;

            for (int r = 0; r < _focusRootPaths.Length; r++)
            {
                // cached FocusableViewModel is the winner from a prior frame — if it's still alive,
                // reuse it. cheaper than re-walking children every frame.
                var vm = _focusVms[r];
                if (vm != null) return vm._isInFocus;

                var root = _focusRootTransforms[r];
                if (root == null)
                {
                    var go = GameObject.Find(_focusRootPaths[r]);
                    if (go == null) continue;
                    root = _focusRootTransforms[r] = go.transform;
                }

                for (int i = 0; i < root.childCount; i++)
                {
                    vm = root.GetChild(i).GetComponentInChildren<FocusableViewModel>(false);
                    if (vm != null) { _focusVms[r] = vm; return vm._isInFocus; }
                }
            }
            return false;
        }

        public bool IsAlive => GameObject != null;

        // NavigationPromptButton's own Init callback pokes this; IsPressed drains it once. that way
        // whatever binding the glyph resolves to on the current controller layout fires us — no
        // guessing at action ids, no falling out when a NavPrompt like LE_Down has no InputActions.
        private bool _gamePressed;
        internal void MarkGamePressed() { _gamePressed = true; }

        // the live NavigationPromptButton — we read its current elementIdentifierId every frame so
        // the raw-element poll follows controller layout swaps without stale ids.
        private NavigationPromptButton _promptButton;
        internal void AttachPromptButton(NavigationPromptButton btn) { _promptButton = btn; }

        // poll once. returns true if the prompt's action fired this frame. fires _onPressed too
        // if one was attached at build time.
        public bool IsPressed()
        {
            if (!IsAlive) return false;
            // still drain _gamePressed so a stale press from an unfocused frame doesn't fire the
            // instant focus returns; we just discard the result.
            bool gated = _requireGameplayFocus && !GameplayFocused();
            if (_gamePressed) { _gamePressed = false; if (gated) return false; _onPressed?.Invoke(); return true; }
            if (gated) return false;
            if (_acceptEscape && Input.GetKeyDown(KeyCode.Escape)) { _onPressed?.Invoke(); return true; }
            if (_extraKey != KeyCode.None && BetterFG.Services.KeybindService.KeyDown(_extraKey)) { _onPressed?.Invoke(); return true; }
            if (PollData()) { _onPressed?.Invoke(); return true; }
            return false;
        }

        // poll the actions declared on the NavigationPromptData so whatever glyph is currently
        // showing (keyboard or controller) is exactly what we accept here. survives a disabled
        // Rewired UI category by walking GetButtonMapsWithAction directly on each joystick + keyboard.
        // when an element-name filter is set we only accept presses on buttons whose Rewired name
        // matches — needed because across controller layouts the same elementIdentifierId means
        // Circle on one and Cross on another, and "Menu_UICancel" is bound to both.
        private bool PollData()
        {
            int[] actions = _pollActionIds;
            if (actions == null || actions.Length == 0)
            {
                var dataActions = _data?.InputActions;
                if (dataActions == null || dataActions.Length == 0) return false;
                for (int a = 0; a < dataActions.Length; a++)
                    if (NavPromptCore.PollActionDirect(dataActions[a], _elementFilter, _joystickPollOnly)) return true;
                return false;
            }
            for (int a = 0; a < actions.Length; a++)
                if (NavPromptCore.PollActionDirect(actions[a], _elementFilter, _joystickPollOnly)) return true;
            return false;
        }

        public void Destroy()
        {
            // only kill our own prompt — the CustomNavPrompt container is shared, leave it.
            if (GameObject != null) UnityEngine.Object.Destroy(GameObject);
            GameObject = null;
        }
    }

    // adds one of our own entries to whatever prompt set the current screen has broadcast, by
    // prefixing NavigationOverlayManager.UpdateNavPrompts. the manager then builds the button
    // itself and tracks it in its own list the same as every game-authored prompt — no orphan
    // sibling under _navPromptsParent, no fighting the screen's own layout. Add/Remove triggers a
    // rebroadcast of the last game-authored dict so the manager rebuilds the row.
    //
    // callers should use a NavPrompt enum value they own (200+), not one the game itself uses,
    // so two features can inject simultaneously without stomping each other. Pass a data clone
    // (via NavPromptInjection.BuildData) so the manager has something to look up for our key;
    // pass a postBuild callback to apply glyph overrides once the manager has built our button.
    public static class NavPromptInjection
    {
        // reserved range for BettrFG-owned NavPrompt keys. The game's enum values sit below this.
        public const NavPrompt CopyCode = (NavPrompt)200;
        public const NavPrompt LevelPort = (NavPrompt)201;
        public const NavPrompt PasteCode = (NavPrompt)202;
        public const NavPrompt RandomShow = (NavPrompt)203;
        public const NavPrompt IntroCamExit = (NavPrompt)204;
        public const NavPrompt EmoticonManage = (NavPrompt)205;

        private sealed class Entry
        {
            public NavPrompt Key;
            public Action Callback;
            public NavigationPromptData Data;
            public Action<NavigationPromptButton> PostBuild;
        }

        private static readonly Dictionary<NavPrompt, Entry> _injected = new Dictionary<NavPrompt, Entry>();
        private static Il2CppNavPromptDict _pristine;
        private static bool _pristineSeen;
        // buttons we've pinned custom glyphs on, and which entry each was pinned for. the manager
        // pools these GameObjects, so the same button can come back owned by a DIFFERENT prompt —
        // ours or the game's — and has to be re-pinned or reset instead of keeping stale sprites.
        private static readonly Dictionary<NavigationPromptButton, NavPrompt> _ownedButtons
            = new Dictionary<NavigationPromptButton, NavPrompt>();

        // build a fresh data clone off a game prefab source, retargeted with a custom label +
        // Rewired action mapping (IconAction/IconCategory drive the built-in glyph). InputActions
        // is emptied so the game's button never self-fires — we poll the press ourselves.
        public static NavigationPromptData BuildData(NavPrompt prefabSource, string label, string labelKey,
            int iconAction, int iconCategory)
        {
            var data = NavPromptCore.GetOrCloneData(prefabSource, labelKey, label);
            if (data != null)
            {
                data.IconAction = iconAction;
                data.IconCategory = iconCategory;
                data.InputActions = new Il2CppStructArray<int>(0);
            }
            return data;
        }

        public static void Add(NavPrompt key, Action cb,
            NavigationPromptData data = null,
            Action<NavigationPromptButton> postBuild = null)
        {
            _injected.Remove(key);

            var mgr = NavPromptCore.Manager;
            if (data != null && mgr != null && mgr._navPromptsDictionary != null)
                mgr._navPromptsDictionary[key] = data;

            _injected[key] = new Entry { Key = key, Callback = cb, Data = data, PostBuild = postBuild };
            Rebroadcast();
        }

        public static void Remove(NavPrompt key)
        {
            if (!_injected.Remove(key)) return;
            Rebroadcast();
        }

        internal static void OnBeforeUpdatePrompts(NavPromptChanged evt)
        {
            var dict = evt?.NavPrompts;
            if (dict == null) return;

            if (_pristine == null) _pristine = new Il2CppNavPromptDict();
            _pristine.Clear();
            foreach (var kv in dict)
            {
                if (_injected.ContainsKey(kv.Key) || NavPromptCore.ClaimedKeys.Contains(kv.Key)) continue;
                _pristine[kv.Key] = kv.Value;
            }
            _pristineSeen = true;

            foreach (var kv in _injected)
                dict[kv.Key] = (Il2CppSystem.Action)kv.Value.Callback;
        }

        private static readonly Dictionary<NavigationPromptButton, NavPrompt> _sweepScratch
            = new Dictionary<NavigationPromptButton, NavPrompt>();

        internal static void OnAfterUpdatePrompts(NavigationOverlayManager mgr)
        {
            if (mgr == null) return;
            if (_injected.Count == 0 && _ownedButtons.Count == 0) return;
            var parent = mgr._navPromptsParent;
            if (parent == null) return;

            _sweepScratch.Clear();
            int childCount = parent.childCount;
            for (int i = 0; i < childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child == null || !child.gameObject.activeInHierarchy) continue;
                var btn = child.GetComponent<NavigationPromptButton>();
                if (btn == null) continue;
                var label = btn._localisedStaticLabel;
                if (label == null) continue;
                string key = label.Key;
                if (string.IsNullOrEmpty(key)) continue;

                Entry match = null;
                foreach (var entry in _injected.Values)
                {
                    if (entry.Data == null || entry.Data.LocalisationKey != key) continue;
                    match = entry;
                    break;
                }
                if (match == null) continue;

                _sweepScratch[btn] = match.Key;
                // only run the (expensive) glyph resolution when this button isn't already pinned
                // for THIS entry. same button/same entry across a rebuild = keep the sprites as-is;
                // a button that swapped between two of our prompts must be wiped first, or it keeps
                // whichever side the new pin doesn't overwrite.
                if (match.PostBuild == null) continue;
                if (_ownedButtons.TryGetValue(btn, out var pinnedFor) && pinnedFor == match.Key) continue;
                ResetGlyph(btn);
                match.PostBuild(btn);
            }

            foreach (var kv in _ownedButtons)
            {
                if (kv.Key == null || _sweepScratch.ContainsKey(kv.Key)) continue;
                ResetGlyph(kv.Key);
            }
            _ownedButtons.Clear();
            foreach (var kv in _sweepScratch) _ownedButtons[kv.Key] = kv.Value;
            _sweepScratch.Clear();
        }

        // undo an earlier ApplyOwnGlyph on a button the manager has since reassigned to a game
        // prompt. flip _mappeable back on so the ACU rebuilds glyphs from its RewiredAction.
        private static void ResetGlyph(NavigationPromptButton btn)
        {
            var ctrl = btn.TryCast<NavigationPromptButtonController>();
            var acu = ctrl != null ? ctrl._activeControllerUI : null;
            if (acu == null) return;
            acu._mappeable = true;
            acu._notMappeableKeyboardSprite = null;
            acu._notMappeableJoystickSprite = null;
            acu.UpdateGlyphsWithActiveController();
        }

        private static void Rebroadcast()
        {
            if (!_pristineSeen) return;
            var dict = new Il2CppNavPromptDict();
            if (_pristine != null)
                foreach (var kv in _pristine) dict[kv.Key] = kv.Value;
            Broadcaster.Instance?.Broadcast(new NavPromptChanged(dict));
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(NavigationOverlayManager), "UpdateNavPrompts")]
    internal static class NavigationOverlayManagerUpdateNavPromptsPatch
    {
        [HarmonyLib.HarmonyPrefix]
        public static void Prefix(NavPromptChanged navPromptChangedEvent)
            => NavPromptInjection.OnBeforeUpdatePrompts(navPromptChangedEvent);

        [HarmonyLib.HarmonyPostfix]
        public static void Postfix(NavigationOverlayManager __instance)
            => NavPromptInjection.OnAfterUpdatePrompts(__instance);
    }
}
