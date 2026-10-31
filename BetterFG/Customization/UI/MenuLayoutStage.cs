using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;

namespace BetterFG.Customization.UI
{
    public static class MenuLayoutStage
    {
        private const string MENU_UI_CONFIG = "Data/UIConfig_MainMenu_Shared.asset";
        private const string BUILDER = "MainMenuBuilder";
        private const string TOPBAR = "Topbar_Prime(Clone)";
        private const int BACKDROP_ORDER = 4000;
        private const int OUTLINE_ORDER = 4300;
        private const float FRAME_SCALE = 0.78f;
        private const float FRAME_MARGIN = 24f;
        private const float OUTLINE_PAD = 6f;
        private const string TEX = "BetterFG.assets.ui.general.";
        private static readonly Color BACK_COL = new Color(0.17f, 0.2f, 0.26f, 1f);
        private static readonly Color GRADIENT_COL = new Color(0.33f, 0.37f, 0.45f, 0.69f);

        public static GameObject Host { get; private set; }

        public static RectTransform Build(MenuLayoutService.Kind kind)
        {
            Teardown();

            var canvasGo = GameObject.Find("UICanvas_Client_V2(Clone)");
            if (canvasGo == null) return null;
            if (kind.Source != null) return BuildLoading(kind, canvasGo.transform);
            var def = canvasGo.transform.Find("Default");

            var srt = MakeStage(canvasGo.transform);

            var known = new HashSet<int>();
            if (def != null)
                for (int i = 0; i < def.childCount; i++) known.Add(def.GetChild(i).GetInstanceID());

            var prefab = BuilderPrefab();
            if (prefab == null)
            {
                Plugin.Log.LogWarning("layout stage: addressables gave back no MainMenuBuilder prefab, nothing to edit");
                Teardown();
                return null;
            }

            var seen = new HashSet<int>();
            foreach (var t in canvasGo.transform.GetComponentsInChildren<Transform>(true)) seen.Add(t.GetInstanceID());

            var clone = UnityEngine.Object.Instantiate(prefab, srt, false);
            clone.name = "MainMenuBuilder(Clone)";

            var screenPrefab = MenuScreenPrefab(clone);
            StripLiveWiring(clone);
            srt.gameObject.SetActive(true);

            if (screenPrefab != null)
            {
                var screenParent = clone.transform.Find("MainScreensParent/Menu_Screen_Main");
                if (screenParent != null)
                {
                    var screen = UnityEngine.Object.Instantiate(screenPrefab, screenParent, false);
                    screen.name = "Prime_UI_MainMenu_Canvas(Clone)";
                }
            }
            else
            {
                Plugin.Log.LogWarning("layout stage: no MainMenuPrefab on the builder config, the stage will be topbar only");
            }

            AdoptTopbar(def, srt, known);
            MirrorLive(srt, kind);
            MirrorMoved(srt, kind);
            Sweep(canvasGo.transform, Host.transform, seen);
            SoloScreen(srt);
            Reveal(srt);
            StripBehaviours(srt);
            ShowSlots(srt, kind);
            Raise(srt);
            return srt;
        }

        private static RectTransform BuildLoading(MenuLayoutService.Kind kind, Transform canvasRoot)
        {
            GameObject source = null;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go.name == kind.Source) { source = go; break; }
            if (source == null)
            {
                Plugin.Log.LogWarning($"no {kind.Source} loaded anywhere, can't stage that loading screen");
                return null;
            }

            var srt = MakeStage(canvasRoot);
            var clone = UnityEngine.Object.Instantiate(source, srt, false);
            clone.name = "BFG_LoadingStage";
            clone.SetActive(true);

            if (kind == MenuLayoutService.Lobby)
                foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                {
                    if (go.name != MenuLayoutService.LOBBY_MM_NAME || go.scene.IsValid()) continue;
                    var mm = UnityEngine.Object.Instantiate(go, clone.transform, false);
                    mm.name = go.name;
                    mm.SetActive(true);

                    if (BetterFG.UI.Tabs.UIScalingTab.CanvasScaleEnabled)
                    {
                        Stretch(mm.GetComponent<RectTransform>());
                        break;
                    }

                    CanvasScaler scaler = null;
                    for (var p = go.transform; p != null && scaler == null; p = p.parent) scaler = p.GetComponent<CanvasScaler>();
                    var screen = new Vector2(UnityEngine.Screen.width, UnityEngine.Screen.height);
                    float s = Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(screen.x / scaler.referenceResolution.x, 2f),
                        Mathf.Log(screen.y / scaler.referenceResolution.y, 2f), scaler.matchWidthOrHeight));
                    var live = screen / s;

                    var mrt = mm.GetComponent<RectTransform>();
                    mrt.anchorMin = mrt.anchorMax = mrt.pivot = new Vector2(0.5f, 0.5f);
                    mrt.anchoredPosition = Vector2.zero;
                    mrt.sizeDelta = live;
                    float fit = srt.rect.width / live.x;
                    mrt.localScale = new Vector3(fit, fit, 1f);
                    break;
                }

            var liveAlphas = new List<(CanvasGroup Group, float Alpha)>();
            foreach (var extra in kind.Extras)
            {
                var parts = extra.Split('/');
                var src = GameObject.Find(parts[0] + "(Clone)");
                bool live = src != null;
                if (!live)
                    foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                        if (go.name == parts[0] && !go.scene.IsValid() && go.transform.parent == null) { src = go; break; }
                if (src == null) continue;

                var copy = UnityEngine.Object.Instantiate(src, clone.transform, false);
                copy.name = parts[0] + "(Clone)";
                copy.SetActive(true);
                if (live)
                {
                    MenuLayoutService.MirrorTouched(src.transform, copy.transform);
                    foreach (var cg in copy.GetComponentsInChildren<CanvasGroup>(true))
                        if (cg.gameObject != copy) liveAlphas.Add((cg, cg.alpha));
                }
                if (parts.Length > 1)
                {
                    copy.transform.Find(parts[1]).SetParent(clone.transform, false);
                    UnityEngine.Object.DestroyImmediate(copy);
                }
            }
            if (kind == MenuLayoutService.Pause)
            {
                var loadingPanel = clone.transform.Find("Prefab_UI_PartyMenu_Overlay(Clone)/SafeArea/Overlay_Container/Loading_Panel_Container");
                if (loadingPanel != null) loadingPanel.gameObject.SetActive(false);
                var ivm = clone.GetComponent<FGClient.InGameMenuViewModel>();
                ivm._menuTransform.anchoredPosition = new Vector2(ivm._horizontalOffset, 0f);
            }

            foreach (var vm in clone.GetComponentsInChildren<FGClient.UI.Core.ScreenViewModel>(true))
                UnityEngine.Object.DestroyImmediate(vm);
            if (kind == MenuLayoutService.Hud) PrepHud(clone, kind);
            if (kind == MenuLayoutService.Lobby && Tweaks.LobbyCustomiserTweak.Instance.IsEnabled)
                Tweaks.LobbyCustomiserTweak.SpawnGroup(clone.transform.Find("SafeArea"), Tweaks.LobbyCustomiserTweak.LiveCustomiserRow());
            if (kind != MenuLayoutService.Hud) StripBehaviours(clone.transform);

            if (clone.GetComponent<Canvas>() == null) clone.AddComponent<Canvas>();
            var rt = clone.GetComponent<RectTransform>();
            Stretch(rt);
            if (kind == MenuLayoutService.Hud) MenuLayoutService.PlaceSpectatorCount(rt.Find("GameStates/PlayingState"));
            srt.gameObject.SetActive(true);
            Reveal(rt);
            foreach (var (group, alpha) in liveAlphas)
                if (group != null) group.alpha = alpha;
            if (kind == MenuLayoutService.Pause)
                foreach (var t in rt.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Container_Highlight") t.GetComponent<CanvasGroup>().alpha = 0f;
                    else if (t.name == "Generic_UI_GreenCheck") t.gameObject.SetActive(false);
                }

            var safe = kind == MenuLayoutService.Loading ? rt.Find("SafeArea") : null;
            var pb = safe == null ? null : Features.QualificationTime.FeatureQualificationTime.SpawnPBLoadPanel(rt, safe, null);
            if (pb != null) pb.text = Features.QualificationTime.FeatureQualificationTime.FormatPbText(true, 0f);

            Raise(rt);
            return rt;
        }

        private static void PrepHud(GameObject clone, MenuLayoutService.Kind kind)
        {
            var timer = Features.QualificationTime.FeatureQualificationTime.BuildLiveTimer(clone.transform.Find("GameStates/PlayingState"));
            if (timer != null)
            {
                timer.transform.Find("Canvas/LapTimeText").GetComponent<TMPro.TextMeshProUGUI>().text = "00:00:000";
                Features.QualificationTime.FeatureQualificationTime.AddLivePbLabel(timer, null);
            }

            var feed = clone.GetComponentInChildren<FGClient.FallFeed.FallFeedContainer>(true);
            var feedRows = clone.transform.Find("PersistentOverlayUI/PB_UI_FallFeed/SafeArea/FallFeedNotificationsContainer");
            for (int i = 0; i < 5; i++)
            {
                var row = UnityEngine.Object.Instantiate(feed._notificationPrefab.gameObject, feedRows, false);
                row.SetActive(true);
                var vm = row.GetComponent<FGClient.FallFeed.FallFeedNotificationViewModel>();
                vm._playerNameText.text = BetterFG.Services.LocalizationService.Format("layout.fallfeed.name", i + 1);
                vm._messageBodyText.text = BetterFG.Services.LocalizationService.Get("layout.fallfeed.body");
            }

            StripBehaviours(clone.transform);

            var states = clone.transform.Find("GameStates");
            for (int i = 0; i < states.childCount; i++)
            {
                var st = states.GetChild(i);
                for (int j = 0; j < st.childCount; j++) st.GetChild(j).gameObject.SetActive(false);
            }
            foreach (var layer in new[] { "PersistentUnderlayUI", "PersistentOverlayUI" })
            {
                var l = clone.transform.Find(layer);
                for (int i = 0; i < l.childCount; i++) l.GetChild(i).gameObject.SetActive(false);
            }

            for (int i = 0; i < kind.Slots.Length; i++)
            {
                var slot = kind.Slots[i];
                var rt = clone.transform.Find(slot.Path).GetComponent<RectTransform>();
                if (!slot.OneChild) { Placehold(rt); continue; }
                for (int c = 0; c < rt.childCount; c++) Placehold(rt.GetChild(c).GetComponent<RectTransform>());
            }
        }

        private static void StripBehaviours(Transform root)
        {
            foreach (var tw in root.GetComponentsInChildren<FGClient.Tweening.AppearTween>(true))
            {
                if (tw._rectTransform == null) continue;
                if (tw._useRectMovementTween) tw._rectTransform.anchoredPosition = tw._startAnchorPos;
                if (tw._useScaleTween) tw._rectTransform.localScale = tw._startLocalScale;
                if (tw._useFadeTween && tw._canvasGroup != null) tw._canvasGroup.alpha = 1f;
            }

            for (int pass = 0; pass < 2; pass++)
                foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null || mb.TryCast<Graphic>() != null) continue;
                    var ns = mb.GetIl2CppType().Namespace ?? "";
                    if (ns.StartsWith("UnityEngine", StringComparison.Ordinal) || ns.StartsWith("TMPro", StringComparison.Ordinal)) continue;
                    UnityEngine.Object.DestroyImmediate(mb);
                }
        }

        private static void Placehold(RectTransform rt)
        {
            if (rt.GetComponentsInChildren<Graphic>(true).Length > 0) return;

            var ph = new GameObject("BFG_Placeholder").AddComponent<RectTransform>();
            ph.SetParent(rt, false);
            ph.anchorMin = ph.anchorMax = new Vector2(0.5f, 0.5f);
            ph.pivot = rt.anchorMin;
            ph.sizeDelta = new Vector2(260f, 90f);
            ph.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            ph.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.18f);
        }

        private static RectTransform MakeStage(Transform canvasRoot)
        {
            var hostGo = new GameObject("BFG_LayoutStage");
            var hrt = hostGo.AddComponent<RectTransform>();
            hrt.SetParent(canvasRoot, false);
            Stretch(hrt);
            Host = hostGo;

            var bgGo = new GameObject("Backdrop");
            var brt = bgGo.AddComponent<RectTransform>();
            brt.SetParent(hrt, false);
            Stretch(brt);
            var bcv = bgGo.AddComponent<Canvas>();
            bcv.overrideSorting = true;
            bcv.sortingOrder = BACKDROP_ORDER - 10;
            bgGo.AddComponent<GraphicRaycaster>();
            bgGo.AddComponent<Image>().color = Color.black;
            ScreenBackgroundService.BuildBackdrop(brt, ScreenBackgroundService.Screen.FinalRound, 0.22f);
            Reskin(brt);

            var frameGo = new GameObject("Frame");
            var frt = frameGo.AddComponent<RectTransform>();
            frt.SetParent(hrt, false);
            Stretch(frt);
            frt.pivot = Vector2.one;
            frt.offsetMin = frt.offsetMax = new Vector2(-FRAME_MARGIN, -FRAME_MARGIN);
            frt.localScale = new Vector3(FRAME_SCALE, FRAME_SCALE, 1f);
            var fcv = frameGo.AddComponent<Canvas>();
            fcv.overrideSorting = true;
            fcv.sortingOrder = BACKDROP_ORDER;
            frameGo.AddComponent<RectMask2D>();
            frameGo.AddComponent<Image>().color = Color.black;

            var outGo = new GameObject("FrameOutline");
            var ort = outGo.AddComponent<RectTransform>();
            ort.SetParent(hrt, false);
            ort.anchorMin = new Vector2(1f - FRAME_SCALE, 1f - FRAME_SCALE);
            ort.anchorMax = Vector2.one;
            ort.offsetMin = new Vector2(-FRAME_MARGIN - OUTLINE_PAD, -FRAME_MARGIN - OUTLINE_PAD);
            ort.offsetMax = new Vector2(-FRAME_MARGIN + OUTLINE_PAD, -FRAME_MARGIN + OUTLINE_PAD);
            var ocv = outGo.AddComponent<Canvas>();
            ocv.overrideSorting = true;
            ocv.sortingOrder = OUTLINE_ORDER;
            var oImg = outGo.AddComponent<Image>();
            BettrFG.uGUI.UGUIShip.ApplyDeluxPanelOutline(oImg);
            oImg.raycastTarget = false;

            var stageGo = new GameObject("Stage");
            var srt = stageGo.AddComponent<RectTransform>();
            srt.SetParent(frt, false);
            Stretch(srt);
            stageGo.SetActive(false);
            return srt;
        }

        private static void Reskin(RectTransform backdrop)
        {
            var clone = backdrop.Find("FallForceClone");
            if (clone == null) return;

            clone.Find("Backdrop").GetComponent<Image>().color = BACK_COL;
            clone.Find("Gradient").GetComponent<Image>().color = GRADIENT_COL;
            var pattern = ScreenBackgroundService.FindCirclesChild(clone).GetComponent<Image>();
            var tex = Utilities.EmbeddedResourceandUnity.LoadTexture(TEX + "pattern_wrench.png");
            tex.wrapMode = TextureWrapMode.Repeat;
            pattern.sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect);
            pattern.pixelsPerUnitMultiplier = 1.4f;
            pattern.color = new Color(GRADIENT_COL.r, GRADIENT_COL.g, GRADIENT_COL.b, 1f);

            var fadeRt = new GameObject("TopFade").AddComponent<RectTransform>();
            fadeRt.SetParent(backdrop, false);
            fadeRt.anchorMin = new Vector2(0f, 0.5f);
            fadeRt.anchorMax = Vector2.one;
            fadeRt.offsetMin = fadeRt.offsetMax = Vector2.zero;
            var fade = fadeRt.gameObject.AddComponent<RawImage>();
            fade.texture = Utilities.EmbeddedResourceandUnity.LoadTexture(TEX + "gradient_fade_black.png");
            fade.raycastTarget = false;
        }

        private static GameObject MenuScreenPrefab(GameObject clone)
        {
            var mb = clone.GetComponent<FGClient.MainMenuBuilder>();
            var builderCfg = mb == null ? null : mb._builderConfig;
            if (builderCfg == null) return null;

            var platform = builderCfg.GetPlatformSpecificConfig();
            if (platform != null && platform.MainMenuPrefab != null) return platform.MainMenuPrefab.gameObject;

            var shared = builderCfg.GetSharedConfig();
            if (shared != null && shared.MainMenuPrefab != null) return shared.MainMenuPrefab.gameObject;
            return null;
        }

        private static void Sweep(Transform canvasRoot, Transform stageHost, HashSet<int> seen)
        {
            var strays = new List<GameObject>();
            foreach (var t in canvasRoot.GetComponentsInChildren<Transform>(true))
            {
                if (seen.Contains(t.GetInstanceID())) continue;
                if (t.IsChildOf(stageHost)) continue;
                if (t.parent == null || !seen.Contains(t.parent.GetInstanceID())) continue;
                strays.Add(t.gameObject);
            }

            foreach (var go in strays)
            {
                Plugin.Log.LogInfo("layout stage left " + go.name + " sitting in the live UI, binning it");
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static void StripLiveWiring(GameObject clone)
        {
            var vcam = clone.GetComponent<FGClient.UI.SwitchableViewExtension_MainMenuVCam>();
            if (vcam != null) UnityEngine.Object.DestroyImmediate(vcam);

            var audio = clone.GetComponent<FGClient.MainMenuSwitchableViewAudio>();
            if (audio != null) UnityEngine.Object.DestroyImmediate(audio);

            var focus = clone.GetComponent<FGClient.SwitchableViewFocusHandler>();
            if (focus != null) UnityEngine.Object.DestroyImmediate(focus);

            var builder = clone.GetComponent<FGClient.MainMenuBuilder>();
            if (builder != null) UnityEngine.Object.DestroyImmediate(builder);
        }

        private static void SoloScreen(RectTransform stage)
        {
            var main = stage.Find("MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Main");
            if (main == null) return;

            for (int i = 0; i < main.childCount; i++)
            {
                var c = main.GetChild(i);
                c.gameObject.SetActive(c.name.StartsWith("Prime_UI_MainMenu_Canvas", StringComparison.Ordinal));
            }
        }

        private static void Reveal(RectTransform stage)
        {
            foreach (var fader in stage.GetComponentsInChildren<CanvasGroupFader>(true))
                fader.enabled = false;

            foreach (var bind in stage.GetComponentsInChildren<Mediatonic.Tools.MVVM.ActiveBinding>(true))
                UnityEngine.Object.DestroyImmediate(bind);

            foreach (var vm in stage.GetComponentsInChildren<FG.Common.UI.FocusableViewModel>(true))
                UnityEngine.Object.DestroyImmediate(vm);

            foreach (var trigger in stage.GetComponentsInChildren<FGClient.Tweening.UITweenTriggerComponent>(true))
                trigger.enabled = false;

            foreach (var tween in stage.GetComponentsInChildren<FGClient.Tweening.UITweenData>(true))
                tween.enabled = false;

            foreach (var cg in stage.GetComponentsInChildren<CanvasGroup>(true))
            {
                cg.alpha = 1f;
                cg.blocksRaycasts = true;
                cg.interactable = false;
            }
        }

        public static void SetPreviewScale(float s)
        {
            var frame = Host.transform.Find("Frame").GetComponent<RectTransform>();
            var stage = frame.Find("Stage").GetComponent<RectTransform>();
            stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(0.5f, 0.5f);
            stage.anchoredPosition = Vector2.zero;
            stage.sizeDelta = frame.rect.size / s;
            stage.localScale = new Vector3(s, s, 1f);

            var mm = stage.Find("BFG_LoadingStage/" + MenuLayoutService.LOBBY_MM_NAME);
            if (mm == null) return;
            var mrt = mm.GetComponent<RectTransform>();
            if (mrt.anchorMin != mrt.anchorMax) return;
            float fit = frame.rect.width / (mrt.sizeDelta.x * s);
            mrt.localScale = new Vector3(fit, fit, 1f);
        }

        public static void SetCollapse(float s)
        {
            var frame = Host.transform.Find("Frame").GetComponent<RectTransform>();
            frame.localScale = new Vector3(FRAME_SCALE, FRAME_SCALE * s, 1f);
            frame.anchoredPosition = new Vector2(-FRAME_MARGIN, -FRAME_MARGIN + frame.rect.height * FRAME_SCALE * (s - 1f) * 0.5f);
            Host.transform.Find("FrameOutline").localScale = new Vector3(1f, s, 1f);
        }

        public static void Teardown()
        {
            if (Host != null) UnityEngine.Object.Destroy(Host);
            Host = null;
        }

        internal static GameObject BuilderPrefab()
        {
            FGClient.UI.Core.UIConfig cfg = null;
            try { cfg = Addressables.LoadAssetAsync<FGClient.UI.Core.UIConfig>(MENU_UI_CONFIG).WaitForCompletion(); }
            catch (Exception ex) { Plugin.Log.LogWarning("couldn't pull " + MENU_UI_CONFIG + " out of addressables: " + ex.Message); }
            if (cfg == null) return null;

            for (int i = 0; i < cfg.Screens.Count; i++)
            {
                var s = cfg.Screens[i];
                if (s != null && s.name == BUILDER) return s.gameObject;
            }
            return null;
        }

        private static void AdoptTopbar(Transform def, RectTransform stage, HashSet<int> known)
        {
            if (def == null) return;

            Transform live = null;
            for (int i = def.childCount - 1; i >= 0; i--)
            {
                var c = def.GetChild(i);
                if (c.name != TOPBAR) continue;
                if (!known.Contains(c.GetInstanceID()))
                {
                    c.SetParent(stage, false);
                    var rt = c.GetComponent<RectTransform>();
                    if (rt != null) Stretch(rt);
                    return;
                }
                live = c;
            }

            if (live == null) return;
            var copy = UnityEngine.Object.Instantiate(live.gameObject, stage, false);
            copy.name = TOPBAR;
            var crt = copy.GetComponent<RectTransform>();
            if (crt != null) Stretch(crt);
        }

        private static void MirrorLive(RectTransform stage, MenuLayoutService.Kind kind)
        {
            var live = kind.Root;
            if (live == null) return;

            for (int i = 0; i < kind.Slots.Length; i++)
            {
                var slot = kind.Slots[i];
                if (slot.FramePath != null) continue;

                var staged = MenuLayoutService.Resolve(stage, slot);
                var liveRt = MenuLayoutService.Resolve(live, slot);
                if (staged == null || liveRt == null) continue;

                var copy = UnityEngine.Object.Instantiate(liveRt.gameObject, staged.parent, false);
                copy.name = staged.name;
                copy.transform.SetSiblingIndex(staged.GetSiblingIndex());
                UnityEngine.Object.DestroyImmediate(staged.gameObject);
                MenuLayoutService.CopyStockPose(liveRt, copy.GetComponent<RectTransform>());
            }
        }

        private static void MirrorMoved(RectTransform stage, MenuLayoutService.Kind kind)
        {
            var live = kind.Root;
            if (live == null) return;

            for (int i = 0; i < kind.Slots.Length; i++)
            {
                var slot = kind.Slots[i];
                if (MenuLayoutService.Resolve(stage, slot) != null) continue;

                var liveRt = MenuLayoutService.Resolve(live, slot);
                if (liveRt == null) continue;

                if (slot.FramePath != null)
                {
                    var frameHost = stage.Find(slot.FrameHostPath);
                    if (frameHost == null || liveRt.parent == null) continue;
                    var frameCopy = UnityEngine.Object.Instantiate(liveRt.parent.gameObject, frameHost, false);
                    frameCopy.name = liveRt.parent.name;
                    continue;
                }

                var host = MenuLayoutService.HostFor(stage, slot);
                if (host == null) continue;

                var copy = UnityEngine.Object.Instantiate(liveRt.gameObject, host, false);
                copy.name = slot.Name;
            }
        }

        public static void ShowSlots(RectTransform stage, MenuLayoutService.Kind kind)
        {
            for (int i = 0; i < kind.Slots.Length; i++)
            {
                var t = stage.Find(kind.Slots[i].Path);
                if (t == null) continue;

                var cur = t;
                while (cur != null && cur != stage)
                {
                    if (!cur.gameObject.activeSelf) cur.gameObject.SetActive(true);
                    cur = cur.parent;
                }
            }
        }

        private static void Raise(RectTransform stage)
        {
            foreach (var cv in stage.GetComponentsInChildren<Canvas>(true))
            {
                cv.overrideSorting = false;
            }
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }
    }
}
