using System;
using BetterFG.Core;
using BetterFG.Nametag;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Windows
{
    public class PlayerdetailsWindow : SideWindow
    {
        public PlayerdetailsWindow(IntPtr ptr) : base(ptr) { }

        public static PlayerdetailsWindow Instance { get; private set; }

        // exactly TweaksWindow's frame — same size, bg, title.
        protected override float WindowWidth => 300f;
        protected override float WindowHeight => 86f;
        protected override string WindowTitle => "ui.player_details";

        private const string KEY_ENABLED = "nametag.enabled";

        private InputField _nameField;

        protected override void BuildContent(RectTransform contentRoot)
        {
            Instance = this;

            // every transform value below is copied verbatim from TweaksWindow so the two windows match.
            BgPosition = new Vector3(150.8f, 26.2f, 0f);
            BgScale = new Vector3(1.3254f, 3.233f, 1f);
            ContentPosition = new Vector3(-1.6132f, -78.6f, 0f);
            ContentScale = new Vector3(1.0473f, 1.04f, 1f);
            ContentOffsetMin = new Vector2(-1.6132f, -25.92f);
            ContentOffsetMax = new Vector2(-1.6132f, -18.72f);
            Pivot = new Vector2(0f, 0.5f);
            TitlePosition = new Vector3(20.0368f, -24.1f, 0f);
            TitleScale = new Vector3(1.1818f, 1.3491f, 1f);

            PlayerDetailsRows.Build(contentRoot, this);
        }

        protected override void BuildTitleExtras(Transform titleRoot)
        {
            titleRoot.localPosition = new Vector3(173.9044f, 53.5891f, 0f);
        }

        internal void SetNameField(InputField f) => _nameField = f;

        // ── Apply handlers ─────────────────────────────────────────────────────

        internal void ApplyName(string val)
        {
            LocalPlayerInfo.CustomName = val ?? "";
            NametagFinder.ReapplyAllNameplates();
            if (SettingsService.Get(KEY_ENABLED, "false") == "true")
                NametagIconApplicator.ApplyNametag();
            if (CrownRankService.Enabled)
                CrownRankService.ApplyLocal();
        }
    }

    internal static class PlayerDetailsRows
    {
        private const float ROW_H = 24f;
        private const float PAD = 6f;
        private const float HEADER_H = 18f;
        private const float LABEL_X = SideWindow.RowLabelX;
        private const float LABEL_W = 64f;
        private const float BTN_W = 66f;
        private const float CTRL_H = 18f;

        private static readonly Color BTN_COL = new Color(0.25f, 0.45f, 0.75f, 1f);

        public static void Build(RectTransform parent, PlayerdetailsWindow win)
        {
            var header = UGUIShip.CreateLabel(parent, new Rect(0f, 0f, 200f, HEADER_H),
                "ui.custom_name", 10, Color.white, TextAnchor.MiddleLeft);
            header.fontStyle = FontStyle.Bold;
            var hrt = header.rectTransform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0f, 1f);
            hrt.pivot = new Vector2(0f, 1f);
            hrt.anchoredPosition = new Vector2(LABEL_X, 0f);
            hrt.localScale = new Vector3(1.3f, 1.3f, 1f);

            var rowGo = new GameObject("Row_ui.name");
            rowGo.transform.SetParent(parent, false);
            var rowRt = rowGo.AddComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0f, 1f);
            rowRt.anchorMax = new Vector2(1f, 1f);
            rowRt.pivot = new Vector2(0.5f, 1f);
            rowRt.anchoredPosition = new Vector2(0f, -HEADER_H - 2f);
            rowRt.sizeDelta = new Vector2(0f, ROW_H);
            UGUIShip.PaintStaticRowFill(rowGo, new Color(1f, 1f, 1f, 0.03f));

            var lbl = UGUIShip.CreateLabel(rowGo.transform, new Rect(0f, 0f, LABEL_W, ROW_H),
                "ui.name", 13, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);
            var lrt = lbl.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(0f, 1f);
            lrt.pivot = new Vector2(0f, 0.5f);
            lrt.anchoredPosition = new Vector2(LABEL_X, 0f);
            lrt.sizeDelta = new Vector2(LABEL_W, 0f);

            var fieldGo = new GameObject("Field");
            fieldGo.transform.SetParent(rowGo.transform, false);
            var frt = fieldGo.AddComponent<RectTransform>();
            frt.anchorMin = new Vector2(0f, 0.5f);
            frt.anchorMax = new Vector2(1f, 0.5f);
            frt.pivot = new Vector2(0.5f, 0.5f);
            frt.offsetMin = new Vector2(LABEL_X + LABEL_W, -CTRL_H / 2f);
            frt.offsetMax = new Vector2(-(PAD + BTN_W + 3f), CTRL_H / 2f);
            var field = UGUIShip.CreateInputField(fieldGo.transform, new Rect(0f, 0f, 100f, CTRL_H),
                "", new Color(0.12f, 0.12f, 0.12f, 1f), Color.white, 12);
            var inner = field.GetComponent<RectTransform>();
            inner.anchorMin = Vector2.zero;
            inner.anchorMax = Vector2.one;
            inner.offsetMin = inner.offsetMax = Vector2.zero;
            UGUIShip.SetInputText(field, LocalPlayerInfo.CustomName ?? "", false);
            field.onEndEdit.AddListener(new Action<string>(win.ApplyName));
            win.SetNameField(field);

            var apply = UGUIShip.CreateButton(rowGo.transform, new Rect(0f, 0f, BTN_W, CTRL_H),
                "ui.apply", BTN_COL, Color.white, 11);
            var art = apply.GetComponent<RectTransform>();
            art.anchorMin = art.anchorMax = new Vector2(1f, 0.5f);
            art.pivot = new Vector2(1f, 0.5f);
            art.anchoredPosition = new Vector2(-PAD, 0f);
            art.sizeDelta = new Vector2(BTN_W, CTRL_H);
            apply.onClick.AddListener(new Action(() => win.ApplyName(field.text)));
        }
    }
}
