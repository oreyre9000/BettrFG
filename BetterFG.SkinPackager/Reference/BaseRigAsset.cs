using System.Diagnostics;
using System.IO;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace BetterFG.Editor.Reference
{
    public static class BaseRigAsset
    {
        const string RESOURCE = "BetterFG.Creator.Reference.Files.baserig.blend";
        const string PREF_KEY = "BetterFG.BaseRigPath";

        [MenuItem("BettrFG/References/Base Rig (Blender)")]
        static void OpenBaseRig()
        {
            string path = EditorPrefs.GetString(PREF_KEY, "");
            if (!File.Exists(path))
            {
                path = EditorUtility.SaveFilePanel("Save base rig", "", "base rig", "blend");
                if (string.IsNullOrEmpty(path)) return;

                using (var stream = typeof(BaseRigAsset).Assembly.GetManifestResourceStream(RESOURCE))
                {
                    if (stream == null)
                    {
                        Debug.LogError("BaseRigAsset: embedded blend not found - " + RESOURCE);
                        return;
                    }
                    using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                        stream.CopyTo(fs);
                }
                EditorPrefs.SetString(PREF_KEY, path);
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }
}
