using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Patches.GameStates;
using BetterFG.Services;
using FallGuysLib.UI;
using FGClient.UI;
using FGClient.UI.Core;
using FGClient.UI.PrivateLobby;
using FGClient;
using UnityEngine;

namespace BetterFG.Features.CreativeExpansion
{
    // "Copy Code" on C, in the game's own NavigationOverlay row. Same shape as
    // LevelBrowserPortPrompt: postfixes hand us whichever view model currently owns the screen, and
    // while it's live we float the prompt so it appears and disappears exactly like the game's own
    // Back / Start prompts. Three sources, mutually exclusive in practice:
    //   - private lobby screen -> the lobby code
    //   - discovery / show selector tile -> that show's share code
    // the creative level browser is NOT one of them: it copies through the Import / Export menu
    // instead, so the browser doesn't carry two prompts at once.
    // Persistent singleton spawned from Plugin.InitGameObjects.
    public class CopyCodePrompt : MonoBehaviour
    {
        public CopyCodePrompt(IntPtr ptr) : base(ptr) { }

        public static CopyCodePrompt Instance { get; private set; }

        private PrivateLobbyScreenViewModel _lobbyVm;

        // never keep the show tile itself: its backing pointer goes stale as the selector rebuilds
        // and any read off it access-violates. the preview panel hands us the show, we keep the code.
        private string _showCode;

        // private lobby's own show list. empty share code = an official show, nothing to copy.
        private string _showListCode;
        private bool _showListActive;

        private NavigationPromptData _data;
        private bool _injected;

        void Awake()
        {
            Instance = this;
        }

        // PrivateLobbyScreenViewModel.OnGainFocus / OnLoseFocus
        public static void OnLobbyFocus(PrivateLobbyScreenViewModel vm)
        {
            if (Instance == null) return;
            Instance._lobbyVm = vm;
        }

        public static void OnLobbyBlur()
        {
            if (Instance == null) return;
            Instance._lobbyVm = null;
        }

        private string _showName;

        // ShowSelectorShowPreviewViewModel.SetIndividualShowData — the highlighted show
        public static void OnShowPreviewed(ShowSelectorShow show)
        {
            if (Instance == null) return;
            Instance._showCode = show != null ? show.ShareCode : null;
            Instance._showName = show?.ShowData?.ShowName?.Text;
        }

        public static void OnShowPreviewCleared()
        {
            if (Instance == null) return;
            Instance._showCode = null;
            Instance._showName = null;
        }

        // FragglePlobbiesManager.ShowHighlighted — private lobby show list nav
        public static void OnPrivateLobbyShowHighlighted(FragglePlobbiesManager mgr)
        {
            if (Instance == null) return;
            Instance._showListCode = mgr.ShareCode;
            Instance._showListActive = true;
        }

        public static void OnPrivateLobbyShowListClosed()
        {
            if (Instance == null) return;
            Instance._showListCode = null;
            Instance._showListActive = false;
        }

        public static void OnPublishSuccess(LevelEditorPublishSuccessViewModel vm)
        {
            if (Instance == null) return;
            Instance._publishVm = vm;
        }

        private LevelEditorPublishSuccessViewModel _publishVm;

        private bool PublishScreenUp =>
            _publishVm != null && _publishVm.gameObject.activeInHierarchy && !_publishVm.IsBeingRemoved;

        void Update()
        {
            if (!FeatureCreativeExpansion.On("levelcode"))
            {
                if (_injected) { NavPromptInjection.Remove(NavPromptInjection.CopyCode); _injected = false; }
                return;
            }

            bool screenActive = ScreenActive();

            if (screenActive != _injected)
            {
                if (screenActive) NavPromptInjection.Add(NavPromptInjection.CopyCode, Copy, PromptData, ApplyGlyph);
                else NavPromptInjection.Remove(NavPromptInjection.CopyCode);
                _injected = screenActive;
            }
            if (!screenActive) { _copiedUntil = 0f; return; }

            if (_copiedUntil > 0f && Time.unscaledTime >= _copiedUntil)
            {
                _copiedUntil = 0f;
                NavPromptInjection.Add(NavPromptInjection.CopyCode, Copy, PromptData, ApplyGlyph);
            }

            if (CopyPressed()) Copy();
        }

        private float _copiedUntil;

        private NavigationPromptData CopiedData => NavPromptInjection.BuildData(NavPrompt.Report, LocalizationService.Get("level_port.copied"), "bfg_copycode_copied",
            RewiredConsts.Action.Default_OpenInGameMenu, RewiredConsts.Category.Default);

        internal NavigationPromptData PromptData => _data ??= NavPromptInjection.BuildData(NavPrompt.Report, LocalizationService.Get("nav.copy_code"), "bfg_copycode_prompt",
            RewiredConsts.Action.Default_OpenInGameMenu, RewiredConsts.Category.Default);

        internal static void ApplyGlyph(NavigationPromptButton btn) =>
            NavPromptCore.ApplyOwnGlyph(btn, KeyCode.C, RewiredConsts.Action.Default_OpenInGameMenu);

        internal static bool CopyPressed() =>
            !(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            && (BetterFG.Services.KeybindService.KeyDown(KeyCode.C) || NavPromptCore.PollActionDirect(RewiredConsts.Action.Default_OpenInGameMenu, null, true));

        private bool ScreenActive()
        {
            if (BetterFG.Tweaks.LevelBrowserTweak.IsOpen) return false;
            if (_showListActive && !PublishScreenUp) return !string.IsNullOrEmpty(_showListCode);
            return !string.IsNullOrEmpty(CurrentCode());
        }

        private string CurrentCode()
        {
            ResolveCurrent(out string code, out _);
            return code;
        }

        private void Copy()
        {
            ResolveCurrent(out string code, out _);
            if (!WriteClipboard(code)) return;
            _copiedUntil = Time.unscaledTime + 1f;
            NavPromptInjection.Add(NavPromptInjection.CopyCode, Copy, CopiedData, ApplyGlyph);
        }

        private static bool WriteClipboard(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            GUIUtility.systemCopyBuffer = System.Text.RegularExpressions.Regex.Replace(code, @"<[^>]+>|[\s​]", "");
            return true;
        }

        // the level browser reaches this through the Import / Export menu instead of its own prompt,
        // so the clipboard write + "Copied" popup live here as the one copy both paths call.
        internal void CopyResolved(string code, string source)
        {
            if (!WriteClipboard(code)) return;
            code = GUIUtility.systemCopyBuffer;

            string body = source == "lobby" ? LocalizationService.Get("copycode.body_lobby")
                : string.IsNullOrEmpty(source) ? LocalizationService.Format("copycode.body_plain", code)
                : source == "level" ? LocalizationService.Format("copycode.body_level", code)
                : source == "show" ? LocalizationService.Format("copycode.body_show", code)
                : LocalizationService.Format("copycode.body_show_named", code, source.Substring(6, source.Length - 7));

            NavPromptCore.RegisterCmsString("bfg_copycode_title", LocalizationService.Get("nav.copied"));
            NavPromptCore.SetCmsString("bfg_copycode_body", body);
            PopUp.ShowPopup("bfg_copycode_title", "bfg_copycode_body",
                PopupInteractionType.Info, UIModalMessage.ModalType.MT_OK,
                UIModalMessage.OKButtonType.Default,
                (Action<bool>)(_ => StartCoroutine(RestoreLobbyFocusAfterClose().WrapToIl2Cpp())));
        }

        private static IEnumerator RestoreLobbyFocusAfterClose()
        {
            yield return new WaitForSeconds(1f);
            RestoreLobbyFocus.Kick();
        }

        // resolve which source is live right now + a friendly label for it. same priority order
        // as CurrentCode. source is a display string, safe to inline into the popup body (may be
        // null if we don't have a good name).
        private void ResolveCurrent(out string code, out string source)
        {
            if (PublishScreenUp) { code = _publishVm.LevelCode; source = "level"; return; }
            if (!string.IsNullOrEmpty(_showListCode)) { code = _showListCode; source = "show"; return; }
            if (!string.IsNullOrEmpty(_showCode)) { code = _showCode; source = string.IsNullOrEmpty(_showName) ? "show" : $"show \"{_showName}\""; return; }
            if (_lobbyVm != null && _lobbyVm._isInFocus) { code = _lobbyVm.Code; source = "lobby"; return; }
            code = null; source = null;
        }

    }
}
