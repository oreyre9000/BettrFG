using System;
using System.Collections;
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
        protected override float WindowHeight => 160f;
        protected override string WindowTitle => "ui.menu_music";

        private RectTransform _listRt;
        private readonly ProgressBarTracker _bars = new ProgressBarTracker();

        // the first-open help prompt points its pulse at these
        public static RectTransform EnabledToggleRect { get; internal set; }
        public static RectTransform RefreshRect { get; internal set; }
        public static RectTransform FirstTrackRect { get; internal set; }

        protected override void BuildContent(RectTransform contentRoot)
        {
            BgPosition = new Vector3(148.7123f, 50.0206f, 0f);
            BgScale = new Vector3(1.2833f, 4.3332f, 1f);
            ContentPosition = new Vector3(138.3868f, -16.32f, 0f);
            ContentScale = new Vector3(1.0473f, 1f, 1f);
            Pivot = new Vector2(0f, 0.5f);
            TitlePosition = new Vector3(27.8546f, -20.2182f, 0);
            TitleScale = new Vector3(1.1818f, 1.3491f, 1f);

            var scroll = UGUIShip.CreateScrollView(contentRoot,
                new Rect(0f, 0f, WindowWidth, WindowHeight - TITLE_H));
            var scrollRt = scroll.scrollRect.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = scrollRt.offsetMax = Vector2.zero;

            _listRt = scroll.content;
            var vlg = _listRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 0f;
            var csf = _listRt.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Rebuild();

            if (!MenuMusicCatalog.Loaded)
                StartCoroutine(MenuMusicCatalog.Fetch(Rebuild).WrapToIl2Cpp());
        }

        private void Rebuild()
        {
            if (_listRt == null) return;
            for (int i = _listRt.childCount - 1; i >= 0; i--)
                Destroy(_listRt.GetChild(i).gameObject);
            _bars.Clear();
            MenuMusicWindowBuilder.BuildRows(_listRt, this);
        }

        public void RefreshList() => Rebuild();

        public void StartFetch() => StartCoroutine(MenuMusicCatalog.Fetch(Rebuild).WrapToIl2Cpp());

        public void StartDownload(MenuMusicCatalog.Track t, Action<bool> onDone)
        {
            StartCoroutine(MenuMusicCatalog.Download(t, onDone).WrapToIl2Cpp());
            Rebuild();
        }

        internal void RegisterBar(string trackName, LoadingBar bar) => _bars.Add(trackName, bar);

        protected override void ManagedUpdate()
        {
            base.ManagedUpdate();
            _bars.Tick(MenuMusicCatalog.Downloading);
        }
    }

    internal static class MenuMusicWindowBuilder
    {
        private const float ROW_H = 22f;
        private const float TOGGLE_W = 36f;
        private const float BTN_W = 44f;
        private const float TOGGLE_H = 16f;
        private const float PAD = 6f;
        private const float HEADER_H = 18f;
        private const float HEADER_LEFT = SideWindow.RowLabelX;
        private const float LABEL_X = SideWindow.RowLabelX;
        private const float HEADER_SCALE = 1.3f;
        private static readonly Color ROW_EVEN = new Color(1f, 1f, 1f, 0.03f);
        private static readonly Color ROW_ODD = new Color(0f, 0f, 0f, 0f);
        private static readonly Color ON_COL = UGUIShip.TOGGLE_ON;
        private static readonly Color OFF_COL = UGUIShip.TOGGLE_OFF;
        private static readonly Color DL_COL = new Color(0.25f, 0.45f, 0.75f, 1f);
        private static readonly Color USED_COL = new Color(0.45f, 0.35f, 0.25f, 1f);

        public static void BuildRows(RectTransform parent, MenuMusicWindow window)
        {
            MenuMusicWindow.FirstTrackRect = null;
            BuildHeader(parent, "ui.custom_music");
            BuildEnabledRow(parent, ROW_EVEN);

            BuildHeader(parent, "ui.tracks");
            BuildRefreshRow(parent, ROW_EVEN, window);

            int i = 0;
            foreach (var t in MenuMusicCatalog.Tracks)
            {
                BuildTrackRow(parent, i % 2 == 0 ? ROW_ODD : ROW_EVEN, t, window);
                i++;
            }
        }

        private static void BuildHeader(RectTransform parent, string title)
        {
            var rowGo = new GameObject("Header_" + title);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = HEADER_H;
            le.flexibleWidth = 1f;

            var lbl = UGUIShip.CreateLabel(rowGo.transform,
                new Rect(HEADER_LEFT, 0f, 200f, HEADER_H),
                title, 10, Color.white, TextAnchor.MiddleLeft);
            lbl.fontStyle = FontStyle.Bold;
            var rt = lbl.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(HEADER_LEFT, 0f);
            rt.localScale = new Vector3(HEADER_SCALE, HEADER_SCALE, 1f);
        }

        private static void BuildEnabledRow(RectTransform parent, Color bg)
        {
            var rowGo = new GameObject("Row_Enabled");
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            UGUIShip.PaintStaticRowFill(rowGo, bg);

            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(LABEL_X, 0f,200f, ROW_H),
                "ui.custom_song", 13,
                new Color(1f, 1f, 1f, 0.85f),
                TextAnchor.MiddleLeft);

            var btnGo = new GameObject("Toggle");
            btnGo.transform.SetParent(rowGo.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(1f, 0.5f);
            btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-PAD, 0f);
            btnRt.sizeDelta = new Vector2(TOGGLE_W, TOGGLE_H);
            MenuMusicWindow.EnabledToggleRect = btnRt;

            bool on = MenuMusicService.Enabled;
            var btn = UGUIShip.CreateButton(btnGo.transform,
                new Rect(0f, 0f, TOGGLE_W, TOGGLE_H),
                on ? "ui.on" : "ui.off",
                on ? ON_COL : OFF_COL,
                Color.white, 9);
            var lbl = btn.GetComponentInChildren<Text>();

            btn.onClick.AddListener(new Action(() =>
            {
                bool next = !MenuMusicService.Enabled;
                MenuMusicService.SetEnabled(next);
                Paint(btn, next ? ON_COL : OFF_COL);
                if (lbl != null) UGUIShip.RelabelText(lbl, next ? "ui.on" : "ui.off");
            }));
        }

        private static void BuildRefreshRow(RectTransform parent, Color bg, MenuMusicWindow window)
        {
            var rowGo = new GameObject("Row_Refresh");
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            UGUIShip.PaintStaticRowFill(rowGo, bg);

            string countTxt = MenuMusicCatalog.Loaded
                ? $"{MenuMusicCatalog.Tracks.Count} available"
                : "loading...";
            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(LABEL_X, 0f,200f, ROW_H),
                countTxt, 13,
                new Color(1f, 1f, 1f, 0.55f),
                TextAnchor.MiddleLeft);

            var btnGo = new GameObject("Refresh");
            btnGo.transform.SetParent(rowGo.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(1f, 0.5f);
            btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-PAD, 0f);
            btnRt.sizeDelta = new Vector2(BTN_W, TOGGLE_H);
            MenuMusicWindow.RefreshRect = btnRt;

            UGUIShip.CreateButton(btnGo.transform,
                new Rect(0f, 0f, BTN_W, TOGGLE_H),
                "ui.refresh_2", DL_COL, Color.white, 9)
                .onClick.AddListener(new Action(() => window.StartFetch()));
        }

        private static void BuildTrackRow(RectTransform parent, Color bg,
            MenuMusicCatalog.Track track, MenuMusicWindow window)
        {
            var rowGo = new GameObject("Row_" + track.name);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            UGUIShip.PaintStaticRowFill(rowGo, bg);
            if (MenuMusicWindow.FirstTrackRect == null)
                MenuMusicWindow.FirstTrackRect = rowGo.GetComponent<RectTransform>();

            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(LABEL_X, 0f,160f, ROW_H),
                StripExt(track.name), 13,
                new Color(1f, 1f, 1f, 0.85f),
                TextAnchor.MiddleLeft);

            string cachedPath = MenuMusicCatalog.CachedPath(track);
            bool isCached = File.Exists(cachedPath);
            bool isUsed = isCached && string.Equals(MenuMusicService.CurrentPath, cachedPath,
                StringComparison.OrdinalIgnoreCase);

            // ── right side: [Use] then [Download], right-aligned ──
            float rightOffset = PAD;

            // Use button (only if cached)
            if (isCached)
            {
                var useGo = new GameObject("Use");
                useGo.transform.SetParent(rowGo.transform, false);
                var useRt = useGo.AddComponent<RectTransform>();
                useRt.anchorMin = new Vector2(1f, 0.5f);
                useRt.anchorMax = new Vector2(1f, 0.5f);
                useRt.pivot = new Vector2(1f, 0.5f);
                useRt.anchoredPosition = new Vector2(-rightOffset, 0f);
                useRt.sizeDelta = new Vector2(BTN_W, TOGGLE_H);

                UGUIShip.CreateButton(useGo.transform,
                    new Rect(0f, 0f, BTN_W, TOGGLE_H),
                    isUsed ? "ui.used" : "ui.use",
                    isUsed ? USED_COL : OFF_COL,
                    Color.white, 9)
                    .onClick.AddListener(new Action(() =>
                    {
                        MenuMusicService.SetPath(cachedPath);
                        window.RefreshList();
                    }));

                rightOffset += BTN_W + 3f;
            }

            // Download / cached marker
            var dlGo = new GameObject("Dl");
            dlGo.transform.SetParent(rowGo.transform, false);
            var dlRt = dlGo.AddComponent<RectTransform>();
            dlRt.anchorMin = new Vector2(1f, 0.5f);
            dlRt.anchorMax = new Vector2(1f, 0.5f);
            dlRt.pivot = new Vector2(1f, 0.5f);
            dlRt.anchoredPosition = new Vector2(-rightOffset, 0f);
            dlRt.sizeDelta = new Vector2(BTN_W, TOGGLE_H);

            if (isCached)
            {
                UGUIShip.CreateLabel(dlGo.transform,
                    new Rect(0f, 0f, BTN_W, TOGGLE_H),
                    "ui.saved", 9, new Color(1f, 1f, 1f, 0.4f),
                    TextAnchor.MiddleCenter);
            }
            else
            {
                bool busy = MenuMusicCatalog.Downloading.ContainsKey(track.name);
                var dlBtn = UGUIShip.CreateButton(dlGo.transform,
                    new Rect(0f, 0f, BTN_W, TOGGLE_H),
                    busy ? "..." : "ui.get", DL_COL, Color.white, 9);
                dlBtn.interactable = !busy;
                dlBtn.onClick.AddListener(new Action(() =>
                    window.StartDownload(track, ok => window.RefreshList())));

                if (busy)
                    window.RegisterBar(track.name, UGUIShip.CreateLoadingBar(rowGo.transform, DL_COL));
            }
        }

        private static string StripExt(string name)
        {
            int dot = name.LastIndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
        }

        private static void Paint(Button btn, Color c) => UGUIShip.SetButtonColor(btn, c);
    }
}
