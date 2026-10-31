using System;
using System.Collections.Generic;
using BetterFG.Services;
using DG.Tweening;
using FGClient.Store;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterFG.Customization.UI.Styles.Season1
{
    internal static class S1Shop
    {
        public const int SHOP = 14;
        const string HOST = "BFG_S1Store";
        const string CANVAS = "Prime_UI_Shop_Prefab_Canvas(Clone)";
        const string OFFERS = "SafeArea/UI_Offers_Container/TabScrollContainer/TabViewport/TabContentMaster";
        const string FEAT = "FeaturedItemsContainer/FeaturedItemsCarrouselScrollView";
        const string REG = "RegularItemsContainer/RegularItemsScrollView";
        static readonly string[] Stock = { "SafeArea", "SafeAreaTop" };

        sealed class Row
        {
            public ScrollRect Scroll;
            public readonly List<Transform> Tiles = new List<Transform>();
            public readonly List<Transform> Cards = new List<Transform>();
        }

        sealed class Live
        {
            public Transform Canvas, Root, Fame, Last, Cur;
            public SymphonyStoreInputHandler Handler;
            public Row Feat = new Row(), Reg = new Row();
            public readonly List<Moved> Moved = new List<Moved>();
            public string Key = "";
        }

        sealed class Moved
        {
            public RectTransform Node;
            public Transform Parent;
            public int Sibling;
            public Vector2 Min, Max, Pivot, Pos, Size;
            public Vector3 Scale;
        }

        public const int LEFT = 0, RIGHT = 1, UP = 2, DOWN = 3;
        static readonly Dictionary<int, Live> _live = new Dictionary<int, Live>();
        static int _waits;

        public static void Arm(Transform t)
        {
            for (; t != null; t = t.parent)
                if (t.name.StartsWith("SymStoreDeferred")) { S1Kit.Follow(t.gameObject, SHOP, 2).Arm(); return; }
        }

        public static void ArmIn(Transform screens)
        {
            _waits = 0;
            for (int i = 0; i < screens.childCount; i++) Arm(screens.GetChild(i));
        }

        static string Txt(Transform t) => t == null ? "" : t.GetComponent<TMP_Text>().text;
        static void Set(Transform t, string s) => t.GetComponent<TMP_Text>().text = s;
        static bool On(Transform t) => t != null && t.gameObject.activeSelf;

        static int Num(string s)
        {
            int n = 0;
            foreach (var ch in s) if (ch >= '0' && ch <= '9') n = n * 10 + (ch - '0');
            return n;
        }

        static void Fit(Transform t)
        {
            var x = t.GetComponent<TMP_Text>();
            x.fontSizeMax = x.fontSize;
            x.fontSizeMin = x.fontSize * 0.5f;
            x.enableAutoSizing = true;
            x.enableWordWrapping = false;
        }

        static RectTransform Fill(Transform parent, string name)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static void Apply(UIStyle style, Transform deferred)
        {
            var canvas = deferred.Find(CANVAS);
            if (canvas == null) return;
            var master = canvas.Find(OFFERS);
            var again = S1Kit.Follow(deferred.gameObject, SHOP, 30);
            Watch(master, again);
            var feat = new List<Transform>();
            var reg = new List<Transform>();
            Transform fame = null;
            for (int i = 0; i < master.childCount; i++)
            {
                Watch(master.GetChild(i), again);
                var tiles = new List<Transform>();
                foreach (var x in master.GetChild(i).GetComponentsInChildren<Transform>(true))
                    if (x.name.StartsWith("Prime_UI_ShopOfferTile") && x.Find("Panel_BG") != null)
                    {
                        tiles.Add(x);
                        Watch(x.parent, again);
                    }
                if (tiles.Count == 1 && fame == null) fame = tiles[0];
                else if (i == 0) feat.AddRange(tiles);
                else reg.AddRange(tiles);
            }
            if (feat.Count + reg.Count == 0)
            {
                if (_waits++ < 20) again.Arm();
                return;
            }
            var key = string.Join(",", feat.ConvertAll(t => t.GetInstanceID().ToString())) + "|" + string.Join(",", reg.ConvertAll(t => t.GetInstanceID().ToString())) + "|" + (fame != null ? fame.GetInstanceID() : 0);
            int id = canvas.GetInstanceID();
            bool rebuilt = !_live.TryGetValue(id, out var l) || l.Root == null || l.Key != key;
            if (rebuilt) l = Build(style, canvas, feat, reg, fame, key);
            bool missing = reg.Count == 0 || fame == null;
            Sync(l.Feat, true, ref missing);
            Sync(l.Reg, false, ref missing);
            if (l.Fame != null) missing |= !FameArt(l);
            if ((rebuilt || missing) && _waits++ < 20) again.Arm();
            else if (!rebuilt && !missing) _waits = 0;
        }

        static void Watch(Transform t, StyleFollower relay)
        {
            if (t.GetComponent<StyleFollower>() != null) return;
            var f = t.gameObject.AddComponent<StyleFollower>();
            f.Kind = -1;
            f.Relay = relay;
        }

        static Live Build(UIStyle style, Transform canvas, List<Transform> feat, List<Transform> reg, Transform fame, string key)
        {
            foreach (var t in feat) Unhook(t);
            foreach (var t in reg) Unhook(t);
            if (fame != null) Unhook(fame);
            Transform keep = null;
            if (_live.TryGetValue(canvas.GetInstanceID(), out var old))
            {
                Restore(old);
                keep = old.Cur;
            }
            var root = S1Kit.Extract(style, "screen_Store_UICanvas", "Prefab_UI_Menu_Store/StoreContainer", canvas, HOST);
            S1Kit.Stretch(root);
            root.SetAsLastSibling();
            foreach (var p in Stock) StyledUI.Hide(canvas.Find(p), true);
            foreach (var p in new[] { "StoreBackground", "SafeArea/SubMenuNavigation", "SafeArea/Container/FeaturedItemsContainer/FeaturedRemainingTime", "SafeArea/Container/RegularItemsContainer/RegularRemainingTime", "SafeArea/Container/" + FEAT + "/FeaturedItemsLoadingView", "SafeArea/Container/" + REG + "/RegularItemsLoadingView" })
                root.Find(p).gameObject.SetActive(false);
            var c = root.Find("SafeArea/Container");
            foreach (var t in new[] { "FeaturedItemsContainer/FeaturedItemsTitleText", "RegularItemsContainer/RegularItemsTitleText" })
                Set(c.Find(t), Txt(c.Find(t)).ToUpperInvariant());
            var l = new Live { Canvas = canvas, Root = root, Fame = fame, Key = key, Handler = canvas.GetComponent<SymphonyStoreInputHandler>() };
            Rows(l, l.Feat, c.Find(FEAT), feat, S1Kit.Donor(style, "StoreFeaturedItem"), false);
            Rows(l, l.Reg, c.Find(REG), reg, S1Kit.Donor(style, "StoreRegularItem"), true);
            Right(l, c);
            _live[canvas.GetInstanceID()] = l;
            Focus(l, keep != null && (feat.Contains(keep) || reg.Contains(keep) || keep == fame) ? keep : feat.Count > 0 ? feat[0] : null);
            return l;
        }

        static void Unhook(Transform tile)
        {
            var et = tile.GetComponent<EventTrigger>();
            if (et != null) UnityEngine.Object.DestroyImmediate(et);
        }

        static void Rows(Live l, Row row, Transform view, List<Transform> tiles, Transform donor, bool bar)
        {
            row.Scroll = view.GetComponent<ScrollRect>();
            var grid = view.GetChild(0).GetComponent<RectTransform>();
            for (int i = grid.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(grid.GetChild(i).gameObject);
            grid.anchorMin = Vector2.zero;
            grid.anchorMax = new Vector2(0f, 1f);
            grid.pivot = new Vector2(0f, 0.5f);
            grid.anchoredPosition = Vector2.zero;
            grid.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            view.gameObject.AddComponent<RectMask2D>().padding = new Vector4(0f, -40f, 0f, -40f);
            foreach (var tile in tiles)
            {
                var card = UnityEngine.Object.Instantiate(donor.gameObject, grid, false).transform;
                card.name = "BFG_Card";
                card.Find("SoldoutContainer/SoldoutContainer").gameObject.SetActive(false);
                card.Find("FeaturedItemHighlight").gameObject.SetActive(false);
                var art = card.Find("GenericItemDisplayContainer");
                for (int i = 0; i < art.childCount; i++) art.GetChild(i).gameObject.SetActive(false);
                var raw = Fill(art, "BFG_Raw").gameObject;
                raw.AddComponent<RawImage>().raycastTarget = false;
                raw.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                var gen = tile.Find("Container_ItemPreview/GenericItemDisplayContainer");
                if (gen != null) Borrow(l, gen.GetComponent<RectTransform>(), art.GetComponent<RectTransform>());
                Fit(card.Find(bar ? "ItemRarityHeaderContainer/ItemName" : "ItemRarityHeaderContainer/FeaturedItemName"));
                card.Find("NotOwnedContainer/FeaturedItemPriceContainer/FeaturedItemPriceKudosIcon").GetComponent<Image>().preserveAspect = true;
                Mirror(l, card.gameObject, tile);
                Forward(card.gameObject, row.Scroll);
                row.Tiles.Add(tile);
                row.Cards.Add(card);
            }
            Bar(view, row.Scroll);
        }

        static void Forward(GameObject card, ScrollRect scroll)
        {
            var et = card.GetComponent<EventTrigger>();
            void On(EventTriggerType type, Action<PointerEventData> a)
            {
                var e = new EventTrigger.Entry { eventID = type };
                e.callback.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<BaseEventData>>(new Action<BaseEventData>(d => a(d.TryCast<PointerEventData>()))));
                et.triggers.Add(e);
            }
            On(EventTriggerType.Scroll, d => { ShortcutExtensions.DOKill(scroll.content, false); scroll.OnScroll(d); });
            On(EventTriggerType.InitializePotentialDrag, scroll.OnInitializePotentialDrag);
            On(EventTriggerType.BeginDrag, d => { ShortcutExtensions.DOKill(scroll.content, false); scroll.OnBeginDrag(d); });
            On(EventTriggerType.Drag, scroll.OnDrag);
            On(EventTriggerType.EndDrag, scroll.OnEndDrag);
        }

        static void Borrow(Live l, RectTransform node, RectTransform box)
        {
            var size = node.rect.size;
            l.Moved.Add(new Moved { Node = node, Parent = node.parent, Sibling = node.GetSiblingIndex(), Min = node.anchorMin, Max = node.anchorMax, Pivot = node.pivot, Pos = node.anchoredPosition, Size = node.sizeDelta, Scale = node.localScale });
            node.SetParent(box, false);
            node.anchorMin = node.anchorMax = node.pivot = new Vector2(0.5f, 0.5f);
            node.anchoredPosition = Vector2.zero;
            node.sizeDelta = size;
            float s = Mathf.Min(box.rect.width / Mathf.Max(1f, size.x), box.rect.height / Mathf.Max(1f, size.y));
            node.localScale = new Vector3(s, s, 1f);
        }

        static void Restore(Live l)
        {
            foreach (var m in l.Moved)
            {
                if (m.Node == null || m.Parent == null) continue;
                m.Node.SetParent(m.Parent, false);
                m.Node.SetSiblingIndex(m.Sibling);
                m.Node.anchorMin = m.Min;
                m.Node.anchorMax = m.Max;
                m.Node.pivot = m.Pivot;
                m.Node.anchoredPosition = m.Pos;
                m.Node.sizeDelta = m.Size;
                m.Node.localScale = m.Scale;
            }
            l.Moved.Clear();
        }

        static void Bar(Transform view, ScrollRect scroll)
        {
            var track = new GameObject("BFG_Bar");
            var trt = track.AddComponent<RectTransform>();
            trt.SetParent(view, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = new Vector2(1f, 0f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -12f);
            trt.sizeDelta = new Vector2(0f, 13f);
            track.AddComponent<Image>().color = Color.clear;
            var area = Fill(track.transform, "Area");
            var handle = Fill(area, "Handle");
            var hi = handle.gameObject.AddComponent<Image>();
            hi.sprite = UIStyles.Active.Assets.Sprite("UI_customise_scroll_1080p");
            hi.type = Image.Type.Sliced;
            var sb = track.AddComponent<Scrollbar>();
            sb.handleRect = handle;
            sb.targetGraphic = hi;
            sb.direction = Scrollbar.Direction.LeftToRight;
            sb.navigation = new Navigation { mode = Navigation.Mode.None };
            scroll.horizontalScrollbar = sb;
            scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        static void Mirror(Live l, GameObject card, Transform tile)
        {
            var press = tile.Find("Container_ItemPreview").gameObject;
            S1Kit.Listen(card, EventTriggerType.PointerEnter, () => Focus(l, tile));
            S1Kit.Listen(card, EventTriggerType.PointerClick, () => S1Kit.Click(press));
        }

        static Transform CardOf(Live l, Transform tile)
        {
            int i = l.Feat.Tiles.IndexOf(tile);
            if (i >= 0) return l.Feat.Cards[i];
            i = l.Reg.Tiles.IndexOf(tile);
            if (i >= 0) return l.Reg.Cards[i];
            return tile == l.Fame ? l.Root.Find("SafeArea/Container/RightCarouselDouble/RightCarouselDoubleTopCarousel/DLCStore") : null;
        }

        static void Lit(Live l, Transform tile, bool on)
        {
            var card = CardOf(l, tile);
            if (card == null) return;
            (card.Find("FeaturedItemHighlight") ?? card.Find("Highlight")).gameObject.SetActive(on);
            ShortcutExtensions.DOKill(card, true);
            card.localScale = on ? new Vector3(1.08f, 1.08f, 1f) : Vector3.one;
            if (on) ShortcutExtensions.DOPunchScale(card, new Vector3(0.15f, 0.1f, 0f), 0.2f, 1, 1f);
        }

        static void Focus(Live l, Transform tile)
        {
            if (tile == null) return;
            if (l.Cur != null && l.Cur != tile) Lit(l, l.Cur, false);
            l.Cur = tile;
            Lit(l, tile, true);
            l.Handler.Highlight(tile.GetComponent<SymphonyStoreBundleViewModel>().UIIndexData, true, false, true, false);
            if (tile == l.Fame) return;
            l.Last = tile;
            Reveal(CardOf(l, tile));
        }

        public static bool Move(Component handler, int dir)
        {
            Live l = null;
            foreach (var x in _live.Values) if (x.Root != null && x.Root.gameObject.activeInHierarchy && x.Handler == handler) l = x;
            if (l == null) return false;
            var at = l.Cur;
            int fi = l.Feat.Tiles.IndexOf(at), ri = l.Reg.Tiles.IndexOf(at);
            Transform next = null;
            if (at != null && at == l.Fame)
                next = dir == LEFT ? l.Last ?? (l.Feat.Tiles.Count > 0 ? l.Feat.Tiles[0] : null) : null;
            else if (fi >= 0 || ri >= 0)
            {
                var row = fi >= 0 ? l.Feat : l.Reg;
                int i = fi >= 0 ? fi : ri;
                if (dir == LEFT) next = i > 0 ? row.Tiles[i - 1] : null;
                else if (dir == RIGHT) next = i < row.Tiles.Count - 1 ? row.Tiles[i + 1] : l.Fame;
                else if (dir == DOWN && fi >= 0) next = Nearest(l.Reg, row.Cards[i]);
                else if (dir == UP && ri >= 0) next = Nearest(l.Feat, row.Cards[i]);
            }
            else next = l.Feat.Tiles.Count > 0 ? l.Feat.Tiles[0] : null;
            Focus(l, next);
            return true;
        }

        static void Reveal(Transform card)
        {
            var scroll = card.GetComponentInParent<ScrollRect>();
            if (scroll == null) return;
            var view = scroll.viewport != null ? scroll.viewport : scroll.GetComponent<RectTransform>();
            var content = scroll.content;
            ShortcutExtensions.DOKill(content, true);
            var c = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
            var v = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
            card.GetComponent<RectTransform>().GetWorldCorners(c);
            view.GetWorldCorners(v);
            float pad = 20f * content.lossyScale.x;
            float shift = c[0].x < v[0].x + pad ? v[0].x + pad - c[0].x : c[2].x > v[2].x - pad ? v[2].x - pad - c[2].x : 0f;
            if (shift == 0f) return;
            float local = shift / content.lossyScale.x;
            float min = Mathf.Min(0f, view.rect.width - content.rect.width);
            float delta = Mathf.Clamp(content.anchoredPosition.x + local, min, 0f) - content.anchoredPosition.x;
            scroll.velocity = Vector2.zero;
            TweenSettingsExtensions.SetEase(ShortcutExtensions.DOLocalMoveX(content, content.localPosition.x + delta, 0.25f), Ease.OutQuad);
        }

        static void Sync(Row row, bool featured, ref bool missing)
        {
            for (int i = 0; i < row.Cards.Count; i++)
            {
                var s1 = row.Cards[i];
                var m = row.Tiles[i];
                if (m == null) continue;
                Set(s1.Find(featured ? "ItemRarityHeaderContainer/FeaturedItemName" : "ItemRarityHeaderContainer/ItemName"), Txt(m.Find("Panel_Title/Text_Title")).ToUpperInvariant());
                var rar = Txt(m.Find("Panel_Rarity/Container_Rarity/Text__Rarity"));
                s1.Find("ItemRarityPill").gameObject.SetActive(On(m.Find("Panel_Rarity")) && rar.Length > 0);
                Set(s1.Find("ItemRarityPill/SelectedRewardRarityText"), rar);
                bool owned = On(m.Find("OwnedContainerParent/OwnedContainerBackground"));
                var price = s1.Find("NotOwnedContainer/FeaturedItemPriceContainer");
                price.gameObject.SetActive(!owned);
                s1.Find("OwnedContainer/OwnedContainer").gameObject.SetActive(owned);
                var fin = m.Find("Generic_UI_Prices_Container/FinalPrice_ContainerGroup/FinalPrice_Container");
                Set(price.Find("FeaturedItemFinalPriceText"), Txt(fin.Find("Text")));
                foreach (var ic in new[] { "GemsImage", "KudosImage", "FreeImage" })
                {
                    if (!On(fin.Find(ic))) continue;
                    var src = fin.Find(ic).GetComponent<Image>() ?? fin.Find(ic + "/Icon").GetComponent<Image>();
                    price.Find("FeaturedItemPriceKudosIcon").GetComponent<Image>().sprite = src.sprite;
                }
                var disc = m.Find("Generic_UI_Prices_Container/DiscountedPrice_Container");
                price.Find("RealPriceContainer").gameObject.SetActive(On(disc));
                Set(price.Find("RealPriceContainer/FeaturedItemRealPriceText"), Txt(disc.Find("Text")));
                int now = Num(Txt(fin.Find("Text"))), was = Num(Txt(disc.Find("Text")));
                bool sale = On(m.Find("Panel_Discount")) && was > now;
                s1.Find("FeaturedItemDiscountText").gameObject.SetActive(sale);
                if (sale) Set(s1.Find("FeaturedItemDiscountText/FeaturedItemDiscountText"), string.Format(LocalizationService.Get("style.shop.off"), Mathf.RoundToInt(100f * (was - now) / was)));
                missing |= !Art(s1.Find("GenericItemDisplayContainer"), m);
            }
        }

        static bool Art(Transform box, Transform m)
        {
            var raw = box.Find("BFG_Raw");
            var dlc = m.Find("Container_ItemPreview/Mask/DlcImage");
            var src = dlc != null && dlc.gameObject.activeSelf ? dlc.GetComponent<RawImage>() : null;
            if (src != null)
            {
                raw.gameObject.SetActive(src.texture != null);
                if (src.texture == null) return false;
                var r = raw.GetComponent<RawImage>();
                r.texture = src.texture;
                r.uvRect = src.uvRect;
                raw.GetComponent<AspectRatioFitter>().aspectRatio = src.texture.width * src.uvRect.width / Mathf.Max(1f, src.texture.height * src.uvRect.height);
                return true;
            }
            raw.gameObject.SetActive(false);
            foreach (var p in box.GetComponentsInChildren<FGClient.UI.SkinPatternWithRoundedCorners>(true))
            {
                p.PrimaryColour = p.PrimaryColour;
                p.SecondaryColour = p.SecondaryColour;
            }
            return true;
        }

        static void Right(Live l, Transform c)
        {
            var dlc = c.Find("RightCarouselDouble/RightCarouselDoubleTopCarousel/DLCStore");
            dlc.Find("Highlight").gameObject.SetActive(false);
            c.Find("RightCarouselDouble/RightCarouselDoubleBottomCarousel/KudosPage/Highlight").gameObject.SetActive(false);
            if (l.Fame == null) return;
            var text = dlc.Find("DLCStoreMask/DLCStoreBlackImage/DLCStoreText");
            Set(text, Txt(l.Fame.Find("Panel_Title/Text_Title")));
            Fit(text);
            var bg = dlc.Find("DLCStoreMask/DLCStoreBackgroundImage");
            UnityEngine.Object.DestroyImmediate(bg.GetComponent<Image>());
            bg.gameObject.AddComponent<RawImage>().raycastTarget = false;
            var brt = bg.GetComponent<RectTransform>();
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            var fit = bg.GetComponent<AspectRatioFitter>() ?? bg.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            Mirror(l, dlc.gameObject, l.Fame);
        }

        static bool FameArt(Live l)
        {
            var src = l.Fame.Find("Container_ItemPreview/Mask/DlcImage").GetComponent<RawImage>();
            if (src.texture == null) return false;
            var bg = l.Root.Find("SafeArea/Container/RightCarouselDouble/RightCarouselDoubleTopCarousel/DLCStore/DLCStoreMask/DLCStoreBackgroundImage");
            var r = bg.GetComponent<RawImage>();
            r.texture = src.texture;
            r.uvRect = src.uvRect;
            bg.GetComponent<AspectRatioFitter>().aspectRatio = src.texture.width * src.uvRect.width / Mathf.Max(1f, src.texture.height * src.uvRect.height);
            return true;
        }

        static Transform Nearest(Row row, Transform card)
        {
            Transform best = null;
            float bestD = float.MaxValue;
            float x = card.position.x;
            for (int i = 0; i < row.Cards.Count; i++)
            {
                float d = Mathf.Abs(row.Cards[i].position.x - x);
                if (d < bestD) { bestD = d; best = row.Tiles[i]; }
            }
            return best;
        }

        public static void Teardown()
        {
            foreach (var l in _live.Values)
            {
                Restore(l);
                if (l.Root != null) UnityEngine.Object.Destroy(l.Root.gameObject);
                if (l.Canvas == null) continue;
                foreach (var p in Stock) StyledUI.Hide(l.Canvas.Find(p), false);
                foreach (var t in l.Feat.Tiles) if (t != null) Unhook(t);
                foreach (var t in l.Reg.Tiles) if (t != null) Unhook(t);
                if (l.Fame != null) Unhook(l.Fame);
            }
            _live.Clear();
        }
    }
}
