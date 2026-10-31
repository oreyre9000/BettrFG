using System;
using System.Collections.Generic;
using BetterFG.Core;
using BetterFG.Features.Replay;
using BetterFG.Services;
using BetterFG.Utilities;
using FG.Common;
using FGClient.UI;
using FGClient.UI.Core;
using LevelEditor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Features.CreativeExpansion
{
    public class SavedGroupsBrowser : MonoBehaviour
    {
        public SavedGroupsBrowser(IntPtr ptr) : base(ptr) { }

        public static SavedGroupsBrowser Instance { get; private set; }
        public static bool Showing => Instance != null && Instance._root != null;
        public static bool Blocking => Showing || SavedGroupNamePopup.Root != null || BetterFG.UI.Windows.Creative.BatchEditMenu.AnyOpen;
        public const string GateKey = "bfg.savedgroups.browser";

        private const float Gap = 14f;
        private const float Edge = 28f;
        private const float BarWidth = 20f;
        private const float PanelTop = 270f;
        private const float DescriptionGap = 12f;
        private const float MaxPanelHeight = 580f;
        private const float ScrollTime = 0.25f;
        private const int GraceFrames = 10;
        private const string DeleteTitleKey = "bfg_savedgroups_delete_title";

        private static readonly string[] HiddenHud = { "Play Prompt", "Library Prompt", "Escape_Prompt", "UI_Reticle" };
        private static readonly NavPrompt[] Prompts =
        {
            NavPromptInjection.SavedGroupPlace, NavPromptInjection.SavedGroupFolder,
            NavPromptInjection.SavedGroupDelete, NavPromptInjection.SavedGroupBack,
        };

        private static Transform _rowTemplate;
        private static Transform _panelTemplate;
        private static Transform _infoTemplate;
        private static Transform _scrollbarTemplate;

        private GameObject _root;
        private RectTransform _panel;
        private RectTransform _content;
        private RectTransform _tiles;
        private ScrollRect _scroll;
        private float _cell;
        private float _padTop;
        private float _scrollFrom;
        private float _scrollTo;
        private float _scrollT = 1f;
        private int _selected;
        private int _openFrame;
        private bool _asking;
        private SavedGroups.Saved _pendingDelete;
        private LevelEditorCameraController _camera;
        private List<LevelEditorPlaceableObject> _saveMembers;
        private MenuInputHandler _paramHandler;
        private MenuInputHandler _reArm;

        private readonly List<SavedGroups.Saved> _groups = new List<SavedGroups.Saved>();
        private readonly List<CanvasGroup> _hud = new List<CanvasGroup>();

        void Awake()
        {
            Instance = this;
        }

        public static void Open()
        {
            if (!CreativeGroups.Enabled || Instance == null || Instance._root != null) return;
            Instance.Show();
        }

        private void Show()
        {
            var lem = LevelEditorManager.Instance;
            if (lem == null) return;
            if (lem.IsInObjectMenuState()) lem.LoadPreviousLevelEditorState();

            FindTemplates();
            BuildUi();
            Select(0, false);
            PatchGate.Request(GateKey, "saved groups browser", true);

            _camera = lem.Camera.Controller;
            _camera.enabled = false;
            HideEditorHud(_hud);

            AddPrompt(NavPromptInjection.SavedGroupPlace, BfgBinds.SavedGroupPlace, PlaceSelected);
            AddPrompt(NavPromptInjection.SavedGroupFolder, BfgBinds.SavedGroupFolder, RevealSelected);
            AddPrompt(NavPromptInjection.SavedGroupDelete, BfgBinds.SavedGroupDelete, AskDelete);
            AddPrompt(NavPromptInjection.SavedGroupBack, BfgBinds.SavedGroupBack, Back);
            NavPromptInjection.Solo(Prompts);

            _openFrame = Time.frameCount;
            EditorSound(AudioManager.EventMasterData?.CreativeModeItemParamsEnter);
            Plugin.Log.LogInfo($"saved groups browser up, {_groups.Count} on disk");
        }

        public static void AskSaveToFile(List<LevelEditorPlaceableObject> members, string suggested)
        {
            if (Instance == null || Instance._root != null || SavedGroupNamePopup.Root != null) return;
            Instance.BeginNaming(members, suggested);
        }

        private void BeginNaming(List<LevelEditorPlaceableObject> members, string suggested)
        {
            var lem = LevelEditorManager.Instance;
            if (lem == null) return;

            _saveMembers = members;
            var vm = LevelEditorParameterMenuViewModel._instance;
            _paramHandler = vm != null && vm._inputHandlers != null && vm._inputHandlers.Count > 0 ? vm._inputHandlers[0] : null;
            if (_paramHandler != null) _paramHandler.enabled = false;
            _camera = lem.Camera.Controller;
            _camera.enabled = false;
            PatchGate.Request(GateKey, "saved groups naming", true);

            SavedGroupNamePopup.Show(suggested, OnNamed, OnNamingClosed);
            EditorSound(AudioManager.EventMasterData?.CreativeModeItemParamsEnter);
        }

        private void OnNamed(string name)
        {
            int n = SavedGroups.Save(name, _saveMembers, out string status);
            Plugin.Log.LogInfo($"group written out from the param menu as '{name}', {_saveMembers.Count} member(s): {status}");
            if (n > 0) EditorSound(AudioManager.EventMasterData?.CreativeModeItemEditConfirm);
        }

        private void OnNamingClosed()
        {
            _saveMembers = null;
            PatchGate.Request(GateKey, "saved groups naming", false);
            if (_camera != null) _camera.enabled = true;
            _camera = null;
            _reArm = _paramHandler;
            _paramHandler = null;
        }

        private void TickReArm()
        {
            bool held = Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter) || Input.GetKey(KeyCode.Escape);
            var player = _reArm._rewiredPlayer;
            if (!held && player != null) held = player.GetButton(_reArm.SubmitAction) || player.GetButton(_reArm.CancelAction);
            if (held) return;
            _reArm.enabled = true;
            _reArm = null;
        }

        internal static void EditorSound(string key)
        {
            if (!string.IsNullOrEmpty(key)) AudioManager.PlayOneShot(key, Vector3.zero);
        }

        internal static void HideEditorHud(List<CanvasGroup> into)
        {
            into.Clear();
            var hud = GameObject.Find("UICanvas_Client_V2(Clone)/Default/Prime_UI_LE_Navigation_Canvas(Clone)/Safe Area").transform;
            foreach (var name in HiddenHud)
            {
                var go = hud.Find(name).gameObject;
                var cg = go.GetComponent<CanvasGroup>();
                if (cg == null) cg = go.AddComponent<CanvasGroup>();
                cg.alpha = 0f;
                cg.blocksRaycasts = false;
                into.Add(cg);
            }
        }

        internal static void RestoreEditorHud(List<CanvasGroup> hidden)
        {
            foreach (var cg in hidden)
            {
                if (cg == null) continue;
                cg.alpha = 1f;
                cg.blocksRaycasts = true;
            }
            hidden.Clear();
        }

        internal static void AddPrompt(NavPrompt key, BfgBind bind, Action pressed)
        {
            var data = NavPromptInjection.BuildData(NavPrompt.Back, LocalizationService.Get(bind.LabelKey),
                "bfg_" + bind.Id, bind.ActionId, RewiredConsts.Category.Default);
            NavPromptInjection.Add(key, pressed, data);
        }

        private void Close()
        {
            if (_root == null) return;
            DestroyUi();
            PatchGate.Request(GateKey, "saved groups browser", false);

            foreach (var key in Prompts) NavPromptInjection.Remove(key);
            NavPromptInjection.Solo(null);
            RestoreEditorHud(_hud);

            if (_camera != null) _camera.enabled = true;
            _camera = null;
            _asking = false;
            _pendingDelete = null;
        }

        private void Back()
        {
            EditorSound(AudioManager.EventMasterData?.CreativeModeItemParamsExit);
            Close();
        }

        void Update()
        {
            if (_reArm != null) TickReArm();
            if (SavedGroupNamePopup.Root != null)
            {
                if (BfgBinds.SavedGroupBack.Down) SavedGroupNamePopup.Close();
                return;
            }
            if (_root == null) return;
            TickScroll();
            if (_asking) return;
            if (!IdentifierObjects.InEditor()) { Close(); return; }
            if (Time.frameCount - _openFrame < GraceFrames || FallGuysLib.UI.PopUp.HasActive) return;

            if (BfgBinds.SavedGroupBack.Down) { Back(); return; }
            if (_groups.Count == 0) return;
            if (BfgBinds.SavedGroupPlace.Down) { PlaceSelected(); return; }
            if (BfgBinds.SavedGroupFolder.Down) { RevealSelected(); return; }
            if (BfgBinds.SavedGroupDelete.Down) { AskDelete(); return; }

            int next = _selected;
            if (BfgBinds.SavedGroupLeft.Down && _selected % 2 == 1) next--;
            else if (BfgBinds.SavedGroupRight.Down && _selected % 2 == 0) next++;
            else if (BfgBinds.SavedGroupUp.Down) next -= 2;
            else if (BfgBinds.SavedGroupDown.Down) next += 2;
            if (next != _selected && next >= 0 && next < _groups.Count) Select(next, true);
        }

        internal void Hover(int index)
        {
            if (_root != null && !_asking && index != _selected && index < _groups.Count) Select(index, true);
        }

        internal void Click(int index)
        {
            if (_root == null || _asking || index >= _groups.Count) return;
            if (index != _selected) Select(index, false);
            PlaceSelected();
        }

        private void PlaceSelected()
        {
            if (_groups.Count == 0) return;
            var g = _groups[_selected];
            EditorSound(AudioManager.EventMasterData?.CreativeModeItemEditConfirm);
            Close();
            int n = SavedGroups.Place(g, out string note, out var middle);
            Plugin.Log.LogInfo($"{g.Name} out of the browser: {note}");
            if (n > 0 && middle != null) middle.SelectObject();
        }

        private void RevealSelected()
        {
            if (_groups.Count > 0) ReplayExport.Reveal(_groups[_selected].Json);
        }

        private void AskDelete()
        {
            if (_groups.Count == 0) return;
            _pendingDelete = _groups[_selected];
            _asking = true;
            NavPromptCore.SetCmsString(DeleteTitleKey, string.Format(LocalizationService.Get("savedgroups.delete_title"), _pendingDelete.Name));
            FallGuysLib.UI.PopUp.Confirm(DeleteTitleKey, "", OnDeleteAnswered, null, null, UIModalMessage.OKButtonType.Disruptive);
        }

        private void OnDeleteAnswered(bool ok)
        {
            _asking = false;
            _openFrame = Time.frameCount;
            if (!ok || _pendingDelete == null || _root == null) return;

            SavedGroups.Delete(_pendingDelete);
            EditorSound(AudioManager.EventMasterData?.CreativeModeItemDelete);
            _pendingDelete = null;
            int keep = _selected;
            DestroyUi();
            BuildUi();
            Select(Mathf.Clamp(keep, 0, Mathf.Max(0, _groups.Count - 1)), false);
        }

        private void Select(int next, bool sound)
        {
            if (_groups.Count == 0) return;
            SetTileOn(_selected, false);
            _selected = next;
            SetTileOn(_selected, true);
            if (sound) EditorSound(AudioManager.EventMasterData?.CreativeModeLibraryTab);

            float top = _padTop + (_selected / 2) * (_cell + Gap);
            float bottom = top + _cell;
            float height = _panel.rect.height;
            float y = _scrollT < 1f ? _scrollTo : _content.anchoredPosition.y;
            if (top - _padTop < y) y = top - _padTop;
            if (bottom + Edge > y + height) y = bottom + Edge - height;
            y = Mathf.Clamp(y, 0f, Mathf.Max(0f, _content.rect.height - height));
            if (Mathf.Approximately(y, _content.anchoredPosition.y)) return;

            _scroll.StopMovement();
            _scrollFrom = _content.anchoredPosition.y;
            _scrollTo = y;
            _scrollT = 0f;
        }

        private void TickScroll()
        {
            if (_scrollT >= 1f) return;
            _scrollT = Mathf.Min(1f, _scrollT + Time.unscaledDeltaTime / ScrollTime);
            float eased = 1f - Mathf.Pow(1f - _scrollT, 3f);
            _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, Mathf.Lerp(_scrollFrom, _scrollTo, eased));
        }

        private void SetTileOn(int index, bool on)
        {
            if (index < 0 || index >= _tiles.childCount) return;
            var tile = _tiles.GetChild(index);
            tile.Find("BgOn").gameObject.SetActive(on);
            tile.Find("BgOff").gameObject.SetActive(!on);
        }

        private void DestroyUi()
        {
            foreach (var g in _groups)
                if (g.Preview != null) Destroy(g.Preview);
            _groups.Clear();
            if (_root != null) Destroy(_root);
            _root = null;
        }

        private static void FindTemplates()
        {
            if (_rowTemplate != null && _panelTemplate != null && _infoTemplate != null && _scrollbarTemplate != null) return;

            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t.parent != null || t.gameObject.scene.IsValid()) continue;
                if (t.name == "Prime_UI_LE_ParameterViewModel_Button") _rowTemplate = t;
                else if (t.name == "Prime_UI_LE_ParametersMenu_Canvas") _panelTemplate = t.Find("Safe Area/UI_LevelEditorParametersMenu2/VisualMask/UI_LevelEditorParametersMenuBG");
                else if (t.name == "Prime_UI_LE_Navigation_Canvas") _infoTemplate = t.Find("Safe Area/UI_ObjectInfo_Main/Panel_Full");
            }
            foreach (var sb in Resources.FindObjectsOfTypeAll<Scrollbar>())
                if (!sb.gameObject.scene.IsValid() && sb.name == "TabScrollbar" && sb.transform.parent != null && sb.transform.parent.name == "ShowsScrollList")
                    _scrollbarTemplate = sb.transform;
        }

        private static void Strip(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                string n = c.GetIl2CppType().Name;
                if (n.EndsWith("Binding") || n.StartsWith("ParameterNodeViewModel") || n == "LocalisedStaticLabel" || n == "TweenDropDown"
                    || n == "UICustomToggle" || n == "LevelEditorTweenOnToggleSubMenu" || n == "LevelEditor_UIButtonClickHandler"
                    || n == "LevelEditorTransformPunchScale" || n == "UITweenTriggerComponent" || n == "UITweenData" || n == "EventTrigger")
                    DestroyImmediate(c);
            }
        }

        private static RectTransform TopRight(GameObject go, float y)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(-290f, y);
            return rt;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private void BuildUi()
        {
            _groups.Clear();
            _groups.AddRange(SavedGroups.All());

            _root = new GameObject("BettrFG_SavedGroups");
            var root = _root.AddComponent<RectTransform>();
            root.SetParent(GameObject.Find("UICanvas_Client_V2(Clone)/Popup").transform, false);
            Stretch(root);

            var info = Instantiate(_infoTemplate.gameObject, root, false);
            Strip(info);
            info.SetActive(true);
            TopRight(info, -170f);
            foreach (var t in info.GetComponentsInChildren<Transform>(true))
                if (t.name == "ParametersHintShow") t.gameObject.SetActive(false);
            TextMeshProUGUI description = null;
            foreach (var tmp in info.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (tmp.name == "Tag_Text_line") tmp.text = LocalizationService.Get("savedgroups.title").ToUpperInvariant();
                else if (tmp.name == "Descriptive Text") { tmp.text = LocalizationService.Get("savedgroups.description"); description = tmp; }
            }
            var badge = info.transform.Find("ContentHolder/Title/Object Cost");
            badge.gameObject.SetActive(false);

            LayoutRebuilder.ForceRebuildLayoutImmediate(info.GetComponent<RectTransform>());
            var descRt = description.rectTransform;
            float descTop = root.rect.yMax - root.InverseTransformPoint(descRt.TransformPoint(new Vector3(0f, descRt.rect.yMax, 0f))).y;
            float maskTop = descTop + description.preferredHeight + DescriptionGap;

            var mask = new GameObject("Mask").AddComponent<RectTransform>();
            mask.SetParent(root, false);
            Stretch(mask);
            mask.gameObject.AddComponent<RectMask2D>().padding = new Vector4(-50f, 91.5f, 0f, maskTop);

            var panel = Instantiate(_panelTemplate.gameObject, mask, false);
            Strip(panel);
            panel.SetActive(true);
            _panel = TopRight(panel, -PanelTop);
            _scroll = panel.GetComponent<ScrollRect>();
            _content = panel.transform.Find("Content").GetComponent<RectTransform>();
            for (int i = _content.childCount - 1; i >= 0; i--) DestroyImmediate(_content.GetChild(i).gameObject);

            var vlg = _content.GetComponent<VerticalLayoutGroup>();
            _padTop = maskTop - PanelTop + 6f;
            vlg.padding.top = (int)_padTop;
            float contentLeft = _content.anchoredPosition.x - _content.rect.width * _content.pivot.x;
            float gridWidth = _panel.rect.width - Edge - Gap - BarWidth - Edge;
            vlg.padding.left = (int)(Edge - contentLeft);
            vlg.padding.right = (int)(_content.rect.width - (Edge + gridWidth - contentLeft));
            _cell = (gridWidth - Gap) * 0.5f;

            _tiles = new GameObject("Tiles").AddComponent<RectTransform>();
            _tiles.SetParent(_content, false);
            var grid = _tiles.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(_cell, _cell);
            grid.spacing = new Vector2(Gap, Gap);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            int rows = (_groups.Count + 1) / 2;
            float gridHeight = Mathf.Max(0f, rows * (_cell + Gap) - Gap);
            var le = _tiles.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = le.minHeight = gridHeight;
            _tiles.sizeDelta = new Vector2(gridWidth, gridHeight);

            for (int i = 0; i < _groups.Count; i++) BuildTile(i, badge);

            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            float height = Mathf.Min(_content.rect.height, MaxPanelHeight);
            var panelLe = panel.GetComponent<LayoutElement>();
            if (panelLe != null) panelLe.preferredHeight = height;
            _panel.sizeDelta = new Vector2(_panel.sizeDelta.x, height);

            var bar = Instantiate(_scrollbarTemplate.gameObject, panel.transform, false);
            bar.SetActive(true);
            var barRt = bar.GetComponent<RectTransform>();
            barRt.anchorMin = new Vector2(1f, 0f);
            barRt.anchorMax = new Vector2(1f, 1f);
            barRt.pivot = new Vector2(0.5f, 0.5f);
            barRt.sizeDelta = new Vector2(BarWidth, 0f);
            barRt.offsetMin = new Vector2(barRt.offsetMin.x, Edge);
            barRt.offsetMax = new Vector2(barRt.offsetMax.x, -_padTop);
            barRt.anchoredPosition = new Vector2(-(Edge + BarWidth * 0.5f), barRt.anchoredPosition.y);

            _scroll.vertical = true;
            _scroll.horizontal = false;
            _scroll.verticalScrollbar = bar.GetComponent<Scrollbar>();
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            _scroll.verticalNormalizedPosition = 1f;
            _scrollT = 1f;
            _selected = 0;
        }

        private void BuildTile(int index, Transform badge)
        {
            var g = _groups[index];
            var tile = new GameObject(g.Name).AddComponent<RectTransform>();
            tile.SetParent(_tiles, false);
            SavedGroupsTilePointer.Wire(tile.gameObject, index);

            foreach (var which in new[] { "Background", "BackgroundSelected" })
            {
                var back = Instantiate(_rowTemplate.Find(which).gameObject, tile, false);
                Strip(back);
                back.name = which == "Background" ? "BgOff" : "BgOn";
                back.SetActive(which == "Background");
                foreach (var rt in back.GetComponentsInChildren<RectTransform>(true)) Stretch(rt);
                foreach (var cg in back.GetComponentsInChildren<CanvasGroup>(true)) cg.alpha = 1f;
                foreach (var t in back.GetComponentsInChildren<Transform>(true))
                    if (t.name == "BackgroundDisabled") t.gameObject.SetActive(false);
            }

            var cover = new GameObject("Cover").AddComponent<RectTransform>();
            cover.SetParent(tile, false);
            cover.anchorMin = Vector2.zero;
            cover.anchorMax = Vector2.one;
            cover.offsetMin = new Vector2(12f, 12f);
            cover.offsetMax = new Vector2(-12f, -12f);
            var raw = cover.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            var tex = SavedGroups.PreviewOf(g);
            if (tex != null) raw.texture = tex;
            else raw.color = Color.clear;

            var title = Instantiate(_rowTemplate.Find("ParamTitle").gameObject, tile, false);
            Strip(title);
            title.SetActive(true);
            var titleRt = title.GetComponent<RectTransform>();
            titleRt.anchorMin = Vector2.zero;
            titleRt.anchorMax = new Vector2(1f, 0f);
            titleRt.pivot = Vector2.zero;
            titleRt.offsetMin = new Vector2(18f, 6f);
            titleRt.offsetMax = new Vector2(-88f, 46f);
            var text = title.GetComponent<TextMeshProUGUI>();
            text.text = g.Name;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.enableAutoSizing = false;
            text.fontSize = 22f;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;

            var tag = Instantiate(badge.gameObject, tile, false);
            tag.SetActive(true);
            var tagRt = tag.GetComponent<RectTransform>();
            tagRt.anchorMin = tagRt.anchorMax = new Vector2(1f, 0f);
            tagRt.pivot = new Vector2(1f, 0f);
            tagRt.anchoredPosition = new Vector2(-11f, 11f);
            tagRt.localScale = new Vector3(0.75f, 0.75f, 1f);
            foreach (var tmp in tag.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (tmp.name == "Points") tmp.text = SavedGroups.CostOf(g).ToString();
        }
    }

    internal static class SavedGroupsTilePointer
    {
        public static void Wire(GameObject tile, int index)
        {
            var trigger = tile.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var enter = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
            enter.callback.AddListener(new Action<UnityEngine.EventSystems.BaseEventData>(_ => SavedGroupsBrowser.Instance?.Hover(index)));
            trigger.triggers.Add(enter);
            var click = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerClick };
            click.callback.AddListener(new Action<UnityEngine.EventSystems.BaseEventData>(_ => SavedGroupsBrowser.Instance?.Click(index)));
            trigger.triggers.Add(click);
        }
    }

    [BfgPatchGate(SavedGroupsBrowser.GateKey)]
    [HarmonyLib.HarmonyPatch(typeof(FGClient.LevelEditorStateReticleInputHandler), "ProcessInput")]
    internal static class SavedGroupsBrowserReticleInputPatch
    {
        [HarmonyLib.HarmonyPrefix]
        public static bool Prefix() => !SavedGroupsBrowser.Blocking;
    }

    [BfgPatchGate(SavedGroupsBrowser.GateKey)]
    [HarmonyLib.HarmonyPatch(typeof(FGClient.LevelEditorStateReticle), "ProcessBackButtonInput")]
    internal static class SavedGroupsBrowserReticleBackPatch
    {
        [HarmonyLib.HarmonyPrefix]
        public static bool Prefix() => !SavedGroupsBrowser.Blocking;
    }

    internal static class SavedGroupNamePopup
    {
        public static GameObject Root { get; private set; }

        private static GameObject _template;
        private static TMP_InputField _input;
        private static Action<string> _onSave;
        private static Action _onClosed;

        public static void Show(string suggested, Action<string> onSave, Action onClosed)
        {
            if (_template == null)
                foreach (var vm in Resources.FindObjectsOfTypeAll<ModalMessageWithInputFieldViewModel>())
                    if (!vm.gameObject.scene.IsValid()) _template = vm.gameObject;

            _onSave = onSave;
            _onClosed = onClosed;

            var holder = new GameObject("BettrFG_PopupHolder");
            holder.SetActive(false);
            var modal = UnityEngine.Object.Instantiate(_template, holder.transform, false);
            foreach (var c in modal.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                string n = c.GetIl2CppType().Name;
                if (n.EndsWith("Binding") || n.EndsWith("ViewModel") || n == "ScreenFaderComponent" || n == "CanvasGroupFader"
                    || n == "TweenOnPopup" || n == "TMPUrlLinkHandler" || n == "OnInputKeyDownClickUIButton")
                    UnityEngine.Object.DestroyImmediate(c);
            }

            Root = new GameObject("BettrFG_SaveGroupPopup");
            var root = Root.AddComponent<RectTransform>();
            root.SetParent(GameObject.Find("UICanvas_Client_V2(Clone)/Popup").transform, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            Root.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            modal.transform.SetParent(root, false);
            UnityEngine.Object.Destroy(holder);
            var modalRt = modal.GetComponent<RectTransform>();
            modalRt.anchorMin = modalRt.anchorMax = new Vector2(0.5f, 0.5f);
            modalRt.anchoredPosition = Vector2.zero;
            var group = modal.GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;

            var t = modal.transform;
            t.Find("Panel/Title/Title_Text").GetComponent<TextMeshProUGUI>().text = LocalizationService.Get("savedgroups.save_title").ToUpperInvariant();
            t.Find("Content/Content_Text").gameObject.SetActive(false);
            t.Find("Content/Content_Text_2").gameObject.SetActive(false);
            t.Find("Content/InputTextArea/InputField/Text Area/PlaceholderText").GetComponent<TextMeshProUGUI>().text = LocalizationService.Get("savedgroups.save_placeholder");
            t.Find("ButtonContainer/LeftButton/Content/Text").GetComponent<TextMeshProUGUI>().text = LocalizationService.Get("savedgroups.cancel");
            t.Find("ButtonContainer/RightButton/Content/Text").GetComponent<TextMeshProUGUI>().text = LocalizationService.Get("savedgroups.save");
            OnClick(t.Find("ButtonContainer/LeftButton").gameObject, Close);
            OnClick(t.Find("ButtonContainer/RightButton").gameObject, Submit);

            _input = t.Find("Content/InputTextArea/InputField").GetComponent<TMP_InputField>();
            _input.text = suggested ?? "";
            _input.onSubmit.AddListener(new Action<string>(_ => Submit()));
            LayoutRebuilder.ForceRebuildLayoutImmediate(modalRt);
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(_input.gameObject);
            _input.ActivateInputField();
        }

        private static void OnClick(GameObject button, Action pressed)
        {
            var trigger = button.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var click = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerClick };
            click.callback.AddListener(new Action<UnityEngine.EventSystems.BaseEventData>(_ => pressed()));
            trigger.triggers.Add(click);
        }

        private static void Submit()
        {
            if (Root == null) return;
            string name = _input.text.Trim();
            if (name.Length == 0) return;
            _onSave?.Invoke(name);
            Close();
        }

        public static void Close()
        {
            if (Root == null) return;
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            UnityEngine.Object.Destroy(Root);
            Root = null;
            _input = null;
            _onSave = null;
            var closed = _onClosed;
            _onClosed = null;
            closed?.Invoke();
        }
    }
}
