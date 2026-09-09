using System;
using System.Diagnostics;
using System.IO;
using BetterFG.Services;
using BetterFG.UI;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.Features.Replay
{
    internal static class ReplayExport
    {
        public static readonly int[] Heights = { 480, 720, 1080 };
        public static readonly int[] Rates = { 24, 30, 60 };
        public static readonly int[] Bitrates = { 2500, 6000, 12000, 24000 };
        public static readonly string[] BitrateLabels = { "Very low", "Low", "Medium", "High" };
        static readonly int[] Quality = { 55, 70, 85, 95 };

        public static int HeightIndex = Read("replay.export.height", 1, Heights.Length);
        public static int RateIndex = Read("replay.export.fps", 1, Rates.Length);
        public static int BitrateIndex = Read("replay.export.bitrate", 2, Bitrates.Length);
        public static bool AudioOn = SettingsService.Get("replay.export.audio", "1") != "0";

        static int Read(string key, int fallback, int count)
        {
            int value = int.TryParse(SettingsService.Get(key, fallback.ToString()), out int parsed) ? parsed : fallback;
            return Mathf.Clamp(value, 0, count - 1);
        }

        public static void Save()
        {
            SettingsService.Set("replay.export.height", HeightIndex.ToString());
            SettingsService.Set("replay.export.fps", RateIndex.ToString());
            SettingsService.Set("replay.export.bitrate", BitrateIndex.ToString());
            SettingsService.Set("replay.export.audio", AudioOn ? "1" : "0");
        }

        public static int Height => Heights[HeightIndex];
        public static int Width => Mathf.RoundToInt(Height * 16f / 9f * 0.5f) * 2;
        public static int FrameRate => Rates[RateIndex];
        public static int Kbps => Bitrates[BitrateIndex];
        public static int JpgQuality => Quality[BitrateIndex];

        public static string ExportDir => Path.Combine(SettingsService.ExportsDir, "Replays");

        public static string NewFolder(string label)
        {
            string safe = string.Concat((string.IsNullOrEmpty(label) ? "replay" : label).Split(Path.GetInvalidFileNameChars()));
            string dir = Path.Combine(ExportDir, safe + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static void Reveal(string path)
        {
            try
            {
                if (File.Exists(path)) Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else if (Directory.Exists(path)) Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"explorer wouldn't open at {path}: {ex.Message}"); }
        }

        public static void CleanFrames(string dir)
        {
            try
            {
                foreach (string file in Directory.GetFiles(dir, "frame_*.jpg")) File.Delete(file);
                foreach (string file in Directory.GetFiles(dir, "*.wav")) File.Delete(file);
                if (Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"couldn't tidy the frames in {dir}: {ex.Message}"); }
        }

        public static void DeletePartial(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { Plugin.Log.LogWarning($"couldn't delete the cancelled export at {path}: {ex.Message}"); }
        }
    }


    internal class ReplayExportWindow
    {
        public static ReplayExportWindow Instance;

        const float ROW = ReplayWindowKit.ROW;
        const float PAD = ReplayWindowKit.PAD;

        static readonly Vector3 TITLE_POS = Vector3.zero;

        readonly Action _onClose;
        readonly Action _onExport;
        readonly RectTransform _root;
        readonly float _width;
        RectTransform _content;

        ReplayExportWindow(Transform parent, Rect rect, Action onExport, Action onClose)
        {
            _onExport = onExport;
            _onClose = onClose;
            _width = rect.width;
            _root = UGUIShip.CreatePanel(parent, rect, Color.clear, "ReplayExportWindow");
            ReplayWindowKit.MainBackdrop(_root);
            Rebuild();
        }

        public static ReplayExportWindow Show(Transform parent, Rect rect, Action onExport, Action onClose)
        {
            Instance?.Close();
            Instance = new ReplayExportWindow(parent, rect, onExport, onClose);
            return Instance;
        }

        public void Close()
        {
            if (Instance == this) Instance = null;
            ReplayExport.Save();
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _onClose?.Invoke();
        }

        void Rebuild()
        {
            if (_content != null) UnityEngine.Object.Destroy(_content.gameObject);

            _content = ReplayWindowKit.Content(_root);

            ReplayWindowKit.Title(_content, _width, "Export", TITLE_POS);

            float y = ReplayWindowKit.HEAD + 4f;

            ReplayWindowKit.Carousel(_content, y, _width, 0, "Resolution", ReplayExport.Height + "p",
                new Action(() => StepHeight(-1)), new Action(() => StepHeight(1)));
            y += ROW;

            ReplayWindowKit.Carousel(_content, y, _width, 1, "Frame rate", ReplayExport.FrameRate + " fps",
                new Action(() => StepRate(-1)), new Action(() => StepRate(1)));
            y += ROW;

            ReplayWindowKit.Carousel(_content, y, _width, 2, "Bitrate", ReplayExport.BitrateLabels[ReplayExport.BitrateIndex],
                new Action(() => StepBitrate(-1)), new Action(() => StepBitrate(1)));
            y += ROW;

            ReplayWindowKit.Carousel(_content, y, _width, 3, "Audio", ReplayExport.AudioOn ? "Game audio" : "Silent",
                new Action(ToggleAudio), new Action(ToggleAudio));
            y += ROW;

            ReplayWindowKit.Stripe(_content, y, _width, 4);
            UGUIShip.CreateLabel(_content, new Rect(PAD, y, _width - PAD * 2f, ROW),
                ReplayExport.Width + " x " + ReplayExport.Height + " · " + ReplayExport.Kbps / 1000 + " Mbps",
                UIScale.FS_SM, ReplayWindowKit.HINT);
            y += ROW + 6f;

            float bw = (_width - PAD * 3f) / 2f;
            UGUIShip.CreateButton(_content, new Rect(PAD, y, bw, ROW + 4f), "ui.export",
                ReplayWindowKit.BTN_GREEN, Color.white, UIScale.FS_SM, _onExport);
            UGUIShip.CreateButton(_content, new Rect(PAD * 2f + bw, y, bw, ROW + 4f), "ui.close_2",
                ReplayWindowKit.BTN_DARK, Color.white, UIScale.FS_SM, new Action(Close));
            y += ROW + 4f;

            _root.sizeDelta = new Vector2(_width, y + PAD);
        }

        void StepHeight(int dir)
        {
            ReplayExport.HeightIndex = ReplayWindowKit.Step(ReplayExport.HeightIndex, dir, ReplayExport.Heights.Length, true);
            Rebuild();
        }

        void StepRate(int dir)
        {
            ReplayExport.RateIndex = ReplayWindowKit.Step(ReplayExport.RateIndex, dir, ReplayExport.Rates.Length, true);
            Rebuild();
        }

        void ToggleAudio()
        {
            ReplayExport.AudioOn = !ReplayExport.AudioOn;
            Rebuild();
        }

        void StepBitrate(int dir)
        {
            ReplayExport.BitrateIndex = ReplayWindowKit.Step(ReplayExport.BitrateIndex, dir, ReplayExport.Bitrates.Length, true);
            Rebuild();
        }
    }

    internal class ReplayExportProgressWindow
    {
        public static ReplayExportProgressWindow Instance;

        const float ROW = ReplayWindowKit.ROW;
        const float PAD = ReplayWindowKit.PAD;
        const float MARGIN = 16f;
        const float WIDTH = 260f;
        const float BAR_H = 10f;

        static readonly Vector3 TITLE_POS = Vector3.zero;
        static readonly Color BAR_BG = new Color(0f, 0f, 0f, 0.35f);

        readonly Action _onCancel;
        readonly RectTransform _root;
        RectTransform _content;
        Text _status;
        RectTransform _barFillRt;
        Button _actionBtn;
        Text _actionLabel;
        float _progress;
        bool _done;

        ReplayExportProgressWindow(Transform parent, Action onCancel)
        {
            _onCancel = onCancel;
            _root = UGUIShip.CreatePanel(parent, new Rect(0f, 0f, WIDTH, 1f), Color.clear, "ReplayExportProgressWindow");
            _root.anchorMin = new Vector2(1f, 0f);
            _root.anchorMax = new Vector2(1f, 0f);
            _root.pivot = new Vector2(1f, 0f);
            _root.anchoredPosition = new Vector2(-MARGIN, MARGIN);
            ReplayWindowKit.EditBackdrop(_root);
            Rebuild();
        }

        public static ReplayExportProgressWindow Show(Transform parent, Action onCancel)
        {
            Instance?.Close();
            Instance = new ReplayExportProgressWindow(parent, onCancel);
            return Instance;
        }

        public void Close()
        {
            if (Instance == this) Instance = null;
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
        }

        public void SetStatus(string text)
        {
            if (_status != null) UGUIShip.RelabelText(_status, text ?? "");
        }

        public void SetProgress(float t)
        {
            _progress = Mathf.Clamp01(t);
            if (_barFillRt != null) _barFillRt.sizeDelta = new Vector2((WIDTH - PAD * 2f) * _progress, BAR_H);
        }

        public void SetDone(string finalStatus)
        {
            _done = true;
            SetStatus(finalStatus);
            if (_actionBtn != null)
            {
                UGUIShip.SetButtonColor(_actionBtn, ReplayWindowKit.BTN_DARK);
                _actionBtn.interactable = true;
            }
            if (_actionLabel != null) UGUIShip.RelabelText(_actionLabel, "ui.close_2");
        }

        void Rebuild()
        {
            _content = ReplayWindowKit.Content(_root);
            ReplayWindowKit.Title(_content, WIDTH, "Export", TITLE_POS);

            float y = ReplayWindowKit.HEAD + 4f;

            _status = UGUIShip.CreateLabel(_content, new Rect(PAD, y, WIDTH - PAD * 2f, ROW), "",
                UIScale.FS_SM, ReplayWindowKit.HINT);
            y += ROW + 2f;

            var barBg = UGUIShip.CreatePanel(_content, new Rect(PAD, y, WIDTH - PAD * 2f, BAR_H), BAR_BG, "BarBg");
            barBg.GetComponent<Image>().raycastTarget = false;

            _barFillRt = UGUIShip.CreatePanel(_content, new Rect(PAD, y, 0f, BAR_H), ReplayWindowKit.BTN_GREEN, "BarFill");
            _barFillRt.GetComponent<Image>().raycastTarget = false;
            y += BAR_H + 8f;

            _actionBtn = UGUIShip.CreateButton(_content, new Rect(PAD, y, WIDTH - PAD * 2f, ROW + 4f), "ui.cancel",
                ReplayWindowKit.BTN_RED, Color.white, UIScale.FS_SM, new Action(OnAction));
            _actionLabel = _actionBtn.GetComponentInChildren<Text>();
            y += ROW + 4f;

            _root.sizeDelta = new Vector2(WIDTH, y + PAD);
            SetProgress(_progress);
        }

        void OnAction()
        {
            if (_done) { Close(); return; }
            _actionBtn.interactable = false;
            if (_actionLabel != null) UGUIShip.RelabelText(_actionLabel, "ui.cancelling");
            _onCancel?.Invoke();
        }
    }
}
