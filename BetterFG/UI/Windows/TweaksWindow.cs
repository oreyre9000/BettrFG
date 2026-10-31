using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using BetterFG.Tweaks;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Windows
{
    public class TweaksWindow : SideWindow
    {
        public TweaksWindow(IntPtr ptr) : base(ptr) { }

        protected override float WindowWidth => 320f;
        protected override float WindowHeight => 220f;
        protected override string WindowTitle => "ui.tweaks";

        // survives the window being destroyed, so backing out of a tweak's config window (or just
        // reopening Tweaks) drops you where you left off instead of back at the top
        private static float _scrollPos = 1f;
        private static string _query = "";
        private int _seq;

        protected override void ManagedUpdate()
        {
            base.ManagedUpdate();
            if (_seq != BfgTweak.RowSeq)
            {
                _seq = BfgTweak.RowSeq;
                TweaksWindowBuilder.Refresh();
                return;
            }
            TweaksWindowBuilder.TickBars();
            TweaksWindowBuilder.TickPanels();
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            BgPosition = new Vector3(166.2293f, 74.9552f, 0f);
            BgScale = new Vector3(1.3415f, 4.5877f, 1f);
            ContentPosition = new Vector3(-1.6132f, -17.32f, 0f);
            ContentScale = new Vector3(1.0473f, 1.04f, 1f);
            ContentOffsetMin = new Vector2(-1.6132f, -25.92f);
            ContentOffsetMax = new Vector2(-1.6132f, -18.72f);
            Pivot = new Vector2(0f, 0.5f);
            TitlePosition = new Vector3(20.0368f, -6.7966f, 0f);
            TitleScale = new Vector3(1.1818f, 1.3491f, 1f);

            const float SEARCH_H = 22f;
            const float SEARCH_PAD = 3f;

            var searchField = UGUIShip.CreateInputField(contentRoot,
                new Rect(0f, 0f, 100f, SEARCH_H),
                "ui.search_tweaks", new Color(0f, 0f, 0f, 0.75f), Color.white, 12);
            UGUIShip.SetInputText(searchField, _query);
            var sfRt = searchField.GetComponent<RectTransform>();
            sfRt.anchorMin = new Vector2(0f, 1f);
            sfRt.anchorMax = new Vector2(1f, 1f);
            sfRt.pivot = new Vector2(0.5f, 1f);
            sfRt.offsetMin = new Vector2(RowLabelX, -(SEARCH_H + SEARCH_PAD));
            sfRt.offsetMax = new Vector2(-RowRightPad, -SEARCH_PAD);

            var scroll = UGUIShip.CreateScrollView(contentRoot,
                new Rect(0f, 0f, WindowWidth, WindowHeight - TITLE_H - SEARCH_H - SEARCH_PAD * 2f));
            var scrollRt = scroll.scrollRect.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = Vector2.zero;
            scrollRt.offsetMax = new Vector2(0f, -(SEARCH_H + SEARCH_PAD * 2f));
            scroll.scrollRect.viewport.GetComponent<RectMask2D>().softness = new Vector2Int(0, 36);

            // search field MUST sit on top in sibling order so the scroll viewport doesn't catch
            // its click events (children render+raycast in draw order, last sibling wins).
            sfRt.SetAsLastSibling();

            var listRt = scroll.content;
            var vlg = listRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 0f;
            var csf = listRt.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _seq = BfgTweak.RowSeq;
            TweaksWindowBuilder.BuildRows(listRt);
            TweaksWindowBuilder.Filter(_query);
            StartCoroutine(RestoreScroll(scroll.scrollRect, _scrollPos).WrapToIl2Cpp());

            searchField.onValueChanged.AddListener(new Action<string>(q =>
            {
                _query = q ?? "";
                TweaksWindowBuilder.Filter(_query);
            }));
        }

        // the fitter needs a frame to size the content before a normalised position means anything.
        // the listener only goes on after the restore, or the layout pass settling at the top would
        // overwrite the value we're about to put back.
        private IEnumerator RestoreScroll(ScrollRect sr, float pos)
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            sr.verticalNormalizedPosition = pos;
            sr.onValueChanged.AddListener(new Action<Vector2>(v => _scrollPos = Mathf.Clamp01(v.y)));
        }
    }

    internal static class TweaksWindowBuilder
    {
        private const float ROW_H = 22f;
        private const float TOGGLE_W = 36f;
        private const float TOGGLE_H = 16f;
        private const float PAD = 6f;
        private const float CHEV_W = 14f;
        private const float RIGHT = PAD + CHEV_W + 4f;
        private const float PANEL_SECS = 0.18f;
        private const float HEADER_GAP = 10f;
        private const float HEADER_H = 18f + HEADER_GAP;
        private const float HEADER_LEFT = SideWindow.RowLabelX;
        private const float HEADER_SCALE = 1.3f;  // literal Text gameObject localScale bump
        private static readonly Color ROW_EVEN = new Color(1f, 1f, 1f, 0.03f);
        private static readonly Color ROW_ODD = new Color(0f, 0f, 0f, 0f);
        private static readonly Color ON_COL = UGUIShip.TOGGLE_ON;
        private static readonly Color OFF_COL = UGUIShip.TOGGLE_OFF;
        private static readonly Color HEADER_COL = Color.white;

        // last list root we built into, so a custom button that flips a tweak's own state (e.g.
        // skip-rewards requeue mode) can redraw the rows and have its label reflect the new state
        // without the user reopening the window.
        private static RectTransform _lastRoot;
        private static string _lastQuery = "";

        // one entry per tweak row (+ its expanded settings/sliders/increments sub-rows, which
        // live and die with it) so Filter can show/hide instantly without rebuilding GameObjects.
        private struct TweakUnit
        {
            public List<GameObject> Gos;
            public string Hay;
        }
        private struct CategoryGroup
        {
            public GameObject Header;
            public string Hay;
            public List<TweakUnit> Units;
        }
        private static readonly List<CategoryGroup> _groups = new List<CategoryGroup>();
        private static readonly List<GameObject> _locked = new List<GameObject>();
        private static readonly List<(BfgTweak tweak, LoadingBar bar)> _bars = new List<(BfgTweak, LoadingBar)>();
        private static readonly Color PENDING_COL = new Color(0.25f, 0.45f, 0.75f, 1f);

        private static string StateLabel(BfgTweak t) => t.Pending ? "tweak.state_popup" : t.IsEnabled ? "ui.on" : "ui.off";
        private static Color StateColor(BfgTweak t) => t.Pending ? PENDING_COL : t.IsEnabled ? ON_COL : OFF_COL;

        private sealed class Panel
        {
            public RectTransform Body;
            public LayoutElement Le;
            public RectTransform Arrow;
            public bool Open;
            public float T;
            public Func<Texture> PreviewGet;
            public RawImage PreviewImg;
            public AspectRatioFitter PreviewFit;
        }
        private static readonly List<Panel> _panels = new List<Panel>();
        private static readonly HashSet<string> _openIds = new HashSet<string>();

        public static void TickBars()
        {
            foreach (var (tweak, bar) in _bars)
                if (bar != null) bar.Set(Mathf.Max(tweak.Progress, 0f));
        }

        public static void TickPanels()
        {
            float step = Time.unscaledDeltaTime / PANEL_SECS;
            foreach (var p in _panels)
            {
                if (p.PreviewGet != null && p.T > 0f) SyncPreview(p);
                float goal = p.Open ? 1f : 0f;
                if (p.T == goal) continue;
                p.T = Mathf.MoveTowards(p.T, goal, step);
                PosePanel(p);
            }
        }

        private static void SyncPreview(Panel p)
        {
            var tex = p.PreviewGet();
            if (p.PreviewImg.texture == tex) return;
            p.PreviewImg.texture = tex;
            p.PreviewImg.enabled = tex != null;
            if (tex != null) p.PreviewFit.aspectRatio = (float)tex.width / tex.height;
        }

        private static void PosePanel(Panel p)
        {
            float e = p.T * p.T * (3f - 2f * p.T);
            p.Body.gameObject.SetActive(p.T > 0f);
            p.Body.localScale = new Vector3(1f, e, 1f);
            p.Le.preferredHeight = p.Le.minHeight = p.T >= 1f ? -1f : p.Body.rect.height * e;
            p.Arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-90f, 90f, e));
        }

        public static void Refresh()
        {
            if (_lastRoot == null) return;
            for (int i = _lastRoot.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_lastRoot.GetChild(i).gameObject);
            BuildRows(_lastRoot);
            Filter(_lastQuery);
        }

        public static void BuildRows(RectTransform parent)
        {
            _lastRoot = parent;
            _groups.Clear();
            _locked.Clear();
            _bars.Clear();
            _panels.Clear();
            // section per category, header on top, then its tweaks. built in full every time —
            // Filter() is what handles the search box, purely by toggling activeSelf.
            foreach (var cat in TweakRegistry.CategoryOrder)
            {
                var tweaks = TweakRegistry.InCategory(cat);
                bool any = false;
                var group = new CategoryGroup
                {
                    Hay = cat.ToString().ToLowerInvariant(),
                    Units = new List<TweakUnit>()
                };
                int i = 0;
                foreach (var tweak in tweaks)
                {
                    if (!any) { group.Header = BuildHeader(parent, cat.ToString().ToUpperInvariant()); any = true; }
                    var gos = new List<GameObject>();
                    BuildRow(parent, tweak, i % 2 == 0 ? ROW_EVEN : ROW_ODD, gos);
                    group.Units.Add(new TweakUnit
                    {
                        Gos = gos,
                        Hay = ((tweak.TweakLabel ?? "") + " " + (tweak.TweakId ?? "")).ToLowerInvariant()
                    });
                    i++;
                }
                if (any) _groups.Add(group);
            }
            if (_locked.Count == 0) return;
            var mid = _locked[_locked.Count / 2];
            var note = UGUIShip.CreateLabel(mid.transform, new Rect(0f, 0f, 0f, ROW_H), "perf.locked", 11, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleCenter);
            var noteRt = note.rectTransform;
            noteRt.anchorMin = Vector2.zero;
            noteRt.anchorMax = Vector2.one;
            noteRt.offsetMin = noteRt.offsetMax = new Vector2(0f, _locked.Count % 2 == 0 ? ROW_H * 0.5f : 0f);
            note.gameObject.AddComponent<CanvasGroup>().ignoreParentGroups = true;
        }

        // toggles visibility of every built row against the query — no rebuild, so typing stays cheap
        // no matter how many tweaks exist. a category-name match shows the whole section.
        public static void Filter(string query)
        {
            _lastQuery = query ?? "";
            string q = _lastQuery.Trim().ToLowerInvariant();
            foreach (var g in _groups)
            {
                bool catMatch = q.Length > 0 && g.Hay.IndexOf(q, StringComparison.Ordinal) >= 0;
                bool anyShown = false;
                foreach (var unit in g.Units)
                {
                    bool show = q.Length == 0 || catMatch || unit.Hay.IndexOf(q, StringComparison.Ordinal) >= 0;
                    foreach (var go in unit.Gos)
                        if (go != null) go.SetActive(show);
                    if (show) anyShown = true;
                }
                if (g.Header != null) g.Header.SetActive(q.Length == 0 || anyShown);
            }
        }

        private static GameObject BuildHeader(RectTransform parent, string title)
        {
            float gap = parent.childCount == 0 ? 0f : HEADER_GAP;
            var rowGo = new GameObject("Header_" + title);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = HEADER_H - HEADER_GAP + gap;
            le.flexibleWidth = 1f;

            var lbl = UGUIShip.CreateLabel(rowGo.transform,
                new Rect(HEADER_LEFT, 0f, 200f, HEADER_H),
                title, 10,
                HEADER_COL,
                TextAnchor.MiddleLeft);
            lbl.fontStyle = FontStyle.Bold;
            // literal gameObject scale, not just font size. anchor to the row's left-center and pivot
            // left-center so the scale grows rightward + vertically-centered (not up off the top).
            var rt = lbl.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(HEADER_LEFT, -gap * 0.5f);
            rt.localScale = new Vector3(HEADER_SCALE, HEADER_SCALE, 1f);
            return rowGo;
        }

        private static void BuildRow(RectTransform parent, BfgTweak tweak, Color bg, List<GameObject> unitGos)
        {
            var rowGo = new GameObject("Row_" + tweak.TweakId);
            unitGos.Add(rowGo);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            UGUIShip.PaintStaticRowFill(rowGo, bg);

            // label — stretch fill minus toggle area
            const float LABEL_X = SideWindow.RowLabelX;
            var lbl = UGUIShip.CreateLabel(rowGo.transform,
                new Rect(LABEL_X, 0f, 200f, ROW_H),
                tweak.TweakLabel, 13,
                new Color(1f, 1f, 1f, 0.85f),
                TextAnchor.MiddleLeft);

            // little "?" right after the label text (no delay tooltip) for tweaks with credits/notes
            if (!string.IsNullOrEmpty(tweak.TweakTooltip))
            {
                const float HELP_D = 14f;
                UGUIShip.CreateHelp(rowGo.transform,
                    new Rect(LABEL_X + UGUIShip.PreferredWidth(lbl) + 5f, (ROW_H - HELP_D) * 0.5f, HELP_D, HELP_D),
                    tweak.TweakTooltip);
            }

            float rightOffset = RIGHT + TOGGLE_W;

            // inline numeric inputs — right-aligned, left of the toggle (and left of buttons).
            var inputs = tweak.GetInputFields();
            if (inputs != null && inputs.Count > 0)
            {
                for (int b = inputs.Count - 1; b >= 0; b--)
                {
                    var def = inputs[b];
                    float w = def.Width > 0f ? def.Width : 40f;
                    rightOffset += w + 3f;

                    var ifGo = new GameObject("InputField_" + b);
                    ifGo.transform.SetParent(rowGo.transform, false);
                    var ifRt = ifGo.AddComponent<RectTransform>();
                    ifRt.anchorMin = new Vector2(1f, 0.5f);
                    ifRt.anchorMax = new Vector2(1f, 0.5f);
                    ifRt.pivot = new Vector2(1f, 0.5f);
                    ifRt.anchoredPosition = new Vector2(-rightOffset + w, 0f);
                    ifRt.sizeDelta = new Vector2(w, TOGGLE_H);

                    var field = UGUIShip.CreateInputField(ifGo.transform,
                        new Rect(0f, 0f, w, TOGGLE_H),
                        def.Placeholder ?? "", null, null, 11);
                    field.text = def.Get?.Invoke() ?? "";
                    var capturedSet = def.Set;
                    field.onEndEdit.AddListener(new Action<string>(v => capturedSet?.Invoke(v)));
                }
            }

            LimitLabel(lbl, LABEL_X, rightOffset + 4f);

            // toggle button — right-anchored via child RectTransform
            var btnGo = new GameObject("Toggle");
            btnGo.transform.SetParent(rowGo.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(1f, 0.5f);
            btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-RIGHT, 0f);
            btnRt.sizeDelta = new Vector2(TOGGLE_W, TOGGLE_H);

            var capturedTweak = tweak;
            Text capturedLbl = null;

            var btn = UGUIShip.CreateButton(btnGo.transform,
                new Rect(0f, 0f, TOGGLE_W, TOGGLE_H),
                StateLabel(tweak),
                StateColor(tweak),
                Color.white, 9);

            if (tweak.Progress >= 0f)
            {
                var bar = UGUIShip.CreateLoadingBar(rowGo.transform, PENDING_COL);
                bar.Set(tweak.Progress);
                _bars.Add((tweak, bar));
            }

            capturedLbl = btn.GetComponentInChildren<Text>();

            btn.onClick.AddListener(new Action(() =>
            {
                capturedTweak.Toggle();
                UGUIShip.SetButtonColor(btn, StateColor(capturedTweak));
                if (capturedLbl != null) UGUIShip.RelabelText(capturedLbl, StateLabel(capturedTweak));
            }));

            if (PerformanceModeTweak.Overrides(tweak) && PerformanceModeTweak.Active)
            {
                var cg = rowGo.AddComponent<CanvasGroup>();
                cg.alpha = 0.4f;
                cg.interactable = false;
                _locked.Add(rowGo);
            }

            var customBtns = tweak.GetCustomButtons();
            var settings = tweak.GetSettings();
            var incs = tweak.GetIncrements();
            var sliders = tweak.GetSliders();
            var preview = tweak.GetPreview();
            if (!(preview != null || customBtns?.Count > 0 || settings?.Count > 0 || incs?.Count > 0 || sliders?.Count > 0)) return;

            var panelGo = new GameObject("Panel_" + tweak.TweakId);
            panelGo.transform.SetParent(parent, false);
            unitGos.Add(panelGo);
            var panelLe = panelGo.AddComponent<LayoutElement>();
            panelLe.flexibleWidth = 1f;
            var panelVlg = panelGo.AddComponent<VerticalLayoutGroup>();
            panelVlg.childControlWidth = true;
            panelVlg.childForceExpandWidth = true;
            panelVlg.childControlHeight = false;
            panelVlg.childForceExpandHeight = false;

            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(panelGo.transform, false);
            var bodyRt = bodyGo.AddComponent<RectTransform>();
            bodyRt.pivot = new Vector2(0.5f, 1f);
            var bodyVlg = bodyGo.AddComponent<VerticalLayoutGroup>();
            bodyVlg.childForceExpandWidth = true;
            bodyVlg.childForceExpandHeight = false;
            bodyGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RawImage previewImg = null;
            AspectRatioFitter previewFit = null;
            if (preview != null) (previewImg, previewFit) = BuildPreviewRow(bodyRt, preview.Height, bg);
            if (customBtns?.Count > 0) BuildButtonRow(bodyRt, customBtns, bg);
            if (settings != null)
                foreach (var s in settings)
                    BuildSettingRow(bodyRt, tweak, s, bg);
            if (incs != null)
                foreach (var inc in incs)
                    BuildIncrementRow(bodyRt, tweak, inc, bg);
            if (sliders != null)
                foreach (var sl in sliders)
                    BuildSliderRow(bodyRt, sl, bg);

            var chevGo = new GameObject("Chevron");
            chevGo.transform.SetParent(rowGo.transform, false);
            var chevRt = chevGo.AddComponent<RectTransform>();
            chevRt.anchorMin = chevRt.anchorMax = new Vector2(1f, 0.5f);
            chevRt.pivot = new Vector2(0.5f, 0.5f);
            chevRt.anchoredPosition = new Vector2(-(PAD + CHEV_W * 0.5f), 0f);
            chevRt.sizeDelta = new Vector2(CHEV_W, TOGGLE_H);
            var chev = UGUIShip.CreateButton(chevGo.transform, new Rect(0f, 0f, CHEV_W, TOGGLE_H),
                "›", new Color(0.18f, 0.18f, 0.18f, 1f), Color.white, 12);

            var arrow = chev.GetComponentInChildren<Text>().rectTransform;
            arrow.pivot = new Vector2(0.5f, 0.375f);
            arrow.anchoredPosition = new Vector2(CHEV_W * 0.5f, -TOGGLE_H * 0.5f);

            var panel = new Panel { Body = bodyRt, Le = panelLe, Arrow = arrow, Open = _openIds.Contains(tweak.TweakId) };
            panel.T = panel.Open ? 1f : 0f;
            if (preview != null)
            {
                panel.PreviewGet = preview.Get;
                panel.PreviewImg = previewImg;
                panel.PreviewFit = previewFit;
            }
            PosePanel(panel);
            _panels.Add(panel);
            string id = tweak.TweakId;
            chev.onClick.AddListener(new Action(() =>
            {
                panel.Open = !panel.Open;
                if (panel.Open) _openIds.Add(id); else _openIds.Remove(id);
            }));
        }

        private static void LimitLabel(Text lbl, float x, float right)
        {
            var rt = lbl.rectTransform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(-(x + right), ROW_H);
        }

        private static (RawImage, AspectRatioFitter) BuildPreviewRow(RectTransform parent, float height, Color bg)
        {
            var rowGo = new GameObject("Preview");
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = height + 8f;
            le.flexibleWidth = 1f;
            UGUIShip.PaintStaticRowFill(rowGo, bg);

            var (frame, inner) = UGUIShip.CreateFramedImage(rowGo.transform);
            frame.offsetMin = new Vector2(SideWindow.RowSubLabelX, 4f);
            frame.offsetMax = new Vector2(-RIGHT, -4f);

            var imgGo = new GameObject("Picture");
            imgGo.transform.SetParent(inner, false);
            var imgRt = imgGo.AddComponent<RectTransform>();
            imgRt.anchorMin = imgRt.anchorMax = new Vector2(0.5f, 0.5f);
            imgRt.pivot = new Vector2(0.5f, 0.5f);
            imgRt.offsetMin = imgRt.offsetMax = Vector2.zero;
            var raw = imgGo.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.enabled = false;
            var fit = imgGo.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            return (raw, fit);
        }

        private static GameObject BuildButtonRow(RectTransform parent, List<TweakButton> defs, Color bg)
        {
            var rowGo = new GameObject("Buttons");
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            UGUIShip.PaintStaticRowFill(rowGo, bg);

            var stripGo = new GameObject("Strip");
            stripGo.transform.SetParent(rowGo.transform, false);
            var stripRt = stripGo.AddComponent<RectTransform>();
            stripRt.anchorMin = Vector2.zero;
            stripRt.anchorMax = Vector2.one;
            stripRt.offsetMin = new Vector2(SideWindow.RowSubLabelX, (ROW_H - TOGGLE_H) * 0.5f);
            stripRt.offsetMax = new Vector2(-RIGHT, -(ROW_H - TOGGLE_H) * 0.5f);
            var hlg = stripGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            foreach (var def in defs)
            {
                var cellGo = new GameObject("Btn");
                cellGo.transform.SetParent(stripGo.transform, false);
                cellGo.AddComponent<RectTransform>();
                var captured = def.OnClick;
                var b = UGUIShip.CreateButton(cellGo.transform, new Rect(0f, 0f, 60f, TOGGLE_H),
                    def.Label, new Color(0.25f, 0.45f, 0.75f, 1f), Color.white, 9);
                b.onClick.AddListener(new Action(() => captured?.Invoke()));
                var brt = b.GetComponent<RectTransform>();
                brt.anchorMin = Vector2.zero;
                brt.anchorMax = Vector2.one;
                brt.offsetMin = brt.offsetMax = Vector2.zero;
            }
            return rowGo;
        }

        // slider as a sub-row in the expanded panel: label on the left (indented, like a setting),
        // slider + value readout on the right where the toggle sits on the parent row.
        private static GameObject BuildSliderRow(RectTransform parent, TweakSlider sl, Color bg)
        {
            var rowGo = new GameObject("Slider_" + sl.Label);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            UGUIShip.PaintStaticRowFill(rowGo, bg);

            const float SET_LABEL_X = SideWindow.RowSubLabelX;
            var label = UGUIShip.CreateLabel(rowGo.transform,
                new Rect(SET_LABEL_X, 0f, 200f, ROW_H),
                sl.Label, 12,
                new Color(1f, 1f, 1f, 0.7f),
                TextAnchor.MiddleLeft);

            const float SLIDER_W = 90f;
            const float VAL_W = 34f;
            float controlW = SLIDER_W + VAL_W + 4f;
            LimitLabel(label, SET_LABEL_X, RIGHT + controlW + 4f);

            var ctrlGo = new GameObject("SliderCtrl");
            ctrlGo.transform.SetParent(rowGo.transform, false);
            var ctrlRt = ctrlGo.AddComponent<RectTransform>();
            ctrlRt.anchorMin = ctrlRt.anchorMax = new Vector2(1f, 0.5f);
            ctrlRt.pivot = new Vector2(1f, 0.5f);
            ctrlRt.anchoredPosition = new Vector2(-RIGHT, 0f);
            ctrlRt.sizeDelta = new Vector2(controlW, TOGGLE_H);

            int decimals = sl.Step >= 1f ? 0 : sl.Step >= 0.1f ? 1 : 2;
            string Fmt(float v) => v.ToString("F" + decimals, System.Globalization.CultureInfo.InvariantCulture);

            var readout = UGUIShip.CreateLabel(ctrlGo.transform,
                new Rect(SLIDER_W + 4f, 0f, VAL_W, TOGGLE_H),
                Fmt(sl.Get()), 9, Color.white, TextAnchor.MiddleRight);

            var capturedSl = sl;
            UGUIShip.CreateSlider(ctrlGo.transform, 0f, 0f, SLIDER_W,
                "", Mathf.InverseLerp(sl.Min, sl.Max, sl.Get()), TOGGLE_H, 0f, 9,
                new Action<float>(t =>
                {
                    float v = Mathf.Round(Mathf.Lerp(capturedSl.Min, capturedSl.Max, t) / capturedSl.Step) * capturedSl.Step;
                    capturedSl.Set(v);
                    readout.text = Fmt(v);
                }),
                null, null, false);
            return rowGo;
        }

        // increment as a sub-row in the expanded panel: label on the left (indented, like a setting),
        // [-] value [+] on the right where the toggle sits on the parent row. gives the tweak a
        // two-line-tall look — toggle row on top, the stepper on its own line under it.
        private static GameObject BuildIncrementRow(RectTransform parent, BfgTweak tweak, TweakIncrement inc, Color bg)
        {
            var rowGo = new GameObject("Inc_" + tweak.TweakId);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            UGUIShip.PaintStaticRowFill(rowGo, bg);

            const float SET_LABEL_X = SideWindow.RowSubLabelX;
            var label = UGUIShip.CreateLabel(rowGo.transform,
                new Rect(SET_LABEL_X, 0f, 200f, ROW_H),
                string.IsNullOrEmpty(inc.Label) ? "Limit" : inc.Label, 12,
                new Color(1f, 1f, 1f, 0.7f),
                TextAnchor.MiddleLeft);

            float w = inc.Width > 0f ? inc.Width : 62f;
            LimitLabel(label, SET_LABEL_X, RIGHT + w + 4f);
            var incGo = new GameObject("IncCtrl");
            incGo.transform.SetParent(rowGo.transform, false);
            var incRt = incGo.AddComponent<RectTransform>();
            incRt.anchorMin = incRt.anchorMax = new Vector2(1f, 0.5f);
            incRt.pivot = new Vector2(1f, 0.5f);
            incRt.anchoredPosition = new Vector2(-RIGHT, 0f);
            incRt.sizeDelta = new Vector2(w, TOGGLE_H);

            UGUIShip.CreateIncrement(incGo.transform, new Rect(0f, 0f, w, TOGGLE_H),
                (float)inc.Min, (float)inc.Max, () => inc.Get(), v => inc.Set(Mathf.RoundToInt(v)),
                inc.Step > 0 ? inc.Step : 1f, false, inc.Wrap, 11);
            return rowGo;
        }

        private static GameObject BuildSettingRow(RectTransform parent, BfgTweak tweak, TweakSetting setting, Color bg)
        {
            var rowGo = new GameObject("Setting_" + tweak.TweakId + "_" + setting.Label);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            UGUIShip.PaintStaticRowFill(rowGo, bg);

            // label, indented past where the toggle icon sits so it reads as a child setting
            const float SET_LABEL_X = SideWindow.RowSubLabelX;
            var label = UGUIShip.CreateLabel(rowGo.transform,
                new Rect(SET_LABEL_X, 0f, 240f, ROW_H),
                setting.Label, 12,
                new Color(1f, 1f, 1f, 0.7f),
                TextAnchor.MiddleLeft);

            // cycling option button on the right, where the toggle lives on the parent row
            int idx = Mathf.Clamp(setting.Selected?.Invoke() ?? 0, 0, Mathf.Max(0, setting.Options.Length - 1));
            float w = 0f;
            foreach (var o in setting.Options) w = Mathf.Max(w, LocalizationService.Get(o).Length * 6.5f + 14f);
            w = Mathf.Clamp(w, 52f, 110f);
            LimitLabel(label, SET_LABEL_X, RIGHT + w + 4f);

            var btnGo = new GameObject("OptBtn");
            btnGo.transform.SetParent(rowGo.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(1f, 0.5f);
            btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-RIGHT, 0f);
            btnRt.sizeDelta = new Vector2(w, TOGGLE_H);

            var btn = UGUIShip.CreateButton(btnGo.transform,
                new Rect(0f, 0f, w, TOGGLE_H),
                setting.Options[idx],
                new Color(0.25f, 0.45f, 0.75f, 1f),
                Color.white, 9);

            var capturedSetting = setting;
            var capturedLbl = btn.GetComponentInChildren<Text>();
            btn.onClick.AddListener(new Action(() =>
            {
                int cur = capturedSetting.Selected?.Invoke() ?? 0;
                int next = (cur + 1) % capturedSetting.Options.Length;
                capturedSetting.OnPick?.Invoke(next);
                if (capturedLbl != null) UGUIShip.RelabelText(capturedLbl, capturedSetting.Options[next]);
            }));
            return rowGo;
        }
    }
}
