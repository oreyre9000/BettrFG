using System;
using BetterFG.Features;
using BetterFG.UI;
using BetterFG.Utilities;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class FeaturesTab : Tab
    {
        public FeaturesTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Features";
        protected override string TitleId => "ui.features";
        protected override string BgResource => "BetterFG.assets.ui.tab.features.png";

        static readonly Color WHITE = UGUIShip.WHITE;
        static readonly Color ROW_BG = new Color(0f, 0f, 0f, 0.55f);
        static readonly Color HEADER_BG = new Color(0f, 0f, 0f, 0.82f);
        // delux buttons render normalColor at 0.4x — pick a color that still reads as green after that
        static readonly Color ON = new Color(0.7f, 1.4f, 0.7f, 1f);
        static readonly Color OFF = new Color(0f, 0f, 0f, 1f);


        const float HEADER_H = 58f;
        const float CAROUSEL_H = 30f;
        const float SETTING_H = 26f;
        const float TOGGLE_W = 54f;
        const float TOGGLE_H = 18f;
        const float PREVIEW_W = 168f;
        const float PREVIEW_H = 126f;

        static readonly System.Collections.Generic.Dictionary<string, Sprite> _featurePics = new System.Collections.Generic.Dictionary<string, Sprite>();
        RectTransform _listRt;
        RectTransform _scrollRt;
        Text _carouselLabel;
        int _selected;
        float _contentW;

        protected override void BuildBackground(RectTransform root)
        {
            base.BuildBackground(root);
            var bgRt = root.Find("BG") as RectTransform;
            if (bgRt != null) bgRt.offsetMax = new Vector2(0f, 1f);
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            var all = FeatureRegistry.all;
            if (all.Count == 0) return;

            _selected = Mathf.Clamp(_selected, 0, all.Count - 1);
            var names = new string[all.Count];
            for (int i = 0; i < all.Count; i++) names[i] = all[i].title;

            _carouselLabel = UGUIShip.CreateCarousel(contentRoot,
                new Rect(PAD, VPAD, TabWidth - PAD * 2f, CAROUSEL_H), names, _selected,
                new Action<int>(step =>
                {
                    var list = FeatureRegistry.all;
                    _selected = (_selected + step + list.Count) % list.Count;
                    UGUIShip.RelabelText(_carouselLabel, list[_selected].title);
                    if (IsOpen) UiTransition.Run(_scrollRt, RefreshRows);
                    else RefreshRows();
                }), null, FS + 1);

            float top = VPAD + CAROUSEL_H + 6f;
            var scrollRect = new Rect(PAD, top, TabWidth - PAD * 2f, TabHeight - top - VPAD);
            _contentW = scrollRect.width - 26f;
            var scroll = UGUIShip.CreateScrollView(contentRoot, scrollRect);
            scroll.scrollRect.scrollSensitivity = 45f;
            _listRt = scroll.content;
            _scrollRt = scroll.scrollRect.GetComponent<RectTransform>();

            var vlg = _listRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 0f;
            vlg.padding = new RectOffset(0, 0, 0, 0);

            _listRt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            RefreshRows();
        }

        void RefreshRows()
        {
            if (_listRt == null) return;
            for (int i = _listRt.childCount - 1; i >= 0; i--)
                Destroy(_listRt.GetChild(i).gameObject);

            BuildFeature(_selected);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_listRt);
        }

        void BuildFeature(int featureIndex)
        {
            var feature = FeatureRegistry.all[featureIndex];
            var rowGo = new GameObject("Feature_" + feature.id);
            rowGo.transform.SetParent(_listRt, false);
            rowGo.AddComponent<RectTransform>();
            UGUIShip.PaintStaticRowFill(rowGo, HEADER_BG);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = HEADER_H;
            le.flexibleWidth = 1f;

            float w = _contentW > 0f ? _contentW : TabWidth - PAD * 4f;
            AddFeaturePicture(rowGo.transform, feature.id);

            var title = UGUIShip.CreateLabel(rowGo.transform, new Rect(PAD + 18f, PAD + 5f, w - TOGGLE_W - PAD * 2f - 18f, BTN_H),
                feature.title, FS + 1, WHITE, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            title.transform.localScale = new Vector3(1.06f, 1.06f, 1f);

            UGUIShip.CreateButton(rowGo.transform,
                new Rect(w - TOGGLE_W, HEADER_H - TOGGLE_H - PAD, TOGGLE_W, TOGGLE_H),
                feature.enabled ? "ui.on" : "ui.off",
                feature.enabled ? ON : OFF,
                WHITE, FS_SM,
                new Action(() =>
                {
                    feature.SetEnabled(!feature.enabled);
                    RefreshRows();
                }));

            if (feature.id == "customizefallguys") BuildPreviewRow();

            var settings = feature.settings;
            int rowCount = 0;
            if (settings != null)
            {
                for (int i = 0; i < settings.Count; i++)
                {
                    var section = settings[i].section;
                    if (!string.IsNullOrEmpty(section) && (i == 0 || settings[i - 1].section != section))
                        BuildSectionRow(feature, section);
                    BuildSettingRow(featureIndex, i, i % 2 == 0 ? ROW_BG : Color.clear);
                }
                rowCount = settings.Count;
            }

            // every declared choice auto-renders as a dropdown row — no per-feature special casing.
            var choices = feature.choices;
            if (choices != null)
            {
                for (int i = 0; i < choices.Count; i++)
                {
                    BuildChoiceRow(feature, choices[i], rowCount % 2 == 0 ? ROW_BG : Color.clear);
                    rowCount++;
                }
            }

            var ranges = feature.ranges;
            if (ranges != null)
            {
                for (int i = 0; i < ranges.Count; i++)
                {
                    BuildRangeRow(feature, ranges[i], rowCount % 2 == 0 ? ROW_BG : Color.clear);
                    rowCount++;
                }
            }

            // finish placement gets a stepper row: how many spots to show on the leaderboard.
            if (feature.id == "timeplacement")
                BuildMaxRowsRow(feature, rowCount % 2 == 0 ? ROW_BG : Color.clear);
        }

        // renders a FeatureChoice as a single-select dropdown row, wired straight to the feature's
        // GetChoice/SetChoice so the saved pick and its onChoiceChanged callback are handled for us.
        void BuildChoiceRow(BfgFeature feature, FeatureChoice choice, Color bg)
        {
            var rowGo = new GameObject("Choice_" + feature.id + "_" + choice.id);
            rowGo.transform.SetParent(_listRt, false);
            rowGo.AddComponent<RectTransform>();
            UGUIShip.PaintStaticRowFill(rowGo, bg);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = SETTING_H;
            le.flexibleWidth = 1f;

            float w = _contentW > 0f ? _contentW : TabWidth - PAD * 4f;

            var labels = choice.optionLabels;
            // size the dropdown to the longest option so nothing gets clipped. estimate text width
            // from char count at the dropdown font (~0.62*fontSize per char in this UI's font), plus
            // room for the arrow/margins. min 110 so short lists still look uniform.
            int longest = 0;
            for (int i = 0; i < labels.Count; i++) if (labels[i].Length > longest) longest = labels[i].Length;
            float ddW = Mathf.Max(110f, longest * FS_SM * 0.62f + 28f);

            var choiceLabel = UGUIShip.CreateLabel(rowGo.transform,
                new Rect(PAD * 3f, 0f, w - ddW - PAD * 4f, SETTING_H),
                choice.label,
                FS,
                feature.enabled ? new Color(1f, 1f, 1f, 0.86f) : new Color(1f, 1f, 1f, 0.35f),
                TextAnchor.MiddleLeft);

            AttachHint(choiceLabel, choice.hint);

            int selected = choice.optionIds.IndexOf(feature.GetChoice(choice.id));
            if (selected < 0) selected = 0;

            var initial = new System.Collections.Generic.List<bool>();
            for (int i = 0; i < labels.Count; i++) initial.Add(i == selected);
            Button ddBtn = null;
            ddBtn = UGUIShip.CreateMultiSelectDropdown(rowGo.transform,
                new Rect(w - ddW, (SETTING_H - TOGGLE_H) * 0.5f - 2f, ddW, TOGGLE_H + 4f),
                labels[selected], labels, initial,
                new Action<int, bool>((idx, _) =>
                {
                    if (idx < 0 || idx >= choice.optionIds.Count) return;
                    feature.SetChoice(choice.id, choice.optionIds[idx]);
                    var lbl = ddBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, labels[idx]);
                }), FS_SM, ddW, 20f, true, true, true);
        }

        void BuildPreviewRow()
        {
            var rowGo = new GameObject("Preview_customizefallguys");
            rowGo.transform.SetParent(_listRt, false);
            rowGo.AddComponent<RectTransform>();
            UGUIShip.PaintStaticRowFill(rowGo, ROW_BG);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = PREVIEW_H + PAD * 2f;
            le.flexibleWidth = 1f;

            float w = _contentW > 0f ? _contentW : TabWidth - PAD * 4f;

            var go = new GameObject("Render");
            go.transform.SetParent(rowGo.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(w * 0.5f, -PAD);
            rt.sizeDelta = new Vector2(PREVIEW_W, PREVIEW_H);

            var (_, inner) = UGUIShip.CreateFramedImage(go.transform);
            var raw = inner.gameObject.AddComponent<RawImage>();
            raw.texture = Features.CustomizeFallGuys.FeatureCustomizeFallGuys.PreviewTexture;
            raw.raycastTarget = false;

            Features.CustomizeFallGuys.FeatureCustomizeFallGuys.SetPreviewPanel(go);
        }

        static void AttachHint(Text label, string hint)
        {
            if (string.IsNullOrEmpty(hint)) return;

            var labelRt = label.rectTransform;
            var hoverGo = new GameObject("HoverBG");
            hoverGo.transform.SetParent(labelRt, false);
            var hoverRt = hoverGo.AddComponent<RectTransform>();
            hoverRt.anchorMin = Vector2.zero;
            hoverRt.anchorMax = Vector2.one;
            hoverRt.offsetMin = Vector2.zero;
            hoverRt.offsetMax = Vector2.zero;
            var hoverImg = hoverGo.AddComponent<Image>();
            UGUIShip.ApplyDeluxColorFill(hoverImg);
            hoverImg.color = new Color(1f, 1f, 1f, 0.04f);
            hoverImg.raycastTarget = false;
            hoverGo.transform.SetSiblingIndex(0);
            hoverGo.SetActive(false);

            var trig = TooltipTrigger.Attach(labelRt.gameObject);
            trig.text = hint;
            trig.hoverImage = hoverGo;
        }

        void BuildRangeRow(BfgFeature feature, FeatureRange range, Color bg)
        {
            var rowGo = new GameObject("Range_" + feature.id + "_" + range.id);
            rowGo.transform.SetParent(_listRt, false);
            rowGo.AddComponent<RectTransform>();
            UGUIShip.PaintStaticRowFill(rowGo, bg);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = SETTING_H;
            le.flexibleWidth = 1f;

            float w = _contentW > 0f ? _contentW : TabWidth - PAD * 4f;
            const float SLIDER_W = 118f;
            const float VAL_W = 40f;

            var label = UGUIShip.CreateLabel(rowGo.transform,
                new Rect(PAD * 3f, 0f, w - SLIDER_W - VAL_W - PAD * 5f, SETTING_H),
                range.label,
                FS,
                feature.enabled ? new Color(1f, 1f, 1f, 0.86f) : new Color(1f, 1f, 1f, 0.35f),
                TextAnchor.MiddleLeft);
            AttachHint(label, range.hint);

            int decimals = range.step >= 1f ? 0 : range.step >= 0.1f ? 1 : 2;
            string Fmt(float v) => v.ToString("F" + decimals, System.Globalization.CultureInfo.InvariantCulture);

            var readout = UGUIShip.CreateLabel(rowGo.transform,
                new Rect(w - VAL_W, 0f, VAL_W, SETTING_H),
                Fmt(feature.GetRange(range.id)), FS_SM, WHITE, TextAnchor.MiddleRight);

            UGUIShip.CreateSlider(rowGo.transform,
                w - SLIDER_W - VAL_W - PAD, (SETTING_H - TOGGLE_H) * 0.5f, SLIDER_W,
                "", Mathf.InverseLerp(range.min, range.max, feature.GetRange(range.id)),
                TOGGLE_H, PAD, FS_SM,
                new Action<float>(t =>
                {
                    float v = Mathf.Round(Mathf.Lerp(range.min, range.max, t) / range.step) * range.step;
                    feature.SetRange(range.id, v);
                    readout.text = Fmt(v);
                }),
                null, null, false,
                Mathf.InverseLerp(range.min, range.max, range.defaultValue));
        }

        void BuildMaxRowsRow(BfgFeature feature, Color bg)
        {
            var rowGo = new GameObject("Setting_timeplacement_maxrows");
            rowGo.transform.SetParent(_listRt, false);
            rowGo.AddComponent<RectTransform>();
            UGUIShip.PaintStaticRowFill(rowGo, bg);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = SETTING_H;
            le.flexibleWidth = 1f;

            float w = _contentW > 0f ? _contentW : TabWidth - PAD * 4f;
            const float INC_W = 96f;

            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(PAD * 3f, 0f, w - INC_W - PAD * 4f, SETTING_H),
                "ui.players_to_show",
                FS,
                feature.enabled ? new Color(1f, 1f, 1f, 0.86f) : new Color(1f, 1f, 1f, 0.35f),
                TextAnchor.MiddleLeft);

            string K = Features.TimePlacement.FeatureTimePlacement.MaxRowsKey;
            int Cur() => int.TryParse(Services.SettingsService.Get(K, Features.TimePlacement.FeatureTimePlacement.MaxRowsDefault.ToString()), out int v)
                ? v : Features.TimePlacement.FeatureTimePlacement.MaxRowsDefault;

            UGUIShip.CreateIncrement(rowGo.transform,
                new Rect(w - INC_W, (SETTING_H - TOGGLE_H) * 0.5f, INC_W, TOGGLE_H),
                Features.TimePlacement.FeatureTimePlacement.MaxRowsMin,
                Features.TimePlacement.FeatureTimePlacement.MaxRowsMax,
                Cur, n => Services.SettingsService.Set(K, n.ToString()), false, FS_SM);
        }


        void AddFeaturePicture(Transform parent, string id)
        {
            var sprite = FeaturePic(id);
            if (sprite == null) return;

            var go = new GameObject("Picture");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-TOGGLE_W - PAD * 2.8f, 0f);
            rt.sizeDelta = new Vector2(74f, 54f);

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0.22f);
        }

        static Sprite FeaturePic(string id)
        {
            if (_featurePics.TryGetValue(id, out var cached)) return cached;

            string res = id == "pb"
                ? "BetterFG.assets.ui.feature.qualificationtime.featurequalificationtime_icon.png"
                : id == "stars"
                    ? "BetterFG.assets.ui.feature.star.featurestar_star.png"
                    : id == "moreplatformicon"
                        ? "BetterFG.assets.ui.feature.moreplatformicon.featuremoreplatformicon_platformicons.png"
                        : "BetterFG.assets.ui.tab.menu.png";

            var sprite = EmbeddedResourceandUnity.LoadSprite(res, 100f);
            _featurePics[id] = sprite;
            return sprite;
        }

        void BuildSectionRow(BfgFeature feature, string section)
        {
            var rowGo = new GameObject("Section_" + feature.id + "_" + section);
            rowGo.transform.SetParent(_listRt, false);
            rowGo.AddComponent<RectTransform>();
            UGUIShip.PaintStaticRowFill(rowGo, HEADER_BG);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = SETTING_H;
            le.flexibleWidth = 1f;

            float w = _contentW > 0f ? _contentW : TabWidth - PAD * 4f;
            UGUIShip.CreateLabel(rowGo.transform, new Rect(PAD * 2f, 0f, w - PAD * 3f, SETTING_H),
                section, FS, feature.enabled ? WHITE : new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
        }

        void BuildSettingRow(int featureIndex, int settingIndex, Color bg)
        {
            var feature = FeatureRegistry.all[featureIndex];
            var setting = feature.settings[settingIndex];
            var rowGo = new GameObject("Setting_" + feature.id + "_" + setting.id);
            rowGo.transform.SetParent(_listRt, false);
            rowGo.AddComponent<RectTransform>();
            UGUIShip.PaintStaticRowFill(rowGo, bg);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = SETTING_H;
            le.flexibleWidth = 1f;

            float w = _contentW > 0f ? _contentW : TabWidth - PAD * 4f;
            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(PAD * 3f, 0f, w - TOGGLE_W - PAD * 4f, SETTING_H),
                setting.label,
                FS,
                feature.enabled ? new Color(1f, 1f, 1f, 0.86f) : new Color(1f, 1f, 1f, 0.35f),
                TextAnchor.MiddleLeft);

            bool on = feature.GetRaw(setting.id);
            UGUIShip.CreateButton(rowGo.transform,
                new Rect(w - TOGGLE_W, (SETTING_H - TOGGLE_H) * 0.5f, TOGGLE_W, TOGGLE_H),
                on ? "ui.on" : "ui.off",
                on ? ON : OFF,
                WHITE, FS_SM,
                new Action(() =>
                {
                    feature.Set(setting.id, !feature.GetRaw(setting.id));
                    RefreshRows();
                }));
        }

    }
}
