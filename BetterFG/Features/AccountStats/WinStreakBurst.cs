using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using Events;
using FGClient.CatapultServices;
using FMODUnity;
using MPG.Utility;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Features.AccountStats
{
    public class WinStreakBurst : BetterFG.Tweaks.BfgTweak
    {
        public WinStreakBurst(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "win_streak_popup";
        public override string TweakLabel => "tweak.win_streak_popup";
        public override bool DefaultEnabled => true;
        public override string TweakTooltip => "tweak.win_streak_popup.tip";

        static WinStreakBurst _instance;
        EventSystem.Handle _statsHandle;
        int _celebrated = -1;
        bool _inMenu;

        RectTransform _burst, _rays, _crown;
        Transform _card;
        Image _glow;
        Image[] _beams, _stars;
        TextMeshProUGUI _numOld, _numNew;

        void Awake() => _instance = this;

        internal static void MenuEntered()
        {
            if (_instance == null) return;
            _instance._inMenu = true;
            _instance.Apply();
        }

        internal static void OnMatchmakingStarted()
        {
            if (_instance == null) return;
            _instance._inMenu = false;
            if (_instance._burst != null)
            {
                _instance.StopAllCoroutines();
                _instance._burst.gameObject.SetActive(false);
            }
            _instance.Apply();
        }

        void OnStatsChanged(OnPlayerStatsChangedEvent e) => Apply();

        void Apply()
        {
            if (_statsHandle == null) _statsHandle = Broadcaster.Instance.Register<OnPlayerStatsChangedEvent>(new Action<OnPlayerStatsChangedEvent>(OnStatsChanged));
            var streak = AccountStatsData.CurrentStreak();
            if (!streak.HasValue) return;
            if (_celebrated < 0) { _celebrated = streak.Value; return; }
            if (!_inMenu || streak.Value == _celebrated) return;
            if (!IsEnabled) { _celebrated = streak.Value; return; }
            if (_card == null && !BuildBurst()) return;
            Plugin.Log.LogInfo($"streak went {_celebrated} -> {streak.Value}, fanfare time");
            StartCoroutine(Celebrate(_celebrated, streak.Value).WrapToIl2Cpp());
            _celebrated = streak.Value;
        }

        IEnumerator Celebrate(int from, int to)
        {
            _burst.gameObject.SetActive(true);
            _numOld.text = from.ToString();
            _numNew.text = to.ToString();
            Layout(_numOld);
            _numNew.rectTransform.anchoredPosition = _numOld.rectTransform.anchoredPosition;
            _numOld.color = Color.white;
            _numNew.color = new Color(1f, 1f, 1f, 0f);
            foreach (var s in _stars) s.color = Color.clear;
            Sfx("UI_RewardScreen_Whoosh");
            Sfx("UI_Gen_PopUp");

            bool reset = to == 0;
            float t0 = Time.unscaledTime;
            bool swapped = false, outed = false;
            try
            {
                while (true)
                {
                    float t = Time.unscaledTime - t0;
                    if (t >= 5f) break;
                    float sIn = t < 0.4f ? BackOut(t / 0.4f) : 1f;
                    float o = Mathf.Clamp01((t - 4.4f) / 0.3f);
                    float sOut = 1f - o * o;
                    if (t > 4.4f && !outed) { outed = true; Sfx("UI_RewardScreen_Whoosh"); }
                    float scale = Mathf.Max(0f, sIn * sOut);
                    _card.localScale = Vector3.one * scale;
                    _rays.localScale = Vector3.one * scale;
                    _rays.localRotation = Quaternion.Euler(0f, 0f, -t * 12f);
                    float vis = reset ? 0f : Mathf.Clamp01(t / 0.3f) * sOut;
                    foreach (var b in _beams) b.color = new Color(1f, 0.85f, 0.45f, 0.25f * vis);
                    _glow.color = new Color(1f, 0.8f, 0.4f, 0.45f * vis);

                    if (t > 1.6f && !swapped)
                    {
                        swapped = true;
                        Sfx(reset ? "UI_RewardScreen_Medal_Lost" : "UI_RewardScreen_CrownRank_Achieve");
                        var oldPos = _numOld.rectTransform.anchoredPosition;
                        Layout(_numNew);
                        _numOld.rectTransform.anchoredPosition = oldPos;
                    }
                    if (swapped)
                    {
                        float u = t - 1.6f;
                        float a = Mathf.Clamp01(u / 0.2f);
                        _numOld.color = new Color(1f, 1f, 1f, 1f - a);
                        _numOld.rectTransform.anchoredPosition = new Vector2(_numOld.rectTransform.anchoredPosition.x, NumY + 30f * a);
                        float b = Mathf.Clamp01((u - 0.1f) / 0.3f);
                        var tint = reset ? Color.Lerp(new Color(1f, 0.25f, 0.25f), Color.white, Mathf.Clamp01((u - 1f) / 0.3f)) : Color.white;
                        _numNew.color = new Color(tint.r, tint.g, tint.b, b);
                        _numNew.rectTransform.anchoredPosition = new Vector2(_numNew.rectTransform.anchoredPosition.x, NumY - 24f * (1f - BackOut(b)));
                        if (reset) { yield return null; continue; }
                        float st = Mathf.Clamp01(u / 1.2f);
                        for (int i = 0; i < _stars.Length; i++)
                        {
                            float ang = i / (float)_stars.Length * Mathf.PI * 2f + 0.3f;
                            float d = 90f + 150f * (1f - (1f - st) * (1f - st)) * (i % 2 == 0 ? 1f : 0.7f);
                            var r = _stars[i].rectTransform;
                            r.anchoredPosition = new Vector2(Mathf.Cos(ang) * d * 1.5f, Mathf.Sin(ang) * d * 0.8f);
                            r.localRotation = Quaternion.Euler(0f, 0f, u * 200f * (i % 2 == 0 ? 1f : -1f));
                            r.localScale = Vector3.one * (i % 3 == 0 ? 1.2f : 0.8f);
                            _stars[i].color = new Color(1f, 0.9f, 0.4f, st < 1f ? 1f - st * st : 0f);
                        }
                    }
                    yield return null;
                }
            }
            finally { _burst.gameObject.SetActive(false); }
        }

        void Layout(TextMeshProUGUI t)
        {
            float tw = t.GetPreferredValues(t.text).x;
            float total = _crown.sizeDelta.x + 10f + tw;
            float x0 = -total / 2f;
            _crown.anchoredPosition = new Vector2(x0 + _crown.sizeDelta.x / 2f, NumY);
            t.rectTransform.anchoredPosition = new Vector2(x0 + _crown.sizeDelta.x + 10f, NumY);
        }

        const float NumY = 3f;

        static float BackOut(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            x -= 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        static void Sfx(string key) => RuntimeManager.PlayOneShot(AudioManager.GetGuidForKey(key), Vector3.zero);

        static void Center(RectTransform r) => r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);

        static Image MakeImage(string name, Transform parent, Sprite sprite, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var i = go.AddComponent<Image>();
            i.raycastTarget = false;
            i.sprite = sprite;
            i.color = Color.clear;
            i.rectTransform.sizeDelta = size;
            return i;
        }

        bool BuildBurst()
        {
            GameObject src = null;
            foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>())
                if (!g.scene.IsValid() && g.transform.parent == null && g.name == "PB_UI_InGameNotification_CrownShards") { src = g; break; }
            Sprite flare = null, beam = null, star = null;
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                var n = s.name;
                if (flare == null && n == "UI_GolderFlare_Image") flare = s;
                else if (beam == null && n == "UI_128xGlow_Image") beam = s;
                else if (star == null && n == "UI_Star") star = s;
            }
            TMP_FontAsset titan = null;
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                if (f.name == "TitanOne-Expanded SDF (Title)") { titan = f; break; }
            if (src == null || flare == null || beam == null || star == null || titan == null)
            {
                Plugin.Log.LogWarning($"streak fanfare missing parts (card {src != null}, flare {flare != null}, beam {beam != null}, star {star != null}, font {titan != null}), skipping it");
                return false;
            }

            var host = new GameObject("WinStreakBurstCanvas");
            host.transform.SetParent(transform, false);
            var canvas = host.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30001;
            var scaler = host.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            _burst = new GameObject("WinStreakBurst").AddComponent<RectTransform>();
            _burst.SetParent(host.transform, false);
            _burst.anchorMin = _burst.anchorMax = new Vector2(0.5f, 1f);
            _burst.anchoredPosition = new Vector2(0f, -130f);

            _glow = MakeImage("Glow", _burst, flare, new Vector2(520f, 300f));
            _rays = new GameObject("Rays").AddComponent<RectTransform>();
            _rays.SetParent(_burst, false);
            _beams = new Image[12];
            for (int i = 0; i < _beams.Length; i++)
            {
                _beams[i] = MakeImage("Beam", _rays, beam, i % 2 == 0 ? new Vector2(36f, 520f) : new Vector2(24f, 400f));
                _beams[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, i * 15f);
            }
            _stars = new Image[10];
            for (int i = 0; i < _stars.Length; i++) _stars[i] = MakeImage("Star", _burst, star, new Vector2(30f, 30f));

            var hold = new GameObject("Hold");
            hold.SetActive(false);
            hold.transform.SetParent(_burst, false);
            var card = Instantiate(src, hold.transform, false);
            card.name = "Card";
            foreach (var b in card.GetComponentsInChildren<MonoBehaviour>(true))
            {
                var tn = b.GetIl2CppType().Name;
                if (tn.Contains("Localised") || tn.Contains("ToggleActive") || tn.EndsWith("ViewModel") || tn.Contains("Binding") || tn.Contains("Tween") || tn.Contains("Focus")) DestroyImmediate(b);
            }
            foreach (var an in card.GetComponentsInChildren<Animator>(true)) DestroyImmediate(an);
            foreach (var gr in card.GetComponentsInChildren<Graphic>(true)) gr.raycastTarget = false;
            _card = card.transform;
            _card.SetParent(_burst, false);
            Destroy(hold);

            const float W = 300f, H = 112f, TH = 46f;
            var crt = card.GetComponent<RectTransform>();
            Center(crt);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(W, H);
            card.GetComponent<CanvasGroup>().alpha = 1f;

            var titleRt = _card.Find("Title").GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.sizeDelta = new Vector2(-4f, TH);
            titleRt.anchoredPosition = Vector2.zero;
            var title = titleRt.Find("Text").GetComponent<TextMeshProUGUI>();
            title.text = LocalizationService.Get("account_stats.activity.streak_banner");
            title.alignment = TextAlignmentOptions.Center;
            title.fontSize = 26f;
            title.rectTransform.anchoredPosition = new Vector2(0f, title.rectTransform.anchoredPosition.y);

            var body = _card.Find("CrownShardContainer").GetComponent<RectTransform>();
            body.GetComponent<CanvasGroup>().alpha = 1f;
            body.Find("Background").gameObject.SetActive(false);
            body.Find("CrownShardIconBG").gameObject.SetActive(false);
            body.Find("Prefab_SplashVFX").gameObject.SetActive(false);
            Center(body);
            body.sizeDelta = new Vector2(W, H - TH);
            body.anchoredPosition = new Vector2(0f, -TH / 2f);
            _crown = body.Find("CrownIcon").GetComponent<RectTransform>();
            Center(_crown);
            _crown.sizeDelta = new Vector2(64f, 63f);

            _numOld = MakeNumber(body, titan);
            _numNew = MakeNumber(body, titan);
            _burst.gameObject.SetActive(false);
            return true;
        }

        static TextMeshProUGUI MakeNumber(Transform parent, TMP_FontAsset font)
        {
            var go = new GameObject("Number");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.raycastTarget = false;
            t.font = font;
            t.fontSize = 58f;
            t.color = Color.white;
            t.horizontalAlignment = HorizontalAlignmentOptions.Left;
            t.verticalAlignment = VerticalAlignmentOptions.Geometry;
            t.enableWordWrapping = false;
            var r = t.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0f, 0.5f);
            r.sizeDelta = new Vector2(160f, 60f);
            var m = t.fontMaterial;
            m.SetFloat("_OutlineWidth", 0.18f);
            m.SetColor("_OutlineColor", new Color(0.08f, 0.08f, 0.1f));
            return t;
        }
    }
}
