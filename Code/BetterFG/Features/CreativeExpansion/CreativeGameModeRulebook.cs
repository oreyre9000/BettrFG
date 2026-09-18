using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using BetterFG.Core;
using BetterFG.Features.UnityRound.Editor;
using BetterFG.Services;
using BetterFG.Utilities;
using FallGuysLib.UI;
using FGClient;
using FGClient.UI;
using TMPro;
using UnityEngine;
using BettrFG.uGUI;

namespace BetterFG.Features.CreativeExpansion
{
    // Adds a "Game Mode" row to the creative editor's Rulebook (Settings) screen. Picking a new
    // mode can't hot-swap (geometry rules, cameras, kill plane, completion criteria all differ) and
    // the editor's save refuses a mid-session mode change, so instead we: confirm via the game's
    // popup, record the wanted mode against this level's share code, kick the user out of the
    // editor, and swap the mode id into the level JSON as it reloads.
    //
    // The row is a clone of a live HorizontalList rulebook row (Time Limit / Max Players etc).
    // BfgGameModeRow paints our label + value onto the row's text every frame, and a prefix on the
    // VM's OnIncrement/OnDecrement catches left/right on our row and stops the clone touching the
    // setting it came from.
    internal static class CreativeGameModeRulebook
    {
        internal static bool Enabled => FeatureCreativeExpansion.On("creativegamemode");

        internal readonly struct Mode
        {
            public readonly string Id;
            public readonly string Title;
            public Mode(string id, string title) { Id = id; Title = title; }
        }

        private static List<Mode> _modes;
        private static bool _stringsDone;
        private static BfgGameModeRow _live;
        private static readonly HashSet<int> _ourVmIds = new HashSet<int>();

        // the mode shown on the row right now, and the level's actual loaded mode. stepping just
        // moves _selectedIndex; the confirm popup only fires when the rulebook closes on a changed
        // value. _sessionMode is the loaded mode we last saw - when it changes the level has
        // reloaded, so we re-baseline; otherwise a rulebook rebuild keeps the user's selection.
        private static int _selectedIndex = -1;
        private static int _baselineIndex = -1;
        private static int _sessionMode = -1;
        private static int _lastCloseFrame = -1;

        private static List<Mode> Modes()
        {
            if (_modes != null) return _modes;
            _modes = new List<Mode>();
            try
            {
                var cfgs = global::GameModeManager.GameModeConfigs;
                if (cfgs == null) { Plugin.Log.LogWarning("game mode configs came back null"); return _modes; }

                var tmp = new List<(int prio, string id, string title)>();
                for (int i = 0; i < cfgs.Length; i++)
                {
                    var d = cfgs[i];
                    if (d == null) continue;
                    string id = d.ID;
                    if (string.IsNullOrEmpty(id)) continue;
                    bool enabled;
                    try { enabled = d.IsGameModeEnabled; } catch { enabled = true; }
                    if (!enabled) continue;
                    int prio;
                    try { prio = d.MenuPriority; } catch { prio = 0; }
                    tmp.Add((prio, id, Prettify(id)));
                }
                tmp.Sort((a, b) => a.prio != b.prio ? a.prio.CompareTo(b.prio) : string.CompareOrdinal(a.id, b.id));
                foreach (var t in tmp) _modes.Add(new Mode(t.id, t.title));
                Plugin.Log.LogInfo($"creative game modes available: {string.Join(", ", _modes.ConvertAll(m => m.Id + "=" + m.Title))}");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"couldn't read game mode configs: {ex.Message}"); }
            return _modes;
        }

        // "GAMEMODE_SLIME_CLIMB" / "slimeClimb" -> "Slime Climb"
        private static string Prettify(string id)
        {
            string s = id;
            if (s.StartsWith("GAMEMODE_", StringComparison.OrdinalIgnoreCase)) s = s.Substring("GAMEMODE_".Length);
            var sb = new StringBuilder(s.Length + 4);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '_' || c == '-') { if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' '); continue; }
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(s[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            var words = sb.ToString().Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return id;
            for (int i = 0; i < words.Length; i++)
                words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
            return string.Join(" ", words);
        }

        private static int CurrentIndex(List<Mode> modes)
        {
            string cur = LoadedModeId();
            if (string.IsNullOrEmpty(cur)) return 0;
            for (int i = 0; i < modes.Count; i++) if (modes[i].Id == cur) return i;
            return 0;
        }

        // the mode the level in the editor is actually running - the level's own GameModeData,
        // falling back to GameModeManager's global current.
        private static string LoadedModeId()
        {
            try
            {
                string id = FG.Common.LevelEditorManagerProxy.CurrentLevel?.GetGameMode()?.ID;
                if (!string.IsNullOrEmpty(id)) return id;
            }
            catch { }
            try { return global::GameModeManager.CurrentGameModeData?.ID; } catch { return null; }
        }

        private static void EnsureStrings()
        {
            if (_stringsDone) return;
            _stringsDone = true;
            NavPromptCore.RegisterCmsString("bfg_gm_title", "Change Game Mode");
            NavPromptCore.RegisterCmsString("bfg_gm_ok", "Change & Exit");
            NavPromptCore.RegisterCmsString("bfg_gm_cancel", "Keep Current");
        }

        internal static bool IsOurVm(int instanceId) => _ourVmIds.Contains(instanceId);

        internal static string SelectedTitle => TitleAt(_selectedIndex);

        // left/right on the row: just cycle through the modes, wrapping. no popup here.
        internal static void Cycle(int dir)
        {
            int n = Count;
            if (n < 2) return;
            if (_selectedIndex < 0) _selectedIndex = IndexOfCurrent();
            _selectedIndex = ((_selectedIndex + dir) % n + n) % n;
            _live?.Repaint();
            Plugin.Log.LogInfo($"game mode row -> {TitleAt(_selectedIndex)} (baseline {TitleAt(_baselineIndex)})");
        }

        // called each time the row is freshly built. re-baseline only when the loaded mode has
        // actually changed since last time (level reloaded); a plain rulebook rebuild keeps the
        // user's in-progress selection.
        private static void NoteBaseline()
        {
            int now = IndexOfCurrent();
            if (now != _sessionMode || _selectedIndex < 0)
            {
                _sessionMode = now;
                _baselineIndex = _selectedIndex = now;
            }
            else
            {
                _baselineIndex = now;
            }
            Plugin.Log.LogInfo($"game mode row baseline = {TitleAt(_baselineIndex)}, showing {TitleAt(_selectedIndex)}");
        }

        // called from the RulebookMenuCollectionBinding.HandleChanged postfix every time the screen
        // (re)builds its rows. Clone a live HorizontalList row, then register the clone in the
        // binding's _instances/_selectables (both List<GameObject>) so the carousel navigates onto it
        // just like a real entry.
        public static void InjectRow(global::RulebookMenuCollectionBinding binding)
        {
            try
            {
                if (!Enabled || binding == null) return;
                var modes = Modes();
                if (modes.Count < 2) return;

                var r = RulebookRowClone.Inject(binding, BfgGameModeRow.RowName);
                if (r.Clone == null) { Plugin.Log.LogWarning("game mode row inject found no parent/source row"); return; }

                if (r.Created)
                {
                    if (r.CloneVmId != 0) { _ourVmIds.Clear(); _ourVmIds.Add(r.CloneVmId); }
                    NoteBaseline();
                    var row = r.Clone.AddComponent<BfgGameModeRow>();
                    row.Bind(r.LabelTmp, r.ValueTmp);
                    _live = row;
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"game mode row inject blew up: {ex}"); }
        }

        // fired when the rulebook screen closes. if the row's mode was moved off the level's real
        // one, ask whether to actually apply it (which queues the swap + leaves the editor).
        public static void OnRulebookClosed(string via)
        {
            if (Time.frameCount == _lastCloseFrame) return;   // CloseScreen + OnClosed both fire
            _lastCloseFrame = Time.frameCount;

            int sel = _selectedIndex, baseline = _baselineIndex;
            _selectedIndex = baseline;   // reopen shows the real mode again unless we apply below
            Plugin.Log.LogInfo($"rulebook closed ({via}); mode row sel={TitleAt(sel)} baseline={TitleAt(baseline)}");

            if (sel < 0 || baseline < 0 || sel == baseline) return;
            var modes = Modes();
            if (sel >= modes.Count) return;
            var target = modes[sel];
            if (target.Id == SafeCurrentId()) return;

            EnsureStrings();
            NavPromptCore.RegisterCmsString("bfg_gm_body",
                $"Switch this level's game mode to \"{target.Title}\"?\n\nThe editor will close now. Re-open the level and it will load as {target.Title}. (The level file itself isn't touched - BettrFG swaps the mode in as the level loads.)");

            PopUp.ShowPopup("bfg_gm_title", "bfg_gm_body",
                PopupInteractionType.Query, UIModalMessage.ModalType.MT_OK_CANCEL, UIModalMessage.OKButtonType.CallToAction,
                ok => { if (ok) Apply(target.Id); },
                "bfg_gm_ok", "bfg_gm_cancel");
        }

        private static string SafeCurrentId() => LoadedModeId() ?? "";

        internal static int IndexOfCurrent() => CurrentIndex(Modes());
        internal static string TitleAt(int i)
        {
            var m = Modes();
            return (i >= 0 && i < m.Count) ? m[i].Title : "";
        }
        internal static int Count => Modes().Count;

        // The editor's own save chokes (JSON_Serialisation_Failed) if we mutate GameModeManager /
        // the level config live, so we don't. Instead: record the wanted mode against this level's
        // share code, kick the user out, and swap the id into the level JSON as it reloads (see
        // RewriteJsonForLoad + the CreateLevelLoaderFromDownloadedJSON prefix).
        private static void Apply(string id)
        {
            string code = null;
            try { code = CreativeRoundMemory.GetCurrentShareCode(); } catch { }

            if (string.IsNullOrEmpty(code))
            {
                Plugin.Log.LogWarning("this level has no share code yet - save + publish it once before changing the mode");
                NavPromptCore.RegisterCmsString("bfg_gm_nocode",
                    "Save and publish this level at least once first - BettrFG needs its share code to swap the game mode in when it loads.");
                PopUp.ShowPopup("bfg_gm_title", "bfg_gm_nocode",
                    PopupInteractionType.Warning, UIModalMessage.ModalType.MT_OK, UIModalMessage.OKButtonType.Default,
                    _ => { }, "bfg_gm_ok", "bfg_gm_ok");
                return;
            }

            StoreSet(code, id);
            Plugin.Log.LogInfo($"queued game mode {id} for level {code}; leaving the editor so it reloads");

            bool left = false;
            try
            {
                var proxy = FG.Common.LevelEditorManagerProxy.Instance;
                if (proxy != null) { proxy.LoadMenuScene(); left = true; Plugin.Log.LogInfo("LevelEditorManagerProxy.LoadMenuScene() to leave"); }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"LoadMenuScene failed: {ex.Message}"); }

            if (!left)
            {
                try
                {
                    LevelEditorOptionsViewModel opts = null;
                    foreach (var v in Resources.FindObjectsOfTypeAll<LevelEditorOptionsViewModel>()) { opts = v; break; }
                    if (opts != null) { opts.ExitEditor(); left = true; }
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"ExitEditor failed: {ex.Message}"); }
            }

            if (!left)
            {
                try { GlobalGameStateClient.Instance.ReturnToMainLobby(18, null); }
                catch (Exception ex) { Plugin.Log.LogError($"couldn't leave the editor at all: {ex.Message}"); }
            }
        }

        // ── per-level mode store (share code -> client id like GAMEMODE_SURVIVAL) ──────────

        private const string STORE_PREFIX = "creativegamemode.";
        private static string StoreKey(string code) => STORE_PREFIX + code;

        internal static string StoreGet(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            string v = SettingsService.Get(StoreKey(code), "");
            return string.IsNullOrEmpty(v) ? null : v;
        }

        private static void StoreSet(string code, string id) => SettingsService.Set(StoreKey(code), id ?? "");
        internal static void StoreClear(string code) { if (!string.IsNullOrEmpty(code)) SettingsService.Remove(StoreKey(code)); }

        // prefix hook on LevelLoader.CreateLevelLoaderFromDownloadedJSON. If this level's share code
        // has a queued mode, swap the mode id tokens in the raw JSON before the loader parses it.
        internal static void RewriteJsonForLoad(ref string json, string dtoShareCode)
        {
            try
            {
                if (!Enabled || string.IsNullOrEmpty(json)) return;

                string code = dtoShareCode;
                if (string.IsNullOrEmpty(code))
                {
                    var m = Regex.Match(json, "\"[Ss]hare[Cc]ode\"\\s*:\\s*\"([^\"]+)\"");
                    if (m.Success) code = m.Groups[1].Value;
                }
                if (string.IsNullOrEmpty(code)) return;

                string want = StoreGet(code);
                if (string.IsNullOrEmpty(want)) return;

                string wantArch = "gamemode_" + want.Substring(want.IndexOf('_') + 1).ToLowerInvariant();

                int hits = 0;
                foreach (var mode in Modes())
                {
                    if (mode.Id == want) continue;
                    string arch = "gamemode_" + mode.Id.Substring(mode.Id.IndexOf('_') + 1).ToLowerInvariant();
                    string before = json;
                    json = json.Replace("\"" + mode.Id + "\"", "\"" + want + "\"");
                    json = json.Replace("\"" + arch + "\"", "\"" + wantArch + "\"");
                    if (before != json) hits++;
                }

                if (hits > 0) Plugin.Log.LogInfo($"level {code}: swapped game mode to {want} in the loading json");
                else Plugin.Log.LogInfo($"level {code} wants {want} but its json had no other mode token to swap - key name may differ");
                // ponytail: blunt token swap on the mode id. If a level ever legitimately contains a
                // second mode's id elsewhere, tighten this to the real "GameModeID" json key.
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"game mode json rewrite failed: {ex.Message}"); }
        }
    }

    // Lives on the cloned rulebook row. Pure display: shows "Game Mode" + the currently selected
    // mode. Stepping cycles the selection (no popup); the confirm popup only fires on rulebook close.
    // Repaints every frame because the clone's own MVVM binding still points at the row it came from.
    internal sealed class BfgGameModeRow : MonoBehaviour
    {
        public const string RowName = "BFG_GameModeRow";

        private TMP_Text _label;
        private TMP_Text _value;

        public BfgGameModeRow(IntPtr ptr) : base(ptr) { }

        public void Bind(TMP_Text label, TMP_Text value)
        {
            _label = label;
            _value = value;
            Paint();
        }

        public void Step(int dir) => CreativeGameModeRulebook.Cycle(dir);
        public void Repaint() => Paint();

        private void Paint()
        {
            if (_label != null) UGUIShip.RelabelText(_label, "ui.game_mode");
            if (_value != null)
            {
                string t = CreativeGameModeRulebook.SelectedTitle;
                if (!string.IsNullOrEmpty(t) && _value.text != t) _value.text = t;
            }
        }

        private void LateUpdate() => Paint();
    }
}
