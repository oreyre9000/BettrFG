using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Features.Onboarding;
using BetterFG.Services;
using BetterFG.UI;
using BettrFG.uGUI;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterFG.Customization.UI
{
    public class MenuLayoutEditor : MonoBehaviour
    {
        public MenuLayoutEditor(IntPtr ptr) : base(ptr) { }

        public static MenuLayoutEditor Instance;

        private const float RING = 150f;
        private const float HANDLE = 44f;
        private const string GIZMO_TEX = "BetterFG.assets.ui.general.";

        private static readonly Color OUTLINE = new Color(1f, 0.72f, 0.25f, 0.95f);

        private MenuLayoutService.Kind _kind;
        private RectTransform _stage;
        private Canvas _canvas;
        private RectTransform _canvasRt;
        private Image _blocker;
        private RectTransform _hud, _box;
        private Text _viewLabel;
        private int _view, _child;

        private Dictionary<string, MenuLayoutService.Entry> _entries;
        private Transform _sel;
        private RectTransform _selRt;
        private string _selPath;
        private MenuLayoutService.Slot _selSlot;
        private bool _dragging;
        private Vector2 _grabLocal, _dragStart;
        private bool _dragMoved;
        private const float DRAG_SLOP = 4f;
        private readonly Dictionary<MenuLayoutService.Kind, Dictionary<string, MenuLayoutService.Entry>> _pending =
            new Dictionary<MenuLayoutService.Kind, Dictionary<string, MenuLayoutService.Entry>>();
        private readonly HashSet<string> _manualAnchor = new HashSet<string>();
        private bool _swapping;
        private float _previewScale = 1f;
        private const float PREVIEW_MIN = 0.5f, PREVIEW_MAX = 1.5f;
        private GameObject _anchorPulse;
        private float _anchorPulseUntil;
        private const float ROW_H = 26f;
        private const float ROW_GAP = 4f;
        private const float ROW_NUDGE = 4.5f;
        private const int LOADING_ORDER = 6000;
        private Text _kindLabel;
        private const float PROPS_W = 250f;
        private const float PROPS_LBL_H = 18f;
        private const float PROPS_GAP = 8f;
        private const float PROPS_TAIL = 20f;
        private const float PROPS_PREVIEW_H = 64f;
        private const int PROPS_NONE = 0, PROPS_ELEMENT = 1, PROPS_IMAGE = 2, PROPS_LAYERS = 3;
        private RectTransform _props;
        private int _propsMode;
        private string _propsKey;
        private Text _layerLabel, _layerViewLabel;
        private int _layer;
        private bool _layerOnly;
        private const float VIEW_DIM = 0.35f;
        private readonly Dictionary<CanvasGroup, float> _dimmed = new Dictionary<CanvasGroup, float>();
        private GameObject _menu;
        private Vector2 _menuPoint;
        private const float IMAGE_MAX = 400f;
        private GameObject _confirm;
        private const float ZEBRA_PAD = 12f;
        private static readonly Color TAPE_TINT = new Color(0.8f, 0.72f, 0.52f, 0.88f);
        private static readonly Vector2[] ANCHORS =
        {
            new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0.5f),
            new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
            new Vector2(0.5f, 0.5f),
        };
        private RectTransform _gizmo, _rotHandle, _scaleHandle, _eyeHandle, _cycleHandle, _anchorHandle;
        private RawImage _eyeImg;
        private Texture2D _eyeOn, _eyeOff;
        private int _gizmoMode;
        private bool _gizmoMoved;
        private Vector2 _gizmoCentre, _gizmoStart;
        private float _gizmoStartValue;
        private bool _wasUiVisible;
        private bool _closing;
        private GameObject _returnSel;
        private readonly List<(Dictionary<string, MenuLayoutService.Entry> Entries, HashSet<string> Anchors)> _undo =
            new List<(Dictionary<string, MenuLayoutService.Entry>, HashSet<string>)>();
        private readonly List<(Dictionary<string, MenuLayoutService.Entry> Entries, HashSet<string> Anchors)> _redo =
            new List<(Dictionary<string, MenuLayoutService.Entry>, HashSet<string>)>();
        private (Dictionary<string, MenuLayoutService.Entry> Entries, HashSet<string> Anchors) _before;

        public static void Open(MenuLayoutService.Kind kind)
        {
            if (Instance != null || FullscreenPrompt.Intercept("layout", () => Open(kind))) return;

            var go = new GameObject("BettrFG_LayoutEditor");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<MenuLayoutEditor>();
            Instance._kind = kind;
        }

        public static bool CanOpen(MenuLayoutService.Kind kind) => GameObject.Find("UICanvas_Client_V2(Clone)") != null;

        private void Start() => StartCoroutine(Enter().WrapToIl2Cpp());

        private System.Collections.IEnumerator Enter()
        {
            _returnSel = EventSystem.current.currentSelectedGameObject;
            var man = BetterFGUIMan.Instance;
            _wasUiVisible = man != null && man.IsVisible;
            if (man != null) man.SetVisible(false);
            FGInputLockService.SetScreenLock(true);

            LoadingScreenService.Show();
            LoadingScreenService.Canvas.sortingOrder = LOADING_ORDER;
            yield return new WaitForSecondsRealtime(0.35f);

            if (!Stage())
            {
                Plugin.Log.LogWarning("layout editor couldn't stage a menu to edit, backing out");
                Close(false);
                yield break;
            }

            yield return new WaitForSecondsRealtime(0.15f);
            yield return LoadingScreenService.HideRoutine();
        }

        private bool Stage()
        {
            _stage = MenuLayoutStage.Build(_kind);
            if (_stage == null) return false;
            if (!_pending.TryGetValue(_kind, out _entries)) _pending[_kind] = _entries = MenuLayoutService.Load(_kind);
            MenuLayoutService.EnsureImages(_kind, _stage, _entries);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_stage);
            foreach (var slot in MenuLayoutService.SlotsOf(_kind, _entries.Keys)) MenuLayoutService.RestoreStaged(_kind, slot, _stage);
            ApplyAllToStage();
            MenuLayoutStage.SetPreviewScale(_previewScale);
            if (_canvas == null) Build();
            else RebuildHud();
            return true;
        }

        private void PickKind(MenuLayoutService.Kind kind)
        {
            if (_swapping || kind == _kind) return;
            _undo.Clear();
            _redo.Clear();
            StartCoroutine(SwapKind(kind).WrapToIl2Cpp());
        }

        private System.Collections.IEnumerator SwapKind(MenuLayoutService.Kind next)
        {
            _swapping = true;
            yield return UGUIShip.Squash(new Action<float>(MenuLayoutStage.SetCollapse), false);

            var prev = _kind;
            DropStage();
            _kind = next;
            if (!Stage())
            {
                Plugin.Log.LogWarning($"nothing to stage for {next.Id} right now, staying on {prev.Id}");
                _kind = prev;
                Stage();
            }

            MenuLayoutStage.SetCollapse(0f);
            yield return UGUIShip.Squash(new Action<float>(MenuLayoutStage.SetCollapse), true);
            _swapping = false;
        }

        private void DropStage()
        {
            CloseMenu();
            SetSelection(null);
            _layer = 0;
            _layerOnly = false;
            _dimmed.Clear();
            ShowProps(PROPS_NONE);
            _dragging = false;
            _gizmoMode = 0;
            _view = _child = 0;
            _viewLabel = null;
            if (_anchorPulse != null) Destroy(_anchorPulse);
            _anchorPulse = null;
            MenuLayoutStage.Teardown();
        }

        private void ApplyAllToStage()
        {
            foreach (var slot in MenuLayoutService.SlotsOf(_kind, _entries.Keys))
            {
                if (!_entries.TryGetValue(slot.Path, out var e)) continue;

                var rt = MenuLayoutService.Resolve(_stage, slot);
                if (rt == null) continue;
                if (e.Hosted) MenuLayoutService.MoveToHost(_stage, slot, rt);
                StageApply(slot, rt, e);
            }
            RefreshDim();
        }

        private static void StageApply(MenuLayoutService.Slot slot, RectTransform rt, MenuLayoutService.Entry e)
        {
            var shown = e;
            shown.Hidden = false;
            MenuLayoutService.ApplySlot(slot, rt, shown);
            var cg = rt.GetComponent<CanvasGroup>();
            if (cg == null) cg = rt.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = e.Hidden ? HIDDEN_DIM : 1f;
        }

        private void Build()
        {
            _canvas = UGUIShip.CreateCanvas("BettrFG_LayoutEditorCanvas");
            _canvas.sortingOrder = 5000;
            _canvasRt = _canvas.GetComponent<RectTransform>();

            var blockGo = new GameObject("Blocker");
            blockGo.transform.SetParent(_canvas.transform, false);
            var brt = blockGo.AddComponent<RectTransform>();
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            _blocker = blockGo.AddComponent<Image>();
            _blocker.color = new Color(0f, 0f, 0f, 0.004f);
            _blocker.raycastTarget = true;

            BuildBox();
            BuildGizmo();
            BuildHud();
            RefreshGizmo();
        }

        private void BuildBox()
        {
            var go = new GameObject("SelectionBox");
            go.transform.SetParent(_canvas.transform, false);
            _box = go.AddComponent<RectTransform>();
            _box.anchorMin = _box.anchorMax = new Vector2(0.5f, 0.5f);
            _box.pivot = Vector2.zero;
            _box.sizeDelta = Vector2.zero;

            Edge(new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 2f));
            Edge(new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 2f));
            Edge(new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(2f, 0f));
            Edge(new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(2f, 0f));

            go.SetActive(false);
        }

        private void BuildGizmo()
        {
            var go = new GameObject("Gizmo");
            go.transform.SetParent(_canvas.transform, false);
            _gizmo = go.AddComponent<RectTransform>();
            _gizmo.anchorMin = _gizmo.anchorMax = _gizmo.pivot = new Vector2(0.5f, 0.5f);
            _gizmo.sizeDelta = new Vector2(RING, RING);

            var ring = GizmoImage(_gizmo, "gizmo_ring", RING);
            ring.color = new Color(1f, 1f, 1f, 0.85f);
            _rotHandle = GizmoImage(_gizmo, "icon_gizmo_rotate", HANDLE).rectTransform;
            _rotHandle.anchoredPosition = new Vector2(0f, RING * 0.5f);
            _scaleHandle = GizmoImage(_gizmo, "icon_gizmo_scale", HANDLE).rectTransform;
            _scaleHandle.anchoredPosition = new Vector2(RING * 0.5f, 0f);
            _eyeOn = Utilities.EmbeddedResourceandUnity.LoadTexture(GIZMO_TEX + "icon_gizmo_eye.png");
            _eyeOff = Utilities.EmbeddedResourceandUnity.LoadTexture(GIZMO_TEX + "icon_gizmo_eye_off.png");
            _eyeImg = GizmoImage(_gizmo, "icon_gizmo_eye", HANDLE);
            _eyeHandle = _eyeImg.rectTransform;
            _eyeHandle.anchoredPosition = new Vector2(-RING * 0.5f, 0f);
            _cycleHandle = GizmoImage(_gizmo, "icon_gizmo_cycle", HANDLE).rectTransform;
            _cycleHandle.anchoredPosition = new Vector2(0f, -RING * 0.5f);
            _anchorHandle = GizmoImage(_gizmo, "icon_gizmo_anchor", HANDLE).rectTransform;
            _anchorHandle.anchoredPosition = new Vector2(RING * 0.3536f, RING * 0.3536f);

            go.SetActive(false);
        }

        private static RawImage GizmoImage(RectTransform parent, string tex, float size)
        {
            var go = new GameObject(tex);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<RawImage>();
            img.texture = Utilities.EmbeddedResourceandUnity.LoadTexture(GIZMO_TEX + tex + ".png");
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            return img;
        }

        private void Edge(Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 size)
        {
            var go = new GameObject("Edge");
            go.transform.SetParent(_box, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.pivot = pivot;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = OUTLINE;
            img.raycastTarget = false;
        }

        private void BuildHud(bool animate = true)
        {
            var specs = new[]
            {
                new OnboardingPopup.ButtonSpec("layout.editor.reset_all", new Action(AskReset), UGUIShip.BTN_REMOVE),
                new OnboardingPopup.ButtonSpec("layout.editor.cancel", new Action(Cancel), OnboardingPopup.BtnGhost),
                new OnboardingPopup.ButtonSpec("layout.editor.save", new Action(Save), OnboardingPopup.BtnPrimary),
            };

            float viewRow = ROW_H + ROW_GAP;
            float rows = ROW_H + ROW_GAP + ROW_H + (_kind.Views.Length > 0 ? viewRow : 0f);
            var root = OnboardingPopup.Show(_canvas, "layout.editor.title", null, Vector2.zero, new Vector2(0f, 1f), specs,
                customH: rows, onCustom: new Action<RectTransform>(BuildRows));
            _hud = root.GetComponent<RectTransform>();
            _hud.anchorMin = _hud.anchorMax = Vector2.zero;
            _hud.anchoredPosition = new Vector2(40f, 40f + _hud.sizeDelta.y + (_kind.Views.Length > 0 ? 0f : viewRow));
            DressHud(_hud);
            if (animate) StartCoroutine(PromptHost.OpenPopup(root).WrapToIl2Cpp());
            else _hud.localScale = Vector3.one;
        }

        private void RebuildHud()
        {
            bool shown = _hud.gameObject.activeSelf;
            Destroy(_hud.gameObject);
            BuildHud(false);
            _hud.gameObject.SetActive(shown);
        }

        private static void DressHud(RectTransform root)
        {
            var title = root.Find("Label").GetComponent<RectTransform>();
            title.anchoredPosition = new Vector2(16f + ROW_NUDGE - ZEBRA_PAD, title.anchoredPosition.y);

            var panel = root.Find("Panel");
            var panelImg = panel.GetComponent<Image>();
            UGUIShip.ApplyDeluxPanelHighShadow(panelImg);
            var under = Instantiate(panel.gameObject, root);
            under.name = "Under";
            under.transform.SetSiblingIndex(panel.GetSiblingIndex());
            under.GetComponent<Image>().color = Color.black;

            var size = panel.GetComponent<RectTransform>().rect.size;
            var scale = panel.localScale;
            var grid = Layer(panel, "Grid").gameObject.AddComponent<RawImage>();
            grid.rectTransform.offsetMin = new Vector2(8f, 8f);
            grid.rectTransform.offsetMax = new Vector2(-8f, -8f);
            grid.texture = RepeatTex("pattern_blueprint_grid.png");
            grid.color = new Color(1f, 1f, 1f, 0.22f);
            grid.uvRect = new Rect(0f, 0f, (size.x - 16f) * scale.x / 32f, (size.y - 16f) * scale.y / 32f);
            grid.raycastTarget = false;

            var tape = Layer(panel, "Tape");
            tape.anchorMin = Vector2.zero;
            tape.anchorMax = new Vector2(1f, 0f);
            tape.pivot = new Vector2(0.5f, 0f);
            tape.offsetMin = new Vector2(-8f, -3f);
            tape.offsetMax = new Vector2(8f, 5f);
            tape.localRotation = Quaternion.Euler(0f, 0f, -0.8f);
            var tapeMask = tape.gameObject.AddComponent<Image>();
            UGUIShip.ApplyDeluxColorFill(tapeMask, 4f);
            tapeMask.raycastTarget = false;
            tape.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var stripes = Layer(tape, "Stripes").gameObject.AddComponent<RawImage>();
            stripes.texture = RepeatTex("pattern_hazard.png");
            stripes.color = TAPE_TINT;
            stripes.uvRect = new Rect(0f, 0f, (size.x + 16f) / 18f, 8f / 18f);
            stripes.raycastTarget = false;

            var shine = Layer(tape, "Shine").gameObject.AddComponent<Image>();
            UGUIShip.ApplyDeluxShine(shine, 4f);
            shine.color = new Color(1f, 1f, 1f, 0.35f);
            shine.raycastTarget = false;
        }

        private static RectTransform Layer(Transform parent, string name)
        {
            var rt = new GameObject(name).AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static Texture2D RepeatTex(string file)
        {
            var tex = Utilities.EmbeddedResourceandUnity.LoadTexture(GIZMO_TEX + file);
            tex.wrapMode = TextureWrapMode.Repeat;
            return tex;
        }

        private void BuildRows(RectTransform box)
        {
            float w = box.sizeDelta.x;
            float cw = 150f + ROW_H * 2f;
            var dim = new Color(1f, 1f, 1f, 0.78f);
            box.anchoredPosition += new Vector2(ROW_NUDGE, 0f);

            var kinds = MenuLayoutService.Kinds;
            var labels = new string[kinds.Length];
            for (int i = 0; i < kinds.Length; i++) labels[i] = kinds[i].LabelId;
            Zebra(box, 0f, w);
            UGUIShip.CreateLabel(box, new Rect(0f, 0f, w - cw, ROW_H), "layout.editor.kind_label", 13, dim);
            _kindLabel = UGUIShip.CreateCarousel(box, new Rect(w - cw, 0f, cw, ROW_H), labels, Array.IndexOf(kinds, _kind),
                new Action<int>(StepKind), OnboardingPopup.BtnGhost, 12);

            float y = ROW_H + ROW_GAP;
            if (_kind.Views.Length > 0)
            {
                UGUIShip.CreateLabel(box, new Rect(0f, y, w - cw, ROW_H), "layout.editor.view_label", 13, dim);
                _viewLabel = UGUIShip.CreateCarousel(box, new Rect(w - cw, y, cw, ROW_H), _kind.Views, 0,
                    new Action<int>(StepView), OnboardingPopup.BtnGhost, 12);
                y += ROW_H + ROW_GAP;
            }

            UGUIShip.CreateLabel(box, new Rect(0f, y, w - cw, ROW_H), "layout.editor.preview_scale", 13, dim);
            UGUIShip.CreateSlider(box, w - cw, y, cw, "", Mathf.InverseLerp(PREVIEW_MIN, PREVIEW_MAX, _previewScale),
                ROW_H, 0f, 12, new Action<float>(SetPreviewScale), reserveLabel: false);
        }

        private void StepKind(int d)
        {
            if (_swapping) return;
            var kinds = MenuLayoutService.Kinds;
            var next = kinds[(Array.IndexOf(kinds, _kind) + d + kinds.Length) % kinds.Length];
            UGUIShip.RelabelText(_kindLabel, next.LabelId);
            PickKind(next);
        }

        private void ShowProps(int mode, bool force = false)
        {
            if (_canvas == null) return;
            string key = mode == PROPS_ELEMENT || mode == PROPS_IMAGE ? _selPath : null;
            if (!force && mode == _propsMode && key == _propsKey) return;

            if (_props != null) StartCoroutine(PromptHost.CloseThenDestroy(_props.gameObject).WrapToIl2Cpp());
            _props = null;
            _propsMode = mode;
            _propsKey = key;
            if (mode == PROPS_NONE) return;

            float rows = mode == PROPS_IMAGE ? PROPS_LBL_H + PROPS_PREVIEW_H
                : mode == PROPS_ELEMENT ? PROPS_LBL_H + ROW_H
                : (PROPS_LBL_H + ROW_H) * 2f + PROPS_GAP;
            var root = OnboardingPopup.Show(_canvas, "layout.editor.properties", null, Vector2.zero, new Vector2(0f, 1f),
                new OnboardingPopup.ButtonSpec[0], customH: rows + PROPS_TAIL, onCustom: new Action<RectTransform>(BuildPropRows), width: PROPS_W);
            _props = root.GetComponent<RectTransform>();
            _props.anchorMin = _props.anchorMax = new Vector2(0f, 1f);
            _props.anchoredPosition = new Vector2(28f, -40f);
            DressHud(_props);
            _props.gameObject.SetActive(_hud.gameObject.activeSelf);
            StartCoroutine(PromptHost.OpenPopup(root).WrapToIl2Cpp());
        }

        private void BuildPropRows(RectTransform box)
        {
            float w = box.sizeDelta.x;
            var dim = new Color(1f, 1f, 1f, 0.6f);
            box.anchoredPosition += new Vector2(ROW_NUDGE, 0f);

            if (_propsMode == PROPS_ELEMENT)
            {
                UGUIShip.CreateLabel(box, new Rect(0f, 0f, w, PROPS_LBL_H), "layout.editor.element", 12, dim);
                var name = _selSlot.Name;
                foreach (var junk in new[] { "Prefab_UI_", "Generic_UI_", "PB_UI_", "Container_" }) name = name.Replace(junk, "");
                var nameLbl = UGUIShip.CreateLabel(box, new Rect(0f, PROPS_LBL_H, w, ROW_H), name.Replace('_', ' ').Trim(), 14, UGUIShip.WHITE);
                nameLbl.horizontalOverflow = HorizontalWrapMode.Wrap;
                nameLbl.resizeTextForBestFit = true;
                nameLbl.resizeTextMinSize = 8;
                nameLbl.resizeTextMaxSize = 14;
                return;
            }

            if (_propsMode == PROPS_IMAGE)
            {
                float pw = PROPS_PREVIEW_H * 16f / 9f;
                UGUIShip.CreateLabel(box, new Rect(0f, 0f, w, PROPS_LBL_H), "layout.editor.image", 12, dim);
                UGUIShip.CreateButton(box, new Rect(0f, PROPS_LBL_H + (PROPS_PREVIEW_H - ROW_H) * 0.5f, w - pw - 8f, ROW_H),
                    "layout.editor.choose_image", OnboardingPopup.BtnGhost, UGUIShip.WHITE, 12, new Action(ChooseImage));

                var (frame, inner) = UGUIShip.CreateFramedImage(box);
                UGUIShip.SetPixelRect(frame, new Rect(w - pw, PROPS_LBL_H, pw, PROPS_PREVIEW_H));
                var tex = MenuLayoutService.ImageTexture(EntryFor(_selPath).Image);
                var pic = new GameObject("Picture").AddComponent<RectTransform>();
                pic.SetParent(inner, false);
                var raw = pic.gameObject.AddComponent<RawImage>();
                raw.texture = tex;
                raw.raycastTarget = false;
                var fit = pic.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fit.aspectRatio = tex == null ? 1f : (float)tex.width / tex.height;
                return;
            }

            UGUIShip.CreateLabel(box, new Rect(0f, 0f, w, PROPS_LBL_H), "layout.editor.click_layer", 12, dim);
            _layerLabel = UGUIShip.CreateCarousel(box, new Rect(0f, PROPS_LBL_H, w, ROW_H), LayerLabels(), _layer,
                new Action<int>(StepLayer), OnboardingPopup.BtnGhost, 12);
            float y = PROPS_LBL_H + ROW_H + PROPS_GAP;
            UGUIShip.CreateLabel(box, new Rect(0f, y, w, PROPS_LBL_H), "layout.editor.view", 12, dim);
            _layerViewLabel = UGUIShip.CreateCarousel(box, new Rect(0f, y + PROPS_LBL_H, w, ROW_H),
                new[] { "layout.editor.view_all", "layout.editor.view_layer" }, _layerOnly ? 1 : 0,
                new Action<int>(StepLayerView), OnboardingPopup.BtnGhost, 12);
        }

        private int LayerCount()
        {
            int top = 0;
            foreach (var kv in _entries)
                if (kv.Key.StartsWith(MenuLayoutService.IMAGE, StringComparison.Ordinal)) top = Mathf.Max(top, kv.Value.Layer);
            return top + 2;
        }

        private string[] LayerLabels()
        {
            var labels = new string[LayerCount()];
            for (int i = 0; i < labels.Length; i++) labels[i] = LocalizationService.Format("layout.editor.layer", i);
            return labels;
        }

        private void StepLayer(int d)
        {
            int n = LayerCount();
            _layer = (_layer + d + n) % n;
            UGUIShip.RelabelText(_layerLabel, LocalizationService.Format("layout.editor.layer", _layer));
            SetSelection(null);
            RefreshDim();
        }

        private void StepLayerView(int d)
        {
            _layerOnly = !_layerOnly;
            UGUIShip.RelabelText(_layerViewLabel, _layerOnly ? "layout.editor.view_layer" : "layout.editor.view_all");
            RefreshDim();
        }

        private int LayerOf(MenuLayoutService.Slot slot) =>
            slot.Image && _entries.TryGetValue(slot.Path, out var e) ? e.Layer : 0;

        private void RefreshDim()
        {
            foreach (var kv in _dimmed)
                if (kv.Key != null) kv.Key.alpha = kv.Value;
            _dimmed.Clear();
            if (!_layerOnly) return;

            var box = MenuLayoutService.ImageBox(_kind, _stage);
            if (_layer != 0)
            {
                if (box == null) DimChildren(_stage, null);
                else
                    for (Transform t = box; t != _stage; t = t.parent) DimChildren(t.parent, t);
            }
            if (box == null) return;
            for (int i = 0; i < box.childCount; i++)
            {
                var c = box.GetChild(i);
                if (_entries.TryGetValue(MenuLayoutService.IMAGE + c.name, out var e) && e.Layer != _layer) Dim(c);
            }
        }

        private void DimChildren(Transform parent, Transform except)
        {
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i) != except) Dim(parent.GetChild(i));
        }

        private void Dim(Transform t)
        {
            var cg = t.GetComponent<CanvasGroup>();
            if (cg == null) cg = t.gameObject.AddComponent<CanvasGroup>();
            _dimmed[cg] = cg.alpha;
            cg.alpha *= VIEW_DIM;
        }

        private void OpenMenu()
        {
            var hit = PickSlot();
            var items = new List<(string LabelId, Action Act)>();
            if (hit == null) items.Add(("layout.editor.add_image", new Action(AddImageHere)));
            else if (hit.Image)
            {
                SetSelection(hit);
                items.Add(("layout.editor.delete_image", new Action(DeleteSelectedImage)));
            }
            else return;
            _menuPoint = Input.mousePosition;

            _menu = new GameObject("ContextMenu");
            _menu.transform.SetParent(_canvas.transform, false);
            var rootRt = _menu.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;

            var catchGo = new GameObject("Catch");
            catchGo.transform.SetParent(_menu.transform, false);
            var catchRt = catchGo.AddComponent<RectTransform>();
            catchRt.anchorMin = Vector2.zero;
            catchRt.anchorMax = Vector2.one;
            catchRt.offsetMin = catchRt.offsetMax = Vector2.zero;
            catchGo.AddComponent<Image>().color = Color.clear;
            var catchBtn = catchGo.AddComponent<Button>();
            catchBtn.transition = Selectable.Transition.None;
            catchBtn.onClick.AddListener(new Action(CloseMenu));

            float rowH = 20f, w = 90f;
            foreach (var it in items) w = Mathf.Max(w, UGUIShip.MeasureLabel(it.LabelId, UIScale.FS_SM) + 26f);
            w = Mathf.Min(w, UGUIShip.ACTION_MENU_MAX_W);

            var menuGo = new GameObject("Menu");
            menuGo.transform.SetParent(_menu.transform, false);
            var rt = menuGo.AddComponent<RectTransform>();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRt, _menuPoint, null, out var local);
            var r = rootRt.rect;
            UGUIShip.SetPixelRect(rt, new Rect(local.x - r.xMin, -(local.y - r.yMax), w, rowH * items.Count + 4f));
            UGUIShip.ApplyDeluxPanelDark(menuGo.AddComponent<Image>(), 2f);

            for (int i = 0; i < items.Count; i++)
            {
                var itemGo = new GameObject("Item" + i);
                itemGo.transform.SetParent(rt, false);
                UGUIShip.SetPixelRect(itemGo.AddComponent<RectTransform>(), new Rect(2f, 2f + i * rowH, w - 4f, rowH));
                var img = itemGo.AddComponent<Image>();
                img.color = UGUIShip.ROW_CLEAR;
                var b = itemGo.AddComponent<Button>();
                b.targetGraphic = img;
                UGUIShip.PaintListRow(b, i, false);
                var lbl = UGUIShip.CreateLabel(itemGo.transform, new Rect(8f, 0f, w - 16f, rowH), items[i].LabelId,
                    UIScale.FS_SM, UGUIShip.WHITE, TextAnchor.MiddleLeft);
                lbl.raycastTarget = false;
                lbl.horizontalOverflow = HorizontalWrapMode.Overflow;
                b.onClick.AddListener(items[i].Act);
            }
        }

        private void CloseMenu()
        {
            if (_menu != null) Destroy(_menu);
            _menu = null;
        }

        private static string ImageFilter() => LocalizationService.Get("layout.editor.image_filter") + "\0*.png;*.jpg;*.jpeg\0";

        private void AddImageHere()
        {
            CloseMenu();
            var at = _menuPoint;
            WinDialogs.PickFile(LocalizationService.Get("layout.editor.pick_image"), path =>
            {
                if (!string.IsNullOrEmpty(path) && _stage != null) PlaceImage(path, at);
            }, ImageFilter());
        }

        private void PlaceImage(string path, Vector2 screen)
        {
            string file = MenuLayoutService.ImportImage(path);
            var tex = MenuLayoutService.ImageTexture(file);
            if (tex == null) return;

            var host = MenuLayoutService.ImageHostOf(_kind, _stage);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(host, screen, null, out var local);
            float k = Mathf.Min(1f, IMAGE_MAX / Mathf.Max(tex.width, tex.height));
            var half = new Vector2(0.5f, 0.5f);
            var e = new MenuLayoutService.Entry
            {
                AMin = half, AMax = half, Pivot = half,
                Pos = local - host.rect.center,
                Size = new Vector2(tex.width * k, tex.height * k),
                Scale = 1f,
                Layer = _layer,
                Image = file,
            };

            var before = Snapshot();
            string key = MenuLayoutService.IMAGE + Guid.NewGuid().ToString("N").Substring(0, 8);
            _entries[key] = e;
            Record(before);
            MenuLayoutService.EnsureImages(_kind, _stage, _entries);
            var slot = MenuLayoutService.SlotAt(_kind, key);
            StageApply(slot, MenuLayoutService.Resolve(_stage, slot), e);
            RefreshDim();
            SetSelection(slot);
        }

        private void DeleteSelectedImage()
        {
            CloseMenu();
            if (_selSlot == null || !_selSlot.Image) return;
            var before = Snapshot();
            _entries.Remove(_selPath);
            _manualAnchor.Remove(_selPath);
            Record(before);
            SetSelection(null);
            MenuLayoutService.EnsureImages(_kind, _stage, _entries);
            RefreshDim();
        }

        private void ChooseImage()
        {
            string key = _selPath;
            WinDialogs.PickFile(LocalizationService.Get("layout.editor.pick_image"), path =>
            {
                if (string.IsNullOrEmpty(path) || _stage == null || key != _selPath || !_entries.TryGetValue(key, out var e)) return;
                string file = MenuLayoutService.ImportImage(path);
                var tex = MenuLayoutService.ImageTexture(file);
                if (tex == null) return;

                var before = Snapshot();
                e.Image = file;
                e.Size = new Vector2(e.Size.x, e.Size.x * tex.height / tex.width);
                _entries[key] = e;
                Record(before);
                MenuLayoutService.EnsureImages(_kind, _stage, _entries);
                StageApply(_selSlot, _selRt, e);
                ShowProps(PROPS_IMAGE, true);
            }, ImageFilter());
        }

        private static void Zebra(RectTransform box, float y, float w)
        {
            var rt = new GameObject("Zebra").AddComponent<RectTransform>();
            rt.SetParent(box, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(-ZEBRA_PAD, 3f - y);
            rt.sizeDelta = new Vector2(w + ZEBRA_PAD * 2f, ROW_H + 6f);
            var img = rt.gameObject.AddComponent<Image>();
            UGUIShip.ApplyDeluxColorFill(img);
            img.color = Color.black;
            img.raycastTarget = false;
        }

        private void SetPreviewScale(float t)
        {
            _previewScale = Mathf.Lerp(PREVIEW_MIN, PREVIEW_MAX, t);
            MenuLayoutStage.SetPreviewScale(_previewScale);
        }

        private void AskReset()
        {
            if (_confirm != null || _swapping) return;
            _confirm = new GameObject("ResetConfirm");
            var rt = _confirm.AddComponent<RectTransform>();
            rt.SetParent(_canvas.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _confirm.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.99f);

            var specs = new[]
            {
                new OnboardingPopup.ButtonSpec("layout.editor.cancel", new Action(() => CloseConfirm(false)), OnboardingPopup.BtnGhost),
                new OnboardingPopup.ButtonSpec("layout.editor.reset_all", new Action(() => CloseConfirm(true)), UGUIShip.BTN_REMOVE),
            };
            var popup = OnboardingPopup.Show(_canvas, "layout.editor.reset_confirm_title", null, Vector2.zero,
                new Vector2(0.5f, 0.5f), specs);
            popup.transform.SetParent(rt, false);
            StartCoroutine(PromptHost.OpenPopup(popup).WrapToIl2Cpp());
        }

        private void CloseConfirm(bool reset)
        {
            if (_confirm == null) return;
            StartCoroutine(CloseConfirmRoutine(_confirm).WrapToIl2Cpp());
            _confirm = null;
            if (!reset) return;
            var before = Snapshot();
            _entries.Clear();
            _manualAnchor.Clear();
            Record(before);
            StartCoroutine(SwapKind(_kind).WrapToIl2Cpp());
        }

        private (Dictionary<string, MenuLayoutService.Entry>, HashSet<string>) Snapshot() =>
            (new Dictionary<string, MenuLayoutService.Entry>(_entries, _entries.Comparer), new HashSet<string>(_manualAnchor));

        private void Record((Dictionary<string, MenuLayoutService.Entry> Entries, HashSet<string> Anchors) before)
        {
            bool same = before.Entries.Count == _entries.Count && before.Anchors.SetEquals(_manualAnchor);
            if (same)
                foreach (var kv in before.Entries)
                    if (!_entries.TryGetValue(kv.Key, out var e) || !e.Equals(kv.Value)) { same = false; break; }
            if (same) return;
            _undo.Add(before);
            _redo.Clear();
        }

        private void StepHistory(List<(Dictionary<string, MenuLayoutService.Entry> Entries, HashSet<string> Anchors)> from,
            List<(Dictionary<string, MenuLayoutService.Entry> Entries, HashSet<string> Anchors)> to)
        {
            if (from.Count == 0 || _dragging || _gizmoMode != 0 || _swapping) return;
            var snap = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            to.Add(Snapshot());
            var changed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in _entries)
                if (!snap.Entries.TryGetValue(kv.Key, out var was) || !was.Equals(kv.Value)) changed.Add(kv.Key);
            foreach (var key in snap.Entries.Keys)
                if (!_entries.ContainsKey(key)) changed.Add(key);
            var touched = MenuLayoutService.SlotsOf(_kind, changed).Where(sl => changed.Contains(sl.Path)).ToList();

            _entries.Clear();
            foreach (var kv in snap.Entries) _entries[kv.Key] = kv.Value;
            _manualAnchor.Clear();
            _manualAnchor.UnionWith(snap.Anchors);
            MenuLayoutService.EnsureImages(_kind, _stage, _entries);

            foreach (var slot in touched)
            {
                MenuLayoutService.RestoreStaged(_kind, slot, _stage);
                var rt = MenuLayoutService.Resolve(_stage, slot);
                var cg = rt == null ? null : rt.GetComponent<CanvasGroup>();
                if (cg != null) cg.alpha = 1f;
            }
            ApplyAllToStage();
            SetSelection(_selSlot == null ? null : MenuLayoutService.SlotAt(_kind, _selSlot.Path));
        }

        private System.Collections.IEnumerator CloseConfirmRoutine(GameObject confirm)
        {
            yield return PromptHost.CloseThenDestroy(confirm.transform.Find("Onboarding_Popup").gameObject);
            Destroy(confirm);
        }

        private void Save() => Close(true);

        private void Cancel() => Close(false);

        private void Update()
        {
            if (_closing || _canvas == null) return;
            if (_stage == null) { Close(false); return; }
            WinDialogs.Tick();
            if (_confirm != null || _menu != null) return;

            if (Input.GetKeyDown(KeyCode.Escape)) { Close(false); return; }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { Close(true); return; }
            if (Input.GetKeyDown(KeyCode.Z) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) StepHistory(_redo, _undo);
                else StepHistory(_undo, _redo);
            }
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                _hud.gameObject.SetActive(!_hud.gameObject.activeSelf);
                if (_props != null) _props.gameObject.SetActive(_hud.gameObject.activeSelf);
            }
            if (Input.GetMouseButtonDown(1) && !OverUi()) { OpenMenu(); return; }
            if (Input.GetKeyDown(KeyCode.H)) ToggleHidden();
            if (Input.GetKeyDown(KeyCode.R)) ResetSelection();
            if (Input.GetKeyDown(KeyCode.V) && _viewLabel != null) StepView(1);
            if (Input.GetKeyDown(KeyCode.C)) CycleChild();

            KeepSlotsVisible();
            HandleWheel();
            if (!HandleGizmo()) HandlePointer();
            UpdateBox();
            PlaceAnchorPulse();
        }

        private bool InView(MenuLayoutService.Slot slot) => _kind.Views.Length == 0 || slot.Image || slot.View == _view;

        private void KeepSlotsVisible()
        {
            var keep = new HashSet<int>();
            for (int i = 0; i < _kind.Slots.Length; i++)
            {
                var slot = _kind.Slots[i];
                if (!InView(slot)) continue;

                var rt = MenuLayoutService.Resolve(_stage, slot);
                if (rt == null) continue;
                Transform t = rt.transform;
                while (t != null && t != _stage)
                {
                    keep.Add(t.GetInstanceID());
                    if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                    t = t.parent;
                }

                if (!slot.OneChild || rt.childCount == 0) continue;
                int pick = _child % rt.childCount;
                for (int c = 0; c < rt.childCount; c++)
                {
                    var kid = rt.GetChild(c).gameObject;
                    if (kid.activeSelf != (c == pick)) kid.SetActive(c == pick);
                }
            }

            for (int i = 0; i < _kind.Slots.Length; i++)
            {
                var slot = _kind.Slots[i];
                if (InView(slot)) continue;

                var rt = MenuLayoutService.Resolve(_stage, slot);
                Transform t = rt == null ? null : rt.transform, top = null;
                while (t != null && t != _stage && !keep.Contains(t.GetInstanceID()))
                {
                    top = t;
                    t = t.parent;
                }
                if (top != null && top.gameObject.activeSelf) top.gameObject.SetActive(false);
            }
        }

        private void StepView(int d)
        {
            _view = (_view + d + _kind.Views.Length) % _kind.Views.Length;
            UGUIShip.RelabelText(_viewLabel, _kind.Views[_view]);
            if (_selSlot != null && !InView(_selSlot)) SetSelection(null);
        }

        private void CycleChild()
        {
            if (_selSlot == null || !_selSlot.OneChild || _selRt == null || _selRt.childCount == 0) return;
            _child = (_child + 1) % _selRt.childCount;
        }

        private bool OverUi() => Over(_hud) || Over(_props);

        private static bool Over(RectTransform rt) =>
            rt != null && rt.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, null);

        private void HandleWheel()
        {
            if (OverUi()) return;

            float wheel = Input.mouseScrollDelta.y;
            if (wheel > 0.01f) CycleSlot(1);
            else if (wheel < -0.01f) CycleSlot(-1);
        }

        private bool HandleGizmo()
        {
            if (_gizmoMode != 0)
            {
                if (Input.GetMouseButton(0)) { GizmoStep(); return true; }
                if (!_gizmoMoved)
                {
                    var r = EntryFor(_selPath);
                    if (_gizmoMode == 1) r.Rot = 0f; else r.Scale = 1f;
                    Commit(r);
                }
                _gizmoMode = 0;
                Record(_before);
                return true;
            }

            if (_selRt == null || !_gizmo.gameObject.activeSelf || !Input.GetMouseButtonDown(0)) return false;
            if (Covers(_eyeHandle)) { ToggleHidden(); return true; }
            if (_cycleHandle.gameObject.activeSelf && Covers(_cycleHandle)) { CycleChild(); return true; }
            if (_anchorHandle.gameObject.activeSelf && Covers(_anchorHandle)) { CycleAnchor(); return true; }
            int mode = Covers(_rotHandle) ? 1 : Covers(_scaleHandle) ? 2 : 0;
            if (mode == 0) return false;

            _before = Snapshot();
            _gizmoMode = mode;
            _gizmoMoved = false;
            _gizmoCentre = RectTransformUtility.WorldToScreenPoint(null, _gizmo.position);
            _gizmoStart = Input.mousePosition;
            var e = EntryFor(_selPath);
            _gizmoStartValue = mode == 1 ? e.Rot : e.Scale;
            return true;
        }

        private void GizmoStep()
        {
            Vector2 m = Input.mousePosition;
            if (!_gizmoMoved && (m - _gizmoStart).sqrMagnitude < 9f) return;
            _gizmoMoved = true;

            var from = _gizmoStart - _gizmoCentre;
            var to = m - _gizmoCentre;
            var e = EntryFor(_selPath);
            if (_gizmoMode == 1) e.Rot = Mathf.Repeat(_gizmoStartValue + Vector2.SignedAngle(from, to) + 180f, 360f) - 180f;
            else e.Scale = Mathf.Clamp(_gizmoStartValue * to.magnitude / Mathf.Max(1f, from.magnitude), 0.1f, 4f);
            Commit(e);
        }

        private void HandlePointer()
        {
            if (Input.GetMouseButtonDown(0) && !OverUi())
            {
                var hit = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) ? PickAny() : PickSlot();
                bool layersUp = _propsMode == PROPS_LAYERS;
                SetSelection(hit);
                if (hit == null) ShowProps(layersUp ? PROPS_NONE : PROPS_LAYERS);
                _before = Snapshot();
                if (hit != null) BeginDrag();
            }

            if (Input.GetMouseButtonUp(0) && _dragging)
            {
                _dragging = false;
                if (_dragMoved) SnapAnchor();
                Record(_before);
            }
            if (_dragging && Input.GetMouseButton(0)) DragStep();
        }

        private MenuLayoutService.Slot PickSlot()
        {
            MenuLayoutService.Slot best = null;
            Transform bestT = null;
            float bestArea = float.MaxValue;

            foreach (var slot in MenuLayoutService.SlotsOf(_kind, _entries.Keys))
            {
                if (!InView(slot) || slot.Path.StartsWith(MenuLayoutService.PICKED, StringComparison.Ordinal) || LayerOf(slot) != _layer) continue;
                var rt = MenuLayoutService.Resolve(_stage, slot);
                if (rt == null) continue;

                float area = HitArea(rt.transform, slot);
                if (area < 0f) continue;
                bool tie = Mathf.Abs(area - bestArea) < 1f;
                if (area > bestArea && !tie) continue;
                if (tie && bestT != null && !rt.IsChildOf(bestT)) continue;
                best = slot;
                bestT = rt;
                bestArea = area;
            }
            return best;
        }

        private MenuLayoutService.Slot PickAny()
        {
            if (_layer != 0) return null;
            Graphic best = null;
            float bestArea = float.MaxValue;
            var mouse = (Vector2)Input.mousePosition;
            var images = MenuLayoutService.ImageBox(_kind, _stage);
            foreach (var g in _stage.GetComponentsInChildren<Graphic>(false))
            {
                if (images != null && g.transform.IsChildOf(images)) continue;
                if (!Seen(g, _stage, true, mouse, out var box, out bool inside, true) || !inside) continue;
                float area = box.width * box.height;
                if (area >= bestArea) continue;
                best = g;
                bestArea = area;
            }
            if (best == null) return null;

            MenuLayoutService.Slot owner = null;
            Transform ownerT = null;
            for (int i = 0; i < _kind.Slots.Length; i++)
            {
                if (!InView(_kind.Slots[i])) continue;
                var rt = MenuLayoutService.Resolve(_stage, _kind.Slots[i]);
                if (rt == null || !best.transform.IsChildOf(rt) || (ownerT != null && !rt.IsChildOf(ownerT))) continue;
                owner = _kind.Slots[i];
                ownerT = rt;
            }
            if (best.transform == ownerT || (owner != null && owner.Whole)) return owner;
            var path = owner == null
                ? MenuLayoutService.PathOf(_stage, best.transform)
                : owner.Path + "/" + MenuLayoutService.PathOf(ownerT, best.transform);
            var slot = MenuLayoutService.SlotAt(_kind, path);
            MenuLayoutService.RestoreStaged(_kind, slot, _stage);
            return slot;
        }

        private float HitArea(Transform t, MenuLayoutService.Slot slot)
        {
            if (slot.HitPaths.Length == 0) return AreaUnderMouse(t.GetComponent<RectTransform>());

            for (int i = 0; i < slot.HitPaths.Length; i++)
            {
                var child = t.Find(slot.HitPaths[i]);
                if (child == null) continue;
                float area = AreaUnderMouse(child.GetComponent<RectTransform>());
                if (area >= 0f) return area;
            }
            return -1f;
        }

        private const float HIT_PAD = 6f;
        private const float HIDDEN_DIM = 0.3f;

        private static float AreaUnderMouse(RectTransform rt)
        {
            if (!Measure(rt, true, out var r, out bool hit, out bool hollow) || !hit) return -1f;
            return (hollow ? HOLLOW_RANK : 0f) + r.width * r.height;
        }

        private const float HOLLOW_RANK = 1e9f;

        private static bool ScreenBounds(RectTransform rt, out Rect r) => Measure(rt, false, out r, out _, out _);

        private static bool Measure(RectTransform rt, bool testMouse, out Rect r, out bool hit, out bool hollow)
        {
            hollow = false;
            var mouse = (Vector2)Input.mousePosition;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            bool any = false;
            hit = false;

            foreach (var g in rt.GetComponentsInChildren<Graphic>(false))
            {
                if (!Seen(g, rt, testMouse, mouse, out var box, out bool inside)) continue;

                min = Vector2.Min(min, box.min);
                max = Vector2.Max(max, box.max);
                any = true;
                hit |= inside;
            }

            if (!any && rt.rect.width * rt.rect.height > 1f)
            {
                hollow = true;
                var box = WorldBox(rt, rt.rect.min, rt.rect.max);
                min = box.min;
                max = box.max;
                any = true;
                hit = testMouse && LocalHit(rt, rt.rect.min, rt.rect.max, mouse, HIT_PAD);
            }

            r = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return any;
        }

        private static bool Seen(Graphic g, Transform top, bool testMouse, Vector2 mouse, out Rect box, out bool inside, bool allowDim = false)
        {
            box = default;
            inside = false;
            if (!Shown(g, top, allowDim) || !Quad(g, out var lo, out var hi)) return false;

            var grt = g.rectTransform;
            box = WorldBox(grt, lo, hi);
            inside = testMouse && LocalHit(grt, lo, hi, mouse, HIT_PAD);
            for (var p = grt.parent; p != null; p = p.parent)
            {
                if (p.GetComponent<Mask>() == null && p.GetComponent<RectMask2D>() == null) continue;
                var prt = p.GetComponent<RectTransform>();
                var clip = WorldBox(prt, prt.rect.min, prt.rect.max);
                box = Rect.MinMaxRect(Mathf.Max(box.xMin, clip.xMin), Mathf.Max(box.yMin, clip.yMin),
                    Mathf.Min(box.xMax, clip.xMax), Mathf.Min(box.yMax, clip.yMax));
                inside &= !testMouse || LocalHit(prt, prt.rect.min, prt.rect.max, mouse, 0f);
            }
            return box.width > 0f && box.height > 0f;
        }

        private static bool Shown(Graphic g, Transform top, bool allowDim)
        {
            if (!g.enabled || g.GetIl2CppType().Name == "EmptyGraphic") return false;
            var mask = g.GetComponent<Mask>();
            if (mask != null && !mask.showMaskGraphic) return false;

            float a = g.color.a * g.canvasRenderer.GetAlpha();
            for (var p = g.transform; p != null && p != top.parent && a > 0.01f; p = p.parent)
            {
                var cg = p.GetComponent<CanvasGroup>();
                if (cg == null) continue;
                if (p != top && !allowDim && cg.alpha == HIDDEN_DIM) return false;
                a *= cg.alpha;
            }
            return a > 0.01f;
        }

        private static bool Quad(Graphic g, out Vector2 lo, out Vector2 hi)
        {
            var tmp = g.TryCast<TMPro.TMP_Text>();
            if (tmp != null && tmp.textBounds.size.x > 0f)
            {
                lo = tmp.textBounds.min;
                hi = tmp.textBounds.max;
            }
            else
            {
                lo = g.rectTransform.rect.min;
                hi = g.rectTransform.rect.max;
            }
            var ls = g.transform.lossyScale;
            return (hi.x - lo.x) * (hi.y - lo.y) > 1f && ls.x * ls.y != 0f;
        }

        private static Rect WorldBox(RectTransform rt, Vector2 lo, Vector2 hi)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                Vector2 w = rt.TransformPoint(new Vector3(i < 2 ? lo.x : hi.x, i % 2 == 0 ? lo.y : hi.y, 0f));
                min = Vector2.Min(min, w);
                max = Vector2.Max(max, w);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static bool LocalHit(RectTransform rt, Vector2 lo, Vector2 hi, Vector2 screen, float pad)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screen, null, out var p)) return false;
            return p.x >= lo.x - pad && p.x <= hi.x + pad && p.y >= lo.y - pad && p.y <= hi.y + pad;
        }

        private static bool Covers(RectTransform rt) =>
            rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, null);

        private RectTransform OutlineRect()
        {
            if (_selSlot == null || _selSlot.HitPaths.Length == 0 || _sel == null) return _selRt;

            for (int i = 0; i < _selSlot.HitPaths.Length; i++)
            {
                var child = _sel.Find(_selSlot.HitPaths[i]);
                if (child == null) continue;
                var rt = child.GetComponent<RectTransform>();
                if (rt != null) return rt;
            }
            return _selRt;
        }

        private void SetSelection(MenuLayoutService.Slot slot)
        {
            _selSlot = slot;
            _selPath = slot == null ? null : slot.Path;
            _selRt = slot == null ? null : MenuLayoutService.Resolve(_stage, slot);
            _sel = _selRt == null ? null : _selRt.transform;
            RefreshGizmo();
            if (_selSlot != null) ShowProps(_selSlot.Image ? PROPS_IMAGE : PROPS_ELEMENT);
            else if (_propsMode != PROPS_LAYERS) ShowProps(PROPS_NONE);
        }

        private void CycleSlot(int dir)
        {
            var slots = _kind.Slots;
            if (slots.Length == 0) return;

            int start = 0;
            for (int i = 0; i < slots.Length; i++)
                if (_selSlot == slots[i]) { start = i + dir; break; }

            for (int step = 0; step < slots.Length; step++)
            {
                int idx = ((start + dir * step) % slots.Length + slots.Length) % slots.Length;
                if (InView(slots[idx]) && LayerOf(slots[idx]) == _layer && MenuLayoutService.Resolve(_stage, slots[idx]) != null) { SetSelection(slots[idx]); return; }
            }
        }

        private void BeginDrag()
        {
            if (_selRt == null) return;
            _dragStart = Input.mousePosition;
            _dragMoved = false;
            _dragging = true;
        }

        private void EnsureHosted()
        {
            if (_selSlot == null || (_selSlot.HostPath == null && _selSlot.FramePath == null)) return;
            bool stretched = _selRt.anchorMin != _selRt.anchorMax;
            if (!MenuLayoutService.MoveToHost(_stage, _selSlot, _selRt)) return;

            var seeded = MenuLayoutService.Entry.From(_selRt);
            seeded.Hosted = true;
            if (stretched) seeded.Size = _selRt.sizeDelta;
            if (_entries.TryGetValue(_selPath, out var prev))
            {
                seeded.Scale = prev.Scale;
                seeded.Hidden = prev.Hidden;
                seeded.Rot = prev.Rot;
                if (prev.Size.x > 0f) seeded.Size = prev.Size;
            }
            _entries[_selPath] = seeded;
        }

        private void DragStep()
        {
            if (_selRt == null) { _dragging = false; return; }
            if (!_dragMoved)
            {
                if (((Vector2)Input.mousePosition - _dragStart).sqrMagnitude < DRAG_SLOP * DRAG_SLOP) return;
                EnsureHosted();
                var from = ParentRect();
                if (from == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(from, _dragStart, null, out _grabLocal))
                {
                    _dragging = false;
                    return;
                }
                _dragMoved = true;
            }
            var parent = ParentRect();
            if (parent == null) { _dragging = false; return; }
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Input.mousePosition, null, out var now)) return;

            var moved = now - _grabLocal;
            if (moved == Vector2.zero) return;
            _grabLocal = now;

            var step = moved;
            if (_selSlot != null && _selSlot.Axis == MenuLayoutService.Axis.Horizontal) step.y = 0f;
            if (_selSlot != null && _selSlot.Axis == MenuLayoutService.Axis.Vertical) step.x = 0f;

            var e = EntryFor(_selPath);
            e.Pos += step;
            Commit(e);
        }

        private void SnapAnchor()
        {
            if (_selRt == null || _selSlot == null || !_selSlot.Snap || _selSlot.FixedAnchor || _manualAnchor.Contains(_selPath)) return;
            if (_selRt.anchorMin != _selRt.anchorMax) return;

            var parent = ParentRect();
            if (parent == null) return;

            var pr = parent.rect;
            if (pr.width <= 1f || pr.height <= 1f) return;

            var centre = (Vector2)_selRt.localPosition + _selRt.rect.min + _selRt.rect.size * 0.5f;
            SetAnchor(new Vector2(
                Snap((centre.x - pr.xMin) / pr.width),
                Snap((centre.y - pr.yMin) / pr.height)));
        }

        private void CycleAnchor()
        {
            int cur = 0;
            float best = float.MaxValue;
            for (int i = 0; i < ANCHORS.Length; i++)
            {
                float d = (ANCHORS[i] - _selRt.anchorMin).sqrMagnitude;
                if (d < best) { best = d; cur = i; }
            }
            var before = Snapshot();
            SetAnchor(ANCHORS[(cur + 1) % ANCHORS.Length]);
            _manualAnchor.Add(_selPath);
            Record(before);

            if (_anchorPulse != null) Destroy(_anchorPulse);
            _anchorPulse = new GameObject("AnchorPulse");
            var rt = _anchorPulse.AddComponent<RectTransform>();
            rt.SetParent(_canvas.transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            _anchorPulse.AddComponent<BetterFG.UI.Components.HollowCirclePulse>().BaseSize = 70f;
            _anchorPulseUntil = Time.unscaledTime + 3f;
            PlaceAnchorPulse();
        }

        private void PlaceAnchorPulse()
        {
            if (_anchorPulse == null) return;
            var parent = ParentRect();
            if (Time.unscaledTime > _anchorPulseUntil || parent == null)
            {
                Destroy(_anchorPulse);
                _anchorPulse = null;
                return;
            }

            var pr = parent.rect;
            var a = _selRt.anchorMin;
            var world = parent.TransformPoint(new Vector3(pr.xMin + a.x * pr.width, pr.yMin + a.y * pr.height, 0f));
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, RectTransformUtility.WorldToScreenPoint(null, world), null, out var local))
                _anchorPulse.GetComponent<RectTransform>().anchoredPosition = local;
        }

        private void SetAnchor(Vector2 anchor)
        {
            EnsureHosted();
            var parent = ParentRect();
            if (parent == null) return;

            var r = _selRt.rect;
            var world = _selRt.TransformPoint(new Vector3(r.xMin + anchor.x * r.width, r.yMin + anchor.y * r.height, 0f));
            bool stretched = _selRt.anchorMin != _selRt.anchorMax;

            _selRt.anchorMin = _selRt.anchorMax = _selRt.pivot = anchor;
            _selRt.sizeDelta = r.size;
            _selRt.position = world;

            var e = EntryFor(_selPath);
            e.AMin = e.AMax = anchor;
            e.Pivot = anchor;
            e.Pos = _selRt.anchoredPosition;
            if (stretched || e.Size.x > 0f) e.Size = r.size;
            e.Hosted = _selSlot.HostPath != null || _selSlot.FramePath != null;
            Commit(e);
        }

        private static float Snap(float f)
        {
            if (f < 1f / 3f) return 0f;
            if (f > 2f / 3f) return 1f;
            return 0.5f;
        }

        private RectTransform ParentRect()
        {
            var p = _selRt == null || _selRt.parent == null ? null : _selRt.parent;
            return p == null ? null : p.GetComponent<RectTransform>();
        }

        private MenuLayoutService.Entry EntryFor(string path)
        {
            if (path != null && _entries.TryGetValue(path, out var e)) return e;
            return MenuLayoutService.Entry.From(_selRt);
        }

        private void Commit(MenuLayoutService.Entry e)
        {
            if (_selPath == null || _selRt == null) return;
            _entries[_selPath] = e;
            StageApply(_selSlot, _selRt, e);
            RefreshGizmo();
        }

        private void ToggleHidden()
        {
            if (_selPath == null) return;
            var before = Snapshot();
            var e = EntryFor(_selPath);
            e.Hidden = !e.Hidden;
            Commit(e);
            Record(before);
        }

        private void ResetSelection()
        {
            if (_selPath == null || _selRt == null) return;
            var before = Snapshot();
            if (_selSlot.Image)
            {
                var e = EntryFor(_selPath);
                e.Rot = 0f;
                e.Scale = 1f;
                Commit(e);
                Record(before);
                return;
            }
            _entries.Remove(_selPath);
            _manualAnchor.Remove(_selPath);
            Record(before);
            MenuLayoutService.RestoreStaged(_kind, _selSlot, _stage);
            var cg = _selRt.GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = 1f;
            RefreshGizmo();
        }

        private void RefreshGizmo()
        {
            if (_eyeImg == null) return;
            _eyeImg.texture = _selPath != null && EntryFor(_selPath).Hidden ? _eyeOff : _eyeOn;
            _cycleHandle.gameObject.SetActive(_selSlot != null && _selSlot.OneChild && _selRt != null && _selRt.childCount > 1);
            _anchorHandle.gameObject.SetActive(_selRt != null && !_selSlot.FixedAnchor);
        }

        private void UpdateBox()
        {
            var outline = _selRt == null ? null : OutlineRect();
            if (outline == null)
            {
                _box.gameObject.SetActive(false);
                _gizmo.gameObject.SetActive(false);
                return;
            }

            if (!ScreenBounds(outline, out var sr) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, sr.min, null, out var min) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, sr.max, null, out var max))
            {
                _box.gameObject.SetActive(false);
                _gizmo.gameObject.SetActive(false);
                return;
            }

            _box.gameObject.SetActive(true);
            _box.anchoredPosition = min;
            _box.sizeDelta = max - min;
            _gizmo.gameObject.SetActive(true);
            _gizmo.anchoredPosition = (min + max) * 0.5f;
        }

        private void Close(bool save)
        {
            if (_closing) return;
            _closing = true;
            StartCoroutine(Exit(save).WrapToIl2Cpp());
        }

        private System.Collections.IEnumerator Exit(bool save)
        {
            LoadingScreenService.Show();
            LoadingScreenService.Canvas.sortingOrder = LOADING_ORDER;
            yield return new WaitForSecondsRealtime(0.35f);

            if (save)
                foreach (var kv in _pending)
                {
                    MenuLayoutService.RestoreLive(kv.Key);
                    MenuLayoutService.Save(kv.Key, kv.Value);
                }

            MenuLayoutStage.Teardown();
            FGInputLockService.SetScreenLock(false);
            if (_canvas != null) Destroy(_canvas.gameObject);

            foreach (var kv in _pending) MenuLayoutService.Apply(kv.Key);
            if (_wasUiVisible) BetterFGUIMan.Instance?.SetVisible(true);
            if (_returnSel == null || !_returnSel.activeInHierarchy)
                foreach (var h in FindObjectsOfType<FGClient.UI.NavigableMenuInputHandler>())
                {
                    var last = h._lastSelectedGameObject;
                    if (h._inFocus && last != null && last.activeInHierarchy) { _returnSel = last; break; }
                }
            if (_returnSel != null && _returnSel.activeInHierarchy) EventSystem.current.SetSelectedGameObject(_returnSel);
            else Plugin.Log.LogWarning("layout editor out and no focused menu had anything to reselect, input stays dead till a hover");

            yield return LoadingScreenService.HideRoutine();
            LoadingScreenService.Canvas.sortingOrder = LoadingScreenService.DefaultOrder;
            Instance = null;
            Destroy(gameObject);
        }
    }
}
