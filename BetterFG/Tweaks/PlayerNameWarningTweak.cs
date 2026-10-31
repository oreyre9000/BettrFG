using System;
using System.Collections.Generic;
using BetterFG.Services;
using BetterFG.UI.Windows;
using FallGuysLib.Players;
using FallGuysLib.UI;
using FG.Common.CMS;
using FGClient.UI;
using UnityEngine;
using static FGClient.UI.UIModalMessage;

namespace BetterFG.Tweaks
{
    public class PlayerNameWarningTweak : BfgTweak
    {
        public PlayerNameWarningTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "player_name_warning";
        public override string TweakLabel => "tweak.warn_on_player_names";
        public override bool DefaultEnabled => true;

        public static PlayerNameWarningTweak Instance { get; private set; }
        void Awake() => Instance = this;

        public struct NameRule
        {
            public bool Exact;
            public string Text;
        }

        public static readonly NameRule[] DefaultRules =
        {
            new NameRule { Exact = false, Text = "size>" },
            new NameRule { Exact = false, Text = "scale>" },
            new NameRule { Exact = false, Text = "<" },
            new NameRule { Exact = false, Text = ">" },
            new NameRule { Exact = false, Text = "\\u003" },
        };

        private const string CountKey = "tweak.player_name_warning.rule.count";
        private const string TitleKey = "bfg_namewarning_title";

        public override List<TweakButton> GetCustomButtons() => new List<TweakButton>
        {
            new TweakButton { Label = "ui.configure", Width = 30f, OnClick = OpenConfig }
        };

        private void OpenConfig()
        {
            if (PlayerNameWarningConfigWindow.Instance != null)
            {
                PlayerNameWarningConfigWindow.Instance.Close();
                return;
            }

            var go = new GameObject("BetterFG_PlayerNameWarningConfigWindow");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<PlayerNameWarningConfigWindow>().Configure(this);
        }

        // called from the shared RoundLoader.CleanupLoadingScreens hub in GameStatePatches.
        public static void OnCleanupLoadingScreens()
        {
            if (Instance == null || !Instance.IsEnabled) return;
            Instance.CheckPlayers();
        }

        private void CheckPlayers()
        {
            try
            {
                var rules = LoadRules();
                if (rules.Count == 0) return;

                var cpm = PlayerUtils.GetClientPlayerManager();
                if (cpm?._playerIdIndex == null) return;

                var matched = new List<string>();
                foreach (var kvp in cpm._playerIdIndex)
                {
                    var data = kvp.Value;
                    if (data?.fgcc == null || data.fgcc.IsLocalPlayer) continue;
                    var name = PlayerUtils.CleanPlayerName(data.playerKey ?? "");
                    if (string.IsNullOrEmpty(name)) continue;
                    if (!MatchesAnyRule(name, rules)) continue;
                    if (!matched.Contains(name)) matched.Add(name);
                }

                if (matched.Count == 0) return;
                Plugin.Log?.LogWarning("PlayerNameWarningTweak: flagged " + matched.Count + " name(s) this round");
                ShowWarning(matched);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError("PlayerNameWarningTweak: check failed " + ex);
            }
        }

        private static bool MatchesAnyRule(string name, List<NameRule> rules)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (string.IsNullOrEmpty(rule.Text)) continue;
                if (rule.Exact)
                {
                    if (string.Equals(name, rule.Text, StringComparison.OrdinalIgnoreCase)) return true;
                }
                else if (name.IndexOf(rule.Text, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static void ShowWarning(List<string> matched)
        {
            var strings = CMSLoader.Instance._localisedStrings;
            if (!strings._localisedStrings.ContainsKey(TitleKey))
                strings._localisedStrings.Add(TitleKey, "Flagged player name");

            // fresh key every popup — the body is the match list, which changes call to call.
            string msgKey = "bfg_namewarning_msg_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            strings._localisedStrings.Add(msgKey, "Leave this match?\n" + string.Join("\n", matched));

            PopUp.ShowPopup(TitleKey, msgKey, PopupInteractionType.Query, ModalType.MT_OK_CANCEL, OKButtonType.Disruptive,
                (Action<bool>)(ok =>
                {
                    if (!ok) return;
                    LeaveOnLoadingScreenTweak.LeaveMatch();
                }));
        }

        public static List<NameRule> LoadRules()
        {
            if (!int.TryParse(SettingsService.Get(CountKey, ""), out int count))
                return new List<NameRule>(DefaultRules);

            var rules = new List<NameRule>();
            for (int i = 0; i < count; i++)
            {
                string text = SettingsService.Get(TextKey(i), "").Trim();
                if (string.IsNullOrEmpty(text)) continue;
                bool exact = SettingsService.Get(ModeKey(i), "C") == "E";
                rules.Add(new NameRule { Exact = exact, Text = text });
            }

            return rules;
        }

        public static void SaveRules(IList<NameRule> rules)
        {
            int oldCount = int.TryParse(SettingsService.Get(CountKey, "0"), out int old) ? old : 0;
            int count = rules?.Count ?? 0;
            SettingsService.Set(CountKey, count.ToString());

            for (int i = 0; i < count; i++)
            {
                SettingsService.Set(TextKey(i), rules[i].Text ?? "");
                SettingsService.Set(ModeKey(i), rules[i].Exact ? "E" : "C");
            }
            for (int i = count; i < oldCount; i++)
            {
                SettingsService.Remove(TextKey(i));
                SettingsService.Remove(ModeKey(i));
            }
        }

        private static string ModeKey(int idx) => "tweak.player_name_warning.rule." + idx + ".mode";
        private static string TextKey(int idx) => "tweak.player_name_warning.rule." + idx + ".text";
    }
}
