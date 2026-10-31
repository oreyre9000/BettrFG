using System;
using System.Collections;
using System.Linq;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.UI;
using FallGuysLib.Round;
using FG.Common;
using FGClient;
using UnityEngine;

namespace BetterFG.Services
{
    public static class DiscordPresenceService
    {
        private const string KEY_ENABLED = "discord.presence";
        private const string ClientId = "1524048184215867463";
        private const string LargeImage = "bettrfglogo";

        private static bool _enabled;

        public static bool Enabled => _enabled;

        private static readonly long _stampUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        private static bool _wonLastShow;
        private static bool _inRewards;
        private static string _lastShow;

        private static string _roundName;
        private static string _loadingRoundName;
        private static string _loadingBadge;
        private static string _loadingPlayers;
        private static string _status;
        private static int _place;
        private static bool _atMenu;
        private static MainMenuManager _mainMenu;
        private static int _squadUp;
        private static int _squadOut;
        private static int _squadSize;
        private static DiscordRpcClient.Activity _lastComposed;
        private static bool _inReplayViewer;
        private static string _replayName;
        private static long _replaySizeBytes;
        private static int? _exportPercent;
        private static bool _showSelectorOpen;
        private static bool _pushPending;
        private static bool _roundLive;
        private static bool _localOutSeen;
        private static bool _skipped;

        public static void OnShowSelectorTileSeen()
        {
            if (_showSelectorOpen) return;
            _showSelectorOpen = true;
            Push();
        }

        public static void OnShowSelectorClosed()
        {
            if (!_showSelectorOpen) return;
            _showSelectorOpen = false;
            Push();
        }

        public static void OnReplayViewerOpened()
        {
            _inReplayViewer = true;
            _exportPercent = null;
            Push();
        }

        public static void OnReplayViewerClosed()
        {
            _inReplayViewer = false;
            _replayName = null;
            _replaySizeBytes = 0;
            _exportPercent = null;
            Push();
        }

        public static void SetReplayInfo(string name, long sizeBytes)
        {
            _replayName = name;
            _replaySizeBytes = sizeBytes;
            Push();
        }

        public static void SetExportProgress(int? percent)
        {
            _exportPercent = percent;
            Push();
        }

        public static void OnPlayerProgress(ClientGameManager cgm, GameMessageServerPlayerProgress msg)
        {
            if (cgm != null && msg != null && !msg.succeeded && cgm.IsMyLocalPlayer(msg.playerId))
            {
                _localOutSeen = true;
                _skipped = msg.isSkipping;
            }

            if (cgm != null && cgm.IsSquadShow && msg != null && !msg.isSkipping)
            {
                var idx = cgm._clientPlayerManager?._playerIdIndex;
                if (idx != null && idx.ContainsKey(msg.playerId) && idx.ContainsKey(cgm._myPlayerID))
                {
                    uint mine = idx[cgm._myPlayerID].SquadID;
                    if (idx[msg.playerId].SquadID == mine)
                    {
                        if (msg.succeeded) _squadUp++; else _squadOut++;

                        int size = 0;
                        foreach (var kvp in idx)
                            if (kvp.Value != null && kvp.Value.SquadID == mine) size++;
                        _squadSize = size;
                    }
                }
            }
            Push();
        }

        public static void OnLoadingScreen(string roundName, string waitingForPlayers, FG.Common.CMS.Round round)
        {
            string players = waitingForPlayers != null && waitingForPlayers.Any(char.IsDigit)
                ? waitingForPlayers.Trim()
                : null;

            string badge = BadgeKey(round);
            if (roundName == _loadingRoundName && players == _loadingPlayers
                && (badge == null || badge == _loadingBadge)) return;

            _atMenu = false;
            _roundLive = false;
            if (badge != null) _loadingBadge = badge;
            if (!string.IsNullOrEmpty(roundName)) _loadingRoundName = roundName;
            _loadingPlayers = players;
            Plugin.Log.LogInfo($"loading screen: {_loadingRoundName}, {players ?? "no player count yet"}, badge {_loadingBadge ?? "none"}");
            Push();
        }

        public static void OnLoadingRound(FG.Common.CMS.Round round)
        {
            string badge = BadgeKey(round);
            string name = round?.DisplayNameUnindented;
            if (badge == _loadingBadge && name == _loadingRoundName) return;

            _atMenu = false;
            _roundLive = false;
            if (badge != null) _loadingBadge = badge;
            if (!string.IsNullOrEmpty(name)) _loadingRoundName = name;
            Plugin.Log.LogInfo($"loader says next up is {_loadingRoundName ?? "something unnamed"}, badge {_loadingBadge ?? "none"}");
            Push();
        }

        private static void ClearRoundResult()
        {
            _status = null;
            _place = 0;
            _localOutSeen = false;
            _skipped = false;
            _squadUp = 0;
            _squadOut = 0;
            _squadSize = 0;
        }

        private static string Join(params string[] parts)
        {
            string line = string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));
            return line.Length > 0 ? line : null;
        }

        public static void Init()
        {
            _enabled = SettingsService.Get(KEY_ENABLED, "true") == "true";
            if (_enabled) DiscordRpcClient.Start(ClientId);
            Application.quitting += new Action(OnGameQuitting);
        }

        private static void OnGameQuitting() => DiscordRpcClient.Stop();

        public static void SetEnabled(bool on)
        {
            if (_enabled == on) return;
            _enabled = on;
            SettingsService.Set(KEY_ENABLED, on ? "true" : "false");

            if (on)
            {
                DiscordRpcClient.Start(ClientId);
                Push();
            }
            else
            {
                DiscordRpcClient.Stop();
            }
        }

        public static void OnStateChanged(GameStateMachine.IGameState newState)
        {
            if (newState != null)
            {
                if (newState.TryCast<StateVictoryScreen>() != null) _wonLastShow = true;
                _inRewards = newState.TryCast<StateRewardScreen>() != null;
            }
            Push();
        }

        public static void OnMainMenuEntered(MainMenuManager mainMenu)
        {
            _atMenu = true;
            _roundLive = false;
            _mainMenu = mainMenu;
            _wonLastShow = false;
            _inRewards = false;
            _roundName = null;
            _loadingRoundName = null;
            _loadingBadge = null;
            _loadingPlayers = null;
            _showSelectorOpen = false;
            ClearRoundResult();
            Push();
        }

        public static void OnRoundStart()
        {
            var ggsc = GlobalGameStateClient.Instance;
            ClientGameManager cgm = null;
            ggsc?.GameStateView?.GetLiveClientGameManager(out cgm);

            _atMenu = false;
            _roundLive = true;
            _roundName = cgm?._round?.DisplayNameUnindented;
            if (string.IsNullOrEmpty(_roundName)) _roundName = _loadingRoundName;
            _loadingRoundName = null;
            _loadingBadge = null;
            _loadingPlayers = null;
            ClearRoundResult();

            Plugin.Log.LogInfo($"presence round: {_roundName} — session counter {ggsc?.RoundCounterInPlaySession}, GameRules index {cgm?.GameRules?.RoundIndex}");
            Push();
        }

        public static void RequestPush()
        {
            _pushPending = true;
        }

        public static void FlushPendingPush()
        {
            if (!_pushPending) return;
            _pushPending = false;
            Push();
        }

        public static void Push()
        {
            if (!_enabled) return;
            try
            {
                var composed = Compose();
                if (!composed.HasValue) return;

                var activity = composed.Value;
                if (!activity.Equals(_lastComposed))
                {
                    _lastComposed = activity;
                    Plugin.Log.LogInfo($"presence -> {activity.Details} | {activity.State ?? "-"} | icon {activity.SmallImage ?? "none"}");
                }
                DiscordRpcClient.Set(activity);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"couldn't work out what to tell discord: {ex.Message}"); }
        }

        private static DiscordRpcClient.Activity? Compose()
        {
            if (_inReplayViewer)
            {
                if (_exportPercent.HasValue)
                    return Build(LocalizationService.Format("rpc.exporting_replay_fmt", _exportPercent), _replayName, "replay");

                string mb = (_replaySizeBytes / 1024f / 1024f).ToString("0.0") + " MB";
                return Build(LocalizationService.Format("rpc.editing_replay_fmt", mb), _replayName, "replay");
            }

            if (BetterFG.Features.QualificationTime.PBTabView.IsOpen)
                return Build(LocalizationService.Get("rpc.viewing_personal_bests"), null, "tab_home");

            var ggsc = GlobalGameStateClient.Instance;
            if (ggsc == null) return Build(LocalizationService.Get("rpc.booting_up"), null);

            if (ggsc.IsInCreativeEditor)
            {
                string mode = GameModeManager.CurrentGameModeData?.ID switch
                {
                    "GAMEMODE_GAUNTLET" => LocalizationService.Get("rpc.mode_race"),
                    "GAMEMODE_SURVIVAL" => LocalizationService.Get("rpc.mode_survival"),
                    "GAMEMODE_POINTS" => LocalizationService.Get("rpc.mode_points"),
                    _ => null
                };

                var cost = LevelEditorManager.Instance?.CostManager;
                string budget = cost != null ? LocalizationService.Format("rpc.budget_fmt", cost.UsedBuildPoints, cost.TotalBuildPoints) : null;

                var placed = LevelEditorGameObjectManager.Instance?._levelEditorPlaceableObjects;
                string objects = placed != null ? LocalizationService.Format("rpc.objects_fmt", placed.Count) : null;

                string levelName = LevelEditorManagerProxy.CurrentLevelName;
                string details;
                if (!string.IsNullOrEmpty(levelName))
                    details = mode != null
                        ? LocalizationService.Format("rpc.building_named_level_fmt", levelName, mode)
                        : LocalizationService.Format("rpc.building_named_level_nomode_fmt", levelName);
                else
                    details = mode != null ? LocalizationService.Format("rpc.building_mode_level_fmt", mode) : LocalizationService.Get("rpc.in_creative_editor");

                return Build(details, Join(budget, objects));
            }

            string show = ShowName(ggsc);

            var gsv = ggsc.GameStateView;
            ClientGameManager cgm = null;
            gsv?.GetLiveClientGameManager(out cgm);
            var round = cgm?._round;

            if (round != null && !_atMenu && _roundLive)
            {
                string level = round.DisplayNameUnindented;
                if (string.IsNullOrEmpty(level)) level = _roundName;
                if (string.IsNullOrEmpty(level)) level = gsv.CurrentGameLevelName;
                if (string.IsNullOrEmpty(level)) level = LocalizationService.Get("rpc.a_round");

                string baseLevel = level;
                int number = ggsc.RoundCounterInPlaySession;
                if (number > 0) level += " " + LocalizationService.Format("rpc.round_number_fmt", number);

                string details = cgm.IsSpectatorMode ? LocalizationService.Format("rpc.spectating_fmt", level) : level;
                string effShow = string.Equals(show, baseLevel, StringComparison.OrdinalIgnoreCase) ? null : show;
                return Build(details, Progress(cgm, effShow), BadgeKey(round), baseLevel);
            }

            if (ggsc.PrivateLobbyOpened)
                return Build(LocalizationService.Get("rpc.in_private_lobby"), show);

            if (ggsc.IsInAnyMatchmakingState)
            {
                int connected = BetterFG.Tweaks.MatchmakingQueueCountTweak.ConnectedPlayers;
                int total = BetterFG.Tweaks.MatchmakingQueueCountTweak.TotalPlayers;
                string filling = connected > 0
                    ? (total > 0 ? LocalizationService.Format("rpc.players_count_fmt", connected, total) : LocalizationService.Format("rpc.players_count_solo_fmt", connected))
                    : null;
                return Build(LocalizationService.Get("rpc.searching_for_match"), Join(show, filling));
            }

            if (_inRewards)
            {
                bool won = ggsc._clientPlayerManager?.LocalPlayerSucceeded ?? false;
                return Build(LocalizationService.Get("rpc.collecting_rewards"),
                    Join(show, won ? LocalizationService.Get("rpc.just_won") : _skipped ? LocalizationService.Get("rpc.rewards_skipped") : LocalizationService.Get("rpc.rewards_eliminated")));
            }

            if (_wonLastShow)
                return Build(string.IsNullOrEmpty(_roundName) ? LocalizationService.Get("rpc.won_the_show") : LocalizationService.Format("rpc.just_won_in_fmt", _roundName), show);

            if (ggsc.IsInGameMatch)
            {
                string next = _loadingRoundName
                    ?? BetterFG.Features.QualificationTime.FeatureQualificationTime.CachedRoundName;

                if (string.IsNullOrEmpty(next)) return null;

                string baseNext = next;
                int number = ggsc.RoundCounterInPlaySession + 1;
                if (number > 1) next += " " + LocalizationService.Format("rpc.round_number_fmt", number);
                return Build(LocalizationService.Format("rpc.loading_into_fmt", next), Join(show, _loadingPlayers), _loadingBadge, baseNext);
            }

            if (_mainMenu != null)
            {
                if (_showSelectorOpen)
                    return Build(LocalizationService.Get("rpc.picking_a_show"), null, "tab_home");

                // CurrentNavigationView only catches up when the switch animation ends, so leaving the
                // PB tab reported the Settings view it landed on positionally. the SwitchableView index
                // is already the destination by the time we're pushing.
                var sv = _mainMenu.MainMenuBuilder?.SwitchableView;
                var view = sv != null ? _mainMenu.GetViewType(sv.CurrentViewIndex) : _mainMenu.CurrentNavigationView;
                switch (view)
                {
                    case MainMenuViews.Customiser:
                        return Build(LocalizationService.Get("rpc.customising_bean"), null, "tab_customize");
                    case MainMenuViews.Settings:
                        return Build(LocalizationService.Get("rpc.changing_settings"), null);
                    case MainMenuViews.Seasons:
                        return Build(LocalizationService.Get("rpc.looking_fame_pass"), null, "tab_famepass");
                    case MainMenuViews.Shop:
                    case MainMenuViews.SymphonyShop:
                        return Build(LocalizationService.Get("rpc.looking_shop"), null, "tab_shop");
                    case MainMenuViews.LiveEvent:
                        return Build(LocalizationService.Get("rpc.checking_live_event"), null);
                    case MainMenuViews.LevelEditor:
                        return Build(LocalizationService.Get("rpc.heading_into_creative"), null);
                }
            }

            return Build(LocalizationService.Get("rpc.in_main_menu"), null, "tab_home");
        }

        private static string Progress(ClientGameManager cgm, string show)
        {
            var gsv = GlobalGameStateClient.Instance.GameStateView;
            int initial = (int)gsv.InitialRoundPlayerCount;
            if (initial <= 0) initial = cgm._initialNumParticipants;

            string counts;
            if (gsv.IsSurvivalRound())
            {
                int needOut = cgm.RequiredEliminatedPlayerCount;
                if (needOut > 0 && needOut < initial)
                {
                    counts = LocalizationService.Format("rpc.eliminated_count_fmt", cgm.EliminatedPlayerCount, needOut);
                }
                else
                {
                    int left = initial - cgm._qualifiedPlayerCount - cgm._eliminatedPlayerCount;
                    counts = left > 0 ? LocalizationService.Format(left == 1 ? "rpc.player_left_singular_fmt" : "rpc.player_left_plural_fmt", left) : null;
                }
            }
            else
            {
                int required = cgm.RequiredQualifiedPlayerCount;
                if (required > 1 && required < initial)
                    counts = LocalizationService.Format("rpc.qualified_count_fmt", cgm.QualifiedPlayerCount, required);
                else
                    counts = initial > 0 ? LocalizationService.Format("rpc.qualified_count_fmt", cgm.QualifiedPlayerCount, initial) : null;
            }

            string status = null;
            if (cgm.PlayerSucceeded) status = "QUALIFIED";
            else if (cgm.PlayerEliminated && _localOutSeen)
            {
                var mates = cgm._playerTeamManager;
                status = _skipped
                    ? "SKIPPED"
                    : cgm.IsSquadShow && mates != null
                      && mates.CurrentTeamSize(ClientGameManager.LocalPlayerTeamId) > 0
                        ? "WAITING FOR NOW"
                        : "ELIMINATED";
            }

            if (status != _status)
            {
                if (status == "QUALIFIED") _place = cgm._qualifiedPlayerCount + 1;
                _status = status;
                int team = ClientGameManager.LocalPlayerTeamId;
                var teams = cgm._playerTeamManager;
                Plugin.Log.LogInfo(cgm.IsSquadShow && teams != null
                    ? $"status now {status ?? "still playing"} — squad {team}, {teams.CurrentTeamSize(team)} of {teams.TeamSize(team)} still in"
                    : $"status now {status ?? "still playing"}");
            }

            string shown = status switch
            {
                "QUALIFIED" => _place > 0
                    ? LocalizationService.Format("rpc.qualified_place_fmt", _place, BetterFG.Features.TimePlacement.FeatureTimePlacement.Suffix(_place))
                    : LocalizationService.Get("rpc.qualified"),
                "SKIPPED" => LocalizationService.Get("rpc.status_skipped"),
                "WAITING FOR NOW" => LocalizationService.Get("rpc.waiting_for_now"),
                "ELIMINATED" => LocalizationService.Get("rpc.status_eliminated"),
                _ => null
            };

            string points = null;
            if (status == null && !GameRulesUtils.IsRaceRound() && GameRulesUtils.IsScoringRound())
            {
                int score;
                if (cgm.IsSquadShow && cgm.SquadSize >= 2)
                {
                    score = cgm.LocalPlayerSquadScore;
                }
                else
                {
                    var idx = cgm._clientPlayerManager?._playerIdIndex;
                    score = idx != null && idx.ContainsKey(cgm._myPlayerID) && cgm._soloScoreManager != null
                        ? cgm._soloScoreManager.GetSoloScore(idx[cgm._myPlayerID].objectNetID)
                        : 0;
                }

                int target = GameRulesUtils.ScoreTarget();
                points = target > 0 ? LocalizationService.Format("rpc.points_fmt", score, target) : LocalizationService.Format("rpc.points_notarget_fmt", score);
            }

            string squad = cgm.IsSquadShow && _squadSize > 0
                ? (_squadOut > 0
                    ? LocalizationService.Format("rpc.squad_status_out_fmt", _squadUp, _squadSize, _squadOut)
                    : LocalizationService.Format("rpc.squad_status_fmt", _squadUp, _squadSize))
                : null;

            return Join(show, shown, points, counts, squad);
        }

        private static string ShowName(GlobalGameStateClient ggsc)
        {
            string name = ggsc.SelectedShow?.ShowName?.Text;
            if (string.IsNullOrEmpty(name))
            {
                var defs = ShowsManager.Instance?.SelectedShowDef;
                if (defs != null)
                    foreach (var kvp in defs)
                        if (kvp.Value) { name = kvp.Key?.ShowSelectorShow?.ShowData?.ShowName?.Text; break; }
            }

            if (!string.IsNullOrEmpty(name) && name != _lastShow)
            {
                _lastShow = name;
                Plugin.Log.LogInfo($"show is {name}");
            }
            return _lastShow;
        }

        private static string BadgeKey(FG.Common.CMS.Round round)
        {
            string badge = round?.LevelBadgeName;
            return string.IsNullOrEmpty(badge) ? null : badge.ToLowerInvariant();
        }

        private static DiscordRpcClient.Activity Build(string details, string state,
            string smallImage = null, string smallText = null)
        {
            int size = 0, max = 0;
            var psm = PartyStateManager.Instance;
            if (psm != null && psm.IsInPartyWithOthers())
            {
                size = psm.Party.GetNumMembers();
                max = PartyService.MaxPartySize;
                if (max < size || max > 16) max = 4;
            }

            return new DiscordRpcClient.Activity(details, state, LargeImage,
                BetterFGInfo.PresenceName + " " + BetterFGInfo.Version, smallImage, smallText, _stampUnix, size, max);
        }
    }
}
