using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

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
        private static readonly string[] SORT_LABELS = { "Name A-Z", "Name Z-A", "Newest", "Author", "Type" };

        private const float FILTER_W = 156f;
        private const float GRID_GAP = 14f;
        private const float TILE_MIN = 168f;
        private const int THUMB_W = 256;
        private const int THUMB_H = 204;

        private List<Card> _cards;
        private readonly List<int> _view = new List<int>();
        private int[] _repoCounts = new int[SCAN_DIRS.Length];
        private int _menuCard = -1;
        private int _filter = -1;
        private int _sort;
        private string _search = "";
        private Vector2 _gridScroll;

        private void DrawBrowseStep()
        {
            if (_cards == null) RefreshCards();

            if (_menuCard >= 0 && _menuCard < _cards.Count) { DrawCardMenu(); return; }

            Rect all = GUILayoutUtility.GetRect(10f, 10f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            var newRect = new Rect(all.x, all.y, 110f, 22f);
            var searchRect = new Rect(newRect.xMax + 8f, all.y, Mathf.Max(all.width - 276f, 80f), 22f);
            var sortRect = new Rect(searchRect.xMax + 8f, all.y, 150f, 22f);

            GUI.backgroundColor = new Color(0.35f, 0.62f, 0.38f);
            if (GUI.Button(newRect, "Make New", _button))
            {
                GUI.backgroundColor = Color.white;
                ResetForNew();
                GoTo(Step.Kind);
                return;
            }
            GUI.backgroundColor = Color.white;

            EditorGUI.BeginChangeCheck();
            _search = EditorGUI.TextField(searchRect, _search, _field);
            _sort = EditorGUI.Popup(sortRect, _sort, SORT_LABELS);
            if (EditorGUI.EndChangeCheck()) BuildView();

            if (string.IsNullOrEmpty(_search) && Event.current.type == EventType.Repaint)
                GUI.Label(new Rect(searchRect.x + 7f, searchRect.y, searchRect.width, searchRect.height), "Search", _label);

            float top = all.y + 34f;
            float h = all.height - 34f;
            DrawFilters(new Rect(all.x, top, FILTER_W, h));
            DrawGrid(new Rect(all.x + FILTER_W + 16f, top, all.width - FILTER_W - 16f, h));
        }

        private void DrawFilters(Rect r)
        {
            float y = r.y;
            for (int i = 0; i < FILTER_CATS.Length; i++)
            {
                var row = new Rect(r.x, y, r.width, 30f);
                y += 34f;

                bool on = _filter == i;
                int count = 0;
                foreach (var c in _cards)
                    if (c.category == FILTER_CATS[i]) count++;

                EditorGUI.DrawRect(row, on
                    ? new Color(0.28f, 0.62f, 0.95f, 0.3f)
                    : row.Contains(Event.current.mousePosition) ? new Color(1f, 1f, 1f, 0.08f) : new Color(1f, 1f, 1f, 0.03f));
                if (on) EditorGUI.DrawRect(new Rect(row.x, row.y, 3f, row.height), new Color(0.28f, 0.62f, 0.95f));

                GUI.Label(new Rect(row.x + 12f, row.y, row.width - 48f, row.height), FILTER_CATS[i], on ? _value : _label);
                GUI.Label(new Rect(row.xMax - 42f, row.y, 32f, row.height), count.ToString(), _num);

                EditorGUIUtility.AddCursorRect(row, MouseCursor.Link);
                if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
                {
                    _filter = on ? -1 : i;
                    BuildView();
                    Event.current.Use();
                }
            }

            GUI.Label(new Rect(r.x, y + 8f, r.width, 18f), _view.Count + " shown", _label);
        }

        private void DrawGrid(Rect r)
        {
            if (_view.Count == 0)
            {
                GUI.Label(r, _cards.Count == 0 ? "Nothing packed in this repo yet" : "Nothing matches that", _small);
                return;
            }

            float inner = r.width - 18f;
            int cols = Mathf.Clamp(Mathf.FloorToInt((inner + GRID_GAP) / (TILE_MIN + GRID_GAP)), 1, 6);
            float tileW = (inner - (cols - 1) * GRID_GAP) / cols;
            float coverH = tileW * COVER_H / COVER_W;
            float tileH = coverH + 42f;
            int rows = Mathf.CeilToInt(_view.Count / (float)cols);

            _gridScroll = GUI.BeginScrollView(r, _gridScroll, new Rect(0f, 0f, inner, rows * (tileH + GRID_GAP) - GRID_GAP));

            for (int i = 0; i < _view.Count; i++)
            {
                var tile = new Rect(
                    (i % cols) * (tileW + GRID_GAP),
                    (i / cols) * (tileH + GRID_GAP),
                    tileW, tileH);
                DrawCard(_view[i], tile, coverH, r.height);
            }

            GUI.EndScrollView();
        }

        private void DrawCard(int i, Rect r, float coverH, float viewH)
        {
            var card = _cards[i];
            var coverRect = new Rect(r.x, r.y, r.width, coverH);

            bool near = r.yMax > _gridScroll.y - 300f && r.yMin < _gridScroll.y + viewH + 300f;
            if (card.cover == null && near && !string.IsNullOrEmpty(card.coverPath))
            {
                card.cover = LoadThumb(card.coverPath);
                if (card.cover == null) card.coverPath = "";
                _cards[i] = card;
            }

            EditorGUI.DrawRect(coverRect, new Color(0.1f, 0.1f, 0.11f));
            if (card.cover != null) GUI.DrawTexture(coverRect, card.cover, ScaleMode.ScaleAndCrop);
            else GUI.Label(coverRect, "no cover", _small);

            if (card.notPublic)
            {
                var pill = new Rect(coverRect.x + 6f, coverRect.y + 6f, 78f, 17f);
                EditorGUI.DrawRect(pill, new Color(0.72f, 0.16f, 0.16f, 0.94f));
                GUI.Label(pill, "NOT PUBLIC", _tag);
            }

            bool hover = r.Contains(Event.current.mousePosition);
            if (hover)
                EditorGUI.DrawRect(new Rect(coverRect.x, coverRect.yMax - 3f, coverRect.width, 3f), new Color(0.28f, 0.62f, 0.95f));

            GUI.Label(new Rect(r.x, coverRect.yMax + 5f, r.width, 18f), card.name, _value);
            GUI.Label(new Rect(r.x, coverRect.yMax + 23f, r.width, 16f),
                string.IsNullOrEmpty(card.author) ? card.type : card.author + "  ·  " + card.type, _label);

            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);

            if (Event.current.type == EventType.MouseDown && hover)
            {
                Event.current.Use();
                Fade(() => _menuCard = i);
            }
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

        private void DrawCardMenu()
        {
            var card = _cards[_menuCard];
            bool isEmote = card.type == "emote";

            GUILayout.Space(14);
            GUILayout.Label("What do you want to do with this?", _big);
            GUILayout.Space(16);

            float ch = 172f;
            float cw = ch * COVER_W / COVER_H;
            Rect row = GUILayoutUtility.GetRect(cw, ch, GUILayout.ExpandWidth(true));
            var box = new Rect(row.x + (row.width - cw) * 0.5f, row.y, cw, ch);
            EditorGUI.DrawRect(box, new Color(0.1f, 0.1f, 0.11f));
            if (card.cover != null) GUI.DrawTexture(box, card.cover, ScaleMode.ScaleAndCrop);
            else GUI.Label(box, "no cover", _small);

            if (card.notPublic)
            {
                var pill = new Rect(box.x + 6f, box.y + 6f, 78f, 17f);
                EditorGUI.DrawRect(pill, new Color(0.72f, 0.16f, 0.16f, 0.94f));
                GUI.Label(pill, "NOT PUBLIC", _tag);
            }

            GUILayout.Space(12);
            GUILayout.Label(card.name, _big);
            GUILayout.Space(4);
            GUILayout.Label(string.IsNullOrEmpty(card.author) ? card.type : card.author + "  ·  " + card.type, _small);
            GUILayout.Space(24);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(isEmote ? "Emote Packager" : "Edit", _bigButton, GUILayout.Width(180f), GUILayout.Height(40f)))
            {
                string dir = card.dir;
                string type = card.type;

                if (isEmote) EmotePackagerWindow.Open();
                else if (type == "round") { if (LoadRound(dir)) GoTo(Step.Source); }
                else if (LoadSkin(dir)) GoTo(Step.Source);

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                return;
            }

            GUILayout.Space(14);

            GUI.backgroundColor = new Color(0.78f, 0.24f, 0.24f);
            if (GUILayout.Button("Delete", _bigButton, GUILayout.Width(150f), GUILayout.Height(40f)))
            {
                GUI.backgroundColor = Color.white;
                DeleteCard(card);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                return;
            }
            GUI.backgroundColor = Color.white;

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DeleteCard(Card card)
        {
            if (!EditorUtility.DisplayDialog("Delete",
                "Delete \"" + card.name + "\" from the repo?\n\n" + card.dir +
                "\n\nThe folder and everything in it goes from disk. This can't be undone.",
                "Delete", "Cancel"))
                return;

            try
            {
                Directory.Delete(card.dir, true);
            }
            catch (Exception ex)
            {
                Err("couldn't delete " + card.name + ": " + ex.Message);
                return;
            }

            CatalogBat.Run(_repoRoot);
            WriteNewCatalog(_repoRoot);
            RefreshGroups();
            Fade(() => { _menuCard = -1; RefreshCards(); });
            Ok("deleted " + card.name);
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
            _view.Clear();
            _gridScroll = Vector2.zero;
            if (_cards == null) return;

            string q = (_search ?? "").Trim();

            for (int i = 0; i < _cards.Count; i++)
            {
                var c = _cards[i];
                if (_filter >= 0 && c.category != FILTER_CATS[_filter]) continue;
                if (q.Length > 0 && !(Has(c.name, q) || Has(c.author, q) || Has(c.group, q) || Has(c.type, q))) continue;
                _view.Add(i);
            }

            _view.Sort((a, b) =>
            {
                Card x = _cards[a], y = _cards[b];
                switch (_sort)
                {
                    case 1: return string.Compare(y.name, x.name, StringComparison.OrdinalIgnoreCase);
                    case 2: return y.stamp.CompareTo(x.stamp);
                    case 3: return Tie(string.Compare(x.author, y.author, StringComparison.OrdinalIgnoreCase), x.name, y.name);
                    case 4: return Tie(string.Compare(x.type, y.type, StringComparison.OrdinalIgnoreCase), x.name, y.name);
                    default: return string.Compare(x.name, y.name, StringComparison.OrdinalIgnoreCase);
                }
            });
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
            _view.Clear();
        }
    }
}
