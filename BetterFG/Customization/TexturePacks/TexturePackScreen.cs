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
using BetterFG.Utilities;
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
        private AsyncOperationHandle<Texture2D> _texHandle;
        private AsyncOperationHandle<Sprite> _sprHandle;
        private bool _texHandleValid, _sprHandleValid;
        private Texture2D _originalPreviewTex, _localPreviewTex;
        private string _previewTexName, _previewLocalHash;
        private Sprite _previewLiveSprite;

        private GameObject _previewGroup, _settingsGroup;
        private InputField _settingsNameField, _settingsAuthorField;
        private Text _settingsCompLbl, _settingsCoverLbl;
        private bool _flipping;

        private static readonly Color ROW_PICK = new Color(0.22f, 0.34f, 0.55f, 1f);
        private static readonly Color EXPORTED = new Color(0.55f, 0.9f, 0.6f, 1f);
        private static readonly Color SUBTLE = new Color(1f, 1f, 1f, 0.45f);
        private const float ROW_H = 42f;
        private static TMPro.TMP_FontAsset _titanFont;
        private static Material _titanMat;
        private static Texture2D _arcsA, _arcsB, _checkerTex;
        private TMPro.TextMeshProUGUI _titleTmp;
        private RectTransform _ringA, _ringB, _checker, _detail, _actions;
        private Text _detailName, _detailMeta;
        private Dictionary<string, Sprite> _liveSprites;
        private Dictionary<string, Texture2D> _liveTextures;
        private readonly List<CanvasGroup> _rowFades = new List<CanvasGroup>();
        private readonly List<RawImage> _thumbs = new List<RawImage>();
        private readonly List<AspectRatioFitter> _thumbFits = new List<AspectRatioFitter>();
        private readonly List<Text> _subs = new List<Text>();
        private readonly List<string> _subPrefix = new List<string>();
        private readonly Dictionary<int, AsyncOperationHandle<Texture2D>> _thumbOps = new Dictionary<int, AsyncOperationHandle<Texture2D>>();
        private float _navHold, _navDir, _padAxis;
        private GameObject _openWith;
        private readonly List<Button> _openWithBtns = new List<Button>();
        private int _openWithSel;
        private string _pendingOpen;

        private TexturePackProject _project;
        private static TexturePackProject _pendingProject;
        private static bool _pendingCreate;

        private bool _creating;
        private GameObject _wizardPopup;
        private InputField _wizNameField, _wizAuthorField;
        private string _wizName = "", _wizAuthor = "", _wizLocation = "";

        public static void Open(TexturePackProject project)
        {
            if (Instance != null || FullscreenPrompt.Intercept("texpack", () => Open(project))) return;
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
            if (Instance != null || FullscreenPrompt.Intercept("texpack", OpenForNewProject)) return;
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
            if (_content != null && _content.gameObject.activeSelf && !_closing)
            {
                TickThumbs();
                TickFx();
                if (_openWith != null) TickOpenWith();
                else TickNav();
            }
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
                ReleaseThumbs();
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
            ScreenBackgroundService.BuildBackdrop(brt, ScreenBackgroundService.Screen.LoadingLevel, 0.78f);
            foreach (var t in brt.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Circles" || t.name == "Pattern") t.gameObject.SetActive(false);
                else if (t.name == "Gradient" && t.GetComponent<Image>() != null) t.GetComponent<Image>().color = new Color(0.86f, 0.93f, 1f, 1f);
            }

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

            UGUIShip.CreateGradientPanel(_content, new Rect(0f, 0f, W, gradH),
                new Color(0f, 0f, 0f, 1f), new Color(0f, 0f, 0f, 0f));

            float rowH = 20f;
            float fileW = 56f;
            UGUIShip.CreateLabel(_content, new Rect(listX, 22f, 400f, 20f), "texpack.kicker", 15,
                new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            _titleTmp = Titan(_content, new Rect(listX - 4f, 38f, _listW * 0.9f - fileW - 12f, 66f));
            SetTitle(_project != null ? _project.name : "");

            UGUIShip.CreateActionDropdown(_content, new Rect(listX + _listW * 0.9f - fileW, H * 0.045f + 1f, fileW, rowH), "texpack.file",
                new List<string> { "texpack.pack", "texpack.enable", "texpack.disable", "texpack.index_names", "texpack.settings_link", "texpack.exit" },
                i =>
                {
                    if (i == 0) DoPack();
                    else if (i == 1) DoEnable();
                    else if (i == 2) DoDisable();
                    else if (i == 3) StartCoroutine(IndexGuidNamesRoutine().WrapToIl2Cpp());
                    else if (i == 4) ToggleSettings();
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

            float pvW = W * 0.29f, pvH = H * 0.514f, pvX = W * 0.855f - pvW, pvY = H * 0.233f;
            var mid = new Vector2(pvX + pvW * 0.5f, -(pvY + pvH * 0.5f));
            var glow = Decor("Glow", mid, new Vector2(1100f, 1000f));
            var glowImg = glow.gameObject.AddComponent<Image>();
            glowImg.sprite = LiveSprite("UI_Generic_Glow");
            glowImg.enabled = glowImg.sprite != null;
            glowImg.color = new Color(0.75f, 0.9f, 1f, 0.35f);
            glowImg.raycastTarget = false;
            if (_arcsA == null)
            {
                _arcsA = Arcs(768, 0.955f, 0.975f, new[] { 10f, 80f, 190f, 260f });
                _arcsB = Arcs(768, 0.90f, 0.935f, new[] { 0f, 55f, 120f, 175f, 240f, 295f });
            }
            _ringA = Ring("RingA", _arcsA, mid, 640f, 0.32f);
            _ringB = Ring("RingB", _arcsB, mid, 600f, 0.2f);

            _previewGroup = PanelGroup("Preview", new Rect(pvX, pvY, pvW, pvH), false);
            if (_checkerTex == null)
            {
                _checkerTex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                var hi = new Color32(255, 255, 255, 18);
                var lo = new Color32(255, 255, 255, 6);
                _checkerTex.SetPixels32(new[] { hi, lo, lo, hi });
                _checkerTex.filterMode = FilterMode.Point;
                _checkerTex.wrapMode = TextureWrapMode.Repeat;
                _checkerTex.Apply();
            }
            var checkGo = new GameObject("Checker");
            checkGo.transform.SetParent(_previewGroup.transform, false);
            _checker = checkGo.AddComponent<RectTransform>();
            _checker.anchorMin = _checker.anchorMax = _checker.pivot = new Vector2(0.5f, 0.5f);
            var checkImg = checkGo.AddComponent<RawImage>();
            checkImg.texture = _checkerTex;
            checkImg.raycastTarget = false;
            foreach (var corner in new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one })
                foreach (var horizontal in new[] { true, false })
                {
                    var mark = new GameObject("Mark").AddComponent<RectTransform>();
                    mark.SetParent(_checker, false);
                    mark.anchorMin = mark.anchorMax = mark.pivot = corner;
                    mark.anchoredPosition = new Vector2(corner.x == 0f ? -10f : 10f, corner.y == 0f ? -10f : 10f);
                    mark.sizeDelta = horizontal ? new Vector2(22f, 3f) : new Vector2(3f, 22f);
                    var markImg = mark.gameObject.AddComponent<Image>();
                    markImg.color = new Color(1f, 1f, 1f, 0.6f);
                    markImg.raycastTarget = false;
                }
            checkGo.SetActive(false);

            var rawGo = new GameObject("Render");
            rawGo.transform.SetParent(_previewGroup.transform, false);
            var rrt = rawGo.AddComponent<RectTransform>();
            rrt.anchorMin = rrt.anchorMax = rrt.pivot = new Vector2(0.5f, 0.5f);
            _previewImg = rawGo.AddComponent<RawImage>();
            _previewImg.color = Color.clear;
            _previewImg.raycastTarget = false;

            _settingsGroup = PanelGroup("Settings", new Rect(pvX, pvY, pvW, pvH), true);
            BuildSettings(_settingsGroup.transform, pvW, pvH);
            _settingsGroup.SetActive(false);

            _detail = new GameObject("Detail").AddComponent<RectTransform>();
            _detail.SetParent(_content, false);
            UGUIShip.SetPixelRect(_detail, new Rect(pvX, pvY + pvH + 14f, pvW, 150f));
            _detailName = UGUIShip.CreateLabel(_detail, new Rect(0f, 0f, pvW, 38f), "", 30, WHITE, TextAnchor.MiddleCenter);
            _detailName.fontStyle = FontStyle.Bold;
            _detailMeta = UGUIShip.CreateLabel(_detail, new Rect(0f, 40f, pvW, 22f), "", 15, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter);
            _detailMeta.fontStyle = FontStyle.Bold;
            _detailMeta.supportRichText = true;
            _actions = new GameObject("Actions").AddComponent<RectTransform>();
            _actions.SetParent(_detail, false);
            UGUIShip.SetPixelRect(_actions, new Rect(0f, 76f, pvW, 36f));
            _status = UGUIShip.CreateLabel(_content, new Rect(pvX, pvY + pvH + 138f, pvW, 18f), "", UIScale.FS_SM, HINT, TextAnchor.MiddleCenter);
            _detail.gameObject.SetActive(false);

            BuildPrompts();

            StartCoroutine(ScanImagesRoutine("").WrapToIl2Cpp());

            if (_creating)
            {
                contentGo.SetActive(false);
                ShowWizardNameStep();
            }
        }

        private GameObject PanelGroup(string name, Rect rect, bool panel)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_content, false);
            var rt = go.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(rt, rect);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(rect.x + rect.width * 0.5f, -(rect.y + rect.height * 0.5f));
            if (panel) UGUIShip.ApplyDeluxPanel(go.AddComponent<Image>(), 1.4f);
            return go;
        }

        private RectTransform Decor(string name, Vector2 center, Vector2 size)
        {
            var rt = new GameObject(name).AddComponent<RectTransform>();
            rt.SetParent(_content, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = center;
            rt.sizeDelta = size;
            return rt;
        }

        private RectTransform Ring(string name, Texture2D tex, Vector2 center, float size, float alpha)
        {
            var rt = Decor(name, center, new Vector2(size, size));
            var img = rt.gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.color = new Color(1f, 1f, 1f, alpha);
            img.raycastTarget = false;
            return rt;
        }

        private static Texture2D Arcs(int n, float rIn, float rOut, float[] spans)
        {
            var px = new byte[n * n * 4];
            float half = n * 0.5f, mid = (rIn + rOut) * 0.5f * half, th = (rOut - rIn) * 0.5f * half;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x + 0.5f - half, dy = y + 0.5f - half;
                float r = MathF.Sqrt(dx * dx + dy * dy);
                float ang = MathF.Atan2(dy, dx) * 57.29578f;
                if (ang < 0f) ang += 360f;
                float best = float.MaxValue;
                for (int i = 0; i + 1 < spans.Length; i += 2)
                {
                    float a0 = spans[i] / 57.29578f, a1 = spans[i + 1] / 57.29578f;
                    float d = ang >= spans[i] && ang <= spans[i + 1]
                        ? MathF.Abs(r - mid)
                        : MathF.Min(MathF.Sqrt(Sq(dx - MathF.Cos(a0) * mid) + Sq(dy - MathF.Sin(a0) * mid)),
                                    MathF.Sqrt(Sq(dx - MathF.Cos(a1) * mid) + Sq(dy - MathF.Sin(a1) * mid)));
                    best = MathF.Min(best, d);
                }
                int k = (y * n + x) * 4;
                px[k] = px[k + 1] = px[k + 2] = 255;
                px[k + 3] = (byte)(255f * Math.Clamp(th - best + 0.5f, 0f, 1f));
            }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            tex.LoadRawTextureData(px);
            tex.Apply(false, true);
            return tex;
        }

        private static float Sq(float v) => v * v;

        private TMPro.TextMeshProUGUI Titan(Transform parent, Rect rect)
        {
            if (_titanFont == null)
                foreach (var f in Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>())
                    if (f.name == "TitanOne-Expanded SDF (Title)") { _titanFont = f; break; }
            if (_titanMat == null)
                foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (m.name != "TitanOne-Expanded SDF Material - DropShadowAlpha_2_0") continue;
                    _titanMat = new Material(m) { name = "BettrFG_TexPackTitle", hideFlags = HideFlags.HideAndDontSave };
                    _titanMat.EnableKeyword("OUTLINE_ON");
                    _titanMat.SetFloat("_FaceDilate", 0.18f);
                    _titanMat.SetFloat("_OutlineWidth", 0.18f);
                    _titanMat.SetColor("_OutlineColor", new Color32(236, 96, 28, 255));
                    break;
                }
            var rt = new GameObject("Title").AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            UGUIShip.SetPixelRect(rt, rect);
            var tmp = rt.gameObject.AddComponent<TMPro.TextMeshProUGUI>();
            if (_titanFont != null) tmp.font = _titanFont;
            if (_titanMat != null) tmp.fontSharedMaterial = _titanMat;
            tmp.fontSize = 52f;
            tmp.characterSpacing = 6f;
            tmp.alignment = TMPro.TextAlignmentOptions.TopLeft;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TMPro.TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            tmp.color = WHITE;
            return tmp;
        }

        private void SetTitle(string name) => _titleTmp.text = (name ?? "").ToUpperInvariant();

        private Sprite LiveSprite(string name)
        {
            if (_liveSprites == null) BuildLiveIndex();
            return _liveSprites.TryGetValue(name, out var s) ? s : null;
        }

        private void BuildLiveIndex()
        {
            _liveSprites = new Dictionary<string, Sprite>();
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                if (s != null && !_liveSprites.ContainsKey(s.name)) _liveSprites[s.name] = s;
            _liveTextures = new Dictionary<string, Texture2D>();
            foreach (var t in Resources.FindObjectsOfTypeAll<Texture2D>())
                if (t != null && t.width > 4 && !_liveTextures.ContainsKey(t.name)) _liveTextures[t.name] = t;
        }

        private void BuildPrompts()
        {
            var bar = new GameObject("Prompts").AddComponent<RectTransform>();
            bar.SetParent(_content, false);
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(1f, 0f);
            bar.anchoredPosition = new Vector2(-40f, 26f);
            bar.sizeDelta = new Vector2(900f, 30f);
            var row = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleRight;
            row.spacing = 6f;
            row.childControlWidth = row.childControlHeight = row.childForceExpandWidth = row.childForceExpandHeight = false;

            var prompts = new (string[] glyphs, string label)[]
            {
                (new[] { "1x-pc_up", "1x-pc_down" }, "texpack.prompt_browse"),
                (new[] { "1x-pc_left", "1x-pc_right" }, "texpack.prompt_page"),
                (new[] { "1x-pc_enter" }, "texpack.prompt_open"),
                (new[] { "1x-pc_f" }, "texpack.prompt_search"),
                (new[] { "1x-pc_r" }, "texpack.prompt_restore"),
                (new[] { "1x-pc_escape" }, "texpack.prompt_close"),
            };
            foreach (var (glyphs, label) in prompts)
            {
                foreach (var g in glyphs)
                {
                    var sp = LiveSprite(g);
                    if (sp == null) continue;
                    var glyph = new GameObject(g).AddComponent<RectTransform>();
                    glyph.SetParent(bar, false);
                    glyph.sizeDelta = new Vector2(26f * sp.rect.width / Mathf.Max(1f, sp.rect.height), 26f);
                    var gi = glyph.gameObject.AddComponent<Image>();
                    gi.sprite = sp;
                    gi.raycastTarget = false;
                }
                var lbl = UGUIShip.CreateLabel(bar, new Rect(0f, 0f, 100f, 26f), label, 15, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);
                lbl.fontStyle = FontStyle.Bold;
                lbl.raycastTarget = false;
                lbl.rectTransform.sizeDelta = new Vector2(lbl.preferredWidth + 18f, 26f);
            }
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
                float a = Mathf.Clamp01((c - s - _listPadTop * 0.62f) / (_listPadTop * 0.38f));
                _rowFades[i].alpha = a * a;
                _rowFades[i].blocksRaycasts = a > 0.3f;
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
            SetTitle(_project.name);
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
            yield return StartCoroutine(UGUIShip.Squash(outGo.transform, false).WrapToIl2Cpp());
            outGo.SetActive(false);
            inGo.transform.localScale = new Vector3(1f, 0f, 1f);
            inGo.SetActive(true);
            yield return StartCoroutine(UGUIShip.Squash(inGo.transform, true).WrapToIl2Cpp());
            _flipping = false;
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
            ReleaseThumbs();
            _rowFades.Clear();
            _thumbs.Clear();
            _thumbFits.Clear();
            _subs.Clear();
            _subPrefix.Clear();
            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);
            if (_liveSprites == null) BuildLiveIndex();

            float rowH = ROW_H, y = _listPadTop;
            for (int i = 0; i < _keys.Count; i++)
            {
                int idx = i;
                string key = _keys[i];
                var btn = UGUIShip.CreateButton(_listContent, new Rect(0f, y, _listW, rowH), "",
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

                _rowFades.Add(rowGo.AddComponent<CanvasGroup>());

                var thumb = new GameObject("Thumb").AddComponent<RectTransform>();
                thumb.SetParent(rowRt, false);
                thumb.anchorMin = thumb.anchorMax = thumb.pivot = new Vector2(0f, 0.5f);
                thumb.sizeDelta = new Vector2(34f, 34f);
                thumb.anchoredPosition = new Vector2(6f, 0f);
                var thumbBg = thumb.gameObject.AddComponent<Image>();
                UGUIShip.ApplyDeluxColorFill(thumbBg);
                thumbBg.color = new Color(0f, 0f, 0f, 0.35f);
                thumbBg.raycastTarget = false;
                var pic = new GameObject("Pic").AddComponent<RectTransform>();
                pic.SetParent(thumb, false);
                pic.anchorMin = pic.anchorMax = new Vector2(0.5f, 0.5f);
                pic.sizeDelta = new Vector2(30f, 30f);
                var raw = pic.gameObject.AddComponent<RawImage>();
                raw.raycastTarget = false;
                raw.color = Color.clear;
                var fit = pic.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fit.aspectRatio = 1f;

                string kind;
                if (_liveSprites.TryGetValue(key, out var sp) && sp.texture != null)
                {
                    var r = sp.textureRect;
                    raw.texture = sp.texture;
                    raw.uvRect = new Rect(r.x / sp.texture.width, r.y / sp.texture.height, r.width / sp.texture.width, r.height / sp.texture.height);
                    raw.color = WHITE;
                    fit.aspectRatio = r.width / Mathf.Max(1f, r.height);
                    kind = LocalizationService.Format("texpack.kind_sprite_fmt", (int)r.width, (int)r.height);
                }
                else if (_liveTextures.TryGetValue(key, out var tex))
                {
                    raw.texture = tex;
                    raw.color = WHITE;
                    fit.aspectRatio = tex.width / (float)Mathf.Max(1, tex.height);
                    kind = LocalizationService.Format("texpack.kind_texture_fmt", tex.width, tex.height);
                }
                else kind = LocalizationService.Get("texpack.kind_game_file");

                var file = FindProjectFile(key);
                string path = file != null ? Path.Combine(_project.TexturesDir, file.fileName) : null;
                bool exported = path != null && File.Exists(path);
                bool edited = exported && TexturePackProject.Hash(path) != file.hash;

                var lbl = btn.GetComponentInChildren<Text>();
                UGUIShip.RelabelText(lbl, RowLabel(key) + (edited ? " <color=#FFD65A>" + LocalizationService.Get("texpack.edited_tag") + "</color>" : ""));
                lbl.supportRichText = true;
                lbl.fontStyle = FontStyle.Bold;
                lbl.alignment = TextAnchor.LowerLeft;
                lbl.verticalOverflow = VerticalWrapMode.Truncate;
                var lrt = lbl.rectTransform;
                lrt.anchorMin = new Vector2(0f, 0.5f);
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = new Vector2(50f, 0f);
                lrt.offsetMax = new Vector2(-10f, -4f);

                string prefix = exported ? LocalizationService.Get("texpack.state_exported") + "   " : "";
                var sub = UGUIShip.CreateLabel(rowRt, new Rect(50f, rowH * 0.5f + 1f, _listW - 60f, rowH * 0.5f - 1f), prefix + kind, 11,
                    exported ? EXPORTED : SUBTLE, TextAnchor.UpperLeft);
                sub.fontStyle = FontStyle.Bold;
                sub.raycastTarget = false;

                var le = rowGo.AddComponent<LayoutElement>();
                le.preferredHeight = rowH;
                rowRt.sizeDelta = new Vector2(_listW, rowH);
                _rowBodies.Add(rowRt);
                _rowCenters.Add(y + rowH * 0.5f);
                _rowVisible.Add(true);
                y += rowH + ROW_GAP;
                _rows.Add(btn);
                _thumbs.Add(raw);
                _thumbFits.Add(fit);
                _subs.Add(sub);
                _subPrefix.Add(prefix);
            }
            _listContent.sizeDelta = new Vector2(0f, y);
            _scrollVel = 0f;
            ApplyCurve();
        }

        private static Color RowColor(int i, bool selected) => selected ? ROW_PICK : (i % 2 == 0 ? BTN_DARK : ROW_ALT);

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
            if (_detail != null) _detail.gameObject.SetActive(false);
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
            float k = Mathf.Min(3f, 440f / Mathf.Max(tex.width, tex.height));
            _previewImg.rectTransform.sizeDelta = new Vector2(tex.width * k, tex.height * k);
            RefreshDetail(tex);
        }

        private void RefreshDetail(Texture tex)
        {
            if (_previewIdx < 0 || _previewIdx >= _keys.Count) return;
            string key = _keys[_previewIdx];
            UGUIShip.SetButtonColor(_rows[_previewIdx], ROW_PICK);
            UGUIShip.RelabelText(_detailName, RowLabel(key));

            var file = FindProjectFile(key) ?? FindProjectFile(_previewTexName);
            string path = file != null ? Path.Combine(_project.TexturesDir, file.fileName) : null;
            bool onDisk = path != null && File.Exists(path);
            bool edited = onDisk && TexturePackProject.Hash(path) != file.hash;
            var atlas = GetAtlasIfCropped(key);
            string state = edited ? "<color=#FFD65A>" + LocalizationService.Get("texpack.state_edited") + "</color>"
                : onDisk ? "<color=#8EE69A>" + LocalizationService.Get("texpack.state_exported") + "</color>"
                : LocalizationService.Get("texpack.state_original");
            UGUIShip.RelabelText(_detailMeta, tex.width + " x " + tex.height + "    "
                + (atlas != null ? LocalizationService.Format("texpack.from_atlas_fmt", atlas.name) + "    " : "") + state);

            for (int i = _actions.childCount - 1; i >= 0; i--) Destroy(_actions.GetChild(i).gameObject);
            var specs = new List<(string id, string icon, Color color, Action act)>();
            if (onDisk) specs.Add(("texpack.open", "Icon_Pencil", BAR_FILL, () => OpenFile(path)));
            else specs.Add(("texpack.export_and_open", "Icon_Pencil", BAR_FILL, () => ExportKey(key, false, true)));
            if (edited) specs.Add(("texpack.restore_original", "UI_IconReset_Image", BTN_DARK, () => ExportKey(key, true, false)));
            if (atlas != null)
            {
                var af = FindProjectFile(atlas.name);
                string ap = af != null ? Path.Combine(_project.TexturesDir, af.fileName) : null;
                if (ap != null && File.Exists(ap)) specs.Add(("texpack.open_atlas", "UI_TabIcon_XL", BTN_DARK, () => OpenFile(ap)));
                else specs.Add(("texpack.export_and_open_atlas", "UI_TabIcon_XL", BTN_DARK, () =>
                {
                    if (_project == null) { SetStatus("texpack.no_project"); return; }
                    StartCoroutine(ExportAtlasRoutine(atlas, false, true).WrapToIl2Cpp());
                }));
            }
            specs.Add(("texpack.settings_link", "UI_SettingsIcon_Image", BTN_DARK, ToggleSettings));

            const float gap = 8f, h = 36f;
            var widths = new float[specs.Count];
            float total = gap * (specs.Count - 1);
            for (int i = 0; i < specs.Count; i++) total += widths[i] = 42f + UGUIShip.MeasureLabel(specs[i].id, 15) + 18f;
            float x = (_actions.rect.width - total) * 0.5f;
            for (int i = 0; i < specs.Count; i++)
            {
                var spec = specs[i];
                var b = UGUIShip.CreateButton(_actions, new Rect(x, 0f, widths[i], h), spec.id, spec.color, WHITE, 15, new Action(spec.act));
                b.name = spec.id;
                var lbl = b.GetComponentInChildren<Text>();
                lbl.alignment = TextAnchor.MiddleLeft;
                lbl.horizontalOverflow = HorizontalWrapMode.Overflow;
                var lrt = lbl.rectTransform;
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = new Vector2(42f, 0f);
                lrt.offsetMax = Vector2.zero;
                var icon = LiveSprite(spec.icon);
                if (icon != null)
                {
                    var ic = new GameObject("Icon").AddComponent<RectTransform>();
                    ic.SetParent(b.transform, false);
                    ic.anchorMin = ic.anchorMax = ic.pivot = new Vector2(0f, 0.5f);
                    ic.sizeDelta = new Vector2(18f, 18f);
                    ic.anchoredPosition = new Vector2(18f, 0f);
                    var ii = ic.gameObject.AddComponent<Image>();
                    ii.sprite = icon;
                    ii.preserveAspect = true;
                    ii.raycastTarget = false;
                }
                x += widths[i] + gap;
            }
            _detail.gameObject.SetActive(true);
        }

        private void ExportKey(string key, bool overwrite, bool open)
        {
            if (_project == null) { SetStatus("texpack.no_project"); return; }
            StartCoroutine(ExportRoutine(key, overwrite, open).WrapToIl2Cpp());
        }

        private void ReleaseThumbs()
        {
            foreach (var kv in _thumbOps)
            {
                if (kv.Key < _thumbs.Count && _thumbs[kv.Key] != null) _thumbs[kv.Key].texture = null;
                Addressables.Release(kv.Value);
            }
            _thumbOps.Clear();
        }

        private void TickThumbs()
        {
            int loading = 0;
            var done = new List<int>();
            foreach (var kv in _thumbOps)
            {
                if (!kv.Value.IsDone) { loading++; continue; }
                var raw = _thumbs[kv.Key];
                if (!_rowVisible[kv.Key]) { raw.texture = null; raw.color = Color.clear; done.Add(kv.Key); continue; }
                if (raw.texture != null || raw.color.a > 0f) continue;
                if (kv.Value.Status != AsyncOperationStatus.Succeeded || kv.Value.Result == null) { raw.color = new Color(0f, 0f, 0f, 0.01f); continue; }
                var tex = kv.Value.Result;
                raw.texture = tex;
                raw.color = WHITE;
                _thumbFits[kv.Key].aspectRatio = tex.width / (float)Mathf.Max(1, tex.height);
                UGUIShip.RelabelText(_subs[kv.Key], _subPrefix[kv.Key] + LocalizationService.Format("texpack.kind_game_file_fmt", tex.width, tex.height));
            }
            foreach (int i in done) { Addressables.Release(_thumbOps[i]); _thumbOps.Remove(i); }

            for (int i = 0; i < _thumbs.Count && loading < 6; i++)
            {
                if (!_rowVisible[i] || _thumbs[i].texture != null || _thumbOps.ContainsKey(i) || !_catalogKeys.Contains(_keys[i])) continue;
                _thumbOps[i] = Addressables.LoadAssetAsync<Texture2D>(_keys[i]);
                loading++;
            }
        }

        private void TickFx()
        {
            float t = Time.unscaledTime;
            _ringA.localRotation = Quaternion.Euler(0f, 0f, t * 7f);
            _ringB.localRotation = Quaternion.Euler(0f, 0f, -t * 11f);
            bool on = _previewImg.texture != null && _previewImg.color.a > 0f && _previewGroup.activeSelf;
            if (_checker.gameObject.activeSelf != on) _checker.gameObject.SetActive(on);
            if (!on) return;
            var size = _previewImg.rectTransform.sizeDelta;
            _checker.sizeDelta = size;
            _checker.GetComponent<RawImage>().uvRect = new Rect(0f, 0f, size.x / 24f, size.y / 24f);
        }

        private void TickNav()
        {
            var kb = Rewired.ReInput.isReady ? Rewired.ReInput.controllers.Keyboard : null;
            var pad = Rewired.ReInput.isReady ? Rewired.ReInput.players.GetPlayer(0) : null;
            bool Held(KeyCode k) => kb != null && kb.GetKey(k);
            bool Pressed(KeyCode k) => kb != null && kb.GetKeyDown(k);

            bool cancel = Pressed(KeyCode.Escape) || pad != null && pad.GetButtonDown(16);
            if (_searchField.isFocused)
            {
                if (cancel) _searchField.DeactivateInputField();
                return;
            }
            if (cancel)
            {
                if (_rowMenu != null) CloseRowMenu();
                else if (_settingsGroup.activeSelf) ToggleSettings();
                else Close();
                return;
            }
            if (Pressed(KeyCode.F)) { _searchField.ActivateInputField(); _searchField.Select(); return; }

            bool prev = Pressed(KeyCode.LeftArrow) || Pressed(KeyCode.A);
            bool next = Pressed(KeyCode.RightArrow) || Pressed(KeyCode.D);
            if (pad != null)
            {
                float ax = pad.GetAxis(13);
                float dir = Mathf.Abs(ax) > 0.5f ? Mathf.Sign(ax) : 0f;
                if (dir != 0f && dir != _padAxis) { next |= dir > 0f; prev |= dir < 0f; }
                _padAxis = dir;
            }
            if (prev || next) { StepPage(next ? 1 : -1); return; }

            float v = 0f;
            if (Held(KeyCode.UpArrow) || Held(KeyCode.W)) v = 1f;
            if (Held(KeyCode.DownArrow) || Held(KeyCode.S)) v = -1f;
            if (pad != null && Mathf.Abs(pad.GetAxis(14)) > 0.5f) v = Mathf.Sign(pad.GetAxis(14));
            bool step = false;
            if (v != 0f && _rows.Count > 0)
            {
                if (_navDir == 0f) { step = true; _navHold = 0.35f; }
                else if ((_navHold -= Time.unscaledDeltaTime) <= 0f) { step = true; _navHold = 0.07f; }
            }
            _navDir = v;
            if (step)
            {
                int target = Mathf.Clamp(_previewIdx < 0 ? 0 : _previewIdx - (int)v, 0, _rows.Count - 1);
                if (target != _previewIdx) PickRow(target);
                float sc = _listContent.anchoredPosition.y, c = _rowCenters[target];
                float lo = sc + _listPadTop + 40f, hi = sc + _listH - 80f;
                if (c < lo) sc -= lo - c;
                else if (c > hi) sc += c - hi;
                _listContent.anchoredPosition = new Vector2(_listContent.anchoredPosition.x, Mathf.Clamp(sc, 0f, MaxScroll()));
                ApplyCurve();
            }

            bool submit = Pressed(KeyCode.Return) || Pressed(KeyCode.KeypadEnter) || pad != null && pad.GetButtonDown(15);
            if (submit && _detail.gameObject.activeSelf && _actions.childCount > 0)
                _actions.GetChild(0).GetComponent<Button>().onClick.Invoke();
            if (Pressed(KeyCode.R))
                _actions.Find("texpack.restore_original")?.GetComponent<Button>().onClick.Invoke();
        }

        private void OpenFile(string path)
        {
            string editor = SettingsService.Get("texpack.editor", "");
            if (string.IsNullOrEmpty(editor)) { ShowOpenWith(path); return; }
            try
            {
                if (editor == "default") System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(editor, "\"" + path + "\"") { UseShellExecute = false });
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"couldn't open {path} with {editor}: {ex.Message}"); }
        }

        private static List<(string name, string path)> PngEditors()
        {
            var list = new List<(string, string)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string exe)
            {
                if (string.IsNullOrEmpty(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
                string path = Path.IsPathRooted(exe) ? exe : null;
                if (path == null)
                    using (var k = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"Applications\" + exe + @"\shell\open\command"))
                    {
                        var cmd = k?.GetValue("") as string;
                        if (!string.IsNullOrEmpty(cmd)) path = cmd.StartsWith("\"") ? cmd.Substring(1, Math.Max(0, cmd.IndexOf('"', 1) - 1)) : cmd.Split(' ')[0];
                    }
                if (path == null)
                    using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe))
                        path = k?.GetValue("") as string;
                if (path == null && File.Exists(Path.Combine(Environment.SystemDirectory, exe))) path = Path.Combine(Environment.SystemDirectory, exe);
                path = path?.Trim('"');
                if (path == null || !File.Exists(path) || !seen.Add(path)) return;
                string name = Path.GetFileNameWithoutExtension(path);
                try
                {
                    var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
                    if (!string.IsNullOrWhiteSpace(info.FileDescription)) name = info.FileDescription.Trim();
                }
                catch { }
                list.Add((name, path));
            }
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.png\OpenWithList"))
                    if (k != null) foreach (var v in k.GetValueNames()) if (v.Length == 1) Add(k.GetValue(v) as string);
                using (var k = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@".png\OpenWithList"))
                    if (k != null) foreach (var n in k.GetSubKeyNames()) Add(n);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"png editor lookup in the registry fell over: {ex.Message}"); }
            Add("mspaint.exe");
            return list;
        }

        private void ShowOpenWith(string pending)
        {
            CloseOpenWith();
            _pendingOpen = pending;
            _openWith = new GameObject("OpenWith");
            var rt = _openWith.AddComponent<RectTransform>();
            rt.SetParent(_root, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _openWith.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var items = new List<(string label, Action act)>();
            foreach (var app in PngEditors()) { string p = app.path; items.Add((app.name, () => ChooseEditor(p))); }
            items.Add(("texpack.open_with_browse", () => WinDialogs.PickFile(LocalizationService.Get("texpack.open_with_pick"),
                path => { if (!string.IsNullOrEmpty(path)) ChooseEditor(path); }, "Programs\0*.exe\0")));
            items.Add(("texpack.open_with_default", () => ChooseEditor("default")));
            items.Add(("ui.cancel", () => { _pendingOpen = null; CloseOpenWith(); }));

            const float rowH = 40f, w = 460f;
            float h = 70f + items.Count * (rowH + 6f) + 14f;
            var panel = new GameObject("Panel").AddComponent<RectTransform>();
            panel.SetParent(rt, false);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(w, h);
            var bg = panel.gameObject.AddComponent<Image>();
            bg.sprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.general.uisprite_squarey.png", new Vector2(0.5f, 0.5f), new Vector4(46f, 46f, 46f, 46f));
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 1.5f;
            bg.color = new Color(0.04f, 0.07f, 0.12f, 0.97f);
            UGUIShip.CreateLabel(panel, new Rect(0f, 18f, w, 34f), "texpack.open_with_title", 24, WHITE, TextAnchor.MiddleCenter).fontStyle = FontStyle.Bold;

            _openWithBtns.Clear();
            for (int i = 0; i < items.Count; i++)
                _openWithBtns.Add(UGUIShip.CreateButton(panel, new Rect(24f, 66f + i * (rowH + 6f), w - 48f, rowH), items[i].label,
                    BTN_DARK, WHITE, 17, new Action(items[i].act)));
            _openWithSel = 0;
            PaintOpenWith();
        }

        private void PaintOpenWith()
        {
            for (int i = 0; i < _openWithBtns.Count; i++)
                UGUIShip.SetButtonColor(_openWithBtns[i], i == _openWithSel ? ROW_PICK : i == _openWithBtns.Count - 1 ? UGUIShip.BTN_REMOVE : BTN_DARK);
        }

        private void TickOpenWith()
        {
            var kb = Rewired.ReInput.isReady ? Rewired.ReInput.controllers.Keyboard : null;
            var pad = Rewired.ReInput.isReady ? Rewired.ReInput.players.GetPlayer(0) : null;
            bool up = kb != null && (kb.GetKeyDown(KeyCode.UpArrow) || kb.GetKeyDown(KeyCode.W)) || pad != null && pad.GetButtonDown(14);
            bool down = kb != null && (kb.GetKeyDown(KeyCode.DownArrow) || kb.GetKeyDown(KeyCode.S)) || pad != null && pad.GetNegativeButtonDown(14);
            if (up || down)
            {
                _openWithSel = (_openWithSel + (down ? 1 : -1) + _openWithBtns.Count) % _openWithBtns.Count;
                PaintOpenWith();
            }
            if (kb != null && (kb.GetKeyDown(KeyCode.Return) || kb.GetKeyDown(KeyCode.KeypadEnter)) || pad != null && pad.GetButtonDown(15))
                _openWithBtns[_openWithSel].onClick.Invoke();
            else if (kb != null && kb.GetKeyDown(KeyCode.Escape) || pad != null && pad.GetButtonDown(16))
                _openWithBtns[_openWithBtns.Count - 1].onClick.Invoke();
        }

        private void ChooseEditor(string editor)
        {
            SettingsService.Set("texpack.editor", editor);
            string pending = _pendingOpen;
            _pendingOpen = null;
            CloseOpenWith();
            if (!string.IsNullOrEmpty(pending)) OpenFile(pending);
        }

        private void CloseOpenWith()
        {
            if (_openWith != null) Destroy(_openWith);
            _openWith = null;
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
            SetTitle(_project.name);
            _content.gameObject.SetActive(true);
        }
    }
}
