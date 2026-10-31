using System;
using UnityEngine;
using UnityEngine.UI;
using FGClient;
using FG.Common.UI;

namespace BetterFG.Features.QualificationTime
{
    // the PB top-bar tab. NOT a modal popup and NOT a real SwitchableView view (the game's view
    // machinery is a native index->enum map we can't extend). our clone tab sits at the end of the
    // Rewired nav's _menuTabs; landing on it positionally lands on view 6 (Settings), which the nav
    // patch catches. we then navigate to the Customiser view for the scene backdrop (same as the old
    // popup did) and drop our content on top. lives on the game canvas so it ignores the UI scale.
    internal static class PBTabView
    {
        public static bool IsOpen => _root != null || _opening;
        static GameObject _root;
        // Show() navigates to the Customiser backdrop first, which pushes a presence update before our
        // root exists — without this the RPC flashes "Customising their bean" for a beat.
        static bool _opening;

        // position of our clone in the nav's _menuTabs (the last slot).
        public static int MenuTabIndex = -1;
        public static Toggle TabToggle;
        // the SwitchableView index we consider "still on our tab" (== MenuTabIndex). a switch to any
        // OTHER index means the user navigated off us, so drop the overlay.
        public static int BackdropIndex = -1;
        static FGClient.Customiser.CustomiserScreenViewModel _custVm;
        static MenuInputHandler _custHandler;
        static GameObject _custUiGo;
        // the SwitchableView + the REAL index its Customiser view sits at. we lie that current is our
        // slot for nav, so SetView never tears Customiser down — we deactivate it ourselves on leave.
        static SwitchableView _sv;
        static int _custViewIdx = -1;

        // the Rewired nav moved to a tab. landing on our slot shows the PB tab. leaving is handled by
        // the clone toggle going off (see the onValueChanged wiring), so we don't hide from here.
        public static void OnNavIndex(SwitchableView sv, int index)
        {
            if (MenuTabIndex < 0) return;
            if (index == MenuTabIndex) Show();
        }

        public static void Show()
        {
            if (_root != null) return;
            _opening = true;

            // Customiser view for its 3D scene backdrop, like the old popup. that leaves CurrentViewIndex
            // pointing at Customiser though, so the pad would navigate left/right around Customiser and
            // the toggle handler would light Customiser's tab. write the index back to OUR slot (the
            // last one) so nav treats us as the last tab and the handler lights no stock toggle (there's
            // none at our index) — leaving it ENABLED so a mouse click on another tab still fires SetView.
            var mmm = GameObject.Find("MainMenuManager")?.GetComponent<MainMenuManager>();
            if (mmm != null) mmm.NavigateToView(MainMenuViews.Customiser);
            _sv = GameObject.Find("UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)")?.GetComponent<SwitchableView>();
            if (_sv != null)
            {
                _custViewIdx = _sv.CurrentViewIndex; // real Customiser view index, before we overwrite it
                if (MenuTabIndex >= 0) _sv._currentViewIndex = MenuTabIndex;
            }
            BackdropIndex = MenuTabIndex;

            // the Customiser backdrop screen VM keeps focus, and its MenuInputHandler reads the stick
            // (scrolling cosmetics under our overlay). just calling OnLoseFocus doesn't hold — alt-tab
            // and screen changes re-assert focus. instead pull the handler off the VM's input list so
            // even a re-focused VM has nothing to drive; we hand it back on Hide.
            var custScreen = GameObject.Find("UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Customiser");
            var custUi = custScreen?.transform.Find("Prime_UI_Customizer_Prefab_Canvas(Clone)");
            _custVm = custUi?.GetComponent<FGClient.Customiser.CustomiserScreenViewModel>();
            _custHandler = custScreen?.GetComponent<MenuInputHandler>();
            if (_custVm != null && _custHandler != null) _custVm.RemoveInputHandler(_custHandler);
            // the Customiser view is only here for its 3D scene backdrop — its cosmetics UI panels
            // shouldn't be seen, navigated, or entered under our tab. deactivate the whole UI canvas;
            // the bean/camera live in the scene, not under it, so the backdrop stays.
            _custUiGo = custUi?.gameObject;
            if (_custUiGo != null) _custUiGo.SetActive(false);

            // our tab now owns the screen — drop the custom menu background image too.
            BetterFG.Customization.UI.MenuCustomizationApplication.Instance?.RefreshImageBgVisibility();

            _root = PBScreen.Open(GameObject.Find("UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)/MainScreensParent").transform);

            // our clone is in the tab ToggleGroup; turning it on makes it the sole lit tab. the handler
            // keeps the stock toggles off since none sits at our index.
            if (TabToggle != null) TabToggle.isOn = true;
            _opening = false;
            BetterFG.Services.DiscordPresenceService.Push();
        }

        public static void Hide()
        {
            if (_root == null) return;
            PBScreen.Close();
            _root = null;
            _opening = false;

            // put the customiser UI + its input handler back so it works normally next time.
            if (_custUiGo != null) _custUiGo.SetActive(true);
            if (_custVm != null && _custHandler != null) _custVm.AddInputHandler(_custHandler);

            // we lied that current was our slot, so SetView tore down _views[our slot] (nothing) and
            // left the Customiser view active. deactivate it ourselves — unless the user actually
            // navigated TO Customiser, in which case it's the real destination and should stay.
            if (_sv != null && _custViewIdx >= 0 && _sv.CurrentViewIndex != _custViewIdx
                && _custViewIdx < _sv.Views.Length && _sv.Views[_custViewIdx] != null)
                _sv.Views[_custViewIdx].SetActive(false);

            _custUiGo = null;
            _custVm = null;
            _custHandler = null;
            _sv = null;
            _custViewIdx = -1;
            BackdropIndex = -1;

            // restore the custom menu image to whatever the current view calls for.
            BetterFG.Customization.UI.MenuCustomizationApplication.Instance?.RefreshImageBgVisibility();

            if (TabToggle != null) TabToggle.SetIsOnWithoutNotify(false);
            BetterFG.Services.DiscordPresenceService.Push();
        }
    }
}
