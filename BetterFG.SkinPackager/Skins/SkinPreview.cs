using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using BetterFG.Editor.Reference;
using UnityEditor;
using UnityEngine;

namespace BetterFG.Editor
{
    public class SkinPreview
    {
        private static readonly Color BG = new Color(0.13f, 0.135f, 0.15f);
        private const float MIN_CLIP_SECS = 3f;

        private class Clip
        {
            public string Name;
            public float Length;
            public int Frames;
            public readonly Dictionary<string, float[]> Bones = new Dictionary<string, float[]>();
        }

        private static List<Clip> _clips;

        private PreviewRenderUtility _util;
        private GameObject _instance;
        private GameObject _baseInstance;
        private GameObject _dummyInstance;
        private GameObject _source;
        private bool _showBase;
        private bool _showDummy;
        private Bounds _bounds;
        private Bounds _dummyBounds;
        private class Bone
        {
            public Transform T;
            public Vector3 P;
            public Quaternion R;
            public Vector3 S;
            public float K;
        }

        private class Pair
        {
            public Transform Custom;
            public Transform Driver;
            public Transform CustomParent;
            public Transform DriverParent;
            public Vector3 CustomRest;
            public Vector3 DriverRest;
            public bool Track;
        }

        private readonly List<Pair> _pairs = new List<Pair>();
        private readonly Dictionary<string, Vector3> _offsets = new Dictionary<string, Vector3>();
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private Dictionary<string, Bone> _driverRig;
        private Dictionary<string, Bone> _dummyRig;
        private double _clock;
        private double _last;

        public int Selected = -1;
        public bool Paused;

        public Vector2 Orbit = new Vector2(150f, 8f);
        public float Zoom = 1f;

        public void SetSource(GameObject source)
        {
            if (ReferenceEquals(_source, source) && _instance != null) return;
            _source = source;
            Rebuild();
        }

        public void SetShowBase(bool show)
        {
            if (_showBase == show) return;
            _showBase = show;
            Rebuild();
        }

        public void SetShowDummy(bool show)
        {
            if (_showDummy == show) return;
            _showDummy = show;
            Rebuild();
        }

        public void Rebuild()
        {
            if (_instance != null) { Object.DestroyImmediate(_instance); _instance = null; }
            if (_baseInstance != null) { Object.DestroyImmediate(_baseInstance); _baseInstance = null; }
            if (_dummyInstance != null) { Object.DestroyImmediate(_dummyInstance); _dummyInstance = null; }
            foreach (var m in _meshes) Object.DestroyImmediate(m);
            _meshes.Clear();
            _pairs.Clear();
            _offsets.Clear();
            _driverRig = null;
            _dummyRig = null;
            if (_source == null) return;

            _util = _util ?? new PreviewRenderUtility();
            _clock = 0;
            _last = EditorApplication.timeSinceStartup;

            _instance = Spawn(_source, out _);
            _bounds = Measure(_instance);

            var bean = ReferenceSpawner.AnimationBean(Rebuild);
            if (bean == null) return;

            _baseInstance = Spawn(bean, out _driverRig);
            if (_showBase) _bounds.Encapsulate(Measure(_baseInstance));
            else foreach (var r in _baseInstance.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            BindSkin();

            if (_showDummy)
            {
                _dummyInstance = Spawn(bean, out _dummyRig);
                _dummyBounds = Measure(_dummyInstance);
            }
        }

        private void BindSkin()
        {
            var beanSmrs = _baseInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var bind = new Dictionary<string, Matrix4x4>();
            var parentOf = new Dictionary<string, string>();
            var perRenderer = new List<Dictionary<string, Matrix4x4>>();
            foreach (var smr in beanSmrs)
            {
                var bp = smr.sharedMesh.bindposes;
                var mine = new Dictionary<string, Matrix4x4>();
                for (int i = 0; i < smr.bones.Length; i++)
                {
                    var b = smr.bones[i];
                    mine[b.name] = bp[i];
                    if (!bind.ContainsKey(b.name))
                    {
                        bind[b.name] = bp[i];
                        if (b.parent != null) parentOf[b.name] = b.parent.name;
                    }
                    string lower = b.name.ToLowerInvariant();
                    if (!bind.ContainsKey(lower)) bind[lower] = bp[i];
                }
                perRenderer.Add(mine);
            }

            var driverBones = new Dictionary<string, Transform>();
            foreach (var kv in _driverRig)
            {
                driverBones[kv.Key] = kv.Value.T;
                string lower = kv.Key.ToLowerInvariant();
                if (!driverBones.ContainsKey(lower)) driverBones[lower] = kv.Value.T;
            }
            var droot = driverBones.TryGetValue("Root", out var rt) ? rt : beanSmrs[0].rootBone;
            if (!driverBones.ContainsKey("Body_LOD0 (merge)")) driverBones["Body_LOD0 (merge)"] = droot;

            var skin = _instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Transform chest = null;
            var offsets = new Dictionary<string, Vector3>();
            var seen = new HashSet<string>();
            foreach (var smr in skin)
                foreach (var b in smr.bones)
                {
                    if (b == null) continue;
                    if (b.name == "Chest_C_jnt") chest = b;
                    if (!seen.Add(b.name) || !parentOf.TryGetValue(b.name, out var parentName)) continue;
                    foreach (var dict in perRenderer)
                        if (dict.TryGetValue(b.name, out var bp) && dict.TryGetValue(parentName, out var parentBp))
                        {
                            var rest = parentBp * bp.inverse;
                            if ((b.localPosition - (Vector3)rest.GetColumn(3)).sqrMagnitude >= 0.000001f) offsets[b.name] = b.localPosition;
                            break;
                        }
                }

            int bound = 0, custom = 0;
            if (chest == null || chest.localPosition.magnitude >= 0.05f)
                foreach (var smr in skin)
                {
                    bool hasCustom = false;
                    foreach (var b in smr.bones)
                        if (b != null && !bind.ContainsKey(b.name) && !bind.ContainsKey(b.name.ToLowerInvariant())) { hasCustom = true; break; }
                    if (hasCustom) { custom++; continue; }

                    var newBind = new Matrix4x4[smr.bones.Length];
                    var newBones = new Transform[smr.bones.Length];
                    bool all = true;
                    for (int i = 0; i < newBind.Length && all; i++)
                    {
                        var b = smr.bones[i];
                        all = b != null && (bind.TryGetValue(b.name, out newBind[i]) || bind.TryGetValue(b.name.ToLowerInvariant(), out newBind[i]));
                        if (all) newBones[i] = driverBones.TryGetValue(b.name, out var db) ? db : driverBones[b.name.ToLowerInvariant()];
                    }
                    if (all) { var mesh = Rebased(smr.sharedMesh, newBind); _meshes.Add(mesh); smr.sharedMesh = mesh; }
                    smr.bones = newBones;
                    smr.rootBone = droot;
                    bound++;
                }

            if (bound > 0 && custom == 0)
            {
                foreach (var kv in offsets) _offsets[kv.Key] = kv.Value;
                return;
            }

            Transform root = null;
            foreach (var path in new[] { "Main FG/Body_LOD0 (merge)", "Body_LOD0 (merge)", "Main FG/Body_LOD0", "Body_LOD0" })
                if ((root = _instance.transform.Find(path)) != null) break;
            if (root == null) return;

            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                if ((driverBones.TryGetValue(c.name, out var d) || driverBones.TryGetValue(c.name.ToLowerInvariant(), out d)))
                {
                    bool track = c == root || c.parent == root;
                    if (track || (c.parent != null && d.parent != null))
                        _pairs.Add(new Pair { Custom = c, Driver = d, CustomParent = c.parent, DriverParent = d.parent, CustomRest = c.localPosition, DriverRest = d.localPosition, Track = track });
                }
                for (int i = 0; i < c.childCount; i++) stack.Push(c.GetChild(i));
            }
        }

        private static Mesh Rebased(Mesh src, Matrix4x4[] newBind)
        {
            var mesh = Object.Instantiate(src);
            mesh.hideFlags = HideFlags.HideAndDontSave;
            var oldBind = src.bindposes;
            var weights = src.boneWeights;
            if (oldBind.Length == newBind.Length && weights.Length == src.vertexCount)
            {
                var m = new Matrix4x4[newBind.Length];
                for (int i = 0; i < m.Length; i++) m[i] = newBind[i].inverse * oldBind[i];
                var verts = src.vertices;
                var normals = src.normals;
                bool hasN = normals.Length == verts.Length;
                var ids = new int[4];
                var ws = new float[4];
                for (int v = 0; v < verts.Length; v++)
                {
                    var bw = weights[v];
                    ids[0] = bw.boneIndex0; ws[0] = bw.weight0;
                    ids[1] = bw.boneIndex1; ws[1] = bw.weight1;
                    ids[2] = bw.boneIndex2; ws[2] = bw.weight2;
                    ids[3] = bw.boneIndex3; ws[3] = bw.weight3;
                    Vector3 p = Vector3.zero, n = Vector3.zero;
                    for (int k = 0; k < 4; k++)
                    {
                        if (ws[k] <= 0f || ids[k] >= m.Length) continue;
                        p += m[ids[k]].MultiplyPoint3x4(verts[v]) * ws[k];
                        if (hasN) n += m[ids[k]].MultiplyVector(normals[v]) * ws[k];
                    }
                    verts[v] = p;
                    if (hasN && n.sqrMagnitude > 0.000001f) normals[v] = n.normalized;
                }
                mesh.vertices = verts;
                if (hasN) mesh.normals = normals;
            }
            mesh.bindposes = newBind;
            mesh.RecalculateBounds();
            return mesh;
        }

        private void Drive(Clip clip, float t)
        {
            if (_driverRig == null) return;
            Pose(_driverRig, clip, t);
            foreach (var kv in _offsets) _driverRig[kv.Key].T.localPosition = kv.Value;
            foreach (var p in _pairs)
            {
                var pos = p.Driver.position;
                if (!p.Track) pos = p.CustomParent.TransformPoint(p.CustomRest) + (pos - p.DriverParent.TransformPoint(p.DriverRest));
                p.Custom.SetPositionAndRotation(pos, p.Driver.rotation);
            }
        }

        private GameObject Spawn(GameObject src, out Dictionary<string, Bone> rig)
        {
            var go = Object.Instantiate(src);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.forceMatrixRecalculationPerRender = true;
                smr.updateWhenOffscreen = true;
            }
            foreach (var a in go.GetComponentsInChildren<Animator>(true)) a.enabled = false;

            rig = new Dictionary<string, Bone>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t != go.transform && !rig.ContainsKey(t.name))
                    rig[t.name] = new Bone { T = t, P = t.localPosition, R = t.localRotation, S = t.localScale, K = t.parent != null ? t.parent.lossyScale.y : 1f };

            _util.AddSingleGO(go);
            return go;
        }

        private static Bounds Measure(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        private static List<Clip> Clips()
        {
            if (_clips != null) return _clips;
            _clips = new List<Clip>();
            using (var res = typeof(SkinPreview).Assembly.GetManifestResourceStream("BetterFG.Creator.Reference.Files.fganims.dat"))
            using (var r = new BinaryReader(new GZipStream(res, CompressionMode.Decompress)))
            {
                int clipCount = r.ReadInt32();
                for (int c = 0; c < clipCount; c++)
                {
                    var clip = new Clip { Name = r.ReadString(), Length = r.ReadSingle(), Frames = r.ReadInt32() };
                    int boneCount = r.ReadInt32();
                    for (int b = 0; b < boneCount; b++)
                    {
                        string bone = r.ReadString();
                        var d = new float[clip.Frames * 10];
                        for (int i = 0; i < d.Length; i++) d[i] = r.ReadSingle();
                        clip.Bones[bone] = d;
                    }
                    _clips.Add(clip);
                }
            }
            return _clips;
        }

        private static void Pose(Dictionary<string, Bone> rig, Clip clip, float time)
        {
            foreach (var b in rig.Values) { b.T.localPosition = b.P; b.T.localRotation = b.R; b.T.localScale = b.S; }
            int o = Mathf.Min(clip.Frames - 1, Mathf.RoundToInt(time % clip.Length / clip.Length * (clip.Frames - 1))) * 10;
            foreach (var kv in clip.Bones)
            {
                if (!rig.TryGetValue(kv.Key, out var b)) continue;
                var d = kv.Value;
                b.T.localPosition = b.P + b.R * new Vector3(d[o], d[o + 1], d[o + 2]) / b.K;
                b.T.localRotation = b.R * new Quaternion(d[o + 3], d[o + 4], d[o + 5], d[o + 6]);
                b.T.localScale = Vector3.Scale(b.S, new Vector3(d[o + 7], d[o + 8], d[o + 9]));
            }
        }

        public static List<string> ClipNames() => Clips().ConvertAll(c => System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(c.Name, "^FG_|_A$", ""), "_|(?<=[a-z])(?=[A-Z])", " "));

        private void Animate()
        {
            var clips = Clips();
            double real = EditorApplication.timeSinceStartup;
            if (!Paused) _clock += real - _last;
            _last = real;
            float now = (float)_clock;

            Clip clip;
            float t;
            if (Selected >= 0 && Selected < clips.Count)
            {
                clip = clips[Selected];
                t = now;
            }
            else
            {
                float cycle = 0f;
                foreach (var c in clips) cycle += Mathf.Max(c.Length, MIN_CLIP_SECS);
                t = now % cycle;
                clip = clips[0];
                foreach (var c in clips)
                {
                    float span = Mathf.Max(c.Length, MIN_CLIP_SECS);
                    if (t < span) { clip = c; break; }
                    t -= span;
                }
            }

            Drive(clip, t);
            if (_dummyRig != null) Pose(_dummyRig, clips[0], now);
        }

        public Texture2D RenderStill(int w, int h)
        {
            var orbit = Orbit;
            float zoom = Zoom;
            Orbit = new Vector2(160f, 6f);
            Zoom = 1.15f;
            var rt = (RenderTexture)Render(new Rect(0f, 0f, w, h), 1f, true);
            Orbit = orbit;
            Zoom = zoom;
            if (rt == null) return null;

            var tmp = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(rt, tmp);
            var prev = RenderTexture.active;
            RenderTexture.active = tmp;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(tmp);
            return tex;
        }

        public Texture Render(Rect rect, float scale, bool still = false)
        {
            if (_source == null || _instance == null || rect.width < 2f || rect.height < 2f) return null;

            if (scale <= 0f) scale = 1f;
            _instance.transform.localScale = Vector3.one * scale;
            if (_baseInstance != null) _baseInstance.transform.localScale = Vector3.one * scale;
            if (still) Drive(Clips()[0], 0f);
            else Animate();

            var frame = _bounds;
            if (_dummyInstance != null)
            {
                float x = _bounds.min.x - _dummyBounds.max.x - 0.15f;
                frame.Encapsulate(new Bounds(_dummyBounds.center + Vector3.right * x, _dummyBounds.size));
                _dummyInstance.transform.position = new Vector3(Mathf.Min(x, _bounds.min.x * scale - _dummyBounds.max.x - 0.15f), 0f, 0f);
            }

            _util.BeginPreview(rect, GUIStyle.none);

            var cam = _util.camera;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BG;
            cam.fieldOfView = 32f;

            float radius = Mathf.Max(frame.extents.magnitude, 0.1f);
            float dist = radius * 3.1f / Mathf.Max(Zoom, 0.05f);
            var pivot = frame.center;
            var rot = Quaternion.Euler(Orbit.y, Orbit.x, 0f);

            cam.transform.rotation = rot;
            cam.transform.position = pivot + rot * new Vector3(0f, 0f, -dist);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = dist + radius * Mathf.Max(scale, 1f) * 6f + 1f;

            _util.ambientColor = new Color(0.24f, 0.25f, 0.27f);
            _util.lights[0].intensity = 0.8f;
            _util.lights[0].transform.rotation = Quaternion.Euler(38f, 40f, 0f);
            _util.lights[0].color = Color.white;
            _util.lights[1].intensity = 0.35f;
            _util.lights[1].transform.rotation = Quaternion.Euler(12f, -120f, 0f);
            _util.lights[1].color = new Color(0.75f, 0.8f, 1f);

            cam.Render();
            return _util.EndPreview();
        }

        public void OrbitBy(Vector2 delta)
        {
            Orbit.x += delta.x * 0.6f;
            Orbit.y = Mathf.Clamp(Orbit.y - delta.y * 0.4f, -85f, 85f);
        }

        public void ZoomBy(float lines) => Zoom = Mathf.Clamp(Zoom * (1f - lines * 0.04f), 0.2f, 6f);

        public void Cleanup()
        {
            _source = null;
            Rebuild();
            if (_util != null) { _util.Cleanup(); _util = null; }
        }
    }
}
