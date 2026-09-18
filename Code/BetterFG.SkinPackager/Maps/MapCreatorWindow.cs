using UnityEditor;
using UnityEngine;

namespace BetterFG.Editor
{
    public class MapCreatorWindow : EditorWindow
    {
        [MenuItem("BettrFG/Round/Map Creator")]
        public static void Open() => GetWindow<MapCreatorWindow>("Map Creator");

        private string _levelName = "";
        private string _statusMsg = "";
        private UnityEditor.MessageType _statusType = UnityEditor.MessageType.None;

        private GUIStyle _sectionStyle;
        private GUIStyle _warnStyle;
        private bool _stylesReady;

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _sectionStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(8, 8, 6, 6) };
            _warnStyle = new GUIStyle(EditorStyles.helpBox)
            {
                normal = { textColor = new Color(1f, 0.75f, 0.1f) },
                fontStyle = FontStyle.Bold,
                fontSize = 11,
                padding = new RectOffset(8, 8, 6, 6),
                wordWrap = true,
            };
            _stylesReady = true;
        }

        private void OnGUI()
        {
            EnsureStyles();

            bool templateReady = MapCreate.TemplateReady;

            if (!templateReady)
            {
                GUILayout.BeginVertical(_sectionStyle);
                GUILayout.Label("Setup Required", EditorStyles.boldLabel);
                GUILayout.Label(
                    "round_template.unitypackage hasn't been imported yet.\n" +
                    "Click below to import it... you wont have to do it again",
                    _warnStyle);
                GUILayout.Space(4);

                GUI.backgroundColor = new Color(0.9f, 0.6f, 0.15f);
                if (GUILayout.Button("Import round_template.unitypackage", GUILayout.Height(28)))
                    TryImportPackage();
                GUI.backgroundColor = Color.white;
                GUILayout.EndVertical();
                GUILayout.Space(4);
            }

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("New Map", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Creates a new scene round_(levelname) in the map's folder, drops a detached copy\n" +
                "of the round template into it, and creates a BetterFGMapAsset.",
                UnityEditor.MessageType.Info);
            GUILayout.Space(4);

            GUI.enabled = templateReady;
            _levelName = EditorGUILayout.TextField("Level Name", _levelName);
            GUILayout.Space(6);

            GUI.backgroundColor = new Color(0.25f, 0.7f, 0.35f);
            if (GUILayout.Button("Create Map", GUILayout.Height(28))) TryCreate();
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            GUILayout.EndVertical();

            if (!string.IsNullOrEmpty(_statusMsg))
            {
                GUILayout.Space(4);
                EditorGUILayout.HelpBox(_statusMsg, _statusType);
            }

            GUILayout.Space(8);
        }

        private void TryImportPackage()
        {
            MapCreate.ImportTemplate(() => EditorApplication.delayCall += Repaint);
        }

        private void TryCreate()
        {
            _statusMsg = "";
            if (!MapCreate.Create(_levelName, out string err, out string rootName))
            {
                if (!string.IsNullOrEmpty(err)) Err(err);
                return;
            }
            Ok($"Created '{rootName}' scene. Edit your map, then pack it in BettrFG > Creator Hub.");
        }

        private void Ok(string msg) { _statusMsg = msg; _statusType = UnityEditor.MessageType.Info; Repaint(); }
        private void Err(string msg) { _statusMsg = msg; _statusType = UnityEditor.MessageType.Error; Repaint(); }
    }
}
