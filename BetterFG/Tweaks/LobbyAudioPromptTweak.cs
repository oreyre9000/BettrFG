using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Services;
using FG.Common;
using FGClient;
using FGClient.UI;
using FGClient.UI.Core;
using FGClient.UI.PrivateLobby;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BetterFG.Tweaks
{
    public class LobbyAudioPromptTweak : BfgTweak
    {
        public LobbyAudioPromptTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "lobby_audio_prompt";
        public override string TweakLabel => "tweak.audio_settings_prompt_in_lobby";
        public override bool DefaultEnabled => true;

        public static LobbyAudioPromptTweak Instance { get; private set; }
        void Awake() => Instance = this;
        public bool IsOpen => _settingsOpen;

        private const string UiRootPath = "UICanvas_Client_V2(Clone)/Default";
        private const string LobbyCanvasSub = "Prime_UI_PrivateLobby_Canvas(Clone)";

        private bool _inLobby;
        private NavigationPromptData _data;
        private bool _injected;
        private bool _settingsOpen;
        private GameObject _settingsViewGo;
        private int _lobbyViewIndex = -1;
        private PrivateLobbyScreenViewModel _lobbyVm;
        private SettingsScreenViewModel _screen;
        private float _settingsOpenedAt;

        public static void OnLobbyFocus(PrivateLobbyScreenViewModel vm)
        {
            if (Instance != null) Instance._lobbyVm = vm;
        }

        public override void OnStateChanged(GameStateMachine.IGameState newState)
        {
            _inLobby = newState != null && newState.TryCast<StatePrivateLobby>() != null;
            if (!_inLobby)
            {
                DestroyPrompt();
                _settingsOpen = false;
                _settingsViewGo = null;
                _lobbyViewIndex = -1;
                _lobbyVm = null;
                _screen = null;
            }
        }

        public override void DisableTweak()
        {
            if (_settingsOpen) CloseSettings();
            DestroyPrompt();
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus || !_settingsOpen) return;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            StartCoroutine(RegainSettingsFocusDelayed().WrapToIl2Cpp());
        }

        private IEnumerator RegainSettingsFocusDelayed()
        {
            for (int i = 0; i < 5; i++) yield return null;
            if (_settingsOpen) _screen?.OnGainFocus();
        }

        void Update()
        {
            if (!IsEnabled || !_inLobby) { DestroyPrompt(); return; }

            if (_settingsOpen)
            {
                bool pastGrace = Time.unscaledTime - _settingsOpenedAt > 0.5f;
                bool viewGone = _settingsViewGo == null || !_settingsViewGo.activeInHierarchy;
                bool backedToHub = _screen != null && _screen._switchableView != null
                    && _screen._switchableView.CurrentViewIndex == (int)SettingsScreens.SelectButtons;

                if (pastGrace && (viewGone || backedToHub))
                {
                    CloseSettings();
                    return;
                }

                if (_lobbyVm != null && _lobbyVm._isInFocus) _lobbyVm.OnLoseFocus();
            }

            bool live = !_settingsOpen && _lobbyVm != null && _lobbyVm._isInFocus
                && !LobbyCustomiserTweak.PopupOpen && !(LobbyCustomiserTweak.Instance?.IsBrowsing ?? false);

            if (live != _injected)
            {
                if (live)
                {
                    if (_data == null)
                        _data = NavPromptInjection.BuildData(NavPrompt.Report,
                            LocalizationService.Get("lobby_audio.prompt"), "lobby_audio.prompt",
                            RewiredConsts.Action.Menu_Report, RewiredConsts.Category.Menu);
                    NavPromptInjection.Add(NavPromptInjection.LobbyAudio, OpenSettings, _data);
                }
                else NavPromptInjection.Remove(NavPromptInjection.LobbyAudio);
                _injected = live;
            }

            if (live && NavPromptCore.PollActionDirect(RewiredConsts.Action.Menu_Report, null, false))
                OpenSettings();
        }

        private void OpenSettings()
        {
            var builder = FindBuilder();
            var view = builder != null ? builder.SwitchableView : null;
            if (view == null)
            {
                Plugin.Log.LogWarning("lobby audio prompt: no MainMenuBuilder/SwitchableView under " + UiRootPath);
                return;
            }

            var settingsParent = builder._settingsUIParent;
            var views = view.Views;
            int index = -1;
            for (int i = 0; i < views.Length; i++)
            {
                if (views[i] != null && views[i].transform == settingsParent) { index = i; break; }
            }

            var screen = builder.SettingsScreenViewModel;
            if (index < 0 || screen == null)
            {
                Plugin.Log.LogWarning($"lobby audio prompt: settings page not found (index={index}, screen={(screen != null)})");
                return;
            }
            _screen = screen;

            _lobbyViewIndex = view.CurrentViewIndex;
            if (_lobbyViewIndex < 0) _lobbyViewIndex = 0;
            SetLobbyVisible(false);

            _settingsViewGo = views[index];
            view.SetView(index, false, true, false);
            bool opened = screen.OpenSettingsOption(SettingsScreens.Audio);
            _settingsOpen = true;
            _settingsOpenedAt = Time.unscaledTime;
            Plugin.Log.LogInfo($"lobby audio prompt: opened settings view {index} (was {_lobbyViewIndex}), OpenSettingsOption(Audio)={opened}, active={_settingsViewGo.activeInHierarchy}");
        }

        private void CloseSettings()
        {
            var builder = FindBuilder();
            var view = builder != null ? builder.SwitchableView : null;
            if (view != null && _lobbyViewIndex >= 0)
                view.SetView(_lobbyViewIndex, false, true, false);

            bool stillActiveAfterSetView = _settingsViewGo != null && _settingsViewGo.activeInHierarchy;
            if (_settingsViewGo != null) _settingsViewGo.SetActive(false);

            Plugin.Log.LogInfo($"lobby audio prompt: closed, back to view {_lobbyViewIndex}, settings still active after SetView alone={stillActiveAfterSetView}");

            _settingsViewGo = null;
            _screen = null;
            _settingsOpen = false;
            SetLobbyVisible(true);
            Customization.Player.SkinApplicationService.Instance?.ApplyGameColourPatternToAllBeans();
            StartCoroutine(RegainLobbyFocusDelayed().WrapToIl2Cpp());
        }

        private IEnumerator RegainLobbyFocusDelayed()
        {
            for (int i = 0; i < 5; i++) yield return null;
            if (!_settingsOpen) _lobbyVm?.OnGainFocus();
        }

        private void SetLobbyVisible(bool visible)
        {
            var rootGo = GameObject.Find(UiRootPath);
            var lobbyCanvas = rootGo == null ? null : rootGo.transform.Find(LobbyCanvasSub);
            if (lobbyCanvas != null) lobbyCanvas.gameObject.SetActive(visible);

            var lobbyScreen = GameObject.Find("Menu_Screen_Lobby(Clone)");
            if (lobbyScreen != null)
            {
                var fg = lobbyScreen.transform.Find("ForegroundCanvas");
                if (fg != null) fg.gameObject.SetActive(visible);
            }
            if (visible) UI.Tabs.UIScalingTab.ApplyCanvasScalingFromSettings();

            if (visible && EventSystem.current != null)
            {
                EventSystem.current.sendNavigationEvents = true;
                var back = _lobbyVm?._inputHandler?._lastSelectedGameObject;
                if (back != null && back.activeInHierarchy) EventSystem.current.SetSelectedGameObject(back);
            }
        }

        private static MainMenuBuilder FindBuilder()
        {
            var rootGo = GameObject.Find(UiRootPath);
            var builderGo = rootGo == null ? null : rootGo.transform.Find("MainMenuBuilder(Clone)");
            return builderGo == null ? null : builderGo.GetComponent<MainMenuBuilder>();
        }

        private void DestroyPrompt()
        {
            if (!_injected) return;
            NavPromptInjection.Remove(NavPromptInjection.LobbyAudio);
            _injected = false;
        }
    }
}
