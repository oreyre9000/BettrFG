using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using EditorMessageType = UnityEditor.MessageType;

namespace BetterFG.Editor
{
    public partial class CreatorHubWindow : EditorWindow
    {
        private static CreatorHubWindow current;

        [MenuItem("BettrFG/Creator Hub")]
        public static void Open()
        {
            var w = GetWindow<CreatorHubWindow>("Creator Hub");
            w.minSize = new Vector2(760f, 560f);
            w.position = new Rect(w.position.x, w.position.y, 1080f, 720f);
        }

        private const string PREF_LAST_COVER_DIR = "BetterFG.SkinPackager.LastCoverDir";
        private const string PREF_LAST_CATALOG_DIR = "BetterFG.SkinPackager.LastCatalogDir";

        private const float PAD_X = 44f;
        private const float PAD_Y = 18f;
        private const float ROW = 22f;

        private enum Step { Repo, Browse, Kind, Source, Details, Cover, Options, Build }
        private static readonly string[] STEP_TITLES = { "Repository", "Library", "Type", "Source", "Details", "Cover Image", "Settings", "Build" };

        private enum PackKind { Costume, Accessory, Item, Plinth, Round }
        private static readonly string[] KIND_LABELS = { "Costume", "Accessory", "Item", "Plinth", "Round" };

        private Step _step = Step.Repo;
        private PackKind _kind = PackKind.Costume;

        private GameObject _sourceObject;
        private string _loadedBundleFile = "";
        private string _loadedDir = "";
        private bool _keepBundle;
        private GameObject _bundlePreviewObject;

        private string BundleName => _kind == PackKind.Round ? RoundBundleName
            : _keepBundle && !string.IsNullOrEmpty(_loadedBundleFile)
            ? _loadedBundleFile
            : _sourceObject == null ? "" : SanitizeBundleName(_sourceObject.name);

        private string _name = "";
        private string _author = "";
        private string _description = "";
        private string _group = "";
        private List<string> _knownGroups = new List<string>();
        private string _newGroup = "";
        private bool _addingGroup;
        private bool _groupFieldFocused;

        private bool _keepBase;
        private float _skinScale = 1f;

        private float _itemScale = 1f;
        private Transform _leftTransform;
        private Transform _rightTransform;

        private string _coverPath = "";
        private Texture2D _coverPreview;
        private const int COVER_W = 956;
        private const int COVER_H = 763;

        private string _outputDir = "";
        private string _repoRoot = "";
        private string _repoCoverPath = "";
        private Texture2D _repoCoverPreview;
        private const int REPO_COVER_W = 532;
        private const int REPO_COVER_H = 38;
        private string _repoOwner = "";
        private string _repoName = "";
        private int _repoSkinCount;
        private string _statusMsg = "";
        private EditorMessageType _statusType = EditorMessageType.None;

        private readonly SkinPreview _preview = new SkinPreview();
        private Vector2 _optionsScroll;
        private Vector2 _bodyScroll;

        private const float FADE_HALF = 0.5f;
        private bool _fading;
        private double _fadeT0;
        private Action _pending;

        private GUIStyle _title, _label, _value, _h2, _wrap, _big, _small, _field, _area, _button, _bigButton, _toolbar, _num, _tag;
        private bool _stylesReady;

        private void OnEnable()
        {
            current = this;
            wantsMouseMove = true;
            if (string.IsNullOrEmpty(_repoRoot))
                _repoRoot = EditorPrefs.GetString(PREF_LAST_CATALOG_DIR, "");
            RefreshGroups();
        }

        private void OnDisable()
        {
            if (current == this) current = null;
            _preview.Cleanup();
            ClearCards();
            if (_coverPreview != null) { DestroyImmediate(_coverPreview); _coverPreview = null; }
            if (_repoCoverPreview != null) { DestroyImmediate(_repoCoverPreview); _repoCoverPreview = null; }
            EditorApplication.update -= FadeTick;
        }

        private void OnSelectionChange()
        {
            if (_step == Step.Source && !_fading && Selection.activeGameObject != null)
                _sourceObject = Selection.activeGameObject;
            Repaint();
        }

        private static CreatorHubWindow GetOpenWindow()
        {
            if (current != null) return current;
            current = HasOpenInstances<CreatorHubWindow>() ? GetWindow<CreatorHubWindow>() : null;
            return current;
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            bool pro = EditorGUIUtility.isProSkin;
            Color dim = pro ? new Color(0.60f, 0.62f, 0.66f) : new Color(0.38f, 0.38f, 0.42f);
            Color bright = pro ? new Color(0.93f, 0.94f, 0.96f) : new Color(0.10f, 0.10f, 0.12f);

            _title = new GUIStyle(EditorStyles.label) { fontSize = 21, fontStyle = FontStyle.Bold, normal = { textColor = bright } };
            _label = new GUIStyle(EditorStyles.label) { fontSize = 12, normal = { textColor = dim } };
            _value = new GUIStyle(EditorStyles.label) { fontSize = 13, normal = { textColor = bright } };
            _h2 = new GUIStyle(EditorStyles.label) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = bright } };
            _wrap = new GUIStyle(EditorStyles.label) { fontSize = 12, wordWrap = true, normal = { textColor = dim } };
            _big = new GUIStyle(EditorStyles.label) { fontSize = 17, alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = bright } };
            _small = new GUIStyle(EditorStyles.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = dim } };
            _field = new GUIStyle(EditorStyles.textField) { fontSize = 13, fixedHeight = 0f, padding = new RectOffset(6, 6, 4, 4) };
            _area = new GUIStyle(EditorStyles.textArea) { fontSize = 13, fixedHeight = 0f, padding = new RectOffset(6, 6, 4, 4), wordWrap = true };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 12, fixedHeight = 0f };
            _bigButton = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold, fixedHeight = 0f };
            _toolbar = new GUIStyle(GUI.skin.button) { fontSize = 13, fixedHeight = 0f };
            _num = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleRight, normal = { textColor = dim } };
            _tag = new GUIStyle(EditorStyles.label) { fontSize = 9, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _stylesReady = true;
        }

        private void OnGUI()
        {
            EnsureStyles();

            if (_fading && Event.current.type == EventType.MouseDown) Event.current.Use();
            if (Event.current.type == EventType.MouseMove && _step == Step.Browse) Repaint();

            EditorGUIUtility.labelWidth = 108f;

            GUILayout.BeginHorizontal();
            GUILayout.Space(PAD_X);
            GUILayout.BeginVertical();
            GUILayout.Space(PAD_Y);

            DrawHeader();
            DrawBody();

            if (!string.IsNullOrEmpty(_statusMsg))
            {
                GUILayout.Space(10);
                EditorGUILayout.HelpBox(_statusMsg, _statusType);
            }

            DrawFooter();

            GUILayout.Space(PAD_Y);
            GUILayout.EndVertical();
            GUILayout.Space(PAD_X);
            GUILayout.EndHorizontal();

            if (_fading)
            {
                float t = (float)(EditorApplication.timeSinceStartup - _fadeT0);
                float a = t < FADE_HALF ? t / FADE_HALF : 1f - (t - FADE_HALF) / FADE_HALF;
                var bg = EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.24f) : new Color(0.76f, 0.76f, 0.78f);
                bg.a = Mathf.Clamp01(a);
                EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height), bg);
            }
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(STEP_TITLES[(int)_step], _title);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{(int)_step + 1}/{STEP_TITLES.Length}", _label);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            var bar = GUILayoutUtility.GetRect(1f, 3f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bar, EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.08f) : new Color(0f, 0f, 0f, 0.1f));
            float done = ((int)_step + 1) / (float)STEP_TITLES.Length;
            EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width * done, bar.height), new Color(0.28f, 0.62f, 0.95f));

            GUILayout.Space(12);
        }

        private void DrawBody()
        {
            GUILayout.BeginVertical();

            if (_step == Step.Options) DrawOptionsStep();
            else if (_step == Step.Cover) DrawCoverStep();
            else if (_step == Step.Build) DrawBuildStep();
            else if (_step == Step.Browse) DrawBrowseStep();
            else
            {
                _bodyScroll = EditorGUILayout.BeginScrollView(_bodyScroll);
                switch (_step)
                {
                    case Step.Repo: DrawRepoStep(); break;
                    case Step.Kind: DrawKindStep(); break;
                    case Step.Source: DrawSourceStep(); break;
                    case Step.Details: DrawDetailsStep(); break;
                }
                EditorGUILayout.EndScrollView();
            }

            GUILayout.EndVertical();
        }

        private void DrawFooter()
        {
            GUILayout.Space(12);
            var line = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(line, EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.07f) : new Color(0f, 0f, 0f, 0.1f));
            GUILayout.Space(10);

            GUILayout.BeginHorizontal();

            GUI.enabled = _step != Step.Repo && !_fading;
            if (GUILayout.Button("Back", _button, GUILayout.Width(90f), GUILayout.Height(30f)))
            {
                if (_menuCard >= 0) Fade(() => _menuCard = -1);
                else GoTo((Step)((int)_step - 1));
            }
            GUI.enabled = true;

            GUILayout.FlexibleSpace();

            if (_step != Step.Build && _step != Step.Browse)
            {
                GUI.enabled = CanAdvance() && !_fading;
                if (GUILayout.Button("Next", _bigButton, GUILayout.Width(120f), GUILayout.Height(30f)))
                    GoTo((Step)((int)_step + 1));
                GUI.enabled = true;
            }

            GUILayout.EndHorizontal();
        }

        private bool CanAdvance()
        {
            switch (_step)
            {
                case Step.Repo: return !string.IsNullOrWhiteSpace(_repoRoot) && Directory.Exists(_repoRoot);
                case Step.Source: return _kind == PackKind.Round ? CurrentMap != null : (_sourceObject != null || _keepBundle);
                case Step.Details: return !string.IsNullOrWhiteSpace(_name);
                default: return true;
            }
        }

        private void Fade(Action apply)
        {
            if (_fading) return;
            _statusMsg = "";
            _pending = apply;
            _fading = true;
            _fadeT0 = EditorApplication.timeSinceStartup;
            EditorApplication.update += FadeTick;
        }

        private void GoTo(Step step)
        {
            Fade(() =>
            {
                _step = step;
                _bodyScroll = Vector2.zero;
                _menuCard = -1;
                if (_step == Step.Browse) RefreshCards();
                if (_step == Step.Source && _kind == PackKind.Round) PullRoundDetails();
                if (_step == Step.Options && _kind != PackKind.Round)
                {
                    _preview.SetSource(ResolvePreviewObject());
                    _preview.SetShowBase(_kind == PackKind.Costume && _keepBase);
                }
            });
        }

        private void FadeTick()
        {
            float t = (float)(EditorApplication.timeSinceStartup - _fadeT0);

            if (t >= FADE_HALF && _pending != null)
            {
                var apply = _pending;
                _pending = null;
                apply();
            }

            if (t >= FADE_HALF * 2f)
            {
                _fading = false;
                EditorApplication.update -= FadeTick;
            }

            Repaint();
        }

        private void DrawRepoStep()
        {
            GUILayout.Label("Public skins repo", _label);
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            _repoRoot = EditorGUILayout.TextField(_repoRoot, _field, GUILayout.Height(ROW));
            if (EditorGUI.EndChangeCheck()) RefreshGroups();
            if (GUILayout.Button("Browse", _button, GUILayout.Width(90f), GUILayout.Height(ROW)))
            {
                string seed = !string.IsNullOrEmpty(_repoRoot) ? _repoRoot : EditorPrefs.GetString(PREF_LAST_CATALOG_DIR, "");
                string picked = EditorUtility.OpenFilePanel("Select generate_catalog.bat", seed, "bat");
                if (!string.IsNullOrEmpty(picked))
                {
                    _repoRoot = Path.GetDirectoryName(picked);
                    EditorPrefs.SetString(PREF_LAST_CATALOG_DIR, _repoRoot);
                    RefreshGroups();
                    GUI.FocusControl(null);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(12);

            if (_repoCoverPreview != null)
            {
                float w = Mathf.Min(EditorGUIUtility.currentViewWidth - PAD_X * 2f - 20f, REPO_COVER_W);
                float h = w * REPO_COVER_H / REPO_COVER_W;
                Rect box = GUILayoutUtility.GetRect(w, h, GUILayout.ExpandWidth(false));
                GUI.DrawTexture(box, _repoCoverPreview, ScaleMode.ScaleToFit);

                bool sizeOk = _repoCoverPreview.width == REPO_COVER_W && _repoCoverPreview.height == REPO_COVER_H;
                GUILayout.Space(4);
                if (!sizeOk)
                {
                    var warn = new GUIStyle(_small) { normal = { textColor = new Color(0.95f, 0.55f, 0.35f) } };
                    GUILayout.Label($"cover.png is {_repoCoverPreview.width}x{_repoCoverPreview.height}, must be {REPO_COVER_W}x{REPO_COVER_H} to fit in the mod", warn);
                }
            }
            else if (!string.IsNullOrWhiteSpace(_repoRoot) && Directory.Exists(_repoRoot))
            {
                GUILayout.Label($"No cover.png found in repo root — must be {REPO_COVER_W}x{REPO_COVER_H} to fit in the mod", _small);
            }

            if (!string.IsNullOrWhiteSpace(_repoRoot) && Directory.Exists(_repoRoot))
            {
                GUILayout.Space(10);
                if (!string.IsNullOrEmpty(_repoOwner))
                    GUILayout.Label($"{_repoOwner} / {_repoName}", _value);
                GUILayout.Label($"{_repoSkinCount} item{(_repoSkinCount == 1 ? "" : "s")}", _label);
                GUILayout.Space(2);
                var parts = new List<string>();
                for (int i = 0; i < SCAN_DIRS.Length; i++)
                    if (_repoCounts[i] > 0) parts.Add($"{SCAN_DIRS[i]} {_repoCounts[i]}");
                if (parts.Count > 0) GUILayout.Label(string.Join("   ·   ", parts.ToArray()), _label);
            }
        }

        private void ResetForNew()
        {
            _sourceObject = null;
            _loadedBundleFile = "";
            _loadedDir = "";
            _keepBundle = false;
            _bundlePreviewObject = null;
            _name = _author = _description = _group = "";
            _keepBase = false;
            _skinScale = 1f;
            _itemScale = 1f;
            _leftTransform = _rightTransform = null;
            _creatingMap = false;
            _newMapName = "";
            _coverPath = "";
            if (_coverPreview != null) { DestroyImmediate(_coverPreview); _coverPreview = null; }
            _outputDir = "";
            _kind = PackKind.Costume;
        }

        private void DrawSourceStep()
        {
            if (_kind == PackKind.Round) { DrawRoundSourceStep(); return; }

            if (!string.IsNullOrEmpty(_loadedBundleFile))
            {
                int mode = _keepBundle ? 0 : 1;
                int newMode = GUILayout.Toolbar(mode, new[] { "Don't change", "Rebuild from object" }, _toolbar, GUILayout.Height(26f));
                if (newMode != mode) _keepBundle = newMode == 0;
                GUILayout.Space(20);
            }

            if (_keepBundle)
            {
                GUILayout.Space(24);
                GUILayout.Label("Keeping the packed bundle", _big);
                GUILayout.Space(8);
                GUILayout.Label(_loadedBundleFile, _small);
                return;
            }

            GUILayout.Space(24);
            GUILayout.Label("Select a GameObject from the Hierarchy window", _big);
            GUILayout.Space(8);
            GUILayout.Label("Or a prefab from the Project window", _small);
            GUILayout.Space(24);

            _sourceObject = (GameObject)EditorGUILayout.ObjectField(_sourceObject, typeof(GameObject), true, GUILayout.Height(ROW));

            if (_sourceObject != null)
            {
                GUILayout.Space(16);
                GUILayout.Label(_sourceObject.name, _big);

                if (_kind == PackKind.Costume || _kind == PackKind.Accessory)
                {
                    GUILayout.Space(10);
                    Transform check = _sourceObject.transform.Find("Main FG/Body_LOD0 (merge)/Torso_C_jnt_NoStrechSquash");
                    if (check == null)
                    {
                        var warn = new GUIStyle(_small) { normal = { textColor = new Color(0.95f, 0.55f, 0.35f) } };
                        GUILayout.Label("doesn't look like it was exported/made using the base rig.blend file — missing Main FG/Body_LOD0 (merge)/Torso_C_jnt_NoStrechSquash", warn);
                    }
                }
            }
        }

        private void DrawKindStep()
        {
            GUILayout.Space(24);
            GUILayout.Label("What are you packing?", _big);
            GUILayout.Space(16);
            _kind = (PackKind)GUILayout.Toolbar((int)_kind, KIND_LABELS, _toolbar, GUILayout.Height(26f));
        }

        private void DrawDetailsStep()
        {
            _name = EditorGUILayout.TextField("Display name", _name, _field, GUILayout.Height(ROW));
            GUILayout.Space(7);

            if (_kind == PackKind.Round)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Description", _label, GUILayout.Width(EditorGUIUtility.labelWidth - 2f));
                _description = EditorGUILayout.TextArea(_description, _area, GUILayout.Height(42f));
                GUILayout.EndHorizontal();
                GUILayout.Space(10);
                GUILayout.Label("Shown on the loading screen when someone plays the round.", _wrap);
                return;
            }

            _author = EditorGUILayout.TextField("Author", _author, _field, GUILayout.Height(ROW));
            GUILayout.Space(7);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Description", _label, GUILayout.Width(EditorGUIUtility.labelWidth - 2f));
            _description = EditorGUILayout.TextArea(_description, _area, GUILayout.Height(42f));
            GUILayout.EndHorizontal();
            GUILayout.Space(7);

            if (_addingGroup)
            {
                GUI.SetNextControlName("bfgNewGroup");
                _newGroup = EditorGUILayout.TextField("Group", _newGroup, _field, GUILayout.Height(ROW));

                if (!_groupFieldFocused)
                {
                    EditorGUI.FocusTextInControl("bfgNewGroup");
                    _groupFieldFocused = true;
                }

                var e = Event.current;
                if (e.type == EventType.KeyDown)
                {
                    if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { CommitNewGroup(); e.Use(); }
                    else if (e.keyCode == KeyCode.Escape) { _addingGroup = false; _groupFieldFocused = false; _newGroup = ""; e.Use(); }
                }
                return;
            }

            var labels = new List<string> { "Unsorted" };
            labels.AddRange(_knownGroups);
            labels.Add("Add new...");

            int selected = 0;
            for (int i = 0; i < _knownGroups.Count; i++)
                if (string.Equals(_knownGroups[i], _group, StringComparison.OrdinalIgnoreCase))
                    selected = i + 1;

            int picked = EditorGUILayout.Popup("Group", selected, labels.ToArray(), GUILayout.Height(ROW));
            if (picked == labels.Count - 1)
            {
                _addingGroup = true;
                _groupFieldFocused = false;
                _newGroup = "";
            }
            else _group = picked == 0 ? "" : _knownGroups[picked - 1];
        }

        private void CommitNewGroup()
        {
            string g = (_newGroup ?? "").Trim();
            if (g.Length > 0)
            {
                _group = g;
                if (!_knownGroups.Exists(x => string.Equals(x, g, StringComparison.OrdinalIgnoreCase)))
                {
                    _knownGroups.Add(g);
                    _knownGroups.Sort(StringComparer.OrdinalIgnoreCase);
                }
            }
            _newGroup = "";
            _addingGroup = false;
            _groupFieldFocused = false;
            GUI.FocusControl(null);
        }

        private void RefreshGroups()
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _repoCounts = new int[SCAN_DIRS.Length];
            int skinCount = 0;
            if (!string.IsNullOrWhiteSpace(_repoRoot) && Directory.Exists(_repoRoot))
            {
                for (int c = 0; c < SCAN_DIRS.Length; c++)
                {
                    string path = Path.Combine(_repoRoot, SCAN_DIRS[c]);
                    if (!Directory.Exists(path)) continue;
                    foreach (string info in Directory.GetFiles(path, "info.json", SearchOption.AllDirectories))
                    {
                        skinCount++;
                        _repoCounts[c]++;
                        string group = SkinInfoJson.ReadStr(File.ReadAllText(info), "group").Trim();
                        if (!string.IsNullOrEmpty(group)) found.Add(group);
                    }
                }
            }
            _repoSkinCount = skinCount;
            _knownGroups = new List<string>(found);
            _knownGroups.Sort(StringComparer.OrdinalIgnoreCase);
            ClearCards();
            LoadRepoCover();
            LoadRepoGitInfo();
        }

        private void LoadRepoGitInfo()
        {
            _repoOwner = "";
            _repoName = "";
            if (string.IsNullOrWhiteSpace(_repoRoot) || !Directory.Exists(_repoRoot)) return;

            string configPath = Path.Combine(_repoRoot, ".git", "config");
            if (!File.Exists(configPath)) return;

            foreach (string line in File.ReadAllLines(configPath))
            {
                string t = line.Trim();
                if (!t.StartsWith("url", StringComparison.OrdinalIgnoreCase)) continue;
                int eq = t.IndexOf('=');
                if (eq < 0) continue;
                string url = t.Substring(eq + 1).Trim();

                int slash = url.LastIndexOf('/');
                int slash2 = slash > 0 ? url.LastIndexOf('/', slash - 1) : -1;
                if (slash < 0 || slash2 < 0) continue;

                string owner = url.Substring(slash2 + 1, slash - slash2 - 1);
                string name = url.Substring(slash + 1);
                if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                    name = name.Substring(0, name.Length - 4);

                int colon = owner.LastIndexOf(':');
                if (colon >= 0) owner = owner.Substring(colon + 1);

                _repoOwner = owner;
                _repoName = name;
                break;
            }
        }

        private void LoadRepoCover()
        {
            if (_repoCoverPreview != null) { DestroyImmediate(_repoCoverPreview); _repoCoverPreview = null; }
            _repoCoverPath = "";
            if (string.IsNullOrWhiteSpace(_repoRoot) || !Directory.Exists(_repoRoot)) return;

            string path = Path.Combine(_repoRoot, "cover.png");
            if (!File.Exists(path)) return;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (tex.LoadImage(File.ReadAllBytes(path)))
            {
                _repoCoverPath = path;
                _repoCoverPreview = tex;
            }
            else DestroyImmediate(tex);
        }

        private void DrawCoverStep()
        {
            Rect area = GUILayoutUtility.GetRect(10f, 10f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            float h = Mathf.Min(area.height, area.width * COVER_H / COVER_W);
            float w = h * COVER_W / COVER_H;
            var box = new Rect(area.x + (area.width - w) * 0.5f, area.y + (area.height - h) * 0.5f, w, h);

            EditorGUI.DrawRect(box, new Color(0.1f, 0.1f, 0.11f));
            if (_coverPreview != null) GUI.DrawTexture(box, _coverPreview, ScaleMode.ScaleAndCrop);
            else GUI.Label(box, "No cover picked", _small);

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_coverPreview == null ? "Choose image" : "Replace image", _button, GUILayout.Height(30f)))
            {
                string seed = !string.IsNullOrEmpty(_coverPath) ? _coverPath : EditorPrefs.GetString(PREF_LAST_COVER_DIR, "");
                string pickedFile = EditorUtility.OpenFilePanel("Select Cover Image", seed, "png,jpg,jpeg");
                if (!string.IsNullOrEmpty(pickedFile) && File.Exists(pickedFile))
                {
                    _coverPath = pickedFile;
                    EditorPrefs.SetString(PREF_LAST_COVER_DIR, Path.GetDirectoryName(pickedFile));
                    LoadCoverPreview(pickedFile);
                }
            }
            if (_coverPreview != null && GUILayout.Button("Clear", _button, GUILayout.Width(80f), GUILayout.Height(30f)))
            {
                _coverPath = "";
                DestroyImmediate(_coverPreview);
                _coverPreview = null;
            }
            GUILayout.EndHorizontal();
        }

        private void LoadCoverPreview(string path)
        {
            if (_coverPreview != null) { DestroyImmediate(_coverPreview); _coverPreview = null; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (tex.LoadImage(File.ReadAllBytes(path))) _coverPreview = tex;
            else DestroyImmediate(tex);
        }

        private void DrawOptionsStep()
        {
            if (_kind == PackKind.Round) { DrawRoundOptionsStep(); return; }

            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(230f));
            _optionsScroll = EditorGUILayout.BeginScrollView(_optionsScroll);

            if (_kind == PackKind.Costume)
            {
                EditorGUI.BeginChangeCheck();
                _keepBase = EditorGUILayout.ToggleLeft("Keep Fall Guy underneath", _keepBase, _label);
                if (EditorGUI.EndChangeCheck()) _preview.SetShowBase(_keepBase);
                GUILayout.Space(14);
                GUILayout.Label("Skin scale", _label);
                GUILayout.Space(4);
                _skinScale = EditorGUILayout.Slider(_skinScale, 0.1f, 4f);
            }
            else if (_kind == PackKind.Item)
            {
                if (_sourceObject != null) _itemScale = _sourceObject.transform.localScale.x;
                GUILayout.Label("Item scale", _label);
                GUILayout.Space(4);
                GUILayout.Label(_itemScale.ToString("0.###"), _value);
                GUILayout.Space(16);
                GUILayout.Label("Left hand", _label);
                GUILayout.Space(4);
                _leftTransform = (Transform)EditorGUILayout.ObjectField(_leftTransform, typeof(Transform), true, GUILayout.Height(ROW));
                GUILayout.Space(14);
                GUILayout.Label("Right hand", _label);
                GUILayout.Space(4);
                _rightTransform = (Transform)EditorGUILayout.ObjectField(_rightTransform, typeof(Transform), true, GUILayout.Height(ROW));
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.Space(14);

            Rect view = GUILayoutUtility.GetRect(10f, 10f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            _preview.Draw(view, _kind == PackKind.Costume ? _skinScale : _itemScale);

            GUILayout.EndHorizontal();
        }

        private void Ok(string msg) { _statusMsg = msg; _statusType = EditorMessageType.Info; Repaint(); }
        private void Err(string msg) { _statusMsg = msg; _statusType = EditorMessageType.Error; Repaint(); }
    }
}
