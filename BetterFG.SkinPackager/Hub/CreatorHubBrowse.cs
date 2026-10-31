using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.Editor
{
    public partial class CreatorHubWindow
    {
        private struct Card
        {
            public string dir;
            public string category;
            public string name;
            public string author;
            public string type;
            public string group;
            public string coverPath;
            public Texture2D cover;
            public long stamp;
            public bool notPublic;
        }

        private static readonly string[] SCAN_DIRS = { "Costumes", "Accessories", "Items", "Plinths", "Emotes", "Rounds" };
        private static readonly string[] FILTER_CATS = { "Costumes", "Accessories", "Items", "Emotes", "Rounds" };
        private static readonly string[] SORT_LABELS = { "creator.sort_name", "creator.sort_date", "creator.sort_author", "creator.sort_type" };
        private static readonly string[] FILTER_LABELS = { "creator.filter_public", "creator.filter_not_public", "creator.filter_has_cover", "creator.filter_no_cover" };

        private const float FILTER_W = 156f;
        private const float GRID_GAP = 14f;
        private const float TILE_MIN = 168f;
        private const int THUMB_W = 256;
        private const int THUMB_H = 204;

        private List<Card> _cards;
        private readonly List<int> _shown = new List<int>();
        private int[] _repoCounts = new int[SCAN_DIRS.Length];
        private int _menuCard = -1;
        private int _filter = -1;
        private int _sort;
        private bool _sortDesc;
        private readonly bool[] _flags = new bool[4];
        private Button _filterBtn;
        private string _search = "";
        private ScrollRect _grid;
        private readonly List<(int card, RawImage img, float top, float bottom, float aspect)> _thumbs = new List<(int, RawImage, float, float, float)>();

        private void BuildBrowseStep(RectTransform p, float w, float h)
        {
            if (_cards == null) RefreshCards();

            if (_menuCard >= 0 && _menuCard < _cards.Count) { BuildCardMenu(p, w); return; }

            Btn(p, new Rect(0f, 0f, 130f, ROW), "creator.make_new", BTN_GREEN, () =>
            {
                ResetForNew();
                GoTo(Step.Kind);
            });
            const float ddW = 116f, dirW = ROW, listW = 170f, rowH = 30f;
            float ddX = w - dirW - ddW * 2f - 16f;
            var search = Field(p, new Rect(140f, 0f, Mathf.Max(ddX - 150f, 80f), ROW), _search, "creator.search_hint", null, null);
            search.onValueChanged.AddListener(v => { _search = v; BuildView(); BuildGrid(); });

            _filterBtn = UGUIShip.CreateMultiSelectDropdown(p, new Rect(ddX, 0f, ddW, ROW), "ui.filters",
                new List<string>(FILTER_LABELS), new List<bool>(_flags), (i, on) =>
                {
                    _flags[i] = on;
                    PaintFilterBtn();
                    BuildView();
                    BuildGrid();
                }, FS, listW, rowH);
            UGUIShip.SetButtonColor(_filterBtn, BTN_DARK);
            CheckIcons(_filterBtn);
            UGUIShip.AddHeaderIcon(_filterBtn, "BetterFG.assets.ui.button.filter.png");
            PaintFilterBtn();

            var sorts = new List<bool>();
            for (int i = 0; i < SORT_LABELS.Length; i++) sorts.Add(i == _sort);
            var sortBtn = UGUIShip.CreateMultiSelectDropdown(p, new Rect(ddX + ddW + 8f, 0f, ddW, ROW), "ui.sort",
                new List<string>(SORT_LABELS), sorts, (i, on) =>
                {
                    _sort = i;
                    BuildView();
                    BuildGrid();
                }, FS, listW, rowH, true, true);
            UGUIShip.SetButtonColor(sortBtn, BTN_DARK);
            CheckIcons(sortBtn);
            UGUIShip.AddHeaderIcon(sortBtn, "BetterFG.assets.ui.button.sort.png");

            Button dirBtn = null;
            dirBtn = Btn(p, new Rect(w - dirW, 0f, dirW, ROW), _sortDesc ? "↓" : "↑", BTN_DARK, () =>
            {
                _sortDesc = !_sortDesc;
                dirBtn.GetComponentInChildren<Text>().text = _sortDesc ? "↓" : "↑";
                BuildView();
                BuildGrid();
            }, FS + 2);

            float top = ROW + 14f;
            BuildFilters(Area(p, new Rect(0f, top, FILTER_W, h - top), "Filters"));
            _gridArea = Area(p, new Rect(FILTER_W + 16f, top, w - FILTER_W - 16f, h - top), "Grid");
            BuildGrid();
        }

        private RectTransform _gridArea;
        private Text _shownLbl;

        private void BuildFilters(RectTransform r)
        {
            float y = 0f;
            for (int i = 0; i < FILTER_CATS.Length; i++)
            {
                int idx = i;
                bool on = _filter == i;
                int count = 0;
                foreach (var c in _cards)
                    if (c.category == FILTER_CATS[i]) count++;

                var row = UGUIShip.CreateListRow(r, new Rect(0f, y, FILTER_W, 30f), CategoryName(FILTER_CATS[i]), i, FS, () =>
                {
                    _filter = _filter == idx ? -1 : idx;
                    BuildView();
                    RequestRebuild();
                });
                UGUIShip.PaintListRow(row, i, on);
                UGUIShip.AddLeatherSpot(row.transform.Find("RowShine").GetComponent<Image>(), (RectTransform)row.transform, 140f, 110f, 2.5f);
                Lbl(row.transform, new Rect(FILTER_W - 48f, 0f, 40f, 30f), count.ToString(), FS_SM, HINT, TextAnchor.MiddleRight);
                y += 34f;
            }
            _shownLbl = Lbl(r, new Rect(4f, y + 8f, FILTER_W, 20f), ShipLoc.Format("creator.shown_fmt", _shown.Count), FS_SM, HINT);
        }

        private void BuildGrid()
        {
            for (int i = _gridArea.childCount - 1; i >= 0; i--) DestroyImmediate(_gridArea.GetChild(i).gameObject);
            _thumbs.Clear();
            _grid = null;
            if (_shownLbl != null) _shownLbl.text = ShipLoc.Format("creator.shown_fmt", _shown.Count);

            var r = _gridArea.rect;
            if (_shown.Count == 0)
            {
                Lbl(_gridArea, new Rect(0f, 0f, r.width, r.height), _cards.Count == 0 ? "creator.library_empty" : "creator.library_no_match", FS, HINT, TextAnchor.MiddleCenter);
                return;
            }

            var (sr, content) = UGUIShip.CreateScrollView(_gridArea, new Rect(0f, 0f, r.width, r.height));
            _grid = sr;
            float inner = r.width - UGUIShip.SCROLLBAR_INSET * 2f;
            int cols = Mathf.Clamp(Mathf.FloorToInt((inner + GRID_GAP) / (TILE_MIN + GRID_GAP)), 1, 6);
            float tileW = (inner - (cols - 1) * GRID_GAP) / cols;
            float coverH = tileW * COVER_H / COVER_W;
            float tileH = coverH + 46f;
            int rows = Mathf.CeilToInt(_shown.Count / (float)cols);
            content.sizeDelta = new Vector2(0f, rows * (tileH + GRID_GAP) - GRID_GAP);
            content.anchoredPosition = Vector2.zero;
            sr.verticalScrollbar.value = 1f;

            for (int i = 0; i < _shown.Count; i++)
            {
                var tile = new Rect((i % cols) * (tileW + GRID_GAP), (i / cols) * (tileH + GRID_GAP), tileW, tileH);
                BuildCard(content, _shown[i], tile, coverH);
            }
        }

        private void BuildCard(RectTransform content, int i, Rect r, float coverH)
        {
            var card = _cards[i];
            var tile = Area(content, r, "Card");
            var hit = tile.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            var btn = tile.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            btn.onClick.AddListener(() => Fade(() => _menuCard = i));
            UGUIShip.PaintHoverFill(tile.gameObject, new Color(1f, 1f, 1f, 0.07f));
            UGUIShip.AddLeatherSpot(tile.Find("RowFill").GetComponent<Image>(), tile, 140f, 110f, 2.5f);
            UGUIShip.ForwardScrollToParent(tile.gameObject);

            var raw = Framed(tile, new Rect(0f, 0f, r.width, coverH), card.cover, card.cover == null && string.IsNullOrEmpty(card.coverPath) ? "creator.no_cover" : null);
            if (card.cover == null && !string.IsNullOrEmpty(card.coverPath)) _thumbs.Add((i, raw, r.y, r.y + r.height, r.width / coverH));

            if (card.notPublic) Pill(tile, new Rect(8f, 8f, 84f, 18f), "creator.not_public", new Color(0.72f, 0.16f, 0.16f, 1f));

            Lbl(tile, new Rect(4f, coverH + 4f, r.width - 8f, 20f), card.name, FS, WHITE);
            Lbl(tile, new Rect(4f, coverH + 24f, r.width - 8f, 18f), CardSub(card), FS_SM, HINT);
        }

        private static string CardSub(Card card)
        {
            string type = ShipLoc.Get("creator.kind_" + card.type);
            return string.IsNullOrEmpty(card.author) ? type : card.author + "  ·  " + type;
        }

        private bool LoadVisibleThumbs()
        {
            if (_grid == null || _thumbs.Count == 0) return false;
            float viewTop = _grid.content.anchoredPosition.y;
            float viewBottom = viewTop + _grid.viewport.rect.height;
            int budget = 3;
            for (int k = _thumbs.Count - 1; k >= 0 && budget > 0; k--)
            {
                var t = _thumbs[k];
                if (t.bottom < viewTop - 300f || t.top > viewBottom + 300f) continue;
                _thumbs.RemoveAt(k);
                budget--;
                var card = _cards[t.card];
                if (card.cover == null && !string.IsNullOrEmpty(card.coverPath))
                {
                    card.cover = LoadThumb(card.coverPath);
                    if (card.cover == null) card.coverPath = "";
                    _cards[t.card] = card;
                }
                if (t.img != null) SetCover(t.img, card.cover, t.aspect);
            }
            return budget < 3;
        }

        private static Texture2D LoadThumb(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!src.LoadImage(File.ReadAllBytes(path))) { DestroyImmediate(src); return null; }

                var rt = RenderTexture.GetTemporary(THUMB_W, THUMB_H, 0, RenderTextureFormat.ARGB32);
                rt.filterMode = FilterMode.Bilinear;
                Graphics.Blit(src, rt);
                DestroyImmediate(src);

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var thumb = new Texture2D(THUMB_W, THUMB_H, TextureFormat.RGB24, false);
                thumb.ReadPixels(new Rect(0f, 0f, THUMB_W, THUMB_H), 0, 0);
                thumb.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                return thumb;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("cover wouldn't load (" + path + "): " + ex.Message);
                return null;
            }
        }

        private void BuildCardMenu(RectTransform p, float w)
        {
            var card = _cards[_menuCard];
            bool isEmote = card.type == "emote";

            Big(p, 8f, w, "creator.card_question");

            float ch = 200f;
            float cw = ch * COVER_W / COVER_H;
            var box = new Rect((w - cw) * 0.5f, 52f, cw, ch);
            Framed(p, box, card.cover, "creator.no_cover");
            if (card.notPublic) Pill(p, new Rect(box.x + 8f, box.y + 8f, 84f, 18f), "creator.not_public", new Color(0.72f, 0.16f, 0.16f, 1f));

            float y = box.yMax + 14f;
            Big(p, y, w, card.name);
            Small(p, y + 32f, w, CardSub(card));
            y += 76f;

            float bx = (w - 180f - 14f - 150f) * 0.5f;
            Btn(p, new Rect(bx, y, 180f, 40f), isEmote ? "creator.emote_packager" : "ui.edit", BTN_BLUE, () =>
            {
                if (isEmote) EmotePackagerWindow.Open();
                else if (card.type == "round") { if (LoadRound(card.dir)) GoTo(Step.Source); }
                else if (LoadSkin(card.dir)) GoTo(Step.Source);
            }, FS + 1);
            Btn(p, new Rect(bx + 194f, y, 150f, 40f), "ui.delete", BTN_RED, () => DeleteCard(card), FS + 1);
        }

        private void DeleteCard(Card card)
        {
            if (!EditorUtility.DisplayDialog(ShipLoc.Get("creator.delete_title"),
                ShipLoc.Format("creator.delete_body_fmt", card.name, card.dir),
                ShipLoc.Get("creator.delete_title"), ShipLoc.Get("ui.cancel")))
                return;

            try
            {
                Directory.Delete(card.dir, true);
            }
            catch (Exception ex)
            {
                Err(ShipLoc.Format("creator.err_delete_fmt", card.name, ex.Message));
                return;
            }

            CatalogBat.Run(_repoRoot);
            WriteNewCatalog(_repoRoot);
            RefreshGroups();
            Fade(() => { _menuCard = -1; RefreshCards(); });
            Ok(ShipLoc.Format("creator.deleted_fmt", card.name));
            Debug.Log("removed folder " + card.dir + " and regenerated the catalog");
        }

        private void RefreshCards()
        {
            ClearCards();
            _cards = new List<Card>();
            if (string.IsNullOrWhiteSpace(_repoRoot) || !Directory.Exists(_repoRoot)) { BuildView(); return; }

            var tracked = TrackedDirs();
            string repoFull = Path.GetFullPath(_repoRoot).TrimEnd('\\', '/');

            foreach (string category in SCAN_DIRS)
            {
                string root = Path.Combine(_repoRoot, category);
                if (!Directory.Exists(root)) continue;

                foreach (string dir in Directory.GetDirectories(root))
                {
                    string info = Path.Combine(dir, "info.json");
                    if (!File.Exists(info)) continue;

                    string json = File.ReadAllText(info);

                    string name = SkinInfoJson.ReadStr(json, "name");
                    if (string.IsNullOrWhiteSpace(name)) name = SkinInfoJson.ReadStr(json, "displayName");
                    if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileName(dir);

                    string type = SkinInfoJson.ReadStr(json, "type");
                    if (string.IsNullOrWhiteSpace(type)) type = CategoryType(category);

                    _cards.Add(new Card
                    {
                        dir = dir,
                        category = category,
                        name = name,
                        author = SkinInfoJson.ReadStr(json, "author"),
                        type = type,
                        group = SkinInfoJson.ReadStr(json, "group"),
                        coverPath = FindCover(dir),
                        stamp = Directory.GetLastWriteTimeUtc(dir).Ticks,
                        notPublic = tracked != null && !tracked.Contains(RepoRelative(repoFull, dir)),
                    });
                }
            }

            BuildView();
        }

        private void BuildView()
        {
            _shown.Clear();
            if (_cards == null) return;

            string q = (_search ?? "").Trim();

            for (int i = 0; i < _cards.Count; i++)
            {
                var c = _cards[i];
                if (_filter >= 0 && c.category != FILTER_CATS[_filter]) continue;
                if (q.Length > 0 && !(Has(c.name, q) || Has(c.author, q) || Has(c.group, q) || Has(c.type, q))) continue;
                bool cover = c.cover != null || !string.IsNullOrEmpty(c.coverPath);
                if (_flags[0] && c.notPublic) continue;
                if (_flags[1] && !c.notPublic) continue;
                if (_flags[2] && !cover) continue;
                if (_flags[3] && cover) continue;
                _shown.Add(i);
            }

            _shown.Sort((a, b) =>
            {
                Card x = _cards[a], y = _cards[b];
                int r;
                switch (_sort)
                {
                    case 1: r = y.stamp.CompareTo(x.stamp); break;
                    case 2: r = Tie(string.Compare(x.author, y.author, StringComparison.OrdinalIgnoreCase), x.name, y.name); break;
                    case 3: r = Tie(string.Compare(x.type, y.type, StringComparison.OrdinalIgnoreCase), x.name, y.name); break;
                    default: r = string.Compare(x.name, y.name, StringComparison.OrdinalIgnoreCase); break;
                }
                return _sortDesc ? -r : r;
            });
        }

        private void PaintFilterBtn()
        {
            bool any = false;
            foreach (var f in _flags) any |= f;
            _filterBtn.GetComponentInChildren<Text>().color = any ? new Color(1f, 0.85f, 0.2f) : Color.white;
        }

        private static string RepoRelative(string repoFull, string dir)
        {
            string full = Path.GetFullPath(dir);
            return full.Length <= repoFull.Length ? "" : full.Substring(repoFull.Length + 1).Replace('\\', '/');
        }

        private HashSet<string> TrackedDirs()
        {
            if (!Directory.Exists(Path.Combine(_repoRoot, ".git"))) return null;

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "ls-files -z",
                    WorkingDirectory = _repoRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };

                string output;
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    output = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(8000)) return null;
                    if (p.ExitCode != 0) return null;
                }

                var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string entry in output.Split(' '))
                {
                    int cut = entry.LastIndexOf('/');
                    while (cut > 0)
                    {
                        string d = entry.Substring(0, cut);
                        if (!dirs.Add(d)) break;
                        cut = d.LastIndexOf('/');
                    }
                }
                return dirs;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("couldn't ask git what's tracked, skipping the public/private tags: " + ex.Message);
                return null;
            }
        }

        private static int Tie(int first, string a, string b) => first != 0 ? first : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);

        private static bool Has(string s, string q) => !string.IsNullOrEmpty(s) && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string CategoryType(string category)
        {
            switch (category)
            {
                case "Accessories": return "accessory";
                case "Items": return "item";
                case "Plinths": return "plinth";
                case "Emotes": return "emote";
                case "Rounds": return "round";
                default: return "costume";
            }
        }

        private static string FindCover(string dir)
        {
            foreach (string f in Directory.GetFiles(dir, "cover.*"))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg") return f;
            }
            return "";
        }

        private void ClearCards()
        {
            if (_cards == null) return;
            foreach (var c in _cards)
                if (c.cover != null) DestroyImmediate(c.cover);
            _cards = null;
            _shown.Clear();
        }
    }
}
