using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Player;
using BetterFG.Nametag;
using BetterFG.Network;
using BetterFG.Services;
using BetterFG.UI;
using BetterFG.UI.Tabs;
using BetterFG.Utilities;
using Cinemachine;
using FallGuysLib.Camera;
using FallGuysLib.NPC;
using FG.Common;
using FG.Common.Fraggle;
using FGClient;
using MPG.Utility;
using UnityEngine;
using Wushu.Integration;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.Features.Replay
{
    public partial class ReplayViewer
    {
        void ShowContextMenu(Vector2 cursor)
        {
            OpenMenu(cursor, 3);
            MenuItem(0, "ui.add_camera_keyframe", new Action(AddKeyframeHere));
            MenuItem(1, "ui.add_visibility_keyframe", new Action(AddVisibilityKeyframeHere));
            MenuItem(2, "ui.add_post_fx_keyframe", new Action(AddPostFxKeyframeHere));
        }

        void ShowAddCameraMenu(Vector2 cursor)
        {
            OpenMenu(cursor, 1);
            MenuItem(0, "ui.add_camera_keyframe", new Action(AddKeyframeHere));
        }

        void ShowAddVisMenu(Vector2 cursor)
        {
            OpenMenu(cursor, 1);
            MenuItem(0, "ui.add_visibility_keyframe", new Action(AddVisibilityKeyframeHere));
        }

        void ShowAddFxMenu(Vector2 cursor)
        {
            OpenMenu(cursor, 1);
            MenuItem(0, "ui.add_post_fx_keyframe", new Action(AddPostFxKeyframeHere));
        }

        void ShowKeyframeMenu(ReplayKeyframe k, Vector2 cursor)
        {
            OpenMenu(cursor, 2);
            MenuItem(0, "ui.duplicate", new Action(() => { CloseContextMenu(); Duplicate(k); }));
            MenuItem(1, _selected.Count > 1 ? "Delete " + _selected.Count + " keyframes" : "Delete",
                new Action(DeleteSelected));
        }

        void ShowVisKeyframeMenu(ReplayVisibilityKeyframe k, Vector2 cursor)
        {
            OpenMenu(cursor, 2);
            MenuItem(0, "ui.duplicate", new Action(() => { CloseContextMenu(); DuplicateVis(k); }));
            MenuItem(1, _selectedVis.Count > 1 ? "Delete " + _selectedVis.Count + " keyframes" : "Delete",
                new Action(DeleteSelected));
        }

        void ShowFxKeyframeMenu(ReplayPostFxKeyframe k, Vector2 cursor)
        {
            OpenMenu(cursor, 2);
            MenuItem(0, "ui.duplicate", new Action(() => { CloseContextMenu(); DuplicateFx(k); }));
            MenuItem(1, _selectedFx.Count > 1 ? "Delete " + _selectedFx.Count + " keyframes" : "Delete",
                new Action(DeleteSelected));
        }

        void OpenMenu(Vector2 cursor, int rows)
        {
            CloseContextMenu();

            float h = rows * (ROW + 4f) + 4f;
            float x = Mathf.Min(cursor.x + 8f, _trackLeft + _trackWidth - MENU_W);
            float y = Mathf.Min(cursor.y + 8f, TimelineH - h);
            _contextMenu = UGUIShip.CreatePanel(_timelineRt, new Rect(x, y, MENU_W, h),
                MENU_BG, "KeyframeMenu");
        }

        void MenuItem(int row, string label, Action onClick)
        {
            var btn = UGUIShip.CreateButton(_contextMenu, new Rect(4f, 4f + row * (ROW + 4f), MENU_W - 8f, ROW),
                label, BTN_DARK, Color.white, UIScale.FS_SM, onClick);
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;
        }

        void CloseContextMenu()
        {
            if (_contextMenu == null) return;
            Destroy(_contextMenu.gameObject);
            _contextMenu = null;
        }

        void Duplicate(ReplayKeyframe k)
        {
            var copy = Clone(k);
            copy.time = Mathf.Min(k.time + DUP_GAP, _rec.duration);
            _rec.keyframes.Add(copy);
            _rec.SortKeyframes();
            SelectOnly(copy);
            Plugin.Log.LogInfo($"copied the {Stamp(k.time)} keyframe over to {Stamp(copy.time)}");
            SyncUi();
        }

        void DuplicateVis(ReplayVisibilityKeyframe k)
        {
            var copy = CloneVis(k);
            copy.time = Mathf.Min(k.time + DUP_GAP, _rec.duration);
            _rec.visibilityKeyframes.Add(copy);
            _rec.SortVisibilityKeyframes();
            SelectOnlyVis(copy);
            Plugin.Log.LogInfo($"copied the {Stamp(k.time)} visibility keyframe over to {Stamp(copy.time)}");
            SyncUi();
        }

        void DuplicateFx(ReplayPostFxKeyframe k)
        {
            var copy = CloneFx(k);
            copy.time = Mathf.Min(k.time + DUP_GAP, _rec.duration);
            _rec.postFxKeyframes.Add(copy);
            _rec.SortPostFxKeyframes();
            SelectOnlyFx(copy);
            Plugin.Log.LogInfo($"copied the {Stamp(k.time)} post fx keyframe over to {Stamp(copy.time)}");
            SyncUi();
        }

        void DeleteSelected()
        {
            CloseContextMenu();
#if MACHINIMA
            DeleteSelectedKeys();
#endif
            if (_selected.Count > 0)
            {
                int gone = 0;
                foreach (var k in _selected)
                    if (_rec.keyframes.Remove(k)) gone++;
                _selected.Clear();
                _dragKeyframe = null;

                var window = ReplayKeyframeWindow.Instance;
                if (window != null && !_rec.keyframes.Contains(window.Keyframe)) window.Close();

                Plugin.Log.LogInfo($"deleted {gone}, {_rec.keyframes.Count} camera keyframes left");
            }

            if (_selectedVis.Count > 0)
            {
                int gone = 0;
                foreach (var k in _selectedVis)
                    if (_rec.visibilityKeyframes.Remove(k)) gone++;
                _selectedVis.Clear();
                _dragVisKeyframe = null;

                var window = ReplayVisibilityKeyframeWindow.Instance;
                if (window != null && !_rec.visibilityKeyframes.Contains(window.Keyframe)) window.Close();

                Plugin.Log.LogInfo($"deleted {gone}, {_rec.visibilityKeyframes.Count} visibility keyframes left");
            }

            if (_selectedFx.Count > 0)
            {
                int gone = 0;
                foreach (var k in _selectedFx)
                    if (_rec.postFxKeyframes.Remove(k)) gone++;
                _selectedFx.Clear();
                _dragFxKeyframe = null;

                var window = ReplayPostFxKeyframeWindow.Instance;
                if (window != null && !_rec.postFxKeyframes.Contains(window.Keyframe)) window.Close();

                Plugin.Log.LogInfo($"deleted {gone}, {_rec.postFxKeyframes.Count} post fx keyframes left");
            }

            ApplyTime();
            SyncUi();
        }

        static ReplayVisibilityKeyframe CloneVis(ReplayVisibilityKeyframe k)
        {
            var copy = new ReplayVisibilityKeyframe
            {
                time = k.time,
                showPhrases = k.showPhrases,
                crowns = k.crowns,
                names = k.names,
                players = k.players,
                showGhosts = k.showGhosts,
            };
            copy.nameOnlyPlayers.AddRange(k.nameOnlyPlayers);
            copy.onlyPlayers.AddRange(k.onlyPlayers);
            copy.crownOnlyPlayers.AddRange(k.crownOnlyPlayers);
            return copy;
        }

        void AddKeyframeHere()
        {
            CloseContextMenu();

            ReplayKeyframe kf;
            if (KeyframeSpan(out var neighbour, out _, out _)) kf = Clone(neighbour);
            else
            {
                kf = new ReplayKeyframe();
                if (_rec.players.Count > 0)
                {
                    kf.targetPlayerId = _rec.players[0].playerId;
                    kf.lookAtPlayerId = _rec.players[0].playerId;
                }
            }

            kf.time = _time;
            CaptureIntoKeyframe(kf);

            _rec.keyframes.Add(kf);
            _rec.SortKeyframes();
            SelectOnly(kf);
            _freeLook = false;
            Plugin.Log.LogInfo($"keyframe at {Stamp(kf.time)}, {_rec.keyframes.Count} on this replay now");

            OpenKeyframeWindow(kf);
            BeginCameraEdit(kf);
        }

        void AddVisibilityKeyframeHere()
        {
            CloseContextMenu();

            var neighbour = VisibilityAt(_time);
            var kf = neighbour != null ? CloneVis(neighbour) : new ReplayVisibilityKeyframe();
            kf.time = _time;

            _rec.visibilityKeyframes.Add(kf);
            _rec.SortVisibilityKeyframes();
            SelectOnlyVis(kf);
            _freeLook = false;
            Plugin.Log.LogInfo($"visibility keyframe at {Stamp(kf.time)}, {_rec.visibilityKeyframes.Count} on this replay now");

            OpenVisibilityKeyframeWindow(kf);
        }

        void AddPostFxKeyframeHere()
        {
            CloseContextMenu();

            var neighbour = PostFxAt(_time);
            var kf = neighbour != null ? CloneFx(neighbour) : new ReplayPostFxKeyframe();
            kf.time = _time;

            _rec.postFxKeyframes.Add(kf);
            _rec.SortPostFxKeyframes();
            SelectOnlyFx(kf);
            _freeLook = false;
            Plugin.Log.LogInfo($"post fx keyframe at {Stamp(kf.time)}, {_rec.postFxKeyframes.Count} on this replay now");

            OpenPostFxKeyframeWindow(kf);
        }

        void OpenKeyframeWindow(ReplayKeyframe kf)
        {
            _freeLook = false;
            ReplayVisibilityKeyframeWindow.Instance?.Close();
            ReplayPostFxKeyframeWindow.Instance?.Close();
            ReplayKeyframeWindow.Show(_canvas.transform, new Rect(MARGIN, MARGIN, WIN_W, WIN_H), _rec, kf,
                new Action(OnKeyframeWindowClosed));
            _windowRt.gameObject.SetActive(false);
        }

        void OpenVisibilityKeyframeWindow(ReplayVisibilityKeyframe kf)
        {
            _freeLook = false;
            ReplayKeyframeWindow.Instance?.Close();
            ReplayPostFxKeyframeWindow.Instance?.Close();
            ReplayVisibilityKeyframeWindow.Show(_canvas.transform, new Rect(MARGIN, MARGIN, WIN_W, WIN_H), _rec, kf,
                new Action(OnVisibilityKeyframeWindowClosed));
            _windowRt.gameObject.SetActive(false);
        }

        void OpenPostFxKeyframeWindow(ReplayPostFxKeyframe kf)
        {
            _freeLook = false;
            ReplayKeyframeWindow.Instance?.Close();
            ReplayVisibilityKeyframeWindow.Instance?.Close();
            ReplayPostFxKeyframeWindow.Show(_canvas.transform, new Rect(MARGIN, MARGIN, WIN_W, WIN_H), kf,
                new Action(OnPostFxKeyframeWindowClosed));
            _windowRt.gameObject.SetActive(false);
        }

        void Minimize()
        {
            _minimized = true;
            CloseContextMenu();
            SetReplayUiVisible(true);
        }

        void Restore()
        {
            _minimized = false;
            SetReplayUiVisible(true);
        }

        void OnKeyframeWindowClosed()
        {
            _windowRt.gameObject.SetActive(!_minimized);
            _rec.SortKeyframes();
            for (int i = _selected.Count - 1; i >= 0; i--)
                if (!_rec.keyframes.Contains(_selected[i])) _selected.RemoveAt(i);
            _freeLook = false;
            ApplyTime();
            SyncUi();
        }

        void OnVisibilityKeyframeWindowClosed()
        {
            _windowRt.gameObject.SetActive(!_minimized);
            _rec.SortVisibilityKeyframes();
            for (int i = _selectedVis.Count - 1; i >= 0; i--)
                if (!_rec.visibilityKeyframes.Contains(_selectedVis[i])) _selectedVis.RemoveAt(i);
            _freeLook = false;
            ApplyTime();
            SyncUi();
        }

        void OnPostFxKeyframeWindowClosed()
        {
            _windowRt.gameObject.SetActive(!_minimized);
            _rec.SortPostFxKeyframes();
            for (int i = _selectedFx.Count - 1; i >= 0; i--)
                if (!_rec.postFxKeyframes.Contains(_selectedFx[i])) _selectedFx.RemoveAt(i);
            _freeLook = false;
            ApplyTime();
            SyncUi();
        }

        void CaptureIntoKeyframe(ReplayKeyframe kf)
        {
            var pos = _cam.transform.position;
            kf.rotation = _cam.transform.rotation;
            kf.position = kf.cameraType == ReplayCameraType.Fixed || kf.cameraType == ReplayCameraType.FixedObject
                ? pos - TargetPositionAt(kf.targetObject, kf.targetPlayerId, kf.time)
                : pos;
            kf.fov = _cam.fieldOfView;
        }

        static void SampleFrames(List<ReplayFrame> frames, float time, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;
            if (frames == null || frames.Count == 0) return;

            int i = 0;
            while (i + 1 < frames.Count && frames[i + 1].t <= time) i++;
            if (i + 1 < frames.Count)
            {
                float den = frames[i + 1].t - frames[i].t;
                float f = den > 0f ? Mathf.Clamp01((time - frames[i].t) / den) : 0f;
                pos = Vector3.Lerp(frames[i].pos, frames[i + 1].pos, f);
                rot = Quaternion.Slerp(frames[i].rot, frames[i + 1].rot, f);
                return;
            }
            pos = frames[i].pos;
            rot = frames[i].rot;
        }

    }
}
