using System;
using System.Collections.Generic;
using FallGuys.Client.Protocol.ShowSelector.GetShowSelector;
using FGClient;

namespace BetterFG.Tweaks
{
    public class UpcomingShowsTweak : BfgTweak
    {
        public UpcomingShowsTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "upcoming_shows";
        public override string TweakLabel => "tweak.show_upcoming_shows";
        public override bool DefaultEnabled => true;
        public override string TweakTooltip => "ui.adds_a_row_to_the_show_selector_for_scheduled_sh";

        public static UpcomingShowsTweak Instance { get; private set; }
        void Awake() => Instance = this;

        public static readonly HashSet<string> Injected = new HashSet<string>();

        public static void OnPageBuilt(ShowSelectorPage page)
        {
            var inst = Instance;
            if (inst == null || !inst.IsEnabled || page?.Sections == null || page.Sections.Count == 0) return;

            var already = new HashSet<string>();
            ShowSelectorSection featured = null;
            foreach (var existing in page.Sections)
            {
                if (existing == null || existing.Shows == null) continue;
                if (featured == null) featured = existing;
                if (existing.SectionId != null && existing.SectionId.IndexOf("featured", StringComparison.OrdinalIgnoreCase) >= 0)
                    featured = existing;
                foreach (var sd in existing.Shows)
                {
                    var id = sd?.ShowSelectorShow?.Id;
                    if (!string.IsNullOrEmpty(id)) already.Add(id);
                }
            }
            if (featured == null) return;

            var shows = Collect(already);
            if (shows.Count == 0) return;

            foreach (var show in shows)
            {
                var data = new ShowData();
                data._showSelectorShow = show;
                data.ShowSelectorEntryPoint = show;
                data.Index = featured.Shows.Count;
                featured.Shows.Add(data);
                Injected.Add(show.Id);
            }

            Plugin.Log.LogInfo($"{shows.Count} upcoming shows onto the end of {featured.SectionId} ({page.PageId})");
        }

        static List<ShowSelectorShow> Collect(HashSet<string> already)
        {
            var result = new List<ShowSelectorShow>();
            var sm = ShowsManager.Instance;
            var downloader = sm?._showsDataDownloader;
            if (downloader?.Shows == null) return result;

            var now = DateTime.UtcNow;
            var known = new Dictionary<string, ShowSelectorShow>();
            foreach (var sd in sm.ShowsDefCache)
            {
                var show = sd?.ShowSelectorShow;
                if (show != null && !string.IsNullOrEmpty(show.Id)) known[show.Id] = show;
            }
            foreach (var sd in sm.LiveShowDefCache)
            {
                var show = sd?.ShowSelectorShow;
                if (show != null && !string.IsNullOrEmpty(show.Id)) known[show.Id] = show;
            }

            var dated = new List<KeyValuePair<ShowSelectorShow, DateTime>>();
            var seen = new HashSet<string>();
            foreach (var dto in downloader.Shows)
            {
                var id = dto?.ShowId?.Value;
                if (string.IsNullOrEmpty(id) || dto.Schedule == null) continue;
                if (already.Contains(id) || !seen.Add(id)) continue;

                var opens = new DateTime(dto.Schedule.OpensAt.Ticks, DateTimeKind.Utc);
                if (opens <= now) continue;

                var show = known.TryGetValue(id, out var existing) ? existing : FromCms(sm, id, dto);
                if (show == null) continue;
                dated.Add(new KeyValuePair<ShowSelectorShow, DateTime>(show, opens));
            }

            dated.Sort((a, b) => a.Value.CompareTo(b.Value));
            foreach (var entry in dated) result.Add(entry.Key);
            return result;
        }

        static ShowSelectorShow FromCms(ShowsManager sm, string id, ShowDto dto)
        {
            FG.Common.CMS.Show cmsShow = null;
            if (sm._cmsData?.Shows == null || !sm._cmsData.Shows.TryGetValue(id, out cmsShow) || cmsShow == null)
                return null;

            var closes = new Il2CppSystem.Nullable<Il2CppSystem.DateTime>(dto.Schedule.ClosesAt);
            return new ShowSelectorShow(cmsShow, "", dto.Schedule.OpensAt, closes,
                new Il2CppSystem.Nullable<Il2CppSystem.DateTime>(), "", ShowsManager.GameMode.Public,
                false, "", null, "", null, "", "", "", "", ShowsManager.ShowSelectorScreen.LiveShows,
                "", false, null);
        }
    }
}
