using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BetterFG.Core;
using BetterFG.Nametag;
using BetterFG.Services;
using BetterFG.Customization.Player;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using FG.Common;
using FGClient;
using FallGuysLib.Camera;
using HarmonyLib;
using UnityEngine;
using PlayerUtils = FallGuysLib.Players.PlayerUtils;
using BetterFG.UI.Tabs;
using BetterFG.Network;
using BetterFG.Customization.Social;
using BetterFG.Customization.UI;
using FG.Common.CMS;
using FG.Common.UI;
using FGClient.ShowSelector;
using FGClient.VictoryScreen;
using FGClient.Challenges;
using FGClient.UI;
using FGClient.UI.PrivateLobby;
using BetterFG.Features.UnityRound;
using BetterFG.Features.Stars;
using BetterFG.Features.QualificationTime;
using BetterFG.Features.AllCosmetics;
using BetterFG.Features.MorePlatformIcon;
using BetterFG.Features.TimePlacement;
using System.Runtime.InteropServices;
using BetterFG.UI;
using FallGuysLib.UI;
using static FG.Common.PartyService;

namespace BetterFG.Patches.GameStates
{

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OnMainMenuEntered), new[] { typeof(bool), typeof(bool) })]
    public class MainMenuBean
    {
        [HarmonyPostfix]
        public static void OnMainMenuEntered(MainMenuManager __instance)
        {
            MenuCustomizationApplication.CacheMenuManager(__instance);

            if (__instance._lobbyVirtualCam != null)
            {
                MenuCustomizationApplication.Instance?.CacheCamBase(__instance._lobbyVirtualCam);
                MenuCustomizationApplication.AutoApplyCamFromSettings();
            }

            MenuCustomizationApplication.Instance?.CacheBgImageBase();

            BetterFG.Customization.TexturePacks.TexturePackRuntimeService.SweepLoadedTextures();

            BetterFG.Utilities.PatchGate.SetRoundActive(false);
            // copy-code prompt hooks go in here and stay in — nothing can reach a lobby or show
            // tile before the menu exists. Install() no-ops once they're live.
            BetterFG.Utilities.PatchGate.SetActive("copycode.prompts", true);
            BetterFG.Utilities.PatchGate.SetActive("pastecode.prompts", true);
            BetterFG.Tweaks.BfgTweak.RaiseMainMenuEntered();
            BetterFG.Core.BfgBinds.OnMainMenuEntered();
            BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.SetInRound(false);
            BetterFG.Services.DiscordPresenceService.OnMainMenuEntered(__instance);
            BetterFG.Features.Replay.ReplayViewer.OnMainMenuEntered();
            BetterFG.Patches.SeasonProgressHoverPatch.SetMenuActive(true);
            BetterFG.Customization.Pets.PetPreview.RetryOnMainMenuEntered();
            BetterFG.Features.CreativeExpansion.Definers.Teardown();
            BetterFG.Features.CreativeExpansion.DisableBackgroundRulebook.OnMainMenuEntered();

            BetterFG.UI.Tabs.NametagTab.CacheNameAssets();
            BetterFG.UI.Tabs.AllCosmeticsTab.RefreshLiveInstances();
            BetterFG.UI.Tabs.CustomSkinTextureTab.RefreshLiveInstances();
            BetterFG.UI.BetterFGUIMan.ResolveAsapFont();

            MenuCustomizationApplication.Instance.StartCoroutine(MenuCustomizationApplication.ReapplyForegroundFromSettingsCoroutine().WrapToIl2Cpp());

            FontReplacementService.ReapplyFromSettings();
            MenuCustomizationApplication.Instance.StartCoroutine(HealFontAfterMenuEntered().WrapToIl2Cpp());

            FeatureStars.CreateInMenu();
            MenuCustomizationApplication.Instance.StartCoroutine(CreateQualTabAfterForegroundApplied().WrapToIl2Cpp());
            BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.Refresh(true);

            if (__instance._menuFallGuy != null)
                BeanMonitorService.PushBean(__instance._menuFallGuy);
            if (__instance._lobbyFallGuy != null)
                BeanMonitorService.PushBean(__instance._lobbyFallGuy);
            SkinApplicationService.Instance?.RestoreSavedGameCosmetics();

            BetterFG.Patches.ShowSelectorBg.AttachApplier();

            var creativeEditorBg = GameObject.Find("CameraRig")?.transform.Find("VirtualCameras/MainMenu_LevelEditor/Generic_UI_CreativeBackground_Prefab_Canvas");
            if (creativeEditorBg != null && creativeEditorBg.GetComponent<CreativeEditorBgApplier>() == null)
                creativeEditorBg.gameObject.AddComponent<CreativeEditorBgApplier>();

            MenuCustomizationApplication.Instance?.ReapplyToMainMenu();
            MenuCustomizationApplication.Instance?.SpawnMenuBg();

            if (MenuCustomizationApplication.Instance != null)
            {
                MenuCustomizationApplication.Instance._pendingFgReapply = true;
                MenuCustomizationApplication._fullCanvasReapplyPending = true;
            }

            if (__instance._menuFallGuy != null)
                PlayerScaleService.ApplyToBean(__instance._menuFallGuy, PlayerScaleService.GetPlayerScale(), PlayerScaleService.BeanScaleMode.Local);

            SkinApplicationService.Instance.TryAutoReapplyCustomTextureForBean(__instance._menuFallGuy);
            if (BeanMonitorService.Instance != null)
                BeanMonitorService.Instance.StartCoroutine(DelayedCustomSkinTextureReapply().WrapToIl2Cpp());

            try { NetworkClient.Instance?.RegisterLocalProfile(); } catch { }

            try
            {
                if (UnityEngine.Random.value < 0.0001f)
                {
                    var holder = GameObject.Find("3D Environment/MainMenu_Environment/PlinthRig/CharacterAndPlinthHolder_Main/ENV_Plinth_MO/CharacterHolder");
                    if (holder != null)
                    {
                        holder.transform.localScale = new Vector3(1f, -1f, 1f);
                        holder.transform.localPosition = new Vector3(0f, 7.04f, 0f);
                    }
                }
            }
            catch { }

            var localName = GlobalGameStateClient.Instance?.GetLocalPlayerName();
            if (!string.IsNullOrEmpty(localName))
            {
                LocalPlayerInfo.FGlocalplayerusername = localName;
                string platformName = "";
                try { platformName = GlobalGameStateClient.Instance?.PlayerProfile?.PlatformAccountName ?? ""; } catch { }
                Plugin.Log.LogInfo($"MainMenuBean: FG username set: {localName}"
                    + (string.IsNullOrEmpty(platformName) || platformName == localName ? "" : $" (party menu shows you as '{platformName}')"));
            }

#if PROFILES
            NetworkClient.PrimeProfilesForLobby(force: true);
            BetterFG.Customization.Profiles.LobbyProfileService.ApplyToLobby();
#endif

            if (__instance != null)
                __instance.StartCoroutine(RefocusParentAfterMenuEntered(__instance).WrapToIl2Cpp());

            if (__instance != null)
                __instance.StartCoroutine(HideMenuClutter().WrapToIl2Cpp());

            if (BetterFG.Services.MenuMusicService.Enabled
                && !string.IsNullOrEmpty(BetterFG.Services.MenuMusicService.CurrentPath))
                BetterFG.Services.MenuMusicService.Play();
        }

        private static IEnumerator HideMenuClutter()
        {
            yield return null;
            var canvas = GameObject.Find("UICanvas_Client_V2(Clone)")?.transform;
            canvas?.Find("Default/MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Main/Prime_UI_MainMenu_Canvas(Clone)/SafeArea/BottomRight_Group")?.gameObject.SetActive(true);
            canvas?.Find("Default/Topbar_Prime(Clone)/SafeArea/TabsHorizontalLayout/SeasonPassButton")?.gameObject.SetActive(true);
        }

        private static IEnumerator RefocusParentAfterMenuEntered(MainMenuManager mmm)
        {
            yield return null;
            yield return null;

            var focusHandler = mmm?._mainMenuBuilder?._focusHandler;
            if (focusHandler != null)
            {
                int idx = focusHandler._focusedSwitchableViewIndex;
                if (idx < 0) idx = 0;
                focusHandler.GainFocusOnSwitchableViewModel(idx);
            }

            var builder = GameObject.Find("UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)");
            builder?.GetComponent<SwitchableViewFocusHandler>()?.OnParentViewGainedFocus();

            BetterFG.Nametag.NametagPatchHub.RefreshRemoteNametags();
        }

        private static IEnumerator DelayedCustomSkinTextureReapply()
        {
            yield return new WaitForSeconds(0.3f);
            SkinApplicationService.ReapplyAllEnabledFromSettings();
            SkinApplicationService.Instance?.ReapplyExpectedGameCosmeticVisuals();
        }

        private static IEnumerator CreateQualTabAfterForegroundApplied()
        {
            yield return new WaitForSeconds(0.05f);
            FeatureQualificationTime.CreateInMenu();
        }

        private static IEnumerator HealFontAfterMenuEntered()
        {
            yield return new WaitForSeconds(0.3f);
            FontReplacementService.HealAndReapply();
            yield return new WaitForSeconds(0.7f);
            FontReplacementService.HealAndReapply();
        }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.ShowLobbyScreen), new[] { typeof(OnDisplayLobby) })]
    public class LobbyBean
    {
        [HarmonyPostfix]
        public static void OnShowLobbyScreen(MainMenuManager __instance)
        {
            if (__instance._lobbyFallGuy == null) return;
            BeanMonitorService.PushBean(__instance._lobbyFallGuy);
            var bean = __instance._lobbyFallGuy;
            BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.Refresh(false, 0.5f);
            MenuCustomizationApplication.Instance.StartCoroutine(MenuCustomizationApplication.ApplyLobbyBGForegroundNextFrame().WrapToIl2Cpp());
            SkinApplicationService.Instance?.ReapplyExpectedGameCosmeticVisuals(bean);

            MenuCustomizationApplication.Instance?.StartCoroutine(ApplyScalingNextFrame().WrapToIl2Cpp());

#if PROFILES
            NetworkClient.PrimeProfilesForLobby(force: true);
            BetterFG.Customization.Profiles.LobbyProfileService.ApplyToLobby();
#endif
        }

        private static IEnumerator ApplyScalingNextFrame()
        {
            yield return null;
            UIScalingTab.ApplyCanvasScalingFromSettings();
        }
    }

    [HarmonyPatch(typeof(FGClient.CelebrationPreview), "CustomiseFallGuy")]
    public class CelebrationPreviewBean
    {
        [HarmonyPostfix]
        public static void Postfix(GameObject __0)
        {
            if (__0 == null) return;
            BeanMonitorService.PushBean(__0);
            SkinApplicationService.Instance?.ReapplyExpectedGameCosmeticVisuals(__0);
        }
    }

    [HarmonyPatch(typeof(MainMenuFallGuy), nameof(MainMenuFallGuy.OnAnimatedInComplete))]
    public class MainMenuFallGuyAnimatedIn
    {
        [HarmonyPostfix]
        public static void Postfix(MainMenuFallGuy __instance)
        {
#if PROFILES
            NetworkClient.PrimeProfilesForLobby(force: true);
            BetterFG.Customization.Profiles.LobbyProfileService.ApplyToFallGuy(__instance);
#endif
        }
    }

    [HarmonyPatch(typeof(PartyNameTag), nameof(PartyNameTag.SetStatus))]
    public class PartyNameTagStatusChanged
    {
        [HarmonyPostfix]
        static void Postfix(PartyNameTag __instance)
        {
            if (__instance == null) return;
            BetterFG.Customization.UI.MenuCustomizationApplication.Instance?
                .QueueForegroundScope(__instance.transform, anyImage: true);
#if PROFILES
            BetterFG.Customization.Profiles.LobbyProfileService.ReapplyTag(__instance);
#endif
        }
    }

    [HarmonyPatch(typeof(PartyNameTag), nameof(PartyNameTag.HandlePlayerJoinedOrLeft))]
    public class PartyNameTagJoinLeave
    {
        [HarmonyPostfix]
        public static void Postfix(IPartyMember member, PartyService.RemoteMemberLoginState state)
        {
#if PROFILES
            if (state == PartyService.RemoteMemberLoginState.Left)
                BetterFG.Customization.Profiles.LobbyProfileService.ClearLobby();
#endif
            BetterFG.Services.DiscordPresenceService.Push();
        }
    }

    // this is the 3D-space nametag floating over a bean in the main menu (PlayerInfo = local,
    // PartyPlayerInfo = remote) — a totally different widget from NameTagViewModel/PlayerInfoDisplay,
    // so NametagPatchHub never touches it. traced live: SetUsernameText fires exactly once per member
    // (join, or the initial local spawn), immediately followed by SetNameMaterial in the same call
    // burst, which resets the font material our colour tag relies on — so reapplying synchronously
    // got stomped. UpdateForMember re-fires every few seconds after that (icon/status/material poll)
    // but never touches the text again, so a same-burst stomp meant the tag stuck wrong forever.
    // wait a frame so we land after that whole burst.
    [HarmonyPatch(typeof(PartyNameTag), nameof(PartyNameTag.SetUsernameText))]
    public class PartyNameTagSetUsernameText
    {
        [HarmonyPostfix]
        public static void Postfix(PartyNameTag __instance)
        {
            if (__instance == null) return;
            MenuCustomizationApplication.Instance?.StartCoroutine(ReapplyNextFrame(__instance).WrapToIl2Cpp());
        }

        private static System.Collections.IEnumerator ReapplyNextFrame(PartyNameTag tag)
        {
            yield return null;
            if (tag == null) yield break;

            MenuCustomizationApplication.Instance?.ReapplyForegroundFromSettings(tag.transform, null, anyImage: true);

            var tmp = tag._usernameText;
            if (tmp == null) yield break;

            string current = tmp.text;
            string localName = LocalPlayerInfo.FGlocalplayerusername;
            bool isLocal = !string.IsNullOrEmpty(localName)
                && FallGuysLib.Players.PlayerUtils.CleanPlayerName(current) == localName;
            if (!isLocal)
            {
#if PROFILES
                BetterFG.Customization.Profiles.LobbyProfileService.ReapplyTag(tag);
#endif
                yield break;
            }

            bool useDisplay = SettingsService.Get("nametag.enabled", "false") == "true"
                              || !string.IsNullOrEmpty(LocalPlayerInfo.CustomName);
            string name = useDisplay ? LocalPlayerInfo.DisplayName : localName;
            NametagIconApplicator.ApplyToNameplate(tmp, name, NametagIconApplicator.NameplateType.Party);
        }
    }

    // lobby party-slot preview beans (Prefab_UI_Lobby._partyCharacters, one UILobbyCharacter per
    // slot) get pushed into the eye feature while still disabled — their skinned mesh isn't settled
    // yet, so the eye carve/attach silently fails and the tracked entry is left dead. UpdateMember is
    // the game's own per-member refresh, so re-push each slot's bean here now that it's actually live;
    // Apply() already no-ops on a duplicate reference.
    [HarmonyPatch(typeof(Prefab_UI_Lobby), nameof(Prefab_UI_Lobby.UpdateMember))]
    internal static class PrefabUILobbyUpdateMember
    {
        [HarmonyPostfix]
        public static void Postfix(Prefab_UI_Lobby __instance, IPartyMember remoteMember)
        {
            var characters = __instance?._partyCharacters;
            if (characters == null) return;

            for (int i = 0; i < characters.Count; i++)
            {
                var character = characters[i];
                var bean = character?._fallGuyGO;
                if (bean == null || !bean.activeInHierarchy) continue;
                BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.Apply(bean, isLocalPlayer: false);
            }

#if PROFILES
            // MenuIndex is the same slot ordering _partyCharacters is built in - unconfirmed live,
            // watch for a "profile 'X' -> slot N" log line landing on the wrong bean and holler.
            if (remoteMember != null && !remoteMember.IsLocal)
            {
                int idx = remoteMember.MenuIndex;
                var bean = idx >= 0 && idx < characters.Count ? characters[idx]?._fallGuyGO : null;
                string cleanName = PlayerUtils.CleanPlayerName(remoteMember.NameKey ?? "");
                var rp = !string.IsNullOrEmpty(cleanName)
                    ? BetterFG.Customization.Profiles.ProfileService.GetRemoteProfileForName(cleanName)
                    : null;
                if (bean != null && rp != null)
                {
                    BetterFG.Customization.Player.CustomizationHandler.Dress(rp, bean);
                    BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.ApplyProfileOverride(bean, rp);
                    Plugin.Log.LogInfo($"falling screen: profile '{cleanName}' -> slot {idx}");
                }
            }
#endif
        }
    }

    [HarmonyPatch(typeof(PartyStateManager), nameof(PartyStateManager.AddOrRemovePlayerFromParty))]
    internal static class PartyRosterChangePresence
    {
        [HarmonyPostfix]
        public static void Postfix() => BetterFG.Services.DiscordPresenceService.Push();
    }

    [HarmonyPatch(typeof(PartyStateManager), nameof(PartyStateManager.Core_HandlePartyLeft))]
    internal static class PartyLeftPresence
    {
        [HarmonyPostfix]
        public static void Postfix() => BetterFG.Services.DiscordPresenceService.Push();
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.NavigateToView), new[] { typeof(MainMenuViews) })]
    public class Patch_NavigateToView
    {
        [HarmonyPostfix]
        public static void Postfix(MainMenuManager __instance, MainMenuViews view)
        {
            if (view == MainMenuViews.Lobby)
            {
                MenuCustomizationApplication.AutoApplyCamFromSettings();
                var builder = GameObject.Find("UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)");
                MenuCustomizationApplication.Instance.StartCoroutine(
                    MenuCustomizationApplication.ReapplyForegroundFromSettingsCoroutine(builder != null ? builder.transform : null).WrapToIl2Cpp());
            }
            else if (view == MainMenuViews.Settings)
                MenuCustomizationApplication.Instance?.ReapplyPatternNextFrame();

            SkinApplicationService.Instance?.ReapplyExpectedGameCosmeticVisuals();
            BetterFG.Services.DiscordPresenceService.Push();
            BetterFG.Customization.TexturePacks.TexturePackRuntimeService.SweepLoadedTextures();
        }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.PauseMusic))]
    public class Patch_MainMenuMusic_PauseSync
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!BetterFG.Services.MenuMusicService.Enabled) return;
            BetterFG.Services.MenuMusicService.Pause();
        }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.StopMusic))]
    public class Patch_MainMenuMusic_StopSync
    {
        [HarmonyPostfix]
        public static void Postfix(MainMenuManager __instance)
        {
            if (!BetterFG.Services.MenuMusicService.Enabled) return;
            BetterFG.Services.MenuMusicService.Resume();
            if (__instance != null)
                __instance.StartCoroutine(SilenceAfterStop(__instance).WrapToIl2Cpp());
        }

        private static IEnumerator SilenceAfterStop(MainMenuManager mmm)
        {
            yield return null;
            for (int i = 0; i < 40; i++)
            {
                try
                {
                    var inst = mmm?._menuMusic;
                    if (inst != null) { inst.SetPaused(true); yield break; }
                }
                catch { }
                yield return new WaitForSeconds(0.15f);
            }
        }
    }


    [HarmonyPatch(typeof(ModalMessageBaseViewModel), nameof(ModalMessageBaseViewModel.SetModalBaseData))]
    public class Patch_ModalMessageBase_SetData
    {
        [HarmonyPostfix]
        public static void Postfix(ModalMessageBaseViewModel __instance)
        {
            MenuCustomizationApplication.Instance?.ReapplyForegroundFromSettings(__instance?.transform);
        }
    }

    [HarmonyPatch]
    public class Patch_ModalMessage_SetData
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            Type[] types =
            {
                typeof(ModalMessagePopupViewModel),
                typeof(ModalMessageOptInPopupViewModel),
                typeof(ModalMessageWithOptionSelectionPopupViewModel),
                typeof(ModalMessageWithDropdownFieldViewModel),
                typeof(ModalMessageImagePopupViewModel),
                typeof(ModalMessageTagPopupViewModel),
                typeof(ModalMessageWithDropdownFieldWithFilterViewModel),
                typeof(ModalMessageWithInputFieldViewModel),
                typeof(ModalMessageWithDlcImagePopupViewModel)
            };

            for (int i = 0; i < types.Length; i++)
            {
                var method = AccessTools.DeclaredMethod(types[i], "SetData", new[] { typeof(Il2CppSystem.Object) });
                if (method != null && !method.ContainsGenericParameters)
                    yield return method;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Component __instance)
        {
            MenuCustomizationApplication.Instance?.ReapplyForegroundFromSettings(__instance?.transform);
        }
    }

    [HarmonyPatch(typeof(ShowSelectorViewModel), "ShowMainMenu")]
    public class Patch_ShowSelectorShowMainMenu
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            BetterFG.Services.DiscordPresenceService.OnShowSelectorClosed();
            BetterFG.Tweaks.ShowTilePlaysTweak.PruneCache();
            BetterFG.Tweaks.ShowTileDislikesTweak.PruneCache();
            MenuCustomizationApplication.AutoApplyCamFromSettings();

            var app = MenuCustomizationApplication.Instance;
            if (app != null)
            {
                bool wasPendingFgReapply = app._pendingFgReapply;
                app._pendingFgReapply = false;

                var partymenu = GameObject.Find("UICanvas_Client_V2(Clone)/PartyMenu/").transform;

                Transform scopeRoot = null;
                if (MenuCustomizationApplication._fullCanvasReapplyPending)
                {
                    MenuCustomizationApplication._fullCanvasReapplyPending = false;
                    var rootGo = GameObject.Find("UICanvas_Client_V2(Clone)");
                    if (rootGo != null) scopeRoot = rootGo.transform;
                }
                else
                {
                    if (!wasPendingFgReapply)
                    {
                        return;
                    }

                    var rootGo = GameObject.Find("UICanvas_Client_V2(Clone)/Default/");
                    if (rootGo != null) scopeRoot = rootGo.transform;
                }

                MenuCustomizationApplication.Instance.StartCoroutine(MenuCustomizationApplication.ReapplyForegroundFromSettingsCoroutine(scopeRoot).WrapToIl2Cpp());
            }
        }
    }

    public class Patch_BindMeshToFallguy
    {
        [HarmonyPostfix]
        public static void ReapplyCustomTexture(FallguyCustomisationHandler __instance, SkinnedMeshRenderer mesh)
        {
            var svc = SkinApplicationService.Instance;
            if (svc == null || __instance == null) return;
            if (svc.IsBindingGameCosmetic(__instance.gameObject)) return;
            svc.ApplyEnabledEntriesToRenderer(__instance.gameObject, mesh);
        }
    }


    [HarmonyPatch(typeof(RoundLoader), "LoadViaShareCodeAndVersion")]
    public class UGCLevelLoad
    {
        [HarmonyPostfix]
        public static void Postfix(Round round)
        {
            BetterFG.Features.QualificationTime.FeatureQualificationTime.OnLoadViaShareCodeAndVersion(round);
            BetterFG.Services.DiscordPresenceService.OnLoadingRound(round);

            var desc = round?.RoundDescription?.Text;
            if (string.IsNullOrEmpty(desc))
            {
                BetterFGUnityRounds.ResetRoundState(unloadBundle: true);
                BetterFGUnityRounds.RestoreEnvironment();
                return;
            }
            Plugin.Log.LogInfo($"UGCLevelLoad: round desc: {desc}");
            if (!BetterFGUnityRounds.TryHandleDescription(desc))
            {
                BetterFGUnityRounds.ResetRoundState(unloadBundle: true);
                BetterFGUnityRounds.RestoreEnvironment();
            }
        }
    }

    [HarmonyPatch(typeof(LevelEditorStatePlay), "Initialise")]
    public class LevelEditorStatePlayPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            BeanMonitorService.CheckLevelEditorBean();
            CameraUtils.DisableXRayRenderer();
            BetterFG.Features.CreativeExpansion.CustomIntroCams.ShowShotGizmos(false);
        }
    }


    [HarmonyPatch(typeof(RoundLoader), nameof(RoundLoader.NotifyLoadingFinished))]
    public class NotifyLoadingFinishedPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            BetterFG.Nametag.NametagIconApplicator.ForgetRecentKeys();
            BeanMonitorService.ForgetForeignBeans();
            BetterFGUnityRounds.MarkSceneReadyAndInstantiateQueuedRound();
            BetterFG.Features.CreativeExpansion.CreativeGroups.OnLevelLoaded();
            BetterFG.Features.CreativeExpansion.CustomLights.InvalidateRoundLights();
            BetterFG.Features.CreativeExpansion.NeonMaterial.ResetEditorState();
            BetterFG.Features.CreativeExpansion.RuleColours.ResetEditorState();
            BetterFG.Features.CreativeExpansion.SlimeColours.ResetEditorState();
            BetterFG.Features.CreativeExpansion.ObjectThemes.ResetEditorState();
            BetterFG.Utilities.IdentifierObjects.InvalidateLevelJson();
            BetterFG.Features.CreativeExpansion.DisableBackgroundRulebook.Invalidate();
            BetterFG.Customization.TexturePacks.TexturePackRuntimeService.SweepLoadedTextures();

            var host = BeanMonitorService.Instance;
            if (host != null) host.StartCoroutine(AfterLoad().WrapToIl2Cpp());
            else AfterLoadNow();
        }

        // Every call below walks the whole scene or the level json. Run inline it stalls the frame
        // the client is meant to report "finished loading" on, and the server drops us mid-load.
        private static IEnumerator AfterLoad()
        {
            yield return null;
            AfterLoadNow();
        }

        private static void AfterLoadNow()
        {
            BetterFG.Features.CreativeExpansion.Definers.Recheck();
            BetterFG.Features.CreativeExpansion.CustomLights.SyncDelayed();
            BetterFG.Customization.UI.CreativeGizmoScale.Apply();
            BetterFG.Tweaks.RetroGoopMaterialsTweak.OnRoundLoaded();

            // CEP leaves checkpoint/spawn zone flags visible so it can dynamically place them in the
            // editor - fine there, but they leak into actual UGC rounds too. Only hide in-round.
            if (GlobalGameStateClient.Instance?.IsInCreativeEditor != true)
            {
                foreach (var checker in UnityEngine.Object.FindObjectsOfType<LevelEditorCheckpointTriggerZoneChecker>())
                    checker.ShowVisual(false);
            }
        }
    }

    [HarmonyPatch(typeof(RoundLoader), nameof(RoundLoader.BeginShowUGCLoadingGameScreenViewModel))]
    public class BeginShowUGCLoadingGameScreenViewModelPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            MenuCustomizationApplication.Instance.StartCoroutine(MenuCustomizationApplication.ReapplyForegroundFromSettingsCoroutine().WrapToIl2Cpp());
        }
    }

    [HarmonyPatch(typeof(FGClient.UI.GameplayInstructionsViewModel), "set_ObjectiveText")]
    internal static class GameplayInstructionsObjectiveTextPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref string value)
            => value = BetterFG.Tweaks.ObjectiveRoundNumberTweak.AppendRoundNumber(value);
    }

    [HarmonyPatch(typeof(LoadingUGCGameScreenViewModel), "InitTexts")]
    public class LoadingUGCInitTextsPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => BetterFGUnityRounds.ApplyDescriptionNow();
    }

    [HarmonyPatch(typeof(LoadingUGCGameScreenViewModel), "SetData")]
    public class LoadingUGCSetDataPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => BetterFGUnityRounds.ApplyDescriptionNow();
    }

    [HarmonyPatch(typeof(InGameMenuViewModel), nameof(InGameMenuViewModel.ToggleOpen))]
    public class InGameMenuViewModelToggleOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(bool isInGameMenuOpen, bool playSound)
        {
            if (!isInGameMenuOpen) return;
            MenuCustomizationApplication.Instance.StartCoroutine(MenuCustomizationApplication.ReapplyForegroundFromSettingsCoroutine().WrapToIl2Cpp());
        }
    }

    [HarmonyPatch(typeof(UGCLevelLikeScreenViewModel), nameof(UGCLevelLikeScreenViewModel.Initialise))]
    public class UGCLevelLikeScreenViewModelToggleOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            MenuCustomizationApplication.Instance.StartCoroutine(MenuCustomizationApplication.ReapplyForegroundFromSettingsCoroutine().WrapToIl2Cpp());
        }
    }

    [HarmonyPatch(typeof(PrivateLobbyShowListEntryViewModel), "SetData")]
    internal static class PrivateLobbyShowListEntryViewModelSetDataPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PrivateLobbyShowListEntryViewModel __instance)
        {
            MenuCustomizationApplication.Instance?.ReapplyPrivateLobbyShowEntryForeground(__instance.transform);
        }
    }

    [HarmonyPatch(typeof(ShowSelectorShowTileViewModel), nameof(ShowSelectorShowTileViewModel.SetIndividualShowData))]
    internal static class ShowSelectorShowTileViewModelSetIndividualShowDataPatch
    {
        private const string PANEL_FILL_PATH = "ShowTileHolder/NestedPanel/MainPanel/Panel_Fill";

        [HarmonyPostfix]
        public static void Postfix(ShowSelectorShowTileViewModel __instance, ShowSelectorShow showSelectorShow)
        {
            if (__instance == null) return;
            BetterFG.Services.DiscordPresenceService.OnShowSelectorTileSeen();
            MenuCustomizationApplication.Instance?.QueueForegroundScope(__instance.transform);
            BetterFG.Features.Stars.FeatureStars.OnSetIndividualShowData(__instance, showSelectorShow);
            BetterFG.Tweaks.ShowTilePlaysTweak.OnSetShowData(__instance, showSelectorShow);
            BetterFG.Tweaks.ShowTileDislikesTweak.OnSetShowData(__instance, showSelectorShow);
        }
    }

    [HarmonyPatch(typeof(PrivateLobbyShowTagViewModel), nameof(PrivateLobbyShowTagViewModel.SetPrivateLobbyShowTagData))]
    internal static class PrivateLobbyShowTagDataPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PrivateLobbyShowTagViewModel __instance)
        {
            BetterFG.Tweaks.ShowTileDislikesTweak.OnPlobbyTag(__instance);
        }
    }

    [HarmonyPatch(typeof(ShowSelectorShowTileViewModel), nameof(ShowSelectorShowTileViewModel.OnShowConfirmed))]
    internal static class ShowSelectorShowTileViewModelOnShowConfirmedPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ShowSelectorShowTileViewModel __instance)
        {
            var id = __instance.ShowData?.ShowSelectorShow?.Id;
            if (!BetterFG.Tweaks.UpcomingShowsTweak.Injected.Contains(id)) return true;
            Plugin.Log.LogInfo($"swallowed a confirm on upcoming show {id}, it hasn't opened yet");
            return false;
        }
    }

    [HarmonyPatch(typeof(Wushu.LevelEditor.Runtime.UI.LevelBrowser.LevelBrowserTileViewModel), "SetData")]
    internal static class LevelBrowserTileViewModelSetDataPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Wushu.LevelEditor.Runtime.UI.LevelBrowser.LevelBrowserTileViewModel __instance)
        {
            if (__instance == null) return;
            MenuCustomizationApplication.Instance?.QueueForegroundScope(__instance.transform, true);
            BetterFG.Tweaks.ShowTileDislikesTweak.OnBrowserTile(__instance);
        }
    }

    // tags populate after the tile's own SetData fires, so the tile-scoped sweep above always
    // misses them on first entry — hook the tag's own data point so it colours itself the moment
    // it actually exists, instead of relying on manual apply to catch it late.
    [HarmonyPatch(typeof(Wushu.LevelEditor.Runtime.UI.LevelBrowser.LevelBrowserTileTagViewModel), "SetData")]
    internal static class LevelBrowserTileTagViewModelSetDataPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Wushu.LevelEditor.Runtime.UI.LevelBrowser.LevelBrowserTileTagViewModel __instance)
        {
            if (__instance == null) return;
            MenuCustomizationApplication.Instance?.QueueForegroundScope(__instance.transform, true);
        }
    }

    // tile hover / nav-highlight in the creative Level Browser. feeds LevelBrowserPortPrompt so it
    // can float the import/export nav prompt while a real level sits under the cursor. patching the
    // screen's OnHighlightEvent instead crashes Harmony's glue — it takes a struct-by-value param.
    [HarmonyPatch(typeof(Wushu.LevelEditor.Runtime.UI.LevelBrowser.LevelBrowserTileViewModel), "OnSelected")]
    internal static class LevelBrowserTileSelectedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Wushu.LevelEditor.Runtime.UI.LevelBrowser.LevelBrowserTileViewModel __instance)
        {
            if (__instance == null) return;
            BetterFG.Features.CreativeExpansion.LevelBrowserPortPrompt.OnTileSelected(__instance);
        }
    }

    // private lobby screen focus. feeds CopyCodePrompt so the copy-code prompt lives and dies with
    // the screen's own focus, the way the game's built-in nav prompts do.
    // all four go in once the main menu has opened and stay in — none of their targets can be hit
    // before that, so they'd only be trampolines sitting in the boot path.
    [Utilities.BfgPatchGate("copycode.prompts")]
    [HarmonyPatch(typeof(FGClient.UI.PrivateLobby.PrivateLobbyScreenViewModel), "OnGainFocus")]
    internal static class PrivateLobbyScreenGainFocusPatch
    {
        [HarmonyPostfix]
        public static void Postfix(FGClient.UI.PrivateLobby.PrivateLobbyScreenViewModel __instance)
        {
            if (__instance == null) return;
            BetterFG.Features.CreativeExpansion.CopyCodePrompt.OnLobbyFocus(__instance);
        }
    }

    [Utilities.BfgPatchGate("copycode.prompts")]
    [HarmonyPatch(typeof(FGClient.UI.PrivateLobby.PrivateLobbyScreenViewModel), "OnLoseFocus")]
    internal static class PrivateLobbyScreenLoseFocusPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => BetterFG.Features.CreativeExpansion.CopyCodePrompt.OnLobbyBlur();
    }

    // "Enter Code" input field enable/disable. feeds PasteCodePrompt so the paste-code nav prompt
    // lives and dies with the field the way the game's own prompts do.
    [Utilities.BfgPatchGate("pastecode.prompts")]
    [HarmonyPatch(typeof(InputCodeViewModel), "OnEnable")]
    internal static class InputCodeViewModelEnabledPatch
    {
        [HarmonyPostfix]
        public static void Postfix(InputCodeViewModel __instance)
        {
            if (__instance == null) return;
            BetterFG.Features.CreativeExpansion.PasteCodePrompt.OnInputEnabled(__instance);
        }
    }

    [Utilities.BfgPatchGate("pastecode.prompts")]
    [HarmonyPatch(typeof(InputCodeViewModel), "OnDisable")]
    internal static class InputCodeViewModelDisabledPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => BetterFG.Features.CreativeExpansion.PasteCodePrompt.OnInputDisabled();
    }

    // the game's own OnSelect/OnDeselect chain doesn't reliably hand focus back to the private
    // lobby screen when the field loses selection (click-away, alt-tab, submit) — nav just goes
    // dead, mouse-only, until something else forces the lobby to regain focus. restore it
    // ourselves right at the field's own defocus/submit events. same GameObject lookup path
    // LobbyAudioPromptTweak / LobbyCustomiserTweak already use.
    //
    // hooked on the input field, NOT InputCodeViewModel.OnDeselect(string): il2cpp deduplicates
    // identical method bodies, and that one shares its native address with the parameterless
    // LevelEditorColourPickerInputFieldItem.HideCaret(). harmony detours the address, so the glue
    // read a nonexistent string argument off a garbage register and hard-crashed the process in
    // Il2CppStringToManaged the moment any creative colour picker opened. Kick() no-ops off the
    // lobby, so the wider hook costs nothing.
    [Utilities.BfgPatchGate("pastecode.prompts")]
    [HarmonyPatch(typeof(UICustomTMP_InputField), nameof(UICustomTMP_InputField.OnDeselect))]
    internal static class InputCodeViewModelDeselectPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => RestoreLobbyFocus.Kick();
    }

    [Utilities.BfgPatchGate("pastecode.prompts")]
    [HarmonyPatch(typeof(InputCodeViewModel), nameof(InputCodeViewModel.OnConfirmPressed))]
    internal static class InputCodeViewModelConfirmPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => RestoreLobbyFocus.Kick();
    }

    internal static class RestoreLobbyFocus
    {
        private const string LobbyCanvasPath = "UICanvas_Client_V2(Clone)/Default/Prime_UI_PrivateLobby_Canvas(Clone)";

        public static void Kick()
        {
            var go = GameObject.Find(LobbyCanvasPath);
            if (go == null) return;
            var vm = go.GetComponentInChildren<FGClient.UI.PrivateLobby.PrivateLobbyScreenViewModel>(true);
            vm?.OnGainFocus();
        }
    }

    // discovery / show-selector highlight. NOT the tile: reading ShowData off a tile crashes, its
    // backing pointer goes stale as the selector rebuilds. The preview panel follows the highlight
    // and is handed the show as an argument, which is safe to read.
    [Utilities.BfgPatchGate("copycode.prompts")]
    [HarmonyPatch(typeof(ShowSelectorShowPreviewViewModel), nameof(ShowSelectorShowPreviewViewModel.SetIndividualShowData))]
    internal static class ShowPreviewIndividualShowPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ShowSelectorShow showSelectorShow)
            => BetterFG.Features.CreativeExpansion.CopyCodePrompt.OnShowPreviewed(showSelectorShow);
    }

    // highlight moved onto a nested-shows entry point — a folder of shows, no share code of its own
    [Utilities.BfgPatchGate("copycode.prompts")]
    [HarmonyPatch(typeof(ShowSelectorShowPreviewViewModel), nameof(ShowSelectorShowPreviewViewModel.SetNestedShowsEntryPointData))]
    internal static class ShowPreviewNestedShowsPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => BetterFG.Features.CreativeExpansion.CopyCodePrompt.OnShowPreviewCleared();
    }

    [Utilities.BfgPatchGate("copycode.prompts")]
    [HarmonyPatch(typeof(ShowSelectorViewModel), nameof(ShowSelectorViewModel.OnLoseFocus))]
    internal static class ShowSelectorLoseFocusPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => BetterFG.Features.CreativeExpansion.CopyCodePrompt.OnShowPreviewCleared();
    }

    // private lobby's own show list. the manager owns the highlight and exposes the highlighted
    // show's ShareCode, which is empty for official shows — so only a UGC round gets the prompt.
    [Utilities.BfgPatchGate("copycode.prompts")]
    [HarmonyPatch(typeof(FragglePlobbiesManager), nameof(FragglePlobbiesManager.ShowHighlighted))]
    internal static class PrivateLobbyShowHighlightedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(FragglePlobbiesManager __instance)
        {
            if (__instance == null) return;
            BetterFG.Features.CreativeExpansion.CopyCodePrompt.OnPrivateLobbyShowHighlighted(__instance);
        }
    }

    [Utilities.BfgPatchGate("copycode.prompts")]
    [HarmonyPatch(typeof(FGClient.UI.PrivateLobby.PrivateLobbyShowListViewModel), "OnDisable")]
    internal static class PrivateLobbyShowListDisabledPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            BetterFG.Features.CreativeExpansion.CopyCodePrompt.OnPrivateLobbyShowListClosed();
            BetterFG.Tweaks.RandomShowSelectTweak.OnShowListClosed();
            BetterFG.Tweaks.LobbyCustomiserTweak.OnPopupClosed();
        }
    }

    [HarmonyPatch(typeof(FragglePlobbiesManager), nameof(FragglePlobbiesManager.SelectShow))]
    internal static class PrivateLobbySelectShowPatch
    {
        [HarmonyPrefix]
        public static bool Prefix() => !BetterFG.Tweaks.LobbyShowSearchTweak.ShouldSuppressConfirm();
    }

    [HarmonyPatch(typeof(ShowsManager), nameof(ShowsManager.RefreshShowSelectorPageData))]
    internal static class ShowSelectorPageDataPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ShowSelectorPage __result)
        {
            BetterFG.Tweaks.UpcomingShowsTweak.OnPageBuilt(__result);
        }
    }

    [HarmonyPatch(typeof(ChallengesScreenViewModel), "SetData")]
    internal static class ChallengesScreenViewModelSetDataPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ChallengesScreenViewModel __instance, ChallengesScreenData challengesScreenData)
        {
            if (__instance == null) return;
            MenuCustomizationApplication.Instance?.ReapplyForegroundFromSettings(__instance.transform);
        }
    }

    [HarmonyPatch(typeof(ChallengesScreenViewModel), "OnEnable")]
    internal static class ChallengesScreenViewModelOnEnablePatch
    {
        [HarmonyPostfix]
        public static void Postfix(ChallengesScreenViewModel __instance)
        {
            if (__instance == null || MenuCustomizationApplication.Instance == null) return;
            MenuCustomizationApplication.Instance.HideImageBg();
            MenuCustomizationApplication.Instance.StartCoroutine(MenuCustomizationApplication.ReapplyForegroundFromSettingsCoroutine(__instance.transform).WrapToIl2Cpp());
        }
    }

    [HarmonyPatch(typeof(ChallengesScreenViewModel), "CloseChallenges")]
    internal static class ChallengesScreenViewModelCloseChallengesPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ChallengesScreenViewModel __instance)
        {
            var app = MenuCustomizationApplication.Instance;
            if (app == null || __instance == null) return;

            var view = __instance._mainMenuManager?.MainMenuBuilder?.SwitchableView;
            app.StartCoroutine(SetViewImplementationpatch.ApplyIndexZeroAfterFrames(view, 2).WrapToIl2Cpp());
        }
    }

    [HarmonyPatch(typeof(PrivateLobbyPlayerListEntryViewModel), "SetData")]
    internal static class PrivateLobbyPlayerListEntryViewModelSetDataPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PrivateLobbyPlayerListEntryViewModel __instance)
        {
            if (__instance == null) return;
            FeatureMorePlatformIcon.ApplyPrivateLobbyLocalName(__instance.transform, "", __instance.PlayerName);
            FeatureMorePlatformIcon.ApplyPrivateLobbyCustomName(__instance.transform, __instance.PlayerName);
        }
    }

    [HarmonyPatch(typeof(PrivateLobbyPlayerListViewModel), "OnEnable")]
    internal static class PrivateLobbyPlayerListViewModelNamePatch
    {
        [HarmonyPostfix]
        public static void Postfix(PrivateLobbyPlayerListViewModel __instance)
        {
            var app = MenuCustomizationApplication.Instance;
            if (__instance == null || app == null) return;

            app.StartCoroutine(app.ReapplySpecialForegroundNextFrame(MenuCustomizationApplication.SpecialScreen.PrivateLobbyPlayerList).WrapToIl2Cpp());
            app.StartCoroutine(FeatureMorePlatformIcon.ApplyPrivateLobbyAfterOpen(__instance).WrapToIl2Cpp());
        }
    }

    [HarmonyPatch(typeof(PrivateLobbyPlayerListViewModel), "UpdatePlayerList")]
    internal static class PrivateLobbyPlayerListViewModelUpdateListPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PrivateLobbyPlayerListViewModel __instance)
        {
            var app = MenuCustomizationApplication.Instance;
            if (__instance != null && app != null)
                app.StartCoroutine(app.ReapplySpecialForegroundNextFrame(MenuCustomizationApplication.SpecialScreen.PrivateLobbyPlayerList).WrapToIl2Cpp());

            FeatureMorePlatformIcon.QueuePrivateLobbyApply(__instance);
        }
    }

    [HarmonyPatch(typeof(PrivateLobbyPlayerListViewModel), "OnGainFocus")]
    internal static class PrivateLobbyPlayerListViewModelOnOpenedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PrivateLobbyPlayerListViewModel __instance)
        {
            if (__instance != null)
                FeatureMorePlatformIcon.QueuePrivateLobbyApply(__instance);
            BetterFG.Services.DiscordPresenceService.Push();
        }
    }

    [HarmonyPatch(typeof(PrivateLobbyShowListViewModel), "Awake")]
    internal static class PrivateLobbyShowListViewModelAwakePatch
    {
        [HarmonyPostfix]
        public static void Postfix(PrivateLobbyShowListViewModel __instance)
        {
            var app = MenuCustomizationApplication.Instance;
            app?.StartCoroutine(app.ReapplySpecialForegroundNextFrame(MenuCustomizationApplication.SpecialScreen.PrivateLobbyShowSelect).WrapToIl2Cpp());
            BetterFG.Tweaks.LobbyShowSearchTweak.OnShowListAwake(__instance);
            BetterFG.Tweaks.RandomShowSelectTweak.OnShowListAwake(__instance);
            BetterFG.Tweaks.LobbyCustomiserTweak.OnPopupOpened();
        }
    }

    [HarmonyPatch(typeof(PrivateLobbyPlayerListViewModel), "Awake")]
    internal static class PrivateLobbyPlayerListViewModelAwakePatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            var app = MenuCustomizationApplication.Instance;
            app?.StartCoroutine(app.ReapplySpecialForegroundNextFrame(MenuCustomizationApplication.SpecialScreen.PrivateLobbyPlayerList).WrapToIl2Cpp());
        }
    }

    // per-row view-profile/kick popup on a private lobby player row. opens via ToggleOpen, not
    // DoFadeIn (it's not a full screen), so it needs its own hook.
    [HarmonyPatch(typeof(PrivateLobbyPlayerActionViewModel), nameof(PrivateLobbyPlayerActionViewModel.ToggleOpen))]
    internal static class PrivateLobbyPlayerActionViewModelToggleOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PrivateLobbyPlayerActionViewModel __instance, bool value)
        {
            if (!value || __instance == null) return;
            MenuCustomizationApplication.Instance?.ReapplyForegroundFromSettings(__instance.transform);
        }
    }

    [HarmonyPatch(typeof(StateMatchmaking), nameof(StateMatchmaking.Initialise))]
    internal static class MatchmakingBeginpatch
    {
        [HarmonyPostfix]
        public static void Postfix() => BetterFG.Tweaks.MatchmakingQueueCountTweak.OnMatchmakingStart();
    }

    [HarmonyPatch(typeof(StateMatchmaking), nameof(StateMatchmaking.Teardown))]
    internal static class MatchmakingEndpatch
    {
        [HarmonyPostfix]
        public static void Postfix() => BetterFG.Tweaks.MatchmakingQueueCountTweak.OnMatchmakingEnd();
    }

    [HarmonyPatch(typeof(ClientGameManager), "HandleServerPlayerProgress")]
    internal static class HandleServerPlayerProgressHub
    {
        // the fall feed notification fires synchronously inside the original method - a postfix
        // here runs too late to have the qual time stored for FallFeedQualTimeTweak to read.
        [HarmonyPrefix]
        public static void Prefix(GameMessageServerPlayerProgress progressMessage)
        {
            BetterFG.Features.TimePlacement.FeatureTimePlacement.OnServerPlayerProgress(progressMessage);
        }

        [HarmonyPostfix]
        public static void Postfix(ClientGameManager __instance, GameMessageServerPlayerProgress progressMessage)
        {
            BetterFG.Features.Stars.FeatureStars.OnServerPlayerProgress(__instance, progressMessage);
            BetterFG.Features.QualificationTime.FeatureQualificationTime.OnServerPlayerProgress(__instance, progressMessage);
            BetterFG.Features.Replay.FeatureReplay.OnServerPlayerProgress(progressMessage);
            BetterFG.Services.DiscordPresenceService.OnPlayerProgress(__instance, progressMessage);
        }
    }

    [HarmonyPatch(typeof(ClientGameManager), nameof(ClientGameManager.HandleRequiredQualificationProgressUpdate))]
    internal static class RequiredQualificationUpdateHub
    {
        [HarmonyPostfix]
        public static void Postfix() => BetterFG.Services.DiscordPresenceService.Push();
    }

    [HarmonyPatch(typeof(LevelEditorCostManager), nameof(LevelEditorCostManager.SetUsedBuildPoints))]
    internal static class CreativeBudgetPresenceHub
    {
        [HarmonyPostfix]
        public static void Postfix() => BetterFG.Services.DiscordPresenceService.RequestPush();
    }

    [HarmonyPatch(typeof(ClientGameManager), nameof(ClientGameManager.HandleServerRoundResults))]
    internal static class HandleServerRoundResultsHub
    {
        [HarmonyPostfix]
        public static void Postfix(ClientGameManager __instance)
        {
        }
    }

    [HarmonyPatch(typeof(BootSplashScreenViewModel), nameof(BootSplashScreenViewModel.Awake))]
    internal static class BootSplashUiBuild
    {
        [HarmonyPostfix]
        public static void Postfix() => Plugin.BuildModUI();
    }

    [HarmonyPatch(typeof(LoadingScreenViewModel), nameof(LoadingScreenViewModel.UpdateDisplay))]
    internal static class LoadingScreenUpdateDisplayHub
    {
        [HarmonyPostfix]
        public static void Postfix(LoadingScreenViewModel __instance)
        {
            if (__instance == null) return;
            BetterFG.Tweaks.ChangeSplashScreenTweak.OnLoadingScreenUpdateDisplay(__instance);
            var game = __instance.TryCast<LoadingGameScreenViewModel>();
            if (game == null) return;
            BetterFG.Features.QualificationTime.FeatureQualificationTime.OnLoadingScreenUpdateDisplay();
            BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.Refresh();
            var ugc = __instance.TryCast<LoadingUGCGameScreenViewModel>();
            string loadingRound = ugc?._loadingGameScreenDto?.levelInfoDto?.Title;
            if (string.IsNullOrEmpty(loadingRound)) loadingRound = game._round?.DisplayNameUnindented;
            if (string.IsNullOrEmpty(loadingRound)) loadingRound = game.RoundNameText;

            BetterFG.Services.DiscordPresenceService.OnLoadingScreen(loadingRound, game.WaitingForPlayers, game._round);
        }
    }


    [HarmonyPatch(typeof(ClientGameManager), nameof(ClientGameManager.DoCharacterObjectSpawnPreparations))]
    internal static class RoundBeanSpawn
    {
        [HarmonyPostfix]
        public static void Postfix(MPGNetObject pNetObject, string accountId, string platformId, uint playerId,
            string playerName, string playerGeneratedName, int teamId, uint squadId, string partyId,
            CustomisationSelections customisationSelections, bool isLocalPlayer, bool isNPC)
        {
            BetterFG.Features.Replay.FeatureReplay.OnPlayerSpawned(playerId, accountId, platformId, playerName,
                playerGeneratedName, teamId, squadId, partyId, customisationSelections, isLocalPlayer, isNPC);

            BetterFG.Tweaks.CreativeIntroCameraTweak.OnPlayerSpawned(playerId);

            var bean = pNetObject != null ? pNetObject.gameObject : null;
            if (bean == null) return;

            if (isLocalPlayer)
            {
                Plugin.Log.LogInfo($"RoundBeanSpawn: local bean: {bean.name}");
                BeanMonitorService.LocalPlayerBean = bean;
            }
            BeanMonitorService.PushBean(bean, isLocalPlayer);
        }
    }


    [HarmonyPatch(typeof(FallguyCustomisationHandler), nameof(FallguyCustomisationHandler.UpdateFaceplateColours))]
    internal static class FaceplateColoursChangedHub
    {
        [HarmonyPostfix]
        public static void Postfix(FallguyCustomisationHandler __instance, FaceplateOption faceplateOption)
        {
            if (__instance == null) return;
            BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.OnFaceplateChanged(__instance.gameObject, faceplateOption);
        }
    }

    [HarmonyPatch(typeof(CellBehaviour), nameof(CellBehaviour.AddFallGuy))]
    internal static class QualifyScreenOnBeanSpawn
    {
        [HarmonyPostfix]
        public static void Postfix(CellBehaviour __instance, Transform t)
        {
            if (t == null) return;
            if (__instance._localPlayer) BeanMonitorService.LocalPlayerBean = t.gameObject;
            BeanMonitorService.PushBean(t.gameObject, __instance._localPlayer);
        }
    }

    [HarmonyPatch(typeof(CellBehaviour), nameof(CellBehaviour.ChangeFallGuyMaterialForBacklight))]
    internal static class QualCellBacklightEyeRepair
    {
        [HarmonyPostfix]
        public static void Postfix(CellBehaviour __instance)
        {
            if (__instance == null) return;
            var fg = __instance._fallGuy;
            if (fg == null) return;
            var bean = fg.gameObject;
            bool local = __instance._localPlayer;
            BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.ReassertOn(bean);
            BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.Apply(bean, local);
            BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.ApplyLater(bean, 0.5f, local);
            BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.ApplyLater(bean, 1.5f, local);
        }
    }

    [HarmonyPatch(typeof(CellBehaviour), nameof(CellBehaviour.SetColors_WithGuy))]
    internal static class QualCellGuyColourEyeRepair
    {
        [HarmonyPostfix]
        public static void Postfix(CellBehaviour __instance)
            => BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.ReassertOn(__instance);
    }

    [HarmonyPatch(typeof(FallguyCustomisationHandler), nameof(FallguyCustomisationHandler.SetupForVictoryScreen))]
    internal static class VictorySetupEyeRepair
    {
        [HarmonyPostfix]
        public static void Postfix(FallguyCustomisationHandler __instance)
        {
            if (__instance == null) return;
            var bean = __instance.gameObject;
            BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.Apply(bean);
            BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.ApplyLater(bean, 0.5f);
            BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.ApplyLater(bean, 1.5f);
        }
    }

    [HarmonyPatch(typeof(CellBehaviour), nameof(CellBehaviour.SetName), new[] { typeof(string) })]
    internal static class QualCellSetNamePatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref string __0)
        {
            if (SettingsService.Get("nametag.enabled", "false") != "true") return;

            string custom = LocalPlayerInfo.CustomName;
            if (string.IsNullOrEmpty(custom)) return;

            string real = LocalPlayerInfo.FGlocalplayerusername;
            if (string.IsNullOrEmpty(real)) return;

            if (__0 != null && __0.Equals(real, StringComparison.Ordinal))
                __0 = custom;
        }
    }

    [HarmonyPatch(typeof(StateQualificationScreen), "DisplayFallguy")]
    internal static class QualificationScreenPlatformPatch
    {
        [HarmonyPostfix]
        public static void Postfix(GameObject __0, CellBehaviour __1, PlayerMetadata __2)
        {
            string cellKey = __2 != null ? __2.PlayerKey : "";
            if (__1 != null && BeanMonitorService.Instance != null)
                BeanMonitorService.Instance.StartCoroutine(FixCellNameNextFrame(__1, cellKey).WrapToIl2Cpp());

            if (__1 != null && __1._localPlayer)
                BetterFG.Tweaks.DynamicQualScreenTweak.OnLocalPlayerDisplayed(__1);

            try
            {
                if (__1 == null || __2 == null) return;

                var sprite = FeatureMorePlatformIcon.SpriteForPlayerKey(__2.PlayerKey);
                if (sprite == null) return;

                var renderer = __1._platformIcon;
                if (renderer == null) return;

                renderer.sprite = sprite;
                renderer.gameObject.SetActive(true);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("QualScreen: platform " + ex.Message);
            }
        }

        private static IEnumerator FixCellNameNextFrame(CellBehaviour cell, string cellKey)
        {
            yield return null;
            if (cell == null) yield break;
            foreach (var t in cell.GetComponentsInChildren<TMPro.TMP_Text>(true))
                if (t != null) BetterFG.Customization.UI.FontReplacementService.ApplyToNametag(t);

            if (BetterFG.Nametag.CrownRankService.Enabled && BetterFG.Nametag.CrownRankService.IsLocalPlayerKey(cellKey))
                BetterFG.Nametag.CrownRankService.ApplyCrownTo(cell, BetterFG.Nametag.CrownRankService.CfgFromSettings());
        }
    }

    [HarmonyPatch(typeof(StateQualificationScreen), nameof(StateQualificationScreen.ShowNames))]
    internal static class QualScreenShowNamesHook
    {
        [HarmonyPostfix]
        public static void Postfix(bool show)
        {
            BetterFG.Tweaks.KeepNametagsTweak.OnQualScreenNamesToggled(show);
        }
    }

    [HarmonyPatch(typeof(StateQualificationScreen), nameof(StateQualificationScreen.Teardown))]
    internal static class QualScreenTeardownLeaveHook
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            BetterFG.Tweaks.KeepNametagsTweak.OnQualScreenTeardownStarting();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            BetterFG.Tweaks.LeaveOnLoadingScreenTweak.OnExternalLeaveTrigger();
            BetterFG.Tweaks.KeepNametagsTweak.OnQualScreenTeardown();
        }
    }

    [HarmonyPatch]
    internal static class Patch_BannerScreens_OnOpened
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            Type[] types =
            {
                typeof(FGClient.UI.QualifiedScreenViewModel),
                typeof(FGClient.EliminatedScreenViewModel),
                typeof(FGClient.EliminatedSquadEarlyScreenViewModel),
                typeof(FGClient.EliminatedSquadScreenViewModel),
                typeof(FGClient.UI.WinnerScreenViewModel),
                typeof(FGClient.RoundEndedScreenViewModel),
            };
            foreach (var t in types)
            {
                var m = AccessTools.DeclaredMethod(t, "OnOpened");
                if (m != null) yield return m;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Component __instance)
        {
            BetterFG.Features.TimePlacement.TimePlacementBannerReparent.Apply(__instance);
            BetterFG.Features.QualificationTime.FeatureQualificationTime.OnBannerOpened(__instance);

            var app = MenuCustomizationApplication.Instance;
            bool eliminatedBanner = false;
            if (__instance.TryCast<FGClient.UI.QualifiedScreenViewModel>() != null)
                app?.ApplyBannerColours(__instance, MenuCustomizationApplication.BannerScreen.Qualified);
            else if (__instance.TryCast<FGClient.EliminatedSquadScreenViewModel>() != null)
            { app?.ApplyBannerColours(__instance, MenuCustomizationApplication.BannerScreen.Squad); eliminatedBanner = true; }
            else if (__instance.TryCast<FGClient.EliminatedScreenViewModel>() != null
                  || __instance.TryCast<FGClient.EliminatedSquadEarlyScreenViewModel>() != null)
            { app?.ApplyBannerColours(__instance, MenuCustomizationApplication.BannerScreen.Eliminated); eliminatedBanner = true; }
            else if (__instance.TryCast<FGClient.UI.WinnerScreenViewModel>() != null)
                app?.ApplyBannerColours(__instance, MenuCustomizationApplication.BannerScreen.Winner);
            else
                app?.ApplyBannerColours(__instance, MenuCustomizationApplication.BannerScreen.RoundOver);

            if (eliminatedBanner)
            {
                var vm = __instance.TryCast<FGClient.UI.Core.ScreenViewModel>();
                BetterFG.Tweaks.LeaveOnLoadingScreenTweak.OnExternalLeaveTrigger();
                BetterFG.Tweaks.LeaveOnLoadingScreenTweak.ActiveEliminatedBannerClose =
                    () => { if (vm != null) vm.OnClosed(); };
            }

            BetterFG.Tweaks.BfgTweak.RaiseBannerShown();
        }
    }

    [HarmonyPatch]
    internal static class Patch_EliminatedBanners_OnClosed
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            Type[] types =
            {
                typeof(FGClient.EliminatedScreenViewModel),
                typeof(FGClient.EliminatedSquadEarlyScreenViewModel),
                typeof(FGClient.EliminatedSquadScreenViewModel),
            };
            foreach (var t in types)
            {
                var m = AccessTools.DeclaredMethod(t, "OnClosed");
                if (m != null) yield return m;
            }
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            BetterFG.Tweaks.LeaveOnLoadingScreenTweak.OnExternalLeaveTriggerEnd();
            BetterFG.Tweaks.LeaveOnLoadingScreenTweak.ActiveEliminatedBannerClose = null;
        }
    }


    [HarmonyPatch(typeof(StateRewardScreen), nameof(StateRewardScreen.OnSceneLoaded))]
    internal static class RewardScreen
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            BetterFG.Tweaks.LeaveOnLoadingScreenTweak.OnRewardScreenEntered();
            if (BeanMonitorService.Instance == null) return;
            BeanMonitorService.Instance.StartCoroutine(PollRewardScreen().WrapToIl2Cpp());

        }

        private static IEnumerator PollRewardScreen()
        {
            float elapsed = 0f;
            bool first = true;
            while (elapsed < 10f)
            {
                var root = GameObject.Find("3D Assets");
                if (root != null)
                {
                    var handler = root.GetComponentInChildren<RewardScreen3DReferencesHandler>();
                    var bean = handler?.FallGuyGameObject;
                    if (bean != null)
                    {
                        Plugin.Log.LogInfo($"RewardScreen: bean: {bean.name}");
                        BeanMonitorService.PushBean(bean);
                        SkinApplicationService.Instance?.ReapplyExpectedGameCosmeticVisuals(bean);
                        BeanMonitorService.Instance.StartCoroutine(BeanMonitorService.PollAndPushRewardPlinth().WrapToIl2Cpp());
                        yield break;
                    }
                }
                if (first) { yield return null; first = false; }
            }
            Plugin.Log.LogWarning("RewardScreen: timed out waiting for bean");
        }
    }

    [HarmonyPatch(typeof(FallguyCustomisationHandler), nameof(FallguyCustomisationHandler.CheckFallGuyAndCostumeVisibility))]
    public class FallGuyVisibilityRecheck
    {
        [HarmonyPostfix]
        public static void Postfix(FallguyCustomisationHandler __instance)
        {
            var bean = __instance.gameObject;
            BetterFG.Customization.Player.CostumePollerComponent.RehideForBean(bean.GetInstanceID());

            var svc = SkinApplicationService.Instance;
            if (svc == null || svc.IsBindingGameCosmetic(bean)) return;
            svc.TryAutoReapplyCustomTextureForBean(bean);
        }
    }

    internal static class GameStateDispatcher
    {
        public static void OnStateChanged(GameStateMachine.IGameState newState)
        {
            try
            {
                BetterFG.Features.UnityRound.Editor.UnityRoundLoader.OnReplaceCurrentState(newState);
                BetterFG.Tweaks.BfgTweak.RaiseStateChanged(newState);
                BetterFG.Services.MenuMusicService.OnReplaceCurrentState(newState);
                BetterFG.Services.DiscordPresenceService.OnStateChanged(newState);

                if (newState == null) return;

                if (newState.TryCast<StateQualificationScreen>() != null)
                    BetterFG.Tweaks.KeepNametagsTweak.OnQualScreenSwitch();

                var victory = newState.TryCast<StateVictoryScreen>();
                if (victory == null) return;

                BetterFG.Tweaks.SkipVictoryTweak.ActiveState = victory;

                Plugin.Log.LogInfo($"VictoryScreen: ReplaceCurrentState -> StateVictoryScreen, starting handler");
                if (BeanMonitorService.Instance == null)
                {
                    Plugin.Log.LogWarning("VictoryScreen: BeanMonitorService.Instance is null, can't start coroutine");
                    return;
                }
                BeanMonitorService.Instance.StartCoroutine(HandleVictory(victory).WrapToIl2Cpp());
            }
            catch (Exception ex) { Plugin.Log.LogError($"VictoryScreen: postfix: {ex}"); }
        }

        private static IEnumerator HandleVictory(StateVictoryScreen victory)
        {
            Il2CppSystem.Collections.Generic.List<WinnerInfo> infos = null;
            float elapsed = 0f;
            while (elapsed < 5f)
            {
                try
                {
                    var winnersInfos = victory._victoryScreenViewModel?.WinnersInfos;
                    if (winnersInfos != null && winnersInfos.IsPopulated)
                    {
                        infos = winnersInfos.WinnersInfo;
                        if (infos != null && infos.Count > 0) break;
                    }
                }
                catch (Exception ex) { Plugin.Log.LogError($"VictoryScreen: poll infos: {ex}"); yield break; }

                yield return new WaitForSeconds(0.1f);
                elapsed += 0.1f;
            }

            if (infos == null || infos.Count == 0)
            {
                Plugin.Log.LogWarning($"VictoryScreen: winner infos never populated after {elapsed:0.0}s");
                yield break;
            }

            var winners = new List<(WinnerInfo info, string name, string key, bool local)>();
            try
            {
                string localkey = GlobalGameStateClient.Instance?.GetLocalPlayerKey();
                string localName = string.IsNullOrEmpty(localkey) ? null : PlayerUtils.CleanPlayerName(localkey);
                Plugin.Log.LogInfo($"VictoryScreen: localkey={localkey}, winnerCount={infos.Count}");

                for (int i = 0; i < infos.Count; i++)
                {
                    var info = infos[i];
                    string winnerKey = info?.PlayerMetadata?.PlayerKey;
                    if (string.IsNullOrEmpty(winnerKey)) continue;

                    string name = PlayerUtils.CleanPlayerName(winnerKey);
                    bool mine = localName != null && name.Equals(localName, StringComparison.OrdinalIgnoreCase);
                    if (mine && !string.IsNullOrEmpty(LocalPlayerInfo.FGlocalplayerusername))
                        name = LocalPlayerInfo.FGlocalplayerusername;
                    Plugin.Log.LogInfo($"VictoryScreen: winner[{i}] key={winnerKey}{(mine ? " (that's us)" : "")}");
                    winners.Add((info, name, winnerKey, mine));
                }
            }
            catch (Exception ex) { Plugin.Log.LogError($"VictoryScreen: resolve: {ex}"); yield break; }

            if (winners.Count == 0)
            {
                Plugin.Log.LogWarning("VictoryScreen: none of the winners carried a player key, nothing to hang a profile off");
                yield break;
            }

            yield return null;

            try
            {
                foreach (var w in winners)
                {
                    var vm = w.info.NamePlateRef;
                    if (vm == null) { Plugin.Log.LogInfo($"VictoryScreen: {w.name} has no nameplate ref"); continue; }
                    NametagPatchHub.HandleViewModel(vm, w.name);
                    BeanMonitorService.Instance.StartCoroutine(NudgeVictoryIcon(vm).WrapToIl2Cpp());
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"VictoryScreen: nameplate: {ex}"); }

            BeanMonitorService.Instance.StartCoroutine(BeanMonitorService.PollAndPushVictoryPlinth().WrapToIl2Cpp());

            foreach (var w in winners)
            {
                var spawn = w.info._spawnTransform;
                Plugin.Log.LogInfo($"VictoryScreen: {w.name} spawn={(spawn != null ? spawn.name : "null")}");
                if (spawn != null)
                    BeanMonitorService.Instance.StartCoroutine(PollForBean(spawn, w.key, w.local).WrapToIl2Cpp());
            }
        }

        private const float VICTORY_ICON_NUDGE_X = 25f;
        private const float VICTORY_ICON_NUDGE_Y = 0f;

        private static IEnumerator NudgeVictoryIcon(NameTagViewModel vm)
        {
            yield return null;
            yield return null;

            RectTransform iconRt = null;
            try
            {
                var t = FindIconUnder(vm.transform);
                iconRt = t != null ? t.GetComponent<RectTransform>() : null;
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"VictoryScreen: nudge find: {ex.Message}"); yield break; }

            if (iconRt == null) yield break;
            iconRt.anchoredPosition += new Vector2(VICTORY_ICON_NUDGE_X, VICTORY_ICON_NUDGE_Y);
        }

        private static Transform FindIconUnder(Transform root)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].name == "BetterFG_UINametagIcon")
                    return all[i];
            return null;
        }

        private static IEnumerator PollForBean(Transform spawnTransform, string winnerKey, bool isLocalWinner)
        {
            float elapsed = 0f;
            while (elapsed < 10f)
            {
                try
                {
                    var bean = spawnTransform.Find("FallGuy(Clone)");
                    if (bean != null)
                    {
                        if (isLocalWinner)
                        {
                            BeanMonitorService.PushBean(bean.gameObject);
                            yield break;
                        }

                        BeanMonitorService.MarkForeignBean(bean.gameObject);
                        var profile = RemoteProfileStore.TryGet(winnerKey);
                        if (profile != null)
                            BeanMonitorService.Instance.StartCoroutine(CustomizationHandler.Apply(profile, bean.gameObject).WrapToIl2Cpp());
                        else
                            Plugin.Log.LogInfo($"no profile on file for {winnerKey}, their podium bean stays stock");
                        yield break;
                    }
                }
                catch (Exception ex) { Plugin.Log.LogError($"VictoryScreen: poll: {ex.Message}"); yield break; }
                yield return new WaitForSeconds(0.25f);
                elapsed += 0.25f;
            }
            Plugin.Log.LogWarning("VictoryScreen: timed out waiting for bean");
        }
    }

    [HarmonyPatch(typeof(FGClient.UI.UGCLevelLikeViewModel), nameof(FGClient.UI.UGCLevelLikeViewModel.UpdateData))]
    internal static class UGCLevelLikeViewModelForeground
    {
        [HarmonyPostfix]
        public static void Postfix(FGClient.UI.UGCLevelLikeViewModel __instance)
        {
            try
            {
                if (__instance == null || MenuCustomizationApplication.Instance == null) return;
                MenuCustomizationApplication.Instance.StartCoroutine(
                    MenuCustomizationApplication.ReapplyForegroundFromSettingsCoroutine(__instance.transform).WrapToIl2Cpp());
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"VictoryScreen: likes panel reapply: {ex}"); }
        }
    }


    [HarmonyPatch(typeof(RoundLoader), "CleanupLoadingScreens")]
    public class CleanupLoadingScreens
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            BetterFG.Features.QualificationTime.FeatureQualificationTime.OnCleanupLoadingScreens();
            BetterFG.Features.Replay.FeatureReplay.OnCleanupLoadingScreens();
            BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.SetInRound(true);
            BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.Refresh(false, 0.5f);
            BetterFG.Tweaks.CreativeIntroCameraTweak.OnCleanupLoadingScreens();
            BetterFG.Tweaks.PlayerNameWarningTweak.OnCleanupLoadingScreens();
            BetterFG.Nametag.CrownRankFovFix.Forget();
            BetterFG.Nametag.NametagIconApplicator.ForgetIconRows();

            BetterFGUnityRounds.RestoreMusic();

            var menuApp = MenuCustomizationApplication.Instance;
            if (menuApp != null)
                menuApp.StartCoroutine(ReapplyPinkGreyAfterDelay(menuApp).WrapToIl2Cpp());

            PlayerUtils.TryFindMatchPlayer();
            var fgcc = PlayerUtils.PlayerController;
            if (fgcc != null)
            {
                BeanMonitorService.LocalPlayerBean = fgcc.gameObject;
                Plugin.Log.LogInfo($"CLS: player set: {fgcc.gameObject.name}");

                var beanHost = BeanMonitorService.Instance;
                if (beanHost != null)
                    beanHost.StartCoroutine(ReapplyLocalCosmeticsBehindLoadingScreen(fgcc.gameObject).WrapToIl2Cpp());
            }
            try
            {
                var indicator = PlayerUtils.PlayerLandingIndicator();
                if (indicator != null)
                    indicator.transform.localPosition = Vector3.zero;
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"CLS: landing indicator: {ex.Message}"); }

            CameraUtils.DisableXRayRenderer();
            NetworkClient.Instance?.OnRoundStart();
            BetterFG.Nametag.NametagPatchHub.OnRoundStart();

            BetterFG.Tweaks.ShadowCustomResolutionTweak.ApplyIfEnabled();
        }

        private static IEnumerator ReapplyPinkGreyAfterDelay(MenuCustomizationApplication menuApp)
        {
            yield return new WaitForSeconds(1f);
            menuApp.ReapplyBakedPinkGreyTextures();
        }

        private static IEnumerator ReapplyLocalCosmeticsBehindLoadingScreen(GameObject bean)
        {
            yield return new WaitForSeconds(1f);
            if (bean != null) BeanMonitorService.PushBean(bean);

            yield return new WaitForSeconds(1f);
            if (bean != null) BeanMonitorService.PushBean(bean);

            if (bean != null)
                PlayerScaleService.RestorePlayerScaleToBean(bean);
        }

    }


    [HarmonyPatch(typeof(GlobalGameStateClient), "HandleServerStartRound")]
    public partial class HandleServerStartRoundPa
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            BetterFG.Utilities.PatchGate.SetRoundActive(true);
            if (BetterFG.Tweaks.HideZoneArchEffectsTweak.Instance?.IsEnabled == true)
                BetterFG.Tweaks.HideZoneArchEffectsTweak.StripSpeedArchCameraLines();
            BetterFGUnityRounds.StartCustomMusicIfAny();
            BetterFG.Patches.SeasonProgressHoverPatch.SetMenuActive(false);

            MenuCustomizationApplication.Instance.StartCoroutine(MenuCustomizationApplication.ReapplyForegroundFromSettingsCoroutine().WrapToIl2Cpp());

            FeatureTimePlacement.SpawnList();

            PlayerUtils.TryFindMatchPlayer();
            var fgcc = PlayerUtils.PlayerController;
            if (fgcc != null)
                BeanMonitorService.LocalPlayerBean = fgcc.gameObject;

            NametagIconApplicator.ApplyNametag();
            NametagIconApplicator.ApplyPlatformIcon();
            BetterFG.Nametag.CrownRankService.ApplyLocal();

            var platformHost = BeanMonitorService.Instance;
            if (platformHost != null)
                platformHost.StartCoroutine(ApplyKnownNametagPlatformsAfterRoundStart().WrapToIl2Cpp());

            if (platformHost != null)
                platformHost.StartCoroutine(PollNametagsAfterRoundStart().WrapToIl2Cpp());

            try
            {
                var primeHandler = UnityEngine.Object.FindObjectOfType<SocialPrimeHandler>();
                if (primeHandler != null)
                {
                    PhraseInjectionService.ReapplyToWheel(primeHandler);
                    EmoticonInjectionService.RestoreSlots(primeHandler);
                    EmoteInjectionService.RestoreSlots(primeHandler);
                    EmoticonInjectionService.InjectSlots(primeHandler);
                    EmoteInjectionService.InjectSlots(primeHandler);
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"HSR: phrase patch: {ex.Message}"); }

            BetterFG.Tweaks.BfgTweak.RaiseRoundStart();
            BetterFG.Customization.Pets.PetService.OnRoundStart();
            BetterFG.Services.DiscordPresenceService.OnRoundStart();
            BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.Refresh();

            var gameStates = GameObject.Find("UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates");
            var playing = gameStates != null ? gameStates.transform.Find("PlayingState") : null;
            var speccount = playing != null ? playing.Find("UI_SpectatorCount_Prefab") : null;
            if (speccount != null)
            {
                var specRT = speccount.GetComponent<RectTransform>();
                if (specRT != null)
                {
                    specRT.anchorMin = new Vector2(1f, 1f);
                    specRT.anchorMax = new Vector2(1f, 1f);
                    specRT.pivot = new Vector2(1f, 1f);
                    var parentRT = specRT.parent as RectTransform;
                    var parentSize = parentRT != null ? parentRT.rect.size : new Vector2(1920f, 1080f);
                    specRT.anchoredPosition = new Vector2(-parentSize.x * 0.18f, -parentSize.y * 0.18f);
                }
                var container = speccount.Find("Container");
                if (container != null)
                    container.localPosition = Vector3.zero;
            }
            else Plugin.Log.LogInfo("spectator count prefab not up yet, left it where it was");

            var persis = GameObject.Find("UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/PersistentOverlayUI");
            var fallfeed = persis != null ? persis.transform.Find("PB_UI_FallFeed") : null;
            if (fallfeed != null) fallfeed.localPosition = new Vector3(0f, -66.1997f, 0f);

            CompAnticheatRoundStart();
        }

        static partial void CompAnticheatRoundStart();



        private static IEnumerator ApplyKnownNametagPlatformsAfterRoundStart()
        {
            yield return null;

            yield return new WaitForSeconds(0.75f);
            NametagIconApplicator.ApplyKnownPlatformIcons();
            NametagIconApplicator.ApplyRenderQueueToAllNametags();
        }

        private static IEnumerator PollNametagsAfterRoundStart()
        {
            for (int i = 0; i < 2; i++)
            {
                yield return new WaitForSeconds(1f);
                NametagPatchHub.RefreshRemoteNametags();
                NametagIconApplicator.ApplyNametag();
                BetterFG.Nametag.CrownRankService.ApplyLocal();
            }
        }
    }

    [HarmonyPatch(typeof(SwitchableView), nameof(SwitchableView.SetViewImplementation))]
    internal static class SetViewImplementationpatch
    {
        [HarmonyPostfix]
        static void SetViewImplementation(SwitchableView __instance)
        {
            // a switch to any view OTHER than our Customiser backdrop means the user navigated away
            // from the PB tab — drop the overlay.
            if (BetterFG.Features.QualificationTime.PBTabView.IsOpen && __instance != null
                && __instance.CurrentViewIndex != BetterFG.Features.QualificationTime.PBTabView.BackdropIndex)
                BetterFG.Features.QualificationTime.PBTabView.Hide();


            if (BetterFG.Features.UnityRound.Editor.UnityRoundLoader.InLevelEditor) return;

            var app = MenuCustomizationApplication.Instance;
            if (app != null && __instance != null)
                app.StartCoroutine(ApplyIndexZeroAfterFrames(__instance, 1).WrapToIl2Cpp());

            BetterFG.Services.DiscordPresenceService.Push();
        }

        internal static IEnumerator ApplyIndexZeroAfterFrames(SwitchableView view, int frames)
        {
            for (var i = 0; i < frames; i++)
                yield return null;

            if (view != null && view.gameObject.name == "TabsHorizontalLayout") yield break;

            Transform scope = null;
            try
            {
                var views = view?._views;
                int index = view != null ? view.CurrentViewIndex : -1;
                if (views != null && index >= 0 && index < views.Length && views[index] != null)
                    scope = views[index].transform;
                else if (view != null)
                    scope = view.transform;

                for (int depth = 0; depth < 4 && scope != null; depth++)
                {
                    var nested = scope.GetComponent<SwitchableView>();
                    if (nested == null) break;
                    var nestedViews = nested._views;
                    int nestedIndex = nested.CurrentViewIndex;
                    if (nestedViews == null || nestedIndex < 0 || nestedIndex >= nestedViews.Length || nestedViews[nestedIndex] == null) break;
                    var next = nestedViews[nestedIndex].transform;
                    if (next == scope) break;
                    scope = next;
                }
            }
            catch { }

            UIScaleService.ApplySaved();

            var mainView = MenuCustomizationApplication.MainMenuSwitchableView;
            bool isMainMenuView = mainView == null || view == null || view.GetInstanceID() == mainView.GetInstanceID();

            if (isMainMenuView)
            {
                SkinApplicationService.Instance?.ReapplyExpectedGameCosmeticVisuals();

                MenuCustomizationApplication.Instance?.ApplyAmbientFromSettings();
                MenuCustomizationApplication.Instance?.ApplySunFromSettings();
                MenuCustomizationApplication.ReassertMenuBackground3D();

                MenuCustomizationApplication.Instance?.RefreshImageBgVisibility();
            }

            MenuCustomizationApplication.Instance?.ApplyPatternFromSettings();
            MenuCustomizationApplication.Instance?.ApplyGradientFromSettings();

            if (scope != null) NametagFinder.ReapplyNameplatesInScope(scope);
            else NametagFinder.ReapplyAllNameplates();

            FontReplacementService.RestoreUncovered();
            if (scope != null) FontReplacementService.ApplyToScope(scope);
            else FontReplacementService.HealAndReapply();

            if (view == null || view.CurrentViewIndex != 0) yield break;

            if (isMainMenuView) MenuCustomizationApplication.AutoApplyCamFromSettings();

            var app = MenuCustomizationApplication.Instance;
            if (app == null) yield break;

            if (scope != null)
                app.ReapplyForegroundFromSettings(scope, "Prime_UI_SymphonyShowSelector_Prefab_Canvas(Clone)");
        }
    }

    // the top bar's Rewired nav switches views positionally. our PB clone sits at the end of
    // _menuTabs, so navigating to it lands on view 6 (Settings). catch that here and show the PB tab
    // over the (now hidden) Settings screen instead. covers both the immediate and the animated path.
    [HarmonyPatch(typeof(SwitchableViewRewiredNavigation), nameof(SwitchableViewRewiredNavigation.SetView))]
    internal static class Patch_RewiredNav_SetView
    {
        [HarmonyPostfix]
        static void Postfix(SwitchableViewRewiredNavigation __instance, int viewIndex)
        {
            BetterFG.Features.QualificationTime.PBTabView.OnNavIndex(__instance.SwitchableView, viewIndex);
        }
    }

    [HarmonyPatch(typeof(SwitchableViewRewiredNavigation), nameof(SwitchableViewRewiredNavigation.ChangeTab))]
    internal static class Patch_RewiredNav_ChangeTab
    {
        [HarmonyPostfix]
        static void Postfix(SwitchableViewRewiredNavigation __instance, int nextViewIndex)
            => BetterFG.Features.QualificationTime.PBTabView.OnNavIndex(__instance.SwitchableView, nextViewIndex);
    }

    [HarmonyPatch(typeof(ClientGameManager), nameof(ClientGameManager.SwitchToSpectatorMode))]
    internal static class Patch_SwitchToSpectatorMode
    {
        [HarmonyPrefix]
        private static void Prefix(ClientGameManager __instance)
        {
            BetterFG.Tweaks.SpectatorMusicTweak.ApplyIfWanted(__instance);
            BetterFG.Tweaks.SpectatorMusicTweak.ForceSpectatorMix(true);
        }

        [HarmonyPostfix]
        private static void Postfix(ClientGameManager __instance)
        {
            BetterFG.Tweaks.SpectatorMusicTweak.ApplyIfWanted(__instance);
            BetterFG.Tweaks.SpectatorMusicTweak.ForceSpectatorMix(true);
            BetterFG.Tweaks.BfgTweak.RaiseSpectatorMode();
            FeatureTimePlacement.OnSpectatorMode();
            BetterFG.Services.DiscordPresenceService.Push();
        }
    }

    [HarmonyPatch(typeof(LobbyScreenViewModel), nameof(LobbyScreenViewModel.ShowCancelConfirmationPopup))]
    internal static class Patch_LobbyScreenViewModel_ShowCancelConfirmationPopup
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            if (BetterFG.Tweaks.LobbyAudioPromptTweak.Instance != null && BetterFG.Tweaks.LobbyAudioPromptTweak.Instance.IsOpen)
                return false;
            if (BetterFG.Tweaks.LobbyCustomiserTweak.Instance != null && BetterFG.Tweaks.LobbyCustomiserTweak.Instance.IsBrowsing)
                return false;
            return true;
        }
    }

}

