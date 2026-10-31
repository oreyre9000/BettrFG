using System;
using System.Reflection;
using BetterFG.Customization.Menu;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Tab
{
    public class UITab : BetterFGTab
    {
        public UITab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "User Interface";
        protected override string BgResource => "BetterFG.assets.ui.tab.ui.png";

        internal static float subTabH => BTN_H * 0.9f;

        internal static readonly Color WHITE = Color.white;
        internal static readonly Color BTN_DARK = new Color(0.2f, 0.2f, 0.2f, 1f);
        internal static readonly Color SEL_COLOR = new Color(0.25f, 0.5f, 0.25f, 1f);
        internal static readonly Color BTN_REMOVE = new Color(0.55f, 0.15f, 0.15f, 1f);
        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private const float FG_PREVIEW_H = 120f;

        static readonly (string label, Func<BetterFGTab> make)[] Sections =
        {
            ("Foreground", () => BetterFGTabRegistry.NewTab<UITab>()),
            ("Font", () => BetterFGTabRegistry.NewTab<UIFontTab>()),
            ("Scaling", () => BetterFGTabRegistry.NewTab<UIScalingTab>()),
        };

        internal static float BuildSectionBar(BetterFGTab from, RectTransform parent, float x, float y, float w, string current)
        {
            float qGap = PAD * 0.5f;
            float btnW = (w - qGap * (Sections.Length - 1)) / Sections.Length;
            for (int i = 0; i < Sections.Length; i++)
            {
                var (label, make) = Sections[i];
                UGUIShip.CreateButton(parent, new Rect(x + (btnW + qGap) * i, y, btnW, subTabH), label,
                    label == current ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                    new Action(() => BetterFGUIMan.Instance?.SwitchSlotTab(from, make())));
            }
            y += subTabH + SH;
            UGUIShip.CreatePanel(parent, new Rect(x, y, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            return y + 1f + SH;
        }

        private static Texture2D _bgTex;
        private static Texture2D _hoverTex;

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

        internal static GameObject BuildBackdrop(RectTransform root, float extra = 0f)
        {
            var bgTex = LoadTex("BetterFG.assets.ui.tab.ui.png", ref _bgTex);
            if (bgTex == null) return null;

            GameObject hoverGo = null;
            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(root, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = new Vector2(0f, extra);
            bgRt.offsetMax = Vector2.zero;
            bgRt.localScale = new Vector3(1.5015f, 1.3502f, 1f);
            bgRt.localPosition = new Vector3(267.7578f, 285.8921f + extra, 0);
            var raw = bgGo.AddComponent<RawImage>();
            raw.texture = bgTex;
            raw.raycastTarget = false;

            if (extra > 0f)
            {
                var fillGo = new GameObject("BG_Fill");
                fillGo.transform.SetParent(bgGo.transform, false);
                var fillRt = fillGo.AddComponent<RectTransform>();
                fillRt.anchorMin = Vector2.zero;
                fillRt.anchorMax = new Vector2(1f, 0f);
                fillRt.pivot = new Vector2(0.5f, 1f);
                fillRt.sizeDelta = new Vector2(0f, extra / bgRt.localScale.y + 1f);
                fillRt.anchoredPosition = new Vector2(0f, 1f);
                var fill = fillGo.AddComponent<RawImage>();
                fill.texture = bgTex;
                fill.uvRect = new Rect(0f, 4.5f / bgTex.height, 1f, 0f);
                fill.raycastTarget = false;
            }

            var hoverTex = LoadTex("BetterFG.assets.ui.bg_hover.png", ref _hoverTex);
            if (hoverTex != null)
            {
                hoverGo = new GameObject("BG_Hover");
                hoverGo.transform.SetParent(bgGo.transform, false);
                var hoverRt = hoverGo.AddComponent<RectTransform>();
                hoverRt.anchorMin = Vector2.zero;
                hoverRt.anchorMax = Vector2.one;
                hoverRt.offsetMin = hoverRt.offsetMax = Vector2.zero;
                hoverGo.AddComponent<RawImage>().texture = hoverTex;
                hoverGo.SetActive(false);
            }
            return hoverGo;
        }

        private readonly BannerPreviewClone _fgPreview = new BannerPreviewClone();
        private Transform _fgViewport;
        private Text _fgCarouselLabel;
        private int _fgIndex;
        private GameObject _fgPreviewGo;
        private GameObject _fgEditBtnGo;
        private GameObject _fgCustomRoot;

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;
            float cy = BuildSectionBar(this, contentRoot, PAD, VPAD, w, "Foreground");

            float arrow = subTabH;
            UGUIShip.CreateButton(contentRoot, new Rect(PAD, cy, arrow, BTN_H),
                "<", BTN_DARK, WHITE, FS_SM, new Action(() => CycleForeground(-1)));
            _fgCarouselLabel = UGUIShip.CreateLabel(contentRoot, new Rect(PAD + arrow, cy, w - arrow * 2f, BTN_H),
                UIForegroundDetailTab.Label((UIForegroundKind)_fgIndex), FS_SM, WHITE, TextAnchor.MiddleCenter);
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + w - arrow, cy, arrow, BTN_H),
                ">", BTN_DARK, WHITE, FS_SM, new Action(() => CycleForeground(1)));
            cy += BTN_H + SH;

            var (scrollRect, _) = UGUIShip.CreateScrollView(contentRoot, new Rect(0f, cy, TabWidth, FG_PREVIEW_H));
            scrollRect.vertical = false;
            _fgViewport = scrollRect.transform.Find("Viewport");
            _fgPreviewGo = scrollRect.gameObject;

            _fgEditBtnGo = UGUIShip.CreateButton(contentRoot, new Rect(PAD, cy + FG_PREVIEW_H + SH, w, BTN_H),
                "Edit", BTN_DARK, WHITE, FS_SM, new Action(() => OpenForeground((UIForegroundKind)_fgIndex))).gameObject;

            float editH = TabHeight - cy - VPAD;
            _fgCustomRoot = new GameObject("FgCustomUI");
            _fgCustomRoot.transform.SetParent(contentRoot, false);
            var customRt = _fgCustomRoot.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(customRt, new Rect(0f, cy, TabWidth, editH));
            LoadSettings();
            BuildFgPanel(customRt, PAD, 0f, w, editH);

            RefreshForegroundPreview();
        }

        void CycleForeground(int d)
        {
            _fgIndex = (_fgIndex + d + 6) % 6;
            if (_fgCarouselLabel != null) _fgCarouselLabel.text = UIForegroundDetailTab.Label((UIForegroundKind)_fgIndex);
            RefreshForegroundPreview();
        }

        void RefreshForegroundPreview()
        {
            var kind = (UIForegroundKind)_fgIndex;
            bool inline = kind == UIForegroundKind.CustomUI;

            _fgPreviewGo.SetActive(!inline);
            _fgEditBtnGo.SetActive(!inline);
            _fgCustomRoot.SetActive(inline);
            if (inline) return;

            _fgPreview.Refresh(this, kind, _fgViewport,
                new Vector3(205.4f, -44.6501f, 0f), new Vector3(0.8236f, 0.8236f, 0.6f),
                () => MenuCustomizationApplication.Instance.GetBannerColoursFromSettings(ToBannerScreen(kind)));
        }

        static MenuCustomizationApplication.BannerScreen ToBannerScreen(UIForegroundKind k) => k switch
        {
            UIForegroundKind.Qualified => MenuCustomizationApplication.BannerScreen.Qualified,
            UIForegroundKind.Eliminated => MenuCustomizationApplication.BannerScreen.Eliminated,
            UIForegroundKind.EliminatedSquad => MenuCustomizationApplication.BannerScreen.Squad,
            UIForegroundKind.Winner => MenuCustomizationApplication.BannerScreen.Winner,
            UIForegroundKind.RoundOver => MenuCustomizationApplication.BannerScreen.RoundOver,
            _ => MenuCustomizationApplication.BannerScreen.Qualified,
        };

        void OpenForeground(UIForegroundKind kind)
        {
            var t = BetterFGTabRegistry.NewTab<UIForegroundDetailTab>();
            t.What = kind;
            BetterFGUIMan.Instance?.SwitchSlotTab(this, t);
        }

        private class FgSlot
        {
            public string label, key;
            public Color def, c;
            public Image swatch;
        }

        private readonly FgSlot[] _fgSlots =
        {
            new FgSlot { label = "CYAN REPLACEMENT", key = "menu.fg.cyan", def = new Color(0.196f, 0.788f, 0.922f) },
            new FgSlot { label = "BLACK REPLACEMENT", key = "menu.fg.black", def = new Color(0.133f, 0.137f, 0.149f) },
            new FgSlot { label = "YELLOW REPLACEMENT", key = "menu.fg.yellow", def = new Color(1f, 0.808f, 0f) },
            new FgSlot { label = "BLUE REPLACEMENT", key = "menu.fg.blue", def = new Color(0.353f, 0.388f, 0.678f) },
            new FgSlot { label = "PINK REPLACEMENT", key = "menu.fg.pink", def = new Color(1f, 0f, 0.647f) },
            new FgSlot { label = "ORANGE REPLACEMENT", key = "menu.fg.orange", def = new Color(1f, 0.525f, 0.039f) },
        };

        private RectTransform _fgPanelRt;
        private float _fgPanelW, _fgPanelH;
        private bool _fgDirty;
        private float _fgNextApply;

        void Update()
        {
            if (!_fgDirty || Time.unscaledTime < _fgNextApply) return;
            _fgDirty = false;
            _fgNextApply = Time.unscaledTime + 0.5f;
            OnApply();
        }

        private void BuildFgPanel(RectTransform parent, float x, float y, float w, float h)
        {
            _fgPanelRt = parent;
            _fgPanelW = w;
            _fgPanelH = h;
            var (_, content) = UGUIShip.CreateScrollView(parent, new Rect(0f, y, TabWidth, h));

            float cy = PAD;
            float swH = (LH + SH) * 4f - SH;
            float sliderW = w - swH - PAD * 2f;
            foreach (var s in _fgSlots)
            {
                var slot = s;
                UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), slot.label, FS_SM, HINT);
                cy += LH + SH;

                var swGo = new GameObject("Swatch");
                swGo.transform.SetParent(content, false);
                UGUIShip.SetPixelRect(swGo.AddComponent<RectTransform>(), new Rect(x + sliderW + PAD * 2f, cy, swH, swH));
                slot.swatch = swGo.AddComponent<Image>();
                slot.swatch.color = slot.c;
                slot.swatch.raycastTarget = false;

                UGUIShip.CreateColorControls(content, x, ref cy, sliderW,
                    () => slot.c.r, () => slot.c.g, () => slot.c.b,
                    v => slot.c.r = v, v => slot.c.g = v, v => slot.c.b = v,
                    () => { slot.swatch.color = slot.c; _fgDirty = true; }, out _, out _, out _, slot.def);

                UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
                cy += 1f + PAD;
            }

            UGUIShip.CreateButton(content, new Rect(x, cy, w, BTN_H), "Reset all to default", BTN_REMOVE, WHITE, FS_SM, new Action(ResetCustomColours));
            cy += BTN_H + PAD * 2f;
            content.sizeDelta = new Vector2(0f, cy);
        }

        private void ResetCustomColours()
        {
            foreach (var s in _fgSlots) s.c = s.def;
            OnApply();
            for (int i = _fgPanelRt.childCount - 1; i >= 0; i--)
                Destroy(_fgPanelRt.GetChild(i).gameObject);
            BuildFgPanel(_fgPanelRt, PAD, 0f, _fgPanelW, _fgPanelH);
        }

        private void OnApply()
        {
            SaveSettings();
            MenuCustomizationApplication.Instance?.RevertForeground();
            MenuCustomizationApplication.Instance?.ReapplyForegroundFromSettings();
            MenuCustomizationApplication.Instance?.ReapplyBakedPinkGreyTextures();
            MenuCustomizationApplication.Instance?.ReapplyCreativeBgLive();
            BetterFG.Features.QualificationTime.FeatureQualificationTime.ReapplyTimerColors();
        }

        private void LoadSettings()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float P(string key, float def) =>
                float.TryParse(SettingsService.Get(key, def.ToString(ci)),
                    System.Globalization.NumberStyles.Float, ci, out float v) ? v : def;

            foreach (var s in _fgSlots)
                s.c = SettingsService.Get(s.key + ".on", "false") == "true"
                    ? new Color(P(s.key + ".r", s.def.r), P(s.key + ".g", s.def.g), P(s.key + ".b", s.def.b))
                    : s.def;
        }

        private void SaveSettings()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var s in _fgSlots)
            {
                SettingsService.Set(s.key + ".on", ScreenBackgroundService.NearColor(s.c, s.def) ? "false" : "true");
                SettingsService.Set(s.key + ".r", s.c.r.ToString(ci));
                SettingsService.Set(s.key + ".g", s.c.g.ToString(ci));
                SettingsService.Set(s.key + ".b", s.c.b.ToString(ci));
            }
        }
    }
}
