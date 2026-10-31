using System;
using System.Globalization;
using BetterFG.Customization.Player;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class ItemConfigTab : SwitchTab
    {
        public ItemConfigTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "UGC Item Config";
        protected override string TitleId => "ui.ugc_customization";
        protected override string BgResource => "BetterFG.assets.ui.cskins.bg.png";
        protected override string SwitchLabel => "ui.back_3";
        protected override Tab MakeSwitchTarget() => BetterFGTabRegistry.NewTab<CustomizationTab>();

        public SkinInfo Skin;

        private static readonly Color WHITE = UGUIShip.WHITE;
        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.45f);
        private static readonly Color HAND_ON = new Color(0.35f, 0.8f, 1f, 1f);
        private static readonly Color ENABLED = new Color(0.2f, 0.5f, 0.27f, 1f);
        private static readonly Color OFF = new Color(0.14f, 0.14f, 0.14f, 1f);
        private static readonly Color RESET = new Color(0.45f, 0.25f, 0.25f, 1f);
        private static readonly Color TRACK = new Color(0.7f, 0.7f, 0.7f, 1f);
        private static readonly string[] AXES = { "x", "y", "z" };
        private const float POS_RANGE = 0.3f;

        private int _hand;
        private readonly Button[] _handBtns = new Button[2];
        private Button _enabledBtn;
        private readonly Slider[] _pos = new Slider[3];
        private readonly Slider[] _rot = new Slider[3];
        private readonly Text[] _posVal = new Text[3];
        private readonly Text[] _rotVal = new Text[3];

        private bool Left => _hand == 0;

        protected override void BuildContent(RectTransform contentRoot)
        {
            float x = PAD * 2f, w = TabWidth - PAD * 4f, y = PAD;
            float bh = BTN_H * 0.9f, rh = LH, half = (w - PAD) * 0.5f;

            UGUIShip.CreateLabel(contentRoot, new Rect(x, y, w, LH), Skin.name, FS, WHITE);
            y += LH + PAD;

            _hand = Skin.left != null ? 0 : 1;
            for (int h = 0; h < 2; h++)
            {
                int hand = h;
                _handBtns[h] = UGUIShip.CreateButton(contentRoot, new Rect(x + h * (half + PAD), y, half, bh),
                    h == 0 ? "ugc_skins.item_left" : "ugc_skins.item_right", OFF, WHITE, FS, new Action(() => { _hand = hand; Repaint(); }));
                _handBtns[h].interactable = (h == 0 ? Skin.left : Skin.right) != null;
            }
            y += bh + PAD;

            _enabledBtn = UGUIShip.CreateButton(contentRoot, new Rect(x, y, w, bh * 0.92f), "ugc_skins.item_enabled",
                OFF, WHITE, FS_SM, new Action(ToggleEnabled));
            y += bh + PAD * 1.5f;

            float x2 = x + half + PAD;
            float valW = 30f * UIScale.S * 0.75f, axisW = 14f;
            float sw = half - axisW - valW;
            UGUIShip.CreateLabel(contentRoot, new Rect(x, y, half, rh), "ui.position", FS_SM, HINT);
            UGUIShip.CreateLabel(contentRoot, new Rect(x2, y, half, rh), "ui.rotation", FS_SM, HINT);
            float resetW = 52f;
            UGUIShip.CreateButton(contentRoot, new Rect(x2 + half - resetW, y - 1f, resetW, rh), "ui.reset",
                RESET, WHITE, FS_SM - 1, new Action(ResetHand));
            y += rh + PAD * 0.5f;

            for (int a = 0; a < 3; a++)
            {
                int axis = a;
                string axisId = "ugc_skins.axis_" + AXES[a];
                UGUIShip.CreateLabel(contentRoot, new Rect(x, y, axisW, rh), axisId, FS_SM, WHITE);
                _posVal[a] = UGUIShip.CreateLabel(contentRoot, new Rect(x + half - valW, y, valW, rh), "", FS_SM - 1, WHITE);
                _pos[a] = UGUIShip.CreateSlider(contentRoot, x + axisW, y, sw, "", 0.5f, rh, 0f, FS_SM,
                    new Action<float>(v => Write("offset", axis, v * POS_RANGE * 2f - POS_RANGE)), HINT, TRACK, true, 0.5f);
                UGUIShip.CreateLabel(contentRoot, new Rect(x2, y, axisW, rh), axisId, FS_SM, WHITE);
                _rotVal[a] = UGUIShip.CreateLabel(contentRoot, new Rect(x2 + half - valW, y, valW, rh), "", FS_SM - 1, WHITE);
                _rot[a] = UGUIShip.CreateSlider(contentRoot, x2 + axisW, y, sw, "", 0f, rh, 0f, FS_SM,
                    new Action<float>(v => Write("rot", axis, v * 360f)), HINT, TRACK, true, 0f);
                y += rh + PAD * 0.5f;
            }
            y += PAD;

            var frameGo = new GameObject("Preview");
            frameGo.transform.SetParent(contentRoot, false);
            float ph = TabHeight - y - PAD * 2f;
            UGUIShip.SetPixelRect(frameGo.AddComponent<RectTransform>(), new Rect(x, y, w, ph));
            var (_, slot) = UGUIShip.CreateFramedImage(frameGo.transform);
            var raw = slot.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.texture = ItemPreview.Show(Skin, w / ph);

            for (int h = 0; h < 2; h++)
                ItemPreview.SetHand(h, HandOn(Mask(), h), Skin.ItemVec("offset", h == 0), Skin.ItemVec("rot", h == 0));
            Repaint();
        }

        public override void OnOpened() { base.OnOpened(); ItemPreview.SetLive(true); }
        public override void OnClosed() { base.OnClosed(); ItemPreview.SetLive(false); }
        void OnDestroy() => ItemPreview.Hide();

        private int Mask() => BetterFG.Network.RemoteProfileStore.LocalHandOverrides().TryGetValue(Skin.file, out int m) ? m : 3;

        private static bool HandOn(int mask, int hand) => mask == 0 || mask == 3 || mask == hand + 1;

        private void ToggleEnabled()
        {
            int m = Mask();
            bool l = HandOn(m, 0) && Skin.left != null, r = HandOn(m, 1) && Skin.right != null;
            if (Left) l = !l; else r = !r;
            if (!l && !r) return;

            var overrides = BetterFG.Network.RemoteProfileStore.LocalHandOverrides();
            overrides[Skin.file] = l && r ? 3 : l ? 1 : 2;
            BetterFG.Network.RemoteProfileStore.SaveLocalHandOverrides(overrides);
            Skin.handOverride = overrides[Skin.file];
            CustomizationServices.ApplicationService.TryReapplyLoadedSkin(Skin);
            Push();
            Repaint();
        }

        private void Write(string kind, int axis, float value)
        {
            SettingsService.Set(Skin.ItemKey(kind, Left, AXES[axis]), value.ToString(CultureInfo.InvariantCulture));
            (kind == "rot" ? _rotVal : _posVal)[axis].text = value.ToString(kind == "rot" ? "F0" : "F2");
            Push();
        }

        private void ResetHand()
        {
            foreach (var kind in new[] { "offset", "rot" })
                foreach (var a in AXES) SettingsService.Remove(Skin.ItemKey(kind, Left, a));
            Push();
            Repaint();
        }

        private void Push()
        {
            CustomizationServices.ApplicationService.RefreshItemTransforms(Skin.file);
            ItemPreview.SetHand(_hand, HandOn(Mask(), _hand), Skin.ItemVec("offset", Left), Skin.ItemVec("rot", Left));
        }

        private void Repaint()
        {
            for (int h = 0; h < 2; h++)
            {
                UGUIShip.SetButtonColor(_handBtns[h], h == _hand ? HAND_ON : OFF);
                _handBtns[h].GetComponentInChildren<Text>().color = WHITE;
            }
            bool on = HandOn(Mask(), _hand);
            UGUIShip.SetButtonColor(_enabledBtn, on ? ENABLED : OFF);
            UGUIShip.RelabelText(_enabledBtn.GetComponentInChildren<Text>(), on ? "ugc_skins.item_enabled" : "ugc_skins.item_disabled");

            var off = Skin.ItemVec("offset", Left);
            var rot = Skin.ItemVec("rot", Left);
            for (int a = 0; a < 3; a++)
            {
                _pos[a].SetValueWithoutNotify((off[a] + POS_RANGE) / (POS_RANGE * 2f));
                _posVal[a].text = off[a].ToString("F2");
                _rot[a].SetValueWithoutNotify(rot[a] / 360f);
                _rotVal[a].text = rot[a].ToString("F0");
            }
        }
    }
}
