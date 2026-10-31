using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI
{
    internal static class NewsViewer
    {
        internal struct Page
        {
            public string image, title, text;
        }

        private const float BAR_H = 150f;
        private const float BTN = 64f;
        private const float EDGE = 28f;

        private static Canvas _canvas;
        private static RawImage _img, _blur;
        private static AspectRatioFitter _fit, _blurFit;
        private static readonly Dictionary<string, RenderTexture> _blurs = new Dictionary<string, RenderTexture>();
        private static Text _title, _text, _count;
        private static GameObject _prev, _next, _captionBar;
        private static readonly List<Page> _pages = new List<Page>();
        private static int _i;
        private static bool _busy;
        private static RectTransform _pageRt, _canvasRt;
        private static int _slideDir;
        private static float _slideT;
        private static bool _slidingOut;
        private const float SLIDE_SECONDS = 0.18f;
        private const float PREFETCH_TIMEOUT = 8f;

        public static bool IsOpen => _canvas != null && _canvas.gameObject.activeSelf;

        public static void Open(List<Page> pages)
        {
            if (_busy || IsOpen || pages.Count == 0 || AssetManager.Instance == null) return;
            _pages.Clear();
            _pages.AddRange(pages);
            _i = 0;
            AssetManager.Instance.StartCoroutine(OpenRoutine().WrapToIl2Cpp());
        }

        public static void Close()
        {
            if (_busy || !IsOpen) return;
            AssetManager.Instance.StartCoroutine(CloseRoutine().WrapToIl2Cpp());
        }

        private static IEnumerator OpenRoutine()
        {
            _busy = true;
            LoadingScreenService.Show();
            float started = Time.unscaledTime;
            yield return new WaitForSecondsRealtime(0.35f);
            while (Time.unscaledTime - started < PREFETCH_TIMEOUT)
            {
                bool waiting = false;
                foreach (var p in _pages)
                    if (NewsFeed.PageTexture(p.image) == null && NewsFeed.PageLoading(p.image)) waiting = true;
                if (!waiting) break;
                yield return null;
            }
            foreach (var p in _pages)
            {
                var t = NewsFeed.PageTexture(p.image);
                if (t != null) BlurOf(p.image, t);
            }
            if (_canvas == null) Build();
            _slideDir = 0;
            _pageRt.anchoredPosition = Vector2.zero;
            _canvas.gameObject.SetActive(true);
            BetterFGUIMan.Instance?.SetOverlayVisible(false);
            ShowPage();
            yield return new WaitForSecondsRealtime(0.15f);
            yield return LoadingScreenService.HideRoutine();
            _busy = false;
        }

        private static IEnumerator CloseRoutine()
        {
            _busy = true;
            LoadingScreenService.Show();
            yield return new WaitForSecondsRealtime(0.35f);
            _canvas.gameObject.SetActive(false);
            BetterFGUIMan.Instance?.SetOverlayVisible(true);
            yield return new WaitForSecondsRealtime(0.15f);
            yield return LoadingScreenService.HideRoutine();
            _busy = false;
        }

        public static void Tick()
        {
            if (!IsOpen || _busy) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (Input.GetKeyDown(KeyCode.RightArrow)) Step(1);
            else if (Input.GetKeyDown(KeyCode.LeftArrow)) Step(-1);

            if (_slideDir == 0) return;
            _slideT += Time.unscaledDeltaTime / SLIDE_SECONDS;
            float e = Mathf.Clamp01(_slideT);
            e = e * e * (3f - 2f * e);
            float width = _canvasRt.rect.width;
            if (_slidingOut)
            {
                _pageRt.anchoredPosition = new Vector2(-_slideDir * width * e, 0f);
                if (_slideT < 1f) return;
                _i = (_i + _slideDir + _pages.Count) % _pages.Count;
                ShowPage();
                _slidingOut = false;
                _slideT = 0f;
            }
            else
            {
                _pageRt.anchoredPosition = new Vector2(_slideDir * width * (1f - e), 0f);
                if (_slideT < 1f) return;
                _slideDir = 0;
                _pageRt.anchoredPosition = Vector2.zero;
            }
        }

        public static void Refresh()
        {
            if (IsOpen) ShowPage();
        }

        private static void Step(int d)
        {
            if (_pages.Count < 2 || _slideDir != 0) return;
            _slideDir = d;
            _slideT = 0f;
            _slidingOut = true;
        }

        private static void Build()
        {
            _canvas = UGUIShip.CreateCanvas("BettrFG_NewsViewer");
            _canvas.gameObject.hideFlags = HideFlags.HideAndDontSave;
            _canvas.sortingOrder = LoadingScreenService.DefaultOrder - 1;
            _canvasRt = _canvas.GetComponent<RectTransform>();
            var root = _canvas.transform;

            var bg = Stretch(root, "Backdrop").gameObject.AddComponent<Image>();
            bg.color = Color.black;

            var blurRt = Stretch(root, "Blur");
            _blur = blurRt.gameObject.AddComponent<RawImage>();
            _blur.raycastTarget = false;
            _blurFit = blurRt.gameObject.AddComponent<AspectRatioFitter>();
            _blurFit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;

            _pageRt = Stretch(root, "Page");

            var imgRt = Stretch(_pageRt, "Content");
            _img = imgRt.gameObject.AddComponent<RawImage>();
            _img.raycastTarget = false;
            _fit = imgRt.gameObject.AddComponent<AspectRatioFitter>();
            _fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

            var barRt = Stretch(_pageRt, "Caption");
            barRt.anchorMax = new Vector2(1f, 0f);
            barRt.offsetMax = new Vector2(0f, BAR_H);
            _captionBar = barRt.gameObject;
            var bar = _captionBar.AddComponent<Image>();
            bar.color = new Color(0f, 0f, 0f, 0.9f);
            bar.raycastTarget = false;

            _title = Label(barRt, 34, FontStyle.Bold, new Vector2(EDGE * 3f, -18f), 46f);
            _text = Label(barRt, 22, FontStyle.Normal, new Vector2(EDGE * 3f, -70f), 70f);
            _text.color = new Color(1f, 1f, 1f, 0.82f);

            _count = UGUIShip.CreateLabel(barRt, new Rect(0f, 0f, 160f, 40f), "", 24, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleRight);
            var crt = _count.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.anchoredPosition = new Vector2(-EDGE * 3f, -20f);
            crt.sizeDelta = new Vector2(160f, 40f);

            Corner(root, "✕", new Vector2(1f, 1f), new Vector2(-EDGE, -EDGE), new Color(0.6f, 0.18f, 0.18f, 1f), Close);
            _prev = Corner(root, "<", new Vector2(0f, 0.5f), new Vector2(EDGE, 0f), new Color(0.35f, 0.55f, 0.9f, 1f), () => Step(-1));
            _next = Corner(root, ">", new Vector2(1f, 0.5f), new Vector2(-EDGE, 0f), new Color(0.35f, 0.55f, 0.9f, 1f), () => Step(1));
        }

        private static Texture BlurOf(string key, Texture2D src)
        {
            if (_blurs.TryGetValue(key, out var cached) && cached != null) return cached;
            int w = 48, h = Mathf.Max(1, Mathf.RoundToInt(48f * src.height / src.width));
            var a = RenderTexture.GetTemporary(w * 4, h * 4);
            var b = RenderTexture.GetTemporary(w * 2, h * 2);
            a.filterMode = b.filterMode = FilterMode.Bilinear;
            Graphics.Blit(src, a);
            Graphics.Blit(a, b);
            var rt = new RenderTexture(w, h, 0);
            rt.filterMode = FilterMode.Bilinear;
            rt.wrapMode = TextureWrapMode.Clamp;
            rt.hideFlags = HideFlags.HideAndDontSave;
            Graphics.Blit(b, rt);
            RenderTexture.ReleaseTemporary(a);
            RenderTexture.ReleaseTemporary(b);
            _blurs[key] = rt;
            return rt;
        }

        private static RectTransform Stretch(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static Text Label(RectTransform parent, int size, FontStyle style, Vector2 topLeft, float h)
        {
            var t = UGUIShip.CreateLabel(parent, new Rect(0f, 0f, 100f, h), "", size, Color.white, TextAnchor.UpperLeft);
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            var rt = t.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(topLeft.x, topLeft.y - h);
            rt.offsetMax = new Vector2(-topLeft.x, topLeft.y);
            return t;
        }

        private static GameObject Corner(Transform parent, string glyph, Vector2 anchor, Vector2 pos, Color col, Action onClick)
        {
            var btn = UGUIShip.CreateButton(parent, new Rect(0f, 0f, BTN, BTN), glyph, col, Color.white, 30, onClick);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(BTN, BTN);
            return btn.gameObject;
        }

        private static void ShowPage()
        {
            var p = _pages[_i];
            var tex = NewsFeed.PageTexture(p.image);
            _img.texture = tex;
            _img.color = tex != null ? Color.white : Color.clear;
            _blur.texture = tex != null ? BlurOf(p.image, tex) : null;
            _blur.color = tex != null ? new Color(1f, 1f, 1f, 0.8f) : Color.clear;
            if (tex != null) _fit.aspectRatio = _blurFit.aspectRatio = (float)tex.width / tex.height;

            _title.text = p.title ?? "";
            _text.text = p.text ?? "";
            _captionBar.SetActive(!string.IsNullOrEmpty(p.title) || !string.IsNullOrEmpty(p.text) || _pages.Count > 1);
            _count.text = _pages.Count > 1 ? $"{_i + 1} / {_pages.Count}" : "";
            _prev.SetActive(_pages.Count > 1);
            _next.SetActive(_pages.Count > 1);
        }
    }
}
