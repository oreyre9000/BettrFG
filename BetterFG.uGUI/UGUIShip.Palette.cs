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
        // shared palette — tabs/windows alias their local color fields to these instead of
        // redeclaring the literal, so there's one place that sets what "white"/"dark button"/"remove
        // button" actually mean instead of two dozen near-identical (and sometimes drifted) copies.
        public static readonly Color WHITE = Color.white;
        public static readonly Color BTN_DARK = new Color(0.2f, 0.2f, 0.2f, 1f);
        public static readonly Color BTN_REMOVE = new Color(0.55f, 0.15f, 0.15f, 1f);

        public static readonly Color ROW_ALT = new Color(1f, 1f, 1f, 0.03f);
        public static readonly Color ROW_CLEAR = new Color(0f, 0f, 0f, 0f);
        public static readonly Color ROW_HOVER = new Color(1f, 1f, 1f, 0.13f);
        public static readonly Color ROW_PRESS = new Color(1f, 1f, 1f, 0.2f);
        public static readonly Color ROW_SEL = new Color(0.45f, 1f, 0.45f, 0.16f);
    }
}
