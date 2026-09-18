using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using FG.Common;
using FGClient;
using FGClient.Customiser;
using FGClient.UI;
using FGClient.UI.PrivateLobby;
using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BetterFG.Tweaks
{
    public class LobbyCustomiserTweak : BfgTweak
    {
        public LobbyCustomiserTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "lobby_customiser";
        public override string TweakLabel => "tweak.customise_in_private_lobby";
        public override string TweakTooltip => "ui.shows_the_customiser_options_in_the_top_right_of";
        public override bool DefaultEnabled => true;

        public static LobbyCustomiserTweak Instance { get; private set; }
        void Awake() => Instance = this;

        public bool IsBrowsing => _selectorUp;
        public bool IsOnTabs => _onTabs;

        private static Transform _showSelect, _playerList;

        private static bool _showListUp;

        // the show list owns the screen while it's up. anything we leave behind — the forced-active
        // customiser view, the hidden topbar, the re-anchored row, SetRow's handler/selection writes —
        // fights it, so we tear the whole thing down and rebuild it when the list closes.
        public static void OnPopupOpened()
        {
            _showListUp = true;
            var inst = Instance;
            if (inst != null && inst._open) inst.Close();
        }

        public static void OnPopupClosed()
        {
            _showListUp = false;
            var inst = Instance;
            if (inst != null && inst.IsEnabled && inst._inLobby && !inst._open)
                inst.StartCoroutine(inst.Open().WrapToIl2Cpp());
        }

        private static int _popupFrame = -1;
        private static bool _popupOpen;

        public static bool PopupOpen
        {
            get
            {
                if (_showListUp) return true;
                int f = Time.frameCount;
                if (f == _popupFrame) return _popupOpen;
                _popupFrame = f;
                _popupOpen = GameObject.Find(UiRootPath + "/" + ShowSelectSub) != null ||
                             GameObject.Find(UiRootPath + "/" + PlayerListSub) != null;
                return _popupOpen;
            }
        }

        const string UiRootPath = "UICanvas_Client_V2(Clone)/Default";
        const string LobbyCanvasSub = "Prime_UI_PrivateLobby_Canvas(Clone)";
        const string TopBarSub = "Topbar_Prime(Clone)";
        const string ShowSelectSub = "Prefab_UI_PrivateLobbyShowSelect(Clone)";
        const string PlayerListSub = "Prime_UI_PrivateLobbyPlayerList(Clone)";

        const string SafeAreaSub = "Prime_UI_Customizer_Prefab_Canvas(Clone)/Generic_UI_CustomizerLandingPage_Prefab/SafeArea";

        static readonly Vector2 TopRight = new Vector2(1f, 1f);
        const float RowScale = 0.68f;
        const float MarginX = 4f;
        const float MarginY = 52f;
        const float PixelsPerUnit = 0.76f;

        static readonly Vector3 PartyHome = Vector3.zero;
        static readonly Vector3 PartyShifted = new Vector3(-3.9f, 0f, 0f);
        const float SlideTime = 0.35f;

        private RectTransform _row;
        private Vector2 _origAnchorMin, _origAnchorMax, _origPivot, _origAnchoredPos, _origSizeDelta;
        private Vector3 _origScale;
        private List<UnityEngine.UI.Image> _rowImages;
        private List<float> _origPpu;
        private CustomiserScreenViewModel _vm;
        private CustomiserSelectButtonsScreenViewModel _tabsVm;
        private TabsMenuInputHandler _tabsInput;
        private NavigableMenuInputHandler _lobbyInput;
        private PrivateLobbyScreenViewModel _lobbyVm;
        private Player _navPlayer;
        private int _vertAction;
        private GameObject _prevSel;
        private bool _onTabs;
        private bool _heldVert;
        private bool _selectorUp;
        private bool _stoodDown;
        private GameObject _customiserViewGo;
        private bool _open;
        private bool _inLobby;

        public override void OnStateChanged(GameStateMachine.IGameState newState)
        {
            bool lobby = newState != null && newState.TryCast<StatePrivateLobby>() != null;
            if (lobby == _inLobby) return;
            _inLobby = lobby;
            if (lobby) StartCoroutine(Open().WrapToIl2Cpp());
            else Close();
        }

        public override void EnableTweak()
        {
            if (_inLobby && !_open) StartCoroutine(Open().WrapToIl2Cpp());
        }

        public override void DisableTweak() => Close();

        private bool _navSuspended;

        // lets other lobby-scoped tweaks (LobbyAudioPromptTweak) put their own full-screen overlay
        // over the lobby without our row fighting them for input - hiding just the row isn't enough,
        // our own Update() keeps polling the vertical axis and flipping _tabsInput/_lobbyInput/
        // EventSystem.sendNavigationEvents underneath whatever's now on top, which can desync that
        // global flag and take out navigation for everything, lobby included, once we resume.
        public void SetNavSuspended(bool suspended)
        {
            _navSuspended = suspended;
            if (_row == null) return;

            if (suspended)
            {
                if (_onTabs)
                {
                    if (_tabsInput != null) foreach (var tab in _tabsInput._tabs) tab.Deselect(true);
                    _tabsVm?.OnLoseFocus();
                }
                _row.gameObject.SetActive(false);
                if (_tabsInput != null) _tabsInput.enabled = false;
                if (_lobbyInput != null) _lobbyInput.enabled = false;
            }
            else
            {
                _row.gameObject.SetActive(true);
                if (_tabsInput != null) _tabsInput.enabled = _onTabs;
                if (_lobbyInput != null) _lobbyInput.enabled = !_onTabs;
                if (_onTabs)
                {
                    _tabsVm?.OnGainFocus();
                    if (_tabsInput != null) _tabsInput._tabs[_tabsInput._currentTabIndex].Select(true);
                }
                if (EventSystem.current != null)
                {
                    EventSystem.current.sendNavigationEvents = true;
                    if (!_onTabs)
                    {
                        var back = _lobbyInput?._lastSelectedGameObject;
                        if (back != null && back.activeInHierarchy) EventSystem.current.SetSelectedGameObject(back);
                    }
                }
            }
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) return;
            _heldVert = true;
            if (!_open || _navSuspended || _stoodDown) return;
            if (_selectorUp) { StartCoroutine(RegainSelectorFocusDelayed().WrapToIl2Cpp()); return; }
            if (_tabsInput != null) _tabsInput.enabled = _onTabs;
        }

        private IEnumerator RegainSelectorFocusDelayed()
        {
            for (int i = 0; i < 5; i++) yield return null;
            if (_open && _selectorUp) _vm?.OnGainFocus();
        }

        private IEnumerator RegainLobbyFocusDelayed()
        {
            for (int i = 0; i < 5; i++) yield return null;
            if (!_open || _selectorUp) yield break;
            var go = GameObject.Find(UiRootPath + "/" + LobbyCanvasSub);
            var lobbyVm = go == null ? null : go.GetComponentInChildren<PrivateLobbyScreenViewModel>(true);
            lobbyVm?.OnGainFocus();
        }

        void Update()
        {
            if (!IsEnabled || !_open || _navSuspended) return;

            var rootGo = GameObject.Find(UiRootPath);
            if (rootGo == null) { Close(); return; }
            var root = rootGo.transform;

            if (PopupOpen)
            {
                _stoodDown = true;
                if (_tabsInput != null && _tabsInput.enabled) _tabsInput.enabled = false;
                if (_lobbyInput != null && _lobbyInput.enabled) _lobbyInput.enabled = false;
                if (_customiserViewGo != null && _customiserViewGo.activeSelf) _customiserViewGo.SetActive(false);
                return;
            }
            if (_stoodDown)
            {
                _stoodDown = false;
                if (_customiserViewGo != null) _customiserViewGo.SetActive(true);
                if (_tabsInput != null) _tabsInput.enabled = _onTabs;
                if (_lobbyInput != null) _lobbyInput.enabled = !_onTabs;
            }

            var bar = root.Find(TopBarSub);
            if (bar != null && bar.gameObject.activeSelf) bar.gameObject.SetActive(false);

            if (_vm == null) return;

            bool selectorUp = _vm.CurrentScreen != CustomiserScreens.SelectButtons;
            if (selectorUp != _selectorUp)
            {
                _selectorUp = selectorUp;
                SetSelectorMode(selectorUp);
                if (selectorUp) _vm.OnGainFocus();
                else
                {
                    _vm.OnLoseFocus();
                    Customization.Player.SkinApplicationService.Instance?.ApplyGameColourPatternToAllBeans();
                    StartCoroutine(RegainLobbyFocusDelayed().WrapToIl2Cpp());
                }
            }
            if (selectorUp) return;

            int dy = _navPlayer.GetButton(_vertAction) ? 1 : _navPlayer.GetNegativeButton(_vertAction) ? -1 : 0;
            if (dy != 0 && !_heldVert)
            {
                if (_onTabs)
                {
                    if (dy < 0) SetRow(false);
                }
                else if (dy > 0)
                {
                    var from = _prevSel == null ? null : _prevSel.GetComponent<UnityEngine.UI.Selectable>();
                    if (from == null || from.FindSelectableOnUp() == null) SetRow(true);
                }
            }
            _heldVert = dy != 0;
            _prevSel = EventSystem.current.currentSelectedGameObject;
        }

        private void SetRow(bool tabs)
        {
            _onTabs = tabs;
            EventSystem.current.sendNavigationEvents = !tabs;
            _tabsInput.enabled = tabs;
            _lobbyInput.enabled = !tabs;
            if (tabs)
            {
                _lobbyVm?.OnLoseFocus();
                _tabsVm.OnGainFocus();
                _tabsInput._tabs[_tabsInput._currentTabIndex].Select(true);
            }
            else
            {
                foreach (var tab in _tabsInput._tabs) tab.Deselect(true);
                _tabsVm.OnLoseFocus();
                var back = _lobbyInput._lastSelectedGameObject;
                if (back != null && back.activeInHierarchy) EventSystem.current.SetSelectedGameObject(back);
                _lobbyVm?.OnGainFocus();
            }
        }

        private void SetSelectorMode(bool on)
        {
            _tabsInput.enabled = on || _onTabs;

            var rootGo = GameObject.Find(UiRootPath);
            var lobbyCanvas = rootGo == null ? null : rootGo.transform.Find(LobbyCanvasSub);
            if (lobbyCanvas != null) lobbyCanvas.gameObject.SetActive(!on);

            var lobbyScreen = GameObject.Find("Menu_Screen_Lobby(Clone)");
            if (lobbyScreen != null)
            {
                var fg = lobbyScreen.transform.Find("ForegroundCanvas");
                if (fg != null) fg.gameObject.SetActive(!on);
            }

            EventSystem.current.sendNavigationEvents = on || !_onTabs;

            if (!on && !_onTabs)
            {
                var back = _lobbyInput?._lastSelectedGameObject;
                if (back != null && back.activeInHierarchy) EventSystem.current.SetSelectedGameObject(back);
            }

            StartCoroutine(SlideParty(on ? PartyShifted : PartyHome).WrapToIl2Cpp());
        }

        private IEnumerator Open()
        {
            MainMenuBuilder builder = null;
            Transform lobby = null;
            float waited = 0f;
            while (waited < 10f)
            {
                var rootGo = GameObject.Find(UiRootPath);
                var root = rootGo == null ? null : rootGo.transform;
                lobby = root == null ? null : root.Find(LobbyCanvasSub);

                var builderGo = root == null ? null : root.Find("MainMenuBuilder(Clone)");
                builder = builderGo == null ? null : builderGo.GetComponent<MainMenuBuilder>();

                if (lobby != null && builder != null) break;
                yield return new WaitForSeconds(0.1f);
                waited += 0.1f;
            }

            if (lobby == null || builder == null)
            {
                Plugin.Log.LogWarning("lobby canvas or menu builder never showed up, customiser stays closed");
                yield break;
            }

            var view = builder.SwitchableView;
            var parent = builder._customiseUIParent;
            int index = -1;
            var views = view.Views;
            for (int i = 0; i < views.Length; i++)
                if (views[i] != null && views[i].transform == parent) { index = i; break; }

            if (index < 0)
            {
                Plugin.Log.LogWarning($"customiser isn't in the menu's view list ({views.Length} views), giving up");
                yield break;
            }

            _customiserViewGo = views[index];
            _customiserViewGo.SetActive(true);
            _open = true;

            var topBar = GameObject.Find(UiRootPath).transform.Find(TopBarSub);
            if (topBar != null) topBar.gameObject.SetActive(false);

            yield return null;

            var safeArea = parent.Find(SafeAreaSub);
            safeArea.Find("LandingPageTitle_Text").gameObject.SetActive(false);

            var rowT = safeArea.Find("HorizontalLayout");
            var row = rowT.GetComponent<RectTransform>();

            _row = row;
            _origAnchorMin = row.anchorMin;
            _origAnchorMax = row.anchorMax;
            _origPivot = row.pivot;
            _origAnchoredPos = row.anchoredPosition;
            _origSizeDelta = row.sizeDelta;
            _origScale = rowT.localScale;

            var size = row.rect.size;

            row.anchorMin = TopRight;
            row.anchorMax = TopRight;
            row.pivot = TopRight;
            row.sizeDelta = size;
            rowT.localScale = new Vector3(RowScale, RowScale, RowScale);
            row.anchoredPosition = new Vector2(-MarginX, -MarginY);

            _rowImages = new List<UnityEngine.UI.Image>();
            _origPpu = new List<float>();
            foreach (var img in rowT.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                _rowImages.Add(img);
                _origPpu.Add(img.pixelsPerUnitMultiplier);
                img.pixelsPerUnitMultiplier = PixelsPerUnit;
            }

            _vm = builder.CustomiserScreenViewModel;
            _tabsVm = safeArea.parent.GetComponent<CustomiserSelectButtonsScreenViewModel>();
            _tabsInput = rowT.GetComponent<TabsMenuInputHandler>();
            _lobbyInput = lobby.GetComponent<NavigableMenuInputHandler>();
            _lobbyVm = lobby.GetComponentInChildren<PrivateLobbyScreenViewModel>(true);
            _navPlayer = _lobbyInput._rewiredPlayer;
            _vertAction = _lobbyInput.VerticalAction;
            _heldVert = false;
            _prevSel = null;
            SetRow(false);

            Plugin.Log.LogInfo($"customiser view {index} forced active directly, buttons moved top right ({size.x} x {size.y} at {RowScale})");
            Plugin.Log.LogInfo($"row swap on vertical action {_vertAction}, up = customiser");
        }

        private IEnumerator SlideParty(Vector3 target)
        {
            var go = GameObject.Find("Menu_Screen_Lobby(Clone)");
            if (go == null) yield break;
            var party = go.transform.Find("PartyPlacementTransforms");
            if (party == null) yield break;

            var from = party.localPosition;
            float t = 0f;
            while (t < SlideTime)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / SlideTime);
                party.localPosition = Vector3.Lerp(from, target, 1f - Mathf.Pow(1f - k, 3f));
                yield return null;
            }
            party.localPosition = target;
        }

        private void Close()
        {
            if (!_open) return;
            _open = false;
            _navSuspended = false;
            Customization.Player.SkinApplicationService.Instance?.ApplyGameColourPatternToAllBeans();

            if (_row != null)
            {
                _row.anchorMin = _origAnchorMin;
                _row.anchorMax = _origAnchorMax;
                _row.pivot = _origPivot;
                _row.anchoredPosition = _origAnchoredPos;
                _row.sizeDelta = _origSizeDelta;
                _row.transform.localScale = _origScale;
                _row.transform.parent.Find("LandingPageTitle_Text").gameObject.SetActive(true);
                _row = null;
            }

            if (_rowImages != null)
            {
                for (int i = 0; i < _rowImages.Count; i++)
                    if (_rowImages[i] != null) _rowImages[i].pixelsPerUnitMultiplier = _origPpu[i];
                _rowImages = null;
                _origPpu = null;
            }

            if (_tabsInput != null)
            {
                if (_onTabs)
                {
                    foreach (var tab in _tabsInput._tabs) tab.Deselect(true);
                    _tabsVm.OnLoseFocus();
                }
                _tabsInput.enabled = true;
            }
            if (_lobbyInput != null) _lobbyInput.enabled = true;
            _onTabs = false;
            _stoodDown = false;
            _heldVert = false;
            _prevSel = null;
            EventSystem.current.sendNavigationEvents = true;

            if (_selectorUp) SetSelectorMode(false);
            _selectorUp = false;
            if (_vm != null) _vm.OnLoseFocus();
            _vm = null;
            _tabsVm = null;
            _tabsInput = null;
            _lobbyInput = null;
            _lobbyVm = null;
            _navPlayer = null;

            if (_customiserViewGo != null) _customiserViewGo.SetActive(false);
            _customiserViewGo = null;
            Plugin.Log.LogInfo("customiser closed, menu view handed back");
        }
    }
}
