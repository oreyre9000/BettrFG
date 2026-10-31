using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace BetterFG.Customization.UI.Styles.Season1
{
    internal static class S1Ingame
    {
        public const int BANNER = 11;
        const string OVERLAY = "client_Prefab_UI_Client_Game_Overlay";
        const string HOST = "BFG_S1Banner";

        static readonly HashSet<string> HudNames = new HashSet<string>
        {
            "BigTimerContainer", "SmallTimerContainer", "QualifiedEliminated", "ObjectiveContainer", "TimeRemaining",
            "ProgressCount", "ProgressSubText", "ObjectiveLevel", "ObjectiveState", "ScoreContext", "GameplayCountdown",
            "BumperDescription", "SelectedItem", "TeamSprite",
        };

        static readonly Dictionary<string, (string Layout, string Anim, string Text, string[] Vfx)> Banners =
            new Dictionary<string, (string, string, string, string[])>
            {
                { "QualifiedScreenViewModel", ("Prefab_UI_Qualified", "Qualified", "Container/result-GPR/text-QUALIFIED", new[] { "Qualified_Prefab_Drops_VFX2:Container/Prefab_Drops_VFX2", "Qualified_Prefab_Drops_VFX2(1):Container/Prefab_Drops_VFX2 (1)" }) },
                { "EliminatedScreenViewModel", ("Prefab_UI_Eliminated", "Eliminated", "Container/result-GPR/text-ELIMINATED", new[] { "Eliminated_Prefab_Drops_VFX2:Container/Prefab_Drops_VFX2", "Eliminated_ZizZagLat:Container/ZizZagLat" }) },
                { "RoundEndedScreenViewModel", ("Prefab_UI_Round_Ended", "RoundOver", "Container/round-over-GPR/text-ROUND", new[] { "RoundOver_Prefab_Drops_VFX2:Container/Prefab_Drops_VFX2" }) },
            };

        public static void Screen(UIStyle style, Component screen)
        {
            var name = screen.GetIl2CppType().Name;
            if (!Banners.TryGetValue(name, out var b)) return;
            var root = screen.transform;
            if (root.Find(HOST) != null) return;
            var s1 = StyleLayout.Build(style.Assets, b.Layout, root);
            s1.name = HOST;
            StyleUndo.Own(s1);
            S1Kit.Stretch(s1.transform);
            s1.transform.SetSiblingIndex(1);
            foreach (var p in new[] { "Container/Badge", "Container/Prefab_QualifiedBadgeViewModel", "Container/Container_Buttons_OLD", "SafeArea" })
                s1.transform.Find(p)?.gameObject.SetActive(false);
            var bg = s1.GetComponent<UnityEngine.UI.Image>();
            if (bg != null) bg.enabled = false;
            var container = root.Find("Container");
            if (container != null)
                for (int i = 0; i < container.childCount; i++)
                {
                    var c = container.GetChild(i);
                    if (c.name == "Layout" || c.name.Contains("Badge")) continue;
                    StyledUI.Hide(c, true);
                }
            StyleAnim.Load(style.Assets, s1, b.Anim);
            foreach (var v in b.Vfx)
            {
                var parts = v.Split(':');
                var t = s1.transform.Find(parts[1]);
                if (t != null) StyleAnim.Load(style.Assets, t.gameObject, parts[0]);
            }
            s1.SetActive(true);
            S1Kit.Follow(s1, BANNER, 2);
        }

        public static void Settled(Transform s1)
        {
            var root = s1.parent;
            foreach (var c in root.GetComponents<Component>())
            {
                if (!Banners.TryGetValue(c.GetIl2CppType().Name, out var b)) continue;
                var src = root.Find(b.Text)?.GetComponent<TMP_Text>();
                if (src != null)
                    foreach (var t in s1.Find(b.Text).parent.GetComponentsInChildren<TMP_Text>(true)) t.text = src.text;
                foreach (var a in s1.GetComponentsInChildren<Animation>(true)) a.Play();
                return;
            }
        }

        public static void Hud(UIStyle style, Transform root)
        {
            S1Skins.ByName(root, S1Kit.Donor(style, OVERLAY), HudNames);
            S1Skins.Broad(style, root);
        }

        public static void Pause(UIStyle style, Transform root)
        {
            var opts = root.Find("Generic_UI_InGameMenu_Options/Container_Options");
            if (opts == null) return;
            var s1 = S1Kit.Donor(style, OVERLAY).Find("PB_UI_InGameMenu/SubMenuOptions");
            var panel = opts.Find("Container_Panel");
            if (panel != null)
            {
                S1Kit.Image(panel.Find("Panel_Fill"), s1.Find("OptionsBacking/Background"));
                S1Kit.Off(panel.Find("Panel_Shadow"));
                S1Kit.Off(panel.Find("Panel_Stroke"));
                S1Kit.Image(panel.Find("Pattern_Mask (1)"), s1.Find("SubMenuLeavePatternMask"));
                S1Kit.Image(panel.Find("Pattern_Mask (1)/Pattern_Zigzag"), s1.Find("SubMenuLeavePatternMask/SubMenuLeaveIcon"));
                S1Kit.Image(panel.Find("Divider"), s1.Find("SubMenuLeaveLine"));
                S1Kit.Text(panel.Find("Party_Menu_Text (1)"), s1.Find("SubMenuTitle/Title"));
            }
            var rows = opts.Find("ButtonsCarrousel");
            if (rows != null)
                for (int i = 0; i < rows.childCount; i++)
                {
                    var row = rows.GetChild(i);
                    var src = s1.Find(row.name.Contains("Leave") ? "CarrouselOptions/LeaveGame" : "CarrouselOptions/Resume");
                    var act = row.Find("Container_Active");
                    if (act != null)
                    {
                        S1Kit.Image(act.Find("Button_Fill"), src.Find("CustomiserSubMenuDisabled"));
                        S1Kit.Off(act.Find("Button_Stroke"));
                        foreach (var t in act.GetComponentsInChildren<TMP_Text>(true)) S1Kit.Text(t.transform, src.Find("CustomiserSubMenuDisabled/CustomiserSubMenuText"));
                    }
                    var hi = row.Find("Container_Highlight");
                    if (hi == null) continue;
                    S1Kit.Image(hi.Find("Button_Fill"), src.Find("CustomiserSubMenuActive"));
                    S1Kit.Off(hi.Find("Button_Stroke"));
                    S1Kit.Off(hi.Find("Button_Shadow"));
                    S1Kit.Off(hi.Find("Button_Highlight"));
                    foreach (var t in hi.GetComponentsInChildren<TMP_Text>(true)) S1Kit.Text(t.transform, src.Find("CustomiserSubMenuActive/CustomiserSubMenuText"));
                }
            S1Skins.Broad(style, root);
        }
    }
}
