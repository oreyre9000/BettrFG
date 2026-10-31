using System;
using System.Globalization;
using System.Text;
using BetterFG.Core;
using BetterFG.Features.UnityRound.Editor;
using BetterFG.Services;
using BetterFG.Utilities;
using FallGuysLib.UI;
using FG.Common;
using TMPro;
using UnityEngine;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class LevelStats
    {
        private const string RowName = "BFG_LevelStatsRow";
        private const string TitleKey = "bfg_levelstats_title";
        private const string BodyKey = "bfg_levelstats_body";

        private static int _rowVmId = int.MinValue;
        private static string _code;
        private static float _since = -1f;

        internal static bool IsOurVm(int instanceId) => instanceId == _rowVmId;

        internal static void InjectRow(global::RulebookMenuCollectionBinding binding)
        {
            Checkpoint();
            var parent = binding._itemsParent;
            var tags = parent.GetComponentInChildren<LevelEditorRulebookEntryTagsButtonViewModel>(true);
            if (tags == null) return;

            GameObject clone = null;
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == RowName) { clone = parent.GetChild(i).gameObject; break; }

            if (clone == null)
            {
                clone = UnityEngine.Object.Instantiate(tags.gameObject, parent);
                clone.name = RowName;
                clone.transform.SetSiblingIndex(tags.transform.GetSiblingIndex() + 1);
                _rowVmId = clone.GetComponent<LevelEditorRulebookEntryTagsButtonViewModel>().GetInstanceID();

                var label = clone.transform.Find("Text");
                UnityEngine.Object.DestroyImmediate(label.GetComponent<Mediatonic.Tools.MVVM.TMPTextBinding>());
                UnityEngine.Object.DestroyImmediate(label.GetComponent<LocalisedStaticLabel>());
                label.GetComponent<TMP_Text>().text = LocalizationService.Get("levelstats.button");

                var value = clone.transform.Find("Tags");
                UnityEngine.Object.DestroyImmediate(value.GetComponent<Mediatonic.Tools.MVVM.TextBinding>());
                value.GetComponent<TMP_Text>().text = "";
            }

            if (!binding._instances.Contains(clone)) binding._instances.Add(clone);
            if (!binding._selectables.Contains(clone)) binding._selectables.Add(clone);
        }

        internal static void SetInEditor(bool inEditor)
        {
            if (!inEditor)
            {
                Checkpoint();
                _since = -1f;
                _code = null;
                return;
            }
            if (_since >= 0f) { Checkpoint(); return; }
            _code = CreativeRoundMemory.GetCurrentShareCode();
            _since = Time.realtimeSinceStartup;
        }

        internal static void Checkpoint()
        {
            if (_since < 0f) return;
            _code ??= CreativeRoundMemory.GetCurrentShareCode();
            if (_code == null) return;
            float now = Time.realtimeSinceStartup;
            SettingsService.Set(TimeKey(_code), (Stored(_code) + now - _since).ToString(CultureInfo.InvariantCulture));
            Plugin.Log.LogDebug($"editor time {_code} +{now - _since:0}s");
            _since = now;
        }

        private static string TimeKey(string code) => "leveltime." + code;

        private static float Stored(string code)
            => float.TryParse(SettingsService.Get(TimeKey(code), "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 0f;

        internal static void Show()
        {
            var level = LevelEditorManagerProxy.CurrentLevel;
            var mgr = LevelEditorManager.Instance;
            var costs = mgr.CostManager;
            Checkpoint();
            string code = CreativeRoundMemory.GetCurrentShareCode();
            var span = TimeSpan.FromSeconds(code != null ? Stored(code) : 0f);

            var sb = new StringBuilder();
            Row(sb, "levelstats.time_editing", string.Format(LocalizationService.Get("levelstats.duration_fmt"), (int)span.TotalHours, span.Minutes));
            Row(sb, "levelstats.objects", costs._buildCosts.Count.ToString("N0"));
            Row(sb, "levelstats.budget", costs.UsedBuildPoints.ToString("N0") + " / " + costs.TotalBuildPoints.ToString("N0"));
            Row(sb, "levelstats.placed_session", mgr.NumObjectsPlacedInSession.ToString("N0"));

            var dto = level?.Dto;
            if (dto != null)
            {
                var stats = dto.LevelStats;
                if (stats != null)
                {
                    Row(sb, "levelstats.plays", stats.PlayCount.ToString("N0"));
                    Row(sb, "levelstats.likes", stats.LikesCount.ToString("N0"));
                }
                Row(sb, "levelstats.versions", dto.VersionCount.ToString("N0"));
                Row(sb, "levelstats.published", LocalizationService.Get(dto.IsPublished ? "ui.yes" : "ui.no"));
                string json = dto.SerializeObject(Newtonsoft.Json.Formatting.None);
                DateRow(sb, "levelstats.first_saved", json, json.IndexOf(DateKey, StringComparison.Ordinal));
                DateRow(sb, "levelstats.last_saved", json, json.LastIndexOf(DateKey, StringComparison.Ordinal));
            }

            NavPromptCore.RegisterCmsString(TitleKey, LocalizationService.Get("levelstats.title"));
            NavPromptCore.SetCmsString(BodyKey, sb.ToString().TrimEnd());
            PopUp.Info(TitleKey, BodyKey);
        }

        private static void Row(StringBuilder sb, string key, string value) => sb.Append(LocalizationService.Get(key)).Append(": ").AppendLine(value);

        private const string DateKey = "\"last_modified_date\":";

        private static void DateRow(StringBuilder sb, string key, string json, int at)
        {
            if (at < 0) return;
            string raw = JsonUtil.ReadString(json, at + DateKey.Length, out _);
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
                Row(sb, key, date.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        }
    }
}
