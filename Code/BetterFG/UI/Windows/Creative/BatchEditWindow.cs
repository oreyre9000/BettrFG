using System;
using System.Collections.Generic;
using BetterFG.Features.CreativeExpansion;
using BetterFG.Features.CreativeIncrements;
using BetterFG.Features.Replay;
using BetterFG.Services;
using FallGuysLib.UI;
using FG.Common.CMS;
using FGClient.UI;
using LevelEditor;
using BetterFG.Utilities;
using UnityEngine;
using UnityEngine.UI;
using Groups = BetterFG.Features.CreativeExpansion.CreativeGroups;
using BettrFG.uGUI;

namespace BetterFG.UI.Windows.Creative
{
    // Batch-edit window for a multi-object level-editor selection. A carousel header ( ‹ Style › ) cycles
    // between three subtabs — Recolour, Scale, Material — each rebuilding the body below. Every op records
    // an undo entry (BatchEditHistory); the Undo button at the bottom reverts our edits only (Fall Guys'
    // own undo doesn't see them).
    //
    // Opened by CreativeSelectionWatcher's nav prompt. AnyOpen lets ControllerManager drive the cursor
    // while we're up (so the stick + A work on our sliders/buttons) without any polling here; it clears
    // on close so the game gets its cursor back.
    public class BatchEditWindow : BetterFGWindow
    {
        public BatchEditWindow(IntPtr ptr) : base(ptr) { }

        public static BatchEditWindow Instance { get; private set; }
        public static bool AnyOpen { get; private set; }

        // on/off toggle for the whole batch-edit feature (the nav prompt + window). default on.
        private const string ENABLED_KEY = "creative.batchedit.enabled";
        public static bool FeatureEnabled
        {
            get => Services.SettingsService.Get(ENABLED_KEY, "true") != "false";
            set => Services.SettingsService.Set(ENABLED_KEY, value ? "true" : "false");
        }

        protected override float WindowWidth => 310f;
        protected override float WindowHeight => 340f;
        protected override string WindowTitle => LibraryOnly ? "Groups" : "Batch Edit";
        protected override string BgResourceName => "BetterFG.assets.ui.windows.generalbg_2.png";
        protected override string BgHoverResourceName => "BetterFG.assets.ui.windows.generalbg_2_hover.png";
        protected override bool DraggableFromTitle => true;

        protected override Vector3 InitialBgPosition => new Vector3(184f, 18.5f, 0f);
        protected override Vector3 InitialBgScale => new Vector3(1.41f, 1.6f, 1f);

        private static readonly Color BTN_STEP = new Color(0.22f, 0.34f, 0.55f, 1f);
        private static readonly Color BTN_APPLY = new Color(0.25f, 0.5f, 0.25f, 1f);
        private static readonly Color BTN_UNDO = new Color(0.45f, 0.35f, 0.2f, 1f);
        private static readonly Color BTN_ARROW = new Color(0.28f, 0.28f, 0.34f, 1f);
        private static Sprite _openFolderIcon;
        private static Sprite OpenFolderIcon => _openFolderIcon ??= EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.button.openfolder.png");
        private static readonly Color HINT_COL = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color OK_COL = new Color(0.55f, 0.85f, 0.55f, 1f);
        private static readonly Color STEP_COL = new Color(0.55f, 0.75f, 1f, 0.9f);

        private static readonly string[] BUILTIN_SUBTABS = { "Recolour", "Scale", "Material", "Physics", "Link", "Group", "Saved" };
        // built-ins first, then whatever external DLLs registered (usually none). rebuilt each open so a
        // plugin that registers after the window's first build still shows up next time it opens.
        private string[] Subtabs()
        {
            var extras = BatchSubtabRegistry.Extras;
            if (extras.Count == 0) return BUILTIN_SUBTABS;
            var all = new string[BUILTIN_SUBTABS.Length + extras.Count];
            Array.Copy(BUILTIN_SUBTABS, all, BUILTIN_SUBTABS.Length);
            for (int i = 0; i < extras.Count; i++) all[BUILTIN_SUBTABS.Length + i] = extras[i].Name;
            return all;
        }
        private int _subtab;
        private BatchSubtab _activeExtra; // the registered extra currently shown, so we can fire its OnHide

        // recolour state
        private enum RecolourMode { SetColour, Modify }
        private RecolourMode _recolourMode = RecolourMode.SetColour;
        private Color _colour = new Color(1f, 0.4f, 0.2f, 1f);
        private float _modBright, _modContrast, _modHue, _modSat; // modify-mode sliders, 0 = no change
        private Image _preview;
        private Text _recolourModeLabel;
        // live preview session: originals snapshotted once, re-applied from every slider move, pushed as
        // ONE undo entry on commit (apply / subtab / mode switch / window close / selection change).
        private readonly Dictionary<LevelEditorPlaceableObject, Color> _colourOriginals
            = new Dictionary<LevelEditorPlaceableObject, Color>();
        private BatchEditHistory.BatchEntry _colourEntry;
        private int _colourSessionSelCount; // selection count when the session opened — commit if it changes

        private const float FACTOR_LIMIT = 1000f;
        private readonly float[] _factors = { 1f, 1f, 1f };
        private readonly float[] _committedFactors = { 1f, 1f, 1f };
        private static ScaleMode _scaleMode = ScaleMode.Individual;
        private readonly InputField[] _valFields = new InputField[3];
        private Text _modeLabel;
        private readonly UGUIShip.HoldButtonState[] _scaleHold = new UGUIShip.HoldButtonState[6]; // -/+ per axis
        // one undo entry per hold session, not one per nudge tick — opened on the first nudge since
        // the last release, pushed when the button is released.
        private BatchEditHistory.BatchEntry _scaleEntry;
        private CanvasGroup _scaleRowsGroup; // fades/blocks the X/Y/Z rows when FromSelected has no pivot

        private int _weightIndex;

        private int _groupPick;
        private InputField _groupNameField;

        private string _savedName = "";

        private Text _countLabel;
        private Text _statusLabel;

        // ── api ───────────────────────────────────────────────────────────────

        public static bool LibraryOnly { get; private set; }
        private const int SAVED_SUBTAB = 6;

        private static bool Solo => LibraryOnly;

        public static void OpenGroupsTool()
        {
            if (!Groups.Enabled) return;
            if (Instance != null)
            {
                if (LibraryOnly) return;
                Instance.Close();
            }
            LibraryOnly = true;
            Patches.BatchEditBlockPlacePatch.BlockedAPlace = false;
            var go = new GameObject("BetterFG_GroupsWindow");
            go.AddComponent<BatchEditWindow>().Configure();
        }

        public void Configure()
        {
            Instance = this;
            AnyOpen = true;
            if (LibraryOnly) _subtab = SAVED_SUBTAB;
            // selected a controller? that's what you came here for — land on Link, not Recolour
            else if (BatchLink.Controller() != null) _subtab = 4;
            SetAnchorPosition(new Vector2(560f, 30f));
            ShowWindow();
            RebuildContent();
        }

        protected override bool ShowCloseButton => true;

        public override void Close()
        {
            CommitPending(); // don't lose a pending recolour/scale on close
            BatchScale.BakeOwnerScale(); // hand the editor's multiselect owner back unscaled
            if (Instance == this) Instance = null;
            AnyOpen = false;
            LibraryOnly = false;
            // commit the selection ourselves a frame after close (AnyOpen is false by then, so our own
            // block prefix lets it through) — saves you clicking off the backlog of blocked place attempts.
            CreativeSelectionWatcher.Instance?.PlaceAfterFrame();
            base.Close();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            AnyOpen = false;
            LibraryOnly = false;
            // window closing — let the shown extra tear down its world overlay.
            if (_activeExtra != null)
            {
                try { _activeExtra.OnHide?.Invoke(); } catch (Exception ex) { Plugin.Log.LogError($"subtab OnHide threw: {ex}"); }
                _activeExtra = null;
            }
        }

        protected override void ManagedUpdate()
        {
            base.ManagedUpdate();
            // keep the mouse usable while we're open — the editor otherwise locks+hides it
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            float dt = Time.unscaledDeltaTime;
            foreach (var h in _scaleHold) h?.Tick(dt);

            if (_subtab == 1) UpdateScaleRowsDim(); // pivot can appear/vanish live in "from selected"

            if (Solo) return; // nothing selected to watch, the close button is the only way out
            int sel = BatchRecolour.SelectionCount();
            if (sel == 0) { Close(); return; }
            // selection changed mid-edit → checkpoint the pending recolour (its snapshot is now stale)
            if (_colourEntry != null && sel != _colourSessionSelCount) CommitColourEntry();
        }

        // close button in the title bar — the nav-prompt can't reopen/close while our input lock is up,
        // so the window needs its own way out (clickable by mouse or controller cursor).
        // ── content ───────────────────────────────────────────────────────────

        protected override void BuildContent(RectTransform contentRoot)
        {
            ContentPosition = new Vector3(190.6421f, 4.4f, 0f);
            ContentScale = new Vector3(1.0473f, 1f, 1f);
            Pivot = new Vector2(0f, 0.5f);
            TitlePosition = new Vector3(32.5674f, -1f, 0f);
            TitleScale = new Vector3(1.1818f, 1.3491f, 1f);

            float w = WindowWidth - PAD * 2f;
            float y = PAD * 0.5f;

            var subtabs = Subtabs();
            if (_subtab >= subtabs.Length) _subtab = 0; // a registered extra vanished between opens

            // fire OnHide on the extra we're leaving so its world overlay doesn't linger after a switch.
            var extras = BatchSubtabRegistry.Extras;
            int extraIdx = _subtab - BUILTIN_SUBTABS.Length;
            var nowExtra = (extraIdx >= 0 && extraIdx < extras.Count) ? extras[extraIdx] : null;
            if (_activeExtra != null && _activeExtra != nowExtra)
            {
                try { _activeExtra.OnHide?.Invoke(); } catch (Exception ex) { Plugin.Log.LogError($"subtab OnHide threw: {ex}"); }
            }
            _activeExtra = nowExtra;

            // ── carousel header: ‹  Style  › ──
            if (Solo)
            {
                MakeLabel(contentRoot, new Rect(PAD, y, w, 22f), subtabs[_subtab], FS_BODY, WHITE, TextAnchor.MiddleCenter);
                y += 26f;
            }
            else
            {
                UGUIShip.CreateCarousel(contentRoot, new Rect(PAD, y, w, 22f), subtabs, _subtab,
                    d => { CommitColourEntry(); _subtab = (_subtab + d + subtabs.Length) % subtabs.Length; RebuildContent(); },
                    BTN_ARROW, FS_BODY);
                y += 26f;

                _countLabel = MakeLabel(contentRoot, new Rect(PAD, y, w - 24f, 14f),
                    CountText(), FS_SM, new Color(1f, 1f, 1f, 0.72f));
                y += 18f;
            }

            MakeSeparator(contentRoot, new Rect(PAD, y, w, 1f));
            y += 6f;

            switch (_subtab)
            {
                case 0: BuildRecolour(contentRoot, w, ref y); break;
                case 1: BuildScale(contentRoot, w, ref y); break;
                case 2: BuildMaterial(contentRoot, w, ref y); break;
                case 3: BuildPhysics(contentRoot, w, ref y); break;
                case 4: BuildLink(contentRoot, w, ref y); break;
                case 5: BuildGroup(contentRoot, w, ref y); break;
                case 6: BuildSaved(contentRoot, w, ref y); break;
                default: BuildExtra(contentRoot, w, ref y); break;
            }

            // ── footer: undo + redo (left) + status (right) ──
            float footY = WindowHeight - TITLE_H - 24f;
            UGUIShip.CreateButton(contentRoot, new Rect(PAD, footY, 64f, 20f),
                $"UNDO ({BatchEditHistory.Count})", BTN_UNDO, WHITE, FS_SM, new Action(DoUndo));
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + 68f, footY, 64f, 20f),
                $"REDO ({BatchEditHistory.RedoCount})", BTN_UNDO, WHITE, FS_SM, new Action(DoRedo));
            _statusLabel = MakeLabel(contentRoot, new Rect(PAD + 138f, footY, w - 138f, 20f), "", FS_SM, HINT_COL);
        }

        // ── recolour subtab ──────────────────────────────────────────────────

        private void BuildRecolour(RectTransform root, float w, ref float y)
        {
            // mode carousel:  ‹ set to colour / modify ›
            float arrow = 20f;
            MakeLabel(root, new Rect(PAD, y, 36f, 20f), "ui.mode_2", FS_SM, HINT_COL);
            UGUIShip.CreateButton(root, new Rect(PAD + 40f, y, arrow, 20f), "‹", BTN_ARROW, WHITE, FS_BODY,
                new Action(() => CycleRecolourMode(-1)));
            _recolourModeLabel = MakeLabel(root, new Rect(PAD + 40f + arrow, y, w - 40f - arrow * 2f - 40f, 20f),
                RecolourModeName(_recolourMode), FS_SM, WHITE, TextAnchor.MiddleCenter);
            UGUIShip.CreateButton(root, new Rect(PAD + w - 40f - arrow, y, arrow, 20f), "›", BTN_ARROW, WHITE, FS_BODY,
                new Action(() => CycleRecolourMode(+1)));
            y += 26f;

            if (_recolourMode == RecolourMode.SetColour) BuildRecolourSet(root, w, ref y);
            else BuildRecolourModify(root, w, ref y);

            y += 4f;
            UGUIShip.CreateButton(root, new Rect(PAD, y, w, 24f),
                "ui.apply_2", BTN_APPLY, WHITE, FS_SM, new Action(ApplyColour));
        }

        // "set to colour" — RGB sliders, live preview onto the whole selection.
        private void BuildRecolourSet(RectTransform root, float w, ref float y)
        {
            var pvGo = new GameObject("Preview");
            pvGo.transform.SetParent(root, false);
            UGUIShip.SetPixelRect(pvGo.AddComponent<RectTransform>(), new Rect(w - 20f + PAD, y, 20f, 20f));
            _preview = pvGo.AddComponent<Image>();
            _preview.color = _colour;

            var suppress = new bool[1];   // shared flag so slider↔hex don't fight (same pattern as CreateColorControls)
            InputField hex = null;
            void RefreshHex()
            {
                if (hex == null) return;
                suppress[0] = true;
                UGUIShip.SetInputText(hex, "#" + UGUIShip.ColorToHex(_colour.r, _colour.g, _colour.b));
                suppress[0] = false;
            }

            var sR = UGUIShip.CreateSlider(root, PAD, y, w - 26f, "R", _colour.r, 16f, 4f, FS_SM,
                new Action<float>(v => { if (suppress[0]) return; _colour.r = v; PreviewColourSet(); RefreshHex(); }),
                new Color(1f, 0.4f, 0.4f), new Color(1f, 0.3f, 0.3f), true, 1f);
            y += 22f;
            var sG = UGUIShip.CreateSlider(root, PAD, y, w, "G", _colour.g, 16f, 4f, FS_SM,
                new Action<float>(v => { if (suppress[0]) return; _colour.g = v; PreviewColourSet(); RefreshHex(); }),
                new Color(0.4f, 1f, 0.4f), new Color(0.3f, 1f, 0.3f), true, 0.4f);
            y += 22f;
            var sB = UGUIShip.CreateSlider(root, PAD, y, w, "B", _colour.b, 16f, 4f, FS_SM,
                new Action<float>(v => { if (suppress[0]) return; _colour.b = v; PreviewColourSet(); RefreshHex(); }),
                new Color(0.4f, 0.6f, 1f), new Color(0.3f, 0.5f, 1f), true, 0.2f);
            y += 26f;

            float lblW = FS_SM * 2.4f;
            float fieldW = FS_SM * 7f;
            UGUIShip.CreateLabel(root, new Rect(PAD, y, lblW, 16f), "ui.hex", FS_SM, new Color(1f, 1f, 1f, 0.35f));
            hex = UGUIShip.CreateInputField(root, new Rect(PAD + lblW, y, fieldW, 16f), "ui.rrggbb", null, null, FS_SM);
            hex.characterLimit = 7;
            hex.onEndEdit.AddListener(new Action<string>(txt =>
            {
                if (suppress[0]) return;
                if (!UGUIShip.HexToColor(txt, out float r, out float g, out float b)) { RefreshHex(); return; }
                _colour.r = r; _colour.g = g; _colour.b = b;
                suppress[0] = true;
                if (sR != null) sR.value = r;
                if (sG != null) sG.value = g;
                if (sB != null) sB.value = b;
                suppress[0] = false;
                PreviewColourSet();
            }));
            RefreshHex();
            y += 24f;

            // the creative colour picker's own custom-colour slots, newest first, exactly as its grid
            // shows them. re-read on every build so a colour picked since we opened shows up.
            var recents = LevelEditorColourPaletteSettings.GetFavouriteCustomColoursHexCodes();
            if (recents == null) return;
            float sx = PAD;
            for (int i = 0, shown = 0; i < recents.Length && shown < 5; i++)
            {
                if (!UGUIShip.HexToColor(recents[i], out float cr, out float cg, out float cb)) continue;
                var c = new Color(cr, cg, cb, 1f);
                UGUIShip.CreateButton(root, new Rect(sx, y, 20f, 20f), "", c, WHITE, FS_SM,
                    new Action(() =>
                    {
                        _colour.r = c.r; _colour.g = c.g; _colour.b = c.b;
                        suppress[0] = true;
                        if (sR != null) sR.value = c.r;
                        if (sG != null) sG.value = c.g;
                        if (sB != null) sB.value = c.b;
                        suppress[0] = false;
                        RefreshHex();
                        PreviewColourSet();
                    }), customSprite: false);
                sx += 24f;
                shown++;
            }
            y += 24f;
        }

        // "modify" — brightness / contrast / hue / saturation adjust each object's OWN colour. sliders
        // are 0..1: signed params map 0.5→0 (no change), hue maps 0..1→0..360°.
        private void BuildRecolourModify(RectTransform root, float w, ref float y)
        {
            _preview = null; // no flat-colour preview chip in modify mode
            UGUIShip.CreateSlider(root, PAD, y, w, "Bright", _modBright * 0.5f + 0.5f, 16f, 4f, FS_SM,
                new Action<float>(v => { _modBright = (v - 0.5f) * 2f; PreviewColourModify(); }),
                new Color(0.9f, 0.9f, 0.6f), new Color(0.8f, 0.8f, 0.5f), true, 0.5f);
            y += 22f;
            UGUIShip.CreateSlider(root, PAD, y, w, "Contr", _modContrast * 0.5f + 0.5f, 16f, 4f, FS_SM,
                new Action<float>(v => { _modContrast = (v - 0.5f) * 2f; PreviewColourModify(); }),
                new Color(0.7f, 0.7f, 0.7f), new Color(0.6f, 0.6f, 0.6f), true, 0.5f);
            y += 22f;
            UGUIShip.CreateSlider(root, PAD, y, w, "Hue", _modHue / 360f, 16f, 4f, FS_SM,
                new Action<float>(v => { _modHue = v * 360f; PreviewColourModify(); }),
                new Color(0.8f, 0.5f, 0.9f), new Color(0.7f, 0.4f, 0.8f), true, 0f);
            y += 22f;
            UGUIShip.CreateSlider(root, PAD, y, w, "Sat", _modSat * 0.5f + 0.5f, 16f, 4f, FS_SM,
                new Action<float>(v => { _modSat = (v - 0.5f) * 2f; PreviewColourModify(); }),
                new Color(0.5f, 0.9f, 0.7f), new Color(0.4f, 0.8f, 0.6f), true, 0.5f);
            y += 26f;
        }

        // ── scale subtab ─────────────────────────────────────────────────────

        private void BuildScale(RectTransform root, float w, ref float y)
        {
            // the X/Y/Z nudge rows go in their own container with a CanvasGroup, so "from selected"
            // with no pivot yet can fade + disable them (scaling would do nothing meaningful) while
            // the mode carousel below stays live so you can still switch modes.
            var rowsGo = new GameObject("ScaleRows");
            rowsGo.transform.SetParent(root, false);
            var rowsRt = rowsGo.AddComponent<RectTransform>();
            rowsRt.anchorMin = Vector2.zero; rowsRt.anchorMax = Vector2.one;
            rowsRt.offsetMin = Vector2.zero; rowsRt.offsetMax = Vector2.zero;
            _scaleRowsGroup = rowsGo.AddComponent<CanvasGroup>();
            var rows = rowsRt;

            // one row per axis:  X   [-] 1.00 [+]   — starts at 1 (no change, same as the multiselect
            // owner's own localScale); +/- add or subtract directly, so it shrinks as easily as it grows.
            // the value is a typable field, for the jump the step buttons would take all day to reach.
            string[] axis = { "X", "Y", "Z" };
            float step = CreativeIncrements.Enabled ? CreativeIncrements.Step : 0.25f;
            float repeat = CreativeIncrements.Enabled ? CreativeIncrements.Speed : 0.05f;
            var holds = new UGUIShip.HoldButtonState[2];
            for (int i = 0; i < 3; i++)
            {
                int a = i;
                MakeLabel(rows, new Rect(PAD, y, 16f, 20f), axis[i], FS_SM, WHITE);
                _valFields[a] = UGUIShip.CreateIncrement(rows, new Rect(PAD + 20f, y, w - 20f, 20f),
                    -FACTOR_LIMIT, FACTOR_LIMIT, () => _factors[a], v => _factors[a] = v, step,
                    isFloat: true, wrap: false, fontSize: FS_SM, fmt: FactorText,
                    onChange: _ => ApplyScale(), holds: holds);
                for (int h = 0; h < 2; h++)
                {
                    _scaleHold[a * 2 + h] = holds[h];
                    holds[h].RepeatInterval = repeat;
                    holds[h].OnRelease = CommitScaleEntry;
                }
                y += 24f;
            }
            UpdateScaleRowsDim();

            y += 2f;
            // mode carousel:  ‹ mode ›
            float arrow = 20f;
            MakeLabel(root, new Rect(PAD, y, 40f, 20f), "ui.mode_2", FS_SM, HINT_COL);
            UGUIShip.CreateButton(root, new Rect(PAD + 44f, y, arrow, 20f), "‹", BTN_ARROW, WHITE, FS_BODY,
                new Action(() => CycleMode(-1)));
            _modeLabel = MakeLabel(root, new Rect(PAD + 44f + arrow, y, w - 44f - arrow * 2f - 44f, 20f),
                ModeName(_scaleMode), FS_SM, WHITE, TextAnchor.MiddleCenter);
            UGUIShip.CreateButton(root, new Rect(PAD + w - 44f - arrow, y, arrow, 20f), "›", BTN_ARROW, WHITE, FS_BODY,
                new Action(() => CycleMode(+1)));
            y += 26f;

            MakeLabel(root, new Rect(PAD, y, w, 16f), "ui.tap_to_nudge_hold_to_run_or_just_type_a_value", FS_SM, HINT_COL);
            y += 16f;

            UGUIShip.CreateLinkText(root, new Rect(PAD, y, w, 16f), "ui.change_nudge_amount_repeat_speed",
                new Action(() => BetterFGUIMan.Instance?.OpenCreativeArgs()), fontSize: FS_SM);
        }

        // live, no Apply press. Individual bakes into each object immediately, so it gets only this
        // session's share of the total (minus what previous commits already baked in). group modes set
        // the live owner's scale, which PERSISTS between holds until the game bakes it on deselect — so
        // they always get the full running factor (can cross 1 into negative to mirror).
        private void ApplyScale()
        {
            _scaleEntry ??= BatchEditHistory.Begin("scale");
            var offset = _scaleMode == ScaleMode.Individual
                ? new Vector3(
                    _factors[0] - _committedFactors[0],
                    _factors[1] - _committedFactors[1],
                    _factors[2] - _committedFactors[2])
                : new Vector3(_factors[0] - 1f, _factors[1] - 1f, _factors[2] - 1f);
            int n = BatchScale.ApplyInto(_scaleEntry, offset, _scaleMode);
            Status(n, "scaled");
        }

        // "from selected" needs an actual pivot object clicked before scaling means anything — while
        // there's none, fade the X/Y/Z rows and block them. all other modes are always ready.
        private bool ScaleReady() => _scaleMode != ScaleMode.FromSelected || BatchScale.PivotObject() != null;

        private void UpdateScaleRowsDim()
        {
            if (_scaleRowsGroup == null) return;
            bool ready = ScaleReady();
            _scaleRowsGroup.alpha = ready ? 1f : 0.35f;
            _scaleRowsGroup.interactable = ready;
            _scaleRowsGroup.blocksRaycasts = ready;
        }

        // resets the factor displays AND the committed baseline back to 1 — called on undo/redo (the
        // running total no longer matches whatever history just changed) and on a mode switch (the
        // total got baked in, so it's the new baseline). NOT called on commit: the number persists
        // across holds to show total scaling so far.
        private void ResetFactors()
        {
            for (int i = 0; i < 3; i++)
            {
                _factors[i] = 1f;
                _committedFactors[i] = 1f;
                // null on every subtab but Scale, and undo/redo can fire from any of them
                if (_valFields[i] != null) UGUIShip.SetInputText(_valFields[i], FactorText(1f), false);
            }
        }

        // pushes the accumulated hold-session entry as a single undo step, so the next press starts a
        // fresh undo entry instead of growing this one. the display total persists — but the committed
        // baseline catches up to it, so the NEXT session only applies what's added after this point
        // (its snapshots already contain everything up to here).
        private void CommitScaleEntry()
        {
            if (_scaleEntry == null) return;
            BatchEditHistory.Push(_scaleEntry);
            _scaleEntry = null;
            for (int i = 0; i < 3; i++) _committedFactors[i] = _factors[i];
        }

        // ── material subtab ──────────────────────────────────────────────────

        private void BuildMaterial(RectTransform root, float w, ref float y)
        {
            MakeLabel(root, new Rect(PAD, y, w, 16f), "ui.set_surface_on_all_selected", FS_SM, HINT_COL);
            y += 22f;
            float half = (w - 6f) * 0.5f;
            UGUIShip.CreateButton(root, new Rect(PAD, y, half, 28f), "ui.slime", BTN_APPLY, WHITE, FS_BODY,
                new Action(() => Status(BatchMaterial.SetSlime(), "slimed")));
            UGUIShip.CreateButton(root, new Rect(PAD + half + 6f, y, half, 28f), "ui.none_3", BTN_STEP, WHITE, FS_BODY,
                new Action(() => Status(BatchMaterial.SetNone(), "cleared")));
            y += 32f;

            MakeLabel(root, new Rect(PAD, y, w, 16f), "ui.visibility", FS_SM, HINT_COL);
            y += 22f;
            UGUIShip.CreateButton(root, new Rect(PAD, y, half, 28f), "ui.visible", BTN_APPLY, WHITE, FS_BODY,
                new Action(() => Status(BatchVisibility.SetVisible(true), "shown")));
            UGUIShip.CreateButton(root, new Rect(PAD + half + 6f, y, half, 28f), "ui.invisible", BTN_STEP, WHITE, FS_BODY,
                new Action(() => Status(BatchVisibility.SetVisible(false), "hidden")));
            y += 32f;

            MakeLabel(root, new Rect(PAD, y, w, 16f), "ui.collision", FS_SM, HINT_COL);
            y += 22f;
            UGUIShip.CreateButton(root, new Rect(PAD, y, half, 28f), "ui.on", BTN_APPLY, WHITE, FS_BODY,
                new Action(() => Status(BatchCollision.SetCollisionEnabled(true), "collidable")));
            UGUIShip.CreateButton(root, new Rect(PAD + half + 6f, y, half, 28f), "ui.off", BTN_STEP, WHITE, FS_BODY,
                new Action(() => Status(BatchCollision.SetCollisionEnabled(false), "non-collidable")));
            y += 32f;

            // only stickers have an unlit mode, so this row stays off the tab entirely for anything else
            if (!BatchMaterial.AnySticker()) return;
            MakeLabel(root, new Rect(PAD, y, w, 16f), "ui.sticker_lighting", FS_SM, HINT_COL);
            y += 22f;
            UGUIShip.CreateButton(root, new Rect(PAD, y, half, 28f), "ui.lit", BTN_APPLY, WHITE, FS_BODY,
                new Action(() => Status(BatchMaterial.SetLighting(true), "lit")));
            UGUIShip.CreateButton(root, new Rect(PAD + half + 6f, y, half, 28f), "ui.unlit", BTN_STEP, WHITE, FS_BODY,
                new Action(() => Status(BatchMaterial.SetLighting(false), "unlit")));
        }


        private void BuildPhysics(RectTransform root, float w, ref float y)
        {
            if (!BatchPhysics.AnyPhysics())
            {
                MakeLabel(root, new Rect(PAD, y, w, 16f), "ui.nothing_selected_can_do_physics", FS_SM, HINT_COL);
                return;
            }

            float half = (w - 6f) * 0.5f;

            MakeLabel(root, new Rect(PAD, y, w, 16f), "ui.physics", FS_SM, HINT_COL);
            y += 22f;
            UGUIShip.CreateButton(root, new Rect(PAD, y, half, 28f), "ui.on", BTN_APPLY, WHITE, FS_BODY,
                new Action(() => Status(BatchPhysics.SetPhysicsEnabled(true), "physics on")));
            UGUIShip.CreateButton(root, new Rect(PAD + half + 6f, y, half, 28f), "ui.off", BTN_STEP, WHITE, FS_BODY,
                new Action(() => Status(BatchPhysics.SetPhysicsEnabled(false), "physics off")));
            y += 32f;

            var weights = BatchPhysics.WeightNames();
            if (weights.Length > 0)
            {
                _weightIndex = Mathf.Clamp(_weightIndex, 0, weights.Length - 1);
                MakeLabel(root, new Rect(PAD, y, 44f, 22f), "ui.weight", FS_SM, HINT_COL);
                var weightField = UGUIShip.CreateIncrement(root, new Rect(PAD + 46f, y, w - 46f, 22f), 0, weights.Length - 1,
                    () => _weightIndex, v => _weightIndex = v, wrap: true, fontSize: FS_SM,
                    fmt: i => weights[Mathf.Clamp(i, 0, weights.Length - 1)],
                    onChange: i => Status(BatchPhysics.SetWeight(i), "set " + weights[Mathf.Clamp(i, 0, weights.Length - 1)] + " on"));
                weightField.contentType = InputField.ContentType.Standard;
                weightField.readOnly = true;
                UGUIShip.SetInputText(weightField, weights[_weightIndex], false);
                y += 30f;
            }

            if (!BatchPhysics.AnyDraggable()) return;
            MakeLabel(root, new Rect(PAD, y, w, 16f), "ui.grabbable", FS_SM, HINT_COL);
            y += 22f;
            UGUIShip.CreateButton(root, new Rect(PAD, y, half, 28f), "ui.on", BTN_APPLY, WHITE, FS_BODY,
                new Action(() => Status(BatchPhysics.SetDraggable(true), "grabbable")));
            UGUIShip.CreateButton(root, new Rect(PAD + half + 6f, y, half, 28f), "ui.off", BTN_STEP, WHITE, FS_BODY,
                new Action(() => Status(BatchPhysics.SetDraggable(false), "not grabbable")));
        }

        // ── link subtab ──────────────────────────────────────────────────────

        private void BuildLink(RectTransform root, float w, ref float y)
        {
            var controller = BatchLink.Controller(out int controllers);
            if (controller == null)
            {
                MakeLabel(root, new Rect(PAD, y, w, 48f),
                    "ui.no_movement_or_rotation_controller_in_the_select",
                    FS_SM, HINT_COL, TextAnchor.UpperLeft);
                y += 52f;
                return;
            }

            MakeLabel(root, new Rect(PAD, y, w, 16f), BatchLink.TypeName(controller) + " controller", FS_BODY, WHITE);
            y += 20f;

            if (controllers > 1)
            {
                MakeLabel(root, new Rect(PAD, y, w, 16f),
                    controllers + " controllers selected, using the last one you clicked", FS_SM, HINT_COL);
                y += 18f;
            }

            BatchLink.Survey(controller, out int receivers, out int linked, out int slotsFree);
            MakeLabel(root, new Rect(PAD, y, w, 16f),
                $"{linked} of {receivers} selected linked  ·  " + (slotsFree < 0 ? "no slot limit" : slotsFree + " slot(s) free"),
                FS_SM, HINT_COL);
            y += 24f;

            float half = (w - 6f) * 0.5f;
            UGUIShip.CreateButton(root, new Rect(PAD, y, half, 28f), "ui.link_all", BTN_APPLY, WHITE, FS_BODY,
                new Action(DoLink));
            UGUIShip.CreateButton(root, new Rect(PAD + half + 6f, y, half, 28f), "ui.unlink_all", BTN_STEP, WHITE, FS_BODY,
                new Action(DoUnlink));
            y += 34f;

            MakeLabel(root, new Rect(PAD, y, w, 48f),
                "ui.each_object_goes_through_the_editor_s_own_link_c",
                FS_SM, HINT_COL, TextAnchor.UpperLeft);
            y += 52f;
        }

        private void DoLink()
        {
            var controller = BatchLink.Controller();
            var entry = BatchEditHistory.Begin("link");
            int n = BatchLink.LinkAll(entry, controller, out string note);
            BatchEditHistory.Push(entry);
            RebuildContent();
            SetStatus(n > 0 ? $"linked {n} object(s)" + (note != null ? ", " + note : "") : (note ?? "nothing to link"),
                n > 0 ? OK_COL : HINT_COL);
        }

        private void DoUnlink()
        {
            var controller = BatchLink.Controller();
            var entry = BatchEditHistory.Begin("unlink");
            int n = BatchLink.UnlinkAll(entry, controller);
            BatchEditHistory.Push(entry);
            RebuildContent();
            SetStatus(n > 0 ? $"unlinked {n} object(s)" : "none of the selection was linked to it", n > 0 ? OK_COL : HINT_COL);
        }

        // ── group subtab ─────────────────────────────────────────────────────

        private void BuildGroup(RectTransform root, float w, ref float y)
        {
            var ids = Groups.Ids();
            if (_groupPick == 0) _groupPick = Groups.SelectionGroupId();
            if (_groupPick != 0 && !ids.Contains(_groupPick)) _groupPick = 0;

            MakeLabel(root, new Rect(PAD, y, w, 14f), "ui.group_name", FS_SM, STEP_COL);
            y += 17f;

            float ddW = 28f;
            _groupNameField = UGUIShip.CreateInputField(root, new Rect(PAD, y, w - ddW - 4f, 24f),
                "ui.name_this_group", null, WHITE, FS_BODY);
            UGUIShip.SetInputText(_groupNameField, _groupPick != 0 ? Groups.NameOf(_groupPick) : "");

            var labels = new List<string> { "ui.new_group" };
            int selected = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                labels.Add(Groups.Label(ids[i]));
                if (ids[i] == _groupPick) selected = i + 1;
            }

            var pick = UGUIShip.CreateDropdown(root, new Rect(PAD + w - ddW, y, ddW, 24f), labels, selected,
                new Action<int>(i =>
                {
                    _groupPick = i > 0 && i <= ids.Count ? ids[i - 1] : 0;
                    UGUIShip.SetInputText(_groupNameField, _groupPick != 0 ? Groups.NameOf(_groupPick) : "");
                }), FS_SM, 150f, w);
            ArrowCaption(pick);
            y += 30f;

            float half = (w - 6f) * 0.5f;
            UGUIShip.CreateButton(root, new Rect(PAD, y, half, 28f), "ui.link", BTN_APPLY, WHITE, FS_BODY,
                new Action(DoGroupLink));
            UGUIShip.CreateButton(root, new Rect(PAD + half + 6f, y, half, 28f), "ui.unlink", BTN_STEP, WHITE, FS_BODY,
                new Action(DoGroupUnlink));
            y += 34f;

            MakeLabel(root, new Rect(PAD, y, w, 48f),
                "ui.grouped_objects_get_picked_dragged_and_undone_to",
                FS_SM, HINT_COL, TextAnchor.UpperLeft);
            y += 52f;
        }

        private static void ArrowCaption(Dropdown dd)
        {
            var cap = dd.captionText;
            UGUIShip.RelabelText(cap, "▾");
            cap.alignment = TextAnchor.MiddleCenter;
            cap.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rt = cap.rectTransform;
            rt.offsetMin = new Vector2(0f, 2f);
            rt.offsetMax = new Vector2(0f, -2f);
            dd.captionText = null;
        }

        private void DoGroupLink()
        {
            int n = Groups.LinkSelection(_groupPick, _groupNameField.text, out int landed);
            if (landed != 0) _groupPick = landed;
            RebuildContent();
            SetStatus(n > 0 ? $"{n} object(s) into {Groups.NameOf(_groupPick)}" : "already all in that group",
                n > 0 ? OK_COL : HINT_COL);
        }

        private void DoGroupUnlink()
        {
            int n = Groups.UnlinkSelection();
            _groupPick = 0;
            RebuildContent();
            SetStatus(n > 0 ? $"{n} object(s) out of their group" : "none of the selection was grouped",
                n > 0 ? OK_COL : HINT_COL);
        }

        // ── saved groups subtab ──────────────────────────────────────────────

        private const float SAVED_GRID_GAP = 6f;
        private const float SAVED_CAPTION_H = 22f;
        private static readonly Color SAVED_CELL_BG = new Color(0f, 0f, 0f, 0.35f);

        private void BuildSaved(RectTransform root, float w, ref float y)
        {
            if (!Solo)
            {
                int sel = BatchRecolour.SelectionCount();
                MakeLabel(root, new Rect(PAD, y, w, 14f),
                    sel > 0 ? $"save the {sel} selected object(s) as" : "nothing selected to save right now",
                    FS_SM, sel > 0 ? STEP_COL : HINT_COL);
                y += 17f;

                float saveW = 66f;
                var nameField = UGUIShip.CreateInputField(root, new Rect(PAD, y, w - saveW - 6f, 24f),
                    "ui.name_this_group", null, WHITE, FS_BODY);
                UGUIShip.SetInputText(nameField, _savedName);

                var saveBtn = UGUIShip.CreateButton(root, new Rect(PAD + w - saveW, y, saveW, 24f),
                    SavedGroups.Exists(_savedName) ? "ui.replace" : "ui.save_2", BTN_APPLY, WHITE, FS_SM,
                    new Action(() => DoSaveGroup(nameField.text)));
                var saveLabel = saveBtn.GetComponentInChildren<Text>();
                nameField.onValueChanged.AddListener(new Action<string>(v =>
                {
                    _savedName = v;
                    saveLabel.text = SavedGroups.Exists(v) ? "REPLACE" : "SAVE";
                }));
                y += 30f;

                MakeSeparator(root, new Rect(PAD, y, w, 1f));
                y += 6f;
            }

            var all = SavedGroups.All();
            if (all.Count == 0)
            {
                MakeLabel(root, new Rect(PAD, y, w, 48f),
                    "ui.nothing_in_the_library_yet_multi_select_some_obj",
                    FS_SM, HINT_COL, TextAnchor.UpperLeft);
                y += 52f;
                return;
            }

            float gridH = WindowHeight - TITLE_H - 24f - y;
            var (_, content) = UGUIShip.CreateScrollView(root, new Rect(PAD, y, w, gridH));

            float cellW = (w - UGUIShip.SCROLLBAR_INSET * 2f - SAVED_GRID_GAP) / 2f;
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.spacing = new Vector2(SAVED_GRID_GAP, SAVED_GRID_GAP);
            grid.cellSize = new Vector2(cellW, cellW + SAVED_CAPTION_H);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            foreach (var g in all) BuildSavedCell(content, g, cellW);

            y += gridH;
        }

        private void BuildSavedCell(RectTransform parent, SavedGroups.Saved g, float cellW)
        {
            var cellGo = new GameObject("SavedGroup");
            cellGo.transform.SetParent(parent, false);
            cellGo.AddComponent<RectTransform>();
            var cellImg = cellGo.AddComponent<Image>();
            cellImg.color = SAVED_CELL_BG;

            var btn = cellGo.AddComponent<Button>();
            btn.targetGraphic = cellImg;
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;
            btn.onClick.AddListener(new Action(() =>
            {
                AudioService.PlayButtonClick();
                DoPlaceGroup(g);
            }));
            UGUIShip.ForwardScrollToParent(cellGo);

            var maskGo = new GameObject("Shot");
            maskGo.transform.SetParent(cellGo.transform, false);
            var mRt = maskGo.AddComponent<RectTransform>();
            mRt.anchorMin = new Vector2(0f, 1f);
            mRt.anchorMax = new Vector2(1f, 1f);
            mRt.pivot = new Vector2(0.5f, 1f);
            mRt.offsetMin = new Vector2(0f, -cellW);
            mRt.offsetMax = Vector2.zero;
            maskGo.AddComponent<RectMask2D>();

            var imgGo = new GameObject("Img");
            imgGo.transform.SetParent(maskGo.transform, false);
            var iRt = imgGo.AddComponent<RectTransform>();
            iRt.anchorMin = Vector2.zero;
            iRt.anchorMax = Vector2.one;
            iRt.offsetMin = iRt.offsetMax = Vector2.zero;
            var raw = imgGo.AddComponent<RawImage>();
            raw.raycastTarget = false;
            var tex = SavedGroups.PreviewOf(g);
            if (tex != null) raw.texture = tex;
            else raw.color = new Color(1f, 1f, 1f, 0.06f);

            var nameLbl = MakeLabel(cellGo.transform, new Rect(0f, 0f, cellW - 58f, SAVED_CAPTION_H - 4f),
                g.Name, FS_SM - 1, WHITE, TextAnchor.MiddleLeft);
            nameLbl.horizontalOverflow = HorizontalWrapMode.Overflow;
            nameLbl.verticalOverflow = VerticalWrapMode.Truncate;
            var nameRt = nameLbl.rectTransform;
            nameRt.anchorMin = nameRt.anchorMax = nameRt.pivot = new Vector2(0f, 0f);
            nameRt.anchoredPosition = new Vector2(4f, 5f);

            var (openBtn, _) = UGUIShip.CreateSpriteButton(cellGo.transform, new Rect(0f, 0f, 18f, 18f),
                OpenFolderIcon, null, new Action(() => ReplayExport.Reveal(g.Json)));
            var openRt = openBtn.GetComponent<RectTransform>();
            openRt.anchorMin = openRt.anchorMax = openRt.pivot = new Vector2(1f, 0f);
            openRt.anchoredPosition = new Vector2(-22f, 0f);

            var delBtn = UGUIShip.CreateButton(cellGo.transform, new Rect(0f, 0f, 18f, 18f),
                "✕", new Color(0.5f, 0.22f, 0.22f, 1f), WHITE, FS_SM, new Action(() => DoDeleteGroup(g)));
            var delRt = delBtn.GetComponent<RectTransform>();
            delRt.anchorMin = delRt.anchorMax = delRt.pivot = new Vector2(1f, 0f);
            delRt.anchoredPosition = new Vector2(-2f, 0f);

            var shine = UGUIShip.BuildShine(cellGo);
            if (shine != null) UGUIShip.WireShineHover(cellGo, shine);
            UGUIShip.WireButtonAudio(cellGo);
        }

        private void DoSaveGroup(string name)
        {
            _savedName = name;
            int n = SavedGroups.Save(name, out string note);
            RebuildContent();
            SetStatus(note, n > 0 ? OK_COL : HINT_COL);
        }

        private void DoPlaceGroup(SavedGroups.Saved g)
        {
            int n = SavedGroups.Place(g, out string note, out var middle);
            if (n <= 0) { SetStatus(note, HINT_COL); return; }

            Plugin.Log.LogInfo($"{g.Name} placed ({note}), handing you {(middle != null ? middle.name : "nothing")} to drag it by");
            if (middle != null) middle.SelectObject();
            Close();
        }

        private void DoDeleteGroup(SavedGroups.Saved g)
        {
            SavedGroups.Delete(g);
            RebuildContent();
            SetStatus($"{g.Name} gone", OK_COL);
        }

        // ── registered extra subtab ──────────────────────────────────────────

        // hands the external module a context and lets it lay out its own body from y downward. index into
        // Extras is offset by the built-in count. wrapped so a throwing module draws an error line instead
        // of taking the whole window down.
        private void BuildExtra(RectTransform root, float w, ref float y)
        {
            int idx = _subtab - BUILTIN_SUBTABS.Length;
            var extras = BatchSubtabRegistry.Extras;
            if (idx < 0 || idx >= extras.Count) return;

            var ctx = new BatchSubtabContext
            {
                Root = root,
                Width = w,
                Y = y,
                SelectionCount = BatchRecolour.SelectionCount(),
                SetStatus = (msg, ok) => SetStatus(msg, ok ? OK_COL : HINT_COL),
                MakeLabel = (parent, rect, text, fs, col, anchor) => MakeLabel(parent, rect, text, fs, col, anchor),
            };
            try { extras[idx].Build(ctx); }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"batch subtab '{extras[idx].Name}' threw while building: {ex}");
                MakeLabel(root, new Rect(PAD, y, w, 16f), "ui.this_page_errored_check_the_log", FS_SM, HINT_COL);
            }
            y = ctx.Y;
        }

        // ── colour preview session ────────────────────────────────────────────

        // opens a preview session (snapshots each RGB object's original colour once) if one isn't
        // already open. re-applying colour then always recomputes from these originals, and the single
        // undo entry is these same originals.
        private void EnsureColourSession()
        {
            if (_colourEntry != null) return;
            BatchRecolour.SnapshotOriginals(_colourOriginals);
            if (_colourOriginals.Count == 0) return;
            var entry = BatchEditHistory.Begin(_recolourMode == RecolourMode.Modify ? "modify colour" : "recolour");
            foreach (var kv in _colourOriginals)
                entry.Snaps.Add(new BatchEditHistory.ObjectSnap { Obj = kv.Key, Colour = kv.Value, Palette = BatchTargets.GetColourParam(kv.Key).CurrentPalette });
            _colourEntry = entry;
            _colourSessionSelCount = BatchRecolour.SelectionCount();
        }

        private void ApplyColour()
        {
            if (_recolourMode == RecolourMode.SetColour) PreviewColourSet();
            else PreviewColourModify();
            CommitColourEntry();
        }

        private void PreviewColourSet()
        {
            if (_preview != null) _preview.color = _colour;
            EnsureColourSession();
            BatchRecolour.SetPreview(_colourOriginals, _colour);
            Status(_colourOriginals.Count, "recoloured");
        }

        private void PreviewColourModify()
        {
            EnsureColourSession();
            BatchRecolour.ModifyPreview(_colourOriginals, _modBright, _modContrast, _modHue, _modSat);
            Status(_colourOriginals.Count, "modified");
        }

        // pushes the open preview session as one undo entry and clears it. called on apply, subtab/mode
        // switch, window close, and selection change — the change stays applied, this just checkpoints it.
        private void CommitColourEntry()
        {
            if (_colourEntry == null) return;
            BatchEditHistory.Push(_colourEntry);
            _colourEntry = null;
            _colourOriginals.Clear();
            // reset modify sliders so the next session starts from "no change" (set-mode keeps its colour)
            _modBright = _modContrast = _modHue = _modSat = 0f;
        }

        // ── carousel / control handlers ──────────────────────────────────────

        // switching modes has to settle the current one first: individual writes into the scale params
        // while the group modes ride the owner's multiplier, so carrying either one across the switch
        // double-applies it. commit, bake, and start the new mode from a clean 0.
        private void CycleMode(int d)
        {
            CommitScaleEntry();
            BatchScale.BakeOwnerScale();
            ResetFactors();
            _scaleMode = (ScaleMode)(((int)_scaleMode + d + 4) % 4);
            RebuildContent();
        }

        private void CycleRecolourMode(int d)
        {
            CommitColourEntry(); // switching set/modify checkpoints the pending edit first
            _recolourMode = (RecolourMode)(((int)_recolourMode + d + 2) % 2);
            RebuildContent();
        }

        private static string RecolourModeName(RecolourMode m) => m switch
        {
            RecolourMode.SetColour => "set to colour", RecolourMode.Modify => "modify", _ => "?"
        };

        // flush any pending live edit (scale hold / colour preview) into the undo stack so undo/redo
        // act on a settled history, not a half-open session.
        private void CommitPending()
        {
            CommitScaleEntry();
            CommitColourEntry();
        }

        private void DoUndo()
        {
            CommitPending();
            BatchScale.ResetOwnerScale(); // restores write straight to the objects — clear the live parent multiplier first
            string msg = BatchEditHistory.Undo();
            SetStatus(msg ?? "nothing to undo", msg != null ? OK_COL : HINT_COL);
            ResetFactors(); // reverted scale no longer matches the running total
            RebuildContent(); // refresh undo/redo counters
        }

        private void DoRedo()
        {
            CommitPending();
            BatchScale.ResetOwnerScale();
            string msg = BatchEditHistory.Redo();
            SetStatus(msg ?? "nothing to redo", msg != null ? OK_COL : HINT_COL);
            ResetFactors();
            RebuildContent();
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static string ModeName(ScaleMode m) => m switch
        {
            ScaleMode.FromOrigin => "from 0,0,0", ScaleMode.Individual => "individual", ScaleMode.FromCenter => "from center", ScaleMode.FromSelected => "from selected", _ => "?"
        };

        private static string FactorText(float v) => v.ToString("0.##");

        private void Status(int n, string verb)
        {
            SetStatus(n > 0 ? $"{verb} {n} object(s)" : "nothing applicable in selection", n > 0 ? OK_COL : HINT_COL);
            if (_countLabel != null) UGUIShip.RelabelText(_countLabel, CountText());
        }

        private static string CountText() => BatchRecolour.SelectionCount() + " object(s) selected";

        private void SetStatus(string text, Color col)
        {
            if (_statusLabel == null) return;
            _statusLabel.text = text;
            _statusLabel.color = col;
        }
    }
}
