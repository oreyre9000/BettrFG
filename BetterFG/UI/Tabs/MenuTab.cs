using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.UI;
using BetterFG.Services;
using BetterFG.Tweaks;
using BetterFG.Utilities;
using UnityEngine;
using UnityEngine.UI;
using LayoutElement = UnityEngine.UI.LayoutElement;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class MenuTab : Tab
    {
        public MenuTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Main Menu";
        protected override string TitleId => "ui.main_menu";
        protected override string BgResource => "BetterFG.assets.ui.tab.menu.png";


        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color DIM = new Color(1f, 1f, 1f, 0.4f);
        private static readonly Color WHITE = UGUIShip.WHITE;
        private static readonly Color BTN_APPLY = new Color(0.45f, 0.35f, 0.25f, 1f);
        private static readonly Color BTN_REMOVE = UGUIShip.BTN_REMOVE;
        private static readonly Color BTN_DARK = UGUIShip.BTN_DARK;
        private static readonly Color BTN_ADD = new Color(0.3f, 0.3f, 0.15f, 1f);
        private static readonly Color SEL_COLOR = new Color(0.25f, 0.5f, 0.25f, 1f);
        private static readonly Color BTN_ON = new Color(0.25f, 0.5f, 0.25f, 1f);

        private static float subTabH => BTN_H * 0.9f;

        // ── Sub-tab ───────────────────────────────────────────────────────────
        // falling-screen (lobby bg) colours moved to the UI tab's Background section, so this tab
        // is just Background + Camera now.
        private enum SubTab { Background, Camera }
        private SubTab _sub = SubTab.Background;
        private Button _btnSubBg, _btnSubCam;
        private GameObject _bgPanel, _camPanel;

        // ── State: background ─────────────────────────────────────────────────
        private float _topR, _topG, _topB;
        private float _botR = 1f, _botG = 1f, _botB = 1f;
        private float _bias, _smooth = 1f;

        // ── State: background carousel (3D Background / Images / Ambient / Sun) ──
        private enum BgCarouselPage { Background3D, Images, Ambient, Sun }
        private BgCarouselPage _bgPage = BgCarouselPage.Background3D;
        private Text _bgCarouselLabel;
        private RectTransform _bgCarouselBody;
        private float _bgBodyW, _bgBodyH;

        // ── State: 3D background (native main menu skybox swap) ──────────────
        private int _bg3dIndex;

        // ── State: background images list ─────────────────────────────────────
        private List<MenuCustomizationApplication.BgImageEntry> _bgEntries = new List<MenuCustomizationApplication.BgImageEntry>();
        private RectTransform _bgImagesContent;
        private static readonly Dictionary<string, Texture2D> _bgThumbCache =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static float BG_ROW_H => 30f * UIScale.S;

        // ── State: ambient light + sun ────────────────────────────────────────
        private bool _ambientOn;
        private float _ambientR = 0.5f, _ambientG = 0.5f, _ambientB = 0.5f;
        private Button _ambientToggleBtn;
        private Image _ambientSwatch;
        private bool _sunOn;
        private float _sunRotX = 50f, _sunRotY, _sunRotZ;
        private Button _sunToggleBtn;

        // ── State: circles pattern ────────────────────────────────────────────
        // apply/restore + the original-texture cache live in MenuCustomizationApplication now so the
        // boot-time auto-apply and this UI share one path. we only keep the settings key + label here.
        private const string KEY_PATTERN_PATH = MenuCustomizationApplication.KEY_PATTERN_PATH;
        private Text _patternLabel;

        // ── State: camera ─────────────────────────────────────────────────────
        private bool _camOn;
        private Button _camToggleBtn;
        private float _fov = 40f;
        private float _camX, _camY, _camZ;
        private float _lookAtX, _lookAtY, _lookAtZ;

        // ── State: plinth colour ──────────────────────────────────────────────
        private bool _plinthColOn;
        private float _plinthColR = 1f, _plinthColG = 1f, _plinthColB = 1f;
        private Button _plinthColToggleBtn;
        private Image _plinthColSwatch;

        // ── Settings keys (bg shared with MenuCustomizationApplication) ───────
        private static string KEY_TOP_R => MenuCustomizationApplication.KEY_BG_TOP_R;
        private static string KEY_TOP_G => MenuCustomizationApplication.KEY_BG_TOP_G;
        private static string KEY_TOP_B => MenuCustomizationApplication.KEY_BG_TOP_B;
        private static string KEY_BOT_R => MenuCustomizationApplication.KEY_BG_BOT_R;
        private static string KEY_BOT_G => MenuCustomizationApplication.KEY_BG_BOT_G;
        private static string KEY_BOT_B => MenuCustomizationApplication.KEY_BG_BOT_B;
        private static string KEY_BIAS => MenuCustomizationApplication.KEY_BG_BIAS;
        private static string KEY_SMOOTH => MenuCustomizationApplication.KEY_BG_SMOOTH;

        private const string KEY_CAM_ENABLED = MenuCustomizationApplication.KEY_CAM_ENABLED;
        private const string KEY_CAM_FOV = MenuCustomizationApplication.KEY_CAM_FOV;
        private const string KEY_CAM_X = MenuCustomizationApplication.KEY_CAM_X;
        private const string KEY_CAM_Y = MenuCustomizationApplication.KEY_CAM_Y;
        private const string KEY_CAM_Z = MenuCustomizationApplication.KEY_CAM_Z;
        private const string KEY_CAM_LOOKAT_X = MenuCustomizationApplication.KEY_CAM_LOOKAT_X;
        private const string KEY_CAM_LOOKAT_Y = MenuCustomizationApplication.KEY_CAM_LOOKAT_Y;
        private const string KEY_CAM_LOOKAT_Z = MenuCustomizationApplication.KEY_CAM_LOOKAT_Z;

        // ── Build ─────────────────────────────────────────────────────────────

        protected override void BuildContent(RectTransform contentRoot)
        {
            LoadSettings();

            float w = TabWidth - PAD * 2f;
            float y = VPAD;

            // subtab bar
            float halfw = (w - PAD * 0.5f) / 2f;
            _btnSubBg = UGUIShip.CreateButton(contentRoot,
                new Rect(PAD, y, halfw, subTabH), "ui.background",
                _sub == SubTab.Background ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() => SetSubTab(SubTab.Background)));
            _btnSubCam = UGUIShip.CreateButton(contentRoot,
                new Rect(PAD + halfw + PAD * 0.5f, y, halfw, subTabH), "ui.foreground",
                _sub == SubTab.Camera ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() => SetSubTab(SubTab.Camera)));
            y += subTabH + SH;

            UGUIShip.CreatePanel(contentRoot, new Rect(PAD, y, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            y += 1f + SH;

            float btnRowH = BTN_H + PAD * 2f + 1f;
            float bodyH = TabHeight - y - VPAD - btnRowH;

            // panels are scroll views; the viewport is inset by SCROLLBAR_INSET on both sides
            // (bar lives in the right one). content is laid out at x=PAD, so trim the width down
            // to the viewport's right edge or full-width controls get clipped under the bar.
            float panelW = w - (UGUIShip.SCROLLBAR_INSET * 2f - PAD);

            // background panel
            _bgPanel = new GameObject("BgPanel");
            _bgPanel.transform.SetParent(contentRoot, false);
            var bgPanelRt = _bgPanel.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(bgPanelRt, new Rect(0f, y, TabWidth, bodyH));
            BuildBgPanel(bgPanelRt, PAD, 0f, panelW, bodyH);

            // camera panel
            _camPanel = new GameObject("CamPanel");
            _camPanel.transform.SetParent(contentRoot, false);
            var camPanelRt = _camPanel.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(camPanelRt, new Rect(0f, y, TabWidth, bodyH));
            BuildCamPanel(camPanelRt, PAD, 0f, panelW, bodyH);

            // apply / remove always visible
            float by = y + bodyH + PAD;
            UGUIShip.CreatePanel(contentRoot, new Rect(PAD, by, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            by += 1f + PAD;
            float btnw = (w - PAD * 0.5f) / 2f;
            UGUIShip.CreateButton(contentRoot, new Rect(PAD, by, btnw, BTN_H),
                "ui.apply", BTN_APPLY, WHITE, FS, new Action(OnApply));
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + btnw + PAD * 0.5f, by, btnw, BTN_H),
                "ui.remove_2", BTN_REMOVE, WHITE, FS, new Action(OnRemove));

            RefreshSubTabVisibility();
        }

        // ── Sub-tab switching ─────────────────────────────────────────────────

        private void SetSubTab(SubTab sub)
        {
            Transition(_sub == SubTab.Background ? _bgPanel : _camPanel, sub == SubTab.Background ? _bgPanel : _camPanel, () =>
            {
                _sub = sub;
                UGUIShip.SetButtonSelected(_btnSubBg, sub == SubTab.Background, SEL_COLOR);
                UGUIShip.SetButtonSelected(_btnSubCam, sub == SubTab.Camera, SEL_COLOR);
                RefreshSubTabVisibility();
            });
        }

        private void RefreshSubTabVisibility()
        {
            if (_bgPanel != null) _bgPanel.SetActive(_sub == SubTab.Background);
            if (_camPanel != null) _camPanel.SetActive(_sub == SubTab.Camera);
        }

        // ── Background panel ──────────────────────────────────────────────────
        // a carousel ( ‹ 3D Background / Background Images / Ambient Light / Main Sun Rotation › ), same
        // ‹ Style › cycle shape as BatchEditWindow's subtab header. each page rebuilds the body below.

        private void BuildBgPanel(RectTransform parent, float x, float y, float w, float h)
        {
            float cy = PAD;

            // ── carousel header: ‹  Background Images  › ──
            float arrow = subTabH;
            UGUIShip.CreateButton(parent, new Rect(x, cy, arrow, BTN_H),
                "<", BTN_DARK, WHITE, FS_SM, new Action(() => CycleBgPage(-1)));
            _bgCarouselLabel = UGUIShip.CreateLabel(parent, new Rect(x + arrow, cy, w - arrow * 2f, BTN_H),
                BgPageTitle(_bgPage), FS_SM, WHITE, TextAnchor.MiddleCenter);
            UGUIShip.CreateButton(parent, new Rect(x + w - arrow, cy, arrow, BTN_H),
                ">", BTN_DARK, WHITE, FS_SM, new Action(() => CycleBgPage(1)));
            cy += BTN_H + SH;

            UGUIShip.CreatePanel(parent, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            _bgBodyW = w;
            _bgBodyH = h - cy;

            var bodyGo = new GameObject("BgCarouselBody");
            bodyGo.transform.SetParent(parent, false);
            _bgCarouselBody = bodyGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(_bgCarouselBody, new Rect(0f, cy, TabWidth, _bgBodyH));

            RebuildBgCarouselBody();
        }

        private void CycleBgPage(int d)
        {
            _bgPage = (BgCarouselPage)(((int)_bgPage + d + 4) % 4);
            if (_bgCarouselLabel != null) UGUIShip.RelabelText(_bgCarouselLabel, BgPageTitle(_bgPage));
            RebuildBgCarouselBody();
        }

        private static string BgPageTitle(BgCarouselPage p) => p switch
        {
            BgCarouselPage.Background3D => "bg3d.title",
            BgCarouselPage.Images => "ui.background_images",
            BgCarouselPage.Ambient => "ui.ambient_light",
            BgCarouselPage.Sun => "ui.main_sun_rotation",
            _ => "?"
        };

        private void RebuildBgCarouselBody()
        {
            if (_bgCarouselBody == null) return;
            for (int i = _bgCarouselBody.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_bgCarouselBody.GetChild(i).gameObject);

            switch (_bgPage)
            {
                case BgCarouselPage.Background3D: BuildBackground3DPage(_bgCarouselBody, _bgBodyW, _bgBodyH); break;
                case BgCarouselPage.Images: BuildBgImagesPage(_bgCarouselBody, _bgBodyW, _bgBodyH); break;
                case BgCarouselPage.Ambient: BuildAmbientPage(_bgCarouselBody, _bgBodyW, _bgBodyH); break;
                case BgCarouselPage.Sun: BuildSunPage(_bgCarouselBody, _bgBodyW, _bgBodyH); break;
            }
        }

        // ── 3D background page ────────────────────────────────────────────────
        // a mode switch (Native game skyboxes / Custom BettrFG bundles — the same ones from the
        // "2D To 3D Background" tweak) plus a single ‹ Name › cycle within whichever mode is active.
        // "Normal" leaves the game's own native 3D background system alone entirely (it already has
        // its own on/off in the game's Options). cycling live-swaps it immediately, so browsing IS
        // the preview.

        private enum Bg3dMode { Native, Custom }
        private Bg3dMode _bg3dMode = Bg3dMode.Native;
        private string _bg3dCustomBundle = "";
        private RectTransform _bg3dContent;
        private static bool _bg3dCatalogueTried;

        private void BuildBackground3DPage(RectTransform parent, float w, float h)
        {
            _bg3dMode = SettingsService.Get(MenuCustomizationApplication.KEY_BG3D_MODE, "native") == "custom" ? Bg3dMode.Custom : Bg3dMode.Native;
            _bg3dIndex = MenuCustomizationApplication.Bg3dIndexOf(SettingsService.Get(MenuCustomizationApplication.KEY_BG3D_SELECTED, ""));
            _bg3dCustomBundle = SettingsService.Get(MenuCustomizationApplication.KEY_BG3D_CUSTOM_BUNDLE, "");

            float keepY = _bg3dContent != null ? _bg3dContent.anchoredPosition.y : 0f;
            var (scrollRect, content) = UGUIShip.CreateScrollView(parent, new Rect(0f, 0f, TabWidth, h));
            _bg3dContent = content;
            float x = PAD;
            float cy = PAD;

            if (!_bg3dCatalogueTried) { _bg3dCatalogueTried = true; StartCoroutine(Background3dTweak.FetchCatalogue(RebuildBg3dIfShown).WrapToIl2Cpp()); }
            StartCoroutine(Background3dTweak.FetchPreviews(RebuildBg3dIfShown).WrapToIl2Cpp());

            if (_bg3dMode == Bg3dMode.Custom && Background3dTweak.Have(_bg3dCustomBundle))
            {
                string bundle = _bg3dCustomBundle;
                var off = MenuCustomizationApplication.Bg3dOffset(bundle);
                UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "ui.position", FS_SM, HINT);
                cy += LH + SH;
                BuildSliderRaw(content, x, cy, w, "X", off.x, -200f, 200f, v => MenuCustomizationApplication.SetBg3dOffset(bundle, 0, v), 0f);
                cy += LH + SH;
                BuildSliderRaw(content, x, cy, w, "Y", off.y, -200f, 200f, v => MenuCustomizationApplication.SetBg3dOffset(bundle, 1, v), 0f);
                cy += LH + SH;
                BuildSliderRaw(content, x, cy, w, "Z", off.z, -200f, 200f, v => MenuCustomizationApplication.SetBg3dOffset(bundle, 2, v), 0f);
                cy += LH + PAD;
            }

            string pick = Bg3dPickKey();
            var options = BetterFG.Customization.Round.RoundBackgroundService.Options();
            var chosen = options[0];
            foreach (var o in options) if (o.Key == pick) chosen = o;
            float ph = Mathf.Round((w - 8f) * 9f / 16f);
            BetterFG.UI.Components.PreviewCard.Build(content, x, cy, w, ph, LH, FS_SM, chosen.Label,
                chosen.Key.Length > 0 ? Background3dTweak.Preview(chosen.Preview) : null, false, BTN_DARK, SEL_COLOR, OpenBg3dLibrary);
            cy += ph + LH + 8f + SH;
            UGUIShip.CreateButton(content, new Rect(x, cy, w, BTN_H), "round_bg.change", BTN_DARK, WHITE, FS_SM, OpenBg3dLibrary);
            cy += BTN_H;

            content.sizeDelta = new Vector2(0f, cy + PAD);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, keepY);
        }

        private string Bg3dPickKey()
        {
            if (_bg3dMode == Bg3dMode.Custom) return _bg3dCustomBundle.StartsWith("round:") ? _bg3dCustomBundle : "bettrfg:" + _bg3dCustomBundle;
            string id = MenuCustomizationApplication.Bg3dCatalog[_bg3dIndex].Id;
            return id.Length > 0 ? "native:" + id : "";
        }

        private void OpenBg3dLibrary() =>
            BetterFG.Customization.Round.BackgroundLibrary.Open(PickBg3d, Bg3dPickKey(), allow: key => !key.StartsWith("scene:") && !key.StartsWith("sky:"));

        private void PickBg3d(string key)
        {
            if (key.Length == 0 || key.StartsWith("native:"))
            {
                _bg3dMode = Bg3dMode.Native;
                _bg3dIndex = MenuCustomizationApplication.Bg3dIndexOf(key.Length == 0 ? "" : key.Substring(7));
            }
            else
            {
                _bg3dMode = Bg3dMode.Custom;
                _bg3dCustomBundle = key.StartsWith("bettrfg:") ? key.Substring(8) : key;
            }
            ApplyBg3dLive();
            RebuildBgCarouselBody();
        }

        private void RebuildBg3dIfShown()
        {
            if (_bgPage == BgCarouselPage.Background3D) RebuildBgCarouselBody();
        }

        private void ApplyBg3dLive()
        {
            if (_bg3dMode == Bg3dMode.Native)
                MenuCustomizationApplication.Instance?.RequestMenuBackground3D(MenuCustomizationApplication.Bg3dCatalog[_bg3dIndex].Id);
            else
                MenuCustomizationApplication.Instance?.RequestMenuBackgroundCustomBundle(_bg3dCustomBundle);
        }

        // ── Background images page ──────────────────────────────────────────────
        // 1:1 with CustomSkinTextureTab's list: square thumbnail + name on the left, edit/on-off/remove
        // on the right, "+ Add" row at the bottom opening the wizard. only one entry can be active at a
        // time (single background quad) — toggling one on turns the previously active one off.

        private void BuildBgImagesPage(RectTransform parent, float w, float h)
        {
            _bgEntries = MenuCustomizationApplication.LoadBgImageEntries();

            var (scrollRect, content) = UGUIShip.CreateScrollView(parent, new Rect(0f, 0f, TabWidth, h));
            _bgImagesContent = content;
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(2, 2, 2, 2);
            vlg.spacing = 2f;
            vlg.childControlHeight = false;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RefreshBgImagesList();
        }

        private void RefreshBgImagesList()
        {
            if (_bgImagesContent == null) return;
            for (int i = _bgImagesContent.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_bgImagesContent.GetChild(i).gameObject);

            float rowW = TabWidth - PAD * 2f - 8f;

            for (int i = 0; i < _bgEntries.Count; i++)
            {
                int idx = i;
                var entry = _bgEntries[i];
                bool active = entry.enabled;

                var rowGo = new GameObject("BgRow_" + i);
                rowGo.transform.SetParent(_bgImagesContent, false);
                rowGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, BG_ROW_H);
                var le = rowGo.AddComponent<LayoutElement>();
                le.preferredHeight = BG_ROW_H;
                le.flexibleWidth = 1f;
                rowGo.AddComponent<Image>().color = WHITE;

                var rowBtn = rowGo.AddComponent<Button>();
                var nav = rowBtn.navigation;
                nav.mode = Navigation.Mode.None;
                rowBtn.navigation = nav;

                UGUIShip.PaintListRow(rowBtn, i, false);

                // square preview, cut to the row height
                float thumbSz = BG_ROW_H - 4f;
                var thumbGo = new GameObject("Thumb");
                thumbGo.transform.SetParent(rowBtn.transform, false);
                var thumbRt = thumbGo.AddComponent<RectTransform>();
                UGUIShip.SetPixelRect(thumbRt, new Rect(3f, 2f, thumbSz, thumbSz));
                var raw = thumbGo.AddComponent<RawImage>();
                raw.raycastTarget = false;
                var thumb = BgThumb(entry.path);
                if (thumb != null) raw.texture = thumb;
                else raw.color = new Color(0f, 0f, 0f, 0.4f);

                float editW = 30f * UIScale.S, toggleW = 30f * UIScale.S, removeW = 22f * UIScale.S;
                float nameX = thumbSz + 6f;
                float nameW = rowW - editW - toggleW - removeW - nameX - 10f;

                var nameLbl = UGUIShip.CreateLabel(rowBtn.transform,
                    new Rect(nameX, 0f, nameW, BG_ROW_H), entry.entryName,
                    FS_SM, active ? WHITE : DIM, TextAnchor.MiddleLeft);

                BgRowBtn(rowBtn.transform, -(removeW + toggleW + editW + 4f), editW,
                    "ui.edit_2", BTN_DARK, () => OpenBgWizard(idx));

                BgRowBtn(rowBtn.transform, -(removeW + toggleW + 2f), toggleW,
                    active ? "ui.on_2" : "ui.off_2", active ? BTN_ON : BTN_DARK, () => ToggleBgEntry(idx));

                BgRowBtn(rowBtn.transform, -2f, removeW, "x", BTN_REMOVE, () => RemoveBgEntry(idx));
            }

            var addBtn = UGUIShip.CreateButton(_bgImagesContent, new Rect(0f, 0f, TabWidth - PAD * 2f - 8f, BG_ROW_H),
                "ui.add_background_image", BTN_ADD, WHITE, FS, new Action(() => OpenBgWizard(-1)));
            var addLe = addBtn.gameObject.AddComponent<LayoutElement>();
            addLe.preferredHeight = BG_ROW_H;
            addLe.flexibleWidth = 1f;
        }

        private Button BgRowBtn(Transform parent, float anchoredX, float bw, string label, Color bg, Action onClick)
        {
            float bh = Mathf.Min(BG_ROW_H - 6f, 24f * UIScale.S);
            var btn = UGUIShip.CreateButton(parent, new Rect(0f, 0f, bw, bh), label, bg, WHITE, FS_SM - 1, onClick);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(anchoredX, 0f);
            rt.sizeDelta = new Vector2(bw, bh);
            return btn;
        }

        private static Texture2D BgThumb(string path)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
            if (_bgThumbCache.TryGetValue(path, out var cached) && cached != null) return cached;
            try
            {
                var bytes = System.IO.File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);
                tex.wrapMode = TextureWrapMode.Clamp;
                _bgThumbCache[path] = tex;
                return tex;
            }
            catch (Exception ex) { Plugin.Log.LogError("MenuTab: bg thumb failed: " + ex.Message); return null; }
        }

        private void OpenBgWizard(int editIdx)
        {
            var wizard = BetterFGTabRegistry.NewTab<MenuBackgroundImageWizardTab>();
            wizard.EditIndex = editIdx;
            BetterFGUIMan.Instance?.SwitchSlotTab(this, wizard);
        }

        private void ToggleBgEntry(int idx)
        {
            MenuCustomizationApplication.Instance?.SetBgImageEnabled(idx, !_bgEntries[idx].enabled);
            _bgEntries[idx].enabled = !_bgEntries[idx].enabled;
            RefreshBgImagesList();
        }

        private void RemoveBgEntry(int idx)
        {
            bool wasEnabled = _bgEntries[idx].enabled;
            _bgEntries.RemoveAt(idx);
            MenuCustomizationApplication.SaveBgImageEntries(_bgEntries);
            if (wasEnabled) MenuCustomizationApplication.Instance?.ApplyImageBgFromSettings();
            RefreshBgImagesList();
        }

        // ── Ambient light page ────────────────────────────────────────────────

        private void BuildAmbientPage(RectTransform parent, float w, float h)
        {
            var (scrollRect, content) = UGUIShip.CreateScrollView(parent, new Rect(0f, 0f, TabWidth, h));
            float x = PAD;
            float cy = PAD;

            float ambSwatchW = BTN_H;
            float ambSliderW = w - ambSwatchW - PAD;

            _ambientToggleBtn = UGUIShip.CreateButton(content, new Rect(x, cy, ambSliderW, BTN_H),
                _ambientOn ? "ui.ambient_on" : "ui.ambient_off",
                _ambientOn ? BTN_ON : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _ambientOn = !_ambientOn;
                    MenuCustomizationApplication.Instance?.SetAmbientEnabled(_ambientOn);
                    var lbl = _ambientToggleBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, _ambientOn ? "ui.ambient_on" : "ui.ambient_off");
                    var img = _ambientToggleBtn?.GetComponent<Image>();
                    if (img != null) img.color = _ambientOn ? BTN_ON : BTN_DARK;
                }));

            var ambSwatchGo = new GameObject("AmbientSwatch");
            ambSwatchGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(ambSwatchGo.AddComponent<RectTransform>(), new Rect(x + ambSliderW + PAD, cy, ambSwatchW, BTN_H));
            _ambientSwatch = ambSwatchGo.AddComponent<Image>();
            _ambientSwatch.color = new Color(_ambientR, _ambientG, _ambientB);
            cy += BTN_H + SH;

            UGUIShip.CreateColorControls(content, x, ref cy, w,
                () => _ambientR, () => _ambientG, () => _ambientB,
                v => _ambientR = v, v => _ambientG = v, v => _ambientB = v, () => ApplyAmbient(), out _, out _, out _,
                new Color(0.5f, 0.5f, 0.5f));

            content.sizeDelta = new Vector2(0f, cy + PAD);
        }

        // ── Main sun rotation page ────────────────────────────────────────────

        private void BuildSunPage(RectTransform parent, float w, float h)
        {
            var (scrollRect, content) = UGUIShip.CreateScrollView(parent, new Rect(0f, 0f, TabWidth, h));
            float x = PAD;
            float cy = PAD;

            _sunToggleBtn = UGUIShip.CreateButton(content, new Rect(x, cy, w, BTN_H),
                _sunOn ? "ui.sun_override_on" : "ui.sun_override_off",
                _sunOn ? BTN_ON : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _sunOn = !_sunOn;
                    MenuCustomizationApplication.Instance?.SetSunEnabled(_sunOn);
                    var lbl = _sunToggleBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, _sunOn ? "ui.sun_override_on" : "ui.sun_override_off");
                    var img = _sunToggleBtn?.GetComponent<Image>();
                    if (img != null) img.color = _sunOn ? BTN_ON : BTN_DARK;
                }));
            cy += BTN_H + SH;

            BuildSliderRaw(content, x, cy, w, "X", _sunRotX, 0f, 360f,
                v => { _sunRotX = v; ApplySun(); }, 50f);
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, w, "Y", _sunRotY, 0f, 360f,
                v => { _sunRotY = v; ApplySun(); }, 0f);
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, w, "Z", _sunRotZ, 0f, 360f,
                v => { _sunRotZ = v; ApplySun(); }, 0f);
            cy += LH + PAD;

            content.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void ApplyAmbient()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            SettingsService.Set(MenuCustomizationApplication.KEY_AMBIENT_R, _ambientR.ToString(ci));
            SettingsService.Set(MenuCustomizationApplication.KEY_AMBIENT_G, _ambientG.ToString(ci));
            SettingsService.Set(MenuCustomizationApplication.KEY_AMBIENT_B, _ambientB.ToString(ci));
            if (_ambientSwatch != null) _ambientSwatch.color = new Color(_ambientR, _ambientG, _ambientB);
            if (_ambientOn) MenuCustomizationApplication.Instance?.ApplyAmbient(new Color(_ambientR, _ambientG, _ambientB));
        }

        private void ApplySun()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            SettingsService.Set(MenuCustomizationApplication.KEY_SUN_ROT_X, _sunRotX.ToString(ci));
            SettingsService.Set(MenuCustomizationApplication.KEY_SUN_ROT_Y, _sunRotY.ToString(ci));
            SettingsService.Set(MenuCustomizationApplication.KEY_SUN_ROT_Z, _sunRotZ.ToString(ci));
            if (_sunOn) MenuCustomizationApplication.Instance?.ApplySunRotation(_sunRotX, _sunRotY, _sunRotZ);
        }

        // ── Camera panel ──────────────────────────────────────────────────────

        private void BuildCamPanel(RectTransform parent, float x, float y, float w, float h)
        {
            var (scrollRect, content) = UGUIShip.CreateScrollView(parent, new Rect(0f, y, TabWidth, h));

            float cy = PAD;

            _camToggleBtn = UGUIShip.CreateButton(content, new Rect(x, cy, w, BTN_H),
                _camOn ? "ui.custom_camera_on" : "ui.custom_camera_off",
                _camOn ? BTN_ON : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _camOn = !_camOn;
                    SettingsService.Set(KEY_CAM_ENABLED, _camOn ? "true" : "false");
                    if (_camOn) ApplyCam();
                    else MenuCustomizationApplication.Instance?.ResetCam();
                    var lbl = _camToggleBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, _camOn ? "ui.custom_camera_on" : "ui.custom_camera_off");
                    var img = _camToggleBtn?.GetComponent<Image>();
                    if (img != null) img.color = _camOn ? BTN_ON : BTN_DARK;
                }));
            cy += BTN_H + PAD;

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "ui.fov", FS_SM, HINT);
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, w, "FOV", _fov, 20f, 120f,
                v => _fov = v, 40f);
            cy += LH + PAD;

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "ui.position", FS_SM, HINT);
            cy += LH + SH;

            BuildSliderRaw(content, x, cy, w, "X", _camX, -5f, 5f, v => _camX = v, 0f);
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, w, "Y", _camY, -5f, 5f, v => _camY = v, 0f);
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, w, "Z", _camZ, -5f, 5f, v => _camZ = v, 0f);

            cy += LH + PAD;
            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "ui.look_at_offset", FS_SM, HINT);
            cy += LH + SH;

            BuildSliderRaw(content, x, cy, w, "X", _lookAtX, -5f, 5f, v => _lookAtX = v, 0f);
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, w, "Y", _lookAtY, -5f, 5f, v => _lookAtY = v, 0f);
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, w, "Z", _lookAtZ, -5f, 5f, v => _lookAtZ = v, 0f);
            cy += LH + PAD;

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            // ── Plinth colour ─────────────────────────────────────────────────
            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "ui.plinth_colour", FS_SM, HINT);
            cy += LH + SH;

            float plinthSwatchW = BTN_H;
            float plinthToggleW = w - plinthSwatchW - PAD;

            _plinthColToggleBtn = UGUIShip.CreateButton(content, new Rect(x, cy, plinthToggleW, BTN_H),
                _plinthColOn ? "ui.plinth_colour_on" : "ui.plinth_colour_off",
                _plinthColOn ? BTN_ON : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _plinthColOn = !_plinthColOn;
                    MenuCustomizationApplication.Instance?.SetPlinthColorEnabled(_plinthColOn);
                    var lbl = _plinthColToggleBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, _plinthColOn ? "ui.plinth_colour_on" : "ui.plinth_colour_off");
                    var img = _plinthColToggleBtn?.GetComponent<Image>();
                    if (img != null) img.color = _plinthColOn ? BTN_ON : BTN_DARK;
                }));

            var plinthSwatchGo = new GameObject("PlinthColSwatch");
            plinthSwatchGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(plinthSwatchGo.AddComponent<RectTransform>(), new Rect(x + plinthToggleW + PAD, cy, plinthSwatchW, BTN_H));
            _plinthColSwatch = plinthSwatchGo.AddComponent<Image>();
            _plinthColSwatch.color = new Color(_plinthColR, _plinthColG, _plinthColB);
            cy += BTN_H + SH;

            UGUIShip.CreateColorControls(content, x, ref cy, w,
                () => _plinthColR, () => _plinthColG, () => _plinthColB,
                v => _plinthColR = v, v => _plinthColG = v, v => _plinthColB = v, () => ApplyPlinthCol(), out _, out _, out _,
                Color.white);

            content.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void ApplyPlinthCol()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            SettingsService.Set(MenuCustomizationApplication.KEY_PLINTH_COL_R, _plinthColR.ToString(ci));
            SettingsService.Set(MenuCustomizationApplication.KEY_PLINTH_COL_G, _plinthColG.ToString(ci));
            SettingsService.Set(MenuCustomizationApplication.KEY_PLINTH_COL_B, _plinthColB.ToString(ci));
            if (_plinthColSwatch != null) _plinthColSwatch.color = new Color(_plinthColR, _plinthColG, _plinthColB);
            if (_plinthColOn) MenuCustomizationApplication.Instance?.ApplyPlinthColor(new Color(_plinthColR, _plinthColG, _plinthColB));
        }

        // ── Apply / Remove ────────────────────────────────────────────────────

        private void OnApply()
        {
            SaveSettings();
            if (_sub == SubTab.Background)
            {
                var app = MenuCustomizationApplication.Instance;
                if (app != null)
                    app.ApplyGradient(
                        new Color(_topR, _topG, _topB),
                        new Color(_botR, _botG, _botB),
                        _bias, _smooth);

                ApplyPatternFromSettings();
            }
            else
            {
                ApplyCam();
                ApplyPlinthCol();
            }
        }

        private void OnRemove()
        {
            if (_sub == SubTab.Background)
            {
                RemoveBgKeys();
                var app = MenuCustomizationApplication.Instance;
                if (app != null)
                    app.RestoreBackdrop();
                var ff = ScreenBackgroundService.Screen.FallForce;
                var top = ScreenBackgroundService.DefaultTop(ff);
                var bot = ScreenBackgroundService.DefaultBot(ff);
                _topR = top.r; _topG = top.g; _topB = top.b;
                _botR = bot.r; _botG = bot.g; _botB = bot.b;
                _bias = 0f; _smooth = ScreenBackgroundService.DefaultSmooth(ff);

                RestorePattern();
                SettingsService.Remove(KEY_PATTERN_PATH);
                if (_patternLabel != null) UGUIShip.RelabelText(_patternLabel, "ui.none");
            }
            else
            {
                SettingsService.Set(KEY_CAM_ENABLED, "false");
                SettingsService.Remove(KEY_CAM_FOV);
                SettingsService.Remove(KEY_CAM_X);
                SettingsService.Remove(KEY_CAM_Y);
                SettingsService.Remove(KEY_CAM_Z);
                SettingsService.Remove(KEY_CAM_LOOKAT_X);
                SettingsService.Remove(KEY_CAM_LOOKAT_Y);
                SettingsService.Remove(KEY_CAM_LOOKAT_Z);
                _camOn = false;
                _fov = 40f; _camX = _camY = _camZ = 0f;
                _lookAtX = _lookAtY = _lookAtZ = 0f;
                MenuCustomizationApplication.Instance?.ResetCam();
                if (_camToggleBtn != null)
                {
                    var camLbl = _camToggleBtn.GetComponentInChildren<Text>();
                    if (camLbl != null) UGUIShip.RelabelText(camLbl, "ui.custom_camera_off");
                    var camImg = _camToggleBtn.GetComponent<Image>();
                    if (camImg != null) camImg.color = BTN_DARK;
                }

                SettingsService.Set(MenuCustomizationApplication.KEY_PLINTH_COL_ON, "false");
                SettingsService.Remove(MenuCustomizationApplication.KEY_PLINTH_COL_R);
                SettingsService.Remove(MenuCustomizationApplication.KEY_PLINTH_COL_G);
                SettingsService.Remove(MenuCustomizationApplication.KEY_PLINTH_COL_B);
                _plinthColOn = false;
                _plinthColR = _plinthColG = _plinthColB = 1f;
                MenuCustomizationApplication.Instance?.RevertPlinthColor();
                if (_plinthColSwatch != null) _plinthColSwatch.color = new Color(_plinthColR, _plinthColG, _plinthColB);
                if (_plinthColToggleBtn != null)
                {
                    var lbl = _plinthColToggleBtn.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, "ui.plinth_colour_off");
                    var img = _plinthColToggleBtn.GetComponent<Image>();
                    if (img != null) img.color = BTN_DARK;
                }
            }
        }

        private void RemoveBgKeys()
        {
            SettingsService.Remove(KEY_TOP_R); SettingsService.Remove(KEY_TOP_G); SettingsService.Remove(KEY_TOP_B);
            SettingsService.Remove(KEY_BOT_R); SettingsService.Remove(KEY_BOT_G); SettingsService.Remove(KEY_BOT_B);
            SettingsService.Remove(KEY_BIAS); SettingsService.Remove(KEY_SMOOTH);
        }

        // both delegate to MenuCustomizationApplication so the boot-time auto-apply and the UI share
        // one cache of the original texture — otherwise Remove can't restore a pattern the app applied.
        private void ApplyPatternFromSettings()
            => MenuCustomizationApplication.Instance?.ApplyPatternFromSettings();

        private void RestorePattern()
            => MenuCustomizationApplication.Instance?.RestorePattern();

        private void ApplyCam()
        {
            if (!_camOn) { MenuCustomizationApplication.Instance?.ResetCam(); return; }
            MenuCustomizationApplication.Instance?.ApplyCam(
                new Vector3(_camX, _camY, _camZ), _fov,
                new Vector3(_lookAtX, _lookAtY, _lookAtZ));
        }

        // ── Settings ──────────────────────────────────────────────────────────

        private void LoadSettings()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float P(string key, float def) =>
                float.TryParse(SettingsService.Get(key, def.ToString(ci)),
                    System.Globalization.NumberStyles.Float, ci, out float v) ? v : def;

            var ff = ScreenBackgroundService.Screen.FallForce;
            var top = ScreenBackgroundService.TopColor(ff);
            var bot = ScreenBackgroundService.BotColor(ff);
            _topR = top.r; _topG = top.g; _topB = top.b;
            _botR = bot.r; _botG = bot.g; _botB = bot.b;
            _bias = ScreenBackgroundService.Bias(ff);
            _smooth = ScreenBackgroundService.Smooth(ff);

            _bg3dMode = SettingsService.Get(MenuCustomizationApplication.KEY_BG3D_MODE, "native") == "custom" ? Bg3dMode.Custom : Bg3dMode.Native;
            _bg3dIndex = MenuCustomizationApplication.Bg3dIndexOf(SettingsService.Get(MenuCustomizationApplication.KEY_BG3D_SELECTED, ""));
            _bg3dCustomBundle = SettingsService.Get(MenuCustomizationApplication.KEY_BG3D_CUSTOM_BUNDLE, "");

            _ambientOn = SettingsService.Get(MenuCustomizationApplication.KEY_AMBIENT_ON, "false") == "true";
            _ambientR = P(MenuCustomizationApplication.KEY_AMBIENT_R, 0.5f);
            _ambientG = P(MenuCustomizationApplication.KEY_AMBIENT_G, 0.5f);
            _ambientB = P(MenuCustomizationApplication.KEY_AMBIENT_B, 0.5f);
            _sunOn = SettingsService.Get(MenuCustomizationApplication.KEY_SUN_ON, "false") == "true";
            _sunRotX = P(MenuCustomizationApplication.KEY_SUN_ROT_X, 50f);
            _sunRotY = P(MenuCustomizationApplication.KEY_SUN_ROT_Y, 0f);
            _sunRotZ = P(MenuCustomizationApplication.KEY_SUN_ROT_Z, 0f);

            _camOn = SettingsService.Get(KEY_CAM_ENABLED, "false") == "true";
            _fov = P(KEY_CAM_FOV, 40f);
            _camX = P(KEY_CAM_X, 0f);
            _camY = P(KEY_CAM_Y, 0f);
            _camZ = P(KEY_CAM_Z, 0f);
            _lookAtX = P(KEY_CAM_LOOKAT_X, 0f);
            _lookAtY = P(KEY_CAM_LOOKAT_Y, 0f);
            _lookAtZ = P(KEY_CAM_LOOKAT_Z, 0f);

            _plinthColOn = SettingsService.Get(MenuCustomizationApplication.KEY_PLINTH_COL_ON, "false") == "true";
            _plinthColR = P(MenuCustomizationApplication.KEY_PLINTH_COL_R, 1f);
            _plinthColG = P(MenuCustomizationApplication.KEY_PLINTH_COL_G, 1f);
            _plinthColB = P(MenuCustomizationApplication.KEY_PLINTH_COL_B, 1f);
        }

        private void SaveSettings()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            void S(string k, float v) => SettingsService.Set(k, v.ToString(ci));

            S(KEY_TOP_R, _topR); S(KEY_TOP_G, _topG); S(KEY_TOP_B, _topB);
            S(KEY_BOT_R, _botR); S(KEY_BOT_G, _botG); S(KEY_BOT_B, _botB);
            S(KEY_BIAS, _bias);
            S(KEY_SMOOTH, _smooth);

            SettingsService.Set(KEY_CAM_ENABLED, _camOn ? "true" : "false");
            S(KEY_CAM_FOV, _fov);
            S(KEY_CAM_X, _camX); S(KEY_CAM_Y, _camY); S(KEY_CAM_Z, _camZ);
            S(KEY_CAM_LOOKAT_X, _lookAtX); S(KEY_CAM_LOOKAT_Y, _lookAtY); S(KEY_CAM_LOOKAT_Z, _lookAtZ);
        }

        // ── Slider helpers ────────────────────────────────────────────────────

        // 0..1 slider (for RGB/A)
        private Slider BuildSlider(Transform parent, float x, float y, float w,
            string lbl, float init, Action<float> onChange,
            Color? labelColor = null, Color? fillColor = null, float? resetTo = null)
            => UGUIShip.CreateSlider(parent, x, y, w, lbl, init, LH, PAD, FS_SM, onChange, labelColor, fillColor, true, resetTo);

        // arbitrary range slider
        private Slider BuildSliderRaw(Transform parent, float x, float y, float w,
            string lbl, float init, float min, float max, Action<float> onChange, float? resetTo = null)
        {
            var s = UGUIShip.CreateSlider(parent, x, y, w, lbl, Mathf.InverseLerp(min, max, init),
                LH, PAD, FS_SM, t => onChange(Mathf.Lerp(min, max, t)), null, null, true,
                resetTo.HasValue ? Mathf.InverseLerp(min, max, resetTo.Value) : (float?)null);
            return s;
        }

        // overload for 0..1 sliders without range (matches nametag pattern)
        private Slider BuildSliderRaw(Transform parent, float x, float y, float w,
            string lbl, float init, Action<float> onChange, float? resetTo = null)
            => BuildSlider(parent, x, y, w, lbl, init, onChange, null, null, resetTo);

    }
}
