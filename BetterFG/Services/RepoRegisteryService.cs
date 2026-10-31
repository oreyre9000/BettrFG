using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
namespace BetterFG.Services
{
    public class SkinRepo
    {
        public string githubUrl;   // e.g. https://github.com/oreyre9000/BetterFGPublicSkins
        public string author;      // parsed from url
        public string repoName;    // parsed from url
        public bool isDefault;     // can't be removed

        public string RawBase => $"https://raw.githubusercontent.com/{author}/{repoName}/main";
        public string ApiBase => $"https://api.github.com/repos/{author}/{repoName}/contents";
        public string DisplayName => $"{author} / {repoName}";
    }

    public class RepoSearchResult
    {
        public SkinRepo repo;
        public string description;
        public int stars;
        public string pushedAt;
        public int skinCount;
        public List<KeyValuePair<string, string>> ownSkins;
    }

    public class FeaturedRepo
    {
        public string url;         // github repo url
        public string description; // curator's blurb from featured.json
    }

    public class RepoRegistry : MonoBehaviour
    {
        public static RepoRegistry Instance { get; private set; }

        public event Action OnReposChanged;
        public event Action<string> OnValidationStatus;
        public event Action<SkinRepo, Texture2D> OnCoverLoaded;
        public event Action OnFeaturedLoaded;

        public const string DEFAULT_GITHUB_URL = "https://github.com/oreyre9000/BetterFGPublicSkins";
        public const string FEATURED_JSON_URL = "https://raw.githubusercontent.com/oreyre9000/BettrFG/main/featured.json";
        public const string TEMPLATE_GITHUB_URL = "https://github.com/oreyre9000/BettrFGDefaultRepo";
        private int _searchGen;
        public const long MAX_BUNDLE_BYTES = 4_048_576; // temu 4 MB

        private const string SETTINGS_KEY = "repos.list";
        private readonly Dictionary<string, Texture2D> _coverCache = new Dictionary<string, Texture2D>();
        private readonly HashSet<string> _coverLoading = new HashSet<string>();

        private readonly List<SkinRepo> _repos = new List<SkinRepo>();
        public IReadOnlyList<SkinRepo> Repos => _repos;

        private SkinRepo _active;
        public SkinRepo Active => _active ?? (_repos.Count > 0 ? _repos[0] : null);

        private const string KEY_ACTIVE = "repos.active";

        // featured repos — fetched once from FEATURED_JSON_URL, cached here so it survives tab rebuilds
        private readonly List<FeaturedRepo> _featured = new List<FeaturedRepo>();
        public IReadOnlyList<FeaturedRepo> Featured => _featured;
        private bool _featuredFetched;
        private bool _featuredLoading;
        public bool FeaturedFetched => _featuredFetched;

        public bool HasRepo(string githubUrl) => FindRepo(githubUrl) != null;

        public SkinRepo FindRepo(string githubUrl)
        {
            if (string.IsNullOrEmpty(githubUrl)) return null;
            foreach (var r in _repos)
                if (string.Equals(r.githubUrl, githubUrl, StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }

        public Texture2D GetCover(SkinRepo repo)
        {
            if (repo == null) return null;
            return _coverCache.TryGetValue(repo.githubUrl, out var tex) && tex != null && tex.width > 0 ? tex : null;
        }

        // returns sourceRepo if set, otherwise falls back to active repo raw base
        public static string ResolveRaw(string sourceRepo)
        {
            if (!string.IsNullOrEmpty(sourceRepo)) return sourceRepo;
            return Instance?.Active?.RawBase ?? DEFAULT_RAW;
        }

        public const string DEFAULT_RAW = "https://raw.githubusercontent.com/oreyre9000/BetterFGPublicSkins/main";

        public void FetchCover(SkinRepo repo)
        {
            if (repo == null) return;
            if (GetCover(repo) != null || _coverLoading.Contains(repo.githubUrl)) return;
            _coverLoading.Add(repo.githubUrl);
            StartCoroutine(FetchCoverCoroutine(repo).WrapToIl2Cpp());
        }

        private IEnumerator FetchCoverCoroutine(SkinRepo repo)
        {
            foreach (string coverUrl in new[] { $"{repo.RawBase}/cover.png", $"{repo.RawBase}/cover.jpg" })
            {
                var req = UnityWebRequest.Get(coverUrl);
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    var bytes = req.downloadHandler.data;
                    if (bytes != null && bytes.Length > 0)
                    {
                        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (tex.LoadImage(bytes))
                        {
                            // default wrapMode is Repeat — bilinear sampling at the UV edge then
                            // bleeds a 1px sliver of the opposite edge into the RawImage
                            tex.wrapMode = TextureWrapMode.Clamp;
                            UnityEngine.Object.DontDestroyOnLoad(tex);
                            _coverCache[repo.githubUrl] = tex;
                            _coverLoading.Remove(repo.githubUrl);
                            OnCoverLoaded?.Invoke(repo, tex);
                            req.Dispose();
                            yield break;
                        }
                    }
                }
                req.Dispose();
            }
            _coverLoading.Remove(repo.githubUrl);
        }

        // re-fetches on every call (guarded only against overlapping loads); fires OnFeaturedLoaded when ready
        public void FetchFeatured()
        {
            if (_featuredLoading) return;
            _featuredLoading = true;
            StartCoroutine(FetchFeaturedCoroutine().WrapToIl2Cpp());
        }

        private IEnumerator FetchFeaturedCoroutine()
        {
            var req = UnityWebRequest.Get(FEATURED_JSON_URL);
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                ParseFeatured(req.downloadHandler.text);
                _featuredFetched = true;
            }
            else OnValidationStatus?.Invoke("Couldn't load featured repos");

            req.Dispose();
            _featuredLoading = false;
            OnFeaturedLoaded?.Invoke();
        }

        private void ParseFeatured(string json)
        {
            _featured.Clear();
            foreach (string obj in BetterFG.Utilities.JsonUtil.GetArray(json, "repos"))
            {
                string url = BetterFG.Utilities.JsonUtil.GetValue(obj, "url");
                if (string.IsNullOrEmpty(url)) continue;
                _featured.Add(new FeaturedRepo { url = url, description = BetterFG.Utilities.JsonUtil.GetValue(obj, "description") });
            }
        }

        void Awake()
        {
            Instance = this;
            Load();
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void SetActive(SkinRepo repo)
        {
            if (repo == null || !_repos.Contains(repo)) return;
            _active = repo;
            SettingsService.Set(KEY_ACTIVE, repo.githubUrl);
            OnReposChanged?.Invoke();
        }

        /// <summary>
        /// Validates then adds a repo. Fires OnValidationStatus with result, then OnReposChanged on success.
        /// </summary>
        public void AddRepo(string githubUrl)
        {
            string normalized = NormalizeUrl(githubUrl);
            if (string.IsNullOrEmpty(normalized))
            {
                OnValidationStatus?.Invoke("Invalid URL");
                return;
            }

            foreach (var r in _repos)
                if (string.Equals(r.githubUrl, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    OnValidationStatus?.Invoke("Already added");
                    return;
                }

            var repo = ParseRepo(normalized);
            if (repo == null) { OnValidationStatus?.Invoke("Couldn't parse repo URL"); return; }

            OnValidationStatus?.Invoke($"Validating {repo.DisplayName}...");
            StartCoroutine(ValidateAndAdd(repo).WrapToIl2Cpp());
        }

        public void RemoveRepo(SkinRepo repo)
        {
            if (repo == null || repo.isDefault) return;
            _repos.Remove(repo);
            if (_active == repo) _active = _repos.Count > 0 ? _repos[0] : null;
            Save();
            OnReposChanged?.Invoke();
        }

        // ── Validation ────────────────────────────────────────────────────────

        private static IEnumerator HasMarker(SkinRepo repo, Action<bool> done)
        {
            bool ok = false;
            foreach (string marker in new[] { "betterfg", "bettrfg" })
            {
                string markerUrl = $"{repo.RawBase}/{marker}";
                var req = UnityWebRequest.Head(markerUrl);
                yield return req.SendWebRequest();

                ok = req.result == UnityWebRequest.Result.Success && req.responseCode == 200;
                req.Dispose();

                if (!ok)
                {
                    // try GET fallback, some raw hosts don't support HEAD
                    req = UnityWebRequest.Get(markerUrl);
                    yield return req.SendWebRequest();
                    ok = req.result == UnityWebRequest.Result.Success;
                    req.Dispose();
                }

                if (ok) break;
            }
            done(ok);
        }

        public void SearchRepos(string query, Action<RepoSearchResult> onFound, Action<int, int> onChecking, Action<string> onDone)
        {
            _searchGen++;
            StartCoroutine(SearchCoroutine(query, _searchGen, onFound, onChecking, onDone).WrapToIl2Cpp());
        }

        public void CancelSearch() => _searchGen++;

        private const int MIN_SKINS = 3;
        private const int CHECK_WORKERS = 8;

        private class SearchState
        {
            public int Gen, Next, Checked, Running;
            public List<RepoSearchResult> Candidates;
            public HashSet<string> BasePaths;
            public Action<RepoSearchResult> OnFound;
            public Action<int, int> OnChecking;
        }

        private static void AddCandidates(List<string> items, string mustContain, List<RepoSearchResult> found, HashSet<string> seen)
        {
            foreach (string item in items)
            {
                var repo = ParseRepo("https://github.com/" + BetterFG.Utilities.JsonUtil.GetValue(item, "full_name"));
                if (repo == null || !seen.Add(repo.githubUrl)) continue;
                string desc = BetterFG.Utilities.JsonUtil.GetValue(item, "description");
                if (!string.IsNullOrEmpty(mustContain)
                    && repo.DisplayName.IndexOf(mustContain, StringComparison.OrdinalIgnoreCase) < 0
                    && (desc ?? "").IndexOf(mustContain, StringComparison.OrdinalIgnoreCase) < 0) continue;
                found.Add(new RepoSearchResult
                {
                    repo = repo,
                    description = desc,
                    stars = BetterFG.Utilities.JsonUtil.GetInt(item, "stargazers_count"),
                    pushedAt = BetterFG.Utilities.JsonUtil.GetValue(item, "pushed_at"),
                });
            }
        }

        private static List<KeyValuePair<string, string>> OwnSkins(SkinRepo repo, List<KeyValuePair<string, string>> entries, HashSet<string> basePaths)
        {
            if (entries == null) return new List<KeyValuePair<string, string>>();
            if (string.Equals(repo.githubUrl, DEFAULT_GITHUB_URL, StringComparison.OrdinalIgnoreCase)) return entries;
            return entries.FindAll(e => !basePaths.Contains(e.Key));
        }

        private IEnumerator SearchCoroutine(string query, int gen, Action<RepoSearchResult> onFound, Action<int, int> onChecking, Action<string> onDone)
        {
            query = (query ?? "").Trim();
            string[] queries = query.Length == 0
                ? new[] { "bettrfg OR betterfg", "bfg skins", "bfg costumes", "fg skins" }
                : new[] { query, query + " bfg" };
            var found = new List<RepoSearchResult>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { TEMPLATE_GITHUB_URL };
            string error = null;

            foreach (string q in queries)
            {
                var req = UnityWebRequest.Get("https://api.github.com/search/repositories?per_page=100&q=" + UnityWebRequest.EscapeURL(q));
                req.SetRequestHeader("Accept", "application/vnd.github+json");
                yield return req.SendWebRequest();
                if (gen != _searchGen) { req.Dispose(); yield break; }
                if (req.result == UnityWebRequest.Result.Success)
                    AddCandidates(BetterFG.Utilities.JsonUtil.GetArray(req.downloadHandler.text, "items"), null, found, seen);
                else error = req.error;
                req.Dispose();
            }

            foreach (string source in new[] { TEMPLATE_GITHUB_URL, DEFAULT_GITHUB_URL })
            {
                var src = ParseRepo(source);
                var req = UnityWebRequest.Get($"https://api.github.com/repos/{src.author}/{src.repoName}/forks?per_page=100&sort=newest");
                req.SetRequestHeader("Accept", "application/vnd.github+json");
                yield return req.SendWebRequest();
                if (gen != _searchGen) { req.Dispose(); yield break; }
                if (req.result == UnityWebRequest.Result.Success)
                    AddCandidates(BetterFG.Utilities.JsonUtil.GetRootArray(req.downloadHandler.text), query, found, seen);
                req.Dispose();
            }

            var catalog = CustomizationServices.CatalogService;
            var state = new SearchState
            {
                Gen = gen, Candidates = found, OnFound = onFound, OnChecking = onChecking,
                BasePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            };
            int basesLeft = 2;
            foreach (string baseUrl in new[] { TEMPLATE_GITHUB_URL, DEFAULT_GITHUB_URL })
                catalog.FetchPreview(ParseRepo(baseUrl), entries =>
                {
                    foreach (var e in entries) state.BasePaths.Add(e.Key);
                    basesLeft--;
                });
            while (basesLeft > 0)
            {
                if (gen != _searchGen) yield break;
                yield return null;
            }

            for (int i = 0; i < CHECK_WORKERS; i++)
            {
                state.Running++;
                StartCoroutine(CheckWorker(state, catalog).WrapToIl2Cpp());
            }
            while (state.Running > 0)
            {
                if (gen != _searchGen) yield break;
                yield return null;
            }
            onDone?.Invoke(found.Count == 0 ? error : null);
        }

        private IEnumerator CheckWorker(SearchState s, BetterFG.Customization.Player.SkinCatalogService catalog)
        {
            while (s.Next < s.Candidates.Count)
            {
                var candidate = s.Candidates[s.Next++];
                bool ok = false;
                var check = HasMarker(candidate.repo, r => ok = r);
                while (check.MoveNext()) yield return check.Current;
                if (s.Gen != _searchGen) { s.Running--; yield break; }

                if (ok)
                {
                    List<KeyValuePair<string, string>> entries = null;
                    bool done = false;
                    catalog.FetchPreview(candidate.repo, e => { entries = e; done = true; });
                    while (!done) yield return null;
                    if (s.Gen != _searchGen) { s.Running--; yield break; }
                    var own = OwnSkins(candidate.repo, entries, s.BasePaths);
                    if (own.Count >= MIN_SKINS)
                    {
                        candidate.ownSkins = own;
                        candidate.skinCount = own.Count;
                        s.OnFound?.Invoke(candidate);
                    }
                }
                s.Checked++;
                s.OnChecking?.Invoke(s.Checked, s.Candidates.Count);
            }
            s.Running--;
        }

        private IEnumerator ValidateAndAdd(SkinRepo repo)
        {
            bool ok = false;
            var check = HasMarker(repo, r => ok = r);
            while (check.MoveNext()) yield return check.Current;

            if (!ok)
            {
                OnValidationStatus?.Invoke($"Not a valid BetterFG repo: {repo.DisplayName}");
                yield break;
            }

            _repos.Add(repo);
            Save();
            OnValidationStatus?.Invoke($"Added {repo.DisplayName}");
            OnReposChanged?.Invoke();
        }

        // ── Bundle size gate (used by SkinLoaderService) ──────────────────────

        /// <summary>
        /// Checks Content-Length header before downloading. Returns false + error if over 1 MB.
        /// Call before issuing a full GET on a bundle URL.
        /// </summary>
        public static IEnumerator CheckBundleSize(string url, Action<bool, string> callback)
        {
            var req = UnityWebRequest.Head(url);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                req.Dispose();
                // can't check, let it through and fail at load if broken
                callback(true, null);
                yield break;
            }

            string cl = req.GetResponseHeader("Content-Length");
            req.Dispose();

            if (!string.IsNullOrEmpty(cl) && long.TryParse(cl, out long size) && size > MAX_BUNDLE_BYTES)
            {
                callback(false, $"Bundle too large ({size / 1024}KB > 1MB), skipping");
                yield break;
            }

            callback(true, null);
        }

        // ── Persist ───────────────────────────────────────────────────────────

        private void Load()
        {
            _repos.Clear();

            // default is always first and can't be removed
            var def = ParseRepo(DEFAULT_GITHUB_URL);
            def.isDefault = true;
            _repos.Add(def);

            string raw = SettingsService.Get(SETTINGS_KEY, "");
            if (!string.IsNullOrEmpty(raw))
            {
                foreach (string url in raw.Split('|'))
                {
                    string u = url.Trim();
                    if (string.IsNullOrEmpty(u)) continue;
                    if (string.Equals(u, DEFAULT_GITHUB_URL, StringComparison.OrdinalIgnoreCase)) continue;
                    var r = ParseRepo(u);
                    if (r != null) _repos.Add(r);
                }
            }

            string activeUrl = SettingsService.Get(KEY_ACTIVE, DEFAULT_GITHUB_URL);
            _active = _repos.Find(r => string.Equals(r.githubUrl, activeUrl, StringComparison.OrdinalIgnoreCase))
                      ?? _repos[0];
        }

        private void Save()
        {
            var parts = new List<string>();
            foreach (var r in _repos)
                if (!r.isDefault) parts.Add(r.githubUrl);
            SettingsService.Set(SETTINGS_KEY, string.Join("|", parts));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        public static SkinRepo ParseRepo(string githubUrl)
        {
            try
            {
                // expected: https://github.com/{author}/{repo}[/...]
                string url = githubUrl.TrimEnd('/');
                // strip trailing segments past author/repo
                var uri = new Uri(url);
                string[] segs = uri.AbsolutePath.TrimStart('/').Split('/');
                if (segs.Length < 2) return null;
                return new SkinRepo
                {
                    githubUrl = $"https://github.com/{segs[0]}/{segs[1]}",
                    author = segs[0],
                    repoName = segs[1],
                };
            }
            catch { return null; }
        }

        private static string NormalizeUrl(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            raw = raw.Trim();
            if (!raw.StartsWith("http")) raw = "https://" + raw;
            if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) raw = "https://" + raw.Substring(7);
            return BetterFG.Utilities.UntrustedInput.IsGithubUrl(raw) ? raw : null;
        }
    }
}
