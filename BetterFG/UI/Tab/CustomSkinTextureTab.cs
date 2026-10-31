using System;
using System.Collections.Generic;
using System.IO;
using BetterFG.Customization.Player;
using BetterFG.Services;
using FG.Common;
using FG.Common.CMS;
using UnityEngine;
using UnityEngine.UI;
using LayoutElement = UnityEngine.UI.LayoutElement;

namespace BetterFG.UI.Tab
{
    public class SkinTexEntry
    {
        public string entryName;
        public string texPath;
        public int matIdx;
        public bool enabled;
        public string costumeName;

        public List<Material> mats = new List<Material>();
        public List<string> matNames = new List<string>();
    }

    public class CustomSkinTextureTab : BetterFGTab
    {
        public CustomSkinTextureTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Skin Texture";
        protected override string BgResource => "BetterFG.assets.ui.tab.customskintexture.png";

        private static readonly Color BTN_DARK = new Color(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Color BTN_APPLY = new Color(0.25f, 0.45f, 0.25f, 1f);
        private static readonly Color BTN_REMOVE = new Color(0.55f, 0.15f, 0.15f, 1f);
        private static readonly Color BTN_ADD = new Color(0.3f, 0.3f, 0.15f, 1f);
        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color DIM = new Color(1f, 1f, 1f, 0.4f);
        private static readonly Color WHITE = Color.white;
        private static readonly Color ROW_A = new Color(0.12f, 0.12f, 0.12f, 1f);
        private static readonly Color ROW_B = new Color(0.09f, 0.09f, 0.09f, 1f);
        private static readonly Color ROW_SELECTED = new Color(0.25f, 0.45f, 0.25f, 1f);

        private static float ROW_H => 30f * UIScale.S;

        private const string KEY_ENTRY_COUNT = "skintex.entryCount";
        private static string EK(int i, string f) => $"skintex.entry.{i}.{f}";

        private List<SkinTexEntry> _entries = new List<SkinTexEntry>();
        private int _selectedEntry = -1;

        private RectTransform _entryContent;
        private Text _statusLbl;

        private struct RowRefs
        {
            public Image Bg;
            public Button Toggle;
            public Text ToggleLbl;
            public Text Name;
        }

        private readonly List<RowRefs> _rows = new List<RowRefs>();

        protected override void BuildContent(RectTransform contentRoot)
        {
            _entries = LoadEntriesFromSettings();

            float w = TabWidth - PAD * 2f;
            float y = VPAD;

            float listH = TabHeight - y - BTN_H - LH - VPAD - 4f;
            var scroll = UGUIShip.CreateScrollView(contentRoot, new Rect(PAD, y, w, listH));
            _entryContent = scroll.content;
            var vlg = _entryContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(2, 2, 2, 2);
            vlg.spacing = 2f;
            vlg.childControlHeight = false;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            _entryContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            y += listH + 2f;

            float halfW = (w - PAD * 0.5f) / 2f;
            UGUIShip.CreateButton(contentRoot, new Rect(PAD, y, halfW, BTN_H),
                "Apply Selected", BTN_APPLY, WHITE, FS_SM, new Action(OnApplySelected));
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + halfW + PAD * 0.5f, y, halfW, BTN_H),
                "Revert All", BTN_REMOVE, WHITE, FS_SM, new Action(OnRevert));
            y += BTN_H + 2f;

            _statusLbl = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, y, w, LH), "", FS_SM, HINT, TextAnchor.MiddleCenter);

            RefreshEntryList();
        }

        public override void OnOpened()
        {
            _entries = LoadEntriesFromSettings();
            if (_selectedEntry >= _entries.Count) _selectedEntry = _entries.Count - 1;
            RefreshEntryList();
        }

        internal static Dictionary<string, Sprite> CostumeIcons()
        {
            var map = new Dictionary<string, Sprite>();
            var raw = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<CostumeOption>());
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == null) continue;
                var opt = raw[i].TryCast<CostumeOption>();
                if (opt == null || string.IsNullOrEmpty(opt.name) || map.ContainsKey(opt.name)) continue;
                Sprite s = null;
                try { s = ((ItemDefinitionSO)opt).MenuDisplaySprite; } catch { }
                map[opt.name] = s;
            }
            return map;
        }

        private void RefreshEntryList()
        {
            if (_entryContent == null) return;

            for (int i = _entryContent.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_entryContent.GetChild(i).gameObject);

            _rows.Clear();
            float rowW = TabWidth - PAD * 2f - 8f;
            var icons = _entries.Count > 0 && SkinApplicationService.MainMenuReached ? CostumeIcons() : null;

            for (int i = 0; i < _entries.Count; i++)
            {
                int idx = i;
                var entry = _entries[i];

                var rowGo = new GameObject("ERow_" + i);
                rowGo.transform.SetParent(_entryContent, false);
                rowGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, ROW_H);
                var le = rowGo.AddComponent<LayoutElement>();
                le.preferredHeight = ROW_H;
                le.flexibleWidth = 1f;
                var bg = rowGo.AddComponent<Image>();
                var btnSpr = UGUIShip.GetButtonSprite();
                if (btnSpr != null) { bg.sprite = btnSpr; bg.type = Image.Type.Simple; }

                var rowBtn = rowGo.AddComponent<Button>();
                rowBtn.transition = Selectable.Transition.None;
                var nav = rowBtn.navigation;
                nav.mode = UnityEngine.UI.Navigation.Mode.None;
                rowBtn.navigation = nav;
                rowBtn.onClick.AddListener(new Action(() =>
                {
                    AudioService.PlayButtonClick();
                    SelectEntry(idx);
                }));

                float thumbSz = ROW_H - 6f;
                var thumbGo = new GameObject("Thumb");
                thumbGo.transform.SetParent(rowGo.transform, false);
                UGUIShip.SetPixelRect(thumbGo.AddComponent<RectTransform>(), new Rect(3f, 3f, thumbSz, thumbSz));
                var thumb = thumbGo.AddComponent<Image>();
                thumb.raycastTarget = false;
                thumb.preserveAspect = true;
                if (icons != null && !string.IsNullOrEmpty(entry.costumeName) && icons.TryGetValue(entry.costumeName, out var spr) && spr != null)
                    thumb.sprite = spr;
                else
                    thumb.color = new Color(0f, 0f, 0f, 0.4f);

                float editW = 30f * UIScale.S, toggleW = 30f * UIScale.S, removeW = 22f * UIScale.S;
                float nameX = 3f + thumbSz + 6f;
                float nameW = rowW - editW - toggleW - removeW - nameX - 10f;

                var nameLbl = UGUIShip.CreateLabel(rowGo.transform, new Rect(nameX, 0f, nameW, ROW_H), entry.entryName,
                    FS_SM, WHITE, TextAnchor.MiddleLeft);

                RowBtn(rowGo.transform, -(removeW + toggleW + editW + 4f), editW, "edit", BTN_DARK, () => OpenWizard(idx));
                var toggleBtn = RowBtn(rowGo.transform, -(removeW + toggleW + 2f), toggleW, "on", BTN_APPLY, () => ToggleEntry(idx));
                RowBtn(rowGo.transform, -2f, removeW, "x", BTN_REMOVE, () => RemoveEntry(idx));

                _rows.Add(new RowRefs
                {
                    Bg = bg,
                    Name = nameLbl,
                    Toggle = toggleBtn,
                    ToggleLbl = toggleBtn.GetComponentInChildren<Text>()
                });
            }

            PaintRows();

            var addBtn = UGUIShip.CreateButton(_entryContent, new Rect(0f, 0f, rowW, ROW_H),
                "+ Add Texture", BTN_ADD, WHITE, FS, new Action(() => OpenWizard(-1)));
            var addLe = addBtn.gameObject.AddComponent<LayoutElement>();
            addLe.preferredHeight = ROW_H;
            addLe.flexibleWidth = 1f;
        }

        private Button RowBtn(Transform parent, float anchoredX, float bw, string label, Color bg, Action onClick)
        {
            float bh = Mathf.Min(ROW_H - 6f, 24f * UIScale.S);
            var btn = UGUIShip.CreateButton(parent, new Rect(0f, 0f, bw, bh), label, bg, WHITE, FS_SM - 1, onClick);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(anchoredX, 0f);
            rt.sizeDelta = new Vector2(bw, bh);
            return btn;
        }

        private void PaintRows()
        {
            for (int i = 0; i < _rows.Count && i < _entries.Count; i++)
            {
                var entry = _entries[i];
                var row = _rows[i];
                row.Bg.color = i == _selectedEntry ? ROW_SELECTED : (i % 2 == 0 ? ROW_A : ROW_B);
                var img = row.Toggle.GetComponent<Image>();
                if (img != null) img.color = entry.enabled ? BTN_APPLY : BTN_DARK;
                row.ToggleLbl.text = entry.enabled ? "on" : "off";
                row.Name.color = entry.enabled ? WHITE : DIM;
            }
        }

        private void OpenWizard(int editIdx)
        {
            var wizard = BetterFGTabRegistry.NewTab<SkinTextureWizardTab>();
            wizard.EditIndex = editIdx;
            BetterFGUIMan.Instance?.SwitchSlotTab(this, wizard);
        }

        private void SelectEntry(int idx)
        {
            if (_selectedEntry == idx)
            {
                _selectedEntry = -1;
                PaintRows();
                return;
            }
            _selectedEntry = idx;
            PaintRows();
            SetStatus(_entries[idx].entryName + " selected");
        }

        private void ToggleEntry(int idx)
        {
            _entries[idx].enabled = !_entries[idx].enabled;
            SaveEntries(_entries);
            PaintRows();
            ReapplyAllEnabledFromSettings(_entries, SetStatus);
        }

        private void RemoveEntry(int idx)
        {
            bool wasEnabled = _entries[idx].enabled;
            _entries.RemoveAt(idx);
            if (_selectedEntry >= _entries.Count) _selectedEntry = _entries.Count - 1;
            SaveEntries(_entries);
            RefreshEntryList();

            if (wasEnabled) ReapplyAllEnabledFromSettings(_entries, SetStatus);
        }

        private void OnApplySelected()
        {
            if (_selectedEntry < 0 || _selectedEntry >= _entries.Count)
            {
                SetStatus("select an entry first");
                return;
            }
            if (!_entries[_selectedEntry].enabled) { SetStatus("entry is disabled"); return; }
            ReapplyAllEnabledFromSettings(_entries, SetStatus);
        }

        public static void ReapplyAllEnabledFromSettings()
            => ReapplyAllEnabledFromSettings(LoadEntriesFromSettings(), _ => { });

        private static void ReapplyAllEnabledFromSettings(List<SkinTexEntry> entries, Action<string> status)
        {
            var svc = SkinApplicationService.Instance;
            if (svc == null) return;

            foreach (var bean in GatherBeans())
                svc.RevertCustomTexture(bean);

            foreach (var entry in entries)
                if (entry.enabled) ApplyEntryStatic(entry, status);
        }

        static readonly Dictionary<string, Texture2D> _texCache = new Dictionary<string, Texture2D>();

        static Texture2D LoadTexCached(string path)
        {
            if (_texCache.TryGetValue(path, out var cached) && cached != null) return cached;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(path));
            tex.Apply();
            _texCache[path] = tex;
            return tex;
        }

        public static void PrewarmTextureCache()
        {
            foreach (var entry in LoadEntriesFromSettings())
            {
                if (!entry.enabled || string.IsNullOrEmpty(entry.texPath)) continue;
                try { LoadTexCached(entry.texPath); }
                catch (Exception e) { Plugin.Log.LogWarning($"SkinTex: prewarm failed for {entry.texPath}: {e.Message}"); }
            }
        }

        private static void ApplyEntryStatic(SkinTexEntry entry, Action<string> status)
        {
            if (string.IsNullOrEmpty(entry.texPath)) return;

            var svc = SkinApplicationService.Instance;
            if (svc == null) return;

            Texture2D tex;
            try { tex = LoadTexCached(entry.texPath); }
            catch (Exception e) { status?.Invoke("tex load err: " + e.Message); return; }

            var matchNames = BuildMatchNames(entry);
            int total = 0;
            foreach (var bean in GatherBeans())
                total += svc.ApplyCustomTexture(bean, entry.matIdx, tex, matchNames);

            status?.Invoke(total > 0 ? $"applied {entry.entryName}" : "nothing matched");
        }

        private void OnRevert()
        {
            var svc = SkinApplicationService.Instance;
            if (svc == null) return;
            foreach (var bean in GatherBeans())
                svc.RevertCustomTexture(bean);
            SetStatus("reverted");
        }

        private static HashSet<string> BuildMatchNames(SkinTexEntry entry)
        {
            var matchNames = new HashSet<string>();
            if (entry.matNames.Count > 0 && entry.matIdx >= 0 && entry.matIdx < entry.matNames.Count)
            {
                var name = entry.matNames[entry.matIdx];
                if (!string.IsNullOrEmpty(name)) matchNames.Add(name);
            }

            if (matchNames.Count == 0 && entry.mats.Count > 0 && entry.matIdx >= 0 && entry.matIdx < entry.mats.Count)
            {
                var mat = entry.mats[entry.matIdx];
                if (mat != null)
                {
                    if (!string.IsNullOrEmpty(mat.name)) matchNames.Add(CleanMatName(mat.name));
                    foreach (var prop in new[] { "_MainTex", "_BaseMap", "_BaseTexture", "_MainTex2" })
                    {
                        try
                        {
                            if (mat.HasProperty(prop))
                            {
                                var t = mat.GetTexture(prop);
                                if (t != null && !string.IsNullOrEmpty(t.name)) { matchNames.Add(t.name); break; }
                            }
                        }
                        catch { }
                    }
                }
            }
            return matchNames;
        }

        private static string CleanMatName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.EndsWith(" (Instance)") ? name.Substring(0, name.Length - 11) : name;
        }

        private static List<GameObject> GatherBeans()
        {
            var beans = new List<GameObject>();
            if (BeanMonitorService.LocalPlayerBean != null)
                beans.Add(BeanMonitorService.LocalPlayerBean);
            foreach (var b in BeanMonitorService.GetTrackedBeans())
                if (b != null && !beans.Contains(b)) beans.Add(b);
            return beans;
        }

        internal static void SaveEntries(List<SkinTexEntry> entries)
        {
            SettingsService.Set(KEY_ENTRY_COUNT, entries.Count.ToString());
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                SettingsService.Set(EK(i, "name"), e.entryName);
                SettingsService.Set(EK(i, "texPath"), e.texPath);
                SettingsService.Set(EK(i, "matIdx"), e.matIdx.ToString());
                SettingsService.Set(EK(i, "enabled"), e.enabled ? "1" : "0");
                SettingsService.Set(EK(i, "costume"), e.costumeName);
                SettingsService.Set(EK(i, "matNames"), string.Join("|", e.matNames));
            }
        }

        internal static List<SkinTexEntry> LoadEntriesFromSettings()
        {
            var entries = new List<SkinTexEntry>();
            if (!int.TryParse(SettingsService.Get(KEY_ENTRY_COUNT, "0"), out int count)) return entries;
            for (int i = 0; i < count; i++)
            {
                var e = new SkinTexEntry
                {
                    entryName = SettingsService.Get(EK(i, "name"), "entry " + i),
                    texPath = SettingsService.Get(EK(i, "texPath"), ""),
                    matIdx = 0,
                    enabled = SettingsService.Get(EK(i, "enabled"), "1") == "1",
                    costumeName = SettingsService.Get(EK(i, "costume"), "")
                };
                if (int.TryParse(SettingsService.Get(EK(i, "matIdx"), "0"), out int mi))
                    e.matIdx = mi;

                string matNamesRaw = SettingsService.Get(EK(i, "matNames"), "");
                if (!string.IsNullOrEmpty(matNamesRaw))
                {
                    foreach (var n in matNamesRaw.Split('|'))
                        if (!string.IsNullOrEmpty(n)) e.matNames.Add(n);
                }

                entries.Add(e);
            }
            return entries;
        }

        public static string GetDisplayName(CostumeOption option)
        {
            try { return option.CMSData.Name._text ?? option.name ?? ""; } catch { }
            try { return option.name ?? ""; } catch { }
            return "";
        }

        public void SetStatus(string msg)
        {
            if (_statusLbl != null) _statusLbl.text = msg;
        }
    }
}
