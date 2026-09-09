using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Features.Replay;
using BetterFG.Services;
using BetterFG.Utilities;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class ReplaysTab : ReplayTab
    {
        public ReplaysTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Replays";
        protected override string TitleId => "ui.replays_3";

        protected override string SwitchLabel => "ui.images";
        protected override Tab MakeSwitchTarget() => BetterFGTabRegistry.NewTab<ReplayImagesTab>();

        const float ROW_H = 48f;
        const int PAGE_SIZE = 25;

        const string SP = "BetterFG.assets.ui.feature.qualificationtime.";
        static Sprite _starOn, _starOff, _delIdle, _delHover;
        static Sprite StarOn => _starOn ??= EmbeddedResourceandUnity.LoadSprite(SP + "featurequalificationtime_favoritedstar.png");
        static Sprite StarOff => _starOff ??= EmbeddedResourceandUnity.LoadSprite(SP + "featurequalificationtime_favoritestar.png");
        static Sprite DelIdle => _delIdle ??= EmbeddedResourceandUnity.LoadSprite(SP + "featurequalificationtime_delete_idle.png");
        static Sprite DelHover => _delHover ??= EmbeddedResourceandUnity.LoadSprite(SP + "featurequalificationtime_delete.png");

        static readonly Color TIME_COL = new Color(1f, 0.92f, 0.2f);

        Button _autoBtn, _filterBtn;
        InputField _searchField;
        bool _wasOpen;

        readonly List<(string path, RawImage raw, RectTransform iRt)> _pendingThumbs = new List<(string, RawImage, RectTransform)>();
        Coroutine _thumbRoutine;
        Coroutine _scanRoutine;

        readonly List<ReplayMeta> _data = new List<ReplayMeta>();
        List<ReplayMeta> _filtered = new List<ReplayMeta>();
        string _query = "";
        bool _filterFav, _filterCreative, _filterUnity, _filterMachinima;
        static readonly Color MACHINIMA_COL = new Color(0.82f, 0.72f, 1f);
        enum SortMode { Date, Name, Duration }
        SortMode _sort = SortMode.Date;
        bool _sortDesc = true;

        protected override float BuildHeader(RectTransform contentRoot, float y, float w)
        {
            float openW = 86f;
            float machW = 104f;
            float topH = BTN_H * 0.8f;
            UGUIShip.CreateButton(contentRoot, new Rect(PAD, y, openW, topH), "ui.open_file",
                DARK, Color.white, FS_SM - 1, new Action(OpenFromDisk));
#if MACHINIMA
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + openW + PAD * 0.5f, y, machW, topH),
                "machinima.new", new Color(0.35f, 0.25f, 0.5f, 1f), Color.white, FS_SM - 1, new Action(NewMachinima));
#endif
            y += topH + SH * 0.5f;

            bool auto = FeatureReplay.AutoRecord;
            _autoBtn = UGUIShip.CreateButton(contentRoot, new Rect(PAD, y, w, topH),
                AutoLabel(auto), auto ? GREEN : DARK, Color.white, FS_SM, new Action(ToggleAuto));
            y += topH + SH;

            UGUIShip.CreateDivider(contentRoot, PAD, y, w);
            y += 1f + SH;

            float ddW = 56f;
            float refreshW = 24f;
            float dirW = 24f;
            float searchW = w - (ddW + PAD) * 2f - (dirW + PAD) - (refreshW + PAD);

            _searchField = UGUIShip.CreateInputField(contentRoot, new Rect(PAD, y, searchW, HEADER_H),
                "ui.search_replays", new Color(0f, 0f, 0f, 0.4f), Color.white, FS_SM);
            _searchField.onValueChanged.AddListener(new Action<string>(val =>
            {
                _query = val ?? "";
                ApplySearch();
            }));

            float ddX = PAD + searchW + PAD;
            float listW = 120f;

            _filterBtn = UGUIShip.CreateMultiSelectDropdown(contentRoot, new Rect(ddX, y, ddW, HEADER_H),
                "ui.filters",
                new List<string> { "ui.favourited", "ui.creative_round", "ui.unity_round_2", "machinima.filter" },
                new List<bool> { _filterFav, _filterCreative, _filterUnity, _filterMachinima },
                new Action<int, bool>((i, on) =>
                {
                    if (i == 0) _filterFav = on;
                    else if (i == 1) _filterCreative = on;
                    else if (i == 2) _filterUnity = on;
                    else _filterMachinima = on;
                    var lbl = _filterBtn.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.color = (_filterFav || _filterCreative || _filterUnity || _filterMachinima) ? new Color(1f, 0.85f, 0.2f) : Color.white;
                    ApplySearch();
                }), FS_SM, listW);
            UGUIShip.AddHeaderIcon(_filterBtn, "BetterFG.assets.ui.button.filter.png");

            var sortBtn = UGUIShip.CreateMultiSelectDropdown(contentRoot, new Rect(ddX + ddW + PAD, y, ddW, HEADER_H),
                "ui.sort",
                new List<string> { "ui.sort_by_date", "ui.sort_by_name", "ui.sort_by_length" },
                new List<bool> { _sort == SortMode.Date, _sort == SortMode.Name, _sort == SortMode.Duration },
                new Action<int, bool>((i, on) =>
                {
                    _sort = i == 1 ? SortMode.Name : i == 2 ? SortMode.Duration : SortMode.Date;
                    ApplySearch();
                }), FS_SM, listW, 20f, true, true);
            UGUIShip.AddHeaderIcon(sortBtn, "BetterFG.assets.ui.button.sort.png");

            Button dirBtn = null;
            dirBtn = UGUIShip.CreateButton(contentRoot,
                new Rect(ddX + (ddW + PAD) * 2f, y, dirW, HEADER_H),
                _sortDesc ? "↓" : "↑", DARK, Color.white, FS_SM, new Action(() =>
                {
                    _sortDesc = !_sortDesc;
                    var lbl = dirBtn.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, _sortDesc ? "↓" : "↑");
                    ApplySearch();
                }));
            var dirTxt = dirBtn.GetComponentInChildren<Text>();
            if (dirTxt != null)
            {
                dirTxt.fontSize = FS + 2;
                dirTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
                dirTxt.verticalOverflow = VerticalWrapMode.Overflow;
            }

            var refreshBtn = UGUIShip.CreateButton(contentRoot,
                new Rect(ddX + (ddW + PAD) * 2f + dirW + PAD, y, refreshW, HEADER_H),
                "↻", DARK, Color.white, FS_SM, new Action(Refresh));
            var refreshTxt = refreshBtn.GetComponentInChildren<Text>();
            if (refreshTxt != null)
            {
                refreshTxt.fontSize = FS + 6;
                refreshTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
                refreshTxt.verticalOverflow = VerticalWrapMode.Overflow;
            }

            return y + HEADER_H + SH;
        }

        protected override void BuildListLayout(RectTransform content)
        {
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(2, 2, 2, 2);
            vlg.spacing = 1f;
            vlg.childControlHeight = false;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        void Update()
        {
            if (_wasOpen == IsOpen) return;
            _wasOpen = IsOpen;
            if (!IsOpen) return;
            Refresh();
            PaintAuto(FeatureReplay.AutoRecord);
        }

        static string AutoLabel(bool on) =>
            LocalizationService.Format("ui.auto_record_rounds_state_fmt", LocalizationService.Get(on ? "ui.on" : "ui.off"));

        void ToggleAuto()
        {
            bool on = !FeatureReplay.AutoRecord;
            FeatureReplay.SetAutoRecord(on);
            PaintAuto(on);
            if (on)
                BetterFGUIMan.Instance?.ShowTooltipTimed("ui.replay_fps_warning", 3.5f);
            Plugin.Log.LogInfo(on ? "replays will record from the next round start" : "auto-record off, rounds won't be saved");
        }

        void PaintAuto(bool on)
        {
            _autoBtn.GetComponentInChildren<Text>().text = AutoLabel(on);
            UGUIShip.SetButtonColor(_autoBtn, on ? GREEN : DARK);
        }

        // headers only. thumbnails and rows are built per page in RenderPage so a folder full of
        // replays doesn't turn into a folder full of live textures
        protected override void Refresh()
        {
            if (_scanRoutine != null) StopCoroutine(_scanRoutine);
            _scanRoutine = StartCoroutine(ScanFolder().WrapToIl2Cpp());
        }

        IEnumerator ScanFolder()
        {
            var files = LoadReplay.ListFiles();
            _data.Clear();

            bool painted = false;
            var slice = System.Diagnostics.Stopwatch.StartNew();
            foreach (string path in files)
            {
                _data.Add(LoadReplay.ReadMeta(path));
                if (slice.ElapsedMilliseconds < 4L) continue;

                if (!painted && _data.Count >= PAGE_SIZE) { painted = true; ApplySearch(); }
                SetStatus(LocalizationService.Format("ui.reading_replays_fmt", _data.Count, files.Count));
                yield return null;
                slice.Restart();
            }

            _scanRoutine = null;
            ApplySearch();
        }

        void ApplySearch()
        {
            string q = (_query ?? "").ToLowerInvariant();
            bool searching = q.Length > 0;

            var matched = _data.Where(m =>
            {
                if (searching && !m.haystack.Contains(q)) return false;
                if (_filterFav && !m.isFav) return false;
                if (_filterCreative && !m.isUgc) return false;
                if (_filterUnity && m.isUgc) return false;
                if (_filterMachinima && !m.isMachinima) return false;
                return true;
            });

            IOrderedEnumerable<ReplayMeta> ordered;
            if (_sort == SortMode.Name)
                ordered = _sortDesc
                    ? matched.OrderByDescending(m => m.label, StringComparer.OrdinalIgnoreCase)
                    : matched.OrderBy(m => m.label, StringComparer.OrdinalIgnoreCase);
            else if (_sort == SortMode.Duration)
                ordered = _sortDesc ? matched.OrderByDescending(m => m.duration) : matched.OrderBy(m => m.duration);
            else
                ordered = _sortDesc ? matched.OrderByDescending(m => m.when) : matched.OrderBy(m => m.when);
            _filtered = ordered.ToList();

            Page = 0;
            RenderPage();
        }

        protected override void RenderPage()
        {
            if (ListContent == null) return;
            if (_thumbRoutine != null) { StopCoroutine(_thumbRoutine); _thumbRoutine = null; }
            _pendingThumbs.Clear();

            ClearList();

            int total = _filtered.Count;
            int pageCount = Math.Max(1, (total + PAGE_SIZE - 1) / PAGE_SIZE);
            Page = Mathf.Clamp(Page, 0, pageCount - 1);
            int start = Page * PAGE_SIZE;
            int end = Math.Min(start + PAGE_SIZE, total);

            for (int i = start; i < end; i++)
                BuildRow(_filtered[i], (i - start) % 2 == 0);

            if (_pendingThumbs.Count > 0)
                _thumbRoutine = StartCoroutine(LoadPendingThumbs().WrapToIl2Cpp());

            ShowPaging(pageCount > 1);

            if (_data.Count == 0) SetStatus(LocalizationService.Get("ui.nothing_recorded_yet"));
            else if (total == 0) SetStatus(LocalizationService.Get("ui.no_results"));
            else if (pageCount > 1) SetStatus(LocalizationService.Format("ui.replays_page_fmt", total, Page + 1, pageCount));
            else SetStatus(LocalizationService.Format(total == 1 ? "ui.replay_count_singular_fmt" : "ui.replay_count_plural_fmt", total));
        }

        void BuildRow(ReplayMeta meta, bool alt)
        {
            var rowGo = new GameObject("ReplayRow");
            rowGo.transform.SetParent(ListContent, false);
            rowGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, ROW_H);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;

            var rowImg = rowGo.AddComponent<Image>();
            rowImg.color = ROW_CLEAR;
            var rowBtn = rowGo.AddComponent<Button>();
            rowBtn.transition = Selectable.Transition.None;
            var nav = rowBtn.navigation;
            nav.mode = Navigation.Mode.None;
            rowBtn.navigation = nav;
            rowBtn.onClick.AddListener(new Action(() =>
            {
                AudioService.PlayButtonClick();
                OpenFile(meta.path);
            }));
            UGUIShip.PaintRowStripe(rowGo, alt, ROW_ALT);

            float thumbW = ROW_H * 2.4f;
            float starW = 26f, delW = 24f;
            float rowW = TabWidth - PAD * 2f;

            var cachedThumb = ReplayThumbnail.Peek(meta.path);
            bool hasThumb = cachedThumb != null || ReplayThumbnail.HasThumbnail(meta.path);
            if (hasThumb)
            {
                var maskGo = new GameObject("Thumb");
                maskGo.transform.SetParent(rowGo.transform, false);
                var mRt = maskGo.AddComponent<RectTransform>();
                mRt.anchorMin = new Vector2(0f, 0f);
                mRt.anchorMax = new Vector2(0f, 1f);
                mRt.pivot = new Vector2(0f, 0.5f);
                mRt.anchoredPosition = new Vector2(0f, 0f);
                mRt.sizeDelta = new Vector2(thumbW, 0f);
                maskGo.AddComponent<RectMask2D>();

                var imgGo = new GameObject("Img");
                imgGo.transform.SetParent(maskGo.transform, false);
                var iRt = imgGo.AddComponent<RectTransform>();
                iRt.anchorMin = Vector2.zero;
                iRt.anchorMax = Vector2.one;
                iRt.pivot = new Vector2(0.5f, 0.5f);
                var raw = imgGo.AddComponent<RawImage>();
                raw.raycastTarget = false;

                if (cachedThumb != null)
                {
                    raw.texture = cachedThumb;
                    SizeThumbImage(iRt, thumbW, cachedThumb);
                }
                else
                {
                    _pendingThumbs.Add((meta.path, raw, iRt));
                }
            }

            float timeW = 80f;
            var t = TimeSpan.FromSeconds(meta.duration);
            var tTxt = UGUIShip.CreateLabel(rowGo.transform, new Rect(0f, 0f, timeW, FS + 4f),
                string.Format("{0:D2}:{1:D2}", t.Minutes, t.Seconds), FS, TIME_COL, TextAnchor.UpperRight);
            tTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
            var tRt = tTxt.rectTransform;
            tRt.anchorMin = tRt.anchorMax = tRt.pivot = new Vector2(1f, 1f);
            tRt.anchoredPosition = new Vector2(-(starW + delW + 8f), -3f);

            double sizeMb = meta.sizeBytes / (1024.0 * 1024.0);
            var szTxt = UGUIShip.CreateLabel(rowGo.transform, new Rect(0f, 0f, timeW, FS_SM),
                (sizeMb >= 100.0 ? $"{sizeMb:F0} MB" : $"{sizeMb:F1} MB"), FS_SM - 2, DIM, TextAnchor.UpperRight);
            szTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
            var szRt = szTxt.rectTransform;
            szRt.anchorMin = szRt.anchorMax = szRt.pivot = new Vector2(1f, 1f);
            szRt.anchoredPosition = new Vector2(-(starW + delW + 8f), -3f - (FS + 4f));

            float textX = (hasThumb ? thumbW + 6f : 4f);
            float textW = rowW - textX - starW - delW - 12f;

            var dTxt = UGUIShip.CreateLabel(rowGo.transform, new Rect(0f, 0f, textW, FS_SM),
                meta.when.ToString("g", System.Globalization.CultureInfo.CurrentCulture),
                FS_SM - 2, DIM, TextAnchor.UpperLeft);
            dTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
            dTxt.verticalOverflow = VerticalWrapMode.Truncate;
            var dRt2 = dTxt.rectTransform;
            dRt2.anchorMin = dRt2.anchorMax = dRt2.pivot = new Vector2(0f, 1f);
            dRt2.anchoredPosition = new Vector2(textX, -3f);

            var nameMask = new GameObject("NameMask");
            nameMask.transform.SetParent(rowGo.transform, false);
            var nmRt = nameMask.AddComponent<RectTransform>();
            nmRt.anchorMin = new Vector2(0f, 0f);
            nmRt.anchorMax = new Vector2(0f, 0f);
            nmRt.pivot = new Vector2(0f, 0f);
            nmRt.anchoredPosition = new Vector2(textX, 3f);
            nmRt.sizeDelta = new Vector2(textW, FS_SM + 4f);
            nameMask.AddComponent<RectMask2D>();

            var nTxt = UGUIShip.CreateLabel(nameMask.transform, new Rect(0f, 0f, textW + 400f, 0f),
                meta.label, FS_SM, meta.isMachinima ? MACHINIMA_COL : meta.isUgc ? UGC_COL : Color.white, TextAnchor.LowerLeft);
            nTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
            nTxt.verticalOverflow = VerticalWrapMode.Overflow;
            var nRt = nTxt.rectTransform;
            nRt.anchorMin = new Vector2(0f, 0f);
            nRt.anchorMax = new Vector2(0f, 1f);
            nRt.pivot = new Vector2(0f, 0f);
            nRt.anchoredPosition = Vector2.zero;

            string sub = meta.players == 1
                ? LocalizationService.Get("ui.player_count_singular")
                : LocalizationService.Format("ui.player_count_plural_fmt", meta.players);
            if (!string.IsNullOrEmpty(meta.shareCode)) sub = meta.shareCode + "  ·  " + sub;
            if (meta.isMachinima) sub = LocalizationService.Get("machinima.badge") + "  ·  " + sub;
            var cTxt = UGUIShip.CreateLabel(rowGo.transform, new Rect(0f, 0f, textW, FS_SM),
                sub, FS_SM - 2, DIM, TextAnchor.LowerLeft);
            cTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
            cTxt.verticalOverflow = VerticalWrapMode.Truncate;
            var cRt = cTxt.rectTransform;
            cRt.anchorMin = cRt.anchorMax = cRt.pivot = new Vector2(0f, 0f);
            cRt.anchoredPosition = new Vector2(textX, 3f + FS_SM + 3f);

            var starOn = StarOn;
            var starOff = StarOff;
            var (_, starImg) = UGUIShip.CreateSpriteButton(rowGo.transform,
                new Rect(0f, 0f, starW, starW), meta.isFav ? starOn : starOff, null, null);
            var sRt = starImg.GetComponent<RectTransform>();
            sRt.anchorMin = sRt.anchorMax = new Vector2(1f, 0.5f);
            sRt.pivot = new Vector2(1f, 0.5f);
            sRt.anchoredPosition = new Vector2(-(delW + 6f), 0f);
            starImg.GetComponent<Button>().onClick.AddListener(new Action(() =>
            {
                bool now = LoadReplay.ToggleFavourite(meta.path);
                meta.isFav = now;
                var spr = now ? starOn : starOff;
                if (spr != null) starImg.sprite = spr;
                if (_filterFav && !now) ApplySearch();
            }));

            var (_, delImg) = UGUIShip.CreateSpriteButton(rowGo.transform,
                new Rect(0f, 0f, delW, delW), DelIdle, DelHover, new Action(() =>
                {
                    if (LoadReplay.Delete(meta.path)) Refresh();
                }));
            var dRt = delImg.GetComponent<RectTransform>();
            dRt.anchorMin = dRt.anchorMax = new Vector2(1f, 0.5f);
            dRt.pivot = new Vector2(1f, 0.5f);
            dRt.anchoredPosition = new Vector2(-3f, 0f);
        }

        static void SizeThumbImage(RectTransform iRt, float thumbW, Texture thumb)
        {
            float imgH = thumbW / ((float)thumb.width / thumb.height);
            iRt.offsetMin = new Vector2(0f, -(imgH - ROW_H) * 0.5f);
            iRt.offsetMax = new Vector2(0f, (imgH - ROW_H) * 0.5f);
        }

        IEnumerator LoadPendingThumbs()
        {
            var batch = new List<(string path, RawImage raw, RectTransform iRt)>(_pendingThumbs);
            _pendingThumbs.Clear();

            foreach (var (path, raw, iRt) in batch)
            {
                if (raw == null || iRt == null) continue;
                var tex = ReplayThumbnail.Load(path);
                if (tex != null && raw != null && iRt != null)
                {
                    raw.texture = tex;
                    SizeThumbImage(iRt, ROW_H * 2.4f, tex);
                }
                yield return null;
            }
            _thumbRoutine = null;
        }

#if MACHINIMA
        static void NewMachinima()
        {
            BetterFGUIMan.Instance?.SetVisible(false);
            MachinimaLevelPrompt.Open();
        }
#endif

        void OpenFromDisk()
        {
            WinDialogs.PickFile("Open replay", new Action<string>(OpenFile), SaveReplay.PickerFilter);
        }

        static void OpenFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var rec = LoadReplay.Read(path);
            if (rec == null) return;

            BetterFGUIMan.Instance?.SetVisible(false);
            if (ReplayViewer.Instance != null) ReplayViewer.Instance.Swap(rec);
            else ReplayViewer.Open(rec);
        }
    }
}
