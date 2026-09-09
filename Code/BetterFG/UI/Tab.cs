using System;
using System.Collections.Generic;
using BetterFG.Services;
using BetterFG.Utilities;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI
{
    // shared config for the tab-title hover background ("BG_Hover"). by default it only shows on
    // hover; with AlwaysShow on it stays visible at IdleAlpha when not hovered and full alpha when
    // hovered, and Tint recolors it in both states. each Tab drives its OWN image and
    // registers itself here so option changes broadcast to every live tab. we key on the tab (a
    // managed MonoBehaviour) not the RawImage, because using IL2Cpp Unity objects as dictionary
    // keys is unreliable across the wrapper boundary.
    public static class TabHoverStyle
    {
        public const string KEY_ALWAYS = "ui.tabhover.always";
        public const string KEY_IDLE_ALPHA = "ui.tabhover.idleAlpha";
        public const string KEY_TINT_R = "ui.tabhover.tintR";
        public const string KEY_TINT_G = "ui.tabhover.tintG";
        public const string KEY_TINT_B = "ui.tabhover.tintB";

        private static readonly List<Tab> _tabs = new List<Tab>();
        // every button-shine overlay image, so a tint change recolors them live. keeps each image's
        // own alpha (0.4 idle / 1.0 hover) — we only override RGB.
        private static readonly List<Image> _shines = new List<Image>();
        // static zebra-row colorfills (PaintStaticRowFill) — base colour varies per row, so we
        // multiply by Tint instead of overriding, keeping white Tint a no-op.
        private static readonly List<(Image img, Color baseColor)> _fills = new List<(Image, Color)>();

        public static bool AlwaysShow;
        public static float IdleAlpha = 0.15f;
        public static Color Tint = Color.white;

        private static readonly System.Globalization.CultureInfo CI = System.Globalization.CultureInfo.InvariantCulture;
        private static bool _loaded;

        // lazy — tabs can build before BetterFGUIMan.Awake runs (IL2Cpp doesn't always fire Awake
        // synchronously on AddComponent), so make sure the real values are read on first use.
        public static void EnsureLoaded()
        {
            if (_loaded) return;
            LoadFromSettings();
        }

        public static void LoadFromSettings()
        {
            _loaded = true;
            AlwaysShow = SettingsService.Get(KEY_ALWAYS, "false") == "true";
            IdleAlpha = F(KEY_IDLE_ALPHA, 0.15f);
            Tint = new Color(F(KEY_TINT_R, 1f), F(KEY_TINT_G, 1f), F(KEY_TINT_B, 1f));
        }

        private static float F(string key, float def) =>
            float.TryParse(SettingsService.Get(key, def.ToString(CI)), System.Globalization.NumberStyles.Float, CI, out float v) ? v : def;

        public static void Save()
        {
            SettingsService.Set(KEY_ALWAYS, AlwaysShow ? "true" : "false");
            SettingsService.Set(KEY_IDLE_ALPHA, IdleAlpha.ToString(CI));
            SettingsService.Set(KEY_TINT_R, Tint.r.ToString(CI));
            SettingsService.Set(KEY_TINT_G, Tint.g.ToString(CI));
            SettingsService.Set(KEY_TINT_B, Tint.b.ToString(CI));
        }

        public static void Register(Tab tab)
        {
            if (tab == null || _tabs.Contains(tab)) return;
            _tabs.Add(tab);
        }

        // called by UGUIShip.BuildShine — the shine already has its idle color set; we just tint it
        // and remember it so future tint changes recolor it live.
        public static void RegisterShine(Image shine)
        {
            if (shine == null) return;
            EnsureLoaded();
            _shines.Add(shine);
            shine.color = new Color(Tint.r, Tint.g, Tint.b, shine.color.a);
        }

        // static zebra-row colorfill — the same RGB-multiply live tint as shines get.
        public static void RegisterFill(Image fill, Color baseColor)
        {
            if (fill == null) return;
            EnsureLoaded();
            _fills.Add((fill, baseColor));
            fill.color = baseColor * Tint;
        }

        // push current style to every live tab + shine (called when a slider/toggle changes). prunes
        // entries whose GameObject got destroyed on a slot swap.
        public static void ApplyAll()
        {
            _tabs.RemoveAll(t => t == null);
            foreach (var t in _tabs) t.ApplyHoverStyle();

            _shines.RemoveAll(s => s == null);
            foreach (var s in _shines) s.color = new Color(Tint.r, Tint.g, Tint.b, s.color.a);

            _fills.RemoveAll(f => f.img == null);
            foreach (var f in _fills) f.img.color = f.baseColor * Tint;
        }
    }

    public class Tab : MonoBehaviour
    {
        public Tab(IntPtr ptr) : base(ptr) { }

        // the tab-title hover overlay, found under BG after BuildBackground runs
        private RawImage _hoverImg;
        private bool _hovered;

        // TitleBar's own transform. closed sits at y 231.0001, slides down to 226.2728 when opened
        private RectTransform _titleRt;
        private static readonly Vector3 TitleClosedPos = new Vector3(21.6729f, 228.6001f, 0f);
        private static readonly Vector3 TitleOpenedPos = new Vector3(21.6729f, 225f, 0f);
        private const float TitleReachClosed = 8f;
        private const float TitleReachOpened = -4f;

        protected static float PAD => UIScale.PAD;
        protected static float VPAD => UIScale.VPAD;
        protected static float SH => UIScale.SH;
        protected static float LH => UIScale.LH;
        protected static float BTN_H => UIScale.BTN_H;
        protected static int FS => UIScale.FS;
        protected static int FS_SM => UIScale.FS_SM;

        public virtual string TabTitle => "Tab";

        // optional localization id for the DISPLAYED title. TabTitle itself must stay stable, plain
        // English text forever — BetterFGTabRegistry uses it as the tab's exact-match identity key
        // (registration, lookup, and what gets saved as "last open tab" in last.txt). this is a
        // separate, purely cosmetic override so a title can be translated without ever touching identity.
        protected virtual string TitleId => null;

        // public read of the same override, for callers outside the Tab hierarchy (the switch-tab
        // dropdown lists every registered tab's display name without instantiating a live tab).
        public string TitleLocId => TitleId;

        public string BgResourceName => BgResource;

        public virtual Tab MakeFallbackTab() => null;

        protected virtual string TitleDisplay => TabTitle;
        private Text _titleLabel;
        protected void RefreshTitle() => UGUIShip.RelabelText(_titleLabel, TitleId ?? TitleDisplay.ToUpper());

        protected virtual float TitleYOffset => 0f;

        public float TabWidth { get; set; } = UIScale.TAB_W;
        public float TabHeight { get; set; } = UIScale.TAB_CONTENT_H;

        // optional: override the local Y position when this tab is opened
        public float? OpenedTabLocalY { get; protected set; } = null;

        public static float TITLE_H => UIScale.TITLE_H;

        public RectTransform Root { get; private set; }
        public RectTransform TitleRt => _titleRt;

        // the ContentArea GameObject (everything under the title). disabled while the tab is closed so
        // uGUI stops laying out / repainting it off-screen for nothing; the title peek stays active
        private GameObject _contentArea;
        public void SetContentActive(bool active)
        {
            if (_contentArea != null && _contentArea.activeSelf != active) _contentArea.SetActive(active);
        }

        private Vector3 _labelClosedPos;
        private bool _isOpen = false;
        public bool IsOpen
        {
            get => _isOpen;
            set
            {
                _isOpen = value;
                if (_titleRt != null)
                {
                    // grow the click zone downward via height only — writing offsetMin would also reset
                    // the horizontal stretch inset and jitter the title sideways for a frame
                    var sd = _titleRt.sizeDelta;
                    _titleRt.sizeDelta = new Vector2(sd.x, UIScale.TITLE_H + (value ? TitleReachOpened : TitleReachClosed));
                    var basePos = value ? TitleOpenedPos : TitleClosedPos;
                    _titleRt.localPosition = new Vector3(basePos.x, basePos.y + TitleYOffset, basePos.z);
                }
                if (_titleLabel != null)
                    _titleLabel.transform.localPosition = value ? new Vector3(7.8f, -3.96f, 0f) : _labelClosedPos;
            }
        }

        private bool _built = false;

        public void Initialize(RectTransform root) { Root = root; }

        public void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            BuildTab();
        }

        private void BuildTab()
        {
            BuildBackground(Root);
            // bg images are decorative — kill their raycast so they don't eat input on whatever's behind
            foreach (var img in Root.GetComponentsInChildren<Image>(true))
                if (img != null) img.raycastTarget = false;
            foreach (var raw in Root.GetComponentsInChildren<RawImage>(true))
                if (raw != null) raw.raycastTarget = false;

            TabHoverStyle.Register(this);
            ApplyHoverStyle();

            var windowGo = new GameObject("Content");
            windowGo.transform.SetParent(Root, false);
            var windowRt = windowGo.AddComponent<RectTransform>();
            windowRt.anchorMin = Vector2.zero;
            windowRt.anchorMax = Vector2.one;
            windowRt.offsetMin = windowRt.offsetMax = Vector2.zero;

            var titleGo = new GameObject("TitleBar");
            titleGo.transform.SetParent(windowGo.transform, false);
            var titleRt = titleGo.AddComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            // rect reaches lower than the visible title so the click/hover zone follows the tilted
            // text down; the label is pinned to the original top band so growing this doesn't move it
            titleRt.offsetMin = new Vector2(0f, -UIScale.TITLE_H - TitleReachClosed);
            titleRt.offsetMax = Vector2.zero;
            _titleRt = titleRt;
            titleRt.localPosition = new Vector3(TitleClosedPos.x, TitleClosedPos.y + TitleYOffset, TitleClosedPos.z);
            titleRt.localRotation = Quaternion.Euler(22f, 345f, 0f);
            titleRt.localScale = new Vector3(1.2f, 1.3f, 1.3f);

            var t = UGUIShip.CreateLabel(titleGo.transform, default, TitleId ?? TitleDisplay.ToUpper(), UIScale.FS_TITLE,
                new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);
            t.fontStyle = FontStyle.Bold;
            UGUIShip.Unstylize(t); // tab titles stay Arial
            _titleLabel = t;
            var labelRt = t.rectTransform;
            labelRt.anchorMin = new Vector2(0f, 1f);
            labelRt.anchorMax = new Vector2(1f, 1f);
            labelRt.pivot = new Vector2(0.5f, 1f);
            labelRt.sizeDelta = new Vector2(0f, UIScale.TITLE_H);
            labelRt.anchoredPosition = Vector2.zero;
            labelRt.offsetMin = new Vector2(UIScale.PAD * 3f, labelRt.offsetMin.y);
            _labelClosedPos = labelRt.localPosition;

            var hoverGo = new GameObject("HoverTint");
            hoverGo.transform.SetParent(titleGo.transform, false);
            var hoverRt = hoverGo.AddComponent<RectTransform>();
            hoverRt.anchorMin = Vector2.zero;
            hoverRt.anchorMax = Vector2.one;
            hoverRt.offsetMin = hoverRt.offsetMax = Vector2.zero;
            var hoverImg = hoverGo.AddComponent<Image>();
            hoverImg.color = Color.clear;
            hoverImg.raycastTarget = false;
            var hoverTint = hoverGo.AddComponent<TabHoverTint>();
            hoverTint.Tab = this;

            titleGo.AddComponent<Image>().color = Color.clear;
            var btn = titleGo.AddComponent<Button>();
            var cols = btn.colors;
            cols.normalColor = cols.highlightedColor = cols.pressedColor = Color.white;
            cols.colorMultiplier = 1f;
            btn.colors = cols;
            btn.transition = Selectable.Transition.None;
            var nav = btn.navigation;
            nav.mode = UnityEngine.UI.Navigation.Mode.None;
            btn.navigation = nav;
            btn.onClick.AddListener(new Action(() =>
            {
                if (BetterFGUIMan.Instance != null && BetterFGUIMan.Instance.ConsumeTitleDragSuppression(this)) return;
                OnTitleClicked();
                if (UnityEngine.EventSystems.EventSystem.current != null)
                    UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            }));

            BuildTitleExtras(titleGo.transform, t);

            var contentGo = new GameObject("ContentArea");
            contentGo.transform.SetParent(windowGo.transform, false);
            var contentRt = contentGo.AddComponent<RectTransform>();
            contentRt.anchorMin = Vector2.zero;
            contentRt.anchorMax = Vector2.one;
            contentRt.offsetMin = Vector2.zero;
            contentRt.offsetMax = new Vector2(0f, -UIScale.TITLE_H);
            contentRt.localScale = new Vector3(0.98f, 0.98f, 0.98f);

            var fillGo = new GameObject("ContentFill");
            fillGo.transform.SetParent(contentGo.transform, false);
            var fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.raycastTarget = false;
            UGUIShip.ApplyDeluxPanel(fillImg);

            BuildContent(contentRt);

            // tabs start closed/peeked — park the content inactive so it isn't repainted off-screen
            _contentArea = contentGo;
            if (!_isOpen) contentGo.SetActive(false);
        }

        protected virtual string BgResource => null;

        private static readonly Dictionary<string, Texture2D> _bgTexCache = new Dictionary<string, Texture2D>();
        private GameObject _bgHoverGo;

        private static Texture2D LoadBgTex(string resource)
        {
            if (string.IsNullOrEmpty(resource)) return null;
            if (_bgTexCache.TryGetValue(resource, out var t) && t != null) return t;
            t = EmbeddedResourceandUnity.LoadTexture(resource);
            _bgTexCache[resource] = t;
            return t;
        }

        protected virtual void BuildBackground(RectTransform root)
        {
            var bgTex = LoadBgTex(BgResource);
            if (bgTex == null) return;

            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(root, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
            bgRt.localScale = new Vector3(1.5015f, 1.3502f, 1f);
            bgRt.localPosition = new Vector3(267.7578f, 285.8921f, 0f);
            var raw = bgGo.AddComponent<RawImage>();
            raw.texture = bgTex;
            raw.raycastTarget = false;

            var hoverTex = LoadBgTex("BetterFG.assets.ui.bg_hover.png");
            if (hoverTex == null) return;

            var hoverGo = new GameObject("BG_Hover");
            hoverGo.transform.SetParent(bgGo.transform, false);
            var hoverRt = hoverGo.AddComponent<RectTransform>();
            hoverRt.anchorMin = Vector2.zero;
            hoverRt.anchorMax = Vector2.one;
            hoverRt.offsetMin = hoverRt.offsetMax = Vector2.zero;
            hoverGo.AddComponent<RawImage>().texture = hoverTex;
            hoverGo.SetActive(false);
            _bgHoverGo = hoverGo;
        }

        protected virtual void BuildTitleExtras(Transform titleBar, Text title) { }

        protected virtual void OnTitleHoverChanged(bool hovering)
        {
            if (_bgHoverGo != null) _bgHoverGo.SetActive(hovering);
        }
        // called by the UI manager when this tab is opened/closed
        public virtual void OnOpened() { }
        public virtual void OnClosed() { }
        internal void NotifyTitleHover(bool hovering)
        {
            _hovered = hovering;
            ApplyHoverStyle();
        }

        // the BG_Hover RawImage can go stale across a slot swap/rebuild (Unity fake-null), so re-find
        // it from the current hierarchy each time rather than trusting a cached ref.
        private RawImage FindHoverImg()
        {
            if (_hoverImg != null) return _hoverImg;
            if (Root == null) return null;
            foreach (var raw in Root.GetComponentsInChildren<RawImage>(true))
                if (raw != null && raw.gameObject.name == "BG_Hover") { _hoverImg = raw; break; }
            return _hoverImg;
        }

        // apply the shared hover-bg config to this tab's own overlay image, honoring current hover
        internal void ApplyHoverStyle()
        {
            var img = FindHoverImg();
            if (img == null) { OnTitleHoverChanged(_hovered); return; }
            TabHoverStyle.EnsureLoaded();
            bool visible = _hovered || TabHoverStyle.AlwaysShow;
            if (img.gameObject.activeSelf != visible) img.gameObject.SetActive(visible);
            if (!visible) return;
            float a = _hovered ? 1f : TabHoverStyle.IdleAlpha;
            var t = TabHoverStyle.Tint;
            img.color = new Color(t.r, t.g, t.b, a);
            img.SetAllDirty(); // RawImage.color alone doesn't always trigger a repaint under IL2Cpp uGUI
        }
        protected virtual void OnTitleClicked() { BetterFGUIMan.Instance?.ToggleTab(this); }
        protected virtual void BuildContent(RectTransform contentRoot) { }

        // search-as-you-type used to destroy + rebuild the whole row list on every keystroke —
        // hundreds of GameObjects torn down and remade, covers reloaded, a forced layout pass, per
        // letter. instead each tab builds its rows once and registers them here; a keystroke just
        // toggles each row's active state against the query and lets the layout groups reflow.
        private readonly List<(GameObject go, string hay)> _searchRows = new List<(GameObject, string)>();

        protected void ClearSearchRows() => _searchRows.Clear();

        protected void RegisterSearchRow(GameObject go, params string[] fields)
        {
            string hay = "";
            foreach (var f in fields)
                if (!string.IsNullOrEmpty(f)) hay += f.ToLowerInvariant() + "\n";
            _searchRows.Add((go, hay));
        }

        // toggles visibility of every registered row against the query; returns how many are shown
        protected int ApplySearchFilter(string query)
        {
            string q = (query ?? "").Trim().ToLowerInvariant();
            int shown = 0;
            foreach (var (go, hay) in _searchRows)
            {
                if (go == null) continue;
                bool show = q.Length == 0 || hay.Contains(q);
                if (go.activeSelf != show) go.SetActive(show);
                if (show) shown++;
            }
            return shown;
        }
    }
}
