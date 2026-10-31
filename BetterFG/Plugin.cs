using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BetterFG.Core;
using BetterFG.Tweaks;
using BetterFG.Network;
using BetterFG.Patches;
using BetterFG.Services;
using BetterFG.Customization.Player;
using BetterFG.Customization.TexturePacks;
using BetterFG.Customization.Social;
using BetterFG.UI;
using BetterFG.UI.Components;
using BetterFG.UI.SideWheel;
using BetterFG.UI.Tabs;
using BetterFG.UI.Windows;
using BetterFG.Customization.UI;
using BetterFG.Features.Assets;
using BetterFG.Features.UnityRound;
using BetterFG.Features.UnityRound.Behaviours;
using BetterFG.Features.QualificationTime;
using FGClient;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using System;
using System.IO;
using System.Reflection;
using static LevelEditor.LevelEditorWallResizer;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BettrFG.uGUI;
using FallGuysLib.Managers;

namespace BetterFG
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public partial class Plugin : BasePlugin
    {
        internal static new ManualLogSource Log;
        internal static Harmony HarmonyInstance;

        public override void Load()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveNAudio;

            Log = base.Log;
            Log.LogInfo($"{BettrFGMeta.DisplayName} {BetterFGInfo.Version} [{BetterFGInfo.DisplayBuildHash}] loaded");

            SettingsService.Init();
            BetterFG.Customization.Users.UserService.MigratePresets();
            BugReportService.Init();
            BetterFGConfig.Init();
            AudioService.Init();
            TexturePackRuntimeService.LoadFromSettingsOnStartup();
            LocalizationService.Init();
            BetterFG.Tweaks.StartupTitleScreenTweak.ForceOn =
                SettingsService.Get(BetterFG.UI.BetterFGStartupWindow.KEY_SEEN, "false") != "true"
                || SettingsService.Get(BetterFG.Features.Onboarding.OnboardingController.KEY_SEEN, "false") != "true";
            MenuMusicService.Init();
            DiscordPresenceService.Init();

            WireUGUIShip();
            UGUIShip.SetFontByCode(SettingsService.Get("font.family", "tmp_default"), resolveNow: false);

            try { TMPro.TMP_Settings.instance.m_warningsDisabled = true; } catch { }

            RegisterIl2CppTypes();

            HarmonyInstance = new Harmony(MyPluginInfo.PLUGIN_GUID);

            // Gateway/auth has been causing crashes in some environments. Disable creating
            // the `BetterFG_Gateway` here and initialize the mod directly so core features
            // and UI are available even without remote auth.
            ApplyAllPatches();

            // FallGuysLib owns the shared game-state patch and re-raises it; we subscribe instead of
            // patching GameStateMachine.ReplaceCurrentState ourselves (one patch across all FGLib mods).
            FallGuysLib.Game.GameStateEvents.OnStateChanged += BetterFG.Patches.GameStates.GameStateDispatcher.OnStateChanged;
            FallGuysLib.Game.LevelEditorEvents.OnLevelEditorPlaytest += BetterFG.Tweaks.BfgTweak.RaiseLevelEditorPlaytest;
            FallGuysLib.Game.LevelEditorEvents.OnLevelEditorPlaytestEnd += BetterFG.Tweaks.BfgTweak.RaiseLevelEditorPlaytestEnd;
            FallGuysLib.Game.LevelEditorEvents.OnLevelEditorPlaytest += () => BetterFG.Features.CustomizeFallGuys.FeatureCustomizeFallGuys.Refresh(true);
            FallGuysLib.Game.LevelEditorEvents.OnLevelEditorPlaytest += BetterFG.Features.CreativeExpansion.LevelStats.Checkpoint;
            FallGuysLib.Game.LevelEditorEvents.OnLevelEditorPlaytestEnd += BetterFG.Features.CreativeExpansion.LevelStats.Checkpoint;
            FallGuysLib.Game.LevelEditorEvents.OnLevelEditorPlaytest += BetterFG.Features.CreativeExpansion.YoutubeLevelMusic.OnPlaytest;
            FallGuysLib.Game.LevelEditorEvents.OnLevelEditorPlaytestEnd += BetterFG.Features.CreativeExpansion.YoutubeLevelMusic.OnPlaytestEnd;
            FallGuysLib.Game.LevelEditorEvents.OnLevelLoad += BetterFG.Features.CreativeExpansion.YoutubeLevelMusic.OnEditorLevelLoad;
            FallGuysLib.Round.RoundEvents.OnPlayingStarted += BetterFG.Features.CreativeExpansion.YoutubeLevelMusic.OnPlayingStarted;

            FallGuysLib.Round.RoundEvents.OnCleanupLoadingScreens += BetterFG.Features.UnityRound.BetterFGRoundPostmodifier.RefreezeAnimations;
            FallGuysLib.Round.RoundEvents.OnPlayingStarted += BetterFG.Features.UnityRound.BetterFGRoundPostmodifier.StartAnimations;
            FallGuysLib.Round.RoundEvents.OnPlayingStarted += BetterFG.Services.PartyRoundStatus.Publish;
            FallGuysLib.Round.RoundEvents.OnPlayingStarted += BetterFG.Features.CustomizeFallGuys.BeanLookAt.OnPlayingStarted;
            FallGuysLib.Round.RoundEvents.OnPlayingStarted += BetterFG.Features.CustomGamemodes.CustomGamemodeHost.OnPlayingStarted;
            FallGuysLib.Game.LevelEditorEvents.OnLevelEditorPlaytest += BetterFG.Features.CustomGamemodes.CustomGamemodeHost.OnPlayingStarted;
            FallGuysLib.Game.LevelEditorEvents.OnLevelEditorPlaytestEnd += BetterFG.Features.CustomGamemodes.CustomGamemodeHost.OnPlaytestEnd;
            FallGuysLib.Game.LevelEditorEvents.OnLevelEditorPlaytest += BetterFG.Features.UnityRound.BetterFGRoundPostmodifier.StartAnimations;
            FallGuysLib.Game.LevelEditorEvents.OnLevelEditorPlaytestEnd += BetterFG.Features.UnityRound.BetterFGRoundPostmodifier.RefreezeAnimations;
            FallGuysLib.Game.LevelEditorEvents.OnLevelEditorPlaytest += () => BetterFG.Features.UnityRound.Editor.UnityRoundLoader.SetPlaytesting(true);
            FallGuysLib.Game.LevelEditorEvents.OnLevelEditorPlaytestEnd += () => BetterFG.Features.UnityRound.Editor.UnityRoundLoader.SetPlaytesting(false);

            FallGuysLib.Game.MenuEvents.OnMainMenuEntered += BetterFG.Patches.GameStates.MainMenuBean.OnMainMenuEntered;
            FallGuysLib.Game.LevelEditorEvents.OnPlayStateInitialised += BetterFG.Patches.GameStates.LevelEditorPlayStateHub.Postfix;
            FallGuysLib.Game.LevelEditorEvents.OnLevelLoad += BetterFG.Features.CreativeExpansion.CreativeSweep.OnEditorLevelLoad;
            FallGuysLib.Game.LevelEditorEvents.OnLevelEditorEnter += BetterFG.Features.CreativeExpansion.FeatureCreativeContent.Ask;
            FallGuysLib.Round.RoundEvents.OnLoadingFinished += _ => BetterFG.Patches.GameStates.LoadingFinishedHub.Postfix();
            FallGuysLib.Round.RoundEvents.OnCleanupLoadingScreens += BetterFG.Patches.GameStates.CleanupLoadingScreensHub.Postfix;
            FallGuysLib.Round.RoundEvents.OnRoundStart += BetterFG.Patches.GameStates.HandleServerStartRoundPa.Postfix;
            FallGuysLib.Round.RoundEvents.OnGameManagerShutdown += BetterFG.Patches.BettrFGRounds.RoundTeardownHub.Prefix;
            FallGuysLib.Round.RoundEvents.OnGameManagerShutdownComplete += BetterFG.Patches.BettrFGRounds.RoundTeardownHub.Postfix;
            FallGuysLib.Players.PlayerEvents.OnPlayerProgressReceived += (_, msg) => BetterFG.Patches.GameStates.HandleServerPlayerProgressHub.Prefix(msg);
            FallGuysLib.Players.PlayerEvents.OnPlayerProgress += BetterFG.Patches.GameStates.HandleServerPlayerProgressHub.Postfix;
            BetterFG.Customization.Round.BackgroundWire.Changed += Ss2RoundLookTweak.OnBackground;
            BetterFG.Customization.Round.BackgroundWire.Changed += VisualEnhancerTweak.OnBackground;

            // decode saved custom skin textures now, at load, so the first auto-reapply on menu enter
            // hits the cache instead of reading + decoding the whole png on that frame.
            BetterFG.Customization.Player.SkinApplicationService.PrewarmCustomTexCache();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private static bool _uiBuilt;

        internal static void BuildModUI()
        {
            if (_uiBuilt) return;
            _uiBuilt = true;

            InitCompBuild();
            InitGameObjects(0);
            SidewheelRegistry.RegisterAll(SideWheelManager.Create());
            BetterFGUpdateWindow.Show();
            BetterFG.Features.Onboarding.PrivacyNotice.ShowThen(ContinueBoot);
        }

        private static void ContinueBoot()
        {
            if (BetterFGStartupWindow.Show() == null)
                BetterFG.Features.Onboarding.OnboardingController.StartupDone = true;
        }

        private static void WireUGUIShip()
        {
            UGUIShip.Log = Log;
            UGUIShip.ResourceAssembly = Assembly.GetExecutingAssembly();
            UGUIShip.LoadTexture = BetterFG.Utilities.EmbeddedResourceandUnity.LoadTexture;
            UGUIShip.LoadSprite = res => BetterFG.Utilities.EmbeddedResourceandUnity.LoadSprite(res);
            UGUIShip.LoadSlicedSprite = BetterFG.Utilities.EmbeddedResourceandUnity.LoadSprite;
            UGUIShip.PlayClick = AudioService.PlayButtonClick;
            UGUIShip.PlayHover = AudioService.PlayButtonHoverOn;
            UGUIShip.RegisterCanvas = UIScaleService.Register;
            UGUIShip.ProtectFont = FontReplacementService.Protect;
            UGUIShip.RegisterShine = TabHoverStyle.RegisterShine;
            UGUIShip.RegisterFill = TabHoverStyle.RegisterFill;
            UGUIShip.DropdownPops = Features.Onboarding.OnboardingPopup.AddCornerPopsAround;
            UGUIShip.LeatherMaterial = () => Core.AssetManager.GetMaterial("bettrfg_ui_leather");
            UGUIShip.Tint = () => TabHoverStyle.Tint;
            UGUIShip.AddTooltip = (go, tip) =>
            {
                var trig = TooltipTrigger.Attach(go);
                trig.text = tip;
                trig.instant = true;
            };
            UGUIShip.LocalizeGet = LocalizationService.Get;
            UGUIShip.BindLocalized = LocalizationService.Bind;
        }

        static partial void InitCompBuild();

        internal static void ApplyAllPatches()
        {
            var harmony = HarmonyInstance;
            BetterFG.Utilities.PatchGate.ResetForRepatch();
            foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (BetterFG.Utilities.PatchGate.Claim(type)) continue;
                try { new HarmonyLib.PatchClassProcessor(harmony, type).Patch(); }
                catch (Exception ex) { Log.LogError($"Harmony: Failed to patch {type.FullName}: {ex.Message}"); }
            }
            BetterFG.Utilities.PatchGate.ApplyInitial();
        }

        private static Assembly ResolveNAudio(object _, ResolveEventArgs args)
        {
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string name = new AssemblyName(args.Name).Name + ".dll";

            string path = Path.Combine(dir, "Libs", name);
            if (File.Exists(path)) return Assembly.LoadFrom(path);

            // installs from before the vendored dlls moved into Libs\ still have them loose next to us
            path = Path.Combine(dir, name);
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }

        private static void RegisterIl2CppTypes()
        {
            // services
            ClassInjector.RegisterTypeInIl2Cpp<SkinCatalogService>();
            ClassInjector.RegisterTypeInIl2Cpp<SkinLoaderService>();
            ClassInjector.RegisterTypeInIl2Cpp<SkinApplicationService>();
            ClassInjector.RegisterTypeInIl2Cpp<BeanMonitorService>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.CreativeExpansion.SlimeColourDriver>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.CreativeExpansion.ThrusterBeamDriver>();
            ClassInjector.RegisterTypeInIl2Cpp<PlayerScaleService>();
            ClassInjector.RegisterTypeInIl2Cpp<BeanVisualRig>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Customization.UI.Styles.StyleFollower>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Customization.UI.Styles.Season1.S1PrivateLobbyDriver>();
            ClassInjector.RegisterTypeInIl2Cpp<CostumePollerComponent>();
            ClassInjector.RegisterTypeInIl2Cpp<BaseBodyGuard>();
            ClassInjector.RegisterTypeInIl2Cpp<BoneSyncComponent>();
            ClassInjector.RegisterTypeInIl2Cpp<InvisibilitySyncComponent>();
            ClassInjector.RegisterTypeInIl2Cpp<MenuCustomizationApplication>();
            ClassInjector.RegisterTypeInIl2Cpp<RepoRegistry>();
            ClassInjector.RegisterTypeInIl2Cpp<AssetManager>();
            ClassInjector.RegisterTypeInIl2Cpp<NetworkClient>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFGUnityRounds>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.UnityRound.UnityRoundCoreBillboard>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.UnityRound.Editor.CreativeRoundMemory>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.CustomizeFallGuys.FallGuyEyeDriver>();

            ClassInjector.RegisterTypeInIl2Cpp<CustomEndzoneTrigger>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.CreativeExpansion.BfgGameModeRow>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.CreativeExpansion.BfgDisableBackgroundRow>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.CreativeExpansion.BfgYoutubeRow>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.CreativeExpansion.ParamSliderRow>();

            // ui
            ClassInjector.RegisterTypeInIl2Cpp<BetterFGUIMan>();
            ClassInjector.RegisterTypeInIl2Cpp<ControllerManager>();
            ClassInjector.RegisterTypeInIl2Cpp<TabHoverTint>();
            ClassInjector.RegisterTypeInIl2Cpp<Tooltip>();
            ClassInjector.RegisterTypeInIl2Cpp<GradientImage>();
            ClassInjector.RegisterTypeInIl2Cpp<MovePulseContinuous>();
            ClassInjector.RegisterTypeInIl2Cpp<AlphaPulseContinuousFade>();
            ClassInjector.RegisterTypeInIl2Cpp<HollowCirclePulse>();
            ClassInjector.RegisterTypeInIl2Cpp<HollowBoxPulse>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.Onboarding.PromptHost>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.Assets.UnityProjectPrompt>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.Assets.BundleReaderPrompt>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Tweaks.Ss2RoundLookPrompt>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.Onboarding.OnboardingController>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.Onboarding.HelpPrompt>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.Onboarding.FullscreenPrompt>();
            ClassInjector.RegisterTypeInIl2Cpp<MoveScrollUvRaw>();
            ClassInjector.RegisterTypeInIl2Cpp<DragHandler>();
            ClassInjector.RegisterTypeInIl2Cpp<SmoothScroll>();
            ClassInjector.RegisterTypeInIl2Cpp<LoadingBar>();
            ClassInjector.RegisterTypeInIl2Cpp<SideWheelManager>();
            ClassInjector.RegisterTypeInIl2Cpp<RingHoleGraphic>();
            ClassInjector.RegisterTypeInIl2Cpp<AutoFetchTrigger>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Patches.MenuPatternAlphaMirror>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Patches.ShowSelectorBgApplier>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Patches.CreativeEditorBgApplier>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Nametag.GifAnimator>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.Replay.ReplayViewer>();
#if MACHINIMA
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.Replay.MachinimaLevelPrompt>();
#endif
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Customization.Player.BeanCreatorScreen>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Customization.UI.MenuLayoutEditor>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Customization.UI.LayoutSync>();

            // tabs
            ClassInjector.RegisterTypeInIl2Cpp<SwitchTab>();
            ClassInjector.RegisterTypeInIl2Cpp<UISubTab>();
            ClassInjector.RegisterTypeInIl2Cpp<UGCTab>();
            ClassInjector.RegisterTypeInIl2Cpp<CustomizationTab>();
            ClassInjector.RegisterTypeInIl2Cpp<ItemConfigTab>();
            ClassInjector.RegisterTypeInIl2Cpp<MenuTab>();
            ClassInjector.RegisterTypeInIl2Cpp<RoundsTab>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Customization.Round.BackgroundSpin>();
            ClassInjector.RegisterTypeInIl2Cpp<MenuBackgroundImageWizardTab>();
            ClassInjector.RegisterTypeInIl2Cpp<NametagTab>();
            ClassInjector.RegisterTypeInIl2Cpp<NametagColourTab>();
            ClassInjector.RegisterTypeInIl2Cpp<NametagIconTab>();
            ClassInjector.RegisterTypeInIl2Cpp<NametagNameplateTab>();
            ClassInjector.RegisterTypeInIl2Cpp<NametagCrownTab>();
            ClassInjector.RegisterTypeInIl2Cpp<UITab>();
            ClassInjector.RegisterTypeInIl2Cpp<UIForegroundDetailTab>();
            ClassInjector.RegisterTypeInIl2Cpp<UIFontTab>();
            ClassInjector.RegisterTypeInIl2Cpp<UIFontWizardTab>();
            ClassInjector.RegisterTypeInIl2Cpp<UIBackgroundTab>();
            ClassInjector.RegisterTypeInIl2Cpp<UIPatternPickerTab>();
            ClassInjector.RegisterTypeInIl2Cpp<UIScalingTab>();
            ClassInjector.RegisterTypeInIl2Cpp<LayoutTab>();
            ClassInjector.RegisterTypeInIl2Cpp<EmoticonsPhrasesTab>();
            ClassInjector.RegisterTypeInIl2Cpp<FeaturesTab>();
            ClassInjector.RegisterTypeInIl2Cpp<CustomSkinTextureTab>();
            ClassInjector.RegisterTypeInIl2Cpp<SkinTextureWizardTab>();
            ClassInjector.RegisterTypeInIl2Cpp<SkinTextureMaterialPropsTab>();
            ClassInjector.RegisterTypeInIl2Cpp<AssetsTab>();
            ClassInjector.RegisterTypeInIl2Cpp<TexturePacksTab>();
            ClassInjector.RegisterTypeInIl2Cpp<TexturePackScreen>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Customization.SkinCreator.SkinCreatorScreen>();
            ClassInjector.RegisterTypeInIl2Cpp<AssetsScreen>();
            ClassInjector.RegisterTypeInIl2Cpp<AllCosmeticsTab>();
            ClassInjector.RegisterTypeInIl2Cpp<AllCosmeticsWizardTab>();
            ClassInjector.RegisterTypeInIl2Cpp<CreativeTab>();
            ClassInjector.RegisterTypeInIl2Cpp<CustomCreativeTextureTab>();
            ClassInjector.RegisterTypeInIl2Cpp<PersonalBestTab>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.QualificationTime.PBScreenDriver>();
            ClassInjector.RegisterTypeInIl2Cpp<ReplayTab>();
            ClassInjector.RegisterTypeInIl2Cpp<ReplaysTab>();
            ClassInjector.RegisterTypeInIl2Cpp<ReplayImagesTab>();
            ClassInjector.RegisterTypeInIl2Cpp<PlinthsTab>();
            ClassInjector.RegisterTypeInIl2Cpp<PlinthsInGameTab>();
            ClassInjector.RegisterTypeInIl2Cpp<PlinthsUgcTab>();
            ClassInjector.RegisterTypeInIl2Cpp<RepoSelectorTab>();
            ClassInjector.RegisterTypeInIl2Cpp<PetsTab>();
            ClassInjector.RegisterTypeInIl2Cpp<PetSkinTextureTab>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Customization.Player.BeanCreatorTab>();
            ClassInjector.RegisterTypeInIl2Cpp<PetPhrasesTab>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Customization.Pets.PetService>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Customization.Pets.RemotePetService>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Customization.Pets.PetFollowComponent>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Customization.Pets.PetSpeechComponent>();

            // windows
            ClassInjector.RegisterTypeInIl2Cpp<BetterFGWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<SideWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<PartialWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFGStartupWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFGInfoWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFGUpdateWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<AudioSettingsWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<MenuMusicWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<PlayerdetailsWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<PlayerScaleWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<LobbyAutokickConfigWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<PlayerNameWarningConfigWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<UnityRoundLoaderWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<ObstacleTextureWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.UI.Windows.Creative.CreativeSelectionWatcher>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.CreativeExpansion.LevelBrowserPortPrompt>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.CreativeExpansion.CopyCodePrompt>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.OptionExpand.OptionExpander>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.CreativeExpansion.IntroCamPreview>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.CreativeExpansion.SavedGroupsBrowser>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.CreativeExpansion.PasteCodePrompt>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Customization.Social.EmoticonLockerPrompt>();
            ClassInjector.RegisterTypeInIl2Cpp<WindowDragHandle>();
            ClassInjector.RegisterTypeInIl2Cpp<TweaksWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Customization.Round.BackgroundLibrary>();
            ClassInjector.RegisterTypeInIl2Cpp<OptionsWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<UsersWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<CreditsWindow>();
#if PROFILES
            ClassInjector.RegisterTypeInIl2Cpp<ProfilesWindow>();
#endif
            ClassInjector.RegisterTypeInIl2Cpp<KeybindRecorder>();



            // tweaks
            ClassInjector.RegisterTypeInIl2Cpp<BfgTweak>(); //ts first
            ClassInjector.RegisterTypeInIl2Cpp<ChangeFallGuysLogo>();
            ClassInjector.RegisterTypeInIl2Cpp<ChangeSplashScreenTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<HideCreatorCodeTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<SaveFailureDetailsTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<LobbyAutokickTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<PlayerNameWarningTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<LobbyAudioPromptTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<SpectatorMusicTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<MuteSocialSoundsTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<DisablePlayerEmoticonsTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<DisablePlayerPhrasesTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<BringBackFallGuyNoisesTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<DisableRtcVoiceChatTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<StripSizeTagsTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<FallFeedQualTimeTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<MaxFallFeedTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<SkipNotificationTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<Background3dTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<MatchmakingQueueCountTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<AlwaysShowTimerTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<SkipVictoryTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<SkipRewardsTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<LobbyShowSearchTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<LobbyCustomiserTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<LeaveToLobbyTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<SeenPlayersTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<DisableCameraAssistTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<CustomCameraTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<CameraOptionRow>();
            ClassInjector.RegisterTypeInIl2Cpp<DisableStepAssistTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<CinematicSpectatorTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<CreativeIntroCameraTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<ObjectiveRoundNumberTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<HideZoneArchEffectsTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<PhraseCooldownBypassTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<DisableInstructionControlsTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<CustomCursorTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<DisableAntiAfkTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<StartupTitleScreenTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<ShowServerInfoTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<DisableAgeRatingPopupTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<ShowTilePlaysTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<ShowTileDislikesTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<ShowRoundsTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<LevelBrowserTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<MultiShowSelectTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<JoinLastLobbyTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<PerformanceModeTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<FpsCounterTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<ShadowDistanceTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<ShadowCustomResolutionTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<ShadowCascadeSplitTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<Ss2RoundLookTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<VisualEnhancerTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<LeaveOnLoadingScreenTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<ImmediateRespawnTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<DynamicQualScreenTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<QualScreen60Tweak>();
            ClassInjector.RegisterTypeInIl2Cpp<FasterTransitionsTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<InstantLandingIndicatorTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<KeepNametagsTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<LevelDescriptionOnPauseTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<CreativeTypeValueTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterStickerSelectionTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<NotifyRoundStartTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<ShowMedalOnQualifyTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<SquadCantQualifyTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<UpcomingShowsTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<RandomShowSelectTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<FavouriteCreativeRoundsTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<RetroGoopMaterialsTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<PlaytestTrailTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<EditorLevelMusicTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<EditorFreeCameraTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.AccountStats.WinStreakBurst>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.CustomGamemodes.CustomGamemodeHost>();
        }

        private static void InitGameObjects(ulong seed)
        {
            Spawn<AssetManager>(ModManagers.Common);
            Spawn<PlayerScaleService>(ModManagers.Common);
            Spawn<BetterFG.Features.CustomizeFallGuys.FallGuyEyeDriver>(ModManagers.Common);
            Spawn<BetterFG.Customization.Pets.PetService>(ModManagers.Common);
            Spawn<BetterFG.Customization.Pets.RemotePetService>(ModManagers.Common);

            Spawn<NetworkClient>(ModManagers.Client);
            Spawn<BetterFGUnityRounds>(ModManagers.Client);
            Spawn<BetterFG.Features.UnityRound.Editor.CreativeRoundMemory>(ModManagers.Client);
            Spawn<BetterFG.UI.Windows.Creative.CreativeSelectionWatcher>(ModManagers.Client);
            Spawn<BetterFG.Features.CreativeExpansion.LevelBrowserPortPrompt>(ModManagers.Client);
            Spawn<BetterFG.Features.CreativeExpansion.CopyCodePrompt>(ModManagers.Client);
            Spawn<BetterFG.Features.OptionExpand.OptionExpander>(ModManagers.Client);
            Spawn<BetterFG.Features.CreativeExpansion.IntroCamPreview>(ModManagers.Client);
            Spawn<BetterFG.Features.CreativeExpansion.SavedGroupsBrowser>(ModManagers.Client);
            Spawn<BetterFG.Features.CreativeExpansion.PasteCodePrompt>(ModManagers.Client);
            Spawn<BetterFG.Customization.Social.EmoticonLockerPrompt>(ModManagers.Client);
            Spawn<BetterFG.Features.Onboarding.OnboardingController>(ModManagers.Client);

            Spawn<BeanMonitorService>(ModManagers.Scene);
            Spawn<BetterFG.Features.CreativeExpansion.SlimeColourDriver>(ModManagers.Scene);

            Spawn<BetterFG.Features.CustomGamemodes.CustomGamemodeHost>(ModManagers.Client);

            TweakRegistry.Init();

            BetterFG.Features.Colourblind.ColourblindOptions.Apply();

            BetterFG.Utilities.IdentifierObjectRegistry.Register(new BetterFG.Features.CreativeExpansion.CustomLightIdentifier());
            BetterFG.Utilities.IdentifierObjectRegistry.Register(new BetterFG.Features.CreativeExpansion.ThemeIdentifier());
            BetterFG.Utilities.IdentifierObjectRegistry.Register(new BetterFG.Features.CreativeExpansion.CustomIntroCamIdentifier());
            BetterFG.Utilities.IdentifierObjectRegistry.Register(new BetterFG.Features.CreativeExpansion.RuleColourIdentifier());
            BetterFG.Utilities.IdentifierObjectRegistry.Register(new BetterFG.Features.CreativeExpansion.SlimeColourIdentifier());
            BetterFG.Utilities.IdentifierObjectRegistry.Register(new BetterFG.Features.CreativeExpansion.ObjectThemeIdentifier());
            BetterFG.Utilities.IdentifierObjectRegistry.Register(new BetterFG.Features.CreativeExpansion.ThrusterIdentifier());
            BetterFG.Utilities.IdentifierObjectRegistry.Register(new BetterFG.Features.CreativeExpansion.FoliageIdentifier());
            BetterFG.Utilities.IdentifierObjectRegistry.Register(new BetterFG.Features.UnityRound.UnityRoundCoreIdentifier());
            BetterFG.Utilities.IdentifierObjectRegistry.Register(new BetterFG.Features.UnityRound.UnityRoundAnchorIdentifier());
            BetterFG.Features.CreativeExpansion.SavedGroups.RegisterShellThumbnails();

            var svcGo = Managers.Host(ModManagers.Common, "Prefab_CustomizationServices");

            var repoRegistry = svcGo.AddComponent<RepoRegistry>();
            var catalogService = svcGo.AddComponent<SkinCatalogService>();
            var applicationService = svcGo.AddComponent<SkinApplicationService>();
            var loaderService = svcGo.AddComponent<SkinLoaderService>();
            loaderService.skinApp = applicationService;
            var plinthApp = svcGo.AddComponent<MenuCustomizationApplication>();

            CustomizationServices.Provide(repoRegistry, catalogService, applicationService, loaderService, plinthApp);

            PhraseInjectionService.SetEntries(PhraseSettingsService.Load());
            EmoticonInjectionService.SetEntries(EmoticonSettingsService.Load());
            EmoteInjectionService.SetEntries(EmoteSettingsService.Load());

            applicationService.StartCoroutine(applicationService.EarlyRestoreCoroutine().WrapToIl2Cpp());

            // fallback: also restore after the first catalog fetch
            catalogService.OnFetchCompleted += () =>
            {
                applicationService.RestoreFromSettings(catalogService.AvailableSkins, loaderService, plinthApp);
            };

            if (BetterFGConfig.AutoFetchOnStartup.Value)
            {
                var triggerGo = new GameObject("BetterFG_AutoFetchTrigger");
                triggerGo.hideFlags = HideFlags.HideAndDontSave;
                var trigger = triggerGo.AddComponent<AutoFetchTrigger>();
                trigger.CatalogService = catalogService;
                trigger.RepoRegistry = repoRegistry;
            }

            BetterFGTabRegistry.Register<CustomizationTab>();
            BetterFGTabRegistry.Register<MenuTab>();
            BetterFGTabRegistry.Register<UITab>();
            BetterFGTabRegistry.Register<UIBackgroundTab>();
            BetterFGTabRegistry.Register<LayoutTab>();
            BetterFGTabRegistry.Register<NametagTab>();
            BetterFGTabRegistry.Register<EmoticonsPhrasesTab>();
            BetterFGTabRegistry.Register<FeaturesTab>();
            BetterFGTabRegistry.Register<CustomSkinTextureTab>();
            BetterFGTabRegistry.Register<AssetsTab>();
            BetterFGTabRegistry.Register<TexturePacksTab>();
            BetterFGTabRegistry.Register<AllCosmeticsTab>();
            BetterFGTabRegistry.Register<CreativeTab>();
            BetterFGTabRegistry.Register<PersonalBestTab>();
            BetterFGTabRegistry.Register<ReplaysTab>();
            BetterFGTabRegistry.Register<PlinthsInGameTab>();
            BetterFGTabRegistry.Register<PetsTab>();

            BetterFGTabRegistry.RegisterPartialTab<PetSkinTextureTab>();
            BetterFGTabRegistry.RegisterPartialTab<ItemConfigTab>();
            BetterFGTabRegistry.RegisterPartialTab<PetPhrasesTab>();
            BetterFGTabRegistry.RegisterPartialTab<UIFontTab>();
            BetterFGTabRegistry.RegisterPartialTab<UIScalingTab>();
            BetterFGTabRegistry.RegisterPartialTab<UIPatternPickerTab>();
            BetterFGTabRegistry.RegisterPartialTab<RoundsTab>();
            BetterFGTabRegistry.RegisterPartialTab<PlinthsUgcTab>();
            BetterFGTabRegistry.RegisterPartialTab<ReplayImagesTab>();
            BetterFGTabRegistry.RegisterPartialTab<CustomCreativeTextureTab>();
            BetterFGTabRegistry.RegisterPartialTab<SkinTextureWizardTab>();
            BetterFGTabRegistry.RegisterPartialTab<SkinTextureMaterialPropsTab>();
            BetterFGTabRegistry.RegisterPartialTab<AllCosmeticsWizardTab>();
            BetterFGTabRegistry.RegisterPartialTab<UIFontWizardTab>();
            BetterFGTabRegistry.RegisterPartialTab<NametagColourTab>();
            BetterFGTabRegistry.RegisterPartialTab<NametagIconTab>();
            BetterFGTabRegistry.RegisterPartialTab<NametagNameplateTab>();
            BetterFGTabRegistry.RegisterPartialTab<NametagCrownTab>();
            BetterFGTabRegistry.RegisterPartialTab<MenuBackgroundImageWizardTab>();

            var uiMan = Managers.Add<BetterFGUIMan>(ModManagers.UI);

            for (int i = 0; i < BetterFGUIMan.MAX_SLOTS && i < BetterFGTabRegistry.All.Count; i++)
                uiMan.RegisterTab(BetterFGTabRegistry.All[i].Factory());

            uiMan.LoadSavedSlots();

            ControllerManager.Create();
        }

        internal static ModManagers Managers => ModManagers.For("BettrFG");

        private static T Spawn<T>(string group) where T : MonoBehaviour
            => Managers.Add<T>(group);

    }

    // for ugc only
    public static class CustomizationServices
    {
        public static RepoRegistry RepoRegistry { get; private set; }
        public static SkinCatalogService CatalogService { get; private set; }
        public static SkinApplicationService ApplicationService { get; private set; }
        public static SkinLoaderService LoaderService { get; private set; }
        public static MenuCustomizationApplication PlinthApp { get; private set; }

        public static void Provide(
            RepoRegistry repo,
            SkinCatalogService catalog,
            SkinApplicationService app,
            SkinLoaderService loader,
            MenuCustomizationApplication plinth)
        {
            RepoRegistry = repo;
            CatalogService = catalog;
            ApplicationService = app;
            LoaderService = loader;
            PlinthApp = plinth;
        }
    }

    // Delayed auto-fetch for repos and UGC on startup
    public class AutoFetchTrigger : MonoBehaviour
    {
        public AutoFetchTrigger(IntPtr ptr) : base(ptr) { }

        public SkinCatalogService CatalogService;
        public RepoRegistry RepoRegistry;

        void Start()
        {
            Invoke("DoFetch", 5f);
        }

        private void DoFetch()
        {
            var active = RepoRegistry?.Active;
            if (active != null) CatalogService?.FetchSkins(active);
            Destroy(gameObject);
        }
    }
}
