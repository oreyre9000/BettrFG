#if MACHINIMA
using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Player;
using BetterFG.Services;
using BetterFG.UI;
using BetterFG.Utilities;
using FG.Common.Character;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.Features.Replay
{
    public partial class ReplayViewer
    {
        void SeedMachinima()
        {
            if (!Machinima) return;
            foreach (var p in _rec.players)
                if (p.playerId >= _nextPlayerId) _nextPlayerId = p.playerId + 1;
        }

        uint _nextPlayerId = 1;
        const int PLAYER_ROWS_VISIBLE = 4;
        const float PLAYER_BAR_W = 8f;

        readonly List<RectTransform> _zoneBars = new List<RectTransform>();
        readonly List<Text> _zoneLabels = new List<Text>();
        int _playerScroll;
        RectTransform _playerBarFill;
        bool _draggingPlayerBar;
        ReplayPlayer _dragZone;
        int _dragZoneMode;
        float _dragZoneGrabTime;
        float _dragZoneInAt, _dragZoneOutAt;
        ReplayPlayer _clickedZone;
        float _clickedZoneAt;
        ReplayPlayer _highlightedPlayer;

        readonly List<MachinimaKey> _selKeys = new List<MachinimaKey>();
        MachinimaKey _dragKey;
        float _dragKeyGrabMouseX;
        bool _dragKeyMoved;
        MachinimaKey _clickedKey;
        float _clickedKeyAt;
        RectTransform _moveTicks, _actionTicks;

        static readonly Color ZONE_IDLE = new Color(0.28f, 0.55f, 0.78f, 0.85f);
        static readonly Color ZONE_SEL = new Color(1f, 0.82f, 0.25f, 0.9f);

        int VisiblePlayerRows => Mathf.Min(_rec.players.Count, PLAYER_ROWS_VISIBLE);

        float ZoneRowY(int index) => PlayerLanesTop + index * (LANE_H + LANE_GAP);

        void BuildMachinimaLanes(RectTransform bar, float w, float trackW)
        {
            _zoneBars.Clear();
            _zoneLabels.Clear();
            _playerBarFill = null;
            _moveTicks = null;
            _actionTicks = null;
            if (!Machinima) return;

            if (_solo != null)
            {
                UGUIShip.CreateButton(bar, new Rect(PAD, PAD, 54f, HEADER_H - 4f),
                    "machinima.back", BTN_DARK, Color.white, UIScale.FS_SM, new Action(ExitSolo));
                UGUIShip.CreateLabel(bar, new Rect(PAD + 60f, PAD, 240f, HEADER_H),
                    PlayerLabel(_solo), UIScale.FS, Color.white);
                _playLabel.gameObject.SetActive(false);

                var moveGo = new GameObject("MachinimaMoveTicks");
                _moveTicks = moveGo.AddComponent<RectTransform>();
                _moveTicks.SetParent(bar, false);
                UGUIShip.SetPixelRect(_moveTicks, new Rect(0f, 0f, w, LANES_BOTTOM));

                var actionGo = new GameObject("MachinimaActionTicks");
                _actionTicks = actionGo.AddComponent<RectTransform>();
                _actionTicks.SetParent(bar, false);
                UGUIShip.SetPixelRect(_actionTicks, new Rect(0f, 0f, w, LANES_BOTTOM));
                return;
            }

            int rows = VisiblePlayerRows;
            bool bar1 = _rec.players.Count > rows;
            _playerScroll = Mathf.Clamp(_playerScroll, 0, Mathf.Max(0, _rec.players.Count - rows));
            float laneW = trackW - (bar1 ? PLAYER_BAR_W + 2f : 0f);

            for (int i = 0; i < rows; i++)
            {
                float y = ZoneRowY(i);
                UGUIShip.CreatePanel(bar, new Rect(_trackLeft, y, laneW, LANE_H), LANE_BG, "PlayerLane")
                    .GetComponent<Image>().raycastTarget = false;
                _zoneLabels.Add(UGUIShip.CreateLabel(bar, new Rect(PAD, y, LANE_NAME_W - 6f, LANE_H),
                    "", UIScale.FS_SM - 1, HINT, TextAnchor.MiddleRight));

                var zone = UGUIShip.CreatePanel(bar, new Rect(_trackLeft, y + 3f, 10f, LANE_H - 6f),
                    ZONE_IDLE, "PlayerZone");
                var zoneImg = zone.GetComponent<Image>();
                zoneImg.raycastTarget = false;
                UGUIShip.ApplyDeluxColorFill(zoneImg, 2f);
                _zoneBars.Add(zone);
            }

            if (!bar1) return;

            float barX = _trackLeft + trackW - PLAYER_BAR_W;
            UGUIShip.CreatePanel(bar, new Rect(barX, PlayerLanesTop, PLAYER_BAR_W, PlayerLanesH),
                new Color(0f, 0f, 0f, 0.25f), "PlayerScrollTrack").GetComponent<Image>().raycastTarget = false;
            _playerBarFill = UGUIShip.CreatePanel(bar, new Rect(barX, PlayerLanesTop, PLAYER_BAR_W, PlayerLanesH),
                new Color(EDGE_BLUE.r, EDGE_BLUE.g, EDGE_BLUE.b, 0.8f), "PlayerScrollFill");
            var fillImg = _playerBarFill.GetComponent<Image>();
            fillImg.raycastTarget = false;
            UGUIShip.ApplyDeluxColorFill(fillImg, 2f);
        }

        void ScrollPlayers(int delta)
        {
            int max = Mathf.Max(0, _rec.players.Count - VisiblePlayerRows);
            int next = Mathf.Clamp(_playerScroll + delta, 0, max);
            if (next == _playerScroll) return;
            _playerScroll = next;
            SyncUi();
        }

        void DragPlayerBar(float cursorY)
        {
            int max = Mathf.Max(0, _rec.players.Count - VisiblePlayerRows);
            float f = Mathf.Clamp01((cursorY - PlayerLanesTop) / Mathf.Max(PlayerLanesH, 1f));
            _playerScroll = Mathf.Clamp(Mathf.RoundToInt(f * max), 0, max);
        }

        static string PlayerLabel(ReplayPlayer p) =>
            string.IsNullOrEmpty(p.name) ? LocalizationService.Format("machinima.player_fallback_fmt", p.playerId) : p.name;

        void RefreshMachinimaLanes()
        {
            if (!Machinima) return;

            if (_solo != null)
            {
                RefreshKeyTicks(_moveTicks, MoveKeys(), FX_KEYROW_Y);
                RefreshKeyTicks(_actionTicks, ActionKeys(), KEYROW_Y);
                return;
            }

            int max = Mathf.Max(0, _rec.players.Count - VisiblePlayerRows);
            _playerScroll = Mathf.Clamp(_playerScroll, 0, max);

            for (int i = 0; i < _zoneBars.Count; i++)
            {
                int index = _playerScroll + i;
                if (index >= _rec.players.Count) { _zoneBars[i].gameObject.SetActive(false); continue; }
                if (!_zoneBars[i].gameObject.activeSelf) _zoneBars[i].gameObject.SetActive(true);

                var p = _rec.players[index];
                float x0 = TrackX(p.inTime);
                float x1 = TrackX(p.outTime);
                UGUIShip.SetPixelRect(_zoneBars[i],
                    new Rect(Mathf.Min(x0, x1), ZoneRowY(i) + 3f, Mathf.Max(Mathf.Abs(x1 - x0), 3f), LANE_H - 6f));
                _zoneBars[i].GetComponent<Image>().color = p == _dragZone || p == _highlightedPlayer ? ZONE_SEL : ZONE_IDLE;
                if (i < _zoneLabels.Count) _zoneLabels[i].text = PlayerLabel(p);
            }

            if (_playerBarFill == null) return;

            float span = PlayerLanesH;
            float thumb = Mathf.Max(span * VisiblePlayerRows / Mathf.Max(_rec.players.Count, 1), 12f);
            float travel = span - thumb;
            float at = max > 0 ? travel * _playerScroll / max : 0f;
            UGUIShip.SetPixelRect(_playerBarFill,
                new Rect(_trackLeft + _trackWidth - PLAYER_BAR_W, PlayerLanesTop + at, PLAYER_BAR_W, thumb));
        }

        List<MachinimaKey> MoveKeys()
        {
            var list = new List<MachinimaKey>(_solo.moves.Count);
            foreach (var m in _solo.moves) list.Add(m);
            return list;
        }

        List<MachinimaKey> ActionKeys()
        {
            var list = new List<MachinimaKey>(_solo.actions.Count);
            foreach (var a in _solo.actions) list.Add(a);
            return list;
        }

        void RefreshKeyTicks(RectTransform parent, List<MachinimaKey> keys, float rowY)
        {
            if (parent == null) return;
            if (parent.childCount != keys.Count)
            {
                for (int i = parent.childCount - 1; i >= 0; i--)
                    DestroyImmediate(parent.GetChild(i).gameObject);
                for (int i = 0; i < keys.Count; i++) BuildKeyframeTick(parent);
            }

            for (int i = 0; i < keys.Count && i < parent.childCount; i++)
            {
                float f = TimeToFraction(keys[i].time);
                var rt = parent.GetChild(i).GetComponent<RectTransform>();
                bool onScreen = f >= -0.01f && f <= 1.01f;
                if (rt.gameObject.activeSelf != onScreen) rt.gameObject.SetActive(onScreen);
                if (!onScreen) continue;

                rt.anchoredPosition = new Vector2(_trackLeft + _trackWidth * f - KEY_W * 0.5f, -rowY);
                rt.GetChild(0).GetComponent<Image>().color = _selKeys.Contains(keys[i]) ? KEY_SELECTED : KEY_IDLE;
            }
        }

        void ShowWorldMenu()
        {
            CloseContextMenu();
            float sf = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            float x = Input.mousePosition.x / sf;
            float y = (Screen.height - Input.mousePosition.y) / sf;

            _contextMenu = UGUIShip.CreatePanel(_canvas.transform,
                new Rect(x, y, MENU_W, ROW + 8f), MENU_BG, "MachinimaMenu");
            var btn = UGUIShip.CreateButton(_contextMenu, new Rect(4f, 4f, MENU_W - 8f, ROW),
                "machinima.create_player", BTN_DARK, Color.white, UIScale.FS_SM, new Action(CreatePlayerAtCursor));
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;
        }

        Vector3 CursorWorldPoint()
        {
            var ray = _cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, 500f, ~0, QueryTriggerInteraction.Ignore)) return hit.point;
            return ray.origin + ray.direction * 8f;
        }

        void CreatePlayerAtCursor()
        {
            var spot = CursorWorldPoint();
            CloseContextMenu();

            while (PlayerById(_nextPlayerId) != null) _nextPlayerId++;
            var p = new ReplayPlayer
            {
                playerId = _nextPlayerId++,
                name = LocalizationService.Format("machinima.default_player_name_fmt", _rec.players.Count + 1),
                inTime = _time,
                outTime = _time + 5f,
                bfgScale = 1f,
            };
            p.moves.Add(new MachinimaMove { time = _time, position = spot });
            _rec.players.Add(p);

            MachinimaBaker.BakeAll(_rec);
            SpawnOne(p, null);
            RebuildTimeline();
            SelectPlayer(p);
            Plugin.Log.LogInfo($"machinima player '{p.name}' dropped at {spot}, zone {Stamp(p.inTime)}-{Stamp(p.outTime)}");
        }

        ReplayPlayer PlayerById(uint id)
        {
            for (int i = 0; i < _rec.players.Count; i++)
                if (_rec.players[i].playerId == id) return _rec.players[i];
            return null;
        }

        public void SelectPlayer(ReplayPlayer p)
        {
            _highlightedPlayer = p;
            HighlightPlayer(p.playerId);
            MachinimaPlayerWindow.Show(_canvas.transform, new Rect(MARGIN, MARGIN, WIN_W, WIN_H), p,
                new Action(OnPlayerWindowClosed));
            _windowRt.gameObject.SetActive(false);
            SyncUi();
        }

        void OnPlayerWindowClosed()
        {
            _windowRt.gameObject.SetActive(!_minimized);
            _highlightedPlayer = null;
            ClearPlayerHighlight();
            ApplyTime();
            SyncUi();
        }

        public void DeletePlayer(ReplayPlayer p)
        {
            MachinimaPlayerWindow.Instance?.Close();
            if (_solo == p) _solo = null;

            for (int i = _tracks.Count - 1; i >= 0; i--)
            {
                if (_tracks[i].player != p) continue;
                if (_tracks[i].nameLabel != null) Destroy(_tracks[i].nameLabel);
                _beans.Remove(_tracks[i].bean);
                if (_tracks[i].bean != null) Destroy(_tracks[i].bean);
                _tracks.RemoveAt(i);
            }
            _speakers.Remove(p.playerId);
            _rec.players.Remove(p);

            MachinimaBaker.BakeAll(_rec);
            RebuildSpeech();
            RebuildTimeline();
            Plugin.Log.LogInfo($"machinima player gone, {_rec.players.Count} left");
        }

        public void EnterSolo(ReplayPlayer p)
        {
            MachinimaPlayerWindow.Instance?.Close();
            _solo = p;
            _selKeys.Clear();
            _dragKey = null;
            _dragZone = null;
            RebuildTimeline();
        }

        void ExitSolo()
        {
            _solo = null;
            _selKeys.Clear();
            _dragKey = null;
            _dragZone = null;
            MachinimaKeyWindow.Instance?.Close();
            RebuildTimeline();
        }

        bool HandleSoloRows(Vector2 cursor, float time)
        {
            if (OverFxRow(cursor)) return HandleKeyRow(cursor, time, MoveKeys(), true);
            if (OverCameraRow(cursor)) return HandleKeyRow(cursor, time, ActionKeys(), false);
            return false;
        }

        MachinimaKey KeyNear(List<MachinimaKey> keys, float time)
        {
            if (_trackWidth <= 0f || _rec.duration <= 0f) return null;
            float tolerance = (KEY_W * 0.75f / _trackWidth) * ViewSpan;

            MachinimaKey best = null;
            float bestDelta = float.MaxValue;
            foreach (var k in keys)
            {
                float delta = Mathf.Abs(k.time - time);
                if (delta <= tolerance && delta < bestDelta) { bestDelta = delta; best = k; }
            }
            return best;
        }

        bool HandleKeyRow(Vector2 cursor, float time, List<MachinimaKey> keys, bool moveLane)
        {
            if (Input.GetMouseButtonDown(0))
            {
                CloseContextMenu();
                var k = KeyNear(keys, time);
                if (k == null) { _selKeys.Clear(); return true; }

                if (k == _clickedKey && Time.realtimeSinceStartup - _clickedKeyAt <= DOUBLE_CLICK)
                {
                    _clickedKey = null;
                    OpenKeyWindow(k);
                    return true;
                }
                _clickedKey = k;
                _clickedKeyAt = Time.realtimeSinceStartup;

                _selKeys.Clear();
                _selKeys.Add(k);
                SeekTo(k.time);
                OpenKeyWindow(k);

                _dragKey = k;
                _dragKeyGrabMouseX = Input.mousePosition.x;
                _dragKeyMoved = false;
                return true;
            }

            if (Input.GetMouseButtonDown(1))
            {
                CloseContextMenu();
                var k = KeyNear(keys, time);
                SeekTo(time);
                OpenMenu(cursor, 1);
                if (k != null) MenuItem(0, "ui.delete", new Action(() => { CloseContextMenu(); DeleteKey(k); }));
                else if (moveLane) MenuItem(0, "machinima.add_move", new Action(AddMoveHere));
                else MenuItem(0, "machinima.add_action", new Action(AddActionHere));
                return true;
            }

            return false;
        }

        bool HandleMachinimaDrag()
        {
            if (_draggingPlayerBar)
            {
                if (Input.GetMouseButton(0)) DragPlayerBar(TimelineCursor().y);
                else _draggingPlayerBar = false;
                return true;
            }

            if (_dragKey != null)
            {
                if (Input.GetMouseButton(0))
                {
                    if (!_dragKeyMoved && Mathf.Abs(Input.mousePosition.x - _dragKeyGrabMouseX) < DRAG_DEADZONE) return true;
                    _dragKeyMoved = true;
                    _dragKey.time = Mathf.Clamp(SnapFrame(TimeAt(TimelineCursor().x)), _solo.inTime, _solo.outTime);
                    _solo.actions.Sort((a, b) => a.time.CompareTo(b.time));
                            _time = _dragKey.time;
                }
                else
                {
                    if (_dragKeyMoved) Plugin.Log.LogInfo($"machinima key moved to {Stamp(_dragKey.time)}");
                    _dragKey = null;
                    _dragKeyMoved = false;
                    RefreshFromWindow();
                }
                return true;
            }

            if (_dragZone == null) return false;

            if (Input.GetMouseButton(0))
            {
                float t = SnapFrame(TimeAt(TimelineCursor().x));
                if (_dragZoneMode == 0)
                    _dragZone.inTime = Mathf.Clamp(t, 0f, _dragZone.outTime - MachinimaBaker.MinZone);
                else if (_dragZoneMode == 1)
                    _dragZone.outTime = Mathf.Max(t, _dragZone.inTime + MachinimaBaker.MinZone);
                else
                {
                    float span = _dragZoneOutAt - _dragZoneInAt;
                    float start = Mathf.Max(0f, _dragZoneInAt + (t - _dragZoneGrabTime));
                    _dragZone.inTime = start;
                    _dragZone.outTime = start + span;
                    foreach (var m in _dragZone.moves) m.time = Mathf.Clamp(m.time, start, start + span);
                    foreach (var a in _dragZone.actions) a.time = Mathf.Clamp(a.time, start, start + span);
                }
                MachinimaBaker.BakeAll(_rec);
            }
            else
            {
                Plugin.Log.LogInfo($"{PlayerLabel(_dragZone)} now runs {Stamp(_dragZone.inTime)} to {Stamp(_dragZone.outTime)}");
                _dragZone = null;
                RebuildSpeech();
                RefreshRuler();
            }
            return true;
        }

        bool HandlePlayerLaneRow(Vector2 cursor, float time)
        {
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f) { ScrollPlayers(wheel > 0f ? -1 : 1); return true; }

            if (_playerBarFill != null && cursor.x >= _trackLeft + _trackWidth - PLAYER_BAR_W - 2f)
            {
                if (Input.GetMouseButtonDown(0)) { _draggingPlayerBar = true; DragPlayerBar(cursor.y); }
                return true;
            }

            int row = Mathf.FloorToInt((cursor.y - PlayerLanesTop) / (LANE_H + LANE_GAP));
            int index = _playerScroll + row;
            if (row < 0 || row >= VisiblePlayerRows || index >= _rec.players.Count) return false;
            var p = _rec.players[index];

            if (Input.GetMouseButtonDown(0))
            {
                CloseContextMenu();
                float inX = TrackX(p.inTime);
                float outX = TrackX(p.outTime);
                if (cursor.x < inX - GRAB_PX || cursor.x > outX + GRAB_PX) return true;

                if (p == _clickedZone && Time.realtimeSinceStartup - _clickedZoneAt <= DOUBLE_CLICK)
                {
                    _clickedZone = null;
                    EnterSolo(p);
                    return true;
                }
                _clickedZone = p;
                _clickedZoneAt = Time.realtimeSinceStartup;

                _dragZone = p;
                _dragZoneMode = Mathf.Abs(cursor.x - inX) <= GRAB_PX ? 0
                    : Mathf.Abs(cursor.x - outX) <= GRAB_PX ? 1 : 2;
                _dragZoneGrabTime = SnapFrame(time);
                _dragZoneInAt = p.inTime;
                _dragZoneOutAt = p.outTime;
                SelectPlayer(p);
                return true;
            }

            if (Input.GetMouseButtonDown(1))
            {
                CloseContextMenu();
                OpenMenu(cursor, 2);
                MenuItem(0, "machinima.open_timeline", new Action(() => { CloseContextMenu(); EnterSolo(p); }));
                MenuItem(1, "ui.delete", new Action(() => { CloseContextMenu(); DeletePlayer(p); }));
                return true;
            }

            return false;
        }

        void AddMoveHere()
        {
            CloseContextMenu();
            SampleFrames(_solo.frames, _time, out var spot, out _);
            var key = new MachinimaMove { time = Mathf.Clamp(_time, _solo.inTime, _solo.outTime), position = spot };
            _solo.moves.Add(key);
            _selKeys.Clear();
            _selKeys.Add(key);
            OpenKeyWindow(key);
            Plugin.Log.LogInfo($"movement key at {Stamp(key.time)} for {PlayerLabel(_solo)}, {_solo.moves.Count} on this one now");
        }

        void AddActionHere()
        {
            CloseContextMenu();
            var key = new MachinimaAction { time = Mathf.Clamp(_time, _solo.inTime, _solo.outTime) };
            _solo.actions.Add(key);
            _solo.actions.Sort((a, b) => a.time.CompareTo(b.time));
            _selKeys.Clear();
            _selKeys.Add(key);
            OpenKeyWindow(key);
            Plugin.Log.LogInfo($"action key at {Stamp(key.time)} for {PlayerLabel(_solo)}");
        }

        void DeleteKey(MachinimaKey key)
        {
            var move = key as MachinimaMove;
            if (move != null) _solo.moves.Remove(move);
            else
            {
                var action = key as MachinimaAction;
                if (action != null) _solo.actions.Remove(action);
            }
            _selKeys.Remove(key);
            if (MachinimaKeyWindow.Instance != null && MachinimaKeyWindow.Instance.Key == key)
                MachinimaKeyWindow.Instance.Close();
            RebuildSpeech();
            RefreshFromWindow();
        }

        void DeleteSelectedKeys()
        {
            if (_selKeys.Count == 0 || _solo == null) return;
            var doomed = new List<MachinimaKey>(_selKeys);
            foreach (var k in doomed) DeleteKey(k);
        }

        void OpenKeyWindow(MachinimaKey key)
        {
            _freeLook = false;
            MachinimaKeyWindow.Show(_canvas.transform, new Rect(MARGIN, MARGIN, WIN_W, WIN_H), _rec, _solo, key,
                new Action(OnKeyWindowClosed));
            _windowRt.gameObject.SetActive(false);
            ShowMoveMarker(key as MachinimaMove);
        }

        MachinimaMove _markedMove;

        void ShowMoveMarker(MachinimaMove move)
        {
            _markedMove = move;
            if (move == null) { ClearPlayerHighlight(); return; }

            if (_highlight == null) BuildHighlight();
            if (_highlight != null) _highlight.SetActive(true);
            _highlightOn = false;
            UpdateMoveMarker();
        }

        void UpdateMoveMarker()
        {
            if (_markedMove == null || _highlight == null) return;
            var at = _markedMove.position + Vector3.up * HIGHLIGHT_HEIGHT;
            _highlight.transform.position = at;
            _highlight.transform.rotation = Quaternion.LookRotation(at - _cam.transform.position, Vector3.up);
        }

        public void BeginMovePick(MachinimaMove move)
        {
            _pickMove = move;
            StartPick(false, null, "machinima.click_where_it_walks");
        }

        MachinimaMove _pickMove;

        bool UpdateMovePick()
        {
            if (_pickMove == null) return false;

            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) { EndMovePick(); return true; }
            if (!Input.GetMouseButtonDown(0)) return true;

            _pickMove.position = CursorWorldPoint();
            EndMovePick();
            return true;
        }

        void EndMovePick()
        {
            var move = _pickMove;
            _pickMove = null;
            _picking = false;
            ClearPlayerHighlight();

            if (_pickHint != null) Destroy(_pickHint.gameObject);
            _pickHint = null;

            foreach (var go in _hiddenUi)
                if (go != null) go.SetActive(true);
            _hiddenUi.Clear();

            if (move != null) OpenKeyWindow(move);
            RefreshFromWindow();
        }

        void OnKeyWindowClosed()
        {
            _windowRt.gameObject.SetActive(!_minimized);
            if (_pickMove == null) ShowMoveMarker(null);
            RebuildSpeech();
            ApplyTime();
            SyncUi();
        }

        public void RebuildSpeech()
        {
            if (!Machinima || _speech == null) return;

            _rec.speechEvents.Clear();
            foreach (var p in _rec.players)
                foreach (var a in p.actions)
                {
                    if (a.kind != MachinimaActionKind.Phrase || string.IsNullOrEmpty(a.payload)) continue;
                    _rec.speechEvents.Add(new ReplaySpeechEvent
                    {
                        t = a.time,
                        end = a.time + Mathf.Max(a.duration, 0.5f),
                        playerId = p.playerId,
                        option = SpeechOptionIndex(a.payload),
                    });
                }

            _speech.Release();
            _speech = new ReplaySpeechPlayer(_rec, transform);
            _speech.Prepare(_speakers);
        }

        int SpeechOptionIndex(string itemId)
        {
            for (int i = 0; i < _rec.speechOptions.Count; i++)
                if (_rec.speechOptions[i].itemId == itemId) return i;

            _rec.speechOptions.Add(new ReplaySpeechOption { itemId = itemId, duration = 3f });
            return _rec.speechOptions.Count - 1;
        }

        void StartMachinimaAction(Track track)
        {
            var action = MachinimaBaker.ActiveAction(track.player, _time, out _);
            if (action == track.lastAction) return;

            bool wasDiving = track.lastAction != null && track.lastAction.kind == MachinimaActionKind.Dive;
            track.lastAction = action;
            if (wasDiving) track.anim.SetBool(MotorFunctionDiveStateDive.diveAnimParam, false);
            if (action == null) return;

            if (action.kind == MachinimaActionKind.Jump)
                track.anim.Play(MotorFunctionJumpStateLiftOff.animStateName, 0, 0f);
            else if (action.kind == MachinimaActionKind.Dive)
            {
                track.anim.SetTrigger(MotorFunctionDiveStateDive.startDiveAnimParam);
                track.anim.SetBool(MotorFunctionDiveStateDive.diveAnimParam, true);
            }
        }

        void SampleMachinimaEmote(Track track)
        {
            if (track.emoteClips.Count == 0) return;

            var action = MachinimaBaker.ActiveAction(track.player, _time, out float local);
            if (action == null || action.kind != MachinimaActionKind.Emote) return;
            if (!track.emoteClips.TryGetValue(action.payload ?? "", out var clip) || clip == null) return;

            clip.SampleAnimation(track.anim.gameObject, Mathf.Min(local, clip.length));
        }

        IEnumerator LoadEmoteClips(Track track)
        {
            var loader = CustomizationServices.LoaderService;
            if (loader == null) yield break;

            foreach (var entry in track.player.bfgSkins)
            {
                if (SkinTypeParser.FromString(entry.type) != SkinType.Emote) continue;

                ActiveSkinSlot slot = null;
                yield return loader.ResolveProfileSlot(entry, new Action<ActiveSkinSlot>(s => slot = s)).WrapToIl2Cpp();
                if (slot == null || slot.bundle == null) continue;

                AnimationClip clip = null;
                if (!string.IsNullOrEmpty(slot.skinInfo.clip))
                {
                    var named = slot.bundle.LoadAsset(slot.skinInfo.clip);
                    clip = named != null ? named.TryCast<AnimationClip>() : null;
                }
                if (clip == null)
                    foreach (string name in slot.bundle.GetAllAssetNames())
                    {
                        var asset = slot.bundle.LoadAsset(name);
                        clip = asset != null ? asset.TryCast<AnimationClip>() : null;
                        if (clip != null) break;
                    }

                if (clip == null) { Plugin.Log.LogWarning($"emote bundle {entry.file} had no clip in it"); continue; }
                track.emoteClips[entry.file] = clip;
            }

            if (track.emoteClips.Count > 0)
                Plugin.Log.LogInfo($"{PlayerLabel(track.player)} can perform {track.emoteClips.Count} custom emote(s)");
        }

        public void SeekFromWindow(float time) => SeekTo(time);

        public void RefreshFromWindow()
        {
            MachinimaBaker.BakeAll(_rec);
            ApplyTime();
            SyncUi();
        }

        public void RefreshAfterPlayerRename()
        {
            foreach (var track in _tracks)
                if (track.nameLabel != null) { Destroy(track.nameLabel); track.nameLabel = null; track.crownState = -1; }
            RebuildTimeline();
        }

        public Vector3 CameraGroundPoint()
        {
            var t = _cam.transform;
            if (Physics.Raycast(t.position, t.forward, out var hit, 200f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point;
            var ahead = t.position + t.forward * 6f;
            return Physics.Raycast(ahead + Vector3.up * 50f, Vector3.down, out var down, 200f, ~0, QueryTriggerInteraction.Ignore)
                ? down.point : ahead;
        }

        public void EditPlayerLook(ReplayPlayer p)
        {
            MachinimaPlayerWindow.Instance?.Close();
            _canvas.enabled = false;
            BeanCreatorScreen.Open(BeanLook.From(p), new Action<BeanLook>(look =>
            {
                _canvas.enabled = true;
                if (look == null) return;
                look.Into(p);
                StartCoroutine(RedressPlayer(p).WrapToIl2Cpp());
            }));
        }

        IEnumerator RedressPlayer(ReplayPlayer p)
        {
            Track track = null;
            foreach (var t in _tracks) if (t.player == p) { track = t; break; }
            if (track == null) yield break;

            if (track.nameLabel != null) Destroy(track.nameLabel);
            _beans.Remove(track.bean);
            if (track.bean != null) Destroy(track.bean);
            _tracks.Remove(track);

            yield return null;
            SpawnOne(p, null);
            ApplyTime();
            SyncUi();
            Plugin.Log.LogInfo($"{PlayerLabel(p)} re-dressed");
        }
    }
}
#endif
