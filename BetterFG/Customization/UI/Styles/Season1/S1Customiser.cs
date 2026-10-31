using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Customization.UI.Styles.Season1
{
    internal static class S1Customiser
    {
        const string ROOT = "UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Customiser/Prime_UI_Customizer_Prefab_Canvas(Clone)/Generic_UI_CustomizerLandingPage_Prefab";
        const string HOST = "BFG_S1CustLanding";
        static readonly string[] StockVisuals = { "SafeArea/HorizontalLayout", "SafeArea/LandingPageTitle_Text" };

        public static void Apply(UIStyle style)
        {
            var landing = GameObject.Find(ROOT)?.transform;
            if (landing == null) return;
            var s1 = S1Kit.Extract(style, "screen_Customiser_UICanvas", "Prefab_UI_Character_Customiser", landing, HOST);
            S1Kit.Stretch(s1);
            foreach (var p in StockVisuals) StyledUI.Hide(landing.Find(p), true);
            s1.Find("SafeArea")?.gameObject.SetActive(false);
            var car = s1.Find("CustomiserSubMenuCarrousel");
            var hl = landing.Find("SafeArea/HorizontalLayout");
            var outfits = car.Find("CustomiserSubMenuOutfits");
            var theatrics = car.Find("CustomiserSubMenuTheatrics");
            var iface = car.Find("CustomiserSubMenuInterface");
            S1Tiles.Pair(style, outfits, hl.Find("Generic_UI_CustomizerLandingOutfitsButton_Variant").gameObject);
            S1Tiles.Pair(style, theatrics, hl.Find("Generic_UI_CustomizerLandingTheatricsButton_Variant").gameObject);
            var mi = hl.Find("Generic_UI_CustomizerLandingInterfaceButton_Variant");
            S1Tiles.Pair(style, iface, mi.gameObject, mi.GetComponentInChildren<TMP_Text>(true)?.text?.ToUpperInvariant());
            var ico = mi.Find("Icon")?.GetComponent<Image>();
            if (ico != null)
                foreach (var n in new[] { "CustomiserSubMenuDisabled/CustomiserSubMenuIcon", "CustomiserSubMenuActive/CustomiserSubMenuIcon" })
                {
                    var dst = iface.Find(n)?.GetComponent<Image>();
                    if (dst == null) continue;
                    dst.sprite = ico.sprite;
                    dst.preserveAspect = true;
                }
            S1Kit.Intro<FGClient.CustomizerTweenMenu>(s1.gameObject, c =>
            {
                c._outfits = outfits.GetComponent<RectTransform>();
                c._theatrics = theatrics.GetComponent<RectTransform>();
                c._interface = iface.GetComponent<RectTransform>();
                c._waitCamTransitionTime = 0.3f;
            });
        }
    }
}
