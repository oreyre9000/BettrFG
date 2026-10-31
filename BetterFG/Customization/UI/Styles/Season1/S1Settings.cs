using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Customization.UI.Styles.Season1
{
    internal static class S1Settings
    {
        public const int BOARD = 10;
        public const int STOCK = 17;
        const string ROOT = "UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Settings/PC_UI_SettingsPrefabCanvas_Variant(Clone)/";
        const string LAYOUT = "screen_Settings_UICanvas";
        const string HOST = "BFG_S1Options";
        const string TITLE = "BFG_S1SubTitle";

        static readonly (string S1, string Stock)[] Tiles =
        {
            ("OptionsSubMenuCarrouselTop/OptionsSubMenuAudio", "FirstRow/AudioButton"),
            ("OptionsSubMenuCarrouselTop/OptionsSubMenuGraphics", "FirstRow/VideoButton"),
            ("OptionsSubMenuCarrouselTop/OptionsSubMenuKeyboardControls", "FirstRow/KeyboardButtonPrime"),
            ("OptionsSubMenuCarrouselTop/OptionsSubMenuJoystickControls", "FirstRow/GamingButtonPrime"),
            ("OptionsSubMenuCarrouselBottom/OptionsSubMenuTutorial", "SecondRow/HowToPlayButton"),
            ("OptionsSubMenuCarrouselBottom/OptionsSubMenuSupport", "SecondRow/SupportButton"),
            ("OptionsSubMenuCarrouselBottom/OptionsSubMenuCredits", "SecondRow/CreditsButton"),
            ("OptionsSubMenuCarrouselBottom/OptionsSubMenuExit", "SecondRow/ExitButtonPrime"),
        };

        static Transform _backing, _hdr, _hdrText, _row, _hl, _bar, _fill, _pip, _arrow;
        static TMP_Text _srcTitle, _dstTitle;
        static readonly HashSet<int> _done = new HashSet<int>();

        static Transform Deep(Transform t, string name)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
            return null;
        }

        public static void Apply(UIStyle style)
        {
            var landing = GameObject.Find(ROOT + "SettingsMenu_Prime_Canvas")?.transform;
            if (landing == null) return;
            var s1 = S1Kit.Extract(style, LAYOUT, "Prefab_UI_Menu_Options", landing, HOST);
            S1Kit.Stretch(s1);
            StyledUI.Hide(landing.Find("SafeArea/CenterButtons_Group"), true);
            s1.Find("SafeArea/SubMenuNavigation")?.gameObject.SetActive(false);
            var rows = landing.Find("SafeArea/CenterButtons_Group/VerticalContainer");
            var car = s1.Find("SafeArea/OptionsSubMenuCarrousel");
            foreach (var (path, stock) in Tiles)
            {
                var t = car.Find(path);
                var m = rows.Find(stock);
                if (t == null || m == null) continue;
                S1Tiles.Pair(style, t, m.gameObject);
            }
            S1Kit.Intro<FGClient.SettingsTweenMenu>(s1.gameObject, c =>
            {
                c._tabs = new Il2CppReferenceArray<RectTransform>(0);
                c._waitCamTransitionTime = 0.3f;
            });
            Extras(style);

            var d = S1Kit.Donor(style, LAYOUT).Find("Prefab_UI_SubMenu_Settings/SafeArea");
            _backing = d.Find("SettingsBacking/SettingsBackingSprite");
            var video = d.Find("Prefab_UI_Settings_Page_Video");
            var gh = Deep(video, "GraphicsHeader");
            _hdr = gh.Find("SettingsHeaderBar");
            _hdrText = gh.Find("SettingsHeaderText");
            _row = Deep(video, "PresetMultipleChoice");
            _arrow = _row.Find("SettingsLeftArrow");
            _hl = Deep(d.Find("Prefab_UI_Settings_Page_JoystickControls"), "SettingsHighlightBar");
            var slider = Deep(d.Find("Prefab_UI_Settings_Page_Audio"), "MasterSlider").Find("Slider");
            _bar = Deep(slider, "Background");
            _fill = Deep(slider, "Fill");
            _pip = Deep(slider, "Handle");

            var sub = GameObject.Find(ROOT + "SettingsSubMenu_Prime_Canvas").transform;
            var sa = sub.Find("SafeArea");
            var old = sa.Find(TITLE);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var title = UnityEngine.Object.Instantiate(d.Find("SubMenuTitle").gameObject, sa, false).transform;
            title.name = TITLE;
            StyleUndo.Own(title.gameObject);
            S1Kit.Show(title);
            var tl = sa.Find("TopLeft_Group");
            StyledUI.Hide(tl, true);
            _srcTitle = tl.Find("SettingsTitle/Title_Text").GetComponent<TMP_Text>();
            _dstTitle = title.GetComponentInChildren<TMP_Text>(true);
            S1Kit.Follow(sub.gameObject, StyleFollower.SUPPRESS_TOPBAR, 0);
            for (int i = 0; i < sa.childCount; i++)
                if (sa.GetChild(i).name.EndsWith("Switchboard_Prime_Canvas") || sa.GetChild(i).name == "BFG_TextChatOptions")
                    S1Kit.Follow(sa.GetChild(i).gameObject, BOARD, 2);
            if (sub.gameObject.activeInHierarchy) for (int i = 0; i < sa.childCount; i++) if (sa.GetChild(i).gameObject.activeSelf) Board(sa.GetChild(i));
        }

        public static void Extras(UIStyle style)
        {
            var landing = GameObject.Find(ROOT + "SettingsMenu_Prime_Canvas")?.transform;
            var s1 = landing?.Find(HOST);
            if (s1 == null) return;
            var car = s1.Find("SafeArea/OptionsSubMenuCarrousel");
            var rows = landing.Find("SafeArea/CenterButtons_Group/VerticalContainer");
            var watch = S1Kit.Follow(rows.gameObject, STOCK, 1);
            for (int r = 0; r < rows.childCount; r++)
            {
                S1Kit.Follow(rows.GetChild(r).gameObject, -1, 0).Relay = watch;
                for (int i = 0; i < rows.GetChild(r).childCount; i++) S1Kit.Follow(rows.GetChild(r).GetChild(i).gameObject, -1, 0).Relay = watch;
            }
            var mapped = new HashSet<string>();
            var paired = new HashSet<string>();
            foreach (var (path, stock) in Tiles)
            {
                mapped.Add(stock.Substring(stock.IndexOf('/') + 1));
                paired.Add(path.Substring(path.IndexOf('/') + 1));
            }
            foreach (var side in new[] { "OptionsSubMenuCarrouselTop", "OptionsSubMenuCarrouselBottom" })
            {
                var dst = car.Find(side);
                for (int i = 0; i < dst.childCount; i++)
                {
                    var c = dst.GetChild(i);
                    if (!paired.Contains(c.name) && !c.name.StartsWith("BFG_Extra_")) c.gameObject.SetActive(false);
                }
            }
            var title = System.Globalization.CultureInfo.InvariantCulture.TextInfo;
            for (int r = 0; r < rows.childCount; r++)
            {
                var row = rows.GetChild(r);
                var dst = car.Find(r == 0 ? "OptionsSubMenuCarrouselTop" : "OptionsSubMenuCarrouselBottom");
                var tpl = dst.Find(r == 0 ? "OptionsSubMenuJoystickControls" : "OptionsSubMenuCredits");
                for (int i = 0; i < row.childCount; i++)
                {
                    var m = row.GetChild(i);
                    if (!m.gameObject.activeSelf || mapped.Contains(m.name) || dst.Find("BFG_Extra_" + m.name) != null) continue;
                    var tile = UnityEngine.Object.Instantiate(tpl.gameObject, dst, false).transform;
                    tile.name = "BFG_Extra_" + m.name;
                    tile.localScale = Vector3.one;
                    var icon = m.Find("Content/IconContainer/Icon")?.GetComponent<Image>();
                    if (icon != null)
                        foreach (var img in tile.GetComponentsInChildren<Image>(true))
                        {
                            if (img.name != "OptionsSubMenuIcon") continue;
                            img.sprite = icon.sprite;
                            img.preserveAspect = true;
                        }
                    var label = m.Find("Content")?.GetComponentInChildren<TMP_Text>(true)?.text;
                    S1Tiles.Pair(style, tile, m.gameObject, label == null ? null : title.ToTitleCase(label.ToLowerInvariant()));
                }
            }
            var byStock = new Dictionary<string, Transform>();
            foreach (var (path, stock) in Tiles) byStock[stock.Substring(stock.IndexOf('/') + 1)] = car.Find(path);
            foreach (var side in new[] { "OptionsSubMenuCarrouselTop", "OptionsSubMenuCarrouselBottom" })
            {
                var dst = car.Find(side);
                for (int i = 0; i < dst.childCount; i++)
                    if (dst.GetChild(i).name.StartsWith("BFG_Extra_")) byStock[dst.GetChild(i).name.Substring(10)] = dst.GetChild(i);
            }
            for (int r = 0; r < rows.childCount; r++)
            {
                var row = rows.GetChild(r);
                var dst = car.Find(r == 0 ? "OptionsSubMenuCarrouselTop" : "OptionsSubMenuCarrouselBottom");
                int at = 0;
                for (int i = 0; i < row.childCount; i++)
                {
                    if (!byStock.TryGetValue(row.GetChild(i).name, out var t) || t == null) continue;
                    t.SetParent(dst, false);
                    t.SetSiblingIndex(at++);
                    t.gameObject.SetActive(row.GetChild(i).gameObject.activeSelf);
                }
            }
            var tabs = new List<RectTransform>();
            foreach (var side in new[] { "OptionsSubMenuCarrouselTop", "OptionsSubMenuCarrouselBottom" })
            {
                var dst = car.Find(side);
                for (int i = 0; i < dst.childCount; i++) if (dst.GetChild(i).gameObject.activeSelf) tabs.Add(dst.GetChild(i).GetComponent<RectTransform>());
            }
            s1.GetComponent<FGClient.SettingsTweenMenu>()._tabs = new Il2CppReferenceArray<RectTransform>(tabs.ToArray());
        }

        public static void Teardown() => _done.Clear();

        public static void Board(Transform sw)
        {
            if (_backing == null || sw == null) return;
            if (_srcTitle != null) _dstTitle.text = _srcTitle.text.Trim().ToUpperInvariant();
            if (_done.Add(sw.GetInstanceID()))
            {
                S1Kit.Image(sw.Find("Panel_Fill"), _backing);
                S1Kit.Off(sw.Find("Panel_Outline"));
                var mask = sw.Find("Mask")?.GetComponent<Image>();
                StyleUndo.Graphic(mask);
                if (mask != null) { mask.sprite = _backing.GetComponent<Image>().sprite; mask.type = Image.Type.Sliced; }
                var desc = sw.Find("DescriptionPanel");
                if (desc != null)
                {
                    S1Kit.Image(desc.Find("DescriptionPanelFill"), _backing);
                    S1Kit.Off(desc.Find("DescriptionPanel_Outline"));
                }
            }
            var content = sw.Find("Mask/TabScrollContainer/TabViewport/TabContent");
            if (content == null) return;
            for (int r = 0; r < content.childCount; r++) Row(content.GetChild(r));
        }

        static void Bar(Transform left, Transform right, Transform donor, bool stretch)
        {
            S1Kit.Off(left);
            if (right == null) return;
            S1Kit.Image(right, donor);
            StyleUndo.Rect(right);
            var rt = right.GetComponent<RectTransform>();
            if (stretch)
            {
                rt.anchorMin = new Vector2(0f, 0.1f);
                rt.anchorMax = new Vector2(1f, 0.9f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                return;
            }
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = donor.GetComponent<RectTransform>().sizeDelta;
        }

        static void Row(Transform row)
        {
            if (!_done.Add(row.GetInstanceID())) return;
            var sel = row.Find("SettingsSelectionHighlight");
            if (sel != null) Bar(sel.Find("SettingsHeaderBarLeft"), sel.Find("SettingsHeaderBarRight"), _hl, true);
            else if (row.Find("SettingsHeaderBarRight") != null) Bar(row.Find("SettingsHeaderBarLeft"), row.Find("SettingsHeaderBarRight"), _hdr, false);
            var ht = row.Find("SettingsHeaderText");
            if (ht != null)
            {
                S1Kit.Text(ht, _hdrText);
                StyleUndo.Rect(ht);
                ht.GetComponent<RectTransform>().anchoredPosition = new Vector2(32f, 0f);
            }
            S1Kit.Text(row.Find("SettingsText"), _row.Find("SettingsText"));
            foreach (var vt in row.GetComponentsInChildren<TMP_Text>(true))
            {
                if (vt.name != "SettingsValueText") continue;
                S1Kit.Text(vt.transform, _row.Find("SettingsValueText"));
                vt.enableWordWrapping = false;
                vt.overflowMode = TextOverflowModes.Overflow;
            }
            foreach (var img in row.GetComponentsInChildren<Image>(true))
            {
                var n = img.name;
                if (n == "LeftArrow" || n == "RightArrow")
                {
                    StyleUndo.Graphic(img);
                    img.sprite = _arrow.GetComponent<Image>().sprite;
                    img.preserveAspect = true;
                    img.color = new Color(1f, 1f, 1f, 0.5f);
                }
                else if (img.GetComponentInParent<Slider>() == null) continue;
                else if (n == "Background") S1Kit.Image(img.transform, _bar);
                else if (n == "Fill") S1Kit.Image(img.transform, _fill);
                else if (n == "Handle")
                {
                    S1Kit.Image(img.transform, _pip);
                    img.preserveAspect = true;
                }
            }
        }
    }
}
