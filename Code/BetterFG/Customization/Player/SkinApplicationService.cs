using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Utilities;
using BetterFG.Services;
using BetterFG.Core;
using FGClient;
using FG.Common;
using FG.Common.CMS;
using BetterFG.Customization.UI;
using BetterFG.Network;
using BetterFG.UI.Tabs;
using Il2CppInterop.Runtime;

namespace BetterFG.Customization.Player
{
    public partial class SkinApplicationService : MonoBehaviour
    {
        public static SkinApplicationService Instance { get; private set; }

        public event Action<SkinApplyEvent> OnSkinApplied;
        public event Action<string> OnSkinRemoved;

        void Awake() => Instance = this;

        // ── Applied skins ─────────────────────────────────────────────────────
        // what our skins actually put on a bean. keyed "<beanInstanceId>|<file>", value holds the
        // live clone + everything needed to undo it (hidden children, addressable handle, bound SMRs).
        private Dictionary<string, AppliedSkinInfo> appliedSkins = new Dictionary<string, AppliedSkinInfo>();
        // original materials of renderers we made invisible instead of disabling, keyed "<applyKey>_<childIdx>"
        private Dictionary<string, Dictionary<Renderer, Material[]>> hiddenRendererMaterials = new Dictionary<string, Dictionary<Renderer, Material[]>>();

        // ── Equipped loadout (live) ───────────────────────────────────────────
        // the menu's working set: what's equipped right now, with its bundle. loadout INTENT lives in
        // RemoteProfileStore.LocalLoadout() — this is the live apply-pipeline mirror of it.
        private List<ActiveSkinSlot> activeSlots = new List<ActiveSkinSlot>();
        // bumped on every teardown; a slot whose stamp is stale is dead and its coroutine bails
        private int applyStamp = 1;
        // apply keys with a coroutine in flight, so two passes don't both spawn the same skin
        private HashSet<string> pendingKeys = new HashSet<string>();

        public List<ActiveSkinSlot> GetActiveSlots() => new List<ActiveSkinSlot>(activeSlots);

        public SkinInfo ActiveCostumeSkin()
        {
            foreach (var s in activeSlots)
                if (s?.skinInfo != null && s.type == SkinType.Costume && !string.IsNullOrEmpty(s.skinInfo.file))
                    return s.skinInfo;
            return null;
        }

        public void ScaleAppliedSkins(GameObject bean, float scale)
        {
            string prefix = bean.GetInstanceID() + "|";
            var s = new Vector3(scale, scale, scale);
            foreach (var kvp in appliedSkins)
            {
                if (!kvp.Key.StartsWith(prefix)) continue;
                var entry = kvp.Value;
                if (entry == null || entry.instance == null || entry.type == SkinType.Item) continue;
                entry.instance.transform.localScale = s;
            }
        }

        private void RaiseSkinApplied(SkinInfo skinInfo, GameObject bean, ApplyReason reason)
        {
            PlayerScaleService.RefreshVisualRig(bean);
            OnSkinApplied?.Invoke(new SkinApplyEvent { skinInfo = skinInfo, bean = bean, reason = reason });
        }

        // ── Bundles ───────────────────────────────────────────────────────────
        // single source of truth for ALL loaded bundles — local player AND remote
        private Dictionary<string, AssetBundle> loadedBundles = new Dictionary<string, AssetBundle>();
        // remote-owned files, so the menu teardown doesn't unload bundles NetworkClient still needs
        private HashSet<string> remoteBundleFiles = new HashSet<string>();
        // in-flight redownloads, so a missing bundle only gets re-fetched once
        private HashSet<string> redownloadingFiles = new HashSet<string>();

        private static string GetRepoRaw(SkinInfo skinInfo) => RepoRegistry.ResolveRaw(skinInfo?.sourceRepo);

        // ── Startup restore ───────────────────────────────────────────────────
        private bool _restoredOnStartup;
        // files we're waiting to apply once their bundle arrives via SkinLoaderService
        private List<SkinInfo> _restoreQueue = new List<SkinInfo>();

        // saved skins don't need the catalog (they carry their own repo/file in skin.multi.*), so
        // kick the restore right at plugin load instead of waiting for the catalog fetch to finish.
        // with the disk cache the bundles are loaded before the menu even exists, and the skin lands
        // the moment the menu bean spawns instead of popping in seconds later. the OnFetchCompleted
        // hook stays as a fallback — _restoredOnStartup makes it a no-op when this already ran.
        public IEnumerator EarlyRestoreCoroutine()
        {
            // let RepoRegistry/settings finish their Awake loads first
            yield return null;
            yield return null;
            var loader = CustomizationServices.LoaderService;
            if (loader == null || _restoredOnStartup) yield break;
            var catalog = CustomizationServices.CatalogService;
            RestoreFromSettings(catalog != null ? catalog.AvailableSkins : null, loader, CustomizationServices.PlinthApp);
        }

        // called once after the active repo's catalog is fetched
        // force = true lets this run again after boot (preset switching) — the whole pipeline below
        // is live-safe (download-or-cached then additive ApplySkin), the guard is only there to stop
        // the boot path double-firing.
        public void RestoreFromSettings(List<SkinInfo> catalogSkins, SkinLoaderService loader, MenuCustomizationApplication plinthApp, bool force = false)
        {
            if (_restoredOnStartup && !force) return;
            _restoredOnStartup = true;

            var catalogService = CustomizationServices.CatalogService;
            var repoRegistry = CustomizationServices.RepoRegistry;

            // kick catalog fetches for any repos referenced by saved skins (so they populate the
            // UI list later). we no longer defer the restore on the catalog — saved skins download
            // directly via their known file/repo without waiting for the catalog to enumerate.
            if (catalogService != null && repoRegistry != null)
            {
                string savedRepos = SettingsService.Get("skin.multi.repos", "");
                if (!string.IsNullOrEmpty(savedRepos))
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string entry in savedRepos.Split(','))
                    {
                        string raw = entry.Trim();
                        if (string.IsNullOrEmpty(raw) || !seen.Add(raw)) continue;
                        foreach (var repo in repoRegistry.Repos)
                        {
                            if (string.Equals(repo.RawBase, raw, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(repo.githubUrl, raw, StringComparison.OrdinalIgnoreCase))
                            {
                                catalogService.FetchSkins(repo);
                                break;
                            }
                        }
                    }
                }
            }

            // the local loadout IS a RemoteProfile — the same shape every other player's skins ride in.
            // RemoteProfileStore owns reading it back from skin.multi.*; we just apply each entry.
            var local = RemoteProfileStore.LocalLoadout();
            if (local == null) return;

            foreach (var entry in local.skins)
            {
                if (string.IsNullOrEmpty(entry.file)) continue;

                if (entry.source == "game")
                {
                    ApplySavedGamePlinth(entry.file, plinthApp);
                    continue;
                }

                // already equipped (forced preset reload) — leave it alone, only apply what's missing
                if (HasActiveSlotForFile(entry.file)) continue;

                if (entry.source == "local")
                {
                    if (!string.IsNullOrEmpty(entry.localPath) && System.IO.Directory.Exists(entry.localPath))
                    {
                        loader.OnSkinImported += OnRestoreImported;
                        loader.ImportSkinFromFolder(entry.localPath);
                    }
                    continue;
                }

                SkinInfo skinInfo = null;
                if (catalogSkins != null)
                    foreach (var sk in catalogSkins)
                        if (sk.file == entry.file) { skinInfo = sk; break; }

                // not in catalog yet (catalog still fetching, or file is in a repo we haven't
                // catalogued)? don't wait for OnSkinsLoaded — the per-skin loader pulls info.json
                // alongside the bundle anyway, and waiting for the catalog to finish enumerating
                // every other file in the repo added multi-second delays on menu enter when the
                // saved items lived in a different repo than the currently-active one.
                if (skinInfo == null)
                    skinInfo = new SkinInfo { file = entry.file, name = entry.file, sourceRepo = entry.repoUrl, type = entry.type };
                else if (!string.IsNullOrEmpty(entry.repoUrl))
                    skinInfo.sourceRepo = entry.repoUrl;

                if (!string.IsNullOrEmpty(entry.folder)) skinInfo.repoFolder = entry.folder;

                skinInfo.handOverride = entry.hand;
                RestoreOneSkin(skinInfo, loader, plinthApp);
            }
        }

        // ReapplyToMainMenu bails when the plinth app has no runtime _lastInfo (e.g. the game rebuilt
        // the plinth screen and our clone died, or the initial async restore never landed). the plinth
        // is still saved in skin.multi.*, so pull it back out of settings and re-run the restore rather
        // than leaving the game's default plinth showing while the tab says one's selected.
        public void RestorePlinthFromSettings()
        {
            var local = RemoteProfileStore.LocalLoadout();
            if (local == null) return;

            var loader = CustomizationServices.LoaderService;
            var plinthApp = CustomizationServices.PlinthApp;
            if (loader == null || plinthApp == null) return;

            foreach (var entry in local.skins)
            {
                if (SkinTypeParser.FromString(entry.type) != SkinType.Plinth || string.IsNullOrEmpty(entry.file)) continue;
                if (entry.source == "game") ApplySavedGamePlinth(entry.file, plinthApp);
                else RestoreOneSkin(new SkinInfo { file = entry.file, name = entry.file, sourceRepo = entry.repoUrl, type = entry.type, repoFolder = entry.folder }, loader, plinthApp);
                return;
            }
        }

        private static void ApplySavedGamePlinth(string id, MenuCustomizationApplication plinthApp)
        {
            if (plinthApp == null) return;
            foreach (var gp in MenuCustomizationApplication.GamePlinths)
                if (gp.Id == id) { plinthApp.ApplyGamePlinth(gp); return; }
            Plugin.Log.LogWarning($"saved plinth id {id} isn't in the game plinth table anymore, skipping");
        }

        private void RestoreOneSkin(SkinInfo skinInfo, SkinLoaderService loader, MenuCustomizationApplication plinthApp)
        {
            if (SkinTypeParser.FromString(skinInfo.type) == SkinType.Plinth)
            {
                if (plinthApp != null)
                    StartCoroutine(RestorePlinthCoroutine(skinInfo, plinthApp).WrapToIl2Cpp());
                return;
            }

            if (loadedBundles.TryGetValue(skinInfo.file, out var existingBundle) && existingBundle != null)
            {
                ApplySkin(skinInfo, existingBundle, additive: true, reason: ApplyReason.AutoReapply);
                return;
            }

            // already waiting on this file from an earlier restore pass — don't queue it twice
            if (_restoreQueue.FindIndex(s => s.file == skinInfo.file) >= 0) return;

            _restoreQueue.Add(skinInfo);
            loader.OnSkinLoaded += OnRestoreDownloaded;

            string category = SkinTypeParser.CategoryFolder(skinInfo.type);
            string folder = !string.IsNullOrEmpty(skinInfo.repoFolder) ? skinInfo.repoFolder : $"{category}/{skinInfo.file}";
            string repoRaw = RepoRegistry.ResolveRaw(skinInfo.sourceRepo);
            string url = $"{repoRaw}/{folder}/{skinInfo.file}";
            string infoUrl = $"{repoRaw}/{folder}/info.json";

            Plugin.Log.LogInfo($"BetterFG: Downloading: {url}");
            skinInfo.sourceRepo = repoRaw;
            loader.DownloadSkinWithInfo(skinInfo.file, url, infoUrl);
        }

        private void OnRestoreDownloaded(SkinInfo skinInfo, AssetBundle bundle)
        {
            int idx = _restoreQueue.FindIndex(s => s.file == skinInfo.file);
            if (idx < 0) return;
            SkinInfo queued = _restoreQueue[idx];
            _restoreQueue.RemoveAt(idx);

            // unsubscribe when queue drains
            if (_restoreQueue.Count == 0)
            {
                var loader = CustomizationServices.LoaderService;
                if (loader != null) loader.OnSkinLoaded -= OnRestoreDownloaded;
            }

            // the loader already fetched + parsed this skin's info.json (bone offsets included) while
            // downloading. carry that onto the queued stub so SetupBoneSyncCoroutine uses the offsets
            // directly and skips its fallback info.json request to github (~0.8s per skin) — even for
            // skins with zero offsets, since infoFetched proves there's nothing more to go get.
            // keepBase/skinScale MUST come across too: the restore stub is built blind from settings
            // (keepBase defaults false), so without this a keepBase=true overlay costume gets treated
            // as full-body and hides the bean it's meant to sit on.
            if (skinInfo != null && skinInfo.infoFetched)
            {
                queued.boneOffsets = skinInfo.boneOffsets;
                queued.infoFetched = true;
                queued.keepBase = skinInfo.keepBase;
                if (skinInfo.skinScale > 0f) queued.skinScale = skinInfo.skinScale;
            }

            ApplySkin(queued, bundle, additive: true, reason: ApplyReason.AutoReapply);
        }

        private void OnRestoreImported(SkinInfo skinInfo, AssetBundle bundle, Texture2D _cover)
        {
            var loader = CustomizationServices.LoaderService;
            if (loader != null) loader.OnSkinImported -= OnRestoreImported;
            ApplySkin(skinInfo, bundle, additive: true, reason: ApplyReason.AutoReapply);
        }

        private IEnumerator RestorePlinthCoroutine(SkinInfo info, MenuCustomizationApplication plinthApp)
        {
            if (plinthApp.TryGetBundle(info.file, out var cached)) { plinthApp.ApplyPlinth(info, cached); yield break; }

            string repoRaw = RepoRegistry.ResolveRaw(info.sourceRepo);
            string folder = !string.IsNullOrEmpty(info.repoFolder) ? info.repoFolder : $"Plinths/{info.file}";
            string url = $"{repoRaw}/{folder}/{info.file}";
            var loader = CustomizationServices.LoaderService;

            string cachePath = SkinLoaderService.CachePathFor(info.file);
            AssetBundle bundle = null;
            if (cachePath != null && System.IO.File.Exists(cachePath))
            {
                AssetBundle fileLoaded = null;
                yield return Bundles.LoadFile(info.file, cachePath, ab => fileLoaded = ab).WrapToIl2Cpp();
                bundle = fileLoaded;
                if (bundle == null) { try { System.IO.File.Delete(cachePath); } catch { } }
            }

            if (bundle == null)
            {
                var req = UnityWebRequest.Get(url);
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { req.Dispose(); yield break; }

                byte[] bytes = req.downloadHandler.data;
                req.Dispose();

                if (plinthApp.TryGetBundle(info.file, out var raced)) { plinthApp.ApplyPlinth(info, raced); yield break; }

                if (cachePath != null && loader != null)
                    loader.StartCoroutine(loader.SaveCacheCoroutine(cachePath, bytes).WrapToIl2Cpp());
                AssetBundle memLoaded = null;
                yield return Bundles.LoadMemory(info.file, bytes, ab => memLoaded = ab).WrapToIl2Cpp();
                bundle = memLoaded;
            }

            if (bundle == null) yield break;
                plinthApp.ApplyPlinth(info, bundle);
        }

        public bool TryGetLoadedBundle(string file, out AssetBundle bundle)
        {
            bundle = null;
            return !string.IsNullOrEmpty(file) && loadedBundles.TryGetValue(file, out bundle) && bundle != null;
        }

        // NetworkClient calls this to register its bundles into the shared registry
        public void RegisterRemoteBundle(string file, AssetBundle bundle)
        {
            if (string.IsNullOrEmpty(file) || bundle == null) return;
            loadedBundles[file] = bundle;
            remoteBundleFiles.Add(file);
        }

        public void UnloadBundleForFile(string file, bool force = false)
        {
            if (string.IsNullOrEmpty(file)) return;
            if (!loadedBundles.TryGetValue(file, out var b) || b == null) return;
            // normally never unload remote-owned bundles — NetworkClient still needs them.
            // force is the local-import path: it MUST drop the resident bundle (even a repo one)
            // or LoadFromMemory throws "same files already loaded" on the updated copy.
            if (remoteBundleFiles.Contains(file) && !force) return;
            Plugin.Log.LogInfo($"pre-unload bundle '{file}'{(force ? " (forced for local import)" : "")}");
            b.Unload(false);
            loadedBundles.Remove(file);
            remoteBundleFiles.Remove(file);
        }

        // called by NetworkClient.CleanupAllRemote so we can actually release remote bundles
        public void UnloadRemoteBundle(string file)
        {
            if (string.IsNullOrEmpty(file)) return;
            remoteBundleFiles.Remove(file);
            if (!loadedBundles.TryGetValue(file, out var b) || b == null) return;
            b.Unload(false);
            loadedBundles.Remove(file);
        }

        public void UpdateItemOffsets(string file, Vector3 offset)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var kvp in appliedSkins)
            {
                if (kvp.Value == null || kvp.Value.instance == null) continue;
                if (kvp.Value.type != SkinType.Item) continue;
                if (!kvp.Key.Contains("|" + file)) continue;

                bool isLeft = kvp.Key.EndsWith("_L");
                ActiveSkinSlot slot = null;
                foreach (var s in activeSlots)
                    if (s.skinInfo.file == file) { slot = s; break; }
                if (slot == null) continue;

                ItemHandInfo handInfo = isLeft ? slot.skinInfo.left : slot.skinInfo.right;
                if (handInfo == null) continue;

                string hk = isLeft ? "l" : "r";
                bool isEdited = (slot.skinInfo.handOverride == 1 && isLeft) || (slot.skinInfo.handOverride == 2 && !isLeft);
                Vector3 off;
                if (isEdited)
                    off = offset;
                else
                {
                    float ox = float.TryParse(SettingsService.Get($"item{hk}offset.{file}.x", "0"), System.Globalization.NumberStyles.Float, ci, out float vx) ? vx : 0f;
                    float oy = float.TryParse(SettingsService.Get($"item{hk}offset.{file}.y", "0"), System.Globalization.NumberStyles.Float, ci, out float vy) ? vy : 0f;
                    float oz = float.TryParse(SettingsService.Get($"item{hk}offset.{file}.z", "0"), System.Globalization.NumberStyles.Float, ci, out float vz) ? vz : 0f;
                    off = new Vector3(ox, oy, oz);
                }
                Vector3 basePos = new Vector3(handInfo.position[0], handInfo.position[1], handInfo.position[2]);
                kvp.Value.instance.transform.localPosition = basePos + off;
            }
        }

        public void UpdateItemRotations(string file, Vector3 eulerOffset)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var kvp in appliedSkins)
            {
                if (kvp.Value == null || kvp.Value.instance == null) continue;
                if (kvp.Value.type != SkinType.Item) continue;
                if (!kvp.Key.Contains("|" + file)) continue;

                bool isLeft = kvp.Key.EndsWith("_L");
                ActiveSkinSlot slot = null;
                foreach (var s in activeSlots)
                    if (s.skinInfo.file == file) { slot = s; break; }
                if (slot == null) continue;

                ItemHandInfo handInfo = isLeft ? slot.skinInfo.left : slot.skinInfo.right;
                if (handInfo == null) continue;

                string hk = isLeft ? "l" : "r";
                bool isEdited = (slot.skinInfo.handOverride == 1 && isLeft) || (slot.skinInfo.handOverride == 2 && !isLeft);
                Vector3 rot;
                if (isEdited)
                    rot = eulerOffset;
                else
                {
                    float rx = float.TryParse(SettingsService.Get($"item{hk}rot.{file}.x", "0"), System.Globalization.NumberStyles.Float, ci, out float vx) ? vx : 0f;
                    float ry = float.TryParse(SettingsService.Get($"item{hk}rot.{file}.y", "0"), System.Globalization.NumberStyles.Float, ci, out float vy) ? vy : 0f;
                    float rz = float.TryParse(SettingsService.Get($"item{hk}rot.{file}.z", "0"), System.Globalization.NumberStyles.Float, ci, out float vz) ? vz : 0f;
                    rot = new Vector3(rx, ry, rz);
                }
                Vector3 baseRot = new Vector3(handInfo.rotation[0], handInfo.rotation[1], handInfo.rotation[2]);
                kvp.Value.instance.transform.localEulerAngles = baseRot + rot;
            }
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void ApplySkin(SkinInfo skinInfo, AssetBundle bundle, bool additive = false, ApplyReason reason = ApplyReason.FromMenu)
        {
            if (!additive) RemoveMenuEquippedSlotsOnly();

            // loadedBundles only keys on filename — if whatever's currently active under this
            // filename came from a DIFFERENT repo, its cached bundle is a different skin's content
            // and must not survive into GetOrRegisterBundle below, or the freshly downloaded bundle
            // gets thrown away in favour of the stale one
            foreach (var s in activeSlots)
            {
                if (s?.skinInfo == null || s.skinInfo.file != skinInfo.file) continue;
                if (s.skinInfo.sourceRepo != skinInfo.sourceRepo) UnloadBundleForFile(skinInfo.file, force: true);
                break;
            }

            RemoveSlotByFile(skinInfo.file);

            SkinType type = SkinTypeParser.FromString(skinInfo.type);
            if (type == SkinType.Unknown)
            {
                Plugin.Log.LogWarning($"unknown type for {skinInfo.name}");
                return;
            }

            bundle = GetOrRegisterBundle(skinInfo.file, bundle);
            redownloadingFiles.Remove(skinInfo.file);

            var slot = new ActiveSkinSlot { skinInfo = skinInfo, bundle = bundle, type = type, applyStamp = applyStamp };
            activeSlots.Add(slot);

            // a full-body costume just went on — tear down any game-cosmetic meshes already sitting on
            // the local beans. they stay in the saved loadout and come back when this costume is removed.
            if (type == SkinType.Costume && !skinInfo.keepBase) StripAppliedGameCosmeticInstancesLocal();

            Plugin.Log.LogInfo($"ApplySkin '{skinInfo.name}' type={type} additive={additive} reason={reason}");
            StartCoroutine(ApplySlotToAllBeansCoroutine(slot, reason).WrapToIl2Cpp());
        }

        public IEnumerator ApplySkinToBean(ActiveSkinSlot slot, GameObject bean)
        {
            if (bean == null || slot == null) yield break;
            slot.remotePipeline = true; // only the remote pipeline enters here
            string key = MakeKey(bean, slot.skinInfo.file);
            if (appliedSkins.ContainsKey(key) || pendingKeys.Contains(key)) yield break;
            pendingKeys.Add(key);
            yield return ApplySlotToBeanCoroutine(slot, bean, ApplyReason.AutoReapply).WrapToIl2Cpp();
        }

        public void OnBeansFound(List<GameObject> beans)
        {
            if (beans == null || beans.Count == 0) return;

            PruneDestroyedEntries();

            if (activeSlots.Count > 0)
            {
                foreach (var slot in activeSlots)
                {
                    if (redownloadingFiles.Contains(slot.skinInfo.file)) continue;
                    foreach (var bean in beans)
                    {
                        if (bean == null) continue;
                        if (SkipMenuSkinAutoApplyForThisBean(bean)) continue;
                        string key = MakeKey(bean, slot.skinInfo.file);
                        if (appliedSkins.ContainsKey(key) || pendingKeys.Contains(key)) continue;
                        if (slot.bundle == null) { TriggerReload(slot.skinInfo); continue; }
                        pendingKeys.Add(key);
                        StartCoroutine(ApplySlotToBeanCoroutine(slot, bean, ApplyReason.AutoReapply).WrapToIl2Cpp());
                    }
                }
            }

            if (activeGameCosmetics.Count > 0)
            {
                foreach (var slot in activeGameCosmetics)
                {
                    foreach (var bean in beans)
                    {
                        if (bean == null || SkipMenuSkinAutoApplyForThisBean(bean)) continue;
                        string key = MakeKey(bean, slot.id);
                        if (appliedSkins.ContainsKey(key) || pendingKeys.Contains(key)) continue;
                        pendingKeys.Add(key);
                        StartCoroutine(ApplyGameCosmeticToBeanCoroutine(slot, bean, ApplyReason.AutoReapply).WrapToIl2Cpp());
                    }
                }
            }

            foreach (var bean in beans)
            {
                if (bean == null || SkipMenuSkinAutoApplyForThisBean(bean)) continue;
                ApplyGameColourPatternToBean(bean);
                // the lobby/UI display beans get rebuilt by the game a few frames after they first
                // appear, which wipes the scale we just set. re-apply it over the next handful of frames
                // so it survives (the in-round FallGuy bean carries scale on a wrapper and doesn't need this).
                if (GameObjectHelper.IsLobbyCharacter(bean) || GameObjectHelper.IsUICharacter(bean))
                    StartCoroutine(ReapplyMenuBeanScaleCoroutine(bean).WrapToIl2Cpp());
                if (bean == BeanMonitorService.LocalPlayerBean) continue;
                TryAutoReapplyCustomTextureForBean(bean);
            }
        }

        private IEnumerator ReapplyMenuBeanScaleCoroutine(GameObject bean)
        {
            for (int i = 0; i < 8 && bean != null; i++)
            {
                PlayerScaleService.ApplyToBean(bean, 1f, PlayerScaleService.BeanScaleMode.Local);
                yield return null;
            }
        }

        public void RemoveMenuEquippedSlotsOnly()
        {
            applyStamp++;
            Plugin.Log.LogInfo($"RemoveMenuEquippedSlotsOnly | activeSlots={activeSlots.Count}, applied={appliedSkins.Count}");
            // game cosmetics live in appliedSkins too (keyed by their id); never sweep those —
            // they're owned by activeGameCosmetics and removed via RemoveAllGameCosmetics only
            var gameCosmeticIds = new HashSet<string>();
            foreach (var gc in activeGameCosmetics)
                if (gc != null && !string.IsNullOrEmpty(gc.id))
                    gameCosmeticIds.Add(gc.id);

            var files = new HashSet<string>();
            foreach (var s in activeSlots)
                if (s?.skinInfo != null && !string.IsNullOrEmpty(s.skinInfo.file))
                    files.Add(s.skinInfo.file);
            var localBean = BeanMonitorService.LocalPlayerBean;
            foreach (var kvp in appliedSkins)
            {
                var bean = kvp.Value?.bean;
                if (localBean != null && bean != null && bean != localBean && IsRemoteInRoundBean(bean)) continue;
                string file = SkinFileFromAppliedKey(kvp.Key);
                if (!string.IsNullOrEmpty(file) && !gameCosmeticIds.Contains(file)) files.Add(file);
            }
            foreach (var f in files)
                RemoveSlotByFile(f);

            PruneLoadedBundlesNotUsedByAppliedSkins();
            redownloadingFiles.Clear();
            StartCoroutine(RestoreMenuBeanGEOSoon().WrapToIl2Cpp());
            PlayerScaleService.ApplyToAll(PlayerScaleService.HasUserSetScale() ? PlayerScaleService.GetPlayerScale() : 1f);
            OnSkinRemoved?.Invoke("Menu slots cleared");
        }

        // returns true if this file currently has an active menu-equipped slot
        public bool HasActiveSlotForFile(string file)
        {
            if (string.IsNullOrEmpty(file)) return false;
            foreach (var s in activeSlots)
                if (s?.skinInfo != null && s.skinInfo.file == file) return true;
            return false;
        }

        // same, but also requires the active slot to be from the same repo. two repos can ship a
        // skin under the same filename — matching on filename alone made a freshly-selected skin
        // from a different repo look "already equipped", so OnApply skipped it entirely and the
        // bean kept showing the OLD repo's version while the UI showed the new one selected
        public bool HasActiveSlotForFile(SkinInfo skinInfo)
        {
            if (skinInfo == null || string.IsNullOrEmpty(skinInfo.file)) return false;
            foreach (var s in activeSlots)
                if (s?.skinInfo != null && s.skinInfo.file == skinInfo.file && s.skinInfo.sourceRepo == skinInfo.sourceRepo)
                    return true;
            return false;
        }

        // the menu loadout has a full-body UGC costume (Costume with keepBase off) that hides the base
        // bean. when one's on, applying in-game cosmetics (game costumes/colour/pattern/faceplate) is
        // pointless — they'd be hidden under the costume and the CostumePoller would just spend forever
        // fighting to hide them again. so we skip applying them entirely while it's equipped. accessories
        // and items still apply (they sit ON the costume). taking the costume back off hits the normal
        // removal path (RestoreMenuBeanGEOSoon / RemoveMenuEquippedSlotsOnly) which re-composites the
        // game cosmetics fresh, so nothing's lost.
        public bool LoadoutHidesBaseBody()
        {
            foreach (var s in activeSlots)
                if (s?.skinInfo != null && s.type == SkinType.Costume && !s.skinInfo.keepBase) return true;
            return false;
        }

        // re-equip an already-loaded skin in place using its cached bundle (no redownload).
        // used when only a property like handOverride changed — we drop the old slot's instances
        // and respawn just this one, leaving everything else on the bean untouched.
        public bool TryReapplyLoadedSkin(SkinInfo skinInfo)
        {
            if (skinInfo == null || !TryGetLoadedBundle(skinInfo.file, out var bundle)) return false;
            // ApplySkin(additive:true) already drops the existing slot for this file before respawning
            ApplySkin(skinInfo, bundle, additive: true, reason: ApplyReason.FromMenu);
            return true;
        }

        // targeted unequip of a single menu skin — does NOT touch any other applied slot.
        // use this instead of RemoveMenuEquippedSlotsOnly when only one item changed so we
        // don't tear down + re-apply the whole loadout (no flicker, no pointless redownloads)
        public void RemoveOneSkinByFile(string file)
        {
            if (string.IsNullOrEmpty(file) || !HasActiveSlotForFile(file)) return;

            // figure out the slot type before we remove it. only COSTUMES hide/replace the shared
            // body GEO — taking one off knocks the composited game cosmetics off the bean, so they
            // MUST be re-composited. accessories and items are additive attachments (accessories
            // merge their own mesh onto the bones, they never hide Body_LOD0), so removing one
            // touches nothing else and can skip the heavy GEO/cosmetic restore.
            SkinType removedType = SkinType.Unknown;
            foreach (var s in activeSlots)
                if (s?.skinInfo != null && s.skinInfo.file == file) { removedType = s.type; break; }

            Plugin.Log.LogInfo($"RemoveOneSkinByFile '{file}' type={removedType}");
            RemoveSlotByFile(file);
            PruneLoadedBundlesNotUsedByAppliedSkins();
            redownloadingFiles.Remove(file);

            if (removedType == SkinType.Costume)
                StartCoroutine(RestoreMenuBeanGEOSoon().WrapToIl2Cpp());

            var localBean = BeanMonitorService.LocalPlayerBean;
            if (localBean != null) PlayerScaleService.ApplyToBean(localBean, 1f, PlayerScaleService.BeanScaleMode.Local);
            foreach (var bean in BeanMonitorService.GetTrackedBeans())
            {
                if (bean == null || bean == localBean || IsRemoteInRoundBean(bean)) continue;
                PlayerScaleService.ApplyToBean(bean, 1f, PlayerScaleService.BeanScaleMode.Local);
            }

            SpawnGameCosmeticPoof();
            OnSkinRemoved?.Invoke($"Unequipped {file}");
        }

        public void ApplyGameCosmetic(CostumeOption option)
        {
            if (option == null) return;
            ApplyGameCosmeticsOnly(new List<CostumeOption> { option });
        }

        public bool ApplyGameCosmeticsOnly(List<CostumeOption> options)
        {
            options ??= new List<CostumeOption>();

            var wantedIds = new HashSet<string>();
            for (int i = 0; i < options.Count; i++)
            {
                var option = options[i];
                if (option == null) continue;
                string id = GetGameCosmeticOptionId(option);
                wantedIds.Add(id);
            }

            return ApplyGameCosmeticSelection(options, wantedIds);
        }

        public bool ApplyGameCosmeticSelection(List<CostumeOption> options, HashSet<string> wantedIds)
        {
            options ??= new List<CostumeOption>();
            wantedIds ??= new HashSet<string>();
            SaveGameCosmeticIds(wantedIds);

            var wantedOptions = new Dictionary<string, CostumeOption>();
            for (int i = 0; i < options.Count; i++)
            {
                var option = options[i];
                if (option == null) continue;
                string id = GetGameCosmeticOptionId(option);
                if (wantedIds.Contains(id) && !wantedOptions.ContainsKey(id))
                    wantedOptions[id] = option;
            }

            bool changed = false;
            var current = new HashSet<string>();
            foreach (var slot in activeGameCosmetics)
                if (slot != null && !string.IsNullOrEmpty(slot.id))
                    current.Add(slot.id);

            var remove = new List<string>();
            foreach (var id in current)
                if (!wantedIds.Contains(id)) remove.Add(id);
            foreach (var id in remove)
            {
                RemoveGameCosmeticById(id);
                changed = true;
            }

            foreach (var kvp in wantedOptions)
            {
                if (current.Contains(kvp.Key)) continue;
                var slot = new GameCosmeticSlot
                {
                    option = kvp.Value,
                    id = kvp.Key,
                    name = GetGameName(kvp.Value, "cosmetic"),
                    applyStamp = applyStamp
                };
                activeGameCosmetics.Add(slot);
                Plugin.Log.LogInfo($"ApplyGameCosmetic '{slot.name}'");
                StartCoroutine(ApplyGameCosmeticToAllBeansCoroutine(slot, ApplyReason.FromMenu).WrapToIl2Cpp());
                changed = true;
            }

            if (changed) SpawnGameCosmeticPoof();
            return changed;
        }

        public void RemoveAllGameCosmetics()
        {
            var ids = new List<string>();
            foreach (var slot in activeGameCosmetics)
                if (slot != null && !string.IsNullOrEmpty(slot.id)) ids.Add(slot.id);
            foreach (var id in ids) RemoveGameCosmeticById(id);
            activeGameCosmetics.Clear();
            SaveGameCosmeticIds(null);
            if (ids.Count > 0) SpawnGameCosmeticPoof();
            OnSkinRemoved?.Invoke("Game cosmetics cleared");
        }

        public void RemoveGameCosmetic(string id)
        {
            bool had = false;
            foreach (var slot in activeGameCosmetics)
                if (slot != null && slot.id == id) { had = true; break; }
            RemoveGameCosmeticById(id);
            SaveGameCosmeticIds(GetAppliedGameCosmeticIds());
            if (had) SpawnGameCosmeticPoof();
            OnSkinRemoved?.Invoke("Game cosmetic removed");
        }

        public List<GameCosmeticEntry> GetAppliedGameCosmetics()
        {
            var list = new List<GameCosmeticEntry>();
            foreach (var slot in activeGameCosmetics)
                if (slot != null && !string.IsNullOrEmpty(slot.id))
                    list.Add(new GameCosmeticEntry { id = slot.id, name = slot.name, option = slot.option, kind = "costume" });
            if (activeColour.On && !string.IsNullOrEmpty(activeColour.id))
                list.Add(new GameCosmeticEntry { id = activeColour.id, name = activeColour.name, option = activeColour.option, kind = "colour" });
            if (activePattern.On && !string.IsNullOrEmpty(activePattern.id))
                list.Add(new GameCosmeticEntry { id = activePattern.id, name = activePattern.name, option = activePattern.option, kind = "pattern" });
            if (activeFaceplate.On && !string.IsNullOrEmpty(activeFaceplate.id))
                list.Add(new GameCosmeticEntry { id = activeFaceplate.id, name = activeFaceplate.name, option = activeFaceplate.option, kind = "faceplate" });
            return list;
        }

        public List<GameCosmeticEntry> GetSavedGameCosmetics()
        {
            var saved = LoadSavedGameCosmetics();
            var applied = GetAppliedGameCosmetics();
            var appliedIds = new HashSet<string>();
            foreach (var e in applied) appliedIds.Add(e.id);

            var byId = new Dictionary<string, GameCosmeticEntry>();
            foreach (var e in saved)
            {
                e.enabled = appliedIds.Contains(e.id);
                byId[e.id] = e;
            }
            foreach (var e in applied) byId[e.id] = e;

            var merged = new List<GameCosmeticEntry>(byId.Values);
            SaveSavedGameCosmetics(merged);
            return merged;
        }

        public void SetGameCosmeticEnabled(GameCosmeticEntry entry, bool enabled)
        {
            if (entry == null) return;
            entry.enabled = enabled;

            if (!enabled)
            {
                if (entry.kind == "colour") RemoveGameColour();
                else if (entry.kind == "pattern") RemoveGamePattern();
                else if (entry.kind == "faceplate") RemoveGameFaceplate();
                else RemoveGameCosmetic(entry.id);
            }
            else
            {
                var option = entry.option ?? ResolveGameCosmeticOption(entry.kind, entry.id);
                if (option == null) { Plugin.Log.LogWarning($"{entry.name} not in the catalog, can't turn it back on"); entry.enabled = false; return; }

                if (entry.kind == "colour") ApplyGameColour(option.Cast<ColourOption>());
                else if (entry.kind == "pattern") ApplyGamePattern(option.Cast<SkinPatternOption>());
                else if (entry.kind == "faceplate") ApplyGameFaceplate(option.Cast<FaceplateOption>());
                else
                {
                    var costume = option.Cast<CostumeOption>();
                    var wantedIds = GetAppliedGameCosmeticIds();
                    wantedIds.Add(entry.id);
                    var combined = new List<CostumeOption>();
                    foreach (var slot in activeGameCosmetics)
                        if (slot?.option != null) combined.Add(slot.option);
                    combined.Add(costume);
                    ApplyGameCosmeticSelection(combined, wantedIds);
                }
            }

            var saved = LoadSavedGameCosmetics();
            bool found = false;
            for (int i = 0; i < saved.Count; i++)
            {
                if (saved[i].id != entry.id) continue;
                saved[i].enabled = enabled;
                found = true;
                break;
            }
            if (!found) saved.Add(entry);
            SaveSavedGameCosmetics(saved);
        }

        public static UnityEngine.Object ResolveGameCosmeticOption(string kind, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (kind == "colour") return ResolveColourOption(id, -1);
            if (kind == "pattern") return GetPatternLookup().TryGetValue(id, out var p) ? p : null;
            if (kind == "faceplate") return GetFaceplateLookup().TryGetValue(id, out var f) ? f : null;
            return GetCostumeLookup().TryGetValue(id, out var c) ? c : null;
        }

        public void ForgetGameCosmetic(GameCosmeticEntry entry)
        {
            if (entry == null) return;
            if (entry.enabled) SetGameCosmeticEnabled(entry, false);
            var saved = LoadSavedGameCosmetics();
            saved.RemoveAll(e => e.id == entry.id);
            SaveSavedGameCosmetics(saved);
        }

        public HashSet<string> GetAppliedGameCosmeticIds()
        {
            var ids = new HashSet<string>();
            foreach (var slot in activeGameCosmetics)
                if (slot != null && !string.IsNullOrEmpty(slot.id))
                    ids.Add(slot.id);
            return ids;
        }

        // we save FullItemId, which is "<group>.<item>" behind our own tag, e.g.
        // "gamecosm:costumes_upper.top_boxer_02". the game's *WithId lookups want the bare ItemId
        // ("top_boxer_02"), so drop our tag AND the group. returns the bare id, and the tagless
        // FullItemId as a fallback in case a given lookup does key on the full thing.
        private static string StripIdPrefix(string id, out string full)
        {
            int c = id.IndexOf(':');
            full = c >= 0 ? id.Substring(c + 1) : id;
            int d = full.LastIndexOf('.');
            return d >= 0 ? full.Substring(d + 1) : full;
        }

        private static string StripIdPrefix(string id) => StripIdPrefix(id, out _);

        // we don't persist which slot a costume came from, so try all three
        private static CostumeOption LookupCostume(CustomisationManager cm, string savedId)
        {
            string id = StripIdPrefix(savedId, out string full);
            var opt = cm.GetUpperCostumeWithId(id, false);
            if (opt == null) opt = cm.GetLowerCostumeWithId(id, false);
            if (opt == null) opt = cm.GetFullCostumeWithId(id, false);
            if (opt == null) opt = cm.GetUpperCostumeWithId(full, false);
            if (opt == null) opt = cm.GetLowerCostumeWithId(full, false);
            if (opt == null) opt = cm.GetFullCostumeWithId(full, false);
            return opt;
        }

        public static string GetGameCosmeticOptionId(CostumeOption option)
        {
            return "gamecosm:" + GetGameId(option);
        }

        public static string GetGameColourOptionId(ColourOption option)
        {
            return "gamecolour:" + GetGameId(option);
        }

        public static string GetGamePatternOptionId(SkinPatternOption option)
        {
            return "gamepattern:" + GetGameId(option);
        }

        public static string GetGameFaceplateOptionId(FaceplateOption option)
        {
            return "gamefaceplate:" + GetGameId((ItemDefinitionSO)option);
        }

        public string GetAppliedGameColourId() => activeColour.id ?? "";
        public string GetAppliedGamePatternId() => activePattern.id ?? "";
        public string GetAppliedGameFaceplateId() => activeFaceplate.id ?? "";

        public void ApplyGameColour(ColourOption option)
        {
            if (option == null) return;
            string id = GetGameColourOptionId(option);
            bool changed = !activeColour.On || activeColour.id != id;
            activeColour.Set(option, id, GetGameName(option, "colour"));
            SettingsService.Set("allcosmetics.colour", id);
            ApplyGameColourPatternToAllBeans();
            ReapplyAllEnabledFromSettings();
            if (changed) SpawnGameCosmeticPoof();
        }

        public void ApplyGamePattern(SkinPatternOption option)
        {
            if (option == null) return;
            string id = GetGamePatternOptionId(option);
            bool changed = !activePattern.On || activePattern.id != id;
            try { option.LoadBlocking(); } catch { }
            activePattern.Set(option, id, GetGameName(option, "pattern"));
            SettingsService.Set("allcosmetics.pattern", id);
            ApplyGameColourPatternToAllBeans();
            ReapplyAllEnabledFromSettings();
            if (changed) SpawnGameCosmeticPoof();
        }

        public void ApplyGameFaceplate(FaceplateOption option)
        {
            if (option == null) return;
            string id = GetGameFaceplateOptionId(option);
            bool changed = !activeFaceplate.On || activeFaceplate.id != id;
            activeFaceplate.Set(option, id, GetGameName((ItemDefinitionSO)option, "faceplate"));
            SettingsService.Set("allcosmetics.faceplate", id);
            ApplyGameColourPatternToAllBeans();
            if (changed) SpawnGameCosmeticPoof();
        }

        public void RemoveGameColour()
        {
            if (!activeColour.On) return;
            activeColour.Clear();
            SettingsService.Set("allcosmetics.colour", "");
            RestoreDefaultColourPatternFaceplate();
            SpawnGameCosmeticPoof();
            OnSkinRemoved?.Invoke("Game colour removed");
        }

        public void RemoveGamePattern()
        {
            if (!activePattern.On) return;
            activePattern.Clear();
            SettingsService.Set("allcosmetics.pattern", "");
            RestoreDefaultColourPatternFaceplate();
            SpawnGameCosmeticPoof();
            OnSkinRemoved?.Invoke("Game pattern removed");
        }

        public void RemoveGameFaceplate()
        {
            if (!activeFaceplate.On) return;
            activeFaceplate.Clear();
            SettingsService.Set("allcosmetics.faceplate", "");
            RestoreDefaultColourPatternFaceplate();
            SpawnGameCosmeticPoof();
            OnSkinRemoved?.Invoke("Game faceplate removed");
        }

        private void RestoreDefaultColourPatternFaceplate()
        {
            CustomisationSelections sel = null;
            try
            {
                var mm = GameObject.Find("MainMenuManager")?.GetComponent<MainMenuManager>();
                sel = mm?._playerProfile?.CustomisationSelections;
            }
            catch { }
            if (sel == null) return;

            var beans = BeanMonitorService.GetTrackedBeans();
            for (int i = 0; i < beans.Count; i++)
            {
                var bean = beans[i];
                if (bean == null || SkipMenuSkinAutoApplyForThisBean(bean)) continue;
                var fgch = bean.GetComponent<FallguyCustomisationHandler>();
                if (fgch == null) continue;

                if (!activeColour.On && sel.ColourOption != null)
                {
                    try { fgch.UpdateColourOption(sel.ColourOption); } catch { }
                }
                if (!activePattern.On && sel.PatternOption != null)
                {
                    try { sel.PatternOption.LoadBlocking(); } catch { }
                    try { fgch.UpdatePatternTexture(sel.PatternOption); } catch { }
                }
                if (!activeFaceplate.On && sel.FaceplateOption != null)
                {
                    try { fgch.UpdateFaceplateColours(sel.FaceplateOption); } catch { }
                }
            }
        }

        private static void SaveGameCosmeticIds(IEnumerable<string> ids)
        {
            var list = new List<string>();
            if (ids != null)
                foreach (var id in ids)
                    if (!string.IsNullOrEmpty(id) && !list.Contains(id)) list.Add(id);
            SettingsService.Set("allcosmetics.ids", string.Join("|", list));
        }

        public static List<GameCosmeticEntry> LoadSavedGameCosmetics()
        {
            var list = new List<GameCosmeticEntry>();
            int count = int.TryParse(SettingsService.Get("allcosmetics.saved.count", "0"), out int c) ? c : 0;
            for (int i = 0; i < count; i++)
            {
                string id = SettingsService.Get($"allcosmetics.saved.{i}.id", "");
                if (string.IsNullOrEmpty(id)) continue;
                list.Add(new GameCosmeticEntry
                {
                    id = id,
                    name = SettingsService.Get($"allcosmetics.saved.{i}.name", id),
                    kind = SettingsService.Get($"allcosmetics.saved.{i}.kind", "costume"),
                    enabled = SettingsService.Get($"allcosmetics.saved.{i}.enabled", "1") == "1"
                });
            }
            return list;
        }

        public static void SaveSavedGameCosmetics(List<GameCosmeticEntry> entries)
        {
            SettingsService.Set("allcosmetics.saved.count", entries.Count.ToString());
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                SettingsService.Set($"allcosmetics.saved.{i}.id", e.id);
                SettingsService.Set($"allcosmetics.saved.{i}.name", e.name ?? e.id);
                SettingsService.Set($"allcosmetics.saved.{i}.kind", e.kind ?? "costume");
                SettingsService.Set($"allcosmetics.saved.{i}.enabled", e.enabled ? "1" : "0");
            }
        }

        // Resources.FindObjectsOfTypeAll(<type>) is brutally expensive in il2cpp - it walks every
        // loaded object. Cache the id→option maps with a short TTL so back-to-back applies share
        // one scan instead of one-per-call.
        private static Dictionary<string, CostumeOption> _costumeByIdCache;
        private static Dictionary<string, ColourOption> _colourByIdCache;
        private static Dictionary<string, SkinPatternOption> _patternByIdCache;
        private static Dictionary<string, FaceplateOption> _faceplateByIdCache;
        private static float _costumeCacheStamp, _colourCacheStamp, _patternCacheStamp, _faceplateCacheStamp;
        private const float CACHE_TTL = 2f;

        public static void InvalidateGameCosmeticLookupCaches()
        {
            _costumeByIdCache = null; _colourByIdCache = null;
            _patternByIdCache = null; _faceplateByIdCache = null;
        }

        private static Dictionary<string, CostumeOption> GetCostumeLookup()
        {
            if (_costumeByIdCache != null && Time.realtimeSinceStartup - _costumeCacheStamp < CACHE_TTL) return _costumeByIdCache;
            var map = new Dictionary<string, CostumeOption>();
            var raw = Resources.FindObjectsOfTypeAll(Il2CppType.Of<CostumeOption>());
            for (int i = 0; raw != null && i < raw.Length; i++)
            {
                CostumeOption opt;
                try { opt = raw[i].Cast<CostumeOption>(); } catch { continue; }
                if (opt == null) continue;
                string id = GetGameCosmeticOptionId(opt);
                if (!map.ContainsKey(id)) map[id] = opt;
            }
            _costumeByIdCache = map; _costumeCacheStamp = Time.realtimeSinceStartup;
            return map;
        }

        private static Dictionary<string, ColourOption> GetColourLookup()
        {
            if (_colourByIdCache != null && Time.realtimeSinceStartup - _colourCacheStamp < CACHE_TTL) return _colourByIdCache;
            var map = new Dictionary<string, ColourOption>();
            var raw = Resources.FindObjectsOfTypeAll(Il2CppType.Of<ColourOption>());
            for (int i = 0; raw != null && i < raw.Length; i++)
            {
                ColourOption opt;
                try { opt = raw[i].Cast<ColourOption>(); } catch { continue; }
                if (opt == null) continue;
                string id = GetGameColourOptionId(opt);
                if (!map.ContainsKey(id)) map[id] = opt;
            }
            _colourByIdCache = map; _colourCacheStamp = Time.realtimeSinceStartup;
            return map;
        }

        public static bool IsTeamColourId(string colourId) =>
            !string.IsNullOrEmpty(colourId) && colourId.IndexOf("team_colours", StringComparison.OrdinalIgnoreCase) >= 0;

        public static ColourOption ResolveColourOption(string colourId, int teamId)
        {
            if (string.IsNullOrEmpty(colourId)) return null;
            var cm = CustomisationManager.Instance;

            if (IsTeamColourId(colourId))
            {
                if (cm == null) return null;
                int team = teamId;
                if (team < 0 && int.TryParse(StripIdPrefix(colourId), out int fromId)) team = fromId - 1;

                var teamOpt = team >= 0 ? cm.GetDefaultColourOptionWithId(team) : null;
                if (teamOpt == null) Plugin.Log.LogWarning($"team colour {colourId} (team {team}) didn't resolve, that bean keeps its own colour");
                return teamOpt;
            }

            if (GetColourLookup().TryGetValue(colourId, out var loaded) && loaded != null) return loaded;
            if (cm == null) return null;

            string bare = StripIdPrefix(colourId, out string full);
            var opt = cm.GetColourOptionWithId(bare, false) ?? cm.GetColourOptionWithId(full, false);
            if (opt == null) Plugin.Log.LogWarning($"colour {colourId} not in the catalog");
            return opt;
        }

        private static Dictionary<string, SkinPatternOption> GetPatternLookup()
        {
            if (_patternByIdCache != null && Time.realtimeSinceStartup - _patternCacheStamp < CACHE_TTL) return _patternByIdCache;
            var map = new Dictionary<string, SkinPatternOption>();
            var raw = Resources.FindObjectsOfTypeAll(Il2CppType.Of<SkinPatternOption>());
            for (int i = 0; raw != null && i < raw.Length; i++)
            {
                SkinPatternOption opt;
                try { opt = raw[i].Cast<SkinPatternOption>(); } catch { continue; }
                if (opt == null) continue;
                string id = GetGamePatternOptionId(opt);
                if (!map.ContainsKey(id)) map[id] = opt;
            }
            _patternByIdCache = map; _patternCacheStamp = Time.realtimeSinceStartup;
            return map;
        }

        private static Dictionary<string, FaceplateOption> GetFaceplateLookup()
        {
            if (_faceplateByIdCache != null && Time.realtimeSinceStartup - _faceplateCacheStamp < CACHE_TTL) return _faceplateByIdCache;
            var map = new Dictionary<string, FaceplateOption>();
            var raw = Resources.FindObjectsOfTypeAll(Il2CppType.Of<FaceplateOption>());
            for (int i = 0; raw != null && i < raw.Length; i++)
            {
                FaceplateOption opt;
                try { opt = raw[i].Cast<FaceplateOption>(); } catch { continue; }
                if (opt == null) continue;
                string id = GetGameFaceplateOptionId(opt);
                if (!map.ContainsKey(id)) map[id] = opt;
            }
            _faceplateByIdCache = map; _faceplateCacheStamp = Time.realtimeSinceStartup;
            return map;
        }

        public void RestoreSavedGameCosmetics()
        {
            string rawIds = SettingsService.Get("allcosmetics.ids", "");
            string colourId = SettingsService.Get("allcosmetics.colour", "");
            string patternId = SettingsService.Get("allcosmetics.pattern", "");
            string faceplateId = SettingsService.Get("allcosmetics.faceplate", "");
            if ((string.IsNullOrEmpty(rawIds) && string.IsNullOrEmpty(colourId) && string.IsNullOrEmpty(patternId) && string.IsNullOrEmpty(faceplateId)) || gameCosmeticsRestoring) return;
            StartCoroutine(RestoreSavedGameCosmeticsCoroutine(rawIds, colourId, patternId, faceplateId).WrapToIl2Cpp());
        }

        private IEnumerator RestoreSavedGameCosmeticsCoroutine(string rawIds, string colourId, string patternId, string faceplateId)
        {
            gameCosmeticsRestoring = true;
            var wanted = new HashSet<string>();
            foreach (var id in rawIds.Split('|'))
                if (!string.IsNullOrEmpty(id)) wanted.Add(id);
            if (wanted.Count == 0 && string.IsNullOrEmpty(colourId) && string.IsNullOrEmpty(patternId) && string.IsNullOrEmpty(faceplateId)) { gameCosmeticsRestoring = false; yield break; }

            // StartCoroutine runs to the first yield synchronously — without this the pattern
            // LoadBlocking lands inside the OnMainMenuEntered frame that's already stacked with
            // menu-enter work
            yield return null;

            // ask CustomisationManager, don't scrape memory. Resources.FindObjectsOfTypeAll only sees
            // CostumeOptions the game has already loaded, and on a cold boot most of them haven't been
            // - which is why a saved costume came back as mask-only until you opened the customiser and
            // forced the wardrobe to load. the manager resolves by id off the catalog regardless.
            var cm = CustomisationManager.Instance;
            if (cm == null) { Plugin.Log.LogWarning("no CustomisationManager at menu enter, cosmetics not restored"); gameCosmeticsRestoring = false; yield break; }

            var chosen = new List<CostumeOption>();
            SkinPatternOption foundPattern = null;
            FaceplateOption foundFaceplate = null;

            foreach (var id in wanted)
            {
                var opt = LookupCostume(cm, id);
                if (opt != null) chosen.Add(opt);
                else Plugin.Log.LogWarning($"costume {id} not in the catalog, skipping it");
            }
            var foundColour = ResolveColourOption(colourId, -1);
            if (!string.IsNullOrEmpty(patternId))
            {
                string bare = StripIdPrefix(patternId, out string full);
                foundPattern = cm.GetSkinPatternOptionWithId(bare, false) ?? cm.GetSkinPatternOptionWithId(full, false);
                if (foundPattern == null) Plugin.Log.LogWarning($"pattern {patternId} not in the catalog");
            }
            if (!string.IsNullOrEmpty(faceplateId))
            {
                string bare = StripIdPrefix(faceplateId, out string full);
                foundFaceplate = cm.GetFaceplateOptionWithId(bare, false) ?? cm.GetFaceplateOptionWithId(full, false);
                if (foundFaceplate == null) Plugin.Log.LogWarning($"faceplate {faceplateId} not in the catalog");
            }

            if (chosen.Count > 0) ApplyGameCosmeticSelection(chosen, wanted);
            if (foundColour != null) activeColour.Set(foundColour, colourId, GetGameName(foundColour, "colour"));
            if (foundPattern != null)
            {
                try { foundPattern.LoadBlocking(); } catch { }
                activePattern.Set(foundPattern, patternId, GetGameName(foundPattern, "pattern"));
            }
            if (foundFaceplate != null) activeFaceplate.Set(foundFaceplate, faceplateId, GetGameName((ItemDefinitionSO)foundFaceplate, "faceplate"));
            ApplyGameColourPatternToAllBeans();

            gameCosmeticsRestoring = false;
        }

        // apply a PROFILE's game cosmetics (costumes + colour + pattern + faceplate) to ONE specific
        // bean, resolving the ids ourselves. does NOT touch the local player's global activeGame*
        // state - this is for other players' beans via the profiles feature.
        // onDone runs AFTER the costumes + colour land, so callers can apply skin textures then
        // (otherwise the costume meshes don't exist yet and texture matching finds nothing).
        public void ApplyProfileCosmeticsToBean(string rawIds, string colourId, string patternId, string faceplateId, GameObject bean, Action onDone = null, int teamId = -1)
        {
            if (bean == null) return;
            StartCoroutine(ApplyProfileCosmeticsToBeanCoroutine(rawIds, colourId, patternId, faceplateId, bean, onDone, teamId).WrapToIl2Cpp());
        }

        private IEnumerator ApplyProfileCosmeticsToBeanCoroutine(string rawIds, string colourId, string patternId, string faceplateId, GameObject bean, Action onDone, int teamId)
        {
            yield return new WaitForSeconds(1f);
            if (bean == null) yield break;

            var wanted = new HashSet<string>();
            foreach (var id in (rawIds ?? "").Split('|'))
                if (!string.IsNullOrEmpty(id)) wanted.Add(id);

            var chosen = new List<CostumeOption>();
            SkinPatternOption foundPattern = null;
            FaceplateOption foundFaceplate = null;

            if (wanted.Count > 0)
            {
                var costumes = GetCostumeLookup();
                foreach (var id in wanted)
                    if (costumes.TryGetValue(id, out var opt) && opt != null) chosen.Add(opt);
            }
            var foundColour = ResolveColourOption(colourId, teamId);
            if (!string.IsNullOrEmpty(patternId)) GetPatternLookup().TryGetValue(patternId, out foundPattern);
            if (!string.IsNullOrEmpty(faceplateId)) GetFaceplateLookup().TryGetValue(faceplateId, out foundFaceplate);

            // remember this profile's costume options for the bean, so its cosmetic masks composite
            // against ITS top/bottom costume rather than the local player's
            profileBeanCostumes[bean.GetInstanceID()] = new List<CostumeOption>(chosen);

            // colour/pattern/faceplate FIRST, so each costume mesh composites with them as it binds
            // (the local bean works because activeColour is set before costumes apply), then
            // again after for the base body.
            var fgch = bean.GetComponent<FallguyCustomisationHandler>();
            ApplyProfileColourPattern(fgch, foundColour, foundPattern, foundFaceplate);

            foreach (var opt in chosen)
            {
                string id = GetGameCosmeticOptionId(opt);
                string key = MakeKey(bean, id);
                if (appliedSkins.ContainsKey(key) || pendingKeys.Contains(key)) continue;
                var slot = new GameCosmeticSlot { option = opt, id = id, name = GetGameName(opt, "cosmetic"), remotePipeline = true };
                pendingKeys.Add(key);
                Plugin.Log.LogInfo($"Profiles: cosmetic '{slot.name}' -> {bean.name}");
                yield return ApplyGameCosmeticToBeanCoroutine(slot, bean, ApplyReason.AutoReapply).WrapToIl2Cpp();
            }

            ApplyProfileColourPattern(fgch, foundColour, foundPattern, foundFaceplate);

            try { onDone?.Invoke(); } catch (Exception ex) { Plugin.Log.LogWarning("Profiles: onDone: " + ex.Message); }
        }

        private void ApplyProfileColourPattern(FallguyCustomisationHandler fgch, ColourOption colour, SkinPatternOption pattern, FaceplateOption faceplate)
        {
            if (fgch == null) return;
            if (colour != null)
            {
                try { fgch.UpdateColourOption(colour); } catch (Exception ex) { Plugin.Log.LogWarning("Profiles: colour: " + ex.Message); }
                var team = colour.TryCast<TeamColourOption>();
                if (team != null) { try { fgch.SetCostumeTeamColours(team.costumeTintColour); } catch (Exception ex) { Plugin.Log.LogWarning("Profiles: team tint: " + ex.Message); } }
            }
            if (pattern != null) { try { pattern.LoadBlocking(); } catch { } try { fgch.UpdatePatternTexture(pattern); } catch (Exception ex) { Plugin.Log.LogWarning("Profiles: pattern: " + ex.Message); } }
            if (faceplate != null) { try { fgch.UpdateFaceplateColours(faceplate); } catch (Exception ex) { Plugin.Log.LogWarning("Profiles: faceplate: " + ex.Message); } }
        }

        public void ReapplyExpectedGameCosmeticMasks(GameObject bean = null)
        {
            if (activeGameCosmetics.Count == 0) return;

            if (bean != null)
            {
                StartCoroutine(ReapplyGameCosmeticMasksCoroutine(bean).WrapToIl2Cpp());
                return;
            }

            var beans = BeanMonitorService.GetTrackedBeans();
            for (int i = 0; i < beans.Count; i++)
            {
                var b = beans[i];
                if (b == null || SkipMenuSkinAutoApplyForThisBean(b)) continue;
                StartCoroutine(ReapplyGameCosmeticMasksCoroutine(b).WrapToIl2Cpp());
            }
        }

        public void ReapplyExpectedGameCosmeticVisuals(GameObject bean = null)
        {
            ReapplyExpectedGameCosmeticMasks(bean);
            // custom skin textures reapply regardless — the colour/pattern gate used to sit above
            // this and silently skipped textures for anyone running a texture with no colour/pattern
            // (view switches dropped the texture and it never came back)
            if (activeColour.On || activePattern.On)
            {
                if (bean != null)
                {
                    if (!SkipMenuSkinAutoApplyForThisBean(bean)) ApplyGameColourPatternToBean(bean);
                }
                else
                {
                    var beans = BeanMonitorService.GetTrackedBeans();
                    for (int i = 0; i < beans.Count; i++)
                        if (beans[i] != null && !SkipMenuSkinAutoApplyForThisBean(beans[i]))
                            ApplyGameColourPatternToBean(beans[i]);
                }
            }
            ReapplyAllEnabledFromSettings();
        }

        public bool IsBindingGameCosmetic(GameObject bean)
        {
            return bean != null && gameCosmeticBindingBeans.Contains(bean.GetInstanceID());
        }

        // removes all applied skins for a specific bean and restores its original mesh visibility
        public void RemoveAllSkins(GameObject bean)
        {
            if (bean == null) return;
            applyStamp++;
            RevertCustomTexture(bean);
            string prefix = bean.GetInstanceID() + "|";
            Plugin.Log.LogInfo($"RemoveAllSkins for {bean.name}");

            var toKill = new List<string>();
            foreach (var kvp in appliedSkins)
            {
                if (!kvp.Key.StartsWith(prefix)) continue;
                var entry = kvp.Value;
                if (entry?.instance != null)
                {
                    var poller = entry.instance.GetComponent<CostumePollerComponent>();
                    if (poller != null) { poller.enabled = false; poller.beanGEO = null; }
                    DestroyAppliedInstance(entry.instance, entry.addressableInstance);
                }
                if (entry?.beanGEO != null)
                {
                    RestoreBaseBeanVisibility(kvp.Key, entry);
                }
                toKill.Add(kvp.Key);
            }
            foreach (var k in toKill) { appliedSkins.Remove(k); pendingKeys.Remove(k); }

            // remove active slots for this bean's files and unload bundles no longer used
            activeSlots.RemoveAll(s => toKill.Exists(k => SkinFileFromAppliedKey(k) == s.skinInfo.file));
            PruneLoadedBundlesNotUsedByAppliedSkins();
            redownloadingFiles.Clear();
            PlayerScaleService.ApplyToBean(bean,
                PlayerScaleService.HasUserSetScale() ? PlayerScaleService.GetPlayerScale() : 1f,
                IsRemoteInRoundBean(bean) ? PlayerScaleService.BeanScaleMode.Remote : PlayerScaleService.BeanScaleMode.Local);
            OnSkinRemoved?.Invoke($"Skins removed for {bean.name}");
        }



        // collect all renderers recursively (il2cpp safe), no LOD filtering


        private void PruneLoadedBundlesNotUsedByAppliedSkins()
        {
            var stillUsed = new HashSet<string>();
            foreach (var k in appliedSkins.Keys)
            {
                string f = SkinFileFromAppliedKey(k);
                if (!string.IsNullOrEmpty(f)) stillUsed.Add(f);
            }
            // also keep remote-owned bundles alive
            foreach (var f in remoteBundleFiles)
                stillUsed.Add(f);

            var toUnload = new List<string>();
            foreach (var kvp in loadedBundles)
                if (!stillUsed.Contains(kvp.Key)) toUnload.Add(kvp.Key);
            foreach (var f in toUnload)
            {
                if (loadedBundles.TryGetValue(f, out var b) && b != null) b.Unload(false);
                loadedBundles.Remove(f);
            }
        }

        private static string SkinFileFromAppliedKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            int pipe = key.IndexOf('|');
            if (pipe < 0 || pipe >= key.Length - 1) return null;
            string rest = key.Substring(pipe + 1);
            if (rest.EndsWith("_L", StringComparison.Ordinal)) return rest.Substring(0, rest.Length - 2);
            if (rest.EndsWith("_R", StringComparison.Ordinal)) return rest.Substring(0, rest.Length - 2);
            return rest;
        }

        // ── Internal ──────────────────────────────────────────────────────────

        private IEnumerator ApplySlotToAllBeansCoroutine(ActiveSkinSlot slot, ApplyReason reason = ApplyReason.FromMenu)
        {
            var beans = BeanMonitorService.GetTrackedBeans();
            foreach (var bean in beans)
            {
                if (SlotDead(slot)) yield break;
                if (bean == null) continue;
                if (SkipMenuSkinAutoApplyForThisBean(bean)) continue;
                string key = MakeKey(bean, slot.skinInfo.file);
                if (appliedSkins.ContainsKey(key) || pendingKeys.Contains(key)) continue;
                pendingKeys.Add(key);
                yield return ApplySlotToBeanCoroutine(slot, bean, reason).WrapToIl2Cpp();
            }
        }

        private IEnumerator ApplySlotToBeanCoroutine(ActiveSkinSlot slot, GameObject bean, ApplyReason reason = ApplyReason.FromMenu)
        {
            if (SlotDead(slot)) yield break;
            switch (slot.type)
            {
                case SkinType.Costume: yield return ApplyCostumeCoroutine(slot, bean, reason).WrapToIl2Cpp(); break;
                case SkinType.Accessory: yield return ApplyAccessoryCoroutine(slot, bean, reason).WrapToIl2Cpp(); break;
                case SkinType.Item: yield return ApplyItemCoroutine(slot, bean, reason).WrapToIl2Cpp(); break;
            }
        }

        private void RemoveSlotByFile(string file)
        {
            activeSlots.RemoveAll((ActiveSkinSlot s) => s.skinInfo.file == file);

            // only destroy applied instances on the LOCAL bean — remote beans own their own applied entries
            var localBean = BeanMonitorService.LocalPlayerBean;
            string localPrefix = localBean != null ? (localBean.GetInstanceID() + "|") : null;

            var toKill = new List<string>();
            foreach (var kvp in appliedSkins)
            {
                if (SkinFileFromAppliedKey(kvp.Key) != file) continue;
                var bean = kvp.Value?.bean;
                if (localBean != null && bean != null && bean != localBean && IsRemoteInRoundBean(bean)) continue;
                if (localBean != null && bean == null && !kvp.Key.StartsWith(localPrefix)) continue;
                if (kvp.Value?.instance != null)
                {
                    var poller = kvp.Value.instance.GetComponent<CostumePollerComponent>();
                    if (poller != null) poller.enabled = false;
                    DestroyAppliedInstance(kvp.Value.instance, kvp.Value.addressableInstance);
                }
                if (kvp.Value?.beanGEO != null)
                {
                    RestoreBaseBeanVisibility(kvp.Key, kvp.Value);
                }
                toKill.Add(kvp.Key);
            }
            foreach (var k in toKill) { appliedSkins.Remove(k); pendingKeys.Remove(k); }
        }

        private bool SlotDead(ActiveSkinSlot slot)
        {
            if (slot == null || slot.skinInfo == null) return true;
            // remote-pipeline slots (debug_profiles / .bfgprofile) are one-shot applies to other
            // beans — they're never in activeSlots and nothing stamps them, so the menu-slot
            // liveness rules below would kill them instantly (and silently)
            if (slot.remotePipeline) return false;
            return slot.applyStamp != applyStamp || !activeSlots.Contains(slot);
        }

        private bool SlotDead(GameCosmeticSlot slot)
        {
            if (slot == null || slot.option == null) return true;
            if (slot.remotePipeline) return false; // one-shot remote-bean apply, not in activeGameCosmetics
            return slot.applyStamp != applyStamp || !activeGameCosmetics.Contains(slot);
        }

        private void RestoreBaseBeanVisibility(string key, AppliedSkinInfo entry)
        {
            if (entry == null) return;

            if (entry.disabledChildren != null)
                foreach (var go in entry.disabledChildren)
                    if (go != null) go.SetActive(true);

            if (entry.beanGEO == null) return;
            for (int i = 0; i < entry.beanGEO.childCount; i++)
            {
                Transform child = entry.beanGEO.GetChild(i).Cast<Transform>();
                if (child == null) continue;
                if (entry.instance != null && child.gameObject == entry.instance) continue;

                string matKey = key + "_" + i;
                if (hiddenRendererMaterials.TryGetValue(matKey, out var matMap))
                {
                    foreach (var kv in matMap)
                    {
                        if (kv.Key == null) continue;
                        kv.Key.materials = kv.Value;
                        kv.Key.enabled = true;
                    }
                    hiddenRendererMaterials.Remove(matKey);
                }

                if (entry.restoreAllBaseGEO)
                    child.gameObject.SetActive(true);
            }
        }

        private IEnumerator RestoreMenuBeanGEOSoon()
        {
            // if cosmetics are parked under a just-removed costume, flip them straight back on — the
            // re-instantiate loop below only needs to handle ones that were never stashed.
            RestoreStashedGameCosmetics();
            for (int i = 0; i < 4; i++)
            {
                RestoreMenuBeanGEO();
                yield return null;
            }

            bool anyGameLook = activeGameCosmetics.Count > 0 || activeColour.On || activePattern.On || activeFaceplate.On;
            if (!anyGameLook) yield break;

            // re-composite cosmetics on EVERY non-remote bean, not just the menu plinth bean.
            // the falling-screen LobbyCharacter is a separate bean — a keepBase-off costume hid its
            // cosmetic meshes, and if we only restored _menuFallGuy the lobby bean stayed bare
            // (all game cosmetics "gone" on the falling screen). _menuFallGuy is a tracked bean too.
            var beans = BeanMonitorService.GetTrackedBeans();
            foreach (var bean in beans)
            {
                if (bean == null || SkipMenuSkinAutoApplyForThisBean(bean)) continue;
                // colour/pattern FIRST so the base body is the right colour before each cosmetic
                // rebinds — SetCostumeBaseCopyColor (in BindGameCosmeticToFallguy) copies the base
                // colour onto copy-colour costumes, so binding before this copies the wrong colour.
                ApplyGameColourPatternToBean(bean);
                foreach (var slot in new List<GameCosmeticSlot>(activeGameCosmetics))
                {
                    if (slot == null) continue;
                    string key = MakeKey(bean, slot.id);
                    // already live (un-stashed by RestoreStashedGameCosmetics, instance intact)? leave
                    // it — destroying + re-instantiating it is exactly the 0.5s pop-in we're avoiding.
                    if (appliedSkins.TryGetValue(key, out var live) && live != null && !live.stashed && live.instance != null)
                        continue;
                    // Destroy the previous clone + reparented mesh GOs first — otherwise the
                    // reapply leaves the old CH_*(Clone) meshes orphaned under GEO, and they
                    // never get cleaned up by RemoveGameCosmeticById later.
                    if (appliedSkins.TryGetValue(key, out var prev) && prev != null)
                    {
                        DestroyAppliedInstance(prev.instance, prev.addressableInstance);
                        if (prev.boundRenderers != null)
                            foreach (var go in prev.boundRenderers)
                                if (go != null) GameObject.Destroy(go);
                    }
                    appliedSkins.Remove(key);
                    pendingKeys.Remove(key);
                    // RemoveMenuEquippedSlotsOnly incremented applyStamp, so the slot is now
                    // "dead" and ApplyGameCosmeticToBeanCoroutine would silently early-exit.
                    // Refresh so the reapply actually runs.
                    slot.applyStamp = applyStamp;
                }
                yield return ApplyActiveGameCosmeticsToBeanCoroutine(bean).WrapToIl2Cpp();
            }
        }

        private void RestoreMenuBeanGEO()
        {
            // reset base GEO visibility on every non-remote bean (menu plinth AND the falling-screen
            // LobbyCharacter), so removing a keepBase-off costume doesn't leave any of them bare.
            var beans = BeanMonitorService.GetTrackedBeans();
            foreach (var bean in beans)
            {
                if (bean == null || SkipMenuSkinAutoApplyForThisBean(bean)) continue;
                var geo = FindBeanGEO(bean);
                if (geo == null) continue;

                for (int i = 0; i < geo.childCount; i++)
                {
                    var child = geo.GetChild(i).Cast<Transform>();
                    if (child == null || child.gameObject == null) continue;

                    bool bodyLod = child.name.StartsWith("Body_LOD", StringComparison.OrdinalIgnoreCase);
                    bool keepOff = bodyLod && !child.name.EndsWith("0", StringComparison.OrdinalIgnoreCase);
                    child.gameObject.SetActive(!keepOff);
                }
            }
        }

        private void PruneDestroyedEntries()
        {
            var toRemove = new List<string>();
            foreach (var kvp in appliedSkins)
                if (kvp.Value == null || kvp.Value.instance == null) toRemove.Add(kvp.Key);
            foreach (var k in toRemove) appliedSkins.Remove(k);
        }

        // before we overwrite appliedSkins[key] with a fresh clone, blow away whatever was already
        // sitting there. otherwise the old clone stays parented under GEO with its CostumePoller still
        // alive, and every leftover poller keeps running its 1s HideBeans sweep -> the exact-1s freeze
        // that gets worse every equip/unequip. reapplies (RestoreMenuBeanGEOSoon, auto-reapply) hit the
        // same key repeatedly, so without this they pile up pollers forever.
        private void KillExistingAppliedAtKey(string key)
        {
            if (!appliedSkins.TryGetValue(key, out var prev) || prev == null) return;
            DestroyAppliedInstance(prev.instance, prev.addressableInstance);
            if (prev.boundRenderers != null)
                foreach (var go in prev.boundRenderers)
                    if (go != null) GameObject.Destroy(go);
        }

        // game/UGC cosmetic instances come from Addressables InstantiateAsync — releasing through
        // Addressables decrements the handle ref count AND destroys the GameObject. plain
        // GameObject.Destroy skips the release so the loaded prefab leaks (the equip/unequip leak).
        // ReleaseInstance returns false for anything it isn't tracking, so fall back to Destroy.
        private static void DestroyAppliedInstance(GameObject go, bool addressable)
        {
            if (go == null) return;
            if (addressable)
            {
                try { if (UnityEngine.AddressableAssets.Addressables.ReleaseInstance(go)) return; }
                catch { }
            }
            GameObject.Destroy(go);
        }

        private static string MakeKey(GameObject bean, string skinFile) =>
            bean.GetInstanceID() + "|" + skinFile;

        // strip already-applied game-cosmetic meshes (costumes + their bound renderers) off the local
        // menu beans WITHOUT clearing activeGameCosmetics. used when a keepBase-off UGC costume becomes
        // active: the game cosmetics stay in the saved loadout (so they reappear when the costume comes
        // off) but their meshes are gone so the poller has nothing to keep fighting. colour/pattern/
        // faceplate are bean-state, not instances, and get re-stamped by the costume removal path.
        private void StripAppliedGameCosmeticInstancesLocal()
        {
            if (activeGameCosmetics.Count == 0) return;
            var ids = new HashSet<string>();
            foreach (var slot in activeGameCosmetics)
                if (slot != null && !string.IsNullOrEmpty(slot.id)) ids.Add(slot.id);

            // park the cosmetic meshes (SetActive false) instead of destroying them. the UGC costume
            // hides them anyway, and keeping the instances alive means RestoreStashedGameCosmetics can
            // flip them back on the instant the costume comes off — no re-instantiate, no 0.5s pop-in.
            // the poller is disabled so it doesn't fight to re-hide cosmetics it can't see.
            var localBean = BeanMonitorService.LocalPlayerBean;
            foreach (var kvp in appliedSkins)
            {
                string file = SkinFileFromAppliedKey(kvp.Key);
                if (file == null || !ids.Contains(file)) continue;
                var bean = kvp.Value?.bean;
                if (localBean != null && bean != null && bean != localBean && IsRemoteInRoundBean(bean)) continue;
                if (kvp.Value == null) continue;
                if (kvp.Value.instance != null)
                {
                    var poller = kvp.Value.instance.GetComponent<CostumePollerComponent>();
                    if (poller != null) poller.enabled = false;
                    kvp.Value.instance.SetActive(false);
                }
                if (kvp.Value.boundRenderers != null)
                    foreach (var go in kvp.Value.boundRenderers)
                        if (go != null) go.SetActive(false);
                kvp.Value.stashed = true;
            }
        }

        // re-show the game-cosmetic meshes StripAppliedGameCosmeticInstancesLocal parked when a UGC
        // costume went on. instant flip-back, no re-instantiate. returns true if any entry was stashed
        // (so the caller knows it doesn't need the slow re-composite). re-enables the poller so it
        // keeps the cosmetics composited again now that the base body is back.
        private bool RestoreStashedGameCosmetics()
        {
            var localBean = BeanMonitorService.LocalPlayerBean;
            bool any = false;
            foreach (var kvp in appliedSkins)
            {
                var entry = kvp.Value;
                if (entry == null || !entry.stashed) continue;
                var bean = entry.bean;
                if (localBean != null && bean != null && bean != localBean && IsRemoteInRoundBean(bean)) continue;
                if (entry.instance != null)
                {
                    entry.instance.SetActive(true);
                    var poller = entry.instance.GetComponent<CostumePollerComponent>();
                    if (poller != null) poller.enabled = true;
                }
                if (entry.boundRenderers != null)
                    foreach (var go in entry.boundRenderers)
                        if (go != null) go.SetActive(true);
                entry.stashed = false;
                any = true;
            }
            if (any)
            {
                var beans = BeanMonitorService.GetTrackedBeans();
                foreach (var bean in beans)
                    if (bean != null && !SkipMenuSkinAutoApplyForThisBean(bean))
                        ApplyGameColourPatternToBean(bean);
            }
            return any;
        }


        private static bool SkipMenuSkinAutoApplyForThisBean(GameObject bean)
        {
            if (bean == null) return true;
            return IsRemoteInRoundBean(bean);
        }

        private static bool IsRemoteInRoundBean(GameObject bean)
        {
            if (bean == null) return false;
            if (bean == BeanMonitorService.LocalPlayerBean) return false;
            if (BeanMonitorService.IsForeignBean(bean)) return true;
            if (GameObjectHelper.IsLobbyCharacter(bean)) return false;
            if (GameObjectHelper.IsUICharacter(bean)) return false;
            if (bean.name == "LevelEditor_FallGuy(Clone)") return false;
            return !string.IsNullOrEmpty(BeanNetworkUtil.TryGetPlayerKeyForBean(bean));
        }

        private static bool IsLocalInRoundBean(GameObject bean)
        {
            if (bean == null) return false;
            if (GameObjectHelper.IsLobbyCharacter(bean)) return false;
            if (GameObjectHelper.IsUICharacter(bean)) return false;
            if (bean.name == "LevelEditor_FallGuy(Clone)") return false;
            if (bean != BeanMonitorService.LocalPlayerBean) return false;
            return bean.transform.parent == null && bean.name.StartsWith("FallGuy");
        }

        private void TriggerReload(SkinInfo skinInfo)
        {
            if (redownloadingFiles.Contains(skinInfo.file)) return;

            // bundle still alive in registry — patch slot ref back, no download needed
            if (loadedBundles.TryGetValue(skinInfo.file, out var existing) && existing != null)
            {
                foreach (var s in activeSlots)
                    if (s.skinInfo.file == skinInfo.file) s.bundle = existing;
                return;
            }

            redownloadingFiles.Add(skinInfo.file);

            GameObject uiObj = GameObject.Find("BetterFG_UI");
            if (uiObj == null) { redownloadingFiles.Remove(skinInfo.file); return; }
            var loader = uiObj.GetComponent<SkinLoaderService>();
            if (loader == null) { redownloadingFiles.Remove(skinInfo.file); return; }

            if (skinInfo.isLocalImport && !string.IsNullOrEmpty(skinInfo.localPath))
                loader.ImportSkinFromFolder(System.IO.Path.GetDirectoryName(skinInfo.localPath));
            else
            {
                string category = SkinTypeParser.CategoryFolder(skinInfo.type);
                string folder = !string.IsNullOrEmpty(skinInfo.repoFolder) ? skinInfo.repoFolder : $"{category}/{skinInfo.file}";
                loader.DownloadSkinWithInfo(skinInfo.file, $"{GetRepoRaw(skinInfo)}/{folder}/{skinInfo.file}", $"{GetRepoRaw(skinInfo)}/{folder}/info.json");
            }
        }

        public AssetBundle GetOrRegisterBundle(string file, AssetBundle incoming)
        {
            if (string.IsNullOrEmpty(file)) return incoming;

            if (loadedBundles.TryGetValue(file, out var existing) && existing != null)
                return existing;

            if (incoming != null)
                loadedBundles[file] = incoming;

            return incoming;
        }

        public AssetBundle GetOrLoadBundle(string file, byte[] bytes)
        {
            if (loadedBundles.TryGetValue(file, out var existing) && existing != null)
                return existing;

            var bundle = Bundles.LoadMemorySync(file, bytes);
            loadedBundles[file] = bundle;
            return bundle;
        }


        // ── Bean finding ──────────────────────────────────────────────────────

        public static void SetRenderQueue(GameObject root, int queue)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var mats = r.materials;
                foreach (var m in mats)
                    if (m != null) m.renderQueue = queue;
                r.materials = mats;
            }
        }

        public static IEnumerator ForceRenderQueue(GameObject root, int queue, int frames = 5)
        {
            for (int f = 0; f < frames; f++)
            {
                if (root == null) yield break;

                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;

                    var mats = r.materials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == null) continue;
                        mats[i].renderQueue = queue;
                    }
                    r.materials = mats;
                }

                yield return null; // wait next frame
            }
        }

        private static Transform FindBeanGEO(GameObject bean)
        {
            var f = bean.transform.Find("Character/GEO");
            if (f != null) return f.Cast<Transform>();
            f = bean.transform.Find("GEO");
            if (f != null) return f.Cast<Transform>();
            f = bean.transform.Find("BetterFG_ScaleWrapper/Character/GEO");
            if (f != null) return f.Cast<Transform>();
            f = bean.transform.Find("BetterFG_ScaleWrapper/GEO");
            if (f != null) return f.Cast<Transform>();
            return null;
        }

    }

    public class ActiveSkinSlot
    {
        public SkinInfo skinInfo;
        public AssetBundle bundle;
        public SkinType type;
        public int applyStamp;
        public bool remotePipeline; // came in via debug_profiles/.bfgprofile, exempt from SlotDead
    }

    internal class GameCosmeticSlot
    {
        public CostumeOption option;
        public string id;
        public string name;
        public int applyStamp;
        public bool remotePipeline; // one-shot apply to a remote bean (profile), exempt from SlotDead
    }

    public class GameCosmeticEntry
    {
        public string id;
        public string name;
        public UnityEngine.Object option;
        public string kind;
        public bool enabled = true;
    }
}
