using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using BetterFG.Customization.Player;
using UnityEngine.AddressableAssets;
using Vector3 = UnityEngine.Vector3;
using Quaternion = UnityEngine.Quaternion;

namespace BetterFG.Customization.UI
{
    public partial class MenuCustomizationApplication
    {
        internal const string PLINTH_PATH ="3D Environment/MainMenu_Environment/PlinthRig/CharacterAndPlinthHolder_Main/ENV_Plinth_MO";
        internal const string PLINTH_MESH_PATH = PLINTH_PATH + "/ENV_Plinth_MO";

        public Vector3 internaloffset = new Vector3(0f, 2.4387f, 0f);
        public Vector3 internaloffsetVictory = new Vector3(-0.565f, 0.7669f, 0.381f);
        public Vector3 internaloffsetGame = new Vector3(0f, -0.7075f, 0f);
        public Vector3 internaloffsetGameSeason = new Vector3(0f, 0.3179f, 0f);
        public Vector3 internalscaleGame = new Vector3(0.7f, 0.7f, 0.7f);
        public Vector3 internalscaleGameSeason = new Vector3(0.86f, 0.86f, 0.86f);

        // main-menu slot
        private GameObject _appliedPlinth;
        private string _appliedFile;
        private bool _origActive = true;

        // extra slots (victory, reward, etc.) keyed by holderGO instance id
        private readonly Dictionary<int, GameObject> _extraApplied = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, bool> _extraOrigActive = new Dictionary<int, bool>();

        // party holders showing OTHER people's profile plinths — kept separate from _extraApplied
        // so swapping your own plinth (RemovePlinth/ApplyGamePlinth) can't nuke what's on their beans
        private readonly Dictionary<int, GameObject> _profileApplied = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, bool> _profileOrigActive = new Dictionary<int, bool>();

        // one bundle per file — never double-load
        private readonly HashSet<string> _plinthFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // last applied — so BeanMonitorService.PushPlinth can immediately apply to late-arriving slots
        private SkinInfo _lastInfo;
        private AssetBundle _lastBundle;

        private GameObject _lastGamePrefab;
        private GamePlinth _appliedGame;

        public sealed class GamePlinth
        {
            public readonly string Id, Label, Guid, Cover;
            public readonly bool Season;
            public readonly float? OffsetY;
            public GamePlinth(string id, string label, string guid, bool season = false, float? offsetY = null)
            {
                Id = id; Label = label; Guid = guid; Season = season; OffsetY = offsetY;
                Cover = "BetterFG.assets.ui.plinths.plinth_" + id + ".png";
            }
        }

        public static readonly GamePlinth[] GamePlinths =
        {
            new GamePlinth("castle", "ui.castle", "bad906f2e61031347a7344f63f724784"),
            new GamePlinth("volcano", "ui.volcano", "b3dd168b45e1ca844a555d50f24a94e7"),
            new GamePlinth("heart", "ui.heart", "b98255f3d3475a1409ec08d55fcc2e70"),
            new GamePlinth("star", "ui.star", "7cf4ecfdff205b34fbe6f27e3a525d9e"),
            new GamePlinth("rainbow", "ui.rainbow", "f611682fddec41d47bf8897d6c77f850"),
            new GamePlinth("dot", "ui.dot", "5556695c2ba6aa3419fdb8ac1fb527a1"),
            new GamePlinth("log", "ui.log", "e7780eb521b78b041af0e3bdfc137908"),
            new GamePlinth("bambo", "ui.bamboo", "b80b7190b4ea80e4994037d47bb6c124"),
            new GamePlinth("pencil", "ui.pencil", "1b6ab8fae6b94be44b0d596e993b857d"),
            new GamePlinth("column", "ui.column", "353f01cad79ee11488d6103e1959c14c"),
            new GamePlinth("s03", "ui.season_3", "59d770256b43eb54e81d3dd1649c94c7", true, 0.1454f),
            new GamePlinth("s04", "ui.season_4", "0d438f5fff82cfd44a0f6d7c4ce893e0", true, 0.3906f),
            new GamePlinth("s05", "ui.season_5", "8d0beb020d0a6bf46817a5d217f2f915", true, 1.133f),
            new GamePlinth("s06", "ui.season_6", "a23952441489f6244a03351d53691b87", true),
            new GamePlinth("sy02", "ui.ss2", "8214da4674a969c4c8010bdd90fc218b", true, 2.0233f),
            new GamePlinth("s09", "ui.ss3", "956bbcfcbfa1edd429897709956a8486", true, 0.751f),
            new GamePlinth("s10_retro", "ui.ss4", "553a29ac2b5d85b44b8853fb84c1a9e6", true, 0.3841f),
            new GamePlinth("sy01_trophy", "ui.trophy", "33d60c9c72efd944f8d7902917de5194", true, 0.3841f),
        };

        public string ActiveGameId => _appliedGame?.Id;

        private Vector3 GameOffsetFor(GamePlinth gp)
        {
            var o = gp.Season ? internaloffsetGameSeason : internaloffsetGame;
            if (gp.OffsetY.HasValue) o.y = gp.OffsetY.Value;
            return o;
        }

        internal Vector3 GameScaleFor(GamePlinth gp) => gp.Season ? internalscaleGameSeason : internalscaleGame;

        private Vector3 GameOffset => GameOffsetFor(_appliedGame);

        private Vector3 GameScale => GameScaleFor(_appliedGame);

        public static bool TryGetSavedPlinthEntry(out string file, out string source, out string localPath, out string repoUrl,
            out string folder)
        {
            file = source = localPath = repoUrl = folder = "";
            var files = SettingsService.Get("skin.multi.files", "").Split(',');
            var sources = SettingsService.Get("skin.multi.sources", "").Split(',');
            var paths = SettingsService.Get("skin.multi.paths", "").Split(',');
            var repos = SettingsService.Get("skin.multi.repos", "").Split(',');
            var types = SettingsService.Get("skin.multi.types", "").Split(',');
            var folders = SettingsService.Get("skin.multi.folders", "").Split(',');

            for (int i = 0; i < files.Length; i++)
            {
                if (string.IsNullOrEmpty(files[i].Trim())) continue;
                if (i >= types.Length || SkinTypeParser.FromString(types[i]) != SkinType.Plinth) continue;
                file = files[i].Trim();
                source = i < sources.Length ? sources[i].Trim() : "";
                localPath = i < paths.Length ? paths[i].Trim() : "";
                repoUrl = i < repos.Length ? repos[i].Trim() : "";
                folder = i < folders.Length ? folders[i].Trim() : "";
                return true;
            }
            return false;
        }

        public static void SavePlinthEntry(string file, string source, string localPath, string repoUrl, string folder)
        {
            var files = new List<string>(SettingsService.Get("skin.multi.files", "").Split(','));
            var sources = new List<string>(SettingsService.Get("skin.multi.sources", "").Split(','));
            var paths = new List<string>(SettingsService.Get("skin.multi.paths", "").Split(','));
            var repos = new List<string>(SettingsService.Get("skin.multi.repos", "").Split(','));
            var types = new List<string>(SettingsService.Get("skin.multi.types", "").Split(','));
            var folders = new List<string>(SettingsService.Get("skin.multi.folders", "").Split(','));

            string At(List<string> list, int i) => i < list.Count ? list[i].Trim() : "";

            var nf = new List<string>();
            var ns = new List<string>();
            var np = new List<string>();
            var nr = new List<string>();
            var nt = new List<string>();
            var nfo = new List<string>();

            for (int i = 0; i < files.Count; i++)
            {
                if (string.IsNullOrEmpty(files[i].Trim())) continue;
                if (SkinTypeParser.FromString(At(types, i)) == SkinType.Plinth) continue;
                nf.Add(files[i].Trim());
                ns.Add(At(sources, i));
                np.Add(At(paths, i));
                nr.Add(At(repos, i));
                nt.Add(At(types, i));
                nfo.Add(At(folders, i));
            }

            if (!string.IsNullOrEmpty(file))
            {
                nf.Add(file);
                ns.Add(source);
                np.Add(localPath ?? "");
                nr.Add(repoUrl ?? "");
                nt.Add("Plinth");
                nfo.Add(folder ?? "");
            }

            SettingsService.Set("skin.multi.files", string.Join(",", nf));
            SettingsService.Set("skin.multi.sources", string.Join(",", ns));
            SettingsService.Set("skin.multi.paths", string.Join(",", np));
            SettingsService.Set("skin.multi.repos", string.Join(",", nr));
            SettingsService.Set("skin.multi.types", string.Join(",", nt));
            SettingsService.Set("skin.multi.folders", string.Join(",", nfo));
        }

        // ── Bundle registry ───────────────────────────────────────────────────

        public bool TryGetBundle(string file, out AssetBundle bundle)
        {
            bundle = null;
            if (string.IsNullOrEmpty(file)) return false;
            bundle = BetterFG.Utilities.Bundles.Get(file);
            return bundle != null;
        }

        public AssetBundle GetOrRegisterBundle(string file, AssetBundle incoming)
        {
            if (string.IsNullOrEmpty(file)) return incoming;
            var existing = BetterFG.Utilities.Bundles.Get(file);
            if (existing != null) { _plinthFiles.Add(file); return existing; }
            if (incoming != null) { BetterFG.Utilities.Bundles.Register(file, incoming); _plinthFiles.Add(file); }
            return incoming;
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void ReapplyToMainMenu()
        {
            if (_lastGamePrefab != null && _appliedGame != null)
            {
                if (_appliedPlinth != null) { Destroy(_appliedPlinth); _appliedPlinth = null; }
                _origActive = true;
                ApplyGamePlinth(_appliedGame);
                return;
            }

            if (_lastInfo == null || _lastBundle == null)
            {
                // runtime state's gone (game rebuilt the plinth screen, or initial restore never
                // landed) but a plinth is still saved — pull it back from settings and re-apply.
                SkinApplicationService.Instance?.RestorePlinthFromSettings();
                return;
            }
            if (_appliedPlinth != null) { Destroy(_appliedPlinth); _appliedPlinth = null; }
            _origActive = true;
            StartCoroutine(ApplyToMainMenuCoroutine(_lastInfo, _lastBundle).WrapToIl2Cpp());
        }

        public void ApplyPlinth(SkinInfo info, AssetBundle bundle)
        {
            if (info == null || bundle == null) return;

            bundle = GetOrRegisterBundle(info.file, bundle);
            _lastInfo = info;
            _lastBundle = bundle;
            _lastGamePrefab = null;
            _appliedGame = null;
            SavePlinthEntry(info.file, info.isLocalImport ? "local" : "remote",
                info.isLocalImport && !string.IsNullOrEmpty(info.localPath) ? System.IO.Path.GetDirectoryName(info.localPath) : "",
                info.sourceRepo, info.repoFolder);
            // commit the active file NOW (not at the end of the async coroutine) so a second Apply
            // press while this one is still loading sees ActiveFile==file and skips re-applying
            _appliedFile = info.file;

            StartCoroutine(ApplyToMainMenuCoroutine(info, bundle).WrapToIl2Cpp());

            BeanMonitorService.ClearDestroyedPlinths();
            foreach (var slot in BeanMonitorService.GetTrackedPlinths())
                StartCoroutine(ApplyToSlotCoroutine(slot, info, bundle).WrapToIl2Cpp());
        }

        public void ApplyToPlinthSlot(PlinthSlot slot)
        {
            if (slot == null) return;
            if (_lastGamePrefab != null) { PlaceGamePrefabOnSlot(slot, _lastGamePrefab); return; }
            if (_lastInfo == null || _lastBundle == null) return;
            StartCoroutine(ApplyToSlotCoroutine(slot, _lastInfo, _lastBundle).WrapToIl2Cpp());
        }

        // applies a profile's plinth (loaded from raw bytes) to one specific holder slot WITHOUT
        // touching the local plinth state (_lastInfo/_appliedPlinth). used for lobby remote players —
        // each party holder can get a different person's plinth.
        public void ApplyProfilePlinthToSlot(SkinInfo info, byte[] bytes, PlinthSlot slot)
        {
            if (info == null || bytes == null || slot == null) return;
            StartCoroutine(ApplyProfilePlinthToSlotCoroutine(info, bytes, slot).WrapToIl2Cpp());
        }

        private IEnumerator ApplyProfilePlinthToSlotCoroutine(SkinInfo info, byte[] bytes, PlinthSlot slot)
        {
            AssetBundle bundle;
            if (!TryGetBundle(info.file, out bundle) || bundle == null)
            {
                AssetBundle loaded = null;
                yield return BetterFG.Utilities.Bundles.LoadMemory(info.file, bytes, ab => loaded = ab).WrapToIl2Cpp();
                bundle = loaded;
                if (bundle == null) { Plugin.Log.LogWarning($"lobby plinth bundle wouldn't load: {info.file}"); yield break; }
                bundle = GetOrRegisterBundle(info.file, bundle);
            }
            yield return ApplyToSlotCoroutine(slot, info, bundle, isProfile: true).WrapToIl2Cpp();
        }

        public void RemovePlinth()
        {
            if (_appliedPlinth != null)
            {
                Destroy(_appliedPlinth);
                _appliedPlinth = null;
            }

            var mesh = GameObject.Find(PLINTH_MESH_PATH);
            if (mesh != null) mesh.SetActive(true);

            foreach (var kvp in _extraApplied)
                if (kvp.Value != null) Destroy(kvp.Value);
            _extraApplied.Clear();

            foreach (var slot in BeanMonitorService.GetTrackedPlinths())
            {
                if (slot.meshGO == null) continue;
                slot.meshGO.SetActive(true);
            }
            _extraOrigActive.Clear();

            foreach (var file in _plinthFiles)
                BetterFG.Utilities.Bundles.Unload(file, false);
            _plinthFiles.Clear();

            _lastInfo = null;
            _lastBundle = null;
            _lastGamePrefab = null;
            _appliedGame = null;
            _appliedFile = null;
            _origActive = true;
            SavePlinthEntry(null, "", "", "", "");
        }

        public bool HasPlinthApplied => _appliedPlinth != null || _lastInfo != null || _appliedGame != null;
        public string ActiveFile => _appliedFile;

        // ── Coroutines ────────────────────────────────────────────────────────

        private IEnumerator ApplyToMainMenuCoroutine(SkinInfo info, AssetBundle bundle)
        {
            var holder = GameObject.Find(PLINTH_PATH);
            var mesh = GameObject.Find(PLINTH_MESH_PATH);

            if (holder == null || mesh == null)
            {
                Plugin.Log.LogWarning("no plinth holder/mesh in the main menu, skipping");
                OnStatus?.Invoke("Plinth: mesh not found");
                yield break;
            }

            if (_appliedPlinth != null)
            {
                Destroy(_appliedPlinth);
                _appliedPlinth = null;
            }

            _origActive = mesh.activeSelf;
            mesh.SetActive(false);

            string prefabName = FindPrefabName(bundle);
            if (prefabName == null)
            {
                Plugin.Log.LogWarning("plinth bundle has no prefab");
                OnStatus?.Invoke("Plinth: bad bundle");
                mesh.SetActive(_origActive);
                yield break;
            }

            var req = bundle.LoadAssetAsync<GameObject>(prefabName);
            yield return req;

            var prefab = req.asset?.Cast<GameObject>();
            if (prefab == null)
            {
                Plugin.Log.LogWarning("plinth prefab cast failed");
                OnStatus?.Invoke("Plinth: load failed");
                mesh.SetActive(_origActive);
                yield break;
            }

            var clone = SpawnClone(prefab, holder.transform, internaloffset);

            yield return null;

            _appliedPlinth = clone;
            _appliedFile = info.file;

            Plugin.Log.LogInfo($"plinth {info.name} on the main menu");
            OnStatus?.Invoke($"Plinth: {info.name}");
        }

        private IEnumerator ApplyToSlotCoroutine(PlinthSlot slot, SkinInfo info, AssetBundle bundle, bool isProfile = false)
        {
            if (slot?.holderGO == null || slot.meshGO == null) yield break;

            var applied = isProfile ? _profileApplied : _extraApplied;
            var origActive = isProfile ? _profileOrigActive : _extraOrigActive;
            int id = slot.holderGO.GetInstanceID();

            if (applied.TryGetValue(id, out var existing) && existing != null)
            {
                Destroy(existing);
                applied.Remove(id);
            }

            if (!origActive.ContainsKey(id))
                origActive[id] = slot.meshGO.activeSelf;

            slot.meshGO.SetActive(false);

            string prefabName = FindPrefabName(bundle);
            if (prefabName == null)
            {
                Plugin.Log.LogWarning($"plinth bundle has no prefab, slot {slot.type}");
                slot.meshGO.SetActive(origActive[id]);
                yield break;
            }

            var req = bundle.LoadAssetAsync<GameObject>(prefabName);
            yield return req;

            var prefab = req.asset?.Cast<GameObject>();
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"plinth prefab cast failed, slot {slot.type}");
                slot.meshGO.SetActive(origActive[id]);
                yield break;
            }

            var clone = SpawnClone(prefab, slot.holderGO.transform,
                slot.type == PlinthType.Victory ? internaloffsetVictory : internaloffset);

            yield return null;

            applied[id] = clone;
            Plugin.Log.LogInfo($"plinth {info.name} -> slot {slot.type}");
        }

        public void ApplyPlinthFromSource(SkinInfo info, Action<string> status)
        {
            if (info == null) return;
            UsageStats.Applied(info);
            StartCoroutine(ApplyPlinthFromSourceCoroutine(info, status).WrapToIl2Cpp());
        }

        private IEnumerator ApplyPlinthFromSourceCoroutine(SkinInfo info, Action<string> status)
        {
            if (TryGetBundle(info.file, out var cachedBundle))
            {
                ApplyPlinth(info, cachedBundle);
                yield break;
            }

            byte[] bytes = null;

            if (info.isLocalImport && !string.IsNullOrEmpty(info.localPath))
            {
                status?.Invoke($"Loading plinth {info.name}...");
                try { bytes = System.IO.File.ReadAllBytes(info.localPath); }
                catch (Exception ex) { status?.Invoke($"Plinth read failed: {ex.Message}"); yield break; }
                yield return null;
            }
            else
            {
                string repoRaw = RepoRegistry.ResolveRaw(info.sourceRepo);
                string folder = !string.IsNullOrEmpty(info.repoFolder) ? info.repoFolder : $"Plinths/{info.file}";
                string url = $"{repoRaw}/{folder}/{info.file}";

                status?.Invoke($"Downloading plinth {info.name}...");

                var req = UnityEngine.Networking.UnityWebRequest.Get(url);
                yield return req.SendWebRequest();

                if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    status?.Invoke($"Plinth dl failed: {req.error}");
                    req.Dispose();
                    yield break;
                }

                bytes = req.downloadHandler.data;
                req.Dispose();
            }

            if (TryGetBundle(info.file, out var raceBundle))
            {
                ApplyPlinth(info, raceBundle);
                yield break;
            }

            AssetBundle bundle = BetterFG.Utilities.Bundles.LoadMemorySync(info.file, bytes);
            if (bundle == null) { status?.Invoke("Plinth: bundle load failed"); yield break; }

            ApplyPlinth(info, bundle);
        }


        public void ApplyGamePlinth(GamePlinth gp)
        {
            if (gp == null) return;
            StartCoroutine(ApplyGamePlinthCoroutine(gp).WrapToIl2Cpp());
        }

        private IEnumerator ApplyGamePlinthCoroutine(GamePlinth gp)
        {
            OnStatus?.Invoke($"Plinth: loading {gp.Label}...");
            yield return null;

            GameObject prefab = null;
            try { prefab = Addressables.LoadAssetAsync<GameObject>(gp.Guid).WaitForCompletion(); }
            catch (Exception ex) { Plugin.Log.LogWarning($"plinth {gp.Label} blew up loading from addressables: {ex.Message}"); }

            if (prefab == null)
            {
                Plugin.Log.LogWarning($"no addressable prefab behind {gp.Label} ({gp.Guid})");
                OnStatus?.Invoke("Plinth: load failed");
                yield break;
            }

            if (_appliedPlinth != null) { Destroy(_appliedPlinth); _appliedPlinth = null; }
            foreach (var kvp in _extraApplied)
                if (kvp.Value != null) Destroy(kvp.Value);
            _extraApplied.Clear();

            _lastGamePrefab = prefab;
            _appliedGame = gp;
            SavePlinthEntry(gp.Id, "game", "", "", "");
            _lastInfo = null;
            _lastBundle = null;
            _appliedFile = null;

            var holder = GameObject.Find(PLINTH_PATH);
            var mesh = GameObject.Find(PLINTH_MESH_PATH);
            if (holder == null || mesh == null)
            {
                Plugin.Log.LogWarning("no plinth holder/mesh in the main menu, skipping");
                OnStatus?.Invoke("Plinth: mesh not found");
                yield break;
            }

            _origActive = mesh.activeSelf;
            mesh.SetActive(false);

            var clone = SpawnClone(prefab, holder.transform, GameOffset);
            clone.transform.localScale = GameScale;
            yield return null;
            _appliedPlinth = clone;

            BeanMonitorService.ClearDestroyedPlinths();
            foreach (var slot in BeanMonitorService.GetTrackedPlinths())
                PlaceGamePrefabOnSlot(slot, prefab);

            Plugin.Log.LogInfo($"plinth {gp.Label} on the main menu");
            OnStatus?.Invoke($"Plinth: {gp.Label}");
        }

        public void ApplyProfileGamePlinthToSlot(string gameId, PlinthSlot slot)
        {
            if (string.IsNullOrEmpty(gameId) || slot == null) return;

            GamePlinth gp = null;
            foreach (var g in GamePlinths)
                if (g.Id == gameId) { gp = g; break; }

            if (gp == null) { Plugin.Log.LogWarning($"profile asked for game plinth '{gameId}', not in our table"); return; }

            GameObject prefab = null;
            try { prefab = Addressables.LoadAssetAsync<GameObject>(gp.Guid).WaitForCompletion(); }
            catch (Exception ex) { Plugin.Log.LogWarning($"{gp.Label} wouldn't come out of addressables: {ex.Message}"); }

            if (prefab == null) { Plugin.Log.LogWarning($"no prefab behind {gp.Label} ({gp.Guid})"); return; }

            PlaceGamePrefabOnSlot(slot, prefab, gp, isProfile: true);
            Plugin.Log.LogInfo($"{gp.Label} plinth onto a party holder");
        }

        private void PlaceGamePrefabOnSlot(PlinthSlot slot, GameObject prefab, GamePlinth gp = null, bool isProfile = false)
        {
            if (slot?.holderGO == null || slot.meshGO == null) return;

            gp = gp ?? _appliedGame;
            if (gp == null) return;

            var applied = isProfile ? _profileApplied : _extraApplied;
            var origActive = isProfile ? _profileOrigActive : _extraOrigActive;
            int id = slot.holderGO.GetInstanceID();
            if (applied.TryGetValue(id, out var existing) && existing != null)
            {
                Destroy(existing);
                applied.Remove(id);
            }

            if (!origActive.ContainsKey(id))
                origActive[id] = slot.meshGO.activeSelf;
            slot.meshGO.SetActive(false);

            var clone = SpawnClone(prefab, slot.holderGO.transform, GameOffsetFor(gp));
            clone.transform.localScale = GameScaleFor(gp);
            applied[id] = clone;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private GameObject SpawnClone(GameObject prefab, Transform holder, Vector3 offset)
        {
            var clone = Instantiate(prefab, holder);
            SkinApplicationService.SetRenderQueue(clone, 3000);
            clone.transform.localPosition = offset;
            clone.transform.localRotation = Quaternion.identity;
            clone.layer = LayerMask.NameToLayer("PlayerUI");
            clone.name = "BetterFG_Plinth";
            return clone;
        }

        internal static string FindPrefabName(AssetBundle bundle)
        {
            foreach (var name in bundle.GetAllAssetNames())
                if (name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    return name;
            return null;
        }
    }
}
