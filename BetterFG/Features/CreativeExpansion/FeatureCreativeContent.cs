using System.Collections.Generic;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.Utilities;
using FallGuysLib.UI;
using FGClient.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class FeatureCreativeContent
    {
        public static readonly BfgFeature feature = new BfgFeature("creativecontent", "creativecontent.title", true, new List<FeatureSetting>
        {
            new FeatureSetting { id = "custombackgrounds", label = "creativeexpansion.custombackgrounds", defaultOn = true },
            new FeatureSetting { id = "objectthemes", label = "creativeexpansion.objectthemes", defaultOn = true },
            new FeatureSetting { id = "colouring", label = "creativeexpansion.colouring", defaultOn = true },
            new FeatureSetting { id = "customobjects", label = "creativeexpansion.customobjects", defaultOn = true },
        });

        const string AskedKey = "creativecontent.asked";
        const float PreviewSize = 240f;
        static readonly string[] Previews = { "BetterFG.assets.creativeobjects.thruster.png", "BetterFG.assets.creativeobjects.foliage33.png" };

        public static bool On(string id) => FeatureRegistry.IsOn("creativecontent", id);

        internal static void Ask()
        {
            if (PopUp.HasActive || SettingsService.Get(AskedKey, "false") == "true") return;

            NavPromptCore.RegisterCmsString("bfg_creativecontent_title", LocalizationService.Get("creativecontent.popup_title"));
            NavPromptCore.RegisterCmsString("bfg_creativecontent_body", LocalizationService.Get("creativecontent.popup_body"));
            NavPromptCore.RegisterCmsString("bfg_creativecontent_enable", LocalizationService.Get("creativecontent.popup_enable"));
            NavPromptCore.RegisterCmsString("bfg_creativecontent_disable", LocalizationService.Get("creativecontent.popup_disable"));

            GameObject row = null;
            PopUp.Confirm("bfg_creativecontent_title", "bfg_creativecontent_body", ok =>
            {
                if (row != null) Object.Destroy(row);
                SettingsService.Set(AskedKey, "true");
                feature.SetEnabled(ok);
                Plugin.Log.LogInfo($"bettrfg-only creative content: {(ok ? "on" : "off, flip it back in features if you change your mind")}");
            }, "bfg_creativecontent_enable", "bfg_creativecontent_disable", UIModalMessage.OKButtonType.Positive);

            row = AddPreviews(PopupManager.s_Instance.ActivePopup.transform);
        }

        static GameObject AddPreviews(Transform popup)
        {
            var row = new GameObject("BettrFG_ContentPreviews");
            row.transform.SetParent(popup.Find("Content"), false);
            row.AddComponent<RectTransform>();
            row.AddComponent<LayoutElement>().preferredHeight = PreviewSize;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 40f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;

            foreach (var res in Previews)
            {
                var go = new GameObject("Preview");
                go.transform.SetParent(row.transform, false);
                var le = go.AddComponent<LayoutElement>();
                le.preferredWidth = le.preferredHeight = PreviewSize;
                var img = go.AddComponent<Image>();
                img.sprite = EmbeddedResourceandUnity.LoadSprite(res);
                img.preserveAspect = true;
                img.raycastTarget = false;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(popup.GetComponent<RectTransform>());
            return row;
        }
    }
}
