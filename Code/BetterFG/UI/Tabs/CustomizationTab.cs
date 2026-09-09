using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using BetterFG;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.Utilities;
using BetterFG.Customization.Player;
using BetterFG.UI.Windows;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using BetterFG.Customization.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class CustomizationTab : UGCTab
    {
        public CustomizationTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "UGC Customization";
        protected override string TitleId => "ui.ugc_customization";
        protected override string BgResource => "BetterFG.assets.ui.cskins.bg.png";

        // pick-mode: when set, this tab is on loan to a picker flow (the pet wizard). the whole
        // multi-select UI works exactly as it does for the local player, it just reads and writes
        // this list instead of the player's own loadout keys, and never applies to the live bean.
        public List<BetterFG.Network.RemoteSkinEntry> PickTargetList;
        public Action PickBack;
        public Action OnPickChanged;
        private bool PickMode => PickTargetList != null;
        protected override string SwitchLabel => PickMode ? "ui.back_3" : "";

        // the picker is pushed over the pet wizard's slot while the bean creator stays live behind
        // it, so backing out is a pop, not a tab swap - hand it to the opener and switch to nothing
        protected override Tab MakeSwitchTarget()
        {
            if (!PickMode) return null;
            PickBack?.Invoke();
            return null;
        }

        public override Tab MakeFallbackTab() => PickMode ? null : base.MakeFallbackTab();


        // ── Empty state ───────────────────────────────────────────────────────
        private const string EMPTY_NO_REPO = "No repository selected.";
        private const string EMPTY_NO_RESULTS = "No results for \"{0}\".";
        private const string EMPTY_NO_TYPE = "No {0}s in this repository";
        private const string EMPTY_BEAN_RES = "BetterFG.assets.ui.bean.bean_frighten.png";
        private static Texture2D _frightenTex;
        private Text _fetchCountLabel; // live "X / Y fetched" line under the empty state, updated per skin

        // ── Settings keys ─────────────────────────────────────────────────────
        private const string KEY_MULTI_FILES = "skin.multi.files";
        private const string KEY_MULTI_SOURCES = "skin.multi.sources";
        private const string KEY_MULTI_PATHS = "skin.multi.paths";
        private const string KEY_MULTI_TYPES = "skin.multi.types";
        private const string KEY_IMPORTED_PATHS = "skin.imported.paths";

        // sentinel value used as Active.githubUrl when "Imported Skins" repo is selected
        private const string IMPORTED_REPO_KEY = "__imported__";

        // legacy single-skin keys kept for restore only
        private const string KEY_SOURCE = "skin.source";
        private const string KEY_FILE = "skin.file";
        private const string KEY_LOCAL_PATH = "skin.localPath";

        // hand overrides per skin file: 0=default,1=left,2=right,3=both
        private const string KEY_HAND_OVERRIDES = "skin.hand.overrides";
        private Dictionary<string, int> _handOverrides = new Dictionary<string, int>();

        // ── Imported skins persistent list ────────────────────────────────────
        private List<string> _importedPaths = new List<string>(); // folder paths, persisted
        private Dictionary<string, RawImage> _featuredCoverImages = new Dictionary<string, RawImage>();

        // open item config window
        private ItemConfigWindow _configWindow;
        private string _configWindowFile;

        // ── Selection limits ──────────────────────────────────────────────────
        private const int MAX_COSTUME = 1;
        private const int MAX_ACCESSORY = 3;
        private const int MAX_ITEM = 2;

        // ── Active filter tab ─────────────────────────────────────────────────
        private SkinType _activeFilter = SkinType.Costume;

        // ── Multi-selection ───────────────────────────────────────────────────
        private HashSet<int> selectedIndices = new HashSet<int>();

        // emote "Copy" is two-stage: first press copies + arms this row, second press opens Social>Emotes
        private int _copyArmedIndex = -1;

        private int SelectedCostumeIndex()
        {
            foreach (int i in selectedIndices)
            {
                if (i < 0 || i >= availableSkins.Count) continue;
                if (SkinTypeParser.FromString(availableSkins[i].type) == SkinType.Costume) return i;
            }
            return -1;
        }

        private int CountSelected(SkinType type)
        {
            int c = 0;
            foreach (int i in selectedIndices)
            {
                if (i < 0 || i >= availableSkins.Count) continue;
                if (SkinTypeParser.FromString(availableSkins[i].type) == type) c++;
            }
            return c;
        }



        // ── Textures ──────────────────────────────────────────────────────────
        private static Texture2D LoadTex(string resource, ref Texture2D cache)
        {
            if (cache != null) return cache;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream(resource);
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);
                tex.wrapMode = TextureWrapMode.Clamp;
                cache = tex;
            }
            catch (Exception ex) { Plugin.Log.LogError("BetterFG: Tex load failed: " + ex.Message); }
            return cache;
        }


        // ── Colors ────────────────────────────────────────────────────────────
        private static readonly Color BTN_FETCH = new Color(0.25f, 0.45f, 0.25f, 1f);
        private static readonly Color BTN_IMPORT = new Color(0.25f, 0.35f, 0.45f, 1f);
        private static readonly Color BTN_APPLY = new Color(0.45f, 0.35f, 0.25f, 1f);
        private static readonly Color BTN_REMOVE = UGUIShip.BTN_REMOVE;
        private static readonly Color BTN_DARK = Color.black;
        private static readonly Color BTN_SEL = new Color(0.28f, 0.28f, 0.28f, 1f);
        private static readonly Color BTN_FILTER_ACTIVE = new Color(0.1f, 0.32f, 0.1f, 1f);
        private static readonly Color ITEM_BG = new Color(0f, 0f, 0f, 0f);
        private static readonly Color WHITE = UGUIShip.WHITE;
        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.45f);
        private static readonly Color GOLD = new Color(1f, 0.8f, 0f, 1f);
        private static readonly Color GREEN = new Color(0f, 1f, 0f, 1f);
        private static readonly Color CYAN = new Color(0f, 0.8f, 1f, 1f);
        private static readonly Color ORANGE = new Color(1f, 0.55f, 0.1f, 1f);
        private static readonly Color YELLOW = new Color(1f, 1f, 0f, 1f);

        // ── Layout ────────────────────────────────────────────────────────────
        private static float ROW_H => UIScale.ROW_H;
        private static float COVER_W => UIScale.COVER_W;
        private static float COVER_H => UIScale.COVER_H;
        private static float SEL_W => UIScale.SEL_W;

        // ── UGUI refs ─────────────────────────────────────────────────────────
        private RectTransform _scrollContent;
        private RectTransform _scrollViewRt;      // scroll view root, moved/grown when the filter bar hides
        private RectTransform _filterDivider2Rt;  // divider between filter bar and search, hidden with the bar
        private Rect _searchRectNormal;           // search field rect in normal (filter-bar-visible) mode
        private Rect _scrollRectNormal;           // scroll view rect in normal mode
        private float _filterCollapse;            // vertical space to reclaim when the filter bar is hidden
        private float _rowIndent;                 // left space rows reclaim to reach the dropdown edge; re-added as text indent
        private Text _searchText;
        private Text _searchPlaceholder;
        private RectTransform _searchFieldRt;
        private bool _searchActive;
        private string _searchQuery = "";
        private Dictionary<string, bool> _groupExpanded = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        // group container GOs from the last RefreshSkinList, so a search keystroke can hide a whole
        // group whose rows all filtered out without rebuilding anything
        private readonly List<GameObject> _skinGroups = new List<GameObject>();
        private GameObject _searchEmptyGo;

        // filter bar buttons
        private Button _btnCostumes, _btnAccessories, _btnItems, _btnEmotes;

        private Dictionary<string, Image> _coverImages = new Dictionary<string, Image>();

        // per-row togglable visuals, keyed by skin index, so selecting/deselecting can repaint
        // just the affected rows in place instead of rebuilding the whole list (the freeze)
        private class RowVisual
        {
            public GameObject gradient;   // selection gradient overlay (toggled active)
            public GameObject configBtn;  // item "Configure" button (toggled active)
            public Text selectLabel;      // Select button label (recoloured)
            public Color selColor;        // this row's accent colour
        }
        private Dictionary<int, RowVisual> _rowVisuals = new Dictionary<int, RowVisual>();

        // ── State ─────────────────────────────────────────────────────────────
        private List<SkinInfo> availableSkins = new List<SkinInfo>();
        private Dictionary<string, Texture2D> skinCovers = new Dictionary<string, Texture2D>();

        // ── Services ──────────────────────────────────────────────────────────
        private SkinCatalogService catalogService;
        private SkinLoaderService loaderService;
        private SkinApplicationService applicationService;
        private RepoRegistry repoRegistry;

        // ── Repo dropdown ─────────────────────────────────────────────────────
        private bool _fakeInputLocked = false;

        private List<SkinInfo> _pendingApplyQueue = new List<SkinInfo>();
        private int _pendingTotal = 0;

        // files that failed to match during restore — retried on each OnSkinsLoaded
        private List<(string file, string repo)> _pendingRestoreFiles = new List<(string, string)>();

        // ── Lifecycle ─────────────────────────────────────────────────────────
        private void Awake()
        {
            BindServices();
            LoadImportedPaths();
            SeedImportedSkins();

            // if catalog already has data (tab was swapped back in), seed immediately
            if (catalogService != null)
            {
                var existing = catalogService.AvailableSkins;
                if (existing != null && existing.Count > 0)
                {
                    availableSkins = existing;
                    _restoredOnce = true;
                    // restore cached covers so thumbnails don't go blank
                    foreach (var skin in availableSkins)
                        if (catalogService.TryGetCover(skin, out var tex) && tex != null)
                            skinCovers[CoverKey(skin)] = tex;
                }
            }
        }

        private void SeedImportedSkins()
        {
            foreach (string folder in _importedPaths)
            {
                if (availableSkins.FindIndex(s => s.isLocalImport && !string.IsNullOrEmpty(s.localPath) &&
                    string.Equals(Path.GetDirectoryName(s.localPath), folder, StringComparison.OrdinalIgnoreCase)) >= 0)
                    continue;
                var skin = LoadImportedFromFolder(folder);
                if (skin != null) availableSkins.Add(skin);
            }
        }

        // builds a lightweight SkinInfo from a persisted import folder's info.json (no bundle load)
        private SkinInfo LoadImportedFromFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
            string infoPath = Path.Combine(folder, "info.json");
            if (!File.Exists(infoPath)) return null;
            try
            {
                string json = File.ReadAllText(infoPath);
                string name = JsonUtil.GetValue(json, "name");
                string file = JsonUtil.GetValue(json, "file");
                string type = JsonUtil.GetValue(json, "type");
                string author = JsonUtil.GetValue(json, "author");
                string group = JsonUtil.GetValue(json, "group");
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(file)) return null;
                string bundlePath = Path.Combine(folder, file);
                if (!File.Exists(bundlePath)) return null;
                return new SkinInfo
                {
                    name = name, file = file, type = type, author = author, group = group,
                    isLocalImport = true, localPath = bundlePath, sourceRepo = folder,
                };
            }
            catch { return null; }
        }


        private void OnReposChanged()
        {
            
            FetchSelectedRepo();
            RefreshSkinList();
        }

        private void OnDestroy()
        {
            SetFakeInputLock(false);

            if (repoRegistry != null)
            {
                repoRegistry.OnReposChanged -= OnReposChanged;
                repoRegistry.OnValidationStatus -= SetStatus;
                repoRegistry.OnCoverLoaded -= OnRepoCoverLoaded;
                repoRegistry.OnFeaturedLoaded -= OnFeaturedLoaded;
            }
            if (catalogService != null)
            {
                catalogService.OnSkinsLoaded -= OnSkinsLoaded;
                catalogService.OnFetchCompleted -= OnFetchCompleted;
                catalogService.OnStatusUpdate -= SetStatus;
                catalogService.OnSkinCoverLoaded -= OnSkinCoverLoaded;
            }
            if (loaderService != null)
            {
                loaderService.OnSkinLoaded -= OnSkinDownloaded;
                loaderService.OnSkinImported -= OnSkinImported;
            }
        }

        private void Update()
        {
            WinDialogs.Tick();

            SetFakeInputLock(_searchActive);
            if (!_searchActive) return;

            if (Input.GetMouseButtonDown(0))
            {
                var mousePos = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
                if (_searchFieldRt != null &&
                    !RectTransformUtility.RectangleContainsScreenPoint(_searchFieldRt, mousePos, null))
                {
                    _searchActive = false;
                    UpdateSearchCaret();
                }
                return;
            }

            foreach (char c in Input.inputString)
            {
                if (c == '\b')
                { if (_searchQuery.Length > 0) { _searchQuery = _searchQuery.Substring(0, _searchQuery.Length - 1); OnSearchChanged(); } }
                else if (c == '\n' || c == '\r' || c == '\x1b') { _searchActive = false; }
                else { _searchQuery += c; OnSearchChanged(); }
                UpdateSearchCaret();
            }
        }

        private void UpdateSearchCaret()
        {
            if (_searchText == null) return;
            bool empty = string.IsNullOrEmpty(_searchQuery);
            _searchText.text = empty && !_searchActive ? "" : _searchQuery + (_searchActive ? "|" : "");
            if (_searchPlaceholder != null)
                _searchPlaceholder.color = empty && !_searchActive
                    ? new Color(1f, 1f, 1f, 0.2f) : new Color(1f, 1f, 1f, 0f);
        }

        private void SetFakeInputLock(bool active)
        {
            if (_fakeInputLocked == active) return;
            _fakeInputLocked = active;
            BetterFG.Services.FGInputLockService.SetFakeFieldLock(active);
        }

        // ── Build ─────────────────────────────────────────────────────────────

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;
            float y = VPAD;

            _repoRowParent = contentRoot;
            _repoRowY = y;
            _repoRowW = w;
            y = RepoSelectorTab.BuildCurrentRepoRow(this, contentRoot, y, w);
            UGUIShip.CreatePanel(contentRoot, PR(y, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            y += 1f + SH;

            var belowRt = contentRoot;

            // pick mode (pets) never touches the local player's own loadout - no Fetch/Import/Apply/
            // Remove All (all four write to the SAME shared settings keys and apply straight to the
            // real local bean, regardless of what's being browsed for)
            bool pickMode = PickMode;

            BuildFilterBar(belowRt, y, w);
            y += BTN_H + SH;

            _filterDivider2Rt = UGUIShip.CreatePanel(belowRt, PR(y, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            y += 1f + SH;

            // filter bar block (bar + its spacing + this divider + spacing) is reclaimed when hidden
            _filterCollapse = BTN_H + 1f + 2f * SH;

            _searchRectNormal = new Rect(PAD, y, w, LH);
            BuildSearchField(belowRt, y, w);
            y += LH + SH;

            const float BOTTOM_PAD = 6f;
            float scrollH = TabHeight - y - (pickMode ? 0f : BTN_H + SH) - VPAD - BOTTOM_PAD;
            _scrollRectNormal = new Rect(PAD, y, w, scrollH);
            BuildScrollView(belowRt, y, w, scrollH);
            y += scrollH + SH;

            if (!pickMode)
            {
                float singleW = (w - 3f * (PAD * 0.5f)) / 4f;
                float gap = PAD * 0.5f;
                float bx = PAD;

                UGUIShip.CreateButton(belowRt, new Rect(bx, y, singleW, BTN_H), "ui.fetch", BTN_FETCH, WHITE, FS, new Action(OnFetch)); bx += singleW + gap;
                UGUIShip.CreateButton(belowRt, new Rect(bx, y, singleW, BTN_H), "ui.import", BTN_IMPORT, WHITE, FS, new Action(OnImport)); bx += singleW + gap;
                UGUIShip.CreateButton(belowRt, new Rect(bx, y, singleW, BTN_H), "ui.apply", BTN_APPLY, WHITE, FS, new Action(OnApply)); bx += singleW + gap;
                UGUIShip.CreateButton(belowRt, new Rect(bx, y, singleW, BTN_H), "ui.remove_all", BTN_REMOVE, WHITE, FS, new Action(OnRemoveAll));
            }

            Refresh();
        }

        private RectTransform _repoRowParent;
        private float _repoRowY, _repoRowW;

        public override void OnRepoChanged()
        {
            RepoSelectorTab.BuildCurrentRepoRow(this, _repoRowParent, _repoRowY, _repoRowW);
            Refresh();
        }

        private void Refresh()
        {
            RefreshFilterBar();
            FetchSelectedRepo();
            RefreshSkinList();
        }

        // flip an L/R hand button between on (cyan/black text) and off (dark/white text) in place,
        // without rebuilding the list. mirrors the colours used when the button is first created.
        private static void RecolorHandButton(Button btn, bool on)
        {
            if (btn == null) return;
            Color bg = on ? CYAN : BTN_DARK;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = bg;
            var cols = btn.colors;
            cols.normalColor = bg;
            cols.highlightedColor = bg;
            cols.pressedColor = bg;
            cols.selectedColor = bg;
            cols.fadeDuration = 0f;
            btn.colors = cols;
            var label = btn.transform.Find("Label")?.GetComponent<Text>();
            if (label != null) label.color = on ? Color.black : WHITE;
        }

        private void SaveImportedPaths()
        {
            SettingsService.Set(KEY_IMPORTED_PATHS, string.Join("|", _importedPaths));
        }

        private void LoadImportedPaths()
        {
            _importedPaths.Clear();
            string raw = SettingsService.Get(KEY_IMPORTED_PATHS, "");
            if (string.IsNullOrEmpty(raw)) return;
            foreach (string p in raw.Split('|'))
            {
                string trimmed = p.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    _importedPaths.Add(trimmed);
            }
        }

        private void BuildFilterBar(RectTransform parent, float y, float w)
        {
            float gap = PAD * 0.5f;
            float btnW = (w - gap * 3f) / 4f;
            float bx = PAD;

            _btnCostumes = UGUIShip.CreateButton(parent, new Rect(bx, y, btnW, BTN_H), "ui.costumes", GetFilterBg(SkinType.Costume), WHITE, FS_SM, new Action(() => SetFilter(SkinType.Costume))); bx += btnW + gap;
            _btnAccessories = UGUIShip.CreateButton(parent, new Rect(bx, y, btnW, BTN_H), "ui.accessories", GetFilterBg(SkinType.Accessory), WHITE, FS_SM, new Action(() => SetFilter(SkinType.Accessory))); bx += btnW + gap;
            _btnItems = UGUIShip.CreateButton(parent, new Rect(bx, y, btnW, BTN_H), "ui.items", GetFilterBg(SkinType.Item), WHITE, FS_SM, new Action(() => SetFilter(SkinType.Item))); bx += btnW + gap;
            _btnEmotes = UGUIShip.CreateButton(parent, new Rect(bx, y, btnW, BTN_H), "ui.emotes", GetFilterBg(SkinType.Emote), WHITE, FS_SM, new Action(() => SetFilter(SkinType.Emote)));
        }

        private void SetFilter(SkinType type)
        {
            _activeFilter = type;
            RefreshFilterBar();
            RefreshSkinList();
        }

        private void RefreshFilterBar()
        {
            UGUIShip.SetButtonSelected(_btnCostumes, _activeFilter == SkinType.Costume, BTN_FILTER_ACTIVE);
            UGUIShip.SetButtonSelected(_btnAccessories, _activeFilter == SkinType.Accessory, BTN_FILTER_ACTIVE);
            UGUIShip.SetButtonSelected(_btnItems, _activeFilter == SkinType.Item, BTN_FILTER_ACTIVE);
            UGUIShip.SetButtonSelected(_btnEmotes, _activeFilter == SkinType.Emote, BTN_FILTER_ACTIVE);
        }

        private Color GetFilterBg(SkinType type) =>
            _activeFilter == type ? BTN_FILTER_ACTIVE : BTN_DARK;

        // ── Services ──────────────────────────────────────────────────────────

        private void BindServices()
        {
            repoRegistry = CustomizationServices.RepoRegistry;
            catalogService = CustomizationServices.CatalogService;
            applicationService = CustomizationServices.ApplicationService;
            loaderService = CustomizationServices.LoaderService;

            if (repoRegistry != null)
            {
                repoRegistry.OnReposChanged += OnReposChanged;
                repoRegistry.OnValidationStatus += SetStatus;
                repoRegistry.OnCoverLoaded += OnRepoCoverLoaded;
                repoRegistry.OnFeaturedLoaded += OnFeaturedLoaded;
            }
            if (catalogService != null)
            {
                catalogService.OnSkinsLoaded += OnSkinsLoaded;
                catalogService.OnFetchCompleted += OnFetchCompleted;
                catalogService.OnStatusUpdate += SetStatus;
                catalogService.OnSkinCoverLoaded += OnSkinCoverLoaded;
            }
            if (applicationService != null)
            {
                applicationService.OnSkinApplied += e => SetStatus($"Applied {e.skinInfo.name} to {e.bean?.name}");
                applicationService.OnSkinRemoved += SetStatus;
            }
            if (loaderService != null)
            {
                loaderService.OnSkinLoaded += OnSkinDownloaded;
                loaderService.OnSkinImported += OnSkinImported;
                loaderService.OnProgress += SetStatus;
                loaderService.OnError += err => SetStatus("Error: " + err);
            }
        }

        // ── Scroll / list ─────────────────────────────────────────────────────

        private void BuildScrollView(RectTransform parent, float y, float width, float height)
        {
            var scroll = UGUIShip.CreateScrollView(parent, new Rect(PAD, y, width, height));
            var scrollRect = scroll.scrollRect;
            scrollRect.scrollSensitivity = 60f;
            _scrollViewRt = scrollRect.GetComponent<RectTransform>();
            _scrollContent = scroll.content;
            _scrollContent.pivot = new Vector2(0.5f, 1f);
            _scrollContent.sizeDelta = Vector2.zero;

            // reclaim the viewport's left inset (only the right side needs it, for the scrollbar) and
            // the content's left padding so rows can extend to the scroll view's left edge — which
            // lines up with the repo dropdown. the reclaimed width is re-added per row as text indent
            float viewportLeftInset = scrollRect.viewport != null ? scrollRect.viewport.offsetMin.x : 0f;
            if (scrollRect.viewport != null)
                scrollRect.viewport.offsetMin = new Vector2(0f, scrollRect.viewport.offsetMin.y);
            _rowIndent = viewportLeftInset + PAD;

            var vlg = _scrollContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true; // rows size to their content so multi-line descriptions grow the row down
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = PAD * 0.5f;
            vlg.padding = new RectOffset(0, (int)PAD, (int)(PAD * 0.5f), (int)(PAD * 0.5f));

            _scrollContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void RefreshSkinList()
        {
            if (_scrollContent == null) return;
            _fetchCountLabel = null; // rebuilt below only if the empty state shows
            _coverImages.Clear();
            _featuredCoverImages.Clear();
            _rowVisuals.Clear();
            _skinGroups.Clear();
            _searchEmptyGo = null;
            ClearSearchRows();
            for (int i = _scrollContent.childCount - 1; i >= 0; i--)
            {
                var child = _scrollContent.GetChild(i);
                if (child != null) Destroy(child.gameObject);
            }

            // Featured Repos section: filter bar makes no sense (a repo isn't sortable by costume/item)
            SetFilterBarVisible(!FeaturedSelected);
            if (FeaturedSelected)
            {
                RenderFeaturedRepos();
                LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollContent);
                return;
            }

            string q = _searchQuery?.ToLower() ?? "";
            string activeRaw = SelectedRaw;
            int display = 0;
            var shownGroups = new Dictionary<string, List<(SkinInfo skin, int index)>>(StringComparer.OrdinalIgnoreCase);
            var groupNames = new List<string>();

            for (int i = 0; i < availableSkins.Count; i++)
            {
                var s = availableSkins[i];
                if (ImportedSelected)
                {
                    if (!s.isLocalImport) continue;
                }
                else
                {
                    if (s.isLocalImport) continue;
                    if (!string.IsNullOrEmpty(activeRaw) && s.sourceRepo != activeRaw) continue;
                }
                if (SkinTypeParser.FromString(s.type) != _activeFilter) continue;
                // no search gate here — every row is built once and the query just toggles visibility
                string group = string.IsNullOrWhiteSpace(s.group) ? "Unsorted" : s.group.Trim();
                if (!shownGroups.TryGetValue(group, out var groupSkins))
                {
                    groupSkins = new List<(SkinInfo skin, int index)>();
                    shownGroups[group] = groupSkins;
                    groupNames.Add(group);
                }
                groupSkins.Add((s, i));
            }

            if (shownGroups.Count > 0)
            {
                foreach (string groupName in groupNames)
                {
                    bool expanded = !_groupExpanded.TryGetValue(groupName, out bool savedExpanded) || savedExpanded;
                    var groupGo = new GameObject("SkinGroup_" + groupName);
                    groupGo.transform.SetParent(_scrollContent, false);
                    groupGo.AddComponent<RectTransform>();
                    _skinGroups.Add(groupGo);
                    var groupVlg = groupGo.AddComponent<VerticalLayoutGroup>();
                    groupVlg.childForceExpandWidth = true;
                    groupVlg.childForceExpandHeight = false;
                    groupVlg.spacing = PAD * 0.5f;

                    var groupBtn = UGUIShip.CreateButton(groupGo.transform,
                        (expanded ? "▾ " : "▸ ") + groupName,
                        Color.clear, WHITE, FS_SM,
                        new Action(() => { _groupExpanded[groupName] = !expanded; RefreshSkinList(); }),
                        customSprite: false);
                    var groupBtnLE = groupBtn.gameObject.AddComponent<LayoutElement>();
                    groupBtnLE.preferredHeight = FS_SM + 6f;
                    var groupBtnColors = groupBtn.colors;
                    groupBtnColors.normalColor = Color.clear;
                    groupBtnColors.highlightedColor = new Color(1f, 1f, 1f, 0.2f);
                    groupBtnColors.pressedColor = new Color(1f, 1f, 1f, 0.2f);
                    groupBtnColors.selectedColor = Color.clear;
                    groupBtnColors.fadeDuration = 0f;
                    groupBtn.colors = groupBtnColors;
                    groupBtn.transition = Selectable.Transition.None;
                    UGUIShip.PaintHoverFill(groupBtn.gameObject, new Color(1f, 1f, 1f, 0.12f));
                    var groupBtnLabel = groupBtn.transform.Find("Label")?.GetComponent<Text>();
                    if (groupBtnLabel != null)
                    {
                        groupBtnLabel.alignment = TextAnchor.MiddleLeft;
                        var labelRt = groupBtnLabel.GetComponent<RectTransform>();
                        // keep the header text where it was while the row extends to the dropdown edge
                        labelRt.offsetMin = new Vector2(PAD + _rowIndent, labelRt.offsetMin.y);
                    }

                    if (expanded)
                    {
                        foreach (var shown in shownGroups[groupName])
                            CreateSkinItem(groupGo.transform, shown.skin, shown.index, display++);
                    }
                }
            }
            else
            {
                bool hasRepo = SelectedRepo != null;
                string msg = !hasRepo
                    ? EMPTY_NO_REPO
                    : !string.IsNullOrEmpty(q)
                        ? string.Format(EMPTY_NO_RESULTS, q)
                        : string.Format(EMPTY_NO_TYPE, _activeFilter.ToString().ToLower());

                var emptyGo = new GameObject("EmptyState");
                emptyGo.transform.SetParent(_scrollContent, false);
                var emptyRt = emptyGo.AddComponent<RectTransform>();
                emptyRt.sizeDelta = new Vector2(0f, 120f);
                var emptyLE = emptyGo.AddComponent<LayoutElement>();
                emptyLE.preferredHeight = 120f;

                var vlg = emptyGo.AddComponent<VerticalLayoutGroup>();
                vlg.childAlignment = TextAnchor.MiddleCenter;
                vlg.childForceExpandWidth = false;
                vlg.childForceExpandHeight = false;
                vlg.spacing = 6f;

                // bean image
                var tex = LoadTex(EMPTY_BEAN_RES, ref _frightenTex);
                if (tex != null)
                {
                    var imgGo = new GameObject("BeanImg");
                    imgGo.transform.SetParent(emptyGo.transform, false);
                    var imgRt = imgGo.AddComponent<RectTransform>();
                    float beanH = 48f;
                    float beanW = tex.height > 0 ? beanH * ((float)tex.width / tex.height) : beanH;
                    imgRt.sizeDelta = new Vector2(beanW, beanH);
                    var beanLE = imgGo.AddComponent<LayoutElement>();
                    beanLE.preferredWidth = beanW;
                    beanLE.preferredHeight = beanH;
                    beanLE.flexibleWidth = 0f;
                    var raw = imgGo.AddComponent<RawImage>();
                    raw.texture = tex;
                    raw.raycastTarget = false;
                    raw.color = new Color(1f, 1f, 1f, 0.55f);
                }

                var lbl = UGUIShip.CreateFlowLabel(emptyGo.transform, msg, FS_SM, HINT);
                lbl.alignment = TextAnchor.MiddleCenter;
                var lblLE = lbl.GetComponent<LayoutElement>();
                lblLE.preferredHeight = LH;
                lblLE.flexibleWidth = 1f;

                // show how many we've actually pulled vs how many the catalog says exist, so it's
                // clear when the list is empty just because stuff is still loading in
                int repoTotal = catalogService != null ? catalogService.GetCatalogTotalForRepo(activeRaw) : 0;
                if (repoTotal > 0)
                {
                    var cnt = UGUIShip.CreateFlowLabel(emptyGo.transform,
                        $"{catalogService.GetFetchedCountForRepo(activeRaw)} / {repoTotal} fetched",
                        FS_SM - 2, new Color(HINT.r, HINT.g, HINT.b, HINT.a * 0.7f));
                    cnt.alignment = TextAnchor.MiddleCenter;
                    var cntLE = cnt.GetComponent<LayoutElement>();
                    cntLE.preferredHeight = LH;
                    cntLE.flexibleWidth = 1f;
                    _fetchCountLabel = cnt;
                }
            }

            FilterSkinRows();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollContent);
        }

        // search keystroke path — no teardown. toggle each row against the query, hide groups that
        // emptied out, and show a "no results" note when nothing matches.
        private void OnSearchChanged()
        {
            if (FeaturedSelected) { RefreshSkinList(); return; } // featured list is tiny, full rebuild is fine
            FilterSkinRows();
        }

        private void FilterSkinRows()
        {
            int shown = ApplySearchFilter(_searchQuery);

            foreach (var g in _skinGroups)
            {
                if (g == null) continue;
                bool any = false;
                for (int i = 0; i < g.transform.childCount; i++)
                {
                    var c = g.transform.GetChild(i);
                    if (c != null && c.name.StartsWith("SkinItem") && c.gameObject.activeSelf) { any = true; break; }
                }
                // a collapsed group builds no rows — leave it shown so its header still hints "matches here"
                bool hasRows = false;
                for (int i = 0; i < g.transform.childCount; i++)
                    if (g.transform.GetChild(i)?.name.StartsWith("SkinItem") == true) { hasRows = true; break; }
                bool vis = !hasRows || any;
                if (g.activeSelf != vis) g.SetActive(vis);
            }

            bool empty = shown == 0 && !string.IsNullOrEmpty(_searchQuery) && _skinGroups.Count > 0;
            if (empty && _searchEmptyGo == null)
            {
                _searchEmptyGo = new GameObject("SearchEmpty");
                _searchEmptyGo.transform.SetParent(_scrollContent, false);
                _searchEmptyGo.AddComponent<RectTransform>();
                _searchEmptyGo.AddComponent<LayoutElement>().preferredHeight = LH * 2f;
                var lbl = UGUIShip.CreateFlowLabel(_searchEmptyGo.transform,
                    string.Format(EMPTY_NO_RESULTS, _searchQuery), FS_SM, HINT);
                lbl.alignment = TextAnchor.MiddleCenter;
            }
            else if (empty)
            {
                var txt = _searchEmptyGo.GetComponentInChildren<Text>(true);
                if (txt != null) UGUIShip.RelabelText(txt, string.Format(EMPTY_NO_RESULTS, _searchQuery));
                if (!_searchEmptyGo.activeSelf) _searchEmptyGo.SetActive(true);
            }
            else if (_searchEmptyGo != null && _searchEmptyGo.activeSelf)
            {
                _searchEmptyGo.SetActive(false);
            }
        }

        private void CreateSkinItem(Transform parent, SkinInfo skin, int index, int displayIndex)
        {
            bool isSelected = selectedIndices.Contains(index);
            SkinType type = SkinTypeParser.FromString(skin.type);

            Color selColor = type == SkinType.Costume ? GREEN
                             : type == SkinType.Accessory ? CYAN
                             : type == SkinType.Emote ? YELLOW
                             : ORANGE;
            Color gradColor = new Color(selColor.r, selColor.g, selColor.b, 0.4f);
            Color btnTxtColor = isSelected ? selColor : WHITE;

            float rowH = type == SkinType.Item ? ROW_H + (FS_SM + 4f) * 2f : ROW_H;

            GameObject rowConfigBtn = null; // set when this is an item row

            var rowGo = new GameObject("SkinItem_" + index);
            rowGo.transform.SetParent(parent, false);
            RegisterSearchRow(rowGo, skin.name, skin.author);
            var rowRt = rowGo.AddComponent<RectTransform>();
            rowRt.sizeDelta = new Vector2(0f, rowH);
            rowGo.AddComponent<Image>().color = ITEM_BG;

            // always build the gradient; just toggle it active by selection so a select/deselect
            // doesn't need to add/remove a GameObject (which would force a list rebuild)
            var gradGo = new GameObject("SelGradient");
            gradGo.transform.SetParent(rowGo.transform, false);
            var gradRt = gradGo.AddComponent<RectTransform>();
            gradRt.anchorMin = Vector2.zero;
            gradRt.anchorMax = Vector2.one;
            gradRt.offsetMin = gradRt.offsetMax = Vector2.zero;
            gradGo.AddComponent<Image>().color = Color.white;
            var grad = gradGo.AddComponent<GradientImage>();
            grad.Vertical = true;
            grad.TopColor = new Color(gradColor.r, gradColor.g, gradColor.b, 0f);
            grad.BottomColor = gradColor;
            gradGo.SetActive(isSelected);

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.spacing = PAD;
            // left padding carries the reclaimed indent so text stays put while the row extends left
            hlg.padding = new RectOffset((int)(PAD * 2f + _rowIndent), (int)(PAD * 2f), (int)(PAD * 0.5f), (int)(PAD * 0.5f));

            // Info column
            var infoGo = new GameObject("Info");
            infoGo.transform.SetParent(rowGo.transform, false);
            infoGo.AddComponent<RectTransform>();
            var infoLE = infoGo.AddComponent<LayoutElement>();
            infoLE.preferredWidth = 100f * UIScale.S; infoLE.flexibleWidth = 1f;
            var infoVlg = infoGo.AddComponent<VerticalLayoutGroup>();
            infoVlg.childForceExpandHeight = false;
            infoVlg.childForceExpandWidth = true;
            infoVlg.spacing = 0f;
            infoVlg.padding = new RectOffset(0, 0, (int)(PAD * 0.5f), (int)(PAD * 0.5f));

            UGUIShip.CreateFlowLabel(infoGo.transform, skin.name, FS, WHITE);
            UGUIShip.CreateFlowLabel(infoGo.transform, LocalizationService.Format("ui.by_author_fmt", skin.author), FS_SM, HINT);
            if (!string.IsNullOrEmpty(skin.description))
                UGUIShip.CreateFlowLabel(infoGo.transform, skin.description, FS_SM, HINT, multiline: true);
            if (skin.isLocalImport)
                UGUIShip.CreateFlowLabel(infoGo.transform, "ui.local", FS_SM, GOLD);

            if (type == SkinType.Item && (skin.left != null || skin.right != null))
            {
                string fileKey = skin.file;
                if (!_handOverrides.ContainsKey(fileKey))
                    _handOverrides[fileKey] = 3;

                int ov = _handOverrides[fileKey];
                bool lOn = ov == 1 || ov == 3;
                bool rOn = ov == 2 || ov == 3;

                var handRow = new GameObject("HandRow");
                handRow.transform.SetParent(infoGo.transform, false);
                handRow.AddComponent<RectTransform>();
                var handLE = handRow.AddComponent<LayoutElement>();
                handLE.preferredHeight = FS_SM + 4f;
                var handHlg = handRow.AddComponent<HorizontalLayoutGroup>();
                handHlg.childForceExpandHeight = false;
                handHlg.childForceExpandWidth = false;
                handHlg.spacing = 2f;

                // declared up here so each button's click handler can recolor BOTH in place —
                // toggling a hand only changes these two buttons' colors, so doing a full
                // RefreshSkinList() (destroy + rebuild every row) just to flip a colour was the
                // source of the click freeze. recolor locally instead.
                Button lBtn = null, rBtn = null;
                Action repaintHands = () =>
                {
                    int o = _handOverrides.ContainsKey(fileKey) ? _handOverrides[fileKey] : 3;
                    bool l = o == 1 || o == 3;
                    bool r = o == 2 || o == 3;
                    if (lBtn != null) RecolorHandButton(lBtn, l);
                    if (rBtn != null) RecolorHandButton(rBtn, r);
                };

                if (skin.left != null)
                {
                    string capturedFile = fileKey;
                    lBtn = UGUIShip.CreateButton(handRow.transform, "L",
                        lOn ? CYAN : BTN_DARK, lOn ? Color.black : WHITE, FS_SM, new Action(() =>
                        {
                            int cur = _handOverrides.ContainsKey(capturedFile) ? _handOverrides[capturedFile] : 3;
                            bool wasOn = cur == 1 || cur == 3;
                            bool rStillOn = cur == 2 || cur == 3;
                            _handOverrides[capturedFile] = !wasOn ? rStillOn ? 3 : 1 : rStillOn ? 2 : 0;
                            SaveHandOverrides(); repaintHands();
                        }));
                    lBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = SEL_W * 0.5f;
                }

                if (skin.right != null)
                {
                    string capturedFile = fileKey;
                    rBtn = UGUIShip.CreateButton(handRow.transform, "R",
                        rOn ? CYAN : BTN_DARK, rOn ? Color.black : WHITE, FS_SM, new Action(() =>
                        {
                            int cur = _handOverrides.ContainsKey(capturedFile) ? _handOverrides[capturedFile] : 3;
                            bool lStillOn = cur == 1 || cur == 3;
                            bool wasOn = cur == 2 || cur == 3;
                            _handOverrides[capturedFile] = !wasOn ? lStillOn ? 3 : 2 : lStillOn ? 1 : 0;
                            SaveHandOverrides(); repaintHands();
                        }));
                    rBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = SEL_W * 0.5f;
                }

                // always build Configure; toggle active by selection (no structural change on click)
                string cfgFile = fileKey;
                var cfgBtn = UGUIShip.CreateButton(handRow.transform, "ui.configure",
                    BTN_DARK, ORANGE, FS_SM, new Action(() => OpenConfigWindow(cfgFile)));
                cfgBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = SEL_W;
                cfgBtn.gameObject.SetActive(isSelected);
                rowConfigBtn = cfgBtn.gameObject;
            }

            // Cover — picture in a rounded delux frame, clipped to the fill behind it
            var coverGo = new GameObject("Cover");
            coverGo.transform.SetParent(rowGo.transform, false);
            coverGo.AddComponent<RectTransform>();
            var coverLE = coverGo.AddComponent<LayoutElement>();
            coverLE.preferredWidth = COVER_W;
            coverLE.preferredHeight = COVER_H;
            coverLE.minWidth = COVER_W;
            var (_, coverSlot) = UGUIShip.CreateFramedImage(coverGo.transform);
            var coverImg = coverSlot.gameObject.AddComponent<Image>();
            coverImg.raycastTarget = false;
            string coverKey = CoverKey(skin);
            _coverImages[coverKey] = coverImg;

            Texture2D coverTex = null;
            if (!skinCovers.TryGetValue(coverKey, out coverTex) || coverTex == null)
                catalogService?.TryGetCover(skin, out coverTex);

            if (coverTex != null)
            {
                skinCovers[coverKey] = coverTex;
                try { ApplyCover(coverImg, coverTex); }
                catch
                {
                    skinCovers.Remove(coverKey);
                    coverImg.color = new Color(1f, 1f, 1f, 0f);
                    UGUIShip.CreateStretchLabel(coverSlot, "ui.no_preview", FS_SM, HINT);
                }
            }
            else
            {
                catalogService?.EnsureCover(skin, true);
                UGUIShip.CreateStretchLabel(coverSlot, "ui.no_preview", FS_SM, HINT);
            }

            // Action button. emotes are copy-to-clipboard (not select+apply) — everything else uses Select
            int captured = index;
            if (type == SkinType.Emote && !PickMode)
            {
                var copyBtn = UGUIShip.CreateButton(
                    rowGo.transform,
                    "ui.copy",
                    new Color(0.18f, 0.18f, 0.18f, 1f),
                    YELLOW,
                    FS_SM,
                    new Action(() => OnCopyEmote(captured))
                );
                var copyLE = copyBtn.gameObject.AddComponent<LayoutElement>();
                copyLE.preferredWidth = SEL_W;
                copyLE.minWidth = SEL_W;
                copyLE.preferredHeight = ROW_H - PAD;
            }
            else
            {
                var selBtn = UGUIShip.CreateButton(
                    rowGo.transform,
                    "ui.select",
                    new Color(0.18f, 0.18f, 0.18f, 1f),
                    btnTxtColor,
                    FS_SM,
                    new Action(() => OnToggleSkin(captured))
                );
                var btnGo = selBtn.gameObject;
                var btnLE = btnGo.AddComponent<LayoutElement>();
                btnLE.preferredWidth = SEL_W;
                btnLE.minWidth = SEL_W;
                btnLE.preferredHeight = ROW_H - PAD;

                // register this row's togglable bits so OnToggleSkin can repaint it in place
                _rowVisuals[index] = new RowVisual
                {
                    gradient = gradGo,
                    configBtn = rowConfigBtn,
                    selectLabel = selBtn.transform.Find("Label")?.GetComponent<Text>(),
                    selColor = selColor,
                };
            }

            // delete button — only visible when "Imported Skins" repo is active
            if (ImportedSelected && skin.isLocalImport && !string.IsNullOrEmpty(skin.localPath))
            {
                string capturedFolder = Path.GetDirectoryName(skin.localPath);
                var delBtn = UGUIShip.CreateButton(
                    rowGo.transform, "-", BTN_REMOVE, WHITE, FS_SM,
                    new Action(() => OnDeleteImportedSkin(capturedFolder))
                ).gameObject;
                var delLE = delBtn.AddComponent<LayoutElement>();
                delLE.preferredWidth = BTN_H;
                delLE.minWidth = BTN_H;
                delLE.preferredHeight = ROW_H - PAD;
            }
        }

        // ── Featured repos ────────────────────────────────────────────────────

        private void SetFilterBarVisible(bool visible)
        {
            if (_btnCostumes != null) _btnCostumes.gameObject.SetActive(visible);
            if (_btnAccessories != null) _btnAccessories.gameObject.SetActive(visible);
            if (_btnItems != null) _btnItems.gameObject.SetActive(visible);
            if (_btnEmotes != null) _btnEmotes.gameObject.SetActive(visible);
            if (_filterDivider2Rt != null) _filterDivider2Rt.gameObject.SetActive(visible);

            // absolute layout: hiding the bar alone leaves an empty gap, so pull the search field and
            // scroll view up by the freed height (and grow the scroll so its bottom stays put)
            float dy = visible ? 0f : _filterCollapse;
            if (_searchFieldRt != null)
                UGUIShip.SetPixelRect(_searchFieldRt, new Rect(_searchRectNormal.x, _searchRectNormal.y - dy, _searchRectNormal.width, _searchRectNormal.height));
            if (_scrollViewRt != null)
                UGUIShip.SetPixelRect(_scrollViewRt, new Rect(_scrollRectNormal.x, _scrollRectNormal.y - dy, _scrollRectNormal.width, _scrollRectNormal.height + dy));
        }

        private void RenderFeaturedRepos()
        {
            var featured = repoRegistry?.Featured;
            if (featured == null || featured.Count == 0)
            {
                CreateFeaturedEmptyLabel(repoRegistry != null && repoRegistry.FeaturedFetched
                    ? "No featured repositories yet."
                    : "Loading featured repositories...");
                return;
            }

            string q = _searchQuery?.ToLower() ?? "";
            int shown = 0;
            foreach (var f in featured)
            {
                var repo = RepoRegistry.ParseRepo(f.url);
                if (repo == null) continue;
                if (!string.IsNullOrEmpty(q) && !repo.DisplayName.ToLower().Contains(q)
                    && !(f.description ?? "").ToLower().Contains(q)) continue;
                CreateFeaturedRepoItem(repo, f.description);
                shown++;
            }

            if (shown == 0) CreateFeaturedEmptyLabel(string.Format(EMPTY_NO_RESULTS, _searchQuery));
        }

        // one card in the Featured list: a tall vertical card — repo cover banner on top (with margin),
        // then name/author/description, then a full-width action button. the gradient scrim is kept as
        // the card background
        private void CreateFeaturedRepoItem(SkinRepo repo, string description)
        {
            bool added = repoRegistry != null && repoRegistry.HasRepo(repo.githubUrl);

            var rowGo = new GameObject("FeaturedRepo_" + repo.repoName);
            rowGo.transform.SetParent(_scrollContent, false);
            rowGo.AddComponent<RectTransform>();
            rowGo.AddComponent<Image>().color = ITEM_BG;

            // kept background: the gradient scrim stretched over the whole card, behind the content
            var overlaySprite = BetterFG.Utilities.EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.reposelector.repo_overlay.png");
            if (overlaySprite != null)
            {
                var ovGo = new GameObject("CardBg");
                ovGo.transform.SetParent(rowGo.transform, false);
                var ovRt = ovGo.AddComponent<RectTransform>();
                ovRt.anchorMin = Vector2.zero;
                ovRt.anchorMax = Vector2.one;
                ovRt.offsetMin = ovRt.offsetMax = Vector2.zero;
                ovGo.AddComponent<LayoutElement>().ignoreLayout = true;
                var ovImg = ovGo.AddComponent<Image>();
                ovImg.sprite = overlaySprite;
                ovImg.raycastTarget = false;
            }

            // vertical stack with a margin around the content (left carries the reclaimed row indent)
            var vlg = rowGo.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = PAD * 0.75f;
            vlg.padding = new RectOffset((int)(PAD + _rowIndent), (int)PAD, (int)PAD, (int)PAD);

            // cover banner on top — fixed height, aspect-fit inside (no stretch), centered
            float bannerH = COVER_H * 0.5f;
            var bannerGo = new GameObject("CoverBanner");
            bannerGo.transform.SetParent(rowGo.transform, false);
            bannerGo.AddComponent<RectTransform>();
            var bannerLE = bannerGo.AddComponent<LayoutElement>();
            bannerLE.preferredHeight = bannerH;
            bannerLE.minHeight = bannerH;

            // rounded delux frame; the Mask inside clips the cover that overfills the banner width
            var (_, coverSlot) = UGUIShip.CreateFramedImage(bannerGo.transform);
            // envelope so every banner fills the full card width uniformly (crop overflow, no letterbox)
            var arf = coverSlot.gameObject.AddComponent<AspectRatioFitter>();
            arf.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            var coverImg = coverSlot.gameObject.AddComponent<RawImage>();
            coverImg.color = new Color(1f, 1f, 1f, 0f);
            coverImg.raycastTarget = false;
            _featuredCoverImages[repo.githubUrl] = coverImg;
            var cachedTex = repoRegistry.GetCover(repo);
            if (cachedTex != null)
            {
                coverImg.texture = cachedTex;
                coverImg.color = Color.white;
                if (cachedTex.height > 0) arf.aspectRatio = (float)cachedTex.width / cachedTex.height;
            }
            else repoRegistry.FetchCover(repo);

            // text block
            var infoGo = new GameObject("Info");
            infoGo.transform.SetParent(rowGo.transform, false);
            infoGo.AddComponent<RectTransform>();
            var infoVlg = infoGo.AddComponent<VerticalLayoutGroup>();
            infoVlg.childForceExpandHeight = false;
            infoVlg.childForceExpandWidth = true;
            infoVlg.spacing = 0f;

            UGUIShip.CreateFlowLabel(infoGo.transform, repo.repoName, FS, WHITE);
            UGUIShip.CreateFlowLabel(infoGo.transform, LocalizationService.Format("ui.by_author_fmt", repo.author), FS_SM, HINT);
            if (!string.IsNullOrEmpty(description))
                UGUIShip.CreateFlowLabel(infoGo.transform, description, FS_SM, HINT, multiline: true);

            // full-width action button at the bottom
            string capturedUrl = repo.githubUrl;
            var actBtn = added
                ? UGUIShip.CreateButton(rowGo.transform, "ui.select", new Color(0.18f, 0.18f, 0.18f, 1f), YELLOW, FS_SM, new Action(() => OnSelectFeatured(capturedUrl)))
                : UGUIShip.CreateButton(rowGo.transform, "ui.add", BTN_FETCH, WHITE, FS_SM, new Action(() => OnAddFeatured(capturedUrl)));
            actBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = BTN_H;
        }

        private void CreateFeaturedEmptyLabel(string msg)
        {
            var lbl = UGUIShip.CreateFlowLabel(_scrollContent, msg, FS_SM, HINT);
            lbl.alignment = TextAnchor.MiddleCenter;
            var le = lbl.GetComponent<LayoutElement>();
            le.preferredHeight = LH * 2f;
            le.flexibleWidth = 1f;
        }

        // AddRepo validates (marker + dedupe) async; on success OnReposChanged rebuilds the list
        // and this card flips to "Select"
        private void OnAddFeatured(string url) => repoRegistry?.AddRepo(url);

        private void OnSelectFeatured(string url)
        {
            var repo = repoRegistry?.FindRepo(url);
            if (repo == null) return;
            SelectedRepo = repo;
            FeaturedSelected = false;
            repoRegistry.SetActive(repo);
            OnRepoChanged();
        }

        private void OnDeleteImportedSkin(string folderPath)
        {
            _importedPaths.Remove(folderPath);
            SaveImportedPaths();

            for (int i = availableSkins.Count - 1; i >= 0; i--)
            {
                var s = availableSkins[i];
                if (!s.isLocalImport || string.IsNullOrEmpty(s.localPath)) continue;
                if (!string.Equals(Path.GetDirectoryName(s.localPath), folderPath, StringComparison.OrdinalIgnoreCase)) continue;
                // fix selectedIndices for the removed entry shifting everything above it down
                selectedIndices.Remove(i);
                var shifted = new HashSet<int>();
                foreach (int idx in selectedIndices)
                    shifted.Add(idx > i ? idx - 1 : idx);
                selectedIndices = shifted;
                availableSkins.RemoveAt(i);
                break;
            }

            SaveSelection();
            RefreshSkinList();
        }

        // ── Emote copy ────────────────────────────────────────────────────────

        // stash the emote's bundle/clip/sound/cover urls so the Emotes section of the
        // Emoticons & Phrases tab can paste it (download + fill an EmoteEntry).
        private void OnCopyEmote(int index)
        {
            if (index < 0 || index >= availableSkins.Count) return;

            // second press on an already-copied row: open Social > Emotes (as the 3rd tab)
            if (_copyArmedIndex == index)
            {
                _copyArmedIndex = -1;
                BetterFGUIMan.Instance?.OpenSocialEmotes();
                return;
            }

            var skin = availableSkins[index];

            string repoRaw = RepoRegistry.ResolveRaw(skin.sourceRepo);
            string folder = !string.IsNullOrEmpty(skin.repoFolder) ? skin.repoFolder : $"Emotes/{skin.file}";
            string bundleUrl = $"{repoRaw}/{folder}/{skin.file}";
            string soundUrl = string.IsNullOrEmpty(skin.audio) ? "" : $"{repoRaw}/{folder}/{skin.audio}";
            string coverUrl = $"{repoRaw}/{folder}/cover.jpg"; // paste falls back to .png if this 404s

            // hand over the cover we already have loaded so the paste button can preview it instantly
            Texture2D cover = null;
            if (!skinCovers.TryGetValue(CoverKey(skin), out cover) || cover == null)
                catalogService?.TryGetCover(skin, out cover);

            BetterFG.Customization.Social.EmoteClipboard.Set(skin.name, bundleUrl, soundUrl, coverUrl, skin.audio ?? "", cover);

            // arm this row — the prompt to open lives in the tooltip, never on the button
            _copyArmedIndex = index;
            BetterFGUIMan.Instance?.ShowTooltipTimed("Copied, click again to open Social>Emotes as 3rd tab", 3f);
        }

        // ── Selection toggle ──────────────────────────────────────────────────

        private void OnToggleSkin(int index)
        {
            if (index < 0 || index >= availableSkins.Count) return;
            SkinInfo skin = availableSkins[index];
            SkinType type = SkinTypeParser.FromString(skin.type);

            // track every row whose selected-state changed so we can repaint just those in place
            // instead of rebuilding the entire list (the click freeze). a single click can flip
            // two rows: the clicked one + whatever costume/plinth got auto-deselected.
            var changed = new List<int> { index };

            if (selectedIndices.Contains(index))
            {
                selectedIndices.Remove(index);
            }
            else
            {
                if (!SelectIndexRespectingLimits(index, type, out int evicted)) return;
                if (evicted != -1) changed.Add(evicted);
            }

            SaveSelection();
            foreach (int ci in changed) RepaintRowSelection(ci);
        }

        // enforces the same per-type slot limits whether a row is clicked or a skin lands via
        // import — importing used to add straight to selectedIndices with no cap/eviction check,
        // so importing a 2nd Costume left both "selected" but only the last-applied one actually
        // showed on the bean
        private bool SelectIndexRespectingLimits(int index, SkinType type, out int evicted)
        {
            evicted = -1;
            switch (type)
            {
                case SkinType.Costume:
                    int existing = SelectedCostumeIndex();
                    if (existing != -1 && existing != index) { selectedIndices.Remove(existing); evicted = existing; }
                    selectedIndices.Add(index);
                    return true;

                case SkinType.Accessory:
                    if (CountSelected(SkinType.Accessory) >= MAX_ACCESSORY)
                    { SetStatus($"Max {MAX_ACCESSORY} accessories."); return false; }
                    selectedIndices.Add(index);
                    return true;

                case SkinType.Item:
                    if (CountSelected(SkinType.Item) >= MAX_ITEM)
                    { SetStatus($"Max {MAX_ITEM} items."); return false; }
                    selectedIndices.Add(index);
                    return true;

                default:
                    selectedIndices.Add(index);
                    return true;
            }
        }

        // flip a single row's selection visuals (gradient, Configure btn, Select label colour)
        // to match its current selectedIndices state — no list rebuild, no layout pass
        private void RepaintRowSelection(int index)
        {
            if (!_rowVisuals.TryGetValue(index, out var rv) || rv == null) return;
            bool sel = selectedIndices.Contains(index);
            if (rv.gradient != null) rv.gradient.SetActive(sel);
            if (rv.configBtn != null) rv.configBtn.SetActive(sel);
            if (rv.selectLabel != null) rv.selectLabel.color = sel ? rv.selColor : WHITE;
        }

        // ── Persist & restore ─────────────────────────────────────────────────

        private const string KEY_MULTI_REPOS = "skin.multi.repos";
        private const string KEY_MULTI_FOLDERS = "skin.multi.folders";

        private void SaveSelection()
        {
            if (PickMode)
            {
                PickTargetList.Clear();
                foreach (int i in selectedIndices)
                {
                    if (i < 0 || i >= availableSkins.Count) continue;
                    var picked = availableSkins[i];
                    if (SkinTypeParser.FromString(picked.type) == SkinType.Plinth) continue;
                    PickTargetList.Add(BeanLook.FromSkin(picked));
                }
                Plugin.Log.LogInfo($"pet picker now holds {PickTargetList.Count} ugc pieces");
                OnPickChanged?.Invoke();
                return;
            }

            bool hadPlinth = MenuCustomizationApplication.TryGetSavedPlinthEntry(
                out string plinthFile, out string plinthSource, out string plinthPath, out string plinthRepo,
                out string plinthFolder);

            var files = new List<string>();
            var sources = new List<string>();
            var paths = new List<string>();
            var repos = new List<string>();
            var types = new List<string>();
            var folders = new List<string>();

            foreach (int i in selectedIndices)
            {
                if (i < 0 || i >= availableSkins.Count) continue;
                SkinInfo s = availableSkins[i];
                if (SkinTypeParser.FromString(s.type) == SkinType.Plinth) continue;
                files.Add(s.file);
                sources.Add(s.isLocalImport ? "local" : "remote");
                paths.Add(s.isLocalImport && !string.IsNullOrEmpty(s.localPath)
                    ? Path.GetDirectoryName(s.localPath) : "");
                repos.Add(s.sourceRepo ?? "");
                types.Add(s.type ?? "");
                folders.Add(s.repoFolder ?? "");
            }

            SettingsService.Set(KEY_MULTI_FILES, string.Join(",", files));
            SettingsService.Set(KEY_MULTI_SOURCES, string.Join(",", sources));
            SettingsService.Set(KEY_MULTI_PATHS, string.Join(",", paths));
            SettingsService.Set(KEY_MULTI_REPOS, string.Join(",", repos));
            SettingsService.Set(KEY_MULTI_TYPES, string.Join(",", types));
            SettingsService.Set(KEY_MULTI_FOLDERS, string.Join(",", folders));

            if (hadPlinth)
                MenuCustomizationApplication.SavePlinthEntry(plinthFile, plinthSource, plinthPath, plinthRepo, plinthFolder);
        }

        private void SaveHandOverrides()
        {
            var parts = new List<string>();
            foreach (var kvp in _handOverrides)
                parts.Add($"{kvp.Key}:{kvp.Value}");
            SettingsService.Set(KEY_HAND_OVERRIDES, string.Join(",", parts));
        }

        private void LoadHandOverrides() => _handOverrides = BetterFG.Network.RemoteProfileStore.LocalHandOverrides();

        // pick mode's equivalent of TryRestoreSelection: light up whatever the pet already wears
        private void SeedPickSelection()
        {
            selectedIndices.Clear();
            foreach (var entry in PickTargetList)
            {
                string file = entry.file;
                int idx = availableSkins.FindIndex(s => s.file == file);
                if (idx >= 0) selectedIndices.Add(idx);
            }
            RefreshSkinList();
        }

        private void TryRestoreSelection()
        {
            LoadHandOverrides();
            string multiFiles = SettingsService.Get(KEY_MULTI_FILES);
            string multiSources = SettingsService.Get(KEY_MULTI_SOURCES);
            string multiPaths = SettingsService.Get(KEY_MULTI_PATHS);
            string multiRepos = SettingsService.Get(KEY_MULTI_REPOS);

            if (string.IsNullOrEmpty(multiFiles))
            {
                string legacyFile = SettingsService.Get(KEY_FILE);
                if (!string.IsNullOrEmpty(legacyFile))
                {
                    multiFiles = legacyFile;
                    multiSources = SettingsService.Get(KEY_SOURCE);
                    multiPaths = SettingsService.Get(KEY_LOCAL_PATH);
                }
            }

            if (string.IsNullOrEmpty(multiFiles)) return;

            string[] files = multiFiles.Split(',');
            string[] sources = multiSources?.Split(',') ?? new string[0];
            string[] paths = multiPaths?.Split(',') ?? new string[0];
            string[] repos = multiRepos?.Split(',') ?? new string[0];

            selectedIndices.Clear();
            _pendingRestoreFiles.Clear();

            for (int s = 0; s < files.Length; s++)
            {
                string file = files[s].Trim();
                string source = s < sources.Length ? sources[s].Trim() : "remote";
                string path = s < paths.Length ? paths[s].Trim() : "";
                string repo = s < repos.Length ? repos[s].Trim() : "";

                if (source == "local") continue; // local imports are handled by SkinApplicationService.RestoreFromSettings

                int idx = availableSkins.FindIndex((sk) => sk.file == file);
                if (idx >= 0)
                {
                    if (!string.IsNullOrEmpty(repo)) availableSkins[idx].sourceRepo = repo;
                    selectedIndices.Add(idx);
                }
                else
                    _pendingRestoreFiles.Add((file, repo));
            }

            RefreshSkinList();
            // NOTE: do NOT call OnApply here — SkinApplicationService.RestoreFromSettings handles actual application on boot
        }

        private void RetryPendingRestore()
        {
            if (_pendingRestoreFiles.Count == 0) return;
            var stillPending = new List<(string file, string repo)>();
            bool anyFound = false;

            foreach (var (file, repo) in _pendingRestoreFiles)
            {
                int idx = availableSkins.FindIndex((sk) => sk.file == file);
                if (idx >= 0)
                {
                    if (!string.IsNullOrEmpty(repo)) availableSkins[idx].sourceRepo = repo;
                    selectedIndices.Add(idx);
                    anyFound = true;
                }
                else stillPending.Add((file, repo));
            }

            _pendingRestoreFiles = stillPending;
            if (anyFound) RefreshSkinList();
        }

        // ── Callbacks ─────────────────────────────────────────────────────────

        private void OnSkinsLoaded(List<SkinInfo> skins)
        {
            SwapCatalog(skins);
            RefreshSkinList();
            SetStatus($"Loaded {skins.Count} customizations");
            // sync selection from already-applied slots every time new skins arrive
            // handles the case where a skin came from a repo that was fetched late (e.g. plinth repo)
            SyncSelectedFromApplied();
            RetryPendingRestore();
        }

        private bool _restoredOnce = false;

        private void OnFetchCompleted()
        {
            SwapCatalog(catalogService?.AvailableSkins);
            RefreshSkinList();

            if (!_restoredOnce)
            {
                _restoredOnce = true;
                // never pull the local player's own restored selection into a pick-mode instance -
                // it has nothing to do with what's being picked for the pet
                if (PickMode) SeedPickSelection();
                else TryRestoreSelection();
            }

            RetryPendingRestore();
        }

        // selectedIndices are positions in availableSkins, so replacing the list outright silently
        // repoints every selection at whatever skin now sits at that index (a second repo's catalog
        // landing turned an equipped costume into someone else's item, and SaveSelection wrote it).
        // re-resolve by file across the swap; anything the new catalog dropped goes back on the
        // pending list so RetryPendingRestore picks it up when its repo returns.
        private void SwapCatalog(List<SkinInfo> skins)
        {
            var wasSelected = new List<string>();
            foreach (int i in selectedIndices)
                if (i >= 0 && i < availableSkins.Count) wasSelected.Add(availableSkins[i].file);

            availableSkins = MergeImported(skins);

            selectedIndices.Clear();
            foreach (string file in wasSelected)
            {
                int idx = availableSkins.FindIndex(s => s.file == file);
                if (idx >= 0) selectedIndices.Add(idx);
                else if (_pendingRestoreFiles.FindIndex(p => p.file == file) < 0)
                    _pendingRestoreFiles.Add((file, ""));
            }
        }

        // catalog callbacks return only remote skins — fold the persisted local imports
        // back in (re-seeding from disk if they were dropped) so they survive re-fetches
        private List<SkinInfo> MergeImported(List<SkinInfo> catalogSkins)
        {
            var merged = catalogSkins != null ? new List<SkinInfo>(catalogSkins) : new List<SkinInfo>();

            // keep any local imports already live in availableSkins
            foreach (var s in availableSkins)
                if (s.isLocalImport && !string.IsNullOrEmpty(s.localPath) &&
                    merged.FindIndex(x => x.isLocalImport && x.localPath == s.localPath) < 0)
                    merged.Add(s);

            // re-seed any persisted imports that aren't present anymore (e.g. after ClearCache)
            foreach (string folder in _importedPaths)
            {
                if (string.IsNullOrEmpty(folder)) continue;
                if (merged.FindIndex(x => x.isLocalImport && !string.IsNullOrEmpty(x.localPath) &&
                    string.Equals(Path.GetDirectoryName(x.localPath), folder, StringComparison.OrdinalIgnoreCase)) >= 0)
                    continue;
                var seeded = LoadImportedFromFolder(folder);
                if (seeded != null) merged.Add(seeded);
            }

            return merged;
        }

        // reflect already-applied slots back into the UI selection state
        private void SyncSelectedFromApplied()
        {
            if (applicationService == null || PickMode) return;
            var applied = applicationService.GetActiveSlots();
            foreach (var slot in applied)
            {
                int idx = availableSkins.FindIndex(s => s.file == slot.skinInfo.file);
                if (idx >= 0 && !selectedIndices.Contains(idx))
                    selectedIndices.Add(idx);
            }
            if (selectedIndices.Count > 0) RefreshSkinList();
        }

        private void OnRepoCoverLoaded(SkinRepo repo, Texture2D tex)
        {
            if (repo == null || tex == null) return;
            if (_featuredCoverImages.TryGetValue(repo.githubUrl, out var fimg) && fimg != null)
            {
                fimg.texture = tex; fimg.color = Color.white;
                var arf = fimg.GetComponent<AspectRatioFitter>();
                if (arf != null && tex.height > 0) arf.aspectRatio = (float)tex.width / tex.height;
            }
        }

        private void OnFeaturedLoaded()
        {
            if (FeaturedSelected) RefreshSkinList();
        }

        private void OnSkinCoverLoaded(string key, Texture2D tex)
        {
            if (tex == null || tex.width == 0) return;
            skinCovers[key] = tex;
            if (_coverImages.TryGetValue(key, out Image img) && img != null)
                try { ApplyCover(img, tex); }
                catch (Exception ex) { Plugin.Log.LogWarning($"SkinUI: Cover sprite failed: {ex.Message}"); }
        }

        private static void ApplyCover(Image img, Texture2D tex)
        {
            for (int i = img.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(img.transform.GetChild(i).gameObject);
            tex.wrapMode = TextureWrapMode.Clamp;
            img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);
            img.preserveAspect = true;
            img.color = Color.white;
        }

        private static string CoverKey(SkinInfo skin)
        {
            if (skin == null || string.IsNullOrEmpty(skin.file)) return "";
            string repo = !string.IsNullOrEmpty(skin.sourceRepo) ? skin.sourceRepo : skin.localPath ?? "";
            return repo + "|" + skin.file;
        }

        private void OnSkinDownloaded(SkinInfo _, AssetBundle bundle)
        {
            if (_pendingApplyQueue.Count == 0) return;

            SkinInfo skin = _pendingApplyQueue[0];
            _pendingApplyQueue.RemoveAt(0);

            SetStatus($"Downloaded {skin.name}, applying...");
            applicationService.ApplySkin(skin, bundle, additive: true);
            KickNextPending();
        }

        private void OnSkinImported(SkinInfo skinInfo, AssetBundle bundle, Texture2D cover)
        {
            int idx = availableSkins.FindIndex((s) => s.file == skinInfo.file);
            if (idx < 0) { availableSkins.Add(skinInfo); idx = availableSkins.Count - 1; }
            else availableSkins[idx] = skinInfo;

            if (!selectedIndices.Contains(idx))
                SelectIndexRespectingLimits(idx, SkinTypeParser.FromString(skinInfo.type), out _);

            if (cover != null) skinCovers[CoverKey(skinInfo)] = cover;

            // persist the imported folder path
            if (!string.IsNullOrEmpty(skinInfo.localPath))
            {
                string folder = Path.GetDirectoryName(skinInfo.localPath);
                if (!string.IsNullOrEmpty(folder) && !_importedPaths.Contains(folder))
                {
                    _importedPaths.Add(folder);
                    SaveImportedPaths();
                }
            }

            // jump to the Imported Skins section — otherwise whatever repo filter was active
            // before the import stays selected and the just-imported skin isn't in it, so the
            // list looks empty even though the import succeeded
            if (!ImportedSelected)
            {
                ImportedSelected = true;
                FeaturedSelected = false;
                OnRepoChanged();
            }

            RefreshSkinList();
            SetStatus($"Imported {skinInfo.name}, applying...");
            applicationService.ApplySkin(skinInfo, bundle, additive: true);
            // remove from queue (KickNextPending left it in when it triggered the import) then advance
            int qi = _pendingApplyQueue.FindIndex(s => s.file == skinInfo.file);
            if (qi >= 0) _pendingApplyQueue.RemoveAt(qi);
            KickNextPending();
            SaveSelection();
        }

        // OnStatusUpdate fires per skin during a fetch ("Loading... (N)"). The count label only
        // exists in the empty-state branch, and as soon as the first skin for the active section
        // lands the list stops being empty and the label is gone — so just rebuilding it here goes
        // stale the moment rows show up. Rebuild the whole list per tick while a fetch is running so
        // new rows AND the count come in live, instead of only when you click a section to redraw.
        private void SetStatus(string msg)
        {
            if (catalogService != null && catalogService.IsFetching)
            {
                RefreshSkinList();
                return;
            }

            if (_fetchCountLabel != null && catalogService != null)
            {
                string activeRaw = SelectedRaw;
                int repoTotal = catalogService.GetCatalogTotalForRepo(activeRaw);
                if (repoTotal > 0)
                    _fetchCountLabel.text = $"{catalogService.GetFetchedCountForRepo(activeRaw)} / {repoTotal} fetched";
            }
        }

        private void OpenConfigWindow(string file)
        {
            if (_configWindow != null)
            {
                if (_configWindowFile == file)
                {
                    // If it's just hidden (tab was closed), re-show it
                    if (!_configWindow.IsVisible)
                    {
                        _configWindow.Configure(availableSkins.Find(s => s.file == file), applicationService, this);
                        return;
                    }
                    // Visible and same file — toggle close
                    Destroy(_configWindow.gameObject);
                    _configWindow = null;
                    _configWindowFile = null;
                    return;
                }
                Destroy(_configWindow.gameObject);
            }
            var skin = availableSkins.Find(s => s.file == file);
            if (skin == null) return;
            var go = new GameObject("BetterFG_ItemConfig");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _configWindow = go.AddComponent<ItemConfigWindow>();
            _configWindow.Configure(skin, applicationService, this);
            _configWindowFile = file;
        }

        // ── Button handlers ───────────────────────────────────────────────────

        private void OnFetch()
        {
            if (catalogService == null) return;
            catalogService.ClearCache();
            availableSkins.Clear();
            skinCovers.Clear();
            _restoredOnce = false;
            RefreshSkinList();
            FetchSelectedRepo();
        }

        private void OnImport()
        {
            WinDialogs.PickFolder("Select Skin Folder", path =>
            {
                if (!string.IsNullOrEmpty(path)) loaderService?.ImportSkinFromFolder(path);
            });
        }

        private void OnApply()
        {
            _pendingApplyQueue.Clear();

            var wantedFiles = new HashSet<string>();
            var wantedSkins = new List<SkinInfo>();
            // snapshot what's currently equipped (file -> live hand override) BEFORE we mutate
            // any skinInfo. the active slot shares the same SkinInfo reference as availableSkins,
            // so writing skin.handOverride below also changes the slot's value — we'd never see
            // the change if we compared after. capture it up front.
            var liveHandOverrides = new Dictionary<string, int>();
            foreach (var slot in applicationService.GetActiveSlots())
                if (slot?.skinInfo != null)
                    liveHandOverrides[slot.skinInfo.file] = slot.skinInfo.handOverride;

            foreach (int i in selectedIndices)
            {
                if (i < 0 || i >= availableSkins.Count) continue;
                var skin = availableSkins[i];
                if (SkinTypeParser.FromString(skin.type) == SkinType.Plinth) continue;
                if (_handOverrides.ContainsKey(skin.file))
                    skin.handOverride = _handOverrides[skin.file];
                wantedFiles.Add(skin.file);
                wantedSkins.Add(skin);
            }

            // DIFF instead of nuke-everything: only unequip what's no longer selected, and
            // only download/apply what isn't already on. unchanged slots are left untouched so
            // changing one item (or the plinth) doesn't re-run the whole loadout = no flicker
            foreach (var slot in applicationService.GetActiveSlots())
            {
                if (slot?.skinInfo == null) continue;
                if (!wantedFiles.Contains(slot.skinInfo.file))
                    applicationService.RemoveOneSkinByFile(slot.skinInfo.file);
            }

            foreach (var skin in wantedSkins)
            {
                if (applicationService.HasActiveSlotForFile(skin))
                {
                    // already equipped. the only per-item change the menu can make to a live skin
                    // is the L/R hand override — compare against the snapshot (not the live slot,
                    // which now shares skin's mutated value). unchanged = leave it fully alone.
                    int wasOverride = liveHandOverrides.TryGetValue(skin.file, out int v) ? v : skin.handOverride;
                    if (wasOverride == skin.handOverride)
                        continue;
                    // hand override changed — respawn just this item from its cached bundle
                    // (no redownload, nothing else on the bean disturbed). only queue a full
                    // download if the bundle somehow isn't loaded anymore.
                    if (applicationService.TryReapplyLoadedSkin(skin))
                        continue;
                }
                _pendingApplyQueue.Add(skin);
            }
            _pendingTotal = _pendingApplyQueue.Count;

            // Costumes first, then accessories, then items
            _pendingApplyQueue.Sort((a, b) =>
            {
                int ScoreOf(SkinInfo s)
                {
                    switch (SkinTypeParser.FromString(s.type))
                    {
                        case SkinType.Costume: return 0;
                        case SkinType.Accessory: return 1;
                        case SkinType.Item: return 2;
                        default: return 3;
                    }
                }
                return ScoreOf(a).CompareTo(ScoreOf(b));
            });

            SaveSelection();
            KickNextPending();
        }

        private void KickNextPending()
        {
            if (_pendingApplyQueue.Count == 0)
            {
                SetStatus($"Applied {_pendingTotal} customization(s).");
                StartCoroutine(ReapplySkinTexturesAfterApply().WrapToIl2Cpp());
                return;
            }

            SkinInfo next = _pendingApplyQueue[0];
            SetStatus($"Loading {next.name}... ({_pendingTotal - _pendingApplyQueue.Count + 1}/{_pendingTotal})");

            if (next.isLocalImport && !string.IsNullOrEmpty(next.localPath))
            {
                loaderService?.ImportSkinFromFolder(Path.GetDirectoryName(next.localPath));
                return;
            }

            string repoRaw = RepoRegistry.ResolveRaw(next.sourceRepo);

            string category = SkinTypeParser.CategoryFolder(next.type);
            string folder = !string.IsNullOrEmpty(next.repoFolder) ? next.repoFolder : $"{category}/{next.file}";
            string url = $"{repoRaw}/{folder}/{next.file}";
            string infoUrl = $"{repoRaw}/{folder}/info.json";

            Plugin.Log.LogInfo($"BetterFG: Downloading: {url}");

            // stamp sourceRepo so downstream (SkinApplicationService) can resolve correctly
            next.sourceRepo = repoRaw;

            // size gate before kicking the download
            StartCoroutine(KickWithSizeCheck(next, url, infoUrl).WrapToIl2Cpp());
        }

        private System.Collections.IEnumerator ReapplySkinTexturesAfterApply()
        {
            yield return new WaitForSeconds(0.35f);
            SkinApplicationService.ReapplyAllEnabledFromSettings();
        }

        private System.Collections.IEnumerator KickWithSizeCheck(SkinInfo next, string url, string infoUrl)
        {
            bool sizeOk = false;
            string sizeErr = null;
            yield return RepoRegistry.CheckBundleSize(url, (ok, err) => { sizeOk = ok; sizeErr = err; });
            if (!sizeOk)
            {
                SetStatus($"Skipped {next.name}: {sizeErr}");
                if (_pendingApplyQueue.Count > 0 && _pendingApplyQueue[0] == next)
                    _pendingApplyQueue.RemoveAt(0);
                KickNextPending();
                yield break;
            }
            loaderService?.DownloadSkinWithInfo(next.file, url, infoUrl);
        }


        private void OnRemoveAll()
        {
            SkinType filter = _activeFilter;

            // clear the UI selection for this filter
            var toRemove = new List<int>();
            foreach (int i in selectedIndices)
            {
                if (i < 0 || i >= availableSkins.Count) continue;
                if (SkinTypeParser.FromString(availableSkins[i].type) == filter)
                    toRemove.Add(i);
            }
            foreach (int i in toRemove)
            {
                selectedIndices.Remove(i);
                if (filter == SkinType.Item && availableSkins[i] != null)
                    _handOverrides.Remove(availableSkins[i].file);
            }
            _pendingApplyQueue.RemoveAll(s => SkinTypeParser.FromString(s.type) == filter);

            // strip whatever is actually APPLIED to the bean of this type. the UI selection
            // can drift from what's equipped (selected-not-applied, or applied-not-selected),
            // so walk the active slots directly instead of trusting selectedIndices — GetActiveSlots
            // returns a copy so removing while iterating is fine
            if (applicationService != null)
            {
                foreach (var slot in applicationService.GetActiveSlots())
                {
                    if (slot?.skinInfo == null || string.IsNullOrEmpty(slot.skinInfo.file)) continue;
                    if (slot.type != filter) continue;
                    applicationService.RemoveOneSkinByFile(slot.skinInfo.file);
                    if (filter == SkinType.Item)
                        _handOverrides.Remove(slot.skinInfo.file);
                }
            }

            if (filter == SkinType.Item && _configWindow != null)
            {
                Destroy(_configWindow.gameObject);
                _configWindow = null;
                _configWindowFile = null;
            }

            SaveSelection();
            SaveHandOverrides();
            RefreshSkinList();
            SetStatus($"Removed all {filter.ToString().ToLower()}s.");
        }

        // ── UGUI helpers ──────────────────────────────────────────────────────

        private void BuildSearchField(Transform parent, float y, float w)
        {
            var go = new GameObject("SearchField");
            go.transform.SetParent(parent, false);
            _searchFieldRt = go.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(_searchFieldRt, new Rect(PAD, y, w, LH));

            // search icon on the left — same sizing as AddSearchIcon (0.75 * fontSize)
            float iconSize = FS_SM * 0.75f;
            float iconLeft = 2f;
            float textLeft = iconLeft + iconSize + 4f;
            var iconSprite = BetterFG.Utilities.EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.button.search.png");
            if (iconSprite != null)
            {
                var iconGo = new GameObject("SearchIcon");
                iconGo.transform.SetParent(go.transform, false);
                var iRt = iconGo.AddComponent<RectTransform>();
                iRt.anchorMin = new Vector2(0f, 0.5f);
                iRt.anchorMax = new Vector2(0f, 0.5f);
                iRt.pivot = new Vector2(0f, 0.5f);
                iRt.anchoredPosition = new Vector2(iconLeft, 0f);
                iRt.sizeDelta = new Vector2(iconSize, iconSize);
                var iImg = iconGo.AddComponent<Image>();
                iImg.sprite = iconSprite;
                iImg.preserveAspect = true;
                iImg.raycastTarget = false;
            }

            _searchPlaceholder = UGUIShip.CreateLabel(go.transform, default, "ui.search_2", FS_SM,
                new Color(1f, 1f, 1f, 0.2f), TextAnchor.MiddleLeft);
            _searchPlaceholder.fontStyle = FontStyle.Italic;
            var phRt = _searchPlaceholder.rectTransform;
            phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(textLeft, 0f); phRt.offsetMax = Vector2.zero;

            _searchText = UGUIShip.CreateLabel(go.transform, default, "", FS_SM, WHITE, TextAnchor.MiddleLeft);
            var txtRt = _searchText.rectTransform;
            txtRt.anchorMin = Vector2.zero; txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = new Vector2(textLeft, 0f); txtRt.offsetMax = Vector2.zero;

            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;
            btn.onClick.AddListener(new Action(() => { _searchActive = true; SetFakeInputLock(true); UpdateSearchCaret(); }));
            go.AddComponent<Image>().color = Color.clear;
        }

        private static Rect PR(float y, float w, float h) => new Rect(PAD, y, w, h);

    }
}
