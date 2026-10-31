using System;
using System.Collections.Generic;
using BetterFG.Core;
using BetterFG.Services;
using Events;
using FGClient;
using FGClient.ShowSelector;
using FGClient.UI.Core;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Tweaks
{
    public class MultiShowSelectTweak : BfgTweak
    {
        public MultiShowSelectTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "multi_show_select";
        public override string TweakLabel => "tweak.multi_show_queue";
        public override bool DefaultEnabled => false;
        public override string TweakTooltip => "ui.confirm_shows_to_add_them_to_the_set_instead_of";

        public static MultiShowSelectTweak Instance { get; private set; }
        void Awake() => Instance = this;

        const string CheckName = "BFG_Check";
        const string SelectorRootName = "Prime_UI_SymphonyShowSelector_Prefab_Canvas(Clone)";
        static readonly string[] TriangleNames = { "Y", "Triangle" };
        const string ExploreFlowPath = "UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates/SpectatorState/GameplaySpectatorViewModel/SafeArea/GameplaySpectatorUltimatePartyFlow";
        static readonly string[] DpadRightNames = { "D-Pad Right", "DPad Right", "Hat Right" };

        readonly HashSet<string> _pinned = new HashSet<string>();
        readonly List<GameObject> _checks = new List<GameObject>();
        GameObject _selectorRoot;
        Sprite _on, _off;
        bool _spritesScanned;
        NavigationPromptData _data;
        bool _injected;
        bool _relabelled;
        bool _launching;
        bool _choosing;
        bool _resyncPending;
        float _launchedAt;
        ClientGameManager _cgm;
        GameObject _exploreFlow;
        bool _allowOffShow;
        bool _offShowInjected;
        bool _offShowPromptOn;
        MainMenuShowSelectorPlayButtonViewModel _playButton;
        readonly List<ShowData> _keyScratch = new List<ShowData>();

        public static void OnTileData(ShowSelectorShowTileViewModel tile, ShowSelectorShow show)
        {
            var inst = Instance;
            if (inst == null || !inst.IsEnabled) return;
            if (!string.IsNullOrEmpty(show.ShareCode)) { OnNestedData(tile); return; }
            inst.Stamp(tile, show.Id);
        }

        public static void OnNestedData(ShowSelectorShowTileViewModel tile)
        {
            var check = tile.transform.Find("ShowTileHolder/" + CheckName);
            if (check != null) check.gameObject.SetActive(false);
        }

        public static bool OnTileConfirm(ShowSelectorShowTileViewModel tile)
        {
            var inst = Instance;
            if (inst == null || !inst.IsEnabled || !tile.IsIndividualShowEntryPoint) return false;
            var show = tile.ShowData.ShowSelectorShow;
            if (!string.IsNullOrEmpty(show.ShareCode)) return false;
            var id = show.Id;
            if (!inst._pinned.Remove(id)) inst._pinned.Add(id);
            inst.Stamp(tile, id);
            inst.SyncSelectedShows();
            Plugin.Log.LogInfo($"{id} {(inst._pinned.Contains(id) ? "in" : "out")}, {inst._pinned.Count} checked");
            return true;
        }

        public static void AugmentShowIds(Il2CppSystem.Collections.Generic.List<string> showIds)
        {
            var inst = Instance;
            if (inst == null || !(inst._launching || inst.OffShowLive || HasSeveral(ShowsManager.Instance.SelectedShowDef))) return;
            inst._launching = false;
            inst._resyncPending = true;
            foreach (var id in inst._pinned)
                if (!showIds.Contains(id)) showIds.Add(id);
            Plugin.Log.LogInfo($"asking matchmaking for {showIds.Count} shows at once: {string.Join(", ", inst._pinned)}");
        }

        static bool HasSeveral(Il2CppSystem.Collections.Generic.Dictionary<ShowData, bool> defs)
        {
            int n = 0;
            foreach (var kv in defs)
                if (kv.Value && ++n > 1) return true;
            return false;
        }

        void SyncSelectedShows()
        {
            var sm = ShowsManager.Instance;
            if (_pinned.Count == 1)
            {
                foreach (var id in _pinned) sm.TrySelectShowWithId(id, false);
                return;
            }
            if (_pinned.Count == 0) return;
            var defs = sm.SelectedShowDef;
            _keyScratch.Clear();
            foreach (var kv in defs) _keyScratch.Add(kv.Key);
            foreach (var key in _keyScratch)
                defs[key] = key.ShowSelectorShow != null && _pinned.Contains(key.ShowSelectorShow.Id);
        }

        public static void OnPlayButtonShows(MainMenuShowSelectorPlayButtonViewModel vm, Il2CppSystem.Collections.Generic.Dictionary<ShowData, bool> defs)
        {
            var inst = Instance;
            if (inst == null || !inst.IsEnabled) return;
            inst._playButton = vm;
            bool several = HasSeveral(defs);
            inst.SetPlayButtonCards(several);
            if (several && FG.Common.CMS.CMSLoader.Instance._localisedStrings._localisedStrings.TryGetValue("show_selector_multiple_shows", out var text))
                vm.ShowName = text;
        }

        void SetPlayButtonCards(bool several)
        {
            if (_playButton == null) return;
            var cards = _playButton.transform.Find("Cards");
            cards.Find("SoloCard").gameObject.SetActive(!several);
            cards.Find("MultiCards").gameObject.SetActive(several);
        }

        public static void OnLobbyTitle(FGClient.UI.CurrentSelectedShowViewModel vm)
        {
            var inst = Instance;
            if (inst == null || !inst._choosing) return;
            vm.CurrentSelectedShowName = LocalizationService.Get("multi_show.choosing");
        }

        public static void OnMatchmakingEnd()
        {
            var inst = Instance;
            if (inst == null || !inst._choosing) return;
            inst._choosing = false;
            Plugin.Log.LogInfo($"multi-show matchmaking done after {Time.realtimeSinceStartup - inst._launchedAt:0.0}s");
        }

        public override void EnableTweak()
        {
            FallGuysLib.Round.RoundEvents.OnPlayingStarted -= OnPlayingStarted;
            FallGuysLib.Round.RoundEvents.OnPlayingStarted += OnPlayingStarted;
        }

        static void OnPlayingStarted() => Instance?.Invoke(nameof(FadeStuckLoadingScreen), 1f);

        public void FadeStuckLoadingScreen()
        {
            var screen = BetterFG.Patches.LoadingScreenBg.Active;
            if (screen == null || !screen.gameObject.activeInHierarchy) return;
            Plugin.Log.LogWarning($"{screen.GetType().Name} still up a second into the round, fading it out ourselves");
            screen.DoFadeOut();
        }

        public override void OnSpectatorMode()
        {
            _cgm = null;
            GlobalGameStateClient.Instance?.GameStateView?.GetLiveClientGameManager(out _cgm);
            _exploreFlow = GameObject.Find(ExploreFlowPath);
        }

        public override void OnMainMenuEntered()
        {
            _cgm = null;
            _exploreFlow = null;
            SyncSelectedShows();
        }

        bool OffShowLive => _allowOffShow && SpectatingExplore;

        bool SpectatingExplore => _pinned.Count > 1 && _cgm != null && _exploreFlow != null
            && _exploreFlow.activeInHierarchy && _cgm.IsSpectatorMode;

        void ToggleOffShow()
        {
            _allowOffShow = !_allowOffShow;
            Plugin.Log.LogInfo($"off-show matchmake {(_allowOffShow ? "on, next requeue asks for all " + _pinned.Count : "off, staying in explore")}");
        }

        void TickOffShowPrompt()
        {
            bool live = SpectatingExplore;
            if (live && (!_offShowInjected || _offShowPromptOn != _allowOffShow))
            {
                string loc = _allowOffShow ? "multi_show.offshow_on" : "multi_show.offshow_off";
                var data = NavPromptInjection.BuildData(NavPrompt.Report, LocalizationService.Get(loc), "bfg_" + loc, -1, -1);
                NavPromptInjection.Add(NavPromptInjection.MultiShowOffShow, ToggleOffShow, data,
                    btn => NavPromptCore.ApplyOwnGlyphByElement(btn, KeyCode.O, NavPromptCore.CurrentPadElementByName(DpadRightNames)));
                _offShowInjected = true;
                _offShowPromptOn = _allowOffShow;
            }
            else if (!live && _offShowInjected)
            {
                NavPromptInjection.Remove(NavPromptInjection.MultiShowOffShow);
                _offShowInjected = false;
            }
            if (live && (KeybindService.KeyDown(KeyCode.O) || NavPromptCore.ElementDownByName(DpadRightNames)))
                ToggleOffShow();
        }

        public override void OnRoundStart()
        {
            if (!_resyncPending) return;
            _resyncPending = false;
            var actual = GlobalGameStateClient.Instance.SelectedShow?.Id;
            if (string.IsNullOrEmpty(actual)) return;
            bool ok = ShowsManager.Instance.TrySelectShowWithId(actual, true);
            Plugin.Log.LogInfo($"server went with {actual}, pointing the selected show at it so requeues stay there ({ok})");
        }

        void Stamp(ShowSelectorShowTileViewModel tile, string id)
        {
            if (_selectorRoot == null)
                for (var t = tile.transform; t != null; t = t.parent)
                    if (t.name == SelectorRootName) { _selectorRoot = t.gameObject; break; }

            var holder = tile.transform.Find("ShowTileHolder");
            var check = holder.Find(CheckName);
            if (check == null) check = MakeCheck(holder);
            check.gameObject.SetActive(true);
            check.GetComponent<Image>().sprite = _pinned.Contains(id) ? _on : _off;
        }

        Transform MakeCheck(Transform holder)
        {
            if (!_spritesScanned)
            {
                _spritesScanned = true;
                foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                {
                    if (s.name == "Checkbox_On") _on = s;
                    else if (s.name == "Checkbox_Off") _off = s;
                }
                if (_on == null || _off == null)
                    Plugin.Log.LogWarning("creative checkbox sprites aren't loaded yet, tiles get plain squares this time");
            }

            var go = new GameObject(CheckName);
            go.transform.SetParent(holder, false);
            go.AddComponent<Image>().raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.one;
            rt.sizeDelta = new Vector2(56f, 56f);
            rt.anchoredPosition = new Vector2(-14f, -14f);
            _checks.Add(go);
            return go.transform;
        }

        void Update()
        {
            TickOffShowPrompt();
            bool open = _selectorRoot != null && _selectorRoot.activeInHierarchy;
            if (open != _relabelled)
            {
                if (open)
                {
                    NavPromptInjection.Relabel(NavPrompt.Play, "bfg_multishow_select", LocalizationService.Get("multi_show.select"));
                    NavPromptInjection.Relabel(NavPrompt.Select, "bfg_multishow_select", LocalizationService.Get("multi_show.select"));
                }
                else
                {
                    Unrelabel();
                    if (_playButton != null) _playButton.OnShowSelected(ShowsManager.Instance.SelectedShowDef);
                }
                _relabelled = open;
            }

            bool live = open && _pinned.Count > 0;
            if (live != _injected)
            {
                if (live)
                {
                    _launching = false;
                    if (_data == null)
                        _data = NavPromptInjection.BuildData(NavPrompt.Report, LocalizationService.Get("multi_show.play"), "bfg_multishow_play", -1, -1);
                    NavPromptInjection.Add(NavPromptInjection.MultiShow, LaunchQueue, _data,
                        btn => NavPromptCore.ApplyOwnGlyphByElement(btn, KeyCode.T, NavPromptCore.CurrentPadElementByName(TriangleNames)));
                }
                else NavPromptInjection.Remove(NavPromptInjection.MultiShow);
                _injected = live;
            }
            if (live && (KeybindService.KeyDown(KeyCode.T) || NavPromptCore.ElementDownByName(TriangleNames)))
                LaunchQueue();
        }

        void LaunchQueue()
        {
            if (_pinned.Count == 0) return;
            SyncSelectedShows();
            Plugin.Log.LogInfo($"multi-show queue go, {_pinned.Count} shows: {string.Join(", ", _pinned)}");
            _launching = true;
            _choosing = true;
            _resyncPending = true;
            _launchedAt = Time.realtimeSinceStartup;
            Broadcaster.Instance.Broadcast(new ConnectEvent(GlobalGameStateClient.Instance.PlayerProfile));
        }

        void Unrelabel()
        {
            NavPromptInjection.Unrelabel(NavPrompt.Play);
            NavPromptInjection.Unrelabel(NavPrompt.Select);
        }

        public override void DisableTweak()
        {
            FallGuysLib.Round.RoundEvents.OnPlayingStarted -= OnPlayingStarted;
            CancelInvoke(nameof(FadeStuckLoadingScreen));
            if (_relabelled) Unrelabel();
            _relabelled = false;
            if (_injected) NavPromptInjection.Remove(NavPromptInjection.MultiShow);
            _injected = false;
            _launching = false;
            _choosing = false;
            _resyncPending = false;
            if (_offShowInjected) NavPromptInjection.Remove(NavPromptInjection.MultiShowOffShow);
            _offShowInjected = false;
            _allowOffShow = false;
            _cgm = null;
            SetPlayButtonCards(false);
            _playButton = null;
            _pinned.Clear();
            foreach (var go in _checks)
                if (go != null) Destroy(go);
            _checks.Clear();
            _selectorRoot = null;
        }
    }
}
