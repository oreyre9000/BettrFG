using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using BetterFG.Utilities;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Windows
{
    public class MenuMusicWindow : SideWindow
    {
        public MenuMusicWindow(IntPtr ptr) : base(ptr) { }

        protected override float WindowWidth => 280f;
        protected override float WindowHeight => 220f;
        protected override string WindowTitle => "ui.menu_music";

        private const float BTN_W = 44f;
        private const float TOGGLE_W = 36f;
        private const float GAP = 4f;
        private static readonly Color DL_COL = new Color(0.25f, 0.45f, 0.75f, 1f);
        private static readonly Color USED_COL = new Color(0.45f, 0.35f, 0.25f, 1f);
        private static readonly Color DEL_COL = new Color(0.5f, 0.22f, 0.22f, 1f);
        private const float ROW_PAD = 6f;

        private readonly ProgressBarTracker _bars = new ProgressBarTracker();
        private bool _fetchTried;
        private RectTransform _listRt;
        private readonly List<(string path, Button btn)> _useButtons = new List<(string, Button)>();

        // the first-open help prompt points its pulse at these
        public static RectTransform EnabledToggleRect { get; internal set; }
        public static RectTransform RefreshRect { get; internal set; }
        public static RectTransform FirstTrackRect { get; internal set; }

        protected override void BuildContent(RectTransform contentRoot)
        {
            BgPosition = new Vector3(139.3993f, 74.9552f, 0f);
            BgScale = new Vector3(1.3415f, 4.5877f, 1f);
            ContentPosition = new Vector3(-1.6132f, -17.32f, 0f);
            ContentScale = new Vector3(1.0473f, 1.04f, 1f);
            ContentOffsetMin = new Vector2(-1.6132f, -25.92f);
            ContentOffsetMax = new Vector2(-1.6132f, -18.72f);
            Pivot = new Vector2(0f, 0.5f);
            TitlePosition = new Vector3(20.0368f, -6.7966f, 0f);
            TitleScale = new Vector3(1.1818f, 1.3491f, 1f);

            var scroll = UGUIShip.CreateScrollView(contentRoot,
                new Rect(0f, 0f, WindowWidth, WindowHeight - TITLE_H));
            var scrollRt = scroll.scrollRect.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = scrollRt.offsetMax = Vector2.zero;

            var listRt = scroll.content;
            var vlg = listRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 0f;
            listRt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _listRt = listRt;
            _bars.Clear();
            _useButtons.Clear();
            FirstTrackRect = null;
            const float y = OptionsWindowBuilder.BTN_Y, h = OptionsWindowBuilder.BTN_H, step = BTN_W + GAP;

            OptionsWindowBuilder.BuildHeader(listRt, "ui.custom_music");
            bool on = MenuMusicService.Enabled;
            var toggle = UGUIShip.CreateButton(Row(listRt, "ui.custom_song", TOGGLE_W), new Rect(0f, y, TOGGLE_W, h),
                on ? "ui.on" : "ui.off", on ? UGUIShip.TOGGLE_ON : UGUIShip.TOGGLE_OFF, Color.white, 9);
            toggle.onClick.AddListener(new Action(() =>
            {
                bool next = !MenuMusicService.Enabled;
                MenuMusicService.SetEnabled(next);
                Repaint(toggle, next ? "ui.on" : "ui.off", next ? UGUIShip.TOGGLE_ON : UGUIShip.TOGGLE_OFF);
            }));
            EnabledToggleRect = toggle.GetComponent<RectTransform>();

            OptionsWindowBuilder.BuildHeader(listRt, "ui.tracks");
            string count = MenuMusicCatalog.Loaded
                ? LocalizationService.Format("menu_music.available", MenuMusicCatalog.Tracks.Count)
                : "menu_music.loading";
            RefreshRect = UGUIShip.CreateButton(Row(listRt, count, BTN_W, "Refresh"), new Rect(0f, y, BTN_W, h),
                "ui.refresh_2", DL_COL, Color.white, 9, StartFetch).GetComponent<RectTransform>();

            foreach (var t in MenuMusicCatalog.Tracks)
            {
                var track = t;
                var ctrl = Row(listRt, MenuMusicCatalog.DisplayName(track), step + BTN_W, track.name);
                if (FirstTrackRect == null) FirstTrackRect = ctrl.parent.Cast<RectTransform>();

                string cached = MenuMusicCatalog.CachedPath(track);
                if (File.Exists(cached))
                {
                    UGUIShip.CreateLabel(ctrl, new Rect(0f, y, BTN_W, h),
                        "ui.saved", 9, new Color(1f, 1f, 1f, 0.4f), TextAnchor.MiddleCenter);
                    UseButton(ctrl, new Rect(step, y, BTN_W, h), cached);
                    continue;
                }

                var get = UGUIShip.CreateButton(ctrl, new Rect(step, y, BTN_W, h), "ui.get", DL_COL, Color.white, 9);
                get.onClick.AddListener(new Action(() => StartDownload(track, get, ctrl)));
                if (MenuMusicCatalog.Downloading.ContainsKey(track.name)) ShowDownloading(track, get, ctrl);
            }

            OptionsWindowBuilder.BuildHeader(listRt, "menu_music.header_yours");
            foreach (var s in MenuMusicCatalog.CustomSongs())
            {
                string song = s;
                var ctrl = Row(listRt, Path.GetFileNameWithoutExtension(song), 2f * step + h, Path.GetFileName(song));
                UGUIShip.CreateButton(ctrl, new Rect(0f, y, BTN_W, h), "menu_music.change", OptionsWindowBuilder.BTN_IDLE, Color.white, 9,
                    () => Pick(src => MenuMusicCatalog.ReplaceCustom(song, src)));
                UseButton(ctrl, new Rect(step, y, BTN_W, h), song);
                UGUIShip.CreateButton(ctrl, new Rect(2f * step, y, h, h), "menu_music.delete", DEL_COL, Color.white, 9,
                    () => { MenuMusicCatalog.DeleteCustom(song); Refresh(); });
            }
            UGUIShip.CreateButton(Row(listRt, "menu_music.new_song", BTN_W, "AddSong"), new Rect(0f, y, BTN_W, h),
                "menu_music.add", OptionsWindowBuilder.BTN_IDLE, Color.white, 9,
                () => Pick(src => MenuMusicCatalog.AddCustom(src)));

            if (!MenuMusicCatalog.Loaded && !_fetchTried) StartFetch();
        }

        private static RectTransform Row(RectTransform list, string label, float ctrlW, string name = null)
        {
            var ctrl = OptionsWindowBuilder.Row(list, label, ctrlW: ctrlW, name: name);
            var lrt = ctrl.parent.GetComponentInChildren<Text>().rectTransform;
            lrt.anchorMin = new Vector2(0f, lrt.anchorMin.y);
            lrt.anchorMax = new Vector2(1f, lrt.anchorMax.y);
            lrt.offsetMin = new Vector2(SideWindow.RowLabelX, lrt.offsetMin.y);
            lrt.offsetMax = new Vector2(-(ROW_PAD + ctrlW + GAP), lrt.offsetMax.y);
            return ctrl;
        }

        private void Refresh()
        {
            float keep = _listRt != null ? _listRt.anchoredPosition.y : 0f;
            RebuildContent();
            if (_listRt == null) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_listRt);
            _listRt.anchoredPosition = new Vector2(_listRt.anchoredPosition.x, keep);
        }

        private static bool IsUsed(string path) => string.Equals(MenuMusicService.CurrentPath, path, StringComparison.OrdinalIgnoreCase);

        private void UseButton(RectTransform ctrl, Rect rect, string path)
        {
            bool used = IsUsed(path);
            var btn = UGUIShip.CreateButton(ctrl, rect, used ? "ui.used" : "ui.use", used ? USED_COL : UGUIShip.TOGGLE_OFF, Color.white, 9);
            btn.onClick.AddListener(new Action(() =>
            {
                MenuMusicService.SetPath(path);
                foreach (var (p, b) in _useButtons)
                    if (b != null) Repaint(b, IsUsed(p) ? "ui.used" : "ui.use", IsUsed(p) ? USED_COL : UGUIShip.TOGGLE_OFF);
            }));
            _useButtons.Add((path, btn));
        }

        private static void Repaint(Button btn, string text, Color col)
        {
            UGUIShip.RelabelText(btn.GetComponentInChildren<Text>(), text);
            UGUIShip.SetButtonColor(btn, col);
        }

        private void ShowDownloading(MenuMusicCatalog.Track t, Button get, RectTransform ctrl)
        {
            get.interactable = false;
            UGUIShip.RelabelText(get.GetComponentInChildren<Text>(), "...");
            _bars.Add(t.name, UGUIShip.CreateLoadingBar(ctrl.parent, DL_COL));
        }

        private void StartDownload(MenuMusicCatalog.Track t, Button get, RectTransform ctrl)
        {
            ShowDownloading(t, get, ctrl);
            StartCoroutine(MenuMusicCatalog.Download(t, ok => OnDownloaded(t, get, ctrl, ok)).WrapToIl2Cpp());
        }

        private void OnDownloaded(MenuMusicCatalog.Track t, Button get, RectTransform ctrl, bool ok)
        {
            if (get == null) return;
            var bar = ctrl.parent.GetComponentInChildren<LoadingBar>();
            if (bar != null) Destroy(bar.gameObject);
            if (!ok)
            {
                get.interactable = true;
                UGUIShip.RelabelText(get.GetComponentInChildren<Text>(), "ui.get");
                return;
            }
            Destroy(get.gameObject);
            const float y = OptionsWindowBuilder.BTN_Y, h = OptionsWindowBuilder.BTN_H;
            UGUIShip.CreateLabel(ctrl, new Rect(0f, y, BTN_W, h), "ui.saved", 9, new Color(1f, 1f, 1f, 0.4f), TextAnchor.MiddleCenter);
            UseButton(ctrl, new Rect(BTN_W + GAP, y, BTN_W, h), MenuMusicCatalog.CachedPath(t));
        }

        private void Pick(Action<string> onPicked) =>
            WinDialogs.PickAudio(LocalizationService.Get("menu_music.pick_title"), path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                try { onPicked(path); }
                catch (Exception ex) { Plugin.Log.LogWarning($"custom menu song didn't copy over: {ex.Message}"); }
                Refresh();
            });

        public void RefreshList() => Refresh();

        public void StartFetch()
        {
            _fetchTried = true;
            StartCoroutine(MenuMusicCatalog.Fetch(Refresh).WrapToIl2Cpp());
        }

        protected override void ManagedUpdate()
        {
            base.ManagedUpdate();
            _bars.Tick(MenuMusicCatalog.Downloading);
        }
    }
}
