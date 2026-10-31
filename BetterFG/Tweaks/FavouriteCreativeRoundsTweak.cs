using System;
using System.Collections.Generic;
using System.Reflection;
using BetterFG.Core;
using BetterFG.Services;
using Events;
using FG.Common;
using FGClient;
using FGClient.UI.PrivateLobby;
using HarmonyLib;
using Il2CppInterop.Runtime;
using MPG.Utility;
using UnityEngine;
using Il2CppFavouriteList = Il2CppSystem.Collections.Generic.LinkedList<FGClient.PlayerLobbiesData>;

namespace BetterFG.Tweaks
{
    public class FavouriteCreativeRoundsTweak : BfgTweak
    {
        public FavouriteCreativeRoundsTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "favourite_creative_rounds";
        public override string TweakLabel => "tweak.favourite_creative_rounds";
        public override bool DefaultEnabled => true;

        public static FavouriteCreativeRoundsTweak Instance { get; private set; }
        void Awake() => Instance = this;

        const string CodesKey = "favourite_creative_rounds.codes";

        PrivateLobbyShowListViewModel _vm;
        Il2CppFavouriteList _loadedInto;
        bool _focused;
        NavPrompt? _shown;

        internal static void OnShowListAwake(PrivateLobbyShowListViewModel vm)
        {
            if (Instance != null) Instance._vm = vm;
        }

        void Update()
        {
            bool focused = _vm != null && _vm._isInFocus;
            if (focused && !_focused) LoadSaved();
            _focused = focused;

            var want = focused ? Wanted(SingletonBehaviour<FragglePlobbiesManager>.Instance) : null;
            if (want == _shown) return;
            _shown = want;
            if (want == null) NavPromptInjection.Remove(NavPromptInjection.FavouriteCreative);
            else NavPromptInjection.Add(NavPromptInjection.FavouriteCreative, () => Toggle(), NavPromptCore.Manager._navPromptsDictionary[want.Value]);
        }

        static NavPrompt? Wanted(FragglePlobbiesManager mgr)
        {
            if (mgr.IsFavouriteTab || mgr.IsShowingEnterCodePanel) return null;
            var show = Highlighted(mgr);
            if (show == null) return null;
            return mgr._favouriteShows.Contains(show.ShowId) ? NavPrompt.Unfavourite : NavPrompt.Favourite;
        }

        internal static FragglePlobbiesLevelData Highlighted(FragglePlobbiesManager mgr)
            => mgr._currentHighlightedShow?.TryCast<FragglePlobbiesLevelData>();

        static Il2CppFavouriteList FavouriteList(FragglePlobbiesManager mgr)
        {
            string tab = mgr._favouriteTabId;
            if (string.IsNullOrEmpty(tab) || !mgr._showDictionary.TryGetValue(tab, out var list)) return null;
            return list;
        }

        void LoadSaved()
        {
            var mgr = SingletonBehaviour<FragglePlobbiesManager>.Instance;
            var list = FavouriteList(mgr);
            if (list == null || (_loadedInto != null && _loadedInto.Pointer == list.Pointer)) return;
            _loadedInto = list;

            string saved = SettingsService.Get(CodesKey, "");
            if (saved.Length == 0) return;
            var codes = new Il2CppSystem.Collections.Generic.List<string>();
            foreach (var code in saved.Split(',')) codes.Add(code);
            mgr.GetLevelsByShareCodes(codes, DelegateSupport.ConvertDelegate<Il2CppSystem.Action<LevelsArrayJsonRoot>>(new Action<LevelsArrayJsonRoot>(OnSavedFetched)));
        }

        static void OnSavedFetched(LevelsArrayJsonRoot result)
        {
            var levels = result?.Levels;
            if (levels == null)
            {
                Plugin.Log.LogWarning("level service gave nothing back for the favourite creative rounds, trying again next time the show list opens");
                if (Instance != null) Instance._loadedInto = null;
                return;
            }

            var mgr = SingletonBehaviour<FragglePlobbiesManager>.Instance;
            mgr.AddSnapshotDataToLevels(result);
            for (int i = 0; i < levels.Length; i++) mgr._favouriteShows.Add(levels[i].LevelId);
            mgr.AddLevelsToUI(levels, mgr._favouriteTabId, int.MaxValue);
            Plugin.Log.LogInfo($"{levels.Length} favourite creative rounds back in the favourites tab");
        }

        internal static bool Toggle()
        {
            var mgr = SingletonBehaviour<FragglePlobbiesManager>.Instance;
            var show = Highlighted(mgr);
            var list = FavouriteList(mgr);
            if (show == null || list == null) return false;

            var set = mgr._favouriteShows;
            string id = show.ShowId;
            bool fav = !set.Contains(id);
            var codes = new List<string>(SettingsService.Get(CodesKey, "").Split(',', StringSplitOptions.RemoveEmptyEntries));
            codes.Remove(show.ShareCode);
            if (fav)
            {
                set.Add(id);
                list.AddFirst(show);
                codes.Insert(0, show.ShareCode);
            }
            else
            {
                set.Remove(id);
                for (var node = list.First; node != null;)
                {
                    var next = node.Next;
                    if (node.Value.ShowId == id) list.Remove(node);
                    node = next;
                }
            }
            SettingsService.Set(CodesKey, string.Join(",", codes));
            PlayerPrefsSerializer.Set(Keys.Prefs.PrivateLobbies_FavouriteShows,
                (Il2CppSystem.String)Il2CppSystem.String.Join(";", set.Cast<Il2CppSystem.Collections.Generic.IEnumerable<string>>()), false);

            var sfx = AudioManager.EventMasterData;
            AudioManager.PlayOneShot(fav ? sfx.CustomiserFavorite : sfx.CustomiserUnFavorite, Vector3.zero);
            Broadcaster.Instance.Broadcast(new FragglePlobbiesManager.DataChanged(mgr.IsFavouriteTab
                ? FragglePlobbiesManager.DataChanged.DataChangedReason.ShowDataUpdated
                : FragglePlobbiesManager.DataChanged.DataChangedReason.FavouritesChanged));
            Plugin.Log.LogInfo($"{(fav ? "favourited" : "unfavourited")} creative round {show.ShowName} ({show.ShareCode})");
            return true;
        }

        public override void DisableTweak()
        {
            if (_shown != null) NavPromptInjection.Remove(NavPromptInjection.FavouriteCreative);
            _shown = null;
            _focused = false;
        }
    }

    [Utilities.BfgPatchGate("tweak.favourite_creative_rounds", defaultOn: true)]
    [HarmonyPatch]
    internal static class FavouriteCreativeRoundsTogglePatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(FragglePlobbiesManager), nameof(FragglePlobbiesManager.ToggleSelectedShowFavourite));
            yield return AccessTools.Method(typeof(PrivateLobbyShowListEntryViewModel), nameof(PrivateLobbyShowListEntryViewModel.OnToggleFavourite));
        }

        [HarmonyPrefix]
        public static bool Prefix() => !FavouriteCreativeRoundsTweak.Toggle();
    }

    [Utilities.BfgPatchGate("tweak.favourite_creative_rounds", defaultOn: true)]
    [HarmonyPatch(typeof(FragglePlobbiesManager), nameof(FragglePlobbiesManager.IsFavourite), MethodType.Getter)]
    internal static class FavouriteCreativeRoundsLabelPatch
    {
        [HarmonyPostfix]
        public static void Postfix(FragglePlobbiesManager __instance, ref bool __result)
        {
            var show = FavouriteCreativeRoundsTweak.Highlighted(__instance);
            if (show != null) __result = __instance._favouriteShows.Contains(show.ShowId);
        }
    }
}
