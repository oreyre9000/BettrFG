using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Features.Replay;
using BetterFG.Services;
using BetterFG.Utilities;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    // the pictures taken out of the replay viewer. reached only from the Replays tab's link, so it
    // never sits in the slot dropdown.
    public class ReplayImagesTab : ReplayTab
    {
        public ReplayImagesTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Replays - Images";
        protected override string TitleId => "ui.replays_images";

        protected override string SwitchLabel => "ui.replays";
        protected override Tab MakeSwitchTarget() => BetterFGTabRegistry.NewTab<ReplaysTab>();

        const int PAGE_SIZE = 12;
        const int COLUMNS = 2;
        const float GRID_PAD = 2f;
        const float GRID_GAP = 6f;
        const float CAPTION_H = 26f;

        const string SP = "BetterFG.assets.ui.feature.qualificationtime.";
        static Sprite _delIdle, _delHover;
        static Sprite DelIdle => _delIdle ??= EmbeddedResourceandUnity.LoadSprite(SP + "featurequalificationtime_delete_idle.png");
        static Sprite DelHover => _delHover ??= EmbeddedResourceandUnity.LoadSprite(SP + "featurequalificationtime_delete.png");

        static readonly Color CELL_BG = new Color(0f, 0f, 0f, 0.35f);
        static readonly Color CELL_HOVER = new Color(1f, 1f, 1f, 0.16f);
        static readonly Color CELL_PRESS = new Color(1f, 1f, 1f, 0.24f);

        readonly List<string> _files = new List<string>();
        readonly List<(string path, RawImage raw)> _pending = new List<(string, RawImage)>();
        Coroutine _thumbRoutine;

        float CellW => (TabWidth - PAD * 2f - UGUIShip.SCROLLBAR_INSET * 2f - GRID_PAD * 2f - GRID_GAP * (COLUMNS - 1)) / COLUMNS;

        protected override float BuildHeader(RectTransform contentRoot, float y, float w)
        {
            float topH = BTN_H * 0.8f;
            float folderW = 86f;
            float refreshW = 24f;

            UGUIShip.CreateButton(contentRoot, new Rect(PAD, y, folderW, topH), "ui.open_folder",
                DARK, Color.white, FS_SM - 1, new Action(() => ReplayExport.Reveal(ReplayImages.Dir)));

            UGUIShip.CreateLabel(contentRoot,
                new Rect(PAD + folderW + PAD, y, w - folderW - refreshW - PAD * 2f, topH),
                "ui.hide_the_replay_ui_then_hit_the_picture_prompt", FS_SM - 1, HINT, TextAnchor.MiddleLeft);

            var refreshBtn = UGUIShip.CreateButton(contentRoot, new Rect(PAD + w - refreshW, y, refreshW, topH),
                "↻", DARK, Color.white, FS_SM, new Action(Refresh));
            var refreshTxt = refreshBtn.GetComponentInChildren<Text>();
            if (refreshTxt != null)
            {
                refreshTxt.fontSize = FS + 6;
                refreshTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
                refreshTxt.verticalOverflow = VerticalWrapMode.Overflow;
            }
            y += topH + SH;

            UGUIShip.CreateDivider(contentRoot, PAD, y, w);
            return y + 1f + SH;
        }

        protected override void BuildListLayout(RectTransform content)
        {
            float cellW = CellW;
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.padding = new RectOffset((int)GRID_PAD, (int)GRID_PAD, (int)GRID_PAD, (int)GRID_PAD);
            grid.spacing = new Vector2(GRID_GAP, GRID_GAP);
            grid.cellSize = new Vector2(cellW, cellW * 9f / 16f + CAPTION_H);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = COLUMNS;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        protected override void Refresh()
        {
            ReplayImages.Forget();
            _files.Clear();
            _files.AddRange(ReplayImages.ListFiles());
            Page = 0;
            RenderPage();
        }

        protected override void RenderPage()
        {
            if (ListContent == null) return;
            if (_thumbRoutine != null) { StopCoroutine(_thumbRoutine); _thumbRoutine = null; }
            _pending.Clear();

            ClearList();

            int total = _files.Count;
            int pageCount = Math.Max(1, (total + PAGE_SIZE - 1) / PAGE_SIZE);
            Page = Mathf.Clamp(Page, 0, pageCount - 1);
            int start = Page * PAGE_SIZE;
            int end = Math.Min(start + PAGE_SIZE, total);

            for (int i = start; i < end; i++) BuildCell(_files[i]);

            if (_pending.Count > 0) _thumbRoutine = StartCoroutine(LoadThumbs().WrapToIl2Cpp());

            ShowPaging(pageCount > 1);

            if (total == 0) SetStatus("ui.no_pictures_yet");
            else if (pageCount > 1) SetStatus(string.Format("{0} pictures  ·  page {1}/{2}", total, Page + 1, pageCount));
            else SetStatus(total + (total == 1 ? " picture" : " pictures"));
        }

        void BuildCell(string path)
        {
            float cellW = CellW;
            float shotH = cellW * 9f / 16f;

            var cellGo = new GameObject("Picture");
            cellGo.transform.SetParent(ListContent, false);
            cellGo.AddComponent<RectTransform>();

            var cellImg = cellGo.AddComponent<Image>();
            UGUIShip.ApplyDeluxColorFill(cellImg);
            cellImg.color = Color.white;
            var btn = cellGo.AddComponent<Button>();
            var cols = btn.colors;
            cols.normalColor = CELL_BG;
            cols.highlightedColor = CELL_HOVER;
            cols.pressedColor = CELL_PRESS;
            cols.selectedColor = CELL_BG;
            cols.fadeDuration = 0f;
            btn.colors = cols;
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;
            btn.onClick.AddListener(new Action(() =>
            {
                AudioService.PlayButtonClick();
                ReplayImages.Open(path);
            }));

            var maskGo = new GameObject("Shot");
            maskGo.transform.SetParent(cellGo.transform, false);
            var mRt = maskGo.AddComponent<RectTransform>();
            mRt.anchorMin = new Vector2(0f, 1f);
            mRt.anchorMax = new Vector2(1f, 1f);
            mRt.pivot = new Vector2(0.5f, 1f);
            mRt.offsetMin = new Vector2(0f, -shotH);
            mRt.offsetMax = Vector2.zero;

            var (_, inner) = UGUIShip.CreateFramedImage(maskGo.transform, name: "Frame", ppuMult: 2f);
            var raw = inner.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            _pending.Add((path, raw));

            var meta = ReplayImages.ReadMeta(path);
            var lvl = UGUIShip.CreateLabel(cellGo.transform, new Rect(0f, 0f, cellW - 24f, 13f),
                meta.Label, FS_SM - 1, meta.isUgc ? UGC_COL : Color.white, TextAnchor.MiddleLeft);
            lvl.verticalOverflow = VerticalWrapMode.Truncate;
            var lvlRt = lvl.rectTransform;
            lvlRt.anchorMin = lvlRt.anchorMax = lvlRt.pivot = new Vector2(0f, 0f);
            lvlRt.anchoredPosition = new Vector2(3f, 12f);

            var stamp = File.GetLastWriteTime(path);
            var cap = UGUIShip.CreateLabel(cellGo.transform, new Rect(0f, 0f, cellW - 24f, 12f),
                stamp.ToString("g", System.Globalization.CultureInfo.CurrentCulture), FS_SM - 2, DIM, TextAnchor.MiddleLeft);
            cap.horizontalOverflow = HorizontalWrapMode.Overflow;
            var capRt = cap.rectTransform;
            capRt.anchorMin = capRt.anchorMax = capRt.pivot = new Vector2(0f, 0f);
            capRt.anchoredPosition = new Vector2(3f, 0f);

            var (_, delImg) = UGUIShip.CreateSpriteButton(cellGo.transform, new Rect(0f, 0f, 18f, 18f),
                DelIdle, DelHover, new Action(() =>
                {
                    if (ReplayImages.Delete(path)) Refresh();
                }));
            var dRt = delImg.GetComponent<RectTransform>();
            dRt.anchorMin = dRt.anchorMax = dRt.pivot = new Vector2(1f, 0f);
            dRt.anchoredPosition = new Vector2(-2f, 0f);
        }

        IEnumerator LoadThumbs()
        {
            var batch = new List<(string path, RawImage raw)>(_pending);
            _pending.Clear();

            foreach (var (path, raw) in batch)
            {
                if (raw == null) continue;
                var tex = ReplayImages.Thumb(path);
                if (tex != null && raw != null) raw.texture = tex;
                yield return null;
            }
            _thumbRoutine = null;
        }
    }
}
