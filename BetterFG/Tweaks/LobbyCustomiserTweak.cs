using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using FG.Common;
using FGClient;
using FGClient.Customiser;
using FGClient.UI;
using FGClient.UI.PrivateLobby;
using MPG.Utility;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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

        private static bool _showListUp;

        public static void OnPopupOpened() => _showListUp = true;

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
                _popupOpen = ActiveUnderRoot(ShowSelectSub) || ActiveUnderRoot(PlayerListSub);
                return _popupOpen;
            }
        }

        private static Transform _uiRoot;

        private static Transform UiRoot
        {
            get
            {
                if (_uiRoot is null || _uiRoot.m_CachedPtr == System.IntPtr.Zero)
                {
                    var go = GameObject.Find(UiRootPath);
                    _uiRoot = go != null ? go.transform : null;
                }
                return _uiRoot;
            }
        }

        private static bool ActiveUnderRoot(string sub)
        {
            var root = UiRoot;
            if (root == null) return false;
            var t = root.Find(sub);
            return t != null && t.gameObject.activeInHierarchy;
        }

        const string UiRootPath = "UICanvas_Client_V2(Clone)/Default";
        const string LobbyCanvasSub = "Prime_UI_PrivateLobby_Canvas(Clone)";
        const string TopBarSub = "Topbar_Prime(Clone)";
        const string ShowSelectSub = "Prefab_UI_PrivateLobbyShowSelect(Clone)";
        const string PlayerListSub = "Prime_UI_PrivateLobbyPlayerList(Clone)";
        const string SafeAreaSub = "Prime_UI_Customizer_Prefab_Canvas(Clone)/Generic_UI_CustomizerLandingPage_Prefab/SafeArea";
        const string GroupName = "BFG_CustomiseGroup";

        const float GroupX = -375.8f;
        const float GroupTop = -60f;
        const float Spacing = 8f;
        const float ButtonHeight = 72f;

        const string S1RowSub = "BFG_S1CustLanding/CustomiserSubMenuCarrousel";
        const string S1LobbySub = "BFG_S1PrivateLobby/Prefab_UI_Lobby/UI_Private_Lobby_Container/SafeArea/PrivateLobbiesV2";
        static readonly Vector2 S1GroupPos = new Vector2(-78f, -50f);
        const float S1Spacing = 16f;

        static readonly Vector3 PartyHome = Vector3.zero;
        static readonly Vector3 PartyShifted = new Vector3(-3.9f, 0f, 0f);
        const float SlideTime = 0.35f;

        private GameObject _group;
        private CanvasGroup _groupInput;
        private UICustomToggle _players, _show;
        private Transform _row;
        private RectTransform _s1Row;
        private Transform _s1Group;
        private int _s1Pick = -1;

        public bool S1Picked => _s1Pick >= 0;
        private CustomiserScreenViewModel _vm;
        private CustomiserSelectButtonsScreenViewModel _tabsVm;
        private TabsMenuInputHandler _tabsInput;
        private NavigableMenuInputHandler _lobbyInput;
        private PrivateLobbyScreenViewModel _lobbyVm;
        private GameObject _returnTo;
        private MenuInputHandler.MoveUp _moveUp;
        private MenuInputHandler.MoveDown _moveDown;
        private GameObject _customiserViewGo;
        private bool _selectorUp;
        private bool _stoodDown;
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

        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && _open && _selectorUp && !_stoodDown) StartCoroutine(RegainSelectorFocusDelayed().WrapToIl2Cpp());
        }

        private IEnumerator RegainSelectorFocusDelayed()
        {
            for (int i = 0; i < 5; i++) yield return null;
            if (_open && _selectorUp) _vm.OnGainFocus();
        }

        void Update()
        {
            if (!IsEnabled || !_open || _vm == null) return;

            var root = UiRoot;
            if (root == null || !root.gameObject.activeInHierarchy) { Close(); return; }

            if (PopupOpen)
            {
                _stoodDown = true;
                if (_tabsInput.enabled) _tabsInput.enabled = false;
                if (_customiserViewGo.activeSelf) _customiserViewGo.SetActive(false);
                if (_groupInput.blocksRaycasts) _groupInput.blocksRaycasts = false;
                return;
            }
            if (_stoodDown)
            {
                _stoodDown = false;
                _customiserViewGo.SetActive(true);
                _tabsInput.enabled = _selectorUp;
                _groupInput.blocksRaycasts = true;
                var bottom = _group.transform.GetChild(2).GetComponent<UICustomToggle>();
                LinkUp(_show, bottom);
                LinkUp(_players, bottom);
                DropHandlerVertical();
            }

            var bar = root.Find(TopBarSub);
            if (bar != null && bar.gameObject.activeSelf) bar.gameObject.SetActive(false);

            if (!_selectorUp && _lobbyVm._isInFocus)
            {
                if (_s1Group == null) S1Build();
                if (_s1Group != null) S1Nav();
            }

            bool selectorUp = _vm.CurrentScreen != CustomiserScreens.SelectButtons;
            if (selectorUp == _selectorUp) return;
            _selectorUp = selectorUp;
            SetSelectorMode(selectorUp);
            if (selectorUp) _vm.OnGainFocus();
            else
            {
                _vm.OnLoseFocus();
                SingletonBehaviour<RewiredManager>.Instance.SetActiveMap(_lobbyVm.MetaData.RewiredMap);
                Customization.Player.SkinApplicationService.Instance?.ApplyGameColourPatternToAllBeans();
            }
        }

        private void S1Build()
        {
            var v2 = _lobbyVm.transform.Find(S1LobbySub);
            if (v2 == null) return;
            _s1Pick = -1;
            _s1Row = _row.parent.parent.Find(S1RowSub)?.GetComponent<RectTransform>();
            if (_s1Row != null) _s1Row.localScale = Vector3.zero;

            var g = new GameObject(GroupName);
            var grt = g.AddComponent<RectTransform>();
            grt.SetParent(v2, false);
            grt.anchorMin = grt.anchorMax = grt.pivot = Vector2.one;
            grt.anchoredPosition = S1GroupPos;
            var vl = g.AddComponent<VerticalLayoutGroup>();
            vl.spacing = S1Spacing;
            vl.childAlignment = TextAnchor.UpperRight;
            vl.childControlWidth = vl.childControlHeight = false;
            vl.childForceExpandWidth = vl.childForceExpandHeight = false;
            g.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var src = v2.Find("ButtonPlayersList").gameObject;
            for (int i = 0; i < 3; i++)
            {
                var b = UnityEngine.Object.Instantiate(src, g.transform);
                b.name = "BFG_Customise_" + i;
                b.transform.Find("ButtonInput").gameObject.SetActive(false);
                var txt = b.transform.Find("ButtonText");
                UnityEngine.Object.DestroyImmediate(txt.GetComponent<LocalisedStaticLabel>());
                var tile = _row.GetChild(i);
                txt.GetComponent<TMPro.TextMeshProUGUI>().text = tile.Find("Title_Text").GetComponent<TMPro.TextMeshProUGUI>().text;
                var icon = b.transform.Find("Icon").GetComponent<Image>();
                icon.sprite = tile.Find("Icon").GetComponent<Image>().sprite;
                icon.preserveAspect = true;

                int which = i;
                var trig = b.AddComponent<EventTrigger>();
                var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                click.callback.AddListener(new Action<BaseEventData>(_ => OpenSubScreen(which)));
                trig.triggers.Add(click);
                b.SetActive(true);
            }
            _s1Group = g.transform;
            _s1Group.SetSiblingIndex(src.transform.GetSiblingIndex() + 1);
            Plugin.Log.LogInfo("s1 lobby, customise buttons stacked top right");
        }

        private void S1Nav()
        {
            var input = _lobbyVm.RewiredPlayer;
            int pick = _s1Pick;
            if (input.GetButtonDown(14)) pick = pick < 0 ? 2 : Mathf.Max(pick - 1, 0);
            else if (pick >= 0 && input.GetNegativeButtonDown(14)) pick = pick == 2 ? -1 : pick + 1;
            else if (pick >= 0 && input.GetButtonDown(15)) { OpenSubScreen(pick); return; }
            S1Highlight(pick);
        }

        private void S1Highlight(int pick)
        {
            if (pick == _s1Pick) return;
            var data = new PointerEventData(EventSystem.current);
            if (_s1Pick >= 0 && _s1Group != null) ExecuteEvents.Execute(_s1Group.GetChild(_s1Pick).gameObject, data, ExecuteEvents.pointerExitHandler);
            if (pick >= 0) ExecuteEvents.Execute(_s1Group.GetChild(pick).gameObject, data, ExecuteEvents.pointerEnterHandler);
            _s1Pick = pick;
        }

        private void SetSelectorMode(bool on)
        {
            _tabsInput.enabled = on;

            var rootGo = GameObject.Find(UiRootPath);
            var lobbyCanvas = rootGo == null ? null : rootGo.transform.Find(LobbyCanvasSub);
            if (lobbyCanvas != null) lobbyCanvas.gameObject.SetActive(!on);

            var lobbyScreen = GameObject.Find("Menu_Screen_Lobby(Clone)");
            if (lobbyScreen != null)
            {
                var fg = lobbyScreen.transform.Find("ForegroundCanvas");
                if (fg != null) fg.gameObject.SetActive(!on);
            }

            if (!on)
            {
                DropHandlerVertical();
                if (_returnTo != null && _returnTo.activeInHierarchy) EventSystem.current.SetSelectedGameObject(_returnTo);
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

            var parent = builder._customiseUIParent;
            var views = builder.SwitchableView.Views;
            int index = -1;
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
            _row = safeArea.Find("HorizontalLayout");
            _row.localScale = Vector3.zero;

            _vm = builder.CustomiserScreenViewModel;
            _tabsVm = safeArea.parent.GetComponent<CustomiserSelectButtonsScreenViewModel>();
            _tabsInput = _row.GetComponent<TabsMenuInputHandler>();
            _tabsInput.enabled = false;
            _lobbyInput = lobby.GetComponent<NavigableMenuInputHandler>();
            _lobbyVm = lobby.GetComponentInChildren<PrivateLobbyScreenViewModel>(true);

            BuildButtons(lobby.Find("SafeArea"));
            Plugin.Log.LogInfo($"customiser view {index} up behind the lobby, 3 buttons cloned off the players list");
        }

        internal static Transform LiveCustomiserRow()
        {
            var builderGo = GameObject.Find(UiRootPath + "/MainMenuBuilder(Clone)");
            var builder = builderGo == null ? null : builderGo.GetComponent<MainMenuBuilder>();
            return builder == null ? null : builder._customiseUIParent.Find(SafeAreaSub + "/HorizontalLayout");
        }

        internal static GameObject SpawnGroup(Transform lobbySafeArea, Transform row)
        {
            var stale = lobbySafeArea.Find(GroupName);
            if (stale != null) UnityEngine.Object.DestroyImmediate(stale.gameObject);

            var group = new GameObject(GroupName);
            var grt = group.AddComponent<RectTransform>();
            grt.SetParent(lobbySafeArea, false);
            grt.anchorMin = grt.anchorMax = Vector2.one;
            grt.pivot = new Vector2(0.5f, 1f);
            grt.sizeDelta = new Vector2(400f, 0f);
            grt.anchoredPosition = new Vector2(GroupX, GroupTop);
            var vl = group.AddComponent<VerticalLayoutGroup>();
            vl.spacing = Spacing;
            vl.childAlignment = TextAnchor.UpperCenter;
            vl.childControlWidth = vl.childControlHeight = false;
            vl.childForceExpandWidth = vl.childForceExpandHeight = false;

            var players = lobbySafeArea.Find("PlayersButton").gameObject;
            for (int i = 0; i < 3; i++)
            {
                var b = UnityEngine.Object.Instantiate(players, group.transform);
                b.name = "BFG_Customise_" + i;
                var brt = b.GetComponent<RectTransform>();
                Customization.UI.MenuLayoutService.CopyStockPose(players.GetComponent<RectTransform>(), brt);
                brt.sizeDelta = new Vector2(brt.sizeDelta.x, ButtonHeight);
                UnityEngine.Object.DestroyImmediate(b.GetComponent<EventTrigger>());
                foreach (var ab in b.GetComponents<Mediatonic.Tools.MVVM.ActiveBinding>()) UnityEngine.Object.DestroyImmediate(ab);

                var txt = b.transform.Find("Content/Text");
                UnityEngine.Object.DestroyImmediate(txt.GetComponent<Mediatonic.Tools.MVVM.TMPTextBinding>());
                if (row != null)
                {
                    var tile = row.GetChild(i);
                    txt.GetComponent<TMPro.TextMeshProUGUI>().text = tile.Find("Title_Text").GetComponent<TMPro.TextMeshProUGUI>().text;
                    var icon = b.transform.Find("Content/Icon").GetComponent<Image>();
                    icon.sprite = tile.Find("Icon").GetComponent<Image>().sprite;
                    icon.preserveAspect = true;
                }

                b.GetComponent<UICustomToggle>().isOn = false;
                b.SetActive(true);
            }
            return group;
        }

        private void BuildButtons(Transform lobbySafeArea)
        {
            _group = SpawnGroup(lobbySafeArea, _row);
            _groupInput = _group.AddComponent<CanvasGroup>();
            _players = lobbySafeArea.Find("PlayersButton").GetComponent<UICustomToggle>();
            _show = (lobbySafeArea.Find("BottomRightGroup/ShowSelectorGroup/ShowSelectorButton")
                     ?? lobbySafeArea.Find("ShowSelectorGroup/ShowSelectorButton")).GetComponent<UICustomToggle>();

            var buttons = new UICustomToggle[3];
            for (int i = 0; i < 3; i++)
            {
                var b = _group.transform.GetChild(i).gameObject;
                buttons[i] = b.GetComponent<UICustomToggle>();

                int which = i;
                var trig = b.AddComponent<EventTrigger>();
                var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                click.callback.AddListener(new Action<BaseEventData>(_ => OpenSubScreen(which)));
                trig.triggers.Add(click);
                var submit = new EventTrigger.Entry { eventID = EventTriggerType.Submit };
                submit.callback.AddListener(new Action<BaseEventData>(_ => OpenSubScreen(which)));
                trig.triggers.Add(submit);
            }

            for (int i = 0; i < 3; i++)
            {
                var nav = buttons[i].navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnUp = i > 0 ? buttons[i - 1] : null;
                nav.selectOnDown = i < 2 ? buttons[i + 1] : _show;
                nav.selectOnLeft = _players;
                nav.selectOnRight = null;
                buttons[i].navigation = nav;
                SetNavModes(buttons[i], i > 0, true, true, false);
                _lobbyInput.RegisterDynamicSelectable(buttons[i]);
            }

            LinkUp(_show, buttons[2]);
            LinkUp(_players, buttons[2]);
            DropHandlerVertical();
            Customization.UI.MenuLayoutService.ApplyLobby();
        }

        private void DropHandlerVertical()
        {
            if (_lobbyInput.onMoveUp != null) _moveUp = _lobbyInput.onMoveUp;
            if (_lobbyInput.onMoveDown != null) _moveDown = _lobbyInput.onMoveDown;
            _lobbyInput.onMoveUp = null;
            _lobbyInput.onMoveDown = null;
        }

        private void LinkUp(UICustomToggle t, Selectable up)
        {
            var nav = t.navigation;
            nav.selectOnUp = up;
            t.navigation = nav;
            var mode = up == null ? Navigation.Mode.None : Navigation.Mode.Explicit;
            _lobbyInput._cachedNavModes[t][1] = mode;
            if (_lobbyInput._inFocus) t._navigationModeUp = mode;
        }

        private static void SetNavModes(UICustomToggle t, bool up, bool down, bool left, bool right)
        {
            t._navigationModeUp = up ? Navigation.Mode.Explicit : Navigation.Mode.None;
            t._navigationModeDown = down ? Navigation.Mode.Explicit : Navigation.Mode.None;
            t._navigationModeLeft = left ? Navigation.Mode.Explicit : Navigation.Mode.None;
            t._navigationModeRight = right ? Navigation.Mode.Explicit : Navigation.Mode.None;
        }

        private void OpenSubScreen(int which)
        {
            if (!_open || _selectorUp) return;
            _returnTo = _group.transform.GetChild(which).gameObject;
            if (which == 0) _tabsVm.ShowOutfitsSubScreen();
            else if (which == 1) _tabsVm.ShowTheatricsSubScreen();
            else _tabsVm.ShowInterfaceSubScreen();
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
            Customization.Player.SkinApplicationService.Instance?.ApplyGameColourPatternToAllBeans();

            if (_selectorUp)
            {
                SetSelectorMode(false);
                _vm.OnLoseFocus();
            }
            _selectorUp = false;
            _stoodDown = false;

            if (_group != null)
            {
                foreach (var t in _group.GetComponentsInChildren<UICustomToggle>(true))
                {
                    _lobbyInput.UnregisterDynamicSelectable(t);
                    _lobbyInput._cachedNavModes.Remove(t);
                }
                UnityEngine.Object.Destroy(_group);
                if (_show != null) LinkUp(_show, null);
                if (_players != null) LinkUp(_players, null);
                _lobbyInput.onMoveUp = _moveUp;
                _lobbyInput.onMoveDown = _moveDown;
            }
            _moveUp = null;
            _moveDown = null;
            _returnTo = null;
            _group = null;
            _groupInput = null;
            _show = null;
            _players = null;

            S1Highlight(-1);
            if (_row != null)
            {
                _row.localScale = Vector3.one;
                _row.parent.Find("LandingPageTitle_Text").gameObject.SetActive(true);
                _tabsInput.enabled = true;
            }
            _row = null;

            if (_s1Row != null)
            {
                _s1Row.localScale = Vector3.one;
            }
            _s1Row = null;
            if (_s1Group != null) UnityEngine.Object.Destroy(_s1Group.gameObject);
            _s1Group = null;

            _vm = null;
            _tabsVm = null;
            _tabsInput = null;
            _lobbyInput = null;
            _lobbyVm = null;

            if (_customiserViewGo != null) _customiserViewGo.SetActive(false);
            _customiserViewGo = null;
            Plugin.Log.LogInfo("customiser closed, menu view handed back");
        }
    }
}
