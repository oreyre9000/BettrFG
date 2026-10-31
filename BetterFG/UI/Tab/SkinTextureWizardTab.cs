using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using FG.Common;
using FG.Common.CMS;
using UnityEngine;
using UnityEngine.UI;
using LayoutElement = UnityEngine.UI.LayoutElement;

namespace BetterFG.UI.Tab
{
    public class SkinTextureWizardTab : WizardTab
    {
        public SkinTextureWizardTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => EditIndex >= 0 ? "Skin Texture - Edit" : "Skin Texture - New";
        protected override string BgResource => "BetterFG.assets.ui.tab.customskintexture.png";
        public override float OpenHeight => UIScale.TAB_CONTENT_H * 1.15f;

        private static readonly Color CHECK = new Color(0.55f, 0.9f, 0.55f, 1f);
        private static readonly Color TILE_FILL = new Color(0.04f, 0.04f, 0.04f, 1f);

        private enum WizardStep { Costume, Material, Png, Name }
        protected override string[] StepTitles => new[]
        {
            "Choose a skin",
            "Choose the texture to change",
            "Choose the texture to change to",
            "Name it"
        };

        private const int GridCols = 5;
        private const float TileGap = 5f;
        private const int MaxTiles = 150;

        private string _filter = "";
        private float _tile;
        private RectTransform _resultContent;
        private readonly List<CostumeOption> _results = new List<CostumeOption>();
        private readonly List<Button> _resultRows = new List<Button>();
        private bool _caching;

        private readonly List<Material> _mats = new List<Material>();
        private readonly List<string> _matNames = new List<string>();
        private string _costumeName = "";

        private RectTransform _matContent;
        private int _matIdx = -1;

        private string _pngPath = "";
        private Texture2D _pngTex;
        private RawImage _pngPreview;
        private Text _pngPathLbl;

        private InputField _nameField;
        private Text _summaryLbl;

        protected override void BuildStep(int step, RectTransform root, float w, float bodyH)
        {
            switch ((WizardStep)step)
            {
                case WizardStep.Costume: BuildCostumeStep(root, w, bodyH); break;
                case WizardStep.Material: BuildMaterialStep(root, w, bodyH); break;
                case WizardStep.Png: BuildPngStep(root, w, bodyH); break;
                case WizardStep.Name: BuildNameStep(root, w, bodyH); break;
            }
        }

        protected override int LoadEditedEntry()
        {
            var entries = CustomSkinTextureTab.LoadEntriesFromSettings();
            if (EditIndex >= entries.Count) { EditIndex = -1; return -1; }

            var entry = entries[EditIndex];
            _costumeName = entry.costumeName ?? "";
            _matNames.AddRange(entry.matNames);
            _matIdx = entry.matIdx;
            _pngPath = entry.texPath ?? "";
            UGUIShip.SetInputText(_nameField, entry.entryName, false);
            LoadPngPreview();
            RebuildResultRows();
            RebuildMatRows();
            return (int)WizardStep.Material;
        }

        protected override BetterFGTab MakeListTarget() => BetterFGTabRegistry.CreateTab("Skin Texture");

        protected override bool CanAdvance(int step)
        {
            switch ((WizardStep)step)
            {
                case WizardStep.Costume: return _matNames.Count > 0;
                case WizardStep.Material: return _matIdx >= 0 && _matIdx < _matNames.Count;
                case WizardStep.Png: return !string.IsNullOrEmpty(_pngPath);
                default: return true;
            }
        }

        private void BuildCostumeStep(RectTransform root, float w, float bodyH)
        {
            var search = UGUIShip.CreateInputField(root.transform, new Rect(PAD, 0f, w, BTN_H),
                "search by name", new Color(0f, 0f, 0f, 0.4f), WHITE, FS_SM);
            search.onValueChanged.AddListener(new Action<string>(v => { _filter = v?.Trim() ?? ""; RebuildResultRows(); }));

            float gridY = BTN_H + SH;
            var scroll = UGUIShip.CreateScrollView(root.transform, new Rect(PAD, gridY, w, bodyH - gridY));
            _resultContent = scroll.content;
            _tile = Mathf.Floor((w - UGUIShip.SCROLLBAR_INSET * 2f - 4f - TileGap * (GridCols - 1)) / GridCols);
            var grid = _resultContent.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(_tile, _tile);
            grid.spacing = new Vector2(TileGap, TileGap);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = GridCols;
            grid.padding = new RectOffset(2, 2, 2, 2);
            _resultContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RebuildResultRows();
        }

        private void FillResults()
        {
            _results.Clear();
            var raw = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<CostumeOption>());
            for (int i = 0; i < raw.Length && _results.Count < MaxTiles; i++)
            {
                if (raw[i] == null) continue;
                var opt = raw[i].TryCast<CostumeOption>();
                if (opt == null) continue;
                if (CustomSkinTextureTab.GetDisplayName(opt).IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    _results.Add(opt);
            }
        }

        private void RebuildResultRows()
        {
            if (_resultContent == null) return;
            FillResults();
            _resultRows.Clear();
            _resultContent.anchoredPosition = new Vector2(_resultContent.anchoredPosition.x, 0f);
            for (int i = _resultContent.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_resultContent.GetChild(i).gameObject);

            for (int i = 0; i < _results.Count; i++)
            {
                int idx = i;
                var opt = _results[i];
                var btn = UGUIShip.CreateButton(_resultContent, new Rect(0f, 0f, _tile, _tile), "",
                    opt.name == _costumeName ? ROW_SEL : ROW_IDLE, WHITE, FS_SM, new Action(() => SelectOption(idx)));
                BetterFGUIMan.MakeObjectTooltip(btn.GetComponent<RectTransform>(), CustomSkinTextureTab.GetDisplayName(opt));

                var fillGo = new GameObject("Art");
                fillGo.transform.SetParent(btn.transform, false);
                var fillRt = fillGo.AddComponent<RectTransform>();
                fillRt.anchorMin = Vector2.zero;
                fillRt.anchorMax = Vector2.one;
                fillRt.offsetMin = new Vector2(4f, 4f);
                fillRt.offsetMax = new Vector2(-4f, -4f);
                var fill = fillGo.AddComponent<Image>();
                fill.color = TILE_FILL;
                fill.raycastTarget = false;

                Sprite icon = null;
                try { icon = ((ItemDefinitionSO)opt).MenuDisplaySprite; } catch { }
                if (icon != null)
                {
                    var picGo = new GameObject("Pic");
                    picGo.transform.SetParent(fillGo.transform, false);
                    var picRt = picGo.AddComponent<RectTransform>();
                    picRt.anchorMin = Vector2.zero;
                    picRt.anchorMax = Vector2.one;
                    picRt.offsetMin = new Vector2(2f, 2f);
                    picRt.offsetMax = new Vector2(-2f, -2f);
                    var img = picGo.AddComponent<Image>();
                    img.sprite = icon;
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                }
                _resultRows.Add(btn);
            }
        }

        private void SelectOption(int idx)
        {
            if (_caching) return;
            for (int i = 0; i < _resultRows.Count; i++) UGUIShip.SetButtonSelected(_resultRows[i], i == idx, ROW_SEL);
            StartCoroutine(CacheOptionRoutine(_results[idx]).WrapToIl2Cpp());
        }

        private IEnumerator CacheOptionRoutine(CostumeOption option)
        {
            _caching = true;
            SetStatus("loading " + CustomSkinTextureTab.GetDisplayName(option) + "...");

            GameObject instance = null;
            bool done = false;
            Exception err = null;

            try
            {
                var op = option.costumePrefabReference.InstantiateAsync();
                StartCoroutine(WaitForAsyncOp(op,
                    r => { instance = r; done = true; },
                    e => { err = e; done = true; }).WrapToIl2Cpp());
            }
            catch (Exception e) { _caching = false; SetStatus("couldn't load: " + e.Message); yield break; }

            float elapsed = 0f;
            while (!done && elapsed < 8f)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            _caching = false;

            if (!done || instance == null)
            {
                SetStatus(err != null ? "couldn't load: " + err.Message : $"gave up after {elapsed:0.0}s");
                yield break;
            }

            instance.SetActive(false);
            _mats.Clear();
            _matNames.Clear();
            _matIdx = -1;
            CollectMatsRecursive(instance.transform, _mats, _matNames);
            GameObject.Destroy(instance);

            try { _costumeName = option.name ?? ""; } catch { _costumeName = ""; }
            RebuildMatRows();
            RefreshStep();

            SetStatus($"{CustomSkinTextureTab.GetDisplayName(option)} has {_matNames.Count} texture(s), hit next");
        }

        private static void CollectMatsRecursive(Transform t, List<Material> mats, List<string> names)
        {
            var r = t.GetComponent<Renderer>();
            if (r != null)
            {
                var sharedMats = r.sharedMaterials;
                if (sharedMats != null)
                {
                    foreach (var m in sharedMats)
                    {
                        if (m == null) continue;
                        var mainTex = m.mainTexture;
                        string texName = mainTex != null ? mainTex.name : m.name;
                        if (!string.IsNullOrEmpty(texName) && !names.Contains(texName))
                        {
                            mats.Add(m);
                            names.Add(texName);
                        }
                    }
                }
            }
            for (int i = 0; i < t.childCount; i++)
                CollectMatsRecursive(t.GetChild(i), mats, names);
        }

        private IEnumerator WaitForAsyncOp(
            UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<GameObject> op,
            Action<GameObject> onDone, Action<Exception> onFail)
        {
            yield return op;
            try
            {
                if (op.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
                    onDone?.Invoke(op.Result);
                else
                    onFail?.Invoke(new Exception("op failed: " + op.OperationException?.Message));
            }
            catch (Exception e) { onFail?.Invoke(e); }
        }

        private void BuildMaterialStep(RectTransform root, float w, float bodyH)
        {
            float cy = SH;
            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, LH),
                "Pick the texture on the skin you want to replace", FS_SM, LABEL);
            cy += LH + SH;

            var scroll = UGUIShip.CreateScrollView(root.transform, new Rect(PAD, cy, w, bodyH - cy - SH));
            _matContent = scroll.content;
            var layout = _matContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 1f;
            _matContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void RebuildMatRows()
        {
            if (_matContent == null) return;
            for (int i = _matContent.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_matContent.GetChild(i).gameObject);

            if (_matNames.Count == 0)
            {
                UGUIShip.CreateLabel(_matContent, new Rect(6f, 0f, TabWidth, ROW_H),
                    "go back and pick something first", FS_SM, HINT);
                return;
            }

            float rowW = TabWidth - PAD * 2f - 8f;
            for (int i = 0; i < _matNames.Count; i++)
            {
                int idx = i;
                var btn = UGUIShip.CreateButton(_matContent, new Rect(0f, 0f, 0f, ROW_H), "",
                    idx == _matIdx ? ROW_SEL : ROW_IDLE, WHITE, FS_SM, new Action(() => SelectMat(idx)));
                var trigger = btn.GetComponent<UnityEngine.EventSystems.EventTrigger>();
                if (trigger != null) GameObject.Destroy(trigger);
                var le = btn.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = ROW_H;
                le.flexibleWidth = 1f;

                var thumbGo = new GameObject("Thumb");
                thumbGo.transform.SetParent(btn.transform, false);
                UGUIShip.SetPixelRect(thumbGo.AddComponent<RectTransform>(), new Rect(3f, 2f, ROW_H - 4f, ROW_H - 4f));
                var raw = thumbGo.AddComponent<RawImage>();
                raw.raycastTarget = false;
                Texture tex = idx < _mats.Count && _mats[idx] != null ? _mats[idx].mainTexture : null;
                if (tex != null) raw.texture = tex;
                else raw.color = new Color(0f, 0f, 0f, 0.4f);

                float checkW = 18f * UIScale.S;
                float textX = ROW_H + 3f;
                UGUIShip.CreateLabel(btn.transform, new Rect(textX, 0f, rowW - textX - checkW, ROW_H),
                    _matNames[idx], FS_SM, WHITE, TextAnchor.MiddleLeft);

                var checkLbl = UGUIShip.CreateLabel(btn.transform, new Rect(0f, 0f, checkW, ROW_H),
                    idx == _matIdx && !string.IsNullOrEmpty(_pngPath) ? "✓" : "", FS_SM, CHECK, TextAnchor.MiddleCenter);
                var checkRt = checkLbl.GetComponent<RectTransform>();
                checkRt.anchorMin = checkRt.anchorMax = new Vector2(1f, 0.5f);
                checkRt.pivot = new Vector2(1f, 0.5f);
                checkRt.anchoredPosition = new Vector2(-4f, 0f);
                checkRt.sizeDelta = new Vector2(checkW, ROW_H);
            }
        }

        private void SelectMat(int idx)
        {
            _matIdx = idx;
            RebuildMatRows();
            RefreshStep();
            SetStatus(_matNames[idx] + " selected");
        }

        private void BuildPngStep(RectTransform root, float w, float bodyH)
        {
            float cy = SH;
            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, LH),
                "Pick the PNG to use instead", FS_SM, LABEL);
            cy += LH + SH;

            float browseW = 80f * UIScale.S;
            UGUIShip.CreateButton(root.transform, new Rect(PAD, cy, browseW, BTN_H),
                "Browse", BTN_BLUE, WHITE, FS_SM, new Action(OnBrowsePng));
            _pngPathLbl = UGUIShip.CreateLabel(root.transform, new Rect(PAD + browseW + PAD, cy, w - browseW - PAD, BTN_H),
                "No file picked", FS_SM, HINT, TextAnchor.MiddleLeft);
            cy += BTN_H + SH;

            float previewSz = Mathf.Min(bodyH - cy - SH, w);
            var frameGo = new GameObject("PngPreview");
            frameGo.transform.SetParent(root.transform, false);
            UGUIShip.SetPixelRect(frameGo.AddComponent<RectTransform>(), new Rect(PAD + (w - previewSz) * 0.5f, cy, previewSz, previewSz));
            frameGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
            var innerGo = new GameObject("Preview");
            innerGo.transform.SetParent(frameGo.transform, false);
            var innerRt = innerGo.AddComponent<RectTransform>();
            innerRt.anchorMin = Vector2.zero;
            innerRt.anchorMax = Vector2.one;
            innerRt.offsetMin = new Vector2(2f, 2f);
            innerRt.offsetMax = new Vector2(-2f, -2f);
            _pngPreview = innerGo.AddComponent<RawImage>();
            _pngPreview.raycastTarget = false;
            _pngPreview.color = Color.clear;
            LoadPngPreview();
        }

        private void OnBrowsePng()
        {
            WinDialogs.PickPng("Select PNG Texture", path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                _pngPath = path;
                if (string.IsNullOrWhiteSpace(_nameField.text))
                    UGUIShip.SetInputText(_nameField, Path.GetFileNameWithoutExtension(path), false);
                LoadPngPreview();
                RebuildMatRows();
                RefreshStep();
                SetStatus(Path.GetFileName(path) + " ready");
            });
        }

        private void LoadPngPreview()
        {
            if (_pngPathLbl != null) _pngPathLbl.text = string.IsNullOrEmpty(_pngPath) ? "No file picked" : Path.GetFileName(_pngPath);
            if (_pngPreview == null) return;
            if (string.IsNullOrEmpty(_pngPath) || !File.Exists(_pngPath))
            {
                _pngPreview.texture = null;
                _pngPreview.color = Color.clear;
                return;
            }

            try
            {
                if (_pngTex == null) _pngTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                _pngTex.LoadImage(File.ReadAllBytes(_pngPath));
                _pngTex.Apply();
                _pngPreview.texture = _pngTex;
                _pngPreview.color = WHITE;
            }
            catch (Exception e) { SetStatus("couldn't read that png: " + e.Message); }
        }

        private void BuildNameStep(RectTransform root, float w, float bodyH)
        {
            float cy = SH;
            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, LH),
                "What should this override be called?", FS_SM, LABEL);
            cy += LH + SH;

            _nameField = UGUIShip.CreateInputField(root.transform, new Rect(PAD, cy, w, BTN_H),
                "my override", Color.black, WHITE, FS_SM);
            cy += BTN_H + SH * 2f;

            _summaryLbl = UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, LH * 4f), "", FS_SM, HINT);
            _summaryLbl.alignment = TextAnchor.UpperLeft;
        }

        protected override void RefreshSummary()
        {
            string tex = _matIdx >= 0 && _matIdx < _matNames.Count ? _matNames[_matIdx] : "?";
            _summaryLbl.text = $"item: {_costumeName}\ntexture: {tex}\nreplaced with: {(string.IsNullOrEmpty(_pngPath) ? "?" : Path.GetFileName(_pngPath))}";
        }

        protected override bool Save()
        {
            string name = _nameField.text?.Trim() ?? "";
            if (string.IsNullOrEmpty(name)) { SetStatus("Give it a name first"); return false; }

            var entries = CustomSkinTextureTab.LoadEntriesFromSettings();
            for (int i = 0; i < entries.Count; i++)
            {
                if (i == EditIndex) continue;
                if (entries[i].entryName == name) { SetStatus("You already have one called that"); return false; }
            }

            bool editing = EditIndex >= 0 && EditIndex < entries.Count;
            var entry = editing ? entries[EditIndex] : new SkinTexEntry { enabled = true };

            entry.entryName = name;
            entry.costumeName = _costumeName;
            entry.texPath = _pngPath;
            entry.matIdx = _matIdx;
            entry.matNames.Clear();
            entry.matNames.AddRange(_matNames);

            if (!editing) entries.Add(entry);

            CustomSkinTextureTab.SaveEntries(entries);
            Plugin.Log.LogInfo($"skin texture {(editing ? "updated" : "added")}: {name} -> {entry.costumeName}");
            return true;
        }

        protected override void OnLeave() => CustomSkinTextureTab.ReapplyAllEnabledFromSettings();
    }
}
