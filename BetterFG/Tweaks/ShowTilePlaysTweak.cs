using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using FGClient;
using TMPro;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class ShowTilePlaysTweak : BfgTweak
    {
        public ShowTilePlaysTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "showtile_plays";
        public override string TweakLabel => "tweak.show_discovery_play_counts";
        public override bool DefaultEnabled => true;

        public static ShowTilePlaysTweak Instance { get; private set; }
        void Awake() => Instance = this;

        const string TEXT_PATH = "ShowTileHolder/DefaultTags1/GameMode/GameMode_Text";

        class Tile
        {
            public FGClient.ShowSelector.ShowSelectorShowTileViewModel Vm;
            public TextMeshProUGUI Tmp;
            public GameObject Tag;
            public string Base;
            public string Written;
        }

        static readonly Dictionary<int, Tile> _tiles = new Dictionary<int, Tile>();
        static readonly Dictionary<int, string> _suffixes = new Dictionary<int, string>();

        readonly List<GameObject> _refit = new List<GameObject>();
        bool _refitQueued;

        public static void OnSetShowData(FGClient.ShowSelector.ShowSelectorShowTileViewModel tile, ShowSelectorShow show)
        {
            var inst = Instance;
            if (inst == null || tile == null) return;

            int id = tile.GetInstanceID();
            if (!_tiles.TryGetValue(id, out var t) || t.Tmp == null)
            {
                var tmp = tile.transform.Find(TEXT_PATH)?.GetComponent<TextMeshProUGUI>();
                if (tmp == null) return;
                t = new Tile { Vm = tile, Tmp = tmp, Tag = tmp.transform.parent.gameObject };
                _tiles[id] = t;
            }

            if (!inst.IsEnabled) return;
            var stats = show?.Stats;
            if (stats == null) return;
            inst.Stamp(t, stats.PlayCount);
        }

        void Stamp(Tile t, int playCount)
        {
            string cur = t.Tmp.text;
            string basis = t.Written != null && cur == t.Written ? t.Base : StripSuffix(cur);
            string final = basis + Suffix(playCount);
            t.Base = basis;
            t.Written = final;
            if (final == cur) return;

            t.Tmp.alignment = TextAlignmentOptions.Left;
            t.Tmp.text = final;
            t.Tmp.ForceMeshUpdate();
            QueueRefit(t.Tag);
        }

        static string Suffix(int playCount)
        {
            if (!_suffixes.TryGetValue(playCount, out var suffix))
            {
                suffix = " - " + playCount.ToString("N0") + " PLAYS";
                _suffixes[playCount] = suffix;
            }
            return suffix;
        }

        internal static string SuffixFor(ShowSelectorShow show)
        {
            var stats = show?.Stats;
            return Instance != null && Instance.IsEnabled && stats != null ? Suffix(stats.PlayCount) : "";
        }

        static string StripSuffix(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (!s.EndsWith(" PLAYS", StringComparison.Ordinal)) return s;

            int cut = s.LastIndexOf(" - ", StringComparison.Ordinal);
            int digits = cut + 3;
            int end = s.Length - 6;
            if (cut < 0 || end <= digits) return s;

            for (int i = digits; i < end; i++)
            {
                char c = s[i];
                if (c != ',' && (c < '0' || c > '9')) return s;
            }
            return s.Substring(0, cut);
        }

        // the layout-rebuild route kept losing to the game's own RefreshTagsLayout. instead, after the
        // game's pass settles, toggle the GameMode tag off then back on a frame later — the reactivate
        // makes Unity re-run its fit from scratch and the pill background snaps to our text.
        void QueueRefit(GameObject tag)
        {
            if (tag == null) return;
            _refit.Add(tag);
            if (_refitQueued) return;
            _refitQueued = true;
            StartCoroutine(RefitBatch().WrapToIl2Cpp());
        }

        IEnumerator RefitBatch()
        {
            yield return null;

            var batch = _refit.ToArray();
            _refit.Clear();
            _refitQueued = false;

            foreach (var tag in batch)
                if (tag != null) tag.SetActive(false);

            yield return null;

            foreach (var tag in batch)
                if (tag != null) tag.SetActive(true);
        }

        public static void PruneCache()
        {
            List<int> dead = null;
            foreach (var kv in _tiles)
                if (kv.Value.Tmp == null) (dead ??= new List<int>()).Add(kv.Key);
            if (dead != null) foreach (var k in dead) _tiles.Remove(k);

            if (_suffixes.Count > 512) _suffixes.Clear();
        }

        // toggled on mid-browse: stamp every live tile now instead of waiting for a scroll/re-open.
        public override void EnableTweak()
        {
            foreach (var t in _tiles.Values)
            {
                if (t.Tmp == null) continue;
                var stats = t.Vm?._showData?.ShowSelectorShow?.Stats;
                if (stats != null) Stamp(t, stats.PlayCount);
            }
        }

        // toggled off mid-browse: wipe the suffix off every live tile so they clear right away.
        public override void DisableTweak()
        {
            foreach (var t in _tiles.Values)
            {
                if (t.Tmp == null) continue;

                string cur = t.Tmp.text;
                string basis = StripSuffix(cur);
                t.Base = basis;
                t.Written = null;
                if (basis == cur) continue;

                t.Tmp.text = basis;
                t.Tmp.ForceMeshUpdate();
                QueueRefit(t.Tag);
            }
        }
    }
}
