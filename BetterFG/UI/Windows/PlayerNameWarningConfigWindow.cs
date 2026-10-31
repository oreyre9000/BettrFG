using System;
using System.Collections.Generic;
using BetterFG.Tweaks;
using UnityEngine;
using UnityEngine.UI;
using static BetterFG.Tweaks.PlayerNameWarningTweak;
using BettrFG.uGUI;

namespace BetterFG.UI.Windows
{
    public class PlayerNameWarningConfigWindow : BetterFGWindow
    {
        public PlayerNameWarningConfigWindow(IntPtr ptr) : base(ptr) { }

        public static PlayerNameWarningConfigWindow Instance { get; private set; }

        protected override float WindowWidth => 310f;
        protected override float WindowHeight => 340f;
        protected override string WindowTitle => "ui.player_name_warnings";
        protected override string BgResourceName => "BetterFG.assets.ui.windows.generalbg_2.png";
        protected override string BgHoverResourceName => "BetterFG.assets.ui.windows.generalbg_2_hover.png";
        protected override bool DraggableFromTitle => true;

        protected override Vector3 InitialBgPosition => new Vector3(184f, 18.5f, 0f);
        protected override Vector3 InitialBgScale => new Vector3(1.41f, 1.6f, 1f);

        private readonly List<NameRule> _rules = new List<NameRule>();
        private ScrollRect _scroll;
        private RectTransform _scrollRt;

        private const float ROW_H = 21f;
        private const float BTN_H = 16f;
        private const float MODE_W = 110f;
        private static readonly Color ROW_EVEN = new Color(1f, 1f, 1f, 0.03f);
        private static readonly Color ROW_ODD = new Color(0f, 0f, 0f, 0f);
        private static readonly Color BTN_RED = new Color(0.45f, 0.22f, 0.22f, 1f);
        private static readonly Color BTN_BLUE = new Color(0.22f, 0.34f, 0.55f, 1f);
        private static readonly List<string> MODE_OPTIONS = new List<string> { "ui.contains", "ui.exactly" };

        public void Configure(PlayerNameWarningTweak tweak)
        {
            Instance = this;
            Reload();
            SetAnchorPosition(new Vector2(520f, -220f));
            ShowWindow();
            RebuildContent();
        }

        protected override bool ShowCloseButton => true;

        public override void Close()
        {
            if (Instance == this) Instance = null;
            base.Close();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Reload()
        {
            _rules.Clear();
            _rules.AddRange(PlayerNameWarningTweak.LoadRules());
        }

        private void Save(bool trimEmpty)
        {
            if (trimEmpty)
            {
                for (int i = _rules.Count - 1; i >= 0; i--)
                {
                    var r = _rules[i];
                    if (string.IsNullOrWhiteSpace(r.Text))
                    {
                        _rules.RemoveAt(i);
                        continue;
                    }
                    r.Text = r.Text.Trim();
                    _rules[i] = r;
                }
            }

            PlayerNameWarningTweak.SaveRules(_rules);
        }

        protected override void ManagedUpdate()
        {
            base.ManagedUpdate();
            if (_scroll == null || _scrollRt == null) return;

            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) < 0.01f) return;

            var mouse = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
            if (!RectTransformUtility.RectangleContainsScreenPoint(_scrollRt, mouse, null)) return;

            _scroll.verticalNormalizedPosition = Mathf.Clamp01(
                _scroll.verticalNormalizedPosition + wheel * 0.35f);
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            ContentPosition = new Vector3(190.6421f, 4.4f, 0f);
            ContentScale = new Vector3(1.0473f, 1f, 1f);
            Pivot = new Vector2(0f, 0.5f);
            TitlePosition = new Vector3(32.5674f, -1f, 0f);
            TitleScale = new Vector3(1.1818f, 1.3491f, 1f);

            float w = WindowWidth - PAD * 2f;
            float y = PAD * 0.5f;

            MakeLabel(contentRoot,
                new Rect(PAD, y, w - 48f, BTN_H),
                "ui.warn_if_a_player_s_name",
                FS_SM,
                new Color(1f, 1f, 1f, 0.72f));

            UGUIShip.CreateButton(contentRoot,
                new Rect(PAD + w - 42f, y, 42f, BTN_H),
                "ui.add_2",
                BTN_BLUE,
                WHITE, FS_SM,
                new Action(() =>
                {
                    _rules.Add(new NameRule { Exact = false, Text = "" });
                    Save(false);
                    RebuildContent();
                }));

            y += BTN_H + 4f;

            var scrollGo = new GameObject("Scroll");
            scrollGo.transform.SetParent(contentRoot, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(scrollRt, new Rect(PAD, y, w, WindowHeight - TITLE_H - y - PAD));
            scrollGo.AddComponent<Image>().color = Color.clear;
            _scrollRt = scrollRt;

            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 18f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll = scroll;

            var vpGo = new GameObject("Viewport");
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vpRt = vpGo.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = vpRt.offsetMax = Vector2.zero;
            vpGo.AddComponent<RectMask2D>();
            scroll.viewport = vpRt;

            var listGo = new GameObject("List");
            listGo.transform.SetParent(vpGo.transform, false);
            var listRt = listGo.AddComponent<RectTransform>();
            listRt.anchorMin = new Vector2(0f, 1f);
            listRt.anchorMax = new Vector2(1f, 1f);
            listRt.pivot = new Vector2(0.5f, 1f);
            listRt.offsetMin = listRt.offsetMax = Vector2.zero;
            var layout = listGo.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 0f;
            var fit = listGo.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = listRt;
            scrollGo.AddComponent<SmoothScroll>();

            for (int i = 0; i < _rules.Count; i++)
                BuildRuleRow(listRt, i, i % 2 == 0 ? ROW_EVEN : ROW_ODD);

            if (_rules.Count == 0)
                BuildEmptyRow(listRt);
        }

        private void BuildEmptyRow(RectTransform parent)
        {
            var rowGo = new GameObject("Row_empty");
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            UGUIShip.PaintStaticRowFill(rowGo, ROW_EVEN);
            MakeLabel(rowGo.transform, new Rect(5f, 0f, WindowWidth - PAD * 2f - 10f, ROW_H),
                "ui.no_rules_add_one_to_get_warned", FS_SM, HINT);
        }

        private void BuildRuleRow(RectTransform parent, int idx, Color bg)
        {
            var rowGo = new GameObject("Row_" + idx);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            UGUIShip.PaintStaticRowFill(rowGo, bg);

            int captured = idx;
            float removeW = 22f;
            float pad = 5f;

            UGUIShip.CreateDropdown(rowGo.transform,
                new Rect(0f, 0f, MODE_W, ROW_H),
                MODE_OPTIONS,
                _rules[idx].Exact ? 1 : 0,
                new Action<int>(sel =>
                {
                    if (captured < 0 || captured >= _rules.Count) return;
                    var r = _rules[captured];
                    r.Exact = sel == 1;
                    _rules[captured] = r;
                    Save(false);
                }),
                FS_SM);

            var field = UGUIShip.CreateInputField(rowGo.transform,
                new Rect(0f, 0f, 10f, ROW_H),
                "ui.player_name",
                Color.clear,
                WHITE,
                FS_SM);
            var fieldRt = field.GetComponent<RectTransform>();
            if (fieldRt != null)
            {
                fieldRt.anchorMin = Vector2.zero;
                fieldRt.anchorMax = Vector2.one;
                fieldRt.offsetMin = new Vector2(MODE_W + pad, 0f);
                fieldRt.offsetMax = Vector2.zero;
            }
            UGUIShip.SetInputText(field, _rules[idx].Text ?? "", false);
            field.onValueChanged.AddListener(new Action<string>(value =>
            {
                if (captured < 0 || captured >= _rules.Count) return;
                var r = _rules[captured];
                r.Text = value ?? "";
                _rules[captured] = r;
                Save(false);
            }));
            field.onEndEdit.AddListener(new Action<string>(value =>
            {
                if (captured < 0 || captured >= _rules.Count) return;
                var r = _rules[captured];
                r.Text = value ?? "";
                _rules[captured] = r;
                Save(true);
                RebuildContent();
            }));

            UGUIShip.CreateButton(rowGo.transform,
                new Rect(WindowWidth - PAD * 2f - removeW - pad, 2f, removeW, BTN_H),
                "X",
                BTN_RED,
                WHITE,
                FS_SM,
                new Action(() =>
                {
                    if (captured < 0 || captured >= _rules.Count) return;
                    _rules.RemoveAt(captured);
                    Save(true);
                    RebuildContent();
                }));
        }
    }
}
