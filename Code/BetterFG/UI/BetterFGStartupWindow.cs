using System;
using System.Collections;
using System.Reflection;
using BetterFG.Services;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using FallGuysLib.Utils;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI
{
    public class BetterFGStartupWindow : MonoBehaviour
    {
        public BetterFGStartupWindow(IntPtr ptr) : base(ptr) { }

        // ── Timing constants ───────────────────────────────────────────────────
        private const float LOGO_WAIT = 3f;
        private const float LOGO_FADE_IN = 2f;
        private const float LOGO_FADE_OUT = 2f;
        private const float LANG_FADE_IN = 1.2f;
        private const float LANG_FADE_OUT = 0.6f;
        private const float DISC_FADE_IN = 2f;
        private const float BTN_DELAY = 2f;
        private const float REVEAL_DUR = 1f;
        private const float ONBOARD_HANDOFF = 1f;

        private const float LOGO_SCALE_FROM = 1.00f;
        private const float LOGO_SCALE_TO = 1.08f;
        private const float LOGO_SCALE_END = 1.16f;

        // ── Layout ─────────────────────────────────────────────────────────────
        private const float LOGO_W = 420f;
        private const float LOGO_H = 160f;
        private const float DISC_W = 720f;
        private const float DISC_H = 72f;
        private const float BTN_W = 190f;
        private const float BTN_H = 34f;
        private const float LANG_W = 460f;
        private const float LANG_H = 194f;
        private const float LANG_DD_W = 240f;
        private const float LANG_DD_H = 30f;
        private const int FS_DISC = 21;
        private const int FS_BTN = 13;

        // ── Keys ───────────────────────────────────────────────────────────────
        public const string KEY_SEEN = "startup.seen";

        // ── Colors ─────────────────────────────────────────────────────────────
        private static readonly Color WHITE = UGUIShip.WHITE;
        private static readonly Color BTN_COLOR = new Color(0.12f, 0.12f, 0.14f, 1f);
        private static readonly Color BTN_TEXT = new Color(0.95f, 0.95f, 0.95f, 1f);

        // ── Resource paths ─────────────────────────────────────────────────────
        private const string BUNDLE_RES = "BetterFG.assets.bundles.betterfg_startup";
        private const string LOGO_RES = "BetterFG.assets.ui.betterfglogo.png";

        // ── Runtime refs ───────────────────────────────────────────────────────
        private Canvas _canvas;
        private CanvasGroup _logoGroup;
        private RectTransform _logoRt;
        private CanvasGroup _discGroup;
        private CanvasGroup _btnGroup;
        private GameObject _langGo;
        private CanvasGroup _langGroup;
        private Button _langBtn;
        private string[] _langCodes;
        private System.Collections.Generic.List<string> _langNames;
        private bool _langDone;
        private Image _revealerImg;
        private Material _revealMat;

        private static Texture2D _logoTex;

        // ──────────────────────────────────────────────────────────────────────
        public static BetterFGStartupWindow Show()
        {
            if (SettingsService.Get(KEY_SEEN) == "true") return null;
            SettingsService.Set(KEY_SEEN, "true");

            var go = new GameObject("BetterFG_Startup");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            return go.AddComponent<BetterFGStartupWindow>();
        }

        void Awake()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 0f;

            // Load bundle — shader "wow/ui/reveal" lives inside it
            AssetBundleUtils.LoadEmbeddedBundle(Assembly.GetExecutingAssembly(), BUNDLE_RES);

            BuildCanvas();
            StartCoroutine(Sequence().WrapToIl2Cpp());
        }

        // ── Build ─────────────────────────────────────────────────────────────

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("StartupCanvas");
            canvasGo.hideFlags = HideFlags.HideAndDontSave;
            canvasGo.transform.SetParent(transform, false);
            UnityEngine.Object.DontDestroyOnLoad(canvasGo);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 1001;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            BetterFG.Services.UIScaleService.Register(_canvas);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            BuildRevealer(canvasGo.transform);
            BuildLogo(canvasGo.transform);
            BuildLanguage(canvasGo.transform);
            BuildDisclaimer(canvasGo.transform);
        }

        private void BuildLanguage(Transform canvasT)
        {
            _langGo = new GameObject("Language");
            _langGo.transform.SetParent(canvasT, false);

            var rt = _langGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(LANG_W, LANG_H);
            rt.anchoredPosition = Vector2.zero;

            _langGroup = _langGo.AddComponent<CanvasGroup>();
            _langGroup.alpha = 0f;
            _langGroup.blocksRaycasts = false;
            _langGroup.interactable = false;

            UGUIShip.CreateLabel(_langGo.transform,
                new Rect(0f, 0f, LANG_W, 32f),
                "startup.choose_language", FS_DISC, WHITE, TextAnchor.MiddleCenter);

            _langCodes = LocalizationService.AvailableLanguages;
            _langNames = new System.Collections.Generic.List<string>();
            var initial = new System.Collections.Generic.List<bool>();
            foreach (var c in _langCodes)
            {
                _langNames.Add(LocalizationService.DisplayName(c));
                initial.Add(c == LocalizationService.CurrentLanguage);
            }

            _langBtn = UGUIShip.CreateMultiSelectDropdown(_langGo.transform,
                new Rect((LANG_W - LANG_DD_W) * 0.5f, 48f, LANG_DD_W, LANG_DD_H),
                LocalizationService.DisplayName(LocalizationService.CurrentLanguage),
                _langNames, initial, new Action<int, bool>(OnLanguagePicked),
                FS_BTN, LANG_DD_W, 22f, true, true);

            UGUIShip.CreateButton(_langGo.transform,
                new Rect((LANG_W - BTN_W) * 0.5f, 160f, BTN_W, BTN_H),
                "startup.continue", BTN_COLOR, BTN_TEXT, FS_BTN,
                new Action(OnLanguageChosen));

            _langGo.SetActive(false);
        }

        private void OnLanguagePicked(int index, bool on)
        {
            if (index < 0 || index >= _langCodes.Length) return;
            LocalizationService.SetLanguage(_langCodes[index]);
            var lbl = _langBtn.GetComponentInChildren<Text>();
            if (lbl != null) UGUIShip.RelabelText(lbl, _langNames[index]);
        }

        private void OnLanguageChosen()
        {
            _langGroup.interactable = false;
            _langGroup.blocksRaycasts = false;
            _langDone = true;
        }

        private void BuildLogo(Transform canvasT)
        {
            var logoGo = new GameObject("Logo");
            logoGo.transform.SetParent(canvasT, false);

            _logoRt = logoGo.AddComponent<RectTransform>();
            _logoRt.anchorMin = new Vector2(0.5f, 0.5f);
            _logoRt.anchorMax = new Vector2(0.5f, 0.5f);
            _logoRt.pivot = new Vector2(0.5f, 0.5f);
            _logoRt.sizeDelta = new Vector2(LOGO_W, LOGO_H);
            _logoRt.anchoredPosition = Vector2.zero;

            _logoGroup = logoGo.AddComponent<CanvasGroup>();
            _logoGroup.alpha = 0f;
            _logoGroup.blocksRaycasts = false;

            var img = logoGo.AddComponent<RawImage>();
            img.texture = LoadLogoTexture();
        }

        private void BuildDisclaimer(Transform canvasT)
        {
            var discGo = new GameObject("Disclaimer");
            discGo.transform.SetParent(canvasT, false);

            var discRt = discGo.AddComponent<RectTransform>();
            discRt.anchorMin = new Vector2(0.5f, 0.5f);
            discRt.anchorMax = new Vector2(0.5f, 0.5f);
            discRt.pivot = new Vector2(0.5f, 0.5f);
            discRt.anchoredPosition = new Vector2(0f, -8f);
            discRt.sizeDelta = new Vector2(DISC_W, DISC_H + BTN_H + 20f);

            _discGroup = discGo.AddComponent<CanvasGroup>();
            _discGroup.alpha = 0f;
            _discGroup.blocksRaycasts = false;

            var vlg = discGo.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 14f;
            vlg.childAlignment = TextAnchor.MiddleCenter;

            // Disclaimer text
            var t = UGUIShip.CreateFlowLabel(discGo.transform,
                "ui.every_single_change_in_customization_you_make_fr",
                FS_DISC, WHITE);
            t.alignment = TextAnchor.MiddleCenter;
            t.GetComponent<LayoutElement>().preferredHeight = DISC_H;

            // Button wrapper (separate CanvasGroup for delayed fade-in)
            var btnWrap = new GameObject("BtnWrap");
            btnWrap.transform.SetParent(discGo.transform, false);
            btnWrap.AddComponent<RectTransform>();
            btnWrap.AddComponent<LayoutElement>().preferredHeight = BTN_H;

            _btnGroup = btnWrap.AddComponent<CanvasGroup>();
            _btnGroup.alpha = 0f;
            _btnGroup.blocksRaycasts = false;
            _btnGroup.interactable = false;

            var hLayout = btnWrap.AddComponent<HorizontalLayoutGroup>();
            hLayout.childForceExpandWidth = false;
            hLayout.childForceExpandHeight = false;
            hLayout.childControlWidth = false;
            hLayout.childControlHeight = false;
            hLayout.childAlignment = TextAnchor.MiddleCenter;

            UGUIShip.CreateButton(btnWrap.transform,
                new Rect(0f, 0f, BTN_W, BTN_H),
                "ui.i_understand", BTN_COLOR, BTN_TEXT, FS_BTN,
                new Action(OnUnderstood));
        }

        private void BuildRevealer(Transform canvasT)
        {
            // Must be last child so it sits on top of bg, logo, disclaimer
            var revGo = new GameObject("revealer");
            revGo.transform.SetParent(canvasT, false);

            var revRt = revGo.AddComponent<RectTransform>();
            revRt.anchorMin = Vector2.zero;
            revRt.anchorMax = Vector2.one;
            revRt.offsetMin = revRt.offsetMax = Vector2.zero;

            _revealerImg = revGo.AddComponent<Image>();
            _revealerImg.raycastTarget = false;
            _revealerImg.color = Color.black;

            TryAssignRevealMaterial();
        }

        private void TryAssignRevealMaterial()
        {
            var shader = Shader.Find("wow/ui/reveal");
            if (shader == null) return;

            _revealMat = new Material(shader);
            _revealMat.SetColor("_Color", Color.black);
            _revealMat.SetFloat("_Progress", 0f);
            _revealMat.SetFloat("_Softness", 0.002f);

            if (_revealerImg != null)
                _revealerImg.material = _revealMat;
        }

        // ── Sequence ──────────────────────────────────────────────────────────

        private IEnumerator Sequence()
        {
            yield return WaitFor(LOGO_WAIT);

            // Logo fade-in + scale
            yield return LogoFadeScale(0f, 1f, LOGO_SCALE_FROM, LOGO_SCALE_TO, LOGO_FADE_IN);

            // Logo fade-out
            yield return LogoFadeScale(1f, 0f, LOGO_SCALE_TO, LOGO_SCALE_END, LOGO_FADE_OUT);
            _logoRt.gameObject.SetActive(false);

            _langGo.SetActive(true);
            yield return FadeGroup(_langGroup, 0f, 1f, LANG_FADE_IN);
            _langGroup.blocksRaycasts = true;
            _langGroup.interactable = true;
            while (!_langDone) yield return null;
            yield return FadeGroup(_langGroup, 1f, 0f, LANG_FADE_OUT);
            _langGo.SetActive(false);

            // Disclaimer fade-in
            yield return FadeGroup(_discGroup, 0f, 1f, DISC_FADE_IN);
            _discGroup.blocksRaycasts = true;

            // Button delayed fade-in
            yield return WaitFor(BTN_DELAY);
            _btnGroup.blocksRaycasts = true;
            _btnGroup.interactable = true;
            yield return FadeGroup(_btnGroup, 0f, 1f, 0.5f);
        }

        private void OnUnderstood()
        {
            _btnGroup.interactable = false;
            _btnGroup.blocksRaycasts = false;
            StartCoroutine(RevealSequence().WrapToIl2Cpp());
        }

        private IEnumerator RevealSequence()
        {
            // Try assigning shader if it wasn't ready during Awake
            if (_revealMat == null)
                TryAssignRevealMaterial();

            float elapsed = 0f;
            bool handed = false;
            while (elapsed < REVEAL_DUR)
            {
                elapsed += Time.unscaledDeltaTime;
                if (!handed && elapsed >= ONBOARD_HANDOFF)
                {
                    handed = true;
                    Features.Onboarding.OnboardingController.StartupDone = true;
                }
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / REVEAL_DUR));
                SetRevealProgress(t);
                yield return null;
            }
            SetRevealProgress(1f);

            Time.timeScale = 1f;

            if (!handed)
            {
                yield return WaitFor(ONBOARD_HANDOFF - elapsed);
                Features.Onboarding.OnboardingController.StartupDone = true;
            }

            UnityEngine.Object.Destroy(gameObject);
        }

        private void SetRevealProgress(float t)
        {
            if (_revealMat != null)
                _revealMat.SetFloat("_Progress", t);
            else if (_revealerImg != null)
                _revealerImg.color = new Color(0f, 0f, 0f, 1f - t);
        }

        // ── Coroutine helpers ─────────────────────────────────────────────────

        private IEnumerator WaitFor(float seconds)
        {
            float e = 0f;
            while (e < seconds) { e += Time.unscaledDeltaTime; yield return null; }
        }

        private IEnumerator FadeGroup(CanvasGroup cg, float from, float to, float dur)
        {
            float e = 0f;
            cg.alpha = from;
            while (e < dur)
            {
                e += Time.unscaledDeltaTime;
                cg.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(e / dur));
                yield return null;
            }
            cg.alpha = to;
        }

        private IEnumerator LogoFadeScale(float aFrom, float aTo,
            float sFrom, float sTo, float dur)
        {
            float e = 0f;
            _logoGroup.alpha = aFrom;
            _logoRt.localScale = Vector3.one * sFrom;
            while (e < dur)
            {
                e += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(e / dur);
                _logoGroup.alpha = Mathf.Lerp(aFrom, aTo, t);
                float s = Mathf.Lerp(sFrom, sTo, t);
                _logoRt.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            _logoGroup.alpha = aTo;
            _logoRt.localScale = Vector3.one * sTo;
        }

        // ── Texture ───────────────────────────────────────────────────────────

        private static Texture2D LoadLogoTexture()
        {
            if (_logoTex != null) return _logoTex;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var s = asm.GetManifestResourceStream(LOGO_RES);
                if (s == null) return null;
                var b = new byte[s.Length];
                s.Read(b, 0, b.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                ImageConversion.LoadImage(tex, b);
                tex.wrapMode = TextureWrapMode.Clamp;
                _logoTex = tex;
            }
            catch (Exception ex) { Plugin.Log.LogError($"StartupWindow: logo load: {ex.Message}"); }
            return _logoTex;
        }
    }
}
