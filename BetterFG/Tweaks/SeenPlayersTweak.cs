using System;
using System.Collections.Generic;
using System.IO;
using BetterFG.Services;
using FG.Common;
using FGClient;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PlayerUtils = FallGuysLib.Players.PlayerUtils;

namespace BetterFG.Tweaks
{
    public class SeenPlayersTweak : BfgTweak
    {
        public SeenPlayersTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "seen_players";
        public override string TweakLabel => "tweak.seen_players";
        public override bool DefaultEnabled => true;
        public override string TweakTooltip => "seen_players.tooltip";

        const string SpectatorView = "UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates/SpectatorState/GameplaySpectatorViewModel/SafeArea/";

        static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BettrFG", "Settings", "seen_people.txt");
        static Dictionary<string, (int count, string episode)> _seen;

        CameraDirector _director;
        uint _target = uint.MaxValue;
        GameObject _pill;
        TextMeshProUGUI _text;

        public override void OnRoundStart()
        {
            StopWatching();
            Record();
        }

        public override void OnSpectatorMode()
        {
            GlobalGameStateClient.Instance.GameStateView.GetLiveClientGameManager(out ClientGameManager cgm);
            _director = cgm?.CameraDirector;
            _target = uint.MaxValue;
        }

        public override void OnStateChanged(GameStateMachine.IGameState newState) => StopWatching();

        public override void DisableTweak() => StopWatching();

        void Update()
        {
            if (_director == null) return;
            var sel = _director._selectedPlayerSpectatorCameraTarget;
            uint id = sel != null ? sel.PlayerID : uint.MaxValue;
            if (id == _target) return;
            _target = id;
            Show(id);
        }

        void StopWatching()
        {
            _director = null;
            if (_pill != null) _pill.SetActive(false);
        }

        static bool InMyParty(PlayerMetadata m, string myParty)
            => m.IsLocalPlayer || (!string.IsNullOrEmpty(myParty) && m.PartyId == myParty);

        static string MyParty()
        {
            var me = PlayerUtils.GetPlayerMetadata(PlayerUtils.GetLocalPlayerId());
            return me?.PartyId;
        }

        static void Record()
        {
            string episode = GlobalGameStateClient.Instance.CachedEpisodeGuid;
            if (string.IsNullOrEmpty(episode)) return;
            Load();
            string myParty = MyParty();
            bool changed = false;
            foreach (uint id in PlayerUtils.GetOtherPlayerIds())
            {
                var m = PlayerUtils.GetPlayerMetadata(id);
                if (m == null || string.IsNullOrEmpty(m.PlayerKey) || InMyParty(m, myParty)) continue;
                _seen.TryGetValue(m.PlayerKey, out var e);
                if (e.episode == episode) continue;
                _seen[m.PlayerKey] = (e.count + 1, episode);
                changed = true;
            }
            if (changed) Save();
        }

        void Show(uint id)
        {
            var m = id == uint.MaxValue ? null : PlayerUtils.GetPlayerMetadata(id);
            int before = 0;
            if (m != null && !InMyParty(m, MyParty()) && _seen != null && _seen.TryGetValue(m.PlayerKey ?? "", out var e))
                before = e.episode == GlobalGameStateClient.Instance.CachedEpisodeGuid ? e.count - 1 : e.count;
            if (before < 1)
            {
                if (_pill != null) _pill.SetActive(false);
                return;
            }
            if (_pill == null) Build();
            _text.text = LocalizationService.Get("seen_players.label") + " <color=#FFFFFF>" + before + "×</color>";
            _pill.SetActive(true);
        }

        void Build()
        {
            var tag = GameObject.Find(SpectatorView + "BumperActions/PB_UI_NameTag").transform;
            var nick = tag.Find("NameTagTextContainer/NameTagUserNicknameText").GetComponent<TextMeshProUGUI>();
            var sprite = GameObject.Find(SpectatorView + "GameplaySpectatorUltimatePartyFlow/BottomCenterContainer/NextRoundButton/Panel/Panel_Fill").GetComponent<Image>().sprite;

            _pill = new GameObject("BFG_SeenPill");
            _pill.transform.SetParent(tag, false);
            var ring = Panel(_pill, sprite, new Color(0.07f, 0.05f, 0.15f, 1f));
            var rt = ring.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.58f, 0f);
            rt.anchoredPosition = new Vector2(0f, -6f);
            rt.sizeDelta = new Vector2(176f, 42f);

            var fill = new GameObject("Fill");
            fill.transform.SetParent(_pill.transform, false);
            Stretch(Panel(fill, sprite, new Color(0.15f, 0.1f, 0.3f, 1f)).rectTransform, 2f, 2f);

            var txt = new GameObject("Text");
            txt.transform.SetParent(_pill.transform, false);
            _text = txt.AddComponent<TextMeshProUGUI>();
            _text.font = nick.font;
            _text.fontSharedMaterial = nick.fontSharedMaterial;
            _text.fontSize = 21f;
            _text.alignment = TextAlignmentOptions.Center;
            _text.color = new Color(1f, 0.85f, 0.3f);
            _text.raycastTarget = false;
            Stretch(_text.rectTransform, 8f, 0f);
        }

        static Image Panel(GameObject go, Sprite sprite, Color color)
        {
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static void Stretch(RectTransform rt, float x, float y)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(x, y);
            rt.offsetMax = new Vector2(-x, -y);
        }

        static void Load()
        {
            if (_seen != null) return;
            _seen = new Dictionary<string, (int, string)>(StringComparer.Ordinal);
            if (!File.Exists(FilePath)) return;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                var p = line.Split('\t');
                if (p.Length == 3 && int.TryParse(p[1], out int n)) _seen[p[0]] = (n, p[2]);
            }
        }

        static void Save()
        {
            var lines = new List<string>(_seen.Count);
            foreach (var kv in _seen) lines.Add(kv.Key + "\t" + kv.Value.count + "\t" + kv.Value.episode);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllLines(FilePath, lines);
        }
    }
}
