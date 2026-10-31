using System;
using BetterFG.Features.Onboarding;
using BetterFG.Services;
using BetterFG.UI;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Windows
{
    public class OptionsWindow : SideWindow
    {
        public OptionsWindow(IntPtr ptr) : base(ptr) { }

        protected override float WindowWidth => OptionsWindowBuilder.WIN_W;
        protected override float WindowHeight => 220f;
        protected override string WindowTitle => "ui.bettrfg_options";

        protected override void BuildContent(RectTransform contentRoot)
        {
            BgPosition = new Vector3(139.3993f * OptionsWindowBuilder.WIN_W / 280f, 74.9552f, 0f);
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
                "ui.search_options", new Color(0f, 0f, 0f, 0.75f), Color.white, 12);
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

            OptionsWindowBuilder.BuildRows(listRt);

            var listCapture = listRt;
            searchField.onValueChanged.AddListener(new Action<string>(q =>
                OptionsWindowBuilder.Filter(listCapture, q ?? "")));
        }
    }

    internal static class OptionsWindowBuilder
    {
        private const float ROW_H = 22f;
        // row width isn't the full 280 window — CreateScrollView insets its viewport by
        // SCROLLBAR_INSET on both sides for the scrollbar, so the actual row content is narrower.
        internal const float WIN_W = 370f;
        private const float WINDOW_W = WIN_W - 2f * UGUIShip.SCROLLBAR_INSET + SideWindow.RowsLeftShift;
        internal const float BTN_H = 16f;
        internal const float BTN_Y = (ROW_H - BTN_H) / 2f;
        private const float PREVIEW_H = 44f;
        private const float PAD = 6f;
        internal const float CTRL_W = 110f;
        private const float VAL_W = 30f;
        private const float TOGGLE_W = 36f;
        private const float HEADER_GAP = 10f;
        private const float HEADER_H = 18f + HEADER_GAP;
        private const float HEADER_LEFT = SideWindow.RowLabelX;
        private const float LABEL_X = SideWindow.RowLabelX;
        private const float LABEL_W = WINDOW_W - PAD - CTRL_W - PAD - LABEL_X;
        private const float FULL_W = WINDOW_W - PAD - LABEL_X;
        private const float HEADER_SCALE = 1.3f;  // literal Text gameObject localScale bump
        private const int FS = 13;
        private const int FS_VAL = 11;
        private const int FS_BTN = 9;
        private static readonly Color ROW_EVEN = new Color(1f, 1f, 1f, 0.03f);
        private static readonly Color ROW_ODD = new Color(0f, 0f, 0f, 0f);
        private static readonly Color LABEL_COL = new Color(1f, 1f, 1f, 0.85f);
        private static readonly Color FIELD_BG = new Color(0.12f, 0.12f, 0.12f, 1f);
        internal static readonly Color BTN_IDLE =new Color(0.25f, 0.45f, 0.75f, 1f);
        private static readonly Color BTN_RECORDING = new Color(0.85f, 0.55f, 0.2f, 1f);
        private static readonly Color BTN_DANGER = new Color(0.5f, 0.22f, 0.22f, 1f);
        private static readonly Color FILL_R = new Color(1f, 0.3f, 0.3f, 1f);
        private static readonly Color FILL_G = new Color(0.3f, 1f, 0.3f, 1f);
        private static readonly Color FILL_B = new Color(0.4f, 0.6f, 1f, 1f);

        private const string KEY_MAX_TABS = "ui.maxtabs";

        private static int _stripe;

        // show/hide rows by label match. rows are named "<kind>_<label>", headers "Header_<TITLE>".
        // a header stays visible only if at least one row before the next header matches.
        public static void Filter(RectTransform parent, string query)
        {
            string q = query.Trim().ToLowerInvariant();
            int n = parent.childCount;

            var go = new GameObject[n];
            var isHeader = new bool[n];
            var matches = new bool[n];
            for (int i = 0; i < n; i++)
            {
                go[i] = parent.GetChild(i).gameObject;
                string name = go[i].name;
                int us = name.IndexOf('_');
                string label = LocalizationService.Get(us >= 0 ? name.Substring(us + 1) : name);
                isHeader[i] = name.StartsWith("Header_", StringComparison.Ordinal);
                matches[i] = q.Length == 0 || label.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) >= 0;
            }

            // a header matching the query pulls in its whole section; then a row shows if it
            // matches directly or its section header did.
            for (int i = 0; i < n; i++)
            {
                if (!isHeader[i]) continue;
                if (!matches[i]) continue;
                for (int j = i + 1; j < n && !isHeader[j]; j++)
                    matches[j] = true;
            }

            for (int i = 0; i < n; i++)
            {
                if (isHeader[i])
                {
                    bool anyBelow = false;
                    for (int j = i + 1; j < n && !isHeader[j]; j++)
                        if (matches[j]) { anyBelow = true; break; }
                    go[i].SetActive(q.Length == 0 || anyBelow);
                }
                else
                {
                    go[i].SetActive(matches[i]);
                }
            }
        }

        public static void BuildRows(RectTransform parent)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            Func<float, string> fmt2 = v => v.ToString("0.00", ci);

            // ── General ───────────────────────────────────────────────────────
            BuildHeader(parent, "options.header_general");
            const float uiMin = 0.6f, uiMax = 1.8f;
            BuildSliderRow(parent, "ui.bettrfg_ui", uiMin, uiMax,
                () => UIScaleService.Current,
                v => UIScaleService.SetValue(v),
                fmt2, () => UIScaleService.SetEnabled(true), 1.2f);
            BuildLanguageRow(parent);
            BuildFontRow(parent);
            BuildStepperRow(parent, "ui.max_tab_amount", 1, 5,
                () => int.TryParse(SettingsService.Get(KEY_MAX_TABS, "3"), out int v) ? Mathf.Clamp(v, 1, 5) : 3,
                v =>
                {
                    if (BetterFGUIMan.Instance != null) BetterFGUIMan.Instance.SetMaxTabs(v);
                    else SettingsService.Set(KEY_MAX_TABS, v.ToString());
                });
            BuildToggleRow(parent, "news.show_feed",
                () => NewsFeed.Enabled,
                v => NewsFeed.SetEnabled(v));
            BuildToggleRow(parent, "privacy.usage_stats",
                () => UsageStats.On,
                v => UsageStats.On = v);
            BuildToggleRow(parent, "users.prompt_startup",
                () => UserLogon.PromptOnStartup,
                v => UserLogon.PromptOnStartup = v);

            BuildHeader(parent, "options.header_backdrop");
            BuildBackdropRows(parent);
            BuildSliderRow(parent, "ui.bg_opacity", 0f, 1f,
                () => BetterFGUIMan.BackdropOpacity,
                v => { if (BetterFGUIMan.Instance != null) BetterFGUIMan.Instance.SetBackdropOpacity(v); },
                fmt2, null, 1f);

            // ── Tab hover background ──────────────────────────────────────────
            // the faint pic behind a tab title on hover. optionally keep it visible always at a
            // low idle opacity, and tint it any colour (applies hovered or not).
            BuildHeader(parent, "ui.tab_hover_bg");
            TabHoverStyle.LoadFromSettings();
            BuildToggleRow(parent, "ui.always_show",
                () => TabHoverStyle.AlwaysShow,
                v => { TabHoverStyle.AlwaysShow = v; TabHoverStyle.Save(); TabHoverStyle.ApplyAll(); });
            BuildSliderRow(parent, "ui.idle_opacity", 0f, 1f,
                () => TabHoverStyle.IdleAlpha,
                v => { TabHoverStyle.IdleAlpha = v; TabHoverStyle.Save(); TabHoverStyle.ApplyAll(); },
                fmt2, null, 0.15f);

            BuildHeader(parent, "options.header_tint");
            var swatch = DeluxBox(Row(parent, "", ROW_H, FULL_W, "options.tint"),
                new Rect(0f, BTN_Y, FULL_W, BTN_H), TabHoverStyle.Tint);
            void SetTint(Color c)
            {
                TabHoverStyle.Tint = c;
                TabHoverStyle.Save();
                TabHoverStyle.ApplyAll();
                swatch.color = c;
            }
            BuildSliderRow(parent, "options.tint_r", 0f, 1f, () => TabHoverStyle.Tint.r,
                v => { var c = TabHoverStyle.Tint; SetTint(new Color(v, c.g, c.b)); }, fmt2, null, 1f, FILL_R);
            BuildSliderRow(parent, "options.tint_g", 0f, 1f, () => TabHoverStyle.Tint.g,
                v => { var c = TabHoverStyle.Tint; SetTint(new Color(c.r, v, c.b)); }, fmt2, null, 1f, FILL_G);
            BuildSliderRow(parent, "options.tint_b", 0f, 1f, () => TabHoverStyle.Tint.b,
                v => { var c = TabHoverStyle.Tint; SetTint(new Color(c.r, c.g, v)); }, fmt2, null, 1f, FILL_B);

            // ── Keybinds ──────────────────────────────────────────────────────
            BuildHeader(parent, "ui.keyboard");
            BuildKeyRow(parent, KeybindId.ToggleUI, "ui.toggle_bettrfg_ui");
            BuildKeyRow(parent, KeybindId.ToggleWheel, "ui.toggle_sidewheel");

            BuildHeader(parent, "ui.controller");
            BuildSliderRow(parent, "ui.cursor_speed",
                ControllerBindService.MinCursorSpeed, ControllerBindService.MaxCursorSpeed,
                () => ControllerBindService.CursorSpeed, v => ControllerBindService.CursorSpeed = v,
                null, null, ControllerBindService.DefaultCursorSpeed);
            BuildSliderRow(parent, "ui.scroll_speed",
                ControllerBindService.MinScrollSpeed, ControllerBindService.MaxScrollSpeed,
                () => ControllerBindService.ScrollSpeed, v => ControllerBindService.ScrollSpeed = v,
                null, null, ControllerBindService.DefaultScrollSpeed);
            BuildControllerRow(parent, ControllerBindId.ToggleUI,    "ui.toggle_bettrfg_ui");
            BuildControllerRow(parent, ControllerBindId.ToggleWheel, "ui.toggle_sidewheel");
            BuildControllerRow(parent, ControllerBindId.LeftClick,   "ui.left_click");
            BuildControllerRow(parent, ControllerBindId.RightClick,  "ui.right_click");

            // ── Backup ────────────────────────────────────────────────────────
            BuildHeader(parent, "ui.backup");
            BuildToggleRow(parent, "ui.backup_last_txt",
                () => SettingsService.BackupEnabled,
                v => SettingsService.BackupEnabled = v);
            BuildStepperRow(parent, "ui.interval_minutes", 1, 120,
                () => SettingsService.BackupIntervalMinutes,
                v => SettingsService.BackupIntervalMinutes = v);
            BuildButtonRow(parent, "options.backup_folder", "ui.open_folder", BTN_IDLE, () =>
            {
                try
                {
                    if (!System.IO.Directory.Exists(SettingsService.BackupFolderPath))
                        System.IO.Directory.CreateDirectory(SettingsService.BackupFolderPath);
                    System.Diagnostics.Process.Start("explorer.exe", SettingsService.BackupFolderPath);
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogWarning("couldn't open backup folder: " + ex.Message);
                }
            });

            // ── Misc ──────────────────────────────────────────────────────────
            BuildHeader(parent, "options.header_misc");
            BuildToggleRow(parent, "ui.rich_presence",
                () => DiscordPresenceService.Enabled,
                v => DiscordPresenceService.SetEnabled(v));
            BuildButtonRow(parent, "options.bug_report_logs", "ui.export", BTN_IDLE,
                () => BugReportService.Export(true));
            UGUIShip.CreateButton(Row(parent, "", ROW_H, FULL_W, "options.reset_all"),
                new Rect(0f, BTN_Y, FULL_W, BTN_H), "options.reset_all", BTN_DANGER, Color.white, FS_BTN,
                new Action(() => FullscreenPrompt.Ask("options.reset_all.title", "options.reset_all.body",
                    "options.reset", () =>
                    {
                        SettingsService.ResetAll();
                        Application.Quit();
                    }, PopUpKind.Dangerous)));
        }

        private static Image DeluxBox(Transform parent, Rect rect, Color fill)
        {
            var go = new GameObject("DeluxBox");
            go.transform.SetParent(parent, false);
            UGUIShip.SetPixelRect(go.AddComponent<RectTransform>(), rect);
            var img = go.AddComponent<Image>();
            return UGUIShip.ApplyDeluxSkin(go, img, fill) ?? img;
        }

        internal static RectTransform Row(RectTransform parent, string label, float height = ROW_H,
            float ctrlW = CTRL_W, string name = null)
        {
            var rowGo = new GameObject("Row_" + (name ?? label));
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleWidth = 1f;
            UGUIShip.PaintStaticRowFill(rowGo, _stripe++ % 2 == 0 ? ROW_EVEN : ROW_ODD);

            UGUIShip.CreateLabel(rowGo.transform, new Rect(LABEL_X, 0f, LABEL_W, height),
                label, FS, LABEL_COL, TextAnchor.MiddleLeft);

            var ctrlGo = new GameObject("Ctrl");
            ctrlGo.transform.SetParent(rowGo.transform, false);
            var rt = ctrlGo.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-PAD, 0f);
            rt.sizeDelta = new Vector2(ctrlW, height);
            return rt;
        }

        private static Button BuildButtonRow(RectTransform parent, string label, string text, Color col,
            Action onClick)
        {
            var btn = UGUIShip.CreateButton(Row(parent, label), new Rect(0f, BTN_Y, CTRL_W, BTN_H),
                text, col, Color.white, FS_BTN);
            btn.onClick.AddListener(new Action(onClick));
            return btn;
        }

        // ON/OFF toggle row, right-aligned button mirroring the EmoticonsPhrasesTab style.
        private static void BuildToggleRow(RectTransform parent, string label, Func<bool> get, Action<bool> set)
        {
            bool state = get();
            var btn = UGUIShip.CreateButton(Row(parent, label),
                new Rect(CTRL_W - TOGGLE_W, BTN_Y, TOGGLE_W, BTN_H),
                state ? "ui.on" : "ui.off",
                state ? UGUIShip.TOGGLE_ON : UGUIShip.TOGGLE_OFF, Color.white, FS_BTN);
            btn.onClick.AddListener(new Action(() =>
            {
                bool nv = !get();
                set(nv);
                Apply(btn, nv ? "ui.on" : "ui.off", nv ? UGUIShip.TOGGLE_ON : UGUIShip.TOGGLE_OFF);
            }));
        }

        internal static void BuildHeader(RectTransform parent, string title)
        {
            _stripe = 0;
            float gap = parent.childCount == 0 ? 0f : HEADER_GAP;
            var rowGo = new GameObject("Header_" + title);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = HEADER_H - HEADER_GAP + gap;
            le.flexibleWidth = 1f;

            var lbl = UGUIShip.CreateLabel(rowGo.transform,
                new Rect(HEADER_LEFT, 0f, 200f, HEADER_H),
                title, 10,
                Color.white,
                TextAnchor.MiddleLeft);
            lbl.fontStyle = FontStyle.Bold;
            var rt = lbl.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(HEADER_LEFT, -gap * 0.5f);
            rt.localScale = new Vector3(HEADER_SCALE, HEADER_SCALE, 1f);
        }

        // slider row: label on the left, a slider filling the middle, current value on the right.
        // fmt formats the right-hand value text (defaults to int). onCommit, if given, fires on
        // pointer-up so live-rescaling controls don't apply on every drag tick.
        private static void BuildSliderRow(RectTransform parent, string label,
            float min, float max, Func<float> get, Action<float> set,
            Func<float, string> fmt = null, Action onCommit = null, float? resetTo = null, Color? fill = null)
        {
            if (fmt == null) fmt = v => ((int)v).ToString();

            var ctrl = Row(parent, label);

            // value label hugs the right edge
            var valLbl = UGUIShip.CreateLabel(ctrl,
                new Rect(CTRL_W - VAL_W, 0f, VAL_W, ROW_H),
                fmt(get()), FS_VAL, Color.white, TextAnchor.MiddleRight);

            // shared delux slider (same widget nametag/menu tabs use — tints with the rest of the UI).
            // min/max here is an arbitrary range; CreateSlider only knows 0..1, so normalize in/out.
            float t0 = Mathf.InverseLerp(min, max, Mathf.Clamp(get(), min, max));
            var slider = UGUIShip.CreateSlider(ctrl, 0f, BTN_Y, CTRL_W - VAL_W - 4f, "", t0,
                BTN_H, 0f, FS,
                t =>
                {
                    float v = Mathf.Lerp(min, max, t);
                    set(v);
                    UGUIShip.RelabelText(valLbl, fmt(v));
                },
                null, fill ?? BTN_IDLE, reserveLabel: false,
                resetTo: resetTo.HasValue ? Mathf.InverseLerp(min, max, resetTo.Value) : (float?)null);

            if (onCommit != null)
            {
                var trig = slider.gameObject.GetComponent<UnityEngine.EventSystems.EventTrigger>()
                    ?? slider.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                var up = new UnityEngine.EventSystems.EventTrigger.Entry
                { eventID = UnityEngine.EventSystems.EventTriggerType.PointerUp };
                up.callback.AddListener(new Action<UnityEngine.EventSystems.BaseEventData>(_ => onCommit()));
                trig.triggers.Add(up);
            }
        }

        private static void BuildStepperRow(RectTransform parent, string label,
            int min, int max, Func<int> get, Action<int> set)
            => UGUIShip.CreateIncrement(Row(parent, label), new Rect(0f, BTN_Y, CTRL_W, BTN_H), min, max, get, set,
                fontSize: FS_VAL);

        private static Text BuildCarouselRow(RectTransform parent, string label, string[] labels, int current,
            Action<int> onStep)
        {
            var ctrl = Row(parent, label);
            DeluxBox(ctrl, new Rect(BTN_H, BTN_Y, CTRL_W - BTN_H * 2f, BTN_H), FIELD_BG);
            return UGUIShip.CreateCarousel(ctrl, new Rect(0f, BTN_Y, CTRL_W, BTN_H), labels, current, onStep,
                UGUIShip.STEP_COL, FS_VAL);
        }

        private static void BuildBackdropRows(RectTransform parent)
        {
            Texture2D Pattern(int i)
            {
                var tex = BetterFG.Utilities.EmbeddedResourceandUnity.LoadTexture(BetterFGUIMan.BackdropPatterns[i]);
                tex.wrapMode = TextureWrapMode.Repeat;
                return tex;
            }

            int cur = BetterFGUIMan.BackdropIndex;
            Text lbl = null;
            RawImage preview = null;
            lbl = BuildCarouselRow(parent, "ui.backdrop", BetterFGUIMan.BackdropNames, cur, d =>
            {
                int n = BetterFGUIMan.BackdropPatterns.Length;
                cur = ((cur + d) % n + n) % n;
                if (BetterFGUIMan.Instance != null) BetterFGUIMan.Instance.SetBackdrop(cur);
                preview.texture = Pattern(cur);
                UGUIShip.RelabelText(lbl, BetterFGUIMan.BackdropNames[cur]);
            });

            const float h = PREVIEW_H - BTN_Y * 2f;
            var frameGo = new GameObject("Preview");
            frameGo.transform.SetParent(Row(parent, "", PREVIEW_H, FULL_W), false);
            UGUIShip.SetPixelRect(frameGo.AddComponent<RectTransform>(), new Rect(0f, BTN_Y, FULL_W, h));
            var (_, inner) = UGUIShip.CreateFramedImage(frameGo.transform);
            preview = inner.gameObject.AddComponent<RawImage>();
            preview.raycastTarget = false;
            preview.texture = Pattern(cur);
            preview.uvRect = new Rect(0f, 0f, FULL_W / h, 1f);
        }

        // label + [-] <code> [+], cycling LocalizationService.AvailableLanguages. switches live —
        // every bound Text in the whole UI (any window/tab already open) re-resolves on
        // SetLanguage, no rebuild needed.
        private static void BuildLanguageRow(RectTransform parent)
        {
            Text lbl = null;
            Image flag = null;
            lbl = BuildCarouselRow(parent, "ui.language",
                new[] { LocalizationService.DisplayName(LocalizationService.CurrentLanguage) }, 0, d =>
                {
                    LocalizationService.CycleLanguage(d);
                    UGUIShip.RelabelText(lbl, LocalizationService.DisplayName(LocalizationService.CurrentLanguage));
                    SetFlag(flag);
                });
            var lrt = lbl.rectTransform;
            lrt.anchoredPosition += new Vector2(14f, 0f);
            lrt.sizeDelta -= new Vector2(14f, 0f);
            var flagGo = new GameObject("Flag");
            flagGo.transform.SetParent(lbl.transform.parent, false);
            UGUIShip.SetPixelRect(flagGo.AddComponent<RectTransform>(), new Rect(BTN_H + 5f, BTN_Y + (BTN_H - 10f) * 0.5f, 15f, 10f));
            flag = flagGo.AddComponent<Image>();
            flag.preserveAspect = true;
            flag.raycastTarget = false;
            SetFlag(flag);
        }

        private static void SetFlag(Image flag)
        {
            var code = LocalizationService.FlagCode(LocalizationService.CurrentLanguage);
            flag.sprite = code == null ? null : BetterFG.Nametag.FlagAssets.LoadFlag(code);
            flag.enabled = flag.sprite != null;
        }

        private static void BuildFontRow(RectTransform parent)
        {
            var fonts = UGUIShip.AvailableFonts;
            var names = new string[fonts.Length];
            int cur = 0;
            for (int i = 0; i < fonts.Length; i++)
            {
                names[i] = fonts[i].DisplayName;
                if (fonts[i].Code == UGUIShip.SelectedFontCode) cur = i;
            }

            Text lbl = null;
            lbl = BuildCarouselRow(parent, "font.mod_ui_font", names, cur, d =>
            {
                cur = ((cur + d) % fonts.Length + fonts.Length) % fonts.Length;
                UGUIShip.SetFontByCode(fonts[cur].Code);
                SettingsService.Set("font.family", fonts[cur].Code);
                UGUIShip.RelabelText(lbl, names[cur]);
            });
        }

        private static void BuildKeyRow(RectTransform parent, KeybindId id, string label)
        {
            Button btn = null;
            btn = BuildButtonRow(parent, label, CurrentLabel(id), BTN_IDLE, () =>
                KeybindRecorder.Begin(id, btn,
                    onDone: () => Apply(btn, CurrentLabel(id), BTN_IDLE),
                    onStart: () => Apply(btn, "ui.press_a_key", BTN_RECORDING)));
        }

        // controller button bind row: label + a record button showing the bound button's name
        private static void BuildControllerRow(RectTransform parent, ControllerBindId id, string label)
        {
            Button btn = null;
            btn = BuildButtonRow(parent, label, ControllerBindService.ButtonName(ControllerBindService.GetButton(id)),
                BTN_IDLE, () =>
                    KeybindRecorder.BeginController(id, btn,
                        onDone: () => Apply(btn, ControllerBindService.ButtonName(ControllerBindService.GetButton(id)), BTN_IDLE),
                        onStart: () => Apply(btn, "ui.press_a_button", BTN_RECORDING)));
        }

        private static string CurrentLabel(KeybindId id)
        {
            return id == KeybindId.ToggleUI
                ? "Shift + " + KeybindService.KeyName(KeybindService.Get(id))
                : KeybindService.KeyName(KeybindService.Get(id));
        }

        private static void Apply(Button btn, string text, Color color)
        {
            if (btn == null) return;
            UGUIShip.SetButtonColor(btn, color);
            var lbl = btn.GetComponentInChildren<Text>();
            if (lbl != null) UGUIShip.RelabelText(lbl, text);
        }
    }

    // ── One-shot key recorder ─────────────────────────────────────────────────
    // records either a keyboard key (KeybindId) or a controller button (ControllerBindId),
    // depending on which Begin overload was used.
    public class KeybindRecorder : MonoBehaviour
    {
        public KeybindRecorder(IntPtr ptr) : base(ptr) { }

        private static KeybindRecorder _instance;

        // true while a row is waiting for a key/button, AND for a brief cooldown after — lets
        // ControllerManager ignore the press that was just recorded so the newly-bound button
        // doesn't immediately fire its own action (toggle/click) on the same hold.
        public static bool IsRecording =>
            (_instance != null && _instance._active) || Time.unscaledTime < _cooldownUntil;

        private static float _cooldownUntil;

        private KeybindId _id;
        private ControllerBindId _ctrlId;
        private bool _controllerMode;
        private Button _btn;
        private Action _onDone;
        private bool _active;
        private float _grace;

        public static void Begin(KeybindId id, Button btn, Action onDone, Action onStart)
        {
            BeginCommon(btn, onDone, onStart);
            _instance._id = id;
            _instance._controllerMode = false;
        }

        public static void BeginController(ControllerBindId id, Button btn, Action onDone, Action onStart)
        {
            BeginCommon(btn, onDone, onStart);
            _instance._ctrlId = id;
            _instance._controllerMode = true;
        }

        private static void BeginCommon(Button btn, Action onDone, Action onStart)
        {
            if (_instance == null)
            {
                var go = new GameObject("BetterFG_KeybindRecorder");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                _instance = go.AddComponent<KeybindRecorder>();
            }
            _instance._btn = btn;
            _instance._onDone = onDone;
            _instance._active = true;
            _instance._grace = 0.05f; // ignore the click-frame
            onStart?.Invoke();
        }

        void Update()
        {
            if (!_active) return;

            if (_grace > 0f)
            {
                _grace -= Time.deltaTime;
                return;
            }

            if (KeybindService.KeyDown(KeyCode.Escape))
            {
                Finish();
                return;
            }

            if (_controllerMode)
            {
                int btn = ControllerBindService.PollPressedButton();
                if (btn < 0) return;
                ControllerBindService.SetButton(_ctrlId, btn);
                _cooldownUntil = Time.unscaledTime + 0.35f; // swallow the recorded press
                Finish();
                return;
            }

            var key = KeybindService.PollPressedKey();
            if (key == KeyCode.None) return;

            KeybindService.Set(_id, key);
            Finish();
        }

        private void Finish()
        {
            _active = false;
            _onDone?.Invoke();
            _onDone = null;
            _btn = null;
        }
    }
}
