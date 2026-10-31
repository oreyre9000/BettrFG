using System;
using BetterFG.Nametag;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Tab
{
    public class NametagCrownTab : NametagWizardTab
    {
        public NametagCrownTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Nametag - Crown Rank";

        private static readonly Color SEL_COLOR = new Color(0.25f, 0.5f, 0.25f, 1f);

        private Text _crownEnabledLabel, _crownRecolourOnLabel, _crownSwapLabel;
        private InputField _crownRankField;
        private RawImage _crownPreview;
        private float _crMainR, _crMainG, _crMainB;
        private float _crHiR, _crHiG, _crHiB;
        private float _crOutR, _crOutG, _crOutB;

        protected override string[] StepTitles => new[] { "Crown rank", "Crown colours", "Outline colour" };

        void Awake()
        {
            var mc = CrownRankService.MainColour;
            var hc = CrownRankService.HighlightColour;
            var oc = CrownRankService.OutlineColour;
            _crMainR = mc.r; _crMainG = mc.g; _crMainB = mc.b;
            _crHiR = hc.r; _crHiG = hc.g; _crHiB = hc.b;
            _crOutR = oc.r; _crOutG = oc.g; _crOutB = oc.b;
        }

        protected override void BuildStep(int step, RectTransform root, float w, float bodyH)
        {
            var (_, c) = UGUIShip.CreateScrollView(root, new Rect(0f, 0f, TabWidth, bodyH));
            float cw = w - 26f;
            switch (step)
            {
                case 0: BuildRankStep(c, cw); break;
                case 1: BuildColoursStep(c, cw); break;
                case 2: BuildOutlineStep(c, cw); break;
            }
        }

        private void BuildRankStep(RectTransform c, float w)
        {
            float x = PAD, cy = PAD;
            float bh = BTN_H * 0.85f;

            bool en = CrownRankService.Enabled;
            var enBtn = UGUIShip.CreateButton(c, new Rect(x, cy, w, bh),
                en ? "Crown rank: ON" : "Crown rank: OFF",
                en ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(OnToggleCrownEnabled));
            _crownEnabledLabel = enBtn.GetComponentInChildren<Text>();
            cy += bh + PAD;

            UGUIShip.CreatePanel(c, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            float rankLblW = w * 0.42f;
            UGUIShip.CreateLabel(c, new Rect(x, cy, rankLblW, bh), "RANK TEXT", FS_SM, HINT, TextAnchor.MiddleLeft);
            _crownRankField = UGUIShip.CreateInputField(c, new Rect(x + rankLblW + PAD, cy, w - rankLblW - PAD, bh),
                "rank text", new Color(0.12f, 0.12f, 0.12f, 1f), WHITE, FS_SM);
            UGUIShip.SetInputText(_crownRankField, CrownRankService.RankText, false);
            _crownRankField.onEndEdit.AddListener(new Action<string>(OnCrownRankEdited));
            cy += bh + PAD;

            bool swap = CrownRankService.SwapSide;
            var swapBtn = UGUIShip.CreateButton(c, new Rect(x, cy, w, bh),
                swap ? "Crown side: LEFT" : "Crown side: RIGHT",
                swap ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(OnToggleCrownSwap));
            _crownSwapLabel = swapBtn.GetComponentInChildren<Text>();
            cy += bh + PAD;
            c.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void BuildColoursStep(RectTransform c, float w)
        {
            float x = PAD, cy = PAD;
            float bh = BTN_H * 0.85f;

            bool recol = CrownRankService.RecolourOn;
            var recolBtn = UGUIShip.CreateButton(c, new Rect(x, cy, w, bh),
                recol ? "Recolour: ON" : "Recolour: OFF",
                recol ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(OnToggleCrownRecolour));
            _crownRecolourOnLabel = recolBtn.GetComponentInChildren<Text>();
            cy += bh + PAD;

            float prevW = w * 0.28f;
            float slidersW = w - prevW - PAD;
            float prevX = x + slidersW + PAD;
            float prevStartY = cy;

            var prevGo = new GameObject("CrownPreview");
            prevGo.transform.SetParent(c, false);
            UGUIShip.SetPixelRect(prevGo.AddComponent<RectTransform>(),
                new Rect(prevX, cy, prevW, (LH + SH) * 6f + LH));
            prevGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.3f);
            var prevTexGo = new GameObject("Raw");
            prevTexGo.transform.SetParent(prevGo.transform, false);
            var prtRt = prevTexGo.AddComponent<RectTransform>();
            prtRt.anchorMin = Vector2.zero; prtRt.anchorMax = Vector2.one;
            prtRt.offsetMin = prtRt.offsetMax = Vector2.zero;
            _crownPreview = prevTexGo.AddComponent<RawImage>();
            _crownPreview.raycastTarget = false;

            UGUIShip.CreateLabel(c, new Rect(x, cy, slidersW, LH), "CROWN COLOUR", FS_SM, HINT);
            cy += LH + SH;
            UGUIShip.CreateColorControls(c, x, ref cy, slidersW,
                () => _crMainR, () => _crMainG, () => _crMainB,
                v => _crMainR = v, v => _crMainG = v, v => _crMainB = v, () => { RefreshCrownPreview(); RefreshPreview(); },
                out _, out _, out _, new Color(1f, 0.55f, 0.1f));

            UGUIShip.CreateLabel(c, new Rect(x, cy, slidersW, LH), "HIGHLIGHT COLOUR", FS_SM, HINT);
            cy += LH + SH;
            UGUIShip.CreateColorControls(c, x, ref cy, slidersW,
                () => _crHiR, () => _crHiG, () => _crHiB,
                v => _crHiR = v, v => _crHiG = v, v => _crHiB = v, () => { RefreshCrownPreview(); RefreshPreview(); },
                out _, out _, out _, new Color(1f, 0.92f, 0.55f));

            RefreshCrownPreview();
            cy = Mathf.Max(cy, prevStartY + (LH + SH) * 6f + LH) + PAD;
            c.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void BuildOutlineStep(RectTransform c, float w)
        {
            float x = PAD, cy = PAD;
            UGUIShip.CreateLabel(c, new Rect(x, cy, w, LH), "OUTLINE COLOUR", FS_SM, HINT);
            cy += LH + SH;
            UGUIShip.CreateColorControls(c, x, ref cy, w,
                () => _crOutR, () => _crOutG, () => _crOutB,
                v => _crOutR = v, v => _crOutG = v, v => _crOutB = v, () => RefreshPreview(),
                out _, out _, out _, Color.black);
            c.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void RefreshCrownPreview()
        {
            if (_crownPreview == null) return;
            const int W = 4, H = 32;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var main = new Color(_crMainR, _crMainG, _crMainB);
            var hi = new Color(_crHiR, _crHiG, _crHiB);
            for (int row = 0; row < H; row++)
            {
                var col = row < H / 2 ? hi : main;
                for (int c = 0; c < W; c++) tex.SetPixel(c, row, col);
            }
            tex.Apply();
            _crownPreview.texture = tex;
            _crownPreview.color = Color.white;
        }

        private void OnToggleCrownEnabled()
        {
            bool on = !CrownRankService.Enabled;
            CrownRankService.SetEnabled(on);
            if (_crownEnabledLabel != null) _crownEnabledLabel.text = on ? "Crown rank: ON" : "Crown rank: OFF";
            UGUIShip.SetButtonSelected(_crownEnabledLabel?.transform.parent?.GetComponent<Button>(), on, SEL_COLOR);
            CrownRankService.ApplyLocal();
            RefreshPreview();
        }

        private void OnCrownRankEdited(string val)
        {
            CrownRankService.RankText = val ?? "";
            CrownRankService.SetTextOn(!string.IsNullOrEmpty(val));
            CrownRankService.SetEnabled(true);
            CrownRankService.ApplyLocal();
            RefreshPreview();
        }

        private void OnToggleCrownRecolour()
        {
            bool on = !CrownRankService.RecolourOn;
            CrownRankService.SetRecolourOn(on);
            if (_crownRecolourOnLabel != null) _crownRecolourOnLabel.text = on ? "Recolour: ON" : "Recolour: OFF";
            UGUIShip.SetButtonSelected(_crownRecolourOnLabel?.transform.parent?.GetComponent<Button>(), on, SEL_COLOR);
            CrownRankService.ApplyLocal();
            RefreshPreview();
        }

        private void OnToggleCrownSwap()
        {
            bool on = !CrownRankService.SwapSide;
            CrownRankService.SetSwapSide(on);
            if (_crownSwapLabel != null) _crownSwapLabel.text = on ? "Crown side: LEFT" : "Crown side: RIGHT";
            UGUIShip.SetButtonSelected(_crownSwapLabel?.transform.parent?.GetComponent<Button>(), on, SEL_COLOR);
            if (SettingsService.Get("nametag.icon.mode", "none") != "none")
                NametagIconApplicator.ApplyIcon();
            CrownRankService.ApplyLocal();
            RefreshPreview();
        }

        public override void RefreshPreview()
        {
            var cfg = NametagIconApplicator.CfgFromSettings();
            var crownCfg = CrownRankService.CfgFromSettings();
            crownCfg.main = new Color(_crMainR, _crMainG, _crMainB);
            crownCfg.highlight = new Color(_crHiR, _crHiG, _crHiB);
            crownCfg.outline = new Color(_crOutR, _crOutG, _crOutB);
            ApplyPreview(cfg, crownCfg, NametagPreview.PlatformHideFromSettings(), NametagPreview.PlatformCustomFromSettings());
        }

        protected override bool Save()
        {
            CrownRankService.MainColour = new Color(_crMainR, _crMainG, _crMainB);
            CrownRankService.HighlightColour = new Color(_crHiR, _crHiG, _crHiB);
            CrownRankService.OutlineColour = new Color(_crOutR, _crOutG, _crOutB);
            CrownRankService.InvalidateCache();
            CrownRankService.ApplyLocal();
            return true;
        }

        protected override void OnRemoveClicked()
        {
            CrownRankService.SetEnabled(false);
            CrownRankService.SetTextOn(false);
            CrownRankService.SetRecolourOn(false);
            CrownRankService.ApplyLocal();
        }
    }
}
