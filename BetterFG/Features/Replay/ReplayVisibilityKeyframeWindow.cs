using System;
using BetterFG.UI;
using UnityEngine;
using BettrFG.uGUI;

namespace BetterFG.Features.Replay
{
    internal class ReplayVisibilityKeyframeWindow
    {
        public static ReplayVisibilityKeyframeWindow Instance;

        const float ROW = ReplayWindowKit.ROW;
        const float PAD = ReplayWindowKit.PAD;

        readonly ReplayRecording _rec;
        readonly ReplayVisibilityKeyframe _kf;
        readonly Action _onClose;
        readonly RectTransform _root;
        readonly float _width;
        RectTransform _content;
        float _y;
        int _row;

        public ReplayVisibilityKeyframe Keyframe => _kf;

        ReplayVisibilityKeyframeWindow(Transform parent, Rect rect, ReplayRecording rec, ReplayVisibilityKeyframe kf, Action onClose)
        {
            _rec = rec;
            _kf = kf;
            _onClose = onClose;
            _width = rect.width;
            _root = UGUIShip.CreatePanel(parent, rect, Color.clear, "ReplayVisibilityKeyframeWindow");
            ReplayWindowKit.EditBackdrop(_root);
            Rebuild();
        }

        public static ReplayVisibilityKeyframeWindow Show(Transform parent, Rect rect, ReplayRecording rec, ReplayVisibilityKeyframe kf, Action onClose)
        {
            Instance?.Close();
            Instance = new ReplayVisibilityKeyframeWindow(parent, rect, rec, kf, onClose);
            return Instance;
        }

        public void Close()
        {
            if (Instance == this) Instance = null;
            ReplayWindowKit.Close(_root);
            _onClose?.Invoke();
        }

        void Rebuild()
        {
            if (_content != null) UnityEngine.Object.Destroy(_content.gameObject);
            _content = ReplayWindowKit.Content(_root);

            var stamp = TimeSpan.FromSeconds(_kf.time);
            ReplayWindowKit.Title(_content, _width, BetterFG.Services.LocalizationService.Format("ui.visibility_keyframe_fmt",
                stamp.Minutes, stamp.Seconds, stamp.Milliseconds));

            _y = ReplayWindowKit.HEAD + 4f;
            _row = 0;

            Line("ui.phrases_emoticons", _kf.showPhrases ? "ui.shown" : "ui.hidden", ToggleShowPhrases, ToggleShowPhrases);
            Line("ui.crown_ranks", ReplayVisibilityKeyframe.PlayersLabel(_kf.crowns), () => StepCrowns(-1), () => StepCrowns(1));

            if (_kf.crowns == ReplayVisibilityMode.Only)
            {
                _y += 2f;
                for (int i = 0; i < _kf.crownOnlyPlayers.Count; i++)
                    PlayerRow(_kf.crownOnlyPlayers[i], RemoveCrown);

                UGUIShip.CreateButton(_content, new Rect(PAD, _y, _width - PAD * 2f, ROW),
                    "ui.add_crown", ReplayWindowKit.BTN_GREEN, Color.white, UIScale.FS_SM, new Action(AddCrown));
                _y += ROW;
            }

            Line("ui.names", ReplayVisibilityKeyframe.PlayersLabel(_kf.names), () => StepNames(-1), () => StepNames(1));

            if (_kf.names == ReplayVisibilityMode.Only)
            {
                _y += 2f;
                for (int i = 0; i < _kf.nameOnlyPlayers.Count; i++)
                    PlayerRow(_kf.nameOnlyPlayers[i], RemoveName);

                UGUIShip.CreateButton(_content, new Rect(PAD, _y, _width - PAD * 2f, ROW),
                    "ui.add_name", ReplayWindowKit.BTN_GREEN, Color.white, UIScale.FS_SM, new Action(AddName));
                _y += ROW;
            }

            Line("ui.players", ReplayVisibilityKeyframe.PlayersLabel(_kf.players), () => StepPlayers(-1), () => StepPlayers(1));

            if (_kf.players == ReplayVisibilityMode.Only)
            {
                _y += 2f;
                for (int i = 0; i < _kf.onlyPlayers.Count; i++)
                    PlayerRow(_kf.onlyPlayers[i], RemovePlayer);

                UGUIShip.CreateButton(_content, new Rect(PAD, _y, _width - PAD * 2f, ROW),
                    "ui.add_player", ReplayWindowKit.BTN_GREEN, Color.white, UIScale.FS_SM, new Action(AddPlayer));
                _y += ROW;
            }

            if (_rec.ghosts.Count > 0)
                Line("ui.ghosts", _kf.showGhosts ? "ui.shown" : "ui.hidden", ToggleShowGhosts, ToggleShowGhosts);

            _y += 4f;
            UGUIShip.CreateLabel(_content, new Rect(PAD, _y, _width - PAD * 2f, ROW),
                "ui.x_deletes", UIScale.FS_SM - 1, ReplayWindowKit.HINT);
            _y += ROW;

            _root.sizeDelta = new Vector2(_width, _y + PAD);
        }

        void Line(string label, string value, Action prev, Action next)
        {
            ReplayWindowKit.Carousel(_content, _y, _width, _row, label, value, prev, next);
            _y += ROW;
            _row++;
        }

        void PlayerRow(uint playerId, Action<uint> onRemove)
        {
            ReplayWindowKit.Stripe(_content, _y, _width, _row);
            UGUIShip.CreateLabel(_content, new Rect(PAD, _y, _width - PAD * 2f - ROW - 4f, ROW),
                PlayerName(playerId), UIScale.FS_SM, Color.white);
            UGUIShip.CreateButton(_content, new Rect(_width - PAD - ROW, _y, ROW, ROW),
                "X", ReplayWindowKit.BTN_RED, Color.white, UIScale.FS_SM, () => onRemove(playerId));
            _y += ROW;
            _row++;
        }

        string PlayerName(uint playerId)
        {
            for (int i = 0; i < _rec.players.Count; i++)
                if (_rec.players[i].playerId == playerId)
                    return string.IsNullOrEmpty(_rec.players[i].name) ? ("Player " + playerId) : _rec.players[i].name;
            return "Player " + playerId;
        }

        void ToggleShowPhrases() { _kf.showPhrases = !_kf.showPhrases; Rebuild(); }

        void StepCrowns(int dir)
        {
            _kf.crowns = (ReplayVisibilityMode)ReplayWindowKit.Step((int)_kf.crowns, dir, 3, true);
            Rebuild();
        }

        void RemoveCrown(uint id)
        {
            _kf.crownOnlyPlayers.Remove(id);
            Rebuild();
        }

        void AddCrown() => ReplayViewer.Instance?.BeginCrownPick(_kf);

        void ToggleShowGhosts() { _kf.showGhosts = !_kf.showGhosts; Rebuild(); }

        void StepNames(int dir)
        {
            _kf.names = (ReplayVisibilityMode)ReplayWindowKit.Step((int)_kf.names, dir, 3, true);
            Rebuild();
        }

        void StepPlayers(int dir)
        {
            _kf.players = (ReplayVisibilityMode)ReplayWindowKit.Step((int)_kf.players, dir, 3, true);
            Rebuild();
        }

        void RemoveName(uint id)
        {
            _kf.nameOnlyPlayers.Remove(id);
            Rebuild();
        }

        void RemovePlayer(uint id)
        {
            _kf.onlyPlayers.Remove(id);
            Rebuild();
        }

        void AddName() => ReplayViewer.Instance?.BeginNamePick(_kf);
        void AddPlayer() => ReplayViewer.Instance?.BeginPlayerPick(_kf);
    }
}
