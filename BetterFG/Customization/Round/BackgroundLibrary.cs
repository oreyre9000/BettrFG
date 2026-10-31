using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Features.Onboarding;
using BetterFG.Services;
using BetterFG.Tweaks;
using BetterFG.UI.Components;
using BettrFG.uGUI;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Customization.Round
{
    public class BackgroundLibrary : PromptHost
    {
        public BackgroundLibrary(IntPtr ptr) : base(ptr) { }

        private const int CanvasOrder = 1402;
        private const int BlockerOrder = 1401;
        private const float Width = 640f;
        private const float ListH = 440f;
        private const float Strip = 28f;
        private const float StripBtnH = 18f;
        private const float SearchH = 24f;
        private const float Inset = 4f;
        private const float Gap = 10f;
        private const float Gutter = 19f;
        private const float Aspect = 16f / 9f;
        private const int FsName = 13;
        private const int FsStatus = 11;
        private const int FsCenter = 15;
        private static readonly Color Cyan = new Color(0.2f, 0.9f, 1f, 1f);
        private static readonly Color Dim = new Color(0.45f, 0.45f, 0.45f, 1f);
        private static readonly Color Faint = new Color(1f, 1f, 1f, 0.45f);
        private static readonly Color Empty = new Color(0.08f, 0.08f, 0.08f, 1f);
        private static readonly Color Scrim = new Color(0f, 0f, 0f, 0.85f);
        private static readonly Quaternion Tilt = Quaternion.Euler(6f, 358f, 0f);

        private static BackgroundLibrary _live;

        private Action<string> _onPick;
        private string _picked;
        private Sprite _splash;
        private Predicate<string> _allow;
        private RectTransform _content;
        private Text _count;
        private float _cellW, _previewH;
        private string _query = "";
        private int _seq;
        private readonly List<(string Id, LoadingBar Bar)> _bars = new List<(string, LoadingBar)>();

        public static void Open(Action<string> onPick = null, string picked = null, Sprite splash = null, Predicate<string> allow = null)
        {
            if (_live != null) return;
            var go = new GameObject("BettrFG_BackgroundLibrary");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            _live = go.AddComponent<BackgroundLibrary>();
            _live._onPick = onPick;
            _live._picked = picked;
            _live._splash = splash;
            _live._allow = allow;
            _live.Build();
        }

        private static string IdOf(string key) => RoundBackgroundService.IdOf(key);

        private List<RoundBackgroundService.Option> Items()
        {
            var list = new List<RoundBackgroundService.Option>();
            foreach (var o in RoundBackgroundService.Options())
                if ((_onPick != null || o.Group == "bettrfg") && (_allow == null || _allow(o.Key))) list.Add(o);
            return list;
        }

        private bool Matches(RoundBackgroundService.Option o) =>
            _query.Length == 0 || LocalizationService.Get(o.Label).ToLowerInvariant().Contains(_query);

        private string CountText()
        {
            int have = 0, total = 0;
            foreach (var o in Items())
            {
                string id = IdOf(o.Key);
                if (id == null) continue;
                total++;
                if (Background3dTweak.Downloaded(id)) have++;
            }
            return LocalizationService.Format("bg_library.count", have, total);
        }

        private void Build()
        {
            CreateSurfaces("BackgroundLibrary", CanvasOrder, BlockerOrder, true);
            foreach (var img in _blockerCanvas.GetComponentsInChildren<Image>(true)) img.color = Scrim;

            var popup = OnboardingPopup.Show(_canvas, "bg_library.title", CountText(), Vector2.zero, new Vector2(0.5f, 0.5f),
                new[] { new OnboardingPopup.ButtonSpec("ui.close", new Action(Close), OnboardingPopup.BtnGhost) },
                onBody: t => _count = t, customH: ListH, onCustom: BuildList, width: Width);

            var from = OnboardingPopup.TitleRot.eulerAngles;
            var to = Tilt.eulerAngles;
            var panel = popup.transform.Find("Panel");
            var s = panel.localScale;
            panel.localRotation = Tilt;
            panel.localScale = new Vector3(s.x * Mathf.Cos(from.y * Mathf.Deg2Rad) / Mathf.Cos(to.y * Mathf.Deg2Rad),
                s.y * Mathf.Cos(from.x * Mathf.Deg2Rad) / Mathf.Cos(to.x * Mathf.Deg2Rad), 1f);
            foreach (var t in popup.GetComponentsInChildren<Text>(true))
                if (t.rectTransform.localRotation == OnboardingPopup.TitleRot) { t.rectTransform.localRotation = Tilt; t.rectTransform.localScale = Vector3.one; }

            Swap(popup);
            _seq = Background3dTweak.DownloadSeq;
            StartCoroutine(Background3dTweak.FetchPreviews(Refresh).WrapToIl2Cpp());
            StartCoroutine(RefreshOnManifest().WrapToIl2Cpp());
            if (Background3dTweak.SizeMb.Count == 0) StartCoroutine(Background3dTweak.FetchCatalogue(Refresh).WrapToIl2Cpp());
        }

        private IEnumerator RefreshOnManifest()
        {
            yield return Utilities.OnlineData.LoadManifest().WrapToIl2Cpp();
            Refresh();
        }

        private void BuildList(RectTransform area)
        {
            float innerW = area.sizeDelta.x;
            var search = UGUIShip.CreateInputField(area, new Rect(0f, 0f, innerW, SearchH), "bg_library.search", fontSize: FsName);
            search.onValueChanged.AddListener(new Action<string>(Search));
            var (scroll, content) = UGUIShip.CreateScrollView(area, new Rect(0f, SearchH + Inset * 2f, innerW + Gutter, ListH - SearchH - Inset * 2f));
            scroll.viewport.offsetMin = Vector2.zero;
            scroll.viewport.offsetMax = new Vector2(-Gutter, 0f);

            _cellW = (innerW - Gap) / 2f;
            _previewH = (_cellW - Inset * 2f) / Aspect;
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(_cellW, _previewH + Inset + Strip);
            grid.spacing = new Vector2(Gap, Gap);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _content = content;
            Fill();
        }

        private void Refresh()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--) DestroyImmediate(_content.GetChild(i).gameObject);
            Fill();
            UGUIShip.RelabelText(_count, CountText());
        }

        private void Search(string q)
        {
            _query = (q ?? "").Trim().ToLowerInvariant();
            Refresh();
        }

        private void Fill()
        {
            _bars.Clear();
            foreach (var o in Items()) if (Matches(o)) Card(o);
        }

        internal static RectTransform Anchor(Component c, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
        {
            var rt = c.GetComponent<RectTransform>();
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = offMin;
            rt.offsetMax = offMax;
            return rt;
        }

        private void Card(RoundBackgroundService.Option o)
        {
            string key = o.Key, id = IdOf(key);
            bool have = id == null || Background3dTweak.Downloaded(id);
            float busy = -1f;
            if (id != null && Background3dTweak.Downloading.TryGetValue(id, out float p)) busy = p;

            var btn = UGUIShip.CreateButton(_content, new Rect(0f, 0f, _cellW, _previewH + Inset + Strip), "",
                UGUIShip.BTN_DARK, UGUIShip.WHITE, FsName, () => Click(key));
            if (_onPick != null) UGUIShip.SetButtonSelected(btn, key == _picked, OnboardingPopup.BtnPrimary);

            var pgo = new GameObject("Preview");
            pgo.transform.SetParent(btn.transform, false);
            pgo.AddComponent<RectTransform>();
            var preview = pgo.AddComponent<RawImage>();
            preview.raycastTarget = false;
            Anchor(preview, Vector2.zero, Vector2.one, new Vector2(Inset, Strip), new Vector2(-Inset, -Inset));
            if (key.Length == 0) PreviewCard.SetSprite(preview, _splash);
            else
            {
                var tex = Background3dTweak.Preview(o.Preview);
                preview.texture = tex;
                if (tex != null)
                {
                    float a = (float)tex.width / tex.height;
                    preview.uvRect = a > Aspect ? new Rect((1f - Aspect / a) / 2f, 0f, Aspect / a, 1f) : new Rect(0f, (1f - a / Aspect) / 2f, 1f, a / Aspect);
                }
            }
            preview.color = preview.texture == null ? Empty : have ? Color.white : Dim;

            var name = UGUIShip.CreateLabel(btn.transform, new Rect(0f, 0f, 10f, 10f), o.Label, FsName, UGUIShip.WHITE, TextAnchor.MiddleLeft);
            name.raycastTarget = false;
            name.resizeTextForBestFit = true;
            name.resizeTextMinSize = 8;
            name.resizeTextMaxSize = FsName;
            bool stale = id != null && have && Background3dTweak.Outdated(id);
            bool buttons = id != null && have && busy < 0f;
            Anchor(name, Vector2.zero, new Vector2(1f, 0f), new Vector2(9f, 0f), new Vector2(buttons ? (stale ? -128f : -72f) : -9f, Strip));

            if (id == null) return;
            if (buttons)
            {
                float right = 9f;
                Btn(btn.transform, "bg_library.remove", OnboardingPopup.BtnDanger, 58f, ref right, () => Remove(key));
                if (stale) Btn(btn.transform, "bg_library.update", OnboardingPopup.BtnInfo, 52f, ref right, () => Refetch(id));
                return;
            }

            if (have)
            {
                AddBar(btn.transform, id, busy);
                return;
            }

            var status = UGUIShip.CreateLabel(btn.transform, new Rect(0f, 0f, 10f, 10f),
                busy >= 0f ? "bg_library.downloading" : "bg_library.not_downloaded", FsCenter, Cyan, TextAnchor.MiddleCenter);
            status.raycastTarget = false;
            Anchor(status, Vector2.zero, Vector2.one, new Vector2(Inset, Strip), new Vector2(-Inset, -Inset));
            AddBar(btn.transform, id, busy);
        }

        private void AddBar(Transform card, string id, float busy)
        {
            var bar = UGUIShip.CreateLoadingBar(card, Cyan);
            Anchor(bar, Vector2.zero, new Vector2(1f, 0f), new Vector2(Inset, Strip), new Vector2(-Inset, Strip + 6f));
            bar.Set(Mathf.Max(busy, 0f));
            _bars.Add((id, bar));
        }

        private static void Btn(Transform card, string label, Color bg, float w, ref float right, Action onClick)
        {
            var b = UGUIShip.CreateButton(card, new Rect(0f, 0f, w, StripBtnH), label, bg, UGUIShip.WHITE, FsStatus - 1, onClick);
            Anchor(b, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-right - w, (Strip - StripBtnH) / 2f), new Vector2(-right, (Strip + StripBtnH) / 2f));
            right += w + 4f;
        }

        private void Remove(string key)
        {
            Background3dTweak.Uninstall(IdOf(key));
            Refresh();
        }

        private void Refetch(string id) => Background3dTweak.Instance.StartCoroutine(Background3dTweak.Fetch(id, true).WrapToIl2Cpp());

        private void Click(string key)
        {
            string id = IdOf(key);
            if (id != null && !Background3dTweak.Downloaded(id))
            {
                SettingsService.Set(Background3dTweak.ASKED_KEY, "true");
                Background3dTweak.Instance.StartCoroutine(Background3dTweak.Fetch(id).WrapToIl2Cpp());
                return;
            }
            if (_onPick == null) return;
            _picked = key;
            _onPick(key);
            Refresh();
        }

        void Update()
        {
            if (_content == null) return;
            if (_seq != Background3dTweak.DownloadSeq)
            {
                _seq = Background3dTweak.DownloadSeq;
                Refresh();
                return;
            }
            foreach (var (id, bar) in _bars)
                if (Background3dTweak.Downloading.TryGetValue(id, out float p)) bar.Set(p);
        }

        private void Close()
        {
            if (_live != this) return;
            _live = null;
            StartCoroutine(CloseRoutine().WrapToIl2Cpp());
        }

        private IEnumerator CloseRoutine()
        {
            yield return TearDownSurfaces();
            Destroy(gameObject);
        }
    }
}
