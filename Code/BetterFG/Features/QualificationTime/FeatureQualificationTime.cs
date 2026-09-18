using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Mediatonic.Tools.MVVM;
using FGClient.UI;
using FGClient.UI.Core;
using FGClient;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Wushu.Framework.ExtensionMethods;
using BettrFG.uGUI;
using TMPro;
using FallGuysLib.UI;
using SRF;
using System.Linq;
using FG.Common.CMS;

using BetterFG.Services;
using BetterFG.Core;
using BetterFG.UI;
using BetterFG.Customization.UI;
using BetterFG.Customization.Player;
using BetterFG.Utilities;
using FallGuysLib.NPC;

using FG.Common;
using Levels.Progression;
using Levels.ScoreZone;
using FG.Common.UI;
using UnityEngine.Playables;
using MPG.Utility;

namespace BetterFG.Features.QualificationTime
{
    internal class FeatureQualificationTime
    {
        public static readonly BfgFeature feature = new BfgFeature("pb", "ui.personal_bests", true, new List<FeatureSetting>
        {
            new FeatureSetting { id = "store", label = "ui.store_pbs", defaultOn = true },
            new FeatureSetting { id = "qual", label = "ui.show_pb_on_qual", defaultOn = true },
            new FeatureSetting { id = "banner", label = "pb.use_qual_banner", defaultOn = true },
            new FeatureSetting { id = "loadscreen", label = "ui.show_pb_on_load_screen", defaultOn = true },
            new FeatureSetting { id = "play", label = "ui.show_pb_during_play", defaultOn = true },
            new FeatureSetting { id = "timer", label = "ui.show_live_timer", defaultOn = true },
            new FeatureSetting { id = "menu", label = "ui.show_pb_button_on_menu", defaultOn = true },
            new FeatureSetting { id = "favprompt", label = "ui.show_favorite_button_on_qual", defaultOn = true },
            new FeatureSetting { id = "asksave", label = "ui.ask_to_save_pb", defaultOn = false },
            new FeatureSetting { id = "ghost", label = "ui.ghost_run", defaultOn = true },
        },
        choices: new List<FeatureChoice>
        {
            new FeatureChoice
            {
                id = "ghostmode",
                label = "ui.ghost_run_to_show",
                optionIds = new List<string> { "current", "solos", "duos", "squads", "fastest", "all" },
                optionLabels = new List<string> { "ui.current", "ui.solos", "ui.duos", "ui.squads", "ui.fastest", "ui.all" },
                defaultId = "current",
            },
            new FeatureChoice
            {
                id = "livepbmode",
                label = "ui.which_timer_to_show",
                optionIds = new List<string> { "current", "fastest" },
                optionLabels = new List<string> { "ui.current", "ui.fastest" },
                defaultId = "current",
            },
        });

        static bool On(string setting) => BetterFG.Features.FeatureRegistry.IsOn("pb", setting);

        static readonly AnimationCurve dismissCurve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.5f, 1.1f),
            new Keyframe(1f, 0f)
        );

        static readonly AnimationCurve popInCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.6f, 1.12f),
            new Keyframe(0.8f, 0.95f),
            new Keyframe(1f, 1f)
        );


        public static void CreateInMenu()
        {
            if (!On("menu")) return;
            var topbar = GameObject.Find("UICanvas_Client_V2(Clone)/Default/Topbar_Prime(Clone)");
            var tabsLayout = topbar?.transform.Find("SafeArea/TabsHorizontalLayout");
            var shopBtn = tabsLayout?.Find("ShopButton");
            if (topbar == null || tabsLayout == null || shopBtn == null) return;
            if (tabsLayout.Find("ShopButton(Clone)") != null) return;

            var clone = UnityEngine.Object.Instantiate(shopBtn.gameObject, tabsLayout);
            clone.transform.SetSiblingIndex(9);

            tabsLayout.localScale = Vector3.one * 0.9f;

            // the clone came off ShopButton so it carries the store tab's VM — kill it or it fights
            // us trying to drive the store subscreen when our tab lights up.
            var storeVm = clone.GetComponent<SymphonyStoreMenuTabViewModel>();
            if (storeVm != null) UnityEngine.Object.Destroy(storeVm);

            var toggle = clone.GetComponent<UnityEngine.UI.Toggle>();
            if (toggle != null)
            {
                toggle.onValueChanged.RemoveAllListeners();
                toggle.isOn = false;
                PBTabView.TabToggle = toggle;
                // the clone is in the tab ToggleGroup, so it goes on when picked and off the instant
                // ANY other tab is picked — by click OR nav. that on/off IS the show/hide signal, so we
                // don't have to guess from view indices. (Hide uses SetIsOnWithoutNotify so it never
                // re-enters here.)
                toggle.onValueChanged.AddListener((UnityEngine.Events.UnityAction<bool>)(val =>
                {
                    if (val) PBTabView.Show();
                    else PBTabView.Hide();
                }));
            }

            // register the clone into the top bar's Rewired navigation so a controller/keyboard can
            // reach it as the last tab. it's a plain GameObject[] the nav cycles through positionally,
            // so it lands on view 6 (Settings) — the nav patch catches that and shows our tab instead.
            var nav = topbar.GetComponent<SwitchableViewRewiredNavigation>();
            if (nav != null && nav._menuTabs != null)
            {
                var old = nav._menuTabs;
                bool already = false;
                for (int i = 0; i < old.Length; i++) if (old[i] == clone) { already = true; break; }
                if (!already)
                {
                    var grown = new Il2CppReferenceArray<GameObject>(old.Length + 1);
                    for (int i = 0; i < old.Length; i++) grown[i] = old[i];
                    grown[old.Length] = clone;
                    PBTabView.MenuTabIndex = old.Length;
                    nav.SetupMenuTabs(grown);
                }
            }

            var icon = clone.transform.Find("Icon");
            if (icon != null)
            {
                var img = icon.GetComponent<UnityEngine.UI.Image>();
                if (img != null)
                    img.sprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.feature.qualificationtime.featurequalificationtime_icon.png");
            }

            MenuCustomizationApplication.Instance?.SeedForegroundCloneOriginals(shopBtn.gameObject, clone);
            MenuCustomizationApplication.Instance?.ReapplyForegroundFromSettings(clone.transform);

            clone.SetActive(true);
        }

        // recolour the result/timer panel to our menu foreground replacements: the Qualified_Container
        // image goes yellow, its SlicedPanel goes orange. sweeps by name so it doesn't matter where
        // they sit in the hierarchy.
        static void ApplyTimerColors(GameObject root)
        {
            if (root == null) return;
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                if (img.name == "Qualified_Container")
                    img.color = MenuCustomizationApplication.YellowReplacement();
                else if (img.name == "SlicedPanel")
                    img.color = MenuCustomizationApplication.OrangeReplacement();
            }
        }

        // re-hit whatever timer/result UI is currently spawned, so pressing Apply in the UI tab
        // updates a live timer instead of only affecting the next spawn.
        public static void ReapplyTimerColors()
        {
            ApplyTimerColors(_liveTimerGo);
            var result = GameObject.Find("UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates")?.transform.Find("Thisisacustomname");
            if (result != null) ApplyTimerColors(result.gameObject);
        }

        public static void ShowQualificationTime(float elapsed)
        {
            if (!feature.enabled) return;
            if (_qualHandled)
            {
                Plugin.Log.LogInfo("QualTime: already handled this round's qualify, not doing it twice");
                return;
            }
            _qualHandled = true;

            ClientGameManager cgm = null;
            var gsv = FGClient.GlobalGameStateClient.Instance?.GameStateView;
            if (gsv != null)
                gsv.GetLiveClientGameManager(out cgm);

            bool useBanner = On("qual") && On("banner") && cgm != null && LevelEditorManager.Instance == null;
            GameObject clone = !On("qual") ? null : useBanner ? SpawnPromptHost() : SpawnQualPopup(elapsed, cgm);
            _qualPopupGo = clone;
            if (clone == null || useBanner) DestroyLiveTimer();

            string cgmRoundId = null;
            string cgmRoundName = null;
            try
            {
                if (cgm?._round != null)
                {
                    cgmRoundId = cgm._round.Id;
                    cgmRoundName = cgm._round.DisplayNameUnindented;
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("QualTime: cgm round lookup failed: " + ex.Message); }

            string roundId = _roundIdCache ?? cgmRoundId;
            if (string.IsNullOrEmpty(roundId))
            {
                try { roundId = GlobalGameStateClient.Instance?.GameStateView?.CurrentGameLevelName; }
                catch (Exception ex) { Plugin.Log.LogWarning("QualTime: round lookup failed: " + ex.Message); }
            }
            if (string.IsNullOrEmpty(roundId)) roundId = "unknown";
            roundId = PBStore.CanonicalRoundId(roundId);
            string roundName = _roundNameCache ?? cgmRoundName;
            bool isRealUgc = cgm != null ? cgm.IsUGCRound : roundId.StartsWith("ugc-");
            bool isUnityRound = !isRealUgc;
            string roundCacheId = (isUnityRound && !string.IsNullOrEmpty(roundName)) ? roundName : roundId;
            Plugin.Log.LogInfo("QualTime: round=" + roundId + " name=" + roundName + " elapsed=" + elapsed);

            if (IsRaceRound())
            {
                _ghostRecording = false;
                bool usePb = On("store");
                float prevPb = 0f;
                bool isPb = false;
                bool canStorePb = usePb && roundId != "unknown" && !string.IsNullOrEmpty(roundCacheId)
                    && !string.IsNullOrEmpty(roundName) && roundName != roundId;
                // "Ask to save PB" means don't touch the store on qualify — just work out whether this
                // run *would* be a new PB so the label/prompts read right, and let the Save prompt in
                // WaitForFeatureInput do the actual TrySet + ghost. off = old behavior, save immediately.
                bool askSave = On("asksave") && clone != null;
                if (canStorePb)
                {
                    bool hadPb = PBStore.TryGet(roundCacheId, out prevPb, out _);
                    if (askSave)
                    {
                        isPb = !hadPb || elapsed < prevPb;
                    }
                    else
                    {
                        isPb = PBStore.TrySet(roundCacheId, roundName, elapsed, isRealUgc);
                        if (isPb && On("ghost"))
                            SaveGhost(roundCacheId, PBStore.CurrentType());
                    }
                }
                // override-pb path force-wrote the time + ghost itself; treat this re-spawn as a
                // PB so ShowPbLabel paints "Personal Best!" and the sound fires. prevPb was just
                // read as the new slow time (we overwrote the store) — swap in the real previous.
                if (_forceTreatAsPb) { isPb = true; _forceTreatAsPb = false; prevPb = _forcedPrevPb; _forcedPrevPb = 0f; }
                else if (usePb && !canStorePb)
                    Plugin.Log.LogWarning("QualTime: round has no display name, not saving a PB keyed by id");
                if (canStorePb && isUnityRound) SplashCache.TryRename(roundId, roundCacheId);
                Plugin.Log.LogInfo("QualTime: isPb=" + isPb);
                if (useBanner)
                {
                    _bannerElapsed = elapsed;
                    _bannerIsPb = isPb;
                    _bannerReady = true;
                    if (isPb) AudioService.PlayPB();
                    if (_openBanner != null) DressQualBanner(_openBanner);
                }
                if (canStorePb && clone != null)
                {
                    if (!useBanner) ShowPbLabel(clone.transform, isPb, roundName, elapsed, prevPb);
                    BetterFGUIMan.Instance.StartCoroutine(WaitForFeatureInput(clone, roundCacheId, roundName, isPb, elapsed, PBStore.CurrentType(), isRealUgc).WrapToIl2Cpp());
                }
            }

            if (clone != null)
                BetterFGUIMan.Instance.StartCoroutine(DismissAfterDelay(clone).WrapToIl2Cpp());

            Plugin.Log.LogInfo("QualTime: done");
        }

        static GameObject SpawnQualPopup(float elapsed, ClientGameManager cgm)
        {
            Plugin.Log.LogInfo("QualTime: looking for TimeAttackResultViewModel...");

            // parent under InGameUiManager(Clone)/GameStates so the qual popup + its nav prompts
            // are gated by the same focus state as the rest of gameplay UI (menu open → hidden).
            var gameStates = GameObject.Find("UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates");
            if (gameStates == null)
            {
                Plugin.Log.LogInfo("QualTime: GameStates not found, bailing");
                return null;
            }

            if (gameStates.transform.Find("Thisisacustomname") != null)
            {
                Plugin.Log.LogInfo("QualTime: existing result UI detected, skipping");
                return null;
            }

            var clone = TakeLiveTimerForQual(gameStates.transform);
            if (clone == null)
            {
                var original = GetQualificationResultPrefab();
                if (original == null)
                {
                    Plugin.Log.LogInfo("QualTime: original is null, bailing");
                    return null;
                }

                clone = UnityEngine.Object.Instantiate(original, gameStates.transform);
            }
            clone.transform.name = "Thisisacustomname";

            foreach (var binding in clone.GetComponentsInChildren<ActiveBinding>(true))
                UnityEngine.Object.Destroy(binding);

            var rt = clone.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                if (BetterFGUIMan.Instance != null)
                    BetterFGUIMan.Instance.StartCoroutine(AdjustQualTimerPositionAfterFrame(rt).WrapToIl2Cpp());
                else
                {
                    var parentCanvas = rt.GetComponentInParent<Canvas>();
                    var parentRect = parentCanvas != null ? parentCanvas.GetComponent<RectTransform>() : null;
                    float parentHeight = parentRect != null ? parentRect.rect.height : Screen.height;
                    rt.anchoredPosition = new Vector2(0f, -parentHeight * 0.26f);
                    Plugin.Log.LogInfo("QualTime: positioned at " + rt.anchoredPosition + " (parentHeight=" + parentHeight + ")");
                }
            }

            var popupTimeText = clone.transform.FindChild("Canvas")?.FindChild("LapTimeText")?.GetComponent<TextMeshProUGUI>();
            if (popupTimeText != null)
            {
                var popupTimeRt = popupTimeText.GetComponent<RectTransform>();
                if (popupTimeRt != null)
                {
                    popupTimeRt.anchorMin = new Vector2(0.5f, 0.5f);
                    popupTimeRt.anchorMax = new Vector2(0.5f, 0.5f);
                    popupTimeRt.pivot = new Vector2(1f, 0.5f);
                    popupTimeRt.anchoredPosition = new Vector2(120f, 0f);
                    popupTimeRt.sizeDelta = new Vector2(210f, popupTimeRt.sizeDelta.y);
                }
                popupTimeText.alignment = TextAlignmentOptions.MidlineRight;
                popupTimeText.enableAutoSizing = true;
                popupTimeText.fontSize = 25f;
                popupTimeText.fontSizeMax = 25f;
                popupTimeText.fontSizeMin = 25f;
                popupTimeText.transform.localScale = Vector3.one;
            }

            ApplyTimerColors(clone);

            var vm = clone.GetComponent<FGClient.TimeAttackResultViewModel>();
            if (vm == null)
            {
                Plugin.Log.LogInfo("QualTime: no vm on clone, bailing");
                UnityEngine.Object.Destroy(clone);
                return null;
            }

            TimeSpan t = TimeSpan.FromSeconds(elapsed);
            string formatted = string.Format("{0:D2}:{1:D2}:{2:D3}", t.Minutes, t.Seconds, t.Milliseconds);
            Plugin.Log.LogInfo("QualTime: setting time to " + formatted);

            bool isFinal = cgm?._round?.Archetype?.Id == "archetype_final";
            int pos = isFinal ? 1 : (cgm != null ? cgm._qualifiedPlayerCount + 1 : 434);
            string suffix = pos == 1 ? "st" : pos == 2 ? "nd" : pos == 3 ? "rd" : "th";

            vm.TimeText = formatted;
            vm.PositionText = pos + suffix;
            vm.RaiseAllPropertiesChanged();
            clone.transform.localScale = Vector3.zero;
            clone.SetActive(true);
            BetterFGUIMan.Instance.StartCoroutine(PopInAnimation(clone).WrapToIl2Cpp());
            return clone;
        }

        static GameObject SpawnPromptHost()
        {
            var gameStates = GameObject.Find("UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates");
            if (gameStates == null) return null;
            var host = new GameObject("Thisisacustomname");
            var rt = host.AddComponent<RectTransform>();
            rt.SetParent(gameStates.transform, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            BetterFGUIMan.Instance.StartCoroutine(AdjustQualTimerPositionAfterFrame(rt).WrapToIl2Cpp());
            return host;
        }

        static IEnumerator WaitForFeatureInput(GameObject clone, string roundId, string roundName, bool isPb, float elapsed, PbType type, bool isUgc)
        {
            // Favorite prompt (Report glyph) always shows so you can toggle favorite on this PB
            // whether or not the run beat it. non-PB runs additionally get the Set-as-PB prompt
            // (Favourite glyph — different glyph so the two can't collide). both live in a
            // bottom-center HorizontalLayoutGroup so they lay out side by side and auto-center
            // whether there's one prompt or two. favorite label reflects live state.
            var rowGo = new GameObject("BettrFG_QualPromptRow");
            var rowRect = rowGo.AddComponent<RectTransform>();
            rowRect.SetParent(clone.transform, false);
            rowRect.anchorMin = new Vector2(0.5f, 0f);
            rowRect.anchorMax = new Vector2(0.5f, 0f);
            rowRect.pivot = new Vector2(0.5f, 0f);
            rowRect.anchoredPosition = new Vector2(0f, -190f);
            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            // fitter shrinks the row to exactly its children's width; with the pivot at 0.5 that
            // means the whole row is centered on the clone's center instead of growing rightward
            // from it (a zero-width container has nothing to center within).
            var csf = rowGo.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // "Ask to save PB": gate the favorite/set-as-pb prompts behind a Save prompt (same
            // Favourite glyph/input the set-as-pb prompt uses). press it and it vanishes, then the
            // real prompts appear. off = old behavior, prompts show immediately.
            bool gatedBySave = On("asksave");
            if (gatedBySave)
            {
                var savePrompt = NavPromptCore.From(NavPromptCore.Favourite)
                    .WithLabel("Save PB", "bfg_savepb_label")
                    .Bind(BfgBinds.PbSave)
                    .AnchoredAt(NavPromptAnchor.Custom)
                    .SpawnOn(rowGo.transform);
                while (clone != null)
                {
                    if (savePrompt != null && savePrompt.IsPressed()) break;
                    yield return null;
                }
                savePrompt?.Destroy();
                if (clone == null)
                {
                    if (rowGo != null) UnityEngine.Object.Destroy(rowGo);
                    yield break;
                }
                // this is where the PB actually gets written now — nothing hit the store on qualify.
                // only commit if it really was a new/faster time; a slower run still goes through the
                // Set-as-PB prompt below (isPb stays false, so setPbPrompt shows).
                if (isPb)
                {
                    PBStore.TrySet(roundId, roundName, type, elapsed, isUgc);
                    if (On("ghost")) SaveGhost(roundId, type);
                }
                // small beat before the real prompts pop in
                yield return new WaitForSeconds(0.5f);
                if (clone == null)
                {
                    if (rowGo != null) UnityEngine.Object.Destroy(rowGo);
                    yield break;
                }
            }

            var favPrompt = On("favprompt") ? SpawnFavPrompt(rowGo.transform, roundId, roundName) : null;

            NavPromptHandle setPbPrompt = null;
            if (!isPb)
            {
                setPbPrompt = NavPromptCore.From(NavPromptCore.Favourite)
                    .WithLabel("Set as PB", "bfg_setaspb_label")
                    .Bind(BfgBinds.PbOverride)
                    .AnchoredAt(NavPromptAnchor.Custom)
                    .SpawnOn(rowGo.transform);
            }
            if (gatedBySave)
                BetterFGUIMan.Instance.StartCoroutine(PopInAnimation(rowGo).WrapToIl2Cpp());
            while (clone != null)
            {
                // if a popup is up (e.g. our own Favorites confirmation), B closes that popup — don't
                // let the same press also re-toggle the favorite, or closing it undoes the last action.
                var popupRoot = GameObject.Find("UICanvas_Client_V2(Clone)/ModalMessage")?.transform;
                bool popupOpen = popupRoot != null && popupRoot.childCount > 0;
                if (!popupOpen && favPrompt != null && favPrompt.IsPressed())
                {
                    bool nowFeatured = PBStore.TryFeature(roundId, roundName);
                    var strings = CMSLoader.Instance._localisedStrings;
                    string titleKey = "bfg_favpb_title";
                    string msgKey = nowFeatured ? "bfg_favpb_added_msg" : "bfg_favpb_removed_msg";
                    if (!strings._localisedStrings.ContainsKey(titleKey))
                        strings._localisedStrings.Add(titleKey, "Favorites");
                    if (!strings._localisedStrings.ContainsKey(msgKey))
                        strings._localisedStrings.Add(msgKey, nowFeatured ? "Added to your favorites!" : "Removed from your favorites.");
                    PopUp.ShowPopup(titleKey, msgKey, FGClient.UI.PopupInteractionType.Info, FGClient.UI.UIModalMessage.ModalType.MT_OK, FGClient.UI.UIModalMessage.OKButtonType.Disruptive);
                    // respawn with the flipped label so the same prompt now offers the opposite
                    // action instead of vanishing — you can favorite then unfavorite and back.
                    favPrompt.Destroy();
                    favPrompt = SpawnFavPrompt(rowGo.transform, roundId, roundName);
                    favPrompt?.GameObject.transform.SetSiblingIndex(0); // keep favorite on the left
                }
                if (setPbPrompt != null && setPbPrompt.IsPressed())
                    ShowOverridePbConfirm(roundId, roundName, elapsed, type, isUgc);
                yield return null;
            }
            favPrompt?.Destroy();
            setPbPrompt?.Destroy();
            if (rowGo != null) UnityEngine.Object.Destroy(rowGo);
        }

        // Report-glyph prompt whose label follows the live favorite state. re-called after each
        // toggle so the text flips Favorite <-> Unfavorite. the two states use distinct cms keys so
        // NavPromptCore's clone cache keeps a separate clone per label. Custom anchor -> the parent
        // HorizontalLayoutGroup positions it.
        static NavPromptHandle SpawnFavPrompt(Transform parent, string roundId, string roundName)
        {
            bool fav = PBStore.IsFeatured(roundId, roundName);
            return NavPromptCore.From(NavPrompt.PlayEmote)
                .WithLabel(fav ? "Unfavorite PB" : "Favorite PB", fav ? "bfg_unfavpb_label" : "bfg_favpb_label")
                .Bind(BfgBinds.PbFavorite)
                .AnchoredAt(NavPromptAnchor.Custom)
                .SpawnOn(parent);
        }

        static void ShowOverridePbConfirm(string roundId, string roundName, float elapsed, PbType type, bool isUgc)
        {
            string showLabel = ShowLabel(type);
            TimeSpan tNew = TimeSpan.FromSeconds(elapsed);
            string newTime = string.Format("{0:D2}:{1:D2}:{2:D3}", tNew.Minutes, tNew.Seconds, tNew.Milliseconds);
            string oldTime = "--:--:---";
            if (PBStore.TryGet(roundId, type, out float oldPb, out _, roundName))
            {
                TimeSpan tOld = TimeSpan.FromSeconds(oldPb);
                oldTime = string.Format("{0:D2}:{1:D2}:{2:D3}", tOld.Minutes, tOld.Seconds, tOld.Milliseconds);
            }

            var strings = CMSLoader.Instance._localisedStrings;
            string titleKey = "bfg_overridepb_title";
            string msgKey = "bfg_overridepb_msg_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            if (!strings._localisedStrings.ContainsKey(titleKey))
                strings._localisedStrings.Add(titleKey, "Set as PB?");
            strings._localisedStrings.Add(msgKey,
                "Make " + newTime + " your new " + showLabel + " PB for " + roundName + "?\n" +
                "Current PB: " + oldTime + "\n" +
                "The saved ghost will be replaced with this run.");

            PopUp.ShowPopup(titleKey, msgKey,
                FGClient.UI.PopupInteractionType.Query,
                FGClient.UI.UIModalMessage.ModalType.MT_OK_CANCEL,
                FGClient.UI.UIModalMessage.OKButtonType.Disruptive,
                (System.Action<bool>)(ok =>
                {
                    if (!ok) return;
                    float prevPb = 0f;
                    PBStore.TryGet(roundId, type, out prevPb, out _, roundName);
                    PBStore.ForceSet(roundId, roundName, type, elapsed, isUgc);
                    if (On("ghost")) SaveGhost(roundId, type);
                    Plugin.Log.LogInfo($"QualTime: force-set PB {roundName} [{type}] = {newTime}");
                    _forceTreatAsPb = true;
                    // ForceSet just overwrote the stored PB with elapsed, so the re-spawn's
                    // PBStore.TryGet would read the new slow time as "previous". stash the real
                    // previous to show under "Personal Best!" instead.
                    _forcedPrevPb = prevPb;
                    // old qual clone from the original run is still parented to the canvas (5s
                    // dismiss timer hasn't finished); ShowQualificationTime bails if it sees one.
                    var old = GameObject.Find("UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates")?.transform.Find("Thisisacustomname");
                    if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
                    _qualHandled = false;
                    ShowQualificationTime(elapsed);
                }));
        }

        static IEnumerator PopInAnimation(GameObject target)
        {
            float duration = 0.4f;
            float t = 0f;
            while (t < duration)
            {
                if (target == null) yield break;
                t += Time.deltaTime;
                float s = popInCurve.Evaluate(t / duration);
                target.transform.localScale = Vector3.one * s;
                yield return null;
            }
            if (target != null)
                target.transform.localScale = Vector3.one;
        }

        static IEnumerator AdjustQualTimerPositionAfterFrame(RectTransform rt)
        {
            yield return null; // next frame
            if (rt == null) yield break;
            var parentCanvas = rt.GetComponentInParent<Canvas>();
            var parentRect = parentCanvas != null ? parentCanvas.GetComponent<RectTransform>() : null;
            float parentHeight = parentRect != null ? parentRect.rect.height : Screen.height;
            rt.anchoredPosition = new Vector2(0f, -parentHeight * 0.26f);
            Plugin.Log.LogInfo("QualTime: adjusted position after frame -> " + rt.anchoredPosition + " (parentHeight=" + parentHeight + ")");
        }

        static IEnumerator DismissAfterDelay(GameObject clone)
        {
            yield return new WaitForSeconds(10f);

            if (clone == null) yield break;

            float duration = 0.4f;
            float elapsed = 0f;
            var originalScale = clone.transform.localScale;

            while (elapsed < duration)
            {
                if (clone == null) yield break;
                elapsed += Time.deltaTime;
                float s = dismissCurve.Evaluate(elapsed / duration);
                clone.transform.localScale = originalScale * s;
                yield return null;
            }

            if (clone != null)
                UnityEngine.Object.Destroy(clone);
        }

        static void ShowPbLabel(Transform cloneRoot, bool isPb, string levelName, float elapsed, float prevPb)
        {
            var tmps = cloneRoot.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);

            var labelGo = UnityEngine.Object.Instantiate(tmps[0].gameObject, cloneRoot);
            foreach (var b in labelGo.GetComponents<Mediatonic.Tools.MVVM.TMPTextBinding>())
                UnityEngine.Object.Destroy(b);

            var label = labelGo.GetComponent<TMPro.TextMeshProUGUI>();
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0.5f, 0.5f);
            labelRt.anchorMax = new Vector2(0.5f, 0.5f);
            labelRt.pivot = new Vector2(0.5f, 0.5f);
            labelRt.anchoredPosition = new Vector2(0f, -90f);
            labelRt.sizeDelta = new Vector2(600f, 60f);

            string pbText;
            Color pbColor;
            if (isPb)
            {
                pbText = "Personal Best!";
                pbColor = new Color(1f, 1f, 0.3f);
            }
            else
            {
                TimeSpan pbSpan = TimeSpan.FromSeconds(prevPb);
                pbText = string.Format("PB  {0:D2}:{1:D2}:{2:D3}", pbSpan.Minutes, pbSpan.Seconds, pbSpan.Milliseconds);
                pbColor = Color.white;
            }

            label.text = pbText;
            label.color = pbColor;
            label.alignment = TMPro.TextAlignmentOptions.Center;
            label.ForceMeshUpdate();
            labelGo.SetActive(true);
            Plugin.Log.LogInfo("QualTime: PB label -> " + pbText);

            if (isPb && prevPb > 0f)
            {
                var subGo = UnityEngine.Object.Instantiate(tmps[0].gameObject, cloneRoot);
                foreach (var b in subGo.GetComponents<Mediatonic.Tools.MVVM.TMPTextBinding>())
                    UnityEngine.Object.Destroy(b);

                var sub = subGo.GetComponent<TMPro.TextMeshProUGUI>();
                var subRt = subGo.GetComponent<RectTransform>();
                subRt.anchorMin = new Vector2(0.5f, 0.5f);
                subRt.anchorMax = new Vector2(0.5f, 0.5f);
                subRt.pivot = new Vector2(0.5f, 0.5f);
                subRt.anchoredPosition = new Vector2(0f, -55f);
                subRt.sizeDelta = new Vector2(600f, 40f);

                TimeSpan prevSpan = TimeSpan.FromSeconds(prevPb);
                sub.text = string.Format("Previous {0:D2}:{1:D2}:{2:D3}", prevSpan.Minutes, prevSpan.Seconds, prevSpan.Milliseconds);
                sub.color = Color.white;
                sub.transform.localScale *= 0.65f;
                sub.alignment = TMPro.TextAlignmentOptions.Center;
                sub.ForceMeshUpdate();
                subGo.SetActive(true);
                Plugin.Log.LogInfo("QualTime: prev PB label -> " + sub.text);
            }

            if (isPb)
            {
                AudioService.PlayPB();

                /*
                var hintGo = UnityEngine.Object.Instantiate(tmps[0].gameObject, cloneRoot);
                foreach (var b in hintGo.GetComponents<Mediatonic.Tools.MVVM.TMPTextBinding>())
                    UnityEngine.Object.Destroy(b);

                var hint = hintGo.GetComponent<TMPro.TextMeshProUGUI>();
                var hintRt = hintGo.GetComponent<RectTransform>();
                hintRt.anchorMin = new Vector2(0.5f, 0.5f);
                hintRt.anchorMax = new Vector2(0.5f, 0.5f);
                hintRt.pivot = new Vector2(0.5f, 0.5f);
                hintRt.anchoredPosition = new Vector2(0f, -145f);
                hintRt.sizeDelta = new Vector2(700f, 40f);

                UGUIShip.RelabelText(hint, "ui.press_b_to_favorite_this_personal_best");
                hint.color = new Color(0.3f, 1f, 0.3f);
                hint.transform.localScale *= 0.6f;
                hint.alignment = TMPro.TextAlignmentOptions.Center;
                hint.ForceMeshUpdate();
                hintGo.SetActive(true);

                BetterFGUIMan.Instance.StartCoroutine(PulseHintLabel(hintGo).WrapToIl2Cpp());
                */
            }
        }

        static IEnumerator PulseHintLabel(GameObject hintGo)
        {
            float t = 0f;
            float speed = 1.8f;
            float minScale = 0.95f;
            float maxScale = 1.05f;
            var baseScale = hintGo.transform.localScale;

            while (hintGo != null)
            {
                t += Time.deltaTime * speed;
                float s = Mathf.Lerp(minScale, maxScale, (Mathf.Sin(t * Mathf.PI * 2f) + 1f) * 0.5f);
                hintGo.transform.localScale = baseScale * s;
                yield return null;
            }
        }

        // ── Live in-game timer ────────────────────────────────────────────────

        static GameObject _liveTimerGo;
        static FGClient.TimeAttackResultViewModel _liveTimerVm;
        static TextMeshProUGUI _liveTimerText;

        static bool? _isRaceRoundCache = null;
        static string _roundIdCache = null;
        static string _roundNameCache = null;

        internal static string CachedRoundName => _roundNameCache;
        static bool _forceTreatAsPb;
        static float _forcedPrevPb;
        static bool _qualHandled;

        static GameObject _qualPopupGo;
        public static bool QualPopupOpen => _qualPopupGo != null;

        static float _bannerElapsed;
        static bool _bannerIsPb;
        static bool _bannerReady;
        static Component _openBanner;

        public static void OnBannerOpened(Component banner)
        {
            foreach (var t in banner.GetComponentsInChildren<Transform>(true))
                if (t.name == "BettrFG_QualBannerPb") UnityEngine.Object.Destroy(t.gameObject);
            if (!feature.enabled || !On("qual") || !On("banner")) return;
            var qual = banner.TryCast<FGClient.UI.QualifiedScreenViewModel>();
            var win = banner.TryCast<FGClient.UI.WinnerScreenViewModel>();
            if (qual == null && win == null) return;
            string title = qual != null ? qual.TitleId : win.TitleId;
            if (title != "qualified" && title != "winner") return;
            _openBanner = banner;
            if (_bannerReady) DressQualBanner(banner);
        }

        static void DressQualBanner(Component banner)
        {
            var qual = banner.TryCast<FGClient.UI.QualifiedScreenViewModel>();
            var win = banner.TryCast<FGClient.UI.WinnerScreenViewModel>();
            TimeSpan t = TimeSpan.FromSeconds(_bannerElapsed);
            string timeKey = "bfg_qual_banner_time";
            CMSLoader.Instance._localisedStrings._localisedStrings[timeKey] = string.Format("{0:D2}:{1:D2}:{2:D3}", t.Minutes, t.Seconds, t.Milliseconds);
            if (qual != null) qual.TitleId = timeKey; else win.TitleId = timeKey;
            Plugin.Log.LogInfo($"qual banner title -> {_bannerElapsed:F3}s{(_bannerIsPb ? ", personal best" : "")}");
            if (!_bannerIsPb) return;
            foreach (var b in banner.GetComponentsInChildren<Mediatonic.Tools.MVVM.PropertyBinding>(true))
            {
                if (b._sourcePropertyName != "TitleId" || b.GetComponent<TextMeshProUGUI>() == null) continue;
                var titleRt = b.GetComponent<RectTransform>();
                var pbGo = UnityEngine.Object.Instantiate(b.gameObject, titleRt.parent);
                pbGo.name = "BettrFG_QualBannerPb";
                foreach (var cb in pbGo.GetComponents<Mediatonic.Tools.MVVM.PropertyBinding>())
                    UnityEngine.Object.Destroy(cb);
                var pbRt = pbGo.GetComponent<RectTransform>();
                var tmp = pbGo.GetComponent<TextMeshProUGUI>();
                pbRt.anchoredPosition = titleRt.anchoredPosition + new Vector2(0f, -tmp.fontSize * 0.85f);
                pbRt.localScale = titleRt.localScale * 0.45f;
                UGUIShip.RelabelText(tmp, "pb.banner_personal_best");
                tmp.color = new Color(1f, 1f, 0.3f);
                pbGo.SetActive(true);
                Plugin.Log.LogInfo("personal best line hung under " + b.name);
                break;
            }
        }

        static bool? _isTimeAttackCache;
        static TimeAttackPlayerStats _taLocalStats;

        static void ResetRaceRoundCache()
        {
            _isRaceRoundCache = null;
            _isTimeAttackCache = null;
            _taLocalStats = null;
            _taPbLabel = null;
            _taLiveGhostFrames = null;
        }

        static bool IsTimeAttackRound()
        {
            if (_isTimeAttackCache.HasValue) return _isTimeAttackCache.Value;
            ClientGameManager cgm;
            var gsv = GlobalGameStateClient.Instance?.GameStateView;
            // GameRules lands a frame or two after the loading screen, so an early ask gets no answer
            // and no cache entry — the next caller resolves it for real.
            if (gsv != null && gsv.GetLiveClientGameManager(out cgm) && cgm?.GameRules != null)
                return (_isTimeAttackCache = cgm.GameRules.IsTimeAttackGameMode).Value;
            return false;
        }

        // subtracted from the round clock so ImmediateRespawnTweak's "respawn at start" can zero the
        // live timer / ghost recorder without touching the native round clock itself.
        static float _elapsedBaseline;

        internal static float RaceElapsed()
            => Mathf.Max(0f, (GlobalGameStateClient.Instance?.GameStateView?.GameplayTimeElapsed ?? 0f) - _elapsedBaseline);

        // GhostRecordCoroutine already treats a backwards time jump as a fresh attempt (clears
        // _ghostFrames, re-arms) — that's built for time attack lap resets, but the same check fires
        // here once RaceElapsed() drops, so nothing else needs to touch the ghost state.
        internal static void ResetElapsedBaseline()
        {
            _elapsedBaseline = GlobalGameStateClient.Instance?.GameStateView?.GameplayTimeElapsed ?? 0f;
            Plugin.Log.LogInfo("QualTime: elapsed baseline reset, respawned at start");
        }

        // the ghost's timebase. a normal round is measured off the round clock, which only ever goes
        // up. a time attack run is measured off the CURRENT LAP, which resets to zero every attempt —
        // and that reset is exactly what re-arms the recorder and replays the ghost, no event needed.
        // -1 means no lap is running (you haven't crossed the start line, or you just reset).
        internal static float GhostClock()
        {
            if (!IsTimeAttackRound())
                return RaceElapsed();

            // cached for the round and dropped by ResetRaceRoundCache, so it can't outlive its round
            if (_taLocalStats == null)
            {
                ClientGameManager cgm;
                var gsv = GlobalGameStateClient.Instance?.GameStateView;
                if (gsv == null || !gsv.GetLiveClientGameManager(out cgm) || cgm == null) return -1f;
                var tsm = cgm._timeAttackScoreManager;
                _taLocalStats = tsm?._timeAttackManager?.GetPlayerStats(tsm.LocalPlayer);
                if (_taLocalStats == null) return -1f;
            }

            var lap = _taLocalStats.GetCurrentLap;
            if (lap == null || lap.LapState != TimeAttackLapState.InProgress) return -1f;
            return lap.CurrentLapTime;
        }

        static bool HasRaceArchetype()
        {
            try
            {
                ClientGameManager cgm;
                var gsv = GlobalGameStateClient.Instance?.GameStateView;
                if (gsv != null && gsv.GetLiveClientGameManager(out cgm))
                {
                    string archId = cgm?._round?.Archetype?.Id;
                    return archId == "archetype_race";
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("QualTime: archetype lookup failed: " + ex.Message); }
            return false;
        }

        static bool IsRaceRound()

        {

            if (_isRaceRoundCache.HasValue) return _isRaceRoundCache.Value;

            // GameRules is the source of truth now - archetype is obsolete on finals.
            // IsRaceRound is also true on hunt rounds (scoring/bubble/score-target), which we
            // count as races too, so this single check covers everything below.
            try
            {
                ClientGameManager cgm;
                var gsv = GlobalGameStateClient.Instance?.GameStateView;
                if (gsv != null && gsv.GetLiveClientGameManager(out cgm) && cgm?.GameRules != null)
                    return (_isRaceRoundCache = cgm.GameRules.IsRaceRound).Value;
            }
            catch (Exception ex) { Plugin.Log.LogWarning("QualTime: GameRules lookup failed: " + ex.Message); }

            return (_isRaceRoundCache = false).Value;

            /* old component-based detection - kept for reference
            if (HasRaceArchetype())
                return (_isRaceRoundCache = true).Value;

            var endZones = Resources.FindObjectsOfTypeAll<COMMON_ObjectiveReachEndZone>();

            foreach (var c in endZones)

                if (c != null && c.gameObject != null && c.gameObject.activeInHierarchy && c.gameObject.hideFlags == HideFlags.None)

                    return (_isRaceRoundCache = true).Value;

            var grabs = Resources.FindObjectsOfTypeAll<COMMON_GrabToQualify>();

            foreach (var c in grabs)

                if (c != null && c.gameObject != null && c.gameObject.activeInHierarchy && c.gameObject.hideFlags == HideFlags.None)

                    return (_isRaceRoundCache = true).Value;

            var bubble = Resources.FindObjectsOfTypeAll<COMMON_ScoringBubble>();

            foreach (var c in bubble)

                if (c != null && c.gameObject != null && c.gameObject.activeInHierarchy && c.gameObject.hideFlags == HideFlags.None)

                    return (_isRaceRoundCache = true).Value;

            var bubble2 = Resources.FindObjectsOfTypeAll<BubbleZone>();

            foreach (var c in bubble2)

                if (c != null && c.gameObject != null && c.gameObject.activeInHierarchy && c.gameObject.hideFlags == HideFlags.None)

                    return (_isRaceRoundCache = true).Value;

            var hoops = Resources.FindObjectsOfTypeAll<COMMON_Hoop>();

            foreach (var c in hoops)

                if (c != null && c.gameObject != null && c.gameObject.activeInHierarchy && c.gameObject.hideFlags == HideFlags.None)

                    return (_isRaceRoundCache = true).Value;

            var singleHoops = Resources.FindObjectsOfTypeAll<Levels.HoopHoopRevenge.COMMON_SingleScoreHoop>();

            foreach (var c in singleHoops)

                if (c != null && c.gameObject != null && c.gameObject.activeInHierarchy && c.gameObject.hideFlags == HideFlags.None)

                    return (_isRaceRoundCache = true).Value;

            var destructibles = Resources.FindObjectsOfTypeAll<LevelEditorDestructibleObjectParameter>();

            foreach (var c in destructibles)

                if (c != null && c.gameObject != null && c.gameObject.activeInHierarchy && c.gameObject.hideFlags == HideFlags.None && c._selectedPointsAwarded >= 1)

                    return (_isRaceRoundCache = true).Value;

            var triggerZones = Resources.FindObjectsOfTypeAll<LevelEditorTriggerZoneActiveBase>();

            foreach (var c in triggerZones)

                if (c != null && c.gameObject != null && c.gameObject.activeInHierarchy && c.gameObject.hideFlags == HideFlags.None && c._pointsScored >= 1)

                    return (_isRaceRoundCache = true).Value;

            return (_isRaceRoundCache = false).Value;
            */

        }

        static IEnumerator SpawnLiveTimerDeferred()
        {
            // one frame so cgm.GameRules is populated after CleanupLoadingScreens lands.
            yield return null;

            // time attack already draws a lap timer top-middle, so a second clock top-right is just
            // noise. we only hang the PB underneath the game's box.
            if (IsTimeAttackRound())
            {
                DestroyLiveTimer();
                if (On("play"))
                    yield return BetterFGUIMan.Instance.StartCoroutine(SpawnTimeAttackPbLabel().WrapToIl2Cpp());
                yield break;
            }

            if (!On("timer")) { DestroyLiveTimer(); yield break; }
            if (IsRaceRound()) SpawnLiveTimer();
        }

        const string TaPbLabelName = "BettrFG_TimeAttackPbLabel";
        static TextMeshProUGUI _taPbLabel;

        static IEnumerator SpawnTimeAttackPbLabel()
        {
            // the TA hud is built with PlayingState, which isn't up yet when the loading screen clears
            Transform box = null;
            float waited = 0f;
            while (waited < 15f)
            {
                box = GameObject.Find("UICanvas_Client_V2(Clone)")?.transform.Find(
                    "Default/InGameUiManager(Clone)/GameStates/PlayingState/GameplayTimeAttackViewModel/TopMiddleContainer/PB_UI_TimeAttack_LapTimer/TimeAttackContainer/TimeAttackBox");
                if (box != null) break;
                yield return new WaitForSeconds(0.1f);
                waited += 0.1f;
            }
            if (box == null) { Plugin.Log.LogWarning("time attack lap timer never turned up, so no PB under it"); yield break; }
            if (box.Find(TaPbLabelName) != null) yield break;

            // clone the box's own text so the PB inherits the TA hud's font and material
            var src = box.GetComponentInChildren<TextMeshProUGUI>(true);
            if (src == null) { Plugin.Log.LogWarning("nothing to clone inside TimeAttackBox, no PB label"); yield break; }

            var go = UnityEngine.Object.Instantiate(src.gameObject, box);
            go.name = TaPbLabelName;
            foreach (var b in go.GetComponents<Mediatonic.Tools.MVVM.TMPTextBinding>())
                UnityEngine.Object.DestroyImmediate(b);
            foreach (var b in go.GetComponents<ActiveBinding>())
                UnityEngine.Object.DestroyImmediate(b);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -4f);
            rt.sizeDelta = new Vector2(400f, 44f);
            rt.localScale = Vector3.one * 0.55f;

            _taPbLabel = go.GetComponent<TextMeshProUGUI>();
            _taPbLabel.enableAutoSizing = false;
            _taPbLabel.enableWordWrapping = false;
            _taPbLabel.overflowMode = TextOverflowModes.Overflow;
            _taPbLabel.alignment = TextAlignmentOptions.Center;
            _taPbLabel.color = new Color(1f, 1f, 1f, 0.8f);
            go.SetActive(true);

            // the binding we just killed still gets one more Update this frame, so writing the text
            // inline would get stomped back to the lap time. same dance as the load-screen label.
            yield return null;
            RefreshTimeAttackPbLabel();
        }

        static void RefreshTimeAttackPbLabel()
        {
            var lbl = _taPbLabel;
            if (lbl is null || lbl.m_CachedPtr == IntPtr.Zero) return;
            if (!TryGetLiveRoundIds(out string cacheId, out string roundName, out _)) return;

            bool found = TryGetLiveTimerPb(cacheId, roundName, out float pb);
            UGUIShip.RelabelText(lbl, FormatPbText(found, pb));
            lbl.ForceMeshUpdate();
        }

        // round cacheId + display name off the LIVE cgm. the _roundIdCache pair is wiped by
        // CleanupLoadingScreens, so anything asking mid-round has to go back to the manager.
        static bool TryGetLiveRoundIds(out string cacheId, out string roundName, out bool isUgc)
        {
            cacheId = null;
            roundName = null;
            isUgc = false;

            ClientGameManager cgm;
            var gsv = GlobalGameStateClient.Instance?.GameStateView;
            if (gsv == null || !gsv.GetLiveClientGameManager(out cgm) || cgm?._round == null) return false;

            string rid = PBStore.CanonicalRoundId(_roundIdCache ?? cgm._round.Id);
            roundName = _roundNameCache ?? cgm._round.DisplayNameUnindented;
            isUgc = cgm.IsUGCRound;
            cacheId = (!isUgc && !string.IsNullOrEmpty(roundName)) ? roundName : rid;
            return !string.IsNullOrEmpty(cacheId);
        }

        static void SpawnLiveTimer()
        {
            if (!On("timer"))
            {
                DestroyLiveTimer();
                return;
            }

            DestroyLiveTimer();

            var original = GetQualificationResultPrefab();
            if (original == null)
            {
                Plugin.Log.LogInfo("QualTime: live timer original is null, bailing");
                return;
            }

            var canvas = GameObject.Find("UICanvas_Client_V2(Clone)");
            var playingState = canvas?.transform.Find("Default/InGameUiManager(Clone)/GameStates/PlayingState")?.gameObject;
            if (playingState == null)
            {
                Plugin.Log.LogInfo("QualTime: PlayingState not found, can't spawn live timer");
                return;
            }

            var clone = UnityEngine.Object.Instantiate(original, playingState.transform);

            foreach (var binding in clone.GetComponentsInChildren<ActiveBinding>(true))
                UnityEngine.Object.Destroy(binding);

            var vm = clone.GetComponent<FGClient.TimeAttackResultViewModel>();
            if (vm == null)
            {
                Plugin.Log.LogInfo("QualTime: live timer no vm on clone, bailing");
                UnityEngine.Object.Destroy(clone);
                return;
            }

            var rt = clone.GetComponent<RectTransform>();
            // anchor to the top-right corner so it sticks there across resolutions/aspect ratios,
            // instead of a fixed pixel offset from the parent pivot
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(70f, -250f);

            var timetexttmp = rt.transform.FindChild("Canvas").FindChild("LapTimeText").GetComponent<TextMeshProUGUI>();
            timetexttmp.alignment = TextAlignmentOptions.MidlineLeft;
            timetexttmp.enableAutoSizing = false;
            timetexttmp.transform.localPosition = new Vector3(0, 0, 0);

            foreach (var b in timetexttmp.GetComponents<Mediatonic.Tools.MVVM.TMPTextBinding>())
                UnityEngine.Object.Destroy(b);
            _liveTimerText = timetexttmp;

            ApplyTimerColors(clone);

            vm.PositionText = "";
            vm.TimeText = "00:00:000";
            vm.RaiseAllPropertiesChanged();
            clone.SetActive(true);

            // --- PB LABEL UNDER TIMER ---
            bool localSucceededLive = FGClient.GlobalGameStateClient.Instance._clientPlayerManager?.LocalPlayerSucceeded ?? false;

            var tmps = clone.GetComponentsInChildren<TextMeshProUGUI>(true);
            string roundId2 = _roundIdCache;
            string roundName2 = _roundNameCache;
            try
            {
                ClientGameManager pbCgm;
                var pbGsv = GlobalGameStateClient.Instance?.GameStateView;
                if (pbGsv != null && pbGsv.GetLiveClientGameManager(out pbCgm) && pbCgm?._round != null)
                {
                    if (string.IsNullOrEmpty(roundId2)) roundId2 = pbCgm._round.Id;
                    if (string.IsNullOrEmpty(roundName2)) roundName2 = pbCgm._round.DisplayNameUnindented;
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("QualTime: live pb cgm round lookup failed: " + ex.Message); }
            if (string.IsNullOrEmpty(roundId2))
            {
                try { roundId2 = GlobalGameStateClient.Instance?.GameStateView?.CurrentGameLevelName; }
                catch (Exception ex) { Plugin.Log.LogWarning("QualTime: live pb round lookup failed: " + ex.Message); }
            }
            if (tmps.Length > 0 && !localSucceededLive && IsRaceRound() && On("play"))
            {
                var pbGo = UnityEngine.Object.Instantiate(tmps[0].gameObject, clone.transform);
                pbGo.name = "QualTimeLivePbLabel";

                foreach (var b in pbGo.GetComponents<Mediatonic.Tools.MVVM.TMPTextBinding>())
                    UnityEngine.Object.Destroy(b);

                var pbText = pbGo.GetComponent<TextMeshProUGUI>();
                var pbRt = pbGo.GetComponent<RectTransform>();

                pbRt.anchorMin = new Vector2(0.5f, 0.5f);
                pbRt.anchorMax = new Vector2(0.5f, 0.5f);
                pbRt.pivot = new Vector2(0.5f, 0.5f);
                pbRt.anchoredPosition = new Vector2(0f, -60f);
                pbRt.sizeDelta = new Vector2(400f, 40f);

                float pb = 0f;
                bool pbFound = !string.IsNullOrEmpty(roundId2) && TryGetLiveTimerPb(roundId2, roundName2, out pb);
                if (!pbFound && !string.IsNullOrEmpty(roundName2)) pbFound = TryGetLiveTimerPb(roundName2, null, out pb);
                if (pbFound)
                {
                    TimeSpan pbSpan = TimeSpan.FromSeconds(pb);
                    pbText.text = string.Format("PB {0:D2}:{1:D2}:{2:D3}", pbSpan.Minutes, pbSpan.Seconds, pbSpan.Milliseconds);
                }
                else
                {
                    UGUIShip.RelabelText(pbText, "ui.pb");
                }

                pbText.color = new Color(1f, 1f, 1f, 0.8f);
                pbText.alignment = TextAlignmentOptions.MidlineRight;
                pbText.rectTransform.anchoredPosition = new Vector3(-120f, -55f, 0);
                pbText.transform.localScale = Vector3.one * 0.5f;
                pbText.ForceMeshUpdate();
                pbGo.SetActive(true);
            }

            _liveTimerGo = clone;
            _liveTimerVm = vm;

            HookLiveTimer(true);
            Plugin.Log.LogInfo("QualTime: live timer spawned");
        }

        static GameObject GetQualificationResultPrefab()
        {
            var original = GetTimeAttackResultTemplate();
            if (original == null) return null;

            return AssetManager.RuntimePrefab("qualification_time_result", original.gameObject, go =>
            {
                foreach (var binding in go.GetComponentsInChildren<ActiveBinding>(true))
                    UnityEngine.Object.DestroyImmediate(binding);
            });
        }

        static GameObject TakeLiveTimerForQual(Transform canvas)
        {
            if (_liveTimerGo == null) return null;

            HookLiveTimer(false);
            var go = _liveTimerGo;
            _liveTimerGo = null;
            _liveTimerVm = null;
            _liveTimerText = null;

            var pbLabel = go.transform.Find("QualTimeLivePbLabel");
            if (pbLabel != null) UnityEngine.Object.DestroyImmediate(pbLabel.gameObject);

            go.transform.SetParent(canvas, false);
            go.SetActive(false);
            return go;
        }

        static FGClient.TimeAttackResultViewModel GetTimeAttackResultTemplate()
        {
            var all = Resources.FindObjectsOfTypeAll<FGClient.TimeAttackResultViewModel>();
            foreach (var vm in all)
            {
                if (vm == null || vm.gameObject == null) continue;
                if (vm.transform.root.name.Contains("BetterFG")) continue;
                if (_liveTimerGo != null && vm.gameObject == _liveTimerGo) continue;
                if (vm.gameObject.name == "Thisisacustomname") continue;
                if (vm.gameObject.scene.name == "DontDestroyOnLoad") continue;
                return vm;
            }

            return null;
        }

        static void DestroyLiveTimer()
        {
            HookLiveTimer(false);
            if (_liveTimerGo != null)
            {
                UnityEngine.Object.Destroy(_liveTimerGo);
                _liveTimerGo = null;
                _liveTimerVm = null;
                _liveTimerText = null;
            }
        }

        // the game's own gameplay timer VM refreshes the same clock we display, so its Update is the
        // moment the value changes. patched in only while our timer object is alive.
        static System.Reflection.MethodInfo _hookedLiveTimer;
        static string _liveTimerLast;

        static void HookLiveTimer(bool on)
        {
            var h = Plugin.HarmonyInstance;
            if (h == null) return;

            if (!on)
            {
                if (_hookedLiveTimer == null) return;
                h.Unpatch(_hookedLiveTimer, HarmonyPatchType.Postfix, h.Id);
                _hookedLiveTimer = null;
                return;
            }

            _liveTimerLast = null;
            if (_hookedLiveTimer != null) return;
            var target = AccessTools.Method(typeof(GameplayTimerViewModel), "Update");
            if (target == null) { Plugin.Log.LogWarning("QualTime: no GameplayTimerViewModel.Update, live timer will not tick"); return; }
            h.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(FeatureQualificationTime), nameof(LiveTimerPostfix))));
            _hookedLiveTimer = target;
        }

        static void LiveTimerPostfix()
        {
            var text = _liveTimerText;
            if (text is null || text.m_CachedPtr == IntPtr.Zero) return;
            var gsv = GlobalGameStateClient.Instance?.GameStateView;
            if (gsv == null) return;

            TimeSpan t = TimeSpan.FromSeconds(RaceElapsed());
            string formatted = string.Format("{0:D2}:{1:D2}:{2:D3}", t.Minutes, t.Seconds, t.Milliseconds);
            if (formatted == _liveTimerLast) return;
            _liveTimerLast = formatted;
            text.text = formatted;
        }

        // ── Ghost run ─────────────────────────────────────────────────────────

        // negative magic distinguishes new format (with anim) from old (frame count first)
        const int GhostMagic = unchecked((int)0xBF670002);

        internal static List<(float t, Vector3 pos, Quaternion rot, int stateHash, float animTime)> _ghostFrames;
        // the frame list the live time attack ghost is playing back. we rewrite it in place when you
        // beat your PB mid-round, so the next lap races the run you just did.
        static List<(float, Vector3, Quaternion, int, float)> _taLiveGhostFrames;
        static bool _ghostRecording;
        static int _ghostGen;
        // ghosts currently in the round. usually one, but "All" mode spawns up to three.
        static readonly List<GameObject> _ghostGos = new List<GameObject>();

        // ghost mode: which show's ghost(s) to play. a FeatureChoice on the feature, so it
        // auto-renders as a dropdown in the features tab and stores under "feature.pb.ghostmode".
        internal static string GhostMode => feature.GetChoice("ghostmode");

        // which shows to spawn ghosts for, given the mode and what's available for this round.
        static List<PbType> GhostTypesToSpawn(string cacheId)
        {
            var result = new List<PbType>();
            // a time attack ghost is timestamped against the LAP clock, a race ghost against the round
            // clock — they're not interchangeable, so the ghostmode dropdown doesn't get a say here.
            if (IsTimeAttackRound())
            {
                if (GhostExistsFor(cacheId, PbType.TimeAttack)) result.Add(PbType.TimeAttack);
                return result;
            }
            string mode = GhostMode;
            if (mode == "current") { var t = PBStore.CurrentType(); if (GhostExistsFor(cacheId, t)) result.Add(t); }
            else if (mode == "solos") { if (GhostExistsFor(cacheId, PbType.Solos)) result.Add(PbType.Solos); }
            else if (mode == "duos") { if (GhostExistsFor(cacheId, PbType.Duos)) result.Add(PbType.Duos); }
            else if (mode == "squads") { if (GhostExistsFor(cacheId, PbType.Squads)) result.Add(PbType.Squads); }
            else if (mode == "all")
            {
                foreach (var t in new[] { PbType.Solos, PbType.Duos, PbType.Squads })
                    if (GhostExistsFor(cacheId, t)) result.Add(t);
            }
            else // "fastest": whichever show has the fastest stored PB and a ghost on disk
            {
                PbType? best = null;
                float bestTime = float.MaxValue;
                foreach (var t in new[] { PbType.Solos, PbType.Duos, PbType.Squads })
                {
                    if (!GhostExistsFor(cacheId, t)) continue;
                    if (PBStore.TryGet(cacheId, t, out float time, out _) && time < bestTime)
                    {
                        bestTime = time;
                        best = t;
                    }
                    else if (!best.HasValue)
                        best = t; // no stored time but a ghost exists - still a candidate
                }
                if (best.HasValue) result.Add(best.Value);
            }
            return result;
        }

        // live-timer PB label: "current" reads the show you're actually playing (old behavior),
        // "fastest" scans solos/duos/squads for this round and shows whichever is quickest.
        static bool TryGetLiveTimerPb(string id, string displayNameHint, out float pb)
        {
            if (IsTimeAttackRound())
                return PBStore.TryGet(id, PbType.TimeAttack, out pb, out _, displayNameHint);
            if (feature.GetChoice("livepbmode") != "fastest")
                return PBStore.TryGet(id, out pb, out _, displayNameHint);

            pb = 0f;
            bool found = false;
            foreach (var t in new[] { PbType.Solos, PbType.Duos, PbType.Squads })
            {
                if (PBStore.TryGet(id, t, out float time, out _, displayNameHint) && (!found || time < pb))
                {
                    pb = time;
                    found = true;
                }
            }
            return found;
        }

        static bool GhostExistsFor(string cacheId, PbType t)
        {
            try
            {
                if (File.Exists(GhostPath(cacheId, t))) return true;
                if (t == PbType.Solos && File.Exists(LegacyGhostPath(cacheId))) return true;
            }
            catch { }
            return false;
        }

        static string ShowLabel(PbType t) => t == PbType.Solos ? "Solos" : t == PbType.Duos ? "Duos" : t == PbType.Squads ? "Squads" : "Time Attack";

        static string GetCurrentRoundCacheId()
        {
            string rid = PBStore.CanonicalRoundId(_roundIdCache ?? "");
            if (string.IsNullOrEmpty(rid)) return null;
            bool isUgc = rid.StartsWith("ugc-");
            return (!isUgc && !string.IsNullOrEmpty(_roundNameCache)) ? _roundNameCache : rid;
        }

        static string GhostDir =>
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "BettrFG", "Settings", "ghosts");

        static string Suffix(PbType t) => t == PbType.Solos ? "__solos" : t == PbType.Duos ? "__duos" : t == PbType.Squads ? "__squads" : "__timeattack";

        // per-show ghost file. legacy ghosts have no suffix and are treated as solos.
        static string GhostPath(string cacheId, PbType t) =>
            Path.Combine(GhostDir, string.Concat(cacheId.Split(Path.GetInvalidFileNameChars())) + Suffix(t) + ".ghost");

        static string LegacyGhostPath(string cacheId) =>
            Path.Combine(GhostDir, string.Concat(cacheId.Split(Path.GetInvalidFileNameChars())) + ".ghost");

        static Animator FindBeanAnimator(GameObject bean) => BeanAnimationUtil.FindAnimator(bean);

        static void SaveGhost(string cacheId, PbType type)
        {
            if (_ghostFrames == null || _ghostFrames.Count == 0) return;
            try
            {
                Directory.CreateDirectory(GhostDir);
                using (var bw = new BinaryWriter(File.Create(GhostPath(cacheId, type))))
                {
                    bw.Write(GhostMagic);
                    bw.Write(_ghostFrames.Count);
                    foreach (var (t, p, r, sh, at) in _ghostFrames)
                    {
                        bw.Write(t);
                        bw.Write(p.x); bw.Write(p.y); bw.Write(p.z);
                        bw.Write(r.x); bw.Write(r.y); bw.Write(r.z); bw.Write(r.w);
                        bw.Write(sh);
                        bw.Write(at);
                    }
                }
                Plugin.Log.LogInfo($"Ghost: saved {_ghostFrames.Count} frames for {cacheId}");
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Ghost: save failed: " + ex.Message); }
        }

        // deleting a PB should take its ghost run with it. the ghost is named after roundCacheId,
        // which is the round's display NAME for unity rounds and the ugc- id for ugc rounds. from
        // the delete button we don't know which it was, so just try both candidate ids - whichever
        // file exists gets nuked, the other GhostPath simply won't exist.
        // all candidate ghost paths for a cacheId: the three per-show files plus the legacy unsuffixed one.
        static IEnumerable<string> AllGhostPaths(string cacheId)
        {
            yield return GhostPath(cacheId, PbType.Solos);
            yield return GhostPath(cacheId, PbType.Duos);
            yield return GhostPath(cacheId, PbType.Squads);
            yield return GhostPath(cacheId, PbType.TimeAttack);
            yield return LegacyGhostPath(cacheId);
        }

        internal static void DeleteGhost(params string[] cacheIds)
        {
            foreach (var cacheId in cacheIds)
            {
                if (string.IsNullOrEmpty(cacheId)) continue;
                foreach (var path in AllGhostPaths(cacheId))
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                            Plugin.Log.LogInfo($"Ghost: deleted ghost {path}");
                        }
                    }
                    catch (Exception ex) { Plugin.Log.LogWarning("Ghost: delete failed: " + ex.Message); }
                }
            }
        }

        // does this PB have any saved ghost (any show, or legacy)?
        internal static bool HasGhost(params string[] cacheIds)
        {
            foreach (var cacheId in cacheIds)
            {
                if (string.IsNullOrEmpty(cacheId)) continue;
                foreach (var path in AllGhostPaths(cacheId))
                    try { if (File.Exists(path)) return true; }
                    catch { }
            }
            return false;
        }

        // loads the ghost for a specific show. solos falls back to the legacy unsuffixed file so old
        // single-ghost recordings still play as the solos ghost.
        static List<(float, Vector3, Quaternion, int, float)> LoadGhost(string cacheId, PbType type)
        {
            string path = GhostPath(cacheId, type);
            if (!File.Exists(path) && type == PbType.Solos) path = LegacyGhostPath(cacheId);
            if (!File.Exists(path)) return null;
            try
            {
                using (var br = new BinaryReader(File.OpenRead(path)))
                {
                    int magic = br.ReadInt32();
                    if (magic != GhostMagic) { Plugin.Log.LogWarning("Ghost: old format, re-run to record animations"); return null; }
                    int n = br.ReadInt32();
                    var frames = new List<(float, Vector3, Quaternion, int, float)>(n);
                    for (int i = 0; i < n; i++)
                    {
                        float t = br.ReadSingle();
                        var p = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        var r = new Quaternion(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        int sh = br.ReadInt32();
                        float at = br.ReadSingle();
                        frames.Add((t, p, r, sh, at));
                    }
                    return frames;
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Ghost: load failed: " + ex.Message); return null; }
        }

        static IEnumerator GhostRecordCoroutine(int gen)
        {
            var wait = new WaitForSeconds(1f / 20f);
            FallGuysCharacterController local = null;
            Transform localTf = null;
            Animator localAnim = null;
            float lastT = 0f;
            bool armed = false;
            while (_ghostRecording && _ghostGen == gen)
            {
                if (local is null || local.m_CachedPtr == IntPtr.Zero)
                {
                    // cheap cached getter — NOT a FindObjectsOfTypeAll heap scan. when spectating there's
                    // no local player so this stays null all round; scanning the whole heap 20x/sec here
                    // was freezing the game every tick while spectating.
                    local = FallGuysLib.Players.PlayerUtils.PlayerController;
                    if (local != null && !local.IsLocalPlayer) local = null;
                    if (local != null)
                    {
                        localTf = local.transform;
                        localAnim = FindBeanAnimator(local.gameObject);
                    }
                }
                if (localTf is not null && localTf.m_CachedPtr != IntPtr.Zero)
                {
                    float t = GhostClock();
                    // between time attack laps: hold the finished run in the buffer, because
                    // RegisterTime lands after a network round-trip and SaveGhost reads it there.
                    // the buffer is only wiped once the NEXT lap actually starts.
                    if (t < 0f) { armed = true; yield return wait; continue; }
                    if (armed || t < lastT) { _ghostFrames.Clear(); armed = false; }
                    lastT = t;
                    int sh = 0; float at = 0f;
                    if (localAnim != null)
                    {
                        var info = localAnim.GetCurrentAnimatorStateInfo(0);
                        sh = info.shortNameHash;
                        at = info.normalizedTime;
                    }
                    // one free call instead of two boxing transform getters plus the Transform fetch
                    localTf.GetPositionAndRotation(out var p, out var r);
                    _ghostFrames.Add((t, p, r, sh, at));
                }
                yield return wait;
            }
        }

        static IEnumerator SpawnGhostDeferred(int gen)
        {
            yield return new WaitForSeconds(2f);
            if (_ghostGen != gen) yield break;
            if (SettingsService.Get("debug.disable_ghost", "false") == "true") yield break;

            if (!TryGetLiveRoundIds(out string cacheId, out _, out _)) { Plugin.Log.LogInfo("Ghost: no round id, skipping"); yield break; }

            var types = GhostTypesToSpawn(cacheId);
            if (types.Count == 0) { Plugin.Log.LogInfo("Ghost: nothing to spawn for mode " + GhostMode); yield break; }

            string playerName = string.IsNullOrEmpty(LocalPlayerInfo.DisplayName) ? "Ghost" : LocalPlayerInfo.DisplayName;

            // only tag the ghost with its show name when more than one show could be on screen, or
            // when the user explicitly picked a single show. for plain "fastest" with one ghost we
            // still show the show so you know which run it is. (Name) (PB - Solos/Duos/Squads)
            foreach (var type in types)
            {
                if (_ghostGen != gen) yield break;

                var frames = LoadGhost(cacheId, type);
                Plugin.Log.LogInfo($"Ghost: loaded {frames?.Count ?? -1} frames for {cacheId} [{type}]");
                if (frames == null || frames.Count == 0) continue;
                if (type == PbType.TimeAttack) _taLiveGhostFrames = frames;

                string ghostName = playerName + " (PB - " + ShowLabel(type) + ")";
                BetterFG.Features.Replay.FeatureReplay.BeginGhostSpawn();
                var ghost = SpawnBeanUtils.SpawnBean(ghostName, new NPCCustomization("", "", null, null, -1));
                Plugin.Log.LogInfo($"Ghost: SpawnBean result={ghost != null} [{type}]");
                if (ghost == null) { BetterFG.Features.Replay.FeatureReplay.EndGhostSpawn(); continue; }

                var ghostGo = ghost.gameObject;
                ghostGo.name = "BettrFG_Ghost_" + ghostName;

                if (ghost._rigidbody != null)
                {
                    ghost._rigidbody.isKinematic = true;
                    ghost._rigidbody.useGravity = false;
                }
                if (ghost._ragdollController != null)
                    ghost._ragdollController._upperBodyEnabled = false;
                foreach (var col in ghostGo.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.Destroy(col);

                var ghostAnim = FindBeanAnimator(ghostGo);
                if (ghostAnim != null)
                {
                    // the ghost's pose is recorded, not simulated — so when it's off screen Unity can
                    // skip the retarget/IK/transform-write pass entirely. the state machine keeps
                    // running underneath, so it's in the right state the frame it comes back into view.
                    ghostAnim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                    ghostAnim.applyRootMotion = false;
                }
                UnityEngine.Object.Destroy(ghost);

                BetterFGUIMan.Instance.StartCoroutine(GhostDressCoroutine(ghostGo, gen).WrapToIl2Cpp());
                RegisterGhostNametag(ghostName);
                _ghostGos.Add(ghostGo);
                BetterFG.Features.Replay.FeatureReplay.OnGhostSpawned(ghostName, frames);
                // snapshot the ghost's PB NOW — if the local player beats it this run, PBStore gets
                // overwritten with the faster new time before the ghost finishes, and reading it at
                // fallfeed time would stamp the ghost with our new PB instead of its own.
                float ghostPb = PBStore.TryGet(cacheId, type, out float _pb, out _) ? _pb : frames[frames.Count - 1].Item1;
                BetterFGUIMan.Instance.StartCoroutine(GhostPlayback(ghostGo, ghostAnim, frames, ghostName, ghostPb, gen).WrapToIl2Cpp());
                Plugin.Log.LogInfo($"Ghost: spawned for {cacheId} [{type}]");
            }
        }

        static void RegisterGhostNametag(string ghostName)
        {
            if (SettingsService.Get("nametag.enabled", "false") != "true") return;
            if (SettingsService.Get("nametag.ghost.enabled", "false") != "true") return;

            var profile = BetterFG.Customization.Player.BfgProfile.FromLocal();
            if (profile.nametag == null) return;

            BetterFG.Network.RemoteProfileStore.Register(profile, ghostName);
        }

        static IEnumerator GhostDressCoroutine(GameObject ghostGo, int gen)
        {
            yield return ApplyGhostSkinThenMatCoroutine(ghostGo, gen);
            BetterFG.Features.Replay.FeatureReplay.EndGhostSpawn();
        }

        static IEnumerator ApplyGhostSkinThenMatCoroutine(GameObject ghostGo, int gen)
        {
            var svc = SkinApplicationService.Instance;
            bool replacesBean = false;
            if (svc != null)
            {
                // use the same slot object references GetActiveSlots returns — they're in activeSlots,
                // so SlotDead's Contains check passes and applyStamp matches
                var slots = svc.GetActiveSlots();
                foreach (var slot in slots)
                {
                    if (_ghostGen != gen || ghostGo == null) yield break;
                    if (slot?.bundle == null) continue;
                    // ONLY a full-bean Costume replaces the fall guy. items + accessories are
                    // attachments that sit ON the bean, and a costume with keepBase keeps it too.
                    // treating an item as bean-replacing was nuking the whole body in the ghost,
                    // leaving just the item floating. respect type AND keepBase.
                    if (slot.skinInfo != null && slot.type == SkinType.Costume && !slot.skinInfo.keepBase)
                        replacesBean = true;
                    yield return svc.ApplySkinToBean(slot, ghostGo).WrapToIl2Cpp();
                }
                if (!replacesBean)
                    yield return svc.ApplyActiveGameCosmeticsToBeanCoroutine(ghostGo).WrapToIl2Cpp();
            }
            if (_ghostGen != gen || ghostGo == null) yield break;
            // skin coroutines may have applied their own scale — override with your exact resolved size
            // via the same wrapper mechanism you use, so the ghost matches you.
            PlayerScaleService.ApplyGhostScale(ghostGo);

            if (replacesBean)
            {
                foreach (var p in ghostGo.GetComponentsInChildren<CostumePollerComponent>(true))
                    UnityEngine.Object.Destroy(p);
                foreach (var t in ghostGo.GetComponentsInChildren<Transform>(true))
                    if (t != null && t.name.StartsWith("Body_LOD")) t.gameObject.SetActive(false);
            }

            // a ghost is never in the invisibeans/powerup renderer lists, so the sync components the
            // skin apply hung on it have nothing to follow — and leaving them registered would hold
            // the invisibility patches in the game for no reason.
            foreach (var sync in ghostGo.GetComponentsInChildren<InvisibilitySyncComponent>(true))
                UnityEngine.Object.Destroy(sync);

            var mat = AssetManager.GhostMaterial;
            if (mat == null) yield break;
            foreach (var smr in ghostGo.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = smr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                smr.sharedMaterials = mats;
                // a translucent ghost casting a solid shadow looked wrong anyway, and dropping it
                // takes the whole bean out of every shadow cascade. Bone2 halves the skinning weights
                // — on a fall guy that's not a difference you can see.
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                smr.receiveShadows = false;
                smr.quality = SkinQuality.Bone2;
                smr.skinnedMotionVectors = false;
            }
            foreach (var mr in ghostGo.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = mr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                mr.sharedMaterials = mats;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }

        static IEnumerator GhostPlayback(GameObject ghostGo, Animator ghostAnim, List<(float t, Vector3 pos, Quaternion rot, int stateHash, float animTime)> frames, string ghostName, float ghostPb, int gen)
        {
            int idx = 0;
            bool finished = false;
            int lastState = 0;
            int driftFrames = 0;
            int drivenIdx = -1;
            bool hasAnim = ghostAnim != null;
            bool timeAttack = IsTimeAttackRound();
            var ghostTf = ghostGo.transform;
            // a time attack ghost never runs out of laps, so it isn't allowed to fall out of the loop
            while (ghostGo.m_CachedPtr != IntPtr.Zero && _ghostGen == gen && (timeAttack || idx < frames.Count))
            {
                float elapsed = GhostClock();
                // beating your PB mid-round rewrites this list under us, so never trust the old index
                if (idx >= frames.Count) { idx = 0; drivenIdx = -1; }
                // between attempts the ghost waits on the start line for the next lap to begin
                if (elapsed < 0f)
                {
                    idx = 0;
                    lastState = 0;
                    drivenIdx = -1;
                    finished = false;
                    ghostTf.SetPositionAndRotation(frames[0].pos, frames[0].rot);
                    yield return null;
                    continue;
                }
                if (elapsed < frames[idx].t) { idx = 0; drivenIdx = -1; }
                while (idx + 1 < frames.Count && frames[idx + 1].t <= elapsed)
                    idx++;
                if (idx + 1 < frames.Count)
                {
                    float den = frames[idx + 1].t - frames[idx].t;
                    float frac = den > 0f ? Mathf.Clamp01((elapsed - frames[idx].t) / den) : 0f;
                    ghostTf.SetPositionAndRotation(
                        Vector3.Lerp(frames[idx].pos, frames[idx + 1].pos, frac),
                        Quaternion.Slerp(frames[idx].rot, frames[idx + 1].rot, frac));
                }
                else
                {
                    ghostTf.SetPositionAndRotation(frames[idx].pos, frames[idx].rot);
                    // hit the last frame with live elapsed already past it — ghost has "qualified".
                    // in time attack it sticks around instead: the run isn't over, you just go again.
                    if (elapsed >= frames[idx].t)
                    {
                        if (!timeAttack) { finished = true; break; }
                        if (!finished) { finished = true; FireGhostQualifyFallFeed(ghostName, ghostPb); }
                        yield return null;
                        continue;
                    }
                }

                // Play on a recorded state change, and re-assert if the graph drifts off and stays off.
                // the 4-frame delay avoids stomping short transitions, which restarts the slide clip
                if (hasAnim && frames[idx].stateHash != lastState)
                {
                    lastState = frames[idx].stateHash;
                    ghostAnim.Play(lastState, 0, 0f);
                    driftFrames = 0;
                    drivenIdx = -1;
                }

                // everything below is constant across a keyframe segment: the ghost's velocity comes
                // straight out of the two frames bracketing it, so at 240fps this was writing the same
                // six animator params twelve times over, plus a SphereCast and a boxed state-info read.
                // it all rides the 20Hz recording boundary now, which is the rate the data changes at.
                if (hasAnim && idx != drivenIdx)
                {
                    drivenIdx = idx;

                    if (ghostAnim.GetCurrentAnimatorStateInfo(0).shortNameHash != lastState)
                    {
                        if (++driftFrames >= 4) { ghostAnim.Play(lastState, 0, 0f); driftFrames = 0; }
                    }
                    else driftFrames = 0;

                    Vector3 v = Vector3.zero;
                    if (idx + 1 < frames.Count)
                    {
                        float den = frames[idx + 1].t - frames[idx].t;
                        if (den > 0f) v = (frames[idx + 1].pos - frames[idx].pos) / den;
                    }
                    bool grounded = BeanAnimationUtil.CheckGrounded(ghostTf, out float slopeAngle);
                    BeanAnimationUtil.DriveLocomotion(ghostAnim, ghostTf, v, grounded, slopeAngle);
                }
                yield return null;
            }
            // ghost crossed the line — stamp the fallfeed with the PB snapshotted at spawn time.
            // reading PBStore here would return the new (faster) time if the local player just beat it.
            // time attack already fires its own feed per lap inside the loop.
            if (finished && !timeAttack)
                FireGhostQualifyFallFeed(ghostName, ghostPb);
            if (ghostGo != null && _ghostGos.Contains(ghostGo))
            {
                _ghostGos.Remove(ghostGo);
                UnityEngine.Object.Destroy(ghostGo);
            }
        }

        // spawn a fallfeed for a ghost that just finished its playback. bakes the PB time straight
        // into the message when the qual-time tweak is on (its own postfix would no-op on this feed
        // because FeatureTimePlacement has no server qualifyTime for the ghost's fake player key).
        //
        // disabled after the FallFeed rework: constructing FallFeedManager.PlayerSlot/FallFeedMessageData
        // from managed code with `new` crashed the whole game (no managed exception, log just stops) —
        // Cecil shows PlayerSlot has no constructor at all, so this is an unverified native-boundary
        // allocation. skip firing the toast until a safe construction path is confirmed live.
        static void FireGhostQualifyFallFeed(string ghostName, float pbSeconds)
        {
            try
            {
                var mgr = UnityEngine.Object.FindObjectOfType<FGClient.FallFeed.FallFeedManager>();
                var container = mgr?._fallFeedContainer;
                if (container == null) return;

                Plugin.Log.LogInfo("Ghost fallfeed skipped (PlayerSlot construction unverified post-update): " + ghostName);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Ghost fallfeed failed: " + ex.Message); }
        }

        static void TryShowLocalQualifiedFromServerProgress(ClientGameManager cgm, GameMessageServerPlayerProgress msg)
        {
            if (cgm == null || msg == null) return;
            if (!msg.succeeded || msg.isSkipping) return;
            if (!cgm.IsMyLocalPlayer(msg.playerId)) return;
            // time attack "qualifies" everyone off their ranking when the round ends, so this fires
            // with the whole round's elapsed. the real per-run time comes through RegisterTime.
            if (!IsRaceRound() || IsTimeAttackRound()) return;

            float elapsed = RaceElapsed();

            if (elapsed <= 0f && msg.qualifyTime > 0)
                elapsed = msg.qualifyTime > 1000f ? msg.qualifyTime / 1000f : msg.qualifyTime;

            Plugin.Log.LogInfo("QualTime: server progress fired, elapsed=" + elapsed);
            ShowQualificationTime(elapsed);
        }

        // called from the TimeAttackScoreManager.RegisterTime postfix that the leaderboard already
        // owns. every lap state change for every player lands here; ours is the only one that matters,
        // and only once the lap actually reads Finished — that's the moment the time is real.
        public static void OnTimeAttackTimeRegistered(uint remoteId, TimeAttackLapState lapState, ushort lapIndex)
        {
            if (!feature.enabled || !IsTimeAttackRound()) return;

            ClientGameManager cgm;
            var gsv = GlobalGameStateClient.Instance?.GameStateView;
            if (gsv == null || !gsv.GetLiveClientGameManager(out cgm) || cgm == null) return;
            var tsm = cgm._timeAttackScoreManager;
            if (tsm == null || tsm.LocalPlayer != remoteId) return;

            if (lapState != TimeAttackLapState.Finished)
            {
                Plugin.Log.LogInfo($"time attack lap {lapIndex} -> {lapState}");
                return;
            }

            var stats = tsm._timeAttackManager?.GetPlayerStats(remoteId);
            var laps = stats?.GetLapTimes;
            if (laps == null || laps.Length == 0) { Plugin.Log.LogWarning("time attack finished but no lap times came back?"); return; }

            // the lap the message is about, or the newest positive one if the index is off the end
            float time = lapIndex < laps.Length ? laps[lapIndex] : 0f;
            if (time <= 0f)
                for (int i = laps.Length - 1; i >= 0; i--) if (laps[i] > 0f) { time = laps[i]; break; }
            if (time <= 0f) { Plugin.Log.LogWarning($"time attack lap {lapIndex} finished with no time in {laps.Length} slots"); return; }

            if (!TryGetLiveRoundIds(out string cacheId, out string roundName, out bool isUgc))
            { Plugin.Log.LogWarning("time attack time registered but the round has no id, dropping it"); return; }

            if (string.IsNullOrEmpty(roundName))
            { Plugin.Log.LogWarning("time attack time registered but the round has no display name, dropping it"); return; }

            bool isPb = On("store") && PBStore.TrySet(cacheId, roundName, PbType.TimeAttack, time, isUgc);
            Plugin.Log.LogInfo($"time attack lap {lapIndex} on {roundName}: {time:F3}s{(isPb ? " — new PB" : "")}");
            RefreshTimeAttackPbLabel();
            if (!isPb) return;

            AudioService.PlayPB();
            if (!On("ghost")) return;
            SaveGhost(cacheId, PbType.TimeAttack);
            RaceTheNewTimeAttackGhost();
        }

        // your new PB should be what you race on the next lap, not the one you turned up with. the
        // playback coroutine re-reads its list every frame, so rewriting it in place is the whole swap.
        // nothing to swap into on your first ever time here — spawn the ghost we just wrote instead.
        static void RaceTheNewTimeAttackGhost()
        {
            if (_ghostFrames == null || _ghostFrames.Count == 0) return;
            if (_taLiveGhostFrames == null)
            {
                Plugin.Log.LogInfo("first time attack ghost for this level, bringing it out now");
                BetterFGUIMan.Instance.StartCoroutine(SpawnGhostDeferred(_ghostGen).WrapToIl2Cpp());
                return;
            }
            _taLiveGhostFrames.Clear();
            foreach (var f in _ghostFrames) _taLiveGhostFrames.Add(f);
            Plugin.Log.LogInfo($"ghost swapped onto your new run, {_taLiveGhostFrames.Count} frames");
        }

        // called from the shared ClientGameManager.Shutdown hub in UnityRoundPatches.
        public static void OnClientGameManagerShutdown() => _ghostRecording = false;

        // called from the shared HandleServerPlayerProgress hub in GameStatePatches.
        public static void OnServerPlayerProgress(ClientGameManager cgm, GameMessageServerPlayerProgress progressMessage)
        {
            TryShowLocalQualifiedFromServerProgress(cgm, progressMessage);
        }

        // ── Load-screen PB label ──────────────────────────────────────────────
        // Entry hooks (splash cache patch, share-code patch, LoadingScreenViewModel.UpdateDisplay)
        // all call OnLoadingScreenUpdateDisplay, which fires off SpawnPBLoadLabelCoroutine.
        // Dedupe is by checking the canvas for an already-spawned BettrFG_PBLoadLabel each tick —
        // multiple fires race to find the canvas; first one wins, rest no-op.

        const string PBLoadLabelName = "BettrFG_PBLoadLabel";
        const string LoadingScreenPath = "UICanvas_Client_V2(Clone)/LoadingScreen";

        public static void OnLoadingScreenUpdateDisplay()
        {
            if (!On("loadscreen") || !On("store")) return;
            BetterFGUIMan.Instance.StartCoroutine(SpawnPBLoadLabelCoroutine().WrapToIl2Cpp());
        }

        struct PbLoadInfo
        {
            public string cacheId;
            public PbType type;
            public float pb;
            public bool found;
            public bool isRaceRound;
        }

        // poll the live ClientGameManager for round id/name + squad size + race-round flag. returns
        // true once we have a usable round id AND a definite race-round answer (the qual-screen and
        // live-timer paths use the same gate, so we mirror it here).
        static IEnumerator ResolveLoadScreenPbInfo(System.Action<PbLoadInfo?> result)
        {
            string roundId = null;
            string roundName = null;
            PbType type = PbType.Solos;
            bool? isRace = null;
            float waited = 0f;
            while (waited < 8f)
            {
                try
                {
                    ClientGameManager cgm;
                    var gsv = GlobalGameStateClient.Instance?.GameStateView;
                    if (gsv != null && gsv.GetLiveClientGameManager(out cgm) && cgm != null)
                    {
                        if (cgm._round != null)
                        {
                            roundId = cgm._round.Id;
                            roundName = cgm._round.DisplayNameUnindented;
                        }
                        int sz = (int)cgm.SquadSize;
                        type = sz <= 1 ? PbType.Solos : (sz == 2 ? PbType.Duos : PbType.Squads);
                        if (cgm.GameRules != null)
                        {
                            isRace = cgm.GameRules.IsRaceRound || cgm.GameRules.IsTimeAttackGameMode;
                            if (cgm.GameRules.IsTimeAttackGameMode) type = PbType.TimeAttack;
                        }
                    }
                }
                catch (Exception ex) { Plugin.Log.LogWarning("QualTime: loadscreen cgm lookup failed: " + ex.Message); }

                if (!string.IsNullOrEmpty(roundId) && isRace.HasValue) break;
                yield return new WaitForSeconds(0.1f);
                waited += 0.1f;
            }

            if (string.IsNullOrEmpty(roundId)) roundId = _roundIdCache;
            if (string.IsNullOrEmpty(roundName)) roundName = _roundNameCache;
            if (string.IsNullOrEmpty(roundId)) { result(null); yield break; }

            roundId = PBStore.CanonicalRoundId(roundId);
            bool isUgc = roundId.StartsWith("ugc-");
            string cacheId = (!isUgc && !string.IsNullOrEmpty(roundName)) ? roundName : roundId;

            float pb = 0f;
            bool found = PBStore.TryGet(cacheId, type, out pb, out _, roundName);
            if (!found && !string.IsNullOrEmpty(roundName) && roundName != cacheId)
                found = PBStore.TryGet(roundName, type, out pb, out _, null);

            result(new PbLoadInfo { cacheId = cacheId, type = type, pb = pb, found = found, isRaceRound = isRace == true });
        }

        static string FormatPbText(bool found, float pb)
        {
            if (!found) return "PB --:--:---";
            var t = TimeSpan.FromSeconds(pb);
            return string.Format("PB  {0:D2}:{1:D2}:{2:D3}", t.Minutes, t.Seconds, t.Milliseconds);
        }

        static IEnumerator SpawnPBLoadLabelCoroutine()
        {
            // wait for the loading-screen canvas to appear
            Transform canvas = null;
            float waited = 0f;
            while (waited < 15f)
            {
                var loading = GameObject.Find(LoadingScreenPath);
                if (loading != null && loading.activeInHierarchy)
                {
                    for (int i = 0; i < loading.transform.childCount; i++)
                    {
                        var c = loading.transform.GetChild(i);
                        if (c == null || !c.gameObject.activeInHierarchy) continue;
                        if (c.GetComponent<RectTransform>() == null) continue;
                        canvas = c;
                        break;
                    }
                    if (canvas != null) break;
                }
                yield return new WaitForSeconds(0.1f);
                waited += 0.1f;
            }
            if (canvas == null || canvas.Find(PBLoadLabelName) != null) yield break;

            // resolve PB info (round id, show type, race-round flag, stored pb)
            PbLoadInfo? infoBox = null;
            yield return BetterFGUIMan.Instance.StartCoroutine(
                ResolveLoadScreenPbInfo(r => infoBox = r).WrapToIl2Cpp());
            if (!infoBox.HasValue) yield break;
            var info = infoBox.Value;
            if (!info.isRaceRound) yield break; // matches the PB-store gate elsewhere

            // re-pick canvas right before spawning: the one we polled with could have been torn down
            // during the cgm-wait. also dodges IL2Cpp throwing NRE on GetComponent against a dead
            // transform later in the spawn block.
            var spawnLoading = GameObject.Find(LoadingScreenPath);
            if (spawnLoading == null || !spawnLoading.activeInHierarchy) yield break;
            Transform spawnCanvas = null;
            for (int i = 0; i < spawnLoading.transform.childCount; i++)
            {
                var c = spawnLoading.transform.GetChild(i);
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                if (c.GetComponent<RectTransform>() == null) continue;
                spawnCanvas = c;
                break;
            }
            if (spawnCanvas == null || spawnCanvas.Find(PBLoadLabelName) != null) yield break;
            var safeArea = spawnCanvas.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "SafeArea") ?? spawnCanvas;

            string text = FormatPbText(info.found, info.pb);
            var labelTmp = SpawnPBLoadPanel(spawnCanvas, safeArea, text);

            // wait a frame before writing the PB text. the deleted TMPTextBinding's final Update()
            // still fires once after DestroyImmediate (Unity defers teardown to end-of-frame), so
            // setting text inline gets clobbered back to the round name. setting next frame races
            // past the binding's last tick.
            yield return null;
            if (labelTmp != null) UGUIShip.RelabelText(labelTmp, text);

            Plugin.Log.LogInfo("QualTime: loadscreen PB " + text + " (" + info.cacheId + " " + info.type + ")");
        }

        // clones RoundName_Panel as our PB backdrop, re-anchored to the top-right of SafeArea,
        // mirrored horizontally. uses the panel's own RoundName_Text as the PB label (after
        // stripping its bindings/animators/etc), so the text inherits the same TMP styling /
        // material the game's round-name uses. returns the TMP so the caller can pin the text
        // a frame later — the binding's Update() can fire one more time after we DestroyImmediate
        // it (Unity defers actual component teardown to end-of-frame), which would otherwise
        // overwrite our text back to the round name. doing the set next frame races past that.
        static TMPro.TextMeshProUGUI SpawnPBLoadPanel(Transform spawnCanvas, Transform safeArea, string text)
        {
            var src = spawnCanvas.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "RoundName_Panel");
            if (src == null) return null;

            var go = UnityEngine.Object.Instantiate(src.gameObject, safeArea);
            go.name = "BettrFG_PBLoadPanel";

            // strip auto-sizing/auto-layout off the cloned root so its rect stays at our fixed size
            foreach (var c in go.GetComponents<UnityEngine.UI.ContentSizeFitter>()) UnityEngine.Object.Destroy(c);
            foreach (var c in go.GetComponents<UnityEngine.UI.LayoutGroup>()) UnityEngine.Object.Destroy(c);

            // anchor the panel itself to the top-right of SafeArea with zero offset. position +
            // scale are applied to the inner BettrFG_PBLoadContent child instead so we can tweak
            // those independently of the panel's pinning.
            var goRt = go.GetComponent<RectTransform>();
            goRt.anchorMin = new Vector2(1f, 1f);
            goRt.anchorMax = new Vector2(1f, 1f);
            goRt.pivot = new Vector2(1f, 1f);
            goRt.anchoredPosition = Vector2.zero;
            goRt.localScale = Vector3.one;

            // create a single content container as a child of the panel and re-parent every
            // existing child into it. that way one localPosition/localScale on the container
            // moves the curved sprite + the PB label together.
            var contentGo = new GameObject("BettrFG_PBLoadContent");
            var contentRt = contentGo.AddComponent<RectTransform>();
            contentRt.SetParent(go.transform, false);
            contentRt.anchorMin = new Vector2(1f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(1f, 1f);
            contentRt.anchoredPosition = new Vector2(500f, 0f);
            contentRt.localScale = Vector3.one * 0.6f;
            BetterFGUIMan.Instance.StartCoroutine(PBLoadContentPopIn(contentRt).WrapToIl2Cpp());

            // move every original child (Panel, RoundName_Text, decorations) under the content
            // container. iterate by index but reparent from the front so the child count we walk
            // doesn't include the container itself (which is currently the last child).
            int originalChildCount = go.transform.childCount - 1; // -1 to skip the container we just made
            for (int i = 0; i < originalChildCount; i++)
                go.transform.GetChild(0).SetParent(contentRt, false);

            // disable every direct child of the container except Panel and RoundName_Text - strips
            // out the round-name icons/glyphs/bindings, leaving just the curved sprite + the text.
            for (int i = 0; i < contentRt.childCount; i++)
            {
                var child = contentRt.GetChild(i);
                if (child.name == "Panel")
                {
                    child.localScale = new Vector3(-1f, 1.2f, 1f);
                    child.localPosition = new Vector3(-220.7273f, -98.8166f, 0f);
                    var outline = child.FindChild("BottomPanel_Outline");
                    if (outline != null) outline.gameObject.SetActive(false);
                    //child.gameObject.SetActive(false);
                    continue;
                }
                if (child.name == "RoundName_Text") continue;
                if (child.parent.name == "Panel") continue;
                child.gameObject.SetActive(false);
            }

            // find the cloned RoundName_Text and re-instantiate it as our label so peer
            // animators/bindings under the panel don't fire against a stripped peer (which
            // NRE'd every frame when we mutated in-place).
            var origText = contentRt.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "RoundName_Text");
            TMPro.TextMeshProUGUI tmp = null;
            if (origText != null)
            {
                var labelGo = UnityEngine.Object.Instantiate(origText.gameObject, contentRt);
                labelGo.name = PBLoadLabelName;
                origText.gameObject.SetActive(false);


                // kill the TMPTextBinding on the clone or it'll re-bind to the round name and
                // overwrite our PB text. DestroyImmediate so the binding's Update() can't fire
                // one more time before it's actually gone (which is what Destroy lets happen).
                foreach (var b in labelGo.GetComponents<Mediatonic.Tools.MVVM.TMPTextBinding>())
                    UnityEngine.Object.DestroyImmediate(b);

                tmp = labelGo.GetComponent<TMPro.TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.alignment = TMPro.TextAlignmentOptions.Center;
                    tmp.enableWordWrapping = false;
                }
                labelGo.transform.localPosition = new Vector3(-414.0297f, -100.7729f, 0f);
                labelGo.transform.localScale = new Vector3(1.42f, 1.42f, 1.42f);
                labelGo.SetActive(true);
            }

            go.SetActive(true);
            return tmp;
        }

        // x: 500 -> 0 -> 20, total 0.6s. ease-out cubic on the slide-in, quick settle back.
        static IEnumerator PBLoadContentPopIn(RectTransform rt)
        {
            const float slideDur = 0.45f;
            const float settleDur = 0.15f;
            float t = 0f;
            while (t < slideDur)
            {
                if (rt == null) yield break;
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / slideDur);
                float eased = 1f - Mathf.Pow(1f - u, 3f); // ease-out cubic
                rt.anchoredPosition = new Vector2(Mathf.Lerp(500f, 0f, eased), rt.anchoredPosition.y);
                yield return null;
            }
            t = 0f;
            while (t < settleDur)
            {
                if (rt == null) yield break;
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / settleDur);
                float eased = u * u * (3f - 2f * u); // smoothstep
                rt.anchoredPosition = new Vector2(Mathf.Lerp(0f, 20f, eased), rt.anchoredPosition.y);
                yield return null;
            }
            if (rt != null) rt.anchoredPosition = new Vector2(20f, rt.anchoredPosition.y);
        }

        // called from the shared RoundLoader.CleanupLoadingScreens hub in GameStatePatches.
        public static void OnCleanupLoadingScreens()
        {
            ResetRaceRoundCache();
            _elapsedBaseline = 0f;
            _qualHandled = false;
            _bannerReady = false;
            _openBanner = null;
            _ghostRecording = false;
            _ghostFrames = null;
            foreach (var g in _ghostGos) if (g != null) UnityEngine.Object.Destroy(g);
            _ghostGos.Clear();
            if (On("ghost"))
            {
                int gen = ++_ghostGen;
                BetterFGUIMan.Instance.StartCoroutine(SpawnGhostDeferred(gen).WrapToIl2Cpp());
                _ghostFrames = new List<(float, Vector3, Quaternion, int, float)>();
                _ghostRecording = true;
                BetterFGUIMan.Instance.StartCoroutine(GhostRecordCoroutine(gen).WrapToIl2Cpp());
            }
            _roundIdCache = null;
            _roundNameCache = null;
            BetterFGUIMan.Instance.StartCoroutine(SpawnLiveTimerDeferred().WrapToIl2Cpp());
        }

        [HarmonyPatch(typeof(RoundLoader), "ShowLoadingGameScreenForSelectedRound")]
        public class Patch_RoundLoaderSplashCache
        {
            [HarmonyPostfix]
            public static void Postfix(RoundLoader __instance)
            {
                var round = __instance.Round;
                string roundName = round?.DisplayNameUnindented;

                if (!string.IsNullOrEmpty(round?.Id))
                {
                    _roundIdCache = PBStore.CanonicalRoundId(round.Id);
                    _roundNameCache = roundName;
                }

                Plugin.Log.LogInfo($"loading screen up for {roundName ?? "a round the loader won't name"}, id {_roundIdCache ?? "none"}");

                BetterFGUIMan.Instance.StartCoroutine(SplashCache.TryCacheSplashForCurrentRound(_roundIdCache, roundName).WrapToIl2Cpp());
                OnLoadingScreenUpdateDisplay();
                BetterFG.Services.DiscordPresenceService.OnLoadingRound(round);
                BetterFG.Services.DiscordPresenceService.Push();
            }
        }

        // called from the shared RoundLoader.LoadViaShareCodeAndVersion hub in GameStatePatches.
        public static void OnLoadViaShareCodeAndVersion(Round round)
        {
            if (round != null)
            {
                _roundIdCache = PBStore.CanonicalRoundId(round.Id);
                _roundNameCache = round.DisplayNameUnindented;
            }
            BetterFGUIMan.Instance.StartCoroutine(SplashCache.TryCacheSplashForCurrentRound(_roundIdCache, round?.DisplayNameUnindented).WrapToIl2Cpp());
            OnLoadingScreenUpdateDisplay();
        }
    }

    internal static class SplashCache
    {
        static string CacheDir
        {
            get
            {
                // same folder as the dll
                string dllDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                return Path.Combine(dllDir, "CachedRoundSplashScreens");
            }
        }

        public static string GetCachePath(string roundId)
        {
            if (string.IsNullOrEmpty(roundId)) return null;
            roundId = PBStore.CanonicalRoundId(roundId);
            string safe = string.Concat(roundId.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(CacheDir, safe + ".jpg");
        }

        public static bool HasCached(string roundId) => File.Exists(GetCachePath(roundId));

        public static void TryRename(string oldId, string newId)
        {
            try
            {
                string oldPath = GetCachePath(oldId);
                string newPath = GetCachePath(newId);
                if (File.Exists(oldPath) && !File.Exists(newPath))
                    File.Move(oldPath, newPath);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"SplashCache: rename failed: {ex.Message}"); }
        }

        // keep loaded textures in memory so callers that rebuild their UI a lot (the PB tab re-renders
        // its row list on every sort/filter/subtab change) don't re-read the file + alloc a new texture
        // every time. without this each render churns ~25 disk reads + 25 Texture2D allocs.
        static readonly Dictionary<string, Texture2D> _texCache = new Dictionary<string, Texture2D>();

        public static Texture2D LoadCached(string roundId, string displayName = null)
        {
            roundId = PBStore.CanonicalRoundId(roundId);
            bool isUgc = !string.IsNullOrEmpty(roundId) && roundId.StartsWith("ugc-");
            string cacheKey = (!isUgc && !string.IsNullOrEmpty(displayName)) ? displayName : roundId;
            if (_texCache.TryGetValue(cacheKey, out var cached) && cached != null) return cached;

            string path = GetCachePath(cacheKey);
            if (!File.Exists(path)) return null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (tex.LoadImage(bytes)) { _texCache[cacheKey] = tex; return tex; }
                UnityEngine.Object.Destroy(tex);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"SplashCache: load failed for {roundId}: {ex.Message}"); }
            return null;
        }

        public static IEnumerator TryCacheSplashForCurrentRound(string roundIdHint = null, string roundNameHint = null)
        {
            yield return new WaitForSeconds(0.5f);

            // poll until the sprite texture is actually loaded, or the loading screen is gone
            Texture2D srcTex = null;
            float waited = 0f;
            while (waited < 6f)
            {
                var imgGo = GameObject.Find(
                    "UICanvas_Client_V2(Clone)/LoadingScreen/Prime_UI_RoundSelected_UP_Prefab_Canvas(Clone)/SafeArea/SelectedShow/ShowMask/ShowImage")
                    ?? GameObject.Find(
                    "UICanvas_Client_V2(Clone)/LoadingScreen/Prime_UI_RoundSelected_UGC_Prefab_Canvas(Clone)/SafeArea/SelectedShow/ShowMask/ShowImage")
                    ?? GameObject.Find(
                    "UICanvas_Client_V2(Clone)/LoadingScreen/Prime_UI_RoundSelected_Prefab_Canvas(Clone)/SafeArea/SelectedShow/ShowMask/ShowImage");
                if (imgGo != null)
                {
                    var _img = imgGo.GetComponent<Image>();
                    if (_img != null && _img.sprite != null && _img.sprite.texture != null)
                    {
                        srcTex = _img.sprite.texture;
                        break;
                    }
                }
                yield return new WaitForSeconds(0.2f);
                waited += 0.2f;
            }

            if (srcTex == null) { Plugin.Log.LogWarning("SplashCache: sprite never loaded in 6s, bailing"); yield break; }

            string roundId = roundIdHint;
            int lookupTries = 0;
            while (string.IsNullOrEmpty(roundId) && lookupTries < 3)
            {
                lookupTries++;
                try
                {
                    roundId = GlobalGameStateClient.Instance?.GameStateView?.CurrentGameLevelName;
                    if (string.IsNullOrEmpty(roundNameHint))
                    {
                        ClientGameManager cgm;
                        var gsv = GlobalGameStateClient.Instance?.GameStateView;
                        if (gsv != null && gsv.GetLiveClientGameManager(out cgm) && cgm?._round != null)
                        {
                            if (string.IsNullOrEmpty(roundId)) roundId = cgm._round.Id;
                            roundNameHint = cgm._round.DisplayNameUnindented;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"SplashCache: round lookup try {lookupTries} failed: {ex.Message}");
                }

                if (string.IsNullOrEmpty(roundId))
                    yield return new WaitForSeconds(0.5f);
            }
            if (string.IsNullOrEmpty(roundId)) { Plugin.Log.LogWarning("SplashCache: no roundId, bailing"); yield break; }
            roundId = PBStore.CanonicalRoundId(roundId);

            bool isUgc = roundId.StartsWith("ugc-");
            string cacheKey = roundId;
            if (!isUgc)
            {
                string dn = roundNameHint;
                if (!string.IsNullOrEmpty(dn)) cacheKey = dn;
            }

            if (HasCached(cacheKey)) { Plugin.Log.LogInfo($"SplashCache: already cached {cacheKey}"); yield break; }

            try
            {
                var rt = RenderTexture.GetTemporary(srcTex.width, srcTex.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(srcTex, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;

                var readable = new Texture2D(srcTex.width, srcTex.height, TextureFormat.RGB24, false);
                readable.ReadPixels(new Rect(0, 0, srcTex.width, srcTex.height), 0, 0);
                readable.Apply();

                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);

                byte[] jpg = readable.EncodeToJPG(62);
                UnityEngine.Object.Destroy(readable);

                Directory.CreateDirectory(CacheDir);
                string path = GetCachePath(cacheKey);
                File.WriteAllBytes(path, jpg);
                Plugin.Log.LogInfo($"SplashCache: saved {cacheKey} -> {jpg.Length / 1024}kb");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"SplashCache: exception: {ex.Message}");
            }
        }
    }
}
