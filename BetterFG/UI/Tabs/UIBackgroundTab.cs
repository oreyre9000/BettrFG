using System;
using System.Collections.Generic;
using System.Reflection;
using BetterFG.Customization.UI;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class UIBackgroundTab : UISubTab
    {
        public UIBackgroundTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Themes";
        protected override string TitleId => "themes.title";
        protected override string SwitchLabel => "round_bg.switch_rounds";
        protected override Tab MakeSwitchTarget() => BetterFGTabRegistry.NewTab<RoundsTab>();
        public override float OpenHeight => UIScale.TAB_CONTENT_H * 1.3f;

        // set by UIPatternPickerTab's back link so returning from a pattern pick lands back on
        // whichever screen you were customising, instead of resetting to FallForce.
        public ScreenBackgroundService.Screen? InitialScreen { get; set; }

        private static UIBackgroundTab _live;

        public static void RefreshLive()
        {
            if (_live != null && _live._screenBodyGo != null) _live.RebuildScreenBody();
        }

        public override void OnOpened()
        {
            base.OnOpened();
            if (_screenBodyGo != null) RebuildScreenBody();
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            _live = this;
            if (InitialScreen.HasValue) _screenSel = InitialScreen.Value;
            float w = TabWidth - PAD * 2f;
            float y = VPAD;

            var panelGo = new GameObject("BackgroundPanel");
            panelGo.transform.SetParent(contentRoot, false);
            var panelRt = panelGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(panelRt, new Rect(0f, y, TabWidth, TabHeight - y));

            BuildScreenPanel(panelRt, PAD, w, TabHeight - y);
            if (InitialScreen.HasValue) ApplyScreenLive();
        }

        private ScreenBackgroundService.Screen _screenSel = ScreenBackgroundService.Screen.FallForce;
        // the falling screen (lobby bg) isn't a ScreenBackgroundService.Screen — it recolours the
        // named blue-slot images in Menu_Screen_Lobby, not a gradient backdrop. it's a fifth dropdown
        // entry with its own body. this flag says the dropdown currently has it selected.
        private bool _fallingSel;
        private bool _creativeSel;

        private bool _scEnabled;
        private Color _scTop, _scBot, _scPatternCol;
        private float _scBias, _scSmooth;
        private string _scPattern = "";

        private bool _lbEnabled;
        private readonly Color[] _lb = new Color[3];

        private bool _crEnabled;
        private readonly Color[] _cr = new Color[4];

        private Button _toggleBtn;
        private GameObject _screenBodyGo;
        private float _screenBodyW, _screenBodyH;

        private const float SCREEN_PREVIEW_H = 110f;
        private const float PREVIEW_GAP = 6f;

        private bool CurrentEnabled => _creativeSel ? _crEnabled : _fallingSel ? _lbEnabled : _scEnabled;

        // rounded, masked frame for a live clone preview. sized to (w, SCREEN_PREVIEW_H) at (x, y);
        // returns the inner slot to instantiate the clone into.
        private static Transform BuildPreviewFrame(Transform parent, float x, float y, float w)
        {
            var frameGo = new GameObject("PreviewFrame");
            frameGo.transform.SetParent(parent, false);
            UGUIShip.SetPixelRect(frameGo.AddComponent<RectTransform>(), new Rect(x, y, w, SCREEN_PREVIEW_H));
            var (_, slot) = UGUIShip.CreateFramedImage(frameGo.transform);
            slot.gameObject.AddComponent<RectMask2D>();
            return slot;
        }

        private static Image CreateSwatch(Transform parent, Rect rect, Color c)
        {
            var go = new GameObject("Swatch");
            go.transform.SetParent(parent, false);
            UGUIShip.SetPixelRect(go.AddComponent<RectTransform>(), rect);
            var (_, inner) = UGUIShip.CreateFramedImage(go.transform);
            var img = inner.gameObject.AddComponent<Image>();
            img.color = new Color(c.r, c.g, c.b);
            img.raycastTarget = false;
            return img;
        }

        private void BuildColorBlock(Transform content, float x, ref float cy, float w, string labelId,
            Func<Color> get, Action<Color> set, Action changed, Color reset)
        {
            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), labelId, FS_SM, HINT);
            cy += LH + SH;
            float swH = (LH + SH) * 4f - SH;
            float sliderW = w - swH - PAD * 2f;
            var sw = CreateSwatch(content, new Rect(x + sliderW + PAD * 2f, cy, swH, swH), get());
            UGUIShip.CreateColorControls(content, x, ref cy, sliderW,
                () => get().r, () => get().g, () => get().b,
                v => { var c = get(); c.r = v; set(c); },
                v => { var c = get(); c.g = v; set(c); },
                v => { var c = get(); c.b = v; set(c); },
                () => { var c = get(); sw.color = new Color(c.r, c.g, c.b); changed(); },
                out _, out _, out _, reset);
        }

        private void BuildResetButton(Transform content, float x, ref float cy, float w)
        {
            Divider(content, x, ref cy, w);
            UGUIShip.CreateButton(content, new Rect(x, cy, w, BTN_H), "themes.reset_all", BTN_REMOVE, WHITE, FS_SM, new Action(ResetAll));
            cy += BTN_H + PAD * 2f;
        }

        private void ResetAll()
        {
            if (_creativeSel)
            {
                for (int i = 0; i < 4; i++) _cr[i] = MenuCustomizationApplication.CreativeSlotDefault((MenuCustomizationApplication.CreativeSlot)i);
                CommitCreative();
            }
            else if (_fallingSel)
            {
                for (int i = 0; i < 3; i++) _lb[i] = MenuCustomizationApplication.LobbyBgDefaults[i];
                CommitFalling();
            }
            else
            {
                var s = _screenSel;
                _scTop = ScreenBackgroundService.DefaultTop(s);
                _scBot = ScreenBackgroundService.DefaultBot(s);
                _scBias = 0f;
                _scSmooth = ScreenBackgroundService.DefaultSmooth(s);
                _scPatternCol = ScreenBackgroundService.DefaultPatternColor(s);
                _scPattern = "";
                SettingsService.Set(ScreenBackgroundService.KeyPattern(s), "");
                CommitScreen();
            }
            RebuildScreenBody();
        }

        private void Divider(Transform content, float x, ref float cy, float w)
        {
            cy += SH;
            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD * 2f;
        }

        // screen overlay images (fallforce shared by fallforce + loading level)
        private static readonly Dictionary<string, Sprite> _screenSprites = new Dictionary<string, Sprite>();

        private static Sprite ScreenSprite(ScreenBackgroundService.Screen s)
        {
            string file;
            switch (s)
            {
                case ScreenBackgroundService.Screen.FinalRound: file = "final"; break;
                case ScreenBackgroundService.Screen.Explore: file = "explore"; break;
                default: file = "fallforce"; break; // FallForce + LoadingLevel
            }
            return ScreenSpriteByName(file);
        }

        // load an assets/ui/uiscreen/<file>.png sprite by stem — the falling screen isn't a
        // ScreenBackgroundService.Screen so it grabs its overlay through here directly.
        private static Sprite ScreenSpriteByName(string file)
        {
            if (_screenSprites.TryGetValue(file, out var cached) && cached != null) return cached;
            Sprite sp = null;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("BetterFG.assets.ui.uiscreen." + file + ".png");
                if (stream != null)
                {
                    var bytes = new byte[stream.Length];
                    stream.Read(bytes, 0, bytes.Length);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    tex.LoadImage(bytes);
                    tex.wrapMode = TextureWrapMode.Clamp;
                    sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                }
            }
            catch (Exception ex) { Plugin.Log.LogError("UITab: screen sprite load failed: " + ex.Message); }
            _screenSprites[file] = sp;
            return sp;
        }

        private void BuildScreenPanel(RectTransform parent, float x, float w, float h)
        {
            LoadCurrentSettings();

            float cy = PAD;

            // screen selector dropdown — each option has its screen image as an overlay, text right-aligned
            var screens = new List<ScreenBackgroundService.Screen>
            {
                ScreenBackgroundService.Screen.FallForce,
                ScreenBackgroundService.Screen.LoadingLevel,
                ScreenBackgroundService.Screen.FinalRound,
                ScreenBackgroundService.Screen.Explore,
                ScreenBackgroundService.Screen.ShowSelector,
            };
            var opts = new List<string>();
            var initial = new List<bool>();
            var sprites = new List<Sprite>();
            bool special = _fallingSel || _creativeSel;
            foreach (var s in screens) { opts.Add(ScreenBackgroundService.Label(s)); initial.Add(!special && s == _screenSel); sprites.Add(ScreenSprite(s)); }
            opts.Add("Falling Screen"); initial.Add(_fallingSel); sprites.Add(ScreenSpriteByName("fallingscreen"));
            int fallingIdx = screens.Count;
            opts.Add("Creative Background"); initial.Add(_creativeSel); sprites.Add(ScreenSpriteByName("creative"));
            int creativeIdx = fallingIdx + 1;

            string HeaderLabel() => _creativeSel ? "Creative Background" : _fallingSel ? "Falling Screen" : ScreenBackgroundService.Label(_screenSel);
            Sprite HeaderSprite() => _creativeSel ? ScreenSpriteByName("creative") : _fallingSel ? ScreenSpriteByName("fallingscreen") : ScreenSprite(_screenSel);

            Button screenDd = null;
            screenDd = UGUIShip.CreateMultiSelectDropdown(parent, new Rect(x, cy, w, BTN_H),
                HeaderLabel(), opts, initial,
                new Action<int, bool>((idx, _) =>
                {
                    if (idx < 0 || idx > creativeIdx) return;
                    _fallingSel = idx == fallingIdx;
                    _creativeSel = idx == creativeIdx;
                    if (!_fallingSel && !_creativeSel) _screenSel = screens[idx];
                    var lbl = screenDd?.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, HeaderLabel());
                    var headImg = screenDd?.transform.Find("HeaderMask/HeaderImg")?.GetComponent<Image>();
                    if (headImg != null) headImg.sprite = HeaderSprite();
                    RebuildScreenBody();
                }), FS_SM, w, dropdownRowH, true, true, false, sprites, true);
            cy += BTN_H + PAD;

            _toggleBtn = UGUIShip.CreateButton(parent, new Rect(x, cy, w, BTN_H),
                "ui.custom_colours_off", BTN_DARK, WHITE, FS_SM, new Action(ToggleEnabled));
            RefreshToggle();
            cy += BTN_H + PAD;

            _screenBodyW = w;
            _screenBodyH = h - cy - PAD;
            _screenBodyGo = new GameObject("ScreenBody");
            _screenBodyGo.transform.SetParent(parent, false);
            UGUIShip.SetPixelRect(_screenBodyGo.AddComponent<RectTransform>(), new Rect(0f, cy, TabWidth, _screenBodyH));
            BuildCurrentBody();
        }

        private void LoadCurrentSettings()
        {
            if (_creativeSel) LoadCreativeSettings();
            else if (_fallingSel) LoadFallingSettings();
            else LoadScreenSettings(_screenSel);
        }

        private void BuildCurrentBody()
        {
            var rt = _screenBodyGo.GetComponent<RectTransform>();
            if (_creativeSel) BuildCreativeBody(rt, PAD, _screenBodyW, _screenBodyH);
            else if (_fallingSel) BuildFallingBody(rt, PAD, _screenBodyW, _screenBodyH);
            else BuildScreenBody(rt, PAD, _screenBodyW, _screenBodyH);
        }

        private void RebuildScreenBody()
        {
            LoadCurrentSettings();
            RefreshToggle();
            for (int i = _screenBodyGo.transform.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_screenBodyGo.transform.GetChild(i).gameObject);
            BuildCurrentBody();
        }

        private void RefreshToggle()
        {
            bool on = CurrentEnabled;
            UGUIShip.RelabelText(_toggleBtn.GetComponentInChildren<Text>(), on ? "ui.custom_colours_on" : "ui.custom_colours_off");
            UGUIShip.SetButtonColor(_toggleBtn, on ? BTN_ON : BTN_DARK);
        }

        private void ToggleEnabled()
        {
            if (_creativeSel)
            {
                _crEnabled = !_crEnabled;
                SettingsService.Set(MenuCustomizationApplication.KEY_CREATIVE_ENABLED, _crEnabled ? "true" : "false");
                CommitCreative();
            }
            else if (_fallingSel)
            {
                _lbEnabled = !_lbEnabled;
                SettingsService.Set(MenuCustomizationApplication.KEY_LOBBYBG_ENABLED, _lbEnabled ? "true" : "false");
                CommitFalling();
            }
            else
            {
                _scEnabled = !_scEnabled;
                SettingsService.Set(ScreenBackgroundService.KeyEnabled(_screenSel), _scEnabled ? "true" : "false");
                CommitScreen();
            }
            RefreshToggle();
        }

        // ── Gradient screens ──────────────────────────────────────────────

        private GameObject _scPreviewGo;
        private Image _scPreviewBackdrop;
        private Image _scPreviewCircles;
        private Sprite _scPreviewDefaultBackdrop;
        private Color _scPreviewDefaultBackdropColor;
        private Texture _scPreviewDefaultPattern;
        private Color _scPreviewDefaultCirclesColor = Color.white;
        private Texture2D _scPreviewGradTex;
        private Texture2D _scPreviewPatternTex;
        private string _scPreviewPatternPath;
        private Sprite _scPreviewDefaultPatternSprite;
        private Image.Type _scPreviewDefaultPatternType;
        private Text _scPreviewHintLabel;
        private Material _scPreviewCirclesMat;

        private void BuildScreenBody(RectTransform parent, float x, float w, float h)
        {
            var s = _screenSel;
            BuildScreenPreview(BuildPreviewFrame(parent, x, 0f, w), w);

            float scrollY = SCREEN_PREVIEW_H + PREVIEW_GAP;
            var (_, content) = BuildFramedScroll(parent, x, scrollY, w, h - scrollY);
            float cx = PAD * 2f;
            w -= UGUIShip.SCROLLBAR_INSET * 2f + PAD * 4f;
            float cy = PAD * 2f;

            BuildColorBlock(content, cx, ref cy, w, "ui.top_color", () => _scTop, c => _scTop = c, CommitScreen, ScreenBackgroundService.DefaultTop(s));
            Divider(content, cx, ref cy, w);
            BuildColorBlock(content, cx, ref cy, w, "ui.bottom_color", () => _scBot, c => _scBot = c, CommitScreen, ScreenBackgroundService.DefaultBot(s));
            Divider(content, cx, ref cy, w);

            UGUIShip.CreateLabel(content, new Rect(cx, cy, w, LH), "ui.gradient_shape", FS_SM, HINT);
            cy += LH + SH;
            float shapeLblW = FS_SM * 7f;
            BuildSliderRaw(content, cx, cy, w, "themes.bias", _scBias, -1f, 1f, v => { _scBias = v; CommitScreen(); }, 0f, shapeLblW);
            cy += LH + SH;
            BuildSliderRaw(content, cx, cy, w, "themes.smooth", _scSmooth, 0.1f, 8f, v => { _scSmooth = v; CommitScreen(); }, ScreenBackgroundService.DefaultSmooth(s), shapeLblW);
            cy += LH;
            Divider(content, cx, ref cy, w);

            bool hasPattern = s != ScreenBackgroundService.Screen.ShowSelector;
            if (hasPattern)
            {
                UGUIShip.CreateLabel(content, new Rect(cx, cy, w, LH), "ui.pattern", FS_SM, HINT);
                cy += LH + SH;
                float patBtnW = BTN_H * 3f;
                float patLblW = w - patBtnW - PAD;
                UGUIShip.CreateLabel(content, new Rect(cx, cy, patLblW, BTN_H),
                    ScreenBackgroundService.PatternDisplayName(_scPattern), FS_SM, WHITE, TextAnchor.MiddleLeft);
                UGUIShip.CreateButton(content, new Rect(cx + patLblW + PAD, cy, patBtnW, BTN_H),
                    "ui.browse", BTN_DARK, WHITE, FS_SM, new Action(() =>
                    {
                        var t = BetterFGTabRegistry.NewTab<UIPatternPickerTab>();
                        t.Screen = _screenSel;
                        BetterFGUIMan.Instance?.SwitchSlotTab(this, t);
                    }));
                cy += BTN_H + PAD;
            }

            var defPat = ScreenBackgroundService.DefaultPatternColor(s);
            BuildColorBlock(content, cx, ref cy, w, hasPattern ? "ui.pattern_color" : "ui.circles_color",
                () => _scPatternCol, c => _scPatternCol = c, CommitScreen, defPat);
            cy -= PAD;
            BuildSliderRaw(content, cx, cy, w, "themes.alpha", _scPatternCol.a, 0f, 1f, v => { _scPatternCol.a = v; CommitScreen(); }, defPat.a);
            cy += LH;
            BuildResetButton(content, cx, ref cy, w);
            content.sizeDelta = new Vector2(0f, cy);
        }

        private Transform ScreenPreviewSource(ScreenBackgroundService.Screen s, out bool ownGeometry)
        {
            Transform t;
            switch (s)
            {
                case ScreenBackgroundService.Screen.LoadingLevel:
                case ScreenBackgroundService.Screen.Explore:
                case ScreenBackgroundService.Screen.FinalRound:
                    t = ScreenBackgroundService.FindPreviewSource(s);
                    break;
                case ScreenBackgroundService.Screen.ShowSelector:
                    t = BetterFG.Patches.ShowSelectorBg.FindLiveMask();
                    break;
                default:
                    t = ScreenBackgroundService.FindPreviewSource(ScreenBackgroundService.Screen.LoadingLevel);
                    break;
            }
            ownGeometry = t != null && s != ScreenBackgroundService.Screen.FallForce;
            return t;
        }

        // clones the live Backdrop+Circles container once so the preview shows the real shader/pattern,
        // not an approximation. lives in its own framed slot above the scroll area.
        private void BuildScreenPreview(Transform previewSlot, float w)
        {
            if (_scPreviewGo != null) GameObject.Destroy(_scPreviewGo);
            _scPreviewGo = null; _scPreviewBackdrop = null; _scPreviewCircles = null; _scPreviewDefaultPattern = null;

            var source = ScreenPreviewSource(_screenSel, out bool ownGeometry);
            if (source == null) return; // not in the main menu right now, nothing live to clone

            // this screen's OWN container, found live — learn its real default right now, no need to
            // have actually lived through it this session (LoadingLevel/Explore/ShowSelector are all
            // pre-instantiated and inactive off the menu scene, same as ShowSelectorBg already relied on).
            if (_screenSel != ScreenBackgroundService.Screen.FallForce && ownGeometry)
                ScreenBackgroundService.CacheScreenDefault(_screenSel, source);

            var holderRt = (RectTransform)previewSlot;
            _scPreviewGo = GameObject.Instantiate(source.gameObject, holderRt, false);
            _scPreviewGo.name = "ScreenPreviewClone";
            FitCloneToPreview(_scPreviewGo, source, w, SCREEN_PREVIEW_H);

            foreach (var anim in _scPreviewGo.GetComponentsInChildren<Animator>(true))
                if (anim != null) anim.enabled = false;
            // the framed preview slot sits under a real UI Mask now (rounded corners), not just a
            // RectMask2D — Mask makes MaskableGraphic swap in a cached stencil COPY of the material,
            // taken once and never refreshed, so every SetTexture/color we do below in Refresh*Preview
            // kept painting a material nobody was actually rendering. maskable=false skips that copy;
            // the RectMask2D still clips the box, just square-cornered instead of rounded.
            foreach (var g in _scPreviewGo.GetComponentsInChildren<Graphic>(true))
                if (g != null) { g.raycastTarget = false; if (g is MaskableGraphic mg) mg.maskable = false; }

            _scPreviewBackdrop = _scPreviewGo.transform.Find("Backdrop")?.GetComponent<Image>();
            _scPreviewCircles = ScreenBackgroundService.FindCirclesChild(_scPreviewGo.transform)?.GetComponent<Image>();
            _scPreviewDefaultPatternSprite = _scPreviewCircles != null ? _scPreviewCircles.sprite : null;
            _scPreviewDefaultPatternType = _scPreviewCircles != null ? _scPreviewCircles.type : Image.Type.Simple;

            // Image.material does NOT auto-instance like Renderer.material does — Instantiate leaves the
            // clone pointing at the SAME shared Material asset the live menu's real Circles uses. every
            // SetTexture below would otherwise paint the live game's actual background, not just this
            // preview. give the clone its own copy before touching it.
            if (_scPreviewCircles != null && _scPreviewCircles.material != null)
            {
                if (_scPreviewCirclesMat != null) Destroy(_scPreviewCirclesMat);
                _scPreviewCirclesMat = new Material(_scPreviewCircles.material);
                _scPreviewCircles.material = _scPreviewCirclesMat;
            }

            // FallForce's Backdrop, like Circles, IS mutated live by ApplyGradientFromSettings — if a
            // custom gradient is already active this session, reading the clone's sprite straight off
            // would hand us back our OWN texture as the "default". route through the guarded capture
            if (_screenSel == ScreenBackgroundService.Screen.FallForce && _scPreviewBackdrop != null)
            {
                _scPreviewDefaultBackdrop = MenuCustomizationApplication.Instance?.EnsureOriginalBackdropSpriteCaptured() ?? _scPreviewBackdrop.sprite;
                _scPreviewDefaultBackdropColor = Color.white;
            }
            _scPreviewDefaultPattern = MenuCustomizationApplication.Instance?.EnsureOriginalCirclesPatternCaptured();
            if (_screenSel == ScreenBackgroundService.Screen.FallForce)
            {
                _scPreviewDefaultCirclesColor = ScreenBackgroundService.DefaultPatternColor(_screenSel);
                if (_scPreviewDefaultPattern == null && _scPreviewCirclesMat != null)
                    _scPreviewDefaultPattern = _scPreviewCirclesMat.GetTexture("_Pattern");
            }
            else if (ScreenBackgroundService.TryGetScreenDefaultCirclesColor(_screenSel, out var defCol))
                _scPreviewDefaultCirclesColor = defCol;
            else if (_scPreviewCircles != null)
                _scPreviewDefaultCirclesColor = _scPreviewCircles.color;

            _scPreviewHintLabel = UGUIShip.CreateLabel(holderRt, new Rect(PAD, 0f, w - PAD * 2f, SCREEN_PREVIEW_H),
                "ui.haven_t_seen_this_screen_s_real_default_yet_this",
                FS_SM, HINT, TextAnchor.MiddleCenter);

            RefreshScreenPreview();
        }

        private static void FitCloneToPreview(GameObject clone, Transform source, float w, float h)
        {
            var cloneRt = clone != null ? clone.GetComponent<RectTransform>() : null;
            if (cloneRt == null) return;
            var srcRt = source != null ? source.GetComponent<RectTransform>() : null;
            Vector2 natSize = srcRt != null ? srcRt.sizeDelta : Vector2.zero;
            if (natSize.x <= 1f || natSize.y <= 1f) natSize = new Vector2(1920f, 1080f);
            float fit = Mathf.Max(w / natSize.x, h / natSize.y);
            cloneRt.localRotation = Quaternion.identity;
            cloneRt.anchorMin = cloneRt.anchorMax = new Vector2(0.5f, 0.5f);
            cloneRt.pivot = new Vector2(0.5f, 0.5f);
            cloneRt.sizeDelta = natSize;
            cloneRt.anchoredPosition = Vector2.zero;
            cloneRt.localScale = new Vector3(fit, fit, 1f);
        }

        private void RefreshScreenPreview()
        {
            bool isFallForce = _screenSel == ScreenBackgroundService.Screen.FallForce;
            bool customGradient = _scEnabled && !ScreenBackgroundService.GradientIsDefault(_screenSel, _scTop, _scBot, _scBias, _scSmooth);

            if (_scPreviewBackdrop != null)
            {
                if (customGradient)
                {
                    if (_scPreviewGradTex != null) Destroy(_scPreviewGradTex);
                    _scPreviewGradTex = ScreenBackgroundService.BuildGradientTex(_scTop, _scBot, _scBias, _scSmooth);
                    _scPreviewBackdrop.sprite = Sprite.Create(_scPreviewGradTex,
                        new Rect(0, 0, _scPreviewGradTex.width, _scPreviewGradTex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                    _scPreviewBackdrop.color = Color.white;
                }
                else if (isFallForce)
                {
                    _scPreviewBackdrop.sprite = _scPreviewDefaultBackdrop;
                    _scPreviewBackdrop.color = _scPreviewDefaultBackdropColor;
                }
                else if (ScreenBackgroundService.TryGetScreenDefault(_screenSel, out var cachedSprite, out var cachedColor, out _))
                {
                    _scPreviewBackdrop.sprite = cachedSprite;
                    _scPreviewBackdrop.color = cachedColor;
                }
                else
                {
                    // never seen this screen live yet this session — no default to show, say so instead
                    // of guessing. it'll self-correct once you've actually hit that loading screen once.
                    _scPreviewBackdrop.sprite = null;
                    _scPreviewBackdrop.color = new Color(0f, 0f, 0f, 0.35f);
                }
            }

            bool knownDefault = isFallForce || ScreenBackgroundService.TryGetScreenDefault(_screenSel, out _, out _, out _);
            if (_scPreviewHintLabel != null)
                _scPreviewHintLabel.gameObject.SetActive(!customGradient && !knownDefault);

            if (_scPreviewCircles != null && _scPreviewCircles.material != null)
            {
                if (_scEnabled && !string.IsNullOrEmpty(_scPattern) && _scPreviewPatternPath != _scPattern)
                {
                    if (_scPreviewPatternTex != null) Destroy(_scPreviewPatternTex);
                    _scPreviewPatternTex = ScreenBackgroundService.LoadPatternTexture(_scPattern);
                    if (_scPreviewPatternTex != null && _scPreviewDefaultPattern != null)
                        _scPreviewPatternTex = ScreenBackgroundService.MatchPatternSize(_scPreviewPatternTex, _scPreviewDefaultPattern);
                    _scPreviewPatternPath = _scPattern;
                }

                // FinalRoundBackground's "Pattern" node has no _Pattern property on its material at all
                // (UI/ScrollingSprite2.0 paints from Image.sprite instead) — same split as ApplyToContainer.
                if (_scPreviewCircles.material.HasProperty("_Pattern"))
                {
                    Texture patternTex = _scPreviewDefaultPattern;
                    if (!isFallForce && ScreenBackgroundService.TryGetScreenDefault(_screenSel, out _, out _, out var cachedPattern))
                        patternTex = cachedPattern;
                    if (_scEnabled && !string.IsNullOrEmpty(_scPattern) && _scPreviewPatternTex != null) patternTex = _scPreviewPatternTex;
                    _scPreviewCircles.material.SetTexture("_Pattern", patternTex);
                }
                else if (_scEnabled && !string.IsNullOrEmpty(_scPattern) && _scPreviewPatternTex != null)
                {
                    _scPreviewCircles.sprite = Sprite.Create(_scPreviewPatternTex,
                        new Rect(0, 0, _scPreviewPatternTex.width, _scPreviewPatternTex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                    _scPreviewCircles.type = Image.Type.Tiled;
                    _scPreviewCircles.pixelsPerUnitMultiplier = ScreenBackgroundService.FinalRoundPatternTileScale;
                }
                else
                {
                    _scPreviewCircles.sprite = _scPreviewDefaultPatternSprite;
                    _scPreviewCircles.type = _scPreviewDefaultPatternType;
                }
                _scPreviewCircles.color = _scEnabled ? _scPatternCol : _scPreviewDefaultCirclesColor;
            }
        }

        private void LoadScreenSettings(ScreenBackgroundService.Screen s)
        {
            _scTop = ScreenBackgroundService.TopColor(s);
            _scBot = ScreenBackgroundService.BotColor(s);
            _scBias = ScreenBackgroundService.Bias(s);
            _scSmooth = ScreenBackgroundService.Smooth(s);
            _scEnabled = ScreenBackgroundService.Enabled(s);
            _scPattern = SettingsService.Get(ScreenBackgroundService.KeyPattern(s), "");
            _scPatternCol = ScreenBackgroundService.PatternColor(s);
        }

        private void CommitScreen()
        {
            var s = _screenSel;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            void S(string k, float v) => SettingsService.Set(k, v.ToString(ci));
            S(ScreenBackgroundService.KeyTopR(s), _scTop.r);
            S(ScreenBackgroundService.KeyTopG(s), _scTop.g);
            S(ScreenBackgroundService.KeyTopB(s), _scTop.b);
            S(ScreenBackgroundService.KeyBotR(s), _scBot.r);
            S(ScreenBackgroundService.KeyBotG(s), _scBot.g);
            S(ScreenBackgroundService.KeyBotB(s), _scBot.b);
            S(ScreenBackgroundService.KeyBias(s), _scBias);
            S(ScreenBackgroundService.KeySmooth(s), _scSmooth);
            S(ScreenBackgroundService.KeyPatternR(s), _scPatternCol.r);
            S(ScreenBackgroundService.KeyPatternG(s), _scPatternCol.g);
            S(ScreenBackgroundService.KeyPatternB(s), _scPatternCol.b);
            S(ScreenBackgroundService.KeyPatternA(s), _scPatternCol.a);
            ApplyScreenLive();
            RefreshScreenPreview();
        }

        // push the selected screen's saved state to whatever is showing right now.
        // FallForce = the menu/title; loading screens = the active loading screen if one is up.
        private void ApplyScreenLive()
        {
            switch (_screenSel)
            {
                case ScreenBackgroundService.Screen.FallForce:
                    MenuCustomizationApplication.Instance?.ApplyGradientFromSettings();
                    MenuCustomizationApplication.Instance?.ApplyPatternFromSettings();
                    break;
                case ScreenBackgroundService.Screen.ShowSelector:
                    BetterFG.Patches.ShowSelectorBg.ReapplyLive();
                    break;
                default:
                    BetterFG.Patches.LoadingScreenBg.ReapplyActive();
                    break;
            }
        }

        // ── Falling screen (lobby bg) body ────────────────────────────────────
        // recolours the named DarkBlue/MedBlue/LightBlue images in Menu_Screen_Lobby. moved here out
        // of the Main Menu tab so the falling-screen colours live with the other screens.

        private static readonly string[] FallingSlotLabels = { "ui.dark_blue", "ui.med_blue", "ui.light_blue" };

        private GameObject _lbPreviewGo;
        private readonly List<(Image img, int slot, Color color, Sprite sprite)> _lbPreview = new List<(Image, int, Color, Sprite)>();

        private void BuildFallingBody(RectTransform parent, float x, float w, float h)
        {
            BuildFallingPreview(BuildPreviewFrame(parent, x, 0f, w), w);

            float scrollY = SCREEN_PREVIEW_H + PREVIEW_GAP;
            var (_, content) = BuildFramedScroll(parent, x, scrollY, w, h - scrollY);
            float cx = PAD * 2f;
            w -= UGUIShip.SCROLLBAR_INSET * 2f + PAD * 4f;
            float cy = PAD * 2f;

            for (int i = 0; i < 3; i++)
            {
                int slot = i;
                BuildColorBlock(content, cx, ref cy, w, FallingSlotLabels[slot], () => _lb[slot], c => _lb[slot] = c,
                    CommitFalling, MenuCustomizationApplication.LobbyBgDefaults[slot]);
                if (slot < 2) Divider(content, cx, ref cy, w);
            }

            BuildResetButton(content, cx, ref cy, w);
            content.sizeDelta = new Vector2(0f, cy);
        }

        private void LoadFallingSettings()
        {
            _lbEnabled = SettingsService.Get(MenuCustomizationApplication.KEY_LOBBYBG_ENABLED, "false") == "true";
            for (int i = 0; i < 3; i++) _lb[i] = MenuCustomizationApplication.LobbyBgSlotColor(i);
        }

        private void CommitFalling()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            for (int i = 0; i < 3; i++)
            {
                string k = $"menu.lobbybg.slot{i}";
                SettingsService.Set(k + ".r", _lb[i].r.ToString(ci));
                SettingsService.Set(k + ".g", _lb[i].g.ToString(ci));
                SettingsService.Set(k + ".b", _lb[i].b.ToString(ci));
            }
            MenuCustomizationApplication.Instance?.ApplyLobbyBGForeground();
            RefreshFallingPreview();
        }

        private void BuildFallingPreview(Transform previewSlot, float w)
        {
            if (_lbPreviewGo != null) GameObject.Destroy(_lbPreviewGo);
            _lbPreviewGo = null;
            _lbPreview.Clear();

            var source = MenuCustomizationApplication.FindLobbyBgPreviewSource();
            if (source == null) return;

            var holderRt = (RectTransform)previewSlot;
            _lbPreviewGo = GameObject.Instantiate(source.gameObject, holderRt, false);
            _lbPreviewGo.name = "FallingPreviewClone";
            _lbPreviewGo.SetActive(true);

            foreach (var canvas in _lbPreviewGo.GetComponentsInChildren<Canvas>(true))
                if (canvas != null) Destroy(canvas);
            var scaler = _lbPreviewGo.GetComponent<CanvasScaler>();
            if (scaler != null) Destroy(scaler);
            var raycaster = _lbPreviewGo.GetComponent<GraphicRaycaster>();
            if (raycaster != null) Destroy(raycaster);

            FitCloneToPreview(_lbPreviewGo, source, w, SCREEN_PREVIEW_H);

            // see BuildScreenPreview: skip the Mask's stencil-material caching so live colour/sprite
            // updates below actually show up.
            foreach (var g in _lbPreviewGo.GetComponentsInChildren<Graphic>(true))
                if (g != null) { g.raycastTarget = false; if (g is MaskableGraphic mg) mg.maskable = false; }

            var app = MenuCustomizationApplication.Instance;
            var srcImages = source.GetComponentsInChildren<Image>(true);
            var cloneImages = _lbPreviewGo.GetComponentsInChildren<Image>(true);
            int n = Mathf.Min(srcImages.Length, cloneImages.Length);
            for (int i = 0; i < n; i++)
            {
                var srcImg = srcImages[i];
                var cloneImg = cloneImages[i];
                if (cloneImg == null || srcImg == null) continue;
                int slot = MenuCustomizationApplication.LobbyBgSlotIndex(cloneImg.gameObject.name);
                var trueColor = app != null ? app.TrueLobbyBgColor(srcImg) : srcImg.color;
                var trueSprite = app != null ? app.TrueLobbyBgSprite(srcImg) : srcImg.sprite;
                cloneImg.sprite = trueSprite;
                cloneImg.color = trueColor;
                if (slot >= 0) _lbPreview.Add((cloneImg, slot, trueColor, trueSprite));
            }

            RefreshFallingPreview();
        }

        private void RefreshFallingPreview()
        {
            foreach (var (img, slot, def, sprite) in _lbPreview)
            {
                if (img == null) continue;
                var c = _lb[slot];
                bool custom = _lbEnabled && def.a >= 0.05f && !ScreenBackgroundService.NearColor(c, MenuCustomizationApplication.LobbyBgDefaults[slot]);
                img.sprite = custom ? null : sprite;
                img.color = custom ? new Color(c.r, c.g, c.b, def.a) : def;
            }
        }

        // ── Creative (level browser) body ─────────────────────────────────────
        // four named colour slots on Generic_UI_CreativeBackground_Prefab_Canvas. see
        // MenuCustomizationApplication.CreativeSlot.
        private static readonly string[] CreativeSlotLabels = { "BACKDROP", "GLOWS", "DRAWINGS", "VIGNETTE" };

        private GameObject _crPreviewGo;
        private readonly List<(Graphic g, MenuCustomizationApplication.CreativeSlot slot)> _crPreviewGraphics =
            new List<(Graphic, MenuCustomizationApplication.CreativeSlot)>();
        private readonly List<Color> _crPreviewDefaults = new List<Color>();

        private void BuildCreativeBody(RectTransform parent, float x, float w, float h)
        {
            BuildCreativePreview(BuildPreviewFrame(parent, x, 0f, w), w);

            float scrollY = SCREEN_PREVIEW_H + PREVIEW_GAP;
            var (_, content) = BuildFramedScroll(parent, x, scrollY, w, h - scrollY);
            float cx = PAD * 2f;
            w -= UGUIShip.SCROLLBAR_INSET * 2f + PAD * 4f;
            float cy = PAD * 2f;

            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                BuildColorBlock(content, cx, ref cy, w, CreativeSlotLabels[slot], () => _cr[slot], c => _cr[slot] = c,
                    CommitCreative, MenuCustomizationApplication.CreativeSlotDefault((MenuCustomizationApplication.CreativeSlot)slot));
                if (slot < 3) Divider(content, cx, ref cy, w);
            }

            BuildResetButton(content, cx, ref cy, w);
            content.sizeDelta = new Vector2(0f, cy);
        }

        // clones the live level-editor creative canvas so the preview shows the real paper-craft art,
        // not swatches. lives in its own framed slot above the scroll area.
        private void BuildCreativePreview(Transform previewSlot, float w)
        {
            if (_crPreviewGo != null) GameObject.Destroy(_crPreviewGo);
            _crPreviewGo = null;
            _crPreviewGraphics.Clear();
            _crPreviewDefaults.Clear();

            var source = MenuCustomizationApplication.FindCreativePreviewSource();
            if (source == null) return; // level editor view has never been up this session

            var holderRt = (RectTransform)previewSlot;
            _crPreviewGo = GameObject.Instantiate(source.gameObject, holderRt, false);
            _crPreviewGo.name = "CreativePreviewClone";

            // it's a world-space Canvas prefab (lives on a 3D CameraRig), not a plain UI group like the
            // Screen preview's Mask — its own Canvas component and baked-in world-space localScale both
            // fight our anchor-stretch, which is what made it render tiny. strip the Canvas so it just
            // renders as normal children of our own UI's canvas, and reset the scale explicitly.
            foreach (var canvas in _crPreviewGo.GetComponentsInChildren<Canvas>(true))
                if (canvas != null) Destroy(canvas);
            var scaler = _crPreviewGo.GetComponent<CanvasScaler>();
            if (scaler != null) Destroy(scaler);
            var raycaster = _crPreviewGo.GetComponent<GraphicRaycaster>();
            if (raycaster != null) Destroy(raycaster);

            FitCloneToPreview(_crPreviewGo, source, w, SCREEN_PREVIEW_H);

            foreach (var g in _crPreviewGo.GetComponentsInChildren<Graphic>(true))
                if (g != null) { g.raycastTarget = false; if (g is MaskableGraphic mg) mg.maskable = false; }

            // pair up clone graphics with the SOURCE's true (untainted) colour, slot by slot — the
            // clone's own graphics may already be sitting mid-custom-colour if the source canvas was
            // recoloured earlier this session, same taint risk the Screen preview's pattern had.
            MenuCustomizationApplication.ForEachCreativeSlotGraphic(source,
                (g, slot) => _crPreviewDefaults.Add(MenuCustomizationApplication.Instance?.TrueCreativeColor(g) ?? g.color));
            MenuCustomizationApplication.ForEachCreativeSlotGraphic(_crPreviewGo.transform,
                (g, slot) => _crPreviewGraphics.Add((g, slot)));

            RefreshCreativePreview();
        }

        private void RefreshCreativePreview()
        {
            for (int i = 0; i < _crPreviewGraphics.Count && i < _crPreviewDefaults.Count; i++)
            {
                var (g, slot) = _crPreviewGraphics[i];
                if (g == null) continue;
                var c = _cr[(int)slot];
                if (!_crEnabled || ScreenBackgroundService.NearColor(c, MenuCustomizationApplication.CreativeSlotDefault(slot)))
                    c = _crPreviewDefaults[i];
                g.color = new Color(c.r, c.g, c.b, g.color.a);
            }
        }

        private void LoadCreativeSettings()
        {
            _crEnabled = SettingsService.Get(MenuCustomizationApplication.KEY_CREATIVE_ENABLED, "false") == "true";
            for (int i = 0; i < 4; i++) _cr[i] = MenuCustomizationApplication.CreativeSlotColor((MenuCustomizationApplication.CreativeSlot)i);
        }

        private void CommitCreative()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            for (int i = 0; i < 4; i++)
            {
                string k = "screen.creative." + (MenuCustomizationApplication.CreativeSlot)i;
                SettingsService.Set(k + ".r", _cr[i].r.ToString(ci));
                SettingsService.Set(k + ".g", _cr[i].g.ToString(ci));
                SettingsService.Set(k + ".b", _cr[i].b.ToString(ci));
            }
            MenuCustomizationApplication.Instance?.ReapplyCreativeBgLive();
            RefreshCreativePreview();
        }

        private Slider BuildSliderRaw(Transform parent, float x, float y, float w,
            string lbl, float init, float min, float max, Action<float> onChange, float resetTo, float minLabelW = 0f)
            => UGUIShip.CreateSlider(parent, x, y, w, lbl, Mathf.InverseLerp(min, max, init),
                LH, PAD, FS_SM, t => onChange(Mathf.Lerp(min, max, t)), null, null, true,
                Mathf.InverseLerp(min, max, resetTo), minLabelW);
    }
}
