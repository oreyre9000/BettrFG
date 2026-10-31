using System;
using System.Collections;
using System.Reflection;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Windows
{
    public class BetterFGWindow : MonoBehaviour
    {
        public BetterFGWindow(IntPtr ptr) : base(ptr) { }

        protected const float S = 1.2f;
        protected const float PAD = 8f * S;
        protected const float TITLE_H = 24f * S;
        protected const int FS_TITLE = (int)(11f * S);
        protected const int FS_BODY = (int)(11f * S);
        protected const int FS_SM = (int)(10f * S);

        protected static readonly Color WHITE = UGUIShip.WHITE;
        protected static readonly Color HINT = new Color(1f, 1f, 1f, 0.4f);
        protected static readonly Color PANEL_BG = new Color(0f, 0f, 0f, 0.4f);
        protected static readonly Color BTN_DARK = new Color(0.18f, 0.18f, 0.18f, 1f);
        protected static readonly Color SEL_COL = new Color(0.25f, 0.5f, 0.25f, 1f);
        protected static readonly Color TRANSP = Color.clear;

        protected virtual float WindowWidth => 300f;
        protected virtual float WindowHeight => 200f;
        protected virtual string WindowTitle => "ui.window";
        protected virtual string BgResourceName => "";
        protected virtual string BgHoverResourceName => "BetterFG.assets.ui.windows.generalbg_hover.png";

        protected virtual bool ShowTitleHover => false;
        protected virtual bool DraggableFromTitle => false;

        // Subclasses override to set BG transform before BuildBackground runs
        protected virtual Vector3 InitialBgPosition => new Vector3(192.2909f, 28.3637f, 0f);
        protected virtual Vector3 InitialBgScale => new Vector3(1.2931f, 4.5265f, 1f);
        // hover overlay is a child of BG so it inherits BgPosition/BgScale already; this is only for
        // windows whose hover art needs a further nudge to line up with its own bg image.
        protected virtual Vector3 BgHoverLocalPosition => Vector3.zero;

        public Vector3 BgPosition = new Vector3(192.2909f, 28.3637f, 0f);
        public Vector3 BgScale = new Vector3(1.2931f, 4.5265f, 1f);
        public Vector3 TitlePosition = new Vector3(32.5674f, -12f, 0f);
        public Vector3 TitleScale = new Vector3(1.1818f, 1.3491f, 1f);
        public Vector3 ContentPosition = new Vector3(190.6421f, -11.8564f, 0f);
        public Vector3 ContentScale = new Vector3(1.0473f, 1f, 1f);
        // per-window override for the stretched content rect's offsets. null = use the defaults
        // (full fill minus TITLE_H at the top). populate from BuildContent to retune the area.
        public Vector2? ContentOffsetMin = null;
        public Vector2? ContentOffsetMax = null;
        public Vector2 WindowPosition = Vector2.zero;
        public Vector2 Pivot = new Vector2(0f, 0.5f);

        private RectTransform _rootRt;
        private RectTransform _bgRt;
        protected RectTransform _titleLabelRt;
        private RectTransform _clipRt;
        protected Text _titleText;
        protected RectTransform _contentRt;
        private Transform _titleRoot;
        private RawImage _hoverImage;
        private CanvasGroup _canvasGroup;
        // alpha 0 still leaves the canvas in the render + layout + raycast set, so every closed window
        // kept submitting its whole tree each frame. the canvas goes off with the window instead.
        private Canvas _canvas;

        // Set by whoever spawns the window so we can hide when the tab closes
        public BetterFG.UI.Tab OwnerTab { get; set; }

        // ── API ───────────────────────────────────────────────────────────────

        // every window lives on its own hidden, scene-persistent GameObject
        public static T Spawn<T>() where T : BetterFGWindow
        {
            var go = new GameObject("BetterFG_" + typeof(T).Name);
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            return go.AddComponent<T>();
        }

        public void SetRotation(float zDeg)
        {
            if (_rootRt != null) _rootRt.localRotation = Quaternion.Euler(0f, 0f, zDeg);
        }

        public void SetAnchorPosition(Vector2 pos)
        {
            WindowPosition = pos;
            if (_rootRt != null) _rootRt.anchoredPosition = pos;
        }

        public void SetScaleX(float sx)
        {
            if (_rootRt != null) _rootRt.localScale = new Vector3(sx, 1f, 1f);
        }

        private static BetterFGWindow _activeWindow;

        public void HideWindow()
        {
            if (_activeWindow == this) _activeWindow = null;
            if (_canvas != null) _canvas.enabled = false;
            if (_canvasGroup == null) return;
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;
        }

        public bool IsVisible => _canvasGroup != null && _canvasGroup.alpha > 0f;

        public void ShowWindow()
        {
            if (_activeWindow != null && _activeWindow != this) _activeWindow.HideWindow();
            _activeWindow = this;
            if (_rootRt != null) UiTransition.Run(_rootRt, ShowWindowNow);
            else ShowWindowNow();
        }

        private void ShowWindowNow()
        {
            if (_canvas != null) _canvas.enabled = true;
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.blocksRaycasts = true;
                _canvasGroup.interactable = true;
            }
            SetScaleX(1f);
        }

        protected void RebuildContent()
        {
            if (_contentRt == null) return;
            var parent = _contentRt.parent?.GetComponent<RectTransform>();
            if (parent != null)
            {
                for (int i = parent.childCount - 1; i >= 0; i--)
                {
                    var c = parent.GetChild(i);
                    if (c != null && (c.name == "HandToggle" || c.name == "ResetBtn"))
                        UnityEngine.Object.Destroy(c.gameObject);
                }
            }
            for (int i = _contentRt.childCount - 1; i >= 0; i--)
            {
                var child = _contentRt.GetChild(i);
                if (child == null) continue;
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
            BuildContent(_contentRt);
            _layoutApplied = false;
        }

        protected void RebuildTitleExtras()
        {
            if (_titleRoot == null) return;
            for (int i = _titleRoot.childCount - 1; i >= 0; i--)
            {
                var child = _titleRoot.GetChild(i);
                if (child.name == "HandToggle" || child.name == "ResetBtn")
                    UnityEngine.Object.Destroy(child.gameObject);
            }
            BuildTitleExtras(_titleRoot);
            _layoutApplied = false;
        }

        public void Show()
        {
            gameObject.SetActive(true);
            if (_canvas != null) _canvas.enabled = true;
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        void Awake()
        {
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            Build();
        }

        void OnDestroy()
        {
            if (_activeWindow == this) _activeWindow = null;
        }

        void Update()
        {
            // visibility here has always meant "alpha > 0", and subclasses reach that state through
            // several routes. rather than chase every one, keep the canvas in step with it — a closed
            // window then costs nothing to render, batch or raycast.
            if (_canvas != null && _canvasGroup != null)
            {
                bool want = _canvasGroup.alpha > 0f;
                if (_canvas.enabled != want) _canvas.enabled = want;
            }
            ManagedUpdate();
        }

        protected virtual void ManagedUpdate()
        {
            ApplyRuntimeLayout();

            if (OwnerTab != null && !OwnerTab.IsOpen)
                HideWindow();
        }

        // last-applied copies so the per-frame pass only hits the RectTransforms (interop calls that
        // dirty the canvas) when a value actually changed, instead of re-writing constants every frame
        private bool _layoutApplied;
        private Vector2 _apPivot;
        private Vector3 _apBgPos, _apBgScale, _apTitlePos, _apTitleScale, _apContentPos, _apContentScale;
        private Vector2? _apOffMin, _apOffMax;

        protected virtual void ApplyTitleTransform()
        {
            if (_titleLabelRt == null) return;
            _titleLabelRt.localPosition = TitlePosition;
            _titleLabelRt.localScale = TitleScale;
        }

        protected virtual void ApplyContentTransform()
        {
            if (_contentRt == null) return;
            _contentRt.localPosition = ContentPosition;
            _contentRt.localScale = ContentScale;
            if (ContentOffsetMin.HasValue) _contentRt.offsetMin = ContentOffsetMin.Value;
            if (ContentOffsetMax.HasValue) _contentRt.offsetMax = ContentOffsetMax.Value;
        }

        protected virtual void BuildRootGraphic(GameObject rootGo)
            => rootGo.AddComponent<Image>().color = TRANSP;

        private void SyncContentClip()
        {
            if (_clipRt == null) return;
            float sx = Mathf.Approximately(BgScale.x, 0f) ? 1f : BgScale.x;
            float sy = Mathf.Approximately(BgScale.y, 0f) ? 1f : BgScale.y;
            _clipRt.pivot = Pivot;
            _clipRt.localScale = new Vector3(1f / sx, 1f / sy, 1f);
            _clipRt.localPosition = new Vector3(-BgPosition.x / sx, -BgPosition.y / sy, 0f);
        }

        private void ApplyRuntimeLayout()
        {
            if (_layoutApplied && _apPivot == Pivot && _apBgPos == BgPosition && _apBgScale == BgScale
                && _apTitlePos == TitlePosition && _apTitleScale == TitleScale
                && _apContentPos == ContentPosition && _apContentScale == ContentScale
                && _apOffMin == ContentOffsetMin && _apOffMax == ContentOffsetMax)
                return;

            if (_rootRt != null)
                _rootRt.pivot = Pivot;
            if (_bgRt != null) { _bgRt.localPosition = BgPosition; _bgRt.localScale = BgScale; }
            SyncContentClip();
            ApplyTitleTransform();
            ApplyContentTransform();

            _layoutApplied = true;
            _apPivot = Pivot;
            _apBgPos = BgPosition; _apBgScale = BgScale;
            _apTitlePos = TitlePosition; _apTitleScale = TitleScale;
            _apContentPos = ContentPosition; _apContentScale = ContentScale;
            _apOffMin = ContentOffsetMin; _apOffMax = ContentOffsetMax;
        }

        // ── Build ─────────────────────────────────────────────────────────────

        private void Build()
        {
            // Seed from virtual props BEFORE BuildBackground runs
            BgPosition = InitialBgPosition;
            BgScale = InitialBgScale;

            var canvasGo = new GameObject(WindowTitle + "_Canvas");
            canvasGo.hideFlags = HideFlags.HideAndDontSave;
            canvasGo.transform.SetParent(transform, false);

            var canvas = _canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 997;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            BetterFG.Services.UIScaleService.Register(canvas);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            _canvasGroup = canvasGo.AddComponent<CanvasGroup>();

            var rootGo = new GameObject("Window");
            rootGo.transform.SetParent(canvasGo.transform, false);
            _rootRt = rootGo.AddComponent<RectTransform>();
            _rootRt.anchorMin = new Vector2(0f, 0.5f);
            _rootRt.anchorMax = new Vector2(0f, 0.5f);
            _rootRt.pivot = Pivot;
            _rootRt.sizeDelta = new Vector2(WindowWidth, WindowHeight);
            _rootRt.anchoredPosition = WindowPosition;
            _rootRt.localScale = new Vector3(0f, 1f, 1f); // hidden until ShowWindow()

            BuildRootGraphic(rootGo);

            BuildBackground(rootGo.transform);
            BuildTitleBar(rootGo.transform);

            Transform contentParent = rootGo.transform;
            if (_bgRt != null)
            {
                _bgRt.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                var clipGo = new GameObject("ContentClip");
                clipGo.transform.SetParent(_bgRt, false);
                _clipRt = clipGo.AddComponent<RectTransform>();
                _clipRt.anchorMin = Vector2.zero;
                _clipRt.anchorMax = Vector2.one;
                _clipRt.offsetMin = _clipRt.offsetMax = Vector2.zero;
                SyncContentClip();
                contentParent = clipGo.transform;
            }

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(contentParent, false);
            _contentRt = contentGo.AddComponent<RectTransform>();
            _contentRt.anchorMin = Vector2.zero;
            _contentRt.anchorMax = Vector2.one;
            _contentRt.offsetMin = new Vector2(0f, 0f);
            _contentRt.offsetMax = new Vector2(0f, -TITLE_H);
            _contentRt.localPosition = ContentPosition;
            _contentRt.localScale = ContentScale;

            BuildContent(_contentRt);
        }

        private void BuildBackground(Transform parent)
        {
            if (string.IsNullOrEmpty(BgResourceName)) return;
            var tex = LoadEmbedded(BgResourceName);
            if (tex == null) return;
            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(parent, false);
            _bgRt = bgGo.AddComponent<RectTransform>();
            _bgRt.anchorMin = Vector2.zero;
            _bgRt.anchorMax = Vector2.one;
            _bgRt.offsetMin = _bgRt.offsetMax = Vector2.zero;
            _bgRt.localPosition = BgPosition;
            _bgRt.localScale = BgScale;
            bgGo.AddComponent<RawImage>().texture = tex;

            if (DraggableFromTitle)
            {
                var hoverTex = LoadEmbedded(BgHoverResourceName);
                if (hoverTex != null)
                {
                    var hoverGo = new GameObject("BGHover");
                    hoverGo.transform.SetParent(bgGo.transform, false);
                    var hoverRt = hoverGo.AddComponent<RectTransform>();
                    hoverRt.anchorMin = Vector2.zero;
                    hoverRt.anchorMax = Vector2.one;
                    hoverRt.offsetMin = hoverRt.offsetMax = Vector2.zero;
                    hoverRt.localPosition = BgHoverLocalPosition;
                    hoverRt.localScale = Vector3.one;
                    _hoverImage = hoverGo.AddComponent<RawImage>();
                    _hoverImage.texture = hoverTex;
                    _hoverImage.color = Color.clear;
                }
            }
        }

        private void BuildTitleBar(Transform parent)
        {
            var titleGo = new GameObject("TitleBar");
            titleGo.transform.SetParent(parent, false);
            _titleRoot = titleGo.transform;
            var titleRt = titleGo.AddComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.offsetMin = new Vector2(0f, -TITLE_H);
            titleRt.offsetMax = Vector2.zero;
            var titleImg = titleGo.AddComponent<Image>();
            titleImg.color = TRANSP;
            titleImg.raycastTarget = false;

            if (DraggableFromTitle)
            {
                var dragGo = new GameObject("DragArea");
                dragGo.transform.SetParent(titleGo.transform, false);
                var dragRt = dragGo.AddComponent<RectTransform>();
                dragRt.anchorMin = Vector2.zero;
                dragRt.anchorMax = Vector2.one;
                dragRt.offsetMin = Vector2.zero;
                dragRt.offsetMax = Vector2.zero;
                dragGo.AddComponent<Image>().color = TRANSP;
                dragGo.transform.position += new Vector3(0, 14, 0);
                dragGo.transform.localScale = new Vector3(1, 1.5f, 1);
                dragGo.AddComponent<WindowDragHandle>().Init(_rootRt, _hoverImage);
            }

            var t = UGUIShip.CreateLabel(titleGo.transform, default, WindowTitle, FS_TITLE,
                new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);
            t.fontStyle = FontStyle.Bold;
            _titleText = t;
            _titleLabelRt = t.rectTransform;
            _titleLabelRt.anchorMin = Vector2.zero;
            _titleLabelRt.anchorMax = Vector2.one;
            // CreateLabel leaves pivot at (0,1); every window's TitlePosition was tuned against the
            // old centered pivot, so restore it or all titles drift down-right
            _titleLabelRt.pivot = new Vector2(0.5f, 0.5f);
            _titleLabelRt.offsetMin = new Vector2(PAD, 0f);
            _titleLabelRt.offsetMax = Vector2.zero;
            ApplyTitleTransform();

            BuildTitleExtras(titleGo.transform);
        }

        protected virtual void BuildContent(RectTransform contentRoot) { }

        protected virtual bool ShowCloseButton => false;

        public virtual void Close() => Destroy(gameObject);

        protected virtual void BuildTitleExtras(Transform titleRoot)
        {
            if (!ShowCloseButton) return;
            var go = new GameObject("CloseBtn");
            go.transform.SetParent(titleRoot, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(30f, 28f);
            rt.anchoredPosition = new Vector2(24f, 18f);
            UGUIShip.CreateButton(go.transform, new Rect(0f, 0f, 30f, 28f),
                "✕", new Color(0.5f, 0.22f, 0.22f, 1f), WHITE, FS_SM, new Action(Close));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        protected static Texture2D LoadEmbedded(string resourceName)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);
                tex.wrapMode = TextureWrapMode.Clamp;
                return tex;
            }
            catch (Exception ex) { Plugin.Log.LogError($"Window: {resourceName}: {ex.Message}"); return null; }
        }

        protected static Text MakeLabel(Transform parent, Rect rect, string text, int fs, Color color,
            TextAnchor anchor = TextAnchor.MiddleLeft)
            => UGUIShip.CreateLabel(parent, rect, text, fs, color, anchor);

        protected static void MakeSeparator(Transform parent, Rect rect)
        {
            var go = new GameObject("Sep");
            go.transform.SetParent(parent, false);
            UGUIShip.SetPixelRect(go.AddComponent<RectTransform>(), rect);
            go.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);
        }
    }

    // ── Drag handle component ─────────────────────────────────────────────────

    public class WindowDragHandle : MonoBehaviour
    {
        public WindowDragHandle(IntPtr ptr) : base(ptr) { }

        private RectTransform _target;
        private RectTransform _selfRt;
        private RectTransform _canvasRt;
        private RawImage _hoverImage;
        private bool _dragging;
        private Vector2 _offset;
        private Color _hoverColour = new Color(-1f, -1f, -1f, -1f);
        private static WindowDragHandle _activeDrag;

        private static readonly Color HOVER_ON = new Color(1f, 1f, 1f, 0.08f);

        public void Init(RectTransform target, RawImage hoverImage = null)
        {
            _target = target;
            _selfRt = GetComponent<RectTransform>();
            _hoverImage = hoverImage;
        }

        void Start()
        {
            var p = _target?.parent;
            while (p != null)
            {
                if (p.GetComponent<Canvas>() != null)
                {
                    _canvasRt = p.GetComponent<RectTransform>();
                    break;
                }
                p = p.parent;
            }
        }

        void Update()
        {
            if (_selfRt == null || _target == null || _canvasRt == null) return;
            var mouse = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
            bool over = RectTransformUtility.RectangleContainsScreenPoint(_selfRt, mouse, null);

            if (_hoverImage != null)
            {
                var t = TabHoverStyle.Tint;
                var want = (over || _dragging) ? new Color(t.r, t.g, t.b, HOVER_ON.a) : Color.clear;
                if (_hoverColour != want) { _hoverImage.color = want; _hoverColour = want; }
            }

            if (Input.GetMouseButtonDown(0) && over && (_activeDrag == null || _activeDrag == this))
            {
                _dragging = true;
                _activeDrag = this;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRt, mouse, null, out var lp);
                _offset = _target.anchoredPosition - lp;
            }

            if (Input.GetMouseButtonUp(0))
            {
                _dragging = false;
                if (_activeDrag == this) _activeDrag = null;
            }

            if (_dragging && _activeDrag == this)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRt, mouse, null, out var lp);
                _target.anchoredPosition = lp + _offset;
            }
        }

        private void OnDestroy()
        {
            if (_activeDrag == this) _activeDrag = null;
        }
    }
}
