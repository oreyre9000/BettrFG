using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterFG.Core;
using BetterFG.Customization.UI;
using BetterFG.Features.CreativeExpansion;
using BetterFG.Services;
using BetterFG.Utilities;
using FGClient;
using FGClient.UI;
using FGClient.UI.Core;
using FGClient.UI.PrivateLobby;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MPG.Utility;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterFG.Tweaks
{
    public class LevelBrowserTweak : BfgTweak
    {
        public LevelBrowserTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "level_browser";
        public override string TweakLabel => "tweak.level_browser";
        public override string TweakTooltip => "tweak.level_browser_tip";
        public override bool DefaultEnabled => true;

        public static LevelBrowserTweak Instance { get; private set; }
        void Awake() => Instance = this;

        internal const string RowName = "BFG_BrowseLevels";
        const float RowHeight = 62f;

        internal static bool IsOpen => Instance != null && Instance._screen != null && Instance._screen.Live;

        private PrivateLobbyShowListViewModel _vm;
        private GameObject _row;
        private LevelBrowserScreen _screen;
        private int _rowSelectedFrame = -1;
        private string _pendingCode;
        private float _pendingUntil;
        private readonly List<GameObject> _optionScratch = new List<GameObject>();

        internal static void OnShowListAwake(PrivateLobbyShowListViewModel vm)
        {
            var inst = Instance;
            if (inst == null || !inst.IsEnabled) return;
            inst._vm = vm;
            inst._row = null;
        }

        internal void LoadPicked(PrivateLobbyShowListViewModel lobby, string code)
        {
            lobby.OnEnterCodeButtonPressed();
            lobby.OnCodeInputButtonPressed(code);
            _pendingCode = code.Replace("-", "");
            _pendingUntil = Time.unscaledTime + 20f;
            Plugin.Log.LogInfo($"level browser picked {code}, downloading it for the lobby");
        }

        void Update()
        {
            if (_screen != null)
            {
                if (_screen.Tick()) return;
                _screen = null;
            }
            if (_pendingCode != null)
            {
                string cur = SingletonBehaviour<FragglePlobbiesManager>.Instance?.ShareCode;
                if (_vm != null && cur != null && cur.Replace("-", "").Contains(_pendingCode))
                {
                    _vm.OnConfirmPressed();
                    _pendingCode = null;
                }
                else if (Time.unscaledTime > _pendingUntil)
                {
                    Plugin.Log.LogWarning($"picked level {_pendingCode} never showed up as the highlighted show, left it on the enter code panel");
                    _pendingCode = null;
                }
            }
            if (_vm == null || !IsEnabled) return;
            if (!_vm.gameObject.activeInHierarchy) { _vm = null; _row = null; return; }

            var ec = _vm._enterCodeButtonSelectable;
            if (ec == null) return;
            var content = ec.transform.parent;
            if (_row == null || _row.transform.parent != content) BuildRow(content, ec.gameObject);
            if (_row == null) return;

            var search = content.Find("BFG_ShowSearch");
            if (search != null && search.GetSiblingIndex() < _row.transform.GetSiblingIndex()) _row.transform.SetSiblingIndex(search.GetSiblingIndex());
            EnsureOption(content, ec.gameObject);

            bool selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == _row;
            if (!selected) { _rowSelectedFrame = -1; return; }
            if (_rowSelectedFrame < 0) { _rowSelectedFrame = Time.frameCount; return; }
            if (KeybindService.KeyDown(KeyCode.Return) || KeybindService.KeyDown(KeyCode.Space)
                || NavPromptCore.PollActionDirect(RewiredConsts.Action.Menu_UISubmit, null, false))
                Open();
        }

        private void BuildRow(Transform content, GameObject enterCode)
        {
            var stale = content.Find(RowName);
            if (stale != null) UnityEngine.Object.DestroyImmediate(stale.gameObject);
            if (!ShowRoundsPopup.FindPrefabs()) return;

            _row = UnityEngine.Object.Instantiate(ShowRoundsPopup.RowPrefab, content);
            _row.name = RowName;
            _row.transform.SetSiblingIndex(enterCode.transform.GetSiblingIndex() + 1);
            foreach (var p in new[] { "Button", "Favourite", "SubMenuFavouriteBacking", "SubMenuFavouriteIcon", "Generic_UI_PipNotification_Prefab" })
                UnityEngine.Object.DestroyImmediate(_row.transform.Find(p).gameObject);
            ShowRoundsPopup.StripBindings(_row);

            _row.transform.Find("ShowNameText").GetComponent<TextMeshProUGUI>().text = LocalizationService.Get("level_browser.browse");
            var rt = _row.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, RowHeight);

            var toggle = _row.GetComponent<UICustomToggle>();
            var tween = _row.GetComponent<TweenOnToggleSubMenu>();
            toggle._navigationModeUp = UnityEngine.UI.Navigation.Mode.None;
            toggle._navigationModeDown = UnityEngine.UI.Navigation.Mode.None;
            toggle._navigationModeLeft = UnityEngine.UI.Navigation.Mode.None;
            toggle._navigationModeRight = UnityEngine.UI.Navigation.Mode.None;
            toggle.onValueChanged.RemoveAllListeners();
            ShowRoundsPopup.Mute(toggle.onValueChanged);
            toggle.group = null;
            toggle.isOn = false;
            toggle.onValueChanged.AddListener(new Action<bool>(on =>
            {
                tween.CancelTweens();
                if (on) tween.StartTweenOn();
                else tween.StartTweenOff();
            }));

            var row = _row;
            var trig = _row.AddComponent<EventTrigger>();
            var hover = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            hover.callback.AddListener(new Action<BaseEventData>(_ => EventSystem.current.SetSelectedGameObject(row)));
            trig.triggers.Add(hover);
            var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            click.callback.AddListener(new Action<BaseEventData>(_ => Open()));
            trig.triggers.Add(click);
        }

        private void EnsureOption(Transform content, GameObject enterCode)
        {
            var carrousel = content.GetComponent<ShowSelectorCarrouselInputHandler>();
            var opts = carrousel?._menuOptions;
            if (opts == null) return;
            bool hasEnterCode = false;
            for (int i = 0; i < opts.Length; i++)
            {
                if (opts[i] == _row) return;
                if (opts[i] == enterCode) hasEnterCode = true;
            }
            if (!hasEnterCode) return;

            _optionScratch.Clear();
            var sel = EventSystem.current?.currentSelectedGameObject;
            for (int i = 0; i < opts.Length; i++)
            {
                _optionScratch.Add(opts[i]);
                if (opts[i] == enterCode) _optionScratch.Add(_row);
            }
            int index = _optionScratch.IndexOf(sel != null && sel != _row && _optionScratch.Contains(sel) ? sel : enterCode);
            if (index < 0) index = 0;

            var arr = new Il2CppReferenceArray<GameObject>(_optionScratch.Count);
            for (int i = 0; i < _optionScratch.Count; i++) arr[i] = _optionScratch[i];
            carrousel.SetOptions(arr, index, false);
        }

        private void Open()
        {
            if (_screen != null) return;
            _screen = LevelBrowserScreen.Open(_vm);
        }

        public override void DisableTweak()
        {
            _screen?.Dispose();
            _screen = null;
            if (_row != null) UnityEngine.Object.DestroyImmediate(_row);
            _row = null;
        }
    }

    internal sealed class LevelBrowserScreen
    {
        const int Columns = 4;
        const int MaxTiles = 100;
        const int TilesPerFrame = 6;
        const int MinQuery = 4;
        const float CardW = 420f, CardH = 300f, TileW = 532f, TileH = 380f;
        const string SearchUrl = "https://cloudseeker.xyz/api/cv2/creative-search/?name={0}&min_likes=0&max_likes=99999999&min_dislikes=0&max_dislikes=99999999&min_playcount=&max_playcount=&tag1=&tag2=";
        static readonly string[] SortKeys = { "level_browser.sort_played", "level_browser.sort_liked", "level_browser.sort_rated" };
        static readonly NavPrompt[] Prompts = { NavPromptInjection.LevelBrowserConfirm, NavPromptInjection.LevelBrowserCreator, NavPromptInjection.LevelBrowserCopy, NavPromptInjection.LevelBrowserBack };
        static NavigationPromptData _creatorData;
        static readonly HttpClient Http = new HttpClient();

        static GameObject _tilePrefab, _creatorPrefab;

        static LevelBrowserScreen() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("BettrFG");

        sealed class Lvl
        {
            public string Name, Code, Author;
            public long Likes, Dislikes, Plays;
            public int Players;
            public List<string> Tags = new List<string>();
        }

        sealed class Result
        {
            public int Id, Total;
            public List<Lvl>[] Sorted;
            public string Error;
        }

        readonly PrivateLobbyShowListViewModel _lobby;
        readonly EventSystem _es;
        readonly GameObject _prevSel;
        readonly List<Behaviour> _muted = new List<Behaviour>();
        GameObject _root, _cardTpl, _creatorTpl;
        Transform _container;
        ScrollRect _scroll;
        RectTransform _content;
        TextMeshProUGUI _title;
        PopupSearchRow _search;
        GameObject[] _kbNav, _padNav;
        readonly List<Toggle> _tabs = new List<Toggle>();
        readonly List<GameObject> _cards = new List<GameObject>();
        readonly List<Lvl> _shown = new List<Lvl>();
        readonly List<int> _needCover = new List<int>();
        readonly List<Transform> _nested = new List<Transform>();
        readonly float[] _repeatAt = new float[4];
        readonly bool[] _held = new bool[4];
        Result _result, _done;
        Canvas _navCanvas;
        int _navOrder, _built;
        bool _selectFirst;
        int _queryId;
        float _queryAt = -1f;
        string _lastQuery;
        Vector3 _lastMouse;
        int _cur, _sel = -1, _openFrame;
        bool _closing;
        string _pick;

        internal bool Live => _root != null;

        internal static LevelBrowserScreen Open(PrivateLobbyShowListViewModel lobby) =>
            ShowRoundsPopup.FindPrefabs() && FindPrefabs() ? new LevelBrowserScreen(lobby) : null;

        static bool FindPrefabs()
        {
            if (_tilePrefab != null) return true;
            GameObject builder = null, inGame = null;
            var mainMenu = Addressables.LoadAssetAsync<UIConfig>("Data/UIConfig_MainMenu_Shared.asset").WaitForCompletion().Screens;
            for (int i = 0; i < mainMenu.Count; i++) if (mainMenu[i] != null && mainMenu[i].name == "MainMenuBuilder") builder = mainMenu[i].gameObject;
            var common = Addressables.LoadAssetAsync<UIConfig>("Data/UIConfig_Common_Shared.asset").WaitForCompletion().Screens;
            for (int i = 0; i < common.Count; i++) if (common[i] != null && common[i].name == "InGameUiManager") inGame = common[i].gameObject;
            if (builder == null || inGame == null) { Plugin.Log.LogWarning("level browser: MainMenuBuilder or InGameUiManager missing from the ui configs"); return false; }

            _tilePrefab = builder.GetComponent<MainMenuBuilder>()._builderConfig._sharedConfig._showSelectorPrefab
                .GetComponentInChildren<FGClient.ShowSelector.ShowSelectorLiveShowsTabViewModel>(true)._smallSectionViewModel
                .GetComponent<LoopScrollPooler>()._prefab.gameObject;
            _creatorPrefab = inGame.transform.Find("GameStates/PlayingState/GameplayScoringViewModel/TopMiddleContainer/Generic_UI_LE_CreatorInfoSlim").gameObject;
            return true;
        }

        LevelBrowserScreen(PrivateLobbyShowListViewModel lobby)
        {
            _lobby = lobby;
            _es = EventSystem.current;
            _prevSel = _es.currentSelectedGameObject;
            _openFrame = Time.frameCount;

            _root = new GameObject("BFG_LevelBrowser");
            _root.SetActive(false);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            _root.AddComponent<GraphicRaycaster>();

            var backdrop = new GameObject("BFG_Backdrop");
            backdrop.transform.SetParent(_root.transform, false);
            var backdropRt = backdrop.AddComponent<RectTransform>();
            ShowRoundsPopup.Stretch(backdropRt, 0, 0, 0, 0);
            backdrop.AddComponent<RectMask2D>();
            backdrop.AddComponent<Image>().color = new Color(0.36f, 0.16f, 0.78f, 1f);

            var popup = UnityEngine.Object.Instantiate(ShowRoundsPopup.PopupPrefab, _root.transform);
            var tabTpl = UnityEngine.Object.Instantiate(ShowRoundsPopup.TabPrefab, _root.transform);
            var searchGo = UnityEngine.Object.Instantiate(ShowRoundsPopup.RowPrefab, _root.transform);
            _container = popup.transform.Find("Container");
            var c = _container;

            foreach (var p in new[] { "CodeInputGroup", "MissingInfo_Text", "Mobile Confirm Button", "Title/EnterCodeText", "ShowInfo",
                "ShowsScrollList/ShowsListViewport/ShowsListContent/Tooltip", "ShowsScrollList/ShowsListViewport/ShowsListContent/EnterCodeButton" })
                UnityEngine.Object.DestroyImmediate(c.Find(p).gameObject);
            foreach (var p in new[] { "Button", "Favourite", "SubMenuFavouriteBacking", "SubMenuFavouriteIcon", "Generic_UI_PipNotification_Prefab" })
                UnityEngine.Object.DestroyImmediate(searchGo.transform.Find(p).gameObject);
            ShowRoundsPopup.StripBindings(popup, tabTpl, searchGo);

            popup.transform.Find("Generic_UI_Scrim_Prefab").gameObject.SetActive(false);
            ShowRoundsPopup.Stretch(popup.transform, 0, 0, 0, 0);
            c.GetComponent<Image>().enabled = false;
            ShowRoundsPopup.Stretch(c, 0, 0, 0, 0);

            var title = c.Find("Title");
            ShowRoundsPopup.Place(title, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(60, -36), new Vector2(-120, 80));
            _title = title.Find("ShowInfoText").GetComponent<TextMeshProUGUI>();
            _title.gameObject.SetActive(true);

            searchGo.name = "BFG_LevelSearch";
            searchGo.transform.SetParent(c, false);
            ShowRoundsPopup.Place(searchGo.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -146), new Vector2(440, 62));
            _search = new PopupSearchRow(searchGo, "show_rounds.search", OnQueryChanged, SelectSearch);

            _scroll = c.Find("ShowsScrollList").GetComponent<ScrollRect>();
            ShowRoundsPopup.Stretch(_scroll.transform, 60, 96, 60, 236);
            ShowRoundsPopup.Stretch(_scroll.viewport, 0, 0, 44, 0);
            ShowRoundsPopup.Place(c.Find("ShowsScrollList/TabScrollbar"), new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), Vector2.zero, new Vector2(24, -24));
            _content = _scroll.content;

            BuildCardTemplates();

            UnityEngine.Object.DestroyImmediate(_content.GetComponent<VerticalLayoutGroup>());
            var grid = _content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(CardW, CardH);
            grid.spacing = new Vector2(24, 24);
            grid.padding = new RectOffset(14, 14, 34, 14);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Columns;
            grid.childAlignment = TextAnchor.UpperCenter;

            var carousel = c.Find("TabCarrouselShows");
            ShowRoundsPopup.Place(carousel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(600, -144), new Vector2(0, 66));
            var tabsRoot = carousel.Find("TabsContent");
            var tabGroup = tabsRoot.GetComponent<ToggleGroup>();
            tabGroup.allowSwitchOff = false;
            tabTpl.SetActive(false);
            tabTpl.transform.Find("PrivateLobbyShowTab/Content/SubMenuCreativeIcon").gameObject.SetActive(false);
            tabTpl.transform.Find("PrivateLobbyShowTab/Content/SubMenuFavouriteIcon").gameObject.SetActive(false);
            for (int s = 0; s < SortKeys.Length; s++) BuildTab(tabTpl, tabsRoot, tabGroup, s);
            carousel.Find("TabCarrouselNavigationLeft").GetComponent<Button>().onClick.AddListener(new Action(() => Select(_cur - 1)));
            carousel.Find("TabCarrouselNavigationRight").GetComponent<Button>().onClick.AddListener(new Action(() => Select(_cur + 1)));
            _kbNav = new[] { carousel.Find("TabCarrouselNavigationLeft").gameObject, carousel.Find("TabCarrouselNavigationRight").gameObject };
            _padNav = new[] { carousel.Find("TabCarrouselNavigationLeft_Controller").gameObject, carousel.Find("TabCarrouselNavigationRight_Controller").gameObject };

            _root.SetActive(true);
            Canvas.ForceUpdateCanvases();
            BuildBackdrop(backdropRt);
            c.GetComponent<TweenOnPopup>().BeginTween();
            _tabs[0].isOn = true;
            LayoutRebuilder.ForceRebuildLayoutImmediate(tabsRoot.GetComponent<RectTransform>());
            LayoutRebuilder.ForceRebuildLayoutImmediate(carousel.GetComponent<RectTransform>());
            _search.Redraw();

            if (lobby != null)
                foreach (var b in lobby.GetComponentsInChildren<Behaviour>(true))
                {
                    string n = b.GetIl2CppType().Name;
                    if (b.enabled && (n == "ShowSelectorCarrouselInputHandler" || n == "PrivateLobbiesShowTabsNavigation")) { b.enabled = false; _muted.Add(b); }
                }

            SavedGroupsBrowser.AddPrompt(NavPromptInjection.LevelBrowserConfirm, BfgBinds.BatchEditSelect, ConfirmSelected);
            _creatorData ??= NavPromptInjection.BuildData(NavPrompt.Report, LocalizationService.Get("level_browser.creator"), "bfg_level_browser_creator",
                RewiredConsts.Action.Menu_Favourite, RewiredConsts.Category.Default);
            NavPromptInjection.Add(NavPromptInjection.LevelBrowserCreator, SearchCreator, _creatorData,
                btn => NavPromptCore.ApplyOwnGlyph(btn, KeyCode.F, RewiredConsts.Action.Menu_Favourite));
            NavPromptInjection.Add(NavPromptInjection.LevelBrowserCopy, CopySelected, CopyCodePrompt.Instance.PromptData, CopyCodePrompt.ApplyGlyph);
            SavedGroupsBrowser.AddPrompt(NavPromptInjection.LevelBrowserBack, BfgBinds.SavedGroupBack, BeginClose);
            NavPromptInjection.Solo(Prompts);
            FGInputLockService.SetScreenLock(true);
            _navCanvas = NavPromptCore.Manager?.GetComponent<Canvas>();
            if (_navCanvas != null) { _navOrder = _navCanvas.sortingOrder; _navCanvas.sortingOrder = canvas.sortingOrder + 1; }

            StartQuery("");
            SelectSearch();
        }

        static void BuildBackdrop(RectTransform rt)
        {
            ScreenBackgroundService.BuildBackdrop(rt, ScreenBackgroundService.Screen.LoadingLevel, 0.5f);
            var clone = rt.Find("FallForceClone");
            if (clone == null) return;
            rt.Find("Dim").GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.1f);
            var grad = ScreenBackgroundService.BuildGradientTex(new Color(0.36f, 0.16f, 0.78f), new Color(0.98f, 0.36f, 0.66f), 0f, 1f);
            var back = clone.Find("Backdrop").GetComponent<Image>();
            back.sprite = Sprite.Create(grad, new Rect(0, 0, 1, grad.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            back.color = Color.white;
            var mat = clone.Find("Circles").GetComponent<Image>().material;
            var empty = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            empty.SetPixel(0, 0, Color.clear);
            empty.Apply();
            mat.SetTexture("_Pattern", empty);
            mat.SetVector("_OriginOffset", new Vector4(0.12f, 1.15f, 0f, 0f));
            mat.SetFloat("_CirclesTiling", 18f);
            mat.SetFloat("_CirclesOffset", 0.6f);
        }

        void BuildCardTemplates()
        {
            _creatorTpl = UnityEngine.Object.Instantiate(_creatorPrefab, _root.transform);
            _creatorTpl.SetActive(false);
            ShowRoundsPopup.StripBindings(new HashSet<string> { "CreatorIDViewModel", "NameTagViewModel" }, _creatorTpl);
            _creatorTpl.transform.Find("Container_Content/BG/PlatformIcon").gameObject.SetActive(false);
            _creatorTpl.transform.Find("Container_Content/Text_ShareCode").gameObject.SetActive(false);
            var chip = _creatorTpl.transform.Find("Container_Content");
            chip.gameObject.SetActive(true);
            var chipRt = chip.GetComponent<RectTransform>();
            chipRt.anchorMin = chipRt.anchorMax = chipRt.pivot = new Vector2(0f, 0.5f);
            chipRt.anchoredPosition = Vector2.zero;

            _cardTpl = UnityEngine.Object.Instantiate(_tilePrefab, _root.transform);
            _cardTpl.SetActive(false);
            ShowRoundsPopup.StripBindings(new HashSet<string> { "ShowSelectorShowTileViewModel", "ViewModelDataBinding", "GraphicColorBinding", "TagViewModel", "RemoteImage",
                "RankedProgressionRankProgressViewModel", "RankSchemaViewModel", "DevComments", "ActiveChildBinding", "RemainingTimeViewModel" }, _cardTpl);

            var h = _cardTpl.transform.Find("ShowTileHolder");
            foreach (var p in new[] { "NestedPanel/PanelLeft", "NestedPanel/PanelRight", "NestedPanel/MainPanel/ShowScreenshot_Mask/URL_ShowScreenshot",
                "NestedPanel/MainPanel/ShowScreenshot_Mask/Generic_UI_UPProgresion_Container", "ShowNotAvailableYet_Overlay", "TargetConditionNotMet_Overlay",
                "Notification", "ShowTypeTags", "DefaultTags1/DifficultyTag", "TagsGroup/Generic_UI_CardTagPill_Prefab", "TagsGroup/Extra_UI_CardTagPill",
                "DefaultTags1/GameMode/Panel/Panel_Disabled", "DefaultTags1/SquadSize/Panel/Panel_Disabled", "DefaultTags2/LikeCount/Panel/Panel_Disabled" })
                h.Find(p).gameObject.SetActive(false);
            foreach (var p in new[] { "DefaultTags1/GameMode", "DefaultTags1/SquadSize", "DefaultTags2/LikeCount", "NestedPanel/MainPanel/ShowScreenshot_Mask/LoadingSpinner" })
                h.Find(p).gameObject.SetActive(true);
            var shot = h.Find("NestedPanel/MainPanel/ShowScreenshot_Mask/DLC_ShowScreenshot");
            ShowRoundsPopup.Stretch(shot, 0, 0, 0, 0);
            shot.GetComponent<RawImage>().color = new Color(0f, 0f, 0f, 0.25f);
            var main = h.Find("NestedPanel/MainPanel");
            var name = main.Find("ShowTitle").GetComponent<TextMeshProUGUI>();
            name.enableWordWrapping = false;
            name.overflowMode = TextOverflowModes.Ellipsis;
            name.margin = Vector4.zero;
            name.alignment = TextAlignmentOptions.MidlineLeft;
            name.enableAutoSizing = true;
            name.fontSizeMin = 20f;
            name.fontSizeMax = 32f;
            ShowRoundsPopup.Place(name.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(20, -8), new Vector2(-220, 46));

            var code = UnityEngine.Object.Instantiate(h.Find("DefaultTags1/GameMode").gameObject, main);
            code.name = "BFG_Code";
            ShowRoundsPopup.Place(code.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -16), code.GetComponent<RectTransform>().sizeDelta);

            var stats = new GameObject("BFG_Stats");
            stats.transform.SetParent(main, false);
            ShowRoundsPopup.Place(stats.AddComponent<RectTransform>(), new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(18, -58), new Vector2(-36, 36));
            var row = stats.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 8f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = row.childForceExpandWidth = row.childForceExpandHeight = false;
            h.Find("DefaultTags1/SquadSize").SetParent(stats.transform, false);
            h.Find("DefaultTags1/GameMode").SetParent(stats.transform, false);
            var like = h.Find("DefaultTags2/LikeCount");
            like.SetParent(stats.transform, false);
            var dislike = UnityEngine.Object.Instantiate(like.gameObject, stats.transform);
            dislike.name = "BFG_Dislikes";
            dislike.transform.Find("LikeIcon").localScale = new Vector3(1f, -1f, 1f);
            h.Find("DefaultTags1").gameObject.SetActive(false);
            h.Find("DefaultTags2").gameObject.SetActive(false);

            var tags = h.Find("TagsGroup");
            ShowRoundsPopup.Place(tags, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-16, 18), new Vector2(-32, 30));
            tags.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.LowerRight;
        }

        void BuildTab(GameObject tpl, Transform parent, ToggleGroup group, int sort)
        {
            var t = UnityEngine.Object.Instantiate(tpl, parent).transform;
            var tt = t.Find("PrivateLobbyShowTab/Content/SubMenuTabText").GetComponent<TextMeshProUGUI>();
            tt.text = LocalizationService.Get(SortKeys[sort]);
            float w = Mathf.Max(170f, tt.GetPreferredValues().x + 44f);
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = 65f;
            t.GetComponent<RectTransform>().sizeDelta = new Vector2(w, 65f);
            foreach (var rt in t.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt.transform == t || rt.parent.name == "Content") continue;
                float inset = rt.name == "Content" ? 12f : rt.parent.name == "SubMenuTabActive" && rt.name == "Panel_Fill" ? -1f : 0f;
                ShowRoundsPopup.Stretch(rt, inset, inset, inset, inset);
            }
            var tog = t.GetComponentInChildren<Toggle>(true);
            ShowRoundsPopup.Mute(tog.onValueChanged);
            tog.isOn = false;
            tog.group = group;
            tog.onValueChanged.AddListener(new Action<bool>(on => { if (on) Select(sort); }));
            t.gameObject.SetActive(true);
            _tabs.Add(tog);
        }

        static string Fmt(long n) => n >= 1000000 ? (n / 100000 / 10.0).ToString("0.#") + "M" : n >= 1000 ? (n / 100 / 10.0).ToString("0.#") + "K" : n.ToString();

        void SetTitle()
        {
            string status = _result == null || _result.Id != _queryId
                ? LocalizationService.Get("level_browser.searching")
                : LocalizationService.Format("level_browser.count", _result.Total);
            _title.text = LocalizationService.Get("level_browser.title") + "  <size=50%>" + status + "   " + LocalizationService.Get("level_browser.powered") + "</size>";
        }

        void StartQuery(string q)
        {
            _lastQuery = q;
            int id = ++_queryId;
            bool byCreator = q.StartsWith("@");
            string name = byCreator || q.Length == 0 ? "*" : q;
            string url = string.Format(SearchUrl, Uri.EscapeDataString(name));
            if (byCreator) url += "&author=" + Uri.EscapeDataString("\"" + q.Substring(1).Trim() + "\"");
            Task.Run(async () =>
            {
                var r = new Result { Id = id };
                try { Parse(await Http.GetStringAsync(url), r); }
                catch (Exception ex) { r.Error = ex.Message; }
                Interlocked.Exchange(ref _done, r);
            });
            SetTitle();
        }

        static void Parse(string json, Result r)
        {
            var all = new List<Lvl>();
            foreach (var lv in JsonUtil.Parse(json).GetProperty("level_data").EnumerateArray())
            {
                var l = new Lvl
                {
                    Name = System.Net.WebUtility.HtmlDecode(lv.GetProperty("common_name").GetString() ?? "").Trim(),
                    Code = lv.GetProperty("share_code").GetString(),
                    Author = System.Net.WebUtility.HtmlDecode(lv.GetProperty("author_name").GetString() ?? ""),
                    Likes = lv.GetProperty("likes").GetInt64(),
                    Dislikes = lv.GetProperty("dislikes").GetInt64(),
                    Plays = lv.GetProperty("playcount").GetInt64(),
                    Players = lv.GetProperty("max_players").GetInt32(),
                };
                foreach (var t in lv.GetProperty("tags").EnumerateArray()) l.Tags.Add(t.GetString());
                all.Add(l);
            }
            r.Total = all.Count;
            r.Sorted = new List<Lvl>[SortKeys.Length];
            Comparison<Lvl>[] by =
            {
                (a, b) => b.Plays.CompareTo(a.Plays),
                (a, b) => b.Likes.CompareTo(a.Likes),
                (a, b) => ((double)b.Likes / Math.Max(1, b.Likes + b.Dislikes)).CompareTo((double)a.Likes / Math.Max(1, a.Likes + a.Dislikes)),
            };
            for (int s = 0; s < by.Length; s++)
            {
                var copy = new List<Lvl>(all);
                copy.Sort(by[s]);
                if (copy.Count > MaxTiles) copy.RemoveRange(MaxTiles, copy.Count - MaxTiles);
                r.Sorted[s] = copy;
            }
        }

        void Select(int sort)
        {
            sort = (sort + SortKeys.Length) % SortKeys.Length;
            if (sort == _cur) return;
            _cur = sort;
            if (!_tabs[_cur].isOn) _tabs[_cur].isOn = true;
            Rebuild();
        }

        void Rebuild()
        {
            _selectFirst = _sel >= 0 && !_search.Typing;
            _sel = -1;
            foreach (var g in _cards)
            {
                var cell = g.transform.parent.gameObject;
                cell.SetActive(false);
                UnityEngine.Object.Destroy(cell);
            }
            _cards.Clear();
            _shown.Clear();
            _needCover.Clear();
            _nested.Clear();
            _built = 0;
            if (_result?.Sorted != null) _shown.AddRange(_result.Sorted[_cur]);
            _scroll.StopMovement();
            _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, 0f);
            SetTitle();
        }

        void BuildSome()
        {
            int end = Math.Min(_shown.Count, _built + TilesPerFrame);
            for (; _built < end; _built++)
            {
                var l = _shown[_built];
                var cell = new GameObject("Cell");
                cell.AddComponent<RectTransform>().SetParent(_content, false);
                var card = UnityEngine.Object.Instantiate(_cardTpl, cell.transform);
                ShowRoundsPopup.Place(card.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(TileW, TileH));
                card.transform.localScale = new Vector3(CardH / TileH, CardH / TileH, 1f);
                card.SetActive(true);
                var h = card.transform.Find("ShowTileHolder");
                var main = h.Find("NestedPanel/MainPanel");
                main.Find("ShowTitle").GetComponent<TextMeshProUGUI>().text = l.Name;
                main.Find("BFG_Code/GameMode_Text").GetComponent<TextMeshProUGUI>().text = l.Code;
                main.Find("BFG_Stats/SquadSize/SquadSize_Text").GetComponent<TextMeshProUGUI>().text = l.Players.ToString();
                main.Find("BFG_Stats/GameMode/GameMode_Text").GetComponent<TextMeshProUGUI>().text = LocalizationService.Format("level_browser.plays", Fmt(l.Plays));
                main.Find("BFG_Stats/LikeCount/LikeCount_Text").GetComponent<TextMeshProUGUI>().text = Fmt(l.Likes);
                main.Find("BFG_Stats/BFG_Dislikes/LikeCount_Text").GetComponent<TextMeshProUGUI>().text = Fmt(l.Dislikes);
                LayoutRebuilder.ForceRebuildLayoutImmediate(main.Find("BFG_Stats").GetComponent<RectTransform>());
                LayoutRebuilder.ForceRebuildLayoutImmediate(main.Find("BFG_Code").GetComponent<RectTransform>());

                var pillTpl = h.Find("TagsGroup/Extra_UI_CardTagPill").gameObject;
                for (int t = 0; t < l.Tags.Count && t < 2; t++)
                {
                    var pill = UnityEngine.Object.Instantiate(pillTpl, pillTpl.transform.parent);
                    pill.SetActive(true);
                    var text = pill.transform.Find("Text").GetComponent<TextMeshProUGUI>();
                    text.text = System.Net.WebUtility.HtmlDecode(l.Tags[t]);
                    float w = text.GetPreferredValues().x + 32f;
                    foreach (var le in pill.GetComponentsInChildren<LayoutElement>(true)) { le.preferredWidth = w; le.minWidth = w; }
                    var prt = pill.GetComponent<RectTransform>();
                    prt.sizeDelta = new Vector2(w, prt.sizeDelta.y);
                }

                var creator = UnityEngine.Object.Instantiate(_creatorTpl, main);
                creator.transform.SetSiblingIndex(main.Find("Panel_Outline").GetSiblingIndex());
                ShowRoundsPopup.Place(creator.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(34f, 14f), new Vector2(675f, 55f));
                creator.transform.localScale = new Vector3(0.62f, 0.62f, 1f);
                creator.transform.Find("Container_Content/BG/Text_Creator").GetComponent<TextMeshProUGUI>().text = l.Author;
                creator.SetActive(true);
                _nested.Add(h.Find("NestedPanel"));

                var code = l.Code;
                card.GetComponent<UIButtonMenuElement>()._onClick.AddListener(new Action(() => Pick(code)));
                _needCover.Add(_cards.Count);
                _cards.Add(card);
            }
            if (_selectFirst && _cards.Count > 0 && !_search.Typing) { _selectFirst = false; SelectCard(0, true); }
        }

        void FillCovers()
        {
            for (int i = _needCover.Count - 1; i >= 0; i--)
            {
                int idx = _needCover[i];
                var tex = ShowTileDislikesTweak.CoverFor(_shown[idx].Code);
                if (tex == null) continue;
                _needCover.RemoveAt(i);
                var ri = _cards[idx].transform.Find("ShowTileHolder/NestedPanel/MainPanel/ShowScreenshot_Mask/DLC_ShowScreenshot").GetComponent<RawImage>();
                ri.transform.parent.Find("LoadingSpinner").gameObject.SetActive(false);
                var box = ri.rectTransform.rect;
                float boxAspect = box.width / box.height, aspect = tex.width / (float)tex.height;
                var uv = new Rect(0, 0, 1, 1);
                if (aspect > boxAspect) { float k = boxAspect / aspect; uv.x = (1 - k) / 2; uv.width = k; }
                else { float k = aspect / boxAspect; uv.y = (1 - k) / 2; uv.height = k; }
                ri.texture = tex;
                ri.uvRect = uv;
                ri.color = Color.white;
            }
        }

        void SelectCard(int i, bool scroll)
        {
            if (_cards.Count == 0) { SelectSearch(); return; }
            int next = Mathf.Clamp(i, 0, _cards.Count - 1);
            if (_sel == next) return;
            if (_sel >= 0 && _sel < _cards.Count) _cards[_sel].GetComponent<UIButtonMenuElement>().Deselect();
            _sel = next;
            _es.SetSelectedGameObject(_root);
            _search.Deselect();
            _cards[_sel].GetComponent<UIButtonMenuElement>().Select(true);
            if (!scroll) return;

            var grid = _content.GetComponent<GridLayoutGroup>();
            float top = grid.padding.top + (_sel / Columns) * (CardH + grid.spacing.y), bottom = top + CardH;
            float vh = _scroll.viewport.rect.height, y = _content.anchoredPosition.y;
            if (top - grid.padding.top < y) y = top - grid.padding.top;
            if (bottom + grid.padding.bottom > y + vh) y = bottom + grid.padding.bottom - vh;
            _scroll.StopMovement();
            _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, Mathf.Clamp(y, 0f, Mathf.Max(0f, _content.rect.height - vh)));
        }

        void SelectSearch()
        {
            if (_sel >= 0 && _sel < _cards.Count) _cards[_sel].GetComponent<UIButtonMenuElement>().Deselect();
            _sel = -1;
            _search.Select(_es);
        }

        void OnQueryChanged(string value) => _queryAt = Time.unscaledTime + 0.45f;

        void ConfirmSelected()
        {
            if (_sel >= 0) Pick(_shown[_sel].Code);
            else _search.StartTyping();
        }

        void SearchCreator()
        {
            if (_sel < 0) return;
            string q = "@" + _shown[_sel].Author;
            _search.Field.SetTextWithoutNotify(q);
            _search.Redraw();
            _queryAt = -1f;
            StartQuery(q);
        }

        void CopySelected()
        {
            if (_sel < 0) return;
            GUIUtility.systemCopyBuffer = _shown[_sel].Code;
            Plugin.Log.LogInfo($"level browser: copied {_shown[_sel].Code}");
        }

        void Pick(string code)
        {
            _pick = code;
            BeginClose();
        }

        void BeginClose()
        {
            _closing = true;
            _root.SetActive(false);
        }

        bool Repeat(int slot, bool held)
        {
            bool was = _held[slot];
            _held[slot] = held;
            if (!held) return false;
            if (!was) { _repeatAt[slot] = Time.unscaledTime + 0.4f; return true; }
            if (Time.unscaledTime < _repeatAt[slot]) return false;
            _repeatAt[slot] = Time.unscaledTime + 0.12f;
            return true;
        }

        static bool Kb(KeyCode k) => KeybindService.KeyHeld(k);

        bool ReleaseHeld => Kb(KeyCode.Escape) || Kb(KeyCode.Return) || Kb(KeyCode.Space)
            || NavPromptCore.ElementHeldByName(ShowRoundsPopup.BackNames) || NavPromptCore.ElementHeldByName(ShowRoundsPopup.SubmitNames);

        internal bool Tick()
        {
            if (_root == null) { Dispose(); return false; }
            if (_closing)
            {
                if (ReleaseHeld) return true;
                Dispose();
                return false;
            }

            FGInputLockService.SetScreenLock(true);
            bool pad = Rewired.ReInput.controllers.GetLastActiveControllerType() == Rewired.ControllerType.Joystick;
            foreach (var g in _kbNav) g.SetActive(!pad);
            foreach (var g in _padNav) g.SetActive(pad);

            var done = Interlocked.Exchange(ref _done, null);
            if (done != null && done.Id == _queryId)
            {
                if (done.Error != null) Plugin.Log.LogWarning($"cloudseeker search for '{_lastQuery}' failed: {done.Error}");
                else { _result = done; Rebuild(); }
                SetTitle();
            }
            BuildSome();
            if (Time.frameCount % 10 == 0) FillCovers();
            float settle = 400f * Time.unscaledDeltaTime;
            for (int i = 0; i < _nested.Count; i++)
            {
                var p = _nested[i].localPosition;
                if (p.y == 0f || RectTransformUtility.RectangleContainsScreenPoint(_cards[i].GetComponent<RectTransform>(), Input.mousePosition, null)) continue;
                _nested[i].localPosition = Vector3.MoveTowards(p, Vector3.zero, settle);
            }
            if (_queryAt > 0f && Time.unscaledTime >= _queryAt)
            {
                _queryAt = -1f;
                string q = _search.Field.text.Trim();
                if ((q.Length == 0 || q.Length >= MinQuery || (q.StartsWith("@") && q.Length > 1)) && q != _lastQuery) StartQuery(q);
            }

            if (_search.TickTyping(_es)) return true;

            var cur = _es.currentSelectedGameObject;
            if (cur == null || !cur.transform.IsChildOf(_root.transform))
            {
                if (_sel >= 0) _es.SetSelectedGameObject(_root);
                else SelectSearch();
            }

            var mouse = Input.mousePosition;
            if ((mouse - _lastMouse).sqrMagnitude > 1f)
            {
                _lastMouse = mouse;
                if (RectTransformUtility.RectangleContainsScreenPoint(_scroll.viewport, mouse, null))
                    for (int i = 0; i < _cards.Count; i++)
                        if (i != _sel && RectTransformUtility.RectangleContainsScreenPoint(_cards[i].GetComponent<RectTransform>(), mouse, null)) { SelectCard(i, false); break; }
            }

            float lx = NavPromptCore.AxisByName(ShowRoundsPopup.StickXNames), ly = NavPromptCore.AxisByName(ShowRoundsPopup.StickYNames);
            bool up = Repeat(0, Kb(KeyCode.UpArrow) || Kb(KeyCode.W) || NavPromptCore.ElementHeldByName(ShowRoundsPopup.UpNames) || ly > 0.5f);
            bool down = Repeat(1, Kb(KeyCode.DownArrow) || Kb(KeyCode.S) || NavPromptCore.ElementHeldByName(ShowRoundsPopup.DownNames) || ly < -0.5f);
            bool left = Repeat(2, Kb(KeyCode.LeftArrow) || Kb(KeyCode.A) || NavPromptCore.ElementHeldByName(ShowRoundsPopup.LeftNames) || lx < -0.5f);
            bool right = Repeat(3, Kb(KeyCode.RightArrow) || Kb(KeyCode.D) || NavPromptCore.ElementHeldByName(ShowRoundsPopup.RightNames) || lx > 0.5f);
            if (Time.frameCount - _openFrame < 2) return true;

            if (KeybindService.KeyDown(KeyCode.Escape) || NavPromptCore.ElementDownByName(ShowRoundsPopup.BackNames)) { BeginClose(); return true; }
            if (KeybindService.KeyDown(KeyCode.Q) || NavPromptCore.ElementDownByName(ShowRoundsPopup.PrevNames)) Select(_cur - 1);
            if (KeybindService.KeyDown(KeyCode.E) || NavPromptCore.ElementDownByName(ShowRoundsPopup.NextNames)) Select(_cur + 1);
            if (CopyCodePrompt.CopyPressed()) CopySelected();
            if (KeybindService.KeyDown(KeyCode.F) || NavPromptCore.PollActionDirect(RewiredConsts.Action.Menu_Favourite, null, true)) SearchCreator();
            if (KeybindService.KeyDown(KeyCode.Return) || KeybindService.KeyDown(KeyCode.Space) || NavPromptCore.ElementDownByName(ShowRoundsPopup.SubmitNames))
            {
                ConfirmSelected();
                return true;
            }

            if (_sel < 0)
            {
                if (down) SelectCard(0, true);
            }
            else
            {
                if (up) { if (_sel - Columns >= 0) SelectCard(_sel - Columns, true); else SelectSearch(); }
                if (down && _sel + Columns < _cards.Count) SelectCard(_sel + Columns, true);
                else if (down && _sel / Columns < (_cards.Count - 1) / Columns) SelectCard(_cards.Count - 1, true);
                if (left && _sel % Columns > 0) SelectCard(_sel - 1, true);
                if (right && _sel % Columns < Columns - 1 && _sel + 1 < _cards.Count) SelectCard(_sel + 1, true);
            }

            float y = NavPromptCore.AxisByName(ShowRoundsPopup.ScrollNames);
            if (Mathf.Abs(y) > 0.2f)
            {
                float span = Mathf.Max(1f, _content.rect.height - _scroll.viewport.rect.height);
                _scroll.verticalNormalizedPosition = Mathf.Clamp01(_scroll.verticalNormalizedPosition + y * 1400f * Time.unscaledDeltaTime / span);
            }
            return true;
        }

        internal void Dispose()
        {
            _search?.Drop();
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _queryId++;
            foreach (var b in _muted) if (b != null) b.enabled = true;
            _muted.Clear();
            foreach (var key in Prompts) NavPromptInjection.Remove(key);
            NavPromptInjection.Solo(null);
            if (_navCanvas != null) _navCanvas.sortingOrder = _navOrder;
            _navCanvas = null;
            FGInputLockService.SetScreenLock(false);
            if (_prevSel != null) _es.SetSelectedGameObject(_prevSel);
            if (_pick == null || _lobby == null) return;
            LevelBrowserTweak.Instance.LoadPicked(_lobby, _pick);
            _pick = null;
        }
    }
}
