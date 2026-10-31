using System;
using System.Collections.Generic;
using System.IO;
using BettrFG.uGUI;
using UnityEditor;
using UnityEngine;

namespace BetterFG.Editor
{
    public partial class CreatorHubWindow
    {
        private const string TEMP_ASSET_DIR = "Assets/_BettrFGPack";

        private bool _packed;

        private void BuildBuildStep(RectTransform p, float w, float h)
        {
            float areaH = h - 36f - 40f;
            float coverH = areaH;
            float coverW = coverH * COVER_W / COVER_H;
            float maxW = w * 0.42f;
            if (coverW > maxW) { coverW = maxW; coverH = coverW * COVER_H / COVER_W; }
            Framed(p, new Rect(0f, 0f, coverW, coverH), _coverPreview, "creator.no_cover_image");

            float ix = coverW + 20f;
            float iw = Mathf.Max(w - ix, 60f);
            float y = 0f;

            var title = Lbl(p, new Rect(ix, y, iw, 28f), string.IsNullOrWhiteSpace(_name) ? "creator.untitled" : _name, FS_BIG + 2, WHITE);
            title.fontStyle = FontStyle.Bold;
            y += 30f;
            Lbl(p, new Rect(ix, y, iw, 20f), ShipLoc.Format("creator.by_fmt", string.IsNullOrWhiteSpace(_author) ? ShipLoc.Get("creator.unknown") : _author));
            y += 26f;

            if (!string.IsNullOrWhiteSpace(_description))
            {
                var d = Lbl(p, new Rect(ix, y, iw, 20f), _description, FS_SM, HINT);
                d.alignment = TextAnchor.UpperLeft;
                float dh = Mathf.Min(UGUIShip.PreferredHeight(d, iw) + 4f, Mathf.Max(coverH - y - 26f, 20f));
                d.rectTransform.sizeDelta = new Vector2(iw, dh);
                y += dh + 8f;
            }

            string group = _kind == PackKind.Round ? ActiveSceneName
                : string.IsNullOrWhiteSpace(_group) ? ShipLoc.Get("creator.group_unsorted") : _group;
            Lbl(p, new Rect(ix, y, iw, 20f), $"{ShipLoc.Get(KIND_LABELS[(int)_kind])}   ·   {group}   ·   {BundleName}", FS_SM);

            string dest = ComputeDestDir();
            Lbl(p, new Rect(0f, h - 76f, w, 22f), dest, FS_SM, HINT);

            if (_packed && Directory.Exists(_outputDir))
            {
                float bw = (w - 10f) * 0.5f;
                Btn(p, new Rect(0f, h - 36f, bw, 36f), "ui.open_folder", BTN_DARK, () => EditorUtility.RevealInFinder(_outputDir));
                Btn(p, new Rect(bw + 10f, h - 36f, bw, 36f), "creator.pack_another", BTN_BLUE, () =>
                {
                    _packed = false;
                    ResetForNew();
                    GoTo(Step.Browse);
                });
                return;
            }

            var pack = Btn(p, new Rect(0f, h - 36f, w, 36f), "creator.build_and_pack", BTN_GREEN, () => EditorApplication.delayCall += TryBuildAndPack, FS + 1);
            pack.interactable = (_kind == PackKind.Round ? CurrentMap != null : _sourceObject != null || _keepBundle)
                && !string.IsNullOrWhiteSpace(_name) && !string.IsNullOrEmpty(dest);
        }

        private static string SanitizeBundleName(string raw)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in raw.Trim().ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.ToString();
        }

        private static string KindFolder(PackKind k)
        {
            switch (k)
            {
                case PackKind.Accessory: return "Accessories";
                case PackKind.Item: return "Items";
                case PackKind.Plinth: return "Plinths";
                case PackKind.Round: return "Rounds";
                default: return "Costumes";
            }
        }

        private static string KindStr(PackKind k)
        {
            switch (k)
            {
                case PackKind.Accessory: return "accessory";
                case PackKind.Item: return "item";
                case PackKind.Plinth: return "plinth";
                case PackKind.Round: return "round";
                default: return "costume";
            }
        }

        private string ComputeDestDir()
        {
            if (string.IsNullOrWhiteSpace(_repoRoot)) return "";
            if (_kind == PackKind.Round)
                return string.IsNullOrEmpty(ActiveSceneName) ? "" : Path.Combine(_repoRoot, "Rounds", ActiveSceneName);
            if (string.IsNullOrEmpty(BundleName)) return "";
            return Path.Combine(_repoRoot, KindFolder(_kind), BundleName);
        }

        private void TryBuildAndPack()
        {
            _statusMsg = "";
            _packed = false;

            if (_kind == PackKind.Round) { TryPackRound(); return; }

            string dest = ComputeDestDir();

            if (_keepBundle)
            {
                try
                {
                    string src = Path.Combine(_loadedDir, _loadedBundleFile);
                    if (!File.Exists(src)) { Err(ShipLoc.Format("creator.err_bundle_missing_fmt", src)); return; }

                    Directory.CreateDirectory(dest);
                    _outputDir = dest;

                    string dst = Path.Combine(dest, _loadedBundleFile);
                    if (!string.Equals(src, dst, StringComparison.OrdinalIgnoreCase))
                        File.Copy(src, dst, overwrite: true);

                    WriteInfoJson();
                    CoverImage.Write(_coverPath, _outputDir);

                    if (!string.IsNullOrEmpty(_loadedDir) && !string.Equals(_loadedDir, dest, StringComparison.OrdinalIgnoreCase) && Directory.Exists(_loadedDir))
                    {
                        Directory.Delete(_loadedDir, true);
                        Debug.Log($"repacked under a new name, removed the old skin folder {_loadedDir}");
                    }
                    _loadedDir = dest;

                    CatalogBat.Run(_repoRoot);
                    WriteNewCatalog(_repoRoot);

                    _packed = true;
                    RefreshGroups();
                    Ok(ShipLoc.Format("creator.repacked_fmt", _name));
                    Debug.Log($"repacked {_name} without rebuilding, kept {_loadedBundleFile}");
                }
                catch (Exception ex)
                {
                    Err(ShipLoc.Format("creator.err_pack_fmt", ex.Message));
                    Debug.LogException(ex);
                }
                return;
            }
            string bundleTempDir = Path.Combine(Path.GetTempPath(), "BettrFGPack_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string prefabPath = TEMP_ASSET_DIR + "/" + _sourceObject.name + ".prefab";
            GameObject clone = null;

            try
            {
                EditorUtility.DisplayProgressBar(ShipLoc.Get("creator.progress_skin"), ShipLoc.Get("creator.progress_prefab"), 0.15f);

                if (!AssetDatabase.IsValidFolder(TEMP_ASSET_DIR))
                    AssetDatabase.CreateFolder("Assets", "_BettrFGPack");

                clone = Instantiate(_sourceObject);
                clone.name = _sourceObject.name;

                var saved = PrefabUtility.SaveAsPrefabAsset(clone, prefabPath);
                DestroyImmediate(clone);
                clone = null;

                if (saved == null) { Err(ShipLoc.Get("creator.err_temp_prefab")); return; }

                EditorUtility.DisplayProgressBar(ShipLoc.Get("creator.progress_skin"), ShipLoc.Get("creator.progress_bundle"), 0.45f);

                Directory.CreateDirectory(bundleTempDir);

                var builds = new[]
                {
                    new AssetBundleBuild { assetBundleName = BundleName, assetNames = new[] { prefabPath }, addressableNames = new[] { BundleName } }
                };

                var manifest = BuildPipeline.BuildAssetBundles(bundleTempDir, builds, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
                if (manifest == null) { Err(ShipLoc.Get("creator.err_bundle_build")); return; }

                string built = Path.Combine(bundleTempDir, BundleName);
                if (!File.Exists(built)) { Err(ShipLoc.Format("creator.err_bundle_where_fmt", built)); return; }

                EditorUtility.DisplayProgressBar(ShipLoc.Get("creator.progress_skin"), ShipLoc.Get("creator.progress_repo"), 0.75f);

                Directory.CreateDirectory(dest);
                _outputDir = dest;
                File.Copy(built, Path.Combine(dest, BundleName), overwrite: true);

                WriteInfoJson();
                CoverImage.Write(_coverPath, _outputDir);

                if (!string.IsNullOrEmpty(_loadedDir) && !string.Equals(_loadedDir, dest, StringComparison.OrdinalIgnoreCase) && Directory.Exists(_loadedDir))
                {
                    Directory.Delete(_loadedDir, true);
                    Debug.Log($"rebuilt under a new name, removed the old skin folder {_loadedDir}");
                }
                _loadedDir = dest;

                EditorUtility.DisplayProgressBar(ShipLoc.Get("creator.progress_skin"), ShipLoc.Get("creator.progress_catalog"), 0.92f);
                CatalogBat.Run(_repoRoot);
                WriteNewCatalog(_repoRoot);

                _packed = true;
                RefreshGroups();
                Ok(ShipLoc.Format("creator.packed_fmt", _name, dest));
                Debug.Log($"skin packed: {_name} ({KindStr(_kind)}) as bundle {BundleName} into {dest}");
            }
            catch (Exception ex)
            {
                Err(ShipLoc.Format("creator.err_pack_fmt", ex.Message));
                Debug.LogException(ex);
            }
            finally
            {
                if (clone != null) DestroyImmediate(clone);
                EditorUtility.ClearProgressBar();

                if (AssetDatabase.IsValidFolder(TEMP_ASSET_DIR))
                    AssetDatabase.DeleteAsset(TEMP_ASSET_DIR);
                if (Directory.Exists(bundleTempDir))
                    Directory.Delete(bundleTempDir, true);

                AssetDatabase.Refresh();
            }
        }

        private static void WriteNewCatalog(string repoRoot)
        {
            if (string.IsNullOrWhiteSpace(repoRoot) || !Directory.Exists(repoRoot)) return;

            var entries = new List<string>();

            foreach (string category in new[] { "Costumes", "Accessories", "Items", "Plinths", "Emotes" })
            {
                string root = Path.Combine(repoRoot, category);
                if (!Directory.Exists(root)) continue;
                foreach (string dir in Directory.GetDirectories(root))
                {
                    string info = Path.Combine(dir, "info.json");
                    if (!File.Exists(info)) continue;
                    string body = File.ReadAllText(info).Trim();
                    int open = body.IndexOf('{');
                    int close = body.LastIndexOf('}');
                    if (open < 0 || close <= open) continue;
                    string inner = body.Substring(open + 1, close - open - 1).Trim().TrimEnd(',').Trim();
                    string path = category + "/" + new DirectoryInfo(dir).Name;
                    string pathField = "\"path\": \"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                    string obj = inner.Length == 0 ? "{ " + pathField + " }" : "{ " + pathField + ", " + inner + " }";
                    entries.Add(obj);
                }
            }

            if (entries.Count == 0) return;

            var sb = new System.Text.StringBuilder();
            sb.Append('[');
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('\n');
                sb.Append(entries[i]);
            }
            if (entries.Count > 0) sb.Append('\n');
            sb.Append(']');
            File.WriteAllText(Path.Combine(repoRoot, "catalog2.json"), sb.ToString(), new System.Text.UTF8Encoding(false));
        }

        private void WriteInfoJson()
        {
            var bones = new List<(string, Vector3)>();
            var root = _kind == PackKind.Costume ? ResolvePreviewObject() : null;
            if (root != null)
            {
                var seen = new HashSet<string>();
                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.rootBone != null && seen.Add(smr.rootBone.name)) bones.Add((smr.rootBone.name, smr.rootBone.localPosition));
                    foreach (var b in smr.bones)
                        if (b != null && b.name != "Torso_C_jnt_NoStrechSquash" && seen.Add(b.name)) bones.Add((b.name, b.localPosition));
                }
            }

            SkinInfoJson.Write(_outputDir, BundleName, _name, _author, _description, _group, KindStr(_kind),
                keepBase: _keepBase,
                skinScale: Mathf.Approximately(_skinScale, 1f) ? 0f : _skinScale,
                itemScale: _itemScale,
                leftBoneName: _leftTransform != null ? _leftTransform.name : null,
                rightBoneName: _rightTransform != null ? _rightTransform.name : null,
                leftPos: _leftTransform != null ? _leftTransform.localPosition : Vector3.zero,
                leftRot: _leftTransform != null ? _leftTransform.localEulerAngles : Vector3.zero,
                rightPos: _rightTransform != null ? _rightTransform.localPosition : Vector3.zero,
                rightRot: _rightTransform != null ? _rightTransform.localEulerAngles : Vector3.zero,
                boneOffsets: bones);
        }


        private GameObject ResolvePreviewObject()
        {
            if (!_keepBundle) return _sourceObject;
            if (_bundlePreviewObject != null) return _bundlePreviewObject;

            string path = Path.Combine(_loadedDir, _loadedBundleFile);
            if (!File.Exists(path)) return null;

            var bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
            {
                Debug.LogWarning($"couldn't open {_loadedBundleFile} for the preview - another copy of it is probably already loaded");
                return null;
            }

            var objs = bundle.LoadAllAssets<GameObject>();
            if (objs.Length > 0) _bundlePreviewObject = objs[0];
            else Debug.LogWarning($"{_loadedBundleFile} has no GameObject in it, nothing to preview");

            bundle.Unload(false);
            return _bundlePreviewObject;
        }

        private bool LoadSkin(string dir)
        {
            string infoPath = Path.Combine(dir, "info.json");
            if (!File.Exists(infoPath)) { Err(ShipLoc.Get("creator.err_no_info")); return false; }

            try
            {
                string json = File.ReadAllText(infoPath);

                _loadedBundleFile = SkinInfoJson.ReadStr(json, "file");
                _loadedDir = dir;
                _keepBundle = !string.IsNullOrEmpty(_loadedBundleFile) && File.Exists(Path.Combine(dir, _loadedBundleFile));
                _sourceObject = null;
                _bundlePreviewObject = null;

                _name = SkinInfoJson.ReadStr(json, "name");
                _author = SkinInfoJson.ReadStr(json, "author");
                _description = SkinInfoJson.ReadStr(json, "description");
                _group = SkinInfoJson.ReadStr(json, "group");

                switch (SkinInfoJson.ReadStr(json, "type"))
                {
                    case "accessory": _kind = PackKind.Accessory; break;
                    case "item": _kind = PackKind.Item; break;
                    case "plinth": _kind = PackKind.Plinth; break;
                    case "round": _kind = PackKind.Round; break;
                    default: _kind = PackKind.Costume; break;
                }

                if (_kind == PackKind.Costume)
                {
                    _keepBase = SkinInfoJson.ReadBool(json, "keepBase");
                    float s = SkinInfoJson.ReadFloat(json, "skinScale");
                    _skinScale = s > 0f ? s : 1f;
                }
                else
                {
                    _skinScale = 1f;
                }

                if (_kind == PackKind.Item)
                {
                    _itemScale = SkinInfoJson.ReadFloat(json, "scale", 1f);
                    _leftTransform = FindSceneTransform(SkinInfoJson.ReadItemBoneName(json, "left"));
                    _rightTransform = FindSceneTransform(SkinInfoJson.ReadItemBoneName(json, "right"));
                }
                else
                {
                    _leftTransform = null;
                    _rightTransform = null;
                }

                _outputDir = dir;

                var grandparent = Directory.GetParent(dir)?.Parent;
                if (grandparent != null && File.Exists(Path.Combine(grandparent.FullName, "generate_catalog.bat")))
                {
                    _repoRoot = grandparent.FullName;
                    RefreshGroups();
                }

                string coverPath = FindCover(dir);
                if (!string.IsNullOrEmpty(coverPath))
                {
                    _coverPath = coverPath;
                    _autoCover = false;
                    LoadCoverPreview(coverPath);
                }

                Ok(ShipLoc.Format("creator.loaded_from_fmt", _name, Path.GetFileName(dir)));
                return true;
            }
            catch (Exception ex)
            {
                Err(ShipLoc.Format("creator.err_load_fmt", ex.Message));
                return false;
            }
        }

        private static Transform FindSceneTransform(string boneName)
        {
            if (string.IsNullOrWhiteSpace(boneName)) return null;

            var all = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && !EditorUtility.IsPersistent(all[i]) && string.Equals(all[i].name, boneName, StringComparison.Ordinal))
                    return all[i];

            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && !EditorUtility.IsPersistent(all[i]) && string.Equals(all[i].name, boneName, StringComparison.OrdinalIgnoreCase))
                    return all[i];

            return null;
        }
    }
}
