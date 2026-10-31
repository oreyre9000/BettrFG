using System;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public partial class UIForegroundDetailTab
    {
        private readonly BannerPreviewClone _previewClone = new BannerPreviewClone();

        private class BannerColourUI
        {
            public bool on;
            public float r, g, b;
            public Button toggleBtn;
            public Image swatch;
            public Image areaBg;
        }

        private class BannerSlotUI
        {
            public Customization.UI.MenuCustomizationApplication.BannerBucket bucket;
            public string label;
            public string keyPrefix;
            public float dr, dg, db;
            public readonly BannerColourUI ui = new BannerColourUI();
        }

        private class BannerDef
        {
            public System.Collections.Generic.List<BannerSlotUI> slots;
            public BannerSlotUI highlight;
            public Transform viewport;
            public string enabledKey;
            public bool enabled;
            public Button enabledBtn;
        }

        private System.Collections.Generic.Dictionary<UIForegroundKind, BannerDef> _bannerDefs;

        private static void SetBannerToggle(BannerColourUI ch, bool on)
        {
            ch.on = on;
            var lbl = ch.toggleBtn?.GetComponentInChildren<Text>();
            if (lbl != null) UGUIShip.RelabelText(lbl, on ? "ui.on" : "ui.off");
            UGUIShip.SetButtonSelected(ch.toggleBtn, on, SEL_COLOR);
        }

        private BannerSlotUI MkSlot(Customization.UI.MenuCustomizationApplication.BannerBucket bucket,
            string label, string keyPrefix, float dr, float dg, float db)
        {
            var s = new BannerSlotUI { bucket = bucket, label = label, keyPrefix = keyPrefix, dr = dr, dg = dg, db = db };
            s.ui.r = dr; s.ui.g = dg; s.ui.b = db;
            return s;
        }

        private BannerDef GetBannerDef(UIForegroundKind what)
        {
            if (_bannerDefs == null) BuildBannerDefs();
            return _bannerDefs.TryGetValue(what, out var d) ? d : null;
        }

        private void BuildBannerDefs()
        {
            var B = Customization.UI.MenuCustomizationApplication.BannerBucket.Black;
            var W = Customization.UI.MenuCustomizationApplication.BannerBucket.White;
            var C = Customization.UI.MenuCustomizationApplication.BannerBucket.Cyan;
            var P = Customization.UI.MenuCustomizationApplication.BannerBucket.Pink;
            var Y = Customization.UI.MenuCustomizationApplication.BannerBucket.Yellow;
            var O = Customization.UI.MenuCustomizationApplication.BannerBucket.Orange;
            var Bl = Customization.UI.MenuCustomizationApplication.BannerBucket.Blue;
            var BG = Customization.UI.MenuCustomizationApplication.BannerBucket.BlackGrey;

            _bannerDefs = new System.Collections.Generic.Dictionary<UIForegroundKind, BannerDef>
            {
                [UIForegroundKind.Qualified] = new BannerDef
                {
                    slots = new System.Collections.Generic.List<BannerSlotUI>
                    {
                        MkSlot(C, "ui.cyan_replacement",  "menu.banner.qual.cyan",  0f,    0.78f, 1f),
                        MkSlot(P, "ui.pink_replacement",  "menu.banner.qual.pink",  1f,    0.2f,  0.5f),
                        MkSlot(B, "ui.black_replacement", "menu.banner.qual.black", 0.08f, 0.08f, 0.08f),
                        MkSlot(W, "ui.white_replacement", "menu.banner.qual.white", 1f,    1f,    1f),
                    },
                    highlight = MkSlot(W, "ui.highlight_replacement", "menu.banner.qual.highlight", 1f, 1f, 1f),
                    enabledKey = Customization.UI.MenuCustomizationApplication.KEY_BANNER_QUAL_ENABLED,
                },
                [UIForegroundKind.Eliminated] = new BannerDef
                {
                    slots = new System.Collections.Generic.List<BannerSlotUI>
                    {
                        MkSlot(C, "ui.cyan_replacement",  "menu.banner.elim.cyan",  0f,    0.78f, 1f),
                        MkSlot(P, "ui.pink_replacement",  "menu.banner.elim.pink",  1f,    0.2f,  0.5f),
                        MkSlot(B, "ui.black_replacement", "menu.banner.elim.black", 0.08f, 0.08f, 0.08f),
                        MkSlot(W, "ui.white_replacement", "menu.banner.elim.white", 1f,    1f,    1f),
                    },
                    highlight = MkSlot(W, "ui.highlight_replacement", "menu.banner.elim.highlight", 1f, 1f, 1f),
                    enabledKey = Customization.UI.MenuCustomizationApplication.KEY_BANNER_ELIM_ENABLED,
                },
                [UIForegroundKind.Winner] = new BannerDef
                {
                    slots = new System.Collections.Generic.List<BannerSlotUI>
                    {
                        MkSlot(Y, "ui.yellow_replacement", "menu.banner.win.yellow", 1f,    0.85f, 0f),
                        MkSlot(O, "ui.orange_replacement", "menu.banner.win.orange", 1f,    0.55f, 0.1f),
                        MkSlot(W,  "ui.white_replacement", "menu.banner.win.white",  1f,    1f,    1f),
                        MkSlot(BG, "ui.black_replacement", "menu.banner.win.black",  0.08f, 0.08f, 0.08f),
                    },
                    highlight = MkSlot(W, "ui.highlight_replacement", "menu.banner.win.highlight", 1f, 1f, 1f),
                    enabledKey = Customization.UI.MenuCustomizationApplication.KEY_BANNER_WIN_ENABLED,
                },
                [UIForegroundKind.RoundOver] = new BannerDef
                {
                    slots = new System.Collections.Generic.List<BannerSlotUI>
                    {
                        MkSlot(BG, "ui.black_replacement", "menu.banner.round.black", 0.08f, 0.08f, 0.08f),
                        MkSlot(P,  "ui.pink_replacement",  "menu.banner.round.pink",  1f,    0.2f,  0.5f),
                        MkSlot(C,  "ui.cyan_replacement",  "menu.banner.round.blue",  0f,    0.78f, 1f),
                        MkSlot(W,  "ui.white_replacement", "menu.banner.round.white", 1f,    1f,    1f),
                    },
                    highlight = MkSlot(W, "ui.highlight_replacement", "menu.banner.round.highlight", 1f, 1f, 1f),
                    enabledKey = Customization.UI.MenuCustomizationApplication.KEY_BANNER_ROUND_ENABLED,
                },
                [UIForegroundKind.EliminatedSquad] = new BannerDef
                {
                    slots = new System.Collections.Generic.List<BannerSlotUI>
                    {
                        MkSlot(O,  "ui.orange_replacement", "menu.banner.squad.orange", 1f,    0.55f, 0.1f),
                        MkSlot(BG, "ui.black_replacement",  "menu.banner.squad.black",  0.08f, 0.08f, 0.08f),
                        MkSlot(P,  "ui.pink_replacement",   "menu.banner.squad.pink",   1f,    0.2f,  0.5f),
                        MkSlot(C,  "ui.cyan_replacement",   "menu.banner.squad.blue",   0f,    0.78f, 1f),
                        MkSlot(Y,  "ui.yellow_replacement", "menu.banner.squad.yellow", 1f,    0.85f, 0f),
                        MkSlot(W,  "ui.white_replacement",  "menu.banner.squad.white",  1f,    1f,    1f),
                    },
                    highlight = MkSlot(W, "ui.highlight_replacement", "menu.banner.squad.highlight", 1f, 1f, 1f),
                    enabledKey = Customization.UI.MenuCustomizationApplication.KEY_BANNER_SQUAD_ENABLED,
                },
            };
        }

        private const float BANNER_PREVIEW_SPACE = 120f;

        private void BuildBannerPanel(RectTransform parent, float x, float y, float w, float h, UIForegroundKind what)
        {
            var def = GetBannerDef(what);
            if (def == null) return;
            LoadBannerSettings(what);

            float sectionH = LH + SH + BTN_H + SH + (LH + SH) * 2f + LH;
            var (scrollRect, content) = UGUIShip.CreateScrollView(parent, new Rect(0f, y, TabWidth, h));

            def.viewport = scrollRect.transform.Find("Viewport");

            float cy = BANNER_PREVIEW_SPACE;
            float swatchW = BTN_H;
            float toggleW = BTN_H * 2.2f;
            float slidersW = w - swatchW - toggleW - PAD * 2f;
            float fullSliderW = slidersW + toggleW + swatchW + PAD;

            def.enabledBtn = UGUIShip.CreateButton(content, new Rect(x, cy, w, BTN_H),
                def.enabled ? "ui.custom_colours_on_2" : "ui.custom_colours_off_2",
                def.enabled ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    def.enabled = !def.enabled;
                    SettingsService.Set(def.enabledKey, def.enabled ? "true" : "false");
                    var lbl = def.enabledBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, def.enabled ? "ui.custom_colours_on_2" : "ui.custom_colours_off_2");
                    UGUIShip.SetButtonSelected(def.enabledBtn, def.enabled, SEL_COLOR);
                    UpdateBannerPreviewColours();
                }));
            cy += BTN_H + SH;
            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            foreach (var slot in def.slots)
                BuildBannerSection(content, x, ref cy, w, sectionH, fullSliderW, swatchW, toggleW, slot);
            BuildBannerSection(content, x, ref cy, w, sectionH, fullSliderW, swatchW, toggleW, def.highlight);

            content.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void BuildBannerSection(Transform content, float x, ref float cy, float w, float sectionH,
            float fullSliderW, float swatchW, float toggleW, BannerSlotUI slot)
        {
            string title = slot.label;
            var ch = slot.ui;
            float sectionStart = cy;
            var bgGo = new GameObject(title + "_AreaBg");
            bgGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(bgGo.AddComponent<RectTransform>(),
                new Rect(x - 3f, sectionStart - 3f, w + 6f, sectionH + 6f));
            ch.areaBg = bgGo.AddComponent<Image>();
            ch.areaBg.sprite = UGUIShip.GetRadialGradCornerSprite();
            ch.areaBg.type = Image.Type.Simple;
            ch.areaBg.color = new Color(ch.r, ch.g, ch.b, 0.18f);
            ch.areaBg.raycastTarget = false;

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), title, FS_SM, HINT);
            cy += LH + SH;

            ch.toggleBtn = UGUIShip.CreateButton(content, new Rect(x, cy, toggleW, BTN_H),
                ch.on ? "ui.on" : "ui.off", ch.on ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    ch.on = !ch.on;
                    var lbl = ch.toggleBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, ch.on ? "ui.on" : "ui.off");
                    UGUIShip.SetButtonSelected(ch.toggleBtn, ch.on, SEL_COLOR);
                    UpdateBannerPreviewColours();
                }));

            var swatchGo = new GameObject(title + "_Swatch");
            swatchGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(swatchGo.AddComponent<RectTransform>(),
                new Rect(x + toggleW + PAD, cy, swatchW, BTN_H));
            ch.swatch = swatchGo.AddComponent<Image>();
            UGUIShip.ApplyDeluxColorFill(ch.swatch);
            ch.swatch.color = new Color(ch.r, ch.g, ch.b);
            cy += BTN_H + SH;

            UGUIShip.CreateColorControls(content, x, ref cy, fullSliderW,
                () => ch.r, () => ch.g, () => ch.b,
                v => ch.r = v, v => ch.g = v, v => ch.b = v, () => SyncBannerColour(ch), out _, out _, out _,
                new Color(slot.dr, slot.dg, slot.db));

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;
        }

        private void SyncBannerColour(BannerColourUI ch)
        {
            if (ch.swatch != null) ch.swatch.color = new Color(ch.r, ch.g, ch.b);
            if (ch.areaBg != null) ch.areaBg.color = new Color(ch.r, ch.g, ch.b, 0.18f);
            UpdateBannerPreviewColours();
        }

        private void LoadBannerSettings(UIForegroundKind what)
        {
            var def = GetBannerDef(what);
            if (def == null) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float P(string key, float d) =>
                float.TryParse(SettingsService.Get(key, d.ToString(ci)), System.Globalization.NumberStyles.Float, ci, out float v) ? v : d;

            void Load(BannerSlotUI s)
            {
                s.ui.on = SettingsService.Get(s.keyPrefix + ".on", "false") == "true";
                s.ui.r = P(s.keyPrefix + ".r", s.dr);
                s.ui.g = P(s.keyPrefix + ".g", s.dg);
                s.ui.b = P(s.keyPrefix + ".b", s.db);
            }

            def.enabled = SettingsService.Get(def.enabledKey, "false") == "true";
            foreach (var s in def.slots) Load(s);
            Load(def.highlight);
        }

        private void OnBannerApply(UIForegroundKind what)
        {
            var def = GetBannerDef(what);
            if (def == null) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            void W(BannerSlotUI s)
            {
                SettingsService.Set(s.keyPrefix + ".on", s.ui.on ? "true" : "false");
                SettingsService.Set(s.keyPrefix + ".r", s.ui.r.ToString(ci));
                SettingsService.Set(s.keyPrefix + ".g", s.ui.g.ToString(ci));
                SettingsService.Set(s.keyPrefix + ".b", s.ui.b.ToString(ci));
            }

            foreach (var s in def.slots) W(s);
            W(def.highlight);
        }

        // ── Live preview clone ────────────────────────────────────────────────
        // the clone/position/recolour logic lives in BannerPreviewClone, shared with the UI tab's
        // carousel preview; this just feeds it the currently-edited (unsaved) slider colours.

        private void RefreshBannerPreview()
        {
            Transform viewport = GetBannerDef(What)?.viewport;
            _previewClone.Refresh(this, What, viewport,
                new Vector3(205.4f, -44.6501f, 0f), new Vector3(0.8236f, 0.8236f, 0.6f),
                () => PreviewBannerColours(What));
        }

        private void UpdateBannerPreviewColours() => _previewClone.UpdateColours();

        private Customization.UI.MenuCustomizationApplication.BannerColours PreviewBannerColours(UIForegroundKind what)
        {
            var def = GetBannerDef(what);
            var slots = new System.Collections.Generic.List<Customization.UI.MenuCustomizationApplication.BannerSlot>();
            if (def == null || !def.enabled)
                return new Customization.UI.MenuCustomizationApplication.BannerColours { slots = slots, highlightOn = false, highlight = Color.white };

            foreach (var s in def.slots)
                if (s.ui.on)
                    slots.Add(new Customization.UI.MenuCustomizationApplication.BannerSlot
                    { bucket = s.bucket, target = new Color(s.ui.r, s.ui.g, s.ui.b) });

            var hl = def.highlight.ui;
            return new Customization.UI.MenuCustomizationApplication.BannerColours
            {
                slots = slots,
                highlightOn = hl != null && hl.on,
                highlight = hl != null ? new Color(hl.r, hl.g, hl.b) : Color.white,
            };
        }
    }
}
