using System;
using BetterFG.Services;
using Events;
using FG.Common;
using FGClient;
using FGClient.UI.PrivateLobby;
using Il2CppInterop.Runtime;
using MPG.Utility;

namespace BetterFG.Features.CustomGamemodes
{
    internal static class CustomGamemodesLobby
    {
        const string TabId = "bfg_new";
        const int TabOrder = 4;

        static PrivateLobbyShowListViewModel _vm;
        static bool _wasFocused;

        internal static void OnShowListAwake(PrivateLobbyShowListViewModel vm)
        {
            _vm = vm;
            _wasFocused = false;
        }

        internal static void Tick()
        {
            bool focused = _vm != null && _vm._isInFocus;
            if (focused && !_wasFocused) Refresh();
            _wasFocused = focused;
        }

        static void Refresh()
        {
            if (CustomGamemodeHost.Modes.Count == 0) return;
            var mgr = SingletonBehaviour<FragglePlobbiesManager>.Instance;
            var tabs = mgr._tabsData;
            bool has = false;
            int at = tabs.Count;
            for (int i = 0; i < tabs.Count; i++)
            {
                if (tabs[i].Id == TabId) has = true;
                if (at == tabs.Count && tabs[i].Order > TabOrder) at = i;
            }
            if (!has)
            {
                var tab = new PrivateLobbyShowTabData();
                tab.Id = TabId;
                tab.Name = LocalizationService.Get("custom_gamemodes.tab_new");
                tab.Order = TabOrder;
                tabs.Insert(at, tab);
            }

            if (!mgr._showDictionary.TryGetValue(TabId, out var list))
            {
                list = new Il2CppSystem.Collections.Generic.LinkedList<PlayerLobbiesData>();
                mgr._showDictionary[TabId] = list;
            }
            if (list.Count == 0)
            {
                var codes = new Il2CppSystem.Collections.Generic.List<string>();
                foreach (var mode in CustomGamemodeHost.Modes) codes.Add(mode.ShareCode);
                mgr.GetLevelsByShareCodes(codes, DelegateSupport.ConvertDelegate<Il2CppSystem.Action<LevelsArrayJsonRoot>>(new Action<LevelsArrayJsonRoot>(OnLevelsFetched)));
            }

            if (!has) _vm.PopulateTabs();
        }

        static void OnLevelsFetched(LevelsArrayJsonRoot result)
        {
            var levels = result?.Levels;
            if (levels == null)
            {
                Plugin.Log.LogWarning("level service gave nothing back for the custom game modes, the NEW tab stays empty until the show list opens again");
                return;
            }
            var mgr = SingletonBehaviour<FragglePlobbiesManager>.Instance;
            mgr.AddSnapshotDataToLevels(result);
            mgr.AddLevelsToUI(levels, TabId, int.MaxValue);
            Broadcaster.Instance.Broadcast(new FragglePlobbiesManager.DataChanged(FragglePlobbiesManager.DataChanged.DataChangedReason.ShowDataUpdated));
            Plugin.Log.LogInfo($"{levels.Length} custom game mode level(s) in the NEW tab");
        }
    }
}
