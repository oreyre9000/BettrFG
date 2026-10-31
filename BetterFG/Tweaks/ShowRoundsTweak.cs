using System;
using System.Collections.Generic;
using BetterFG.Core;
using BetterFG.Services;
using Round = FG.Common.CMS.Round;
using RoundPool = FG.Common.CMS.RoundPool;
using FGClient;
using FGClient.UI.Core;
using Mediatonic.Tools.MVVM;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace BetterFG.Tweaks
{
    public class ShowRoundsTweak : BfgTweak
    {
        public ShowRoundsTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "show_rounds";
        public override string TweakLabel => "tweak.show_rounds";
        public override string TweakTooltip => "tweak.show_rounds_tip";
        public override bool DefaultEnabled => true;

        public static ShowRoundsTweak Instance { get; private set; }
        void Awake() => Instance = this;

        internal static readonly string[] OpenPadNames = { "Right Stick Button", "Right Stick Click", "Right Stick", "R3", "RS" };

        private string _showName;
        private List<List<Round>> _stages;
        private NavigationPromptData _data;
        private bool _injected;
        private ShowRoundsPopup _popup;

        public static void OnShowPreviewed(ShowSelectorShow show)
        {
            var inst = Instance;
            if (inst == null || !inst.IsEnabled) return;
            inst._stages = null;
            if (show == null || !string.IsNullOrEmpty(show.ShareCode)) return;
            var pool = show.DefaultEpisode?.DefaultRoundPool;
            if (pool == null) return;
            inst._stages = ShowRoundsPopup.BuildStages(pool);
            inst._showName = show.ShowData.ShowName?.Text ?? show.Id;
        }

        public static void OnShowPreviewCleared()
        {
            if (Instance != null) Instance._stages = null;
        }

        void Update()
        {
            if (_popup != null)
            {
                if (_popup.Tick()) return;
                _popup = null;
            }

            bool live = _stages != null;
            if (live != _injected)
            {
                if (live)
                {
                    if (_data == null)
                        _data = NavPromptInjection.BuildData(NavPrompt.Report, LocalizationService.Get("show_rounds.open"), "bfg_show_rounds_open", -1, -1);
                    NavPromptInjection.Add(NavPromptInjection.ShowRounds, Open, _data,
                        btn => NavPromptCore.ApplyOwnGlyphByElement(btn, KeyCode.N, NavPromptCore.CurrentPadElementByName(OpenPadNames)));
                }
                else NavPromptInjection.Remove(NavPromptInjection.ShowRounds);
                _injected = live;
            }
            if (live && (KeybindService.KeyDown(KeyCode.N) || NavPromptCore.ElementDownByName(OpenPadNames)))
                Open();
        }

        void Open()
        {
            if (_stages == null || _popup != null) return;
            _popup = ShowRoundsPopup.Open(_showName, _stages);
        }

        public override void DisableTweak()
        {
            _popup?.Dispose();
            _popup = null;
            if (_injected) NavPromptInjection.Remove(NavPromptInjection.ShowRounds);
            _injected = false;
            _stages = null;
        }
    }

    internal sealed class ShowRoundsPopup
    {
        const int Columns = 4;
        const float CardW = 320f, CardH = 238f;
        internal static readonly string[] BackNames = { "B", "Circle" };
        internal static readonly string[] SubmitNames = { "A", "Cross" };
        internal static readonly string[] PrevNames = { "Left Shoulder", "LB", "L1" };
        internal static readonly string[] NextNames = { "Right Shoulder", "RB", "R1" };
        internal static readonly string[] UpNames = { "D-Pad Up", "DPad Up", "Hat Up" };
        internal static readonly string[] DownNames = { "D-Pad Down", "DPad Down", "Hat Down" };
        internal static readonly string[] LeftNames = { "D-Pad Left", "DPad Left", "Hat Left" };
        internal static readonly string[] RightNames = { "D-Pad Right", "DPad Right", "Hat Right" };
        internal static readonly string[] StickXNames = { "Left Stick X", "Left Stick Horizontal" };
        internal static readonly string[] StickYNames = { "Left Stick Y", "Left Stick Vertical" };
        internal static readonly string[] ScrollNames = { "Right Stick Y", "Right Stick Vertical" };

        internal static readonly HashSet<string> BindingTypes = new HashSet<string> { "ActiveBinding", "CollectionBinding", "TextBinding", "RawImageBinding", "ImageBinding", "TMPTextBinding", "PropertyToPropertyBinding",
            "PropertyToMethodBinding", "LocalisedStaticLabel", "EventTrigger", "ShowSelectorCarrouselInputHandler", "DlcImageViewModel", "PrivateLobbyShowInfoViewModel",
            "IsUsingControllerViewModel", "AspectRatioFitter", "PrivateLobbyShowListViewModel", "PrivateLobbiesShowTabsNavigation", "PrivateLobbyShowTabViewModel",
            "PrivateLobbyShowListEntryViewModel", "ScrollRectDragStatusListener" };

        internal static GameObject PopupPrefab => _popupPrefab;
        internal static GameObject TabPrefab => _tabPrefab;
        internal static GameObject RowPrefab => _rowPrefab;

        static GameObject _popupPrefab, _tabPrefab, _rowPrefab;

        readonly List<List<Round>> _stages;
        readonly int[] _matches;
        readonly EventSystem _es;
        readonly GameObject _prevSel;
        GameObject _root;
        Transform _container;
        ScrollRect _scroll;
        RectTransform _content;
        GameObject _cardTpl;
        GameObject _searchGo;
        PopupSearchRow _search;
        readonly List<Toggle> _tabs = new List<Toggle>();
        readonly List<TextMeshProUGUI> _tabTexts = new List<TextMeshProUGUI>();
        readonly List<GameObject> _cards = new List<GameObject>();
        readonly List<string> _cardNames = new List<string>();
        readonly List<string> _cardKeys = new List<string>();
        readonly List<GameObject> _visible = new List<GameObject>();
        readonly Dictionary<string, AsyncOperationHandle<Sprite>> _sprites = new Dictionary<string, AsyncOperationHandle<Sprite>>();
        readonly Dictionary<int, float> _repeatAt = new Dictionary<int, float>();
        readonly bool[] _held = new bool[4];
        GameObject[] _kbNav, _padNav;
        int _cur = -1;
        int _sel;
        bool _closing;
        int _openFrame;

        internal static List<List<Round>> BuildStages(RoundPool pool)
        {
            var stages = pool.Stages;
            if (stages == null || stages.Length == 0) return null;
            int maxStage = 0;
            for (int i = 0; i < stages.Length; i++)
            {
                var only = stages[i].CanOnlyBeOnTheseStages;
                if (only != null) for (int j = 0; j < only.Length; j++) maxStage = Math.Max(maxStage, only[j]);
                var not = stages[i].CannotBeOnTheseStages;
                if (not != null) for (int j = 0; j < not.Length; j++) maxStage = Math.Max(maxStage, not[j]);
            }
            var result = new List<List<Round>>();
            void Collect(int n, Func<int, Round> at, bool final)
            {
                var list = new List<Round>();
                var seen = new HashSet<string>();
                for (int i = 0; i < n; i++)
                {
                    var r = at(i);
                    if (r != null && IsFinal(r) == final && !string.IsNullOrEmpty(r.DisplayNameUnindented) && seen.Add(r.DisplayNameUnindented)) list.Add(r);
                }
                list.Sort((a, b) => string.Compare(a.DisplayNameUnindented, b.DisplayNameUnindented, StringComparison.OrdinalIgnoreCase));
                if (list.Count > 0) result.Add(list);
            }
            for (int s = 0; s <= maxStage; s++)
            {
                var poss = pool.GetAllPossibleRoundsForStage(s);
                Collect(poss.Count, i => poss[i].Round, false);
            }
            Collect(stages.Length, i => stages[i].Round, true);
            return result.Count == 0 ? null : result;
        }

        static bool IsFinal(Round r) => r.GameRules?.IsFinalRound ?? false;

        internal static bool FindPrefabs()
        {
            if (_popupPrefab != null) return true;
            var screens = Addressables.LoadAssetAsync<UIConfig>("Data/UIConfig_Common_Shared.asset").WaitForCompletion().Screens;
            for (int i = 0; i < screens.Count; i++)
                if (screens[i].name == "Prefab_UI_PrivateLobbyShowSelect") _popupPrefab = screens[i].gameObject;
            if (_popupPrefab == null) { Plugin.Log.LogWarning("Prefab_UI_PrivateLobbyShowSelect gone from the common ui config?"); return false; }
            var c = _popupPrefab.transform.Find("Container");
            _tabPrefab = c.Find("TabCarrouselShows/TabsContent").GetComponent<CollectionBinding>()._prefab.gameObject;
            _rowPrefab = c.Find("ShowsScrollList/ShowsListViewport/ShowsListContent").GetComponent<CollectionBinding>()._prefab.gameObject;
            return true;
        }

        internal static void StripBindings(params GameObject[] roots) => StripBindings(null, roots);

        internal static void StripBindings(HashSet<string> extra, params GameObject[] roots)
        {
            for (int pass = 0; pass < 2; pass++)
                foreach (var go in roots)
                {
                    var all = go.GetComponentsInChildren<Component>(true);
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i] == null) continue;
                        string type = all[i].GetIl2CppType().Name;
                        if (BindingTypes.Contains(type) || (extra != null && extra.Contains(type))) UnityEngine.Object.DestroyImmediate(all[i]);
                    }
                }
        }

        internal static ShowRoundsPopup Open(string showName, List<List<Round>> stages) => FindPrefabs() ? new ShowRoundsPopup(showName, stages) : null;

        ShowRoundsPopup(string showName, List<List<Round>> stages)
        {
            _stages = stages;
            _matches = new int[stages.Count];
            _es = EventSystem.current;
            _prevSel = _es.currentSelectedGameObject;
            _openFrame = Time.frameCount;

            _root = new GameObject("BFG_ShowRounds");
            _root.SetActive(false);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            _root.AddComponent<GraphicRaycaster>();

            var popup = UnityEngine.Object.Instantiate(_popupPrefab, _root.transform);
            var tabTpl = UnityEngine.Object.Instantiate(_tabPrefab, _root.transform);
            _searchGo = UnityEngine.Object.Instantiate(_rowPrefab, _root.transform);
            _container = popup.transform.Find("Container");
            var c = _container;

            foreach (var p in new[] { "CodeInputGroup", "MissingInfo_Text", "Mobile Confirm Button", "Title/EnterCodeText", "ShowInfo/ShowDetailsLayout/Code", "ShowInfo/ShowDetailsLayout/NameTag",
                "ShowInfo/ShowDetailsLayout/ShowTagsFirstRow", "ShowInfo/ShowDetailsLayout/ShowTagsSecondRow", "ShowInfo/ShowDetailsLayout/Description", "ShowInfo/ShowDetailsLayout/Title_Text",
                "ShowInfo/PublishedDate_Text", "ShowsScrollList/ShowsListViewport/ShowsListContent/Tooltip" })
                UnityEngine.Object.DestroyImmediate(c.Find(p).gameObject);
            foreach (var p in new[] { "Button", "Favourite", "SubMenuFavouriteBacking", "SubMenuFavouriteIcon", "Generic_UI_PipNotification_Prefab" })
                UnityEngine.Object.DestroyImmediate(_searchGo.transform.Find(p).gameObject);

            StripBindings(popup, tabTpl, _searchGo);

            Place(c, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(1500, 880));

            var title = c.Find("Title");
            Place(title, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -24), title.GetComponent<RectTransform>().rect.size);
            int total = 0;
            foreach (var s in stages) total += s.Count;
            title.Find("ShowInfoText").GetComponent<TextMeshProUGUI>().text = showName + "  <size=50%>"
                + string.Format(LocalizationService.Get("show_rounds.summary"), stages.Count, total) + "</size>";

            BuildSearch();

            _scroll = c.Find("ShowsScrollList").GetComponent<ScrollRect>();
            Stretch(_scroll.transform, 40, 32, 40, 232);
            Stretch(_scroll.viewport, 0, 0, 44, 0);
            Place(c.Find("ShowsScrollList/TabScrollbar"), new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), Vector2.zero, new Vector2(24, -24));
            _content = _scroll.content;

            BuildCardTemplate();

            UnityEngine.Object.DestroyImmediate(_content.GetComponent<VerticalLayoutGroup>());
            var grid = _content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(CardW, CardH);
            grid.spacing = new Vector2(24, 24);
            grid.padding = new RectOffset(14, 14, 14, 14);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Columns;
            grid.childAlignment = TextAnchor.UpperCenter;

            var carousel = c.Find("TabCarrouselShows");
            Place(carousel, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -142), new Vector2(0, 66));
            var tabsRoot = carousel.Find("TabsContent");
            var tabGroup = tabsRoot.GetComponent<ToggleGroup>();
            tabGroup.allowSwitchOff = false;
            tabTpl.SetActive(false);
            tabTpl.transform.Find("PrivateLobbyShowTab/Content/SubMenuCreativeIcon").gameObject.SetActive(false);
            tabTpl.transform.Find("PrivateLobbyShowTab/Content/SubMenuFavouriteIcon").gameObject.SetActive(false);
            for (int s = 0; s < stages.Count; s++) BuildTab(tabTpl, tabsRoot, tabGroup, s);
            carousel.Find("TabCarrouselNavigationLeft").GetComponent<Button>().onClick.AddListener(new Action(() => Select(_cur - 1)));
            carousel.Find("TabCarrouselNavigationRight").GetComponent<Button>().onClick.AddListener(new Action(() => Select(_cur + 1)));
            _kbNav = new[] { carousel.Find("TabCarrouselNavigationLeft").gameObject, carousel.Find("TabCarrouselNavigationRight").gameObject };
            _padNav = new[] { carousel.Find("TabCarrouselNavigationLeft_Controller").gameObject, carousel.Find("TabCarrouselNavigationRight_Controller").gameObject };

            foreach (var list in stages)
                foreach (var r in list)
                    if (!string.IsNullOrEmpty(r.LoadingScreenName) && !_sprites.ContainsKey(r.LoadingScreenName))
                        _sprites[r.LoadingScreenName] = Addressables.LoadAssetAsync<Sprite>(r.LoadingScreenName);

            _root.SetActive(true);
            c.GetComponent<TweenOnPopup>().BeginTween();
            _tabs[0].isOn = true;
            Select(0);
            LayoutRebuilder.ForceRebuildLayoutImmediate(tabsRoot.GetComponent<RectTransform>());
            LayoutRebuilder.ForceRebuildLayoutImmediate(carousel.GetComponent<RectTransform>());
            _search.Redraw();
            FGInputLockService.SetScreenLock(true);
            Plugin.Log.LogInfo($"rounds popup for {showName}: {stages.Count} rounds, {total} levels, {_sprites.Count} splashes to load");
        }

        internal static RectTransform Place(Transform t, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var r = t.GetComponent<RectTransform>();
            r.anchorMin = aMin; r.anchorMax = aMax; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = size;
            return r;
        }

        internal static void Stretch(Transform t, float l, float b, float r, float top)
        {
            var rt = t.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -top);
        }

        internal static void Mute(UnityEngine.Events.UnityEventBase e)
        {
            for (int i = 0; i < e.GetPersistentEventCount(); i++) e.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
        }

        void BuildSearch()
        {
            _searchGo.name = "BFG_LevelSearch";
            _searchGo.transform.SetParent(_container, false);
            Place(_searchGo.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-48, -34), new Vector2(425, 62));
            _search = new PopupSearchRow(_searchGo, "show_rounds.search", OnQueryChanged, SelectSearch);
        }

        void BuildCardTemplate()
        {
            _cardTpl = _content.Find("EnterCodeButton").gameObject;
            _cardTpl.transform.SetParent(_root.transform, false);
            _cardTpl.SetActive(false);
            _cardTpl.name = "Card";
            _cardTpl.GetComponent<RectTransform>().sizeDelta = new Vector2(CardW, CardH);
            _cardTpl.transform.Find("Content/ControlGlyphButton").gameObject.SetActive(false);

            var info = _container.Find("ShowInfo");
            var img = info.Find("ShowImage");
            img.SetParent(_cardTpl.transform, false);
            img.SetSiblingIndex(2);
            Stretch(img, 12, 58, 12, 12);
            Stretch(img.Find("ShowSelectorImageMask"), 0, 0, 0, 0);
            Stretch(img.Find("ShowSelectorOutline"), 0, 0, 0, 0);
            Stretch(img.Find("ShowSelectorImageMask/ShowSelectorImage"), 0, 0, 0, 0);
            var content = _cardTpl.transform.Find("Content");
            Place(content, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(-24, 46));
            var name = content.Find("Text").GetComponent<TextMeshProUGUI>();
            name.enableAutoSizing = true;
            name.fontSizeMin = 16;
            name.fontSizeMax = 28;
            name.enableWordWrapping = false;
            UnityEngine.Object.DestroyImmediate(info.gameObject);
        }

        void BuildTab(GameObject tpl, Transform parent, ToggleGroup group, int stage)
        {
            var t = UnityEngine.Object.Instantiate(tpl, parent).transform;
            var tt = t.Find("PrivateLobbyShowTab/Content/SubMenuTabText").GetComponent<TextMeshProUGUI>();
            tt.text = TabLabel(stage, _stages[stage].Count);
            float w = Mathf.Max(170f, tt.GetPreferredValues().x + 44f);
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = 65f;
            t.GetComponent<RectTransform>().sizeDelta = new Vector2(w, 65f);
            foreach (var rt in t.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt.transform == t || rt.parent.name == "Content") continue;
                float inset = rt.name == "Content" ? 12f : rt.parent.name == "SubMenuTabActive" && rt.name == "Panel_Fill" ? -1f : 0f;
                Stretch(rt, inset, inset, inset, inset);
            }
            var tog = t.GetComponentInChildren<Toggle>(true);
            Mute(tog.onValueChanged);
            tog.isOn = false;
            tog.group = group;
            tog.onValueChanged.AddListener(new Action<bool>(on => { if (on) Select(stage); }));
            t.gameObject.SetActive(true);
            _tabs.Add(tog);
            _tabTexts.Add(tt);
        }

        string TabLabel(int stage, int count)
        {
            bool final = IsFinal(_stages[stage][0]);
            string name = final ? LocalizationService.Get("show_rounds.final") : string.Format(LocalizationService.Get("show_rounds.round"), stage + 1);
            return name + "  <size=70%>" + count + "</size>";
        }

        void Select(int stage)
        {
            stage = (stage + _stages.Count) % _stages.Count;
            if (stage == _cur) return;
            _cur = stage;
            if (!_tabs[_cur].isOn) _tabs[_cur].isOn = true;
            if (!_search.Typing) _es.SetSelectedGameObject(null);
            foreach (var g in _cards) UnityEngine.Object.DestroyImmediate(g);
            _cards.Clear();
            _cardNames.Clear();
            _cardKeys.Clear();
            foreach (var r in _stages[_cur])
            {
                var card = UnityEngine.Object.Instantiate(_cardTpl, _content);
                card.SetActive(true);
                card.transform.Find("Content/Text").GetComponent<TextMeshProUGUI>().text = r.DisplayNameUnindented;
                var tog = card.GetComponent<Toggle>();
                Mute(tog.onValueChanged);
                tog.isOn = false;
                card.GetComponent<TweenOnToggleSubMenu>().EnableSelectOnMouseHover(true);
                _cards.Add(card);
                _cardNames.Add(r.DisplayNameUnindented);
                _cardKeys.Add(r.LoadingScreenName ?? "");
            }
            Refilter();
            if (!_search.Typing) SelectCard(0);
        }

        void Refilter()
        {
            string q = _search.Field.text.Trim();
            _visible.Clear();
            for (int i = 0; i < _cards.Count; i++)
            {
                bool match = q.Length == 0 || _cardNames[i].IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                if (_cards[i].activeSelf != match) _cards[i].SetActive(match);
                if (match) _visible.Add(_cards[i]);
            }
            for (int s = 0; s < _stages.Count; s++)
            {
                int n = 0;
                foreach (var r in _stages[s])
                    if (q.Length == 0 || r.DisplayNameUnindented.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) n++;
                _matches[s] = n;
                _tabTexts[s].text = TabLabel(s, n);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            _scroll.StopMovement();
            _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, 0f);
        }

        void SelectCard(int i)
        {
            if (_visible.Count == 0) { SelectSearch(); return; }
            _sel = Mathf.Clamp(i, 0, _visible.Count - 1);
            _es.SetSelectedGameObject(_visible[_sel]);
        }

        void SelectSearch()
        {
            _sel = -1;
            _search.Select(_es);
        }

        void OnQueryChanged(string value)
        {
            Refilter();
            if (_visible.Count == 0)
                for (int s = 0; s < _stages.Count; s++)
                    if (_matches[s] > 0) { Select(s); break; }
        }

        void FillCards()
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                var ri = _cards[i].transform.Find("ShowImage/ShowSelectorImageMask/ShowSelectorImage").GetComponent<RawImage>();
                var spin = ri.transform.Find("Spinner").gameObject;
                if (!spin.activeSelf) continue;
                if (!_sprites.TryGetValue(_cardKeys[i], out var h) || !h.IsDone) continue;
                spin.SetActive(false);
                var sp = h.Status == AsyncOperationStatus.Succeeded ? h.Result : null;
                if (sp == null) continue;
                var tr = sp.textureRect;
                var box = ri.rectTransform.rect;
                float tw = sp.texture.width, th = sp.texture.height;
                float boxAspect = box.width / box.height, sprAspect = tr.width / tr.height;
                var uv = new Rect(tr.x / tw, tr.y / th, tr.width / tw, tr.height / th);
                if (sprAspect > boxAspect) { float k = boxAspect / sprAspect; uv.x += uv.width * (1 - k) / 2; uv.width *= k; }
                else { float k = sprAspect / boxAspect; uv.y += uv.height * (1 - k) / 2; uv.height *= k; }
                ri.texture = sp.texture;
                ri.uvRect = uv;
            }
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

        bool BackHeld => Kb(KeyCode.Escape) || NavPromptCore.ElementHeldByName(BackNames);

        internal bool Tick()
        {
            if (_root == null) { Dispose(); return false; }
            if (_closing)
            {
                if (BackHeld) return true;
                Dispose();
                return false;
            }

            bool pad = Rewired.ReInput.controllers.GetLastActiveControllerType() == Rewired.ControllerType.Joystick;
            foreach (var g in _kbNav) g.SetActive(!pad);
            foreach (var g in _padNav) g.SetActive(pad);
            FillCards();

            if (_search.TickTyping(_es)) return true;

            var cur = _es.currentSelectedGameObject;
            if (cur == _searchGo) _sel = -1;
            else if (cur != null) { int i = _visible.IndexOf(cur); if (i >= 0) _sel = i; }
            if (_sel >= 0 && cur != _searchGo) _search.Deselect();

            float lx = NavPromptCore.AxisByName(StickXNames), ly = NavPromptCore.AxisByName(StickYNames);
            bool up = Repeat(0, Kb(KeyCode.UpArrow) || Kb(KeyCode.W) || NavPromptCore.ElementHeldByName(UpNames) || ly > 0.5f);
            bool down = Repeat(1, Kb(KeyCode.DownArrow) || Kb(KeyCode.S) || NavPromptCore.ElementHeldByName(DownNames) || ly < -0.5f);
            bool left = Repeat(2, Kb(KeyCode.LeftArrow) || Kb(KeyCode.A) || NavPromptCore.ElementHeldByName(LeftNames) || lx < -0.5f);
            bool right = Repeat(3, Kb(KeyCode.RightArrow) || Kb(KeyCode.D) || NavPromptCore.ElementHeldByName(RightNames) || lx > 0.5f);
            if (Time.frameCount == _openFrame) return true;

            if (KeybindService.KeyDown(KeyCode.Escape) || NavPromptCore.ElementDownByName(BackNames))
            {
                _closing = true;
                _root.SetActive(false);
                return true;
            }
            if (KeybindService.KeyDown(KeyCode.Q) || NavPromptCore.ElementDownByName(PrevNames)) Select(_cur - 1);
            if (KeybindService.KeyDown(KeyCode.E) || NavPromptCore.ElementDownByName(NextNames)) Select(_cur + 1);

            bool submit = KeybindService.KeyDown(KeyCode.Return) || KeybindService.KeyDown(KeyCode.Space) || NavPromptCore.ElementDownByName(SubmitNames);
            if (_sel < 0)
            {
                if (submit) _search.StartTyping();
                else if (down) SelectCard(0);
            }
            else
            {
                if (up) { if (_sel - Columns >= 0) SelectCard(_sel - Columns); else SelectSearch(); }
                if (down && _sel + Columns < _visible.Count) SelectCard(_sel + Columns);
                else if (down && _sel / Columns < (_visible.Count - 1) / Columns) SelectCard(_visible.Count - 1);
                if (left && _sel % Columns > 0) SelectCard(_sel - 1);
                if (right && _sel % Columns < Columns - 1 && _sel + 1 < _visible.Count) SelectCard(_sel + 1);
            }

            float y = NavPromptCore.AxisByName(ScrollNames);
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
            foreach (var h in _sprites.Values) Addressables.Release(h);
            _sprites.Clear();
            FGInputLockService.SetScreenLock(false);
            if (_prevSel != null) _es.SetSelectedGameObject(_prevSel);
        }
    }

    internal sealed class PopupSearchRow
    {
        const float PlaceholderAlpha = 0.6f;
        static Sprite _icon;

        internal readonly GameObject Go;
        internal readonly TMP_InputField Field;
        internal bool Typing { get; private set; }

        readonly string _placeholderKey;
        readonly Action<string> _onChanged;
        readonly Action _onStop;
        readonly TextMeshProUGUI _label;
        readonly FontStyles _labelStyle;
        readonly Image _iconImg;
        readonly UICustomToggle _toggle;
        readonly TweenOnToggleSubMenu _tween;
        int _typingFrame = -1;

        internal PopupSearchRow(GameObject go, string placeholderKey, Action<string> onChanged, Action onSelect)
        {
            Go = go;
            _placeholderKey = placeholderKey;
            _onChanged = onChanged;
            _onStop = onSelect;
            _label = go.transform.Find("ShowNameText").GetComponent<TextMeshProUGUI>();
            _labelStyle = _label.fontStyle;

            var iconGo = new GameObject("BFG_SearchIcon");
            iconGo.transform.SetParent(go.transform, false);
            iconGo.transform.SetSiblingIndex(_label.transform.GetSiblingIndex());
            var iRt = iconGo.AddComponent<RectTransform>();
            iRt.anchorMin = iRt.anchorMax = new Vector2(0f, 0.5f);
            iRt.pivot = new Vector2(0f, 0.5f);
            iRt.anchoredPosition = new Vector2(17f, 0f);
            iRt.sizeDelta = new Vector2(24f, 24f);
            _iconImg = iconGo.AddComponent<Image>();
            _iconImg.sprite = _icon ??= Utilities.EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.button.search.png");
            _iconImg.preserveAspect = true;
            _iconImg.raycastTarget = false;
            var thick = iconGo.AddComponent<Outline>();
            thick.effectColor = Color.white;
            thick.effectDistance = new Vector2(1f, 1f);
            var shadow = iconGo.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.173f, 0.118f, 0.275f, 0.502f);
            shadow.effectDistance = new Vector2(2.8f, -2.8f);
            _label.rectTransform.offsetMin = new Vector2(49f, _label.rectTransform.offsetMin.y);

            _toggle = go.GetComponent<UICustomToggle>();
            _tween = go.GetComponent<TweenOnToggleSubMenu>();
            _toggle._navigationModeUp = Navigation.Mode.None;
            _toggle._navigationModeDown = Navigation.Mode.None;
            _toggle._navigationModeLeft = Navigation.Mode.None;
            _toggle._navigationModeRight = Navigation.Mode.None;
            _toggle.onValueChanged.RemoveAllListeners();
            ShowRoundsPopup.Mute(_toggle.onValueChanged);
            _toggle.group = null;
            _toggle.isOn = false;
            _toggle.onValueChanged.AddListener(new Action<bool>(on =>
            {
                _tween.CancelTweens();
                if (on) _tween.StartTweenOn();
                else _tween.StartTweenOff();
            }));

            var fieldGo = new GameObject("BFG_SearchField");
            fieldGo.transform.SetParent(go.transform, false);
            fieldGo.SetActive(false);
            var frt = fieldGo.AddComponent<RectTransform>();
            frt.anchorMin = frt.anchorMax = Vector2.zero;
            frt.pivot = Vector2.zero;
            frt.sizeDelta = new Vector2(1f, 1f);
            var areaGo = new GameObject("TextArea");
            areaGo.transform.SetParent(fieldGo.transform, false);
            var art = areaGo.AddComponent<RectTransform>();
            ShowRoundsPopup.Stretch(art, 0, 0, 0, 0);
            areaGo.AddComponent<RectMask2D>();
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(areaGo.transform, false);
            ShowRoundsPopup.Stretch(textGo.AddComponent<RectTransform>(), 0, 0, 0, 0);
            var hidden = textGo.AddComponent<TextMeshProUGUI>();
            hidden.font = _label.font;
            hidden.fontSharedMaterial = _label.fontSharedMaterial;
            hidden.fontSize = 1f;
            hidden.color = new Color(1f, 1f, 1f, 0f);
            hidden.raycastTarget = false;
            Field = fieldGo.AddComponent<TMP_InputField>();
            Field.textViewport = art;
            Field.textComponent = hidden;
            Field.lineType = TMP_InputField.LineType.SingleLine;
            Field.characterLimit = 40;
            Field.restoreOriginalTextOnEscape = false;
            Field.customCaretColor = true;
            Field.caretColor = new Color(0f, 0f, 0f, 0f);
            Field.selectionColor = new Color(0f, 0f, 0f, 0f);
            Field.transition = Selectable.Transition.None;
            var nav = Field.navigation;
            nav.mode = Navigation.Mode.None;
            Field.navigation = nav;
            Field.onValueChanged.AddListener(new Action<string>(OnValueChanged));
            fieldGo.SetActive(true);
            BetterFG.UI.BetterFGUIMan.AdoptField(fieldGo);

            var trig = go.AddComponent<EventTrigger>();
            var hover = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            hover.callback.AddListener(new Action<BaseEventData>(_ => { if (!Typing) onSelect(); }));
            trig.triggers.Add(hover);
            var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            click.callback.AddListener(new Action<BaseEventData>(_ => StartTyping()));
            trig.triggers.Add(click);
        }

        void OnValueChanged(string value)
        {
            if (Time.frameCount <= _typingFrame && value.Length > 0) { Field.text = ""; return; }
            _onChanged(value);
            Redraw();
        }

        internal void Select(EventSystem es)
        {
            es.SetSelectedGameObject(Go);
            _toggle.isOn = true;
        }

        internal void Deselect() => _toggle.isOn = false;

        internal void StartTyping()
        {
            if (Typing) return;
            Typing = true;
            _typingFrame = Time.frameCount;
            Field.readOnly = true;
            Field.Select();
            Field.ActivateInputField();
            _toggle.SetIsOnWithoutNotify(true);
            _tween.CancelTweens();
            _tween._activeToggleCanvasGroup.alpha = 1f;
            _tween._inactiveToggleCanvasGroup.alpha = 0f;
            Redraw();
        }

        internal bool TickTyping(EventSystem es)
        {
            if (!Typing) return false;
            if (Time.frameCount > _typingFrame && Field.readOnly) Field.readOnly = false;
            if (Time.frameCount <= _typingFrame + 1) return true;
            if (KeybindService.KeyDown(KeyCode.Escape) || KeybindService.KeyDown(KeyCode.Return) || KeybindService.KeyDown(KeyCode.KeypadEnter)
                || !Field.isFocused || es.currentSelectedGameObject != Field.gameObject)
            {
                Typing = false;
                Field.DeactivateInputField();
                _onStop();
                Redraw();
            }
            return true;
        }

        internal void Redraw()
        {
            string q = Field.text;
            _label.alpha = Typing || q.Length > 0 ? 1f : PlaceholderAlpha;
            _label.fontStyle = Typing || q.Length > 0 ? _labelStyle & ~FontStyles.UpperCase : _labelStyle;
            _iconImg.color = new Color(1f, 1f, 1f, _label.alpha);
            _label.text = Typing ? q + "|" : q.Length > 0 ? q : LocalizationService.Get(_placeholderKey);
        }

        internal void Drop() => BetterFG.UI.BetterFGUIMan.DropField(Field.gameObject);
    }
}
