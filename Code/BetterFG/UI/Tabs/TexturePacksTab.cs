using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.TexturePacks;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using LayoutElement = UnityEngine.UI.LayoutElement;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class TexturePacksTab : Tab
    {
        public TexturePacksTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Texture Packs";
        protected override string TitleId => "texpack.tab_title";
        protected override string BgResource => "BetterFG.assets.ui.tab.customskintexture.png";

        private static readonly Color BTN_DARK = UGUIShip.BTN_DARK;
        private static readonly Color BTN_APPLY = new Color(0.25f, 0.45f, 0.25f, 1f);
        private static readonly Color BTN_REMOVE = UGUIShip.BTN_REMOVE;
        private static readonly Color BTN_ADD = new Color(0.65f, 0.7f, 0.28f, 1f);
        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color WHITE = UGUIShip.WHITE;

        private static float ROW_H => 30f * UIScale.S;

        private List<TexturePackProject> _projects = new List<TexturePackProject>();
        private RectTransform _entryContent;
        private Text _statusLbl;

        private static readonly List<TexturePacksTab> _live = new List<TexturePacksTab>();
        void Awake() => _live.Add(this);
        void OnDestroy() => _live.Remove(this);

        public static void RefreshLiveInstances()
        {
            for (int i = 0; i < _live.Count; i++) _live[i]?.RefreshEntryList();
        }

        private float _tickTimer;
        void Update()
        {
            _tickTimer += Time.deltaTime;
            if (_tickTimer < 0.1f) return;
            _tickTimer = 0f;
            WinDialogs.Tick();
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            _projects = TexturePackProject.LoadKnown();

            float w = TabWidth - PAD * 2f;
            float y = VPAD;

            float listH = TabHeight - y - LH - VPAD - 4f;
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

            _statusLbl = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, y, w, LH), "", FS_SM, HINT, TextAnchor.MiddleCenter);

            RefreshEntryList();
        }

        public override void OnOpened()
        {
            _projects = TexturePackProject.LoadKnown();
            RefreshEntryList();
        }

        private void RefreshEntryList()
        {
            if (_entryContent == null) return;

            for (int i = _entryContent.childCount - 1; i >= 0; i--)
            {
                var ch = _entryContent.GetChild(i);
                if (ch != null) GameObject.Destroy(ch.gameObject);
            }

            float rowW = TabWidth - PAD * 2f - 8f;

            var addBtn = UGUIShip.CreateButton(_entryContent, new Rect(0f, 0f, rowW, ROW_H),
                "texpack.make_new", BTN_ADD, WHITE, FS, new Action(TexturePackScreen.OpenForNewProject));
            var addLe = addBtn.gameObject.AddComponent<LayoutElement>();
            addLe.preferredHeight = ROW_H;
            addLe.flexibleWidth = 1f;

            var loadBtn = UGUIShip.CreateButton(_entryContent, new Rect(0f, 0f, rowW, ROW_H),
                "texpack.load_existing", BTN_DARK, WHITE, FS, new Action(OnLoadExisting));
            var loadLe = loadBtn.gameObject.AddComponent<LayoutElement>();
            loadLe.preferredHeight = ROW_H;
            loadLe.flexibleWidth = 1f;

            float toggleW = 30f * UIScale.S, removeW = 22f * UIScale.S;
            float nameX = 6f;
            float nameW = rowW - toggleW - removeW - nameX - 10f;

            for (int i = 0; i < _projects.Count; i++)
            {
                int idx = i;
                var p = _projects[i];

                string subtitle = string.IsNullOrEmpty(p.author) ? "" : "   " + p.author;
                var rowBtn = UGUIShip.CreateButton(_entryContent, new Rect(0f, 0f, rowW, ROW_H),
                    p.name + subtitle, BTN_DARK, WHITE, FS_SM, new Action(() => OpenScreen(idx)), passThroughDrag: true);
                rowBtn.name = "PRow_" + i;
                var le = rowBtn.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = ROW_H;
                le.flexibleWidth = 1f;

                var coverTex = p.LoadCover();
                if (coverTex != null)
                {
                    var maskGo = new GameObject("CoverMask");
                    maskGo.transform.SetParent(rowBtn.transform, false);
                    var maskRt = maskGo.AddComponent<RectTransform>();
                    maskRt.anchorMin = Vector2.zero;
                    maskRt.anchorMax = Vector2.one;
                    maskRt.offsetMin = maskRt.offsetMax = Vector2.zero;
                    var maskImg = maskGo.AddComponent<Image>();
                    UGUIShip.ApplyDeluxColorFill(maskImg);
                    maskImg.color = new Color(1f, 1f, 1f, 0.01f);
                    maskImg.raycastTarget = false;
                    maskGo.AddComponent<Mask>().showMaskGraphic = true;

                    var coverGo = new GameObject("Cover");
                    coverGo.transform.SetParent(maskGo.transform, false);
                    var coverRt = coverGo.AddComponent<RectTransform>();
                    coverRt.anchorMin = Vector2.zero;
                    coverRt.anchorMax = Vector2.one;
                    coverRt.offsetMin = coverRt.offsetMax = Vector2.zero;
                    var coverImg = coverGo.AddComponent<RawImage>();
                    coverImg.texture = coverTex;
                    coverImg.raycastTarget = false;
                    var fade = coverGo.AddComponent<GradientImage>();
                    fade.Vertical = false;
                    fade.LeftMidT = 0.45f;
                    fade.RightMidT = 0.8f;
                    fade.Left = new Color(1f, 1f, 1f, 0.2f);
                    fade.LeftMid = new Color(1f, 1f, 1f, 0.2f);
                    fade.RightMid = new Color(1f, 1f, 1f, 0f);
                    fade.Right = new Color(1f, 1f, 1f, 0f);
                }

                var nameLbl = rowBtn.GetComponentInChildren<Text>();
                nameLbl.alignment = TextAnchor.MiddleLeft;
                nameLbl.transform.SetAsLastSibling();
                UGUIShip.SetPixelRect(nameLbl.rectTransform, new Rect(nameX, 0f, nameW, ROW_H));

                bool active = IsActive(p);
                UGUIShip.CreateRowEndButton(rowBtn.transform, -(removeW + toggleW + 2f), toggleW, ROW_H,
                    active ? "ui.on_2" : "ui.off_2", active ? BTN_APPLY : BTN_DARK, () => ToggleActive(idx));

                UGUIShip.CreateRowEndButton(rowBtn.transform, -2f, removeW, ROW_H, "x", BTN_REMOVE, () => RemoveEntry(idx));
            }
        }

        private static bool IsActive(TexturePackProject p)
            => TexturePackRuntimeService.Active
               && string.Equals(TexturePackRuntimeService.ActivePath, p.PackedPath, StringComparison.OrdinalIgnoreCase);

        private void OpenScreen(int idx)
        {
            if (idx < 0 || idx >= _projects.Count) return;
            TexturePackScreen.Open(_projects[idx]);
        }

        private void ToggleActive(int idx)
        {
            var p = _projects[idx];
            if (IsActive(p))
            {
                TexturePackRuntimeService.Clear();
                SetStatus("texpack.disabled");
            }
            else if (!File.Exists(p.PackedPath))
            {
                SetStatus("texpack.pack_first");
            }
            else if (TexturePackRuntimeService.Load(p.PackedPath, out string err))
            {
                SetStatus("texpack.enabled");
            }
            else
            {
                SetStatus(LocalizationService.Format("texpack.enable_failed", err));
            }
            RefreshEntryList();
        }

        private void RemoveEntry(int idx)
        {
            var p = _projects[idx];
            if (IsActive(p)) TexturePackRuntimeService.Clear();
            TexturePackProject.Unregister(p.location);
            _projects.RemoveAt(idx);
            RefreshEntryList();
            SetStatus("texpack.removed");
        }

        private void OnLoadExisting()
        {
            WinDialogs.PickFile(LocalizationService.Get("texpack.pick_pack"), path =>
            {
                if (!string.IsNullOrEmpty(path)) Import(path);
            }, "BettrFG Texture Pack\0*.bfgtp\0");
        }

        public static bool ImportDropped(string path)
        {
            foreach (var tab in _live)
                if (tab != null && tab.IsOpen) { tab.Import(path); return true; }
            return false;
        }

        private void Import(string path)
        {
            StartCoroutine(TexturePackProject.ImportRoutine(path,
                (i, n) => SetStatus(LocalizationService.Format("texpack.importing", i, n)),
                (p, err) =>
                {
                    if (p == null) { SetStatus(LocalizationService.Format("texpack.import_failed", err)); return; }
                    _projects = TexturePackProject.LoadKnown();
                    RefreshEntryList();
                    SetStatus(LocalizationService.Format("texpack.loaded", p.name, p.files.Count));
                }).WrapToIl2Cpp());
        }

        public void SetStatus(string msg)
        {
            if (_statusLbl != null) UGUIShip.RelabelText(_statusLbl, msg);
        }
    }
}
