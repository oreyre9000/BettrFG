using System;
using System.Collections.Generic;
using System.IO;
using BettrFG.uGUI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UI.Button;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

namespace BetterFG.Editor
{
    public partial class CreatorHubWindow : EditorWindow
    {
        private static CreatorHubWindow current;

        [MenuItem("BettrFG/Creator Hub")]
        public static void Open()
        {
            var w = GetWindow<CreatorHubWindow>(ShipLoc.Get("creator.title"));
            w.minSize = new Vector2(760f, 560f);
            w.position = new Rect(w.position.x, w.position.y, 1080f, 720f);
        }

        private const string PREF_LAST_COVER_DIR = "BetterFG.SkinPackager.LastCoverDir";
        private const string PREF_LAST_CATALOG_DIR = "BetterFG.SkinPackager.LastCatalogDir";

        private const float PAD_X = 44f;
        private const float PAD_Y = 22f;
        private const float ROW = 30f;
        private const float LABEL_W = 120f;
        private const int FS = 14;
        private const int FS_SM = 12;
        private const int FS_BIG = 18;

        private static readonly Color BG = new Color(0.035f, 0.035f, 0.04f, 1f);
        private static readonly Color WHITE = UGUIShip.WHITE;
        private static readonly Color LABEL = new Color(1f, 1f, 1f, 0.72f);
        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color WARN = new Color(0.95f, 0.55f, 0.35f, 1f);
        private static readonly Color BTN_DARK = UGUIShip.BTN_DARK;
        private static readonly Color BTN_BLUE = new Color(0.22f, 0.34f, 0.55f, 1f);
        private static readonly Color BTN_GREEN = UGUIShip.TOGGLE_ON;
        private static readonly Color BTN_RED = UGUIShip.BTN_REMOVE;
        private static readonly Color BTN_ORANGE = new Color(0.8f, 0.5f, 0.12f, 1f);

        private enum Step { Repo, Browse, Kind, Source, Details, Cover, Options, Build }
        private static readonly string[] STEP_TITLES = { "creator.step_repo", "creator.step_library", "creator.step_type", "creator.step_source", "creator.step_details", "creator.step_cover", "creator.step_settings", "creator.step_build" };

        private enum PackKind { Costume, Accessory, Item, Plinth, Round }
        private static readonly string[] KIND_LABELS = { "creator.kind_costume", "creator.kind_accessory", "creator.kind_item", "creator.kind_plinth", "creator.kind_round" };

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

        private bool _keepBase;
        private float _skinScale = 1f;

        private float _itemScale = 1f;
        private Transform _leftTransform;
        private Transform _rightTransform;

        private string _coverPath = "";
        private Texture2D _coverPreview;
        private bool _autoCover;
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
        private bool _statusErr;

        private readonly SkinPreview _preview = new SkinPreview();
        private RawImage _previewImg;

        private const float FADE_HALF = 0.5f;
        private bool _fading;
        private double _fadeT0;
        private Action _pending;
        private Image _fadeImg;

        private ShipHost _ship;
        private ShipInput _input;
        private UnityEngine.UIElements.Image _view;
        private readonly Queue<Action> _events = new Queue<Action>();
        private bool _dirty = true;
        private double _hotUntil;
        private bool _previewDirty;
        private double _nextPreviewFrame;
        private Button _nextBtn;

        private void OnEnable()
        {
            current = this;
            if (string.IsNullOrEmpty(_repoRoot))
                _repoRoot = EditorPrefs.GetString(PREF_LAST_CATALOG_DIR, "");
            RefreshGroups();
            EditorApplication.update += Tick;
        }

        private void OnDisable()
        {
            if (current == this) current = null;
            EditorApplication.update -= Tick;
            _preview.Cleanup();
            ClearCards();
            if (_coverPreview != null) { DestroyImmediate(_coverPreview); _coverPreview = null; }
            if (_repoCoverPreview != null) { DestroyImmediate(_repoCoverPreview); _repoCoverPreview = null; }
            _ship?.Dispose();
            _ship = null;
            _input = null;
        }

        private void OnSelectionChange()
        {
            if (_step == Step.Source && !_fading && _kind != PackKind.Round && !_keepBundle && Selection.activeGameObject != null)
            {
                _sourceObject = Selection.activeGameObject;
                RequestRebuild();
            }
        }

        private void CreateGUI()
        {
            _view = new UnityEngine.UIElements.Image { scaleMode = ScaleMode.StretchToFill, focusable = true };
            _view.style.flexGrow = 1f;
            rootVisualElement.Add(_view);

            _view.RegisterCallback<PointerMoveEvent>(e => { var p = (Vector2)e.localPosition; _events.Enqueue(() => _input.Move(p)); });
            _view.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                _view.Focus();
                _view.CapturePointer(e.pointerId);
                var p = (Vector2)e.localPosition;
                int clicks = e.clickCount;
                _events.Enqueue(() => _input.Down(p, clicks));
            });
            _view.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button != 0) return;
                _view.ReleasePointer(e.pointerId);
                var p = (Vector2)e.localPosition;
                _events.Enqueue(() => _input.Up(p));
            });
            _view.RegisterCallback<PointerLeaveEvent>(e => _events.Enqueue(() => _input.Leave()));
            _view.RegisterCallback<WheelEvent>(e =>
            {
                var p = e.localMousePosition;
                float lines = e.delta.y / 3f;
                _events.Enqueue(() => _input.Scroll(p, lines));
                e.StopPropagation();
            });
            _view.RegisterCallback<KeyDownEvent>(e =>
            {
                var ev = new Event { type = EventType.KeyDown, keyCode = e.keyCode, character = e.character, modifiers = e.modifiers };
                _events.Enqueue(() => _input.Key(ev));
                e.StopPropagation();
                e.PreventDefault();
            });
            _view.RegisterCallback<FocusOutEvent>(e => _events.Enqueue(() => _input.Blur()));
            _view.RegisterCallback<DragUpdatedEvent>(e =>
            {
                if (DragTarget() != null) DragAndDrop.visualMode = DragAndDropVisualMode.Link;
            });
            _view.RegisterCallback<DragPerformEvent>(e =>
            {
                var go = DragTarget();
                if (go == null) return;
                DragAndDrop.AcceptDrag();
                _sourceObject = go;
                RequestRebuild();
            });
        }

        private GameObject DragTarget()
        {
            if (_step != Step.Source || _kind == PackKind.Round || _keepBundle) return null;
            foreach (var o in DragAndDrop.objectReferences)
                if (o is GameObject g) return g;
            return null;
        }

        private void RequestRebuild() => _dirty = true;

        private void Tick()
        {
            if (_view == null) return;
            var size = _view.contentRect.size;
            if (size.x < 32f || size.y < 32f || float.IsNaN(size.x)) return;

            if (_ship == null)
            {
                _events.Clear();
                if (!ShipHost.EnsureTmp()) return;
                _ship = new ShipHost(size.x, size.y, EditorGUIUtility.pixelsPerPoint, BG);
                _input = new ShipInput(_ship);
                _view.image = _ship.Texture;
                _dirty = true;
            }
            else if (!Mathf.Approximately(size.x, _ship.Width) || !Mathf.Approximately(size.y, _ship.Height) || !Mathf.Approximately(EditorGUIUtility.pixelsPerPoint, _ship.Scale))
            {
                _ship.Resize(size.x, size.y, EditorGUIUtility.pixelsPerPoint);
                _view.image = _ship.Texture;
                _dirty = true;
            }

            bool busy = _events.Count > 0;
            while (_events.Count > 0) _events.Dequeue()();

            FadeTick();
            if (_dirty && !_input.Busy) { _dirty = false; BuildAll(); busy = true; _previewDirty = true; }
            if (_step == Step.Browse && LoadVisibleThumbs()) busy = true;

            double now = EditorApplication.timeSinceStartup;
            if (_previewImg != null && now >= _nextPreviewFrame)
            {
                _nextPreviewFrame = now + 1.0 / 30.0;
                _previewDirty = true;
                busy = true;
            }
            if (busy || _fading || _ship.Animating) _hotUntil = now + 0.4;
            if (now > _hotUntil) return;

            if (_previewImg != null && _previewDirty) { _previewDirty = false; RenderPreview(); }
            _ship.Tick();
            _ship.Render();
            _view.MarkDirtyRepaint();
        }

        private void BuildAll()
        {
            _ship.Clear();
            _previewImg = null;
            _nextBtn = null;
            _thumbs.Clear();
            var root = _ship.Root;
            float W = _ship.Width, H = _ship.Height;

            UGUIShip.CreatePanel(root, new Rect(0f, 0f, W, H), BG, "Backdrop");
            var panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(root, false);
            UGUIShip.SetPixelRect((RectTransform)panelGo.transform, new Rect(8f, 8f, W - 16f, H - 16f));
            var panelImg = panelGo.AddComponent<Image>();
            panelImg.raycastTarget = false;
            UGUIShip.ApplyDeluxPanel(panelImg);

            float x = PAD_X, w = W - PAD_X * 2f, y = PAD_Y;
            var title = UGUIShip.CreateLabel(root, new Rect(x, y, w - 80f, 34f), ShipLoc.Get(STEP_TITLES[(int)_step]).ToUpperInvariant(), 24, new Color(1f, 1f, 1f, 0.85f));
            title.fontStyle = FontStyle.Bold;
            UGUIShip.CreateLabel(root, new Rect(x + w - 80f, y, 80f, 34f), ShipLoc.Format("creator.step_count_fmt", (int)_step + 1, STEP_TITLES.Length), FS, LABEL, TextAnchor.MiddleRight);
            y += 42f;

            var trackGo = new GameObject("Progress", typeof(RectTransform));
            trackGo.transform.SetParent(root, false);
            UGUIShip.SetPixelRect((RectTransform)trackGo.transform, new Rect(x, y, w, 10f));
            var track = trackGo.AddComponent<Image>();
            track.raycastTarget = false;
            UGUIShip.ApplyDeluxBase(track, 4f);
            float done = ((int)_step + 1) / (float)STEP_TITLES.Length;
            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(trackGo.transform, false);
            UGUIShip.SetPixelRect((RectTransform)fillGo.transform, new Rect(1f, 1f, (w - 2f) * done, 8f));
            var fill = fillGo.AddComponent<Image>();
            fill.raycastTarget = false;
            UGUIShip.ApplyDeluxColorFill(fill, 4f);
            fill.color = new Color(0.28f, 0.62f, 0.95f, 1f);
            y += 26f;

            float footerY = H - PAD_Y - 34f;
            UGUIShip.CreateDivider(root, x, footerY - 14f, w);
            var body = Area(root, new Rect(x, y, w, footerY - 26f - y));

            switch (_step)
            {
                case Step.Repo: BuildRepoStep(body, w); break;
                case Step.Browse: BuildBrowseStep(body, w, body.rect.height); break;
                case Step.Kind: BuildKindStep(body, w); break;
                case Step.Source: BuildSourceStep(body, w, body.rect.height); break;
                case Step.Details: BuildDetailsStep(body, w); break;
                case Step.Cover: BuildCoverStep(body, w, body.rect.height); break;
                case Step.Options: BuildOptionsStep(body, w, body.rect.height); break;
                case Step.Build: BuildBuildStep(body, w, body.rect.height); break;
            }

            if (_step != Step.Repo)
                Btn(root, new Rect(x, footerY, 110f, 34f), "ui.back", BTN_DARK, () =>
                {
                    if (_menuCard >= 0) Fade(() => _menuCard = -1);
                    else GoTo((Step)((int)_step - 1));
                });

            if (_step != Step.Build && _step != Step.Browse)
                _nextBtn = Btn(root, new Rect(x + w - 140f, footerY, 140f, 34f), "ui.next", BTN_BLUE, () =>
                {
                    if (CanAdvance()) GoTo((Step)((int)_step + 1));
                });
            RefreshFooter();

            if (!string.IsNullOrEmpty(_statusMsg))
            {
                var s = UGUIShip.CreateLabel(root, new Rect(x + 124f, footerY, w - 280f, 34f), _statusMsg, FS_SM,
                    _statusErr ? new Color(1f, 0.45f, 0.4f, 1f) : LABEL, TextAnchor.MiddleCenter);
                s.horizontalOverflow = HorizontalWrapMode.Wrap;
            }

            var fadeGo = new GameObject("Fade", typeof(RectTransform));
            fadeGo.transform.SetParent(root, false);
            UGUIShip.SetPixelRect((RectTransform)fadeGo.transform, new Rect(0f, 0f, W, H));
            _fadeImg = fadeGo.AddComponent<Image>();
            ApplyFadeAlpha();
        }

        private void RefreshFooter()
        {
            if (_nextBtn == null) return;
            bool can = CanAdvance();
            _nextBtn.interactable = can;
            var lbl = _nextBtn.GetComponentInChildren<Text>();
            if (lbl != null) lbl.color = can ? WHITE : HINT;
        }

        private static RectTransform Area(Transform parent, Rect r, string name = "Area")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            UGUIShip.SetPixelRect(rt, r);
            return rt;
        }

        private static Button Btn(Transform parent, Rect r, string label, Color color, Action onClick, int fs = FS)
            => UGUIShip.CreateButton(parent, r, label, color, WHITE, fs, onClick);

        private static Text Lbl(Transform parent, Rect r, string text, int fs = FS, Color? color = null, TextAnchor anchor = TextAnchor.MiddleLeft)
            => UGUIShip.CreateLabel(parent, r, text, fs, color ?? LABEL, anchor);

        private static Text Big(Transform parent, float y, float w, string text) => Lbl(parent, new Rect(0f, y, w, 30f), text, FS_BIG, WHITE, TextAnchor.MiddleCenter);

        private static Text Small(Transform parent, float y, float w, string text, Color? color = null) => Lbl(parent, new Rect(0f, y, w, 22f), text, FS_SM, color ?? HINT, TextAnchor.MiddleCenter);

        private static InputField Field(Transform parent, Rect r, string value, string placeholder, Action<string> onChange, Action<string> onEnd = null)
        {
            var f = UGUIShip.CreateInputField(parent, r, placeholder, null, null, FS);
            UGUIShip.SetInputText(f, value ?? "");
            if (onChange != null) f.onValueChanged.AddListener(v => onChange(v));
            if (onEnd != null) f.onEndEdit.AddListener(v => onEnd(v));
            return f;
        }

        private static InputField AreaField(Transform parent, Rect r, string value, Action<string> onChange)
        {
            var f = Field(parent, r, value, "", onChange);
            f.lineType = InputField.LineType.MultiLineNewline;
            f.textComponent.alignment = TextAnchor.UpperLeft;
            f.textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            f.textComponent.verticalOverflow = VerticalWrapMode.Truncate;
            var trt = f.textComponent.rectTransform;
            trt.offsetMin = new Vector2(8f, 6f);
            trt.offsetMax = new Vector2(-8f, -6f);
            return f;
        }

        private static RawImage Framed(Transform parent, Rect r, Texture tex, string empty)
        {
            var box = Area(parent, r, "Framed");
            var (_, inner) = UGUIShip.CreateFramedImage(box, null, "Frame", 1.5f);
            var raw = inner.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            SetCover(raw, tex, r.width / Mathf.Max(r.height, 1f));
            if (tex == null && !string.IsNullOrEmpty(empty)) Lbl(box, new Rect(0f, 0f, r.width, r.height), empty, FS_SM, HINT, TextAnchor.MiddleCenter);
            return raw;
        }

        private static void SetCover(RawImage raw, Texture tex, float boxAspect)
        {
            raw.texture = tex;
            raw.enabled = tex != null;
            if (tex == null) return;
            float ta = tex.width / (float)Mathf.Max(tex.height, 1);
            raw.uvRect = ta > boxAspect
                ? new Rect((1f - boxAspect / ta) * 0.5f, 0f, boxAspect / ta, 1f)
                : new Rect(0f, (1f - ta / boxAspect) * 0.5f, 1f, ta / boxAspect);
        }

        private static void Pill(Transform parent, Rect r, string text, Color color, int fs = 10, bool bold = true)
        {
            var go = new GameObject("Pill", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UGUIShip.SetPixelRect((RectTransform)go.transform, r);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            var f = UGUIShip.ApplyDeluxSkin(go, img, color, false, 3f);
            if (f != null) f.color = color;
            var t = Lbl(go.transform, new Rect(0f, 0f, r.width, r.height), text, fs, WHITE, TextAnchor.MiddleCenter);
            if (bold) t.fontStyle = FontStyle.Bold;
        }

        private static void Toolbar(Transform parent, Rect r, string[] labels, int selected, Action<int> onPick)
        {
            float gap = 6f;
            float bw = (r.width - gap * (labels.Length - 1)) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                Btn(parent, new Rect(r.x + i * (bw + gap), r.y, bw, r.height), labels[i], i == selected ? BTN_BLUE : BTN_DARK, () => onPick(idx));
            }
        }

        private static void Pick(Transform parent, Rect r, List<string> labels, int selected, Action<int> onPick, bool above = false)
        {
            var on = new List<bool>();
            for (int i = 0; i < labels.Count; i++) on.Add(i == selected);
            var btn = UGUIShip.CreateMultiSelectDropdown(parent, r, labels[selected], labels, on, (i, _) => onPick(i),
                FS, 0f, 30f, true, true, above);
            UGUIShip.SetButtonColor(btn, BTN_DARK);
            CheckIcons(btn);
            var lbl = btn.GetComponentInChildren<Text>();
            lbl.alignment = TextAnchor.MiddleLeft;
            lbl.rectTransform.offsetMin = new Vector2(10f, lbl.rectTransform.offsetMin.y);
        }

        private static void CheckIcons(Button dropdown)
        {
            var panel = dropdown.transform.parent.GetChild(dropdown.transform.GetSiblingIndex() + 1);
            var sprite = UGUIShip.LoadSprite("BetterFG.assets.ui.button.icon_check.png");
            foreach (var t in panel.GetComponentsInChildren<Text>(true))
            {
                if (t.text != "✔") continue;
                t.text = "";
                var go = new GameObject("CheckIcon", typeof(RectTransform));
                go.transform.SetParent(t.transform, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(16f, 16f);
                var img = go.AddComponent<Image>();
                img.sprite = sprite;
                img.color = t.color;
                img.raycastTarget = false;
            }
        }

        private static void Check(Transform parent, Rect r, string label, bool on, Action<bool> onChange)
        {
            Btn(parent, new Rect(r.x, r.y, 52f, r.height), on ? "creator.on" : "creator.off", on ? UGUIShip.TOGGLE_ON : UGUIShip.TOGGLE_OFF, () => onChange(!on), FS_SM);
            Lbl(parent, new Rect(r.x + 62f, r.y, r.width - 62f, r.height), label, FS, WHITE);
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
        }

        private void FadeTick()
        {
            if (!_fading) return;
            float t = (float)(EditorApplication.timeSinceStartup - _fadeT0);
            if (t >= FADE_HALF && _pending != null)
            {
                var apply = _pending;
                _pending = null;
                apply();
                _dirty = true;
            }
            if (t >= FADE_HALF * 2f) _fading = false;
            ApplyFadeAlpha();
        }

        private void ApplyFadeAlpha()
        {
            if (_fadeImg == null) return;
            float t = (float)(EditorApplication.timeSinceStartup - _fadeT0);
            float a = !_fading ? 0f : t < FADE_HALF ? t / FADE_HALF : 1f - (t - FADE_HALF) / FADE_HALF;
            _fadeImg.color = new Color(BG.r, BG.g, BG.b, Mathf.Clamp01(a));
            _fadeImg.raycastTarget = _fading;
            _fadeImg.enabled = _fading;
        }

        private void GoTo(Step step)
        {
            Fade(() =>
            {
                _step = step;
                _menuCard = -1;
                if (_step == Step.Browse) RefreshCards();
                if (_step == Step.Source && _kind == PackKind.Round) PullRoundDetails();
                if (_step == Step.Cover && _kind != PackKind.Round && (_autoCover || string.IsNullOrEmpty(_coverPath))) AutoCover();
                if (_step == Step.Options && _kind != PackKind.Round)
                {
                    _preview.SetSource(ResolvePreviewObject());
                    _preview.SetShowBase(_kind == PackKind.Costume && _keepBase);
                    _preview.SetShowDummy(true);
                }
            });
        }

        private void BuildRepoStep(RectTransform p, float w)
        {
            float cw = Mathf.Min(w, 640f), cx = (w - cw) * 0.5f;
            var hello = Lbl(p, new Rect(cx, 8f, cw, 36f), "creator.welcome", 26, WHITE, TextAnchor.MiddleCenter);
            hello.fontStyle = FontStyle.Bold;

            const float cardY = 64f;
            var card = Area(p, new Rect(cx, cardY, cw, 10f), "RepoCard");
            var cardImg = card.gameObject.AddComponent<Image>();
            cardImg.raycastTarget = false;
            UGUIShip.ApplyDeluxPanelDark(cardImg);

            const float pad = 20f;
            float iw = cw - pad * 2f, y = pad;
            bool exists = !string.IsNullOrWhiteSpace(_repoRoot) && Directory.Exists(_repoRoot);

            if (exists)
            {
                if (_repoCoverPreview != null)
                {
                    float ch = iw * REPO_COVER_H / REPO_COVER_W;
                    Framed(card, new Rect(pad, y, iw, ch), _repoCoverPreview, null);
                    y += ch + 8f;
                    if (_repoCoverPreview.width != REPO_COVER_W || _repoCoverPreview.height != REPO_COVER_H)
                    {
                        Lbl(card, new Rect(pad, y, iw, 20f), ShipLoc.Format("creator.repo_cover_wrong_fmt", _repoCoverPreview.width, _repoCoverPreview.height, REPO_COVER_W, REPO_COVER_H), FS_SM, WARN);
                        y += 24f;
                    }
                    y += 6f;
                }

                var name = Lbl(card, new Rect(pad, y, iw, 26f), string.IsNullOrEmpty(_repoOwner) ? Path.GetFileName(_repoRoot) : $"{_repoOwner} / {_repoName}", FS_BIG + 2, WHITE);
                name.fontStyle = FontStyle.Bold;
                y += 40f;
            }
            else
            {
                Lbl(card, new Rect(pad, y, iw, 28f), "creator.repo_none", FS_BIG, WHITE, TextAnchor.MiddleCenter);
                y += 44f;
            }

            Field(card, new Rect(pad, y, iw - 120f, ROW), _repoRoot, "creator.repo_path_hint", v => _repoRoot = v,
                v => { RefreshGroups(); RequestRebuild(); });
            Btn(card, new Rect(pad + iw - 110f, y, 110f, ROW), "ui.browse", exists ? BTN_DARK : BTN_BLUE, () =>
            {
                string seed = !string.IsNullOrEmpty(_repoRoot) ? _repoRoot : EditorPrefs.GetString(PREF_LAST_CATALOG_DIR, "");
                string picked = EditorUtility.OpenFilePanel(ShipLoc.Get("creator.repo_pick_title"), seed, "bat");
                if (string.IsNullOrEmpty(picked)) return;
                _repoRoot = Path.GetDirectoryName(picked);
                EditorPrefs.SetString(PREF_LAST_CATALOG_DIR, _repoRoot);
                RefreshGroups();
                RequestRebuild();
            });
            y += ROW + pad;
            UGUIShip.SetPixelRect(card, new Rect(cx, cardY, cw, y));

            float ly = cardY + y + 20f;
            Lbl(p, new Rect(cx, ly, 120f, ROW), "ui.language", FS, LABEL);
            var langs = ShipLoc.Languages;
            var names = new List<string>();
            foreach (var l in langs) names.Add(ShipLoc.Name(l));
            Pick(p, new Rect(cx + 120f, ly, 220f, ROW), names, Mathf.Max(0, IndexOf(langs, ShipLoc.Current)), i =>
            {
                ShipLoc.Set(langs[i]);
                titleContent = new GUIContent(ShipLoc.Get("creator.title"));
                RequestRebuild();
            }, true);
        }

        private static int IndexOf(IReadOnlyList<string> list, string v)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == v) return i;
            return -1;
        }

        private static string CategoryName(string dir) => ShipLoc.Get("creator.cat_" + dir.ToLowerInvariant());

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
            _autoCover = false;
            if (_coverPreview != null) { DestroyImmediate(_coverPreview); _coverPreview = null; }
            _outputDir = "";
            _kind = PackKind.Costume;
        }

        private void BuildSourceStep(RectTransform p, float w, float h)
        {
            if (_kind == PackKind.Round) { BuildRoundSourceStep(p, w); return; }

            float y = 0f;
            if (!string.IsNullOrEmpty(_loadedBundleFile))
            {
                Toolbar(p, new Rect(0f, 0f, w, ROW), new[] { "creator.keep_bundle_option", "creator.rebuild_option" }, _keepBundle ? 0 : 1,
                    i =>
                    {
                        _keepBundle = i == 0;
                        if (!_keepBundle && Selection.activeGameObject != null) _sourceObject = Selection.activeGameObject;
                        RequestRebuild();
                    });
                y = ROW + 20f;
            }

            const float colW = 260f;
            float px = colW + 20f;
            _preview.SetSource(ResolvePreviewObject());
            _preview.SetShowBase(_kind == PackKind.Costume && _keepBase);
            _preview.SetShowDummy(false);
            PreviewArea(p, new Rect(px, y, w - px, h - y));

            if (_keepBundle)
            {
                Lbl(p, new Rect(0f, y, colW, 30f), "creator.keeping_bundle", FS_BIG, WHITE);
                Lbl(p, new Rect(0f, y + 32f, colW, 22f), _loadedBundleFile, FS_SM, HINT);
                return;
            }

            var title = Lbl(p, new Rect(0f, y, colW, 30f), "creator.source_pick", FS_BIG, WHITE);
            title.horizontalOverflow = HorizontalWrapMode.Wrap;
            var sub = Lbl(p, new Rect(0f, y + 32f, colW, 44f), "creator.source_pick_sub", FS_SM, HINT, TextAnchor.UpperLeft);
            sub.horizontalOverflow = HorizontalWrapMode.Wrap;
            y += 88f;

            Btn(p, new Rect(0f, y, colW, ROW), _sourceObject != null ? _sourceObject.name : "creator.source_none", new Color(0.12f, 0.12f, 0.12f, 1f),
                () => { if (_sourceObject != null) EditorGUIUtility.PingObject(_sourceObject); });
            y += ROW + 8f;
            Btn(p, new Rect(0f, y, colW, ROW), "creator.use_selection", BTN_DARK, () =>
            {
                if (Selection.activeGameObject == null) { Err(ShipLoc.Get("creator.err_nothing_selected")); return; }
                _sourceObject = Selection.activeGameObject;
                RequestRebuild();
            }, FS_SM);
            y += ROW + 16f;

            if (_sourceObject != null && (_kind == PackKind.Costume || _kind == PackKind.Accessory) &&
                _sourceObject.transform.Find("Main FG/Body_LOD0 (merge)/Torso_C_jnt_NoStrechSquash") == null)
            {
                var t = Lbl(p, new Rect(0f, y, colW, 60f), "creator.warn_not_base_rig", FS_SM, WARN, TextAnchor.UpperLeft);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
        }

        private void PreviewArea(RectTransform p, Rect r)
        {
            var area = Area(p, r, "PreviewArea");
            var (_, inner) = UGUIShip.CreateFramedImage(area, new Color(0.13f, 0.135f, 0.15f, 1f), "PreviewFrame", 1.5f);
            _previewImg = inner.gameObject.AddComponent<RawImage>();
            _previewImg.raycastTarget = true;
            _previewImg.enabled = false;
            var trig = inner.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var drag = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.Drag };
            drag.callback.AddListener(d =>
            {
                var ped = (UnityEngine.EventSystems.PointerEventData)d;
                _preview.OrbitBy(ped.delta / _ship.Scale);
                _previewDirty = true;
            });
            trig.triggers.Add(drag);
            var wheel = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.Scroll };
            wheel.callback.AddListener(d =>
            {
                _preview.ZoomBy(-((UnityEngine.EventSystems.PointerEventData)d).scrollDelta.y * 3f);
                _previewDirty = true;
            });
            trig.triggers.Add(wheel);

            var names = SkinPreview.ClipNames();
            names.Insert(0, ShipLoc.Get("creator.anim_auto"));
            Pick(area, new Rect(r.width - 304f, 8f, 180f, 30f), names, _preview.Selected + 1, i =>
            {
                _preview.Selected = i - 1;
                RequestRebuild();
            });
            var pp = Btn(area, new Rect(r.width - 44f, 8f, 36f, 30f), "", BTN_DARK, () =>
            {
                _preview.Paused = !_preview.Paused;
                RequestRebuild();
            }, FS_SM);
            var ico = new GameObject("Icon", typeof(RectTransform)).AddComponent<Image>();
            ico.transform.SetParent(pp.transform, false);
            ((RectTransform)ico.transform).sizeDelta = new Vector2(16f, 16f);
            ico.sprite = UGUIShip.LoadSprite(_preview.Paused ? "BetterFG.assets.ui.general.icon_play.png" : "BetterFG.assets.ui.general.icon_pause.png");
            ico.raycastTarget = false;
        }

        private void BuildKindStep(RectTransform p, float w)
        {
            Big(p, 24f, w, "creator.kind_question");
            float tw = Mathf.Min(w, 720f);
            Toolbar(p, new Rect((w - tw) * 0.5f, 72f, tw, 36f), KIND_LABELS, (int)_kind, i => { _kind = (PackKind)i; RequestRebuild(); });
        }

        private void BuildDetailsStep(RectTransform p, float w)
        {
            float fx = LABEL_W, fw = w - LABEL_W;
            float y = 0f;
            Lbl(p, new Rect(0f, y, LABEL_W, ROW), "creator.display_name");
            Field(p, new Rect(fx, y, fw, ROW), _name, "creator.display_name_hint", v => { _name = v; RefreshFooter(); });
            y += ROW + 10f;

            if (_kind == PackKind.Round)
            {
                Lbl(p, new Rect(0f, y, LABEL_W, ROW), "creator.description");
                AreaField(p, new Rect(fx, y, fw, 72f), _description, v => _description = v);
                y += 82f;
                Lbl(p, new Rect(fx, y, fw, 22f), "creator.round_description_hint", FS_SM, HINT);
                return;
            }

            Lbl(p, new Rect(0f, y, LABEL_W, ROW), "creator.author");
            Field(p, new Rect(fx, y, fw, ROW), _author, "", v => _author = v);
            y += ROW + 10f;

            Lbl(p, new Rect(0f, y, LABEL_W, ROW), "creator.description");
            AreaField(p, new Rect(fx, y, fw, 72f), _description, v => _description = v);
            y += 82f;

            Lbl(p, new Rect(0f, y, LABEL_W, ROW), "creator.group");
            if (_addingGroup)
            {
                var f = Field(p, new Rect(fx, y, fw - 200f, ROW), _newGroup, "creator.group_new_hint", v => _newGroup = v);
                Btn(p, new Rect(w - 190f, y, 90f, ROW), "ui.add", BTN_GREEN, CommitNewGroup, FS_SM);
                Btn(p, new Rect(w - 94f, y, 94f, ROW), "ui.cancel", BTN_DARK, () => { _addingGroup = false; _newGroup = ""; RequestRebuild(); }, FS_SM);
                _ship.Events.SetSelectedGameObject(f.gameObject);
                return;
            }

            var labels = new List<string> { ShipLoc.Get("creator.group_unsorted") };
            labels.AddRange(_knownGroups);
            labels.Add(ShipLoc.Get("creator.group_add_new"));
            int selected = 0;
            for (int i = 0; i < _knownGroups.Count; i++)
                if (string.Equals(_knownGroups[i], _group, StringComparison.OrdinalIgnoreCase))
                    selected = i + 1;

            Pick(p, new Rect(fx, y, fw, ROW), labels, selected, picked =>
            {
                if (picked == labels.Count - 1) { _addingGroup = true; _newGroup = ""; }
                else _group = picked == 0 ? "" : _knownGroups[picked - 1];
                RequestRebuild();
            });
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
            RequestRebuild();
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

        private void BuildCoverStep(RectTransform p, float w, float h)
        {
            float areaH = h - ROW - 16f;
            float ch = Mathf.Min(areaH, w * COVER_H / COVER_W);
            float cw = ch * COVER_W / COVER_H;
            Framed(p, new Rect((w - cw) * 0.5f, (areaH - ch) * 0.5f, cw, ch), _coverPreview, "creator.no_cover_picked");

            float by = h - ROW;
            bool canGenerate = _coverPreview == null && _kind != PackKind.Round;
            float bw = _coverPreview != null || canGenerate ? w - 110f : w;
            Btn(p, new Rect(0f, by, bw, ROW), _coverPreview == null ? "creator.choose_image" : "creator.replace_image", BTN_BLUE, () =>
            {
                string seed = !string.IsNullOrEmpty(_coverPath) && !_autoCover ? _coverPath : EditorPrefs.GetString(PREF_LAST_COVER_DIR, "");
                string pickedFile = EditorUtility.OpenFilePanel(ShipLoc.Get("creator.cover_pick_title"), seed, "png,jpg,jpeg");
                if (string.IsNullOrEmpty(pickedFile) || !File.Exists(pickedFile)) return;
                _coverPath = pickedFile;
                _autoCover = false;
                EditorPrefs.SetString(PREF_LAST_COVER_DIR, Path.GetDirectoryName(pickedFile));
                LoadCoverPreview(pickedFile);
                RequestRebuild();
            });
            if (_coverPreview != null)
                Btn(p, new Rect(w - 100f, by, 100f, ROW), "creator.clear", BTN_DARK, () =>
                {
                    _coverPath = "";
                    _autoCover = false;
                    DestroyImmediate(_coverPreview);
                    _coverPreview = null;
                    RequestRebuild();
                });
            else if (canGenerate)
                Btn(p, new Rect(w - 100f, by, 100f, ROW), "creator.generate_cover", BTN_DARK, () =>
                {
                    AutoCover();
                    RequestRebuild();
                });
        }

        private void AutoCover()
        {
            _preview.SetSource(ResolvePreviewObject());
            _preview.SetShowBase(_kind == PackKind.Costume && _keepBase);
            _preview.SetShowDummy(false);
            var tex = _preview.RenderStill(COVER_W, COVER_H);
            if (tex == null) return;
            _coverPath = Path.Combine(Path.GetTempPath(), "bettrfg_cover.png");
            File.WriteAllBytes(_coverPath, tex.EncodeToPNG());
            if (_coverPreview != null) DestroyImmediate(_coverPreview);
            _coverPreview = tex;
            _autoCover = true;
        }

        private void LoadCoverPreview(string path)
        {
            if (_coverPreview != null) { DestroyImmediate(_coverPreview); _coverPreview = null; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (tex.LoadImage(File.ReadAllBytes(path))) _coverPreview = tex;
            else DestroyImmediate(tex);
        }

        private void BuildOptionsStep(RectTransform p, float w, float h)
        {
            if (_kind == PackKind.Round) { BuildRoundOptionsStep(p, w); return; }

            const float colW = 260f;
            float y = 0f;
            if (_kind == PackKind.Costume)
            {
                Check(p, new Rect(0f, y, colW, ROW), "creator.keep_base", _keepBase, v =>
                {
                    _keepBase = v;
                    _preview.SetShowBase(v);
                    RequestRebuild();
                });
                y += ROW + 22f;
                Lbl(p, new Rect(0f, y, colW, 22f), "creator.skin_scale");
                var val = Lbl(p, new Rect(colW - 60f, y, 60f, 22f), _skinScale.ToString("0.00"), FS, WHITE, TextAnchor.MiddleRight);
                y += 26f;
                UGUIShip.CreateSlider(p, 0f, y, colW, "", Mathf.InverseLerp(0.1f, 4f, _skinScale), ROW, 4f, FS, v =>
                {
                    _skinScale = Mathf.Lerp(0.1f, 4f, v);
                    val.text = _skinScale.ToString("0.00");
                }, reserveLabel: false);
            }
            else if (_kind == PackKind.Item)
            {
                if (_sourceObject != null) _itemScale = _sourceObject.transform.localScale.x;
                Lbl(p, new Rect(0f, y, colW, 22f), "creator.item_scale");
                Lbl(p, new Rect(0f, y + 24f, colW, 22f), _itemScale.ToString("0.###"), FS, WHITE);
                y += 64f;
                y = HandSlot(p, y, colW, "creator.left_hand", _leftTransform, t => _leftTransform = t);
                HandSlot(p, y, colW, "creator.right_hand", _rightTransform, t => _rightTransform = t);
            }

            float px = colW + 20f;
            PreviewArea(p, new Rect(px, 0f, w - px, h));
        }

        private float HandSlot(RectTransform p, float y, float colW, string label, Transform current, Action<Transform> set)
        {
            Lbl(p, new Rect(0f, y, colW, 22f), label);
            y += 26f;
            Btn(p, new Rect(0f, y, colW - 70f, ROW), current != null ? current.name : "creator.none_transform", new Color(0.12f, 0.12f, 0.12f, 1f),
                () => { if (current != null) EditorGUIUtility.PingObject(current); }, FS_SM);
            Btn(p, new Rect(colW - 64f, y, 64f, ROW), "creator.use_sel_short", BTN_DARK, () =>
            {
                set(Selection.activeTransform);
                RequestRebuild();
            }, FS_SM - 1);
            return y + ROW + 18f;
        }

        private void RenderPreview()
        {
            var r = _previewImg.rectTransform.rect;
            var tex = _preview.Render(new Rect(0f, 0f, r.width, r.height), _kind == PackKind.Costume ? _skinScale : _itemScale);
            _previewImg.texture = tex;
            _previewImg.enabled = tex != null;
        }

        private void Ok(string msg) { _statusMsg = msg; _statusErr = false; RequestRebuild(); }
        private void Err(string msg) { _statusMsg = msg; _statusErr = true; RequestRebuild(); }
    }
}
