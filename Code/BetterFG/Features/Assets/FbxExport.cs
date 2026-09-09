using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BetterFG.Customization.Player;
using BetterFG.Services;
using BetterFG.Utilities;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;

namespace BetterFG.Features.Assets
{
    public static class FbxExport
    {
        private static readonly string SEP = new string(new[] { (char)0, (char)1 });

        private static readonly byte[] FooterId =
        {
            0xFA, 0xBC, 0xAB, 0x09, 0xD0, 0xC8, 0xD4, 0x66,
            0xB1, 0x76, 0xFB, 0x83, 0x1C, 0xF7, 0x26, 0x7E
        };

        private static readonly byte[] FooterMagic =
        {
            0xF8, 0x5A, 0x8C, 0x6A, 0xDE, 0xF5, 0xD9, 0x7E,
            0xEC, 0xE9, 0x0C, 0xE3, 0x75, 0x8F, 0x29, 0x0B
        };

        private struct FI { public int V; public FI(int v) { V = v; } }
        private struct FL { public long V; public FL(long v) { V = v; } }
        private struct FD { public double V; public FD(double v) { V = v; } }

        private class N
        {
            public readonly string Name;
            public readonly List<object> Props = new List<object>();
            public readonly List<N> Kids = new List<N>();

            public N(string name, params object[] props)
            {
                Name = name;
                Props.AddRange(props);
            }

            public N Add(string name, params object[] props)
            {
                var n = new N(name, props);
                Kids.Add(n);
                return n;
            }
        }

        private static void WriteProp(BinaryWriter w, object p)
        {
            switch (p)
            {
                case double[] da:
                    w.Write((byte)'d'); w.Write(da.Length); w.Write(0); w.Write(da.Length * 8);
                    foreach (var d in da) w.Write(d);
                    break;
                case int[] ia:
                    w.Write((byte)'i'); w.Write(ia.Length); w.Write(0); w.Write(ia.Length * 4);
                    foreach (var i in ia) w.Write(i);
                    break;
                case FI fi: w.Write((byte)'I'); w.Write(fi.V); break;
                case FL fl: w.Write((byte)'L'); w.Write(fl.V); break;
                case FD fd: w.Write((byte)'D'); w.Write(fd.V); break;
                case bool b: w.Write((byte)'C'); w.Write((byte)(b ? 1 : 0)); break;
                case int i2: w.Write((byte)'I'); w.Write(i2); break;
                case double d2: w.Write((byte)'D'); w.Write(d2); break;
                case string s:
                    var raw = Encoding.UTF8.GetBytes(s);
                    w.Write((byte)'S'); w.Write(raw.Length); w.Write(raw);
                    break;
                default: throw new ArgumentException("fbx property type " + p.GetType().Name);
            }
        }

        private static void WriteNode(BinaryWriter w, N n)
        {
            var s = w.BaseStream;
            long start = s.Position;
            w.Write(0u); w.Write(0u); w.Write(0u);
            var name = Encoding.UTF8.GetBytes(n.Name);
            w.Write((byte)name.Length);
            w.Write(name);

            long propStart = s.Position;
            foreach (var p in n.Props) WriteProp(w, p);
            long propLen = s.Position - propStart;

            foreach (var c in n.Kids) WriteNode(w, c);
            if (n.Kids.Count > 0) w.Write(new byte[13]);

            long end = s.Position;
            s.Position = start;
            w.Write((uint)end); w.Write((uint)n.Props.Count); w.Write((uint)propLen);
            s.Position = end;
        }

        private class Scene
        {
            private long _id = 1000000L;
            public readonly List<N> Objects = new List<N>();
            public readonly List<(string kind, long child, long parent, string prop)> Conns =
                new List<(string, long, long, string)>();

            public long NextId() => ++_id;

            public void Connect(long child, long parent) => Conns.Add(("OO", child, parent, null));

            public long AddMaterial(string name, Color c, string texAbs, string texRel)
            {
                long mid = NextId();
                var m = new N("Material", new FL(mid), name + SEP + "Material", "");
                m.Add("Version", new FI(102));
                m.Add("ShadingModel", "phong");
                m.Add("MultiLayer", new FI(0));
                var p = m.Add("Properties70");
                p.Add("P", "DiffuseColor", "Color", "", "A", new FD(c.r), new FD(c.g), new FD(c.b));
                p.Add("P", "DiffuseFactor", "Number", "", "A", new FD(1.0));
                p.Add("P", "SpecularColor", "Color", "", "A", new FD(0.2), new FD(0.2), new FD(0.2));
                p.Add("P", "ShininessExponent", "Number", "", "A", new FD(20.0));
                p.Add("P", "Opacity", "Number", "", "A", new FD(c.a));
                Objects.Add(m);

                if (string.IsNullOrEmpty(texAbs)) return mid;

                long vid = NextId(), tid = NextId();
                string base_ = Path.GetFileNameWithoutExtension(texRel);

                var v = new N("Video", new FL(vid), base_ + SEP + "Video", "Clip");
                v.Add("Type", "Clip");
                v.Add("Properties70").Add("P", "Path", "KString", "XRefUrl", "", texAbs);
                v.Add("UseMipMap", new FI(0));
                v.Add("Filename", texAbs);
                v.Add("RelativeFilename", texRel);
                Objects.Add(v);

                var t = new N("Texture", new FL(tid), base_ + SEP + "Texture", "");
                t.Add("Type", "TextureVideoClip");
                t.Add("Version", new FI(202));
                t.Add("TextureName", base_ + SEP + "Texture");
                var tp = t.Add("Properties70");
                tp.Add("P", "UVSet", "KString", "", "", "UVMap");
                tp.Add("P", "UseMaterial", "bool", "", "", new FI(1));
                t.Add("Media", base_ + SEP + "Video");
                t.Add("FileName", texAbs);
                t.Add("RelativeFilename", texRel);
                t.Add("ModelUVTranslation", new FD(0.0), new FD(0.0));
                t.Add("ModelUVScaling", new FD(1.0), new FD(1.0));
                t.Add("Texture_Alpha_Source", "None");
                Objects.Add(t);

                Connect(vid, tid);
                Conns.Add(("OP", tid, mid, "DiffuseColor"));
                return mid;
            }

            public long AddModel(string name, string subType, Matrix4x4 local)
            {
                long id = NextId();
                var pos = local.GetColumn(3);
                var rot = local.rotation.eulerAngles;
                var scl = local.lossyScale;

                var model = new N("Model", new FL(id), name + SEP + "Model", subType);
                model.Add("Version", new FI(232));
                var mp = model.Add("Properties70");
                mp.Add("P", "RotationOrder", "enum", "", "", new FI(4));
                mp.Add("P", "Lcl Translation", "Lcl Translation", "", "A", new FD(pos.x), new FD(pos.y), new FD(pos.z));
                mp.Add("P", "Lcl Rotation", "Lcl Rotation", "", "A", new FD(rot.x), new FD(rot.y), new FD(rot.z));
                mp.Add("P", "Lcl Scaling", "Lcl Scaling", "", "A", new FD(scl.x), new FD(scl.y), new FD(scl.z));
                model.Add("Shading", true);
                model.Add("Culling", "CullingOff");
                Objects.Add(model);
                return id;
            }

            public long AddGeometry(string name, List<Vector3> verts, List<Vector3> norms,
                List<Vector2> uvs, List<List<int>> subMeshes)
            {
                long gid = NextId();

                var vd = new double[verts.Count * 3];
                for (int i = 0; i < verts.Count; i++)
                {
                    vd[i * 3] = -verts[i].x;
                    vd[i * 3 + 1] = verts[i].y;
                    vd[i * 3 + 2] = verts[i].z;
                }

                int triCount = 0;
                foreach (var s in subMeshes) triCount += s.Count / 3;

                var pvi = new int[triCount * 3];
                var nd = new double[triCount * 9];
                var ud = new double[triCount * 6];
                var mats = new int[triCount];

                int c = 0, tri = 0;
                for (int s = 0; s < subMeshes.Count; s++)
                {
                    var idx = subMeshes[s];
                    for (int k = 0; k + 2 < idx.Count; k += 3)
                    {
                        int a = idx[k], b = idx[k + 1], d = idx[k + 2];
                        int i0 = a, i1 = d, i2 = b;
                        for (int j = 0; j < 3; j++)
                        {
                            int vi = j == 0 ? i0 : j == 1 ? i1 : i2;
                            pvi[c] = j == 2 ? ~vi : vi;
                            var nv = vi < norms.Count ? norms[vi] : Vector3.up;
                            nd[c * 3] = -nv.x; nd[c * 3 + 1] = nv.y; nd[c * 3 + 2] = nv.z;
                            var uv = vi < uvs.Count ? uvs[vi] : Vector2.zero;
                            ud[c * 2] = uv.x; ud[c * 2 + 1] = uv.y;
                            c++;
                        }
                        mats[tri++] = s;
                    }
                }

                var g = new N("Geometry", new FL(gid), name + SEP + "Geometry", "Mesh");
                g.Add("GeometryVersion", new FI(124));
                g.Add("Vertices", vd);
                g.Add("PolygonVertexIndex", pvi);

                var ln = g.Add("LayerElementNormal", new FI(0));
                ln.Add("Version", new FI(101));
                ln.Add("Name", "");
                ln.Add("MappingInformationType", "ByPolygonVertex");
                ln.Add("ReferenceInformationType", "Direct");
                ln.Add("Normals", nd);

                var lu = g.Add("LayerElementUV", new FI(0));
                lu.Add("Version", new FI(101));
                lu.Add("Name", "UVMap");
                lu.Add("MappingInformationType", "ByPolygonVertex");
                lu.Add("ReferenceInformationType", "Direct");
                lu.Add("UV", ud);

                var lm = g.Add("LayerElementMaterial", new FI(0));
                lm.Add("Version", new FI(101));
                lm.Add("Name", "");
                lm.Add("MappingInformationType", "ByPolygon");
                lm.Add("ReferenceInformationType", "IndexToDirect");
                lm.Add("Materials", mats);

                var layer = g.Add("Layer", new FI(0));
                layer.Add("Version", new FI(100));
                foreach (var t in new[] { "LayerElementNormal", "LayerElementUV", "LayerElementMaterial" })
                {
                    var e = layer.Add("LayerElement");
                    e.Add("Type", t);
                    e.Add("TypedIndex", new FI(0));
                }
                Objects.Add(g);
                return gid;
            }

            public long AddSkin(string name)
            {
                long id = NextId();
                var d = new N("Deformer", new FL(id), name + SEP + "Deformer", "Skin");
                d.Add("Version", new FI(101));
                d.Add("Link_DeformAcceptance", new FD(0.0));
                Objects.Add(d);
                return id;
            }

            public long AddCluster(string name, int[] indexes, double[] weights,
                Matrix4x4 meshBind, Matrix4x4 boneBind)
            {
                long id = NextId();
                var c = new N("Deformer", new FL(id), name + SEP + "SubDeformer", "Cluster");
                c.Add("Version", new FI(100));
                c.Add("UserData", "", "");
                if (indexes.Length > 0)
                {
                    c.Add("Indexes", indexes);
                    c.Add("Weights", weights);
                }
                c.Add("Transform", Flatten(meshBind));
                c.Add("TransformLink", Flatten(boneBind));
                Objects.Add(c);
                return id;
            }

            public void Save(string path)
            {
                var roots = new List<N>();

                var h = new N("FBXHeaderExtension");
                h.Add("FBXHeaderVersion", new FI(1003));
                h.Add("FBXVersion", new FI(7400));
                h.Add("Creator", "BettrFG");
                roots.Add(h);
                roots.Add(new N("Creator", "BettrFG"));

                var gs = new N("GlobalSettings");
                gs.Add("Version", new FI(1000));
                var gp = gs.Add("Properties70");
                gp.Add("P", "UpAxis", "int", "Integer", "", new FI(1));
                gp.Add("P", "UpAxisSign", "int", "Integer", "", new FI(1));
                gp.Add("P", "FrontAxis", "int", "Integer", "", new FI(2));
                gp.Add("P", "FrontAxisSign", "int", "Integer", "", new FI(1));
                gp.Add("P", "CoordAxis", "int", "Integer", "", new FI(0));
                gp.Add("P", "CoordAxisSign", "int", "Integer", "", new FI(1));
                gp.Add("P", "UnitScaleFactor", "double", "Number", "", new FD(100.0));
                gp.Add("P", "OriginalUnitScaleFactor", "double", "Number", "", new FD(100.0));
                roots.Add(gs);

                var defs = new N("Definitions");
                defs.Add("Version", new FI(100));
                defs.Add("Count", new FI(Objects.Count + 1));
                var counts = new Dictionary<string, int>();
                foreach (var o in Objects)
                {
                    counts.TryGetValue(o.Name, out int n);
                    counts[o.Name] = n + 1;
                }
                defs.Add("ObjectType", "GlobalSettings").Add("Count", new FI(1));
                foreach (var kv in counts)
                    defs.Add("ObjectType", kv.Key).Add("Count", new FI(kv.Value));
                roots.Add(defs);

                var objs = new N("Objects");
                objs.Kids.AddRange(Objects);
                roots.Add(objs);

                var conns = new N("Connections");
                foreach (var (kind, child, parent, prop) in Conns)
                {
                    if (prop == null) conns.Add("C", kind, new FL(child), new FL(parent));
                    else conns.Add("C", kind, new FL(child), new FL(parent), prop);
                }
                roots.Add(conns);

                using (var ms = new MemoryStream())
                {
                    using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
                    {
                        w.Write(Encoding.ASCII.GetBytes("Kaydara FBX Binary  "));
                        w.Write(new byte[] { 0x00, 0x1A, 0x00 });
                        w.Write(7400u);
                        foreach (var r in roots) WriteNode(w, r);
                        w.Write(new byte[13]);
                        w.Write(FooterId);
                        w.Write(new byte[(16 - ms.Position % 16) % 16]);
                        w.Write(0u);
                        w.Write(7400u);
                        w.Write(new byte[120]);
                        w.Write(FooterMagic);
                    }
                    File.WriteAllBytes(path, ms.ToArray());
                }
            }
        }

        private static readonly Matrix4x4 MirrorX = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));

        private static Matrix4x4 Mirror(Matrix4x4 m) => MirrorX * m * MirrorX;

        private static double[] Flatten(Matrix4x4 m)
        {
            var a = new double[16];
            for (int col = 0; col < 4; col++)
                for (int row = 0; row < 4; row++)
                    a[col * 4 + row] = m[row, col];
            return a;
        }

        private class MeshData
        {
            public List<Vector3> Verts = new List<Vector3>();
            public List<Vector3> Norms = new List<Vector3>();
            public List<Vector2> UVs = new List<Vector2>();
            public List<List<int>> SubMeshes = new List<List<int>>();
            public List<Matrix4x4> BindPoses = new List<Matrix4x4>();
            public int[] BoneIdx;
            public float[] BoneWeight;
        }

        private static Dictionary<string, MeshData> LoadGeometry(string dir)
        {
            var map = new Dictionary<string, MeshData>();
            string path = Path.Combine(dir, "geometry.bin");
            if (!File.Exists(path)) return map;

            using (var fs = File.OpenRead(path))
            using (var r = new BinaryReader(fs))
            {
                if (new string(r.ReadChars(4)) != "FGGE") return map;
                int meshes = r.ReadInt32();
                for (int m = 0; m < meshes; m++)
                {
                    string name = Encoding.UTF8.GetString(r.ReadBytes(r.ReadUInt16()));
                    int n = r.ReadInt32();
                    byte flags = r.ReadByte();

                    var d = new MeshData();
                    for (int i = 0; i < n; i++)
                        d.Verts.Add(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
                    if ((flags & 1) != 0)
                        for (int i = 0; i < n; i++)
                            d.Norms.Add(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
                    if ((flags & 2) != 0)
                        for (int i = 0; i < n; i++)
                            d.UVs.Add(new Vector2(r.ReadSingle(), r.ReadSingle()));
                    if ((flags & 4) != 0)
                    {
                        d.BoneIdx = new int[n * 4];
                        d.BoneWeight = new float[n * 4];
                        for (int i = 0; i < n * 4; i++) d.BoneIdx[i] = r.ReadInt32();
                        for (int i = 0; i < n * 4; i++) d.BoneWeight[i] = r.ReadSingle();
                    }

                    int binds = r.ReadInt32();
                    for (int i = 0; i < binds; i++)
                    {
                        var bp = new Matrix4x4();
                        for (int row = 0; row < 4; row++)
                            for (int col = 0; col < 4; col++)
                                bp[row, col] = r.ReadSingle();
                        d.BindPoses.Add(bp);
                    }

                    int subs = r.ReadInt32();
                    for (int s = 0; s < subs; s++)
                    {
                        int c = r.ReadInt32();
                        var tris = new List<int>(c);
                        for (int i = 0; i < c; i++) tris.Add(r.ReadInt32());
                        d.SubMeshes.Add(tris);
                    }
                    map[name + "|" + n] = d;
                }
            }
            return map;
        }

        private static Mesh MeshOf(Renderer r)
        {
            var smr = r.TryCast<SkinnedMeshRenderer>();
            if (smr != null) return smr.sharedMesh;
            var mf = r.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }


        public static string Safe(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' ? c : '_');
            return sb.Length == 0 ? "unnamed" : sb.ToString();
        }

        public static string ExportRoot => UnityProject.Configured
            ? UnityProject.AssetsPath
            : Path.Combine(SettingsService.ExportsDir, "Models");

        public static string SkinsDir => Path.Combine(ExportRoot, "Skins");

        public static string PrepareDir(string name)
        {
            string dir = Path.Combine(SkinsDir, Safe(name));
            Directory.CreateDirectory(Path.Combine(dir, "textures"));
            return dir;
        }

        public static void WriteMeshList(GameObject root, string dir)
        {
            var lines = new List<string>();
            var seen = new HashSet<string>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mesh = MeshOf(r);
                if (mesh == null) continue;
                string line = mesh.name + "\t" + mesh.vertexCount;
                if (seen.Add(line)) lines.Add(line);
            }
            File.WriteAllLines(Path.Combine(dir, "meshes.txt"), lines);
            Plugin.Log.LogInfo("asking the bundle reader for " + lines.Count + " mesh(es)");
        }

        public static string Scratch
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BettrFG", "sidecar");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string Progress()
        {
            try { return File.ReadAllText(Path.Combine(Scratch, "progress.txt")).Trim(); }
            catch { return null; }
        }

        private static string _python;

        // the prefab route hands the whole job to the sidecar: it reads the real serialized objects
        // out of the bundles and writes Unity YAML, so nothing has to be rebuilt by hand in-editor
        public static System.Diagnostics.Process StartPrefabExport(string dir, string key, out string error) =>
            Sidecar(dir, new[] { "fgprefab.py", "fgmesh.py", "fgshader.py" }, key, out error);

        public static System.Diagnostics.Process StartMeshFetch(string dir, out string error) =>
            Sidecar(dir, new[] { "fgmesh.py" }, null, out error);

        public static System.Diagnostics.Process StartSceneExport(string dir, string scene, out string error) =>
            Sidecar(dir, new[] { "fgscene.py", "fgprefab.py", "fgmesh.py", "fgshader.py" }, scene, out error);

        private static System.Diagnostics.Process Sidecar(string dir, string[] modules, string key, out string error)
        {
            error = null;
            string scratch = Scratch;
            foreach (string module in modules)
            {
                string src = EmbeddedResourceandUnity.LoadText("BetterFG.assets." + module);
                if (string.IsNullOrEmpty(src)) { error = module + " never made it into the build"; return null; }
                File.WriteAllText(Path.Combine(scratch, module), src);
            }

            string python = FindPython();
            if (python == null) { error = LocalizationService.Get("asset_export.no_python"); return null; }

            string bundles = Path.Combine(Application.dataPath, "StreamingAssets", "aa", "StandaloneWindows64");
            string args = "\"" + Path.Combine(scratch, modules[0]) + "\" \"" + dir + "\" \"" + bundles + "\"";
            if (key != null) args += " \"" + key + "\"";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = python,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.EnvironmentVariables["BETTRFG_PROGRESS"] = Path.Combine(scratch, "progress.txt");
            return System.Diagnostics.Process.Start(psi);
        }

        private static string FindPython()
        {
            if (_python != null) return _python.Length == 0 ? null : _python;

            var candidates = new List<string>();
            if (File.Exists(PythonRuntime.Exe)) candidates.Add(PythonRuntime.Exe);
            string progs = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python");
            if (Directory.Exists(progs))
            {
                var dirs = Directory.GetDirectories(progs, "Python3*");
                Array.Sort(dirs);
                Array.Reverse(dirs);
                foreach (var d in dirs) candidates.Add(Path.Combine(d, "python.exe"));
            }
            candidates.Add("python");

            foreach (var c in candidates)
            {
                try
                {
                    var probe = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = c,
                        Arguments = "-c \"import UnityPy\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                    });
                    probe.StandardError.ReadToEnd();
                    probe.WaitForExit(20000);
                    if (!probe.HasExited || probe.ExitCode != 0) continue;
                }
                catch { continue; }

                _python = c;
                Plugin.Log.LogInfo("bundle reader will run on " + c);
                return c;
            }

            Plugin.Log.LogWarning("no python with UnityPy on this machine, geometry can't be pulled");
            _python = "";
            return null;
        }

        public static string Build(GameObject root, string dir, string name, out string status)
        {
            string texDir = Path.Combine(dir, "textures");
            Directory.CreateDirectory(texDir);
            var geometry = LoadGeometry(dir);

            var scene = new Scene();
            var matIds = new Dictionary<int, long>();
            var texFiles = new Dictionary<int, string>();
            var rootW2L = root.transform.worldToLocalMatrix;

            long MatId(Material mat)
            {
                int key = mat == null ? 0 : mat.GetInstanceID();
                if (matIds.TryGetValue(key, out long known)) return known;
                if (mat == null)
                {
                    matIds[key] = scene.AddMaterial("NoMaterial", Color.white, null, null);
                    return matIds[key];
                }

                string diffuse = null;
                foreach (var prop in SkinApplicationService.GetTextureProps(mat))
                {
                    var tex = mat.GetTexture(prop);
                    if (tex == null) continue;
                    int tk = tex.GetInstanceID();
                    if (!texFiles.TryGetValue(tk, out string file))
                    {
                        file = Safe(string.IsNullOrEmpty(tex.name) ? prop : tex.name) + ".png";
                        if (!SkinApplicationService.SaveTexturePng(tex, Path.Combine(texDir, file), out string terr))
                        {
                            Plugin.Log.LogWarning("texture " + tex.name + " didn't come out: " + terr);
                            file = null;
                        }
                        texFiles[tk] = file;
                    }
                    if (file != null && (diffuse == null ||
                        prop == "_BaseMap" || prop == "_MainTex" || prop == "_BaseColorMap"))
                        diffuse = file;
                }

                Color col = Color.white;
                if (mat.HasProperty("_BaseColor")) col = mat.GetColor("_BaseColor");
                else if (mat.HasProperty("_Color")) col = mat.GetColor("_Color");

                long id = scene.AddMaterial(Safe(mat.name), col,
                    diffuse == null ? null : Path.GetFullPath(Path.Combine(texDir, diffuse)),
                    diffuse == null ? null : "textures/" + diffuse);
                matIds[key] = id;
                return id;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);

            var boneSet = new List<Transform>();
            var seenBones = new HashSet<int>();
            foreach (var r in renderers)
            {
                var smr = r.TryCast<SkinnedMeshRenderer>();
                if (smr == null) continue;
                var bones = smr.bones;
                if (bones == null) continue;
                for (int i = 0; i < bones.Length; i++)
                {
                    var t = bones[i];
                    while (t != null && t != root.transform && seenBones.Add(t.GetInstanceID()))
                    {
                        boneSet.Add(t);
                        t = t.parent;
                    }
                }
            }

            for (int i = 0; i < boneSet.Count; i++)
            {
                var t = boneSet[i];
                for (int c = 0; c < t.childCount; c++)
                {
                    var child = t.GetChild(c);
                    if (child.GetComponentInChildren<Renderer>(true) != null) continue;
                    if (seenBones.Add(child.GetInstanceID())) boneSet.Add(child);
                }
            }

            var bindGlobal = new Dictionary<int, Matrix4x4>();
            foreach (var r in renderers)
            {
                var smr = r.TryCast<SkinnedMeshRenderer>();
                var mesh = smr != null ? smr.sharedMesh : null;
                if (mesh == null) continue;
                if (!geometry.TryGetValue(mesh.name + "|" + mesh.vertexCount, out var d)) continue;
                var bones = smr.bones;
                for (int b = 0; b < bones.Length && b < d.BindPoses.Count; b++)
                {
                    if (bones[b] == null) continue;
                    int id = bones[b].GetInstanceID();
                    if (!bindGlobal.ContainsKey(id)) bindGlobal[id] = d.BindPoses[b].inverse;
                }
            }

            boneSet.Sort((a, b) => Depth(a, root.transform).CompareTo(Depth(b, root.transform)));

            for (int i = boneSet.Count - 1; i >= 0; i--)
            {
                var t = boneSet[i];
                var p = t.parent;
                if (p == null || bindGlobal.ContainsKey(p.GetInstanceID())) continue;
                if (!bindGlobal.TryGetValue(t.GetInstanceID(), out var mine)) continue;
                if (!seenBones.Contains(p.GetInstanceID())) continue;
                bindGlobal[p.GetInstanceID()] =
                    mine * Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale).inverse;
            }

            foreach (var t in boneSet)
            {
                if (bindGlobal.ContainsKey(t.GetInstanceID())) continue;
                var p = t.parent;
                if (p == null || !bindGlobal.TryGetValue(p.GetInstanceID(), out var pg)) continue;
                bindGlobal[t.GetInstanceID()] =
                    pg * Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
            }

            Matrix4x4 BindOf(Transform t)
            {
                if (bindGlobal.TryGetValue(t.GetInstanceID(), out var g)) return g;
                return rootW2L * t.localToWorldMatrix;
            }

            long armatureId = 0;
            var boneIds = new Dictionary<int, long>();
            if (boneSet.Count > 0)
            {
                armatureId = scene.AddModel("Armature", "Null", Matrix4x4.identity);
                scene.Connect(armatureId, 0);
                foreach (var t in boneSet)
                {
                    long parentId = armatureId;
                    bool rooted = t.parent == null || !boneIds.TryGetValue(t.parent.GetInstanceID(), out parentId);
                    var local = rooted ? BindOf(t) : BindOf(t.parent).inverse * BindOf(t);
                    long id = scene.AddModel(Safe(t.name), "LimbNode", Mirror(local));
                    boneIds[t.GetInstanceID()] = id;
                    scene.Connect(id, rooted ? armatureId : parentId);
                }
            }

            int built = 0, skipped = 0;
            bool restBonesDone = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                var mesh = MeshOf(r);
                if (mesh == null) continue;

                var smr = r.TryCast<SkinnedMeshRenderer>();
                if (!geometry.TryGetValue(mesh.name + "|" + mesh.vertexCount, out var data))
                {
                    skipped++;
                    Plugin.Log.LogWarning("bundles had nothing for " + mesh.name + " (" + mesh.vertexCount + " verts)");
                    continue;
                }

                var local = rootW2L * r.transform.localToWorldMatrix;
                var verts = data.Verts;
                var norms = data.Norms;
                if (smr == null)
                {
                    verts = new List<Vector3>(data.Verts.Count);
                    norms = new List<Vector3>(data.Norms.Count);
                    foreach (var v in data.Verts) verts.Add(local.MultiplyPoint3x4(v));
                    foreach (var nv in data.Norms) norms.Add(local.MultiplyVector(nv).normalized);
                }
                local = Matrix4x4.identity;

                string label = Safe(r.gameObject.name) + "_" + i;
                long gid = scene.AddGeometry(label, verts, norms, data.UVs, data.SubMeshes);
                long mid = scene.AddModel(label, "Mesh", Mirror(local));
                scene.Connect(gid, mid);
                scene.Connect(mid, 0);

                var mats = r.sharedMaterials;
                for (int s = 0; s < data.SubMeshes.Count; s++)
                    scene.Connect(MatId(mats != null && s < mats.Length ? mats[s] : null), mid);

                if (smr != null && data.BoneIdx != null)
                {
                    var bones = smr.bones;
                    var idx = new List<int>[bones.Length];
                    var wts = new List<double>[bones.Length];
                    for (int v = 0; v < data.Verts.Count; v++)
                    {
                        for (int l = 0; l < 4; l++)
                        {
                            float wgt = data.BoneWeight[v * 4 + l];
                            int b = data.BoneIdx[v * 4 + l];
                            if (wgt <= 0f || b < 0 || b >= bones.Length) continue;
                            if (idx[b] == null) { idx[b] = new List<int>(); wts[b] = new List<double>(); }
                            idx[b].Add(v);
                            wts[b].Add(wgt);
                        }
                    }

                    long skinId = scene.AddSkin(label);
                    scene.Connect(skinId, gid);
                    var clustered = new HashSet<int>();
                    for (int b = 0; b < bones.Length; b++)
                    {
                        var bone = bones[b];
                        if (bone == null) continue;
                        if (!boneIds.TryGetValue(bone.GetInstanceID(), out long boneId)) continue;

                        var bindpose = b < data.BindPoses.Count ? data.BindPoses[b] : BindOf(bone).inverse;
                        long cid = scene.AddCluster(Safe(bone.name) + "_" + label,
                            idx[b] == null ? Array.Empty<int>() : idx[b].ToArray(),
                            wts[b] == null ? Array.Empty<double>() : wts[b].ToArray(),
                            Mirror(bindpose), Mirror(bindpose.inverse));
                        scene.Connect(cid, skinId);
                        scene.Connect(boneId, cid);
                        clustered.Add(bone.GetInstanceID());
                    }

                    if (!restBonesDone)
                    {
                        restBonesDone = true;
                        foreach (var t in boneSet)
                        {
                            int bid = t.GetInstanceID();
                            if (clustered.Contains(bid)) continue;
                            var g = BindOf(t);
                            long cid = scene.AddCluster(Safe(t.name) + "_" + label,
                                Array.Empty<int>(), Array.Empty<double>(),
                                Mirror(g.inverse), Mirror(g));
                            scene.Connect(cid, skinId);
                            scene.Connect(boneIds[bid], cid);
                        }
                    }
                }

                built++;
            }

            if (built == 0)
            {
                status = LocalizationService.Get("asset_export.nothing_readable");
                return null;
            }

            string outFile = Path.Combine(dir, Safe(name) + ".fbx");
            scene.Save(outFile);
            Plugin.Log.LogInfo($"{name}: {built} mesh(es), {boneSet.Count} bones, {texFiles.Count} textures -> {outFile}"
                + (skipped > 0 ? $" ({skipped} skipped)" : ""));
            status = LocalizationService.Format("asset_export.done", built, boneSet.Count, texFiles.Count);
            return outFile;
        }

        private static int Depth(Transform t, Transform root)
        {
            int d = 0;
            while (t != null && t != root) { d++; t = t.parent; }
            return d;
        }
    }
}
