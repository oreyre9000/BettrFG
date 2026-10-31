using System;
using BetterFG.Core;
using BetterFG.Services;
using BettrFG.uGUI;
using FallGuysLib.UI;
using FGClient;
using FG.Common.UI;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace BetterFG.Features.TextChat
{
    internal static class TextChatSettingsTile
    {
        private const string Rows = "SettingsMenu_Prime_Canvas/SafeArea/CenterButtons_Group/VerticalContainer/";
        private const string TileName = "BFG_TextChatButton";
        private const string PageName = "BFG_TextChatOptions";
        private const int AudioPage = 1;

        private static SettingsScreenViewModel _screen;
        private static int _page = -1;
        private static TextMeshProUGUI _enabledValue;
        private static TextMeshProUGUI _modeValue;
        private static TextMeshProUGUI _lobbyValue;

        internal static void Inject(SettingsScreenViewModel screen)
        {
            _screen = screen;
            var row = screen.transform.Find(Rows + "FirstRow");
            if (row.Find(TileName) == null)
            {
                var tile = Object.Instantiate(screen.transform.Find(Rows + "SecondRow/SupportButton").gameObject, row, false).transform;
                tile.name = TileName;
                tile.gameObject.SetActive(true);

                var trigger = tile.GetComponent<EventTrigger>();
                trigger.triggers.Clear();
                AddTrigger(trigger, EventTriggerType.PointerClick);
                AddTrigger(trigger, EventTriggerType.Submit);

                var text = tile.Find("Content/Text");
                var loc = text.gameObject.GetComponent("LocalisedStaticLabel")?.TryCast<Behaviour>();
                if (loc != null) loc.enabled = false;
                UGUIShip.RelabelText(text.GetComponent<TextMeshProUGUI>(), "text_chat.tile");

                screen.transform.Find(Rows.TrimEnd('/')).GetComponent<SettingsScreenInputHandler>().BuildFilteredRows();
            }

            var views = screen._switchableView._views;
            _page = -1;
            for (int i = 0; i < views.Length; i++) if (views[i].name == PageName) _page = i;
            if (_page < 0) BuildPage(screen);
            Refresh();
        }

        private static void AddTrigger(EventTrigger trigger, EventTriggerType type)
        {
            var entry = new EventTrigger.Entry();
            entry.eventID = type;
            entry.callback.AddListener((UnityAction<BaseEventData>)(_ => Open()));
            trigger.triggers.Add(entry);
        }

        private static void Open()
        {
            try { _screen.OpenSettingsOption((SettingsScreens)_page); }
            catch (Exception ex)
            {
                var view = _screen._switchableView;
                view._currentViewIndex = _page;
                view._stillProcessingSetView = false;
                Plugin.Log.LogWarning($"text chat page: game's audio focus code blew up mid view switch ({ex.Message.Split('\n')[0]}), unstuck the settings view so back still works");
            }
        }

        private static void BuildPage(SettingsScreenViewModel screen)
        {
            var views = screen._switchableView._views;
            var audio = views[AudioPage];
            var holder = new GameObject("BFG_TextChatHolder");
            holder.SetActive(false);

            var page = Object.Instantiate(audio, holder.transform);
            page.name = PageName;
            var spawner = page.GetComponent<OptionsSpawner>();
            var content = spawner._spawnParent;
            var prefabs = spawner._pcConfig._options;
            Object.DestroyImmediate(spawner);
            for (int i = content.childCount - 1; i >= 0; i--) Object.DestroyImmediate(content.GetChild(i).gameObject);
            page.transform.Find("DescriptionPanel").gameObject.SetActive(false);

            var input = page.GetComponent<SettingsInputHandler>();
            input._settingElements.Clear();

            GameObject header = null, toggle = null;
            foreach (var p in prefabs)
            {
                if (p.name.Contains("VoiceChatHeader")) header = p;
                if (p.GetComponent<VoiceChatEnabledOptionViewModel>() != null) toggle = p;
            }

            var title = Object.Instantiate(header, content, false).transform.Find("SettingsHeaderText");
            title.GetComponent("LocalisedStaticLabel").TryCast<Behaviour>().enabled = false;
            UGUIShip.RelabelText(title.GetComponent<TextMeshProUGUI>(), "text_chat.title");
            _enabledValue = Row(toggle, content, input, "text_chat.option.enabled", ToggleEnabled);
            _modeValue = Row(toggle, content, input, "text_chat.option.mode", ToggleMode);
            _lobbyValue = Row(toggle, content, input, "text_chat.option.lobby_requests", ToggleLobbyRequests);

            page.transform.SetParent(audio.transform.parent, false);
            Object.Destroy(holder);

            var next = new Il2CppReferenceArray<GameObject>(views.Length + 1);
            for (int i = 0; i < views.Length; i++) next[i] = views[i];
            next[views.Length] = page;
            screen._switchableView.SetViews(next);

            var focus = screen._focusHandler;
            var models = focus._focusableViewModels;
            var nextModels = new Il2CppReferenceArray<FocusableViewModel>(models.Length + 1);
            for (int i = 0; i < models.Length; i++) nextModels[i] = models[i];
            nextModels[models.Length] = page.GetComponent<AudioOptionsViewModel>();
            focus.SetViewModels(nextModels);
            _page = views.Length;
            Plugin.Log.LogInfo($"text chat page built as settings view {_page}");
        }

        private static TextMeshProUGUI Row(GameObject prefab, Transform content, SettingsInputHandler input, string labelId, Action change)
        {
            var go = Object.Instantiate(prefab, content, false);
            Object.DestroyImmediate(go.GetComponent<VoiceChatEnabledOptionViewModel>());

            var label = go.transform.Find("SettingsText");
            label.GetComponent("LocalisedStaticLabel").TryCast<Behaviour>().enabled = false;
            UGUIShip.RelabelText(label.GetComponent<TextMeshProUGUI>(), labelId);

            var value = go.transform.Find("InputsContainer/SettingsValue/SettingsValueText");
            value.GetComponent("TextBinding").TryCast<Behaviour>().enabled = false;

            var cfg = go.GetComponent<SettingConfigurationViewModel>();
            cfg._OnMoveLeft.AddListener((UnityAction)(() => change()));
            cfg._OnMoveRight.AddListener((UnityAction)(() => change()));
            cfg._OnPress.AddListener((UnityAction)(() => change()));
            input.AddSettingElement(cfg, false);
            return value.GetComponent<TextMeshProUGUI>();
        }

        private static void ToggleEnabled()
        {
            if (FeatureTextChat.Enabled)
            {
                FeatureTextChat.SetEnabled(false);
                Refresh();
                return;
            }

            NavPromptCore.RegisterCmsString("bfg_textchat_optin_title", LocalizationService.Get("text_chat.optin.title"));
            NavPromptCore.RegisterCmsString("bfg_textchat_optin_body", LocalizationService.Get("text_chat.optin.body"));
            PopUp.Confirm("bfg_textchat_optin_title", "bfg_textchat_optin_body", ok =>
            {
                if (!ok) return;
                FeatureTextChat.SetEnabled(true);
                Refresh();
            });
        }

        private static void ToggleMode()
        {
            FeatureTextChat.SetGameMode(!FeatureTextChat.GameMode);
            Refresh();
        }

        private static void ToggleLobbyRequests()
        {
            FeatureTextChat.SetLobbyRequests(!FeatureTextChat.LobbyRequests);
            Refresh();
        }

        private static void Refresh()
        {
            if (_enabledValue == null) return;
            _enabledValue.text = LocalizationService.Get(FeatureTextChat.Enabled ? "text_chat.value.on" : "text_chat.value.off");
            _modeValue.text = LocalizationService.Get(FeatureTextChat.GameMode ? "text_chat.value.game" : "text_chat.value.party");
            _lobbyValue.text = LocalizationService.Get(FeatureTextChat.LobbyRequests ? "text_chat.value.on" : "text_chat.value.off");
        }
    }
}
