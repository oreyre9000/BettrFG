using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Customization.UI.Styles.Season1
{
    internal static class S1Preview
    {
        public const int PREVIEW = 18;
        const string HOST = "BFG_S1ItemInfo";
        const string POP = "SafeArea/Content/Popup_Content/";
        const string PRICE = POP + "Content_Bottom/Generic_UI_Prices_Container_Variant/FinalPrice_ContainerGroup/FinalPrice_Container/";

        sealed class Live
        {
            public Transform Stock, S1;
            public TMP_Text SrcTitle, SrcTimer;
        }

        static readonly Dictionary<int, Live> _live = new Dictionary<int, Live>();
        static bool _hooked;

        public static void Show(UIStyle style, Transform stock)
        {
            if (!_live.TryGetValue(stock.gameObject.GetInstanceID(), out var l) || l.S1 == null) l = Build(style, stock);
            S1Kit.Follow(l.S1.gameObject, PREVIEW, 1).Arm();
        }

        public static void Settled(Transform s1)
        {
            foreach (var l in _live.Values) if (l.S1 == s1) Sync(l);
        }

        static Live Build(UIStyle style, Transform stock)
        {
            if (!_hooked)
            {
                _hooked = true;
                TMPro_EventManager.TEXT_CHANGED_EVENT.Add(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<UnityEngine.Object>>(new Action<UnityEngine.Object>(OnText)));
            }
            var s1 = S1Kit.Extract(style, "screen_Store_UICanvas", "Prefab_UI_Menu_Store/StoreItemInfo/StoreItemInfoMain/ItemInfoBoxContainer", stock.Find("SafeArea/Content"), HOST);
            s1.GetComponent<RectTransform>().anchoredPosition = new Vector2(430f, 0f);
            StyledUI.Hide(stock.Find(POP.TrimEnd('/')), true);
            var button = stock.Find(POP + "Content_Bottom/Generic_UI_Button_Prefab");
            var buy = s1.Find("StoreRegularItem/BottomContainer/LayoutAvailableForPurchase");
            buy.Find("PriceContainer/CrownIcon").gameObject.SetActive(false);
            var purchase = buy.Find("PurchaseButton");
            purchase.GetComponent<Button>().onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(new Action(() => S1Kit.Click(button.gameObject))));
            purchase.Find("PurchaseButtonContainer/SubMenuNavigationEquipText").GetComponent<TMP_Text>().alignment = TextAlignmentOptions.Capline;
            purchase.Find("PurchaseButtonContainer/SubMenuNavigationEquipIcon/GlyphText").GetComponent<TMP_Text>().alignment = TextAlignmentOptions.Center;
            StyleGlyphs.Bind(purchase.Find("PurchaseButtonContainer/SubMenuNavigationEquipIcon").GetComponent<Image>(), StyleGlyphs.Live(button.Find("Container_Content")));
            var l = new Live
            {
                Stock = stock,
                S1 = s1,
                SrcTitle = stock.Find(POP + "Content_Top/TitleSubtitleContainer/Title_Text").GetComponent<TMP_Text>(),
                SrcTimer = stock.Find(POP + "Content_Top/AvailabilityTimer_Container/Timer_Text").GetComponent<TMP_Text>(),
            };
            _live[stock.gameObject.GetInstanceID()] = l;
            return l;
        }

        static void OnText(UnityEngine.Object o)
        {
            if (o == null) return;
            foreach (var l in _live.Values)
                if (l.S1 != null && (o.Pointer == l.SrcTitle.Pointer || o.Pointer == l.SrcTimer.Pointer)) { l.S1.GetComponent<StyleFollower>().Arm(); return; }
        }

        static void Sync(Live l)
        {
            var item = l.S1.Find("StoreRegularItem");
            string Txt(string p) => l.Stock.Find(p).GetComponent<TMP_Text>().text;
            void Set(string p, string v) => item.Find(p).GetComponent<TMP_Text>().text = v;
            Set("StoreItemName", l.SrcTitle.text.ToUpperInvariant());
            Set("StoreItemTypeText", Txt(POP + "Content_Top/TitleSubtitleContainer/Subtitle/Subtitle_Text"));
            Set("StoreItemRemainingTime/StoreItemRemainingTimeText", l.SrcTimer.text);
            item.Find("StoreItemRemainingTime").gameObject.SetActive(l.Stock.Find(POP + "Content_Top/AvailabilityTimer_Container").gameObject.activeSelf);
            var tag = l.Stock.Find(POP + "Generic_UI_ItemTag_Prefab");
            item.Find("ItemRarityPill").gameObject.SetActive(tag.gameObject.activeSelf);
            Set("ItemRarityPill/SelectedRewardRarityText", Txt(POP + "Generic_UI_ItemTag_Prefab/Tag_Text").ToUpperInvariant());
            bool owned = l.Stock.Find(POP + "OwnedContainerParent/OwnedContainerBackground").gameObject.activeSelf;
            bool sale = l.Stock.Find(POP + "Content_Bottom/Generic_UI_Button_Prefab").gameObject.activeSelf;
            var bottom = item.Find("BottomContainer");
            for (int i = 0; i < bottom.childCount; i++)
            {
                var c = bottom.GetChild(i);
                c.gameObject.SetActive(c.name == "LayoutOwned" ? owned : c.name == "LayoutAvailableForPurchase" && !owned && sale);
            }
            var price = bottom.Find("LayoutAvailableForPurchase/PriceContainer");
            price.Find("PriceText").GetComponent<TMP_Text>().text = Txt(PRICE + "Text");
            foreach (var ic in new[] { "GemsImage", "KudosImage", "FreeImage" })
            {
                var src = l.Stock.Find(PRICE + ic);
                if (!src.gameObject.activeSelf) continue;
                var img = price.Find("KudosIcon").GetComponent<Image>();
                img.sprite = (src.GetComponent<Image>() ?? src.Find("Icon").GetComponent<Image>()).sprite;
                img.preserveAspect = true;
            }
        }

        public static void Teardown()
        {
            foreach (var l in _live.Values)
            {
                if (l.S1 != null) UnityEngine.Object.Destroy(l.S1.gameObject);
                if (l.Stock != null) StyledUI.Hide(l.Stock.Find(POP.TrimEnd('/')), false);
            }
            _live.Clear();
        }
    }
}
