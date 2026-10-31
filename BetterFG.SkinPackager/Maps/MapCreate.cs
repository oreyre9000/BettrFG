using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BetterFG.Editor
{
    public static class MapCreate
    {
        public const string TEMPLATE_PATH = "Assets/assets/reference/round_template/round_template.prefab";
        private const string DEFAULT_SKYBOX_PATH = "Assets/assets/reference/default_skybox.mat";
        private const string MAPS_DIR = "Assets/Maps";
        private const string OUTPUT_DIR = "Assets/Maps/Output";
        private const string PACKAGE_NAME = "roundtemplate.unitypackage";
        private const string PKG_RESOURCE = "BetterFG.Creator.Reference.Files.roundtemplate.unitypackage";

        public static bool TemplateReady => AssetDatabase.LoadAssetAtPath<GameObject>(TEMPLATE_PATH) != null;

        public static void ImportTemplate(Action onDone)
        {
            string tmp = Path.Combine(Path.GetTempPath(), PACKAGE_NAME);
            using (var stream = typeof(MapCreate).Assembly.GetManifestResourceStream(PKG_RESOURCE))
            {
                if (stream == null)
                {
                    Debug.LogError("embedded round template package missing - " + PKG_RESOURCE);
                    return;
                }
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                    stream.CopyTo(fs);
            }

            void Done(string packageName)
            {
                AssetDatabase.importPackageCompleted -= Done;
                AssetDatabase.Refresh();
                onDone?.Invoke();
            }

            void Failed(string packageName, string errorMessage)
            {
                AssetDatabase.importPackageCompleted -= Done;
                Debug.LogError("round template import failed - " + errorMessage);
                onDone?.Invoke();
            }

            AssetDatabase.importPackageCompleted += Done;
            AssetDatabase.importPackageFailed += Failed;
            AssetDatabase.ImportPackage(tmp, interactive: true);
        }

        public static bool Create(string levelName, out string error, out string rootName)
        {
            error = "";
            rootName = "";

            string trimmed = (levelName ?? "").Trim();
            if (string.IsNullOrEmpty(trimmed)) { error = "Level name can't be empty."; return false; }
            if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { error = "Level name has invalid characters."; return false; }

            var templateAsset = AssetDatabase.LoadAssetAtPath<GameObject>(TEMPLATE_PATH);
            if (templateAsset == null) { error = "Template not found, import it first."; return false; }

            rootName = "round_" + trimmed.ToLower().Replace(" ", "_");
            string mapDir = $"{MAPS_DIR}/{rootName}";

            if (!AssetDatabase.IsValidFolder(MAPS_DIR))
                AssetDatabase.CreateFolder("Assets", "Maps");
            if (!AssetDatabase.IsValidFolder(mapDir))
                AssetDatabase.CreateFolder(MAPS_DIR, rootName);
            if (!AssetDatabase.IsValidFolder(OUTPUT_DIR))
                AssetDatabase.CreateFolder(MAPS_DIR, "Output");

            string soPath = $"{mapDir}/{rootName}.asset";
            var mapAsset = AssetDatabase.LoadAssetAtPath<BetterFGMapAsset>(soPath);
            if (mapAsset == null)
            {
                mapAsset = ScriptableObject.CreateInstance<BetterFGMapAsset>();
                AssetDatabase.CreateAsset(mapAsset, soPath);
            }

            mapAsset.displayName = trimmed;
            mapAsset.prefabName = rootName;
            EditorUtility.SetDirty(mapAsset);
            AssetDatabase.SaveAssets();

            string scenePath = $"{mapDir}/{rootName}.unity";
            GameObject go;
            try
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { error = "Cancelled, the open scene wasn't saved."; return false; }

                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                var sky = AssetDatabase.LoadAssetAtPath<Material>(DEFAULT_SKYBOX_PATH);
                if (sky != null) RenderSettings.skybox = sky;

                go = (GameObject)PrefabUtility.InstantiatePrefab(templateAsset, scene);
                go.name = rootName;
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

                EditorSceneManager.SaveScene(scene, scenePath);
            }
            catch (Exception ex)
            {
                error = $"Create map scene failed: {ex.Message}";
                Debug.LogError($"map scene creation blew up: {ex}");
                return false;
            }

            AssetDatabase.Refresh();
            Undo.RegisterCreatedObjectUndo(go, "Create BettrFG Map");

            var folderObj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(mapDir);
            if (folderObj != null)
            {
                Selection.activeObject = folderObj;
                EditorGUIUtility.PingObject(folderObj);
            }

            Debug.Log($"created '{rootName}' - scene {scenePath}, asset {soPath}");
            return true;
        }
    }
}
