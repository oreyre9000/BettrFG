using System;
using BetterFG.Customization.Users;
using BetterFG.Features.Onboarding;
using BetterFG.Services;
using BetterFG.Utilities;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Windows
{
    public class UsersWindow : SideWindow
    {
        public UsersWindow(IntPtr ptr) : base(ptr) { }

        protected override float WindowWidth => OptionsWindowBuilder.WIN_W;
        protected override float WindowHeight => 220f;
        protected override string WindowTitle => "users.title";

        private const float ICON = 11f;
        private const float SWITCH_W = 56f;
        private const float GAP = 4f;
        private const float INPUT_H = 24f;
        private static readonly Color BTN_SWITCH = new Color(0.3f, 0.6f, 0.35f, 1f);
        private static readonly Color BTN_ACTIVE = new Color(0.28f, 0.3f, 0.34f, 1f);
        private static readonly Color BTN_DEL = new Color(0.5f, 0.22f, 0.22f, 1f);

        private static Texture2D _texSave, _texFolder;

        protected override void BuildContent(RectTransform contentRoot)
        {
            BgPosition = new Vector3(139.3993f * OptionsWindowBuilder.WIN_W / 280f, 74.9552f, 0f);
            BgScale = new Vector3(1.3415f, 4.5877f, 1f);
            ContentPosition = new Vector3(-1.6132f, -17.32f, 0f);
            ContentScale = new Vector3(1.0473f, 1.04f, 1f);
            ContentOffsetMin = new Vector2(-1.6132f, -25.92f);
            ContentOffsetMax = new Vector2(-1.6132f, -18.72f);
            Pivot = new Vector2(0f, 0.5f);
            TitlePosition = new Vector3(20.0368f, -6.7966f, 0f);
            TitleScale = new Vector3(1.1818f, 1.3491f, 1f);

            _texSave ??= EmbeddedResourceandUnity.LoadTexture("BetterFG.assets.ui.general.icon_save.png");
            _texFolder ??= EmbeddedResourceandUnity.LoadTexture("BetterFG.assets.ui.button.openfolder.png");
            var scroll = UGUIShip.CreateScrollView(contentRoot,
                new Rect(0f, 0f, WindowWidth, WindowHeight - TITLE_H));
            var scrollRt = scroll.scrollRect.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = scrollRt.offsetMax = Vector2.zero;

            var listRt = scroll.content;
            var vlg = listRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 0f;
            listRt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            const float w = OptionsWindowBuilder.CTRL_W, y = OptionsWindowBuilder.BTN_Y, h = OptionsWindowBuilder.BTN_H;

            OptionsWindowBuilder.BuildHeader(listRt, "users.header_list");
            foreach (var n in SettingsService.ListUsers())
            {
                string name = n;
                var ctrl = OptionsWindowBuilder.Row(listRt, name);
                if (name.Equals(SettingsService.ActiveUser, StringComparison.OrdinalIgnoreCase))
                {
                    UGUIShip.CreateButton(ctrl, new Rect(w - h - GAP - SWITCH_W, y, SWITCH_W, h), "users.active", BTN_ACTIVE, Color.white, 9);
                    continue;
                }
                UGUIShip.CreateButton(ctrl, new Rect(w - h - GAP - SWITCH_W, y, SWITCH_W, h), "users.switch", BTN_SWITCH, Color.white, 9,
                    () => { UserService.Switch(name); RebuildContent(); });
                UGUIShip.CreateButton(ctrl, new Rect(w - h, y, h, h), "users.delete", BTN_DEL, Color.white, 9,
                    () => FullscreenPrompt.Ask("users.delete.title", name, "users.delete.confirm",
                        () => { UserService.Delete(name); RebuildContent(); }, PopUpKind.Warning));
            }

            OptionsWindowBuilder.BuildHeader(listRt, "users.header_new");
            IconButton(OptionsWindowBuilder.Row(listRt, "users.copy_current"), new Rect(0f, y, w, h),
                "users.create", OptionsWindowBuilder.BTN_IDLE, _texSave, () => AskName(true));
            IconButton(OptionsWindowBuilder.Row(listRt, "users.fresh"), new Rect(0f, y, w, h),
                "users.create", OptionsWindowBuilder.BTN_IDLE, _texSave, () => AskName(false));
            IconButton(OptionsWindowBuilder.Row(listRt, "users.folder"), new Rect(0f, y, w, h),
                "users.open_folder", OptionsWindowBuilder.BTN_IDLE, _texFolder,
                () => System.Diagnostics.Process.Start("explorer.exe", SettingsService.UsersDir));
        }

        private void AskName(bool copyCurrent)
        {
            InputField field = null;
            FullscreenPrompt.Ask("users.new.title", null, "users.create", () =>
                {
                    string made = UserService.Create(field.text, copyCurrent);
                    if (made == null) return;
                    UserService.Switch(made);
                    RebuildContent();
                },
                PopUpKind.Info, INPUT_H, box =>
                    field = UGUIShip.CreateInputField(box, new Rect(0f, 0f, box.sizeDelta.x, INPUT_H), "users.name", Color.black, UGUIShip.WHITE, 13));
        }

        private static void IconButton(RectTransform parent, Rect rect, string text, Color col, Texture2D icon, Action onClick)
        {
            var btn = UGUIShip.CreateButton(parent, rect, text, col, Color.white, 9, onClick);
            var go = new GameObject("Icon");
            go.transform.SetParent(btn.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(5f, 0f);
            rt.sizeDelta = new Vector2(ICON, ICON);
            var raw = go.AddComponent<RawImage>();
            raw.texture = icon;
            raw.raycastTarget = false;
        }
    }
}
