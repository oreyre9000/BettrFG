using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using BetterFG.Services;

namespace BetterFG.Customization.UI.Styles.Season1
{
    internal static class S1CustPages
    {
        public const int PAGE = 12;
        const string ROOT = "UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Customiser/Prime_UI_Customizer_Prefab_Canvas(Clone)/";
        const string HOST = "BFG_S1Page";
        const float MODERN_GRID_W = 1153f;

        sealed class Page
        {
            public Transform Panel, Root, Views, Viewport;
            public RectTransform Master;
            public Transform MasterParent;
            public int MasterIndex;
            public Vector2 AMin, AMax, Pivot, Pos, Size;
            public Vector3 Scale;
            public TMP_Text SubText, SrcName, SrcRarity, DstName, DstRarity, Title, StockTitle;
            public readonly List<Transform> Pills = new List<Transform>();
        }

        static readonly (string Panel, string Section, string Carousel, (string Tab, string S1, string Label)[] Tabs)[] Specs =
        {
            ("Prime_UI_CustomizerOutfitsPanel_Variant", "CustomiserOutfits", "TabCarrouselOutfits", new (string, string, string)[]
            {
                ("Colours", "SubMenutab", null), ("Patterns", "SubMenutab (1)", null), ("Faceplates", "SubMenutab (2)", null),
                ("Uppers", "SubMenutab (3)", null), ("Lowers", "SubMenutab (4)", null),
            }),
            ("Prime_UI_CustomizerTheatricsPanel_Variant", "CustomiserTheatrics", "TabCarrouselTheatrics", new (string, string, string)[]
            {
                ("Emote", "SubMenutab (1)", null), ("Celebration", "SubMenutab (3)", null),
                ("Emoticon", null, "style.cust.emoticons"), ("Phrase", null, "style.cust.phrases"),
            }),
            ("Prime_UI_CustomizerInterfacePanel_Variant", "CustomiserInterface", "TabCarrouselInterface", new (string, string, string)[]
            {
                ("Nickname", "SubMenutab", null), ("Nameplate", null, "style.cust.nameplate"),
            }),
        };

        static readonly string[] StockVisuals = { "SafeArea/Container/Background", "SafeArea/Container/TitleTab", "SafeArea/Container/Menu_Outline", "SafeArea/Container/SubmenuIndex", "BottomLeftContainer/BottomLeft_Group/Generic_UI_ItemInfo_Prefab" };
        static readonly Dictionary<int, Page> _pages = new Dictionary<int, Page>();
        static bool _textHooked;

        public static void Apply(UIStyle style)
        {
            if (!_textHooked)
            {
                _textHooked = true;
                TMPro_EventManager.TEXT_CHANGED_EVENT.Add(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<UnityEngine.Object>>(new Action<UnityEngine.Object>(OnText)));
            }
            foreach (var spec in Specs)
            {
                var panel = GameObject.Find(ROOT + spec.Panel)?.transform;
                if (panel == null || panel.Find(HOST) != null) continue;
                try { Build(style, panel, spec.Section, spec.Carousel, spec.Tabs); }
                catch (Exception e) { Plugin.Log.LogWarning($"s1 {spec.Section} page didn't build: {e.Message}"); }
            }
        }

        static void Build(UIStyle style, Transform panel, string section, string carousel, (string Tab, string S1, string Label)[] tabs)
        {
            var root = S1Kit.Extract(style, "screen_Customiser_UICanvas", "Prefab_UI_SubMenu", panel, HOST);
            S1Kit.Stretch(root);
            root.SetAsFirstSibling();
            root.Find("SafeArea/SubMenuNavigation")?.gameObject.SetActive(false);
            var cust = root.Find("Prefab_UI_SubMenu_Customiser");
            cust.gameObject.SetActive(true);
            var safe = cust.Find("SafeArea");
            for (int i = 0; i < safe.childCount; i++)
                if (safe.GetChild(i).name.StartsWith("Customiser") && safe.GetChild(i).name != "CustomiserBacking")
                    safe.GetChild(i).gameObject.SetActive(safe.GetChild(i).name == section);
            foreach (var cg in root.GetComponentsInChildren<CanvasGroup>(true))
                if (!cg.name.Contains("PatternMask")) cg.alpha = 1f;
            foreach (var p in StockVisuals) StyledUI.Hide(panel.Find(p), true);

            var sec = safe.Find(section);
            var container = panel.Find("SafeArea/Container");
            var page = new Page { Panel = panel, Root = root };
            page.Viewport = sec.Find("TabContent" + section.Substring(10) + "/TabMasterViewport");
            page.Master = container.Find("MasterViewport").GetComponent<RectTransform>();
            page.MasterParent = page.Master.parent;
            page.MasterIndex = page.Master.GetSiblingIndex();
            page.AMin = page.Master.anchorMin; page.AMax = page.Master.anchorMax; page.Pivot = page.Master.pivot;
            page.Pos = page.Master.anchoredPosition; page.Size = page.Master.sizeDelta; page.Scale = page.Master.localScale;
            page.Views = page.Master.Find("MasterContent");
            var host = page.Viewport.GetComponent<RectTransform>();
            float scale = host.rect.width / MODERN_GRID_W;
            page.Master.SetParent(host, false);
            page.Master.anchorMin = page.Master.anchorMax = page.Master.pivot = new Vector2(0.5f, 0.5f);
            page.Master.anchoredPosition = Vector2.zero;
            page.Master.sizeDelta = new Vector2(host.rect.width / scale, host.rect.height / scale);
            page.Master.localScale = Vector3.one * scale;

            var modern = container.Find("SubmenuIndex/SubmenuTabsHorizontalLayout");
            var car = sec.Find(carousel);
            StyleGlyphs.Bind(car.Find("TabCarrouselNavigationLeft")?.GetComponent<Image>(), StyleGlyphs.Live(modern.Find("LeftGlyph")));
            StyleGlyphs.Bind(car.Find("TabCarrouselNavigationRight")?.GetComponent<Image>(), StyleGlyphs.Live(modern.Find("RightGlyph")));
            Transform template = null;
            for (int i = 0; i < car.childCount; i++)
                if (car.GetChild(i).name.StartsWith("SubMenutab")) { template = car.GetChild(i); car.GetChild(i).gameObject.SetActive(false); }
            int order = 0;
            foreach (var (tab, s1, label) in tabs)
            {
                var m = modern.Find("Generic_UI_IconTab_" + tab)?.GetComponent<FGClient.UI.UIButtonMenuTab>();
                if (m == null) continue;
                var pill = s1 != null ? car.Find(s1) : UnityEngine.Object.Instantiate(template.gameObject, car, false).transform;
                if (s1 == null)
                {
                    pill.name = "BFG_Pill_" + tab;
                    pill.localScale = template.localScale;
                    pill.GetComponentInChildren<TMP_Text>(true).text = LocalizationService.Get(label);
                }
                pill.gameObject.SetActive(m.gameObject.activeSelf);
                pill.SetSiblingIndex(order++);
                foreach (var c in pill.GetComponents<MonoBehaviour>())
                {
                    var n = c.GetIl2CppType().Name;
                    if (n.StartsWith("Tween") || n == "Toggle" || n == "CustomiserToggle") UnityEngine.Object.DestroyImmediate(c);
                }
                var hit = pill.GetComponent<Image>();
                if (hit == null) hit = pill.gameObject.AddComponent<Image>();
                hit.color = new Color(0f, 0f, 0f, 0f);
                var target = m.gameObject;
                S1Kit.Listen(pill.gameObject, EventTriggerType.PointerDown, () =>
                    ExecuteEvents.Execute(target, new PointerEventData(EventSystem.current), ExecuteEvents.pointerDownHandler));
                page.Pills.Add(pill);
            }

            page.SubText = container.Find("SubmenuIndex/Submenu_Text").GetComponent<TMP_Text>();
            var info = panel.Find("BottomLeftContainer/BottomLeft_Group/Generic_UI_ItemInfo_Prefab");
            page.SrcName = info.Find("ItemName_Text").GetComponent<TMP_Text>();
            page.SrcRarity = info.Find("Generic_UI_ItemTagContainer_Prefab/Generic_UI_ItemTag_Prefab/Tag_Text").GetComponent<TMP_Text>();
            var strip = sec.Find("TabContent" + section.Substring(10) + "/TabItemSelected");
            page.DstName = strip.Find("TabItemSelectedText").GetComponent<TMP_Text>();
            page.DstRarity = strip.Find("ItemRarityPill/SelectedRewardRarityText")?.GetComponent<TMP_Text>();
            page.DstName.enableWordWrapping = false;
            page.DstName.overflowMode = TextOverflowModes.Overflow;
            var title = root.Find("SafeArea/SubMenuTitle/BackingTitle/SubMenuTitleText")?.GetComponent<TMP_Text>();
            var stockTitle = panel.Find("SafeAreaTop/Generic_UI_ScreenTitle_Prefab/TitleTextText")?.GetComponent<TMP_Text>();
            page.Title = title;
            page.StockTitle = stockTitle;

            _pages[root.GetInstanceID()] = page;
            S1Kit.Follow(root.gameObject, PAGE, 1);
            Strip(page);
            Pills(page);
        }

        static void OnText(UnityEngine.Object o)
        {
            if (o == null) return;
            foreach (var p in _pages.Values)
            {
                if (p.Root == null) continue;
                if (p.SubText != null && o.Pointer == p.SubText.Pointer) p.Root.GetComponent<StyleFollower>().Arm();
                else if ((p.SrcName != null && o.Pointer == p.SrcName.Pointer) || (p.SrcRarity != null && o.Pointer == p.SrcRarity.Pointer)) Strip(p);
            }
        }

        public static void Settled(Transform root)
        {
            if (_pages.TryGetValue(root.GetInstanceID(), out var p)) Pills(p);
        }

        static void Strip(Page p)
        {
            p.DstName.text = p.SrcName.text;
            if (p.DstRarity == null) return;
            p.DstRarity.text = p.SrcRarity.text.ToUpperInvariant();
            var pill = p.DstRarity.transform.parent.GetComponent<RectTransform>();
            pill.sizeDelta = new Vector2(p.DstRarity.preferredWidth + 24f, pill.sizeDelta.y);
        }

        static void Pills(Page p)
        {
            if (p.Title != null && p.StockTitle != null && p.StockTitle.text.Length > 0) p.Title.text = p.StockTitle.text.ToUpperInvariant();
            Strip(p);
            int cur = -1, n = 0;
            for (int i = 0; i < p.Views.childCount; i++)
            {
                var v = p.Views.GetChild(i);
                if (v.name.EndsWith("_Full")) continue;
                if (v.gameObject.activeSelf) { cur = n; break; }
                n++;
            }
            for (int i = 0; i < p.Pills.Count; i++)
            {
                var pill = p.Pills[i];
                for (int k = 0; k < pill.childCount; k++)
                {
                    var c = pill.GetChild(k);
                    bool on;
                    if (c.name.EndsWith("Active")) on = i == cur;
                    else if (c.name.EndsWith("Disabled")) on = i != cur;
                    else continue;
                    c.gameObject.SetActive(on);
                    if (on) foreach (var g in c.GetComponentsInChildren<Graphic>(true)) g.canvasRenderer.SetAlpha(1f);
                }
            }
        }

        public static void Teardown()
        {
            foreach (var p in _pages.Values)
            {
                if (p.Master != null && p.MasterParent != null)
                {
                    p.Master.SetParent(p.MasterParent, false);
                    p.Master.SetSiblingIndex(p.MasterIndex);
                    p.Master.anchorMin = p.AMin; p.Master.anchorMax = p.AMax; p.Master.pivot = p.Pivot;
                    p.Master.anchoredPosition = p.Pos; p.Master.sizeDelta = p.Size; p.Master.localScale = p.Scale;
                }
                if (p.Panel != null) foreach (var s in StockVisuals) StyledUI.Hide(p.Panel.Find(s), false);
                if (p.Root != null) UnityEngine.Object.Destroy(p.Root.gameObject);
            }
            _pages.Clear();
        }
    }
}
