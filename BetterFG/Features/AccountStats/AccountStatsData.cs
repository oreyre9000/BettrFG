using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FallGuys.Player.Protocol.Client.Achievements;
using FallGuys.Player.Protocol.Client.PlayerLevel;
using FallGuys.Player.Protocol.Client.Seasons;
using FallGuys.Player.Protocol.Client.Stats;
using FG.Common.CMS;
using FGClient;
using FGClient.CatapultServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using ItemDto = Catapult.Modules.Items.Protocol.Dtos.ItemDto;

namespace BetterFG.Features.AccountStats
{
    internal sealed class AccountStatsData
    {
        internal struct OwnedItem { public string Id, Category; public DateTime EarnedAt; public bool Favourite; }
        internal struct SeasonInfo { public string Id; public int Fame, TiersUnlocked, TiersTotal, PremiumSkipped; }
        internal struct CrownReward { public int Rank, CrownsRequired; public string RewardId; public DateTime? ReachedAt; }
        internal struct RankedInfo { public string Chain; public int Current, High; public DateTime Started; }
        internal struct CreativeLevel { public string Code, Title; public int Plays, Likes, Versions; public bool Published; }
        internal sealed class Achievement { public string Id, SteamId, Name, Description; public int Progress; public DateTime? CompletedAt; public float Percent = -1f; public Texture2D Icon; }

        static readonly DateTime CrownBackfill = new DateTime(2020, 8, 4);

        public string Name, InternalName;
        public int EarnedCrowns, WalletCrowns, CrownShards, CrownRank, MaxCrownRank, NextRankCrowns, ThisRankCrowns;
        public int Kudos, GemsGranted, GemsPurchased, RankedBest;
        public long LifetimeFame;
        public bool HasSteam;
        public int? PlaytimeMinutes, Playtime2WeeksMinutes;
        public DateTime? LastPlayed, StatsSince;
        public DateTime FirstItem = DateTime.MaxValue, NewestItem = DateTime.MinValue;
        public readonly Dictionary<string, int> Stats = new Dictionary<string, int>();
        public readonly List<OwnedItem> Items = new List<OwnedItem>();
        public readonly List<KeyValuePair<string, int>> Categories = new List<KeyValuePair<string, int>>();
        public readonly List<SeasonInfo> Seasons = new List<SeasonInfo>();
        public readonly List<CrownReward> CrownRewards = new List<CrownReward>();
        public readonly List<RankedInfo> Ranked = new List<RankedInfo>();
        public readonly List<CreativeLevel> Levels = new List<CreativeLevel>();
        public readonly List<Achievement> Achievements = new List<Achievement>();
        public bool AchievementsLoaded;

        public int CrownsSpent => EarnedCrowns - WalletCrowns;
        public int Stat(string id) => Stats.TryGetValue(id, out int v) ? v : 0;
        public bool HasStat(string id) => Stats.ContainsKey(id);
        public int Favourites { get { int n = 0; foreach (var i in Items) if (i.Favourite) n++; return n; } }
        public int YearsPlaying => FirstItem == DateTime.MaxValue ? 0 : (int)((DateTime.Now - FirstItem).TotalDays / 365.25);
        public int PassesMaxed { get { int n = 0; foreach (var s in Seasons) if (s.TiersUnlocked == s.TiersTotal) n++; return n; } }
        public int PremiumSkipped { get { int n = 0; foreach (var s in Seasons) n += s.PremiumSkipped; return n; } }
        public int AchievementsDone { get { int n = 0; foreach (var a in Achievements) if (a.CompletedAt.HasValue) n++; return n; } }
        public int LevelPlays { get { int n = 0; foreach (var l in Levels) n += l.Plays; return n; } }
        public int LevelLikes { get { int n = 0; foreach (var l in Levels) n += l.Likes; return n; } }

        internal static AccountStatsData Read()
        {
            var cs = CatapultServices.Instance;
            var d = new AccountStatsData();

            var profile = cs.PlayerProfileService.TryCast<PlayerProfileService>().Profile;
            d.Name = profile.PlayerName;
            d.InternalName = profile.InternalAccountName.PlayerName;

            var wallet = cs.PlayerWalletService.TryCast<PlayerWalletService>().Wallet;
            d.WalletCrowns = wallet.Crowns;
            d.CrownShards = wallet.CrownShards;
            d.Kudos = wallet.Kudos;
            d.GemsGranted = wallet.GemsGranted;
            d.GemsPurchased = wallet.GemsPurchased;

            var stats = cs.PlayerStatsService.TryCast<PlayerStatsService>().Stats.Stats;
            int valueAt = Offset<PlayerStatDto>("<Value>k__BackingField"), sinceAt = Offset<PlayerStatDto>("<TrackedFrom>k__BackingField");
            for (int i = 0; i < stats.Count; i++)
            {
                var s = stats[i];
                var v = RawInt(s.Pointer, valueAt);
                if (v.HasValue) d.Stats[s.ContentId] = v.Value;
                var since = RawDate(s.Pointer, sinceAt);
                if (since.HasValue && (!d.StatsSince.HasValue || since.Value < d.StatsSince.Value)) d.StatsSince = since;
            }

            var level = cs.PlayerLevelService.TryCast<PlayerLevelService>().PlayerLevel;
            d.EarnedCrowns = level.EarnedCrowns;
            d.CrownRank = CrownRankManager.Instance.PlayerCrownRankLevel;
            d.MaxCrownRank = level.Levels.Count;
            d.ThisRankCrowns = d.CrownRank >= 2 ? level.Levels[d.CrownRank - 2].CrownsRequired : 0;
            d.NextRankCrowns = level.Levels[Math.Min(d.CrownRank, d.MaxCrownRank) - 1].CrownsRequired;
            int unlockedAt = Offset<PlayerLevelStateDto>("<UnlockedAt>k__BackingField");
            var rewards = CrownRankManager.Instance.CrownRankRewards;
            for (int i = 0; i < rewards.Count && i < level.Levels.Count; i++)
            {
                var reached = RawDate(level.Levels[i].Pointer, unlockedAt);
                if (reached.HasValue && reached.Value.Date <= CrownBackfill) reached = null;
                d.CrownRewards.Add(new CrownReward { Rank = i + 2, CrownsRequired = level.Levels[i].CrownsRequired, RewardId = rewards[i].LevelCollection.Reward.Id, ReachedAt = reached });
            }

            var season = cs.PlayerSeasonService.TryCast<PlayerSeasonService>().PlayerSeason;
            d.LifetimeFame = season.AccumulatedFame;
            for (int i = 0; i < season.PreviousSeasons.Count; i++) d.AddSeason(season.PreviousSeasons[i]);
            d.AddSeason(season.CurrentSeason);

            var col = cs.PlayerCosmeticsService.TryCast<PlayerCosmeticsService>().CosmeticsCollection;
            d.AddItems(col.UpperCostumePieces, "upper", x => x.EarnedAt, x => x.Item, x => x.IsFavourite);
            d.AddItems(col.LowerCostumePieces, "lower", x => x.EarnedAt, x => x.Item, x => x.IsFavourite);
            d.AddItems(col.MonolithicCostumes, "full", x => x.EarnedAt, x => x.Item, x => x.IsFavourite);
            d.AddItems(col.ColourSchemes, "colours", x => x.EarnedAt, x => x.Item, x => x.IsFavourite);
            d.AddItems(col.Patterns, "patterns", x => x.EarnedAt, x => x.Item, x => x.IsFavourite);
            d.AddItems(col.Faceplates, "faceplates", x => x.EarnedAt, x => x.Item, x => x.IsFavourite);
            d.AddItems(col.Emotes, "emotes", x => x.EarnedAt, x => x.Item, x => x.IsFavourite);
            d.AddItems(col.Emoticons, "emoticons", x => x.EarnedAt, x => x.Item, x => x.IsFavourite);
            d.AddItems(col.Phrases, "phrases", x => x.EarnedAt, x => x.Item, x => x.IsFavourite);
            d.AddItems(col.Punchlines, "punchlines", x => x.EarnedAt, x => x.Item, x => x.IsFavourite);
            d.AddItems(col.Nameplates, "nameplates", x => x.EarnedAt, x => x.Item, x => x.IsFavourite);
            d.AddItems(col.Nicknames, "nicknames", x => x.EarnedAt, x => x.Item, x => x.IsFavourite);
            d.AddItems(col.Plinths, "plinths", x => x.EarnedAt, x => x.Item, x => x.IsFavourite);

            var ranked = FGClient.RankedShows.RankedShowsManager.Instance._rankedScoresDtosDict;
            var keys = new Il2CppStringArray(ranked.Count);
            ranked.Keys.CopyTo(keys, 0);
            for (int i = 0; i < keys.Length; i++)
            {
                var list = ranked[keys[i]];
                for (int j = 0; j < list.Count; j++)
                {
                    var r = list[j];
                    d.Ranked.Add(new RankedInfo { Chain = r.Chainkey, Current = r.CurrentScore, High = r.HighScore, Started = new DateTime(r.StartedAt.Ticks) });
                    d.RankedBest = Math.Max(d.RankedBest, r.HighScore);
                }
            }

            var mine = LevelEditorMainMenuHandler._localLevelList;
            for (int i = 0; mine != null && i < mine.Count; i++)
            {
                var a = mine[i];
                var st = a.LevelStats;
                d.Levels.Add(new CreativeLevel { Code = a.ShareCode, Title = a.Title, Published = a.IsPublished, Versions = a.VersionCount, Plays = st != null ? st.PlayCount : 0, Likes = st != null ? st.LikesCount : 0 });
            }

            try { d.HasSteam = SteamRunning(); }
            catch (Exception ex) { Plugin.Log.LogInfo($"no steam api here ({ex.GetType().Name}), stats popup goes without playtime and achievement art"); }
            if (d.HasSteam)
            {
                d.ReadSteamPlaytime();
                WarmSteamIcons();
            }

            Plugin.Log.LogInfo($"account stats: {d.Stats.Count} counters, {d.Items.Count} items, {d.Seasons.Count} seasons, {d.Levels.Count} creative levels, playtime {d.PlaytimeMinutes?.ToString() ?? "?"} min");
            return d;
        }

        internal static int? CurrentStreak()
        {
            var dto = CatapultServices.Instance.PlayerStatsService.TryCast<PlayerStatsService>().Stats;
            if (dto == null) return null;
            int valueAt = Offset<PlayerStatDto>("<Value>k__BackingField");
            for (int i = 0; i < dto.Stats.Count; i++)
                if (dto.Stats[i].ContentId == "episodes_won_streak_current") return RawInt(dto.Stats[i].Pointer, valueAt) ?? 0;
            return 0;
        }

        internal void FetchAchievements(Action done)
        {
            var svc = CatapultServices.Instance.AchievementsService.TryCast<AchievementsService>();
            UniRx.ObservableExtensions.Subscribe(svc.GetAchievements(),
                new Action<Catapult.Protocol.Result<AchievementsDto>>(r => { if (r.IsSuccess) FillAchievements(r.Body.Achievements); AchievementsLoaded = true; done(); }),
                new Action<Il2CppSystem.Exception>(e => { Plugin.Log.LogWarning($"achievements request died: {e.Message}"); AchievementsLoaded = true; done(); }),
                new Action(() => { }));
        }

        void FillAchievements(Il2CppSystem.Collections.Generic.List<AchievementDto> list)
        {
            var cms = CMSLoader.Instance.CMSData.Achievements;
            int doneAt = Offset<AchievementDto>("<CompletedAt>k__BackingField");
            for (int i = 0; i < list.Count; i++)
            {
                var dto = list[i];
                var a = new Achievement { Id = dto.AchievementId, Progress = dto.Progress, CompletedAt = RawDate(dto.Pointer, doneAt), Name = dto.AchievementId.Replace('_', ' ') };
                if (HasSteam && cms.ContainsKey(dto.AchievementId)) FillSteam(a, cms[dto.AchievementId].SteamID);
                Achievements.Add(a);
            }
            Achievements.Sort((x, y) => x.CompletedAt.HasValue != y.CompletedAt.HasValue ? (x.CompletedAt.HasValue ? -1 : 1) : x.Percent.CompareTo(y.Percent));
        }

        internal void RefreshAchievementIcons()
        {
            foreach (var a in Achievements)
                if (a.Icon == null && a.SteamId != null) a.Icon = SteamIcon(a.SteamId);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool SteamRunning() => Steamworks.SteamAPI.IsSteamRunning();

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void FillSteam(Achievement a, string steamId)
        {
            a.SteamId = steamId;
            a.Name = Steamworks.SteamUserStats.GetAchievementDisplayAttribute(steamId, "name");
            a.Description = Steamworks.SteamUserStats.GetAchievementDisplayAttribute(steamId, "desc");
            if (Steamworks.SteamUserStats.GetAchievementAchievedPercent(steamId, out float pct)) a.Percent = pct;
        }

        static void WarmSteamIcons()
        {
            uint count = Steamworks.SteamUserStats.GetNumAchievements();
            for (uint i = 0; i < count; i++) Steamworks.SteamUserStats.GetAchievementIcon(Steamworks.SteamUserStats.GetAchievementName(i));
        }

        void ReadSteamPlaytime()
        {
            var path = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (path == null) return;
            var file = Path.Combine(path, "userdata", Steamworks.SteamUser.GetSteamID().GetAccountID().m_AccountID.ToString(), "config", "localconfig.vdf");
            if (!File.Exists(file)) return;
            var m = Regex.Match(File.ReadAllText(file), "\"1097150\"\\s*\\{[^{}]*?\"LastPlayed\"\\s*\"(\\d+)\"[^{}]*?\"playtime\"\\s*\"(\\d+)\"(?:[^{}]*?\"Playtime2wks\"\\s*\"(\\d+)\")?", RegexOptions.IgnoreCase);
            if (!m.Success) { Plugin.Log.LogInfo("steam localconfig has no fall guys playtime block, odd"); return; }
            LastPlayed = DateTimeOffset.FromUnixTimeSeconds(long.Parse(m.Groups[1].Value)).LocalDateTime;
            PlaytimeMinutes = int.Parse(m.Groups[2].Value);
            if (m.Groups[3].Success) Playtime2WeeksMinutes = int.Parse(m.Groups[3].Value);
        }

        void AddSeason(SeasonHistoryDto s)
        {
            var info = new SeasonInfo { Id = s.SeasonId, Fame = s.AccruedFame, TiersTotal = s.FameTiers.Count };
            for (int i = 0; i < s.FameTiers.Count; i++)
            {
                var t = s.FameTiers[i];
                if (t.IsUnlocked) info.TiersUnlocked++;
                if (t.LockedRewards != null) info.PremiumSkipped += t.LockedRewards.Count;
            }
            Seasons.Add(info);
        }

        void AddItems<T>(Il2CppSystem.Collections.Generic.List<T> list, string category, Func<T, Il2CppSystem.DateTime> earned, Func<T, ItemDto> item, Func<T, bool> favourite)
        {
            Categories.Add(new KeyValuePair<string, int>(category, list.Count));
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                var at = new DateTime(earned(o).Ticks);
                Items.Add(new OwnedItem { Id = item(o).ContentId, Category = category, EarnedAt = at, Favourite = favourite(o) });
                if (at.Year < 2000) continue;
                if (at < FirstItem) FirstItem = at;
                if (at > NewestItem) NewestItem = at;
            }
        }

        static int Offset<T>(string field) => (int)IL2CPP.il2cpp_field_get_offset(IL2CPP.il2cpp_class_get_field_from_name(Il2CppClassPointerStore<T>.NativeClassPtr, field));

        static int? RawInt(IntPtr obj, int offset) => Marshal.ReadByte(obj + offset) != 0 ? Marshal.ReadInt32(obj + offset + 4) : (int?)null;

        static DateTime? RawDate(IntPtr obj, int offset) => Marshal.ReadByte(obj + offset) != 0 ? new DateTime(Marshal.ReadInt64(obj + offset + 8) & 0x3FFFFFFFFFFFFFFF) : (DateTime?)null;

        [MethodImpl(MethodImplOptions.NoInlining)]
        static Texture2D SteamIcon(string steamId)
        {
            int handle = Steamworks.SteamUserStats.GetAchievementIcon(steamId);
            if (handle <= 0 || !Steamworks.SteamUtils.GetImageSize(handle, out uint w, out uint h)) return null;
            var buf = new Il2CppStructArray<byte>((int)(w * h * 4));
            if (!Steamworks.SteamUtils.GetImageRGBA(handle, buf, buf.Length)) return null;
            var raw = (byte[])buf;
            var flipped = new byte[raw.Length];
            int stride = (int)w * 4;
            for (int y = 0; y < h; y++) Buffer.BlockCopy(raw, y * stride, flipped, ((int)h - 1 - y) * stride, stride);
            var tex = new Texture2D((int)w, (int)h, TextureFormat.RGBA32, false);
            tex.LoadRawTextureData(flipped);
            tex.Apply();
            return tex;
        }

        internal void Dispose()
        {
            foreach (var a in Achievements) if (a.Icon != null) UnityEngine.Object.Destroy(a.Icon);
        }
    }
}
