#if MACHINIMA
using System;
using System.Collections.Generic;
using BetterFG.Customization.Player;
using BetterFG.Services;
using BetterFG.UI;
using Character;
using FGClient;
using MPG.Utility;
using UnityEngine;
using BettrFG.uGUI;

namespace BetterFG.Features.Replay
{
    internal class MachinimaKeyWindow
    {
        public static MachinimaKeyWindow Instance;

        const float ROW = ReplayWindowKit.ROW;
        const float PAD = ReplayWindowKit.PAD;
        const float NUDGE = 0.25f;
        const float FRAME = 1f / 60f;

        readonly ReplayRecording _rec;
        readonly ReplayPlayer _player;
        readonly MachinimaKey _key;
        readonly Action _onClose;
        readonly RectTransform _root;
        readonly float _width;
        RectTransform _content;
        float _y;
        int _row;

        public MachinimaKey Key => _key;

        MachinimaKeyWindow(Transform parent, Rect rect, ReplayRecording rec, ReplayPlayer player, MachinimaKey key, Action onClose)
        {
            _rec = rec;
            _player = player;
            _key = key;
            _onClose = onClose;
            _width = rect.width;
            _root = UGUIShip.CreatePanel(parent, rect, Color.clear, "MachinimaKeyWindow");
            ReplayWindowKit.EditBackdrop(_root);
            Rebuild();
        }

        public static MachinimaKeyWindow Show(Transform parent, Rect rect, ReplayRecording rec, ReplayPlayer player, MachinimaKey key, Action onClose)
        {
            Instance?.Close();
            Instance = new MachinimaKeyWindow(parent, rect, rec, player, key, onClose);
            return Instance;
        }

        public void Close()
        {
            if (Instance == this) Instance = null;
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _onClose?.Invoke();
        }

        public void Rebuild()
        {
            MachinimaBaker.Bake(_player);
            if (_content != null) UnityEngine.Object.Destroy(_content.gameObject);
            _content = ReplayWindowKit.Content(_root);

            var move = _key as MachinimaMove;
            ReplayWindowKit.Title(_content, _width,
                move != null ? "machinima.move_window_title" : "machinima.action_window_title", Vector3.zero);

            _y = ReplayWindowKit.HEAD + 4f;
            _row = 0;

            Line("machinima.time", Stamp(_key.time), () => StepTime(-1), () => StepTime(1));

            if (move != null) BuildMove(move);
            else BuildAction((MachinimaAction)_key);

            _y += 4f;
            UGUIShip.CreateLabel(_content, new Rect(PAD, _y, _width - PAD * 2f, ROW),
                "ui.x_deletes", UIScale.FS_SM - 1, ReplayWindowKit.HINT);
            _y += ROW;

            _root.sizeDelta = new Vector2(_width, _y + PAD);
        }

        void BuildMove(MachinimaMove move)
        {
            ReplayWindowKit.Stripe(_content, _y, _width, _row);
            float half = (_width - PAD * 2f - 4f) * 0.5f;
            UGUIShip.CreateButton(_content, new Rect(PAD, _y, half, ROW),
                "machinima.place_at_camera", ReplayWindowKit.BTN_DARK, Color.white, UIScale.FS_SM,
                new Action(PlaceAtCamera));
            UGUIShip.CreateButton(_content, new Rect(PAD + half + 4f, _y, half, ROW),
                "machinima.place_with_mouse", ReplayWindowKit.BTN_GREEN, Color.white, UIScale.FS_SM,
                new Action(PlaceWithMouse));
            _y += ROW;
            _row++;

            Line("machinima.pos_x", move.position.x.ToString("0.00"), () => Nudge(-1, 0), () => Nudge(1, 0));
            Line("machinima.pos_y", move.position.y.ToString("0.00"), () => Nudge(-1, 1), () => Nudge(1, 1));
            Line("machinima.pos_z", move.position.z.ToString("0.00"), () => Nudge(-1, 2), () => Nudge(1, 2));

            Line("machinima.turn", move.faceMotion ? "machinima.turn_on" : "machinima.turn_off",
                ToggleTurn, ToggleTurn);
        }

        void ToggleTurn()
        {
            var move = (MachinimaMove)_key;
            move.faceMotion = !move.faceMotion;
            ReplayViewer.Instance?.RefreshFromWindow();
            Rebuild();
        }

        void BuildAction(MachinimaAction action)
        {
            Line("machinima.kind", MachinimaAction.KindLabel(action.kind), () => StepKind(-1), () => StepKind(1));
            Line("machinima.duration", LocalizationService.Format("machinima.duration_fmt", action.duration.ToString("0.00")),
                () => StepDuration(-1), () => StepDuration(1));

            if (action.kind == MachinimaActionKind.GrabPlayer)
                Line("machinima.target", TargetName(action), () => StepTarget(-1), () => StepTarget(1));
            else if (action.kind == MachinimaActionKind.GrabObject)
                Line("machinima.target", ObjectName(action), () => StepObject(-1), () => StepObject(1));
            else if (action.kind == MachinimaActionKind.Phrase)
                Line("machinima.phrase", PhraseLabel(action.payload), () => StepPhrase(-1), () => StepPhrase(1));
            else if (action.kind == MachinimaActionKind.Emote)
                Line("machinima.emote", EmoteLabel(action.payload), () => StepEmote(-1), () => StepEmote(1));
        }

        void Line(string label, string value, Action prev, Action next)
        {
            ReplayWindowKit.Carousel(_content, _y, _width, _row, label, value, prev, next);
            _y += ROW;
            _row++;
        }

        static string Stamp(float seconds)
        {
            var t = TimeSpan.FromSeconds(Mathf.Max(seconds, 0f));
            return string.Format("{0:D2}:{1:D2}.{2:D3}", t.Minutes, t.Seconds, t.Milliseconds);
        }

        void StepTime(int dir)
        {
            _key.time = Mathf.Clamp(_key.time + dir * FRAME, _player.inTime, _player.outTime);
            _player.actions.Sort((a, b) => a.time.CompareTo(b.time));
            ReplayViewer.Instance?.SeekFromWindow(_key.time);
            Rebuild();
        }

        void PlaceWithMouse()
        {
            var move = (MachinimaMove)_key;
            Close();
            ReplayViewer.Instance?.BeginMovePick(move);
        }

        void PlaceAtCamera()
        {
            var spot = ReplayViewer.Instance?.CameraGroundPoint() ?? Vector3.zero;
            ((MachinimaMove)_key).position = spot;
            ReplayViewer.Instance?.RefreshFromWindow();
            Rebuild();
        }

        void Nudge(int dir, int axis)
        {
            var move = (MachinimaMove)_key;
            var p = move.position;
            if (axis == 0) p.x += dir * NUDGE;
            else if (axis == 1) p.y += dir * NUDGE;
            else p.z += dir * NUDGE;
            move.position = p;
            ReplayViewer.Instance?.RefreshFromWindow();
            Rebuild();
        }

        void StepKind(int dir)
        {
            var action = (MachinimaAction)_key;
            action.kind = (MachinimaActionKind)ReplayWindowKit.Step((int)action.kind, dir, 6, true);
            action.payload = "";
            Rebuild();
        }

        void StepDuration(int dir)
        {
            var action = (MachinimaAction)_key;
            action.duration = ReplayWindowKit.Step(action.duration, dir, 0.25f, 0.25f, 20f, false);
            ReplayViewer.Instance?.RefreshFromWindow();
            Rebuild();
        }

        string TargetName(MachinimaAction action)
        {
            for (int i = 0; i < _rec.players.Count; i++)
                if (_rec.players[i].playerId == action.targetPlayerId)
                    return string.IsNullOrEmpty(_rec.players[i].name)
                        ? LocalizationService.Format("machinima.player_fallback_fmt", action.targetPlayerId)
                        : _rec.players[i].name;
            return LocalizationService.Get("machinima.no_target");
        }

        void StepTarget(int dir)
        {
            var action = (MachinimaAction)_key;
            var others = new List<ReplayPlayer>();
            foreach (var p in _rec.players) if (p != _player) others.Add(p);
            if (others.Count == 0) return;

            int at = -1;
            for (int i = 0; i < others.Count; i++) if (others[i].playerId == action.targetPlayerId) at = i;
            at = ReplayWindowKit.Step(at < 0 ? 0 : at, dir, others.Count, true);
            action.targetPlayerId = others[at].playerId;
            Rebuild();
        }

        string ObjectName(MachinimaAction action)
        {
            if (action.targetObject < 0 || action.targetObject >= _rec.worldObjects.Count)
                return LocalizationService.Get("machinima.no_target");
            string path = _rec.worldObjects[action.targetObject].path ?? "";
            int cut = path.LastIndexOf('/');
            return cut >= 0 ? path.Substring(cut + 1) : path;
        }

        void StepObject(int dir)
        {
            var action = (MachinimaAction)_key;
            if (_rec.worldObjects.Count == 0) return;
            action.targetObject = ReplayWindowKit.Step(Mathf.Max(action.targetObject, 0), dir, _rec.worldObjects.Count, true);
            Rebuild();
        }

        static List<string> _phraseIds;

        static List<string> PhraseIds()
        {
            if (_phraseIds != null) return _phraseIds;
            _phraseIds = new List<string>();

            var lookup = SingletonBehaviour<SpeechOptionsManager>.Instance?._speechOptionsLookup;
            if (lookup == null)
            {
                Plugin.Log.LogWarning("no speech options loaded yet, machinima phrases have nothing to pick from");
                return _phraseIds;
            }

            foreach (var kvp in lookup)
            {
                var option = kvp.Value;
                if (option == null || option.TryCast<ImageSpeechOption>() == null) continue;
                string id = FeatureReplay.ItemId(option);
                if (!string.IsNullOrEmpty(id) && !_phraseIds.Contains(id)) _phraseIds.Add(id);
            }
            _phraseIds.Sort(StringComparer.OrdinalIgnoreCase);
            Plugin.Log.LogInfo($"{_phraseIds.Count} phrases available to machinima actions");
            return _phraseIds;
        }

        static string PhraseLabel(string id) =>
            string.IsNullOrEmpty(id) ? LocalizationService.Get("machinima.no_phrase") : Tail(id);

        static string Tail(string id)
        {
            int cut = id.LastIndexOf('.');
            return cut >= 0 && cut < id.Length - 1 ? id.Substring(cut + 1) : id;
        }

        void StepPhrase(int dir)
        {
            var action = (MachinimaAction)_key;
            var ids = PhraseIds();
            if (ids.Count == 0) return;
            int at = ids.IndexOf(action.payload ?? "");
            at = ReplayWindowKit.Step(at < 0 ? 0 : at, dir, ids.Count, true);
            action.payload = ids[at];
            Rebuild();
        }

        List<string> EmoteFiles()
        {
            var files = new List<string>();
            foreach (var entry in _player.bfgSkins)
                if (SkinTypeParser.FromString(entry.type) == SkinType.Emote) files.Add(entry.file);
            return files;
        }

        string EmoteLabel(string file) =>
            string.IsNullOrEmpty(file) ? LocalizationService.Get("machinima.no_emote") : file;

        void StepEmote(int dir)
        {
            var action = (MachinimaAction)_key;
            var files = EmoteFiles();
            if (files.Count == 0) return;
            int at = files.IndexOf(action.payload ?? "");
            at = ReplayWindowKit.Step(at < 0 ? 0 : at, dir, files.Count, true);
            action.payload = files[at];
            Rebuild();
        }
    }
}
#endif
