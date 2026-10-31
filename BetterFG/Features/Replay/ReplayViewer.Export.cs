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

namespace BetterFG.Features.Replay
{
    public partial class ReplayViewer
    {
        void SaveAs()
        {
            System.IO.Directory.CreateDirectory(SaveReplay.ReplayDir);
            string suggested = System.IO.Path.Combine(SaveReplay.ReplayDir, SafeName());
            WinDialogs.SaveFile("Save replay", "bfgreplay", suggested, new Action<string>(path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                SaveReplay.WriteTo(_rec, path);
                _rec.sourcePath = path;
                ReplayThumbnail.Capture(_cam, path);
                Plugin.Log.LogInfo($"replay written out to {path}");
                PushReplayPresence();
            }));
        }

        void PushReplayPresence()
        {
            long bytes = 0;
            if (!string.IsNullOrEmpty(_rec.sourcePath) && System.IO.File.Exists(_rec.sourcePath))
                bytes = new System.IO.FileInfo(_rec.sourcePath).Length;
            DiscordPresenceService.SetReplayInfo(ReplayName(), bytes);
        }

        void OnNameChanged(string value)
        {
            _rec.name = (value ?? "").Trim();
            SyncUi();
            PushReplayPresence();
        }

        void OpenExportWindow()
        {
            ReplayKeyframeWindow.Instance?.Close();
            ReplayExportWindow.Show(_canvas.transform, new Rect(MARGIN, MARGIN, WIN_W, WIN_H),
                new Action(StartExport), new Action(OnExportWindowClosed));
            _windowRt.gameObject.SetActive(false);
        }

        void OpenControlsWindow()
        {
            ReplayControlsWindow.Show(_canvas.transform, new Rect(MARGIN, MARGIN, WIN_W, WIN_H),
                Mathf.InverseLerp(SPEED_MIN, SPEED_MAX, _speed), Mathf.InverseLerp(SPEED_MIN, SPEED_MAX, 14f),
                new Action<float>(OnSpeedChanged), new Action(OnControlsWindowClosed));
            _windowRt.gameObject.SetActive(false);
        }

        void OnControlsWindowClosed() => ShowMainWindow();

        void StartExport()
        {
            if (_exporting) return;
            ReplayExportWindow.Instance?.Close();
            StartCoroutine(ExportRoutine().WrapToIl2Cpp());
        }

        void FinishExport(string finalStatus)
        {
            ReplayExportProgressWindow.Instance?.SetDone(finalStatus);
            SetReplayUiVisible(true);
            _exporting = false;
            Time.captureFramerate = 0;
            DiscordPresenceService.SetExportProgress(null);
        }

        IEnumerator AudioPass(string dir)
        {
            var capture = new ReplayProcessAudioCapture();
            string path = System.IO.Path.Combine(dir, "audio.wav");
            if (!capture.Start(path)) yield break;

            ReplayExportProgressWindow.Instance?.SetStatus("audio pass, playing it through...");
            float settle = Time.realtimeSinceStartup + 0.35f;
            while (Time.realtimeSinceStartup < settle) yield return null;

            _exportAudioLead = capture.Seconds;
            _time = SnapOutOfCut(_rec.trimStart);
            _paused = false;
            _freeLook = false;
            _audio.Seek(_time);
            _starchart.Seek(_time);
            _vfx.Seek(_time);

            while (_time < _rec.trimEnd && !_exportCancelled)
            {
                float from = _time;
                float speed = PlaybackSpeed();
                bool cutJump = AdvanceTime(Time.unscaledDeltaTime * speed);
                _time = Mathf.Min(_time, _rec.trimEnd);
                _audio.Tick();
                _audio.SetSpeed(speed);
                if (cutJump) { _audio.Seek(_time); _starchart.Seek(_time); _vfx.Seek(_time); }
                else { _audio.Advance(from, _time); _starchart.Advance(from, _time); _vfx.Advance(from, _time); }
                ApplyTime();
                _world.Apply(_time);
                _tail.Apply(_time);
                _powerups.Apply(_time);
                SyncUi();
                yield return null;
            }

            _paused = true;
            capture.Stop();

            float deadline = Time.realtimeSinceStartup + 3f;
            while (!capture.Finished && Time.realtimeSinceStartup < deadline) yield return null;

            _exportWav = path;
            Plugin.Log.LogInfo($"audio pass done, {capture.Seconds:0.0}s captured with {_exportAudioLead:0.00}s of lead-in to trim");
        }

        IEnumerator ExportRoutine()
        {
            _exporting = true;
            _exportCancelled = false;
            _paused = true;
            _freeLook = false;
            CloseContextMenu();
            ClearPlayerHighlight();
            ReplayKeyframeWindow.Instance?.Close();
            ReplayVisibilityKeyframeWindow.Instance?.Close();
            ReplayPostFxKeyframeWindow.Instance?.Close();
            SetReplayUiVisible(false);
            ReplayExportProgressWindow.Show(_canvas.transform, new Action(() => _exportCancelled = true));
            DiscordPresenceService.SetExportProgress(0);

            int w = ReplayExport.Width;
            int h = ReplayExport.Height;
            int fps = ReplayExport.FrameRate;
            string dir = ReplayExport.NewFolder(ReplayName());
            float from = SnapOutOfCut(_rec.trimStart);
            float until = _rec.trimEnd;
            int cap = Mathf.CeilToInt(Mathf.Max(1f, until - from) * fps / MIN_EXPORT_SPEED);

            _exportWav = null;
            _exportAudioLead = 0f;
            if (ReplayExport.AudioOn) yield return StartCoroutine(AudioPass(dir).WrapToIl2Cpp());

            if (_exportCancelled)
            {
                ReplayExport.CleanFrames(dir);
                Plugin.Log.LogInfo("export cancelled during the audio pass");
                FinishExport("cancelled");
                yield break;
            }

            string outPath = dir + ".mp4";
            var encoder = new ReplayEncoder(outPath, w, h, fps, ReplayExport.Kbps, _exportWav, _exportAudioLead);

            var rt = new RenderTexture(w, h, 24);
            var shot = new Texture2D(w, h, TextureFormat.BGRA32, false);
            var readRect = new Rect(0f, 0f, w, h);
            var endOfFrame = new WaitForEndOfFrame();
            var overlays = new List<Camera>();
            foreach (var child in _childCams)
                if (child != null && child.isActiveAndEnabled) overlays.Add(child);
            overlays.Sort((a, b) => a.depth.CompareTo(b.depth));
            if (overlays.Count > 0) Plugin.Log.LogInfo($"export layering {overlays.Count} overlay cam(s) on top, crown ranks live on one of those");
            Plugin.Log.LogInfo($"export: {w}x{h} @ {fps}fps, {ReplayExport.Kbps}kbps, {Stamp(from)}-{Stamp(until)} of replay -> {dir}");

            int frame = 0;
            Time.captureFramerate = fps;
            float nextUi = 0f;
            float started = Time.realtimeSinceStartup;
            _time = from;
            _shakeTime = 0f;
            _cam.targetTexture = rt;
            foreach (var overlay in overlays) overlay.targetTexture = rt;
            while (_time < until && frame < cap && !_exportCancelled)
            {
                ApplyTime();
                _world.Apply(_time);
                _tail.Apply(_time);
                _powerups.Apply(_time);
                SyncUi();

                yield return endOfFrame;

                var was = RenderTexture.active;
                RenderTexture.active = rt;
                shot.ReadPixels(readRect, 0, 0, false);
                shot.Apply(false);
                RenderTexture.active = was;

                var raw = shot.GetRawTextureData();
                encoder.WriteFrame(raw.Pointer + IntPtr.Size * 4);
                GC.KeepAlive(raw);

                frame++;
                AdvanceTime(Mathf.Max(MIN_EXPORT_SPEED, PlaybackSpeed()) / fps);
                _shakeTime += 1f / fps;
                if (Time.realtimeSinceStartup < nextUi) continue;
                nextUi = Time.realtimeSinceStartup + 0.25f;
                int percent = Mathf.RoundToInt(100f * (_time - from) / Mathf.Max(0.01f, until - from));
                ReplayExportProgressWindow.Instance?.SetStatus($"frame {frame} · {percent}%");
                ReplayExportProgressWindow.Instance?.SetProgress(percent / 100f);
                DiscordPresenceService.SetExportProgress(percent);
            }

            _cam.targetTexture = null;
            foreach (var overlay in overlays) overlay.targetTexture = null;
            Destroy(shot);
            rt.Release();
            Destroy(rt);

            ReplayExportProgressWindow.Instance?.SetStatus("finishing up...");
            encoder.Dispose();
            ReplayExport.CleanFrames(dir);

            if (_exportCancelled)
            {
                ReplayExport.DeletePartial(outPath);
                Plugin.Log.LogInfo($"export cancelled after {frame} frames, partial file removed");
                FinishExport("cancelled");
                yield break;
            }

            float took = Time.realtimeSinceStartup - started;
            Plugin.Log.LogInfo($"export done, {frame} frames into {outPath} in {took:0.0}s ({frame / Mathf.Max(0.01f, took):0.0} fps)");
            ReplayExport.Reveal(outPath);
            FinishExport("done: " + System.IO.Path.GetFileName(outPath));
        }

    }
}
