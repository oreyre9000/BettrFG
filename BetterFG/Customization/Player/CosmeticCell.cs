using FG.Common.CMS;
using FGClient;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;

namespace BetterFG.Customization.Player
{
    internal static class CosmeticCell
    {
        private const string TemplateKey = "RewardScreen_UI_RewardDirect";
        private static GameObject _screen;

        internal static bool Wants(UnityEngine.Object option) =>
            option != null && (option.TryCast<FaceplateOption>() != null || option.TryCast<ColourOption>() != null);

        internal static void Fill(Transform parent, UnityEngine.Object option)
        {
            if (_screen == null) _screen = Addressables.LoadAssetAsync<GameObject>(TemplateKey).WaitForCompletion();
            var faceplate = option.TryCast<FaceplateOption>();
            var template = faceplate != null
                ? _screen.GetComponentInChildren<FaceplateOptionViewModel>(true).gameObject
                : _screen.GetComponentInChildren<ColourOptionViewModel>(true).gameObject;

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
