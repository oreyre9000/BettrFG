using System;
using System.Collections.Generic;
using LevelEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class ColourSwatches
    {
        private const string SwapRoot = "bfg_colour_swap";
        private static readonly Dictionary<string, Func<Color>[]> _rows = new Dictionary<string, Func<Color>[]>();
        private static Sprite _swatchSprite, _borderSprite;
        private static int _frames;

        internal static void Clear()
        {
            _rows.Clear();
            _frames = 4;
        }

        internal static void Show(string title, Func<Color> from, Func<Color> to)
        {
            _rows[title] = new[] { from, to };
            _frames = 4;
        }

        internal static void Refresh() => _frames = 2;

        internal static void Tick()
        {
            if (_frames <= 0) return;
            _frames--;
            Decorate();
        }

        private static void Decorate()
        {
            var menu = LevelEditorParameterMenuViewModel._instance;
            if (menu == null) return;

            foreach (var row in menu.GetComponentsInChildren<ParameterNodeViewModelButton>(true))
            {
                Func<Color>[] colours = null;
                bool ours = row.ParamName != null && _rows.TryGetValue(row.ParamName, out colours);

                var swap = row.transform.Find(SwapRoot);
                var title = row.transform.Find("ParamTitle");
                title.Cast<RectTransform>().anchorMax = new Vector2(ours ? 0.5f : 0.94f, 1f);
                title.GetComponent<TMPro.TextMeshProUGUI>().alignment = ours ? TMPro.TextAlignmentOptions.Left : TMPro.TextAlignmentOptions.Center;
                if (!ours)
                {
                    if (swap != null) swap.gameObject.SetActive(false);
                    continue;
                }

                if (swap == null) swap = BuildSwap(row.transform);
                swap.gameObject.SetActive(true);
                swap.GetChild(0).GetComponent<Image>().color = colours[0]();
                swap.GetChild(2).GetComponent<Image>().color = colours[1]();
            }
        }

        private static Transform BuildSwap(Transform row)
        {
            if (_swatchSprite == null)
                foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>())
                {
                    if (g.name != "UI_ColorPickerSwatch_Prefab") continue;
                    _swatchSprite = g.GetComponent<Image>().sprite;
                    _borderSprite = g.transform.Find("NormalBorder").GetComponent<Image>().sprite;
                    break;
                }

            var root = new GameObject(SwapRoot).AddComponent<RectTransform>();
            root.SetParent(row, false);
            root.anchorMin = new Vector2(0.5f, 0f);
            root.anchorMax = new Vector2(0.94f, 1f);
            root.offsetMin = root.offsetMax = Vector2.zero;

            Swatch(root, 0.2f);

            var stock = row.Find("SettingsRightArrow");
            var arrow = new GameObject("arrow").AddComponent<RectTransform>();
            arrow.SetParent(root, false);
            arrow.anchorMin = arrow.anchorMax = new Vector2(0.5f, 0.5f);
            arrow.sizeDelta = new Vector2(34f, 34f);
            arrow.localScale = stock.localScale;
            arrow.localRotation = stock.localRotation;
            var arrowImg = arrow.gameObject.AddComponent<Image>();
            arrowImg.sprite = stock.GetComponent<Image>().sprite;
            arrowImg.preserveAspect = true;
            arrowImg.raycastTarget = false;

            Swatch(root, 0.8f);
            return root;
        }

        private static void Swatch(RectTransform root, float x)
        {
            var sw = new GameObject("UI_ColorPickerSwatch_Prefab").AddComponent<RectTransform>();
            sw.SetParent(root, false);
            sw.anchorMin = sw.anchorMax = new Vector2(x, 0.5f);
            sw.sizeDelta = new Vector2(42f, 42f);
            var img = sw.gameObject.AddComponent<Image>();
            img.sprite = _swatchSprite;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;

            var border = new GameObject("NormalBorder").AddComponent<RectTransform>();
            border.SetParent(sw, false);
            border.anchorMin = Vector2.zero;
            border.anchorMax = Vector2.one;
            border.sizeDelta = new Vector2(6f, 6f);
            var b = border.gameObject.AddComponent<Image>();
            b.sprite = _borderSprite;
            b.type = Image.Type.Sliced;
            b.color = new Color(0.247f, 0.247f, 0.247f, 1f);
            b.raycastTarget = false;
        }
    }
}
