using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Player;
using BetterFG.Customization.Profiles;
using BetterFG.Customization.Users;
using BetterFG.Features.Onboarding;
using BetterFG.Nametag;
using BetterFG.Services;
using BetterFG.UI.SideWheel;
using BetterFG.Utilities;
using BettrFG.uGUI;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Windows
{
    public static class UserLogon
    {
        public const string KEY_PROMPT = "users.prompt.startup";

        public static bool PromptOnStartup
        {
            get => SettingsService.Get(KEY_PROMPT, "true") != "false";
            set => SettingsService.Set(KEY_PROMPT, value ? "true" : "false");
        }

        const int CanvasOrder = 1300;
        const int CoverOrder = 1350;
        const float HeroX = 300f;
        const float Lean = 0.21f;
        const float RowH = 34f, RowGap = 8f, PanelW = 360f, Pad = 16f, PlayH = 22f, IconS = 52f, PadTop = 30f, IconHead = 18f;
        const int MaxVisible = 5;
        static readonly Vector2 BlockPos = new Vector2(-330f, -20f);
        static readonly Color Warm = new Color(0.95f, 0.55f, 0.18f, 1f);

        sealed class Row { public string Name; public Button Btn; public RectTransform Rt; public RawImage Icon; }

        static GameObject _root, _left, _tag;
        static RectTransform _canvasRt;
        static RawImage _raysA, _raysB, _core;
        static Text _heroName;
        static string _picked;
        static bool _leaving, _uiWasVisible;
        static List<string> _users = new List<string>();
        static readonly List<Row> _rows = new List<Row>();
        static readonly List<KeyValuePair<RectTransform, RectTransform>> _slides = new List<KeyValuePair<RectTransform, RectTransform>>();
        static readonly Dictionary<string, Texture2D> _icons = new Dictionary<string, Texture2D>();
        static Texture2D _texRaysA, _texRaysB, _texCore, _texFade, _placeholder;
        static float _skA, _skB, _skS1, _skS2;
        static readonly System.Random Rng = new System.Random(7);

        static bool _menuReady, _pending;

        public static bool IsOpen => _root != null || _pending;

        public static void Show()
        {
            if (_root != null || _pending) return;
            if (!_menuReady)
            {
                _pending = true;
                Cover();
                return;
            }
            BeanMonitorService.Instance.StartCoroutine(OpenRoutine().WrapToIl2Cpp());
        }

        public static void OnMainMenuEntered()
        {
            _menuReady = true;
            if (!_pending) return;
            _pending = false;
            BeanMonitorService.Instance.StartCoroutine(OpenRoutine().WrapToIl2Cpp());
        }

        static IEnumerator OpenRoutine()
        {
            var cover = Cover();
            yield return new WaitForSecondsRealtime(0.35f);

            var uim = BetterFGUIMan.Instance;
            _uiWasVisible = uim != null && uim.IsVisible;
            if (uim != null) { uim.SetVisibleForced(false); uim.VisibilityLocked = true; }
            SideWheelManager.InputLocked = true;
            FGInputLockService.SetScreenLock(true);
            Build();
            Plugin.Log.LogInfo($"logon up, {_users.Count} users, {_picked} preselected");

            yield return new WaitForSecondsRealtime(0.15f);
            yield return LoadingScreenService.HideRoutine();
            if (cover != null) cover.sortingOrder = LoadingScreenService.DefaultOrder;
        }

        static IEnumerator PlayRoutine()
        {
            _leaving = true;
            var cover = Cover();
            yield return new WaitForSecondsRealtime(0.35f);

            UserService.Switch(_picked);
            UserPreviewStage.Close();
            UnityEngine.Object.Destroy(_root);
            _root = _left = _tag = null;
            _rows.Clear();
            _slides.Clear();

            var uim = BetterFGUIMan.Instance;
            if (uim != null) { uim.VisibilityLocked = false; uim.SetVisibleForced(_uiWasVisible); }
            SideWheelManager.InputLocked = false;
            FGInputLockService.SetScreenLock(false);

            yield return new WaitForSecondsRealtime(0.6f);
            yield return LoadingScreenService.HideRoutine();
            if (cover != null) cover.sortingOrder = LoadingScreenService.DefaultOrder;
            _leaving = false;
        }

        static Canvas Cover()
        {
            LoadingScreenService.Show();
            var cover = LoadingScreenService.Canvas;
            if (cover != null) cover.sortingOrder = CoverOrder;
            return cover;
        }

        static void Build()
        {
            SolveSkew();
            _users = SettingsService.ListUsers();
            _picked = SettingsService.ActiveUser;
            _icons.Clear();

            var canvas = UGUIShip.CreateCanvas("BetterFG_UserLogonBackdrop");
            canvas.sortingOrder = CanvasOrder;
            _root = canvas.gameObject;
            _canvasRt = canvas.GetComponent<RectTransform>();

            var bg = new GameObject("Black");
            bg.transform.SetParent(_canvasRt, false);
            bg.AddComponent<Image>().color = Color.black;
            Fill(bg.transform);

            _texRaysA ??= RaysTex(26, 10f, 0.6f, 3);
            _texRaysB ??= RaysTex(22, 8f, 0.5f, 19);
            _texCore ??= CoreTex();
            _texFade ??= Bake(4, 128, (x, y) => { float a = Mathf.Clamp01((-y - 0.05f) / 0.7f); return new Color(0f, 0f, 0f, a * a * (3f - 2f * a)); });

            var sun = new GameObject("Sun").AddComponent<RectTransform>();
            sun.SetParent(_canvasRt, false);
            sun.anchorMin = sun.anchorMax = new Vector2(0.5f, 1f);
            sun.sizeDelta = Vector2.zero;
            sun.anchoredPosition = new Vector2(HeroX, -30f);
            _raysA = Raw(sun, "RaysA", _texRaysA);
            Place(_raysA, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1300f, 1300f));
            _raysB = Raw(sun, "RaysB", _texRaysB);
            Place(_raysB, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1250f, 1250f)).localEulerAngles = new Vector3(0f, 0f, 4f);
            _core = Raw(sun, "Core", _texCore);
            Place(_core, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800f, 800f));

            UserPreviewStage.Open();
            var hero = Raw(_canvasRt, "Hero", UserPreviewStage.Texture);
            Place(hero, new Vector2(0.5f, 0.5f), new Vector2(HeroX, 63.6f), new Vector2(760f, 770f));
            var fade = Raw(_canvasRt, "HeroFade", _texFade);
            Place(fade, new Vector2(0.5f, 0.5f), new Vector2(HeroX, 63.6f), new Vector2(780f, 790f));
            sun.SetAsLastSibling();

            _heroName = UGUIShip.CreateLabel(_canvasRt, new Rect(0, 0, 500f, 50f), _picked, 34, new Color(1f, 0.9f, 0.75f, 1f), TextAnchor.MiddleCenter);
            Place(_heroName, new Vector2(0.5f, 0.5f), new Vector2(HeroX, -350f), new Vector2(500f, 50f));

            UserPreviewStage.Show(_picked);
            BuildNametag(_picked);
            BuildLeft();

            BeanMonitorService.Instance.StartCoroutine(Tick().WrapToIl2Cpp());
            BeanMonitorService.Instance.StartCoroutine(UserPreviewStage.RefreshIcons(new List<string>(_users), OnIconReady).WrapToIl2Cpp());
        }

        static IEnumerator Tick()
        {
            while (_root != null)
            {
                float t = Time.time;
                float w = Mathf.PerlinNoise(t * 0.35f, 0.3f);
                _raysA.color = new Color(1f, 1f, 1f, 0.35f + 0.65f * w);
                _raysB.color = new Color(1f, 1f, 1f, 1f - 0.65f * w);
                _core.rectTransform.localScale = Vector3.one * (0.98f + 0.04f * Mathf.PerlinNoise(t * 0.5f, 4.2f));
                UserPreviewStage.Render();
                TickSlides();
                yield return null;
            }
        }

        static void Pick(string name)
        {
            if (name == _picked || _leaving) return;
            _picked = name;
            foreach (var r in _rows)
            {
                UGUIShip.SetButtonSelected(r.Btn, r.Name == _picked, Warm);
                if (r.Name == _picked) OnboardingPopup.AddCornerPopsAround(r.Rt);
            }
            UGUIShip.RelabelText(_heroName, name);
            UserPreviewStage.Show(name);
            BuildNametag(name);
        }

        static void AddUser()
        {
            string prefix = LocalizationService.Get("users.default_name");
            int n = _users.Count + 1;
            string made = null;
            for (int tries = 0; made == null && tries < 50; tries++, n++)
                made = UserService.Create(prefix + " " + n, false);
            if (made == null) return;
            _users = SettingsService.ListUsers();
            BuildLeft();
            Pick(made);
            BeanMonitorService.Instance.StartCoroutine(UserPreviewStage.RefreshIcons(new List<string> { made }, OnIconReady).WrapToIl2Cpp());
        }

        static void AskDelete(string name)
        {
            FullscreenPrompt.Ask("users.delete.title", name, "users.delete.confirm", () =>
            {
                UserService.Delete(name);
                _users = SettingsService.ListUsers();
                _icons.Remove(name);
                if (_picked == name) Pick(SettingsService.ActiveUser);
                BuildLeft();
            }, PopUpKind.Warning, 0f, null, () => { });
        }

        static void OnIconReady(string user)
        {
            _icons.Remove(user);
            foreach (var r in _rows)
                if (r.Name == user) r.Icon.texture = IconFor(user);
        }

        static Texture2D IconFor(string name)
        {
            if (_icons.TryGetValue(name, out var have)) return have;
            string path = UserPreviewStage.IconPath(name);
            if (!System.IO.File.Exists(path))
                return _placeholder ??= EmbeddedResourceandUnity.LoadTexture("BetterFG.assets.ui.profile_bean.png", mips: true);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Trilinear };
            tex.LoadImage(System.IO.File.ReadAllBytes(path));
            return _icons[name] = tex;
        }

        static void BuildNametag(string user)
        {
            if (_tag != null) UnityEngine.Object.Destroy(_tag);
            _tag = null;
            GameObject tpl = null;
            foreach (var t in Resources.FindObjectsOfTypeAll<PartyNameTag>())
                if (!t.gameObject.scene.IsValid() && t.gameObject.name == "PartyPlayerInfo") { tpl = t.gameObject; break; }
            if (tpl == null) { Plugin.Log.LogWarning("no PartyPlayerInfo template around, logon goes without a nametag"); return; }

            _tag = UnityEngine.Object.Instantiate(tpl, _canvasRt, false);
            _tag.name = "Nametag";
            foreach (var c in _tag.GetComponentsInChildren<PartyNameTag>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in _tag.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(c);
            _tag.SetActive(true);
            var top = _tag.transform.Find("TopGroup");
            if (top != null) top.gameObject.SetActive(false);
            var rt = _tag.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(HeroX, 330f);
            rt.localScale = Vector3.one * 0.8f;
            _tag.transform.SetAsLastSibling();

            TMPro.TMP_Text text = null;
            foreach (var t in _tag.GetComponentsInChildren<TMPro.TMP_Text>(true))
                if (t.name == "Text" && t.transform.parent.name == "Nameplate") { text = t; break; }
            if (text == null) return;

            var profile = new BfgProfile();
            foreach (var kv in SettingsService.ReadUserSettings(user)) profile.settings[kv.Key] = kv.Value;
            var info = profile.BuildNametag();
            string real = ProfileService.LocalPlayerName();
            text.text = real;
            if (info != null) NametagIconApplicator.ApplyRemoteToNameplate(text, real, info);
        }

        static void BuildLeft()
        {
            if (_left != null) UnityEngine.Object.DestroyImmediate(_left);
            _rows.Clear();
            _slides.Clear();
            _left = new GameObject("Left");
            _left.AddComponent<RectTransform>().SetParent(_canvasRt, false);
            Fill(_left.transform);
            _left.transform.SetSiblingIndex(1);

            int total = _users.Count + 1;
            int visible = Mathf.Min(total, MaxVisible);
            float listH = IconHead + visible * (RowH + RowGap) - RowGap;
            float panelH = PadTop - IconHead + listH + 12f + PlayH + Pad;
            float rowW = PanelW - Pad * 2f;

            var welcome = UGUIShip.CreateLabel(_left.transform, new Rect(0, 0, 500f, 60f), "users.pick.welcome", 40, Color.white, TextAnchor.MiddleLeft);
            Place(welcome, new Vector2(0.5f, 0.5f), BlockPos + new Vector2(70f + Lean * (panelH / 2f + 62f), panelH / 2f + 62f), new Vector2(500f, 60f));
            var sub = UGUIShip.CreateLabel(_left.transform, new Rect(0, 0, 500f, 26f), "users.pick.title", 16, new Color(1f, 1f, 1f, 0.5f), TextAnchor.MiddleLeft);
            Place(sub, new Vector2(0.5f, 0.5f), BlockPos + new Vector2(70f + Lean * (panelH / 2f + 26f), panelH / 2f + 26f), new Vector2(500f, 26f));

            var panelHost = Skew(_left.transform, BlockPos, new Vector2(PanelW, panelH), "PanelSkew");
            var panel = UGUIShip.CreatePanel(panelHost, new Rect(0, 0, PanelW, panelH), new Color(0.11f, 0.11f, 0.12f, 1f), "Panel");
            UGUIShip.ApplyDeluxSkin(panel.gameObject, panel.GetComponent<Image>(), new Color(0.11f, 0.11f, 0.12f, 1f));
            Fill(panel);

            float listUp = panelH / 2f - (PadTop - IconHead) - listH / 2f;
            float slack = Lean * listH / 2f + 8f;
            var scroll = UGUIShip.CreateScrollView(_left.transform, new Rect(0, 0, rowW + slack * 2f, listH));
            var srt = scroll.scrollRect.GetComponent<RectTransform>();
            Place(srt, new Vector2(0.5f, 0.5f), BlockPos + new Vector2(Lean * listUp, listUp), new Vector2(rowW + slack * 2f, listH));
            var vp = scroll.scrollRect.viewport;
            vp.offsetMin = vp.offsetMax = Vector2.zero;
            scroll.content.sizeDelta = new Vector2(0f, IconHead + total * (RowH + RowGap) - RowGap);

            var bar = srt.Find("Scrollbar");
            var barHost = Skew(_left.transform, BlockPos + new Vector2(PanelW / 2f - Pad / 2f + Lean * listUp, listUp), new Vector2(8f, listH - IconHead), "BarSkew");
            bar.SetParent(barHost, false);
            Fill(bar);
            bar.gameObject.SetActive(total > MaxVisible);

            for (int i = 0; i < total; i++)
            {
                bool isNew = i == _users.Count;
                string name = isNew ? "" : _users[i];
                var rowRoot = new GameObject(isNew ? "NewUser" : "Row").AddComponent<RectTransform>();
                rowRoot.SetParent(scroll.content, false);
                rowRoot.anchorMin = rowRoot.anchorMax = new Vector2(0.5f, 1f);
                rowRoot.anchoredPosition = new Vector2(0f, -(IconHead + i * (RowH + RowGap) + RowH / 2f));
                rowRoot.sizeDelta = new Vector2(rowW, RowH);

                var slide = new GameObject("Slide").AddComponent<RectTransform>();
                slide.SetParent(rowRoot, false);
                Fill(slide);
                _slides.Add(new KeyValuePair<RectTransform, RectTransform>(rowRoot, slide));

                var skewHost = Skew(slide, Vector2.zero, new Vector2(rowW, RowH), "RowSkew");
                var b = UGUIShip.CreateButton(skewHost, new Rect(0, 0, rowW, RowH), "",
                    isNew ? new Color(0.14f, 0.14f, 0.15f, 1f) : new Color(0.2f, 0.2f, 0.2f, 1f), Color.white, 13,
                    isNew ? new Action(AddUser) : new Action(() => Pick(name)));
                Fill(b);

                if (isNew)
                {
                    var plus = UGUIShip.CreateLabel(slide, new Rect(0, 0, rowW, RowH), "users.new_row", 15, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleLeft);
                    plus.raycastTarget = false;
                    Fill(plus).offsetMin = new Vector2(IconS + 16f, 0f);
                    continue;
                }

                var icon = Raw(slide, "Icon", IconFor(name));
                var art = icon.rectTransform;
                art.anchorMin = art.anchorMax = art.pivot = Vector2.zero;
                art.anchoredPosition = new Vector2(6f, -3f);
                art.sizeDelta = new Vector2(IconS, IconS);

                var lbl = UGUIShip.CreateLabel(slide, new Rect(0, 0, 200f, RowH), name, 16, Color.white, TextAnchor.MiddleLeft);
                lbl.raycastTarget = false;
                var lrt = Fill(lbl);
                lrt.offsetMin = new Vector2(IconS + 16f, 0f);
                lrt.offsetMax = new Vector2(-36f, 0f);

                if (!name.Equals(SettingsService.ActiveUser, StringComparison.OrdinalIgnoreCase))
                {
                    var delRoot = new GameObject("Delete").AddComponent<RectTransform>();
                    delRoot.SetParent(slide, false);
                    delRoot.anchorMin = delRoot.anchorMax = delRoot.pivot = new Vector2(1f, 0.5f);
                    delRoot.anchoredPosition = new Vector2(-8f, 0f);
                    delRoot.sizeDelta = new Vector2(22f, 22f);
                    var del = UGUIShip.CreateButton(Skew(delRoot, Vector2.zero, new Vector2(22f, 22f), "DeleteSkew"), new Rect(0, 0, 22f, 22f),
                        "users.delete", new Color(0.5f, 0.22f, 0.22f, 1f), Color.white, 10, new Action(() => AskDelete(name)));
                    Fill(del);
                    foreach (var t in del.GetComponentsInChildren<Text>(true)) Unskew(t.rectTransform);
                }

                UGUIShip.SetButtonSelected(b, name == _picked, Warm);
                _rows.Add(new Row { Name = name, Btn = b, Rt = rowRoot, Icon = icon });
            }

            float playUp = panelH / 2f - (panelH - Pad - PlayH / 2f);
            var playRoot = new GameObject("Play").AddComponent<RectTransform>();
            playRoot.SetParent(_left.transform, false);
            Place(playRoot, new Vector2(0.5f, 0.5f), BlockPos + new Vector2(-PanelW / 2f + Pad + 27f + Lean * playUp, playUp), new Vector2(54f, PlayH));
            var play = UGUIShip.CreateButton(Skew(playRoot, Vector2.zero, new Vector2(54f, PlayH), "PlaySkew"), new Rect(0, 0, 54f, PlayH),
                "users.pick.play", Warm, Color.white, 12,
                new Action(() => { if (!_leaving) BeanMonitorService.Instance.StartCoroutine(PlayRoutine().WrapToIl2Cpp()); }));
            Fill(play);
            foreach (var t in play.GetComponentsInChildren<Text>(true)) Unskew(t.rectTransform);
            TickSlides();
        }

        static void TickSlides()
        {
            foreach (var kv in _slides)
            {
                if (kv.Key == null) continue;
                var p = _canvasRt.InverseTransformPoint(kv.Key.position);
                kv.Value.anchoredPosition = new Vector2(Lean * (p.y - BlockPos.y) - (p.x - BlockPos.x), 0f);
            }
        }

        static void SolveSkew()
        {
            float beta = 0.5f * Mathf.Atan2(2f * Lean, -Lean * Lean);
            var v1 = new Vector2(Mathf.Cos(beta), Mathf.Sin(beta));
            var v2 = new Vector2(-Mathf.Sin(beta), Mathf.Cos(beta));
            var m1 = new Vector2(v1.x + Lean * v1.y, v1.y);
            var m2 = new Vector2(v2.x + Lean * v2.y, v2.y);
            _skS1 = m1.magnitude;
            _skS2 = m2.magnitude;
            _skA = Mathf.Atan2(m1.y, m1.x) * Mathf.Rad2Deg;
            _skB = -beta * Mathf.Rad2Deg;
        }

        static RectTransform Skew(Transform parent, Vector2 pos, Vector2 size, string name)
        {
            var a = new GameObject(name).AddComponent<RectTransform>();
            a.SetParent(parent, false);
            Place(a, new Vector2(0.5f, 0.5f), pos, Vector2.zero).localRotation = Quaternion.Euler(0f, 0f, _skA);
            var s = new GameObject("Scale").AddComponent<RectTransform>();
            s.SetParent(a, false);
            Place(s, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).localScale = new Vector3(_skS1, _skS2, 1f);
            var b = new GameObject("Inner").AddComponent<RectTransform>();
            b.SetParent(s, false);
            Place(b, new Vector2(0.5f, 0.5f), Vector2.zero, size).localRotation = Quaternion.Euler(0f, 0f, _skB);
            return b;
        }

        static void Unskew(RectTransform rt)
        {
            var a = new GameObject("Unskew").AddComponent<RectTransform>();
            a.SetParent(rt.parent, false);
            Fill(a).localRotation = Quaternion.Euler(0f, 0f, -_skB);
            var s = new GameObject("Scale").AddComponent<RectTransform>();
            s.SetParent(a, false);
            Fill(s).localScale = new Vector3(1f / _skS1, 1f / _skS2, 1f);
            var r = new GameObject("Rotate").AddComponent<RectTransform>();
            r.SetParent(s, false);
            Fill(r).localRotation = Quaternion.Euler(0f, 0f, -_skA);
            rt.SetParent(r, false);
        }

        static RectTransform Place(Component c, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = c.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static RectTransform Fill(Component c)
        {
            var rt = c.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static RawImage Raw(Transform parent, string name, Texture tex)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<RawImage>();
            r.texture = tex;
            r.raycastTarget = false;
            return r;
        }

        static Texture2D Bake(int w, int h, Func<float, float, Color> f)
        {
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = f((x + 0.5f) / w * 2f - 1f, (y + 0.5f) / h * 2f - 1f);
                    float a = Mathf.Max(0f, c.a - 0.012f) / 0.988f;
                    a = Mathf.Clamp01(a + (float)(Rng.NextDouble() - 0.5) * (3f / 255f) * Mathf.Min(1f, a * 30f));
                    px[y * w + x] = new Color(c.r, c.g, c.b, a);
                }
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4,
            };
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }

        static Color Hot(float white) => Color.Lerp(new Color(1f, 0.55f, 0.18f), new Color(1f, 0.97f, 0.9f), Mathf.Clamp01(white));

        static Texture2D CoreTex() => Bake(512, 512, (x, y) =>
        {
            float d = Mathf.Sqrt(x * x + y * y);
            float core = Mathf.Exp(-d * d * 70f);
            var c = Hot(core * 1.5f);
            c.a = Mathf.Clamp01(core + Mathf.Exp(-d * 5f) * 0.45f * Mathf.Clamp01(1f - d));
            c.a *= c.a;
            return c;
        });

        static Texture2D RaysTex(int count, float sharp, float strength, int seed)
        {
            var r = new System.Random(seed);
            var gain = new float[count];
            var len = new float[count];
            var off = new float[count];
            for (int i = 0; i < count; i++)
            {
                gain[i] = 0.3f + (float)r.NextDouble() * 0.7f;
                len[i] = 0.35f + (float)r.NextDouble() * 0.65f;
                off[i] = (float)r.NextDouble() * 0.5f - 0.25f;
            }
            return Bake(1024, 1024, (x, y) =>
            {
                float d = Mathf.Sqrt(x * x + y * y);
                float a = (Mathf.Atan2(y, x) / (2f * Mathf.PI) + 1f) % 1f * count;
                int i = Mathf.FloorToInt(a) % count;
                float across = Mathf.Abs(a - Mathf.Floor(a) - 0.5f - off[i]) * 2f;
                float ray = Mathf.Pow(Mathf.Clamp01(1f - across), sharp) * gain[i];
                float fall = Mathf.Exp(-d / (len[i] * 0.38f)) * Mathf.Clamp01((1f - d) * 3f) * Mathf.Clamp01(d * 8f);
                var c = Hot(Mathf.Exp(-d * 6f));
                c.a = Mathf.Clamp01(ray * fall * strength);
                return c;
            });
        }
    }
}
