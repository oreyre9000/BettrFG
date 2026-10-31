using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Profiles;
using BetterFG.Features.Onboarding;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Windows
{
    // Player profiles: save your current customization tagged with a player name; when that name
    // shows up in-round (clean name, you or remote) the bean gets that look. Rows show the player
    // name + an enabled toggle + delete. Save row makes one from your current setup. Import (button
    // or drop a .bfgprofile onto the drop bar) pulls a shared profile in.
    public class ProfilesWindow : SideWindow
    {
        public ProfilesWindow(IntPtr ptr) : base(ptr) { }

        // live instance so external callers (drag-drop import) can refresh the row list
        private static ProfilesWindow _instance;
        void OnEnable() => _instance = this;
        void OnDisable() { if (_instance == this) _instance = null; }
        public static void RefreshOpen() => _instance?.RebuildContent();

        // the first-open help prompt points its pulse at these
        public static RectTransform ExportRect { get; private set; }
        public static RectTransform ImportRect { get; private set; }

        protected override float WindowWidth => 280f;
        protected override float WindowHeight => 220f;
        protected override string WindowTitle => "ui.profiles";

        private const float ROW_H = 22f;
        private const float PROFILE_ROW_H = 40f;
        private const float THUMB = 46f;
        private const float THUMB_RAISE = 5f;
        private const float NAME_X = SideWindow.RowLabelX + 46f;
        private const float NAME_Y = 6f;
        private const float NAME_SCALE = 0.78f;
        private static GameObject _tagPrefab;
        private const float BTN_H = 16f;
        private const float ROW_PAD = 6f;
        private static readonly Color ROW_EVEN = new Color(1f, 1f, 1f, 0.03f);
        private static readonly Color ROW_ODD = new Color(0f, 0f, 0f, 0f);
        private static readonly Color BTN_DEL = new Color(0.7f, 0.3f, 0.3f, 1f);
        private static readonly Color BTN_SAVE = new Color(0.25f, 0.45f, 0.75f, 1f);
        private static readonly Color BTN_IMPORT = new Color(0.3f, 0.55f, 0.4f, 1f);
        private static readonly Color ON_COL = UGUIShip.TOGGLE_ON;
        private static readonly Color OFF_COL = UGUIShip.TOGGLE_OFF;

        private ScrollRect _scroll;
        private float _scrollPos = 1f;

        private void RebuildKeepingScroll()
        {
            if (_scroll != null) _scrollPos = _scroll.verticalNormalizedPosition;
            RebuildContent();
            StartCoroutine(RestoreScroll().WrapToIl2Cpp());
        }

        private IEnumerator RestoreScroll()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (_scroll != null) _scroll.verticalNormalizedPosition = Mathf.Clamp01(_scrollPos);
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            BgPosition = new Vector3(130.8522f, 70.3623f, 0f);
            BgScale = new Vector3(1.2833f, 4.5877f, 1f);
            ContentPosition = new Vector3(113.0835f, -14.7893f, 0f);
            ContentScale = new Vector3(1.0473f, 1f, 1f);
            Pivot = new Vector2(0f, 0.5f);
            TitlePosition = new Vector3(21.0566f, -17.2182f, 0);
            TitleScale = new Vector3(1.2763f, 1.457f, 1f);

            var scroll = UGUIShip.CreateScrollView(contentRoot,
                new Rect(0f, 0f, WindowWidth, WindowHeight - TITLE_H));
            _scroll = scroll.scrollRect;
            var scrollRt = scroll.scrollRect.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = scrollRt.offsetMax = Vector2.zero;

            var listRt = scroll.content;
            var vlg = listRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 0f;
            listRt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            BuildSaveRow(listRt);
            BuildImportRow(listRt);

            var loaded = new System.Collections.Generic.Dictionary<string, BetterFG.Customization.Player.BfgProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in ProfileService.GetRemoteProfiles()) loaded[p.name] = p;

            var names = ProfileService.List();
            for (int i = 0; i < names.Count; i++)
                BuildProfileRow(listRt, names[i], loaded.TryGetValue(names[i], out var prof) ? prof : null, i % 2 == 0 ? ROW_EVEN : ROW_ODD);
        }

        // export your current setup to a shareable file. the profile auto-captures YOUR player name
        // as its match key, so whoever imports it sees it on you. does NOT join the list below —
        // that list is for OTHER players' profiles you import.
        private void BuildSaveRow(RectTransform parent)
        {
            var rowGo = new GameObject("Row_Save");
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            UGUIShip.PaintStaticRowFill(rowGo, new Color(0.2f, 0.3f, 0.45f, 0.18f));

            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(SideWindow.RowLabelX, 0f, 150f, ROW_H),
                "ui.export_my_setup_to_a_file", 12, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleLeft);

            var btnGo = new GameObject("ExportBtn");
            btnGo.transform.SetParent(rowGo.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-ROW_PAD, 0f);
            btnRt.sizeDelta = new Vector2(54f, BTN_H);
            ExportRect = btnRt;

            UGUIShip.CreateButton(btnGo.transform, new Rect(0f, 0f, 54f, BTN_H), "ui.export_2", BTN_SAVE, Color.white, 9)
                .onClick.AddListener(new Action(() =>
                {
                    bool pbs = true;
                    FullscreenPrompt.Ask("profiles.export.title", null, "ui.export_2", () =>
                        WinDialogs.SaveFile(LocalizationService.Get("profiles.export.title"), "bfgprofile", ProfileService.LocalPlayerName(), path =>
                        {
                            if (string.IsNullOrEmpty(path)) return;
                            ProfileService.SaveCurrentToPath(path, pbs);
                        }),
                        PopUpKind.Info, OnboardingPopup.ToggleH,
                        box => OnboardingPopup.BuildToggle(box, "profiles.export.pbs", pbs, v => pbs = v));
                }));
        }

        // drop-a-file bar + IMPORT button (both open the picker; native OS drop is a later add)
        private void BuildImportRow(RectTransform parent)
        {
            var rowGo = new GameObject("Row_Import");
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            UGUIShip.PaintStaticRowFill(rowGo, new Color(0.2f, 0.4f, 0.3f, 0.14f));

            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(SideWindow.RowLabelX, 0f, 150f, ROW_H),
                "ui.drop_profile_here", 12, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleLeft);

            var btnGo = new GameObject("ImportBtn");
            btnGo.transform.SetParent(rowGo.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-ROW_PAD, 0f);
            btnRt.sizeDelta = new Vector2(60f, BTN_H);
            ImportRect = btnRt;

            UGUIShip.CreateButton(btnGo.transform, new Rect(0f, 0f, 60f, BTN_H), "ui.import_2", BTN_IMPORT, Color.white, 9)
                .onClick.AddListener(new Action(() =>
                    WinDialogs.PickFile("Select a .bfgprofile", path =>
                    {
                        if (string.IsNullOrEmpty(path)) return;
                        ProfileService.Import(path);
                        RebuildContent();
                    })));
        }

        private void BuildProfileRow(RectTransform parent, string name, BetterFG.Customization.Player.BfgProfile prof, Color bg)
        {
            var rowGo = new GameObject("Row_" + name);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = PROFILE_ROW_H;
            le.flexibleWidth = 1f;
            UGUIShip.PaintStaticRowFill(rowGo, bg);

            var thumbGo = new GameObject("Preview");
            thumbGo.transform.SetParent(rowGo.transform, false);
            var thumbRt = thumbGo.AddComponent<RectTransform>();
            thumbRt.anchorMin = thumbRt.anchorMax = thumbRt.pivot = new Vector2(0f, 0.5f);
            thumbRt.anchoredPosition = new Vector2(SideWindow.RowLabelX - 3f, THUMB_RAISE);
            thumbRt.sizeDelta = new Vector2(THUMB, THUMB);
            var thumb = thumbGo.AddComponent<RawImage>();
            thumb.texture = ProfileService.PreviewTexture(name);
            thumb.raycastTarget = false;

            string shown = prof == null ? name : !string.IsNullOrEmpty(prof.displayName) ? prof.displayName : prof.username;
            if (!BuildNameTag(rowGo.transform, shown, prof))
                UGUIShip.CreateLabel(rowGo.transform, new Rect(NAME_X, 3f, 104f, ROW_H),
                    shown, 13, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);

            UGUIShip.CreateLabel(rowGo.transform, new Rect(NAME_X, 18.5f, 110f, 16f),
                AgeText(DateTime.UtcNow - ProfileService.ExportedAt(name)), 10, new Color(1f, 1f, 1f, 0.45f), TextAnchor.MiddleLeft);

            float right = ROW_PAD;
            AddRowButton(rowGo, "ui.del", 34f, ref right, BTN_DEL, () => { ProfileService.Delete(name); ProfileService.Resync(); RebuildKeepingScroll(); });

            bool on = ProfileService.IsEnabled(name);
            AddRowButton(rowGo, on ? "ui.on" : "ui.off", 38f, ref right, on ? ON_COL : OFF_COL, () =>
            {
                ProfileService.SetEnabled(name, !ProfileService.IsEnabled(name));
                ProfileService.Resync();
                RebuildKeepingScroll();
            });
        }

        private static bool BuildNameTag(Transform row, string shown, BetterFG.Customization.Player.BfgProfile prof)
        {
            if (_tagPrefab == null)
                foreach (var c in Resources.FindObjectsOfTypeAll<PlayerInfoDisplayCanvas>())
                    if (c.name == "NetworkedPlayerTag") { _tagPrefab = c.gameObject; break; }
            if (_tagPrefab == null) return false;

            var tag = UnityEngine.Object.Instantiate(_tagPrefab, row);
            tag.name = "NameTag";
            tag.SetActive(true);
            var disp = tag.GetComponent<PlayerInfoDisplayCanvas>();
            var tmp = disp._text;
            tmp.text = shown;
            if (prof?.nametag != null)
                BetterFG.Nametag.NametagIconApplicator.ApplyRemoteToNameplate(tmp, shown, prof.nametag.WithoutIcon());
            disp.enabled = false;

            disp._crownRankPlayerTagLayoutHelper.crownRankObject.SetActive(false);
            tag.transform.Find("ArrowImage").gameObject.SetActive(false);
            tag.transform.Find("PlatformIconImage").gameObject.SetActive(false);

            var rt = tag.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
            rt.localScale = new Vector3(NAME_SCALE, NAME_SCALE, 1f);
            rt.anchoredPosition = new Vector2(NAME_X, NAME_Y);

            var nac = tag.transform.Find("NameAndCrownParent").GetComponent<RectTransform>();
            nac.GetComponent<HorizontalLayoutGroup>().enabled = false;
            nac.anchorMin = nac.anchorMax = nac.pivot = new Vector2(0f, 0.5f);
            nac.anchoredPosition = Vector2.zero;

            var nt = tmp.rectTransform;
            nt.anchorMin = nt.anchorMax = nt.pivot = new Vector2(0f, 0.5f);
            nt.anchoredPosition = Vector2.zero;
            tmp.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
            return true;
        }

        private static string AgeText(TimeSpan ago)
        {
            if (ago.TotalMinutes < 1) return Ago((int)ago.TotalSeconds, "second");
            if (ago.TotalHours < 1) return Ago((int)ago.TotalMinutes, "minute");
            if (ago.TotalDays < 1) return Ago((int)ago.TotalHours, "hour");
            return Ago((int)ago.TotalDays, "day");
        }

        private static string Ago(int n, string unit)
        {
            n = Math.Max(0, n);
            return BetterFG.Services.LocalizationService.Format("profiles.ago." + unit + (n == 1 ? "" : "s"), n);
        }

        private void AddRowButton(GameObject row, string label, float w, ref float rightOffset, Color col, Action onClick)
        {
            var go = new GameObject(label + "Btn");
            go.transform.SetParent(row.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-rightOffset, 0f);
            rt.sizeDelta = new Vector2(w, BTN_H);
            rightOffset += w + 3f;

            UGUIShip.CreateButton(go.transform, new Rect(0f, 0f, w, BTN_H), label, col, Color.white, 9)
                .onClick.AddListener(new Action(() => onClick?.Invoke()));
        }
    }
}
