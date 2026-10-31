using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public partial class UITab : Tab
    {
        public UITab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "User Interface";
        protected override string TitleId => "ui.user_interface_2";
        protected override string BgResource => "BetterFG.assets.ui.tab.ui.png";

        internal static float subTabH => BTN_H * 0.9f;

        internal static readonly Color WHITE = UGUIShip.WHITE;
        internal static readonly Color BTN_DARK = UGUIShip.BTN_DARK;
        internal static readonly Color SEL_COLOR = new Color(0.25f, 0.5f, 0.25f, 1f);
        internal static readonly Color BTN_APPLY = new Color(0.45f, 0.35f, 0.25f, 1f);
        internal static readonly Color BTN_REMOVE = UGUIShip.BTN_REMOVE;

        static readonly (string label, Func<Tab> make)[] Sections =
        {
            ("ui.foreground", () => BetterFGTabRegistry.NewTab<UITab>()),
            ("font.mod_ui_font", () => BetterFGTabRegistry.NewTab<UIFontTab>()),
            ("ui_tabs.scaling", () => BetterFGTabRegistry.NewTab<UIScalingTab>()),
        };

        // shared subtab bar (Foreground/Font/Background/Scaling) drawn at the top of every UI section
        // tab; each button switches the slot to that section's own tab. `current` is highlighted.
        internal static float BuildSectionBar(Tab from, RectTransform parent, float x, float y, float w, string current)
        {
            float qGap = PAD * 0.5f;
            float btnW = (w - qGap * (Sections.Length - 1)) / Sections.Length;

            // empty rect spanning the bar, so the first-open help prompt has something to point at
            var boundsGo = new GameObject("SectionBarBounds");
            boundsGo.transform.SetParent(parent, false);
            UGUIShip.SetPixelRect(boundsGo.AddComponent<RectTransform>(), new Rect(x, y, w, subTabH));
            SectionBarRect = boundsGo.GetComponent<RectTransform>();
            SectionButtonRects = new RectTransform[Sections.Length];

            for (int i = 0; i < Sections.Length; i++)
            {
                var (label, make) = Sections[i];
                SectionButtonRects[i] = UGUIShip.CreateButton(parent,
                    new Rect(x + (btnW + qGap) * i, y, btnW, subTabH), label,
                    label == current ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                    new Action(() => BetterFGUIMan.Instance?.SwitchSlotTab(from, make())))
                    .GetComponent<RectTransform>();
            }
            y += subTabH + SH;
            UGUIShip.CreatePanel(parent, new Rect(x, y, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            return y + 1f + SH;
        }

        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private const float FG_PREVIEW_H = 120f;

        // the live instance + rects the first-open help prompt drives and points at
        internal static UITab Live { get; private set; }
        internal static RectTransform SectionBarRect { get; private set; }
        internal static RectTransform[] SectionButtonRects { get; private set; }
        internal static RectTransform CarouselRect { get; private set; }
        internal static RectTransform PreviewRect { get; private set; }
        internal static RectTransform EditRect { get; private set; }

        private readonly BannerPreviewClone _fgPreview = new BannerPreviewClone();
        private Transform _fgViewport;
        private Text _fgCarouselLabel;
        private int _fgIndex;
        private GameObject _fgPreviewGo;
        private GameObject _fgEditBtnGo;
        private GameObject _fgEditRoot;
        private GameObject _fgCustomRoot;
        private GameObject _fgNametagRoot;

        private static bool IsInline(UIForegroundKind k) =>
            k == UIForegroundKind.CustomUI || k == UIForegroundKind.Nametag;

        protected override void BuildContent(RectTransform contentRoot)
        {
            Live = this;
            float w = TabWidth - PAD * 2f;
            float cy = BuildSectionBar(this, contentRoot, PAD, VPAD, w, "ui.foreground");

            // ── carousel header: ‹  Qualified banner  › ──
            float arrow = subTabH;
            UGUIShip.CreateButton(contentRoot, new Rect(PAD, cy, arrow, BTN_H),
                "<", BTN_DARK, WHITE, FS_SM, new Action(() => CycleForeground(-1)));
            _fgCarouselLabel = UGUIShip.CreateLabel(contentRoot, new Rect(PAD + arrow, cy, w - arrow * 2f, BTN_H),
                UIForegroundDetailTab.Label((UIForegroundKind)_fgIndex), FS_SM, WHITE, TextAnchor.MiddleCenter);
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + w - arrow, cy, arrow, BTN_H),
                ">", BTN_DARK, WHITE, FS_SM, new Action(() => CycleForeground(1)));
            CarouselRect = _fgCarouselLabel.rectTransform;
            cy += BTN_H + SH;

            var (scrollRect, _) = UGUIShip.CreateScrollView(contentRoot, new Rect(0f, cy, TabWidth, FG_PREVIEW_H));
            scrollRect.vertical = false;
            _fgViewport = scrollRect.transform.Find("Viewport");
            PreviewRect = scrollRect.GetComponent<RectTransform>();
            _fgPreviewGo = scrollRect.gameObject;

            EditRect = UGUIShip.CreateButton(contentRoot, new Rect(PAD, cy + FG_PREVIEW_H + SH, w, BTN_H),
                "ui.edit", BTN_DARK, WHITE, FS_SM, new Action(() => OpenForeground((UIForegroundKind)_fgIndex)))
                .GetComponent<RectTransform>();
            _fgEditBtnGo = EditRect.gameObject;

            BuildInlineEditors(contentRoot, cy, w);
            RefreshForegroundPreview();
        }

        private void BuildInlineEditors(RectTransform contentRoot, float top, float w)
        {
            float editH = TabHeight - top - VPAD;
            float panelH = editH - (BTN_H + PAD * 2f + 1f);

            _fgEditRoot = new GameObject("FgEdit");
            _fgEditRoot.transform.SetParent(contentRoot, false);
            var rootRt = _fgEditRoot.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(rootRt, new Rect(0f, top, TabWidth, editH));

            _fgCustomRoot = new GameObject("FgCustomUI");
            _fgCustomRoot.transform.SetParent(rootRt, false);
            var customRt = _fgCustomRoot.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(customRt, new Rect(0f, 0f, TabWidth, editH));
            LoadSettings();
            BuildFgPanel(customRt, PAD, 0f, w, editH);

            _fgNametagRoot = new GameObject("FgNametag");
            _fgNametagRoot.transform.SetParent(rootRt, false);
            var ntRt = _fgNametagRoot.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(ntRt, new Rect(0f, 0f, TabWidth, panelH));
            LoadNametagSettings();
            BuildNametagPanel(ntRt, PAD, 0f, w, panelH);
            RefreshNametagPreview();

            float by = panelH + PAD;
            UGUIShip.CreatePanel(ntRt, new Rect(PAD, by, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            by += 1f + PAD;
            float btnw = (w - PAD) / 3f;
            UGUIShip.CreateButton(ntRt, new Rect(PAD, by, btnw, BTN_H),
                "ui.apply", BTN_APPLY, WHITE, FS, new Action(OnNametagApply));
            UGUIShip.CreateButton(ntRt, new Rect(PAD + btnw + PAD * 0.5f, by, btnw, BTN_H),
                "ui.enable_all_2", SEL_COLOR, WHITE, FS_SM, new Action(() => { SetAllNametagEnabled(true); OnNametagApply(); }));
            UGUIShip.CreateButton(ntRt, new Rect(PAD + (btnw + PAD * 0.5f) * 2f, by, btnw, BTN_H),
                "ui.disable_all_2", BTN_REMOVE, WHITE, FS_SM, new Action(() => { SetAllNametagEnabled(false); OnNametagApply(); }));
        }

        private static void SetToggle(Button btn, bool on)
        {
            var lbl = btn?.GetComponentInChildren<Text>();
            if (lbl != null) UGUIShip.RelabelText(lbl, on ? "ui.on" : "ui.off");
            UGUIShip.SetButtonSelected(btn, on, SEL_COLOR);
        }

        // walk the carousel on its own so the help prompt can show the previews changing instead of
        // just describing them
        internal void DemoCarousel() => StartCoroutine(DemoCarouselRoutine().WrapToIl2Cpp());

        private IEnumerator DemoCarouselRoutine()
        {
            for (int i = 0; i < 4; i++)
            {
                yield return new WaitForSecondsRealtime(1.1f);
                if (Live != this) yield break;
                CycleForeground(1);
            }
        }

        void CycleForeground(int d)
        {
            _fgIndex = (_fgIndex + d + 7) % 7;
            if (_fgCarouselLabel != null) UGUIShip.RelabelText(_fgCarouselLabel, UIForegroundDetailTab.Label((UIForegroundKind)_fgIndex));
            RefreshForegroundPreview();
        }

        void RefreshForegroundPreview()
        {
            var kind = (UIForegroundKind)_fgIndex;
            bool inline = IsInline(kind);

            _fgPreviewGo.SetActive(!inline);
            _fgEditBtnGo.SetActive(!inline);
            _fgEditRoot.SetActive(inline);
            _fgCustomRoot.SetActive(kind == UIForegroundKind.CustomUI);
            _fgNametagRoot.SetActive(kind == UIForegroundKind.Nametag);
            if (inline) return;

            _fgPreview.Refresh(this, kind, _fgViewport,
                new Vector3(205.4f, -44.6501f, 0f), new Vector3(0.8236f, 0.8236f, 0.6f),
                () => Customization.UI.MenuCustomizationApplication.Instance.GetBannerColoursFromSettings(ToBannerScreen(kind)));
        }

        static Customization.UI.MenuCustomizationApplication.BannerScreen ToBannerScreen(UIForegroundKind k) => k switch
        {
            UIForegroundKind.Qualified => Customization.UI.MenuCustomizationApplication.BannerScreen.Qualified,
            UIForegroundKind.Eliminated => Customization.UI.MenuCustomizationApplication.BannerScreen.Eliminated,
            UIForegroundKind.EliminatedSquad => Customization.UI.MenuCustomizationApplication.BannerScreen.Squad,
            UIForegroundKind.Winner => Customization.UI.MenuCustomizationApplication.BannerScreen.Winner,
            UIForegroundKind.RoundOver => Customization.UI.MenuCustomizationApplication.BannerScreen.RoundOver,
            _ => Customization.UI.MenuCustomizationApplication.BannerScreen.Qualified,
        };

        void OpenForeground(UIForegroundKind kind)
        {
            var t = BetterFGTabRegistry.NewTab<UIForegroundDetailTab>();
            t.What = kind;
            BetterFGUIMan.Instance?.SwitchSlotTab(this, t);
        }
    }
}
