using BetterFG.Utilities;
using FGClient;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Customization.Player
{
    internal static class CosmeticCell
    {
        private const string TemplateKey = "RewardScreen_UI_RewardDirect";

        internal static bool Wants(UnityEngine.Object option) =>
            option != null && (option.TryCast<FaceplateOption>() != null || option.TryCast<ColourOption>() != null);

        internal static void Fill(Transform parent, UnityEngine.Object option)
        {
            var screen = AssetLoads.Get<GameObject>(TemplateKey);
            var faceplate = option.TryCast<FaceplateOption>();
            var template = faceplate != null
                ? screen.GetComponentInChildren<FaceplateOptionViewModel>(true).gameObject
                : screen.GetComponentInChildren<ColourOptionViewModel>(true).gameObject;

            var cell = Object.Instantiate(template, parent, false);
            cell.SetActive(true);

            var rt = cell.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            foreach (var g in cell.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;

            if (faceplate != null) cell.GetComponent<FaceplateOptionViewModel>().SetOption(faceplate);
            else cell.GetComponent<ColourOptionViewModel>().SetOption(option.Cast<ColourOption>());
        }
    }
}
