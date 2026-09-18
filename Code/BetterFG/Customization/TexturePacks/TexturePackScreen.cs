using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Player;
using BetterFG.Customization.UI;
using BetterFG.Features.Onboarding;
using BetterFG.Services;
using static BetterFG.Services.AddressablesCatalogService;
using BetterFG.UI;
using BetterFG.UI.Tabs;
using BettrFG.uGUI;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;
using LayoutElement = UnityEngine.UI.LayoutElement;

namespace BetterFG.Customization.TexturePacks
{
    // browse every texture the game has and pack whichever ones got edited in an already-created
    // project's folder. creating a project and exporting it lives in the wizard + progress tabs -
    // this screen never touches either.
    public class TexturePackScreen : MonoBehaviour
    {
        public TexturePackScreen(IntPtr ptr) : base(ptr) { }

        public static TexturePackScreen Instance;

        private const int BackdropOrder = 500;
        private const int ContentOrder = 501;

        private static readonly Color BACKDROP = Color.black;
        private static readonly Color WHITE = UGUIShip.WHITE;
        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color BTN_DARK = UGUIShip.BTN_DARK;
        private static readonly Color BAR_FILL = new Color(0.30f, 0.62f, 0.34f, 1f);
        private static readonly Color ROW_ALT = new Color(0.27f, 0.27f, 0.29f, 1f);
        private static readonly Color ROW_SEL = new Color(0.25f, 0.5f, 0.25f, 1f);
        private const float ROW_GAP = 2f;

        private Canvas _backdropCanvas, _canvas;
        private RectTransform _root, _content;
        private bool _closing;

        private InputField _searchField;
        private RectTransform _listContent;
        private Text _status;
        private float _listW = 400f;
        private float _listPadTop, _curveCenterY, _curveR;
        private readonly List<RectTransform> _rowBodies = new List<RectTransform>();
        private readonly List<float> _rowCenters = new List<float>();
        private readonly List<bool> _rowVisible = new List<bool>();
        private RectTransform _listRoot;
        private float _listH, _scrollVel;
        private int _selfScrollFrame = -1;

        private const int PAGES = 10;
        private int _page;
        private Text _pageCarousel;
        private GameObject _pageRow, _barRow;
        private LoadingBar _bar;
        private readonly List<string> _matches = new List<string>();
        private HashSet<string> _catalogKeys = new HashSet<string>();
        private readonly List<string> _keys = new List<string>();
        private readonly List<Button> _rows = new List<Button>();
        private int _previewIdx = -1;

        private RawImage _previewImg;
        private AspectRatioFitter _previewFitter;
        private AsyncOperationHandle<Texture2D> _texHandle;
        private AsyncOperationHandle<Sprite> _sprHandle;
        private bool _texHandleValid, _sprHandleValid;
        private Texture2D _originalPreviewTex, _localPreviewTex;
        private string _previewTexName, _previewLocalHash;
        private Sprite _previewLiveSprite;

        private GameObject _previewGroup, _settingsGroup;
        private InputField _settingsNameField, _settingsAuthorField;
        private Text _settingsCompLbl, _settingsCoverLbl;
        private Text _settingsLink;
        private bool _flipping;

        private TexturePackProject _project;
        private static TexturePackProject _pendingProject;
        private static bool _pendingCreate;

        private bool _creating;
        private Text _titleLbl;
        private GameObject _wizardPopup;
        private InputField _wizNameField, _wizAuthorField;
        private string _wizName = "", _wizAuthor = "", _wizLocation = "";

        public static void Open(TexturePackProject project)
        {
            if (Instance != null) return;
            _pendingProject = project;
            _pendingCreate = false;
            var go = new GameObject("BettrFG_TexturePackScreen");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<TexturePackScreen>();
        }

        // opens the screen (properly hiding the game behind it) with its own panels disabled, then
        // runs the name/author/location wizard on the SAME canvas - never a bare popup over the raw
        // game UI. panels come back once the wizard finishes.
        public static void OpenForNewProject()
        {
            if (Instance != null) return;
            _pendingProject = null;
            _pendingCreate = true;
            var go = new GameObject("BettrFG_TexturePackScreen");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<TexturePackScreen>();
        }

        private float _tickTimer;
        void Update()
        {
            TickSmoothScroll();
            _tickTimer += Time.deltaTime;
            if (_tickTimer < 0.1f) return;
            _tickTimer = 0f;
            WinDialogs.Tick();
        }

        public static void Close()
        {
            if (Instance == null || Instance._closing) return;
            Instance._closing = true;
            Instance.StartCoroutine(Instance.CloseRoutine().WrapToIl2Cpp());
        }

        void Start() => StartCoroutine(OpenRoutine().WrapToIl2Cpp());

        private IEnumerator OpenRoutine()
        {
            _project = _pendingProject;
            _pendingProject = null;
            _creating = _pendingCreate;
            _pendingCreate = false;

            LoadingScreenService.Show();
            yield return new WaitForSecondsRealtime(0.35f);

            try
            {
                WorldHideService.HideCanvases(this);
                BetterFGUIMan.Instance?.SetVisibleForced(false);
                if (BetterFGUIMan.Instance != null) BetterFGUIMan.Instance.VisibilityLocked = true;
                Build();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"texture pack screen wouldn't build ({ex}), closing it instead of leaving you stuck behind the cover");
                Close();
                yield break;
            }

            yield return new WaitForSecondsRealtime(0.15f);
            yield return StartCoroutine(LoadingScreenService.HideRoutine().WrapToIl2Cpp());
        }

        private IEnumerator CloseRoutine()
        {
            LoadingScreenService.Show();
            yield return new WaitForSecondsRealtime(0.35f);

            try
            {
                ClearPreview();
                if (_backdropCanvas != null) Destroy(_backdropCanvas.gameObject);
                if (_canvas != null) Destroy(_canvas.gameObject);
            }
            catch (Exception ex) { Plugin.Log.LogError($"texture pack screen teardown threw: {ex}"); }

            WorldHideService.Show(this);
            RestoreBettrFGUi();
            Instance = null;

            yield return StartCoroutine(LoadingScreenService.HideRoutine().WrapToIl2Cpp());
            Destroy(gameObject);
        }

        private static void RestoreBettrFGUi()
        {
            if (BetterFGUIMan.Instance == null) return;
            BetterFGUIMan.Instance.VisibilityLocked = false;
            BetterFGUIMan.Instance.SetVisibleForced(true);
        }

        private void OnDestroy()
        {
            WorldHideService.Show(this);
            RestoreBettrFGUi();
        }

        private void Build()
        {
            _backdropCanvas = UGUIShip.CreateCanvas("BettrFG_TexPackBackdrop");
            _backdropCanvas.sortingOrder = BackdropOrder;
            var bg = new GameObject("Backdrop");
            bg.transform.SetParent(_backdropCanvas.transform, false);
            var brt = bg.AddComponent<RectTransform>();
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            var bimg = bg.AddComponent<Image>();
            bimg.color = BACKDROP;
            bimg.raycastTarget = true;
            BuildFallForceBackdrop(brt);

            _canvas = UGUIShip.CreateCanvas("BettrFG_TexPackScreen");
            _canvas.sortingOrder = ContentOrder;

            var rootGo = new GameObject("Root");
            rootGo.transform.SetParent(_canvas.transform, false);
            _root = rootGo.AddComponent<RectTransform>();
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = _root.offsetMax = Vector2.zero;

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(_root, false);
            _content = contentGo.AddComponent<RectTransform>();
            _content.anchorMin = Vector2.zero;
            _content.anchorMax = Vector2.one;
            _content.offsetMin = _content.offsetMax = Vector2.zero;

            var refRes = UIScaleService.CurrentRef;
            float W = refRes.x, H = refRes.y;

            float listX = W * 0.045f;
            _listW = W * 0.295f;
            float maxShift = W * 0.0425f;
            _curveCenterY = H * 0.55f;
            _curveR = (_curveCenterY * _curveCenterY + maxShift * maxShift) / (2f * maxShift);
            float gradH = H * 0.233f;
            _listPadTop = gradH;

            var scroll = UGUIShip.CreateScrollView(_content, new Rect(listX, 0f, _listW + maxShift, H));
            var vp = scroll.scrollRect.viewport;
            vp.offsetMin = vp.offsetMax = Vector2.zero;
            scroll.scrollRect.verticalScrollbar = null;
            scroll.scrollRect.scrollSensitivity = 0f;
            scroll.scrollRect.inertia = true;
            scroll.scrollRect.decelerationRate = 0.12f;
            Destroy(scroll.scrollRect.transform.Find("Scrollbar").gameObject);
            scroll.scrollRect.onValueChanged.AddListener(new Action<Vector2>(OnListScrolled));
            _listContent = scroll.content;
            _listRoot = scroll.scrollRect.GetComponent<RectTransform>();
            _listH = H;

            UGUIShip.CreateGradientPanel(_content, new Rect(W * 0.01f, 0f, W * 0.415f, gradH),
                new Color(0f, 0f, 0f, 1f), new Color(0f, 0f, 0f, 0f));

            float titleH = 22f, rowH = 20f;
            string title = _project != null ? _project.name : "";
            float fileW = 56f;
            _titleLbl = UGUIShip.CreateLabel(_content, new Rect(listX, H * 0.045f, _listW * 0.9f - fileW - 8f, titleH), title,
                UIScale.FS_TITLE, WHITE, TextAnchor.MiddleLeft);
            _titleLbl.fontStyle = FontStyle.Bold;

            UGUIShip.CreateActionDropdown(_content, new Rect(listX + _listW * 0.9f - fileW, H * 0.045f + 1f, fileW, rowH), "texpack.file",
                new List<string> { "texpack.pack", "texpack.enable", "texpack.disable", "texpack.index_names", "texpack.exit" },
                i =>
                {
                    if (i == 0) DoPack();
                    else if (i == 1) DoEnable();
                    else if (i == 2) DoDisable();
                    else if (i == 3) StartCoroutine(IndexGuidNamesRoutine().WrapToIl2Cpp());
                    else Close();
                },
                UIScale.FS_SM, rowH);

            float searchY = H * 0.095f, searchH = H * 0.033f, searchW = W * 0.27f, searchBtnW = 52f;
            _searchField = UGUIShip.CreateInputField(_content, new Rect(listX, searchY, searchW - searchBtnW - 4f, searchH),
                "texpack.search_hint", Color.black, WHITE, UIScale.FS);
            _searchField.onEndEdit.AddListener(new Action<string>(v => Search()));
            UGUIShip.CreateButton(_content, new Rect(listX + searchW - searchBtnW, searchY, searchBtnW, searchH),
                "texpack.search", BTN_DARK, WHITE, UIScale.FS_SM, new Action(Search));

            float pageY = searchY + searchH + 6f;
            _pageRow = new GameObject("PageRow");
            _pageRow.transform.SetParent(_content, false);
            var pageRt = _pageRow.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(pageRt, new Rect(listX, pageY, searchW, 18f));
            _pageCarousel = UGUIShip.CreateCarousel(pageRt, new Rect(0f, 0f, searchW, 18f),
                PageLabels(), 0, new Action<int>(StepPage), BTN_DARK, UIScale.FS_SM);

            _bar = UGUIShip.CreateLoadingBar(_content, new Rect(listX, pageY + 18f + 5f, searchW, 12f), BAR_FILL);
            _barRow = _bar.gameObject;
            _barRow.SetActive(false);

            float pvW = W * 0.29f, pvH = H * 0.514f, pvX = W * 0.925f - pvW, pvY = H * 0.233f;
            _settingsLink = UGUIShip.CreateLinkText(_content, new Rect(pvX, pvY - titleH - 8f, pvW, titleH),
                "texpack.settings_link", ToggleSettings, fontSize: UIScale.FS, align: TextAnchor.MiddleRight);
            UGUIShip.Unstylize(_settingsLink);

            _previewGroup = PanelGroup("Preview", new Rect(pvX, pvY, pvW, pvH));
            var rawGo = new GameObject("Render");
            rawGo.transform.SetParent(_previewGroup.transform, false);
            var rrt = rawGo.AddComponent<RectTransform>();
            rrt.anchorMin = rrt.anchorMax = rrt.pivot = new Vector2(0.5f, 0.5f);
            rrt.sizeDelta = new Vector2(pvW - 22f, pvH - 22f);
            _previewImg = rawGo.AddComponent<RawImage>();
            _previewImg.color = Color.clear;
            _previewImg.raycastTarget = false;
            _previewFitter = rawGo.AddComponent<AspectRatioFitter>();
            _previewFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            _previewFitter.aspectRatio = 1f;

            _settingsGroup = PanelGroup("Settings", new Rect(pvX, pvY, pvW, pvH));
            BuildSettings(_settingsGroup.transform, pvW, pvH);
            _settingsGroup.SetActive(false);

            _status = UGUIShip.CreateLabel(_content, new Rect(pvX, pvY + pvH + 8f, pvW, 16f),
                "", UIScale.FS_SM, HINT, TextAnchor.MiddleRight);

            StartCoroutine(ScanImagesRoutine("").WrapToIl2Cpp());

            if (_creating)
            {
                contentGo.SetActive(false);
                ShowWizardNameStep();
            }
        }

        private void BuildFallForceBackdrop(RectTransform backdrop)
        {
            var source = ScreenBackgroundService.FindPreviewSource(ScreenBackgroundService.Screen.LoadingLevel);
            if (source == null) { Plugin.Log.LogInfo("no season background to clone for the texture pack screen, staying black"); return; }

            var clone = Instantiate(source.gameObject, backdrop, false);
            clone.name = "FallForceClone";
            clone.SetActive(true);
            foreach (var anim in clone.GetComponentsInChildren<Animator>(true)) anim.enabled = false;
            foreach (var img in clone.GetComponentsInChildren<Image>(true)) { img.raycastTarget = false; img.maskable = false; }

            var refRes = UIScaleService.CurrentRef;
            Vector2 nat = source.GetComponent<RectTransform>().sizeDelta;
            if (nat.x <= 1f || nat.y <= 1f) nat = new Vector2(1920f, 1080f);
            float fit = Mathf.Max(refRes.x / nat.x, refRes.y / nat.y);
            var crt = clone.GetComponent<RectTransform>();
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = nat;
            crt.anchoredPosition = Vector2.zero;
            crt.localScale = new Vector3(fit, fit, 1f);
            crt.localRotation = Quaternion.identity;

            var circles = ScreenBackgroundService.FindCirclesChild(clone.transform)?.GetComponent<Image>();
            if (circles != null && circles.material != null)
            {
                var mat = new Material(circles.material);
                circles.material = mat;
                if (mat.HasProperty("_OriginOffset")) mat.SetVector("_OriginOffset", new Vector4(0.78f, 0.5f, 0f, 0f));
            }

            var dim = new GameObject("Dim");
            dim.transform.SetParent(backdrop, false);
            var drt = dim.AddComponent<RectTransform>();
            drt.anchorMin = Vector2.zero;
            drt.anchorMax = Vector2.one;
            drt.offsetMin = drt.offsetMax = Vector2.zero;
            var dimg = dim.AddComponent<Image>();
            dimg.color = new Color(0f, 0f, 0f, 0.45f);
            dimg.raycastTarget = false;
        }

        private GameObject PanelGroup(string name, Rect rect)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_content, false);
            var rt = go.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(rt, rect);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(rect.x + rect.width * 0.5f, -(rect.y + rect.height * 0.5f));
            UGUIShip.ApplyDeluxPanel(go.AddComponent<Image>(), 1.4f);
            return go;
        }

        private void OnListScrolled(Vector2 _)
        {
            if (Time.frameCount != _selfScrollFrame) _scrollVel = 0f;
            ApplyCurve();
        }

        private float MaxScroll() => Mathf.Max(0f, _listContent.sizeDelta.y - _listH);

        private void TickSmoothScroll()
        {
            if (_listContent == null || !_content.gameObject.activeSelf) return;
            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0f && RectTransformUtility.RectangleContainsScreenPoint(_listRoot, Input.mousePosition))
                _scrollVel -= wheel * 800f;
            if (Mathf.Abs(_scrollVel) < 1f) { _scrollVel = 0f; return; }

            float dt = Time.unscaledDeltaTime;
            float next = Mathf.Clamp(_listContent.anchoredPosition.y + _scrollVel * dt, 0f, MaxScroll());
            if (next <= 0f || next >= MaxScroll()) _scrollVel = 0f;
            else _scrollVel *= Mathf.Exp(-6f * dt);
            _selfScrollFrame = Time.frameCount;
            _listContent.anchoredPosition = new Vector2(_listContent.anchoredPosition.x, next);
            ApplyCurve();
        }

        private void ApplyCurve()
        {
            float s = _listContent.anchoredPosition.y;
            float lo = s - 60f, hi = s + _listH + 60f;
            for (int i = 0; i < _rowBodies.Count; i++)
            {
                float c = _rowCenters[i];
                bool vis = c > lo && c < hi;
                if (vis != _rowVisible[i])
                {
                    _rowVisible[i] = vis;
                    _rowBodies[i].gameObject.SetActive(vis);
                }
                if (!vis) continue;
                float dy = c - s - _curveCenterY;
                float shift = _curveR - Mathf.Sqrt(Mathf.Max(0f, _curveR * _curveR - dy * dy));
                var pos = _rowBodies[i].anchoredPosition;
                pos.x = shift;
                _rowBodies[i].anchoredPosition = pos;
            }
        }

        private static string[] PageLabels()
        {
            var labels = new string[PAGES];
            for (int i = 0; i < PAGES; i++) labels[i] = LocalizationService.Format("assets.page", i + 1, PAGES);
            return labels;
        }

        private void BuildSettings(Transform parent, float w, float h)
        {
            float p = 11f, rowH = 20f, gap = 6f;

            var go = new GameObject("Fields");
            go.transform.SetParent(parent, false);
            UGUIShip.SetPixelRect(go.AddComponent<RectTransform>(), new Rect(p, p, w - p * 2f, h - p * 2f));

            float cy = 0f;
            UGUIShip.CreateLabel(go.transform, new Rect(0f, cy, w, rowH), "texpack.settings_name", UIScale.FS_SM, HINT, TextAnchor.MiddleLeft);
            cy += rowH;
            _settingsNameField = UGUIShip.CreateInputField(go.transform, new Rect(0f, cy, w - p * 2f, rowH), "texpack.name_hint", Color.black, WHITE, (int)UIScale.FS_SM);
            _settingsNameField.text = _project?.name ?? "";
            _settingsNameField.onEndEdit.AddListener(new Action<string>(OnSettingsNameChanged));
            cy += rowH + gap;

            UGUIShip.CreateLabel(go.transform, new Rect(0f, cy, w, rowH), "texpack.settings_author", UIScale.FS_SM, HINT, TextAnchor.MiddleLeft);
            cy += rowH;
            _settingsAuthorField = UGUIShip.CreateInputField(go.transform, new Rect(0f, cy, w - p * 2f, rowH), "texpack.author_hint", Color.black, WHITE, (int)UIScale.FS_SM);
            _settingsAuthorField.text = _project?.author ?? "";
            _settingsAuthorField.onEndEdit.AddListener(new Action<string>(OnSettingsAuthorChanged));
            cy += rowH + gap;

            UGUIShip.CreateLabel(go.transform, new Rect(0f, cy, w, rowH), "texpack.settings_compression", UIScale.FS_SM, HINT, TextAnchor.MiddleLeft);
            cy += rowH;
            int startPct = _project?.compressionPercent ?? 100;
            _settingsCompLbl = UGUIShip.CreateLabel(go.transform, new Rect(w - p * 2f - 40f, cy, 40f, rowH), startPct + "%", UIScale.FS_SM, WHITE, TextAnchor.MiddleRight);
            UGUIShip.CreateSlider(go.transform, 0f, cy, w - p * 2f - 46f, "", startPct / 100f, rowH, 0f, (int)UIScale.FS_SM,
                OnSettingsCompressionChanged, null, BTN_DARK, reserveLabel: false);
            cy += rowH + gap;

            UGUIShip.CreateLabel(go.transform, new Rect(0f, cy, w, rowH), "texpack.settings_cover_art", UIScale.FS_SM, HINT, TextAnchor.MiddleLeft);
            cy += rowH;
            float changeW = 70f;
            _settingsCoverLbl = UGUIShip.CreateLabel(go.transform, new Rect(0f, cy, w - p * 2f - changeW - gap, rowH),
                CoverArtLabel(), UIScale.FS_SM, WHITE, TextAnchor.MiddleLeft);
            UGUIShip.CreateButton(go.transform, new Rect(w - p * 2f - changeW, cy, changeW, rowH),
                "texpack.settings_change_cover", BTN_DARK, WHITE, UIScale.FS_SM, new Action(OnChangeCoverArt));
        }

        private string CoverArtLabel() => string.IsNullOrEmpty(_project?.coverArt)
            ? LocalizationService.Get("texpack.settings_no_cover")
            : Path.GetFileName(_project.coverArt);

        private void OnSettingsNameChanged(string v)
        {
            v = v?.Trim() ?? "";
            if (string.IsNullOrEmpty(v) || _project == null) return;
            _project.name = v;
            _project.Save();
            UGUIShip.RelabelText(_titleLbl, _project.name);
            TexturePacksTab.RefreshLiveInstances();
        }

        private void OnSettingsAuthorChanged(string v)
        {
            if (_project == null) return;
            _project.author = v?.Trim() ?? "";
            _project.Save();
            TexturePacksTab.RefreshLiveInstances();
        }

        private void OnSettingsCompressionChanged(float t)
        {
            if (_project == null) return;
            int pct = Mathf.RoundToInt(t * 100f);
            _project.compressionPercent = pct;
            UGUIShip.RelabelText(_settingsCompLbl, pct + "%");
            _project.Save();
        }

        private void OnChangeCoverArt()
        {
            if (_project == null) return;
            WinDialogs.PickPng(LocalizationService.Get("texpack.settings_cover_art"), path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                _project.coverArt = path;
                _project.Save();
                UGUIShip.RelabelText(_settingsCoverLbl, CoverArtLabel());
            });
        }

        private void ToggleSettings()
        {
            if (_flipping) return;
            StartCoroutine(FlipRoutine().WrapToIl2Cpp());
        }

        private IEnumerator FlipRoutine()
        {
            _flipping = true;
            bool toSettings = !_settingsGroup.activeSelf;
            var outGo = toSettings ? _previewGroup : _settingsGroup;
            var inGo = toSettings ? _settingsGroup : _previewGroup;
            yield return StartCoroutine(ScaleYRoutine(outGo.transform, 1f, 1.1f, 0f, 0.1f, 0.2f).WrapToIl2Cpp());
            outGo.SetActive(false);
            inGo.transform.localScale = new Vector3(1f, 0f, 1f);
            inGo.SetActive(true);
            yield return StartCoroutine(ScaleYRoutine(inGo.transform, 0f, 1.1f, 1f, 0.2f, 0.1f).WrapToIl2Cpp());
            UGUIShip.RelabelText(_settingsLink, toSettings ? "texpack.settings_back" : "texpack.settings_link");
            _flipping = false;
        }

        private static IEnumerator ScaleYRoutine(Transform t, float a, float b, float c, float d1, float d2)
        {
            for (float e = 0f; e < d1; e += Time.unscaledDeltaTime)
            {
                t.localScale = new Vector3(1f, Mathf.Lerp(a, b, e / d1), 1f);
                yield return null;
            }
            for (float e = 0f; e < d2; e += Time.unscaledDeltaTime)
            {
                t.localScale = new Vector3(1f, Mathf.Lerp(b, c, e / d2), 1f);
                yield return null;
            }
            t.localScale = new Vector3(1f, c, 1f);
        }

        private void SetStatus(string msg) { if (_status != null) UGUIShip.RelabelText(_status, msg); }

        private void Search()
        {
            string filter = _searchField.text?.Trim() ?? "";
            StartCoroutine(ScanImagesRoutine(filter).WrapToIl2Cpp());
        }

        private IEnumerator ScanImagesRoutine(string filter)
        {
            _barRow.SetActive(true);
            _bar.Set(0f);
            SetStatus("assets.scanning");
            yield return null;

            if (AddressablesCatalogService.Keys == null)
                yield return StartCoroutine(AddressablesCatalogService.LoadRoutine().WrapToIl2Cpp());
            if (AddressablesCatalogService.Keys == null)
            {
                _barRow.SetActive(false);
                SetStatus("assets.no_catalog");
                yield break;
            }

            _matches.Clear();
            var unnamed = new List<string>();
            var catalog = AddressablesCatalogService.Keys;
            _catalogKeys = new HashSet<string>(catalog, StringComparer.Ordinal);
            LoadGuidNames();
            for (int i = 0; i < catalog.Count; i++)
            {
                string s = catalog[i];
                if (!IsImage(s)) continue;
                bool isGuid = Guid.TryParse(s, out _);
                string label = isGuid && _guidNames.TryGetValue(s, out var nm) && nm.Length > 0 ? nm : s;
                if (filter.Length > 0 && label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                (label == s && isGuid ? unnamed : _matches).Add(s);
                if ((i & 8191) == 0)
                {
                    _bar.Set((i + 1f) / catalog.Count);
                    yield return null;
                }
            }

            var seenNames = new HashSet<string>(_matches, StringComparer.OrdinalIgnoreCase);
            foreach (var spr in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (spr == null || string.IsNullOrEmpty(spr.name) || spr.texture == null || spr.name.StartsWith("bfgtp_")) continue;
                if (Guid.TryParse(spr.name, out _)) continue;
                if (filter.Length > 0 && spr.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (seenNames.Add(spr.name)) _matches.Add(spr.name);
            }
            foreach (var tex in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if (tex == null || string.IsNullOrEmpty(tex.name) || tex.width <= 4 || tex.name.StartsWith("bfg_") || tex.name.StartsWith("bfgtp_")) continue;
                if (Guid.TryParse(tex.name, out _)) continue;
                if (filter.Length > 0 && tex.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (seenNames.Add(tex.name)) _matches.Add(tex.name);
            }

            _matches.Sort((a, b) => string.Compare(RowLabel(a), RowLabel(b), StringComparison.OrdinalIgnoreCase));
            unnamed.Sort(StringComparer.OrdinalIgnoreCase);
            _matches.AddRange(unnamed);
            _page = 0;
            UGUIShip.RelabelText(_pageCarousel, LocalizationService.Format("assets.page", 1, PAGES));
            yield return StartCoroutine(LoadPageRoutine().WrapToIl2Cpp());
        }

        private static Dictionary<string, string> _guidNames;
        private static string GuidNamesPath => Path.Combine(SettingsService.TexturePacksDir, "texture_names.txt");
        private bool _indexing;

        private static void LoadGuidNames()
        {
            if (_guidNames != null) return;
            _guidNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(GuidNamesPath)) return;
            foreach (var line in File.ReadAllLines(GuidNamesPath))
            {
                int t = line.IndexOf('\t');
                if (t > 0) _guidNames[line.Substring(0, t)] = line.Substring(t + 1);
            }
        }

        private static string RowLabel(string key)
        {
            if (!Guid.TryParse(key, out _)) return key;
            return _guidNames != null && _guidNames.TryGetValue(key, out var nm) && nm.Length > 0
                ? nm
                : LocalizationService.Format("texpack.unnamed_texture", key);
        }

        private IEnumerator IndexGuidNamesRoutine()
        {
            if (_indexing || AddressablesCatalogService.Keys == null) yield break;
            _indexing = true;
            LoadGuidNames();

            var todo = new List<string>();
            foreach (var k in AddressablesCatalogService.Keys)
                if (IsImage(k) && Guid.TryParse(k, out _) && !_guidNames.ContainsKey(k)) todo.Add(k);

            _barRow.SetActive(true);
            Directory.CreateDirectory(SettingsService.TexturePacksDir);
            var pending = new System.Text.StringBuilder();
            var batch = new List<(string key, AsyncOperationHandle<Texture2D> op)>();
            int named = 0;
            for (int i = 0; i < todo.Count; i += 8)
            {
                batch.Clear();
                for (int j = i; j < Mathf.Min(todo.Count, i + 8); j++)
                    batch.Add((todo[j], Addressables.LoadAssetAsync<Texture2D>(todo[j])));
                foreach (var b in batch) yield return b.op;
                foreach (var b in batch)
                {
                    string nm = b.op.Status == AsyncOperationStatus.Succeeded && b.op.Result != null ? b.op.Result.name : "";
                    _guidNames[b.key] = nm;
                    if (nm.Length > 0) { named++; pending.Append(b.key).Append('\t').Append(nm).Append('\n'); }
                    try { Addressables.Release(b.op); } catch { }
                }
                if (pending.Length > 8192) { File.AppendAllText(GuidNamesPath, pending.ToString()); pending.Clear(); }
                _bar.Set((i + batch.Count) / (float)todo.Count);
                SetStatus(LocalizationService.Format("texpack.indexing", i + batch.Count, todo.Count));
            }
            if (pending.Length > 0) File.AppendAllText(GuidNamesPath, pending.ToString());
            Resources.UnloadUnusedAssets();
            Plugin.Log.LogInfo($"texture name index: {named} of {todo.Count} guid textures got a name");
            _indexing = false;
            SetStatus(LocalizationService.Format("texpack.indexed", named));
            StartCoroutine(ScanImagesRoutine(_searchField.text?.Trim() ?? "").WrapToIl2Cpp());
        }

        private void StepPage(int dir)
        {
            if (_matches.Count == 0) return;
            _page = (_page + dir + PAGES) % PAGES;
            UGUIShip.RelabelText(_pageCarousel, LocalizationService.Format("assets.page", _page + 1, PAGES));
            StartCoroutine(LoadPageRoutine().WrapToIl2Cpp());
        }

        private IEnumerator LoadPageRoutine()
        {
            _keys.Clear();
            _previewIdx = -1;
            ClearPreview();
            RebuildRows();
            yield return null;

            int pageSize = Mathf.CeilToInt(_matches.Count / (float)PAGES);
            int start = _page * pageSize;
            int end = Mathf.Min(_matches.Count, start + pageSize);
            for (int i = start; i < end; i++) _keys.Add(_matches[i]);

            RebuildRows();
            _barRow.SetActive(false);
            SetStatus(_matches.Count == 0
                ? LocalizationService.Get("assets.nothing_matched")
                : LocalizationService.Format("assets.page_of", _keys.Count, _page + 1, PAGES, _matches.Count));
        }

        private void RebuildRows()
        {
            _rows.Clear();
            _rowBodies.Clear();
            _rowCenters.Clear();
            _rowVisible.Clear();
            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);

            float rowH = 22f * UIScale.S, y = _listPadTop;
            for (int i = 0; i < _keys.Count; i++)
            {
                int idx = i;
                var btn = UGUIShip.CreateButton(_listContent, new Rect(0f, y, _listW, rowH), RowLabel(_keys[i]),
                    RowColor(i, false), WHITE, UIScale.FS_SM, new Action(() => PickRow(idx)), skipHoverSound: true);
                var rowGo = btn.gameObject;
                var rowRt = rowGo.GetComponent<RectTransform>();

                var trig = rowGo.GetComponent<UnityEngine.EventSystems.EventTrigger>() ?? rowGo.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                var entry = new UnityEngine.EventSystems.EventTrigger.Entry
                { eventID = UnityEngine.EventSystems.EventTriggerType.PointerClick };
                entry.callback.AddListener(new Action<UnityEngine.EventSystems.BaseEventData>(data =>
                {
                    var ped = data?.TryCast<UnityEngine.EventSystems.PointerEventData>();
                    if (ped != null && ped.button == UnityEngine.EventSystems.PointerEventData.InputButton.Right)
                        OpenRowMenu(idx);
                }));
                trig.triggers.Add(entry);

                var lbl = btn.GetComponentInChildren<Text>();
                lbl.alignment = TextAnchor.MiddleLeft;
                UGUIShip.SetPixelRect(lbl.rectTransform, new Rect(8f, 0f, _listW - 16f, rowH));
                var le = rowGo.AddComponent<LayoutElement>();
                le.preferredHeight = rowH;
                UGUIShip.FitWrappedRow(le, lbl, 8f, _listW - 16f, rowH);
                float h = le.preferredHeight;
                rowRt.sizeDelta = new Vector2(_listW, h);
                _rowBodies.Add(rowRt);
                _rowCenters.Add(y + h * 0.5f);
                _rowVisible.Add(true);
                y += h + ROW_GAP;
                _rows.Add(btn);
            }
            _listContent.sizeDelta = new Vector2(0f, y);
            _scrollVel = 0f;
            ApplyCurve();
        }

        private static Color RowColor(int i, bool selected) => selected ? ROW_SEL : (i % 2 == 0 ? BTN_DARK : ROW_ALT);

        private void PickRow(int idx)
        {
            CloseRowMenu();
            if (_previewIdx >= 0 && _previewIdx < _rows.Count) UGUIShip.SetButtonColor(_rows[_previewIdx], RowColor(_previewIdx, false));
            _previewIdx = idx;
            UGUIShip.SetButtonColor(_rows[idx], RowColor(idx, true));
            StartCoroutine(PreviewKeyRoutine(_keys[idx]).WrapToIl2Cpp());
        }

        private GameObject _rowMenu;

        private void CloseRowMenu()
        {
            if (_rowMenu != null) { Destroy(_rowMenu); _rowMenu = null; }
        }

        private static Texture2D GetAtlasIfCropped(string key)
        {
            var spr = FindLiveSprite(key);
            if (spr == null || spr.texture == null || string.IsNullOrEmpty(spr.texture.name)) return null;
            return spr.texture.name != key ? spr.texture : null;
        }

        // export options for the picked row: the single cropped texture, and - only when it's
        // confirmed to be cut from a shared atlas - the whole atlas too. editing and re-packing the
        // atlas replaces that shared texture object wholesale, which is what every sprite cropped
        // from it actually samples from.
        // a full-screen invisible catch sits behind the menu so any click outside it - another row,
        // empty space, anywhere - closes it; the menu itself renders on top of that same catch.
        private void OpenRowMenu(int idx)
        {
            CloseRowMenu();

            _rowMenu = new GameObject("RowMenuRoot");
            _rowMenu.transform.SetParent(_root, false);
            var rootRt = _rowMenu.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;

            var catchGo = new GameObject("Catch");
            catchGo.transform.SetParent(_rowMenu.transform, false);
            var catchRt = catchGo.AddComponent<RectTransform>();
            catchRt.anchorMin = Vector2.zero;
            catchRt.anchorMax = Vector2.one;
            catchRt.offsetMin = catchRt.offsetMax = Vector2.zero;
            catchGo.AddComponent<Image>().color = Color.clear;
            var catchBtn = catchGo.AddComponent<Button>();
            catchBtn.transition = Selectable.Transition.None;
            catchBtn.onClick.AddListener(new Action(CloseRowMenu));

            string key = _keys[idx];
            var items = new List<(string labelId, Action action)>();

            var exported = FindProjectFile(key) ?? (idx == _previewIdx ? FindProjectFile(_previewTexName) : null);
            string exportedPath = exported != null ? Path.Combine(_project.TexturesDir, exported.fileName) : null;
            bool onDisk = exportedPath != null && File.Exists(exportedPath);
            bool edited = onDisk && TexturePackProject.Hash(exportedPath) != exported.hash;

            if (onDisk)
                items.Add(("texpack.open", () => { CloseRowMenu(); OpenFile(exportedPath); }));
            else
                items.Add(("texpack.export_and_open", () =>
                {
                    CloseRowMenu();
                    if (_project == null) { SetStatus("texpack.no_project"); return; }
                    StartCoroutine(ExportRoutine(key, false, true).WrapToIl2Cpp());
                }));
            if (edited)
                items.Add(("texpack.restore_original", () =>
                {
                    CloseRowMenu();
                    StartCoroutine(ExportRoutine(key, true, false).WrapToIl2Cpp());
                }));

            var atlasTex = GetAtlasIfCropped(key);
            if (atlasTex != null)
            {
                var atlasFile = FindProjectFile(atlasTex.name);
                string atlasPath = atlasFile != null ? Path.Combine(_project.TexturesDir, atlasFile.fileName) : null;
                bool atlasOnDisk = atlasPath != null && File.Exists(atlasPath);
                bool atlasEdited = atlasOnDisk && TexturePackProject.Hash(atlasPath) != atlasFile.hash;

                if (atlasOnDisk)
                    items.Add(("texpack.open_atlas", () => { CloseRowMenu(); OpenFile(atlasPath); }));
                else
                    items.Add(("texpack.export_and_open_atlas", () =>
                    {
                        CloseRowMenu();
                        if (_project == null) { SetStatus("texpack.no_project"); return; }
                        StartCoroutine(ExportAtlasRoutine(atlasTex, false, true).WrapToIl2Cpp());
                    }));
                if (atlasEdited)
                    items.Add(("texpack.restore_atlas_original", () =>
                    {
                        CloseRowMenu();
                        StartCoroutine(ExportAtlasRoutine(atlasTex, true, false).WrapToIl2Cpp());
                    }));
            }

            float rowH = 20f;
            float w = 90f;
            foreach (var it in items)
                w = Mathf.Max(w, UGUIShip.MeasureLabel(it.labelId, UIScale.FS_SM) + 26f);
            w = Mathf.Clamp(w, 90f, UGUIShip.ACTION_MENU_MAX_W);

            var menuGo = new GameObject("Menu");
            menuGo.transform.SetParent(_rowMenu.transform, false);
            var rt = menuGo.AddComponent<RectTransform>();

            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, Input.mousePosition, null, out var local);
            var r = _root.rect;
            UGUIShip.SetPixelRect(rt, new Rect(local.x - r.xMin, -(local.y - r.yMax), w, rowH * items.Count + 4f));
            UGUIShip.ApplyDeluxPanelDark(menuGo.AddComponent<Image>(), 2f);

            for (int i = 0; i < items.Count; i++)
            {
                var (labelId, action) = items[i];
                var itemGo = new GameObject("Item" + i);
                itemGo.transform.SetParent(rt, false);
                UGUIShip.SetPixelRect(itemGo.AddComponent<RectTransform>(), new Rect(2f, 2f + i * rowH, w - 4f, rowH));
                var img = itemGo.AddComponent<Image>();
                img.color = UGUIShip.ROW_CLEAR;
                var b = itemGo.AddComponent<Button>();
                b.targetGraphic = img;
                UGUIShip.PaintListRow(b, i, false);
                var itemLbl = UGUIShip.CreateLabel(itemGo.transform, new Rect(8f, 0f, w - 16f, rowH),
                    labelId, UIScale.FS_SM, WHITE, TextAnchor.MiddleLeft);
                itemLbl.raycastTarget = false;
                itemLbl.horizontalOverflow = HorizontalWrapMode.Overflow;
                b.onClick.AddListener(new Action(action));
            }
        }

        private static void OpenFile(string path)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { Plugin.Log.LogWarning($"couldn't open {path}: {ex.Message}"); }
        }

        private IEnumerator ExportRoutine(string key, bool overwrite, bool open)
        {
            SetStatus("texpack.exporting_one");

            Texture src = null;
            string texName = key;
            bool releaseHandle = false;
            AsyncOperationHandle<Texture2D> texOp = default;
            if (_catalogKeys.Contains(key))
            {
                texOp = Addressables.LoadAssetAsync<Texture2D>(key);
                yield return texOp;
                if (texOp.Status == AsyncOperationStatus.Succeeded && texOp.Result != null && !string.IsNullOrEmpty(texOp.Result.name))
                {
                    src = texOp.Result;
                    texName = texOp.Result.name;
                    releaseHandle = true;
                }
                else try { Addressables.Release(texOp); } catch { }
            }
            if (src == null)
            {
                var liveSpr = FindLiveSprite(key);
                src = liveSpr != null && liveSpr.texture != null
                    ? SkinApplicationService.CropAtlasToTexture2D(liveSpr, key)
                    : FindLiveTexture(key);
            }

            if (src == null)
            {
                SetStatus("texpack.export_one_failed");
                yield break;
            }

            yield return StartCoroutine(WriteExportRoutine(src, texName, overwrite, open).WrapToIl2Cpp());
            if (releaseHandle) Addressables.Release(texOp);
        }

        private IEnumerator ExportAtlasRoutine(Texture2D atlas, bool overwrite, bool open)
        {
            SetStatus("texpack.exporting_one");
            yield return null;
            yield return StartCoroutine(WriteExportRoutine(atlas, atlas.name, overwrite, open).WrapToIl2Cpp());
        }

        private IEnumerator WriteExportRoutine(Texture src, string texName, bool overwrite, bool open)
        {
            string safe = texName;
            foreach (var c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
            string fileName = safe + ".png";
            Directory.CreateDirectory(_project.TexturesDir);
            string outPath = Path.Combine(_project.TexturesDir, fileName);

            if (overwrite || !File.Exists(outPath))
            {
                if (!SkinApplicationService.SaveTexturePng(src, outPath, out string err))
                {
                    SetStatus(LocalizationService.Format("texpack.export_one_failed_reason", err));
                    yield break;
                }
                RememberFile(texName, fileName, outPath);
                _project.Save();
            }

            if (open) OpenFile(outPath);
            SetStatus(LocalizationService.Format(overwrite ? "texpack.restored_one" : "texpack.exported_one", fileName));
            RefreshPreviewOverride();
        }

        private void RememberFile(string texName, string fileName, string path)
        {
            string hash = TexturePackProject.Hash(path);
            foreach (var f in _project.files)
                if (f.fileName == fileName) { f.texName = texName; f.hash = hash; return; }
            _project.files.Add(new TexturePackFile { texName = texName, fileName = fileName, hash = hash });
        }

        private void ClearPreview()
        {
            if (_texHandleValid) { _texHandleValid = false; try { Addressables.Release(_texHandle); } catch { } }
            if (_sprHandleValid) { _sprHandleValid = false; try { Addressables.Release(_sprHandle); } catch { } }
            if (_localPreviewTex != null) { Destroy(_localPreviewTex); _localPreviewTex = null; }
            _originalPreviewTex = null;
            _previewTexName = null;
            _previewLocalHash = null;
            _previewLiveSprite = null;
            if (_previewImg != null) { _previewImg.texture = null; _previewImg.color = Color.clear; }
        }

        private IEnumerator PreviewKeyRoutine(string key)
        {
            ClearPreview();
            SetStatus("assets.loading_preview");

            if (_catalogKeys.Contains(key))
            {
                var texOp = Addressables.LoadAssetAsync<Texture2D>(key);
                yield return texOp;
                if (texOp.Status == AsyncOperationStatus.Succeeded && texOp.Result != null)
                {
                    _texHandleValid = true;
                    _texHandle = texOp;
                    _originalPreviewTex = texOp.Result;
                    _previewTexName = texOp.Result.name;
                    RefreshPreviewOverride();
                    SetStatus(LocalizationService.Format("assets.texture_info", key, texOp.Result.width, texOp.Result.height));
                    yield break;
                }
                try { Addressables.Release(texOp); } catch { }

                var sprOp = Addressables.LoadAssetAsync<Sprite>(key);
                yield return sprOp;
                if (sprOp.Status == AsyncOperationStatus.Succeeded && sprOp.Result != null && sprOp.Result.texture != null)
                {
                    _sprHandleValid = true;
                    _sprHandle = sprOp;
                    _originalPreviewTex = sprOp.Result.texture;
                    _previewTexName = sprOp.Result.texture.name;
                    RefreshPreviewOverride();
                    SetStatus(LocalizationService.Format("assets.texture_info", key, sprOp.Result.texture.width, sprOp.Result.texture.height));
                    yield break;
                }
                try { Addressables.Release(sprOp); } catch { }
            }

            var liveTex = FindLiveTexture(key);
            if (liveTex != null)
            {
                _originalPreviewTex = liveTex;
                _previewTexName = key;
                RefreshPreviewOverride();
                SetStatus(LocalizationService.Format("assets.texture_info", key, liveTex.width, liveTex.height));
                yield break;
            }

            var liveSpr = FindLiveSprite(key);
            if (liveSpr != null && liveSpr.texture != null)
            {
                var crop = SkinApplicationService.CropAtlasToTexture2D(liveSpr, key);
                if (crop != null)
                {
                    _originalPreviewTex = crop;
                    _previewTexName = key;
                    _previewLiveSprite = liveSpr;
                    RefreshPreviewOverride();
                    SetStatus(LocalizationService.Format("assets.texture_info", key, crop.width, crop.height));
                    yield break;
                }
            }

            Plugin.Log.LogWarning("neither texture nor sprite loaded for " + key);
            SetStatus("assets.not_previewable");
        }

        private static Texture2D FindLiveTexture(string name)
        {
            foreach (var tex in Resources.FindObjectsOfTypeAll<Texture2D>())
                if (tex != null && tex.name == name && tex.width > 4) return tex;
            return null;
        }

        private static Sprite FindLiveSprite(string name)
        {
            foreach (var spr in Resources.FindObjectsOfTypeAll<Sprite>())
                if (spr != null && spr.name == name) return spr;
            return null;
        }

        // alt-tabbing back into the game (after touching up the exported PNG in an external editor)
        // should show the edit, but never by blindly re-fetching - only the currently previewed
        // texture is checked, only against its own recorded export hash, and only if it actually
        // changed. this only ever fires while this screen exists, so it can't run anywhere else.
        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) RefreshPreviewOverride();
        }

        private TexturePackFile FindProjectFile(string texName)
        {
            if (_project == null || texName == null) return null;
            foreach (var f in _project.files)
                if (f.texName == texName) return f;
            return null;
        }

        private void RefreshPreviewOverride()
        {
            if (_previewTexName == null) return;

            var f = FindProjectFile(_previewTexName);
            string path = f != null ? Path.Combine(_project.TexturesDir, f.fileName) : null;
            bool iconExists = path != null && File.Exists(path);
            string iconHash = iconExists ? TexturePackProject.Hash(path) : null;
            bool iconChanged = iconExists && iconHash != f.hash;

            string atlasName = _previewLiveSprite != null && _previewLiveSprite.texture != null ? _previewLiveSprite.texture.name : null;
            var af = atlasName != null ? FindProjectFile(atlasName) : null;
            string atlasPath = af != null ? Path.Combine(_project.TexturesDir, af.fileName) : null;
            bool atlasExists = atlasPath != null && File.Exists(atlasPath);
            string atlasHash = atlasExists ? TexturePackProject.Hash(atlasPath) : null;
            bool atlasChanged = atlasExists && atlasHash != af.hash;

            if (!iconChanged && !atlasChanged)
            {
                if (_localPreviewTex != null)
                {
                    Destroy(_localPreviewTex);
                    _localPreviewTex = null;
                    _previewLocalHash = null;
                }
                ShowActivePreviewTexture();
                return;
            }

            string combinedHash = (iconChanged ? iconHash : "") + "|" + (atlasChanged ? atlasHash : "");
            if (combinedHash == _previewLocalHash) return;

            if (_localPreviewTex != null) Destroy(_localPreviewTex);

            if (iconChanged)
            {
                _localPreviewTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                _localPreviewTex.LoadImage(File.ReadAllBytes(path));
            }
            else
            {
                var editedAtlas = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                editedAtlas.LoadImage(File.ReadAllBytes(atlasPath));
                _localPreviewTex = CropRegion(editedAtlas, _previewLiveSprite.textureRect);
                Destroy(editedAtlas);
            }
            _previewLocalHash = combinedHash;
            ShowActivePreviewTexture();
        }

        private static Texture2D CropRegion(Texture2D src, Rect rect)
        {
            int w = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            int h = Mathf.Max(1, Mathf.RoundToInt(rect.height));
            var prev = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var crop = new Texture2D(w, h, TextureFormat.RGBA32, false);
            crop.ReadPixels(new Rect(rect.x, rect.y, w, h), 0, 0);
            crop.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return crop;
        }

        private void ShowActivePreviewTexture()
        {
            var tex = _localPreviewTex != null ? _localPreviewTex : _originalPreviewTex;
            if (tex == null || _previewImg == null) return;
            _previewImg.texture = tex;
            _previewImg.color = WHITE;
            _previewFitter.aspectRatio = tex.height > 0 ? (float)tex.width / tex.height : 1f;
        }

        private void DoPack()
        {
            if (_project == null) { SetStatus("texpack.no_project"); return; }
            StartCoroutine(_project.PackRoutine(
                (i, n) => SetStatus(LocalizationService.Format("texpack.packing", i, n)),
                (outPath, err) => SetStatus(err != null
                    ? LocalizationService.Format("texpack.pack_failed", err)
                    : LocalizationService.Format("texpack.packed", Path.GetFileName(outPath)))).WrapToIl2Cpp());
        }

        private void DoEnable()
        {
            if (_project == null) { SetStatus("texpack.no_project"); return; }
            if (!File.Exists(_project.PackedPath)) { SetStatus("texpack.pack_first"); return; }
            SetStatus(TexturePackRuntimeService.Load(_project.PackedPath, out string err)
                ? "texpack.enabled"
                : LocalizationService.Format("texpack.enable_failed", err));
        }

        private void DoDisable()
        {
            TexturePackRuntimeService.Clear();
            SetStatus("texpack.disabled");
        }

        // name -> author -> location, all on this screen's own canvas (already hiding the game
        // behind its backdrop) instead of a bare popup floating over raw menu UI. the real panels
        // stay SetActive(false) the whole time, so there's nothing behind the popup to click.
        private void SwapWizardPopup(GameObject next)
        {
            if (_wizardPopup != null) StartCoroutine(PromptHost.CloseThenDestroy(_wizardPopup).WrapToIl2Cpp());
            _wizardPopup = next;
            StartCoroutine(PromptHost.OpenPopup(next).WrapToIl2Cpp());
        }

        private void ShowWizardNameStep()
        {
            SwapWizardPopup(OnboardingPopup.Show(_canvas, "texpack.wizard_title", "texpack.wizard_name_body",
                Vector2.zero, new Vector2(0.5f, 0.5f),
                new[]
                {
                    new OnboardingPopup.ButtonSpec("texpack.wizard_cancel", new Action(CancelWizard), OnboardingPopup.BtnGhost),
                    new OnboardingPopup.ButtonSpec("texpack.wizard_next", new Action(SubmitWizardName), OnboardingPopup.BtnPrimary),
                },
                _wizName, field => _wizNameField = field));
        }

        private void SubmitWizardName()
        {
            string v = _wizNameField != null ? _wizNameField.text?.Trim() : "";
            if (string.IsNullOrEmpty(v)) return;
            _wizName = v;
            ShowWizardAuthorStep();
        }

        private void ShowWizardAuthorStep()
        {
            SwapWizardPopup(OnboardingPopup.Show(_canvas, "texpack.wizard_title", "texpack.wizard_author_body",
                Vector2.zero, new Vector2(0.5f, 0.5f),
                new[]
                {
                    new OnboardingPopup.ButtonSpec("texpack.wizard_back", new Action(ShowWizardNameStep), OnboardingPopup.BtnGhost),
                    new OnboardingPopup.ButtonSpec("texpack.wizard_next", new Action(SubmitWizardAuthor), OnboardingPopup.BtnPrimary),
                },
                _wizAuthor, field => _wizAuthorField = field));
        }

        private void SubmitWizardAuthor()
        {
            _wizAuthor = _wizAuthorField != null ? (_wizAuthorField.text?.Trim() ?? "") : "";
            ShowWizardLocationStep();
        }

        private void ShowWizardLocationStep()
        {
            string body = string.IsNullOrEmpty(_wizLocation)
                ? LocalizationService.Get("texpack.wizard_location_body")
                : LocalizationService.Format("texpack.wizard_location_body_picked", _wizLocation);

            SwapWizardPopup(OnboardingPopup.Show(_canvas, "texpack.wizard_title", body,
                Vector2.zero, new Vector2(0.5f, 0.5f),
                new[]
                {
                    new OnboardingPopup.ButtonSpec("texpack.wizard_back", new Action(ShowWizardAuthorStep), OnboardingPopup.BtnGhost),
                    new OnboardingPopup.ButtonSpec("texpack.browse", new Action(OnBrowseWizardLocation), OnboardingPopup.BtnGhost),
                    new OnboardingPopup.ButtonSpec("texpack.wizard_create", new Action(SubmitWizardLocation), OnboardingPopup.BtnPrimary),
                }));
        }

        private void OnBrowseWizardLocation()
        {
            WinDialogs.PickFolder(LocalizationService.Get("texpack.pick_project_folder"), folder =>
            {
                if (string.IsNullOrEmpty(folder)) return;
                _wizLocation = folder;
                ShowWizardLocationStep();
            });
        }

        private void SubmitWizardLocation()
        {
            if (string.IsNullOrEmpty(_wizLocation)) return;

            _project = new TexturePackProject { location = _wizLocation, name = _wizName, author = _wizAuthor, compressionPercent = 100 };
            _project.Save();
            TexturePacksTab.RefreshLiveInstances();

            CloseWizardPopup();
            RevealPanelsAfterWizard();
        }

        private void CancelWizard()
        {
            CloseWizardPopup();
            Close();
        }

        private void CloseWizardPopup()
        {
            if (_wizardPopup == null) return;
            StartCoroutine(PromptHost.CloseThenDestroy(_wizardPopup).WrapToIl2Cpp());
            _wizardPopup = null;
        }

        private void RevealPanelsAfterWizard()
        {
            _creating = false;
            UGUIShip.RelabelText(_titleLbl, _project.name);
            _content.gameObject.SetActive(true);
        }
    }
}
