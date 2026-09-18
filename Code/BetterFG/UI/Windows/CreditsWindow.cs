using System;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;
using BetterFG.Services;

namespace BetterFG.UI.Windows
{
    public class CreditsWindow : SideWindow
    {
        public CreditsWindow(IntPtr ptr) : base(ptr) { }

        protected override float WindowWidth => 280f;
        protected override float WindowHeight => 220f;
        protected override string WindowTitle => "ui.bettrfg_credits";

        private const float ROW_H = 22f;
        private const float HEADER_GAP = 10f;
        private const float HEADER_H = 18f + HEADER_GAP;
        private const float HEADER_LEFT = SideWindow.RowLabelX;
        private const float HEADER_SCALE = 1.3f;
        private const float ROW_W = 280f - 2f * UGUIShip.SCROLLBAR_INSET + SideWindow.RowsLeftShift;
        private const float LABEL_W = ROW_W - SideWindow.RowLabelX - SideWindow.RowRightPad;
        private static readonly Color ROW_EVEN = new Color(1f, 1f, 1f, 0.03f);
        private static readonly Color ROW_ODD = new Color(0f, 0f, 0f, 0f);

        private struct Section
        {
            public string Title;
            public string[] Names;
        }

        private static readonly Section[] Sections =
        {
            new Section { Title = "credits.section_mod_testers", Names = new[]
            {
                "Lifmo", "08bot/TG", "Pana Hot Dog", "Drift Bone", "dxzhy",
                "windos8pro", "BingBong", "Dhi_2007", "abab", "NPGG",
                "FoxInCharge", "theblums",
            }},
            new Section { Title = "credits.section_scripting", Names = new[]
            {
                "Floyzi - {credits.desc_all_cosmetics}",
                "El Pana Hot Dog - {credits.desc_localization_editor}",
            }},
            new Section { Title = "credits.section_3d_modeling", Names = new[]
            {
                "BurntApple - {credits.desc_scrapyard_background}",
                "ArenaCloser12 - {credits.desc_volcano_background}",
            }},
            new Section { Title = "credits.section_localization", Names = new[]
            {
                "El Pana Hot Dog - {credits.desc_spanish}",
                "IceApple2910 - {credits.desc_korean}",
                "08bot/TG - {credits.desc_korean}",
            }},
        };

        protected override void BuildContent(RectTransform contentRoot)
        {
            BgPosition = new Vector3(139.3993f, 74.9552f, 0f);
            BgScale = new Vector3(1.3415f, 4.5877f, 1f);
            ContentPosition = new Vector3(-1.6132f, -17.32f, 0f);
            ContentScale = new Vector3(1.0473f, 1.04f, 1f);
            ContentOffsetMin = new Vector2(-1.6132f, -25.92f);
            ContentOffsetMax = new Vector2(-1.6132f, -18.72f);
            Pivot = new Vector2(0f, 0.5f);
            TitlePosition = new Vector3(20.0368f, -6.7966f, 0f);
            TitleScale = new Vector3(1.1818f, 1.3491f, 1f);

            var scroll = UGUIShip.CreateScrollView(contentRoot,
                new Rect(0f, 0f, WindowWidth, WindowHeight - TITLE_H));
            var scrollRt = scroll.scrollRect.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = Vector2.zero;
            scrollRt.offsetMax = Vector2.zero;

            var listRt = scroll.content;
            var vlg = listRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 0f;
            var csf = listRt.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            int rowIdx = 0;
            foreach (var section in Sections)
            {
                BuildHeader(listRt, section.Title);
                foreach (var name in section.Names)
                {
                    BuildRow(listRt, name, rowIdx % 2 == 0 ? ROW_EVEN : ROW_ODD);
                    rowIdx++;
                }
            }
        }

        private static void BuildHeader(RectTransform parent, string title)
        {
            float gap = parent.childCount == 0 ? 0f : HEADER_GAP;
            var rowGo = new GameObject("Header_" + title);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = HEADER_H - HEADER_GAP + gap;
            le.flexibleWidth = 1f;

            var lbl = UGUIShip.CreateLabel(rowGo.transform,
                new Rect(HEADER_LEFT, 0f, 200f, HEADER_H),
                title, 10, Color.white, TextAnchor.MiddleLeft);
            lbl.fontStyle = FontStyle.Bold;
            var rt = lbl.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(HEADER_LEFT, -gap * 0.5f);
            rt.localScale = new Vector3(HEADER_SCALE, HEADER_SCALE, 1f);
        }

        private static readonly Regex LocTokenPattern = new Regex(@"\{([^{}]+)\}");

        private static string ResolveLocTokens(string text) =>
            LocTokenPattern.Replace(text, m => LocalizationService.Get(m.Groups[1].Value));

        private static void BuildRow(RectTransform parent, string name, Color bg)
        {
            var rowGo = new GameObject("Row_" + name);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            UGUIShip.PaintStaticRowFill(rowGo, bg);

            var lbl = UGUIShip.CreateLabel(rowGo.transform,
                new Rect(SideWindow.RowLabelX, 0f, LABEL_W, ROW_H),
                ResolveLocTokens(name).Replace(" - ", "\n"), 12,
                new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);
            lbl.horizontalOverflow = HorizontalWrapMode.Wrap;
            lbl.verticalOverflow = VerticalWrapMode.Overflow;

            float h = Mathf.Max(ROW_H, UGUIShip.PreferredHeight(lbl, LABEL_W) + 6f);
            le.preferredHeight = h;
            lbl.rectTransform.sizeDelta = new Vector2(LABEL_W, h);
        }
    }
}
