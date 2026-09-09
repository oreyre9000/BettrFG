using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Pets;
using BetterFG.Features.TimePlacement;
using BetterFG.Services;
using BetterFG.Utilities;
using BettrFG.uGUI;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
using LayoutElement = UnityEngine.UI.LayoutElement;

namespace BetterFG.Customization.Player
{
    public class BeanCreatorScreen : MonoBehaviour
    {
        public BeanCreatorScreen(IntPtr ptr) : base(ptr) { }

        public static BeanCreatorScreen Instance;

        // the UGC picker is a real tab pushed over the top while this screen stays up, so it edits
        // the live look and asks for a rebuild instead of taking the bean off screen
        public List<BetterFG.Network.RemoteSkinEntry> LookSkins => _look.skins;

        public void SetPanelVisible(bool on)
        {
            if (_canvas != null) _canvas.gameObject.SetActive(on);
        }

        public void RefreshFromLook()
        {
            RebuildRows();
            RebuildBean();
        }

        const int ContentOrder = 501;
        const float PANEL_W = 376f;
        const float PAD = 12f;
        const float ROW = 22f;
        const float GAP = 6f;

        const float SPEED = 4f;
        const float BOOST = 3.5f;
        const float LOOK = 0.18f;
        const float PANEL_CLEAR_YAW = -8f;

        static readonly Vector3 Stage = new Vector3(0f, -3000f, 0f);

        static readonly Color WHITE = UGUIShip.WHITE;
        static readonly Color HINT = new Color(1f, 1f, 1f, 0.38f);
        static readonly Color SUB = new Color(1f, 1f, 1f, 0.55f);
        static readonly Color BTN_DARK = UGUIShip.BTN_DARK;
        static readonly Color BTN_BLUE = new Color(0.22f, 0.34f, 0.55f, 1f);
        static readonly Color BTN_GREEN = new Color(0.22f, 0.42f, 0.26f, 1f);
        static readonly Color FIELD_BG = new Color(0.06f, 0.06f, 0.07f, 1f);
        static readonly Color THUMB_EMPTY = new Color(1f, 1f, 1f, 0.05f);

        const string Upper = "upper", Lower = "lower", Pattern = "pattern", Faceplate = "faceplate", Colour = "colour";
        static readonly (string id, string label)[] CATEGORIES =
        {
            (Upper, "ui.upper"), (Lower, "ui.lower"), (Pattern, "ui.pattern_2"), (Faceplate, "ui.faceplate"), (Colour, "ui.colour_2"),
        };

        enum Mode { Cosmetics, Textures }

        static readonly Vector3 KeyAngles = new Vector3(35f, 18f, 0f);
        static readonly Vector3 FillAngles = new Vector3(12f, -142f, 0f);
        static readonly Color AMBIENT = new Color(0.34f, 0.34f, 0.37f);

        BeanLook _look;
        Action<BeanLook> _done;
        Action _addTexture, _pickUgc;
        Action _route;
        Canvas _canvas;
        RectTransform _root;
        InputField _nameField, _searchField;
        RectTransform _listContent;
        Text _categoryLbl, _scaleLbl, _statusLbl;
        Button _arrowPrev, _arrowNext;
        readonly Button[] _modeBtns = new Button[2];
        readonly List<ItemDefinitionSO> _results = new List<ItemDefinitionSO>();
        readonly List<SkinTexEntry> _texResults = new List<SkinTexEntry>();
        Mode _mode = Mode.Cosmetics;
        string _category = Upper;
        float _listW;
        int _rowIdx;
        bool _closing;

        GameObject _camHost, _beanHolder;
        Transform _bean;
        Camera _cam;
        AmbientMode _wasAmbientMode;
        Color _wasAmbient;
        bool _wasFog;
        float _yaw, _pitch;
        Vector3 _lastMouse;
        bool _looking;
        int _gen;

        public static void Open(BeanLook look, Action<BeanLook> done, Action addTexture = null, Action pickUgc = null)
        {
            if (Instance != null) { done?.Invoke(null); return; }

            var go = new GameObject("BettrFG_BeanCreator");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<BeanCreatorScreen>();
            Instance._look = look;
            Instance._done = done;
            Instance._addTexture = addTexture;
            Instance._pickUgc = pickUgc;
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
                Build();
                RebuildBean();
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
            var route = _route;
            _done = null;
            _addTexture = null;
            _pickUgc = null;
            _route = null;

            try
            {
                RenderSettings.ambientMode = _wasAmbientMode;
                RenderSettings.ambientLight = _wasAmbient;
                RenderSettings.fog = _wasFog;
                if (_beanHolder != null) Destroy(_beanHolder);
                if (_camHost != null) Destroy(_camHost);
                if (_canvas != null) Destroy(_canvas.gameObject);
            }
            catch (Exception ex) { Plugin.Log.LogError($"bean creator teardown threw: {ex}"); }

            WorldHideService.Show(this);
            Instance = null;

            try
            {
                done?.Invoke(result);
                route?.Invoke();
            }
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

            AddLight("Key", KeyAngles, 1.1f);
            AddLight("Fill", FillAngles, 0.45f);

            _wasAmbientMode = RenderSettings.ambientMode;
            _wasAmbient = RenderSettings.ambientLight;
            _wasFog = RenderSettings.fog;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = AMBIENT;
            RenderSettings.fog = false;

            float fit = Mathf.Clamp(_look.scale <= 0f ? 1f : _look.scale, 0.45f, 2f);
            var start = Stage + new Vector3(0f, 1.05f * fit, 3.1f * fit);
            var euler = Quaternion.LookRotation(Stage + Vector3.up * 0.62f * fit - start, Vector3.up).eulerAngles;
            _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
            _yaw = euler.y + PANEL_CLEAR_YAW;
            _camHost.transform.SetPositionAndRotation(start, Quaternion.Euler(_pitch, _yaw, 0f));
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

        void RebuildBean()
        {
            _gen++;
            if (_beanHolder != null) Destroy(_beanHolder);
            _beanHolder = null;
            _bean = null;
            StartCoroutine(BuildBeanRoutine(_gen, _look.ToPetData()).WrapToIl2Cpp());
        }

        IEnumerator BuildBeanRoutine(int gen, PetData data)
        {
            GameObject bean = null;
            yield return PetBeanBuilder.Build(data, new Action<GameObject>(b => bean = b), forPreview: true);
            if (gen != _gen || _closing) { if (bean != null) Destroy(bean); yield break; }
            if (bean == null) { Plugin.Log.LogWarning("bean creator got nothing back from the builder, stage stays empty"); yield break; }

            _beanHolder = new GameObject("BettrFG_BeanCreatorBean");
            _beanHolder.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(_beanHolder);
            bean.transform.SetParent(_beanHolder.transform, false);

            foreach (var t in bean.GetComponentsInChildren<TMPro.TMP_Text>(true))
                if (t != null) DestroyImmediate(t.gameObject);

            GameObjectHelper.SetLayerRecursively(bean, LeaderboardMugshotScene.Layer);
            bean.transform.SetPositionAndRotation(Stage, Quaternion.identity);
            foreach (var r in bean.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.updateWhenOffscreen = true;
            _bean = bean.transform;
        }

        void Build()
        {
            _canvas = UGUIShip.CreateCanvas("BettrFG_BeanCreator");
            _canvas.sortingOrder = ContentOrder;

            var rootGo = new GameObject("Root");
            rootGo.transform.SetParent(_canvas.transform, false);
            _root = rootGo.AddComponent<RectTransform>();
            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = new Vector2(20f, -20f);
            _root.sizeDelta = new Vector2(PANEL_W, UIScaleService.CurrentRef.y * 0.86f);
            UGUIShip.ApplyDeluxPanelDark(rootGo.AddComponent<Image>(), 2f);

            float w = PANEL_W - PAD * 2f;
            float y = PAD;

            var title = UGUIShip.CreateLabel(_root, new Rect(PAD, y, w, 24f),
                "bean_creator.title", UIScale.FS_TITLE, WHITE, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            y += 24f + 4f;

            UGUIShip.CreateDivider(_root, PAD, y, w);
            y += 1f + GAP;

            float third = (w - GAP * 2f) / 3f;
            _modeBtns[0] = UGUIShip.CreateButton(_root, new Rect(PAD, y, third, ROW),
                "bean_creator.game_cosmetics", BTN_BLUE, WHITE, UIScale.FS_SM, new Action(PickCosmetics));
            _modeBtns[1] = UGUIShip.CreateButton(_root, new Rect(PAD + third + GAP, y, third, ROW),
                "bean_creator.textures", BTN_DARK, WHITE, UIScale.FS_SM, new Action(PickTextures));
            if (_pickUgc != null)
                UGUIShip.CreateButton(_root, new Rect(PAD + (third + GAP) * 2f, y, third, ROW),
                    "bean_creator.ugc", BTN_DARK, WHITE, UIScale.FS_SM, new Action(RequestUgc));
            y += ROW + GAP;

            float lblW = 48f, valW = 46f;
            UGUIShip.CreateLabel(_root, new Rect(PAD, y, lblW, ROW), "ui.name", UIScale.FS_SM, SUB);
            _nameField = UGUIShip.CreateInputField(_root, new Rect(PAD + lblW, y, w - lblW, ROW),
                "bean_creator.name_hint", FIELD_BG, WHITE, UIScale.FS_SM);
            UGUIShip.SetInputText(_nameField, _look.name, false);
            _nameField.onEndEdit.AddListener(new Action<string>(v => _look.name = (v ?? "").Trim()));
            y += ROW + 4f;

            UGUIShip.CreateLabel(_root, new Rect(PAD, y, lblW, ROW), "bean_creator.scale", UIScale.FS_SM, SUB);
            UGUIShip.CreateSlider(_root, PAD + lblW, y, w - lblW - valW - 4f, "",
                Mathf.InverseLerp(0.3f, 2f, _look.scale), ROW, 0f, UIScale.FS_SM,
                v => { _look.scale = Mathf.Lerp(0.3f, 2f, v); RefreshScale(); },
                fillColor: BTN_BLUE, reserveLabel: false);
            _scaleLbl = UGUIShip.CreateLabel(_root, new Rect(PAD + w - valW, y, valW, ROW),
                LocalizationService.Format("bean_creator.scale_fmt", _look.scale.ToString("0.00")),
                UIScale.FS_SM, WHITE, TextAnchor.MiddleRight);
            y += ROW + GAP;

            UGUIShip.CreateDivider(_root, PAD, y, w);
            y += 1f + GAP;

            float pickH = ROW - 2f;
            var categoryRt = UGUIShip.CreatePanel(_root, new Rect(PAD, y, w, pickH), Color.clear, "CategoryRow");
            categoryRt.GetComponent<Image>().raycastTarget = false;
            _categoryLbl = UGUIShip.CreateCarousel(categoryRt, new Rect(0f, 0f, w, pickH),
                Labels(), 0, new Action<int>(StepCategory), BTN_DARK, UIScale.FS_SM);
            var arrows = categoryRt.GetComponentsInChildren<Button>(true);
            if (arrows.Length >= 2) { _arrowPrev = arrows[0]; _arrowNext = arrows[1]; }
            y += pickH + 4f;

            float searchBtnW = 66f;
            _searchField = UGUIShip.CreateInputField(_root, new Rect(PAD, y, w - searchBtnW - 4f, pickH),
                "ui.search_by_name", FIELD_BG, WHITE, UIScale.FS_SM);
            _searchField.onEndEdit.AddListener(new Action<string>(v => Fetch()));
            UGUIShip.CreateButton(_root, new Rect(PAD + w - searchBtnW, y, searchBtnW, pickH),
                "ui.search", BTN_BLUE, WHITE, UIScale.FS_SM, new Action(Fetch));
            y += pickH + GAP;

            const float statusH = 16f, hintH = 28f;
            float listH = Mathf.Max(_root.sizeDelta.y - y - PAD - ROW - statusH - hintH - GAP * 2f, 180f);
            _listW = w - 26f;
            var scroll = UGUIShip.CreateScrollView(_root, new Rect(PAD, y, w, listH));
            _listContent = scroll.content;
            var layout = _listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = false;
            layout.spacing = 2f;
            _listContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            y += listH + 4f;

            _statusLbl = UGUIShip.CreateLabel(_root, new Rect(PAD, y, w, statusH), "", UIScale.FS_SM, SUB);
            y += statusH + 2f;

            var hint = UGUIShip.CreateLabel(_root, new Rect(PAD, y, w, hintH), "bean_creator.fly_hint",
                UIScale.FS_SM - 1, HINT, TextAnchor.UpperLeft);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            hint.verticalOverflow = VerticalWrapMode.Truncate;
            y += hintH + GAP;

            float half = (w - GAP) / 2f;
            UGUIShip.CreateButton(_root, new Rect(PAD, y, half, ROW),
                "ui.cancel", BTN_DARK, WHITE, UIScale.FS_SM, new Action(Cancel));
            UGUIShip.CreateButton(_root, new Rect(PAD + half + GAP, y, half, ROW),
                "ui.save", BTN_GREEN, WHITE, UIScale.FS_SM, new Action(Save));

            _root.sizeDelta = new Vector2(PANEL_W, y + ROW + PAD);
            SetMode(_mode);
        }

        void Cancel() => Finish(null);

        void Save() => Finish(_look);

        void SetStatus(string msg)
        {
            if (_statusLbl != null) UGUIShip.RelabelText(_statusLbl, msg);
        }

        void RefreshScale()
        {
            if (_scaleLbl != null)
                UGUIShip.RelabelText(_scaleLbl, LocalizationService.Format("bean_creator.scale_fmt", _look.scale.ToString("0.00")));
            if (_bean != null) _bean.localScale = Vector3.one * _look.scale;
        }

        void Update()
        {
            if (_closing) return;
            if (_canvas != null && !_canvas.gameObject.activeSelf) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Cancel(); return; }
            if (_camHost == null) return;
            HandleLook();
            HandleMove();
        }

        void HandleLook()
        {
            if (Input.GetMouseButtonDown(1) && !PointerOverUi())
            {
                _looking = true;
                _lastMouse = Input.mousePosition;
            }
            if (Input.GetMouseButtonUp(1)) _looking = false;
            if (!_looking) return;

            Vector3 now = Input.mousePosition;
            Vector3 delta = now - _lastMouse;
            _lastMouse = now;

            _yaw += delta.x * LOOK;
            _pitch = Mathf.Clamp(_pitch - delta.y * LOOK, -89f, 89f);
            _camHost.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        void HandleMove()
        {
            var sel = EventSystem.current?.currentSelectedGameObject;
            if (sel != null && sel.GetComponent<InputField>() != null) return;

            var dir = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) dir += Vector3.forward;
            if (Input.GetKey(KeyCode.S)) dir -= Vector3.forward;
            if (Input.GetKey(KeyCode.D)) dir += Vector3.right;
            if (Input.GetKey(KeyCode.A)) dir -= Vector3.right;
            if (Input.GetKey(KeyCode.E)) dir += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) dir -= Vector3.up;
            if (dir == Vector3.zero) return;

            float mul = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? BOOST : 1f;
            var t = _camHost.transform;
            var world = t.rotation * new Vector3(dir.x, 0f, dir.z) + Vector3.up * dir.y;
            t.position += world.normalized * SPEED * mul * Time.unscaledDeltaTime;
        }

        static bool PointerOverUi() =>
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        string[] Labels()
        {
            if (_mode == Mode.Textures) return new[] { "bean_creator.textures" };

            var cats = new string[CATEGORIES.Length];
            for (int i = 0; i < CATEGORIES.Length; i++) cats[i] = CATEGORIES[i].label;
            return cats;
        }

        static int CategoryIndex(string id)
        {
            for (int i = 0; i < CATEGORIES.Length; i++)
                if (CATEGORIES[i].id == id) return i;
            return 0;
        }

        void PickCosmetics() => SetMode(Mode.Cosmetics);
        void PickTextures() => SetMode(Mode.Textures);

        void SetMode(Mode mode)
        {
            _mode = mode;
            for (int i = 0; i < _modeBtns.Length; i++)
                UGUIShip.SetButtonColor(_modeBtns[i], (int)mode == i ? BTN_BLUE : BTN_DARK);

            UGUIShip.RelabelText(_categoryLbl,
                mode == Mode.Textures ? "bean_creator.textures"
                : CATEGORIES[CategoryIndex(_category)].label);

            bool steppable = mode != Mode.Textures;
            if (_arrowPrev != null) _arrowPrev.gameObject.SetActive(steppable);
            if (_arrowNext != null) _arrowNext.gameObject.SetActive(steppable);
            Fetch();
        }

        void StepCategory(int dir)
        {
            if (_mode == Mode.Textures) return;
            int at = (CategoryIndex(_category) + dir + CATEGORIES.Length) % CATEGORIES.Length;
            _category = CATEGORIES[at].id;
            UGUIShip.RelabelText(_categoryLbl, CATEGORIES[at].label);
            Fetch();
        }

        void Fetch()
        {
            if (_mode == Mode.Textures) { FetchTextures(); return; }
            StartCoroutine(FetchCosmetics(_searchField.text?.Trim() ?? "").WrapToIl2Cpp());
        }

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
            _texResults.Clear();
            RebuildRows();
            for (int i = 0; i < 2; i++) yield return null;

            var type = TypeFor(_category);
            if (type == null) yield break;

            Il2CppReferenceArray<UnityEngine.Object> raw = null;
            try { raw = Resources.FindObjectsOfTypeAll(type); }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"cosmetic search died: {ex.Message}");
                SetStatus(LocalizationService.Format("bean_creator.search_failed_fmt", ex.Message));
                yield break;
            }
            if (raw == null || raw.Length == 0)
            {
                Plugin.Log.LogWarning($"no {_category} options loaded yet, the creator list comes up empty");
                SetStatus(LocalizationService.Get("bean_creator.nothing_loaded"));
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

            SetStatus(LocalizationService.Format("bean_creator.matches_fmt", _results.Count));
            RebuildRows();
        }

        void FetchTextures()
        {
            _results.Clear();
            _texResults.Clear();

            var pool = new List<SkinTexEntry>(SkinApplicationService.LoadEntries());
            foreach (var mine in _look.texEntries)
            {
                bool known = false;
                foreach (var e in pool) if (e.entryName == mine.entryName) { known = true; break; }
                if (!known) pool.Add(mine);
            }

            string filter = _searchField.text?.Trim() ?? "";
            foreach (var entry in pool)
            {
                if (filter.Length > 0 && (entry.entryName ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                _texResults.Add(entry);
            }

            SetStatus(_texResults.Count == 0
                ? LocalizationService.Get("bean_creator.no_textures")
                : LocalizationService.Format("bean_creator.matches_fmt", _texResults.Count));
            RebuildRows();
        }

        void RebuildRows()
        {
            if (_listContent == null) return;
            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);
            _rowIdx = 0;

            if (_mode == Mode.Textures)
            {
                for (int i = 0; i < _texResults.Count; i++)
                {
                    var entry = _texResults[i];
                    string sub = null;
                    for (int c = 0; c < CATEGORIES.Length; c++)
                        if (CATEGORIES[c].id == entry.category) sub = CATEGORIES[c].label;
                    var icon = SkinApplicationService.ResolveOptionIconTexture(entry.category, entry.costumeName)
                        ?? SkinApplicationService.GetCachedCustomTex(entry);
                    Row(entry.entryName, sub, _look.HasTexture(entry.entryName), icon, true,
                        new Action(() => ToggleTexture(entry)));
                }
                if (_addTexture != null)
                    Row(LocalizationService.Get("ui.add_texture"), null, false, null, false, new Action(RequestAddTexture));
                return;
            }

            Row(LocalizationService.Get("bean_creator.clear"), null, string.IsNullOrEmpty(Current()), null, false,
                new Action(() => Pick(null)));
            for (int i = 0; i < _results.Count; i++)
            {
                var opt = _results[i];
                Row(SkinApplicationService.GetOptionDisplayName(opt), null, Current() == opt.name,
                    SkinApplicationService.ResolveOptionIconTexture(_category, opt.name), true, new Action(() => Pick(opt)));
            }
        }

        void Row(string label, string sub, bool selected, Texture2D icon, bool thumb, Action onClick)
        {
            float rowH = thumb ? 34f : 24f;
            var btn = UGUIShip.CreateButton(_listContent, new Rect(0f, 0f, 0f, rowH), "",
                Color.clear, WHITE, UIScale.FS_SM, onClick, customSprite: false);
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = rowH;
            le.flexibleWidth = 1f;
            UGUIShip.PaintListRow(btn, _rowIdx++, selected);

            float textX = 8f;
            if (thumb)
            {
                float sz = rowH - 8f;
                var iconGo = new GameObject("Icon");
                iconGo.transform.SetParent(btn.transform, false);
                UGUIShip.SetPixelRect(iconGo.AddComponent<RectTransform>(), new Rect(5f, 4f, sz, sz));
                var raw = iconGo.AddComponent<RawImage>();
                raw.raycastTarget = false;
                if (icon != null) raw.texture = icon;
                else raw.color = THUMB_EMPTY;
                textX = 5f + sz + 8f;
            }

            float textW = _listW - textX - 8f;
            bool twoLine = !string.IsNullOrEmpty(sub);
            var text = UGUIShip.CreateLabel(btn.transform,
                new Rect(textX, twoLine ? 3f : 0f, textW, twoLine ? rowH * 0.5f : rowH),
                label, UIScale.FS_SM, WHITE, TextAnchor.MiddleLeft);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;

            if (twoLine)
            {
                var subLbl = UGUIShip.CreateLabel(btn.transform,
                    new Rect(textX, rowH * 0.5f, textW, rowH * 0.5f - 3f),
                    sub, UIScale.FS_SM - 2, SUB, TextAnchor.MiddleLeft);
                subLbl.raycastTarget = false;
            }
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

        void ToggleTexture(SkinTexEntry entry)
        {
            if (_look.HasTexture(entry.entryName))
            {
                _look.texEntries.RemoveAll(e => e.entryName == entry.entryName);
            }
            else
            {
                _look.texEntries.Add(new SkinTexEntry
                {
                    entryName = entry.entryName,
                    enabled = true,
                    category = entry.category,
                    costumeName = entry.costumeName,
                    matNames = new List<string>(entry.matNames),
                    overrides = new List<SkinTexOverride>(entry.overrides),
                    matProps = new List<MatPropOverride>(entry.matProps),
                });
            }
            RebuildRows();
            RebuildBean();
        }

        void RequestAddTexture()
        {
            _route = _addTexture;
            Finish(_look);
        }

        void RequestUgc() => _pickUgc?.Invoke();
    }
}
