using System;
using System.Collections.Generic;
using BetterFG.Network;
using BetterFG.Services;
using BetterFG.Utilities;
using UnityEngine;
using UnityEngine.EventSystems;
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
        protected override float TitleYOffset => 20f;

        public UGCTab Host;


        static readonly Color WHITE = UGUIShip.WHITE;
        static readonly Color HINT = new Color(1f, 1f, 1f, 0.45f);
        static readonly Color GOLD = new Color(1f, 0.8f, 0f, 1f);
        static readonly Color GREEN = new Color(0f, 1f, 0f, 1f);
        static readonly Color CYAN = new Color(0f, 0.8f, 1f, 1f);
        static readonly Color ORANGE = new Color(1f, 0.55f, 0.1f, 1f);
        static readonly Color YELLOW = new Color(1f, 1f, 0f, 1f);
        static readonly Color BTN_DARK = Color.black;
        static readonly Color BTN_REMOVE = UGUIShip.BTN_REMOVE;

        static Texture2D _importedTex, _featuredTex;

        RectTransform _rowsParent;
        RectTransform _scrollContent;
        InputField _searchField;
        string _searchQuery = "";
        readonly Dictionary<string, RawImage> _coverImages = new Dictionary<string, RawImage>();

        static RepoRegistry Registry => RepoRegistry.Instance;

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
                e.Value.texture = tex;
                e.Value.color = Color.white;
            }
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
        static float RepoStripH => BTN_H * 0.75f;
        static float RepoRowH => RepoCoverH;

        void Awake()
        {
            if (Registry != null) Registry.OnCoverLoaded += OnCoverLoaded;
        }

        void OnDestroy()
        {
            if (Registry != null) Registry.OnCoverLoaded -= OnCoverLoaded;
        }

        static Texture2D LoadTex(string resource, ref Texture2D cache)
        {
            if (cache != null) return cache;
            cache = EmbeddedResourceandUnity.LoadTexture(resource);
            return cache;
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;

            var rowsGo = new GameObject("RepoRows");
            rowsGo.transform.SetParent(contentRoot, false);
            _rowsParent = rowsGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(_rowsParent, new Rect(PAD, VPAD, w, 0f));
            var vlg = rowsGo.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 2f;
            rowsGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Refresh();
        }

        void OnCoverLoaded(SkinRepo repo, Texture2D tex)
        {
            if (repo == null || tex == null) return;
            if (_coverImages.TryGetValue(repo.githubUrl, out var img) && img != null)
            { img.texture = tex; img.color = Color.white; }
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

        void Refresh()
        {
            if (_rowsParent == null) return;
            for (int i = _rowsParent.childCount - 1; i >= 0; i--)
                Destroy(_rowsParent.GetChild(i).gameObject);
            _coverImages.Clear();
            _scrollContent = null;

            var registry = Registry;
            if (registry == null) return;

            var searchGo = new GameObject("RepoSearchRow");
            searchGo.transform.SetParent(_rowsParent, false);
            searchGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, BTN_H);
            searchGo.AddComponent<LayoutElement>().preferredHeight = BTN_H;
            var sHlg = searchGo.AddComponent<HorizontalLayoutGroup>();
            sHlg.childForceExpandHeight = true;
            sHlg.childForceExpandWidth = false;
            sHlg.spacing = 3f;

            _searchField = UGUIShip.CreateInputField(searchGo.transform, new Rect(0f, 0f, 100f, BTN_H),
                "ui.search_repositories", new Color(0f, 0f, 0f, 0.4f), WHITE, FS_SM);
            _searchField.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            UGUIShip.SetInputText(_searchField, _searchQuery, false);
            _searchField.onValueChanged.AddListener(new Action<string>(val =>
            {
                _searchQuery = val ?? "";
                ApplySearchFilter(_searchQuery);
            }));

            var backBtn = UGUIShip.CreateButton(searchGo.transform, "‹", BTN_DARK, CYAN, FS_SM,
                new Action(() => CloseBackToHost(false)));
            backBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = BTN_H;

            SkinRepo defaultRepo = null;
            foreach (var repo in registry.Repos)
                if (repo.isDefault) { defaultRepo = repo; break; }
            if (defaultRepo != null) BuildRepoEntryRow(_rowsParent, defaultRepo);
            BuildImportedEntryRow(_rowsParent);
            BuildFeaturedEntryRow(_rowsParent);

            bool anyAdded = false;
            foreach (var repo in registry.Repos)
                if (!repo.isDefault) { anyAdded = true; break; }

            if (anyAdded)
            {
                var secGo = new GameObject("RepoSectionLabel");
                secGo.transform.SetParent(_rowsParent, false);
                secGo.AddComponent<RectTransform>();
                secGo.AddComponent<LayoutElement>().preferredHeight = FS + 18f;
                var secLbl = UGUIShip.CreateStretchLabel(secGo.transform, "ui.added_repositories", FS, WHITE);
                secLbl.alignment = TextAnchor.LowerLeft;
                var secLblRt = secLbl.GetComponent<RectTransform>();
                secLblRt.offsetMin = new Vector2(14f, secLblRt.offsetMin.y);

                float viewportH = TabHeight - BTN_H * 5f - (FS + 18f) - 30f;
                if (viewportH < BTN_H * 2f) viewportH = BTN_H * 2f;

                var scroll = UGUIShip.CreateScrollView(_rowsParent, new Rect(0f, 0f, 100f, viewportH));
                scroll.scrollRect.scrollSensitivity = 24f;
                scroll.scrollRect.gameObject.AddComponent<LayoutElement>().preferredHeight = viewportH;
                var vp = scroll.scrollRect.viewport;
                if (vp != null) vp.offsetMin = new Vector2(0f, vp.offsetMin.y);

                var cVlg = scroll.content.gameObject.AddComponent<VerticalLayoutGroup>();
                cVlg.childControlWidth = true;
                cVlg.childControlHeight = true;
                cVlg.childForceExpandWidth = true;
                cVlg.childForceExpandHeight = false;
                cVlg.spacing = 2f;
                scroll.content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                _scrollContent = scroll.content;
                PopulateRepoEntries(scroll.content);
            }

            var addRow = new GameObject("RepoAddRow");
            addRow.transform.SetParent(_rowsParent, false);
            addRow.AddComponent<RectTransform>();
            addRow.AddComponent<LayoutElement>().preferredHeight = BTN_H;
            var addHlg = addRow.AddComponent<HorizontalLayoutGroup>();
            addHlg.childForceExpandHeight = true;
            addHlg.childForceExpandWidth = false;
            addHlg.spacing = 3f;

            var addBtn = UGUIShip.CreateButton(addRow.transform, "ui.add_repositories",
                BTN_DARK, CYAN, FS_SM, new Action(OnAddRepo));
            var addBtnLE = addBtn.gameObject.AddComponent<LayoutElement>();
            addBtnLE.preferredWidth = 180f;
            addBtnLE.preferredHeight = BTN_H;

            LayoutRebuilder.ForceRebuildLayoutImmediate(_rowsParent);
            if (_scrollContent != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollContent);
        }

        void PopulateRepoEntries(RectTransform contentRt)
        {
            for (int i = contentRt.childCount - 1; i >= 0; i--)
                Destroy(contentRt.GetChild(i).gameObject);

            ClearSearchRows();
            foreach (var repo in Registry.Repos)
            {
                if (repo.isDefault) continue;
                BuildRepoEntryRow(contentRt, repo);
            }
            ApplySearchFilter(_searchQuery);
        }

        static void MakeButtonScrollable(Button btn)
        {
            if (btn == null) return;
            btn.transition = Selectable.Transition.None;
            var trigger = btn.GetComponent<EventTrigger>();
            if (trigger != null) Destroy(trigger);
        }

        static Button CreateRepoRowButton(RectTransform parent, string goName, string label, Color labelColor, Action onClick)
        {
            var rowGo = new GameObject(goName);
            rowGo.transform.SetParent(parent, false);
            rowGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, RepoRowH);
            rowGo.AddComponent<LayoutElement>().preferredHeight = RepoRowH;

            var btn = UGUIShip.CreateButton(rowGo.transform, label, BTN_DARK, labelColor, FS_SM, onClick);
            MakeButtonScrollable(btn);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return btn;
        }

        void BuildRepoEntryRow(RectTransform parent, SkinRepo repo)
        {
            var capturedRepo = repo;
            var btn = CreateRepoRowButton(parent, "RepoRow_" + repo.repoName, capturedRepo.DisplayName, WHITE,
                new Action(() =>
                {
                    Host.ImportedSelected = false;
                    Host.FeaturedSelected = false;
                    Host.SelectedRepo = capturedRepo;
                    Registry.SetActive(capturedRepo);
                    CloseBackToHost(true);
                }));
            RegisterSearchRow(btn.transform.parent.gameObject, capturedRepo.DisplayName);
            BuildRepoButtonContents(btn, capturedRepo, _coverImages);
        }

        static void BuildRepoButtonContents(Button btn, SkinRepo repo, Dictionary<string, RawImage> coverSink,
            bool showControls = true, Texture2D fixedCover = null, Action onRemoved = null)
        {
            var coverGo = new GameObject("RepoCover");
            coverGo.transform.SetParent(btn.transform, false);
            coverGo.transform.SetAsFirstSibling();
            var coverRt = coverGo.AddComponent<RectTransform>();
            coverRt.anchorMin = Vector2.zero;
            coverRt.anchorMax = Vector2.one;
            // sit inside the delux button's rounded frame so its corners/AA fringe don't leak the image
            coverRt.offsetMin = new Vector2(3f, 3f);
            coverRt.offsetMax = new Vector2(-3f, -3f);
            var coverImg = coverGo.AddComponent<RawImage>();
            coverImg.raycastTarget = false;
            if (fixedCover != null)
            {
                coverImg.texture = fixedCover; coverImg.color = Color.white;
            }
            else
            {
                coverImg.color = new Color(1f, 1f, 1f, 0f);
                if (coverSink != null) coverSink[repo.githubUrl] = coverImg;
                var cachedCoverTex = Registry.GetCover(repo);
                if (cachedCoverTex != null) { coverImg.texture = cachedCoverTex; coverImg.color = Color.white; }
                else Registry.FetchCover(repo);
            }

            var lbl = btn.transform.Find("Label")?.GetComponent<Text>();
            if (lbl != null)
            {
                lbl.transform.SetAsLastSibling();
                lbl.alignment = TextAnchor.MiddleLeft;
                var lblRt = lbl.GetComponent<RectTransform>();
                lblRt.anchorMin = Vector2.zero;
                lblRt.anchorMax = Vector2.one;
                lblRt.pivot = new Vector2(0.5f, 0.5f);
                lblRt.offsetMin = new Vector2(14f, 0f);
                lblRt.offsetMax = new Vector2(showControls ? -RepoStripH : 0f, 0f);

                string applied = RemoteProfileStore.LocalAppliedSummary(repo);
                if (!string.IsNullOrEmpty(applied))
                {
                    var mark = UGUIShip.CreateStretchLabel(btn.transform, "*", FS, CYAN);
                    var markRt = mark.GetComponent<RectTransform>();
                    markRt.anchorMin = new Vector2(0f, 0.5f);
                    markRt.anchorMax = new Vector2(0f, 0.5f);
                    markRt.pivot = new Vector2(0f, 0.5f);
                    markRt.sizeDelta = new Vector2(RepoStripH, RepoStripH);
                    markRt.anchoredPosition = new Vector2(14f + UGUIShip.PreferredWidth(lbl) + 3f, 0f);
                    BetterFGUIMan.MakeObjectTooltip(markRt, "Applied from here: " + applied, 0.15f);
                }
            }

            if (!showControls) return;

            if (repo.isDefault)
            {
                var star = UGUIShip.CreateStretchLabel(btn.transform, "★", FS_SM, GOLD);
                var starRt = star.GetComponent<RectTransform>();
                starRt.anchorMin = new Vector2(1f, 0.5f);
                starRt.anchorMax = new Vector2(1f, 0.5f);
                starRt.pivot = new Vector2(1f, 0.5f);
                starRt.sizeDelta = new Vector2(RepoStripH, RepoStripH);
                starRt.anchoredPosition = Vector2.zero;
            }
            else
            {
                var capturedRepo = repo;
                var removeBtn = UGUIShip.CreateButton(btn.transform, "-",
                    BTN_REMOVE, WHITE, FS_SM, new Action(() => { Registry.RemoveRepo(capturedRepo); onRemoved?.Invoke(); }));
                MakeButtonScrollable(removeBtn);
                var rmRt = removeBtn.GetComponent<RectTransform>();
                rmRt.anchorMin = new Vector2(1f, 0.5f);
                rmRt.anchorMax = new Vector2(1f, 0.5f);
                rmRt.pivot = new Vector2(1f, 0.5f);
                rmRt.sizeDelta = new Vector2(RepoStripH, RepoStripH);
                rmRt.anchoredPosition = Vector2.zero;
            }
        }

        void BuildFeaturedEntryRow(RectTransform parent)
        {
            var btn = CreateRepoRowButton(parent, "RepoRow_Featured", "ui.featured_repositories", GOLD, new Action(() =>
            {
                Host.FeaturedSelected = true;
                Host.ImportedSelected = false;
                Registry?.FetchFeatured();
                CloseBackToHost(true);
            }));
            BuildRepoButtonContents(btn, null, null, showControls: false,
                fixedCover: LoadTex("BetterFG.assets.ui.reposelector.featured.png", ref _featuredTex));
        }

        void BuildImportedEntryRow(RectTransform parent)
        {
            var btn = CreateRepoRowButton(parent, "RepoRow_Imported", "ui.imported_skins", ORANGE, new Action(() =>
            {
                Host.ImportedSelected = true;
                Host.FeaturedSelected = false;
                CloseBackToHost(true);
            }));
            BuildRepoButtonContents(btn, null, null, showControls: false,
                fixedCover: LoadTex("BetterFG.assets.ui.reposelector.imported.png", ref _importedTex));
        }

        void OnAddRepo()
        {
            if (_rowsParent == null) return;
            if (_rowsParent.Find("RepoInputRow") != null) return;

            var addRow = _rowsParent.Find("RepoAddRow");
            int addIdx = addRow != null ? addRow.GetSiblingIndex() : _rowsParent.childCount;
            if (addRow != null) addRow.gameObject.SetActive(false);

            var rowGo = new GameObject("RepoInputRow");
            rowGo.transform.SetParent(_rowsParent, false);
            rowGo.transform.SetSiblingIndex(addIdx);
            rowGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, BTN_H);
            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.spacing = 3f;
            rowGo.AddComponent<LayoutElement>().preferredHeight = BTN_H;

            var field = UGUIShip.CreateInputField(rowGo.transform, new Rect(0f, 0f, 100f, BTN_H),
                "ui.https_github_com_author_repo", null, CYAN, FS_SM);
            field.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            Action commit = () =>
            {
                string url = (field.text ?? "").Trim();
                if (addRow != null) addRow.gameObject.SetActive(true);
                Destroy(rowGo);
                if (!string.IsNullOrEmpty(url))
                    Registry?.AddRepo(url);
            };

            var ok = UGUIShip.CreateButton(rowGo.transform, "ui.ok",
                new Color(0.15f, 0.3f, 0.15f, 1f), GREEN, FS_SM, commit);
            ok.gameObject.AddComponent<LayoutElement>().preferredWidth = BTN_H * 1.5f;

            var cancel = UGUIShip.CreateButton(rowGo.transform, "✕",
                BTN_DARK, HINT, FS_SM, new Action(() =>
                {
                    if (addRow != null) addRow.gameObject.SetActive(true);
                    Destroy(rowGo);
                }));
            cancel.gameObject.AddComponent<LayoutElement>().preferredWidth = BTN_H;

            LayoutRebuilder.ForceRebuildLayoutImmediate(_rowsParent);
            field.ActivateInputField();
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

            if (tall)
            {
                BuildRepoButtonContents(nameBtn, activeRepo, null, showControls: false);
                TrackHostRowCover(activeRepo.githubUrl, nameBtn.transform.Find("RepoCover")?.GetComponent<RawImage>());
                var lbl = nameBtn.transform.Find("Label")?.GetComponent<Text>();
                if (lbl != null) lbl.color = labelColor;
            }
            else
            {
                var lbl = nameBtn.transform.Find("Label")?.GetComponent<Text>();
                if (lbl != null)
                {
                    lbl.alignment = TextAnchor.MiddleLeft;
                    lbl.color = labelColor;
                    var lblRt = lbl.GetComponent<RectTransform>();
                    if (lblRt != null) lblRt.offsetMin = new Vector2(14f, lblRt.offsetMin.y);
                }
            }

            var caret = UGUIShip.CreateButton(rowGo.transform, "▾", BTN_DARK, CYAN, FS_SM, open);
            caret.gameObject.AddComponent<LayoutElement>().preferredWidth = BTN_H;

            return y + rowH + SH;
        }
    }
}
