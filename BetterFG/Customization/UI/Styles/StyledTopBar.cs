using System;
using System.Collections.Generic;
using FGClient;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using BetterFG.Features.QualificationTime;
using BetterFG.Services;

namespace BetterFG.Customization.UI.Styles
{
    internal static class StyledTopBar
    {
        const string TOPBAR = "UICanvas_Client_V2(Clone)/Default/Topbar_Prime(Clone)";
        const string HOST = "BFG_StyledTopBar";
        const string SETTINGS_TAB = "BFG_SettingsTab";
        static readonly string[] StockVisuals = { "SafeArea/TabsHorizontalLayout", "SafeArea/TopRight_Group", "SafeArea/Panel" };

        static GameObject _host, _real, _settingsTopBar;
        static readonly List<(Toggle Real, Toggle Styled, string Title)> _pairs = new List<(Toggle, Toggle, string)>();
        static readonly HashSet<int> _hooked = new HashSet<int>();
        static TMP_Text _title, _crowns, _kudos, _realKudos;
        static CurrenciesViewModel _wallet;
        static LocalisedStrings _strings;
        static MainMenuManager _menu;
        static TopBarSpec _spec;
        static bool _sync, _textHooked;

        public static void Build(UIStyle style, MainMenuManager menu)
        {
            _menu = menu;
            _spec = style.TopBar;
            _real = GameObject.Find(TOPBAR);
            if (_real == null || _spec == null) return;
            Teardown();
            _strings = UnityEngine.Object.FindObjectOfType<LocalisedStaticLabel>()?.LocalisedStrings;
            var realTabs = _real.transform.Find("SafeArea/TabsHorizontalLayout");
            var settings = _spec.Settings != null ? SettingsTab(realTabs) : null;

            var hostGo = new GameObject(HOST);
            var hostRt = hostGo.AddComponent<RectTransform>();
            hostRt.SetParent(_real.transform.parent, false);
            hostRt.SetSiblingIndex(_real.transform.GetSiblingIndex() + 1);
            hostRt.anchorMin = Vector2.zero;
            hostRt.anchorMax = Vector2.one;
            hostRt.offsetMin = hostRt.offsetMax = Vector2.zero;
            hostGo.layer = _real.layer;
            var realCanvas = _real.GetComponent<Canvas>();
            var canvas = hostGo.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingLayerID = realCanvas.sortingLayerID;
            canvas.sortingOrder = realCanvas.sortingOrder;
            hostGo.AddComponent<GraphicRaycaster>();
            _host = hostGo;

            var bar = StyleLayout.Build(style.Assets, _spec.Layout, hostRt);
            foreach (var path in StockVisuals) StyledUI.Hide(_real.transform.Find(path), true);

            var carousel = bar.transform.Find(_spec.Carousel);
            var group = carousel.GetComponent<ToggleGroup>();
            var templates = new GameObject[_spec.TabTemplates.Length];
            for (int i = 0; i < templates.Length; i++) templates[i] = carousel.Find(_spec.TabTemplates[i]).gameObject;
            int n = 0;
            for (int i = 0; i < realTabs.childCount; i++)
            {
                var rc = realTabs.GetChild(i);
                var rt = rc.GetComponent<Toggle>();
                if (rt == null || !rc.gameObject.activeSelf) continue;
                bool isSettings = rt == settings;
                TabArt art = isSettings ? _spec.Settings : null;
                if (!isSettings) _spec.Tabs.TryGetValue(rc.name, out art);
                var stockIcon = isSettings ? null : rc.Find("Icon")?.GetComponent<Image>()?.sprite;
                var tab = AddTab(style, templates[n % templates.Length], carousel, group, n, rc.name, art, stockIcon);
                _pairs.Add((rt, tab, rt == PBTabView.TabToggle ? null : art?.TitleKey));
                var target = rt;
                tab.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<bool>>(new Action<bool>(on =>
                {
                    if (_sync || !on || target.isOn) return;
                    target.isOn = true;
                })));
                if (_hooked.Add(rt.GetInstanceID()))
                    rt.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<bool>>(new Action<bool>(OnRealTab)));
                n++;
            }
            foreach (var name in _spec.TemplateTabs) UnityEngine.Object.DestroyImmediate(carousel.Find(name).gameObject);
            var crt = carousel.GetComponent<RectTransform>();
            crt.sizeDelta = new Vector2(n * _spec.TabWidth + (n - 1) * _spec.TabGap, crt.sizeDelta.y);

            StyleGlyphs.Bind(bar.transform.Find(_spec.PrevGlyph)?.GetComponent<Image>(), StyleGlyphs.Live(realTabs.Find("Input_PreviousTab")));
            StyleGlyphs.Bind(bar.transform.Find(_spec.NextGlyph)?.GetComponent<Image>(), StyleGlyphs.Live(realTabs.Find("Input_NextTab")));

            _title = bar.transform.Find(_spec.Title).GetComponent<TMP_Text>();
            _crowns = bar.transform.Find(_spec.Crowns).GetComponent<TMP_Text>();
            _kudos = bar.transform.Find(_spec.Kudos).GetComponent<TMP_Text>();
            var currencies = _real.transform.Find("SafeArea/TopRight_Group/CurrencyHorizontalLayout");
            _wallet = currencies.GetComponent<CurrenciesViewModel>();
            _realKudos = currencies.Find("Kudos_CurrencyCounter/AmountText").GetComponent<TMP_Text>();
            if (!_textHooked)
            {
                _textHooked = true;
                TMPro_EventManager.TEXT_CHANGED_EVENT.Add(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<UnityEngine.Object>>(new Action<UnityEngine.Object>(OnTextChanged)));
            }

            var follower = _real.GetComponent<StyleFollower>() ?? _real.AddComponent<StyleFollower>();
            follower.Kind = StyleFollower.TOPBAR;

            bar.SetActive(true);
            _host.SetActive(_real.activeInHierarchy);
            Mirror();
            Wallet();
            Plugin.Log.LogInfo($"{style.Id} top bar up over {_pairs.Count} real tabs");
        }

        static Toggle SettingsTab(Transform layout)
        {
            var builder = _menu._mainMenuBuilder;
            var screen = builder.SettingsScreenViewModel;
            if (screen._topBar != null) _settingsTopBar = screen._topBar;
            screen._topBar = null;

            var existing = layout.Find(SETTINGS_TAB);
            if (existing != null) return existing.GetComponent<Toggle>();

            var shop = layout.Find("ShopButton");
            var go = UnityEngine.Object.Instantiate(shop.gameObject, layout, false);
            go.name = SETTINGS_TAB;
            go.transform.SetSiblingIndex(shop.GetSiblingIndex() + 1);
            var storeVm = go.GetComponent<SymphonyStoreMenuTabViewModel>();
            if (storeVm != null) UnityEngine.Object.DestroyImmediate(storeVm);
            var toggle = go.GetComponent<Toggle>();
            toggle.onValueChanged.RemoveAllListeners();
            toggle.SetIsOnWithoutNotify(false);
            toggle.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<bool>>(new Action<bool>(on =>
            {
                if (on && _menu.CurrentNavigationView != MainMenuViews.Settings) _menu.NavigateToView(MainMenuViews.Settings);
            })));

            var top = builder.TopBar;
            var tabs = new List<GameObject>(top._menuTabs);
            tabs.Insert(Math.Min(_menu.ViewOrder.IndexOf(MainMenuViews.Settings), tabs.Count), go);
            top._menuTabs = new Il2CppReferenceArray<GameObject>(tabs.ToArray());
            NavTabs(top);

            var handler = top._switchableViewToggleGroupHandler;
            var toggles = new List<Toggle>(handler._toggles) { toggle };
            var actions = new List<UnityAction<bool>>(handler._toggleOnValueChangedActions)
            {
                DelegateSupport.ConvertDelegate<UnityAction<bool>>(new Action<bool>(_ => { }))
            };
            handler._toggles = new Il2CppReferenceArray<Toggle>(toggles.ToArray());
            handler._toggleOnValueChangedActions = new Il2CppReferenceArray<UnityAction<bool>>(actions.ToArray());
            if (_menu.CurrentNavigationView == MainMenuViews.Settings) toggle.SetIsOnWithoutNotify(true);
            return toggle;
        }

        static void NavTabs(TopBarViewModel top)
        {
            var tabs = new List<GameObject>(top._menuTabs);
            var pb = PBTabView.TabToggle != null ? PBTabView.TabToggle.gameObject : null;
            if (pb != null)
            {
                tabs.Add(pb);
                PBTabView.MenuTabIndex = tabs.Count - 1;
            }
            top._switchableViewRewiredNavigation.SetupMenuTabs(new Il2CppReferenceArray<GameObject>(tabs.ToArray()));
        }

        public static void Teardown()
        {
            if (_host != null) UnityEngine.Object.Destroy(_host);
            _host = null;
            _pairs.Clear();
            var real = _real != null ? _real : GameObject.Find(TOPBAR);
            if (real == null) return;
            foreach (var path in StockVisuals) StyledUI.Hide(real.transform.Find(path), false);
        }

        public static void RemoveSettingsTab()
        {
            var real = _real != null ? _real : GameObject.Find(TOPBAR);
            var tab = real?.transform.Find("SafeArea/TabsHorizontalLayout/" + SETTINGS_TAB);
            if (tab == null || _menu == null) return;
            var builder = _menu._mainMenuBuilder;
            if (_settingsTopBar != null) builder.SettingsScreenViewModel._topBar = _settingsTopBar;
            var top = builder.TopBar;
            var tabs = new List<GameObject>(top._menuTabs);
            tabs.Remove(tab.gameObject);
            top._menuTabs = new Il2CppReferenceArray<GameObject>(tabs.ToArray());
            NavTabs(top);
            var handler = top._switchableViewToggleGroupHandler;
            var toggles = new List<Toggle>(handler._toggles);
            var actions = new List<UnityAction<bool>>(handler._toggleOnValueChangedActions);
            int at = toggles.IndexOf(tab.GetComponent<Toggle>());
            toggles.RemoveAt(at);
            actions.RemoveAt(at);
            handler._toggles = new Il2CppReferenceArray<Toggle>(toggles.ToArray());
            handler._toggleOnValueChangedActions = new Il2CppReferenceArray<UnityAction<bool>>(actions.ToArray());
            UnityEngine.Object.Destroy(tab.gameObject);
        }

        public static void Suppress(bool subPageOpen)
        {
            if (_real == null || _menu == null || _menu.CurrentNavigationView != MainMenuViews.Settings) return;
            StyleUndo.Active(_real);
            _real.SetActive(!subPageOpen);
        }

        static Toggle AddTab(UIStyle style, GameObject tpl, Transform carousel, ToggleGroup group, int index, string name, TabArt art, Sprite stockIcon)
        {
            var go = UnityEngine.Object.Instantiate(tpl, carousel, false);
            go.name = "StyledTab_" + name;
            go.transform.SetSiblingIndex(index);
            var img = go.transform.Find(_spec.TabIcon).GetComponent<Image>();
            var own = art?.Icon != null ? style.Assets.Sprite(art.Icon) : null;
            img.sprite = own ?? stockIcon;
            img.preserveAspect = true;
            var r = img.rectTransform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            if (own != null)
            {
                r.sizeDelta = own.rect.size;
                r.anchoredPosition = art.IconOffset;
            }
            else
            {
                r.sizeDelta = new Vector2(_spec.ForeignIconSize, _spec.ForeignIconSize);
                r.anchoredPosition = _spec.ForeignIconOffset;
            }
            go.GetComponent<TweenOnToggle>()._toggleOff.localScale = Vector3.one;
            var t = go.GetComponent<Toggle>();
            t.group = group;
            t.SetIsOnWithoutNotify(false);
            return t;
        }

        static void OnRealTab(bool on)
        {
            if (on) Mirror();
        }

        static void OnTextChanged(UnityEngine.Object o)
        {
            if (o != null && _realKudos != null && o.Pointer == _realKudos.Pointer) Wallet();
        }

        public static void OnRealTopBar(bool active)
        {
            if (_host == null) return;
            _host.SetActive(active);
            if (active) Mirror();
        }

        public static void Reevaluate()
        {
            var real = GameObject.Find(TOPBAR);
            OnRealTopBar(real != null && real.activeInHierarchy);
        }

        public static void Mirror()
        {
            if (_host == null) return;
            _sync = true;
            try
            {
                foreach (var p in _pairs)
                {
                    if (!p.Real.isOn) continue;
                    if (!p.Styled.isOn) p.Styled.isOn = true;
                    _title.text = p.Title == null ? LocalizationService.Get("style.tab.pb") : Loc(p.Title);
                }
            }
            finally { _sync = false; }
        }

        static string Loc(string key)
        {
            if (_strings != null && _strings.ContainsString(key)) return _strings.GetString(key);
            return LocalizationService.Get("style.tab." + key);
        }

        static void Wallet()
        {
            if (_wallet == null || _host == null) return;
            _crowns.text = _wallet.Crowns;
            _kudos.text = _wallet.Kudos;
        }
    }
}
