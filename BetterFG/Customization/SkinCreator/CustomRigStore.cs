using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BetterFG.Customization.SkinCreator
{
    internal static class CustomRigStore
    {
        public const string Ext = ".rig";

        static readonly Dictionary<string, GameObject> _templates = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

        static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BettrFG", "CustomRigs");

        public static bool IsRig(string file) => file != null && file.EndsWith(Ext, StringComparison.OrdinalIgnoreCase);

        public static string Save(string folder, string name, ImportedModel model, string[] beanBones, int[] map)
        {
            if (folder == null)
            {
                string safe = string.Concat(name.Split(Path.GetInvalidFileNameChars()));
                if (safe.Length == 0) safe = "skin";
                folder = Path.Combine(Root, safe);
                for (int n = 2; Directory.Exists(folder); n++) folder = Path.Combine(Root, safe + "_" + n);
            }
            Directory.CreateDirectory(folder);
            string id = Path.GetFileName(folder);

            foreach (string src in model.Files)
            {
                string dest = Path.Combine(folder, Path.GetFileName(src));
                if (!string.Equals(Path.GetFullPath(src), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)) File.Copy(src, dest, true);
            }

            var lines = new List<string> { "model=" + Path.GetFileName(model.Files[0]) };
            for (int i = 0; i < map.Length; i++)
                if (map[i] >= 0) lines.Add("pair=" + beanBones[i] + "|" + model.BoneNames[map[i]]);
            File.WriteAllLines(Path.Combine(folder, id + Ext), lines);
            File.WriteAllText(Path.Combine(folder, "info.json"),
                "{ \"name\": \"" + name.Replace("\"", "'") + "\", \"type\": \"Costume\", \"file\": \"" + id + Ext + "\", \"author\": \"\" }");

            string key = Path.Combine(folder, id + Ext);
            if (_templates.TryGetValue(key, out var old) && old != null) UnityEngine.Object.Destroy(old);
            _templates.Remove(key);
            return folder;
        }

        public static (string model, List<(string bean, string bone)> pairs) Read(string rigPath)
        {
            string model = null;
            var pairs = new List<(string, string)>();
            foreach (string line in File.ReadAllLines(rigPath))
            {
                if (line.StartsWith("model=")) model = line.Substring(6);
                else if (line.StartsWith("pair="))
                {
                    var parts = line.Substring(5).Split('|');
                    pairs.Add((parts[0], parts[1]));
                }
            }
            return (model, pairs);
        }

        public static int[] ToMap(string[] beanBones, ImportedModel model, List<(string bean, string bone)> pairs)
        {
            var map = new int[beanBones.Length];
            for (int i = 0; i < map.Length; i++) map[i] = -1;
            foreach (var (bean, bone) in pairs)
            {
                int bi = Array.IndexOf(beanBones, bean);
                if (bi >= 0) map[bi] = Array.IndexOf(model.BoneNames, bone);
            }
            return map;
        }

        public static GameObject Template(string rigPath, Transform beanGeo)
        {
            if (_templates.TryGetValue(rigPath, out var cached) && cached != null) return cached;
            try
            {
                var (modelFile, pairs) = Read(rigPath);
                var model = SkinImport.Load(Path.Combine(Path.GetDirectoryName(rigPath), modelFile));
                SkinnedMeshRenderer body = null;
                foreach (var smr in beanGeo.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (smr.name.StartsWith("Body_LOD0") && !smr.name.Contains("merge")) { body = smr; break; }

                var bones = body.bones;
                var beanNames = new string[bones.Length];
                for (int i = 0; i < beanNames.Length; i++) beanNames[i] = bones[i].name;
                var template = SkinRig.Build(model, body, ToMap(beanNames, model, pairs), null, SkinRetarget.Materials(model), 0, out _);
                template.name = "BettrFG_CustomRig_" + Path.GetFileNameWithoutExtension(rigPath);
                template.transform.SetParent(null, false);
                template.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                template.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(template);
                template.SetActive(false);
                _templates[rigPath] = template;
                return template;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"custom rig {Path.GetFileName(rigPath)} wouldn't build: {ex}");
                return null;
            }
        }
    }
}
