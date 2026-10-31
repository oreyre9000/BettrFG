using System;
using System.Collections.Generic;
using BetterFG.Customization.UI;
using BetterFG.Customization.Player;
using BetterFG.Services;
using BetterFG.Utilities;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class PlinthsTab : UGCTab
    {
        public PlinthsTab(IntPtr ptr) : base(ptr) { }

        protected static float ROW_H => UIScale.ROW_H;
        protected static float COVER_W => UIScale.COVER_W;
        protected static float COVER_H => UIScale.COVER_H;
        protected static float SEL_W => UIScale.SEL_W;

        protected static readonly Color WHITE = UGUIShip.WHITE;
        protected static readonly Color HINT = new Color(1f, 1f, 1f, 0.45f);
        protected static readonly Color BTN_DARK = new Color(0.18f, 0.18f, 0.18f, 1f);
        protected static readonly Color BTN_REMOVE = UGUIShip.BTN_REMOVE;
        protected static readonly Color COVER_BG = new Color(0.04f, 0.04f, 0.04f, 1f);
        protected static readonly Color ITEM_BG = new Color(0f, 0f, 0f, 0f);
        protected static readonly Color ORANGE = new Color(1f, 0.55f, 0.1f, 1f);

        RectTransform _scrollRt;
        Text _statusLbl;

        // fake-input search — same pattern as CustomizationTab: a clear Button + two labels, keystrokes
        // pumped in Update(). subclasses filter their rows through RowMatchesQuery.
        Text _searchText, _searchPlaceholder;
        RectTransform _searchFieldRt;
        bool _searchActive, _fakeInputLocked;
        protected string SearchQuery { get; private set; } = "";

        protected override string BgResource => "BetterFG.assets.ui.tab.plinths.png";

        protected RectTransform ContentRoot;
        protected RectTransform ListContent;
        protected float ListW;

        protected static MenuCustomizationApplication PlinthApp => CustomizationServices.PlinthApp;
        protected static SkinCatalogService Catalog => CustomizationServices.CatalogService;

        void Awake()
        {
            if (Catalog != null)
            {
                Catalog.OnSkinCoverLoaded += OnCoverLoaded;
                Catalog.OnSkinsLoaded += OnSkinsLoaded;
                Catalog.OnFetchCompleted += OnCatalogChanged;
            }
            if (PlinthApp != null) PlinthApp.OnStatus += OnPlinthStatus;
        }

        void OnDestroy()
        {
            SetFakeInputLock(false);
            if (Catalog != null)
            {
                Catalog.OnSkinCoverLoaded -= OnCoverLoaded;
                Catalog.OnSkinsLoaded -= OnSkinsLoaded;
                Catalog.OnFetchCompleted -= OnCatalogChanged;
            }
            if (PlinthApp != null) PlinthApp.OnStatus -= OnPlinthStatus;
        }

        void OnPlinthStatus(string msg)
        {
            Rebuild();
            SetStatus(msg);
        }

        void OnSkinsLoaded(List<SkinInfo> _) => OnCatalogChanged();

        protected virtual void OnCoverLoaded(string key, Texture2D tex) { }
        protected virtual void OnCatalogChanged() { }

        protected override void BuildContent(RectTransform contentRoot)
        {
            ContentRoot = contentRoot;
            ListW = TabWidth - PAD * 2f;

            var (scroll, content) = UGUIShip.CreateScrollView(contentRoot, new Rect(PAD, PAD, ListW, BTN_H));
            _scrollRt = scroll.GetComponent<RectTransform>();
            ListContent = content;
            var vlg = ListContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = PAD * 0.5f;
            vlg.padding = new RectOffset(0, (int)PAD, (int)(PAD * 0.5f), (int)(PAD * 0.5f));
            ListContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            float statusH = UIScale.LH;
            float statusY = TabHeight - PAD - statusH;
            float removeW = 64f;
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + ListW - removeW, statusY, removeW, statusH), "ui.remove_2",
                BTN_REMOVE, WHITE, FS_SM, new Action(OnRemove));
            _statusLbl = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, statusY, ListW - removeW - PAD, statusH),
                "", FS_SM, HINT, TextAnchor.MiddleLeft);

            BuildSearchField(contentRoot);

            PositionSwitchLink();
            LayoutList();
            Rebuild();
        }

        void LayoutList()
        {
            float y = HeaderY() + LH + SH; // leave a row for the search field
            float statusY = TabHeight - PAD - UIScale.LH;
            UGUIShip.SetPixelRect(_searchFieldRt, new Rect(PAD, HeaderY(), ListW, LH));
            UGUIShip.SetPixelRect(_scrollRt, new Rect(PAD, y, ListW, statusY - SH - y));
        }

        void BuildSearchField(RectTransform parent)
        {
            var go = new GameObject("SearchField");
            go.transform.SetParent(parent, false);
            _searchFieldRt = go.AddComponent<RectTransform>();

            float iconSize = FS_SM * 0.75f;
            float textLeft = 2f + iconSize + 4f;
            var iconSprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.button.search.png");
            if (iconSprite != null)
            {
                var iconGo = new GameObject("SearchIcon");
                iconGo.transform.SetParent(go.transform, false);
                var iRt = iconGo.AddComponent<RectTransform>();
                iRt.anchorMin = iRt.anchorMax = new Vector2(0f, 0.5f);
                iRt.pivot = new Vector2(0f, 0.5f);
                iRt.anchoredPosition = new Vector2(2f, 0f);
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

        void Update()
        {
            SetFakeInputLock(_searchActive);
            if (!_searchActive) return;

            if (Input.GetMouseButtonDown(0))
            {
                var m = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
                if (_searchFieldRt != null && !RectTransformUtility.RectangleContainsScreenPoint(_searchFieldRt, m, null))
                { _searchActive = false; UpdateSearchCaret(); }
                return;
            }

            foreach (char c in Input.inputString)
            {
                if (c == '\b')
                { if (SearchQuery.Length > 0) { SearchQuery = SearchQuery.Substring(0, SearchQuery.Length - 1); FilterRows(); } }
                else if (c == '\n' || c == '\r' || c == '\x1b') { _searchActive = false; }
                else { SearchQuery += c; FilterRows(); }
                UpdateSearchCaret();
            }
        }

        void UpdateSearchCaret()
        {
            if (_searchText == null) return;
            bool empty = string.IsNullOrEmpty(SearchQuery);
            _searchText.text = empty && !_searchActive ? "" : SearchQuery + (_searchActive ? "|" : "");
            if (_searchPlaceholder != null)
                _searchPlaceholder.color = empty && !_searchActive ? new Color(1f, 1f, 1f, 0.2f) : new Color(1f, 1f, 1f, 0f);
        }

        void SetFakeInputLock(bool active)
        {
            if (_fakeInputLocked == active) return;
            _fakeInputLocked = active;
            BetterFG.Services.FGInputLockService.SetFakeFieldLock(active);
        }

        protected virtual float HeaderY() => PAD;

        public override void OnRepoChanged()
        {
            LayoutList();
            Rebuild();
        }

        protected void SetStatus(string msg)
        {
            if (_statusLbl != null) UGUIShip.RelabelText(_statusLbl, msg);
        }

        void OnRemove()
        {
            PlinthApp?.RemovePlinth();
            Rebuild();
            SetStatus("ui.plinth_removed");
        }

        protected void Rebuild()
        {
            if (ListContent == null) return;

            for (int i = ListContent.childCount - 1; i >= 0; i--)
                Destroy(ListContent.GetChild(i).gameObject);

            ClearSearchRows();
            BuildRows();
            FilterRows();
        }

        // keystroke path: no teardown, just show/hide the rows built by the last Rebuild
        protected void FilterRows()
        {
            int shown = ApplySearchFilter(SearchQuery);
            if (!string.IsNullOrEmpty(SearchQuery) && shown == 0) SetStatus("ui.no_matches");
        }

        protected virtual void BuildRows() { }

        protected Image BuildRow(string title, string sub, bool isActive, Action onSelect, params string[] searchFields)
        {
            var rowGo = new GameObject("Row");
            rowGo.transform.SetParent(ListContent, false);
            RegisterSearchRow(rowGo, searchFields.Length > 0 ? searchFields : new[] { title });
            var rowRt = rowGo.AddComponent<RectTransform>();
            rowRt.sizeDelta = new Vector2(0f, ROW_H);
            rowGo.AddComponent<Image>().color = ITEM_BG;

            var gradGo = new GameObject("SelGradient");
            gradGo.transform.SetParent(rowGo.transform, false);
            var gradRt = gradGo.AddComponent<RectTransform>();
            gradRt.anchorMin = Vector2.zero;
            gradRt.anchorMax = Vector2.one;
            gradRt.offsetMin = gradRt.offsetMax = Vector2.zero;
            gradGo.AddComponent<Image>().color = Color.white;
            var grad = gradGo.AddComponent<GradientImage>();
            grad.Vertical = true;
            grad.TopColor = new Color(ORANGE.r, ORANGE.g, ORANGE.b, 0f);
            grad.BottomColor = new Color(ORANGE.r, ORANGE.g, ORANGE.b, 0.4f);
            gradGo.SetActive(isActive);

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.spacing = PAD;
            hlg.padding = new RectOffset((int)(PAD * 2f), (int)(PAD * 2f), (int)(PAD * 0.5f), (int)(PAD * 0.5f));

            var infoGo = new GameObject("Info");
            infoGo.transform.SetParent(rowGo.transform, false);
            infoGo.AddComponent<RectTransform>();
            var infoLE = infoGo.AddComponent<LayoutElement>();
            infoLE.preferredWidth = 100f * UIScale.S;
            infoLE.flexibleWidth = 1f;
            var infoVlg = infoGo.AddComponent<VerticalLayoutGroup>();
            infoVlg.childForceExpandHeight = false;
            infoVlg.childForceExpandWidth = true;
            infoVlg.spacing = 0f;
            infoVlg.padding = new RectOffset(0, 0, (int)(PAD * 0.5f), (int)(PAD * 0.5f));

            UGUIShip.CreateFlowLabel(infoGo.transform, title, FS, WHITE);
            UGUIShip.CreateFlowLabel(infoGo.transform, sub, FS_SM, HINT);

            var coverGo = new GameObject("Cover");
            coverGo.transform.SetParent(rowGo.transform, false);
            coverGo.AddComponent<RectTransform>();
            var coverLE = coverGo.AddComponent<LayoutElement>();
            coverLE.preferredWidth = COVER_W;
            coverLE.preferredHeight = COVER_H;
            coverLE.minWidth = COVER_W;
            var coverImg = coverGo.AddComponent<Image>();
            UGUIShip.ApplyDeluxColorFill(coverImg);
            coverImg.color = COVER_BG;

            var selBtn = UGUIShip.CreateButton(rowGo.transform, "ui.select",
                BTN_DARK, isActive ? ORANGE : WHITE, FS_SM, onSelect);
            var selLE = selBtn.gameObject.AddComponent<LayoutElement>();
            selLE.preferredWidth = SEL_W;
            selLE.minWidth = SEL_W;
            selLE.preferredHeight = ROW_H - PAD;

            return coverImg;
        }

        protected static void ApplyCover(Image img, Texture2D tex)
        {
            for (int i = img.transform.childCount - 1; i >= 0; i--)
                Destroy(img.transform.GetChild(i).gameObject);
            img.sprite = BetterFG.Utilities.EmbeddedResourceandUnity.SpriteFor(tex);
            img.preserveAspect = true;
            img.color = WHITE;
        }
    }
}
