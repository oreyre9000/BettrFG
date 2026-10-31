using System;
using System.Collections.Generic;
using System.IO;
using BetterFG.Utilities;
using UnityEngine;
using A = Assimp;

namespace BetterFG.Customization.SkinCreator
{
    internal sealed class ImportedPart
    {
        public int[] Tris;
        public Texture2D Tex;
        public Color Col = Color.white;
    }

    internal sealed class ImportedModel
    {
        public Vector3[] Pos, Nrm;
        public Vector2[] Uv;
        public ImportedPart[] Parts;
        public int[] BoneIdx;
        public float[] BoneW;
        public string[] BoneNames;
        public Vector3[] BonePos;
        public int[] BoneParent;
        public List<string> Files = new List<string>();

        public float Fit(float height, out Vector3 origin)
        {
            var min = Pos[0];
            var max = Pos[0];
            foreach (var p in Pos) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            origin = new Vector3((min.x + max.x) * 0.5f, min.y, (min.z + max.z) * 0.5f);
            return height / Mathf.Max(max.y - min.y, 0.0001f);
        }
    }

    internal static class SkinImport
    {
        public const string Filter = "3D models\0*.fbx;*.glb;*.gltf;*.obj;*.dae\0All Files\0*.*\0";

        static bool _ready;

        static void EnsureNative()
        {
            if (_ready) return;
            byte[] dll = EmbeddedResourceandUnity.Bytes("BetterFG.assets.shell.assimp.dll");
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BettrFG", "native");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "assimp.dll");
            if (!File.Exists(path) || new FileInfo(path).Length != dll.Length) File.WriteAllBytes(path, dll);
            A.Unmanaged.AssimpLibrary.Instance.LoadLibrary(path);
            _ready = true;
        }

        static Vector3 Point(A.Matrix4x4 m, float x, float y, float z) =>
            new Vector3(m.A1 * x + m.A2 * y + m.A3 * z + m.A4, m.B1 * x + m.B2 * y + m.B3 * z + m.B4, m.C1 * x + m.C2 * y + m.C3 * z + m.C4);

        static Vector3 Dir(A.Matrix4x4 m, float x, float y, float z) =>
            new Vector3(m.A1 * x + m.A2 * y + m.A3 * z, m.B1 * x + m.B2 * y + m.B3 * z, m.C1 * x + m.C2 * y + m.C3 * z).normalized;

        static Texture2D LoadTexture(A.Scene scene, string folder, string path, List<string> files)
        {
            byte[] bytes = null;
            if (path.StartsWith("*") && int.TryParse(path.Substring(1), out int ti) && ti < scene.TextureCount)
            {
                var et = scene.Textures[ti];
                if (et.IsCompressed) bytes = et.CompressedData;
            }
            else
            {
                string name = Path.GetFileName(path);
                foreach (string candidate in new[] { Path.Combine(folder, path), Path.Combine(folder, name), Path.Combine(folder, "textures", name), Path.Combine(folder, "tex", name) })
                {
                    if (!File.Exists(candidate)) continue;
                    bytes = File.ReadAllBytes(candidate);
                    files.Add(candidate);
                    break;
                }
            }
            if (bytes == null) { Plugin.Log.LogWarning($"texture {path} not found or not embedded"); return null; }
            var tex = new Texture2D(2, 2);
            if (!ImageConversion.LoadImage(tex, bytes)) { Plugin.Log.LogWarning($"texture {path} isn't a png/jpg, skipping it"); return null; }
            return tex;
        }

        public static ImportedModel Load(string path)
        {
            EnsureNative();
            A.Scene scene;
            using (var ctx = new A.AssimpContext())
                scene = ctx.ImportFile(path,
                    A.PostProcessSteps.Triangulate | A.PostProcessSteps.JoinIdenticalVertices |
                    A.PostProcessSteps.LimitBoneWeights | A.PostProcessSteps.GenerateSmoothNormals);

            var worldOf = new Dictionary<int, A.Matrix4x4>();
            var parentOf = new Dictionary<string, string>();
            void Walk(A.Node n, A.Matrix4x4 parent)
            {
                var w = parent * n.Transform;
                foreach (int mi in n.MeshIndices) if (!worldOf.ContainsKey(mi)) worldOf[mi] = w;
                foreach (var c in n.Children) { parentOf[c.Name] = n.Name; Walk(c, w); }
            }
            Walk(scene.RootNode, A.Matrix4x4.Identity);

            string folder = Path.GetDirectoryName(path);
            var files = new List<string> { path };
            var pos = new List<Vector3>();
            var nrm = new List<Vector3>();
            var uv = new List<Vector2>();
            var partOf = new Dictionary<int, ImportedPart>();
            var partTris = new Dictionary<int, List<int>>();
            var inf = new List<List<(int bone, float w)>>();
            var boneOf = new Dictionary<string, int>();
            var names = new List<string>();
            var bonePos = new List<Vector3>();

            for (int mIdx = 0; mIdx < scene.MeshCount; mIdx++)
            {
                var mesh = scene.Meshes[mIdx];
                var world = worldOf.TryGetValue(mIdx, out var wm) ? wm : A.Matrix4x4.Identity;
                int baseV = pos.Count;
                bool hasUv = mesh.HasTextureCoords(0);
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    var v = mesh.Vertices[i];
                    var n = mesh.Normals[i];
                    var p = Point(world, v.X, v.Y, v.Z);
                    var d = Dir(world, n.X, n.Y, n.Z);
                    pos.Add(new Vector3(-p.x, p.y, p.z));
                    nrm.Add(new Vector3(-d.x, d.y, d.z));
                    var t = hasUv ? mesh.TextureCoordinateChannels[0][i] : default;
                    uv.Add(new Vector2(t.X, t.Y));
                    inf.Add(new List<(int, float)>());
                }

                int mat = mesh.MaterialIndex;
                if (!partOf.ContainsKey(mat))
                {
                    var part = new ImportedPart();
                    var am = scene.Materials[mat];
                    if (am.HasColorDiffuse) part.Col = new Color(am.ColorDiffuse.R, am.ColorDiffuse.G, am.ColorDiffuse.B, 1f);
                    if (am.HasTextureDiffuse) { part.Tex = LoadTexture(scene, folder, am.TextureDiffuse.FilePath, files); if (part.Tex != null) part.Col = Color.white; }
                    partOf[mat] = part;
                    partTris[mat] = new List<int>();
                }
                var tris = partTris[mat];
                foreach (var f in mesh.Faces)
                {
                    if (f.IndexCount != 3) continue;
                    tris.Add(baseV + f.Indices[0]);
                    tris.Add(baseV + f.Indices[2]);
                    tris.Add(baseV + f.Indices[1]);
                }

                foreach (var bone in mesh.Bones)
                {
                    if (!boneOf.TryGetValue(bone.Name, out int bi))
                    {
                        bi = names.Count;
                        boneOf[bone.Name] = bi;
                        names.Add(bone.Name);
                        var m = bone.OffsetMatrix;
                        m.Inverse();
                        var bp = Point(world, m.A4, m.B4, m.C4);
                        bonePos.Add(new Vector3(-bp.x, bp.y, bp.z));
                    }
                    foreach (var vw in bone.VertexWeights) inf[baseV + vw.VertexID].Add((bi, vw.Weight));
                }
            }

            var parts = new List<ImportedPart>();
            foreach (var kv in partOf) { kv.Value.Tris = partTris[kv.Key].ToArray(); parts.Add(kv.Value); }

            var boneParent = new int[names.Count];
            for (int i = 0; i < names.Count; i++)
            {
                boneParent[i] = -1;
                string p = names[i];
                while (parentOf.TryGetValue(p, out var up))
                {
                    p = up;
                    if (boneOf.TryGetValue(p, out int pi)) { boneParent[i] = pi; break; }
                }
            }

            var boneIdx = new int[pos.Count * 4];
            var boneW = new float[pos.Count * 4];
            for (int v = 0; v < pos.Count; v++)
            {
                var list = inf[v];
                list.Sort((a, b) => b.w.CompareTo(a.w));
                float sum = 0f;
                for (int k = 0; k < 4 && k < list.Count; k++) sum += list[k].w;
                for (int k = 0; k < 4 && k < list.Count; k++)
                {
                    boneIdx[v * 4 + k] = list[k].bone;
                    boneW[v * 4 + k] = sum > 0f ? list[k].w / sum : 0f;
                }
            }

            return new ImportedModel
            {
                Pos = pos.ToArray(), Nrm = nrm.ToArray(), Uv = uv.ToArray(), Parts = parts.ToArray(),
                BoneIdx = boneIdx, BoneW = boneW,
                BoneNames = names.ToArray(), BonePos = bonePos.ToArray(), BoneParent = boneParent,
                Files = files,
            };
        }
    }
}
