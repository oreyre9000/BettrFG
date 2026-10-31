using System;
using System.Collections.Generic;
using BetterFG.Network;
using BetterFG.Services;
using BetterFG.Utilities;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class RepoSelectorTab : Tab
    {
        public RepoSelectorTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Repositories";
        protected override string TitleId => "ui.repositories";
        protected override string BgResource => "BetterFG.assets.ui.tab.reposelector.png";
        public override float OpenHeight => UIScale.TAB_CONTENT_H * 1.3f;

        public UGCTab Host;

        static readonly Color WHITE = UGUIShip.WHITE;
        static readonly Color HINT = new Color(1f, 1f, 1f, 0.5f);
        static readonly Color GOLD = new Color(1f, 0.82f, 0.2f, 1f);
        static readonly Color ORANGE = new Color(1f, 0.62f, 0.25f, 1f);
        static readonly Color YELLOW = new Color(1f, 1f, 0f, 1f);
        static readonly Color BLUE = new Color(0.35f, 0.72f, 1f, 1f);
        static readonly Color BTN_DARK = Color.black;
        static readonly Color BTN_ROW = new Color(0.12f, 0.12f, 0.12f, 1f);
        static readonly Color BTN_ACCENT = new Color(0.12f, 0.25f, 0.36f, 1f);
        static readonly Color BTN_PRIMARY = new Color(0.12f, 0.3f, 0.45f, 1f);
        static readonly Color BTN_CONFIRM = new Color(0.12f, 0.3f, 0.18f, 1f);

        const float HEADER_H = 30f;
        const float ROW_H = 40f;
        const float RESULT_H = 44f;
        const float SPOT_RADIUS = 95f, SPOT_FEATHER = 75f, SPOT_GAIN = 1.8f;

        static Texture2D _importedTex, _featuredTex;

        RectTransform _pageList, _pageSearch, _pageCard;
        RectTransform _listContent, _searchContent, _addBar;
        Text _listStatus, _searchStatus;
        string _listQuery = "", _searchQuery = "";
        readonly List<RepoSearchResult> _results = new List<RepoSearchResult>();
        RepoSearchResult _cardResult;
        bool _searched;
        readonly Dictionary<string, List<RawImage>> _coverImages = new Dictionary<string, List<RawImage>>();

        static RepoRegistry Registry => RepoRegistry.Instance;
        float _contentH;
        float W => TabWidth - PAD * 2f;
        float H => _contentH;

        static readonly List<KeyValuePair<string, RawImage>> _hostRowCovers = new List<KeyValuePair<string, RawImage>>();
        static bool _hostCoverHooked;

        static void OnHostRowCoverLoaded(SkinRepo repo, Texture2D tex)
        {
            if (repo == null || tex == null) return;
            for (int i = _hostRowCovers.Count - 1; i >= 0; i--)
            {
                var e = _hostRowCovers[i];
                if (e.Value == null) { _hostRowCovers.RemoveAt(i); continue; }
                if (e.Key != repo.githubUrl) continue;
                SetCover(e.Value, tex);
            }
        }

        static void SetCover(RawImage img, Texture2D tex)
        {
            img.texture = tex;
            img.color = Color.white;
            if (tex.height > 0) img.GetComponent<AspectRatioFitter>().aspectRatio = (float)tex.width / tex.height;
        }

        static void TrackHostRowCover(string url, RawImage img)
        {
            if (img == null || string.IsNullOrEmpty(url)) return;
            for (int i = _hostRowCovers.Count - 1; i >= 0; i--)
                if (_hostRowCovers[i].Value == null) _hostRowCovers.RemoveAt(i);
            _hostRowCovers.Add(new KeyValuePair<string, RawImage>(url, img));
            if (_hostCoverHooked || Registry == null) return;
            _hostCoverHooked = true;
            Registry.OnCoverLoaded += OnHostRowCoverLoaded;
        }

        static float RepoCoverW => UIScale.TAB_W - PAD * 2f - 4f;
        static float RepoCoverH => RepoCoverW * (38f / 532f);
        internal static float RepoRowH => RepoCoverH;

        void Awake()
        {
            if (Registry == null) return;
            Registry.OnCoverLoaded += OnCoverLoaded;
            Registry.OnReposChanged += OnReposChanged;
            Registry.OnValidationStatus += OnValidationStatus;
        }

        void OnDestroy()
        {
            if (Registry == null) return;
            Registry.OnCoverLoaded -= OnCoverLoaded;
            Registry.OnReposChanged -= OnReposChanged;
            Registry.OnValidationStatus -= OnValidationStatus;
            Registry.CancelSearch();
        }

        static Texture2D LoadTex(string resource, ref Texture2D cache)
        {
            if (cache != null) return cache;
            cache = EmbeddedResourceandUnity.LoadTexture(resource);
            return cache;
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            _contentH = contentRoot.rect.height > 1f ? contentRoot.rect.height : TabHeight - UIScale.TITLE_H;
            _pageList = Page(contentRoot, "PageList");
            _pageSearch = Page(contentRoot, "PageSearch");
            _pageCard = Page(contentRoot, "PageCard");
            BuildList();
            BuildSearch();
            ShowPage(_pageList);
        }

        protected override void OnTitleClicked()
        {
            if (IsOpen) CloseBackToHost(false);
            else base.OnTitleClicked();
        }

        void CloseBackToHost(bool selectionChanged)
        {
            var host = Host;
            BetterFGUIMan.Instance?.PopSlotTab(this);
            if (selectionChanged) host?.OnRepoChanged();
        }

        void ShowPage(RectTransform page)
        {
            var current = _pageList.gameObject.activeSelf ? _pageList : _pageSearch.gameObject.activeSelf ? _pageSearch : _pageCard;
            Transition(current.gameObject, page.gameObject, () =>
            {
                _pageList.gameObject.SetActive(page == _pageList);
                _pageSearch.gameObject.SetActive(page == _pageSearch);
                _pageCard.gameObject.SetActive(page == _pageCard);
            });
        }

        void OnCoverLoaded(SkinRepo repo, Texture2D tex)
        {
            if (repo == null || tex == null || !_coverImages.TryGetValue(repo.githubUrl, out var imgs)) return;
            foreach (var img in imgs)
                if (img != null) SetCover(img, tex);
        }

        void OnReposChanged()
        {
            if (_pageList == null) return;
            BuildList();
            FillResults();
            if (_cardResult != null && _pageCard.gameObject.activeSelf) BuildCard();
        }

        void OnValidationStatus(string status)
        {
            if (_listStatus != null) _listStatus.text = status;
        }

        static RectTransform Node(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<RectTransform>();
        }

        static void Stretch(RectTransform rt, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        static RectTransform Page(RectTransform parent, string name)
        {
            var page = Node(parent, name);
            Stretch(page);
            return page;
        }

        static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var child = t.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }

        static string Clip(string s, int max)
        {
            s = (s ?? "").Replace("\r", "").Replace("\n", " ");
            return s.Length > max ? s.Substring(0, max - 1).TrimEnd() + "..." : s;
        }

        static void Chips(RectTransform row, int fs, params (string icon, string text)[] chips)
        {
            float h = row.rect.height > 1f ? row.rect.height : row.sizeDelta.y;
            float icon = h * 0.8f;
            Row(row.gameObject, 12f);
            foreach (var (name, text) in chips)
            {
                var chip = Node(row, "Chip");
                Row(chip.gameObject, 3f);
                var ic = Node(chip, "Icon");
                var le = ic.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = le.minWidth = icon;
                le.preferredHeight = icon;
                var img = ic.gameObject.AddComponent<Image>();
                img.sprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.button.icon_" + name + ".png");
                img.color = HINT;
                img.raycastTarget = false;
                if (string.IsNullOrEmpty(text)) continue;
                var lbl = UGUIShip.CreateLabel(chip, new Rect(0f, 0f, 10f, h), text, fs, HINT, TextAnchor.MiddleLeft);
                lbl.horizontalOverflow = HorizontalWrapMode.Overflow;
                lbl.raycastTarget = false;
                foreach (var m in lbl.GetComponentsInChildren<RectTransform>(true))
                    if (m != lbl.rectTransform) Stretch(m);
                foreach (var tmp in lbl.GetComponentsInChildren<TMPro.TMP_Text>(true))
                {
                    tmp.enableWordWrapping = false;
                    tmp.overflowMode = TMPro.TextOverflowModes.Overflow;
                }
                var lle = lbl.gameObject.AddComponent<LayoutElement>();
                lle.minWidth = Mathf.Max(UGUIShip.PreferredWidth(lbl), text.Length * fs * 0.62f);
            }
        }

        static void Row(GameObject go, float spacing)
        {
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = spacing;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
        }

        static RectTransform ChipRow(Transform parent, Rect r)
        {
            var row = Node(parent, "Chips");
            UGUIShip.SetPixelRect(row, r);
            return row;
        }

        static Text StretchLabel(Transform parent, string text, int fs, Color color, float left, float bottom, float right, float top, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var t = UGUIShip.CreateStretchLabel(parent, text, fs, color);
            t.alignment = anchor;
            t.raycastTarget = false;
            Stretch(t.rectTransform, left, bottom, right, top);
            return t;
        }

        static void IconOn(Transform parent, string icon, float size, float x, Color color, bool right = false)
        {
            var rt = Node(parent, "Icon");
            float ax = right ? 1f : x < 0f ? 0.5f : 0f;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(ax, 0.5f);
            rt.anchoredPosition = new Vector2(x < 0f ? 0f : right ? -x : x, 0f);
            rt.sizeDelta = new Vector2(size, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.button.icon_" + icon + ".png");
            img.color = color;
            img.raycastTarget = false;
        }

        static Button IconButton(Transform parent, Rect r, string icon, Action onClick, Color bg)
        {
            var b = UGUIShip.CreateButton(parent, r, "", bg, WHITE, FS_SM, onClick);
            IconOn(b.transform, icon, r.height * 0.6f, -1f, new Color(1f, 1f, 1f, 0.92f));
            return b;
        }

        static void ShiftLabel(Button b, float dx)
        {
            foreach (var t in b.GetComponentsInChildren<Text>(true))
            {
                var rt = t.rectTransform;
                rt.offsetMin = new Vector2(rt.offsetMin.x + dx, rt.offsetMin.y);
            }
        }

        RawImage Banner(Transform parent, SkinRepo repo, Texture2D fixedTex, float scrim, RectTransform hoverArea)
        {
            var (_, inner) = UGUIShip.CreateFramedImage(parent, null, "Banner", 2f);
            var raw = inner.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            var fit = inner.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = 14f;
            if (hoverArea != null) UGUIShip.AddLeatherSpot(raw, hoverArea, SPOT_RADIUS, SPOT_FEATHER, SPOT_GAIN);

            var tex = fixedTex ?? (repo != null ? Registry.GetCover(repo) : null);
            if (tex != null) SetCover(raw, tex);
            else
            {
                raw.color = new Color(1f, 1f, 1f, 0f);
                if (repo != null)
                {
                    if (!_coverImages.TryGetValue(repo.githubUrl, out var list))
                        _coverImages[repo.githubUrl] = list = new List<RawImage>();
                    list.Add(raw);
                    Registry.FetchCover(repo);
                }
            }

            if (scrim > 0f)
            {
                var s = Node(inner.parent, "Scrim");
                Stretch(s);
                var img = s.gameObject.AddComponent<Image>();
                img.color = Color.white;
                img.raycastTarget = false;
                var g = s.gameObject.AddComponent<GradientImage>();
                g.Left = new Color(0f, 0f, 0f, scrim);
                g.LeftMid = new Color(0f, 0f, 0f, scrim * 0.85f);
                g.RightMid = new Color(0f, 0f, 0f, scrim * 0.2f);
                g.Right = new Color(0f, 0f, 0f, 0f);
                g.LeftMidT = 0.45f;
                g.RightMidT = 0.8f;
            }
            return raw;
        }

        RectTransform RepoRow(Transform parent, float h, string title, string sub, Color titleColor,
            SkinRepo repo, Texture2D fixedTex, string rightIcon, Action rightClick, Action onClick,
            (string icon, string text)[] chips = null)
        {
            var row = Node(parent, "RepoRow");
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = h;
            le.minHeight = h;

            var btn = UGUIShip.CreateButton(row, new Rect(0f, 0f, 10f, 10f), "", BTN_ROW, WHITE, FS_SM, onClick);
            var btnRt = btn.GetComponent<RectTransform>();
            Stretch(btnRt);

            var bannerHost = Node(btn.transform, "BannerHost");
            Stretch(bannerHost, 2f, 2f, 2f, 2f);
            Banner(bannerHost, repo, fixedTex, 0.92f, btnRt);

            float right = rightIcon != null ? h : 8f;
            StretchLabel(btn.transform, title, FS_SM + 1, titleColor, 12f, h * 0.42f, right, 2f).fontStyle = FontStyle.Bold;
            if (chips != null)
            {
                var chipRow = Node(btn.transform, "Chips");
                chipRow.anchorMin = Vector2.zero;
                chipRow.anchorMax = new Vector2(1f, 0f);
                chipRow.pivot = Vector2.zero;
                chipRow.offsetMin = new Vector2(12f, 5f);
                chipRow.offsetMax = new Vector2(-right, 5f + h * 0.3f);
                Chips(chipRow, FS_SM - 2, chips);
            }
            else
            {
                var subLabel = StretchLabel(btn.transform, sub, FS_SM - 2, HINT, 12f, 3f, right, h * 0.55f);
                subLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
                subLabel.verticalOverflow = VerticalWrapMode.Truncate;
            }

            if (rightIcon == null) return row;
            if (rightClick != null)
            {
                var rb = IconButton(btn.transform, new Rect(0f, 0f, h - 10f, h - 10f), rightIcon, rightClick, BTN_ROW);
                var rrt = rb.GetComponent<RectTransform>();
                rrt.anchorMin = rrt.anchorMax = rrt.pivot = new Vector2(1f, 0.5f);
                rrt.anchoredPosition = new Vector2(-5f, 0f);
            }
            else IconOn(btn.transform, rightIcon, h * 0.45f, 10f, GOLD, true);
            return row;
        }

        static RectTransform VList(Transform parent, Rect r)
        {
            var sv = UGUIShip.CreateScrollView(parent, r);
            sv.scrollRect.scrollSensitivity = 24f;
            var vlg = sv.content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 4f;
            vlg.padding = new RectOffset(0, 10, 0, 4);
            sv.content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return sv.content;
        }

        static void Section(Transform parent, string text)
        {
            var s = Node(parent, "Section");
            s.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
            StretchLabel(s, text, FS_SM - 2, HINT, 4f, 0f, 0f, 2f, TextAnchor.LowerLeft).fontStyle = FontStyle.Bold;
        }

        void SelectRepo(SkinRepo repo)
        {
            Host.ImportedSelected = false;
            Host.FeaturedSelected = false;
            Host.SelectedRepo = repo;
            Registry.SetActive(repo);
            CloseBackToHost(true);
        }

        void BuildList()
        {
            Clear(_pageList);
            _coverImages.Clear();
            var registry = Registry;
            if (registry == null) return;

            IconButton(_pageList, new Rect(PAD, PAD, HEADER_H, HEADER_H), "back", () => CloseBackToHost(false), BTN_ROW);
            var filter = UGUIShip.CreateInputField(_pageList, new Rect(PAD + HEADER_H + 4f, PAD, W - HEADER_H - 4f - 118f, HEADER_H),
                "ui.search_repositories", new Color(0f, 0f, 0f, 0.35f), WHITE, FS_SM);
            UGUIShip.SetInputText(filter, _listQuery, false);
            filter.onValueChanged.AddListener(new Action<string>(v => { _listQuery = v ?? ""; ApplySearchFilter(_listQuery); }));
            var discover = UGUIShip.CreateButton(_pageList, new Rect(PAD + W - 114f, PAD, 114f, HEADER_H), "repos.discover",
                BTN_ACCENT, WHITE, FS_SM, new Action(OpenDiscover));
            IconOn(discover.transform, "explore", HEADER_H * 0.55f, 10f, BLUE);
            ShiftLabel(discover, 14f);

            float top = PAD + HEADER_H + 6f;
            _listContent = VList(_pageList, new Rect(PAD, top, W + 8f, H - top - HEADER_H - 30f));
            ClearSearchRows();

            Section(_listContent, LocalizationService.Get("repos.sources"));
            foreach (var repo in registry.Repos)
            {
                if (!repo.isDefault) continue;
                var def = repo;
                var defRow = RepoRow(_listContent, ROW_H, def.DisplayName, LocalizationService.Get("repos.default_sub"), WHITE,
                    def, null, "star", null, () => SelectRepo(def));
                RegisterSearchRow(defRow.gameObject, def.DisplayName);
            }
            RepoRow(_listContent, ROW_H, LocalizationService.Get("ui.imported_skins"), LocalizationService.Get("repos.imported_sub"), ORANGE,
                null, LoadTex("BetterFG.assets.ui.reposelector.imported.png", ref _importedTex), null, null, () =>
                {
                    Host.ImportedSelected = true;
                    Host.FeaturedSelected = false;
                    CloseBackToHost(true);
                });
            RepoRow(_listContent, ROW_H, LocalizationService.Get("ui.featured_repositories"), LocalizationService.Get("repos.featured_sub"), GOLD,
                null, LoadTex("BetterFG.assets.ui.reposelector.featured.png", ref _featuredTex), null, null, () =>
                {
                    Host.FeaturedSelected = true;
                    Host.ImportedSelected = false;
                    Registry?.FetchFeatured();
                    CloseBackToHost(true);
                });

            int added = 0;
            foreach (var repo in registry.Repos)
                if (!repo.isDefault) added++;
            Section(_listContent, LocalizationService.Format("repos.added_fmt", added));
            foreach (var repo in registry.Repos)
            {
                if (repo.isDefault) continue;
                var r = repo;
                string applied = RemoteProfileStore.LocalAppliedSummary(r);
                string sub = string.IsNullOrEmpty(applied) ? LocalizationService.Get("repos.added_sub") : LocalizationService.Format("repos.applied_fmt", applied);
                var row = RepoRow(_listContent, ROW_H, r.DisplayName, Clip(sub, 56), WHITE, r, null, "delete",
                    () => Registry.RemoveRepo(r), () => SelectRepo(r));
                RegisterSearchRow(row.gameObject, r.DisplayName);
            }
            if (added == 0)
            {
                var empty = Node(_listContent, "Empty");
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
                StretchLabel(empty, "repos.none_added", FS_SM - 1, HINT, 6f, 0f, 0f, 0f);
            }

            _listStatus = UGUIShip.CreateLabel(_pageList, new Rect(PAD + 4f, H - HEADER_H - 24f, W, 18f), "", FS_SM - 2, HINT, TextAnchor.MiddleLeft);
            _listStatus.raycastTarget = false;
            _addBar = Node(_pageList, "AddBar");
            UGUIShip.SetPixelRect(_addBar, new Rect(PAD, H - HEADER_H - 4f, W, HEADER_H));
            BuildAddButton();
            ApplySearchFilter(_listQuery);
        }

        void BuildAddButton()
        {
            Clear(_addBar);
            var add = UGUIShip.CreateButton(_addBar, new Rect(0f, 0f, W, HEADER_H), "repos.add_by_url", BTN_ROW, BLUE, FS_SM, new Action(OpenUrlField));
            IconOn(add.transform, "add", 18f, 10f, BLUE);
        }

        void OpenUrlField()
        {
            Clear(_addBar);
            var field = UGUIShip.CreateInputField(_addBar, new Rect(0f, 0f, W - 2f * (HEADER_H + 4f), HEADER_H),
                "ui.https_github_com_author_repo", new Color(0f, 0f, 0f, 0.35f), BLUE, FS_SM);
            IconButton(_addBar, new Rect(W - 2f * HEADER_H - 4f, 0f, HEADER_H, HEADER_H), "check", () =>
            {
                string url = (field.text ?? "").Trim();
                BuildAddButton();
                if (url.Length > 0) Registry?.AddRepo(url);
            }, BTN_CONFIRM);
            IconButton(_addBar, new Rect(W - HEADER_H, 0f, HEADER_H, HEADER_H), "back", BuildAddButton, BTN_ROW);
            field.ActivateInputField();
        }

        void OpenDiscover()
        {
            ShowPage(_pageSearch);
            if (!_searched) RunSearch("");
        }

        void BuildSearch()
        {
            Clear(_pageSearch);
            IconButton(_pageSearch, new Rect(PAD, PAD, HEADER_H, HEADER_H), "back", () => ShowPage(_pageList), BTN_ROW);
            var field = UGUIShip.CreateInputField(_pageSearch, new Rect(PAD + HEADER_H + 4f, PAD, W - 2f * (HEADER_H + 4f), HEADER_H),
                "repos.search_github", new Color(0f, 0f, 0f, 0.35f), WHITE, FS_SM);
            field.onValueChanged.AddListener(new Action<string>(v => _searchQuery = v ?? ""));
            field.onEndEdit.AddListener(new Action<string>(_ =>
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) RunSearch(_searchQuery);
            }));
            IconButton(_pageSearch, new Rect(PAD + W - HEADER_H, PAD, HEADER_H, HEADER_H), "search", () => RunSearch(_searchQuery), BTN_ACCENT);

            _searchStatus = UGUIShip.CreateLabel(_pageSearch, new Rect(PAD + 4f, PAD + HEADER_H + 4f, W, 18f), "", FS_SM - 2, HINT, TextAnchor.MiddleLeft);
            _searchStatus.raycastTarget = false;
            float top = PAD + HEADER_H + 26f;
            _searchContent = VList(_pageSearch, new Rect(PAD, top, W + 8f, H - top - 6f));
        }

        void RunSearch(string query)
        {
            _searched = true;
            _results.Clear();
            FillResults();
            _searchStatus.text = LocalizationService.Get("repos.searching");
            string q = (query ?? "").Trim();
            Registry.SearchRepos(q,
                r =>
                {
                    _results.Add(r);
                    _results.Sort((a, b) => b.skinCount.CompareTo(a.skinCount));
                    FillResults();
                },
                (i, n) => _searchStatus.text = LocalizationService.Format("repos.checking_fmt", i, n),
                err =>
                {
                    if (!string.IsNullOrEmpty(err)) _searchStatus.text = LocalizationService.Format("repos.search_failed_fmt", err);
                    else if (_results.Count == 0) _searchStatus.text = LocalizationService.Get("repos.found_none");
                    else if (_results.Count == 1) _searchStatus.text = LocalizationService.Get("repos.found_one");
                    else _searchStatus.text = LocalizationService.Format("repos.found_fmt", _results.Count);
                });
        }

        void FillResults()
        {
            if (_searchContent == null) return;
            Clear(_searchContent);
            foreach (var result in _results)
            {
                var r = result;
                var chips = Registry.HasRepo(r.repo.githubUrl)
                    ? new[] { ("check", ""), ("skins", r.skinCount.ToString()), ("star", r.stars.ToString()) }
                    : new[] { ("skins", r.skinCount.ToString()), ("star", r.stars.ToString()) };
                RepoRow(_searchContent, RESULT_H, r.repo.DisplayName, null, WHITE, r.repo, null, "chevron", null, () => OpenCard(r), chips);
            }
        }

        void OpenCard(RepoSearchResult r)
        {
            _cardResult = r;
            BuildCard();
            ShowPage(_pageCard);
        }

        void BuildCard()
        {
            Clear(_pageCard);
            var r = _cardResult;
            if (r == null) return;

            IconButton(_pageCard, new Rect(PAD, PAD, HEADER_H, HEADER_H), "back", () => ShowPage(_pageSearch), BTN_ROW);

            float y = PAD + HEADER_H + 7f;
            var bannerHost = Node(_pageCard, "Banner");
            UGUIShip.SetPixelRect(bannerHost, new Rect(PAD, y, W, 86f));
            Banner(bannerHost, r.repo, null, 0f, null);
            y += 92f;

            const float ghW = 118f;
            UGUIShip.CreateLabel(_pageCard, new Rect(PAD + 2f, y, W - ghW - 8f, 22f), r.repo.repoName, FS_SM + 5, WHITE, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            string url = r.repo.githubUrl;
            var github = UGUIShip.CreateButton(_pageCard, new Rect(PAD + W - ghW, y + 1f, ghW, 20f), "repos.view_github",
                BTN_ROW, new Color(1f, 1f, 1f, 0.85f), FS_SM - 2, new Action(() => Application.OpenURL(url)));
            foreach (var t in github.GetComponentsInChildren<Text>(true))
                Stretch(t.rectTransform, 20f, 0f, 4f, 0f);
            IconOn(github.transform, "github", 13f, 7f, new Color(1f, 1f, 1f, 0.9f));
            y += 22f;
            var meta = new List<(string, string)> { ("person", r.repo.author), ("skins", r.skinCount.ToString()), ("star", r.stars.ToString()) };
            if (!string.IsNullOrEmpty(r.pushedAt) && r.pushedAt.Length >= 10) meta.Add(("updated", r.pushedAt.Substring(0, 10)));
            Chips(ChipRow(_pageCard, new Rect(PAD + 2f, y + 2f, W, 15f)), FS_SM - 1, meta.ToArray());
            y += 20f;
            var desc = UGUIShip.CreateLabel(_pageCard, new Rect(PAD + 2f, y, W - 4f, 34f),
                string.IsNullOrEmpty(r.description) ? "repos.no_description" : Clip(r.description, 110),
                FS_SM - 1, new Color(1f, 1f, 1f, 0.8f), TextAnchor.UpperLeft);
            desc.horizontalOverflow = HorizontalWrapMode.Wrap;
            desc.verticalOverflow = VerticalWrapMode.Truncate;
            y += 38f;

            var skinsLabel = UGUIShip.CreateLabel(_pageCard, new Rect(PAD + 2f, y, W * 0.6f, 18f), "", FS_SM - 2, HINT, TextAnchor.MiddleLeft);
            skinsLabel.fontStyle = FontStyle.Bold;
            var moreLabel = UGUIShip.CreateLabel(_pageCard, new Rect(PAD + W * 0.6f, y, W * 0.4f - 2f, 18f), "", FS_SM - 2, HINT, TextAnchor.MiddleRight);
            y += 20f;

            var grid = Node(_pageCard, "SkinGrid");
            float gridH = H - y - HEADER_H - 12f;
            UGUIShip.SetPixelRect(grid, new Rect(PAD, y, W, gridH));
            var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            float cw = (W - 4f * 6f) / 5f;
            gl.cellSize = new Vector2(cw, cw * 0.78f + 14f);
            gl.spacing = new Vector2(6f, 6f);
            int fit = 5 * Mathf.Max(1, Mathf.FloorToInt((gridH + 6f) / (gl.cellSize.y + 6f)));

            bool added = Registry.HasRepo(r.repo.githubUrl);
            var main = UGUIShip.CreateButton(_pageCard, new Rect(PAD, H - HEADER_H - 4f, W, HEADER_H),
                added ? "repos.open" : "repos.add", added ? BTN_ROW : BTN_PRIMARY, added ? BLUE : WHITE, FS_SM, new Action(() =>
                {
                    var existing = Registry.FindRepo(r.repo.githubUrl);
                    if (existing != null) SelectRepo(existing);
                    else Registry.AddRepo(r.repo.githubUrl);
                }));
            IconOn(main.transform, added ? "chevron" : "add", 18f, 10f, added ? BLUE : WHITE);

            var catalog = CustomizationServices.CatalogService;
            catalog?.FetchPreview(r.repo, entries =>
            {
                if (grid == null || _cardResult != r) return;
                if (r.ownSkins != null) entries = r.ownSkins;
                skinsLabel.text = LocalizationService.Format("repos.skins_fmt", entries.Count);
                int show = Mathf.Min(entries.Count, fit);
                if (entries.Count > show) moreLabel.text = LocalizationService.Format("repos.more_fmt", entries.Count - show);
                for (int i = 0; i < show; i++)
                {
                    var cell = Node(grid, "Skin");
                    var pic = Node(cell, "Pic");
                    Stretch(pic, 0f, 14f, 0f, 0f);
                    var (_, inner) = UGUIShip.CreateFramedImage(pic, null, "Frame", 2f);
                    var raw = inner.gameObject.AddComponent<RawImage>();
                    raw.color = new Color(1f, 1f, 1f, 0f);
                    raw.raycastTarget = false;
                    var fitter = inner.gameObject.AddComponent<AspectRatioFitter>();
                    fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                    fitter.aspectRatio = 1.3f;

                    var name = UGUIShip.CreateLabel(cell, new Rect(0f, 0f, 10f, 10f), Clip(entries[i].Value, 13), FS_SM - 3, HINT, TextAnchor.LowerCenter);
                    name.verticalOverflow = VerticalWrapMode.Truncate;
                    name.raycastTarget = false;
                    var nrt = name.rectTransform;
                    nrt.anchorMin = Vector2.zero;
                    nrt.anchorMax = new Vector2(1f, 0f);
                    nrt.pivot = new Vector2(0.5f, 0f);
                    nrt.anchoredPosition = Vector2.zero;
                    nrt.sizeDelta = new Vector2(0f, 13f);

                    catalog.FetchPreviewCover(r.repo, entries[i].Key, tex =>
                    {
                        if (tex == null || raw == null) return;
                        raw.texture = tex;
                        raw.color = Color.white;
                        fitter.aspectRatio = (float)tex.width / tex.height;
                    });
                }
            });
        }

        static void BuildRepoButtonContents(Button btn, SkinRepo repo)
        {
            var maskGo = new GameObject("RepoMask");
            maskGo.transform.SetParent(btn.transform, false);
            maskGo.transform.SetAsFirstSibling();
            var maskRt = maskGo.AddComponent<RectTransform>();
            maskRt.anchorMin = Vector2.zero;
            maskRt.anchorMax = Vector2.one;
            maskRt.offsetMin = new Vector2(1f, 1f);
            maskRt.offsetMax = new Vector2(-1f, -1f);
            var maskImg = maskGo.AddComponent<Image>();
            UGUIShip.ApplyDeluxColorFill(maskImg);
            maskImg.raycastTarget = false;
            maskGo.AddComponent<Mask>().showMaskGraphic = false;

            var coverGo = new GameObject("RepoCover");
            coverGo.transform.SetParent(maskGo.transform, false);
            var coverRt = coverGo.AddComponent<RectTransform>();
            coverRt.anchorMin = Vector2.zero;
            coverRt.anchorMax = Vector2.one;
            coverRt.offsetMin = coverRt.offsetMax = Vector2.zero;
            var coverImg = coverGo.AddComponent<RawImage>();
            coverImg.raycastTarget = false;
            coverGo.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            coverImg.color = new Color(1f, 1f, 1f, 0f);
            var cachedCoverTex = Registry.GetCover(repo);
            if (cachedCoverTex != null) SetCover(coverImg, cachedCoverTex);
            else Registry.FetchCover(repo);

            var lbl = btn.transform.Find("Label")?.GetComponent<Text>();
            if (lbl == null) return;
            lbl.transform.SetAsLastSibling();
            lbl.alignment = TextAnchor.MiddleLeft;
            var lblRt = lbl.GetComponent<RectTransform>();
            lblRt.anchorMin = Vector2.zero;
            lblRt.anchorMax = Vector2.one;
            lblRt.pivot = new Vector2(0.5f, 0.5f);
            lblRt.offsetMin = new Vector2(14f, 0f);
            lblRt.offsetMax = Vector2.zero;
        }

        public static float BuildCurrentRepoRow(UGCTab host, RectTransform parent, float y, float w)
        {
            var activeRepo = host.SelectedRepo;
            bool tall = !host.ImportedSelected && !host.FeaturedSelected && activeRepo != null;
            float rowH = RepoRowH;

            var existing = parent.Find("RepoActiveRow");
            if (existing != null) Destroy(existing.gameObject);

            var rowGo = new GameObject("RepoActiveRow");
            rowGo.transform.SetParent(parent, false);
            var rowRt = rowGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(rowRt, new Rect(PAD, y, w, rowH));
            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.spacing = 3f;

            Action open = () =>
            {
                var tab = BetterFGTabRegistry.NewTab<RepoSelectorTab>();
                tab.Host = host;
                BetterFGUIMan.Instance?.PushSlotTab(host, tab);
            };

            string label = host.FeaturedSelected ? LocalizationService.Get("ui.featured_repositories_selected")
                : host.ImportedSelected ? LocalizationService.Get("ui.imported_skins_selected")
                : activeRepo != null ? LocalizationService.Format("ui.repo_name_selected_fmt", activeRepo.DisplayName) : LocalizationService.Get("ui.no_repository");
            Color labelColor = host.FeaturedSelected ? GOLD
                : host.ImportedSelected ? ORANGE
                : activeRepo != null ? YELLOW : HINT;

            var nameBtn = UGUIShip.CreateButton(rowGo.transform, label, BTN_DARK, labelColor, FS_SM, open);
            var nameLE = nameBtn.gameObject.GetComponent<LayoutElement>() ?? nameBtn.gameObject.AddComponent<LayoutElement>();
            nameLE.flexibleWidth = 1f;

            var lbl = nameBtn.transform.Find("Label")?.GetComponent<Text>();
            if (tall)
            {
                BuildRepoButtonContents(nameBtn, activeRepo);
                TrackHostRowCover(activeRepo.githubUrl, nameBtn.transform.Find("RepoMask/RepoCover")?.GetComponent<RawImage>());
                if (lbl != null) lbl.color = labelColor;
            }
            else if (lbl != null)
            {
                lbl.alignment = TextAnchor.MiddleLeft;
                lbl.color = labelColor;
                var lblRt = lbl.GetComponent<RectTransform>();
                lblRt.offsetMin = new Vector2(14f, lblRt.offsetMin.y);
            }

            return y + rowH + SH;
        }
    }
}
