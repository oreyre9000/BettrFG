using System;
using System.Collections.Generic;
using BetterFG.Utilities;
using FGClient;
using FGClient.UI.PrivateLobby;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MPG.Utility;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace BetterFG.Tweaks
{
    public class LobbyShowSearchTweak : BfgTweak
    {
        public LobbyShowSearchTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "lobby_show_search";
        public override string TweakLabel => "tweak.show_select_search_bar";
        public override bool DefaultEnabled => true;

        public static LobbyShowSearchTweak Instance { get; private set; }
        void Awake() => Instance = this;

        // shared with RandomShowSelectTweak - one copy, don't duplicate the path string.
        internal const string ContentPath = "Container/ShowsScrollList/ShowsListViewport/ShowsListContent";

        const string SearchName = "BFG_ShowSearch";
        const string PlaceholderId = "ui.search_shows";
        const float RowHeight = 62f;
        const float LabelSize = 28f;

        private static Sprite _searchIconSprite;
        private static Sprite SearchIcon => _searchIconSprite ??= EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.button.search.png");

        private PrivateLobbyShowListViewModel _vm;
        private Transform _content;
        private GameObject _enterCodeGo;
        private ShowSelectorCarrouselInputHandler _carrousel;
        private GameObject _searchGo;
        private TMP_Text _label;
        private TMP_InputField _field;
        private CanvasGroup _litGroup;
        private CanvasGroup _unlitGroup;
        private bool _typing;
        private int _typingFrame = -1;
        private IntPtr _optionsSeen = IntPtr.Zero;
        private readonly List<GameObject> _optionScratch = new List<GameObject>();

        internal static bool ShouldSuppressConfirm()
        {
            var inst = Instance;
            if (inst == null || !inst.IsEnabled || inst._searchGo == null) return false;
            if (inst._typing) return true;
            var es = EventSystem.current;
            return es != null && es.currentSelectedGameObject == inst._searchGo;
        }

        internal static void OnShowListAwake(PrivateLobbyShowListViewModel vm)
        {
            var inst = Instance;
            if (inst == null || !inst.IsEnabled) return;
            inst.Forget();
            inst._vm = vm;
        }

        private void Forget()
        {
            StopTyping();
            if (_field != null) BetterFG.UI.BetterFGUIMan.DropField(_field.gameObject);
            _vm = null;
            _content = null;
            _enterCodeGo = null;
            _carrousel = null;
            _searchGo = null;
            _label = null;
            _field = null;
            _litGroup = null;
            _unlitGroup = null;
            _optionsSeen = IntPtr.Zero;
        }

        private void Update()
        {
            if (_vm == null) { if (_searchGo != null || _typing) Forget(); return; }
            if (!_vm.gameObject.activeInHierarchy) { Forget(); return; }

            var ec = _vm._enterCodeButtonSelectable;
            if (ec == null) return;

            // the collection binding rebuilds the list on every tab switch and can take our clone
            // with it, so the bar is rebuilt from here rather than once on screen open.
            if (_searchGo == null)
            {
                StopTyping();
                _enterCodeGo = ec.gameObject;
                _content = ec.transform.parent;
                _carrousel = _content.GetComponent<ShowSelectorCarrouselInputHandler>();
                Build();
                RefilterRows();
                ApplyOptions(-1);
                return;
            }

            var opts = _carrousel._menuOptions;
            if (opts != null && opts.Pointer != _optionsSeen)
            {
                _optionsSeen = opts.Pointer;
                RefilterRows();
                ApplyOptions(-1);
            }

            if (!_typing) return;

            // the row itself is deselected the moment the hidden field takes the selection, so its
            // own tween settles to unlit. hold the lit panel while we're actually typing in it.
            if (_litGroup != null) { _litGroup.alpha = 1f; _unlitGroup.alpha = 0f; }

            if (Time.frameCount > _typingFrame && _field.readOnly) _field.readOnly = false;

            if (Time.frameCount <= _typingFrame + 1) return;

            var es = EventSystem.current;
            var sel = es == null ? null : es.currentSelectedGameObject;
            if (!_field.isFocused || sel != _field.gameObject) StopTyping();
        }

        private void Build()
        {
            var stale = _content.Find(SearchName);
            if (stale != null) UnityEngine.Object.DestroyImmediate(stale.gameObject);

            _searchGo = UnityEngine.Object.Instantiate(_enterCodeGo, _content);
            _searchGo.name = SearchName;
            _searchGo.transform.SetSiblingIndex(_enterCodeGo.transform.GetSiblingIndex() + 1);

            UnityEngine.Object.DestroyImmediate(_searchGo.GetComponent<EventTrigger>());
            UnityEngine.Object.DestroyImmediate(_searchGo.GetComponent<Mediatonic.Tools.MVVM.ActiveBinding>());

            var rt = _searchGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, RowHeight);

            var toggle = _searchGo.GetComponent<UICustomToggle>();
            toggle._navigationModeUp = UnityEngine.UI.Navigation.Mode.None;
            toggle._navigationModeDown = UnityEngine.UI.Navigation.Mode.None;
            toggle._navigationModeLeft = UnityEngine.UI.Navigation.Mode.None;
            toggle._navigationModeRight = UnityEngine.UI.Navigation.Mode.None;
            toggle.onValueChanged.RemoveAllListeners();
            toggle.group = null;
            toggle.isOn = false;

            var tween = _searchGo.GetComponent<TweenOnToggleSubMenu>();
            _litGroup = tween._activeToggleCanvasGroup;
            _unlitGroup = tween._inactiveToggleCanvasGroup;
            if (_litGroup == null || _unlitGroup == null) { _litGroup = null; _unlitGroup = null; }

            _searchGo.transform.Find("Content/ControlGlyphButton").gameObject.SetActive(false);
            _searchGo.transform.Find("Content/Icon").gameObject.SetActive(false);

            var labelGo = _searchGo.transform.Find("Content/Text");
            labelGo.GetComponent<LocalisedStaticLabel>().enabled = false;
            _label = labelGo.GetComponent<TextMeshProUGUI>();
            _label.fontSize = LabelSize;

            var iconSprite = SearchIcon;
            if (iconSprite != null)
            {
                float iconSize = LabelSize * 0.75f;
                float iconLeft = 2f;
                var iconGo = new GameObject("BFG_SearchIcon");
                iconGo.transform.SetParent(_searchGo.transform.Find("Content"), false);
                var iRt = iconGo.AddComponent<RectTransform>();
                iRt.anchorMin = new Vector2(0f, 0.5f);
                iRt.anchorMax = new Vector2(0f, 0.5f);
                iRt.pivot = new Vector2(0f, 0.5f);
                iRt.anchoredPosition = new Vector2(iconLeft, 0f);
                iRt.sizeDelta = new Vector2(iconSize, iconSize);
                var iImg = iconGo.AddComponent<Image>();
                iImg.sprite = iconSprite;
                iImg.preserveAspect = true;
                iImg.raycastTarget = false;

                _label.rectTransform.offsetMin += new Vector2(iconLeft + iconSize + 4f, 0f);
            }

            BuildField();

            var trig = _searchGo.AddComponent<EventTrigger>();
            var hover = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            hover.callback.AddListener(new Action<BaseEventData>(_ => { var es = EventSystem.current; if (es != null) es.SetSelectedGameObject(_searchGo); }));
            trig.triggers.Add(hover);
            var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            click.callback.AddListener(new Action<BaseEventData>(_ => StartTyping()));
            trig.triggers.Add(click);
            var submit = new EventTrigger.Entry { eventID = EventTriggerType.Submit };
            submit.callback.AddListener(new Action<BaseEventData>(_ => StartTyping()));
            trig.triggers.Add(submit);

            Redraw();
        }

        // a real TMP_InputField, parked at zero size with transparent text, so the typing itself is
        // the engine's (caret, clipboard, repeat) and the on-screen keyboard has something it
        // already knows how to drive. the visible text is the row's own label, mirrored on change.
        private void BuildField()
        {
            var fieldGo = new GameObject("BFG_SearchField");
            fieldGo.transform.SetParent(_searchGo.transform, false);
            fieldGo.SetActive(false);

            var frt = fieldGo.AddComponent<RectTransform>();
            frt.anchorMin = frt.anchorMax = Vector2.zero;
            frt.pivot = Vector2.zero;
            frt.sizeDelta = new Vector2(1f, 1f);

            var areaGo = new GameObject("TextArea");
            areaGo.transform.SetParent(fieldGo.transform, false);
            var art = areaGo.AddComponent<RectTransform>();
            art.anchorMin = Vector2.zero;
            art.anchorMax = Vector2.one;
            art.offsetMin = Vector2.zero;
            art.offsetMax = Vector2.zero;
            areaGo.AddComponent<RectMask2D>();

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(areaGo.transform, false);
            var trt = textGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            var hidden = textGo.AddComponent<TextMeshProUGUI>();
            hidden.font = _label.font;
            hidden.fontSharedMaterial = _label.fontSharedMaterial;
            hidden.fontSize = 1f;
            hidden.color = new Color(1f, 1f, 1f, 0f);
            hidden.raycastTarget = false;

            _field = fieldGo.AddComponent<TMP_InputField>();
            _field.textViewport = art;
            _field.textComponent = hidden;
            _field.lineType = TMP_InputField.LineType.SingleLine;
            _field.characterLimit = 40;
            _field.restoreOriginalTextOnEscape = false;
            _field.customCaretColor = true;
            _field.caretColor = new Color(0f, 0f, 0f, 0f);
            _field.selectionColor = new Color(0f, 0f, 0f, 0f);
            _field.transition = Selectable.Transition.None;
            var nav = _field.navigation;
            nav.mode = UnityEngine.UI.Navigation.Mode.None;
            _field.navigation = nav;
            _field.onValueChanged.AddListener(new Action<string>(OnQueryChanged));

            fieldGo.SetActive(true);
            BetterFG.UI.BetterFGUIMan.AdoptField(fieldGo);
        }

        private void OnQueryChanged(string value)
        {
            if (_content == null) return;
            if (Time.frameCount <= _typingFrame && value.Length > 0) { _field.text = ""; return; }
            RefilterRows();
            Redraw();
        }

        private void StartTyping()
        {
            if (_typing || _field == null) return;
            _typing = true;
            _typingFrame = Time.frameCount;
            _field.readOnly = true;
            BetterFG.Services.FGInputLockService.SetFakeFieldLock(true);
            _field.Select();
            _field.ActivateInputField();
            Redraw();
        }

        private void StopTyping()
        {
            if (!_typing) return;
            _typing = false;
            BetterFG.Services.FGInputLockService.SetFakeFieldLock(false);
            if (_field != null) _field.DeactivateInputField();

            var es = EventSystem.current;
            if (es != null && _searchGo != null
                && (es.currentSelectedGameObject == null
                    || (_field != null && es.currentSelectedGameObject == _field.gameObject)))
                es.SetSelectedGameObject(_searchGo);

            Redraw();
            if (_content == null) return;
            RefilterRows();
            ApplyOptions(-1);
        }

        private string Query => _field == null ? "" : _field.text;

        private void Redraw()
        {
            if (_label == null) return;
            string q = Query;
            if (_typing) _label.text = q + "|";
            else if (q.Length > 0) _label.text = q;
            else BettrFG.uGUI.UGUIShip.RelabelText(_label, PlaceholderId);
        }

        // a row's ShowName indexes straight into the manager's show list and throws once the list
        // has been swapped out from under the rows, which happens on every tab switch.
        private void RefilterRows()
        {
            string q = Query.Trim();
            bool showAll = q.Length == 0;

            var mgr = SingletonBehaviour<FragglePlobbiesManager>.Instance;
            var shows = mgr == null ? null : mgr.ShowData;
            int showCount = shows == null ? 0 : shows.Count;

            int n = _content.childCount;
            for (int i = 0; i < n; i++)
            {
                var child = _content.GetChild(i);
                var row = child.GetComponent<PrivateLobbyShowListEntryViewModel>();
                if (row == null) continue;

                bool match = showAll;
                if (!showAll)
                {
                    int idx = row._index;
                    string name = idx >= 0 && idx < showCount ? row.ShowName : null;
                    match = !string.IsNullOrEmpty(name) && name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                if (child.gameObject.activeSelf != match) child.gameObject.SetActive(match);
            }
        }

        // the carrousel navigates by index over this array, so a hidden row has to leave it or
        // up/down lands on nothing. rebuilt from the live children: us, enter code, visible rows.
        private void ApplyOptions(int preferredIndex)
        {
            if (_carrousel == null) return;

            _optionScratch.Clear();

            int n = _content.childCount;
            for (int i = 0; i < n; i++)
            {
                var child = _content.GetChild(i);
                var go = child.gameObject;
                if (!go.activeSelf) continue;
                if (go != _searchGo && go != _enterCodeGo && child.GetComponent<PrivateLobbyShowListEntryViewModel>() == null) continue;
                _optionScratch.Add(go);
            }
            if (_optionScratch.Count == 0) return;

            int index = preferredIndex;
            bool snapTop = false;
            if (index < 0)
            {
                var sel = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
                int searchIdx = _optionScratch.IndexOf(_searchGo);
                index = searchIdx >= 0 && searchIdx + 1 < _optionScratch.Count ? searchIdx + 1 : 0;
                snapTop = true;
                for (int i = 0; i < _optionScratch.Count; i++)
                    if (sel != null && _optionScratch[i].GetInstanceID() == sel.GetInstanceID()) { index = i; snapTop = false; break; }
            }
            if (index >= _optionScratch.Count) index = 0;

            var arr = new Il2CppReferenceArray<GameObject>(_optionScratch.Count);
            for (int i = 0; i < _optionScratch.Count; i++) arr[i] = _optionScratch[i];

            _carrousel.SetOptions(arr, index, false);
            _optionsSeen = _carrousel._menuOptions.Pointer;

            if (snapTop)
            {
                var sr = _carrousel.GetScrollRect;
                if (sr != null) sr.verticalNormalizedPosition = 1f;
            }
        }

        public override void DisableTweak()
        {
            StopTyping();
            if (_searchGo == null || _content == null) { Forget(); return; }

            if (_field != null) { _field.text = ""; BetterFG.UI.BetterFGUIMan.DropField(_field.gameObject); }
            UnityEngine.Object.DestroyImmediate(_searchGo);
            _searchGo = null;
            _label = null;
            _field = null;
            RefilterRows();
            ApplyOptions(0);
            Forget();
        }
    }
}
