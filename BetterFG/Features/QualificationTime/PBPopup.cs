using System;
using BetterFG.Customization.Menu;
using FG.Common.UI;
using FGClient;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Features.QualificationTime
{
    public class PBPopupDestroyWatcher : MonoBehaviour
    {
        public PBPopupDestroyWatcher(IntPtr ptr) : base(ptr) { }
    }

    internal static class PBTabView
    {
        public static bool IsOpen => _root != null || _opening;
        static GameObject _root;
        static bool _opening;

        public static int MenuTabIndex = -1;
        public static Toggle TabToggle;
        public static int BackdropIndex = -1;
        static FGClient.Customiser.CustomiserScreenViewModel _custVm;
        static MenuInputHandler _custHandler;
        static GameObject _custUiGo;
        static SwitchableView _sv;
        static int _custViewIdx = -1;

        public static void OnNavIndex(SwitchableView sv, int index)
        {
            if (MenuTabIndex < 0) return;
            if (index == MenuTabIndex) Show();
        }

        public static void Show()
        {
            if (_root != null) return;
            _opening = true;

            var mmm = GameObject.Find("MainMenuManager")?.GetComponent<MainMenuManager>();
            if (mmm != null) mmm.NavigateToView(MainMenuViews.Customiser);
            _sv = GameObject.Find("UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)")?.GetComponent<SwitchableView>();
            if (_sv != null)
            {
                _custViewIdx = _sv.CurrentViewIndex;
                if (MenuTabIndex >= 0) _sv._currentViewIndex = MenuTabIndex;
            }
            BackdropIndex = MenuTabIndex;

            var custScreen = GameObject.Find("UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Customiser");
            var custUi = custScreen?.transform.Find("Prime_UI_Customizer_Prefab_Canvas(Clone)");
            _custVm = custUi?.GetComponent<FGClient.Customiser.CustomiserScreenViewModel>();
            _custHandler = custScreen?.GetComponent<MenuInputHandler>();
            if (_custVm != null && _custHandler != null) _custVm.RemoveInputHandler(_custHandler);
            _custUiGo = custUi?.gameObject;
            if (_custUiGo != null) _custUiGo.SetActive(false);

            MenuCustomizationApplication.Instance?.RefreshImageBgVisibility();

            _root = PBScreen.Open(GameObject.Find("UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)/MainScreensParent").transform);

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

            if (_custUiGo != null) _custUiGo.SetActive(true);
            if (_custVm != null && _custHandler != null) _custVm.AddInputHandler(_custHandler);

            if (_sv != null && _custViewIdx >= 0 && _sv.CurrentViewIndex != _custViewIdx
                && _custViewIdx < _sv.Views.Length && _sv.Views[_custViewIdx] != null)
                _sv.Views[_custViewIdx].SetActive(false);

            _custUiGo = null;
            _custVm = null;
            _custHandler = null;
            _sv = null;
            _custViewIdx = -1;
            BackdropIndex = -1;

            MenuCustomizationApplication.Instance?.RefreshImageBgVisibility();

            if (TabToggle != null) TabToggle.SetIsOnWithoutNotify(false);
            BetterFG.Services.DiscordPresenceService.Push();
        }
    }
}
