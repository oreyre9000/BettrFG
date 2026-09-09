using System;
using System.Collections.Generic;
using System.IO;
using BetterFG.Services;
using BetterFG.Utilities;
using BettrFG.uGUI;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI
{
    internal static class NewsFeed
    {
        private const float CARD_W = 278f;
        private const float CARD_H = 116f;
        private const float REFRESH_SECONDS = 300f;
        private const float SLIDE_SECONDS = 0.16f;
        private const float DISMISS_HOVER_DELAY = 2f;
        private const float DISMISS_FADE = 0.25f;
        private const float ARROW_W = 20f;
        private const float ARROW_GAP = 5f;
        private const float ARROW_SIDE = ARROW_W + ARROW_GAP;
        private const float DOT = 6f;
        private const string KEY_ENABLED = "news.enabled";

        private const string FilePath = @"C:\Users\User\Documents\GitHub\BetterFG\news.json";

        private static readonly List<string> _titles = new List<string>();
        private static readonly List<string> _images = new List<string>();
        private static readonly Dictionary<string, Texture2D> _texCache =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        private static GameObject _card;
        private static RectTransform _cardRt, _rootRt;
        private static RawImage _preview;
        private static Text _title;
        private static RectTransform _previewRt, _stripRt, _titleRt;
        private static CanvasGroup _dismissCg;
        private static RectTransform _dotsRt;
        private static readonly List<Image> _dots = new List<Image>();
        private static int _index;
        private static float _nextRead;
        private static float _hoverTime;
        private static Vector2 _lastMouse;
        private static bool _dismissShown;
        private static int _slideDir;
        private static float _slideT;
        private static bool _slidingOut;

        public static bool Enabled
        {
            get => SettingsService.Get(KEY_ENABLED, "true") == "true";
        }

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
            var rt = _rootRt = root.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.one;
            rt.pivot = Vector2.one;
            rt.anchoredPosition = new Vector2(-16f, -16f);
            rt.sizeDelta = new Vector2(CARD_W + ARROW_SIDE * 2f, CARD_H);

            _card = new GameObject("Card");
            _card.transform.SetParent(root.transform, false);
            _cardRt = _card.AddComponent<RectTransform>();
            _cardRt.anchorMin = Vector2.zero; _cardRt.anchorMax = Vector2.one;
            _cardRt.offsetMin = new Vector2(ARROW_SIDE, 0f);
            _cardRt.offsetMax = new Vector2(-ARROW_SIDE, 0f);

            var (_, inner) = UGUIShip.CreateFramedImage(_card.transform, name: "NewsFrame", ppuMult: 2f);
            _previewRt = inner;
            _preview = inner.gameObject.AddComponent<RawImage>();
            _preview.raycastTarget = false;

            var strip = new GameObject("TitleStrip");
            strip.transform.SetParent(inner.parent, false);
            _stripRt = strip.AddComponent<RectTransform>();
            _stripRt.anchorMin = Vector2.zero;
            _stripRt.anchorMax = new Vector2(1f, 0.5f);
            _stripRt.offsetMin = _stripRt.offsetMax = Vector2.zero;
            var scrim = strip.AddComponent<Image>();
            UGUIShip.ApplyDeluxColorFill(scrim, 2f);
            scrim.color = Color.black;
            scrim.raycastTarget = false;

            var scrim2 = new GameObject("Fill2");
            scrim2.transform.SetParent(strip.transform, false);
            var s2rt = scrim2.AddComponent<RectTransform>();
            s2rt.anchorMin = Vector2.zero; s2rt.anchorMax = Vector2.one;
            s2rt.offsetMin = s2rt.offsetMax = Vector2.zero;
            var fill2 = scrim2.AddComponent<Image>();
            UGUIShip.ApplyDeluxColorFill(fill2, 2f);
            fill2.color = Color.black;
            fill2.raycastTarget = false;

            var shineGo = new GameObject("Shine");
            shineGo.transform.SetParent(inner.parent, false);
            var shrt = shineGo.AddComponent<RectTransform>();
            shrt.anchorMin = Vector2.zero; shrt.anchorMax = Vector2.one;
            shrt.offsetMin = shrt.offsetMax = Vector2.zero;
            var shineImg = shineGo.AddComponent<Image>();
            shineImg.color = new Color(1f, 1f, 1f, 0.6f);
            UGUIShip.ApplyDeluxShine(shineImg, 2f);
            shineImg.raycastTarget = false;

            _title = UGUIShip.CreateLabel(inner.parent, new Rect(10f, CARD_H * 0.5f + 3f, CARD_W - 20f, 30f),
                "", 12, Color.white, TextAnchor.UpperLeft);
            _title.horizontalOverflow = HorizontalWrapMode.Wrap;
            _title.verticalOverflow = VerticalWrapMode.Truncate;
            _titleRt = _title.rectTransform;

            float arrowY = (CARD_H - ARROW_W) * 0.5f;
            Arrow(new Rect(-ARROW_SIDE, arrowY, ARROW_W, ARROW_W), "<", -1);
            Arrow(new Rect(CARD_W + ARROW_GAP, arrowY, ARROW_W, ARROW_W), ">", 1);

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

            var dismissGo = new GameObject("Dismiss");
            dismissGo.transform.SetParent(_card.transform, false);
            var drt = dismissGo.AddComponent<RectTransform>();
            drt.anchorMin = drt.anchorMax = Vector2.zero;
            drt.pivot = Vector2.zero;
            drt.anchoredPosition = new Vector2(6f, 4f);
            drt.sizeDelta = new Vector2(104f, 16f);
            _dismissCg = dismissGo.AddComponent<CanvasGroup>();
            _dismissCg.alpha = 0f;
            _dismissCg.blocksRaycasts = false;
            UGUIShip.CreateButton(dismissGo.transform, new Rect(0f, 0f, 104f, 16f), "news.dont_show",
                new Color(0.8f, 0.2f, 0.2f, 1f), Color.white, 10, () => SetEnabled(false));

            Refresh(true);
            return root;
        }

        private static void Arrow(Rect r, string glyph, int dir)
            => UGUIShip.CreateButton(_card.transform, r, glyph, new Color(0.35f, 0.55f, 0.9f, 1f),
                Color.white, 13, () => Step(dir));

        private static void Step(int dir)
        {
            if (_titles.Count < 2 || _slideDir != 0) return;
            _slideDir = dir;
            _slideT = 0f;
            _slidingOut = true;
        }

        public static void Tick()
        {
            if (_card == null || !_card.activeInHierarchy) { _hoverTime = 0f; _dismissShown = false; return; }

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

            if (_slideDir != 0)
            {
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

            if (Time.unscaledTime >= _nextRead) Refresh(false);
        }

        private static void SlideTo(float x)
        {
            _previewRt.anchoredPosition = new Vector2(x, _previewRt.anchoredPosition.y);
            _stripRt.anchoredPosition = new Vector2(x, _stripRt.anchoredPosition.y);
            _titleRt.anchoredPosition = new Vector2(10f + x, _titleRt.anchoredPosition.y);
        }

        private static void Refresh(bool firstRead)
        {
            _nextRead = Time.unscaledTime + REFRESH_SECONDS;
            _titles.Clear();
            _images.Clear();

            if (File.Exists(FilePath))
            {
                foreach (var entry in JsonUtil.GetRootArray(File.ReadAllText(FilePath)))
                {
                    string t = JsonUtil.GetValue(entry, "title");
                    if (t.Length == 0) continue;
                    _titles.Add(t);
                    _images.Add(JsonUtil.GetValue(entry, "image"));
                }
                if (firstRead) Plugin.Log.LogInfo($"news feed: {_titles.Count} stories");
            }
            else if (firstRead) Plugin.Log.LogInfo($"no news.json at {FilePath}, feed stays hidden");

            if (_index >= _titles.Count) _index = 0;
            Show();
        }

        private static void Show()
        {
            _card.SetActive(_titles.Count > 0 && Enabled);
            if (!_card.activeSelf) return;

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
                    UGUIShip.ApplyDeluxColorFill(dimg, 10f);
                    dimg.raycastTarget = false;
                    _dots.Add(dimg);
                }
            }
            for (int i = 0; i < _dots.Count; i++)
                _dots[i].color = new Color(1f, 1f, 1f, i == _index ? 1f : 0.3f);
            _dotsRt.gameObject.SetActive(_titles.Count > 1);

            _title.text = _titles[_index];
            var tex = LoadPreview(_images[_index]);
            _preview.texture = tex;
            _preview.color = tex != null ? Color.white : Color.clear;
            if (tex == null) return;

            float src = (float)tex.width / tex.height, dst = CARD_W / CARD_H;
            _preview.uvRect = src > dst
                ? new Rect((1f - dst / src) * 0.5f, 0f, dst / src, 1f)
                : new Rect(0f, (1f - src / dst) * 0.5f, 1f, src / dst);
        }

        private static Texture2D LoadPreview(string image)
        {
            if (string.IsNullOrEmpty(image)) return null;
            if (_texCache.TryGetValue(image, out var cached)) return cached;

            string path = Path.IsPathRooted(image)
                ? image
                : Path.Combine(Path.GetDirectoryName(FilePath), image);
            Texture2D tex = null;
            if (File.Exists(path))
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.hideFlags = HideFlags.HideAndDontSave;
                if (!tex.LoadImage(File.ReadAllBytes(path)))
                {
                    Plugin.Log.LogWarning($"news image {image} wouldn't decode");
                    tex = null;
                }
            }
            else Plugin.Log.LogWarning($"news image missing: {path}");

            if (tex != null) _texCache[image] = tex;
            return tex;
        }
    }
}
