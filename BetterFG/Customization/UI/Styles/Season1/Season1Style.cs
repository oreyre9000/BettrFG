using System.Collections.Generic;
using FGClient;
using UnityEngine;

namespace BetterFG.Customization.UI.Styles.Season1
{
    public sealed class Season1Style : UIStyle
    {
        const string BAR = "SafeArea/Container/";
        const string CAROUSEL = BAR + "MenuCarrousel";
        const string LOBBY = "SafeArea/";
        const string SEASON = LOBBY + "BottomLeftContainer/SeasonProgress/SeasonProgressBar/";
        const string TAG = LOBBY + "BottomLeftContainer/PB_UI_NameTag/";

        public override string Id => "season1";
        public override string LabelId => "layout.style.season1";
        public override string Resources => "season1";
        public override string OnlineDir => "S1";

        static readonly TopBarSpec Bar = new TopBarSpec
        {
            Layout = "topbar",
            Carousel = CAROUSEL,
            TabTemplates = new[] { "MenuTab", "MenuTab (1)" },
            TemplateTabs = new[] { "MenuTab", "MenuTab (1)", "MenuTab (2)", "MenuTab (3)", "MenuTab (4)" },
            TabIcon = "MenuIcon",
            TabWidth = 84f,
            TabGap = 20f,
            ForeignIconSize = 35.6f,
            ForeignIconOffset = new Vector2(-2.5f, 2.7f),
            Title = BAR + "MenuName",
            Crowns = BAR + "Currencies/Crown/CurrencyTextCrown",
            Kudos = BAR + "Currencies/Kudos/CurrencyTextKudos",
            PrevGlyph = CAROUSEL + "/MenuCarrousselNavigationLeft",
            NextGlyph = CAROUSEL + "/MenuCarrousselNavigationRight",
            Settings = new TabArt { Icon = "UI_icon_settings_1080p", TitleKey = "settings", IconOffset = new Vector2(-3.1f, 2.7f) },
            Tabs = new Dictionary<string, TabArt>
            {
                { "HomeButton", new TabArt { Icon = "UI_icon_lobby_1080p", TitleKey = "lobby", IconOffset = new Vector2(0f, 2.5f) } },
                { "CustomiserButton", new TabArt { Icon = "UI_icon_customise_1080p", TitleKey = "customiser", IconOffset = new Vector2(-3.2f, 2.8f) } },
                { "SeasonPassButton", new TabArt { Icon = "UI_icon_seasons_1080p", TitleKey = "seasons", IconOffset = new Vector2(-2.2f, 2f) } },
                { "ShopButton", new TabArt { Icon = "UI_icon_store_1080p", TitleKey = "shop", IconOffset = new Vector2(-2.9f, 3.2f) } },
                { "LiveEventButton", new TabArt { TitleKey = "live_events" } },
                { "LevelEditorButton", new TabArt { TitleKey = "creative" } },
            },
        };

        static readonly LobbySpec Main = new LobbySpec
        {
            Layout = "screen_Main_UICanvas",
            Root = "Prefab_UI_Menus_Main",
            Disable = new[] { "RenderTexture", LOBBY + "FGPartyButtonGroupMenu", LOBBY + "FGPartyLoadingRoom" },
            Play = LOBBY + "ButtonPlay",
            Invite = LOBBY + "FGPartyButtonAddFriends",
            ShowSelect = "ButtonShowSelector",
            ShowSelectParent = "SafeArea",
            InvitePosition = new Vector2(106f, 194f),
            InviteAnchor = Vector2.zero,
            InviteSpacing = 25f,
            SeasonLevel = SEASON + "SeasonProgressLevelText",
            SeasonCounter = SEASON + "SeasonProgressCounterText",
            SeasonFill = SEASON + "SeasonProgressFillContainer",
            SeasonDone = SEASON + "SeasonCompletedContainer",
            NameplateBacking = TAG + "NameTagBacking",
            Name = TAG + "NameTagUserNameText",
            Nickname = TAG + "NameTagUserNicknameText",
            NameY = -13f,
            NicknameY = -66f,
        };

        const string SCREENS = "UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)/MainScreensParent";
        const string NAV = "Prefab_UI_NavigationOverlay(Clone)";

        public override TopBarSpec TopBar => Bar;
        public override LobbySpec Lobby => Main;

        public override void ApplyMenu(MainMenuManager menu)
        {
            S1Settings.Apply(this);
            S1Customiser.Apply(this);
            S1CustPages.Apply(this);
            var screens = GameObject.Find(SCREENS);
            if (screens != null) { S1Skins.Broad(this, screens.transform); S1Shop.ArmIn(screens.transform); }
            var nav = GameObject.Find(NAV);
            if (nav != null) S1Nav.Apply(this, nav.transform);
            S1Shows.ArmIn();
        }

        public override void TeardownMenu()
        {
            S1Settings.Teardown();
            S1Skins.Teardown();
            S1Nav.Teardown();
            S1CustPages.Teardown();
            S1Modal.Teardown();
            S1Shop.Teardown();
            S1Preview.Teardown();
            S1Shows.Teardown();
            S1PrivateLobby.Teardown();
        }

        public override void OnScreen(Component screen)
        {
            S1Ingame.Screen(this, screen);
            S1Skins.Panels(this, screen.transform);
            S1Skins.Broad(this, screen.transform);
            S1Shop.Arm(screen.transform);
            S1PrivateLobby.Screen(this, screen);
        }

        public override void OnFocus(Component screen)
        {
            S1PrivateLobby.Screen(this, screen);
            var name = screen.GetIl2CppType().Name;
            if (name == "SymphonyStoreDefaultConfirmationScreen") S1Preview.Show(this, screen.transform);
            else if (name == "PurchaseConfirmationScreenViewModel")
            {
                S1Skins.Modal(this, screen.transform);
                S1Skins.Compact(screen.transform);
            }
        }

        public override void OnModal(Component modal)
        {
            if (S1Modal.Handles(modal)) { S1Modal.Show(this, modal.transform); return; }
            S1Skins.Modal(this, modal.transform);
            S1Kit.Follow(modal.gameObject, S1Skins.MODAL, 1).Arm();
        }
        public override void OnNavPrompts(Transform overlay) => S1Nav.Apply(this, overlay);
        public override void OnHud(Transform root) => S1Ingame.Hud(this, root);
        public override void OnPause(Transform root) => S1Ingame.Pause(this, root);

        public override void OnLoading(Transform inside)
        {
            var root = inside;
            while (root.parent != null && root.parent.name != "LoadingScreen") root = root.parent;
            S1Loading.Apply(this, root);
            S1Skins.Broad(this, root);
        }

        public override void OnView(MainMenuViews view)
        {
            if (view == MainMenuViews.Settings) S1Settings.Extras(this);
            else if (view == MainMenuViews.Seasons) S1Fame.Arm();
            else if (view == MainMenuViews.Shop) { var screens = GameObject.Find(SCREENS); if (screens != null) S1Shop.ArmIn(screens.transform); }
        }

        public override bool OnShopMove(Component handler, int dir) => S1Shop.Move(handler, dir);

        public override void OnSettled(int kind, Transform t)
        {
            if (kind == S1Settings.BOARD) S1Settings.Board(t);
            else if (kind == S1Settings.STOCK) S1Settings.Extras(this);
            else if (kind == S1Preview.PREVIEW) S1Preview.Settled(t);
            else if (kind == S1Ingame.BANNER) S1Ingame.Settled(t);
            else if (kind == S1CustPages.PAGE) S1CustPages.Settled(t);
            else if (kind == S1Skins.MODAL) S1Skins.Modal(this, t);
            else if (kind == S1Shop.SHOP) S1Shop.Apply(this, t);
            else if (kind == S1Fame.FAME) S1Fame.Apply(this, t);
            else if (kind == S1Shows.VIEW) S1Shows.Apply(this, t);
        }
    }
}
