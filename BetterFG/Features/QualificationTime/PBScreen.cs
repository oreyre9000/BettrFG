using System;
using System.Collections.Generic;
using System.Globalization;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.UI;
using BetterFG.Utilities;
using FG.Common;
using FGClient.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Features.QualificationTime
{
    internal static class PBScreen
    {
        const float RowH = 64f, RowGap = 4f, RowStep = RowH + RowGap;
        const float LX = 64f, LW = 520f, RX = 608f, RW = 1248f, Top = 300f, PanelH = 700f;
        const float StageW = 1920f, StageH = 1080f;
        const string DeleteTitleKey = "bfg_pb_delete_title", DeleteBodyKey = "bfg_pb_delete_body";

        static readonly Color Ink = new Color(0.133f, 0.137f, 0.149f, 1f);
        static readonly Color Slate = new Color(0.18f, 0.19f, 0.215f, 1f);
        static readonly Color SlateLit = new Color(0.26f, 0.27f, 0.3f, 1f);
        static readonly Color Night = new Color(0.035f, 0.05f, 0.11f, 1f);
        static readonly Color Yellow = new Color(1f, 0.78f, 0f, 1f);
        static readonly Color TextYellow = new Color(1f, 0.89f, 0.12f, 1f);
        static readonly Color Pink = new Color(0.976f, 0f, 0.588f, 1f);
        static readonly Color Cyan = new Color(0.08f, 0.62f, 0.8f, 1f);
        static readonly Color Red = new Color(0.906f, 0.11f, 0.259f, 1f);
        static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);
        static readonly Color Faint = new Color(1f, 1f, 1f, 0.3f);
        static readonly Color Clear = new Color(1f, 1f, 1f, 0f);

        static readonly string[] ShowIds = { "ui.solos", "ui.duos", "ui.squads", "ui.time_attack" };
        static readonly string[] SortIds = { "pb_screen.sort_newest", "pb_screen.sort_fastest", "pb_screen.sort_slowest", "pb_screen.sort_name" };
        static readonly string[] FilterIds = { "pb_screen.filters", "pb_screen.filter_favourites", "pb_screen.filter_ghosts", "pb_screen.filter_creative", "pb_screen.filter_unity" };

        static readonly string[] PadLeft = { "D-Pad Left", "DPad Left", "Hat Left" };
        static readonly string[] PadRight = { "D-Pad Right", "DPad Right", "Hat Right" };
        static readonly string[] PadY = { "Y", "Triangle" };
        static readonly string[] PadX = { "X", "Square" };
        static readonly string[] PadL3 = { "Left Stick Button", "L3", "Left Stick Click" };
        static readonly string[] PadLT = { "Left Trigger", "LT", "L2" };
        static readonly string[] PadRT = { "Right Trigger", "RT", "R2" };

        sealed class Row
        {
            public string Name, RawId, Code, Haystack;
            public readonly float?[] Times = new float?[4];
            public readonly string[] Dates = new string[4];
            public bool Ugc, Fav, Ghost;
        }

        sealed class Glyph { public Image Img; public KeyCode Key; public string[] Pad; }

        static Dictionary<string, Sprite> _art;
        static TMP_FontAsset _titleFont, _bodyFont;
        static Material _titleMat, _bodyMat, _scrollMat;
        static PunchScaleTween _punchTemplate;
        static PunchScaleTween _sortPunch, _filterPunch;

        static GameObject _root;
        static RectTransform _screen, _stage, _pill, _view, _content, _fills, _rowLayer, _cursor, _scrollThumb, _codePill;
        static TextMeshProUGUI _recordsN, _recordsL, _favsN, _favsL, _searchTx, _filterTx, _sortTx, _name, _date, _time, _codeTx, _favTx, _empty;
        static Image _searchOutline, _filterFill, _heart, _ghost;
        static RawImage _splashA, _splashB;
        static readonly TextMeshProUGUI[] _segTx = new TextMeshProUGUI[4], _segCount = new TextMeshProUGUI[4], _showLabel = new TextMeshProUGUI[4], _showValue = new TextMeshProUGUI[4];
        static readonly Image[] _showFill = new Image[4];
        static readonly List<Glyph> _glyphs = new List<Glyph>();
        static readonly List<(RectTransform rt, Action act)> _hits = new List<(RectTransform, Action)>();
        static readonly List<Row> _all = new List<Row>(), _rows = new List<Row>();
        static readonly List<RectTransform> _built = new List<RectTransform>();
        static readonly HashSet<int> _thumbTried = new HashSet<int>();
        static readonly Dictionary<string, float> _repeatAt = new Dictionary<string, float>();
        static readonly HashSet<string> _edgeHeld = new HashSet<string>();

        static float _segW, _scroll, _scrollTarget, _cursorY, _cursorTarget, _heroAt;
        static int _show, _sort, _filter, _sel = -1, _quietUntilFrame;
        static string _query = "";
        static bool _typing, _asking, _dirty;
        static int _padGlyphs = -1;
        static Row _pendingDelete;

        internal static GameObject Open(Transform screensParent)
        {
            Prebuild(screensParent);
            _typing = _asking = false;
            _padGlyphs = -1;
            _quietUntilFrame = Time.frameCount + 2;
            if (_dirty)
            {
                _dirty = false;
                LoadData();
                Refilter(true);
            }
            _root.SetActive(true);
            var canvas = _root.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 0;
            RefreshChrome();
            return _root;
        }

        internal static void Close()
        {
            StopTyping();
            _root.SetActive(false);
        }

        internal static void Prebuild(Transform screensParent)
        {
            if (_root != null && _root.transform.parent == screensParent) return;
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
            CacheArt();
            _root = new GameObject("Menu_Screen_PersonalBests");
            _root.SetActive(false);
            var root = _root.AddComponent<RectTransform>();
            root.SetParent(screensParent, false);
            Fill(root);
            _root.AddComponent<Canvas>();
            _root.AddComponent<GraphicRaycaster>();

            _glyphs.Clear();
            _hits.Clear();
            _built.Clear();
            _thumbTried.Clear();
            _repeatAt.Clear();
            _edgeHeld.Clear();
            _show = _sort = _filter = 0;
            _query = "";
            _typing = _asking = _dirty = false;
            _padGlyphs = -1;
            _sel = -1;

            Build(root);
            LoadData();
            Refilter(false);
            _cursorY = _cursorTarget;
            PBStore.OnChanged -= OnStoreChanged;
            PBStore.OnChanged += OnStoreChanged;
            LocalizationService.LanguageChanged -= OnLanguageChanged;
            LocalizationService.LanguageChanged += OnLanguageChanged;
            _root.AddComponent<PBScreenDriver>();
        }

        static void OnLanguageChanged()
        {
            if (_root == null) return;
            StopTyping();
            int show = _show, sort = _sort, filter = _filter;
            string query = _query;
            var root = _root.GetComponent<RectTransform>();
            for (int i = root.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
            _glyphs.Clear();
            _hits.Clear();
            _built.Clear();
            _thumbTried.Clear();
            _sel = -1;
            _padGlyphs = -1;
            Build(root);
            LoadData();
            _show = show;
            _sort = sort;
            _filter = filter;
            _query = query;
            Refilter(false);
            _cursorY = _cursorTarget;
            RefreshChrome();
        }

        internal static void Closed()
        {
            PBStore.OnChanged -= OnStoreChanged;
            LocalizationService.LanguageChanged -= OnLanguageChanged;
            if (_typing)
            {
                FGInputLockService.SetScreenLock(false);
                OnScreenKeyboard.CloseExternal(BetterFGUIMan.Instance);
            }
            _typing = false;
            _root = null;
            _all.Clear();
            _rows.Clear();
            _built.Clear();
        }

        static void CacheArt()
        {
            if (_art != null) return;
            _art = new Dictionary<string, Sprite>();
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>()) _art[s.name] = s;
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (f.name == "TitanOne-Expanded SDF (Title)") _titleFont = f;
                if (f.name == "Asap-Bold SDF (Body)") _bodyFont = f;
            }
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m.name == "TitanOne-Expanded SDF Material - DropShadowAlpha_2_0") _titleMat = m;
                if (m.name == "asap-bold sdf_2.0_Shadow") _bodyMat = m;
                if (m.name == "crown-pattern-UI") _scrollMat = m;
            }
            foreach (var p in Resources.FindObjectsOfTypeAll<PunchScaleTween>())
                if (!p.gameObject.scene.IsValid()) { _punchTemplate = p; break; }
            Plugin.Log.LogInfo($"pb screen art cached, {_art.Count} sprites, title font {(_titleFont != null)}, body font {(_bodyFont != null)}, punch from {(_punchTemplate != null ? _punchTemplate.name : "nowhere")}");
        }

        static void LoadData()
        {
            _all.Clear();
            foreach (var kv in PBStore.GetAllEntriesDeduped())
            {
                var e = kv.Value;
                var r = new Row
                {
                    Name = string.IsNullOrEmpty(e.displayName) ? kv.Key : e.displayName,
                    RawId = e.rawId ?? kv.Key,
                    Ugc = e.isUgc,
                };
                r.Times[0] = e.solos; r.Times[1] = e.duos; r.Times[2] = e.squads; r.Times[3] = e.timeattack;
                r.Dates[0] = e.solosDate ?? e.date; r.Dates[1] = e.duosDate ?? e.date; r.Dates[2] = e.squadsDate ?? e.date; r.Dates[3] = e.timeattackDate ?? e.date;
                r.Code = ShareCode(r.RawId);
                r.Haystack = (r.Name + " " + kv.Key + " " + r.Code).ToLowerInvariant();
                r.Fav = PBStore.IsFeatured(r.RawId, r.Name);
                r.Ghost = FeatureQualificationTime.HasGhost(r.Name, r.RawId);
                _all.Add(r);
            }
        }

        static void OnStoreChanged()
        {
            if (_root == null) return;
            if (!_root.activeSelf)
            {
                _dirty = true;
                return;
            }
            LoadData();
            Refilter(true);
        }

        static string ShareCode(string id)
        {
            int i = id.IndexOf("ugc-", StringComparison.Ordinal);
            if (i < 0) return null;
            var c = id.Substring(i + 4);
            int cut = c.IndexOf('_');
            return cut >= 0 ? c.Substring(0, cut) : c;
        }

        static string FormatTime(float s)
        {
            var t = TimeSpan.FromSeconds(s);
            return string.Format("{0:D2}:{1:D2}.{2:D3}", (int)t.TotalMinutes, t.Seconds, t.Milliseconds);
        }

        static string FormatDate(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return raw;
            string text = dt.ToString("d MMM yyyy", LocalizationService.Culture) + ", " + dt.ToString("t", LocalizationService.Culture);
            var fallbacks = _bodyFont.fallbackFontAssetTable;
            if (fallbacks != null) for (int i = 0; i < fallbacks.Count; i++) fallbacks[i].TryAddCharacters(text, false);
            TMP_Settings.defaultFontAsset.TryAddCharacters(text, false);
            return text;
        }

        static string Loc(string id) => LocalizationService.Get(id);

        static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var r = new GameObject(name).AddComponent<RectTransform>();
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
            return r;
        }

        static RectTransform Fill(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            return r;
        }

        static Image Img(Transform parent, string name, string sprite, Color c, float x, float y, float w, float h, float ppu = 1f)
        {
            var img = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
            _art.TryGetValue(sprite, out var s);
            img.sprite = s;
            img.color = c;
            img.raycastTarget = false;
            if (s != null && s.border != Vector4.zero)
            {
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = ppu;
            }
            else img.preserveAspect = true;
            return img;
        }

        static Image FillImg(Transform parent, string name, string sprite, Color c, float ppu = 1f)
        {
            var img = Img(parent, name, sprite, c, 0, 0, 0, 0, ppu);
            Fill(img.rectTransform);
            return img;
        }

        static TextMeshProUGUI Txt(Transform parent, string name, string text, bool title, float size, Color c, TextAlignmentOptions align, float x, float y, float w, float h)
        {
            var t = Rect(parent, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            t.font = title ? _titleFont : _bodyFont;
            t.fontSharedMaterial = title ? _titleMat : _bodyMat;
            t.fontStyle = title ? FontStyles.UpperCase : FontStyles.Normal;
            t.text = text;
            t.fontSize = size;
            t.color = c;
            t.alignment = align;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        static void AddGlyph(Transform parent, string name, KeyCode key, string[] pad, float x, float y, float size)
        {
            var img = Img(parent, name, "UI_Clock", Color.white, x, y, size, size);
            _glyphs.Add(new Glyph { Img = img, Key = key, Pad = pad });
        }

        static void Build(RectTransform screen)
        {
            var ML = TextAlignmentOptions.MidlineLeft;
            var MC = TextAlignmentOptions.Midline;
            var MR = TextAlignmentOptions.MidlineRight;

            var bg = Fill(Rect(screen, "Background", 0, 0, 0, 0));
            bg.gameObject.AddComponent<Image>().color = new Color(0.035f, 0.07f, 0.17f, 1f);
            var grad = FillImg(bg, "Gradient", "UI_LinearGradient_Image", new Color(0f, 0.62f, 0.72f, 0.75f));
            grad.type = Image.Type.Simple;
            grad.preserveAspect = false;
            var patTex = EmbeddedResourceandUnity.LoadTexture("BetterFG.assets.ui.general.pattern_clocks.png");
            patTex.wrapMode = TextureWrapMode.Repeat;
            var pattern = Fill(Rect(bg, "Pattern", 0, 0, 0, 0)).gameObject.AddComponent<Image>();
            pattern.sprite = EmbeddedResourceandUnity.SpriteFor(patTex);
            pattern.type = Image.Type.Tiled;
            pattern.material = new Material(_scrollMat);
            pattern.color = new Color(1f, 1f, 1f, 0.07f);
            pattern.raycastTarget = false;
            var scroll = pattern.gameObject.AddComponent<UI_ScrollUvs>();
            scroll._ScrollX = scroll._ScrollY = 0.04f;

            _screen = screen;
            var root = _stage = Rect(screen, "Stage", 0, 0, StageW, StageH);

            var title = Rect(root, "Title", -40, 136, LX + 510, 72);
            var tsh = FillImg(title, "Shadow", "UI_CurvedSlopePanel1_Image", new Color(0f, 0f, 0f, 0.35f), 2f);
            tsh.rectTransform.anchoredPosition = new Vector2(0, -6);
            FillImg(title, "Fill", "UI_CurvedSlopePanel1_Image", Yellow, 2f);
            var tpm = FillImg(title, "PatternMask", "UI_CurvedSlopePanel1_Image", Color.white, 2f);
            tpm.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            FillImg(tpm.transform, "Pattern", "UI_BubblesPattern_Image", new Color(0f, 0f, 0f, 0.07f)).type = Image.Type.Tiled;
            FillImg(title, "Outline", "UI_CurvedSlopePanelOutline1_Image", Color.white, 2f);
            Img(title, "Clock", "UI_BigClock_Icon", Ink, LX + 56, 8, 56, 56);
            Txt(title, "Text", Loc("pb_screen.title"), true, 36, Ink, ML, LX + 122, 0, 370, 72);

            var sw = Rect(root, "ShowSwitcher", RX, 140, RW, 64);
            FillImg(sw, "Fill", "UI_RoundedPanel2_Image", Ink, 2f);
            AddGlyph(sw, "Prev", KeyCode.A, PadLeft, 12, 14, 36);
            AddGlyph(sw, "Next", KeyCode.D, PadRight, RW - 48, 14, 36);
            _segW = (RW - 132) / 4f;
            _pill = Rect(sw, "Pill", 60, 6, _segW, 52);
            FillImg(_pill, "Fill", "UI_RoundedPanel2_Image", Pink, 2.4f);
            FillImg(_pill, "Outline", "UI_RoundedPanelOutline1_Image", Color.white, 2.4f);
            for (int i = 0; i < 4; i++)
            {
                int show = i;
                var seg = Rect(sw, "Show_" + i, 60 + i * (_segW + 4), 6, _segW, 52);
                _segTx[i] = Txt(seg, "Text", Loc(ShowIds[i]), true, 22, Dim, MC, 0, 0, _segW, 52);
                _segCount[i] = Txt(seg, "Count", "", false, 18, Dim, MR, 0, 0, _segW - 18, 52);
                _hits.Add((seg, () => SetShow(show)));
            }

            var stats = Rect(root, "Stats", LX, 228, LW, 56);
            _recordsN = Txt(stats, "Records", "", true, 34, TextYellow, ML, 0, 0, 120, 56);
            _recordsL = Txt(stats, "RecordsLabel", Loc("pb_screen.records"), false, 18, Dim, ML, 0, 0, 180, 56);
            _favsN = Txt(stats, "Favourites", "", true, 34, Pink, ML, 0, 0, 120, 56);
            _favsL = Txt(stats, "FavouritesLabel", Loc("pb_screen.favourites"), false, 18, Dim, ML, 0, 0, 180, 56);

            var bar = Rect(root, "Toolbar", RX, 228, RW, 56);
            var search = Rect(bar, "Search", 0, 0, 760, 56);
            FillImg(search, "Fill", "UI_RoundedPanel2_Image", Ink, 2f);
            _searchOutline = FillImg(search, "Outline", "UI_RoundedPanelOutline1_Image", Clear, 2f);
            AddGlyph(search, "Glyph", KeyCode.Return, PadL3, 12, 10, 36);
            _searchTx = Txt(search, "Text", "", false, 24, Dim, ML, 60, 0, 680, 56);
            _hits.Add((search, StartSearch));
            var filter = Rect(bar, "Filter", 772, 0, 228, 56);
            _filterFill = FillImg(filter, "Fill", "UI_RoundedPanel2_Image", Ink, 2f);
            AddGlyph(filter, "Glyph", KeyCode.T, PadLT, 12, 10, 36);
            _filterTx = Txt(filter, "Text", "", true, 19, Color.white, MC, 52, 0, 164, 56);
            _hits.Add((filter, CycleFilter));
            _filterPunch = AddPunch(filter);
            var sort = Rect(bar, "Sort", 1012, 0, 236, 56);
            FillImg(sort, "Fill", "UI_RoundedPanel2_Image", Ink, 2f);
            AddGlyph(sort, "Glyph", KeyCode.R, PadRT, 12, 10, 36);
            _sortTx = Txt(sort, "Text", "", true, 19, Color.white, MC, 52, 0, 172, 56);
            _hits.Add((sort, CycleSort));
            _sortPunch = AddPunch(sort);

            BuildHero(root);
            BuildList(root);
        }

        static void BuildHero(RectTransform root)
        {
            var ML = TextAlignmentOptions.MidlineLeft;
            var MC = TextAlignmentOptions.Midline;
            const float pad = 12f;
            float w = LW - pad * 2, h = Mathf.Round(w * 9f / 16f);

            var hero = Rect(root, "Hero", LX, Top, LW, PanelH);
            FillImg(hero, "Fill", "UI_RoundedPanel2_Image", Ink, 1.6f);

            var mask = Img(hero, "SplashMask", "UI_RoundedPanel2_Image", Color.white, pad, pad, w, h, 3.2f);
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            _splashB = Fill(Rect(mask.transform, "SplashOld", 0, 0, 0, 0)).gameObject.AddComponent<RawImage>();
            _splashA = Fill(Rect(mask.transform, "Splash", 0, 0, 0, 0)).gameObject.AddComponent<RawImage>();
            _splashA.raycastTarget = _splashB.raycastTarget = false;

            _codePill = Rect(hero, "CodePill", pad + 10, pad + h - 44, 232, 34);
            FillImg(_codePill, "Fill", "UI_32xCircle_Image", new Color(0.133f, 0.137f, 0.149f, 0.95f));
            Img(_codePill, "Icon", "Icon_CreatorMadeTag", Color.white, 4, 3, 28, 28);
            _codeTx = Txt(_codePill, "Text", "", false, 21, Color.white, ML, 38, 0, 190, 34);
            _heart = Img(hero, "Heart", "ui_privatelobby_fav_icon", Color.white, pad + w - 48, pad + 10, 38, 34);
            _ghost = Img(hero, "Ghost", "UI_IconCombinedCostume", new Color(0.9f, 0.88f, 1f, 0.85f), pad + w - 92, pad + 8, 36, 40);

            float y = pad + h + 16;
            _name = Txt(hero, "Name", "", true, 32, Color.white, ML, 20, y, LW - 40, 40);
            _name.enableAutoSizing = true;
            _name.fontSizeMin = 20;
            _name.fontSizeMax = 32;
            _date = Txt(hero, "Date", "", false, 20, Dim, ML, 20, y + 40, LW - 40, 26);

            float btnY = PanelH - pad - 60, showsY = btnY - 74, bestY = showsY - 136;
            var best = Rect(hero, "Best", pad, bestY, w, 120);
            FillImg(best, "Fill", "UI_RoundedPanel2_Image", Night, 2f);
            var tab = Rect(best, "Tab", 0, 0, 150, 30);
            FillImg(tab, "Fill", "UI_CurvedSlopePanel1_Image", Yellow, 3f);
            Txt(tab, "Text", Loc("pb_screen.best_time"), true, 15, Ink, ML, 12, 0, 130, 30);
            Img(best, "Clock", "UI_Clock", TextYellow, 24, 50, 48, 48);
            _time = Txt(best, "Time", "", true, 68, TextYellow, MC, 70, 30, w - 90, 84);

            var times = Rect(hero, "ShowTimes", pad, showsY, w, 62);
            float cw = (w - 12) / 4f;
            for (int i = 0; i < 4; i++)
            {
                var c = Rect(times, "Show_" + i, i * (cw + 4), 0, cw, 62);
                _showFill[i] = FillImg(c, "Fill", "UI_RoundedPanel2_Image", Slate, 2.4f);
                _showLabel[i] = Txt(c, "Label", Loc(ShowIds[i]), true, 13, Dim, MC, 0, 6, cw, 20);
                _showValue[i] = Txt(c, "Value", "", false, 21, Color.white, MC, 0, 28, cw, 28);
            }

            float bw = (w - 8) / 2f;
            var fav = Rect(hero, "FavouriteButton", pad, btnY, bw, 60);
            FillImg(fav, "Shadow", "UI_RoundedPanel2_Image", new Color(0f, 0f, 0f, 0.35f), 2f).rectTransform.anchoredPosition = new Vector2(0, -5);
            FillImg(fav, "Fill", "UI_RoundedPanel2_Image", Yellow, 2f);
            FillImg(fav, "Outline", "UI_RoundedPanelOutline1_Image", Color.white, 2f);
            AddGlyph(fav, "Glyph", KeyCode.F, PadY, 14, 13, 34);
            _favTx = Txt(fav, "Text", "", true, 22, Ink, MC, 48, 0, bw - 56, 60);
            _hits.Add((fav, ToggleFavourite));

            var del = Rect(hero, "DeleteButton", pad + bw + 8, btnY, bw, 60);
            FillImg(del, "Fill", "UI_RoundedPanel2_Image", Slate, 2f);
            FillImg(del, "Outline", "UI_RoundedPanelOutline1_Image", Red, 2f);
            AddGlyph(del, "Glyph", KeyCode.Delete, PadX, 14, 13, 34);
            Txt(del, "Text", Loc("pb_screen.delete"), true, 22, new Color(1f, 0.35f, 0.42f, 1f), MC, 48, 0, bw - 56, 60);
            _hits.Add((del, AskDelete));
        }

        static void BuildList(RectTransform root)
        {
            var list = Rect(root, "List", RX, Top, RW, PanelH);
            FillImg(list, "Fill", "UI_RoundedPanel2_Image", Ink, 1.6f);
            _view = Fill(Rect(list, "Viewport", 0, 0, 0, 0));
            _view.offsetMin = new Vector2(8, 8);
            _view.offsetMax = new Vector2(-20, -8);
            _view.gameObject.AddComponent<Image>().raycastTarget = false;
            _view.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            _content = Rect(_view, "Content", 0, 0, RW - 28, 0);
            _fills = Fill(Rect(_content, "Fills", 0, 0, 0, 0));
            _cursor = Rect(_content, "Cursor", 0, 0, RW - 28, RowH);
            FillImg(_cursor, "Fill", "UI_RoundedPanel2_Image", Pink, 2.4f);
            FillImg(_cursor, "Outline", "UI_RoundedPanelOutline1_Image", Color.white, 2.4f);
            var inline = FillImg(_cursor, "Inline", "UI_RoundedPanelOutline1_Image", Yellow, 2.4f);
            inline.rectTransform.offsetMin = new Vector2(3, 3);
            inline.rectTransform.offsetMax = new Vector2(-3, -3);
            _rowLayer = Fill(Rect(_content, "Rows", 0, 0, 0, 0));

            var track = Rect(list, "Scrollbar", RW - 16, 12, 6, PanelH - 24);
            FillImg(track, "Track", "UI_32xCircle_Image", new Color(1f, 1f, 1f, 0.08f));
            _scrollThumb = Rect(track, "Thumb", 0, 0, 6, 80);
            FillImg(_scrollThumb, "Fill", "UI_32xCircle_Image", new Color(1f, 1f, 1f, 0.5f));
            _empty = Txt(list, "Empty", "", true, 26, Dim, TextAlignmentOptions.Midline, 0, 0, RW, PanelH);
        }

        static RectTransform BuildRow(int i, Row r)
        {
            float w = RW - 28, y = i * RowStep;
            Img(_fills, "Fill", "UI_RoundedPanel2_Image", Slate, 0, y, w, RowH, 2.4f);
            var row = Rect(_rowLayer, "Row_" + i, 0, y, w, RowH);
            var rank = Img(row, "Rank", "UI_96xCircle_Image", new Color(1f, 1f, 1f, 0.08f), 14, 14, 36, 36);
            Txt(rank.transform, "N", (i + 1).ToString(), true, 17, Color.white, TextAlignmentOptions.Midline, 0, 0, 36, 36);
            var thumbMask = Img(row, "ThumbMask", "UI_RoundedPanel2_Image", SlateLit, 62, 6, 92, 52, 4.5f);
            thumbMask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var thumb = Fill(Rect(thumbMask.transform, "Thumb", 0, 0, 0, 0)).gameObject.AddComponent<RawImage>();
            thumb.raycastTarget = false;
            thumb.color = Clear;
            Txt(row, "Name", r.Name, true, 23, Color.white, TextAlignmentOptions.BottomLeft, 168, 6, 640, 30);
            Txt(row, "Meta", FormatDate(r.Dates[_show]) + (r.Code != null ? "   •   " + r.Code : ""), false, 17, Dim, TextAlignmentOptions.TopLeft, 168, 37, 640, 22);
            float ix = w - 266;
            if (r.Ghost) Img(row, "Ghost", "UI_IconCombinedCostume", new Color(0.9f, 0.88f, 1f, 0.6f), ix, 13, 32, 38);
            if (r.Fav) Img(row, "Favourite", "ui_privatelobby_fav_icon", Color.white, ix - 42, 17, 34, 30);
            var cap = Rect(row, "Time", w - 222, 8, 210, 48);
            FillImg(cap, "Fill", "UI_32xCircle_Image", Night);
            Img(cap, "Clock", "UI_Clock", TextYellow, 10, 9, 30, 30);
            Txt(cap, "Text", FormatTime(r.Times[_show].Value), true, 27, TextYellow, TextAlignmentOptions.MidlineRight, 42, 0, 154, 48);
            return row;
        }

        static void Refilter(bool keepSelection)
        {
            string keepId = keepSelection && _sel >= 0 && _sel < _rows.Count ? _rows[_sel].RawId : null;
            string q = _query.Trim().ToLowerInvariant();
            _rows.Clear();
            foreach (var r in _all)
            {
                if (!r.Times[_show].HasValue) continue;
                if (q.Length > 0 && !r.Haystack.Contains(q)) continue;
                if (_filter == 1 && !r.Fav) continue;
                if (_filter == 2 && !r.Ghost) continue;
                if (_filter == 3 && !r.Ugc) continue;
                if (_filter == 4 && r.Ugc) continue;
                _rows.Add(r);
            }
            int s = _show;
            if (_sort == 0) _rows.Sort((a, b) => string.CompareOrdinal(b.Dates[s] ?? "", a.Dates[s] ?? ""));
            else if (_sort == 1) _rows.Sort((a, b) => a.Times[s].Value.CompareTo(b.Times[s].Value));
            else if (_sort == 2) _rows.Sort((a, b) => b.Times[s].Value.CompareTo(a.Times[s].Value));
            else _rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            for (int i = _fills.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_fills.GetChild(i).gameObject);
            for (int i = _rowLayer.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_rowLayer.GetChild(i).gameObject);
            _built.Clear();
            _thumbTried.Clear();
            for (int i = 0; i < _rows.Count; i++) _built.Add(null);
            _content.sizeDelta = new Vector2(_content.sizeDelta.x, Mathf.Max(0f, _rows.Count * RowStep - RowGap));
            _empty.text = _rows.Count > 0 ? "" : Loc(_all.Count == 0 ? "pb_screen.empty" : "pb_screen.no_match");
            _cursor.gameObject.SetActive(_rows.Count > 0);

            int sel = 0;
            if (keepId != null)
                for (int i = 0; i < _rows.Count; i++)
                    if (_rows[i].RawId == keepId) { sel = i; break; }
            _sel = -1;
            _scroll = _scrollTarget = 0f;
            Select(sel);
            _cursorY = _cursorTarget;
            _scroll = _scrollTarget;
            RefreshChrome();
        }

        static void RefreshChrome()
        {
            int favs = 0;
            foreach (var r in _all) if (r.Fav) favs++;
            _recordsN.text = _all.Count.ToString();
            _favsN.text = favs.ToString();
            float x = 0f;
            _recordsN.rectTransform.anchoredPosition = new Vector2(x, 0);
            x += _recordsN.GetPreferredValues().x + 8;
            _recordsL.rectTransform.anchoredPosition = new Vector2(x, -3);
            x += _recordsL.GetPreferredValues().x + 28;
            _favsN.rectTransform.anchoredPosition = new Vector2(x, 0);
            x += _favsN.GetPreferredValues().x + 8;
            _favsL.rectTransform.anchoredPosition = new Vector2(x, -3);

            for (int i = 0; i < 4; i++)
            {
                int n = 0;
                foreach (var r in _all) if (r.Times[i].HasValue) n++;
                _segTx[i].color = i == _show ? Color.white : Dim;
                _segCount[i].text = n.ToString();
                _segCount[i].color = i == _show ? new Color(1f, 1f, 1f, 0.8f) : Faint;
            }
            _sortTx.text = Loc(SortIds[_sort]);
            _filterTx.text = Loc(FilterIds[_filter]);
            _filterFill.color = _filter == 0 ? Ink : Cyan;
            RedrawSearch();
        }

        static void RedrawSearch()
        {
            bool caret = _typing && Time.unscaledTime * 2f % 2f < 1f;
            _searchTx.text = _typing ? _query + (caret ? "|" : " ") : _query.Length > 0 ? _query : Loc("pb_screen.search");
            _searchTx.color = _typing || _query.Length > 0 ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            _searchOutline.color = _typing ? Color.white : Clear;
        }

        static void Select(int i)
        {
            if (_rows.Count == 0) { ShowHero(null); return; }
            i = Mathf.Clamp(i, 0, _rows.Count - 1);
            if (i == _sel) return;
            _sel = i;
            _cursorTarget = i * RowStep;
            float vh = _view.rect.height;
            if (_cursorTarget < _scrollTarget + 8) _scrollTarget = _cursorTarget - 8;
            if (_cursorTarget + RowH > _scrollTarget + vh - 8) _scrollTarget = _cursorTarget + RowH - vh + 8;
            _scrollTarget = Mathf.Clamp(_scrollTarget, 0f, Mathf.Max(0f, _content.sizeDelta.y - vh));
            ShowHero(_rows[i]);
        }

        static void ShowHero(Row r)
        {
            _splashB.texture = _splashA.texture;
            _splashB.color = _splashA.texture != null ? Color.white : Clear;
            _heroAt = Time.unscaledTime;
            if (r == null)
            {
                _name.text = _date.text = "";
                _time.text = "--:--.---";
                _splashA.texture = null;
                _codePill.gameObject.SetActive(false);
                _heart.gameObject.SetActive(false);
                _ghost.gameObject.SetActive(false);
                return;
            }
            _name.text = r.Name;
            _date.text = FormatDate(r.Dates[_show]);
            _time.text = FormatTime(r.Times[_show].Value);
            _codePill.gameObject.SetActive(r.Code != null);
            if (r.Code != null) _codeTx.text = r.Code;
            _heart.gameObject.SetActive(r.Fav);
            _ghost.gameObject.SetActive(r.Ghost);
            _favTx.text = Loc(r.Fav ? "pb_screen.unfavourite" : "pb_screen.favourite");
            for (int k = 0; k < 4; k++)
            {
                bool has = r.Times[k].HasValue;
                _showValue[k].text = has ? FormatTime(r.Times[k].Value) : "--";
                _showValue[k].color = has ? Color.white : Faint;
                _showLabel[k].color = k == _show ? TextYellow : Dim;
                _showFill[k].color = k == _show ? SlateLit : Slate;
            }
            _splashA.texture = SplashCache.LoadCached(r.RawId, r.Name);
        }

        static void SetShow(int show)
        {
            show = (show + 4) % 4;
            if (show == _show) return;
            _show = show;
            Sound("UI_MainMenu_ShowSelector_Move");
            Refilter(true);
        }

        static PunchScaleTween AddPunch(RectTransform rt)
        {
            var p = rt.gameObject.AddComponent<PunchScaleTween>();
            if (_punchTemplate == null) return p;
            p._playOnEnable = false;
            p._bounceTime = _punchTemplate._bounceTime;
            p._bounceNumber = _punchTemplate._bounceNumber;
            p._bouncePower = _punchTemplate._bouncePower;
            p._toSize = _punchTemplate._toSize;
            return p;
        }

        static void CycleSort()
        {
            _sort = (_sort + 1) % SortIds.Length;
            Refilter(true);
            _sortPunch.Play();
            Sound("UI_Gen_Move");
        }

        static void CycleFilter()
        {
            _filter = (_filter + 1) % FilterIds.Length;
            Refilter(true);
            _filterPunch.Play();
            Sound("UI_Gen_Move");
        }

        static void ToggleFavourite()
        {
            if (_sel < 0 || _sel >= _rows.Count) return;
            var r = _rows[_sel];
            PBStore.TryFeature(r.RawId, r.Name);
            Sound(AudioManager.EventMasterData?.CreativeModeItemEditConfirm);
        }

        static void AskDelete()
        {
            if (_sel < 0 || _sel >= _rows.Count || _asking) return;
            _pendingDelete = _rows[_sel];
            _asking = true;
            NavPromptCore.SetCmsString(DeleteTitleKey, Loc("pb_screen.delete_title"));
            NavPromptCore.SetCmsString(DeleteBodyKey, string.Format(Loc("pb_screen.delete_body"), _pendingDelete.Name, FormatTime(_pendingDelete.Times[_show].Value), Loc(ShowIds[_show])));
            FallGuysLib.UI.PopUp.Confirm(DeleteTitleKey, DeleteBodyKey, OnDeleteAnswered, null, null, FGClient.UI.UIModalMessage.OKButtonType.Disruptive);
        }

        static void OnDeleteAnswered(bool ok)
        {
            _asking = false;
            _quietUntilFrame = Time.frameCount + 2;
            var r = _pendingDelete;
            _pendingDelete = null;
            if (!ok || r == null || _root == null) return;
            if (!PBStore.TryDelete(r.RawId, r.Name)) return;
            FeatureQualificationTime.DeleteGhost(r.Name, r.RawId);
            Sound(AudioManager.EventMasterData?.CreativeModeItemDelete);
            Plugin.Log.LogInfo($"pb gone: {r.Name}");
        }

        static void StartSearch()
        {
            _typing = true;
            FGInputLockService.SetScreenLock(true);
            if (IsPad()) OnScreenKeyboard.OpenExternal(() => _query, SetQuery, _ => StopTyping());
            RedrawSearch();
        }

        static void SetQuery(string q)
        {
            if (q.Length > 40) q = q.Substring(0, 40);
            if (q == _query) return;
            _query = q;
            Refilter(false);
        }

        static void StopTyping()
        {
            if (!_typing) return;
            _typing = false;
            _quietUntilFrame = Time.frameCount + 2;
            FGInputLockService.SetScreenLock(false);
            OnScreenKeyboard.CloseExternal(BetterFGUIMan.Instance);
            RedrawSearch();
        }

        static void Sound(string key)
        {
            if (!string.IsNullOrEmpty(key)) AudioManager.PlayOneShot(key, Vector3.zero);
        }

        static bool IsPad() => Rewired.ReInput.isReady && Rewired.ReInput.controllers.GetLastActiveControllerType() == Rewired.ControllerType.Joystick;

        static bool Trigger(string[] names) => NavPromptCore.ElementHeldByName(names) || NavPromptCore.AxisByName(names) > 0.5f;

        static Sprite PadGlyph(string[] names)
        {
            var s = NavPromptCore.PadGlyphByName(names);
            if (s != null || !Rewired.ReInput.isReady || Rewired.ReInput.players.playerCount == 0) return s;
            var p = Rewired.ReInput.players.GetPlayer(0);
            var sticks = p.controllers.Joysticks;
            for (int i = 0; i < p.controllers.joystickCount && s == null; i++)
                s = NavPromptCore.JoystickElementGlyph(sticks[i], NavPromptCore.ResolveAxisIdByName(sticks[i], names));
            return s;
        }

        static bool Repeat(string slot, bool held)
        {
            if (!held) { _repeatAt.Remove(slot); return false; }
            float now = Time.unscaledTime;
            if (!_repeatAt.TryGetValue(slot, out var at)) { _repeatAt[slot] = now + 0.38f; return true; }
            if (now < at) return false;
            _repeatAt[slot] = now + 0.07f;
            return true;
        }

        static bool Edge(string slot, bool held)
        {
            if (!held) { _edgeHeld.Remove(slot); return false; }
            return _edgeHeld.Add(slot);
        }

        internal static void Tick()
        {
            if (_root == null) return;
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;

            var area = _screen.rect;
            float fit = Mathf.Min(area.width / StageW, area.height / StageH);
            _stage.localScale = new Vector3(fit, fit, 1f);
            _stage.anchoredPosition = new Vector2((area.width - StageW * fit) * 0.5f, (StageH * fit - area.height) * 0.5f);

            bool pad = IsPad();
            if (_padGlyphs != (pad ? 1 : 0))
            {
                _padGlyphs = pad ? 1 : 0;
                foreach (var g in _glyphs)
                {
                    var s = pad ? PadGlyph(g.Pad) : NavPromptCore.KeyboardGlyph(g.Key);
                    g.Img.sprite = s;
                    g.Img.enabled = s != null;
                }
            }

            if (_typing) TickTyping();
            else if (!_asking && !FallGuysLib.UI.PopUp.HasActive && Time.frameCount > _quietUntilFrame) TickInput();
            if (!_typing && !_asking) TickMouse();

            float vh = _view.rect.height, max = Mathf.Max(0f, _content.sizeDelta.y - vh);
            _scroll = Mathf.Lerp(_scroll, _scrollTarget, 1f - Mathf.Exp(-dt * 16f));
            _content.anchoredPosition = new Vector2(0f, _scroll);
            float trackH = PanelH - 24, total = _content.sizeDelta.y;
            float thumbH = total <= vh ? trackH : Mathf.Max(40f, trackH * vh / total);
            _scrollThumb.parent.gameObject.SetActive(total > vh);
            _scrollThumb.sizeDelta = new Vector2(6f, thumbH);
            _scrollThumb.anchoredPosition = new Vector2(0f, max <= 0f ? 0f : -(trackH - thumbH) * _scroll / max);

            _cursorY = Mathf.Lerp(_cursorY, _cursorTarget, 1f - Mathf.Exp(-dt * 22f));
            _cursor.anchoredPosition = new Vector2(0f, -_cursorY);

            float pillX = 60 + _show * (_segW + 4);
            _pill.anchoredPosition = new Vector2(Mathf.Lerp(_pill.anchoredPosition.x, pillX, 1f - Mathf.Exp(-dt * 20f)), _pill.anchoredPosition.y);

            float fade = Mathf.Clamp01((now - _heroAt) / 0.2f);
            _splashA.color = _splashA.texture != null ? new Color(1f, 1f, 1f, fade) : Clear;
            if (_splashB.texture != null) _splashB.color = new Color(1f, 1f, 1f, 1f - fade);

            int first = Mathf.Max(0, Mathf.FloorToInt(_scroll / RowStep));
            int last = Mathf.Min(_built.Count - 1, Mathf.CeilToInt((_scroll + vh) / RowStep));
            for (int i = first; i <= last; i++)
            {
                var row = _built[i] ??= BuildRow(i, _rows[i]);
                if (_thumbTried.Add(i))
                {
                    var tex = SplashCache.LoadCached(_rows[i].RawId, _rows[i].Name);
                    if (tex != null)
                    {
                        var thumb = row.Find("ThumbMask/Thumb").GetComponent<RawImage>();
                        thumb.texture = tex;
                        thumb.color = Color.white;
                    }
                }
                bool sel = i == _sel;
                var rank = row.Find("Rank").GetComponent<Image>();
                rank.color = sel ? Color.white : new Color(1f, 1f, 1f, 0.08f);
                rank.transform.Find("N").GetComponent<TextMeshProUGUI>().color = sel ? Pink : Color.white;
                row.Find("Meta").GetComponent<TextMeshProUGUI>().color = sel ? new Color(1f, 1f, 1f, 0.9f) : Dim;
            }
        }

        static void TickTyping()
        {
            string q = _query;
            bool done = false;
            foreach (char ch in Input.inputString)
            {
                if (ch == '\b') { if (q.Length > 0) q = q.Substring(0, q.Length - 1); }
                else if (ch == '\n' || ch == '\r') done = true;
                else if (!char.IsControl(ch) && q.Length < 40) q += ch;
            }
            if (Input.GetKeyDown(KeyCode.Escape)) done = true;
            if (q != _query)
            {
                _query = q;
                Refilter(false);
            }
            if (done) StopTyping();
            else RedrawSearch();
        }

        static void TickInput()
        {
            NavPromptCore.PadDirection(out int dx, out int dy);
            if (Repeat("up", KeybindService.KeyHeld(KeyCode.UpArrow) || KeybindService.KeyHeld(KeyCode.W) || dy > 0)) Move(-1);
            if (Repeat("down", KeybindService.KeyHeld(KeyCode.DownArrow) || KeybindService.KeyHeld(KeyCode.S) || dy < 0)) Move(1);
            if (Repeat("pgup", KeybindService.KeyHeld(KeyCode.PageUp))) Move(-9);
            if (Repeat("pgdn", KeybindService.KeyHeld(KeyCode.PageDown))) Move(9);
            if (KeybindService.KeyDown(KeyCode.Home)) Move(-_rows.Count);
            if (KeybindService.KeyDown(KeyCode.End)) Move(_rows.Count);
            if (Edge("left", KeybindService.KeyHeld(KeyCode.A) || KeybindService.KeyHeld(KeyCode.LeftArrow) || dx < 0)) SetShow(_show - 1);
            if (Edge("right", KeybindService.KeyHeld(KeyCode.D) || KeybindService.KeyHeld(KeyCode.RightArrow) || dx > 0)) SetShow(_show + 1);
            if (KeybindService.KeyDown(KeyCode.R) | Edge("rt", Trigger(PadRT))) CycleSort();
            if (KeybindService.KeyDown(KeyCode.T) | Edge("lt", Trigger(PadLT))) CycleFilter();
            if (KeybindService.KeyDown(KeyCode.F) || NavPromptCore.ElementDownByName(PadY)) ToggleFavourite();
            if (KeybindService.KeyDown(KeyCode.Delete) || NavPromptCore.ElementDownByName(PadX)) AskDelete();
            if (KeybindService.KeyDown(KeyCode.Return) || NavPromptCore.ElementDownByName(PadL3)) StartSearch();
        }

        static void Move(int by)
        {
            int before = _sel;
            Select(_sel + by);
            if (_sel != before) Sound("UI_MainMenu_ShowSelector_Move");
        }

        static void TickMouse()
        {
            Vector3 mp = Input.mousePosition;
            bool overList = RectTransformUtility.ScreenPointToLocalPointInRectangle(_view, mp, null, out var lp) && _view.rect.Contains(lp);
            float wheel = Input.mouseScrollDelta.y;
            if (overList && wheel != 0f)
                _scrollTarget = Mathf.Clamp(_scrollTarget - wheel * RowStep * 3f, 0f, Mathf.Max(0f, _content.sizeDelta.y - _view.rect.height));
            if (!Input.GetMouseButtonDown(0)) return;
            if (overList && _rows.Count > 0)
            {
                float y = _view.rect.yMax - lp.y + _scroll;
                int idx = Mathf.FloorToInt(y / RowStep);
                if (idx >= 0 && idx < _rows.Count && y - idx * RowStep <= RowH)
                {
                    float keep = _scrollTarget;
                    Select(idx);
                    _scrollTarget = keep;
                }
                return;
            }
            foreach (var (rt, act) in _hits)
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, mp, null, out var hp) && rt.rect.Contains(hp)) { act(); return; }
        }
    }

    public class PBScreenDriver : MonoBehaviour
    {
        public PBScreenDriver(IntPtr ptr) : base(ptr) { }
        void Update() => PBScreen.Tick();
        void OnDestroy() => PBScreen.Closed();
    }
}
