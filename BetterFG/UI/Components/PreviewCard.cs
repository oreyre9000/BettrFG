using System;
using BettrFG.uGUI;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Components
{
    internal static class PreviewCard
    {
        public static RawImage Build(RectTransform parent, float x, float y, float w, float ph, float lh, int fs,
            string label, Texture tex, bool selected, Color bg, Color selectedColor, Action onClick)
        {
            var btn = UGUIShip.CreateButton(parent, new Rect(x, y, w, ph + lh + 8f), "", bg, UGUIShip.WHITE, fs, onClick);
            UGUIShip.SetButtonSelected(btn, selected, selectedColor);

            var picGo = new GameObject("Preview");
            picGo.transform.SetParent(btn.transform, false);
            UGUIShip.SetPixelRect(picGo.AddComponent<RectTransform>(), new Rect(4f, 4f, w - 8f, ph));
            var (_, inner) = UGUIShip.CreateFramedImage(picGo.transform, name: "Frame", ppuMult: 2f);
            var raw = inner.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            SetTexture(raw, tex);

            var name = UGUIShip.CreateLabel(btn.transform, new Rect(6f, ph + 6f, w - 12f, lh), label, fs, UGUIShip.WHITE);
            name.horizontalOverflow = HorizontalWrapMode.Wrap;
            name.verticalOverflow = VerticalWrapMode.Truncate;
            return raw;
        }

        public static void SetTexture(RawImage raw, Texture tex)
        {
            raw.texture = tex;
            raw.uvRect = new Rect(0f, 0f, 1f, 1f);
            raw.color = tex != null ? Color.white : Color.clear;
        }

        public static void SetSprite(RawImage raw, Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) { SetTexture(raw, null); return; }
            var r = sprite.textureRect;
            float tw = sprite.texture.width, th = sprite.texture.height;
            raw.texture = sprite.texture;
            raw.uvRect = new Rect(r.x / tw, r.y / th, r.width / tw, r.height / th);
            raw.color = Color.white;
        }
    }
}
