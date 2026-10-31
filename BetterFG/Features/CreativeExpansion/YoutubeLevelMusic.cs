using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Features.UnityRound;
using BetterFG.Services;
using BetterFG.UI.Windows.Creative;
using BetterFG.Utilities;
using FallGuysLib.UI;
using FG.Common;
using FGClient;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class YoutubeLevelMusic
    {
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        private const float MarkerY = 6000f;
        private static readonly Regex LinkId = new Regex(@"(?:v=|youtu\.be/|shorts/|embed/|live/)([A-Za-z0-9_-]{11})|^([A-Za-z0-9_-]{11})$");
        private const string RowName = "BFG_YoutubeTrackRow";
        private const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

        internal static bool Enabled => FeatureCreativeExpansion.On("youtubemusic");
        internal static string VideoId { get; private set; }
        internal static bool HasVideo => Enabled && VideoId != null;

        private static bool _touched;
        private static bool _live;
        private static bool _playtest;
        private static bool _building;
        private static bool _ready;
        private static int _ticket;
        private static int _rowVmId;
        private static TMP_Text _rowLabel;
        private static string _rowText;

        private static string ReadId()
        {
            var found = new System.Collections.Generic.SortedDictionary<float, string>();
            foreach (var lepo in LevelIO.PlaceableObjects)
            {
                if (lepo == null || !lepo.name.StartsWith(Definers.DefinerName, StringComparison.Ordinal)) continue;
                var p = lepo.transform.position;
                if (Mathf.Abs(p.y - MarkerY) > 1f) continue;
                BatchTargets.HideAndDecollide(lepo);
                found[p.z] = lepo.GetComponent<LevelEditorColourChangerParameter>().CurrentColourHexcode;
            }

            if (found.Count == 0)
                foreach (var m in IdentifierObjects.ReadRound(Definers.DefinerName))
                    if (Mathf.Abs(m.Position.y - MarkerY) <= 1f) found[m.Position.z] = m.Hex;
            if (found.Count != 3) return null;

            var bits = new StringBuilder(72);
            foreach (var hex in found.Values) bits.Append(Convert.ToString(Convert.ToInt32(hex.TrimStart('#'), 16), 2).PadLeft(24, '0'));
            var id = new StringBuilder(11);
            for (int i = 0; i < 66; i += 6) id.Append(Alphabet[Convert.ToInt32(bits.ToString(i, 6), 2)]);
            return id.ToString();
        }

        private static void WriteMarkers(string id)
        {
            var old = new System.Collections.Generic.List<LevelEditorPlaceableObject>();
            foreach (var lepo in LevelIO.PlaceableObjects)
                if (lepo != null && lepo.name.StartsWith(Definers.DefinerName, StringComparison.Ordinal) && Mathf.Abs(lepo.transform.position.y - MarkerY) <= 1f)
                    old.Add(lepo);
            foreach (var lepo in old) IdentifierObjects.Remove(lepo);
            if (id == null) return;

            var bits = new StringBuilder(72);
            foreach (char c in id) bits.Append(Convert.ToString(Alphabet.IndexOf(c), 2).PadLeft(6, '0'));
            string packed = bits.ToString().PadRight(72, '0');
            for (int i = 0; i < 3; i++)
                IdentifierObjects.Spawn(Definers.DefinerName, new Vector3(0f, MarkerY, i * 3f), Vector3.zero, Vector3.one,
                    "#" + Convert.ToInt32(packed.Substring(i * 24, 24), 2).ToString("X6"));
        }

        internal static void OnEditorLevelLoad(bool isStart)
        {
            if (isStart)
            {
                VideoId = null;
                _touched = false;
                return;
            }
            BeanMonitorService.Instance.StartCoroutine(AfterEditorLoad().WrapToIl2Cpp());
        }

        private static IEnumerator AfterEditorLoad()
        {
            yield return null;
            if (!Enabled || _touched) yield break;
            VideoId = ReadId();
            RefreshText();
            Plugin.Log.LogInfo(VideoId == null ? "editor level has no youtube song" : $"editor level carries youtube song {VideoId}");
            if (VideoId != null && _listener != null) ApplyToEditor();
        }

        internal static void OnPlayingStarted() => _live = true;

        internal static void OnRoundLoaded()
        {
            _live = false;
            _ticket++;
            if (!Enabled || GlobalGameStateClient.Instance?.IsInCreativeEditor == true) return;

            string id = ReadId();
            Plugin.Log.LogInfo(id == null ? "round has no youtube song" : $"round carries youtube song {id}");
            if (id == null) return;
            BeanMonitorService.Instance.StartCoroutine(OnlineSong(id, _ticket).WrapToIl2Cpp());
        }

        private static IEnumerator OnlineSong(string id, int ticket)
        {
            yield return Fetch(id, false, true, ticket).WrapToIl2Cpp();
            if (_ready && _live && ticket == _ticket) BetterFGUnityRounds.StartCustomMusicIfAny();
        }

        internal static void OnPlaytest()
        {
            if (!HasVideo) return;
            _playtest = true;
            _building = false;
            BeanMonitorService.Instance.StartCoroutine(PlaytestSong(VideoId, ++_ticket).WrapToIl2Cpp());
        }

        internal static void OnPlaytestEnd()
        {
            _playtest = false;
            _building = false;
            RoundMusicService.Stop();
        }

        private static AudioLevelEditorStateListener _listener;

        private static void StartBuild()
        {
            if (_playtest || VideoId == null) return;
            _building = true;
            BeanMonitorService.Instance.StartCoroutine(BuildSong(VideoId, ++_ticket).WrapToIl2Cpp());
        }

        internal static void OnBuildStart(AudioLevelEditorStateListener listener)
        {
            _listener = listener;
            if (!HasVideo) return;
            if (_building) RoundMusicService.SetPaused(false);
            else StartBuild();
        }

        internal static void OnBuildPause()
        {
            if (_building) RoundMusicService.SetPaused(true);
        }

        internal static void OnBuildStop()
        {
            if (!_building) return;
            _building = false;
            RoundMusicService.Stop();
        }

        internal static void OnGameplayStart(AudioLevelEditorStateListener listener)
        {
            if (!HasVideo) return;
            listener._preparationMusicEvent?.SetPaused(true);
        }

        private static void ApplyToEditor()
        {
            _building = false;
            if (VideoId != null)
            {
                StartBuild();
                return;
            }
            RoundMusicService.Stop();
        }

        private static IEnumerator BuildSong(string id, int ticket)
        {
            yield return Fetch(id, true, true, ticket).WrapToIl2Cpp();
            if (!_ready || !_building || ticket != _ticket) yield break;
            RoundMusicService.StartIfPending(EditingMusic);
            Plugin.Log.LogInfo("editor: youtube song playing");
        }

        private static IEnumerator PlaytestSong(string id, int ticket)
        {
            yield return Fetch(id, true, true, ticket).WrapToIl2Cpp();
            if (!_ready || !_playtest || ticket != _ticket) yield break;

            RoundMusicService.StartIfPending(PlayingMusic);
            Plugin.Log.LogInfo("playtest: youtube song queued, it comes in with the game's gameplay music");
        }

        private static EventInstanceReference EditingMusic() => _listener._editingMusicEvent;
        private static EventInstanceReference PlayingMusic() => _listener._playingMusicEvent;

        private static IEnumerator Fetch(string id, bool loud, bool stage, int ticket)
        {
            _ready = false;
            string file = Path.Combine(MenuMusicService.MusicDir, "youtube", id + ".m4a");

            if (!File.Exists(file))
            {
                string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BettrFG", "Tools", "yt-dlp.exe");
                if (!File.Exists(exe))
                {
                    Plugin.Log.LogInfo("no yt-dlp yet, grabbing it");
                    var req = UnityWebRequest.Get(YtDlpUrl);
                    yield return req.SendWebRequest();
                    bool got = req.result == UnityWebRequest.Result.Success;
                    if (got)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(exe));
                        File.WriteAllBytes(exe, req.downloadHandler.data);
                    }
                    req.Dispose();
                    if (!got) { Fail("yt-dlp download bounced", loud); yield break; }
                }

                Directory.CreateDirectory(Path.GetDirectoryName(file));
                var psi = new System.Diagnostics.ProcessStartInfo(exe, $"-f \"ba[ext=m4a]\" --no-playlist -o \"{file}\" \"https://www.youtube.com/watch?v={id}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };

                var err = new StringBuilder();
                System.Diagnostics.Process proc = null;
                string startError = null;
                try
                {
                    proc = System.Diagnostics.Process.Start(psi);
                    proc.ErrorDataReceived += (s, e) => err.AppendLine(e.Data);
                    proc.OutputDataReceived += (s, e) => { };
                    proc.BeginErrorReadLine();
                    proc.BeginOutputReadLine();
                }
                catch (Exception ex) { startError = ex.Message; }
                if (startError != null) { Fail(startError, loud); yield break; }

                while (!proc.HasExited) yield return null;
                int code = proc.ExitCode;
                proc.Dispose();

                if (code != 0 || !File.Exists(file)) { Fail($"yt-dlp exit {code}: {err}", loud); yield break; }
            }

            if (stage && ticket == _ticket) RoundMusicService.SetPending(File.ReadAllBytes(file), file);
            _ready = true;
        }

        private static void Fail(string why, bool loud)
        {
            Plugin.Log.LogWarning($"youtube song {VideoId}, no luck: {why}");
            if (loud) Notice("youtubemusic.fetch_failed");
        }

        private static void Notice(string bodyKey)
        {
            NavPromptCore.RegisterCmsString("bfg_yt_title", LocalizationService.Get("youtubemusic.popup_title"));
            NavPromptCore.SetCmsString("bfg_yt_body", LocalizationService.Get(bodyKey));
            PopUp.Info("bfg_yt_title", "bfg_yt_body", () => { });
        }

        internal static void OpenPopup()
        {
            PopUp.Input(LocalizationService.Get("youtubemusic.popup_title"), LocalizationService.Get("youtubemusic.popup_body"),
                VideoId == null ? "" : "https://youtu.be/" + VideoId, LocalizationService.Get("youtubemusic.popup_placeholder"), (ok, txt) =>
                {
                    if (!ok) return;
                    string link = txt.Trim();
                    if (link.Length == 0) { Set(null); return; }

                    var m = LinkId.Match(link);
                    if (!m.Success) { Notice("youtubemusic.bad_link"); return; }
                    Set(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
                });
        }

        private static void Set(string id)
        {
            VideoId = id;
            _touched = true;
            WriteMarkers(id);
            RefreshText();
            Plugin.Log.LogInfo(id == null ? "youtube song cleared" : $"youtube song set to {id}");
            if (_listener != null) ApplyToEditor();
            else if (id != null) BeanMonitorService.Instance.StartCoroutine(Fetch(id, true, false, _ticket).WrapToIl2Cpp());
        }

        internal static bool OnTickClick(LevelEditorRulebookEntryTickViewModel vm)
        {
            if (!Enabled) return true;
            if (vm.GetInstanceID() == _rowVmId) { OpenPopup(); return false; }
            if (VideoId != null && !string.IsNullOrEmpty(vm._musicEvent)) Set(null);
            return true;
        }

        private static void RefreshText() =>
            _rowText = VideoId == null ? LocalizationService.Get("youtubemusic.row") : LocalizationService.Format("youtubemusic.row_set", VideoId);

        internal static void Paint()
        {
            if (_rowLabel != null && _rowText != null && _rowLabel.text != _rowText) _rowLabel.text = _rowText;
        }

        internal static void InjectRow(global::RulebookMenuCollectionBinding binding)
        {
            if (!Enabled) return;
            var parent = binding._itemsParent;
            if (parent == null) return;

            LevelEditorRulebookEntryTickViewModel src = null;
            foreach (var t in parent.GetComponentsInChildren<LevelEditorRulebookEntryTickViewModel>(true))
                if (!string.IsNullOrEmpty(t._musicEvent)) { src = t; break; }
            if (src == null) return;

            var srcGo = src.transform.parent != null && binding._selectables.Contains(src.transform.parent.gameObject)
                ? src.transform.parent.gameObject : src.gameObject;

            GameObject clone = null;
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == RowName) { clone = parent.GetChild(i).gameObject; break; }

            if (clone == null)
            {
                if (!_touched) VideoId = ReadId();

                clone = UnityEngine.Object.Instantiate(srcGo, parent);
                clone.name = RowName;
                clone.transform.SetSiblingIndex(srcGo.transform.GetSiblingIndex());

                var vm = clone.GetComponentInChildren<LevelEditorRulebookEntryTickViewModel>(true);
                vm._musicEvent = string.Empty;
                if (vm.Ticked) vm.Ticked = false;
                _rowVmId = vm.GetInstanceID();

                foreach (var t in clone.GetComponentsInChildren<TMP_Text>(true))
                    if (!string.IsNullOrEmpty(t.text)) { _rowLabel = t; break; }

                clone.AddComponent<BfgYoutubeRow>();
                RefreshText();
            }

            RulebookRowClone.Register(binding, srcGo, clone, true);
            Paint();
        }
    }

    internal sealed class BfgYoutubeRow : MonoBehaviour
    {
        public BfgYoutubeRow(IntPtr ptr) : base(ptr) { }

        private void LateUpdate() => YoutubeLevelMusic.Paint();
    }

    [HarmonyPatch]
    internal static class YoutubeEditorAudioPatches
    {
        [HarmonyPatch(typeof(AudioLevelEditorStateListener), "OnStartBuildMusic")]
        [HarmonyPostfix]
        private static void StartBuild(AudioLevelEditorStateListener __instance) => YoutubeLevelMusic.OnBuildStart(__instance);

        [HarmonyPatch(typeof(AudioLevelEditorStateListener), "OnUnpauseBuildMusic")]
        [HarmonyPostfix]
        private static void UnpauseBuild(AudioLevelEditorStateListener __instance) => YoutubeLevelMusic.OnBuildStart(__instance);

        [HarmonyPatch(typeof(AudioLevelEditorStateListener), "OnPauseBuildMusic")]
        [HarmonyPostfix]
        private static void PauseBuild() => YoutubeLevelMusic.OnBuildPause();

        [HarmonyPatch(typeof(AudioLevelEditorStateListener), "OnStopBuildMusic")]
        [HarmonyPostfix]
        private static void StopBuild() => YoutubeLevelMusic.OnBuildStop();

        [HarmonyPatch(typeof(AudioLevelEditorStateListener), "OnStartGameplayMusic")]
        [HarmonyPostfix]
        private static void StartGameplay(AudioLevelEditorStateListener __instance) => YoutubeLevelMusic.OnGameplayStart(__instance);
    }

    [HarmonyPatch(typeof(LevelEditorRulebookEntryTickViewModel), nameof(LevelEditorRulebookEntryTickViewModel.OnClick))]
    internal static class YoutubeRowClickPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(LevelEditorRulebookEntryTickViewModel __instance) => YoutubeLevelMusic.OnTickClick(__instance);
    }
}
