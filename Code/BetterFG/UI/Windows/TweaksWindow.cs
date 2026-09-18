using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Tweaks;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Windows
{
    public class TweaksWindow : SideWindow
    {
        public TweaksWindow(IntPtr ptr) : base(ptr) { }

        protected override float WindowWidth => 280f;
        protected override float WindowHeight => 220f;
        protected override string WindowTitle => "ui.tweaks";

        // survives the window being destroyed, so backing out of a tweak's config window (or just
        // reopening Tweaks) drops you where you left off instead of back at the top
        private static float _scrollPos = 1f;
        private static string _query = "";

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

            float rightOffset = PAD + TOGGLE_W;

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

            // custom buttons — right-aligned, left of the toggle
            var customBtns = tweak.GetCustomButtons();
            if (customBtns != null && customBtns.Count > 0)
            {
                for (int b = customBtns.Count - 1; b >= 0; b--)
                {
                    var def = customBtns[b];
                    float w = def.Width > 0f ? def.Width : Mathf.Max(28f, def.Label.Length * 6f + 10f);
                    rightOffset += w + 3f;

                    var cbGo = new GameObject("CustomBtn_" + b);
                    cbGo.transform.SetParent(rowGo.transform, false);
                    var cbRt = cbGo.AddComponent<RectTransform>();
                    cbRt.anchorMin = new Vector2(1f, 0.5f);
                    cbRt.anchorMax = new Vector2(1f, 0.5f);
                    cbRt.pivot = new Vector2(1f, 0.5f);
                    cbRt.anchoredPosition = new Vector2(-rightOffset + w, 0f);
                    cbRt.sizeDelta = new Vector2(w, TOGGLE_H);

                    var captured = def.OnClick;
                    UGUIShip.CreateButton(cbGo.transform,
                        new Rect(0f, 0f, w, TOGGLE_H),
                        def.Label,
                        new Color(0.25f, 0.45f, 0.75f, 1f),
                        Color.white, 9)
                        .onClick.AddListener(new Action(() => captured?.Invoke()));
                }
            }

            // toggle button — right-anchored via child RectTransform
            var btnGo = new GameObject("Toggle");
            btnGo.transform.SetParent(rowGo.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(1f, 0.5f);
            btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-PAD, 0f);
            btnRt.sizeDelta = new Vector2(TOGGLE_W, TOGGLE_H);

            var capturedTweak = tweak;
            Text capturedLbl = null;

            var btn = UGUIShip.CreateButton(btnGo.transform,
                new Rect(0f, 0f, TOGGLE_W, TOGGLE_H),
                tweak.IsEnabled ? "ui.on" : "ui.off",
                tweak.IsEnabled ? ON_COL : OFF_COL,
                Color.white, 9);

            capturedLbl = btn.GetComponentInChildren<Text>();

            // increments live in the expanded panel too, so they also need a full rebuild on toggle.
            bool hasSettings = capturedTweak.GetSettings()?.Count > 0 || capturedTweak.GetIncrements()?.Count > 0;
            btn.onClick.AddListener(new Action(() =>
            {
                capturedTweak.SetEnabled(!capturedTweak.IsEnabled);
                // a tweak with a settings panel needs the whole list rebuilt so the panel appears/
                // disappears under the row. plain tweaks just recolour the toggle in place.
                if (hasSettings) { Refresh(); return; }
                UGUIShip.SetButtonColor(btn, capturedTweak.IsEnabled ? ON_COL : OFF_COL);
                if (capturedLbl != null) UGUIShip.RelabelText(capturedLbl, capturedTweak.IsEnabled ? "ui.on" : "ui.off");
            }));

            // expanded settings panel, only while enabled. one sub-row per setting, same zebra bg as
            // the row so it reads as part of it. sits directly under the row (sibling order = draw order).
            if (tweak.IsEnabled)
            {
                var settings = tweak.GetSettings();
                if (settings != null)
                    foreach (var s in settings)
                        unitGos.Add(BuildSettingRow(parent, tweak, s, bg));

                var incs = tweak.GetIncrements();
                if (incs != null)
                    foreach (var inc in incs)
                        unitGos.Add(BuildIncrementRow(parent, tweak, inc, bg));

                var sliders = tweak.GetSliders();
                if (sliders != null)
                    foreach (var sl in sliders)
                        unitGos.Add(BuildSliderRow(parent, sl, bg));
            }
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
            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(SET_LABEL_X, 0f, 200f, ROW_H),
                sl.Label, 12,
                new Color(1f, 1f, 1f, 0.7f),
                TextAnchor.MiddleLeft);

            const float SLIDER_W = 90f;
            const float VAL_W = 34f;
            float controlW = SLIDER_W + VAL_W + 4f;

            var ctrlGo = new GameObject("SliderCtrl");
            ctrlGo.transform.SetParent(rowGo.transform, false);
            var ctrlRt = ctrlGo.AddComponent<RectTransform>();
            ctrlRt.anchorMin = ctrlRt.anchorMax = new Vector2(1f, 0.5f);
            ctrlRt.pivot = new Vector2(1f, 0.5f);
            ctrlRt.anchoredPosition = new Vector2(-PAD, 0f);
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
            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(SET_LABEL_X, 0f, 200f, ROW_H),
                string.IsNullOrEmpty(inc.Label) ? "Limit" : inc.Label, 12,
                new Color(1f, 1f, 1f, 0.7f),
                TextAnchor.MiddleLeft);

            float w = inc.Width > 0f ? inc.Width : 62f;
            var incGo = new GameObject("IncCtrl");
            incGo.transform.SetParent(rowGo.transform, false);
            var incRt = incGo.AddComponent<RectTransform>();
            incRt.anchorMin = incRt.anchorMax = new Vector2(1f, 0.5f);
            incRt.pivot = new Vector2(1f, 0.5f);
            incRt.anchoredPosition = new Vector2(-PAD, 0f);
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
            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(SET_LABEL_X, 0f, 240f, ROW_H),
                setting.Label, 12,
                new Color(1f, 1f, 1f, 0.7f),
                TextAnchor.MiddleLeft);

            // cycling option button on the right, where the toggle lives on the parent row
            int idx = Mathf.Clamp(setting.Selected?.Invoke() ?? 0, 0, Mathf.Max(0, setting.Options.Length - 1));
            float w = 0f;
            foreach (var o in setting.Options) w = Mathf.Max(w, o.Length * 6.5f + 14f);
            w = Mathf.Max(52f, w);

            var btnGo = new GameObject("OptBtn");
            btnGo.transform.SetParent(rowGo.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(1f, 0.5f);
            btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-PAD, 0f);
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
