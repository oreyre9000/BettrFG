using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Utilities;
using FG.Common.UGCNetworking;
using FGClient;
using UnityEngine;
using UnityEngine.Networking;
using LB = Wushu.LevelEditor.Runtime.UI.LevelBrowser;

namespace BetterFG.Tweaks
{
    public class ShowTileDislikesTweak : BfgTweak
    {
        public ShowTileDislikesTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "showtile_dislikes";
        public override string TweakLabel => "tweak.show_discovery_dislikes";
        public override string TweakTooltip => "tweak.show_discovery_dislikes_tip";
        public override bool DefaultEnabled => false;

        public static ShowTileDislikesTweak Instance { get; private set; }
        void Awake() => Instance = this;

        const string SEP = " / ";
        const int BATCH_MAX = 50;
        const string PILL_PATH = "ShowTileHolder/DefaultTags2/LikeCount";
        const string LB_TAGS_PATH = "OptimisedButtonRoot/Tags";
        const string ICON_NAME = "BFG_DislikeIcon";
        const string PLOBBY_TAG_NAME = "BFG_DislikeTag";
        const string LB_TAG_NAME = "BFG_DislikeTag";

        class Tile
        {
            public FGClient.ShowSelector.ShowSelectorShowTileViewModel Vm;
            public string Code;
            public Transform Pill;
            public bool IconOn;
        }

        class LbTile
        {
            public LB.LevelBrowserTileViewModel Vm;
            public string Code;
        }

        static readonly Dictionary<int, Tile> _tiles = new Dictionary<int, Tile>();
        static readonly Dictionary<int, LbTile> _lbTiles = new Dictionary<int, LbTile>();
        static readonly Dictionary<string, int> _dislikes = new Dictionary<string, int>();

        static PrivateLobbyShowTagViewModel _plobbyTag;

        readonly List<string> _pending = new List<string>();
        readonly HashSet<string> _requested = new HashSet<string>();
        bool _fetching;

        public static void OnSetShowData(FGClient.ShowSelector.ShowSelectorShowTileViewModel tile, ShowSelectorShow show)
        {
            var inst = Instance;
            if (inst == null || tile == null) return;

            string code = show?.ShareCode;
            if (string.IsNullOrEmpty(code)) return;

            int id = tile.GetInstanceID();
            if (!_tiles.TryGetValue(id, out var t)) _tiles[id] = t = new Tile { Vm = tile };
            t.Vm = tile;
            t.Code = code;

            if (!inst.IsEnabled) return;

            if (_dislikes.TryGetValue(code, out int n)) Stamp(t, n);
            else { Clear(t); inst.Request(code); }
        }

        static void Stamp(Tile t, int dislikes)
        {
            if (t.Vm == null) return;

            string cur = t.Vm.LikeCount;
            string final = StripSuffix(cur) + SEP + dislikes.ToString("N0");
            if (final == cur && t.IconOn) return;

            if (final != cur) t.Vm.LikeCount = final;

            if (t.Pill == null) t.Pill = t.Vm.transform.Find(PILL_PATH);
            if (t.Pill == null) return;

            SetIcon(t.Pill, true);
            t.IconOn = true;
            Refit(t.Vm, t.Pill);
        }

        static void Clear(Tile t)
        {
            if (t.Vm == null) return;

            string cur = t.Vm.LikeCount;
            string basis = StripSuffix(cur);
            if (basis == cur && !t.IconOn) return;

            if (basis != cur) t.Vm.LikeCount = basis;

            if (t.Pill == null) t.Pill = t.Vm.transform.Find(PILL_PATH);
            if (t.Pill == null) return;

            SetIcon(t.Pill, false);
            t.IconOn = false;
            Refit(t.Vm, t.Pill);
        }

        static void SetIcon(Transform pill, bool on)
        {
            var icon = pill.Find(ICON_NAME);
            if (icon == null)
            {
                if (!on) return;
                var src = pill.Find("LikeIcon");
                if (src == null) return;

                var clone = UnityEngine.Object.Instantiate(src.gameObject, pill);
                clone.name = ICON_NAME;
                icon = clone.transform;
                icon.localScale = new Vector3(1f, -1f, 1f);
            }
            icon.gameObject.SetActive(on);
        }

        public static void OnBrowserTile(LB.LevelBrowserTileViewModel tile)
        {
            var inst = Instance;
            if (inst == null || tile == null || !tile.HasLevel || !tile.IsPublished) return;

            string code = tile.LevelCode;
            if (string.IsNullOrEmpty(code)) return;

            int id = tile.GetInstanceID();
            if (!_lbTiles.TryGetValue(id, out var t)) _lbTiles[id] = t = new LbTile { Vm = tile };
            t.Code = code;

            if (!inst.IsEnabled) return;

            if (_dislikes.TryGetValue(code, out int n)) StampBrowser(t, n);
            else inst.Request(code);
        }

        static void StampBrowser(LbTile t, int dislikes)
        {
            if (t.Vm == null) return;

            var tags = t.Vm.transform.Find(LB_TAGS_PATH);
            if (tags == null) return;

            var mine = tags.Find(LB_TAG_NAME);
            if (dislikes < 0)
            {
                if (mine != null) mine.gameObject.SetActive(false);
                return;
            }

            var pill = tags.Find("LikeCount");
            if (mine == null)
            {
                var clone = UnityEngine.Object.Instantiate(pill.gameObject, tags);
                clone.name = LB_TAG_NAME;

                foreach (var b in clone.GetComponentsInChildren<Mediatonic.Tools.MVVM.Binding>(true))
                    UnityEngine.Object.Destroy(b);

                mine = clone.transform;
                mine.Find("LikeIcon").localScale = new Vector3(1f, -1f, 1f);
            }

            var tmp = mine.Find("LikeCount_Text").GetComponent<TMPro.TextMeshProUGUI>();
            tmp.enableWordWrapping = false;
            tmp.text = FraggleLocalizationUtils.GetLevelLikeCountString(t.Vm._localisedStrings, dislikes);
            tmp.ForceMeshUpdate();

            mine.gameObject.SetActive(pill.gameObject.activeSelf);
            mine.SetAsLastSibling();
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(tags.TryCast<RectTransform>());
        }

        public static void OnPlobbyTag(PrivateLobbyShowTagViewModel tag)
        {
            var inst = Instance;
            if (inst == null || tag == null || !tag.IsLikesCount) return;
            if (!inst.IsEnabled || !tag.isActiveAndEnabled) return;

            _plobbyTag = tag;
            inst.StartCoroutine(inst.ApplyPlobbyTag(tag).WrapToIl2Cpp());
        }

        IEnumerator ApplyPlobbyTag(PrivateLobbyShowTagViewModel tag)
        {
            yield return null;
            if (tag == null) yield break;

            var row = tag.transform.parent;
            if (row == null) yield break;

            var info = tag.GetComponentInParent<FGClient.PrivateLobbyShowInfoViewModel>();
            string code = info != null && info._codeViewModel != null ? info._codeViewModel.Code : null;
            var mine = row.Find(PLOBBY_TAG_NAME);

            if (string.IsNullOrEmpty(code) || !_dislikes.TryGetValue(code, out int n))
            {
                if (mine != null) mine.gameObject.SetActive(false);
                if (!string.IsNullOrEmpty(code)) Request(code);
                yield break;
            }

            if (mine == null)
            {
                var clone = UnityEngine.Object.Instantiate(tag.gameObject, row);
                clone.name = PLOBBY_TAG_NAME;

                foreach (var b in clone.GetComponentsInChildren<Mediatonic.Tools.MVVM.Binding>(true))
                    UnityEngine.Object.Destroy(b);
                foreach (var v in clone.GetComponentsInChildren<PrivateLobbyShowTagViewModel>(true))
                    UnityEngine.Object.Destroy(v);

                mine = clone.transform;

                var icon = mine.Find("Tag_Icon_Content/Tag_LikesCount");
                if (icon != null) icon.localScale = new Vector3(1f, -1f, 1f);
            }

            var text = mine.Find("Tag_Text");
            if (text != null)
            {
                var tmp = text.GetComponent<TMPro.TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.text = n.ToString("N0");
                    tmp.ForceMeshUpdate();
                }
            }

            mine.gameObject.SetActive(true);
            mine.SetAsLastSibling();

            var rowRt = row.TryCast<RectTransform>();
            if (rowRt != null) UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rowRt);
        }

        static void Refit(FGClient.ShowSelector.ShowSelectorShowTileViewModel vm, Transform pill)
        {
            var text = pill.Find("LikeCount_Text");
            if (text != null)
            {
                var tmp = text.GetComponent<TMPro.TextMeshProUGUI>();
                if (tmp != null) tmp.ForceMeshUpdate();
            }

            var rt = pill.TryCast<RectTransform>();
            if (rt != null) UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

            if (vm.isActiveAndEnabled) vm.StartCoroutine(vm.RefreshTagsLayout());
        }

        static string StripSuffix(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            int cut = s.LastIndexOf(SEP, StringComparison.Ordinal);
            int digits = cut + SEP.Length;
            if (cut < 0 || digits >= s.Length) return s;

            for (int i = digits; i < s.Length; i++)
            {
                char c = s[i];
                if (c != ',' && (c < '0' || c > '9')) return s;
            }
            return s.Substring(0, cut);
        }

        void Request(string code)
        {
            if (!_requested.Add(code)) return;
            _pending.Add(code);
            if (_fetching) return;
            _fetching = true;
            StartCoroutine(FetchPending().WrapToIl2Cpp());
        }

        IEnumerator FetchPending()
        {
            yield return null;

            while (_pending.Count > 0)
            {
                int take = Math.Min(_pending.Count, BATCH_MAX);
                var codes = _pending.GetRange(0, take);
                _pending.RemoveRange(0, take);

                var body = new StringBuilder("{\"share_codes\":[");
                for (int i = 0; i < codes.Count; i++)
                {
                    if (i > 0) body.Append(',');
                    body.Append('"').Append(JsonUtil.Escape(codes[i])).Append('"');
                }
                body.Append("]}");

                var req = UnityWebRequest.Post(UGCNetworkAzure.CreateRequestURL("level/batch"),
                                               body.ToString(), "application/json");
                req.SetRequestHeader("Authorization", "Bearer " + UGCNetworkAzure.SessionToken);
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success) Absorb(req.downloadHandler.text);
                else foreach (var c in codes) _requested.Remove(c);

                req.Dispose();
            }

            _fetching = false;
        }

        static void Absorb(string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            const string KEY = "\"share_code\":";
            int from = 0;
            while (true)
            {
                int at = json.IndexOf(KEY, from, StringComparison.Ordinal);
                if (at < 0) break;

                int q = at + KEY.Length;
                while (q < json.Length && (json[q] == ' ' || json[q] == '\t')) q++;

                string code = JsonUtil.ReadString(json, q, out int after);
                int next = json.IndexOf(KEY, after, StringComparison.Ordinal);
                string seg = next < 0 ? json.Substring(after) : json.Substring(after, next - after);

                int n = JsonUtil.GetInt(seg, "dislikes", -1);
                if (!string.IsNullOrEmpty(code) && n >= 0) _dislikes[code] = n;

                from = after;
            }

            foreach (var t in _tiles.Values)
            {
                if (t.Vm == null || string.IsNullOrEmpty(t.Code)) continue;
                if (_dislikes.TryGetValue(t.Code, out int d)) Stamp(t, d);
            }

            foreach (var t in _lbTiles.Values)
            {
                if (t.Vm == null || string.IsNullOrEmpty(t.Code)) continue;
                if (_dislikes.TryGetValue(t.Code, out int d)) StampBrowser(t, d);
            }

            OnPlobbyTag(_plobbyTag);
        }

        public static void PruneCache()
        {
            List<int> dead = null;
            foreach (var kv in _tiles)
                if (kv.Value.Vm == null) (dead ??= new List<int>()).Add(kv.Key);
            if (dead != null) foreach (var k in dead) _tiles.Remove(k);

            dead = null;
            foreach (var kv in _lbTiles)
                if (kv.Value.Vm == null) (dead ??= new List<int>()).Add(kv.Key);
            if (dead != null) foreach (var k in dead) _lbTiles.Remove(k);
        }

        public override void EnableTweak()
        {
            foreach (var t in _tiles.Values)
            {
                if (t.Vm == null || string.IsNullOrEmpty(t.Code)) continue;
                if (_dislikes.TryGetValue(t.Code, out int n)) Stamp(t, n);
                else Request(t.Code);
            }

            foreach (var t in _lbTiles.Values)
            {
                if (t.Vm == null || string.IsNullOrEmpty(t.Code)) continue;
                if (_dislikes.TryGetValue(t.Code, out int n)) StampBrowser(t, n);
                else Request(t.Code);
            }

            OnPlobbyTag(_plobbyTag);
        }

        public override void DisableTweak()
        {
            foreach (var t in _tiles.Values) Clear(t);
            foreach (var t in _lbTiles.Values) StampBrowser(t, -1);

            if (_plobbyTag == null) return;
            var mine = _plobbyTag.transform.parent?.Find(PLOBBY_TAG_NAME);
            if (mine != null) mine.gameObject.SetActive(false);
        }
    }
}
