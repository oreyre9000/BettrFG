using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Features.CreativeExpansion;
using BetterFG.Features.Replay;
using BetterFG.Features.UnityRound.Editor;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Windows.Creative
{
    public class PublishThumbnailWindow : BetterFGWindow
    {
        public PublishThumbnailWindow(IntPtr ptr) : base(ptr) { }

        public static PublishThumbnailWindow Instance { get; private set; }

        public static bool StepActive => Instance != null;

        protected override float WindowWidth => 310f;
        protected override float WindowHeight => 340f;
        protected override string WindowTitle => "ui.level_thumbnail";
        protected override string BgResourceName => "BetterFG.assets.ui.windows.generalbg_2.png";
        protected override string BgHoverResourceName => "BetterFG.assets.ui.windows.generalbg_2_hover.png";
        protected override bool DraggableFromTitle => true;

        protected override Vector3 InitialBgPosition => new Vector3(184f, 18.5f, 0f);
        protected override Vector3 InitialBgScale => new Vector3(1.41f, 1.6f, 1f);

        private static readonly Color BTN_APPLY = new Color(0.25f, 0.5f, 0.25f, 1f);
        private static readonly Color HINT_COL = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color STEP_COL = new Color(0.55f, 0.75f, 1f, 0.9f);
        private static readonly Color CELL_BG = new Color(1f, 1f, 1f, 0.07f);

        private const int COLUMNS = 2;
        private const float GAP = 6f;

        private static readonly string[] GAME_UI_ROOTS =
        {
            "UICanvas_Client_V2(Clone)/Default",
            "UICanvas_Client_V2(Clone)/Popup",
            "UICanvas_Client_V2(Clone)/Overlay",
            "Prefab_UI_NavigationOverlay(Clone)",
            "NavigationHintUI/Prime_UI_LE_HUDMessageManager",
        };

        private readonly List<CanvasGroup> _dimmed = new List<CanvasGroup>();

        private LevelEditorPublishPopupViewModel _vm;
        private Texture _gameImage;
        private bool _gameHasImage;
        private bool _gameAskThumb, _gameUploadThumb;
        private string _shareCode;

        private Coroutine _thumbRoutine;
        private Texture2D _previewTex;
        private readonly List<string> _files = new List<string>();
        private readonly List<(string path, RawImage raw)> _cells = new List<(string, RawImage)>();

        // ── api ───────────────────────────────────────────────────────────────

        public static void Open(LevelEditorPublishPopupViewModel vm)
        {
            // the popup fades in more than once across the publish flow — same popup, same window
            if (Instance != null && Instance._vm == vm) return;
            Instance?.Close();

            var go = new GameObject("BetterFG_PublishThumbnailWindow");
            go.AddComponent<PublishThumbnailWindow>().Configure(vm);
        }

        public void Configure(LevelEditorPublishPopupViewModel vm)
        {
            Instance = this;
            _vm = vm;
            _gameImage = vm.LevelImage;
            _gameHasImage = vm.HasImage;
            _gameAskThumb = vm._shouldAskThumbnailUpload;
            _gameUploadThumb = vm._shouldUploadThumbnailDirectly;

            // a fresh popup is a fresh decision
            PublishThumbnail.Armed = false;
            PublishThumbnail.Clear();

            _shareCode = CreativeRoundMemory.GetCurrentShareCode();
            _files.Clear();
            _files.AddRange(PublishThumbnail.PicturesFor(_shareCode));
            Plugin.Log.LogInfo($"publish popup for {_shareCode ?? "an unpublished level"}, {_files.Count} picture(s) of it to offer");

            SetAnchorPosition(new Vector2(
                UIScaleService.CurrentRef.x * 0.5f - (WindowWidth * 0.5f + InitialBgPosition.x),
                -InitialBgPosition.y));
            DimGameUi();
            ShowWindow();
            RebuildContent();
        }

        protected override bool ShowCloseButton => false;

        public override void Close()
        {
            if (Instance == this) Instance = null;
            RestoreGameUi();
            base.Close();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            RestoreGameUi();
        }

        protected override void ManagedUpdate()
        {
            base.ManagedUpdate();
            // popup's gone, so are we. the pick survives — closing this isn't cancelling it.
            if (_vm == null) { Close(); return; }
            DimGameUi();
        }

        private void DimGameUi()
        {
            if (_dimmed.Count == 0)
                foreach (string path in GAME_UI_ROOTS)
                {
                    var go = GameObject.Find(path);
                    if (go == null) continue;
                    _dimmed.Add(go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>());
                }

            foreach (var cg in _dimmed)
            {
                if (cg == null || cg.alpha == 0f) continue;
                cg.alpha = 0f;
                cg.interactable = false;
                cg.blocksRaycasts = false;
            }
        }

        private void RestoreGameUi()
        {
            foreach (var cg in _dimmed)
            {
                if (cg == null) continue;
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }
            _dimmed.Clear();
        }

        // ── content ───────────────────────────────────────────────────────────

        protected override void BuildContent(RectTransform contentRoot)
        {
            ContentPosition = new Vector3(190.6421f, 4.4f, 0f);
            ContentScale = new Vector3(1.0473f, 1f, 1f);
            Pivot = new Vector2(0f, 0.5f);
            TitlePosition = new Vector3(32.5674f, -1f, 0f);
            TitleScale = new Vector3(1.1818f, 1.3491f, 1f);

            if (_thumbRoutine != null) { StopCoroutine(_thumbRoutine); _thumbRoutine = null; }
            _cells.Clear();

            float w = WindowWidth - PAD * 2f;
            float y = PAD * 0.5f;

            MakeLabel(contentRoot, new Rect(PAD, y, w, 14f), "ui.the_picture_on_your_level_s_tile_in_game", FS_SM, HINT_COL);
            y += 17f;
            MakeSeparator(contentRoot, new Rect(PAD, y, w, 1f));
            y += 6f;

            float footY = WindowHeight - TITLE_H - 26f;
            if (_files.Count == 0) BuildEmpty(contentRoot, w, y);
            else BuildGrid(contentRoot, w, y, footY - 6f - y);

            UGUIShip.CreateButton(contentRoot, new Rect(PAD, footY, w, 24f),
                "ui.use_the_editor_s_own_shot", BTN_APPLY, WHITE, FS_SM, new Action(UseDefault));
        }

        // nothing to show is the case that actually needs explaining — the pictures come from a corner
        // of the mod nobody would connect to publishing on their own
        private void BuildEmpty(RectTransform root, float w, float y)
        {
            MakeLabel(root, new Rect(PAD, y, w, 14f), "ui.no_pictures_of_this_level_yet", FS_SM, STEP_COL);
            y += 19f;

            MakeLabel(root, new Rect(PAD, y, w, 30f),
                "only shots taken in a replay of " + (_shareCode ?? "this level") + " can go on its tile.",
                FS_SM, HINT_COL, TextAnchor.UpperLeft);
            y += 34f;

            MakeLabel(root, new Rect(PAD, y, w, 14f), "ui.how_to_take_one", FS_SM, STEP_COL);
            y += 19f;

            MakeLabel(root, new Rect(PAD, y, w, 28f), "ui.1_turn_auto_record_rounds_on_in_the_replays_tab",
                FS_SM, WHITE, TextAnchor.UpperLeft);
            y += 30f;
            MakeLabel(root, new Rect(PAD, y, w, 28f), "ui.2_play_this_level_then_open_its_replay",
                FS_SM, WHITE, TextAnchor.UpperLeft);
            y += 30f;
            MakeLabel(root, new Rect(PAD, y, w, 28f), "ui.3_fly_the_camera_to_the_shot_you_want_then_hide",
                FS_SM, WHITE, TextAnchor.UpperLeft);
            y += 30f;
            MakeLabel(root, new Rect(PAD, y, w, 28f), "ui.4_press_take_picture_top_right",
                FS_SM, WHITE, TextAnchor.UpperLeft);
            y += 32f;

            MakeLabel(root, new Rect(PAD, y, w, 28f), "ui.they_collect_in_replays_images_and_turn_up_here",
                FS_SM, HINT_COL, TextAnchor.UpperLeft);
        }

        private void BuildGrid(RectTransform root, float w, float y, float height)
        {
            MakeLabel(root, new Rect(PAD, y, w, 14f),
                _files.Count + (_files.Count == 1 ? " picture" : " pictures") + " from your replays of " + _shareCode,
                FS_SM, STEP_COL);
            y += 18f;

            var (_, content) = UGUIShip.CreateScrollView(root, new Rect(PAD, y, w, height - 18f));

            float cellW = (w - UGUIShip.SCROLLBAR_INSET * 2f - GAP * (COLUMNS - 1)) / COLUMNS;
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.spacing = new Vector2(GAP, GAP);
            grid.cellSize = new Vector2(cellW, cellW * 9f / 16f + 14f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = COLUMNS;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            foreach (string path in _files) BuildCell(content, path, cellW);
            _thumbRoutine = StartCoroutine(LoadThumbs().WrapToIl2Cpp());
        }

        private void BuildCell(RectTransform content, string path, float cellW)
        {
            float shotH = cellW * 9f / 16f;

            var cellGo = new GameObject("Picture");
            cellGo.transform.SetParent(content, false);
            cellGo.AddComponent<RectTransform>();

            var cellImg = cellGo.AddComponent<Image>();
            cellImg.color = CELL_BG;

            var btn = cellGo.AddComponent<Button>();
            var cols = btn.colors;
            cols.normalColor = Color.white;
            cols.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
            cols.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            cols.fadeDuration = 0f;
            btn.colors = cols;
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;
            btn.onClick.AddListener(new Action(() =>
            {
                AudioService.PlayButtonClick();
                Pick(path);
            }));

            var maskGo = new GameObject("Shot");
            maskGo.transform.SetParent(cellGo.transform, false);
            var mRt = maskGo.AddComponent<RectTransform>();
            mRt.anchorMin = new Vector2(0f, 1f);
            mRt.anchorMax = new Vector2(1f, 1f);
            mRt.pivot = new Vector2(0.5f, 1f);
            mRt.offsetMin = new Vector2(2f, -shotH - 2f);
            mRt.offsetMax = new Vector2(-2f, -2f);
            maskGo.AddComponent<RectMask2D>();

            var imgGo = new GameObject("Img");
            imgGo.transform.SetParent(maskGo.transform, false);
            var iRt = imgGo.AddComponent<RectTransform>();
            iRt.anchorMin = Vector2.zero;
            iRt.anchorMax = Vector2.one;
            iRt.offsetMin = iRt.offsetMax = Vector2.zero;
            var raw = imgGo.AddComponent<RawImage>();
            raw.raycastTarget = false;
            _cells.Add((path, raw));

            var stamp = MakeLabel(cellGo.transform, new Rect(0f, 0f, cellW - 6f, 12f),
                File.GetLastWriteTime(path).ToString("g", System.Globalization.CultureInfo.CurrentCulture),
                FS_SM - 2, HINT_COL);
            var sRt = stamp.rectTransform;
            sRt.anchorMin = sRt.anchorMax = sRt.pivot = Vector2.zero;
            sRt.anchoredPosition = new Vector2(4f, 1f);
        }

        // full-res pngs, so decode them a frame apart rather than hitching the publish screen
        private IEnumerator LoadThumbs()
        {
            foreach (var (path, raw) in _cells)
            {
                var tex = ReplayImages.Thumb(path);
                if (tex != null && raw != null) raw.texture = tex;
                yield return null;
            }
            _thumbRoutine = null;
        }

        // ── picking ───────────────────────────────────────────────────────────

        private void Pick(string path)
        {
            PublishThumbnail.Choose(path);

            // the popup's own preview follows the pick, so you see what you're about to publish. it
            // gets its OWN copy — the popup releases whatever texture it's handed the moment the next
            // one arrives, and the first version of this passed it the grid's cached thumbnail, so
            // switching back to the editor's shot wiped the picture out of the grid too.
            if (_previewTex != null) Destroy(_previewTex);
            _previewTex = PublishThumbnail.EditorSizedCopy();
            _vm.LevelImage = _previewTex;
            _vm.HasImage = true;

            // "would you like to update this level's preview image?" decides whether the thumbnail is
            // uploaded at all — a no there would leave your pick sat in the level payload and nowhere
            // else. picking one IS the answer, so take the question off the table.
            _vm._shouldAskThumbnailUpload = false;
            _vm._shouldUploadThumbnailDirectly = true;

            Close();
        }

        private void UseDefault()
        {
            PublishThumbnail.Clear();
            // the popup may already have released its original shot by now — hand back an empty
            // preview rather than a dead texture. the publish itself recaptures either way.
            bool alive = _gameImage != null;
            _vm.LevelImage = alive ? _gameImage : null;
            _vm.HasImage = _gameHasImage && alive;
            _vm._shouldAskThumbnailUpload = _gameAskThumb;
            _vm._shouldUploadThumbnailDirectly = _gameUploadThumb;
            Close();
        }
    }
}
