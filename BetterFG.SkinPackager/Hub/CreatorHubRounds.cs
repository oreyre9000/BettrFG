using System;
using System.Collections.Generic;
using System.IO;
using BetterFG.Editor.Map;
using BettrFG.uGUI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BetterFG.Editor
{
    public partial class CreatorHubWindow
    {
        private const string PROJECT_OUTPUT_DIR = "Assets/Maps/Output";

        private BuildTarget _roundTarget = BuildTarget.StandaloneWindows64;
        private bool _creatingMap;
        private string _newMapName = "";

        private static Scene ActiveScene => SceneManager.GetActiveScene();

        private static string ActiveSceneName => ActiveScene.IsValid() ? ActiveScene.name : "";

        private string RoundBundleName => ActiveSceneName.ToLowerInvariant();

        private static BetterFGMapAsset CurrentMap
        {
            get
            {
                string n = ActiveSceneName;
                return string.IsNullOrEmpty(n) ? null : AssetDatabase.LoadAssetAtPath<BetterFGMapAsset>($"Assets/Maps/{n}/{n}.asset");
            }
        }

        private void PullRoundDetails()
        {
            var map = CurrentMap;
            if (map == null) return;
            if (string.IsNullOrWhiteSpace(_name)) _name = string.IsNullOrWhiteSpace(map.displayName) ? ActiveSceneName : map.displayName;
            if (string.IsNullOrWhiteSpace(_description)) _description = map.description;
        }

        private void BuildRoundSourceStep(RectTransform p, float w)
        {
            var map = CurrentMap;
            if (map == null || _creatingMap) { BuildRoundCreate(p, w, map != null); return; }

            Big(p, 24f, w, ActiveSceneName);
            Small(p, 58f, w, "creator.round_open_scene_sub");
            Lbl(p, new Rect(0f, 104f, w, 22f), ShipLoc.Format("creator.round_skybox_fmt", RenderSettings.skybox != null ? RenderSettings.skybox.name : ShipLoc.Get("creator.round_none")));
            Lbl(p, new Rect(0f, 130f, w, 22f), ShipLoc.Format("creator.round_bundle_fmt", RoundBundleName));
            Btn(p, new Rect(0f, 172f, 240f, ROW), "creator.round_make_different", BTN_DARK, () => Fade(() => _creatingMap = true), FS_SM);
        }

        private void BuildRoundCreate(RectTransform p, float w, bool cancellable)
        {
            Big(p, 24f, w, "creator.round_new");
            Small(p, 58f, w, "creator.round_new_sub");
            float y = 104f;

            if (!MapCreate.TemplateReady)
            {
                Lbl(p, new Rect(0f, y, w, 22f), "creator.round_template_missing", FS, LABEL, TextAnchor.MiddleCenter);
                float bw = Mathf.Min(w, 320f);
                Btn(p, new Rect((w - bw) * 0.5f, y + 36f, bw, 36f), "creator.round_import_template", BTN_ORANGE, () => MapCreate.ImportTemplate(RequestRebuild));
                return;
            }

            Lbl(p, new Rect(0f, y, LABEL_W, ROW), "creator.round_level_name");
            Text preview = null;
            Button create = null;
            Field(p, new Rect(LABEL_W, y, w - LABEL_W, ROW), _newMapName, "creator.round_level_name_hint", v =>
            {
                _newMapName = v;
                preview.text = string.IsNullOrWhiteSpace(v) ? "" : "round_" + v.Trim().ToLower().Replace(" ", "_");
                create.interactable = !string.IsNullOrWhiteSpace(v);
            });
            y += ROW + 6f;
            preview = Lbl(p, new Rect(LABEL_W, y, w - LABEL_W, 22f),
                string.IsNullOrWhiteSpace(_newMapName) ? "" : "round_" + _newMapName.Trim().ToLower().Replace(" ", "_"), FS_SM, HINT);
            y += 40f;

            create = Btn(p, new Rect(LABEL_W, y, 170f, 36f), "creator.round_create", BTN_GREEN, () =>
            {
                if (string.IsNullOrWhiteSpace(_newMapName)) return;
                string picked = _newMapName;
                Fade(() => CreateRoundScene(picked));
            });
            create.interactable = !string.IsNullOrWhiteSpace(_newMapName);

            if (cancellable)
                Btn(p, new Rect(LABEL_W + 180f, y, 110f, 36f), "ui.cancel", BTN_DARK, () => Fade(() => _creatingMap = false));
        }

        private void CreateRoundScene(string levelName)
        {
            if (!MapCreate.Create(levelName, out string err, out string rootName)) { Err(err); return; }

            _creatingMap = false;
            _newMapName = "";
            _name = "";
            _description = "";
            PullRoundDetails();
            Ok(ShipLoc.Format("creator.round_created_fmt", rootName));
        }

        private static readonly BuildTarget[] ROUND_TARGETS =
        {
            BuildTarget.StandaloneWindows64, BuildTarget.StandaloneWindows, BuildTarget.StandaloneOSX, BuildTarget.StandaloneLinux64,
            BuildTarget.PS4, BuildTarget.PS5, BuildTarget.XboxOne, BuildTarget.GameCoreXboxOne, BuildTarget.GameCoreXboxSeries, BuildTarget.Switch,
            BuildTarget.Android, BuildTarget.iOS,
        };

        private void BuildRoundOptionsStep(RectTransform p, float w)
        {
            var map = CurrentMap;
            if (map == null)
            {
                Big(p, 24f, w, "creator.round_open_to_edit");
                return;
            }

            float y = 0f;
            Check(p, new Rect(0f, y, w, ROW), "creator.round_keep_objects", map.keepExistingObjects, v =>
            {
                map.keepExistingObjects = v;
                EditorUtility.SetDirty(map);
                RequestRebuild();
            });
            y += ROW + 22f;

            Lbl(p, new Rect(0f, y, w, 22f), "creator.round_music");
            y += 26f;
            Field(p, new Rect(0f, y, w - 150f, ROW), map.musicFilePath ?? "", "creator.round_music_hint", v =>
            {
                map.musicFilePath = v;
                EditorUtility.SetDirty(map);
            });
            Btn(p, new Rect(w - 140f, y, 100f, ROW), "ui.browse", BTN_DARK, () =>
            {
                string music = map.musicFilePath ?? "";
                string picked = EditorUtility.OpenFilePanel(ShipLoc.Get("creator.round_music_pick_title"), string.IsNullOrEmpty(music) ? "" : Path.GetDirectoryName(music), "mp3,wav");
                if (string.IsNullOrEmpty(picked)) return;
                map.musicFilePath = picked;
                EditorUtility.SetDirty(map);
                RequestRebuild();
            });
            Btn(p, new Rect(w - 34f, y, 34f, ROW), "creator.clear_x", BTN_DARK, () =>
            {
                map.musicFilePath = "";
                EditorUtility.SetDirty(map);
                RequestRebuild();
            });
            y += ROW + 22f;

            Lbl(p, new Rect(0f, y, w, 22f), "creator.round_platform");
            y += 26f;
            var names = new List<string>();
            foreach (var t in ROUND_TARGETS) names.Add(t.ToString());
            Pick(p, new Rect(0f, y, 280f, ROW), names, Mathf.Max(0, Array.IndexOf(ROUND_TARGETS, _roundTarget)),
                v => { _roundTarget = ROUND_TARGETS[v]; RequestRebuild(); });
            y += ROW + 26f;

            Lbl(p, new Rect(0f, y, w, 22f), "creator.round_lighting_note", FS_SM, HINT);
        }

        private bool LoadRound(string dir)
        {
            string infoPath = Path.Combine(dir, "info.json");
            if (!File.Exists(infoPath)) { Err(ShipLoc.Get("creator.err_no_info")); return false; }

            try
            {
                string json = File.ReadAllText(infoPath);

                _kind = PackKind.Round;
                _name = SkinInfoJson.ReadStr(json, "displayName");
                _description = SkinInfoJson.ReadStr(json, "description");
                _author = "";
                _group = "";
                _sourceObject = null;
                _bundlePreviewObject = null;
                _keepBundle = false;
                _loadedBundleFile = "";
                _loadedDir = dir;
                _outputDir = dir;

                string cover = FindCover(dir);
                if (!string.IsNullOrEmpty(cover)) { _coverPath = cover; LoadCoverPreview(cover); }

                string prefab = SkinInfoJson.ReadStr(json, "prefab");
                string scenePath = $"Assets/Maps/{prefab}/{prefab}.unity";
                if (!string.IsNullOrEmpty(prefab) && ActiveSceneName != prefab && File.Exists(scenePath))
                {
                    if (EditorUtility.DisplayDialog(ShipLoc.Get("creator.round_open_title"), ShipLoc.Format("creator.round_open_body_fmt", prefab),
                        ShipLoc.Get("creator.round_open"), ShipLoc.Get("creator.round_not_now")))
                    {
                        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
                        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    }
                }

                Ok(ShipLoc.Format("creator.round_loaded_fmt", _name));
                return true;
            }
            catch (Exception ex)
            {
                Err(ShipLoc.Format("creator.err_load_fmt", ex.Message));
                return false;
            }
        }

        private void TryPackRound()
        {
            var scene = ActiveScene;
            var map = CurrentMap;
            if (map == null) { Err(ShipLoc.Get("creator.err_no_map")); return; }

            string prefabName = scene.name;
            string bundleName = RoundBundleName;
            string mapDir = $"Assets/Maps/{prefabName}";
            string assetPath = $"{mapDir}/{prefabName}.prefab";

            map.displayName = _name;
            map.description = _description;
            map.prefabName = prefabName;
            EditorUtility.SetDirty(map);

            if (scene.isDirty) EditorSceneManager.SaveScene(scene);

            GameObject sceneGo = FindRootInScene(scene, prefabName);
            if (sceneGo == null) { Err(ShipLoc.Format("creator.err_root_missing_fmt", prefabName)); return; }

            if (!AssetDatabase.IsValidFolder(mapDir))
                AssetDatabase.CreateFolder("Assets/Maps", prefabName);
            PrefabUtility.SaveAsPrefabAsset(sceneGo, assetPath);
            AssetDatabase.Refresh();

            var importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null) { Err(ShipLoc.Get("creator.err_importer")); return; }
            string prevBundle = importer.assetBundleName;

            try
            {
                EditorUtility.DisplayProgressBar(ShipLoc.Get("creator.progress_round"), ShipLoc.Get("creator.progress_bundle"), 0.35f);

                importer.assetBundleName = bundleName;
                importer.SaveAndReimport();

                var assetNames = new List<string> { assetPath };

                var skybox = RenderSettings.skybox;
                if (skybox != null)
                {
                    string skyboxPath = AssetDatabase.GetAssetPath(skybox);
                    if (!string.IsNullOrEmpty(skyboxPath)) assetNames.Add(skyboxPath);
                    else Debug.LogWarning($"skybox '{skybox.name}' isn't a project asset, can't bundle it");
                }

                var builds = new[]
                {
                    new AssetBundleBuild { assetBundleName = bundleName, assetNames = assetNames.ToArray() }
                };

                string tmpOut = Path.Combine(Application.temporaryCachePath, "bfgmapbuild");
                Directory.CreateDirectory(tmpOut);
                var manifest = BuildPipeline.BuildAssetBundles(tmpOut, builds, BuildAssetBundleOptions.ChunkBasedCompression, _roundTarget);

                importer.assetBundleName = prevBundle;
                importer.SaveAndReimport();
                AssetDatabase.Refresh();

                if (manifest == null) { Err(ShipLoc.Get("creator.err_bundle_build")); return; }

                string built = Path.Combine(tmpOut, bundleName);
                if (!File.Exists(built)) { Err(ShipLoc.Format("creator.err_bundle_where_fmt", built)); return; }

                EditorUtility.DisplayProgressBar(ShipLoc.Get("creator.progress_round"), ShipLoc.Get("creator.progress_repo"), 0.8f);

                string repoOut = ComputeDestDir();
                if (!AssetDatabase.IsValidFolder(PROJECT_OUTPUT_DIR))
                    AssetDatabase.CreateFolder("Assets/Maps", "Output");

                foreach (string dir in new[] { repoOut, Path.GetFullPath(PROJECT_OUTPUT_DIR) })
                {
                    Directory.CreateDirectory(dir);
                    File.Copy(built, Path.Combine(dir, bundleName), overwrite: true);
                    MapInfoJson.Write(dir, map);
                    CopyMusic(map, dir);
                    CoverImage.Write(_coverPath, dir);
                }
                AssetDatabase.Refresh();

                _outputDir = repoOut;
                CatalogBat.Run(_repoRoot);

                _packed = true;
                RefreshGroups();
                Ok(ShipLoc.Format("creator.packed_fmt", _name, repoOut));
                Debug.Log($"round packed: {_name} as bundle {bundleName} into {repoOut} (+ {PROJECT_OUTPUT_DIR})");
            }
            catch (Exception ex)
            {
                importer.assetBundleName = prevBundle;
                importer.SaveAndReimport();
                Err(ShipLoc.Format("creator.err_pack_fmt", ex.Message));
                Debug.LogException(ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static GameObject FindRootInScene(Scene scene, string prefabName)
        {
            var roots = scene.GetRootGameObjects();
            foreach (var r in roots)
                if (r.name == prefabName) return r;
            return roots.Length > 0 ? roots[0] : null;
        }

        private static void CopyMusic(BetterFGMapAsset map, string dir)
        {
            if (string.IsNullOrEmpty(map.musicFilePath)) return;
            if (!File.Exists(map.musicFilePath)) { Debug.LogWarning($"music file not found: {map.musicFilePath}"); return; }
            File.Copy(map.musicFilePath, Path.Combine(dir, Path.GetFileName(map.musicFilePath)), overwrite: true);
        }
    }
}
