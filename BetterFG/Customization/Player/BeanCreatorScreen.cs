using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Pets;
using BetterFG.Customization.UI;
using BetterFG.Features.TimePlacement;
using BetterFG.Services;
using BetterFG.UI;
using BetterFG.UI.Tabs;
using BetterFG.Utilities;
using BettrFG.uGUI;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace BetterFG.Customization.Player
{
    public class BeanCreatorTab : SwitchTab
    {
        public BeanCreatorTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Bean Creator";
        protected override string TitleId => "bean_creator.title";
        protected override string BgResource => "BetterFG.assets.ui.cskins.bg.png";
        protected override string SwitchLabel => "ui.back_3";

        public Action<RectTransform> Build;
        public Action Back;

        protected override void BuildContent(RectTransform contentRoot) => Build(contentRoot);

        protected override Tab MakeSwitchTarget()
        {
            Back();
            return null;
        }

        public override Tab MakeFallbackTab() => null;
    }

    public class BeanCreatorScreen : MonoBehaviour
    {
        public BeanCreatorScreen(IntPtr ptr) : base(ptr) { }

        public static BeanCreatorScreen Instance;

        const int ContentOrder = 501;
        const float PAD = 12f;
        const float GAP = 10f;
        const float MODE_Y = -10f;
        const float MODE_H = 44f;
        const float SLOT_H = 29f;
        const float TILE_GAP = 8f;
        const float EDGE = 22f;

        static float TAB_H => UIScale.TAB_CONTENT_H * 1.54f;
        static float LOW_Y => -(TAB_H - (MODE_Y + MODE_H + 12f) * 0.98f);

        const float BAR_H = 44f;
        const float BTN_H = 30f;
        const string PANEL_SPRITE = "BetterFG.assets.ui.general.uisprite_squarey.png";
        const string PLINTH_KEY = "aa5228dd13815ca4fa38ba303aba6393";

        static readonly Vector3 Stage = new Vector3(0f, -3000f, 0f);
        static readonly Vector3 OwnerOffset = new Vector3(-1.6f, 0f, 0f);
        const float BEAN_H = 1.98f;

        static readonly Color WHITE = UGUIShip.WHITE;
        static readonly Color BTN_DARK = UGUIShip.BTN_DARK;
        static readonly Color BTN_BLUE = new Color(0.22f, 0.34f, 0.55f, 1f);
        static readonly Color BTN_GREEN = new Color(0.22f, 0.42f, 0.26f, 1f);
        static readonly Color SLOT_ON = new Color(0.1f, 0.32f, 0.1f, 1f);
        static readonly Color FIELD_BG = new Color(0.05f, 0.05f, 0.06f, 1f);
        static readonly Color TILE = new Color(0.24f, 0.24f, 0.28f, 1f);
        static readonly Color ACCENT = new Color(0.98f, 0.78f, 0.18f, 1f);
        static readonly Color ON_ACCENT = new Color(0.1f, 0.1f, 0.12f, 1f);
        static readonly Color THUMB_EMPTY = new Color(1f, 1f, 1f, 0.05f);

        const string Upper = "upper", Lower = "lower", Pattern = "pattern", Faceplate = "faceplate", Colour = "colour";
        static readonly (string id, string label)[] CATEGORIES =
        {
            (Upper, "ui.upper"), (Lower, "ui.lower"), (Pattern, "ui.pattern_2"), (Faceplate, "ui.faceplate"), (Colour, "ui.colour_2"),
        };

        static readonly (string icon, string label)[] MODES =
        {
            ("UI_IconCombinedCostume", "bean_creator.game_cosmetics"),
            ("UI_Icon_SpecialMaterial_Image", "bean_creator.textures"),
            ("Icon_CreatorMadeTag", "bean_creator.ugc"),
        };
        const string NONE_ICON = "UI_customise_icon_none_1080P";

        static readonly Vector3 KeyAngles = new Vector3(35f, 18f, 0f);
        static readonly Vector3 FillAngles = new Vector3(12f, -142f, 0f);
        static readonly Color AMBIENT = new Color(0.5f, 0.47f, 0.42f);
        static readonly string[] ART =
        {
            NONE_ICON, MODES[0].icon, MODES[1].icon, MODES[2].icon,
            "UI_Generic_Glow", "UI_Vignette", "UI_IconSave_Image", "UI_IconExit_Image",
        };
        static TMPro.TMP_FontAsset _titleFont;
        static Material _titleMat;

        BeanLook _look;
        Action<BeanLook> _done;
        Canvas _canvas, _backdrop;
        BeanCreatorTab _tab;
        RectTransform _tabRoot, _hostRoot, _modesRt, _ingameRt, _listContent;
        Tab _hosted;
        InputField _nameField, _searchField;
        Text _equippedLbl, _scaleLbl;
        readonly Button[] _slotBtns = new Button[5];
        readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();
        readonly List<ItemDefinitionSO> _results = new List<ItemDefinitionSO>();
        string _category = Upper;
        float _tile;
        bool _raised, _closing;
        Coroutine _slide;

        bool _withOwner;
        GameObject _camHost, _beanHolder, _ownerHolder, _arrow, _stage;
        Transform _bean;
        Camera _cam;
        AmbientMode _wasAmbientMode;
        Color _wasAmbient;
        bool _wasFog;
        float _wasReflection;
        AsyncOperationHandle<GameObject> _plinthOp;
        int _gen;

        public static void Open(BeanLook look, Action<BeanLook> done, bool withOwner = false)
        {
            if (Instance != null) { done?.Invoke(null); return; }

            var go = new GameObject("BettrFG_BeanCreator");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<BeanCreatorScreen>();
            Instance._look = look;
            Instance._done = done;
            Instance._withOwner = withOwner;
        }

        void Start() => StartCoroutine(OpenRoutine().WrapToIl2Cpp());

        IEnumerator OpenRoutine()
        {
            LoadingScreenService.Show();
            yield return new WaitForSecondsRealtime(0.35f);

            try
            {
                WorldHideService.HideCanvases(this);
                BuildStage();
                BuildBackdrop();
                if (_withOwner) BuildPetArrow();
                Build();
                RebuildBean();
                if (_withOwner)
                    StartCoroutine(BuildBeanRoutine(_gen, BetterFG.Customization.Profiles.ProfileService.LocalLook(), true).WrapToIl2Cpp());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"bean creator wouldn't build ({ex}), backing out instead of leaving you stuck behind the cover");
                Finish(null);
                yield break;
            }

            yield return new WaitForSecondsRealtime(0.15f);
            yield return StartCoroutine(LoadingScreenService.HideRoutine().WrapToIl2Cpp());
        }

        void Finish(BeanLook result)
        {
            if (_closing) return;
            _closing = true;
            StartCoroutine(CloseRoutine(result).WrapToIl2Cpp());
        }

        IEnumerator CloseRoutine(BeanLook result)
        {
            LoadingScreenService.Show();
            yield return new WaitForSecondsRealtime(0.35f);

            _gen++;
            var done = _done;
            _done = null;

            try
            {
                RenderSettings.ambientMode = _wasAmbientMode;
                RenderSettings.ambientLight = _wasAmbient;
                RenderSettings.fog = _wasFog;
                RenderSettings.reflectionIntensity = _wasReflection;
                if (_stage != null) Destroy(_stage);
                if (_plinthOp.IsValid()) Addressables.Release(_plinthOp);
                if (_beanHolder != null) Destroy(_beanHolder);
                if (_ownerHolder != null) Destroy(_ownerHolder);
                if (_arrow != null) Destroy(_arrow);
                if (_camHost != null) Destroy(_camHost);
                if (_canvas != null) Destroy(_canvas.gameObject);
                if (_backdrop != null) Destroy(_backdrop.gameObject);
            }
            catch (Exception ex) { Plugin.Log.LogError($"bean creator teardown threw: {ex}"); }

            WorldHideService.Show(this);
            Instance = null;

            try { done?.Invoke(result); }
            catch (Exception ex) { Plugin.Log.LogError($"whoever opened the bean creator threw on the way back: {ex}"); }

            yield return StartCoroutine(LoadingScreenService.HideRoutine().WrapToIl2Cpp());
            Destroy(gameObject);
        }

        void OnDestroy() => WorldHideService.Show(this);

        void BuildStage()
        {
            _camHost = new GameObject("BettrFG_BeanCreatorCam");
            _camHost.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(_camHost);

            _cam = _camHost.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black;
            _cam.cullingMask = 1 << LeaderboardMugshotScene.Layer;
            _cam.orthographic = false;
            _cam.fieldOfView = 40f;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 400f;
            _cam.depth = 1000f;
            _cam.allowHDR = false;
            _cam.useOcclusionCulling = false;
            _cam.ResetAspect();

            AddLight("Key", KeyAngles, 1.3f);
            AddLight("Fill", FillAngles, 0.6f);

            _wasAmbientMode = RenderSettings.ambientMode;
            _wasAmbient = RenderSettings.ambientLight;
            _wasFog = RenderSettings.fog;
            _wasReflection = RenderSettings.reflectionIntensity;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = AMBIENT;
            RenderSettings.fog = false;
            RenderSettings.reflectionIntensity = 0.15f;

            float fit = Mathf.Clamp(_look.scale <= 0f ? 1f : _look.scale, 0.45f, 2f);
            var focus = Stage;
            if (_withOwner) { fit = Mathf.Max(fit, 1.2f); focus += OwnerOffset * 0.5f; }
            var start = focus + new Vector3(0f, 0.97f * fit, 4.6f * fit);
            var euler = Quaternion.LookRotation(focus + Vector3.up * 0.49f * fit - start, Vector3.up).eulerAngles;
            _camHost.transform.SetPositionAndRotation(start, Quaternion.Euler(euler.x, euler.y, 0f));

            _stage = new GameObject("BettrFG_BeanCreatorStage");
            _stage.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(_stage);

            var spotGo = new GameObject("Spot");
            spotGo.transform.SetParent(_stage.transform, false);
            spotGo.transform.position = focus + new Vector3(0.6f, 5.5f, 2.6f);
            spotGo.transform.LookAt(focus + Vector3.up * 0.6f);
            var spot = spotGo.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.cullingMask = 1 << LeaderboardMugshotScene.Layer;
            spot.renderMode = LightRenderMode.ForcePixel;
            spot.spotAngle = 60f;
            spot.innerSpotAngle = 30f;
            spot.range = 14f;
            spot.intensity = 2f;
            spot.shadows = LightShadows.None;

            StartCoroutine(PlinthRoutine().WrapToIl2Cpp());
        }

        IEnumerator PlinthRoutine()
        {
            _plinthOp = Addressables.LoadAssetAsync<GameObject>(PLINTH_KEY);
            yield return _plinthOp;
            if (_closing || _plinthOp.Status != AsyncOperationStatus.Succeeded || _plinthOp.Result == null)
            {
                Plugin.Log.LogWarning($"plinth {PLINTH_KEY} didn't load ({_plinthOp.Status}), bean creator stands on air");
                yield break;
            }
            Plinth(_plinthOp.Result, Stage);
            if (_withOwner) Plinth(_plinthOp.Result, Stage + OwnerOffset);
        }

        void Plinth(GameObject prefab, Vector3 feet)
        {
            var go = Instantiate(prefab, _stage.transform, false);
            go.transform.rotation = Quaternion.Euler(0f, 189f, 0f);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
            GameObjectHelper.SetLayerRecursively(go, LeaderboardMugshotScene.Layer);
            var b = go.GetComponentInChildren<Renderer>().bounds;
            go.transform.position += new Vector3(feet.x - b.center.x, feet.y - b.max.y, feet.z - b.center.z);
        }

        void AddLight(string name, Vector3 angles, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_camHost.transform, false);
            go.transform.localRotation = Quaternion.Euler(angles);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.cullingMask = 1 << LeaderboardMugshotScene.Layer;
            light.color = Color.white;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
        }

        void BuildBackdrop()
        {
            _backdrop = UGUIShip.CreateCanvas("BettrFG_BeanCreatorBackdrop");
            _backdrop.sortingOrder = -1000;
            _backdrop.renderMode = RenderMode.ScreenSpaceCamera;
            _backdrop.worldCamera = _cam;
            _backdrop.planeDistance = 350f;

            var bg = new GameObject("Backdrop");
            bg.transform.SetParent(_backdrop.transform, false);
            var brt = Stretch(bg);
            var img = bg.AddComponent<Image>();
            img.color = Color.black;
            img.raycastTarget = false;
            ScreenBackgroundService.BuildBackdrop(brt, ScreenBackgroundService.Screen.Explore, 0.62f);

            var mask = brt.Find("FallForceClone")?.GetComponentInChildren<Mask>(true);
            if (mask != null) ScreenBackgroundService.FindCirclesChild(mask.transform)?.gameObject.SetActive(false);
            brt.Find("Dim").GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);

            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                if (s != null && Array.IndexOf(ART, s.name) >= 0 && !_sprites.ContainsKey(s.name)) _sprites[s.name] = s;

            var glow = Overlay(brt, "UI_Generic_Glow", new Color(1f, 0.95f, 0.8f, 0.3f));
            glow.anchorMin = glow.anchorMax = new Vector2(0.5f, 0.55f);
            glow.sizeDelta = new Vector2(1400f, 1100f);
            var beam = Overlay(brt, "UI_Generic_Glow", new Color(1f, 0.97f, 0.88f, 0.18f));
            beam.anchorMin = beam.anchorMax = new Vector2(0.5f, 0.85f);
            beam.sizeDelta = new Vector2(520f, 1500f);
            Overlay(brt, "UI_Vignette", new Color(0f, 0.05f, 0.05f, 0.6f));

            foreach (var t in _backdrop.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = LeaderboardMugshotScene.Layer;
        }

        RectTransform Overlay(Transform parent, string sprite, Color color)
        {
            var go = new GameObject(sprite);
            var rt = Stretch(go, parent);
            var img = go.AddComponent<Image>();
            img.enabled = _sprites.TryGetValue(sprite, out var s);
            img.sprite = s;
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }

        void RebuildBean()
        {
            _gen++;
            if (_beanHolder != null) Destroy(_beanHolder);
            _beanHolder = null;
            _bean = null;
            StartCoroutine(BuildBeanRoutine(_gen, _look.ToPetData()).WrapToIl2Cpp());
        }

        IEnumerator BuildBeanRoutine(int gen, PetData data, bool owner = false)
        {
            GameObject bean = null;
            yield return PetBeanBuilder.Build(data, new Action<GameObject>(b => bean = b), forPreview: true);
            if (_closing || (!owner && gen != _gen)) { if (bean != null) Destroy(bean); yield break; }
            if (bean == null) { Plugin.Log.LogWarning($"bean creator got nothing back from the builder (owner={owner}), stage stays empty"); yield break; }

            var holder = new GameObject(owner ? "BettrFG_BeanCreatorOwner" : "BettrFG_BeanCreatorBean");
            holder.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(holder);
            bean.transform.SetParent(holder.transform, false);

            foreach (var t in bean.GetComponentsInChildren<TMPro.TMP_Text>(true))
                if (t != null) DestroyImmediate(t.gameObject);

            GameObjectHelper.SetLayerRecursively(bean, LeaderboardMugshotScene.Layer);
            bean.transform.SetPositionAndRotation(owner ? Stage + OwnerOffset : Stage, Quaternion.identity);
            foreach (var r in bean.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.updateWhenOffscreen = true;
            if (owner) { _ownerHolder = holder; yield break; }
            _beanHolder = holder;
            _bean = bean.transform;
        }

        void BuildPetArrow()
        {
            Image src = null;
            foreach (var c in Resources.FindObjectsOfTypeAll<PlayerInfoDisplayCanvas>())
                if (c != null && c.name == "LocalPlayerNameTag" && c._arrowImage != null) { src = c._arrowImage; break; }
            if (src == null || src.sprite == null) { Plugin.Log.LogWarning("no LocalPlayerNameTag prefab loaded, so the pet editor goes without its arrow"); return; }

            _arrow = new GameObject("BettrFG_BeanCreatorArrow");
            _arrow.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(_arrow);
            _arrow.layer = LeaderboardMugshotScene.Layer;
            var sr = _arrow.AddComponent<SpriteRenderer>();
            sr.sprite = src.sprite;
            sr.color = src.color;
            _arrow.transform.localScale = Vector3.one * (0.45f / src.sprite.bounds.size.x);
            _arrow.SetActive(false);
        }

        void TrackArrow()
        {
            bool on = _bean != null;
            if (_arrow.activeSelf != on) _arrow.SetActive(on);
            if (!on) return;
            float scale = Mathf.Max(_look.scale, 0.3f);
            _arrow.transform.SetPositionAndRotation(_bean.position + Vector3.up * (BEAN_H * scale + 0.35f), _camHost.transform.rotation);
        }

        void Build()
        {
            _canvas = BetterFGUIMan.CreateCanvas();
            _canvas.gameObject.name = "BettrFG_BeanCreator_Canvas";
            _canvas.sortingOrder = ContentOrder;

            _tabRoot = TabRoot("BeanCreatorTab");
            _tab = BetterFGTabRegistry.NewTab<BeanCreatorTab>();
            _tab.Build = new Action<RectTransform>(BuildTabContent);
            _tab.Back = new Action(LowerTab);
            Mount(_tab, _tabRoot);
            SetRaised(false, false);
            StyleTitle();

            var panel = EmbeddedResourceandUnity.LoadSprite(PANEL_SPRITE, new Vector2(0.5f, 0.5f), new Vector4(46f, 46f, 46f, 46f));
            BuildIdentity(panel);

            var bgRt = new GameObject("ButtonsBg").AddComponent<RectTransform>();
            bgRt.SetParent(_canvas.transform, false);
            Pin(bgRt, new Vector2(1f, 0f), new Vector2(15.718f, -39.663f));
            bgRt.sizeDelta = new Vector2(321.395f, 126f);
            bgRt.localScale = new Vector3(1.185f, 1f, 1f);
            bgRt.localEulerAngles = new Vector3(35.126f, 32.22f, 0f);
            Panel(bgRt.gameObject.AddComponent<Image>(), panel);

            float y = EDGE + (BAR_H - BTN_H) * 0.5f;
            var save = BarButton("ui.save", BTN_GREEN, "UI_IconSave_Image", new Action(Save));
            Pin(save, new Vector2(1f, 0f), new Vector2(-EDGE, y));
            var cancel = BarButton("ui.cancel", BTN_DARK, "UI_IconExit_Image", new Action(Cancel));
            Pin(cancel, new Vector2(1f, 0f), new Vector2(-EDGE - save.sizeDelta.x - 8f, y));
        }

        RectTransform BarButton(string label, Color color, string icon, Action onClick)
        {
            var b = UGUIShip.CreateButton(_canvas.transform, new Rect(0f, 0f, 100f, BTN_H), label, color, WHITE, UIScale.FS_TITLE, onClick);
            Icon(b.transform, icon, new Rect(14f, (BTN_H - 16f) * 0.5f, 16f, 16f));
            var lbl = b.GetComponentInChildren<Text>();
            lbl.alignment = TextAnchor.MiddleLeft;
            var lrt = lbl.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(40f, 0f);
            lrt.offsetMax = Vector2.zero;
            var rt = b.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(40f + lbl.preferredWidth + 16f, BTN_H);
            return rt;
        }

        static void Panel(Image img, Sprite sprite)
        {
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1.5f;
            img.color = Color.black;
            img.raycastTarget = false;
        }

        void StyleTitle()
        {
            Text lbl = null;
            foreach (var t in _tabRoot.GetComponentsInChildren<Text>(true))
                if (t.name == "Label" && t.transform.parent.name == "TitleBar") { lbl = t; break; }
            if (_titleFont == null)
                foreach (var f in Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>())
                    if (f.name == "TitanOne-Expanded SDF (Title)") { _titleFont = f; break; }
            if (_titleMat == null)
                foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                    if (m.name == "TitanOne-Expanded SDF Material - DropShadowAlpha_2_0") { _titleMat = m; break; }
            if (lbl == null || _titleFont == null || _titleMat == null)
            {
                Plugin.Log.LogWarning($"bean creator title stays plain (label {lbl != null}, titan {_titleFont != null}, material {_titleMat != null})");
                return;
            }

            var rt = new GameObject("TitanTitle").AddComponent<RectTransform>();
            rt.SetParent(lbl.transform, false);
            rt.SetAsFirstSibling();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(8f, 6f);
            rt.offsetMax = new Vector2(8f, 18f);
            var tmp = rt.gameObject.AddComponent<TMPro.TextMeshProUGUI>();
            tmp.overflowMode = TMPro.TextOverflowModes.Overflow;
            tmp.font = _titleFont;
            tmp.fontSharedMaterial = _titleMat;
            tmp.text = lbl.text;
            tmp.fontSize = 34f;
            tmp.characterSpacing = 4f;
            tmp.alignment = TMPro.TextAlignmentOptions.BottomLeft;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;
            tmp.color = WHITE;

            lbl.color = Color.clear;
            foreach (var g in lbl.GetComponentsInChildren<Graphic>(true))
                if (g.transform.parent == lbl.transform && (g.name == "TmpShadow" || g.name == "TmpMirror")) g.enabled = false;
            var link = lbl.transform.Find("Link_ui.back_3")?.GetComponent<RectTransform>();
            if (link != null) link.anchoredPosition = new Vector2(tmp.preferredWidth + 14f, link.anchoredPosition.y);
        }

        void BuildTabContent(RectTransform ca)
        {
            float w = UIScale.TAB_W - PAD * 2f;

            _modesRt = Stretch(new GameObject("Modes"), ca);
            var actions = new[] { new Action(RaiseTab), new Action(OpenTextures), new Action(OpenUgc) };
            float mw = (w - 12f) / 3f;
            for (int i = 0; i < MODES.Length; i++)
            {
                var b = UGUIShip.CreateButton(_modesRt, new Rect(PAD + i * (mw + 6f), MODE_Y, mw, MODE_H), "",
                    BTN_DARK, WHITE, UIScale.FS_SM, actions[i]);
                Icon(b.transform, MODES[i].icon, new Rect(10f, 7f, 30f, 30f));
                var lbl = UGUIShip.CreateLabel(b.transform, new Rect(46f, 0f, mw - 52f, MODE_H), MODES[i].label,
                    UIScale.FS_SM + 1, WHITE, TextAnchor.MiddleLeft);
                lbl.fontStyle = FontStyle.Bold;
                lbl.raycastTarget = false;
            }

            _ingameRt = Stretch(new GameObject("InGame"), ca);
            float y = MODE_Y;
            float sw = (w - 12f) / CATEGORIES.Length;
            for (int i = 0; i < CATEGORIES.Length; i++)
            {
                string id = CATEGORIES[i].id;
                _slotBtns[i] = UGUIShip.CreateButton(_ingameRt, new Rect(PAD + i * (sw + 3f), y, sw, SLOT_H),
                    CATEGORIES[i].label, BTN_DARK, WHITE, UIScale.FS_SM, new Action(() => PickCategory(id)));
            }
            y += SLOT_H + GAP;

            _searchField = UGUIShip.CreateInputField(_ingameRt, new Rect(PAD, y, w, 26f),
                "ui.search_by_name", FIELD_BG, WHITE, UIScale.FS_SM);
            _searchField.onEndEdit.AddListener(new Action<string>(v => Fetch()));
            y += 26f + GAP;

            _equippedLbl = UGUIShip.CreateLabel(_ingameRt, new Rect(PAD + 2f, y, w, 22f), "",
                UIScale.FS_SM + 1, WHITE, TextAnchor.MiddleLeft);
            _equippedLbl.fontStyle = FontStyle.Bold;
            y += 22f + GAP;

            var scroll = UGUIShip.CreateScrollView(_ingameRt, new Rect(PAD, y, w, TAB_H - y - PAD));
            _listContent = scroll.content;
            _tile = Mathf.Floor((w - UGUIShip.SCROLLBAR_INSET * 2f - TILE_GAP * 3f) / 4f);
            var grid = _listContent.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(_tile, _tile);
            grid.spacing = new Vector2(TILE_GAP, TILE_GAP);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            _listContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        void BuildIdentity(Sprite panel)
        {
            const float W = 543f, ROW = 30f;
            float top = (BAR_H - ROW) * 0.5f;
            var go = new GameObject("Identity");
            go.transform.SetParent(_canvas.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(W, BAR_H);
            Pin(rt, new Vector2(0.5f, 0f), new Vector2(60f, EDGE));

            var bg = Stretch(new GameObject("Bg"), rt);
            bg.offsetMin = new Vector2(-W * 0.05f, -(EDGE + 60f));
            bg.offsetMax = new Vector2(W * 0.05f, 0f);
            Panel(bg.gameObject.AddComponent<Image>(), panel);

            UGUIShip.CreateLabel(rt, new Rect(14f, top, 50f, ROW), "bean_creator.name", 15, WHITE).fontStyle = FontStyle.Bold;
            _nameField = UGUIShip.CreateInputField(rt, new Rect(64f, top, 200f, ROW),
                "bean_creator.name_hint", FIELD_BG, WHITE, 17);
            _nameField.textComponent.fontStyle = FontStyle.Bold;
            UGUIShip.SetInputText(_nameField, _look.name, false);
            _nameField.onEndEdit.AddListener(new Action<string>(v => _look.name = (v ?? "").Trim()));

            var div = new GameObject("Divider");
            div.transform.SetParent(rt, false);
            UGUIShip.SetPixelRect(div.AddComponent<RectTransform>(), new Rect(278f, (BAR_H - 24f) * 0.5f, 2f, 24f));
            div.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);

            UGUIShip.CreateLabel(rt, new Rect(294f, top, 44f, ROW), "bean_creator.scale", 15, WHITE).fontStyle = FontStyle.Bold;
            UGUIShip.CreateSlider(rt, 343f, (BAR_H - 24f) * 0.5f, 140f, "", Mathf.InverseLerp(0.3f, 2f, _look.scale), 24f, 0f, UIScale.FS_SM,
                v => { _look.scale = Mathf.Lerp(0.3f, 2f, v); RefreshScale(); },
                fillColor: BTN_BLUE, reserveLabel: false);
            _scaleLbl = UGUIShip.CreateLabel(rt, new Rect(491f, top, 48f, ROW),
                LocalizationService.Format("bean_creator.scale_fmt", _look.scale.ToString("0.00")),
                UIScale.FS_SM, WHITE, TextAnchor.MiddleLeft);
            _scaleLbl.fontStyle = FontStyle.Bold;
        }

        RectTransform TabRoot(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.sizeDelta = new Vector2(UIScale.TAB_W, TAB_H + Tab.TITLE_H);
            rt.anchoredPosition = new Vector2(UIScale.TAB_MARGIN_X, 0f);
            go.AddComponent<Image>().color = Color.clear;
            return rt;
        }

        static void Mount(Tab tab, RectTransform root)
        {
            tab.transform.SetParent(root, false);
            Stretch(tab.gameObject);
            tab.TabWidth = UIScale.TAB_W;
            tab.TabHeight = TAB_H;
            tab.Initialize(root);
            tab.EnsureBuilt();
            tab.IsOpen = true;
            tab.SetContentActive(true);
        }

        static RectTransform Stretch(GameObject go, Transform parent = null)
        {
            if (parent != null) go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static void Pin(RectTransform rt, Vector2 anchor, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
        }

        Image Icon(Transform parent, string sprite, Rect rect)
        {
            var go = new GameObject("Icon");
            go.transform.SetParent(parent, false);
            UGUIShip.SetPixelRect(go.AddComponent<RectTransform>(), rect);
            var img = go.AddComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            if (_sprites.TryGetValue(sprite, out var s)) img.sprite = s;
            return img;
        }

        void LowerTab() => SetRaised(false, true);

        void RaiseTab()
        {
            SetRaised(true, true);
            PickCategory(_category);
        }

        void SetRaised(bool raised, bool animate)
        {
            _raised = raised;
            _modesRt.gameObject.SetActive(!raised);
            _ingameRt.gameObject.SetActive(raised);
            _tab.IsOpen = raised;
            if (raised) _tab.OnOpened();
            else _tab.OnClosed();

            float y = raised ? 0f : LOW_Y;
            if (animate) SlideTab(_tabRoot, y, raised);
            else _tabRoot.anchoredPosition = new Vector2(UIScale.TAB_MARGIN_X, y);
        }

        void SlideTab(RectTransform rt, float y, bool open)
        {
            if (_slide != null) StopCoroutine(_slide);
            _slide = StartCoroutine(BetterFGUIMan.Slide(rt, y, open).WrapToIl2Cpp());
            if (open) AudioService.PlayTabOpen();
            else AudioService.PlayTabClose();
        }

        void OpenUgc()
        {
            var tab = BetterFGTabRegistry.NewTab<CustomizationTab>();
            tab.PickTargetList = _look.skins;
            tab.OnPickChanged = new Action(RebuildBean);
            tab.PickBack = new Action(CloseHosted);
            Host(tab);
        }

        void OpenTextures()
        {
            var tab = BetterFGTabRegistry.NewTab<PetSkinTextureTab>();
            tab.Entries = _look.texEntries;
            tab.OnChanged = new Action(RebuildBean);
            tab.Back = new Action(CloseHosted);
            Host(tab);
        }

        void Host(Tab tab)
        {
            _tabRoot.gameObject.SetActive(false);
            _hostRoot = TabRoot("Hosted_" + tab.TabTitle);
            _hostRoot.anchoredPosition = new Vector2(UIScale.TAB_MARGIN_X, LOW_Y);
            _hosted = tab;
            Mount(tab, _hostRoot);
            tab.OnOpened();
            SlideTab(_hostRoot, 0f, true);
        }

        public Tab SwapTab(Tab from, Tab to)
        {
            if (from != _hosted) return null;
            Destroy(_hostRoot.gameObject);
            Host(to);
            return to;
        }

        void CloseHosted() => StartCoroutine(CloseHostedRoutine().WrapToIl2Cpp());

        IEnumerator CloseHostedRoutine()
        {
            var root = _hostRoot;
            _hosted.IsOpen = false;
            _hosted.OnClosed();
            _hostRoot = null;
            _hosted = null;

            if (_slide != null) StopCoroutine(_slide);
            _slide = null;
            AudioService.PlayTabClose();
            var slide = BetterFGUIMan.Slide(root, LOW_Y, false);
            while (slide.MoveNext()) yield return slide.Current;

            Destroy(root.gameObject);
            _tabRoot.gameObject.SetActive(true);
            SetRaised(false, false);
        }

        void Cancel() => Finish(null);

        void Save() => Finish(_look);

        void RefreshScale()
        {
            UGUIShip.RelabelText(_scaleLbl, LocalizationService.Format("bean_creator.scale_fmt", _look.scale.ToString("0.00")));
            if (_bean != null) _bean.localScale = Vector3.one * _look.scale;
        }

        void Update()
        {
            if (_closing) return;
            if (_arrow != null) TrackArrow();
            if (Typing()) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_hosted != null) CloseHosted();
                else if (_raised) LowerTab();
                else Cancel();
            }
        }

        static bool Typing()
        {
            if (FGInputLockService.FakeFieldLocked) return true;
            var sel = EventSystem.current?.currentSelectedGameObject;
            return sel != null && sel.GetComponent<InputField>() != null;
        }


        void PickCategory(string id)
        {
            _category = id;
            for (int i = 0; i < CATEGORIES.Length; i++)
                UGUIShip.SetButtonSelected(_slotBtns[i], CATEGORIES[i].id == id, SLOT_ON);
            Fetch();
        }

        void Fetch() => StartCoroutine(FetchCosmetics(_searchField.text?.Trim() ?? "").WrapToIl2Cpp());

        static Il2CppSystem.Type TypeFor(string category)
        {
            switch (category)
            {
                case Upper:
                case Lower: return Il2CppType.Of<CostumeOption>();
                case Pattern: return Il2CppType.Of<SkinPatternOption>();
                case Faceplate: return Il2CppType.Of<FaceplateOption>();
                case Colour: return Il2CppType.Of<ColourOption>();
            }
            return null;
        }

        IEnumerator FetchCosmetics(string filter)
        {
            _results.Clear();
            RebuildRows();
            for (int i = 0; i < 2; i++) yield return null;

            var type = TypeFor(_category);
            if (type == null) yield break;

            Il2CppReferenceArray<UnityEngine.Object> raw = null;
            try { raw = Resources.FindObjectsOfTypeAll(type); }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"cosmetic search died: {ex.Message}");
                UGUIShip.RelabelText(_equippedLbl, LocalizationService.Format("bean_creator.search_failed_fmt", ex.Message));
                yield break;
            }
            if (raw == null || raw.Length == 0)
            {
                Plugin.Log.LogWarning($"no {_category} options loaded yet, the creator grid comes up empty");
                UGUIShip.RelabelText(_equippedLbl, "bean_creator.nothing_loaded");
                yield break;
            }

            for (int i = 0; i < raw.Length && _results.Count < 200; i++)
            {
                if (raw[i] == null) continue;
                ItemDefinitionSO opt;
                try { opt = raw[i].Cast<ItemDefinitionSO>(); } catch { continue; }
                if (opt == null) continue;

                if (_category == Upper || _category == Lower)
                {
                    CostumeOption co = null;
                    try { co = opt.TryCast<CostumeOption>(); } catch { }
                    if (co == null) continue;
                    bool wantTop = _category == Upper;
                    if (wantTop && co.CostumeType == CostumeType.Bottom) continue;
                    if (!wantTop && co.CostumeType == CostumeType.Top) continue;
                }

                if (_category == Colour)
                {
                    ColourOption co = null;
                    try { co = opt.TryCast<ColourOption>(); } catch { }
                    if (co == null || co.TryCast<TeamColourOption>() != null) continue;
                }

                if (string.IsNullOrEmpty(filter)
                    || SkinApplicationService.GetOptionDisplayName(opt).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    _results.Add(opt);
            }

            RebuildRows();
        }

        void RebuildRows()
        {
            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);

            string cur = Current();
            UGUIShip.RelabelText(_equippedLbl, string.IsNullOrEmpty(cur)
                ? "bean_creator.clear" : SkinApplicationService.ResolveOptionDisplayName(cur));

            Tile(string.IsNullOrEmpty(cur), null, true, new Action(() => Pick(null)));
            foreach (var opt in _results)
            {
                var captured = opt;
                Tile(cur == opt.name, CosmeticCell.Wants(opt) ? null : SkinApplicationService.ResolveOptionIconTexture(_category, opt.name), false,
                    new Action(() => Pick(captured)), opt);
            }
        }

        void Tile(bool selected, Texture2D icon, bool clear, Action onClick, UnityEngine.Object option = null)
        {
            var b = UGUIShip.CreateButton(_listContent, new Rect(0f, 0f, _tile, _tile), "",
                selected ? ACCENT : TILE, WHITE, UIScale.FS_SM, onClick);
            float box = _tile - 12f;

            if (clear)
            {
                float s = box * 0.6f;
                var img = Icon(b.transform, NONE_ICON, new Rect((_tile - s) * 0.5f, (_tile - s) * 0.5f, s, s));
                if (selected) img.color = ON_ACCENT;
                return;
            }

            var go = new GameObject("Icon");
            go.transform.SetParent(b.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            if (CosmeticCell.Wants(option))
            {
                rt.sizeDelta = new Vector2(box, box);
                CosmeticCell.Fill(go.transform, option);
                return;
            }
            var raw = go.AddComponent<RawImage>();
            raw.raycastTarget = false;
            if (icon == null) { rt.sizeDelta = new Vector2(box, box); raw.color = THUMB_EMPTY; return; }
            raw.texture = icon;
            float a = (float)icon.width / icon.height;
            rt.sizeDelta = a < 1f ? new Vector2(box * a, box) : new Vector2(box, box / a);
        }

        string Current() =>
            _category == Upper ? _look.top
            : _category == Lower ? _look.bottom
            : _category == Pattern ? _look.pattern
            : _category == Colour ? _look.colour : _look.faceplate;

        void Pick(ItemDefinitionSO opt)
        {
            _look.SetPick(_category, opt);
            RebuildRows();
            RebuildBean();
        }
    }
}
