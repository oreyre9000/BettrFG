using System;
using System.Collections.Generic;
using System.IO;
using BetterFG.Editor.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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

        private void DrawRoundSourceStep()
        {
            var map = CurrentMap;
            if (map == null || _creatingMap) { DrawRoundCreate(map != null); return; }

            GUILayout.Space(24);
            GUILayout.Label(ActiveSceneName, _big);
            GUILayout.Space(8);
            GUILayout.Label("The open scene is what gets packed.", _small);

            GUILayout.Space(20);
            GUILayout.Label($"Skybox   {(RenderSettings.skybox != null ? RenderSettings.skybox.name : "none")}", _label);
            GUILayout.Space(4);
            GUILayout.Label($"Bundle   {RoundBundleName}", _label);

            GUILayout.Space(22);
            if (GUILayout.Button("Make a different round", _button, GUILayout.Width(200f), GUILayout.Height(26f)))
                Fade(() => _creatingMap = true);
        }

        private void DrawRoundCreate(bool cancellable)
        {
            GUILayout.Space(24);
            GUILayout.Label("Make a new round", _big);
            GUILayout.Space(8);
            GUILayout.Label("Builds the scene, drops the round template into it, and opens it.", _small);
            GUILayout.Space(24);

            if (!MapCreate.TemplateReady)
            {
                GUILayout.Label("The round template hasn't been imported into this project yet. You only do this once.", _wrap);
                GUILayout.Space(12);
                GUI.backgroundColor = new Color(0.9f, 0.6f, 0.15f);
                if (GUILayout.Button("Import round template", _bigButton, GUILayout.Height(34f)))
                    MapCreate.ImportTemplate(Repaint);
                GUI.backgroundColor = Color.white;
                return;
            }

            _newMapName = EditorGUILayout.TextField("Level name", _newMapName, _field, GUILayout.Height(ROW));

            if (!string.IsNullOrWhiteSpace(_newMapName))
            {
                GUILayout.Space(6);
                GUILayout.Label("round_" + _newMapName.Trim().ToLower().Replace(" ", "_"), _label);
            }

            GUILayout.Space(18);
            GUILayout.BeginHorizontal();

            GUI.enabled = !string.IsNullOrWhiteSpace(_newMapName);
            GUI.backgroundColor = new Color(0.35f, 0.62f, 0.38f);
            if (GUILayout.Button("Create", _bigButton, GUILayout.Width(170f), GUILayout.Height(34f)))
            {
                string picked = _newMapName;
                Fade(() => CreateRoundScene(picked));
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            if (cancellable)
            {
                GUILayout.Space(10);
                if (GUILayout.Button("Cancel", _button, GUILayout.Width(110f), GUILayout.Height(34f)))
                    Fade(() => _creatingMap = false);
            }

            GUILayout.EndHorizontal();
        }

        private void CreateRoundScene(string levelName)
        {
            if (!MapCreate.Create(levelName, out string err, out string rootName)) { Err(err); return; }

            _creatingMap = false;
            _newMapName = "";
            _name = "";
            _description = "";
            PullRoundDetails();
            Ok($"created {rootName}, build it out then carry on through the wizard");
        }

        private void DrawRoundOptionsStep()
        {
            var map = CurrentMap;
            if (map == null)
            {
                GUILayout.Space(24);
                GUILayout.Label("Open the round's scene to edit its settings", _big);
                return;
            }

            _optionsScroll = EditorGUILayout.BeginScrollView(_optionsScroll);

            EditorGUI.BeginChangeCheck();

            bool keep = EditorGUILayout.ToggleLeft("Keep existing placeable objects", map.keepExistingObjects, _label);

            GUILayout.Space(16);
            GUILayout.Label("Music  (mp3, optional, leave empty for default FG music)", _label);
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            string music = EditorGUILayout.TextField(map.musicFilePath ?? "", _field, GUILayout.Height(ROW));
            if (GUILayout.Button("Browse", _button, GUILayout.Width(90f), GUILayout.Height(ROW)))
            {
                string picked = EditorUtility.OpenFilePanel("Select music", string.IsNullOrEmpty(music) ? "" : Path.GetDirectoryName(music), "mp3,wav");
                if (!string.IsNullOrEmpty(picked)) music = picked;
            }
            if (GUILayout.Button("x", _button, GUILayout.Width(24f), GUILayout.Height(ROW))) music = "";
            GUILayout.EndHorizontal();

            GUILayout.Space(16);
            GUILayout.Label("Platform", _label);
            GUILayout.Space(4);
            _roundTarget = (BuildTarget)EditorGUILayout.EnumPopup(_roundTarget, GUILayout.Height(ROW));

            if (EditorGUI.EndChangeCheck())
            {
                map.keepExistingObjects = keep;
                map.musicFilePath = music;
                EditorUtility.SetDirty(map);
            }

            GUILayout.Space(20);
            GUILayout.Label("Skybox, ambient, reflections and fog come from the scene's Lighting window at pack time.", _wrap);

            EditorGUILayout.EndScrollView();
        }

        private bool LoadRound(string dir)
        {
            string infoPath = Path.Combine(dir, "info.json");
            if (!File.Exists(infoPath)) { Err("no info.json in that folder"); return false; }

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
                    if (EditorUtility.DisplayDialog("Open round scene", $"Open {prefab}?\n\nRounds are packed from the open scene.", "Open", "Not now"))
                    {
                        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
                        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    }
                }

                Ok($"loaded {_name}");
                return true;
            }
            catch (Exception ex)
            {
                Err($"load failed: {ex.Message}");
                return false;
            }
        }

        private void TryPackRound()
        {
            var scene = ActiveScene;
            var map = CurrentMap;
            if (map == null) { Err("no map asset for the open scene"); return; }

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
            if (sceneGo == null) { Err($"'{prefabName}' root not found in the open scene"); return; }

            if (!AssetDatabase.IsValidFolder(mapDir))
                AssetDatabase.CreateFolder("Assets/Maps", prefabName);
            PrefabUtility.SaveAsPrefabAsset(sceneGo, assetPath);
            AssetDatabase.Refresh();

            var importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null) { Err("couldn't get the asset importer"); return; }
            string prevBundle = importer.assetBundleName;

            try
            {
                EditorUtility.DisplayProgressBar("Packing round", "building the assetbundle", 0.35f);

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

                if (manifest == null) { Err("bundle build failed, check the console"); return; }

                string built = Path.Combine(tmpOut, bundleName);
                if (!File.Exists(built)) { Err($"bundle didn't land where expected ({built})"); return; }

                EditorUtility.DisplayProgressBar("Packing round", "writing into the repo", 0.8f);

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
                Ok($"packed {_name} -> {repoOut}");
                Debug.Log($"round packed: {_name} as bundle {bundleName} into {repoOut} (+ {PROJECT_OUTPUT_DIR})");
            }
            catch (Exception ex)
            {
                importer.assetBundleName = prevBundle;
                importer.SaveAndReimport();
                Err($"pack failed: {ex.Message}");
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
