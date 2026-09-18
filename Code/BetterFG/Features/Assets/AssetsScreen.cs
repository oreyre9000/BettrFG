using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Player;
using BetterFG.Customization.UI;
using BetterFG.Features.Replay;
using BetterFG.Features.TimePlacement;
using BetterFG.Services;
using static BetterFG.Services.AddressablesCatalogService;
using BetterFG.UI;
using BetterFG.Utilities;
using BettrFG.uGUI;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using LayoutElement = UnityEngine.UI.LayoutElement;

namespace BetterFG.Features.Assets
{
    public class AssetsScreen : MonoBehaviour
    {
        public AssetsScreen(IntPtr ptr) : base(ptr) { }

        public static AssetsScreen Instance;

        private const int BackdropOrder = 500;
        private const int ContentOrder = 501;

        private static readonly Color BACKDROP = Color.black;
        private static readonly Color WHITE = UGUIShip.WHITE;
        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color BTN_DARK = UGUIShip.BTN_DARK;
        private static readonly Color BTN_BLUE = new Color(0.22f, 0.34f, 0.55f, 1f);
        private static readonly Color CHECK = new Color(1f, 0.85f, 0.2f, 1f);
        private static readonly Color BAR_FILL = new Color(0.30f, 0.62f, 0.34f, 1f);
        private static readonly Color ROW_ALT = new Color(0.27f, 0.27f, 0.29f, 1f);
        private static readonly Color ROW_SEL = new Color(0.25f, 0.5f, 0.25f, 1f);
        private const float ROW_GAP = 2f;

        private Canvas _backdropCanvas, _canvas;
        private RectTransform _root, _content;

        private InputField _searchField;
        private RectTransform _listContent, _listRoot;
        private Text _status;
        private float _listPadTop, _curveCenterY, _curveR, _maxShift, _listH, _scrollVel;
        private int _selfScrollFrame = -1;
        private readonly List<RectTransform> _rowBodies = new List<RectTransform>();
        private readonly List<float> _rowCenters = new List<float>();
        private readonly List<bool> _rowVisible = new List<bool>();

        private const int PAGES = 10;
        private bool _everything, _rounds;
        private int _page;
        private Text _pageCarousel;
        private GameObject _pageRow, _barRow;
        private LoadingBar _bar;
        private readonly Button[] _modeBtns = new Button[3];
        private Button _sortBtn;
        private int _kind;
        private static readonly string[] KIND_IDS =
        {
            "assets.kind_all", "assets.kind_audio", "assets.kind_prefab",
            "assets.kind_mesh", "assets.kind_texture", "assets.kind_material",
        };
        private readonly List<string> _matches = new List<string>();
        private readonly List<string> _keys = new List<string>();

        private readonly List<ItemDefinitionSO> _results = new List<ItemDefinitionSO>();
        private readonly List<Button> _rows = new List<Button>();
        private readonly List<GameObject> _checks = new List<GameObject>();
        private readonly HashSet<string> _selected = new HashSet<string>();
        private int _previewIdx = -1;
        private bool _busy;

        private RawImage _previewImg, _stageImg;
        private RectTransform _previewRt;
        private GameObject _previewPanel, _previewHint;
        private Vector2 _statusPanelPos, _statusStagePos;
        private RenderTexture _rt;
        private Camera _cam;
        private Light[] _lights;
        private GameObject _camHost, _previewHolder;
        private readonly List<Renderer> _previewRends = new List<Renderer>();
        private readonly List<ParticleSystem> _particles = new List<ParticleSystem>();
        private float _particleClock;
        private int _diagFrames;
        private Transform _previewRoot;
        private float _yaw = 20f, _pitch = 8f, _zoom = 1.25f;
        private bool _dragging;
        private Vector3 _lastMouse;

        private static readonly Vector3 Parked = new Vector3(900f, -1000f, 0f);

        public static void Open()
        {
            if (Instance != null) return;
            var go = new GameObject("BettrFG_AssetsScreen");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<AssetsScreen>();
        }

        public static void Close()
        {
            if (Instance == null || Instance._closing) return;
            Instance._closing = true;
            Instance.StartCoroutine(Instance.CloseRoutine().WrapToIl2Cpp());
        }

        private bool _closing;

        void Start() => StartCoroutine(OpenRoutine().WrapToIl2Cpp());

        private IEnumerator OpenRoutine()
        {
            LoadingScreenService.Show();
            yield return new WaitForSecondsRealtime(0.35f);

            try
            {
                WorldHideService.HideCanvases(this);
                BetterFGUIMan.Instance?.SetVisibleForced(false);
                if (BetterFGUIMan.Instance != null) BetterFGUIMan.Instance.VisibilityLocked = true;
                Build();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"assets screen wouldn't build ({ex}), closing it instead of leaving you stuck behind the cover");
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
                StopStream();
                if (_camHost != null) Destroy(_camHost);
                if (_rt != null) { _rt.Release(); Destroy(_rt); }
                if (_backdropCanvas != null) Destroy(_backdropCanvas.gameObject);
                if (_canvas != null) Destroy(_canvas.gameObject);
            }
            catch (System.Exception ex) { Plugin.Log.LogError($"assets screen teardown threw: {ex}"); }

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
            _backdropCanvas = UGUIShip.CreateCanvas("BettrFG_AssetsBackdrop");
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

            _canvas = UGUIShip.CreateCanvas("BettrFG_AssetsScreen");
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

            float edge = W * 0.045f;
            _listW = W * 0.295f;
            _maxShift = W * 0.0425f;
            float listRight = W - edge;
            float listX = listRight - _listW;
            _curveCenterY = H * 0.55f;
            _curveR = (_curveCenterY * _curveCenterY + _maxShift * _maxShift) / (2f * _maxShift);
            float gradH = H * 0.233f;
            _listPadTop = gradH;

            var scroll = UGUIShip.CreateScrollView(_content, new Rect(listX - _maxShift, 0f, _listW + _maxShift, H));
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

            UGUIShip.CreateGradientPanel(_content, new Rect(W * 0.575f, 0f, W * 0.415f, gradH),
                new Color(0f, 0f, 0f, 1f), new Color(0f, 0f, 0f, 0f));

            float titleH = 22f, rowH = 20f, titleY = H * 0.045f;
            float fileW = 56f, selectW = 66f, optionsW = 72f, menuGap = 4f;
            float menusW = fileW + selectW + optionsW + menuGap * 2f;
            float menuX = listRight - _listW * 0.9f;
            UGUIShip.CreateActionDropdown(_content, new Rect(menuX, titleY + 1f, fileW, rowH), "assets.file",
                new List<string> { "assets.export_selected", "assets.exit" },
                i => { if (i == 0) ExportSelected(); else Close(); }, UIScale.FS_SM, rowH);
            UGUIShip.CreateActionDropdown(_content, new Rect(menuX + fileW + menuGap, titleY + 1f, selectW, rowH), "assets.select",
                new List<string> { "assets.select_all_in_search", "assets.unselect_all" },
                i => { if (i == 0) SelectAllInSearch(); else UnselectAll(); }, UIScale.FS_SM, rowH);
            UGUIShip.CreateActionDropdown(_content, new Rect(menuX + fileW + selectW + menuGap * 2f, titleY + 1f, optionsW, rowH), "assets.options",
                new List<string> { "assets.configure_project" },
                i => UnityProjectPrompt.Open(), UIScale.FS_SM, rowH);
            var title = UGUIShip.CreateLabel(_content, new Rect(menuX + menusW + 8f, titleY, listRight - menuX - menusW - 8f, titleH),
                "asset_export.tab_title", UIScale.FS_TITLE, WHITE, TextAnchor.MiddleRight);
            title.fontStyle = FontStyle.Bold;

            float modeY = H * 0.095f, modeH = H * 0.033f, searchW = W * 0.27f, searchX = listRight - searchW;
            float bw = (searchW - 8f) / 3f;
            _modeBtns[0] = UGUIShip.CreateButton(_content, new Rect(searchX, modeY, bw, modeH),
                "assets.cosmetics", BTN_BLUE, WHITE, UIScale.FS_SM, new Action(PickCosmetics));
            _modeBtns[1] = UGUIShip.CreateButton(_content, new Rect(searchX + bw + 4f, modeY, bw, modeH),
                "assets.rounds", BTN_DARK, WHITE, UIScale.FS_SM, new Action(PickRounds));
            _modeBtns[2] = UGUIShip.CreateButton(_content, new Rect(searchX + bw * 2f + 8f, modeY, bw, modeH),
                "assets.everything", BTN_DARK, WHITE, UIScale.FS_SM, new Action(PickEverything));
            HighlightMode(0);

            float searchY = modeY + modeH + 6f, searchH = modeH, searchBtnW = 52f, sortW = 62f;
            _searchField = UGUIShip.CreateInputField(_content, new Rect(searchX, searchY, searchW - searchBtnW - sortW - 8f, searchH),
                "assets.search_hint", Color.black, WHITE, UIScale.FS);
            _searchField.onEndEdit.AddListener(new Action<string>(v => Search()));
            UGUIShip.CreateButton(_content, new Rect(listRight - sortW - 4f - searchBtnW, searchY, searchBtnW, searchH),
                "assets.search", BTN_DARK, WHITE, UIScale.FS_SM, new Action(Search));
            _sortBtn = UGUIShip.CreateActionDropdown(_content, new Rect(listRight - sortW, searchY, sortW, searchH),
                "assets.sort", new List<string>(KIND_IDS), new Action<int>(PickKind), UIScale.FS_SM, searchH);

            float pageY = searchY + searchH + 6f;
            _pageRow = new GameObject("PageRow");
            _pageRow.transform.SetParent(_content, false);
            var pageRt = _pageRow.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(pageRt, new Rect(searchX, pageY, searchW, 18f));
            _pageCarousel = UGUIShip.CreateCarousel(pageRt, new Rect(0f, 0f, searchW, 18f),
                PageLabels(), 0, new Action<int>(StepPage), BTN_DARK, UIScale.FS_SM);
            _pageRow.SetActive(false);

            _bar = UGUIShip.CreateLoadingBar(_content, new Rect(searchX, pageY + 18f + 5f, searchW, 12f), BAR_FILL);
            _barRow = _bar.gameObject;
            _barRow.SetActive(false);

            float pvW = W * 0.29f, pvH = H * 0.514f, pvX = W * 0.075f, pvY = H * 0.233f;
            _previewHint = UGUIShip.CreateLabel(_content, new Rect(pvX, pvY - titleH - 8f, pvW, titleH),
                "assets.preview_hint", UIScale.FS_SM, HINT, TextAnchor.MiddleLeft).gameObject;

            var stageGo = new GameObject("Stage");
            stageGo.transform.SetParent(_content, false);
            _previewRt = stageGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(_previewRt, new Rect(0f, 0f, listX - _maxShift, H));
            _stageImg = stageGo.AddComponent<RawImage>();
            _stageImg.color = Color.clear;
            _stageImg.raycastTarget = false;
            stageGo.SetActive(false);

            _previewPanel = new GameObject("Preview");
            _previewPanel.transform.SetParent(_content, false);
            var panelRt = _previewPanel.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(panelRt, new Rect(pvX, pvY, pvW, pvH));
            UGUIShip.ApplyDeluxPanel(_previewPanel.AddComponent<Image>(), 1.4f);

            float p = 11f;
            var go = new GameObject("Render");
            go.transform.SetParent(panelRt, false);
            UGUIShip.SetPixelRect(go.AddComponent<RectTransform>(), new Rect(p, p, pvW - p * 2f, pvH - p * 2f));
            go.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);

            var rawGo = new GameObject("Image");
            rawGo.transform.SetParent(go.transform, false);
            var rrt = rawGo.AddComponent<RectTransform>();
            rrt.anchorMin = Vector2.zero;
            rrt.anchorMax = Vector2.one;
            rrt.offsetMin = rrt.offsetMax = Vector2.zero;
            _previewImg = rawGo.AddComponent<RawImage>();
            _previewImg.color = Color.clear;
            _previewImg.raycastTarget = false;

            _soundPanel = new GameObject("SoundList");
            _soundPanel.transform.SetParent(panelRt, false);
            var srt = _soundPanel.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(srt, new Rect(p, p, pvW - p * 2f, pvH - p * 2f));
            _soundW = pvW - p * 2f;

            float listH = pvH - p * 2f - TRANSPORT_H - 6f;
            var sscroll = UGUIShip.CreateScrollView(srt, new Rect(0f, 0f, _soundW, listH));
            _soundContent = sscroll.content;
            var slayout = _soundContent.gameObject.AddComponent<VerticalLayoutGroup>();
            slayout.childForceExpandWidth = true;
            slayout.childForceExpandHeight = false;
            slayout.spacing = 0f;
            _soundContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            BuildTransport(srt, _soundW, listH + 4f);
            _soundPanel.SetActive(false);

            _status = UGUIShip.CreateLabel(_content, new Rect(pvX, pvY + pvH + 8f, pvW, 16f),
                "", UIScale.FS_SM, HINT, TextAnchor.MiddleLeft);
            _statusPanelPos = _status.rectTransform.anchoredPosition;
            _statusStagePos = new Vector2(pvX, -(H - 16f - 12f));
        }

        private void ShowStage(bool on)
        {
            _previewRt.gameObject.SetActive(on);
            _previewPanel.SetActive(!on);
            _previewHint.SetActive(!on);
            _status.rectTransform.anchoredPosition = on ? _statusStagePos : _statusPanelPos;
        }

        private void BuildFallForceBackdrop(RectTransform backdrop)
        {
            var source = ScreenBackgroundService.FindPreviewSource(ScreenBackgroundService.Screen.FinalRound);
            if (source == null) { Plugin.Log.LogInfo("no final round background to clone for the assets screen, staying black"); return; }

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
                if (mat.HasProperty("_OriginOffset")) mat.SetVector("_OriginOffset", new Vector4(0.22f, 0.5f, 0f, 0f));
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

        private void OnListScrolled(Vector2 _)
        {
            if (Time.frameCount != _selfScrollFrame) _scrollVel = 0f;
            ApplyCurve();
        }

        private float MaxScroll() => Mathf.Max(0f, _listContent.sizeDelta.y - _listH);

        private void TickSmoothScroll()
        {
            if (_listContent == null) return;
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
                pos.x = _maxShift - shift;
                _rowBodies[i].anchoredPosition = pos;
            }
        }

        private static string[] PageLabels()
        {
            var labels = new string[PAGES];
            for (int i = 0; i < PAGES; i++) labels[i] = LocalizationService.Format("assets.page", i + 1, PAGES);
            return labels;
        }

        private const float TRANSPORT_H = 26f;

        private RectTransform _seek;
        private LoadingBar _seekTrack;
        private Text _nowPlaying, _timeLbl;
        private Texture2D _texPlay, _texPause;

        private void BuildTransport(RectTransform parent, float w, float y)
        {
            _texPlay = EmbeddedResourceandUnity.LoadTexture("BetterFG.assets.ui.general.icon_play.png");
            _texPause = EmbeddedResourceandUnity.LoadTexture("BetterFG.assets.ui.general.icon_pause.png");
            var texStop = EmbeddedResourceandUnity.LoadTexture("BetterFG.assets.ui.general.icon_stop.png");

            var playBtn = UGUIShip.CreateIconButton(parent, new Rect(0f, y + 4f, 18f, 18f), _texPlay,
                new Action(TogglePlay));
            _playPauseRaw = playBtn.GetComponentInChildren<RawImage>();

            UGUIShip.CreateIconButton(parent, new Rect(22f, y + 4f, 18f, 18f), texStop, new Action(StopPlay));

            var volGo = new GameObject("Volume");
            volGo.transform.SetParent(parent, false);
            UGUIShip.SetPixelRect(volGo.AddComponent<RectTransform>(), new Rect(44f, y + 5f, 16f, 16f));
            var volImg = volGo.AddComponent<RawImage>();
            volImg.texture = EmbeddedResourceandUnity.LoadTexture("BetterFG.assets.ui.general.icon_volume.png");
            volImg.raycastTarget = false;

            float timeW = 74f;
            float barX = 64f;
            _seekTrack = UGUIShip.CreateLoadingBar(parent,
                new Rect(barX, y + 8f, w - barX - timeW - 4f, 10f), BAR_FILL);

            var hit = UGUIShip.CreateButton(parent, new Rect(barX, y + 4f, w - barX - timeW - 4f, 18f), "",
                new Color(0f, 0f, 0f, 0f), WHITE, UIScale.FS_SM, new Action(SeekToPointer));
            hit.transition = Selectable.Transition.None;
            _seekHit = hit.GetComponent<RectTransform>();

            _timeLbl = UGUIShip.CreateLabel(parent, new Rect(w - timeW, y + 4f, timeW, 18f),
                "0:00 / 0:00", UIScale.FS_SM, HINT, TextAnchor.MiddleRight);

            _nowPlaying = UGUIShip.CreateLabel(parent, new Rect(0f, y - 14f, w, 14f),
                "", UIScale.FS_SM, HINT, TextAnchor.MiddleLeft);
        }

        private RawImage _playPauseRaw;
        private RectTransform _seekHit;
        private int _playingIdx = -1;

        private bool Playing => _out != null && _out.PlaybackState == NAudio.Wave.PlaybackState.Playing;

        private void StopStream()
        {
            if (_out != null) { try { _out.Stop(); _out.Dispose(); } catch { } _out = null; }
            if (_stream != null) { try { _stream.Dispose(); } catch { } _stream = null; }
        }

        private void TogglePlay()
        {
            if (_out == null)
            {
                if (_samples.Count > 0) PlaySample(_playingIdx >= 0 ? _playingIdx : 0);
                return;
            }
            if (Playing) _out.Pause();
            else _out.Play();
            RefreshPlayIcon();
        }

        private void StopPlay()
        {
            if (_stream != null) _stream.Position = 0;
            if (_out != null) _out.Stop();
            RefreshPlayIcon();
        }

        private void RefreshPlayIcon()
        {
            if (_playPauseRaw != null) _playPauseRaw.texture = Playing ? _texPause : _texPlay;
        }

        private void SeekToPointer()
        {
            if (_stream == null || _seekHit == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_seekHit, Input.mousePosition, null, out var local))
                return;
            var r = _seekHit.rect;
            float t = r.width <= 0f ? 0f : Mathf.Clamp01((local.x - r.xMin) / r.width);
            long pos = (long)(t * _stream.Length);
            _stream.Position = pos - (pos % Mathf.Max(1, _stream.WaveFormat.BlockAlign));
            if (!Playing && _out != null) _out.Play();
            RefreshPlayIcon();
        }

        private static string Clock(float t)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(t));
            return (total / 60) + ":" + (total % 60).ToString("00");
        }

        private void TickTransport()
        {
            if (_seekTrack == null) return;
            if (_stream == null)
            {
                _seekTrack.Set(0f);
                return;
            }
            float now = (float)_stream.CurrentTime.TotalSeconds;
            float total = (float)_stream.TotalTime.TotalSeconds;
            _seekTrack.Set(total > 0f ? now / total : 0f);
            if (_timeLbl != null) UGUIShip.RelabelText(_timeLbl, Clock(now) + " / " + Clock(total));
        }

        private GameObject _soundPanel;
        private RectTransform _soundContent;
        private NAudio.Wave.WaveOutEvent _out;
        private NAudio.Wave.RawSourceWaveStream _stream;
        private readonly List<FmodSample> _samples = new List<FmodSample>();
        private string _bankName = "";
        private float _listW = 400f, _soundW = 400f;

        private void ClearSounds()
        {
            StopStream();
            _playingIdx = -1;
            _bankName = "";
            _samples.Clear();
            if (_soundContent != null)
                for (int i = _soundContent.childCount - 1; i >= 0; i--)
                    Destroy(_soundContent.GetChild(i).gameObject);
            if (_soundPanel != null) _soundPanel.SetActive(false);
        }

        // FMOD Studio splits a bank in two: BNK_Foo.bytes is metadata only, the audio lives in its
        // BNK_Foo.assets.bytes twin. clicking either should land on the one with sound in it.
        private static string AudioTwin(string key)
        {
            if (key.IndexOf(".assets.", StringComparison.OrdinalIgnoreCase) >= 0) return key;
            int dot = key.LastIndexOf(".bytes", StringComparison.OrdinalIgnoreCase);
            return dot < 0 ? key : key.Substring(0, dot) + ".assets.bytes";
        }

        private IEnumerator PreviewBankRoutine(string rawKey)
        {
            string key = AudioTwin(rawKey);
            SetStatus("assets.opening_bank");
            var op = Addressables.LoadAssetAsync<TextAsset>(key);
            yield return op;
            if (op.Status != AsyncOperationStatus.Succeeded || op.Result == null)
            {
                Plugin.Log.LogWarning("bank " + key + " wouldn't load");
                SetStatus("assets.not_previewable");
                yield break;
            }

            var raw = op.Result.bytes;
            var bank = new byte[raw.Length];
            for (int i = 0; i < raw.Length; i++) bank[i] = raw[i];
            Addressables.Release(op);
            yield return null;

            string baseName = System.IO.Path.GetFileNameWithoutExtension(key);
            _bankName = baseName;
            var got = FmodBankDecode.Read(bank, baseName, out string derr);
            if (got.Count == 0)
            {
                Plugin.Log.LogWarning($"nothing decoded out of {key}: {derr}");
                SetStatus("assets.bank_empty");
                yield break;
            }

            _samples.AddRange(got);
            _soundPanel.SetActive(true);
            BuildSoundRows();
            SetStatus(LocalizationService.Format("assets.bank_sounds", got.Count));
        }

        private void BuildSoundRows()
        {
            float rowH = 22f * UIScale.S;
            for (int i = 0; i < _samples.Count; i++)
            {
                int idx = i;
                var s = _samples[i];

                var rowGo = new GameObject("Sound_" + s.Name);
                rowGo.transform.SetParent(_soundContent, false);
                rowGo.AddComponent<RectTransform>();
                var le = rowGo.AddComponent<LayoutElement>();
                le.preferredHeight = rowH;
                le.flexibleWidth = 1f;

                var rowImg = rowGo.AddComponent<Image>();
                rowImg.color = UGUIShip.ROW_CLEAR;
                var rowBtn = rowGo.AddComponent<Button>();
                rowBtn.targetGraphic = rowImg;
                rowBtn.onClick.AddListener(new Action(() => PlaySample(idx)));
                UGUIShip.PaintListRow(rowBtn, i, false);

                UGUIShip.CreateButton(rowGo.transform, new Rect(4f, (rowH - 16f) * 0.5f, 34f, 16f),
                    "assets.play", BTN_BLUE, WHITE, UIScale.FS_SM, new Action(() => PlaySample(idx)));
                UGUIShip.CreateButton(rowGo.transform, new Rect(42f, (rowH - 16f) * 0.5f, 44f, 16f),
                    "assets.save_mp3", BTN_DARK, WHITE, UIScale.FS_SM, new Action(() => SaveSample(idx)));

                float availW = Mathf.Max(80f, _soundW - 92f);
                var lbl = UGUIShip.CreateLabel(rowGo.transform, new Rect(92f, 0f, availW, rowH),
                    $"{s.Name}   {s.Seconds:0.00}s  {s.Channels}ch {s.Rate}Hz",
                    UIScale.FS_SM, WHITE, TextAnchor.MiddleLeft);
                lbl.raycastTarget = false;
                FitRow(le, lbl, 92f, availW, rowH);
            }
        }

        // playback runs entirely through NAudio in the managed domain. AudioClip.SetData was the
        // obvious route and it cannot work here: it fails internally and its own error logging throws
        // ObjectCollectedException, so the real reason never surfaces. NAudio never crosses into il2cpp.
        private void PlaySample(int idx)
        {
            var s = _samples[idx];
            StopStream();

            var fmt = s.Float
                ? NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(s.Rate, s.Channels)
                : new NAudio.Wave.WaveFormat(s.Rate, s.Bits, s.Channels);

            try
            {
                _stream = new NAudio.Wave.RawSourceWaveStream(new System.IO.MemoryStream(s.Pcm), fmt);
                _out = new NAudio.Wave.WaveOutEvent();
                _out.Init(_stream);
                _out.Play();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"{s.Name} wouldn't start: {ex.Message}");
                SetStatus("assets.bank_empty");
                StopStream();
                return;
            }

            _playingIdx = idx;
            RefreshPlayIcon();
            if (_nowPlaying != null) UGUIShip.RelabelText(_nowPlaying, s.Name);
            for (int i = 0; i < _soundContent.childCount; i++)
            {
                var b = _soundContent.GetChild(i).GetComponent<Button>();
                if (b != null) UGUIShip.PaintListRow(b, i, i == idx);
            }
            SetStatus(LocalizationService.Format("assets.playing", s.Name));
        }

        private void SaveSample(int idx)
        {
            var s = _samples[idx];
            string dir = System.IO.Path.Combine(SettingsService.ExportsDir, "Audio", _bankName);
            string outFile = FmodBankDecode.WriteMp3(s, dir, out string err);
            if (err != null) SetStatus(LocalizationService.Format("assets.mp3_failed", err));
            else SetStatus(LocalizationService.Format("assets.saved", System.IO.Path.GetFileName(outFile)));
            ReplayExport.Reveal(outFile);
        }

        private void HighlightMode(int active)
        {
            for (int i = 0; i < _modeBtns.Length; i++)
                if (_modeBtns[i] != null) UGUIShip.SetButtonColor(_modeBtns[i], i == active ? BTN_BLUE : BTN_DARK);
        }

        private void PickCosmetics()
        {
            HighlightMode(0);
            _rounds = false;
            _everything = false;
            _pageRow.SetActive(false);
            _results.Clear();
            _keys.Clear();
            _previewIdx = -1;
            RebuildRows();
            SetStatus("assets.pick_cosmetics");
        }

        private void PickRounds()
        {
            HighlightMode(1);
            _rounds = true;
            _everything = true;
            _pageRow.SetActive(false);
            _matches.Clear();
            _results.Clear();
            ListRounds(_searchField.text?.Trim() ?? "");
        }

        private void ListRounds(string filter)
        {
            _keys.Clear();
            _previewIdx = -1;
            ClearPreview();
            var scenes = SceneExport.Scenes();
            for (int i = 0; i < scenes.Count; i++)
            {
                string scene = scenes[i];
                if (filter.Length > 0
                    && SceneExport.RoundLabel(scene).IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                    && scene.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                _keys.Add(SceneExport.Prefix + scene);
            }
            RebuildRows();
            SetStatus(_keys.Count == 0
                ? LocalizationService.Get("assets.nothing_matched")
                : LocalizationService.Format("assets.rounds_found", _keys.Count));
        }

        private void PickEverything()
        {
            HighlightMode(2);
            _rounds = false;
            _everything = true;
            _pageRow.SetActive(true);
            _results.Clear();
            _previewIdx = -1;
            RebuildRows();
            SetStatus("assets.everything_hint");
        }

        private void StepPage(int dir)
        {
            if (!_everything || _matches.Count == 0) return;
            _page = (_page + dir + PAGES) % PAGES;
            UGUIShip.RelabelText(_pageCarousel, LocalizationService.Format("assets.page", _page + 1, PAGES));
            StartCoroutine(LoadPageRoutine().WrapToIl2Cpp());
        }

        private IEnumerator ScanKeysRoutine(string filter)
        {
            _barRow.SetActive(true);
            _bar.Set(0f);
            SetStatus("assets.scanning");
            yield return null;

            _matches.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (AddressablesCatalogService.Keys == null)
            {
                yield return StartCoroutine(LoadCatalogRoutine().WrapToIl2Cpp());
                if (AddressablesCatalogService.Keys == null)
                {
                    _barRow.SetActive(false);
                    SetStatus("assets.no_catalog");
                    yield break;
                }
            }

            var catalog = AddressablesCatalogService.Keys;
            for (int i = 0; i < catalog.Count; i++)
            {
                string s = catalog[i];
                if (s.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (IsBank(s) && s.IndexOf(".assets.", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!KindAllows(s)) continue;
                if (seen.Add(s)) _matches.Add(s);
                if ((i & 8191) == 0)
                {
                    _bar.Set((i + 1f) / catalog.Count);
                    yield return null;
                }
            }

            _matches.Sort(StringComparer.OrdinalIgnoreCase);
            _page = 0;
            UGUIShip.RelabelText(_pageCarousel, LocalizationService.Format("assets.page", 1, PAGES));
            Plugin.Log.LogInfo($"addressables scan for '{filter}': {_matches.Count} of {catalog.Count} catalog entries");

            yield return StartCoroutine(LoadPageRoutine().WrapToIl2Cpp());
        }

        private IEnumerator LoadCatalogRoutine()
        {
            SetStatus("assets.reading_catalog");
            yield return null;
            yield return StartCoroutine(AddressablesCatalogService.LoadRoutine().WrapToIl2Cpp());
        }

        // one slice of the match list at a time: the previous page's rows and anything it loaded go
        // first, then Unity gets a chance to actually reclaim it before the next slice is built
        private IEnumerator LoadPageRoutine()
        {
            _barRow.SetActive(true);
            _bar.Set(0f);

            _keys.Clear();
            _results.Clear();
            _previewIdx = -1;
            ClearPreview();
            RebuildRows();
            yield return null;

            Resources.UnloadUnusedAssets();
            yield return null;
            _bar.Set(0.35f);

            int pageSize = Mathf.CeilToInt(_matches.Count / (float)PAGES);
            int start = _page * pageSize;
            int end = Mathf.Min(_matches.Count, start + pageSize);

            for (int i = start; i < end; i++)
            {
                _keys.Add(_matches[i]);
                if ((_keys.Count & 255) == 0)
                {
                    _bar.Set(0.35f + 0.5f * (i - start) / Mathf.Max(1, end - start));
                    yield return null;
                }
            }

            _bar.Set(0.9f);
            yield return null;
            RebuildRows();
            _bar.Set(1f);
            yield return new WaitForSecondsRealtime(0.15f);
            _barRow.SetActive(false);

            SetStatus(_matches.Count == 0
                ? LocalizationService.Get("assets.nothing_matched")
                : LocalizationService.Format("assets.page_of", _keys.Count, _page + 1, PAGES, _matches.Count));
        }

        private void SetStatus(string msg) { if (_status != null) UGUIShip.RelabelText(_status, msg); }

        private void Search()
        {
            string filter = _searchField.text?.Trim() ?? "";
            if (_rounds) { ListRounds(filter); return; }
            if (string.IsNullOrEmpty(filter)) { SetStatus("assets.type_a_name_first"); return; }
            StartCoroutine(_everything
                ? ScanKeysRoutine(filter).WrapToIl2Cpp()
                : SearchRoutine(filter).WrapToIl2Cpp());
        }

        private IEnumerator SearchRoutine(string filter)
        {
            _results.Clear();
            _previewIdx = -1;
            RebuildRows();
            SetStatus("assets.searching");
            for (int i = 0; i < 2; i++) yield return null;

            Il2CppReferenceArray<UnityEngine.Object> raw = null;
            try { raw = Resources.FindObjectsOfTypeAll(Il2CppType.Of<CostumeOption>()); }
            catch (Exception ex) { SetStatus(LocalizationService.Format("assets.search_failed", ex.Message)); yield break; }

            for (int i = 0; i < raw.Length && _results.Count < 200; i++)
            {
                if (raw[i] == null) continue;
                CostumeOption co;
                try { co = raw[i].TryCast<CostumeOption>(); } catch { continue; }
                if (co == null) continue;
                if (SkinApplicationService.GetOptionDisplayName(co).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    _results.Add(co);
            }

            SetStatus(_results.Count == 0
                ? LocalizationService.Get("assets.nothing_matched")
                : LocalizationService.Format("assets.matches", _results.Count));
            RebuildRows();
        }

        private void RebuildRows()
        {
            _rows.Clear();
            _checks.Clear();
            _rowBodies.Clear();
            _rowCenters.Clear();
            _rowVisible.Clear();
            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);

            float rowH = 22f * UIScale.S, y = _listPadTop;
            int count = _everything ? _keys.Count : _results.Count;
            for (int i = 0; i < count; i++)
            {
                int idx = i;
                string id = _everything ? _keys[i] : _results[i].name;
                var btn = UGUIShip.CreateButton(_listContent, new Rect(0f, y, _listW, rowH), "",
                    RowColor(i, false), WHITE, UIScale.FS_SM, new Action(() => PickRow(idx)), skipHoverSound: true);
                var rowGo = btn.gameObject;
                rowGo.name = "Row_" + id;
                var rowRt = rowGo.GetComponent<RectTransform>();
                var le = rowGo.AddComponent<LayoutElement>();
                le.preferredHeight = rowH;

                var trig = rowGo.GetComponent<UnityEngine.EventSystems.EventTrigger>()
                    ?? rowGo.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                var entry = new UnityEngine.EventSystems.EventTrigger.Entry
                { eventID = UnityEngine.EventSystems.EventTriggerType.PointerClick };
                entry.callback.AddListener(new Action<UnityEngine.EventSystems.BaseEventData>(data =>
                {
                    var ped = data?.TryCast<UnityEngine.EventSystems.PointerEventData>();
                    if (ped != null && ped.button == UnityEngine.EventSystems.PointerEventData.InputButton.Right)
                        OpenRowMenu(idx);
                }));
                trig.triggers.Add(entry);

                var box = UGUIShip.CreateButton(rowGo.transform, new Rect(4f, (rowH - 14f) * 0.5f, 14f, 14f), "",
                    new Color(0f, 0f, 0f, 0.5f), WHITE, UIScale.FS_SM, new Action(() => ToggleCheck(idx)));
                var markGo = new GameObject("Tick");
                markGo.transform.SetParent(box.transform, false);
                UGUIShip.SetPixelRect(markGo.AddComponent<RectTransform>(), new Rect(3f, 3f, 8f, 8f));
                var markImg = markGo.AddComponent<Image>();
                markImg.color = CHECK;
                markImg.raycastTarget = false;
                markGo.SetActive(_selected.Contains(id));
                _checks.Add(markGo);

                float textX = 22f;
                var typeTex = _everything ? TypeIcon(id) : null;
                if (typeTex != null)
                {
                    var tGo = new GameObject("Type");
                    tGo.transform.SetParent(rowGo.transform, false);
                    UGUIShip.SetPixelRect(tGo.AddComponent<RectTransform>(), new Rect(textX, (rowH - 16f) * 0.5f, 16f, 16f));
                    var tImg = tGo.AddComponent<RawImage>();
                    tImg.texture = typeTex;
                    tImg.raycastTarget = false;
                    textX += 20f;
                }

                var iconTex = _everything
                    ? null
                    : SkinApplicationService.ResolveOptionIconTexture(SkinTexCategory.Upper, _results[i].name);
                if (iconTex != null)
                {
                    var iconGo = new GameObject("Icon");
                    iconGo.transform.SetParent(rowGo.transform, false);
                    var irt = iconGo.AddComponent<RectTransform>();
                    UGUIShip.SetPixelRect(irt, new Rect(textX, (rowH - 20f) * 0.5f, 20f, 20f));
                    var rawImg = iconGo.AddComponent<RawImage>();
                    rawImg.texture = iconTex;
                    rawImg.raycastTarget = false;
                    textX += 24f;
                }

                float availW = Mathf.Max(80f, _listW - textX - 8f);
                var lbl = btn.GetComponentInChildren<Text>();
                UGUIShip.RelabelText(lbl, _everything ? RowTitle(id) : CosmeticLabel(_results[i]));
                lbl.alignment = TextAnchor.MiddleLeft;
                UGUIShip.SetPixelRect(lbl.rectTransform, new Rect(textX, 0f, availW, rowH));
                FitRow(le, lbl, textX, availW, rowH);
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

        private void SelectAllInSearch()
        {
            if (_everything) foreach (var k in _keys) _selected.Add(k);
            else foreach (var r in _results) _selected.Add(r.name);
            for (int i = 0; i < _checks.Count; i++) _checks[i].SetActive(true);
            SetStatus(LocalizationService.Format("assets.selected_count", _selected.Count));
        }

        private void UnselectAll()
        {
            _selected.Clear();
            for (int i = 0; i < _checks.Count; i++) _checks[i].SetActive(false);
            SetStatus(LocalizationService.Format("assets.selected_count", 0));
        }

        private static bool IsMesh(string key) =>
            key.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) ||
            key.EndsWith(".obj", StringComparison.OrdinalIgnoreCase) ||
            key.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) ||
            key.EndsWith(".mesh", StringComparison.OrdinalIgnoreCase);

        private static bool IsPrefab(string key) =>
            !SceneExport.IsScene(key) &&
            (key.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
             (!IsBank(key) && !IsMesh(key) && !IsImage(key) && !IsMaterial(key) && !Guid.TryParse(key, out _)));

        private bool KindAllows(string key)
        {
            switch (_kind)
            {
                case 1: return IsBank(key);
                case 2: return IsPrefab(key);
                case 3: return IsMesh(key);
                case 4: return IsImage(key);
                case 5: return IsMaterial(key);
                default: return true;
            }
        }

        private void PickKind(int idx)
        {
            _kind = idx;
            var lbl = _sortBtn != null ? _sortBtn.GetComponentInChildren<Text>() : null;
            if (lbl != null) UGUIShip.RelabelText(lbl, KIND_IDS[idx]);
            if (_everything && _matches.Count > 0) Search();
            else SetStatus(KIND_IDS[idx]);
        }

        private static Texture2D _icAudio, _icMesh, _icPrefab, _icTexture, _icMaterial, _icScene;

        private static Texture2D Ic(ref Texture2D slot, string name)
        {
            if (slot == null) slot = EmbeddedResourceandUnity.LoadTexture("BetterFG.assets.ui.general." + name + ".png");
            return slot;
        }

        private static Texture2D TypeIcon(string key)
        {
            if (SceneExport.IsScene(key)) return Ic(ref _icScene, "icon_type_scene");
            if (IsBank(key)) return Ic(ref _icAudio, "icon_type_audio");
            if (IsPrefab(key)) return Ic(ref _icPrefab, "icon_type_prefab");
            if (IsMesh(key)) return Ic(ref _icMesh, "icon_type_mesh");
            if (IsImage(key)) return Ic(ref _icTexture, "icon_type_texture");
            if (IsMaterial(key)) return Ic(ref _icMaterial, "icon_type_material");
            return null;
        }

        private static void FitRow(LayoutElement le, Text lbl, float x, float availW, float rowH)
            => UGUIShip.FitWrappedRow(le, lbl, x, availW, rowH);

        private GameObject _rowMenu;

        private void CloseRowMenu()
        {
            if (_rowMenu != null) { Destroy(_rowMenu); _rowMenu = null; }
        }

        // a small floating menu at the cursor, same delux panel + zebra rows the top bar uses
        private void OpenRowMenu(int idx)
        {
            CloseRowMenu();

            string key = _everything ? _keys[idx] : _results[idx].name;
            var opts = new List<string>();
            if (SceneExport.IsScene(key)) opts.Add("assets.export_scene");
            else if (_everything && IsBank(key)) opts.Add("assets.export_bank");
            else if (_everything && IsImage(key)) opts.Add("assets.export_png");
            else opts.Add("assets.export_fbx");
            if (_everything && IsPrefab(key)) opts.Add("assets.export_prefab");
            if (_everything && IsMaterial(key)) opts.Add("assets.export_material");

            float rowH = 20f, w = 0f;
            for (int i = 0; i < opts.Count; i++)
                w = Mathf.Max(w, UGUIShip.MeasureLabel(opts[i], UIScale.FS_SM) + 26f);
            w = Mathf.Clamp(w, 90f, UGUIShip.ACTION_MENU_MAX_W);
            _rowMenu = new GameObject("RowMenu");
            _rowMenu.transform.SetParent(_root, false);
            var rt = _rowMenu.AddComponent<RectTransform>();

            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, Input.mousePosition, null, out var local);
            var r = _root.rect;
            UGUIShip.SetPixelRect(rt, new Rect(local.x - r.xMin, -(local.y - r.yMax), w, rowH * opts.Count + 4f));
            UGUIShip.ApplyDeluxPanelDark(_rowMenu.AddComponent<Image>(), 2f);

            for (int i = 0; i < opts.Count; i++)
            {
                int pick = i;
                var itemGo = new GameObject("Item");
                itemGo.transform.SetParent(rt, false);
                UGUIShip.SetPixelRect(itemGo.AddComponent<RectTransform>(), new Rect(2f, 2f + rowH * i, w - 4f, rowH));
                var img = itemGo.AddComponent<Image>();
                img.color = UGUIShip.ROW_CLEAR;
                var b = itemGo.AddComponent<Button>();
                b.targetGraphic = img;
                UGUIShip.PaintListRow(b, i, false);
                var itemLbl = UGUIShip.CreateLabel(itemGo.transform, new Rect(8f, 0f, w - 16f, rowH),
                    opts[pick], UIScale.FS_SM, WHITE, TextAnchor.MiddleLeft);
                itemLbl.raycastTarget = false;
                itemLbl.horizontalOverflow = HorizontalWrapMode.Overflow;
                b.onClick.AddListener(new Action(() =>
                {
                    CloseRowMenu();
                    ExportOne(idx, pick != 0);
                }));
            }
        }

        private void ExportOne(int idx, bool asPrefab)
        {
            if (_busy) return;
            string key = _everything ? _keys[idx] : _results[idx].name;
            _prefabMode = asPrefab;
            StartCoroutine(ExportRoutine(new List<string> { key }).WrapToIl2Cpp());
        }

        private bool _prefabMode;

        // a costume name on its own says nothing about which half of the bean it goes on, and the
        // list mixes both, so the slot rides along with the name
        private static string CosmeticSlot(ItemDefinitionSO option)
        {
            CostumeOption co = null;
            try { co = option.TryCast<CostumeOption>(); } catch { }
            if (co == null) return null;
            if (co.CostumeType == CostumeType.Top) return LocalizationService.Get("assets.upper");
            if (co.CostumeType == CostumeType.Bottom) return LocalizationService.Get("assets.lower");
            return null;
        }

        private static string CosmeticLabel(ItemDefinitionSO option)
        {
            string name = SkinApplicationService.GetOptionDisplayName(option);
            string slot = CosmeticSlot(option);
            return slot == null ? name : name + "   " + slot;
        }

        private static string RowTitle(string key)
        {
            if (SceneExport.IsScene(key))
            {
                string scene = SceneExport.NameOf(key);
                return SceneExport.RoundLabel(scene) ?? scene;
            }
            int at = key.IndexOf(".assets.bytes", StringComparison.OrdinalIgnoreCase);
            return at < 0 ? key : key.Substring(0, at);
        }

        private void ToggleCheck(int idx)
        {
            string name = _everything ? _keys[idx] : _results[idx].name;
            if (!_selected.Remove(name)) _selected.Add(name);
            if (idx < _checks.Count)
                _checks[idx].SetActive(_selected.Contains(name));
            SetStatus(LocalizationService.Format("assets.selected_count", _selected.Count));
        }

        private void PickRow(int idx)
        {
            CloseRowMenu();
            if (_previewIdx >= 0 && _previewIdx < _rows.Count) UGUIShip.SetButtonColor(_rows[_previewIdx], RowColor(_previewIdx, false));
            _previewIdx = idx;
            UGUIShip.SetButtonColor(_rows[idx], RowColor(idx, true));
            if (_everything) StartCoroutine(PreviewKeyRoutine(_keys[idx]).WrapToIl2Cpp());
            else StartCoroutine(PreviewRoutine(_results[idx]).WrapToIl2Cpp());
        }

        private bool _previewIsTexture, _texHandleValid, _matHandleValid, _sprHandleValid;
        private AsyncOperationHandle<Texture2D> _texHandle;
        private AsyncOperationHandle<Material> _matHandle;
        private AsyncOperationHandle<Sprite> _sprHandle;

        private void ClearPreview()
        {
            if (_texHandleValid)
            {
                _texHandleValid = false;
                try { Addressables.Release(_texHandle); } catch { }
            }
            if (_matHandleValid)
            {
                _matHandleValid = false;
                try { Addressables.Release(_matHandle); } catch { }
            }
            if (_sprHandleValid)
            {
                _sprHandleValid = false;
                try { Addressables.Release(_sprHandle); } catch { }
            }
            _previewIsTexture = false;
            _previewImg.texture = null;
            _stageImg.texture = null;
            _stageImg.color = Color.clear;
            ShowStage(false);
            ClearSounds();
            _particles.Clear();
            _particleClock = 0f;
            _previewRoot = null;
            _previewRends.Clear();
            if (_previewHolder != null) { Destroy(_previewHolder); _previewHolder = null; }
            if (_previewImg != null) _previewImg.color = Color.clear;
        }

        private IEnumerator PreviewRoutine(ItemDefinitionSO option)
        {
            ClearPreview();
            SetStatus("assets.loading_preview");

            GameObject instance = null;
            yield return StartCoroutine(Spawn(option, r => instance = r).WrapToIl2Cpp());
            if (instance == null) { SetStatus("assets.preview_failed"); yield break; }

            HoldForPreview(instance);
            SetStatus(CosmeticLabel(option));
        }

        private void HoldForPreview(GameObject instance)
        {
            _previewHolder = new GameObject("BettrFG_AssetPreview");
            _previewHolder.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(_previewHolder);
            instance.transform.SetParent(_previewHolder.transform, false);
            instance.transform.SetPositionAndRotation(Parked, Quaternion.identity);
            GameObjectHelper.SetLayerRecursively(instance, LeaderboardMugshotScene.Layer);
            foreach (var mb in instance.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null) mb.enabled = false;
            instance.SetActive(true);
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                if (t != null && !t.gameObject.activeSelf) t.gameObject.SetActive(true);
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
                if (r != null) r.enabled = true;

            _previewRoot = instance.transform;

            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var smr = r.TryCast<SkinnedMeshRenderer>();
                if (smr != null) smr.updateWhenOffscreen = true;
                _previewRends.Add(r);
            }

            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;
                var parent = ps.transform.parent;
                bool nested = false;
                while (parent != null && !nested)
                {
                    if (parent.GetComponent<ParticleSystem>() != null) nested = true;
                    parent = parent.parent;
                }
                if (!nested) _particles.Add(ps);
            }

            if (_particles.Count > 0)
            {
                _particleClock = 0f;
                for (int i = 0; i < _particles.Count; i++)
                    if (_particles[i] != null) _particles[i].Clear(true);
                Plugin.Log.LogInfo($"preview has {_particles.Count} root particle system(s)");
                _diagFrames = 3;
            }

            if (_rt == null) BuildCamera();
            ShowStage(true);
            _stageImg.texture = _rt;
            _stageImg.color = WHITE;
        }

        private static readonly PrimitiveType[] STAGE =
        {
            PrimitiveType.Sphere, PrimitiveType.Cube, PrimitiveType.Capsule, PrimitiveType.Cylinder,
        };

        // a material on its own has nothing to render, so it gets a row of primitives to sit on.
        // colliders come free with CreatePrimitive and are dead weight on a parked preview, so they go.
        private static GameObject BuildMaterialStage(Material mat)
        {
            var root = new GameObject("MaterialStage");
            root.SetActive(false);

            float step = 1.35f;
            float x = -(STAGE.Length - 1) * step * 0.5f;
            for (int i = 0; i < STAGE.Length; i++)
            {
                var prim = GameObject.CreatePrimitive(STAGE[i]);
                prim.name = STAGE[i].ToString();
                prim.transform.SetParent(root.transform, false);
                prim.transform.localPosition = new Vector3(x + i * step, 0f, 0f);

                var col = prim.GetComponent<Collider>();
                if (col != null) Destroy(col);

                var r = prim.GetComponent<MeshRenderer>();
                if (r != null) r.sharedMaterial = mat;
            }
            return root;
        }

        private void BuildCamera()
        {
            _camHost = new GameObject("BettrFG_AssetPreviewCam");
            _camHost.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(_camHost);
            float pw = _previewRt.rect.width, ph = _previewRt.rect.height;
            float aspect = pw > 0f && ph > 0f ? pw / ph : 1f;
            int rtW = aspect >= 1f ? 1280 : Mathf.Max(64, Mathf.RoundToInt(1280f * aspect));
            int rtH = aspect >= 1f ? Mathf.Max(64, Mathf.RoundToInt(1280f / aspect)) : 1280;

            _cam = LeaderboardMugshotScene.BuildCamera(_camHost, out _lights);
            _cam.aspect = aspect;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _rt = new RenderTexture(rtW, rtH, 16, RenderTextureFormat.ARGB32);
            _rt.filterMode = FilterMode.Bilinear;
            _rt.Create();
        }

        private static IEnumerator SpawnKey(string key, Action<GameObject> done)
        {
            var locOp = Addressables.LoadResourceLocationsAsync(key, Il2CppType.Of<GameObject>());
            yield return locOp;
            var locs = locOp.Result?.TryCast<Il2CppSystem.Collections.Generic.List<IResourceLocation>>();
            if (locOp.Status != AsyncOperationStatus.Succeeded || locs == null || locs.Count == 0)
            {
                Plugin.Log.LogInfo(key + " isn't a prefab, nothing to spawn");
                Addressables.Release(locOp);
                done(null);
                yield break;
            }
            Addressables.Release(locOp);

            AsyncOperationHandle<GameObject> op;
            try { op = Addressables.InstantiateAsync(key, (Transform)null, false, true); }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("couldn't instantiate " + key + ": " + ex.Message);
                done(null);
                yield break;
            }
            yield return op;
            if (op.Status == AsyncOperationStatus.Succeeded)
            {
                var go = op.Result;
                if (go != null) go.SetActive(false);
                done(go);
            }
            else done(null);
        }

        private IEnumerator PreviewKeyRoutine(string key)
        {
            ClearPreview();
            SetStatus("assets.loading_preview");

            if (IsBank(key)) { yield return StartCoroutine(PreviewBankRoutine(key).WrapToIl2Cpp()); yield break; }

            if (SceneExport.IsScene(key))
            {
                string splash = SceneExport.SplashKey(SceneExport.NameOf(key));
                AsyncOperationHandle<Sprite> splashOp = default;
                bool asked = !string.IsNullOrEmpty(splash);
                if (asked)
                {
                    try { splashOp = Addressables.LoadAssetAsync<Sprite>(splash); }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning("no splash image under " + splash + ": " + ex.Message);
                        asked = false;
                    }
                }
                if (asked)
                {
                    yield return splashOp;
                    if (splashOp.Status == AsyncOperationStatus.Succeeded && splashOp.Result != null
                        && splashOp.Result.texture != null)
                    {
                        _sprHandleValid = true;
                        _sprHandle = splashOp;
                        _previewIsTexture = true;
                        _previewImg.texture = splashOp.Result.texture;
                        _previewImg.color = WHITE;
                        SetStatus(RowTitle(key));
                        yield break;
                    }
                }
                SetStatus("assets.scene_no_preview");
                yield break;
            }

            if (IsImage(key))
            {
                var texOp = Addressables.LoadAssetAsync<Texture2D>(key);
                yield return texOp;
                if (texOp.Status == AsyncOperationStatus.Succeeded && texOp.Result != null)
                {
                    _texHandleValid = true;
                    _texHandle = texOp;
                    _previewIsTexture = true;
                    _previewImg.texture = texOp.Result;
                    _previewImg.color = WHITE;
                    SetStatus(LocalizationService.Format("assets.texture_info",
                        key, texOp.Result.width, texOp.Result.height));
                    yield break;
                }

                var sprOp = Addressables.LoadAssetAsync<Sprite>(key);
                yield return sprOp;
                if (sprOp.Status == AsyncOperationStatus.Succeeded && sprOp.Result != null
                    && sprOp.Result.texture != null)
                {
                    _sprHandleValid = true;
                    _sprHandle = sprOp;
                    _previewIsTexture = true;
                    _previewImg.texture = sprOp.Result.texture;
                    _previewImg.color = WHITE;
                    SetStatus(LocalizationService.Format("assets.texture_info",
                        key, sprOp.Result.texture.width, sprOp.Result.texture.height));
                    yield break;
                }
                Plugin.Log.LogWarning("neither texture nor sprite loaded for " + key);
                if (IsCubemap(key)) { SetStatus("assets.cubemap_hint"); yield break; }
            }

            if (IsMaterial(key))
            {
                var matOp = Addressables.LoadAssetAsync<Material>(key);
                yield return matOp;
                if (matOp.Status != AsyncOperationStatus.Succeeded || matOp.Result == null)
                {
                    Plugin.Log.LogWarning("material load failed for " + key);
                    SetStatus("assets.not_previewable");
                    yield break;
                }

                _matHandleValid = true;
                _matHandle = matOp;
                HoldForPreview(BuildMaterialStage(matOp.Result));
                SetStatus(LocalizationService.Format("assets.material_info",
                    key, matOp.Result.shader != null ? matOp.Result.shader.name : "?"));
                yield break;
            }

            GameObject instance = null;
            yield return StartCoroutine(SpawnKey(key, r => instance = r).WrapToIl2Cpp());
            if (instance == null)
            {
                Plugin.Log.LogWarning("nothing previewable came back for " + key);
                SetStatus("assets.not_previewable");
                yield break;
            }

            HoldForPreview(instance);
            SetStatus(key);
        }

        private static IEnumerator Spawn(ItemDefinitionSO option, Action<GameObject> done)
        {
            CostumeOption co = null;
            try { co = option.TryCast<CostumeOption>(); } catch { }
            if (co == null) { done(null); yield break; }

            var op = co.costumePrefabReference.InstantiateAsync();
            yield return op;
            if (op.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
            {
                var go = op.Result;
                if (go != null) go.SetActive(false);
                done(go);
            }
            else done(null);
        }

        private void ExportSelected()
        {
            if (_busy) return;
            if (_selected.Count == 0) { SetStatus("assets.nothing_selected"); return; }
            StartCoroutine(ExportRoutine(new List<string>(_selected)).WrapToIl2Cpp());
        }

        // a bank or a texture isn't a model, so it skips the mesh pipeline entirely: the raw bytes go
        // out as a .bank foobar2000/vgmstream can open, and a texture goes out as a png
        private IEnumerator ExportRawRoutine(string key, Action<string> done)
        {
            string safe = System.IO.Path.GetFileNameWithoutExtension(key);
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');

            if (IsCubemap(key))
            {
                string cubeDir = System.IO.Path.Combine(SettingsService.ExportsDir, "Textures", safe);
                System.IO.Directory.CreateDirectory(cubeDir);

                yield return StartCoroutine(BundleReaderPrompt.Ensure().WrapToIl2Cpp());
                System.Diagnostics.Process cubeProc;
                string cubeErr;
                try { cubeProc = FbxExport.StartPrefabExport(cubeDir, key, out cubeErr); }
                catch (Exception ex)
                {
                    Plugin.Log.LogError("cubemap export of " + key + " fell over: " + ex);
                    done(null);
                    yield break;
                }
                if (cubeProc == null) { SetStatus(cubeErr); done(null); yield break; }

                int cubePoll = 0;
                while (!cubeProc.HasExited)
                {
                    if (cubePoll++ % 30 == 0)
                    {
                        string line = FbxExport.Progress();
                        if (!string.IsNullOrEmpty(line)) SetStatus(line);
                    }
                    yield return null;
                }

                string faceDir = System.IO.Path.Combine(cubeDir, "Textures");
                var faces = System.IO.Directory.Exists(faceDir)
                    ? System.IO.Directory.GetFiles(faceDir, "*.png")
                    : new string[0];
                if (faces.Length == 0) { SetStatus("assets.cubemap_failed"); done(null); yield break; }
                Plugin.Log.LogInfo($"{safe}: {faces.Length} face(s) out of the bundles");
                done(faces[0]);
                yield break;
            }

            if (IsBank(key))
            {
                var op = Addressables.LoadAssetAsync<TextAsset>(AudioTwin(key));
                yield return op;
                if (op.Status != AsyncOperationStatus.Succeeded || op.Result == null)
                {
                    Plugin.Log.LogWarning("bank " + key + " wouldn't load");
                    done(null);
                    yield break;
                }

                string dir = System.IO.Path.Combine(SettingsService.ExportsDir, "Audio", safe);
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, safe + ".bank");

                var bytes = op.Result.bytes;
                var managed = new byte[bytes.Length];
                for (int i = 0; i < bytes.Length; i++) managed[i] = bytes[i];
                System.IO.File.WriteAllBytes(path, managed);
                Addressables.Release(op);
                Plugin.Log.LogInfo($"{safe}.bank out, {managed.Length / 1024} KB");

                var got = FmodBankDecode.Read(managed, safe, out string derr);
                if (got.Count == 0) Plugin.Log.LogWarning($"{safe}: bank saved but nothing decoded out of it, {derr}");
                string last = path;
                foreach (var sample in got)
                {
                    string outFile = FmodBankDecode.WriteMp3(sample, dir, out _);
                    if (outFile != null) last = outFile;
                }
                done(last);
                yield break;
            }

            var texOp = Addressables.LoadAssetAsync<Texture2D>(key);
            yield return texOp;
            if (texOp.Status != AsyncOperationStatus.Succeeded || texOp.Result == null)
            {
                Plugin.Log.LogWarning("texture " + key + " wouldn't load");
                done(null);
                yield break;
            }

            string texDir = System.IO.Path.Combine(SettingsService.ExportsDir, "Textures");
            System.IO.Directory.CreateDirectory(texDir);
            string texPath = System.IO.Path.Combine(texDir, safe + ".png");
            bool ok = SkinApplicationService.SaveTexturePng(texOp.Result, texPath, out string terr);
            Addressables.Release(texOp);

            if (!ok) { Plugin.Log.LogWarning("texture " + key + " didn't come out: " + terr); done(null); yield break; }
            done(texPath);
        }

        // an addressable address is not the asset's name (PB_SpinningHammer_0 -> PB_SpinningHammer_MASTER)
        // and the sidecar can only find objects by name, so the address is resolved here first
        private static IEnumerator ResolveAssetName(string key, bool material, Action<string> done)
        {
            AsyncOperationHandle op;
            try
            {
                op = material
                    ? Addressables.LoadAssetAsync<Material>(key)
                    : (AsyncOperationHandle)Addressables.LoadAssetAsync<GameObject>(key);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("couldn't load " + key + ": " + ex.Message);
                done(null);
                yield break;
            }
            yield return op;
            string name = null;
            if (op.Status == AsyncOperationStatus.Succeeded)
            {
                var obj = op.Result == null ? null : op.Result.TryCast<UnityEngine.Object>();
                if (obj != null) name = obj.name;
            }
            Addressables.Release(op);
            done(name);
        }

        private IEnumerator ExportPrefabRoutine(string key, Action<string> done)
        {
            string leaf = System.IO.Path.GetFileNameWithoutExtension(key);
            if (leaf == key)
            {
                string real = null;
                yield return StartCoroutine(
                    ResolveAssetName(key, IsMaterial(key), r => real = r).WrapToIl2Cpp());
                if (string.IsNullOrEmpty(real)) { SetStatus("assets.prefab_failed"); done(null); yield break; }
                leaf = real;
                key = real;
            }
            if (!UnityProject.Configured)
            {
                bool configured = false;
                bool waiting = true;
                UnityProjectPrompt.Open(new Action<bool>(v => { configured = v; waiting = false; }));
                while (waiting) yield return null;
                if (!configured) { done(null); yield break; }
            }

            string bucket = IsMaterial(key) ? "Materials" : "Prefabs";
            string dir = UnityProject.Configured
                ? System.IO.Path.Combine(UnityProject.AssetsPath, FbxExport.Safe(leaf))
                : System.IO.Path.Combine(SettingsService.ExportsDir, "Models", bucket, FbxExport.Safe(leaf));
            System.IO.Directory.CreateDirectory(dir);

            yield return StartCoroutine(BundleReaderPrompt.Ensure().WrapToIl2Cpp());
            System.Diagnostics.Process proc;
            string err;
            try { proc = FbxExport.StartPrefabExport(dir, key, out err); }
            catch (Exception ex)
            {
                Plugin.Log.LogError("prefab export of " + key + " fell over: " + ex);
                done(null);
                yield break;
            }
            if (proc == null) { SetStatus(err); done(null); yield break; }

            int poll = 0;
            while (!proc.HasExited)
            {
                if (poll++ % 30 == 0)
                {
                    string line = FbxExport.Progress();
                    if (!string.IsNullOrEmpty(line)) SetStatus(line);
                }
                yield return null;
            }

            if (IsMaterial(key))
            {
                var mats = System.IO.Directory.GetFiles(System.IO.Path.Combine(dir, "Materials"), "*.mat");
                if (mats.Length == 0) { SetStatus("assets.prefab_failed"); done(null); yield break; }
                Plugin.Log.LogInfo("material out: " + mats[0]);
                done(mats[0]);
                yield break;
            }

            string outFile = System.IO.Path.Combine(dir, FbxExport.Safe(leaf) + ".prefab");
            if (!System.IO.File.Exists(outFile)) { SetStatus("assets.prefab_failed"); done(null); yield break; }
            Plugin.Log.LogInfo("prefab out: " + outFile);
            done(outFile);
        }

        private IEnumerator ExportSceneRoutine(string key, Action<string> done)
        {
            string sceneName = SceneExport.NameOf(key);

            if (!UnityProject.Configured)
            {
                bool configured = false;
                bool waiting = true;
                UnityProjectPrompt.Open(new Action<bool>(v => { configured = v; waiting = false; }));
                while (waiting) yield return null;
                if (!configured) { done(null); yield break; }
            }

            string dir = System.IO.Path.Combine(UnityProject.AssetsPath, FbxExport.Safe(sceneName));
            System.IO.Directory.CreateDirectory(dir);

            SetStatus("assets.scene_reading");
            yield return null;

            yield return StartCoroutine(BundleReaderPrompt.Ensure().WrapToIl2Cpp());
            System.Diagnostics.Process proc;
            string err;
            try { proc = FbxExport.StartSceneExport(dir, sceneName, out err); }
            catch (Exception ex)
            {
                Plugin.Log.LogError("scene export of " + sceneName + " fell over: " + ex);
                done(null);
                yield break;
            }
            if (proc == null) { SetStatus(err); done(null); yield break; }

            int poll = 0;
            while (!proc.HasExited)
            {
                if (poll++ % 30 == 0)
                {
                    string line = FbxExport.Progress();
                    if (!string.IsNullOrEmpty(line)) SetStatus(line);
                }
                yield return null;
            }

            string outFile = System.IO.Path.Combine(dir, FbxExport.Safe(sceneName) + ".unity");
            if (!System.IO.File.Exists(outFile)) { SetStatus("assets.scene_failed"); done(null); yield break; }
            Plugin.Log.LogInfo("scene out: " + outFile);
            done(outFile);
        }

        private IEnumerator ExportRoutine(List<string> names)
        {
            _busy = true;
            string lastFile = null;
            int done = 0;

            foreach (var name in names)
            {
                ItemDefinitionSO option = null;
                foreach (var r in _results)
                    if (r.name == name) { option = r; break; }
                if (option == null && !_keys.Contains(name)) continue;

                SetStatus(LocalizationService.Format("assets.exporting", name, done + 1, names.Count));

                if (SceneExport.IsScene(name))
                {
                    string written = null;
                    yield return StartCoroutine(ExportSceneRoutine(name, r => written = r).WrapToIl2Cpp());
                    if (written != null) { lastFile = written; done++; }
                    continue;
                }

                if (option == null && _prefabMode && (IsPrefab(name) || IsMaterial(name)))
                {
                    string written = null;
                    yield return StartCoroutine(ExportPrefabRoutine(name, r => written = r).WrapToIl2Cpp());
                    if (written != null) { lastFile = written; done++; }
                    continue;
                }

                if (option == null && (IsBank(name) || IsImage(name)))
                {
                    string written = null;
                    yield return StartCoroutine(ExportRawRoutine(name, r => written = r).WrapToIl2Cpp());
                    if (written != null) { lastFile = written; done++; }
                    continue;
                }

                GameObject instance = null;
                yield return StartCoroutine(option != null
                    ? Spawn(option, r => instance = r).WrapToIl2Cpp()
                    : SpawnKey(name, r => instance = r).WrapToIl2Cpp());
                if (instance == null) continue;

                yield return StartCoroutine(BundleReaderPrompt.Ensure().WrapToIl2Cpp());
                string dir = null;
                System.Diagnostics.Process fetch = null;
                string err = null;
                try
                {
                    dir = FbxExport.PrepareDir(name);
                    FbxExport.WriteMeshList(instance, dir);
                    fetch = FbxExport.StartMeshFetch(dir, out err);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError("asset export of " + name + " fell over: " + ex);
                    Destroy(instance);
                    continue;
                }
                if (fetch == null) { SetStatus(err); Destroy(instance); _busy = false; yield break; }

                int poll = 0;
                while (!fetch.HasExited)
                {
                    if (poll++ % 30 == 0)
                    {
                        string line = FbxExport.Progress();
                        if (!string.IsNullOrEmpty(line)) SetStatus(line);
                    }
                    yield return null;
                }

                try
                {
                    string written = FbxExport.Build(instance, dir, name, out string result);
                    if (written != null) { lastFile = written; done++; }
                    SetStatus(result);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError("fbx build of " + name + " died: " + ex);
                }
                Destroy(instance);
            }

            _busy = false;
            _prefabMode = false;
            SetStatus(LocalizationService.Format("assets.export_done", done, names.Count));
            if (lastFile != null) ReplayExport.Reveal(lastFile);
        }

        private float _tickTimer;

        void Update()
        {
            if (_closing) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (_canvas == null) return;

            TickSmoothScroll();
            _tickTimer += Time.deltaTime;
            if (_tickTimer >= 0.1f) { _tickTimer = 0f; WinDialogs.Tick(); }

            if (_rowMenu != null && Input.GetMouseButtonDown(0)
                && !RectTransformUtility.RectangleContainsScreenPoint(
                    _rowMenu.GetComponent<RectTransform>(), Input.mousePosition, null))
                CloseRowMenu();

            Orbit();
            RenderPreview();
            TickTransport();
        }

        private void Orbit()
        {
            if (_previewRt == null || !BetterFGUIMan.UnityMouseReady()) return;
            bool over = RectTransformUtility.RectangleContainsScreenPoint(_previewRt, Input.mousePosition, null);

            if (Input.GetMouseButtonDown(0) && over) { _dragging = true; _lastMouse = Input.mousePosition; }
            if (!Input.GetMouseButton(0)) _dragging = false;

            if (_dragging)
            {
                var delta = Input.mousePosition - _lastMouse;
                _lastMouse = Input.mousePosition;
                _yaw += delta.x * 0.4f;
                _pitch = Mathf.Clamp(_pitch + delta.y * 0.3f, -80f, 80f);
            }

            if (over)
            {
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f) _zoom = Mathf.Clamp(_zoom - wheel * 0.12f, 0.35f, 4f);
            }
        }

        private void RenderPreview()
        {
            if (_previewIsTexture) return;
            if (_rt == null || _previewRends.Count == 0) return;

            if (_particles.Count > 0)
            {
                float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0.001f, 0.1f);
                _particleClock += dt;
                bool loop = _particleClock > 5f;
                if (loop) _particleClock = 0f;
                for (int i = 0; i < _particles.Count; i++)
                {
                    if (_particles[i] == null) continue;
                    if (loop) _particles[i].Clear(true);
                    _particles[i].Simulate(dt, true, false, false);
                }
            }

            var body = _previewRoot != null
                ? new Bounds(_previewRoot.position, Vector3.zero)
                : _previewRends[0].bounds;
            for (int i = 0; i < _previewRends.Count; i++)
            {
                if (_previewRends[i] == null) continue;
                var rb = _previewRends[i].bounds;
                if (rb.size.sqrMagnitude < 1e-6f) continue;
                if (_previewRoot != null && (rb.center - _previewRoot.position).sqrMagnitude > 10000f) continue;
                body.Encapsulate(rb);
            }

            if (_diagFrames > 0)
            {
                _diagFrames--;
                int live = 0;
                for (int i = 0; i < _particles.Count; i++)
                    if (_particles[i] != null) live += _particles[i].particleCount;
                Plugin.Log.LogInfo($"particle diag: {live} alive, bounds c={body.center} s={body.size}, {_previewRends.Count} renderer(s)");
                for (int i = 0; i < _previewRends.Count && i < 4; i++)
                {
                    var r = _previewRends[i];
                    if (r == null) continue;
                    var mat = r.sharedMaterial;
                    Plugin.Log.LogInfo($"   [{i}] {r.gameObject.name} layer={r.gameObject.layer} on={r.enabled}"
                        + $" b.c={r.bounds.center} b.s={r.bounds.size}"
                        + $" mat={(mat != null ? mat.name : "none")} shader={(mat != null && mat.shader != null ? mat.shader.name : "none")}");
                }
            }

            // an emitter with nothing alive yet reports a zero-size box, and framing turns that into
            // an absurd zoom. floor it so the stage stays readable while it warms up.
            if (body.size.magnitude < 0.5f) body = new Bounds(body.center, Vector3.one * 2f);

            var dir = Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.forward;
            LeaderboardMugshotScene.FrameBody(_cam, body, dir, _zoom);

            var prev = RenderTexture.active;
            _cam.targetTexture = _rt;
            LeaderboardMugshotScene.PushLighting(_lights);
            _cam.Render();
            LeaderboardMugshotScene.PopLighting(_lights);
            _cam.targetTexture = null;
            RenderTexture.active = prev;
        }
    }
}
