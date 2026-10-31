using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.Utilities;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace BetterFG.UI
{
    internal static class NewsFeed
    {
        private const string BaseUrl = "https://assets.bettrfg.com/";
        private const float CARD_W = 278f;
        private const float CARD_H = 116f;
        private const float SLIDE_SECONDS = 0.16f;
        private const float DISMISS_HOVER_DELAY = 2f;
        private const float DISMISS_FADE = 0.25f;
        private const float ARROW_W = 20f;
        private const float ARROW_GAP = 5f;
        private const float ARROW_SIDE = ARROW_W + ARROW_GAP;
        private const float DOT = 6f;
        private const string KEY_ENABLED = "news.feed.on";

        private static readonly List<string> _titles = new List<string>();
        private static readonly List<string> _images = new List<string>();
        private static readonly List<List<NewsViewer.Page>> _pages = new List<List<NewsViewer.Page>>();
        private static Image _glow;
        private static bool _tipShown;
        private static RectTransform _cardRt;
        private static readonly Dictionary<string, Texture2D> _texCache =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _texLoading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded, _loading;

        private static GameObject _card;
        private static RectTransform _rootRt;
        private static RawImage _preview;
        private static Text _title;
        private static RectTransform _previewRt, _stripRt, _titleRt;
        private static CanvasGroup _dismissCg;
        private static RectTransform _dotsRt;
        private static readonly List<Image> _dots = new List<Image>();
        private static int _index;
        private static float _hoverTime;
        private static Vector2 _lastMouse;
        private static bool _dismissShown;
        private static int _slideDir;
        private static float _slideT;
        private static bool _slidingOut;

        public static bool Enabled => SettingsService.Get(KEY_ENABLED, "true") == "true";

        public static void SetEnabled(bool on)
        {
            SettingsService.Set(KEY_ENABLED, on ? "true" : "false");
            if (_card != null) Show();
        }

        public static GameObject Build(Transform parent)
        {
            var root = new GameObject("NewsFeed");
            root.hideFlags = HideFlags.HideAndDontSave;
            root.transform.SetParent(parent, false);
            _rootGo = root;
            _rootCg = root.AddComponent<CanvasGroup>();
            var rt = _rootRt = root.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.one;
            rt.pivot = Vector2.one;
            rt.anchoredPosition = new Vector2(-16f, -16f);
            rt.sizeDelta = new Vector2(CARD_W + ARROW_SIDE * 2f, CARD_H);

            _card = new GameObject("Card");
            _card.transform.SetParent(root.transform, false);
            var cardRt = _cardRt = _card.AddComponent<RectTransform>();
            cardRt.anchorMin = Vector2.zero; cardRt.anchorMax = Vector2.one;
            cardRt.offsetMin = new Vector2(ARROW_SIDE, 0f);
            cardRt.offsetMax = new Vector2(-ARROW_SIDE, 0f);
            _card.AddComponent<RectMask2D>();

            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(_card.transform, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
            var bg = bgGo.AddComponent<Image>();
            bg.color = new Color(0.07f, 0.09f, 0.16f, 0.92f);
            bg.raycastTarget = false;

            var frameGo = new GameObject("Frame");
            frameGo.transform.SetParent(_card.transform, false);
            var frameRt = frameGo.AddComponent<RectTransform>();
            frameRt.anchorMin = Vector2.zero; frameRt.anchorMax = Vector2.one;
            frameRt.offsetMin = frameRt.offsetMax = Vector2.zero;
            var frame = frameGo.AddComponent<Outline>();
            frameGo.AddComponent<Image>().color = Color.clear;
            frame.effectColor = new Color(1f, 1f, 1f, 0.35f);
            frame.effectDistance = new Vector2(1f, -1f);

            var previewGo = new GameObject("Preview");
            previewGo.transform.SetParent(_card.transform, false);
            _previewRt = previewGo.AddComponent<RectTransform>();
            _previewRt.anchorMin = Vector2.zero; _previewRt.anchorMax = Vector2.one;
            _previewRt.offsetMin = _previewRt.offsetMax = Vector2.zero;
            _preview = previewGo.AddComponent<RawImage>();
            _preview.raycastTarget = false;

            var strip = new GameObject("TitleStrip");
            strip.transform.SetParent(_card.transform, false);
            _stripRt = strip.AddComponent<RectTransform>();
            _stripRt.anchorMin = Vector2.zero;
            _stripRt.anchorMax = new Vector2(1f, 0.5f);
            _stripRt.offsetMin = _stripRt.offsetMax = Vector2.zero;
            var scrim = strip.AddComponent<Image>();
            scrim.color = new Color(0f, 0f, 0f, 0.88f);
            scrim.raycastTarget = false;

            _title = UGUIShip.CreateLabel(_card.transform, new Rect(10f, CARD_H * 0.5f + 3f, CARD_W - 20f, 30f),
                "", 12, Color.white, TextAnchor.UpperLeft);
            _title.horizontalOverflow = HorizontalWrapMode.Wrap;
            _title.verticalOverflow = VerticalWrapMode.Truncate;
            _titleRt = _title.rectTransform;

            float arrowY = (CARD_H - ARROW_W) * 0.5f;
            Arrow(root.transform, new Rect(0f, arrowY, ARROW_W, ARROW_W), "<", -1);
            Arrow(root.transform, new Rect(CARD_W + ARROW_SIDE, arrowY, ARROW_W, ARROW_W), ">", 1);

            var dotsGo = new GameObject("Dots");
            dotsGo.transform.SetParent(_card.transform, false);
            _dotsRt = dotsGo.AddComponent<RectTransform>();
            _dotsRt.anchorMin = _dotsRt.anchorMax = new Vector2(0.5f, 0f);
            _dotsRt.pivot = new Vector2(0.5f, 0f);
            _dotsRt.anchoredPosition = new Vector2(0f, 5f);
            var hlg = dotsGo.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = 4f;
            var dcsf = dotsGo.AddComponent<ContentSizeFitter>();
            dcsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            dcsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var glowGo = new GameObject("HoverGlow");
            glowGo.transform.SetParent(_card.transform, false);
            var glowRt = glowGo.AddComponent<RectTransform>();
            glowRt.anchorMin = Vector2.zero; glowRt.anchorMax = Vector2.one;
            glowRt.offsetMin = glowRt.offsetMax = Vector2.zero;
            _glow = glowGo.AddComponent<Image>();
            _glow.sprite = UGUIShip.GetButtonShineSprite();
            _glow.type = Image.Type.Sliced;
            _glow.pixelsPerUnitMultiplier = 5f;
            _glow.color = new Color(1f, 1f, 1f, 0f);
            _glow.raycastTarget = false;

            var dismissGo = new GameObject("Dismiss");
            dismissGo.transform.SetParent(_card.transform, false);
            var drt = dismissGo.AddComponent<RectTransform>();
            drt.anchorMin = drt.anchorMax = Vector2.zero;
            drt.pivot = Vector2.zero;
            drt.anchoredPosition = new Vector2(6f, 4f);
            drt.sizeDelta = new Vector2(124f, 16f);
            _dismissCg = dismissGo.AddComponent<CanvasGroup>();
            _dismissCg.alpha = 0f;
            _dismissCg.blocksRaycasts = false;
            UGUIShip.CreateButton(dismissGo.transform, new Rect(0f, 0f, 124f, 16f), "Don't show news feed",
                new Color(0.8f, 0.2f, 0.2f, 1f), Color.white, 10, () => SetEnabled(false));

            Show();
            if (!_loaded && !_loading) AssetManager.Instance.StartCoroutine(Fetch().WrapToIl2Cpp());
            return root;
        }

        private static readonly List<GameObject> _arrows = new List<GameObject>();

        private static void Arrow(Transform parent, Rect r, string glyph, int dir)
            => _arrows.Add(UGUIShip.CreateButton(parent, r, glyph, new Color(0.35f, 0.55f, 0.9f, 1f),
                Color.white, 13, () => Step(dir)).gameObject);

        private static void Step(int dir)
        {
            if (_titles.Count < 2 || _slideDir != 0) return;
            _slideDir = dir;
            _slideT = 0f;
            _slidingOut = true;
        }

        private const float REFRESH_SECONDS = 30f;
        private const float TOAST_SECONDS = 5f;
        private const float TOAST_FADE = 0.5f;
        private const string KEY_SEEN = "news.seen.items";
        private static float _nextFetch;
        private static GameObject _rootGo;
        private static CanvasGroup _rootCg;
        private static bool _uiVisible;
        private static float _toastLeft;
        private static string _hash = "";

        public static void SetUiVisible(bool visible)
        {
            _uiVisible = visible;
            ApplyRoot();
        }

        private static void ApplyRoot()
        {
            if (_rootGo == null) return;
            bool on = _uiVisible || _toastLeft > 0f;
            if (_rootGo.activeSelf != on) _rootGo.SetActive(on);
            _rootCg.alpha = _uiVisible ? 1f : Mathf.Clamp01(_toastLeft / TOAST_FADE);
        }

        private static string ItemKey(string title, string image)
        {
            uint h = 2166136261;
            foreach (char c in title + "|" + image) h = (h ^ c) * 16777619;
            return h.ToString("x8");
        }

        private static string HashOf(List<string> titles, List<string> images, List<List<NewsViewer.Page>> pages)
        {
            uint h = 2166136261;
            for (int i = 0; i < titles.Count; i++)
            {
                foreach (char c in titles[i] + "|" + images[i] + "\n") h = (h ^ c) * 16777619;
                foreach (var p in pages[i])
                    foreach (char c in p.image + "|" + p.title + "|" + p.text + "|" + p.link + "\n") h = (h ^ c) * 16777619;
            }
            return h.ToString("x8");
        }

        private static bool Openable => _index < _pages.Count && _pages[_index].Count > 0;

        internal static bool PageLoading(string image) => !string.IsNullOrEmpty(image) && _texLoading.Contains(image);

        internal static Texture2D PageTexture(string image)
        {
            if (string.IsNullOrEmpty(image)) return null;
            _texCache.TryGetValue(image, out var tex);
            if (tex == null && _texLoading.Add(image))
                AssetManager.Instance.StartCoroutine(FetchImage(image).WrapToIl2Cpp());
            return tex;
        }

        public static void Tick()
        {
            NewsViewer.Tick();

            if (_card != null && !_loading && Time.unscaledTime >= _nextFetch)
            {
                _nextFetch = Time.unscaledTime + REFRESH_SECONDS;
                AssetManager.Instance.StartCoroutine(Fetch().WrapToIl2Cpp());
            }

            if (_toastLeft > 0f)
            {
                _toastLeft = Mathf.Max(0f, _toastLeft - Time.unscaledDeltaTime);
                ApplyRoot();
            }

            if (_card == null || !_card.activeInHierarchy)
            {
                _hoverTime = 0f;
                _dismissShown = false;
                if (_tipShown) { _tipShown = false; BetterFGUIMan.Instance?.HideTooltip(); }
                return;
            }

            float dt = Time.unscaledDeltaTime;

            var mouse = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
            bool over = RectTransformUtility.RectangleContainsScreenPoint(_rootRt, mouse, null);
            bool still = (mouse - _lastMouse).sqrMagnitude < 0.01f
                && !Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2);
            _lastMouse = mouse;

            if (!over) { _hoverTime = 0f; _dismissShown = false; }
            else if (!_dismissShown)
            {
                _hoverTime = still ? _hoverTime + dt : 0f;
                if (_hoverTime >= DISMISS_HOVER_DELAY) _dismissShown = true;
            }

            float want = _dismissShown ? 1f : 0f;
            _dismissCg.alpha = Mathf.MoveTowards(_dismissCg.alpha, want, dt / DISMISS_FADE);
            _dismissCg.blocksRaycasts = _dismissCg.alpha > 0.9f;

            bool overCard = Openable && !NewsViewer.IsOpen
                && RectTransformUtility.RectangleContainsScreenPoint(_cardRt, mouse, null);
            float glow = overCard ? 1f : 0f;
            if (!Mathf.Approximately(glow, _glow.color.a)) _glow.color = new Color(1f, 1f, 1f, glow);
            if (overCard != _tipShown)
            {
                _tipShown = overCard;
                if (overCard) BetterFGUIMan.Instance?.ShowTooltip("Click for more info (full screen)");
                else BetterFGUIMan.Instance?.HideTooltip();
            }
            if (overCard && _dismissCg.alpha < 0.9f && Input.GetMouseButtonDown(0))
                NewsViewer.Open(_pages[_index]);

            if (_slideDir == 0) return;

            _slideT += dt / SLIDE_SECONDS;
            float e = Mathf.Clamp01(_slideT);
            e = e * e * (3f - 2f * e);

            if (_slidingOut)
            {
                SlideTo(-_slideDir * CARD_W * e);
                if (_slideT >= 1f)
                {
                    _index = (_index + _slideDir + _titles.Count) % _titles.Count;
                    Show();
                    _slidingOut = false;
                    _slideT = 0f;
                }
            }
            else
            {
                SlideTo(_slideDir * CARD_W * (1f - e));
                if (_slideT >= 1f) { _slideDir = 0; SlideTo(0f); }
            }
        }

        private static void SlideTo(float x)
        {
            _previewRt.anchoredPosition = new Vector2(x, _previewRt.anchoredPosition.y);
            _stripRt.anchoredPosition = new Vector2(x, _stripRt.anchoredPosition.y);
            _titleRt.anchoredPosition = new Vector2(10f + x, _titleRt.anchoredPosition.y);
        }

        private static IEnumerator Fetch()
        {
            _loading = true;
            _nextFetch = Time.unscaledTime + REFRESH_SECONDS;
            var req = UnityWebRequest.Get(BaseUrl + "news.json?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
            yield return req.SendWebRequest();
            string json = req.result == UnityWebRequest.Result.Success ? req.downloadHandler.text : null;
            string error = req.error;
            req.Dispose();
            _loading = false;

            if (json == null) { Plugin.Log.LogInfo($"news.json didn't load ({error}), feed stays hidden"); yield break; }
            _loaded = true;
            var titles = new List<string>();
            var images = new List<string>();
            var pages = new List<List<NewsViewer.Page>>();
            var dates = new List<string>();
            foreach (var entry in JsonUtil.GetRootArray(json))
            {
                string t = JsonUtil.GetValue(entry, "title");
                if (t.Length == 0) continue;
                titles.Add(t);
                images.Add(JsonUtil.GetValue(entry, "image"));
                dates.Add(JsonUtil.GetValue(entry, "date"));
                var list = new List<NewsViewer.Page>();
                foreach (var p in JsonUtil.GetArray(entry, "pages"))
                    list.Add(new NewsViewer.Page
                    {
                        image = JsonUtil.GetValue(p, "image"),
                        title = JsonUtil.GetValue(p, "title"),
                        text = JsonUtil.GetValue(p, "text"),
                        link = JsonUtil.GetValue(p, "link"),
                    });
                pages.Add(list);
            }

            string hash = HashOf(titles, images, pages);
            if (hash == _hash) yield break;
            bool firstLoad = _hash.Length == 0;
            _hash = hash;

            foreach (var old in _texCache.Values) if (old != null) UnityEngine.Object.Destroy(old);
            _texCache.Clear();
            _titles.Clear();
            _images.Clear();
            _titles.AddRange(titles);
            _images.AddRange(images);
            _pages.Clear();
            _pages.AddRange(pages);
            Plugin.Log.LogInfo($"news feed: {_titles.Count} stories");
            var keys = new List<string>();
            for (int i = 0; i < titles.Count; i++) keys.Add(ItemKey(titles[i], images[i]));
            var seen = new HashSet<string>(SettingsService.Get(KEY_SEEN, "").Split(','));
            int newest = 0, fresh = -1;
            for (int i = 0; i < dates.Count; i++)
            {
                if (string.CompareOrdinal(dates[i], dates[newest]) > 0) newest = i;
                if (!seen.Contains(keys[i]) && (fresh < 0 || string.CompareOrdinal(dates[i], dates[fresh]) > 0)) fresh = i;
            }
            if (firstLoad || _index >= _titles.Count) _index = newest;
            SettingsService.Set(KEY_SEEN, string.Join(",", keys));

            if (fresh >= 0 && Enabled)
            {
                _index = fresh;
                _slideDir = 0;
                _toastLeft = TOAST_SECONDS + TOAST_FADE;
            }
            if (_card != null) Show();
            ApplyRoot();
        }

        private static void Show()
        {
            if (_card == null) return;
            bool on = _titles.Count > 0 && Enabled;
            _card.SetActive(on);
            foreach (var a in _arrows) a.SetActive(on && _titles.Count > 1);
            if (!on) return;

            if (_dots.Count != _titles.Count)
            {
                for (int i = _dotsRt.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(_dotsRt.GetChild(i).gameObject);
                _dots.Clear();
                for (int i = 0; i < _titles.Count; i++)
                {
                    var dotGo = new GameObject("Dot");
                    dotGo.transform.SetParent(_dotsRt, false);
                    dotGo.AddComponent<RectTransform>().sizeDelta = new Vector2(DOT, DOT);
                    var le = dotGo.AddComponent<LayoutElement>();
                    le.preferredWidth = DOT;
                    le.preferredHeight = DOT;
                    var dimg = dotGo.AddComponent<Image>();
                    dimg.raycastTarget = false;
                    _dots.Add(dimg);
                }
            }
            for (int i = 0; i < _dots.Count; i++)
                _dots[i].color = new Color(1f, 1f, 1f, i == _index ? 1f : 0.3f);
            _dotsRt.gameObject.SetActive(_titles.Count > 1);

            _title.text = _titles[_index];
            string image = _images[_index];
            _texCache.TryGetValue(image, out var tex);
            if (tex == null && image.Length > 0 && _texLoading.Add(image))
                AssetManager.Instance.StartCoroutine(FetchImage(image).WrapToIl2Cpp());
            _preview.texture = tex;
            _preview.color = tex != null ? Color.white : Color.clear;
            if (tex == null) return;

            float src = (float)tex.width / tex.height, dst = CARD_W / CARD_H;
            _preview.uvRect = src > dst
                ? new Rect((1f - dst / src) * 0.5f, 0f, dst / src, 1f)
                : new Rect(0f, (1f - src / dst) * 0.5f, 1f, src / dst);
        }

        private static IEnumerator FetchImage(string image)
        {
            var req = UnityWebRequest.Get(BaseUrl + image + "?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
            yield return req.SendWebRequest();
            byte[] data = req.result == UnityWebRequest.Result.Success ? req.downloadHandler.data : null;
            string error = req.error;
            req.Dispose();
            _texLoading.Remove(image);
            if (data == null) { Plugin.Log.LogWarning($"news image {image} didn't come down ({error})"); yield break; }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            if (!tex.LoadImage(data)) { Plugin.Log.LogWarning($"news image {image} wouldn't decode"); yield break; }
            _texCache[image] = tex;
            NewsViewer.Refresh();
            if (_card != null && _titles.Count > _index && _images[_index] == image) Show();
        }
    }
}
