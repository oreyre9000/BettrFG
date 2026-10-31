using System;
using System.Collections.Generic;
using BetterFG.Customization.Menu;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Tab
{
    public class UIPatternPickerTab : UISubTab
    {
        public UIPatternPickerTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Themes Pattern";
        public override float OpenHeight => UIScale.TAB_CONTENT_H * 1.3f;

        protected override BetterFGTab MakeSwitchTarget()
        {
            var t = BetterFGTabRegistry.NewTab<ThemesTab>();
            t.InitialScreen = Screen;
            return t;
        }

        public ScreenBackgroundService.Screen Screen { get; set; } = ScreenBackgroundService.Screen.FallForce;

        const int COLS = 4;
        const float GAP = 6f;
        const float CAPTION_H = 16f;

        static readonly Dictionary<string, Texture2D> _builtinThumbCache = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Texture2D> _customThumbCache = new Dictionary<string, Texture2D>();

        static Texture2D BuiltinThumb(string id)
        {
            if (!_builtinThumbCache.TryGetValue(id, out var tex) || tex == null)
                _builtinThumbCache[id] = tex = ScreenBackgroundService.LoadBuiltinTexture(id);
            return tex;
        }

        static Texture2D CustomThumb(string path)
        {
            if (!_customThumbCache.TryGetValue(path, out var tex) || tex == null)
                _customThumbCache[path] = tex = ScreenBackgroundService.LoadPatternTexture(path);
            return tex;
        }

        RectTransform _content;
        float CellW => (TabWidth - PAD * 2f - UGUIShip.SCROLLBAR_INSET * 2f - GAP * (COLS - 1)) / COLS;

        void Update() => WinDialogs.Tick();

        protected override void BuildContent(RectTransform contentRoot)
        {
            var (_, content) = UGUIShip.CreateScrollView(contentRoot, new Rect(0f, VPAD, TabWidth, TabHeight - VPAD));
            _content = content;

            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.padding = new RectOffset((int)PAD, (int)PAD, (int)PAD, (int)PAD);
            grid.spacing = new Vector2(GAP, GAP);
            grid.cellSize = new Vector2(CellW, CellW + CAPTION_H);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = COLS;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RefreshGrid();
            PositionSwitchLink();
        }

        void Back() => BetterFGUIMan.Instance?.SwitchSlotTab(this, MakeSwitchTarget());

        void RefreshGrid()
        {
            for (int i = _content.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_content.GetChild(i).gameObject);

            string current = SettingsService.Get(ScreenBackgroundService.KeyPattern(Screen), "");

            BuildTile("", null, "Default", false, current);
            BuildTile(ScreenBackgroundService.PatternNone, null, "None", false, current);
            foreach (var (id, label) in ScreenBackgroundService.BuiltinPatterns)
                BuildTile(ScreenBackgroundService.BuiltinKey(id), BuiltinThumb(id), label, false, current);
            foreach (var path in ScreenBackgroundService.LoadCustomPatterns())
                BuildTile(path, CustomThumb(path), System.IO.Path.GetFileName(path), true, current);

            BuildAddTile();
        }

        void BuildTile(string value, Texture2D thumb, string caption, bool deletable, string current)
        {
            float cellW = CellW;
            bool selected = current == value;

            if (thumb == null)
            {
                UGUIShip.CreateButton(_content, new Rect(0f, 0f, cellW, cellW + CAPTION_H), caption,
                    selected ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SelectPattern(value)));
                return;
            }

            var btn = UGUIShip.CreateButton(_content, new Rect(0f, 0f, cellW, cellW + CAPTION_H), "",
                selected ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SelectPattern(value)));
            var cellGo = btn.gameObject;

            var imgGo = new GameObject("Preview");
            imgGo.transform.SetParent(cellGo.transform, false);
            var iRt = imgGo.AddComponent<RectTransform>();
            iRt.anchorMin = new Vector2(0f, 1f);
            iRt.anchorMax = Vector2.one;
            iRt.pivot = new Vector2(0.5f, 1f);
            iRt.offsetMin = new Vector2(2f, -(cellW - 2f));
            iRt.offsetMax = new Vector2(-2f, -2f);
            var raw = imgGo.AddComponent<RawImage>();
            raw.texture = thumb;
            raw.raycastTarget = false;

            var cap = UGUIShip.CreateLabel(cellGo.transform, new Rect(0f, 0f, cellW, CAPTION_H),
                caption, FS_SM - 2, WHITE, TextAnchor.MiddleCenter);
            cap.horizontalOverflow = HorizontalWrapMode.Overflow;
            var capRt = cap.rectTransform;
            capRt.anchorMin = capRt.anchorMax = capRt.pivot = new Vector2(0f, 0f);
            capRt.anchoredPosition = Vector2.zero;

            if (deletable)
            {
                const float delSize = 18f;
                var delBtn = UGUIShip.CreateButton(cellGo.transform, new Rect(0f, 0f, delSize, delSize),
                    "X", BTN_REMOVE, WHITE, FS_SM - 3, new Action(() => DeleteCustomPattern(value)));
                var dRt = delBtn.GetComponent<RectTransform>();
                dRt.anchorMin = dRt.anchorMax = dRt.pivot = new Vector2(1f, 1f);
                dRt.anchoredPosition = Vector2.zero;
            }
        }

        void BuildAddTile()
        {
            float cellW = CellW;
            UGUIShip.CreateButton(_content, new Rect(0f, 0f, cellW, cellW + CAPTION_H), "+",
                BTN_DARK, WHITE, FS_SM + 12, new Action(AddCustomPattern));
        }

        void SelectPattern(string value)
        {
            SettingsService.Set(ScreenBackgroundService.KeyPattern(Screen), value);
            if (string.IsNullOrEmpty(value) && Screen == ScreenBackgroundService.Screen.FallForce)
                MenuCustomizationApplication.Instance?.RestorePattern();
            Back();
        }

        void AddCustomPattern() => WinDialogs.PickPng("Select pattern PNG", path =>
        {
            if (string.IsNullOrEmpty(path)) return;
            ScreenBackgroundService.AddCustomPattern(path);
            SelectPattern(path);
        });

        void DeleteCustomPattern(string path)
        {
            ScreenBackgroundService.RemoveCustomPattern(path);
            _customThumbCache.Remove(path);
            RefreshGrid();
        }
    }
}
