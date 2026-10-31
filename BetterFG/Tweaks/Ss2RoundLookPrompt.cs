using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Round;
using BetterFG.Features.Onboarding;
using BetterFG.Services;
using BetterFG.Utilities;
using BettrFG.uGUI;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Tweaks
{
    public class Ss2RoundLookPrompt : PromptHost
    {
        public Ss2RoundLookPrompt(IntPtr ptr) : base(ptr) { }

        private const int CanvasOrder = 1402;
        private const int BlockerOrder = 1401;
        private const float AskWidth = 600f;
        private const float LibWidth = 620f;
        private const float ListH = 400f;
        private const float RowH = 28f;
        private const float HeadH = 24f;
        private const float BtnH = 18f;
        private const float Inset = 4f;
        private const float Gutter = 19f;
        private const float StateW = 150f;
        private const int Fs = 12;
        private const string Prefix = "OGLighting/";
        private static readonly Color Cyan = new Color(0.2f, 0.9f, 1f, 1f);
        private static readonly Color Faint = new Color(1f, 1f, 1f, 0.45f);
        private static readonly Color Zebra = new Color(1f, 1f, 1f, 0.03f);
        private static readonly Color Scrim = new Color(0f, 0f, 0f, 0.85f);

        private static Ss2RoundLookPrompt _live;
        private RectTransform _content;
        private Text _count, _allLabel, _removeLabel;
        private string _query = "";
        private int _seq;
        private bool _bulk;
        private float _armUntil;
        private readonly List<(string Package, LoadingBar Bar)> _bars = new List<(string, LoadingBar)>();

        internal static void Ask() => Spawn().ShowAsk();

        internal static void OpenLibrary() => Spawn().ShowLibrary();

        private static Ss2RoundLookPrompt Spawn()
        {
            if (_live != null) return _live;
            var go = new GameObject("BetterFG_OgLightingPrompt");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            _live = go.AddComponent<Ss2RoundLookPrompt>();
            _live.CreateSurfaces("OgLighting", CanvasOrder, BlockerOrder, true);
            foreach (var img in _live._blockerCanvas.GetComponentsInChildren<Image>(true)) img.color = Scrim;
            return _live;
        }

        private void ShowAsk() => Swap(OnboardingPopup.Show(_canvas, "tweak.ss2_round_look.ask.title", "tweak.ss2_round_look.ask.body",
            Vector2.zero, new Vector2(0.5f, 0.5f),
            new[]
            {
                new OnboardingPopup.ButtonSpec("ui.cancel", new Action(Exit), OnboardingPopup.BtnGhost),
                new OnboardingPopup.ButtonSpec("tweak.ss2_round_look.ask.enable", new Action(() => Enable(false)), OnboardingPopup.BtnInfo),
                new OnboardingPopup.ButtonSpec("tweak.ss2_round_look.ask.library", new Action(() => Enable(true)), OnboardingPopup.BtnPrimary),
            }, width: AskWidth));

        private void Enable(bool library)
        {
            Ss2RoundLookTweak.Instance.SetEnabled(true);
            if (library) ShowLibrary();
            else Exit();
        }

        private static List<string> Packages()
        {
            var set = new SortedSet<string>(OnlineData.Listed(Prefix));
            string dir = OnlineData.Dir("OGLighting");
            if (Directory.Exists(dir))
                foreach (var d in Directory.GetDirectories(dir)) set.Add(Prefix + Path.GetFileName(d));
            return new List<string>(set);
        }

        private static string Pretty(string package) =>
            Regex.Replace(package.Substring(Prefix.Length).Replace("FallGuy_", "").Replace('_', ' '), "(?<=[a-z])(?=[A-Z0-9])", " ");

        private string CountText()
        {
            int have = 0, total = 0;
            long bytes = 0;
            foreach (var package in Packages())
            {
                total++;
                if (!OnlineData.Installed(package)) continue;
                have++;
                bytes += OnlineData.DiskBytes(package);
            }
            return LocalizationService.Format("tweak.ss2_round_look.library.count", have, total, bytes / 1048576);
        }

        private void ShowLibrary()
        {
            _bulk = Ss2RoundLookTweak.Bulk;
            Swap(OnboardingPopup.Show(_canvas, "tweak.ss2_round_look.library.title", CountText(), Vector2.zero, new Vector2(0.5f, 0.5f),
                new[] { new OnboardingPopup.ButtonSpec("ui.close", new Action(Exit), OnboardingPopup.BtnGhost) },
                onBody: t => _count = t, customH: ListH, onCustom: BuildList, width: LibWidth));
            _seq = Ss2RoundLookTweak.FetchSeq;
            StartCoroutine(RefreshOnManifest().WrapToIl2Cpp());
        }

        private IEnumerator RefreshOnManifest()
        {
            yield return OnlineData.LoadManifest().WrapToIl2Cpp();
            Refresh();
        }

        private void BuildList(RectTransform area)
        {
            float innerW = area.sizeDelta.x;
            const float allW = 104f, removeW = 96f;
            var search = UGUIShip.CreateInputField(area, new Rect(0f, 0f, innerW - allW - removeW - Inset * 2f, HeadH),
                "tweak.ss2_round_look.library.search", fontSize: Fs);
            search.onValueChanged.AddListener(new Action<string>(Search));

            var all = UGUIShip.CreateButton(area, new Rect(innerW - allW - removeW - Inset, 0f, allW, HeadH),
                _bulk ? "tweak.ss2_round_look.library.cancel" : "tweak.ss2_round_look.library.download_all",
                OnboardingPopup.BtnPrimary, UGUIShip.WHITE, Fs - 1, new Action(DownloadAll));
            _allLabel = all.GetComponentInChildren<Text>();
            var remove = UGUIShip.CreateButton(area, new Rect(innerW - removeW, 0f, removeW, HeadH),
                "tweak.ss2_round_look.library.remove_all", OnboardingPopup.BtnDanger, UGUIShip.WHITE, Fs - 1, new Action(RemoveAll));
            _removeLabel = remove.GetComponentInChildren<Text>();

            var (scroll, content) = UGUIShip.CreateScrollView(area, new Rect(0f, HeadH + Inset * 2f, innerW + Gutter, ListH - HeadH - Inset * 2f));
            scroll.viewport.offsetMin = Vector2.zero;
            scroll.viewport.offsetMax = new Vector2(-Gutter, 0f);
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 2f;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _content = content;
            Fill();
        }

        private void Search(string q)
        {
            _query = (q ?? "").Trim().ToLowerInvariant();
            Refresh();
        }

        private void Refresh()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--) DestroyImmediate(_content.GetChild(i).gameObject);
            Fill();
            UGUIShip.RelabelText(_count, CountText());
        }

        private void Fill()
        {
            _bars.Clear();
            int i = 0;
            foreach (var package in Packages())
                if (_query.Length == 0 || Pretty(package).ToLowerInvariant().Contains(_query)) Row(package, i++ % 2 == 0);
        }

        private void Row(string package, bool striped)
        {
            bool busy = Ss2RoundLookTweak.Fetching.TryGetValue(package, out float progress);
            bool have = OnlineData.Installed(package), stale = OnlineData.Outdated(package);

            var row = new GameObject("Row_" + package);
            row.transform.SetParent(_content, false);
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = RowH;
            le.flexibleWidth = 1f;
            UGUIShip.PaintStaticRowFill(row, striped ? Zebra : Color.clear);

            float right = Inset * 2f;
            if (!busy)
            {
                if (have) Btn(row.transform, "tweak.ss2_round_look.library.remove", OnboardingPopup.BtnDanger, 62f, ref right, () => Ss2RoundLookTweak.Remove(package));
                if (stale || !have)
                    Btn(row.transform, stale ? "tweak.ss2_round_look.library.update" : "tweak.ss2_round_look.library.download",
                        stale ? OnboardingPopup.BtnInfo : OnboardingPopup.BtnPrimary, 72f, ref right, () => Ss2RoundLookTweak.Fetch(package));
            }

            string state = busy ? "tweak.ss2_round_look.library.downloading"
                : stale ? "tweak.ss2_round_look.library.state_outdated"
                : have ? "tweak.ss2_round_look.library.state_current"
                : "tweak.ss2_round_look.library.state_missing";
            var stateLbl = UGUIShip.CreateLabel(row.transform, new Rect(0f, 0f, 10f, 10f),
                have && !busy ? LocalizationService.Format(state, OnlineData.DiskBytes(package) / 1048576) : state,
                Fs - 1, stale || busy ? Cyan : Faint, TextAnchor.MiddleRight);
            stateLbl.raycastTarget = false;
            BackgroundLibrary.Anchor(stateLbl, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-right - StateW, 0f), new Vector2(-right, 0f));

            var name = UGUIShip.CreateLabel(row.transform, new Rect(0f, 0f, 10f, 10f), Pretty(package), Fs, UGUIShip.WHITE, TextAnchor.MiddleLeft);
            name.raycastTarget = false;
            BackgroundLibrary.Anchor(name, Vector2.zero, Vector2.one, new Vector2(8f, 0f), new Vector2(-right - StateW - Inset, 0f));

            if (!busy) return;
            var bar = UGUIShip.CreateLoadingBar(row.transform, Cyan);
            BackgroundLibrary.Anchor(bar, Vector2.zero, new Vector2(1f, 0f), new Vector2(8f, 0f), new Vector2(-8f, 6f));
            bar.Set(progress);
            _bars.Add((package, bar));
        }

        private static void Btn(Transform row, string label, Color bg, float w, ref float right, Action onClick)
        {
            var b = UGUIShip.CreateButton(row, new Rect(0f, 0f, w, BtnH), label, bg, UGUIShip.WHITE, Fs - 2, onClick);
            BackgroundLibrary.Anchor(b, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-right - w, -BtnH / 2f), new Vector2(-right, BtnH / 2f));
            right += w + 4f;
        }

        private void DownloadAll()
        {
            if (Ss2RoundLookTweak.Bulk) Ss2RoundLookTweak.CancelFetchAll();
            else Ss2RoundLookTweak.FetchAll();
        }

        private void RemoveAll()
        {
            if (Time.unscaledTime > _armUntil)
            {
                _armUntil = Time.unscaledTime + 3f;
                UGUIShip.RelabelText(_removeLabel, "tweak.ss2_round_look.library.remove_confirm");
                return;
            }
            _armUntil = 0f;
            UGUIShip.RelabelText(_removeLabel, "tweak.ss2_round_look.library.remove_all");
            foreach (var package in Packages()) Ss2RoundLookTweak.Remove(package);
        }

        void Update()
        {
            if (_content == null) return;
            if (_armUntil > 0f && Time.unscaledTime > _armUntil)
            {
                _armUntil = 0f;
                UGUIShip.RelabelText(_removeLabel, "tweak.ss2_round_look.library.remove_all");
            }
            if (_bulk != Ss2RoundLookTweak.Bulk)
            {
                _bulk = Ss2RoundLookTweak.Bulk;
                UGUIShip.RelabelText(_allLabel, _bulk ? "tweak.ss2_round_look.library.cancel" : "tweak.ss2_round_look.library.download_all");
            }
            if (_seq != Ss2RoundLookTweak.FetchSeq)
            {
                _seq = Ss2RoundLookTweak.FetchSeq;
                Refresh();
                return;
            }
            foreach (var (package, bar) in _bars)
                if (Ss2RoundLookTweak.Fetching.TryGetValue(package, out float p)) bar.Set(p);
        }

        private void Exit()
        {
            if (_live != this) return;
            _live = null;
            StartCoroutine(Finish().WrapToIl2Cpp());
        }

        private IEnumerator Finish()
        {
            yield return TearDownSurfaces();
            Destroy(gameObject);
        }
    }
}
