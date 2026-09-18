using System;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

namespace BettrFG.uGUI
{
    public static partial class UGUIShip
    {
        private static Sprite _deluxSprite;
        private static Sprite _deluxFillSprite;
        private static Sprite _deluxShineSprite;
        private static Sprite _deluxOutlineSprite;
        private static Sprite _deluxPanelSprite;
        private static Sprite _deluxPanelDarkSprite;

        // 9-sliced loader for the delux button art. corners ~8px on the 64px source; border 16 is a
        // safe superset, multiplier keeps the drawn corner from eating short buttons.
        private static Sprite LoadDeluxSlice(string res, ref Sprite cache)
        {
            if (cache != null) return cache;
            var tex = LoadTexture(res);
            if (tex == null) return null;
            tex.wrapMode = TextureWrapMode.Clamp;
            cache = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(16, 16, 16, 16));
            return cache;
        }

        private static Image AddDeluxSlice(Transform parent, string res, ref Sprite cache, Color color, string name, float ppuMult)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.sprite = LoadDeluxSlice(res, ref cache);
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = ppuMult;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        // the black 9-slice base behind every delux element — this is the ring that actually reads
        // as each button/scrollbar's outline (the colorfill sits inset on top of it). higher ppuMult
        // shrinks the drawn corner, for thin elements (slider track).
        public static void ApplyDeluxBase(Image img, float ppuMult = 2f)
        {
            var bg = LoadDeluxSlice("BetterFG.assets.ui.general.uisprite_delux.png", ref _deluxSprite);
            if (bg == null) return;
            img.sprite = bg;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = ppuMult;
            RegisterShine(img);
        }

        // the filled rounded-panel 9-slice on `img` — dark-grey body, rounded corners. rendered as
        // authored; bake the alpha you want into the sprite. the default backdrop for content areas.
        public static void ApplyDeluxPanel(Image img, float ppuMult = 2f)
        {
            var s = LoadDeluxSlice("BetterFG.assets.ui.general.uisprite_delux_panel_light.png", ref _deluxPanelSprite);
            if (s == null) return;
            img.sprite = s;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = ppuMult;
            RegisterShine(img);
        }

        public static void ApplyDeluxPanelDark(Image img, float ppuMult = 2f)
        {
            var s = LoadDeluxSlice("BetterFG.assets.ui.general.uisprite_delux_panel.png", ref _deluxPanelDarkSprite);
            if (s == null) return;
            img.sprite = s;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = ppuMult;
            RegisterShine(img);
        }

        public static void ApplyDeluxColorFill(Image img, float ppuMult = 2f)
        {
            var s = LoadDeluxSlice("BetterFG.assets.ui.general.uisprite_delux_colorfill.png", ref _deluxFillSprite);
            if (s == null) return;
            img.sprite = s;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = ppuMult;
        }

        public static void ApplyDeluxShine(Image img, float ppuMult = 2f)
        {
            var s = LoadDeluxSlice("BetterFG.assets.ui.general.uisprite_delux_shineoverlay.png", ref _deluxShineSprite);
            if (s == null) return;
            img.sprite = s;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = ppuMult;
            RegisterShine(img);
        }

        // the rounded panel-outline 9-slice on `img` — a framed border with a see-through middle.
        // for panels/cards that should read as an outlined frame rather than a filled slab.
        public static void ApplyDeluxPanelOutline(Image img, float ppuMult = 2f)
        {
            var s = LoadDeluxSlice("BetterFG.assets.ui.general.uisprite_delux_paneloutline.png", ref _deluxOutlineSprite);
            if (s == null) return;
            img.sprite = s;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = ppuMult;
            RegisterShine(img);
        }

        // a picture in a rounded delux frame: a rounded dark fill behind, the picture clipped to that
        // rounded shape (Mask on the fill slice), and the panel-outline 9-slice as the border on top.
        // returns (container, inner) — caller sizes `container` and adds an Image/RawImage on `inner`.
        public static (RectTransform container, RectTransform inner) CreateFramedImage(Transform parent,
            Color? fill = null, string name = "FramedImage", float ppuMult = 3f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;

            // the fill slice doubles as the stencil: Mask renders its own graphic AND clips children,
            // so children get the fill's rounded corners for free.
            var maskGo = new GameObject("Mask");
            maskGo.transform.SetParent(go.transform, false);
            var mRt = maskGo.AddComponent<RectTransform>();
            mRt.anchorMin = Vector2.zero; mRt.anchorMax = Vector2.one; mRt.offsetMin = mRt.offsetMax = Vector2.zero;
            var mImg = maskGo.AddComponent<Image>();
            ApplyDeluxBase(mImg, ppuMult);
            mImg.color = fill ?? new Color(0.04f, 0.04f, 0.04f, 1f);
            maskGo.AddComponent<Mask>().showMaskGraphic = true;

            // overfill the picture a hair so it bleeds under the border — any 1px clip fringe from the
            // rounded stencil then sits behind the outline instead of showing as an edge line.
            var innerGo = new GameObject("Img");
            innerGo.transform.SetParent(maskGo.transform, false);
            var iRt = innerGo.AddComponent<RectTransform>();
            iRt.anchorMin = Vector2.zero; iRt.anchorMax = Vector2.one;
            iRt.offsetMin = new Vector2(-1f, -1f); iRt.offsetMax = new Vector2(1f, 1f);

            var outGo = new GameObject("Outline");
            outGo.transform.SetParent(go.transform, false);
            var oRt = outGo.AddComponent<RectTransform>();
            oRt.anchorMin = Vector2.zero; oRt.anchorMax = Vector2.one; oRt.offsetMin = oRt.offsetMax = Vector2.zero;
            var oImg = outGo.AddComponent<Image>();
            ApplyDeluxPanelOutline(oImg, ppuMult);
            oImg.raycastTarget = false;

            return (rt, iRt);
        }

        // the delux look painted onto `img` + child layers on `go`: untinted black 9-slice base, a
        // `fill`-tinted colorfill slice, optional shine. returns the colorfill Image so interactive
        // callers (Button/Scrollbar) can point their targetGraphic at it and drive it via ColorTint.
        public static Image ApplyDeluxSkin(GameObject go, Image img, Color fill, bool withShine = true, float ppuMult = 2f)
        {
            if (LoadDeluxSlice("BetterFG.assets.ui.general.uisprite_delux.png", ref _deluxSprite) == null)
            { img.color = fill; return null; }
            ApplyDeluxBase(img, ppuMult);

            var fimg = AddDeluxSlice(go.transform, "BetterFG.assets.ui.general.uisprite_delux_colorfill.png",
                ref _deluxFillSprite, fill, "ColorFill", ppuMult);
            if (withShine)
                RegisterShine(AddDeluxSlice(go.transform,
                    "BetterFG.assets.ui.general.uisprite_delux_shineoverlay.png",
                    ref _deluxShineSprite, new Color(1f, 1f, 1f, 0.4f), "Shine", ppuMult));
            return fimg;
        }
    }
}
