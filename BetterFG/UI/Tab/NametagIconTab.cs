using System;
using System.Collections;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Features.MorePlatformIcon;
using BetterFG.Nametag;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Tab
{
    public class NametagIconTab : NametagWizardTab
    {
        public NametagIconTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Nametag - Icon";

        private const string KEY_ICON_MODE = "nametag.icon.mode";
        private const string KEY_ICON_COUNTRY = "nametag.icon.country";
        private const string KEY_ICON_PATH = "nametag.icon.path";
        private const string KEY_ICON_SCALE = "nametag.icon.scale";
        private const string KEY_ICON_OFFSET_X = "nametag.icon.offset.x";
        private const string KEY_ICON_OFFSET_Y = "nametag.icon.offset.y";
        private const string KEY_PLATFORM_HIDE = "nametag.platform.hide";
        private const string KEY_PLATFORM_CUSTOM = "nametag.platform.custom";

        private static readonly Color SEL_COLOR = new Color(0.25f, 0.5f, 0.25f, 1f);
        private static readonly Color ICON_OFF = new Color(1f, 1f, 1f, 0.3f);

        private static float FLAG_ROW_H => UIScale.BTN_H * 0.72f;
        private static float FLAG_ICON_SIZE => FLAG_ROW_H * 0.75f;
        private static float CUSTOM_ICON_ROW_H => BTN_H + PAD + 1f + PAD + (LH + SH) * 3f;
        private const float PLATFORM_ICON_GRID_H = 117f;

        private enum IconMode { None, Flag, Custom }
        private enum PlatformHideMode { None, Self, Everyone }

        private IconMode _iconMode = IconMode.None;
        private string _selectedCountry = "";
        private string _customIconPath = "";
        private float _iconScale = 1f;
        private float _iconOffsetX, _iconOffsetY;
        private bool _iconTransformApplyPending;
        private Coroutine _iconTransformApplyRoutine;

        private PlatformHideMode _platformHide = PlatformHideMode.None;
        private string _platformCustom = "";

        private GameObject _flagSection, _customSection;
        private Text _customIconLabel;
        private RectTransform _scrollContent;
        private RectTransform _platformIconContent;
        private Button _btnNone, _btnFlag, _btnCustom;
        private Button _btnPlatNone, _btnPlatSelf, _btnPlatEveryone;

        protected override string[] StepTitles => new[] { "Icon source", "Hide platform icon", "Custom platform icon" };

        void Awake()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string modeStr = SettingsService.Get(KEY_ICON_MODE, "none");
            _iconMode = modeStr == "flag" ? IconMode.Flag : modeStr == "custom" ? IconMode.Custom : IconMode.None;
            _selectedCountry = SettingsService.Get(KEY_ICON_COUNTRY, "");
            _customIconPath = SettingsService.Get(KEY_ICON_PATH, "");
            _iconScale = float.TryParse(SettingsService.Get(KEY_ICON_SCALE, "1"), System.Globalization.NumberStyles.Float, ci, out float sv) ? sv : 1f;
            _iconOffsetX = float.TryParse(SettingsService.Get(KEY_ICON_OFFSET_X, "0"), System.Globalization.NumberStyles.Float, ci, out float ox) ? ox : 0f;
            _iconOffsetY = float.TryParse(SettingsService.Get(KEY_ICON_OFFSET_Y, "0"), System.Globalization.NumberStyles.Float, ci, out float oy) ? oy : 0f;

            string platStr = SettingsService.Get(KEY_PLATFORM_HIDE, "none");
            _platformHide = platStr == "self" ? PlatformHideMode.Self : platStr == "everyone" ? PlatformHideMode.Everyone : PlatformHideMode.None;
            _platformCustom = SettingsService.Get(KEY_PLATFORM_CUSTOM, "");
        }

        protected override void BuildStep(int step, RectTransform root, float w, float bodyH)
        {
            var (_, c) = UGUIShip.CreateScrollView(root, new Rect(0f, 0f, TabWidth, bodyH));
            float cw = w - 26f;
            switch (step)
            {
                case 0: BuildIconSourceStep(c, cw); break;
                case 1: BuildPlatformHideStep(c, cw); break;
                case 2: BuildPlatformGridStep(c, cw); break;
            }
        }

        private void BuildIconSourceStep(RectTransform c, float w)
        {
            float x = PAD, cy = PAD;
            UGUIShip.CreateLabel(c, new Rect(x, cy, w, LH), "ICON", FS_SM, HINT);
            cy += LH + SH;
            float modew = (w - PAD) / 3f;
            _btnNone = UGUIShip.CreateButton(c, new Rect(x, cy, modew, BTN_H), "None",
                _iconMode == IconMode.None ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetIconMode(IconMode.None)));
            _btnFlag = UGUIShip.CreateButton(c, new Rect(x + modew + PAD * 0.5f, cy, modew, BTN_H), "Flag",
                _iconMode == IconMode.Flag ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetIconMode(IconMode.Flag)));
            _btnCustom = UGUIShip.CreateButton(c, new Rect(x + (modew + PAD * 0.5f) * 2f, cy, modew, BTN_H), "Custom",
                _iconMode == IconMode.Custom ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetIconMode(IconMode.Custom)));
            cy += BTN_H + PAD;
            UGUIShip.CreatePanel(c, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            float flagListH = FlagAssets.GetAvailableCodes().Length * FLAG_ROW_H;
            var flagHolder = new GameObject("FlagSection");
            flagHolder.transform.SetParent(c, false);
            UGUIShip.SetPixelRect(flagHolder.AddComponent<RectTransform>(), new Rect(x, cy, w, flagListH));
            _flagSection = flagHolder;
            BuildCountryList(flagHolder.transform);

            var customHolder = new GameObject("CustomSection");
            customHolder.transform.SetParent(c, false);
            UGUIShip.SetPixelRect(customHolder.AddComponent<RectTransform>(), new Rect(x, cy, w, CUSTOM_ICON_ROW_H));
            _customSection = customHolder;
            BuildCustomIconRow(customHolder.transform, w);

            cy += Mathf.Max(flagListH, CUSTOM_ICON_ROW_H) + PAD;
            c.sizeDelta = new Vector2(0f, cy + PAD);
            RefreshIconModeVisibility();
        }

        private void BuildPlatformHideStep(RectTransform c, float w)
        {
            float x = PAD, cy = PAD;
            UGUIShip.CreateLabel(c, new Rect(x, cy, w, LH), "DISABLE PLATFORM ICON", FS_SM, HINT);
            cy += LH + SH;
            float modew = (w - PAD) / 3f;
            _btnPlatNone = UGUIShip.CreateButton(c, new Rect(x, cy, modew, BTN_H), "None",
                _platformHide == PlatformHideMode.None ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetPlatformHide(PlatformHideMode.None)));
            _btnPlatSelf = UGUIShip.CreateButton(c, new Rect(x + modew + PAD * 0.5f, cy, modew, BTN_H), "Yourself",
                _platformHide == PlatformHideMode.Self ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetPlatformHide(PlatformHideMode.Self)));
            _btnPlatEveryone = UGUIShip.CreateButton(c, new Rect(x + (modew + PAD * 0.5f) * 2f, cy, modew, BTN_H), "Everyone",
                _platformHide == PlatformHideMode.Everyone ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetPlatformHide(PlatformHideMode.Everyone)));
            cy += BTN_H + PAD;
            c.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void BuildPlatformGridStep(RectTransform c, float w)
        {
            float x = PAD, cy = PAD;
            UGUIShip.CreateLabel(c, new Rect(x, cy, w, LH), "CUSTOM PLATFORM ICON (local)", FS_SM, HINT);
            cy += LH + SH;
            BuildPlatformIconButtons(c, x, cy, w, PLATFORM_ICON_GRID_H);
            cy += PLATFORM_ICON_GRID_H + PAD;
            c.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void BuildCountryList(Transform parent)
        {
            var listGo = new GameObject("FlagList");
            listGo.transform.SetParent(parent, false);
            var rt = listGo.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _scrollContent = rt;

            var vlg = listGo.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 0f;
            vlg.padding = new RectOffset((int)(PAD * 0.5f), (int)(PAD * 0.5f), 0, 0);

            Sprite btnSpr = UGUIShip.GetButtonSprite();

            foreach (string code in FlagAssets.GetAvailableCodes())
            {
                string captured = code;
                bool isSelected = string.Equals(code, _selectedCountry, StringComparison.OrdinalIgnoreCase);

                var rowGo = new GameObject("ISO_" + code);
                rowGo.transform.SetParent(_scrollContent, false);
                rowGo.AddComponent<RectTransform>();
                rowGo.AddComponent<LayoutElement>().preferredHeight = FLAG_ROW_H;

                var rowImg = rowGo.AddComponent<Image>();
                if (isSelected && btnSpr != null)
                {
                    rowImg.sprite = btnSpr;
                    rowImg.type = Image.Type.Simple;
                    rowImg.color = SEL_COLOR;
                }
                else
                {
                    rowImg.color = Color.clear;
                }

                var btn = rowGo.AddComponent<Button>();
                btn.targetGraphic = rowImg;
                var cols = btn.colors;
                cols.normalColor = isSelected ? SEL_COLOR : Color.clear;
                cols.highlightedColor = new Color(1f, 1f, 1f, 0.08f);
                btn.colors = cols;
                btn.onClick.AddListener(new Action(() => OnSelectCountry(captured)));

                var t = UGUIShip.CreateLabel(rowGo.transform, default, code, FS_SM,
                    isSelected ? WHITE : new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleLeft);
                var lblRt = t.rectTransform;
                lblRt.anchorMin = Vector2.zero;
                lblRt.anchorMax = new Vector2(1f, 1f);
                lblRt.offsetMin = new Vector2(PAD, 0f);
                lblRt.offsetMax = new Vector2(-(FLAG_ICON_SIZE + PAD * 2f), 0f);

                Sprite flagSpr = FlagAssets.LoadFlag(code);
                if (flagSpr != null)
                {
                    var iconGo = new GameObject("FlagIcon");
                    iconGo.transform.SetParent(rowGo.transform, false);
                    var iconRt = iconGo.AddComponent<RectTransform>();
                    iconRt.anchorMin = new Vector2(1f, 0.5f);
                    iconRt.anchorMax = new Vector2(1f, 0.5f);
                    iconRt.pivot = new Vector2(1f, 0.5f);
                    iconRt.anchoredPosition = new Vector2(-PAD, 0f);
                    iconRt.sizeDelta = new Vector2(FLAG_ICON_SIZE * 1.5f, FLAG_ICON_SIZE);
                    var iconImg = iconGo.AddComponent<Image>();
                    iconImg.sprite = flagSpr;
                    iconImg.preserveAspect = true;
                    iconImg.raycastTarget = false;
                }
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollContent);
        }

        private void BuildCustomIconRow(Transform parent, float w)
        {
            float cy = 0f;
            float btnW = w * 0.45f;

            UGUIShip.CreateButton(parent, new Rect(0f, cy, btnW, BTN_H), "Browse...",
                new Color(0.25f, 0.35f, 0.45f, 1f), WHITE, FS_SM, new Action(OnBrowseCustomIcon));

            _customIconLabel = UGUIShip.CreateLabel(parent, new Rect(btnW + PAD, cy, w - btnW - PAD, BTN_H),
                string.IsNullOrEmpty(_customIconPath) ? "No file selected" : Path.GetFileName(_customIconPath), FS_SM, HINT);
            cy += BTN_H + PAD;

            UGUIShip.CreatePanel(parent, new Rect(0f, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            UGUIShip.CreateSlider(parent, 0f, cy, w, "S", _iconScale * 0.5f, LH, PAD, FS_SM,
                val => { _iconScale = val * 2f; SaveIconTransform(); QueueIconTransformApply(); }, null, null, true, 0f, 0.5f);
            cy += LH + SH;
            UGUIShip.CreateSlider(parent, 0f, cy, w, "X", _iconOffsetX + 0.5f, LH, PAD, FS_SM,
                val => { _iconOffsetX = val - 0.5f; SaveIconTransform(); QueueIconTransformApply(); }, null, null, true, 0f, 0.5f);
            cy += LH + SH;
            UGUIShip.CreateSlider(parent, 0f, cy, w, "Y", _iconOffsetY + 0.5f, LH, PAD, FS_SM,
                val => { _iconOffsetY = val - 0.5f; SaveIconTransform(); QueueIconTransformApply(); }, null, null, true, 0f, 0.5f);
        }

        private void BuildPlatformIconButtons(Transform parent, float x, float y, float w, float h)
        {
            var root = new GameObject("PlatformIconButtons");
            root.transform.SetParent(parent, false);
            _platformIconContent = root.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(_platformIconContent, new Rect(x, y, w, h));

            var ids = new System.Collections.Generic.List<string> { "" };
            ids.AddRange(FeatureMorePlatformIcon.PlatformIconIds());
            int iconCols = 5;
            int rows = (ids.Count + iconCols - 1) / iconCols;
            float gap = 4f;
            float cellW = (w - gap * (iconCols - 1)) / iconCols;
            float cellH = (h - gap * (rows - 1)) / rows;

            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                int col = i % iconCols;
                int row = i / iconCols;
                bool selected = string.Equals(_platformCustom, id, StringComparison.OrdinalIgnoreCase);

                var btn = UGUIShip.CreateButton(root.transform,
                    new Rect(col * (cellW + gap), row * (cellH + gap), cellW, cellH),
                    "", Color.clear, WHITE, FS_SM, new Action(() => SetPlatformCustom(id)),
                    customSprite: false);
                btn.name = "PlatformIcon_" + id;
                btn.transition = Selectable.Transition.None;
                var bg = btn.GetComponent<Image>();
                if (bg != null) bg.color = Color.clear;
                var cols = btn.colors;
                cols.normalColor = Color.clear;
                cols.highlightedColor = Color.clear;
                cols.pressedColor = Color.clear;
                cols.disabledColor = Color.clear;
                cols.colorMultiplier = 1f;
                btn.colors = cols;

                var spr = string.IsNullOrEmpty(id) ? NoneIconSprite() : FeatureMorePlatformIcon.SpriteForName(id);
                if (spr != null)
                {
                    var iconGo = new GameObject("Icon");
                    iconGo.transform.SetParent(btn.transform, false);
                    var iconRt = iconGo.AddComponent<RectTransform>();
                    iconRt.anchorMin = new Vector2(0.5f, 0.5f);
                    iconRt.anchorMax = new Vector2(0.5f, 0.5f);
                    iconRt.pivot = new Vector2(0.5f, 0.5f);
                    iconRt.anchoredPosition = Vector2.zero;
                    iconRt.sizeDelta = new Vector2(cellH * 0.78f, cellH * 0.78f);
                    var img = iconGo.AddComponent<Image>();
                    img.sprite = spr;
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                    img.color = selected ? WHITE : ICON_OFF;
                }
            }
        }

        private static Sprite _noneIconSprite;
        private static Sprite NoneIconSprite()
            => _noneIconSprite ??= BetterFG.Utilities.EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.feature.moreplatformicon.no.png");

        private void SetIconMode(IconMode mode)
        {
            _iconMode = mode;
            UGUIShip.SetButtonSelected(_btnNone, mode == IconMode.None, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnFlag, mode == IconMode.Flag, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnCustom, mode == IconMode.Custom, SEL_COLOR);
            RefreshIconModeVisibility();
            RefreshPreview();
        }

        private void RefreshIconModeVisibility()
        {
            if (_flagSection != null) _flagSection.SetActive(_iconMode == IconMode.Flag);
            if (_customSection != null) _customSection.SetActive(_iconMode == IconMode.Custom);
        }

        private void SetPlatformHide(PlatformHideMode mode)
        {
            _platformHide = mode;
            UGUIShip.SetButtonSelected(_btnPlatNone, mode == PlatformHideMode.None, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnPlatSelf, mode == PlatformHideMode.Self, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnPlatEveryone, mode == PlatformHideMode.Everyone, SEL_COLOR);

            string modeStr = mode == PlatformHideMode.Self ? "self" : mode == PlatformHideMode.Everyone ? "everyone" : "none";
            SettingsService.Set(KEY_PLATFORM_HIDE, modeStr);
            NametagIconApplicator.ApplyPlatformIcon();
            NametagIconApplicator.ApplyKnownPlatformIcons();
            RefreshPreview();
        }

        private void SetPlatformCustom(string id)
        {
            _platformCustom = string.Equals(_platformCustom, id, StringComparison.OrdinalIgnoreCase) ? "" : (id ?? "");
            SettingsService.Set(KEY_PLATFORM_CUSTOM, _platformCustom);
            RefreshPlatformIconButtons();

            if (string.IsNullOrEmpty(_platformCustom))
                NametagIconApplicator.RestoreKnownPlatformIcons();
            else
                NametagIconApplicator.ApplyPlatformIcon();
            NametagIconApplicator.ApplyKnownPlatformIcons();
            NametagFinder.ReapplyAllNameplates();
            RefreshPreview();
        }

        private void RefreshPlatformIconButtons()
        {
            if (_platformIconContent == null) return;
            for (int i = 0; i < _platformIconContent.childCount; i++)
            {
                var child = _platformIconContent.GetChild(i);
                if (child == null) continue;
                var img = child.Find("Icon")?.GetComponent<Image>();
                if (img == null) continue;
                string id = child.name.StartsWith("PlatformIcon_") ? child.name.Substring("PlatformIcon_".Length) : "";
                img.color = string.Equals(_platformCustom, id, StringComparison.OrdinalIgnoreCase) ? WHITE : ICON_OFF;
            }
        }

        private void OnSelectCountry(string code)
        {
            _selectedCountry = code;
            if (_scrollContent == null) return;

            Sprite btnSpr = UGUIShip.GetButtonSprite();
            for (int i = 0; i < _scrollContent.childCount; i++)
            {
                var child = _scrollContent.GetChild(i);
                if (child == null) continue;
                bool sel = child.name == "ISO_" + code;
                var img = child.GetComponent<Image>();
                if (img != null)
                {
                    if (sel && btnSpr != null)
                    {
                        img.sprite = btnSpr;
                        img.type = Image.Type.Simple;
                        img.color = SEL_COLOR;
                    }
                    else
                    {
                        img.sprite = null;
                        img.color = Color.clear;
                    }
                }
                var lbl = child.GetComponentInChildren<Text>();
                if (lbl != null) lbl.color = sel ? WHITE : new Color(1f, 1f, 1f, 0.7f);
            }
            RefreshPreview();
        }

        private void SaveIconTransform()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            SettingsService.Set(KEY_ICON_SCALE, _iconScale.ToString(ci));
            SettingsService.Set(KEY_ICON_OFFSET_X, _iconOffsetX.ToString(ci));
            SettingsService.Set(KEY_ICON_OFFSET_Y, _iconOffsetY.ToString(ci));
        }

        private void QueueIconTransformApply()
        {
            _iconTransformApplyPending = true;
            if (_iconTransformApplyRoutine == null)
                _iconTransformApplyRoutine = StartCoroutine(ApplyIconTransformLoop().WrapToIl2Cpp());
        }

        private IEnumerator ApplyIconTransformLoop()
        {
            while (_iconTransformApplyPending)
            {
                _iconTransformApplyPending = false;
                NametagIconApplicator.ApplyNametag();
                yield return new WaitForSeconds(0.1f);
            }
            _iconTransformApplyRoutine = null;
        }

        private void OnBrowseCustomIcon()
        {
            WinDialogs.PickImage("Select icon (PNG or GIF)", path =>
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                _customIconPath = path;
                if (_customIconLabel != null) _customIconLabel.text = Path.GetFileName(_customIconPath);
                RefreshPreview();
            });
        }

        public override void RefreshPreview()
        {
            var cfg = NametagIconApplicator.CfgFromSettings();
            cfg.enabled = true;
            cfg.iconMode = _iconMode == IconMode.Flag ? "flag" : _iconMode == IconMode.Custom ? "custom" : "none";
            cfg.iconCountry = _selectedCountry ?? "";
            cfg.iconPath = _customIconPath ?? "";
            bool platHide = _platformHide == PlatformHideMode.Self || _platformHide == PlatformHideMode.Everyone;
            ApplyPreview(cfg, CrownRankService.CfgFromSettings(), platHide, _platformCustom);
        }

        protected override bool Save()
        {
            string modeStr = _iconMode == IconMode.Flag ? "flag" : _iconMode == IconMode.Custom ? "custom" : "none";
            SettingsService.Set(KEY_ICON_MODE, modeStr);
            SettingsService.Set(KEY_ICON_COUNTRY, _selectedCountry ?? "");
            SettingsService.Set(KEY_ICON_PATH, _customIconPath ?? "");
            SaveIconTransform();

            NametagIconApplicator.ApplyNametag();
            NametagIconApplicator.ApplyPlatformIcon();
            NametagFinder.ReapplyAllNameplates();
            return true;
        }

        protected override void OnRemoveClicked()
        {
            SettingsService.Set(KEY_ICON_MODE, "none");
            NametagIconApplicator.ApplyNametag();
            NametagFinder.ReapplyAllNameplates();
        }
    }
}
