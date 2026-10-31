using System;
using System.Collections.Generic;
using BetterFG.Customization.Player;
using UnityEngine;
using UnityEngine.UI;
using LayoutElement = UnityEngine.UI.LayoutElement;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    // material properties belong to a MATERIAL, not a texture slot - this is its own tab (reached
    // from the wizard's Png step, "‹ back" returns there) instead of a step or an in-wizard overlay,
    // so it isn't forced on anyone and isn't gated on which texture row happens to be selected.
    // the wizard hands over its whole WIP entry as plain fields below before switching here, and
    // "‹ back" hands the (possibly edited) fields right back so the wizard resumes mid-edit.
    public class SkinTextureMaterialPropsTab : SwitchTab
    {
        public SkinTextureMaterialPropsTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Skin Texture - Material Props";
        protected override string TitleId => "ui.skin_texture_material_props";
        protected override string BgResource => "BetterFG.assets.ui.tab.customskintexture.png";
        protected override string SwitchLabel => "ui.back_4";

        protected override Tab MakeSwitchTarget()
        {
            var wizard = BetterFGTabRegistry.NewTab<SkinTextureWizardTab>();
            wizard.ResumeSource = this;
            return wizard;
        }

        // null = the shared global catalog; non-null = some other owner's own list (a pet's
        // PetData.skinTexEntries) - see SkinTextureWizardTab.TargetEntries
        public List<SkinTexEntry> TargetEntries;
        public Func<Tab> OwnerListTab;

        private static float ROW_H => 24f * UIScale.S;

        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color LABEL = new Color(1f, 1f, 1f, 0.72f);
        private static readonly Color WHITE = UGUIShip.WHITE;

        public int EditIndex = -1;
        public string Category = SkinTexCategory.Upper;
        public string CostumeName = "";
        public readonly List<Material> Mats = new List<Material>();
        public readonly List<string> MatNames = new List<string>();
        public readonly Dictionary<string, string> OverridePaths = new Dictionary<string, string>();
        public readonly Dictionary<string, MatPropOverride> MatProps = new Dictionary<string, MatPropOverride>();
        public int MatIdx = -1;
        public string EntryName = "";

        private float RowW => TabWidth - PAD * 2f - UGUIShip.SCROLLBAR_INSET * 2f;

        private RectTransform _matDropdownArea;
        private GameObject _matDropdownGo;
        private RectTransform _propContent;
        private Text _statusLbl;

        private readonly List<Material> _distinctMats = new List<Material>();
        private readonly List<string> _distinctMatNames = new List<string>();
        private int _propMatIdx = -1;
        private bool _isOptionField;

        protected override void BuildContent(RectTransform contentRoot)
        {
            _isOptionField = SkinTexCategory.IsOptionField(Category);

            float w = TabWidth - PAD * 2f;
            float y = VPAD;

            UGUIShip.CreateLabel(contentRoot, new Rect(PAD, y, w, LH), _isOptionField ? "ui.option" : "ui.material", FS_SM, LABEL);
            y += LH + SH;

            var areaGo = new GameObject("MatDropdownArea");
            areaGo.transform.SetParent(contentRoot, false);
            _matDropdownArea = areaGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(_matDropdownArea, new Rect(PAD, y, w, BTN_H));
            y += BTN_H + SH;

            float statusH = LH;
            float scrollH = TabHeight - y - VPAD - statusH - SH;
            var scroll = UGUIShip.CreateScrollView(contentRoot, new Rect(PAD, y, w, scrollH));
            _propContent = scroll.content;
            var layout = _propContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 3f;
            _propContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            y += scrollH + SH;

            _statusLbl = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, y, w, statusH), "", FS_SM, HINT, TextAnchor.MiddleCenter);

            if (_isOptionField)
            {
                _distinctMats.Clear();
                _distinctMatNames.Clear();
                _distinctMatNames.Add(string.IsNullOrEmpty(CostumeName) ? Category : CostumeName);
                _propMatIdx = 0;
            }
            else
            {
                _distinctMats.Clear();
                _distinctMatNames.Clear();
                for (int i = 0; i < Mats.Count; i++)
                {
                    string mn = SkinApplicationService.CleanMatName(Mats[i].name);
                    if (string.IsNullOrEmpty(mn) || _distinctMatNames.Contains(mn)) continue;
                    _distinctMats.Add(Mats[i]);
                    _distinctMatNames.Add(mn);
                }
                _propMatIdx = MatIdx >= 0 && MatIdx < Mats.Count ? _distinctMats.IndexOf(Mats[MatIdx]) : -1;
                if (_propMatIdx < 0) _propMatIdx = 0;
            }

            RebuildMatDropdown();
            RebuildPropRows();
            PositionSwitchLink();
        }

        private void SetStatus(string msg)
        {
            if (_statusLbl != null) UGUIShip.RelabelText(_statusLbl, msg);
        }

        private void RebuildMatDropdown()
        {
            if (_matDropdownGo != null) GameObject.Destroy(_matDropdownGo);
            float w = TabWidth - PAD * 2f;
            var dd = UGUIShip.CreateDropdown(_matDropdownArea, new Rect(0f, 0f, w, BTN_H),
                _distinctMatNames, _propMatIdx, new Action<int>(idx => { _propMatIdx = idx; RebuildPropRows(); }), FS_SM);
            _matDropdownGo = dd.gameObject;
        }

        private void RebuildPropRows()
        {
            for (int i = _propContent.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_propContent.GetChild(i).gameObject);

            if (_isOptionField)
            {
                BuildOptionFieldRows();
                return;
            }

            if (_propMatIdx < 0 || _propMatIdx >= _distinctMats.Count)
            {
                UGUIShip.CreateLabel(_propContent, new Rect(6f, 0f, RowW, ROW_H), "ui.pick_a_material_above", FS_SM, HINT);
                return;
            }

            var mat = _distinctMats[_propMatIdx];
            string matName = _distinctMatNames[_propMatIdx];

            bool any = false;
            foreach (var (propName, type, rangeMin, rangeMax) in SkinApplicationService.GetEditableProps(mat))
            {
                any = true;
                if (type == UnityEngine.Rendering.ShaderPropertyType.Color)
                    BuildColorPropRow(matName, propName, () => mat.GetColor(propName));
                else if (type == UnityEngine.Rendering.ShaderPropertyType.Vector)
                    BuildVectorPropRow(matName, propName, () => mat.GetVector(propName));
                else
                {
                    bool isInt = type == UnityEngine.Rendering.ShaderPropertyType.Int;
                    string key = matName + "|" + propName;
                    float current = MatProps.TryGetValue(key, out var existing) ? existing.f : mat.GetFloat(propName);
                    BuildFloatPropRow(matName, propName, current, !isInt);
                }
            }

            if (!any)
                UGUIShip.CreateLabel(_propContent, new Rect(6f, 0f, RowW, ROW_H),
                    "ui.this_material_has_no_editable_properties", FS_SM, HINT);
        }

        private void BuildOptionFieldRows()
        {
            string matName = "@" + Category;
            var opt = SkinApplicationService.FindOptionByName(Category, CostumeName);
            if (opt == null)
                UGUIShip.CreateLabel(_propContent, new Rect(6f, 0f, RowW, LH),
                    CostumeName + " isn't loaded right now - its default values won't show, but overrides still save", FS_SM, HINT);

            foreach (var (prop, kind) in SkinApplicationService.GetOptionFields(Category))
            {
                Color defC = Color.white; float defF = 0f;
                if (opt != null) SkinApplicationService.TryReadOptionField(opt, prop, out _, out defC, out defF);
                if (kind == "color") BuildColorPropRow(matName, prop, () => defC);
                else
                {
                    string key = matName + "|" + prop;
                    float current = MatProps.TryGetValue(key, out var existing) ? existing.f : defF;
                    BuildFloatPropRow(matName, prop, current, true);
                }
            }
        }

        private void BuildFloatPropRow(string matName, string propName, float current, bool isFloat)
        {
            var rowGo = new GameObject("Prop_" + propName);
            rowGo.transform.SetParent(_propContent, false);
            rowGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, ROW_H);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;

            float incW = 130f * UIScale.S;
            float labelW = RowW - incW - 4f;
            UGUIShip.CreateLabel(rowGo.transform, new Rect(0f, 0f, labelW, ROW_H), propName, FS_SM - 1, WHITE, TextAnchor.MiddleLeft);

            UGUIShip.CreateIncrement(rowGo.transform, new Rect(labelW + 4f, 0f, incW, ROW_H),
                -1e6f, 1e6f, () => current, v =>
                {
                    current = v;
                    string key = matName + "|" + propName;
                    MatProps[key] = new MatPropOverride { matName = matName, prop = propName, kind = "float", f = v };
                    ApplyLivePreview();
                    SetStatus(propName + " = " + v);
                }, isFloat ? 0.1f : 1f, isFloat, wrap: false, fontSize: FS_SM - 1);
        }

        private void BuildColorPropRow(string matName, string propName, Func<Color> readInitial)
        {
            string key = matName + "|" + propName;
            Color cur = MatProps.TryGetValue(key, out var existing)
                ? new Color(existing.x, existing.y, existing.z, existing.w)
                : readInitial();

            float rowH2 = ROW_H * 2f + 2f;
            var rowGo = new GameObject("Prop_" + propName);
            rowGo.transform.SetParent(_propContent, false);
            rowGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, rowH2);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = rowH2;
            le.flexibleWidth = 1f;

            UGUIShip.CreateLabel(rowGo.transform, new Rect(0f, ROW_H, RowW, ROW_H), propName, FS_SM - 1, WHITE, TextAnchor.MiddleLeft);

            void Push()
            {
                MatProps[key] = new MatPropOverride
                {
                    matName = matName,
                    prop = propName,
                    kind = "color",
                    x = cur.r,
                    y = cur.g,
                    z = cur.b,
                    w = cur.a
                };
                ApplyLivePreview();
            }

            float gap = 3f;
            float compW = (RowW - gap * 3f) / 4f;

            void Comp(int idx, float x, string glyph)
            {
                UGUIShip.CreateIncrement(rowGo.transform, new Rect(x, 0f, compW, ROW_H),
                    -1e6f, 1e6f, () => cur[idx], v => { cur[idx] = v; Push(); SetStatus(propName + " " + glyph + " = " + v); },
                    0.05f, true, wrap: false, fontSize: FS_SM - 2);
            }

            Comp(0, 0f, "R");
            Comp(1, compW + gap, "G");
            Comp(2, (compW + gap) * 2f, "B");
            Comp(3, (compW + gap) * 3f, "A");
        }

        private void BuildVectorPropRow(string matName, string propName, Func<Vector4> readInitial)
        {
            string key = matName + "|" + propName;
            Vector4 cur = MatProps.TryGetValue(key, out var existing)
                ? new Vector4(existing.x, existing.y, existing.z, existing.w)
                : readInitial();

            float rowH2 = ROW_H * 2f + 2f;
            var rowGo = new GameObject("Prop_" + propName);
            rowGo.transform.SetParent(_propContent, false);
            rowGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, rowH2);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = rowH2;
            le.flexibleWidth = 1f;

            UGUIShip.CreateLabel(rowGo.transform, new Rect(0f, ROW_H, RowW, ROW_H),
                propName + " (x,y,z,w)", FS_SM - 1, WHITE, TextAnchor.MiddleLeft);

            float gap = 3f;
            float compW = (RowW - gap * 3f) / 4f;

            void Comp(int idx, float x)
            {
                UGUIShip.CreateIncrement(rowGo.transform, new Rect(x, 0f, compW, ROW_H),
                    -1e6f, 1e6f, () => cur[idx], v =>
                    {
                        cur[idx] = v;
                        MatProps[key] = new MatPropOverride
                        {
                            matName = matName,
                            prop = propName,
                            kind = "vector",
                            x = cur.x,
                            y = cur.y,
                            z = cur.z,
                            w = cur.w
                        };
                        ApplyLivePreview();
                        SetStatus(propName + " updated");
                    }, 0.1f, true, wrap: false, fontSize: FS_SM - 2);
            }

            Comp(0, 0f);
            Comp(1, compW + gap);
            Comp(2, (compW + gap) * 2f);
            Comp(3, (compW + gap) * 3f);
        }

        // only the global catalog live-previews onto the local player's own beans - a pet's own
        // material props have no live local-player bean to preview onto, they apply next time the
        // pet is (re)spawned instead (PetBeanBuilder)
        private void ApplyLivePreview()
        {
            if (TargetEntries != null) return;
            if (SkinApplicationService.Instance == null) return;
            if (_isOptionField)
            {
                var entry = new SkinTexEntry
                {
                    entryName = EntryName,
                    enabled = true,
                    category = Category,
                    costumeName = CostumeName
                };
                entry.matProps.AddRange(MatProps.Values);
                SkinApplicationService.PreviewOptionOverride(entry);
                return;
            }
            var props = new List<MatPropOverride>(MatProps.Values);
            foreach (var bean in SkinApplicationService.GatherBeans())
                SkinApplicationService.Instance.ApplyMatProps(bean, props);
        }
    }
}
