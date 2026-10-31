using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Round;
using BetterFG.Features.Assets;
using BetterFG.Services;
using BetterFG.Tweaks;
using BetterFG.UI.Components;
using BetterFG.Utilities;
using BettrFG.uGUI;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace BetterFG.UI.Tabs
{
    public class RoundsTab : UISubTab
    {
        public RoundsTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Themes - Rounds";
        protected override string TitleId => "themes.title";
        protected override string SwitchLabel => "round_bg.switch_ui";
        protected override Tab MakeSwitchTarget() => BetterFGTabRegistry.NewTab<UIBackgroundTab>();
        public override float OpenHeight => UIScale.TAB_CONTENT_H * 1.3f;

        private const float GAP = UIScale.PAD;

        private static readonly Dictionary<string, Sprite> _splashes = new Dictionary<string, Sprite>();
        private static bool _catalogueTried;

        private RectTransform _content;
        private RectTransform _levels;
        private string _scene;
        private float _levelsY;
        private bool _splashRun;
        private readonly Dictionary<string, RawImage> _splashImgs = new Dictionary<string, RawImage>();
        private readonly List<(string hay, RectTransform card)> _cards = new List<(string, RectTransform)>();
        private string _query = "";
        private float _cw, _cardH;

        protected override void BuildContent(RectTransform contentRoot)
        {
            _content = contentRoot;
            RoundBackgroundService.CurrentChanged += OnCurrentChanged;
            Rebuild();
        }

        void OnDestroy() => RoundBackgroundService.CurrentChanged -= OnCurrentChanged;

        private void OnCurrentChanged()
        {
            if (_scene == null) Rebuild();
        }

        private void Rebuild()
        {
            if (_content == null) return;
            if (_levels != null) _levelsY = _levels.anchoredPosition.y;
            for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
            _splashImgs.Clear();
            _levels = null;
            if (_scene == null) BuildLevels();
            else BuildOptions();
        }

        private void Navigate(string scene)
        {
            if (IsOpen) UiTransition.Run(_content, () => { _scene = scene; Rebuild(); });
            else { _scene = scene; Rebuild(); }
        }

        private static string NameOf(string scene)
        {
            if (scene == RoundBackgroundService.QualScene) return "round_bg.qual_screen";
            string label = SceneExport.RoundLabel(scene) ?? scene;
            int cut = label.IndexOf("   ", StringComparison.Ordinal);
            return cut > 0 ? label.Substring(0, cut) : label;
        }

        private RectTransform Scroll(float y, out float innerW)
        {
            float w = TabWidth - PAD * 2f;
            var (scrollRect, content) = UGUIShip.CreateScrollView(_content, new Rect(PAD, y, w, TabHeight - y - VPAD - 4f));
            scrollRect.scrollSensitivity = 60f;
            innerW = w;
            var viewport = scrollRect.viewport;
            if (viewport != null)
            {
                viewport.offsetMin = new Vector2(0f, viewport.offsetMin.y);
                innerW = w + viewport.offsetMax.x;
            }
            return content;
        }

        private void Grid(float innerW, out float cw, out float ph, out float cardH)
        {
            cw = Mathf.Floor((innerW - PAD - GAP) * 0.5f);
            ph = Mathf.Round(cw * 9f / 16f);
            cardH = ph + LH + 8f;
        }

        private void BuildLevels()
        {
            float w = TabWidth - PAD * 2f;
            float y = VPAD;
            UGUIShip.CreateLabel(_content, new Rect(PAD, y, w * 0.4f, BTN_H), "round_bg.mode", FS, WHITE, TextAnchor.MiddleLeft);
            var modeIds = new List<string>();
            foreach (var m in RoundBackgroundService.Modes) modeIds.Add("round_bg.mode." + m);
            UGUIShip.CreateDropdown(_content, new Rect(PAD + w * 0.4f, y, w * 0.6f, BTN_H), modeIds,
                Math.Max(0, Array.IndexOf(RoundBackgroundService.Modes, RoundBackgroundService.Mode)),
                i => RoundBackgroundService.SetMode(RoundBackgroundService.Modes[i]), FS);
            y += BTN_H + SH;

            string current = RoundBackgroundService.CurrentScene;
            if (current != null && SceneExport.IsUnityRound(current))
            {
                UGUIShip.CreateButton(_content, new Rect(PAD, y, w, BTN_H),
                    LocalizationService.Format("round_bg.current", LocalizationService.Get(NameOf(current))), SEL_COLOR, WHITE, FS_SM, () => Navigate(current));
                y += BTN_H + SH;
            }

            var search = UGUIShip.CreateInputField(_content, new Rect(PAD, y, w, BTN_H), "round_bg.search");
            UGUIShip.SetInputText(search, _query, false);
            search.onValueChanged.AddListener(new Action<string>(q => { _query = q ?? ""; PlaceCards(); }));

            var list = Scroll(y + BTN_H + SH, out float innerW);
            _levels = list;
            Grid(innerW, out _cw, out float ph, out _cardH);
            _cards.Clear();
            var scenes = new List<string>(SceneExport.Scenes());
            scenes.Insert(0, RoundBackgroundService.QualScene);
            if (!_splashes.ContainsKey(RoundBackgroundService.QualScene))
                _splashes[RoundBackgroundService.QualScene] = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.general.round_bg_qual_screen.png");
            for (int i = 0; i < scenes.Count; i++)
            {
                string scene = scenes[i];
                string name = NameOf(scene);
                _splashes.TryGetValue(scene, out var sprite);
                var raw = PreviewCard.Build(list, 0f, 0f, _cw, ph, LH, FS_SM, name, null,
                    RoundBackgroundService.Picked(scene).Length > 0, BTN_DARK, SEL_COLOR, () => Navigate(scene));
                PreviewCard.SetSprite(raw, sprite);
                _splashImgs[scene] = raw;
                _cards.Add(((LocalizationService.Get(name) + "\n" + scene).ToLowerInvariant(),
                    raw.GetComponentInParent<Button>(true).GetComponent<RectTransform>()));
            }
            PlaceCards();
            list.anchoredPosition = new Vector2(list.anchoredPosition.x, _levelsY);
            if (!_splashRun) StartCoroutine(LoadSplashes(scenes).WrapToIl2Cpp());
            StartCoroutine(Background3dTweak.FetchPreviews(null).WrapToIl2Cpp());
        }

        private void PlaceCards()
        {
            if (_levels == null) return;
            string q = _query.Trim().ToLowerInvariant();
            int shown = 0;
            foreach (var (hay, card) in _cards)
            {
                bool show = q.Length == 0 || hay.Contains(q);
                if (card.gameObject.activeSelf != show) card.gameObject.SetActive(show);
                if (!show) continue;
                card.anchoredPosition = new Vector2((shown % 2) * (_cw + GAP), -(shown / 2) * (_cardH + GAP));
                shown++;
            }
            _levels.sizeDelta = new Vector2(0f, ((shown + 1) / 2) * (_cardH + GAP) + PAD);
        }

        private IEnumerator LoadSplashes(List<string> scenes)
        {
            _splashRun = true;
            for (int i = 0; i < scenes.Count; i++)
            {
                string scene = scenes[i];
                if (_splashes.ContainsKey(scene)) continue;
                string key = SceneExport.SplashKey(scene);
                Sprite sprite = null;
                if (!string.IsNullOrEmpty(key))
                {
                    AsyncOperationHandle<Sprite> op = default;
                    bool asked = true;
                    try { op = Addressables.LoadAssetAsync<Sprite>(key); }
                    catch (Exception ex) { Plugin.Log.LogWarning($"no splash under {key}: {ex.Message}"); asked = false; }
                    if (asked)
                    {
                        while (!op.IsDone) yield return null;
                        if (op.Status == AsyncOperationStatus.Succeeded) sprite = op.Result;
                    }
                }
                _splashes[scene] = sprite;
                if (_splashImgs.TryGetValue(scene, out var raw) && raw != null) PreviewCard.SetSprite(raw, sprite);
            }
            _splashRun = false;
        }

        private void BuildOptions()
        {
            float w = TabWidth - PAD * 2f, back = BTN_H * 3f;
            float y = VPAD;
            UGUIShip.CreateButton(_content, new Rect(PAD, y, back, BTN_H), "ui.back_3", BTN_DARK, WHITE, FS_SM, () => Navigate(null));
            UGUIShip.CreateLabel(_content, new Rect(PAD + back + PAD, y, w - back - PAD, BTN_H), NameOf(_scene), FS_SM, WHITE);
            y += BTN_H + SH;
            UGUIShip.CreatePanel(_content, new Rect(PAD, y, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            y += 1f + SH;

            string pick = RoundBackgroundService.Picked(_scene);
            var options = RoundBackgroundService.Options();
            var chosen = options[0];
            foreach (var o in options) if (o.Key == pick) chosen = o;
            float ph = Mathf.Round((w - 8f) * 9f / 16f);
            var raw = PreviewCard.Build(_content, PAD, y, w, ph, LH, FS_SM, chosen.Label,
                chosen.Key.Length > 0 ? Background3dTweak.Preview(chosen.Preview) : null, false, BTN_DARK, SEL_COLOR, OpenLibrary);
            if (chosen.Key.Length == 0 && _splashes.TryGetValue(_scene, out var splash)) PreviewCard.SetSprite(raw, splash);
            y += ph + LH + 8f + SH;
            UGUIShip.CreateButton(_content, new Rect(PAD, y, w, BTN_H), "round_bg.change", BTN_DARK, WHITE, FS_SM, OpenLibrary);

            StartCoroutine(Background3dTweak.FetchPreviews(Rebuild).WrapToIl2Cpp());
            if (!_catalogueTried)
            {
                _catalogueTried = true;
                StartCoroutine(Background3dTweak.FetchCatalogue(Rebuild).WrapToIl2Cpp());
            }
        }

        private void OpenLibrary()
        {
            string scene = _scene;
            _splashes.TryGetValue(scene, out var splash);
            BackgroundLibrary.Open(key => { RoundBackgroundService.Choose(scene, key); Rebuild(); }, RoundBackgroundService.Picked(scene), splash);
        }
    }
}
