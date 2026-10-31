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
        public static ManualLogSource Log;
        public static Assembly ResourceAssembly;
        public static Func<string, Texture2D> LoadTexture;
        public static Func<string, Sprite> LoadSprite;
        public static Func<string, Vector2, Vector4, Sprite> LoadSlicedSprite;
        public static Action PlayClick;
        public static Action PlayHover;
        public static Action<Canvas> RegisterCanvas;
        public static Action<TMPro.TMP_Text> ProtectFont;
        public static Action<Image> RegisterShine;
        public static Action<Image, Color> RegisterFill;
        public static Func<Color> Tint;
        public static Action<GameObject, string> AddTooltip;
        public static Action<RectTransform> DropdownPops;
        public static Func<Material> LeatherMaterial;
        // localization: text-creating widgets pass their "text" as an id, not literal display text.
        // LocalizeGet resolves id -> current-language string (falls back to id itself if unset/missing
        // so an un-wired call or an un-keyed dynamic string still renders something). BindLocalized
        // registers that Text to keep it in sync on language switch.
        public static Func<string, string> LocalizeGet;
        public static Action<Text, string> BindLocalized;
    }
}
