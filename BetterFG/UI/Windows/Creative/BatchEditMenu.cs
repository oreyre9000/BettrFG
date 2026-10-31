using System;
using System.Collections.Generic;
using System.Globalization;
using BetterFG.Core;
using BetterFG.Features.CreativeExpansion;
using BetterFG.Features.CreativeIncrements;
using BetterFG.Features.Replay;
using BetterFG.Services;
using BetterFG.Utilities;
using BettrFG.uGUI;
using FG.Common;
using FG.Common.CMS;
using FGClient.UI;
using FGClient.UI.Core;
using LevelEditor;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Groups = BetterFG.Features.CreativeExpansion.CreativeGroups;
using Object = UnityEngine.Object;

namespace BetterFG.UI.Windows.Creative
{
    public static class BatchEditMenu
    {
        private const string EnabledKey = "creative.batchedit.enabled";
        public static bool FeatureEnabled
        {
            get => SettingsService.Get(EnabledKey, "true") != "false";
            set => SettingsService.Set(EnabledKey, value ? "true" : "false");
        }

        public static bool AnyOpen => _root != null;

        private const string GateReason = "batch edit";
        private const float MaskTop = 264f;
        private const float CornerHide = 34f;
        private const float PadTop = CornerHide + 6f;
        private const float Edge = 28f;
        private const float MaxPanelHeight = 580f;
        private const float ScrollTime = 0.25f;
        private const int GraceFrames = 10;
        private const float SliderFont = 24f;
        private const float FactorLimit = 1000f;

        private static readonly string[] TabKeys =
        {
            "batchedit.tab.recolour", "batchedit.tab.scale", "batchedit.tab.material", "batchedit.tab.physics",
            "batchedit.tab.link", "batchedit.tab.group",
        };
        private static readonly string[] AxisKeys = { "batchedit.scale_x", "batchedit.scale_y", "batchedit.scale_z" };
        private static readonly string[] ScaleModeKeys = { "batchedit.from_origin", "batchedit.individual", "batchedit.from_center", "batchedit.from_selected" };
        private static readonly NavPrompt[] Prompts = { NavPromptInjection.BatchEditSelect, NavPromptInjection.BatchEditBack };
        private static readonly Color Dark = new Color(0.09f, 0.11f, 0.22f, 1f);

        private sealed class Row
        {
            public RectTransform Rt;
            public GameObject On, Off;
            public TextMeshProUGUI Title, Value;
            public bool Passive;
            public Action<bool> Paint;
            public Action<int> Step;
            public float Repeat;
            public Action Confirm;
            public Action<float> Slide;
            public Action Release;
            public Func<Vector2> Measure;
            public Action<Vector2> Fit;
        }

        private enum RecolourMode { SetColour, Modify }

        private static Transform _choiceT, _buttonT, _colourT, _panelT, _infoT, _pickerT, _swatchT;
        private static GameObject _modalT;
        private static Sprite _smallCircle, _bigCircle, _revert, _tickIcon, _linkIcon, _unlinkIcon, _saveIcon;
        private static Color _pink;

        private static GameObject _root;
        private static RectTransform _panel, _content;
        private static ScrollRect _scroll;
        private static Transform _rowParent;
        private static float _innerW;
        private static readonly List<Row> _rows = new List<Row>();
        private static readonly List<CanvasGroup> _hud = new List<CanvasGroup>();
        private static LevelEditorCameraController _camera;
        private static int _sel, _tab, _act, _openFrame, _editEnd = -1, _repX, _repY;
        private static float _repAtX, _repAtY, _slideHold, _scrollFrom, _scrollTo, _scrollT = 1f;
        private static TMP_InputField _editing;
        private static Row _editRow;
        private static Action<string> _editDone;
        private static bool _editWasOn;

        private static RecolourMode _recolourMode = RecolourMode.SetColour;
        private static Color _colour = new Color(1f, 0.4f, 0.2f, 1f);
        private static float _modBright, _modContrast, _modHue, _modSat;
        private static readonly Dictionary<LevelEditorPlaceableObject, Color> _colourOriginals = new Dictionary<LevelEditorPlaceableObject, Color>();
        private static BatchEditHistory.BatchEntry _colourEntry, _scaleEntry;
        private static int _colourSessionSelCount;
        private static readonly float[] _factors = { 1f, 1f, 1f };
        private static readonly float[] _committed = { 1f, 1f, 1f };
        private static ScaleMode _scaleMode = ScaleMode.Individual;
        private static int _groupPick;
        private static string _groupName = "";

        internal static string L(string key) => LocalizationService.Get(key);
        internal static void Sound(string key) => SavedGroupsBrowser.EditorSound(key);

        public static void Open()
        {
            if (_root != null || !FeatureEnabled) return;
            var lem = LevelEditorManager.Instance;
            if (lem == null) return;

            FindTemplates();
            Patches.BatchEditBlockPlacePatch.BlockedAPlace = false;
            _tab = BatchLink.Controller() != null ? 4 : 0;
            _act = 0;
            _groupPick = -1;
            ResetFactors();

            BuildShell();
            Rebuild(0);

            PatchGate.Request(SavedGroupsBrowser.GateKey, GateReason, true);
            _camera = lem.Camera.Controller;
            _camera.enabled = false;
            SavedGroupsBrowser.HideEditorHud(_hud);
            SavedGroupsBrowser.AddPrompt(NavPromptInjection.BatchEditSelect, BfgBinds.BatchEditSelect, ConfirmSelected);
            SavedGroupsBrowser.AddPrompt(NavPromptInjection.BatchEditBack, BfgBinds.BatchEditBack, Back);
            NavPromptInjection.Solo(Prompts);

            _openFrame = Time.frameCount;
            Sound(AudioManager.EventMasterData?.CreativeModeItemParamsEnter);
            Plugin.Log.LogInfo($"batch edit up on {L(TabKeys[_tab])}, {BatchRecolour.SelectionCount()} selected");
        }

        public static void Close()
        {
            if (_root == null) return;
            if (_editing != null) FinishEdit(false);
            BatchColourPicker.Close(false);
            CommitPending();
            BatchScale.BakeOwnerScale();

            Object.Destroy(_root);
            _root = null;
            _rows.Clear();

            PatchGate.Request(SavedGroupsBrowser.GateKey, GateReason, false);
            foreach (var key in Prompts) NavPromptInjection.Remove(key);
            NavPromptInjection.Solo(null);
            SavedGroupsBrowser.RestoreEditorHud(_hud);
            if (_camera != null) _camera.enabled = true;
            _camera = null;

            CreativeSelectionWatcher.Instance?.PlaceAfterFrame();
        }

        private static void Back()
        {
            if (BatchColourPicker.IsOpen || Time.frameCount == _editEnd) return;
            Sound(AudioManager.EventMasterData?.CreativeModeItemParamsExit);
            Close();
        }

        internal static void Tick()
        {
            if (_root == null) return;
            if (!IdentifierObjects.InEditor()) { Close(); return; }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            TickScroll();

            if (_editing != null)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || BfgBinds.BatchEditBack.Down) FinishEdit(false);
                return;
            }
            if (BatchColourPicker.IsOpen) { BatchColourPicker.Tick(); return; }
            if (SavedGroupNamePopup.Root != null || FallGuysLib.UI.PopUp.HasActive) return;
            if (Time.frameCount - _openFrame < GraceFrames || Time.frameCount == _editEnd) return;

            int count = BatchRecolour.SelectionCount();
            if (count == 0) { Close(); return; }
            if (_colourEntry != null && count != _colourSessionSelCount) CommitColourEntry();

            if (BfgBinds.BatchEditBack.Down) { Back(); return; }
            if (BfgBinds.BatchEditSelect.Down || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { ConfirmSelected(); return; }

            ReadNav(out int dx, out int dy);

            float now = Time.unscaledTime;
            if (dy == 0) _repY = 0;
            else if (dy != _repY || now >= _repAtY)
            {
                _repAtY = now + (dy != _repY ? 0.32f : 0.09f);
                _repY = dy;
                int n = _sel + dy;
                while (n >= 0 && n < _rows.Count && _rows[n].Passive) n += dy;
                if (n >= 0 && n < _rows.Count) Select(n, true);
            }

            var row = _rows[_sel];
            if (row.Slide != null)
            {
                float amount = SlideAmount(dx);
                if (Mathf.Abs(amount) < 0.01f) { _slideHold = 0f; return; }
                _slideHold += Time.unscaledDeltaTime;
                row.Slide(amount * Mathf.Lerp(0.12f, 0.9f, Mathf.Clamp01(_slideHold / 1.2f)) * Time.unscaledDeltaTime);
                return;
            }
            _slideHold = 0f;

            if (dx == 0)
            {
                if (_repX != 0) { _repX = 0; row.Release?.Invoke(); }
                return;
            }
            if (row.Step == null || (dx == _repX && (row.Repeat <= 0f || now < _repAtX))) return;
            _repAtX = now + (dx != _repX ? 0.32f : row.Repeat);
            _repX = dx;
            row.Step(dx);
        }

        internal static void ReadNav(out int dx, out int dy)
        {
            NavPromptCore.PadDirection(out dx, out int padY);
            dy = -padY;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) dx = 1;
            else if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) dx = -1;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) dy = 1;
            else if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) dy = -1;
        }

        internal static float SlideAmount(int dx)
        {
            float stick = NavPromptCore.AxisByName(NavPromptCore.PadStickXNames);
            return Mathf.Abs(stick) > 0.15f ? stick : dx;
        }

        private static void ConfirmSelected()
        {
            if (_root == null || _editing != null || BatchColourPicker.IsOpen || Time.frameCount == _editEnd) return;
            Press(_rows[_sel]);
        }

        private static void Press(Row r)
        {
            if (r.Confirm != null) { Sound(AudioManager.EventMasterData?.CreativeModeItemEditConfirm); r.Confirm(); }
            else if (r.Step != null && r.Slide == null) r.Step(1);
        }

        private static void Click(Row r)
        {
            if (_editing != null || r.Passive) return;
            Select(_rows.IndexOf(r), false);
            Press(r);
        }

        private static void Select(int i, bool sound)
        {
            if (i == _sel || i < 0 || i >= _rows.Count || _rows[i].Passive) return;
            if (_sel >= 0 && _sel < _rows.Count) _rows[_sel].Paint(false);
            _sel = i;
            _rows[_sel].Paint(true);
            if (sound) Sound(AudioManager.EventMasterData?.CreativeModeLibraryTab);
            ScrollToSel();
        }

        private static void ScrollToSel()
        {
            var rt = _rows[_sel].Rt;
            float top = _content.rect.yMax - _content.InverseTransformPoint(rt.TransformPoint(new Vector3(0f, rt.rect.yMax, 0f))).y;
            float bottom = top + rt.rect.height;
            float h = _panel.rect.height;
            float y = _scrollT < 1f ? _scrollTo : _content.anchoredPosition.y;
            if (top - PadTop < y) y = top - PadTop;
            if (bottom + Edge > y + h) y = bottom + Edge - h;
            y = Mathf.Clamp(y, 0f, Mathf.Max(0f, _content.rect.height - h));
            if (Mathf.Approximately(y, _content.anchoredPosition.y)) return;
            _scroll.StopMovement();
            _scrollFrom = _content.anchoredPosition.y;
            _scrollTo = y;
            _scrollT = 0f;
        }

        private static void TickScroll()
        {
            if (_scrollT >= 1f) return;
            _scrollT = Mathf.Min(1f, _scrollT + Time.unscaledDeltaTime / ScrollTime);
            float eased = 1f - Mathf.Pow(1f - _scrollT, 3f);
            _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, Mathf.Lerp(_scrollFrom, _scrollTo, eased));
        }

        private static void FindTemplates()
        {
            if (_choiceT != null && _buttonT != null && _colourT != null && _panelT != null && _infoT != null && _modalT != null
                && _pickerT != null && _swatchT != null && _smallCircle != null && _bigCircle != null && _revert != null && _tickIcon != null && _linkIcon != null && _unlinkIcon != null && _saveIcon != null) return;

            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t.parent != null || t.gameObject.scene.IsValid()) continue;
                if (t.name == "Prime_UI_LE_ParameterViewModel_String") _choiceT = t;
                else if (t.name == "Prime_UI_LE_ParameterViewModel_Button") _buttonT = t;
                else if (t.name == "Prime_UI_LE_ParameterViewModel_ColorV2") _colourT = t;
                else if (t.name == "Prime_UI_LE_ParametersMenu_Canvas") _panelT = t.Find("Safe Area/UI_LevelEditorParametersMenu2/VisualMask/UI_LevelEditorParametersMenuBG");
                else if (t.name == "Prime_UI_LE_Navigation_Canvas") _infoT = t.Find("Safe Area/UI_ObjectInfo_Main/Panel_Full");
                else if (t.name == "Prime_UI_LE_ColourPickerPanel_Prefab") _pickerT = t.Find("Safe Area/UI_LE_ObjectInfoPanel_Prefab");
                else if (t.name == "UI_ColorPickerSwatch_Prefab") _swatchT = t;
            }
            foreach (var vm in Resources.FindObjectsOfTypeAll<ModalMessageWithInputFieldViewModel>())
                if (!vm.gameObject.scene.IsValid()) _modalT = vm.gameObject;
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (s.name == "UI_32xCircle_Image") _smallCircle = s;
                else if (s.name == "UI_Circle") _bigCircle = s;
                else if (s.name == "UI_editor_menu_Revert") _revert = s;
                else if (s.name == "Navprompt_Icon_Confirm_128") _tickIcon = s;
                else if (s.name == "UI_Radial_Icon_Link") _linkIcon = s;
                else if (s.name == "UI_Radial_Icon_Link_Selected") _unlinkIcon = s;
                else if (s.name == "UI_editor_menu_Save") _saveIcon = s;
            }
            _pink = _choiceT.Find("BackgroundSelected").GetComponent<Image>().color;
        }

        private static void Strip(GameObject go, bool keepLabels)
        {
            for (int pass = 0; pass < 2; pass++)
                foreach (var c in go.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    string n = c.GetIl2CppType().Name;
                    if (n.EndsWith("Binding") || n.EndsWith("ViewModel") || n.StartsWith("ParameterNodeViewModel") || (n == "LocalisedStaticLabel" && !keepLabels)
                        || n == "TweenDropDown" || n == "UICustomToggle" || n == "LevelEditorTweenOnToggleSubMenu" || n == "LevelEditor_UIButtonClickHandler"
                        || n == "LevelEditorTransformPunchScale" || n == "UITweenTriggerComponent" || n == "UITweenData" || n == "EventTrigger"
                        || n == "ScreenFaderComponent" || n == "CanvasGroupFader" || n == "TweenOnPopup" || n == "TMPUrlLinkHandler"
                        || n == "OnInputKeyDownClickUIButton" || n == "NavigationPromptButtonController" || n.StartsWith("LevelEditorColourPicker")
                        || n == "TweenOnToggleSubMenu" || n == "TweenOnTMP_InputField" || n == "UICustomTMP_InputField")
                        Object.DestroyImmediate(c);
                }
        }

        internal static GameObject Clone(GameObject template, Transform parent, bool keepLabels = false)
        {
            var holder = new GameObject("BettrFG_CloneHolder");
            holder.SetActive(false);
            var clone = Object.Instantiate(template, holder.transform, false);
            Strip(clone, keepLabels);
            clone.transform.SetParent(parent, false);
            Object.Destroy(holder);
            return clone;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        internal static RectTransform TopRight(GameObject go, float y)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(-290f, y);
            return rt;
        }

        internal static void Trigger(GameObject go, EventTriggerType type, Action<BaseEventData> act)
        {
            var tr = go.GetComponent<EventTrigger>();
            if (tr == null) tr = go.AddComponent<EventTrigger>();
            var e = new EventTrigger.Entry { eventID = type };
            e.callback.AddListener(act);
            tr.triggers.Add(e);
        }

        private static Image Img(string name, Transform parent, Sprite sprite, Color colour, bool sliced)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var im = go.AddComponent<Image>();
            im.sprite = sprite;
            im.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            im.color = colour;
            im.raycastTarget = false;
            return im;
        }

        private static void BuildShell()
        {
            _root = new GameObject("BettrFG_BatchEdit");
            var root = _root.AddComponent<RectTransform>();
            root.SetParent(GameObject.Find("UICanvas_Client_V2(Clone)/Popup").transform, false);
            Stretch(root);

            var info = Clone(_infoT.gameObject, root);
            info.SetActive(true);
            TopRight(info, -170f);
            foreach (var t in info.GetComponentsInChildren<Transform>(true))
                if (t.name == "ParametersHintShow") t.gameObject.SetActive(false);
            foreach (var tmp in info.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (tmp.name == "Tag_Text_line") tmp.text = L("batchedit.title").ToUpperInvariant();
                else if (tmp.name == "Descriptive Text") tmp.text = "";
            }
            info.transform.Find("ContentHolder/Title/Object Cost").gameObject.SetActive(false);

            var mask = new GameObject("Mask").AddComponent<RectTransform>();
            mask.SetParent(root, false);
            Stretch(mask);
            mask.gameObject.AddComponent<RectMask2D>().padding = new Vector4(-50f, 91.5f, 0f, MaskTop);

            var panel = Clone(_panelT.gameObject, mask);
            panel.SetActive(true);
            _panel = TopRight(panel, -(MaskTop - CornerHide));
            _scroll = panel.GetComponent<ScrollRect>();
            _scroll.vertical = true;
            _scroll.horizontal = false;
            _scroll.verticalScrollbar = null;
            _content = panel.transform.Find("Content").GetComponent<RectTransform>();
            for (int i = _content.childCount - 1; i >= 0; i--) Object.DestroyImmediate(_content.GetChild(i).gameObject);

            var vlg = _content.GetComponent<VerticalLayoutGroup>();
            vlg.padding.top = (int)PadTop;
            float contentLeft = _content.anchoredPosition.x - _content.rect.width * _content.pivot.x;
            _innerW = _panel.rect.width - Edge * 2f;
            vlg.padding.left = (int)(Edge - contentLeft);
            vlg.padding.right = (int)(_content.rect.width - (Edge + _innerW - contentLeft));
            vlg.spacing = 8f;
        }

        private static void Rebuild(int keep)
        {
            if (_editing != null) FinishEdit(false);
            for (int i = _content.childCount - 1; i >= 0; i--) Object.DestroyImmediate(_content.GetChild(i).gameObject);
            _rows.Clear();
            _rowParent = _content;
            _sel = -1;

            AddHeader();
            switch (_tab)
            {
                case 0: BuildRecolour(); break;
                case 1: BuildScale(); break;
                case 2: BuildMaterial(); break;
                case 3: BuildPhysics(); break;
                case 4: BuildLink(); break;
                case 5: BuildGroup(); break;
            }
            AddActions(_tab <= 1 ? "batchedit.apply" : null);

            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            var cols = Vector2.zero;
            foreach (var r in _rows)
                if (r.Measure != null)
                {
                    var m = r.Measure();
                    cols = Vector2.Max(cols, new Vector2(Mathf.Ceil(m.x), Mathf.Ceil(m.y)));
                }
            foreach (var r in _rows) r.Fit?.Invoke(cols);

            _panel.sizeDelta = new Vector2(_panel.sizeDelta.x, Mathf.Min(_content.rect.height, MaxPanelHeight));
            _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, 0f);
            _scrollT = 1f;

            int pick = Mathf.Clamp(keep, 0, _rows.Count - 1);
            while (pick > 0 && _rows[pick].Passive) pick--;
            Select(pick, false);
        }

        private static Row NewRow(Transform template, float height)
        {
            var go = Clone(template.gameObject, _rowParent);
            go.SetActive(true);
            var r = new Row { Rt = go.GetComponent<RectTransform>() };
            r.Rt.sizeDelta = new Vector2(_innerW, height);
            foreach (var g in go.GetComponentsInChildren<CanvasGroup>(true)) g.alpha = 1f;
            foreach (var d in go.GetComponentsInChildren<Transform>(true))
                if (d.name == "BackgroundDisabled" || d.name == "ParamValueDisabled" || d.name.EndsWith("ArrowDisabled") || d.name == "NavPrompt_Report")
                    d.gameObject.SetActive(false);
            r.On = go.transform.Find("BackgroundSelected").gameObject;
            r.Off = go.transform.Find("Background").gameObject;
            r.Title = go.transform.Find("ParamTitle").GetComponent<TextMeshProUGUI>();
            var value = go.transform.Find("ParamValue");
            if (value != null) r.Value = value.GetComponent<TextMeshProUGUI>();
            r.Paint = on => { r.On.SetActive(on); r.Off.SetActive(!on); };
            r.Paint(false);
            Trigger(go, EventTriggerType.PointerEnter, _ => { if (!r.Passive && _editing == null) Select(_rows.IndexOf(r), true); });
            Trigger(go, EventTriggerType.PointerClick, _ => Click(r));
            _rows.Add(r);
            return r;
        }

        private static void SetTitle(Row r, string key)
        {
            r.Title.text = L(key);
            r.Title.enableWordWrapping = false;
        }

        private static void WireArrows(Row r)
        {
            foreach (var name in new[] { "SettingsLeftArrow", "SettingsRightArrow" })
            {
                int d = name == "SettingsLeftArrow" ? -1 : 1;
                Trigger(r.Rt.Find(name).gameObject, EventTriggerType.PointerClick, _ =>
                {
                    if (_editing != null) return;
                    Select(_rows.IndexOf(r), false);
                    r.Step?.Invoke(d);
                    r.Release?.Invoke();
                });
            }
        }

        private static void HideArrows(Row r)
        {
            r.Rt.Find("SettingsLeftArrow").gameObject.SetActive(false);
            r.Rt.Find("SettingsRightArrow").gameObject.SetActive(false);
        }

        private static void AddHeader()
        {
            var r = NewRow(_choiceT, 50f);
            r.On.SetActive(false);
            r.Off.SetActive(false);
            r.Title.gameObject.SetActive(false);
            var v = r.Value.rectTransform;
            v.anchorMin = Vector2.zero;
            v.anchorMax = Vector2.one;
            v.offsetMin = new Vector2(70f, 0f);
            v.offsetMax = new Vector2(-70f, 0f);
            r.Value.alignment = TextAlignmentOptions.Center;
            r.Value.enableWordWrapping = false;
            r.Value.text = L(TabKeys[_tab]).ToUpperInvariant();
            foreach (var (name, x) in new[] { ("SettingsLeftArrow", 0f), ("SettingsRightArrow", 1f) })
            {
                var a = r.Rt.Find(name).GetComponent<RectTransform>();
                a.anchorMin = a.anchorMax = new Vector2(x, 0.5f);
                a.pivot = new Vector2(0.5f, 0.5f);
                a.anchoredPosition = new Vector2(x == 0f ? 34f : -34f, 0f);
                a.sizeDelta = new Vector2(60f, 40f);
            }
            r.Paint = on => r.Value.color = on ? _pink : Color.white;
            r.Paint(false);
            r.Step = d =>
            {
                CommitPending();
                _tab = (_tab + d + TabKeys.Length) % TabKeys.Length;
                Sound(AudioManager.EventMasterData?.CreativeModeLibraryTab);
                Rebuild(0);
            };
            WireArrows(r);
        }

        private static Row AddChoice(string key, string[] options, int start, Action<int> changed)
        {
            var r = NewRow(_choiceT, 72f);
            SetTitle(r, key);
            int cur = Mathf.Clamp(start, 0, options.Length - 1);
            r.Value.text = options[cur];
            r.Step = d =>
            {
                cur = (cur + d + options.Length) % options.Length;
                r.Value.text = options[cur];
                Sound(AudioManager.EventMasterData?.CreativeModeLibraryTab);
                changed(cur);
            };
            r.Repeat = 0.22f;
            WireArrows(r);
            return r;
        }

        private static string[] Options(params string[] keys)
        {
            var o = new string[keys.Length];
            for (int i = 0; i < keys.Length; i++) o[i] = L(keys[i]);
            return o;
        }

        private static Row AddButton(string key, Sprite icon, Action pressed)
        {
            var r = NewRow(_buttonT, 72f);
            SetTitle(r, key);
            r.Title.alignment = TextAlignmentOptions.Center;
            AddIcon(r, icon);
            r.Confirm = pressed;
            return r;
        }

        private static void AddIcon(Row r, Sprite icon)
        {
            var im = Img("Icon", r.Rt, icon, Color.white, false);
            im.preserveAspect = true;
            var rt = im.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(40f, 40f);
            rt.anchoredPosition = new Vector2(44f, 0f);
        }

        private static Row AddInfo(string text)
        {
            var r = NewRow(_buttonT, 44f);
            r.Passive = true;
            r.On.SetActive(false);
            r.Off.SetActive(false);
            r.Paint = _ => { };
            var t = r.Title;
            t.text = text;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.color = new Color(1f, 1f, 1f, 0.7f);
            t.enableAutoSizing = false;
            t.fontSize = 22f;
            t.enableWordWrapping = true;
            var trt = t.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(16f, 0f);
            trt.offsetMax = new Vector2(-16f, 0f);
            r.Rt.sizeDelta = new Vector2(_innerW, Mathf.Max(44f, t.GetPreferredValues(text, _innerW - 32f, 0f).y + 16f));
            return r;
        }

        private static Row AddText(string key, Func<string> get, Action<string> set)
        {
            var r = NewRow(_choiceT, 72f);
            SetTitle(r, key);
            HideArrows(r);
            r.Value.text = get();
            r.Confirm = () => BeginEdit(r, get(), s => { set(s); r.Value.text = get(); });
            return r;
        }

        private static void BeginGroup()
        {
            var g = new GameObject("Group").AddComponent<RectTransform>();
            g.SetParent(_content, false);
            g.sizeDelta = new Vector2(_innerW, 0f);
            var bg = new GameObject("Bg").AddComponent<RectTransform>();
            bg.SetParent(g, false);
            Stretch(bg);
            bg.offsetMin = new Vector2(-6f, 0f);
            bg.offsetMax = new Vector2(6f, 0f);
            bg.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
            var img = bg.gameObject.AddComponent<Image>();
            img.sprite = _choiceT.Find("Background").GetComponent<Image>().sprite;
            img.type = Image.Type.Sliced;
            img.color = new Color(0f, 0f, 0f, 0.25f);
            img.raycastTarget = false;
            var gl = g.gameObject.AddComponent<VerticalLayoutGroup>();
            gl.padding = new RectOffset(10, 10, 4, 10);
            gl.spacing = 2f;
            gl.childControlWidth = true;
            gl.childControlHeight = false;
            gl.childForceExpandWidth = true;
            gl.childForceExpandHeight = false;
            g.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _rowParent = g;
        }

        private static void EndGroup() => _rowParent = _content;

        private static void AddGroupHeader(string key)
        {
            var r = AddInfo(L(key));
            r.Title.color = new Color(1f, 1f, 1f, 0.85f);
            r.Title.fontSize = 24f;
            r.Title.fontStyle = FontStyles.Bold;
            r.Title.rectTransform.offsetMin = new Vector2(18f, 0f);
            r.Rt.sizeDelta = new Vector2(_innerW, 40f);
        }

        private static void AddSlider(string key, float start, Color tint, Func<float, string> label, Action<float> changed)
        {
            var r = NewRow(_choiceT, 40f);
            SetTitle(r, key);
            HideArrows(r);
            r.On.SetActive(false);
            r.Off.SetActive(false);

            var title = r.Title;
            var trt = title.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = new Vector2(0f, 1f);
            trt.pivot = new Vector2(0f, 0.5f);
            title.alignment = TextAlignmentOptions.MidlineLeft;
            title.enableAutoSizing = false;
            title.fontSize = SliderFont;

            var area = new GameObject("SliderArea").AddComponent<RectTransform>();
            area.SetParent(r.Rt, false);
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.gameObject.AddComponent<Image>().color = Color.clear;

            var outline = Img("TrackOutline", area, _smallCircle, Color.white, true).rectTransform;
            outline.anchorMin = new Vector2(0f, 0.5f);
            outline.anchorMax = new Vector2(1f, 0.5f);
            outline.sizeDelta = new Vector2(0f, 26f);
            outline.GetComponent<Image>().raycastTarget = true;
            var inner = Img("Track", outline, _smallCircle, Dark, true).rectTransform;
            Stretch(inner);
            inner.offsetMin = new Vector2(3f, 3f);
            inner.offsetMax = new Vector2(-3f, -3f);
            var fill = Img("Fill", inner, _smallCircle, tint, true).rectTransform;
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0f, 1f);
            fill.pivot = new Vector2(0f, 0.5f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;

            var handle = new GameObject("Handle").AddComponent<RectTransform>();
            handle.SetParent(outline, false);
            handle.anchorMin = handle.anchorMax = new Vector2(0f, 0.5f);
            handle.pivot = new Vector2(0.5f, 0.5f);
            handle.sizeDelta = new Vector2(36f, 36f);
            var ring = Img("Ring", handle, _bigCircle, Color.white, false);
            Stretch(ring.rectTransform);
            var knob = Img("Knob", handle, _bigCircle, tint, false);
            Stretch(knob.rectTransform);
            knob.rectTransform.offsetMin = new Vector2(4f, 4f);
            knob.rectTransform.offsetMax = new Vector2(-4f, -4f);

            var v = r.Value;
            var vrt = v.rectTransform;
            vrt.anchorMin = new Vector2(1f, 0f);
            vrt.anchorMax = Vector2.one;
            v.alignment = TextAlignmentOptions.Center;
            v.enableWordWrapping = false;
            v.raycastTarget = false;
            v.enableAutoSizing = false;
            v.fontSize = SliderFont;

            float cur = Mathf.Clamp01(start);
            void Place()
            {
                float w = outline.rect.width, rad = 18f;
                float hx = Mathf.Lerp(rad, Mathf.Max(rad, w - rad), cur);
                handle.anchoredPosition = new Vector2(hx, 0f);
                fill.sizeDelta = new Vector2(Mathf.Max(0f, hx - 3f), 0f);
                v.text = label(cur);
            }
            void Set(float x)
            {
                cur = Mathf.Clamp01(x);
                Place();
                changed(cur);
            }

            r.Paint = on =>
            {
                var c = on ? _pink : Color.white;
                title.color = c;
                v.color = c;
                ring.color = c;
                handle.localScale = on ? new Vector3(1.18f, 1.18f, 1f) : Vector3.one;
            };
            r.Paint(false);
            r.Slide = delta => Set(cur + delta);
            r.Measure = () => new Vector2(title.GetPreferredValues(title.text).x,
                Mathf.Max(v.GetPreferredValues(label(0f)).x, Mathf.Max(v.GetPreferredValues(label(0.5f)).x, v.GetPreferredValues(label(1f)).x)));
            r.Fit = cols =>
            {
                trt.offsetMin = new Vector2(18f, 0f);
                trt.offsetMax = new Vector2(18f + cols.x, 0f);
                vrt.offsetMin = new Vector2(-8f - cols.y, 0f);
                vrt.offsetMax = new Vector2(-8f, 0f);
                area.offsetMin = new Vector2(18f + cols.x + 12f, 0f);
                area.offsetMax = new Vector2(-8f - cols.y - 6f, 0f);
                Place();
            };

            Action<BaseEventData> drag = e =>
            {
                var pe = e.TryCast<PointerEventData>();
                if (pe == null || _editing != null) return;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(outline, pe.position, pe.pressEventCamera, out var local)) return;
                Select(_rows.IndexOf(r), false);
                Set((local.x - outline.rect.xMin - 18f) / Mathf.Max(1f, outline.rect.width - 36f));
            };
            Trigger(outline.gameObject, EventTriggerType.PointerDown, drag);
            Trigger(outline.gameObject, EventTriggerType.Drag, drag);
        }

        private static Image AddColourParam()
        {
            var r = NewRow(_colourT, 72f);
            SetTitle(r, "batchedit.colour");
            var sw = r.Rt.Find("UI_ColorPickerSwatch_Prefab");
            sw.gameObject.SetActive(true);
            var swatch = sw.GetComponent<Image>();
            swatch.color = _colour;
            sw.Find("NormalBorder").gameObject.SetActive(true);
            sw.Find("SelectedBorder").gameObject.SetActive(false);

            r.Rt.Find("Prompt/Title").GetComponent<TextMeshProUGUI>().text = L("batchedit.edit");
            var prompt = r.Rt.Find("Prompt").GetComponent<RectTransform>();
            prompt.anchorMin = prompt.anchorMax = new Vector2(1f, 0.5f);
            prompt.pivot = new Vector2(1f, 0.5f);
            prompt.sizeDelta = new Vector2(110f, 40f);
            prompt.anchoredPosition = new Vector2(-16f, 0f);
            prompt.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleRight;

            var basePaint = r.Paint;
            r.Paint = on => { basePaint(on); prompt.gameObject.SetActive(on); };
            r.Paint(false);
            r.Confirm = () => BatchColourPicker.Open(_root.transform, _pickerT, _swatchT, _colour, c =>
            {
                _colour = c;
                swatch.color = c;
                PreviewColourSet();
            }, () =>
            {
                _editEnd = Time.frameCount;
                Rebuild(_sel);
            });
            return swatch;
        }

        private static void AddRecent(Image swatch)
        {
            var hexes = LevelEditorColourPaletteSettings.GetFavouriteCustomColoursHexCodes();
            var colours = new List<Color>();
            for (int i = 0; hexes != null && i < hexes.Length && colours.Count < 5; i++)
                if (UGUIShip.HexToColor(hexes[i], out float cr, out float cg, out float cb)) colours.Add(new Color(cr, cg, cb, 1f));
            if (colours.Count == 0) return;

            var r = NewRow(_choiceT, 40f);
            HideArrows(r);
            r.On.SetActive(false);
            r.Off.SetActive(false);
            SetTitle(r, "batchedit.recent");
            var title = r.Title;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            var trt = title.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = new Vector2(0.3f, 1f);
            trt.pivot = new Vector2(0f, 0.5f);
            trt.offsetMin = new Vector2(27f, 0f);
            trt.offsetMax = Vector2.zero;
            r.Value.gameObject.SetActive(false);

            const float size = 30f, gap = 8f;
            var strip = new GameObject("Swatches").AddComponent<RectTransform>();
            strip.SetParent(r.Rt, false);
            strip.anchorMin = strip.anchorMax = new Vector2(1f, 0.5f);
            strip.pivot = new Vector2(1f, 0.5f);
            strip.anchoredPosition = new Vector2(-16f, 0f);
            strip.sizeDelta = new Vector2(colours.Count * size + (colours.Count - 1) * gap, size);

            var frames = new List<Image>();
            int hi = 0;
            bool lit = false;
            void Paint()
            {
                for (int k = 0; k < frames.Count; k++)
                {
                    bool on = lit && k == hi;
                    frames[k].color = on ? _pink : Color.white;
                    frames[k].transform.localScale = on ? new Vector3(1.15f, 1.15f, 1f) : Vector3.one;
                }
                title.color = lit ? _pink : Color.white;
            }
            void Pick()
            {
                _colour = colours[hi];
                swatch.color = _colour;
                PreviewColourSet();
            }

            for (int k = 0; k < colours.Count; k++)
            {
                var frame = Img("Swatch" + k, strip, _smallCircle, Color.white, true);
                frame.raycastTarget = true;
                frame.pixelsPerUnitMultiplier = 2.2f;
                var h = frame.rectTransform;
                h.anchorMin = h.anchorMax = new Vector2(0f, 0.5f);
                h.sizeDelta = new Vector2(size, size);
                h.anchoredPosition = new Vector2(size * 0.5f + k * (size + gap), 0f);
                var fill = Img("Colour", h, _smallCircle, colours[k], true);
                fill.pixelsPerUnitMultiplier = 2.8f;
                Stretch(fill.rectTransform);
                fill.rectTransform.offsetMin = new Vector2(3f, 3f);
                fill.rectTransform.offsetMax = new Vector2(-3f, -3f);
                frames.Add(frame);
                int kk = k;
                Trigger(h.gameObject, EventTriggerType.PointerClick, _ =>
                {
                    if (_editing != null) return;
                    Select(_rows.IndexOf(r), false);
                    hi = kk;
                    Paint();
                    Sound(AudioManager.EventMasterData?.CreativeModeItemEditConfirm);
                    Pick();
                });
            }

            r.Paint = on => { lit = on; Paint(); };
            r.Paint(false);
            r.Step = d =>
            {
                hi = Mathf.Clamp(hi + d, 0, colours.Count - 1);
                Paint();
                Sound(AudioManager.EventMasterData?.CreativeModeLibraryTab);
            };
            r.Repeat = 0.2f;
            r.Confirm = Pick;
        }

        private static void AddActions(string primaryKey)
        {
            var r = NewRow(_buttonT, 72f);
            const float d = 64f, gap = 10f;
            float side = d * 2f + gap * 2f;
            bool primary = primaryKey != null;
            var offCol = r.Off.GetComponent<Image>().color;
            var onCol = r.On.GetComponent<Image>().color;
            var offLine = r.Off.transform.Find("BackgroundOutline").GetComponent<Image>().color;
            var onLine = r.On.transform.Find("BackgroundSelectedOutline").GetComponent<Image>().color;

            if (primary)
            {
                SetTitle(r, primaryKey);
                r.Title.alignment = TextAlignmentOptions.Center;
                AddIcon(r, _tickIcon);
                foreach (var b in new[] { r.On, r.Off })
                {
                    var brt = b.GetComponent<RectTransform>();
                    brt.offsetMax = new Vector2(-side, brt.offsetMax.y);
                }
                r.Title.rectTransform.offsetMax = new Vector2(r.Title.rectTransform.offsetMax.x - side, r.Title.rectTransform.offsetMax.y);
            }
            else
            {
                r.On.SetActive(false);
                r.Off.SetActive(false);
                r.Title.gameObject.SetActive(false);
            }

            int first = primary ? 0 : 1;
            _act = Mathf.Max(_act, first);
            bool lit = false;
            var rings = new Image[3];
            var fills = new Image[3];
            void Paint()
            {
                if (primary)
                {
                    bool on = lit && _act == 0;
                    r.On.SetActive(on);
                    r.Off.SetActive(!on);
                }
                for (int k = 1; k < 3; k++)
                {
                    bool on = lit && _act == k;
                    fills[k].color = on ? onCol : offCol;
                    rings[k].color = on ? onLine : offLine;
                    rings[k].transform.localScale = on ? new Vector3(1.08f, 1.08f, 1f) : Vector3.one;
                }
            }
            void Hover(int k)
            {
                if (_editing != null) return;
                bool changed = _act != k;
                _act = k;
                if (_rows[_sel] == r)
                {
                    Paint();
                    if (changed) Sound(AudioManager.EventMasterData?.CreativeModeLibraryTab);
                }
                else Select(_rows.IndexOf(r), true);
            }
            void Hit(int k)
            {
                if (_editing != null) return;
                _act = k;
                if (_rows[_sel] != r) Select(_rows.IndexOf(r), false);
                else Paint();
                Press(r);
            }

            for (int k = 1; k < 3; k++)
            {
                var ring = Img(k == 1 ? "Undo" : "Redo", r.Rt, _bigCircle, offLine, false);
                ring.raycastTarget = true;
                var h = ring.rectTransform;
                h.sizeDelta = new Vector2(d, d);
                if (primary)
                {
                    h.anchorMin = h.anchorMax = new Vector2(1f, 0.5f);
                    h.anchoredPosition = new Vector2(-(d * 0.5f) - (2 - k) * (d + gap), 0f);
                }
                else
                {
                    h.anchorMin = h.anchorMax = new Vector2(0.5f, 0.5f);
                    h.anchoredPosition = new Vector2((k == 1 ? -1f : 1f) * (d + gap) * 0.5f, 0f);
                }
                var fill = Img("Fill", h, _bigCircle, offCol, false);
                Stretch(fill.rectTransform);
                fill.rectTransform.offsetMin = new Vector2(3f, 3f);
                fill.rectTransform.offsetMax = new Vector2(-3f, -3f);
                var icon = Img("Icon", h, _revert, Color.white, false);
                icon.preserveAspect = true;
                icon.rectTransform.sizeDelta = new Vector2(38f, 38f);
                if (k == 2) icon.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
                rings[k] = ring;
                fills[k] = fill;
                int kk = k;
                Trigger(h.gameObject, EventTriggerType.PointerEnter, _ => Hover(kk));
                Trigger(h.gameObject, EventTriggerType.PointerClick, _ => Hit(kk));
            }
            if (primary)
                foreach (var b in new[] { r.On, r.Off })
                {
                    Trigger(b, EventTriggerType.PointerEnter, _ => Hover(0));
                    Trigger(b, EventTriggerType.PointerClick, _ => Hit(0));
                }

            r.Paint = on => { lit = on; Paint(); };
            r.Paint(false);
            r.Step = dd =>
            {
                int n = Mathf.Clamp(_act + dd, first, 2);
                if (n == _act) return;
                _act = n;
                Paint();
                Sound(AudioManager.EventMasterData?.CreativeModeLibraryTab);
            };
            r.Confirm = () =>
            {
                if (_act == 0 && _tab == 0) ApplyColour();
                else if (_act == 0) ApplyScaleNow();
                else if (_act == 1) DoUndo();
                else DoRedo();
            };
        }

        private static void BeginEdit(Row r, string text, Action<string> done)
        {
            var f = Clone(_modalT.transform.Find("Content/InputTextArea/InputField").gameObject, r.Rt);
            f.SetActive(true);
            var frt = f.GetComponent<RectTransform>();
            var vrt = r.Value.rectTransform;
            frt.anchorMin = vrt.anchorMin;
            frt.anchorMax = vrt.anchorMax;
            frt.pivot = vrt.pivot;
            frt.anchoredPosition = vrt.anchoredPosition;
            frt.sizeDelta = new Vector2(Mathf.Max(vrt.rect.width, 180f), Mathf.Min(52f, r.Rt.rect.height - 4f));
            _editWasOn = r.Value.gameObject.activeSelf;
            r.Value.gameObject.SetActive(false);
            _editing = f.GetComponent<TMP_InputField>();
            _editRow = r;
            _editDone = done;
            _editing.text = text;
            _editing.onSubmit.AddListener(new Action<string>(_ => FinishEdit(true)));
            EventSystem.current.SetSelectedGameObject(f);
            _editing.ActivateInputField();
        }

        private static void FinishEdit(bool commit)
        {
            if (_editing == null) return;
            var done = _editDone;
            string value = _editing.text;
            var r = _editRow;
            Object.Destroy(_editing.gameObject);
            _editing = null;
            _editDone = null;
            _editRow = null;
            _editEnd = Time.frameCount;
            r.Value.gameObject.SetActive(_editWasOn);
            EventSystem.current.SetSelectedGameObject(null);
            if (commit) done(value);
        }

        private static string Signed(float x)
        {
            int p = Mathf.RoundToInt((x - 0.5f) * 200f);
            return (p > 0 ? "+" : "") + p + "%";
        }

        private static string Degrees(float x) => Mathf.RoundToInt(x * 360f) + "°";

        private static void BuildRecolour()
        {
            AddChoice("batchedit.mode", Options("batchedit.set_colour", "batchedit.modify"), (int)_recolourMode, i =>
            {
                CommitColourEntry();
                _recolourMode = (RecolourMode)i;
                Rebuild(1);
            });

            if (_recolourMode == RecolourMode.SetColour)
            {
                AddRecent(AddColourParam());
                return;
            }

            BeginGroup();
            AddGroupHeader("batchedit.adjust");
            AddSlider("batchedit.brightness", _modBright * 0.5f + 0.5f, new Color(0.9f, 0.9f, 0.6f), Signed, v => { _modBright = (v - 0.5f) * 2f; PreviewColourModify(); });
            AddSlider("batchedit.contrast", _modContrast * 0.5f + 0.5f, new Color(0.75f, 0.75f, 0.75f), Signed, v => { _modContrast = (v - 0.5f) * 2f; PreviewColourModify(); });
            AddSlider("batchedit.hue", _modHue / 360f, new Color(0.8f, 0.5f, 0.9f), Degrees, v => { _modHue = v * 360f; PreviewColourModify(); });
            AddSlider("batchedit.saturation", _modSat * 0.5f + 0.5f, new Color(0.5f, 0.9f, 0.7f), Signed, v => { _modSat = (v - 0.5f) * 2f; PreviewColourModify(); });
            EndGroup();
        }

        private static void BuildScale()
        {
            float step = CreativeIncrements.Enabled ? CreativeIncrements.Step : 0.25f;
            float repeat = CreativeIncrements.Enabled ? CreativeIncrements.Speed : 0.06f;
            for (int i = 0; i < 3; i++)
            {
                int axis = i;
                var r = NewRow(_choiceT, 72f);
                SetTitle(r, AxisKeys[axis]);
                r.Value.text = FactorText(_factors[axis]);
                r.Repeat = repeat;
                r.Step = d =>
                {
                    _factors[axis] = Mathf.Clamp(_factors[axis] + d * step, -FactorLimit, FactorLimit);
                    r.Value.text = FactorText(_factors[axis]);
                    ApplyScale();
                };
                r.Confirm = () => BeginEdit(r, FactorText(_factors[axis]), s =>
                {
                    if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    {
                        _factors[axis] = Mathf.Clamp(v, -FactorLimit, FactorLimit);
                        ApplyScale();
                    }
                    r.Value.text = FactorText(_factors[axis]);
                });
                WireArrows(r);
            }

            AddChoice("batchedit.mode", Options(ScaleModeKeys), (int)_scaleMode, i =>
            {
                ApplyScaleNow();
                _scaleMode = (ScaleMode)i;
                Rebuild(4);
            });
        }

        private static void ApplyScaleNow()
        {
            bool had = _scaleEntry != null;
            CommitScaleEntry();
            BatchScale.BakeOwnerScale();
            ResetFactors();
            if (had) Plugin.Log.LogInfo($"batch scale applied, {_scaleMode}");
            Rebuild(_sel);
        }

        private static void BuildMaterial()
        {
            AddChoice("batchedit.surface", Options("batchedit.unchanged", "batchedit.slime", "batchedit.none"), 0, i =>
            {
                if (i == 1) BatchMaterial.SetSlime();
                else if (i == 2) BatchMaterial.SetNone();
            });
            AddChoice("batchedit.visibility", Options("batchedit.unchanged", "batchedit.visible", "batchedit.invisible"), 0, i =>
            {
                if (i > 0) BatchVisibility.SetVisible(i == 1);
            });
            AddChoice("batchedit.collision", Options("batchedit.unchanged", "batchedit.on", "batchedit.off"), 0, i =>
            {
                if (i > 0) BatchCollision.SetCollisionEnabled(i == 1);
            });
            if (!BatchMaterial.AnySticker()) return;
            AddChoice("batchedit.sticker_lighting", Options("batchedit.unchanged", "batchedit.lit", "batchedit.unlit"), 0, i =>
            {
                if (i > 0) BatchMaterial.SetLighting(i == 1);
            });
        }

        private static void BuildPhysics()
        {
            if (!BatchPhysics.AnyPhysics())
            {
                AddInfo(L("batchedit.no_physics"));
                return;
            }
            AddChoice("batchedit.physics", Options("batchedit.unchanged", "batchedit.on", "batchedit.off"), 0, i =>
            {
                if (i > 0) BatchPhysics.SetPhysicsEnabled(i == 1);
            });

            var weights = BatchPhysics.WeightNames();
            if (weights.Length > 0)
            {
                var options = new string[weights.Length + 1];
                options[0] = L("batchedit.unchanged");
                Array.Copy(weights, 0, options, 1, weights.Length);
                AddChoice("batchedit.weight", options, 0, i =>
                {
                    if (i > 0) BatchPhysics.SetWeight(i - 1);
                });
            }

            if (!BatchPhysics.AnyDraggable()) return;
            AddChoice("batchedit.grabbable", Options("batchedit.unchanged", "batchedit.on", "batchedit.off"), 0, i =>
            {
                if (i > 0) BatchPhysics.SetDraggable(i == 1);
            });
        }

        private static void BuildLink()
        {
            var controller = BatchLink.Controller(out int controllers);
            if (controller == null)
            {
                AddInfo(L("batchedit.no_controller"));
                return;
            }

            AddInfo(L(BatchLink.TypeName(controller) == "rotation" ? "batchedit.rotation_controller" : "batchedit.movement_controller"));
            if (controllers > 1) AddInfo(string.Format(L("batchedit.many_controllers"), controllers));
            BatchLink.Survey(controller, out int receivers, out int linked, out int slotsFree);
            AddInfo(slotsFree < 0
                ? string.Format(L("batchedit.linked_count"), linked, receivers)
                : string.Format(L("batchedit.linked_count_slots"), linked, receivers, slotsFree));

            AddButton("batchedit.link_all", _linkIcon, () =>
            {
                var entry = BatchEditHistory.Begin("link");
                BatchLink.LinkAll(entry, BatchLink.Controller(), out string note);
                BatchEditHistory.Push(entry);
                if (note != null) Plugin.Log.LogInfo($"batch link: {note}");
                Rebuild(_sel);
            });
            AddButton("batchedit.unlink_all", _unlinkIcon, () =>
            {
                var entry = BatchEditHistory.Begin("unlink");
                BatchLink.UnlinkAll(entry, BatchLink.Controller());
                BatchEditHistory.Push(entry);
                Rebuild(_sel);
            });
        }

        private static void BuildGroup()
        {
            int current = Groups.SelectionGroupId();
            if (current != _groupPick)
            {
                _groupPick = current;
                _groupName = current != 0 ? Groups.NameOf(current) : L("batchedit.default_group_name");
            }

            AddText("batchedit.name", () => _groupName, s => { if (s.Trim().Length > 0) _groupName = s.Trim(); });
            AddButton("batchedit.link", _linkIcon, () =>
            {
                int n = Groups.LinkSelection(_groupPick, _groupName, out int landed);
                Plugin.Log.LogInfo($"{n} into group {landed} from batch edit");
                Rebuild(_sel);
            });
            AddButton("batchedit.unlink", _unlinkIcon, () =>
            {
                Groups.UnlinkSelection();
                Rebuild(_sel);
            });
            AddButton("batchedit.save_to_file", _saveIcon, () =>
            {
                SavedGroups.Save(_groupName, out string note);
                Plugin.Log.LogInfo($"saved '{_groupName}' out of batch edit: {note}");
            });
        }

        private static string FactorText(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        private static void ResetFactors()
        {
            for (int i = 0; i < 3; i++)
            {
                _factors[i] = 1f;
                _committed[i] = 1f;
            }
        }

        private static void ApplyScale()
        {
            _scaleEntry ??= BatchEditHistory.Begin("scale");
            var offset = _scaleMode == ScaleMode.Individual
                ? new Vector3(_factors[0] - _committed[0], _factors[1] - _committed[1], _factors[2] - _committed[2])
                : new Vector3(_factors[0] - 1f, _factors[1] - 1f, _factors[2] - 1f);
            BatchScale.ApplyInto(_scaleEntry, offset, _scaleMode);
        }

        private static void CommitScaleEntry()
        {
            if (_scaleEntry == null) return;
            BatchEditHistory.Push(_scaleEntry);
            _scaleEntry = null;
            for (int i = 0; i < 3; i++) _committed[i] = _factors[i];
        }

        private static void EnsureColourSession()
        {
            if (_colourEntry != null) return;
            BatchRecolour.SnapshotOriginals(_colourOriginals);
            if (_colourOriginals.Count == 0) return;
            var entry = BatchEditHistory.Begin(_recolourMode == RecolourMode.Modify ? "modify colour" : "recolour");
            foreach (var kv in _colourOriginals)
                entry.Snaps.Add(new BatchEditHistory.ObjectSnap { Obj = kv.Key, Colour = kv.Value, Palette = BatchTargets.GetColourParam(kv.Key).CurrentPalette });
            _colourEntry = entry;
            _colourSessionSelCount = BatchRecolour.SelectionCount();
        }

        private static void ApplyColour()
        {
            if (_recolourMode == RecolourMode.SetColour) PreviewColourSet();
            else PreviewColourModify();
            CommitColourEntry();
        }

        private static void PreviewColourSet()
        {
            EnsureColourSession();
            BatchRecolour.SetPreview(_colourOriginals, _colour);
        }

        private static void PreviewColourModify()
        {
            EnsureColourSession();
            BatchRecolour.ModifyPreview(_colourOriginals, _modBright, _modContrast, _modHue, _modSat);
        }

        private static void CommitColourEntry()
        {
            if (_colourEntry == null) return;
            BatchEditHistory.Push(_colourEntry);
            _colourEntry = null;
            _colourOriginals.Clear();
            _modBright = _modContrast = _modHue = _modSat = 0f;
        }

        private static void CommitPending()
        {
            CommitScaleEntry();
            CommitColourEntry();
        }

        private static void DoUndo()
        {
            CommitPending();
            BatchScale.ResetOwnerScale();
            Plugin.Log.LogInfo(BatchEditHistory.Undo() ?? "batch undo, nothing left");
            BatchScale.ResyncHeldOffsets();
            ResetFactors();
            Rebuild(_sel);
        }

        private static void DoRedo()
        {
            CommitPending();
            BatchScale.ResetOwnerScale();
            Plugin.Log.LogInfo(BatchEditHistory.Redo() ?? "batch redo, nothing to redo");
            BatchScale.ResyncHeldOffsets();
            ResetFactors();
            Rebuild(_sel);
        }
    }
}
