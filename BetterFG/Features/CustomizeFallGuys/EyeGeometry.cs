using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BetterFG.Features.CustomizeFallGuys
{
    internal static class EyeGeometry
    {
        const float EyeWeight = 0.5f;
        const float SurfaceLift = 0.0003f;

        // keyed on mesh SHAPE, not instance id. every bean in a round carries its own Body_LOD0(Clone)
        // — the meshes are cloned per character — so an instance-id key never hit and each of the 32-odd
        // beans paid a full Extract (five big il2cpp arrays, 2360 verts / 11856 indices marshalled out)
        // and ended up with its own unique eye mesh, which also made the renderers unbatchable.
        sealed class Carved
        {
            public Mesh mesh;
            // source-skeleton bone index for each slot of the carved mesh's own compact palette
            public int[] palette;
        }

        static readonly Dictionary<string, Carved> _carved = new Dictionary<string, Carved>();

        public static GameObject Attach(GameObject root, Material shared, Color tint, out SkinnedMeshRenderer source)
        {
            source = null;
            // was the customisation handler's SkinnedMeshes list, but the preview clone deliberately
            // has no handler on it. a plain child sweep is a superset and costs nothing extra: only a
            // mesh that actually carries eye-weighted tris survives Carve anyway
            var meshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < meshes.Length; i++)
            {
                var body = meshes[i];
                if (body == null || body.sharedMesh == null) continue;
                if (body.gameObject.name == "BettrFG_Eyes") continue;
                if (pass == 0 && !body.gameObject.activeInHierarchy) continue;
                bool customRig = false;
                for (var up = body.transform; up != null && !customRig; up = up.parent)
                    customRig = up.name.StartsWith("BettrFG_CustomRig");
                if (customRig) continue;

                var bodyBones = body.bones;
                int el = -1, er = -1;
                for (int b2 = 0; b2 < bodyBones.Length; b2++)
                {
                    var bone = bodyBones[b2];
                    if (bone == null) continue;
                    if (bone.name == "Eye_L_jnt") el = b2;
                    else if (bone.name == "Eye_R_jnt") er = b2;
                }
                if (el < 0 || er < 0) continue;

                var carved = Carve(body.sharedMesh, el, er);
                if (carved == null) continue;

                var go = new GameObject("BettrFG_Eyes");
                go.layer = body.gameObject.layer;
                go.transform.SetParent(root.transform, false);

                // source weights stay exactly as authored — the carved verts sit on the BOUNDARY of the
                // eye patch and plenty are only ~0.6 weighted to an eye joint, so flattening them onto
                // two bones at full weight deforms them. tried 2026-08-21, reverted. only the bone LIST
                // is trimmed to the joints those weights actually name: unity rebuilds a skinning matrix
                // for every entry of smr.bones every frame per bean, and the body's full ~60-bone array
                // made a dozen-vertex eye patch cost as much to skin as a whole bean.
                var palette = carved.palette;
                var eyeBones = new Il2CppReferenceArray<Transform>(palette.Length);
                for (int b3 = 0; b3 < palette.Length; b3++) eyeBones[b3] = bodyBones[palette[b3]];

                var smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = carved.mesh;
                smr.bones = eyeBones;
                smr.rootBone = body.rootBone;
                smr.skinnedMotionVectors = false;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var tinted = TintedMaterial(shared, tint);
                smr.sharedMaterial = tinted;
                Reassert(smr, body, tinted);
                source = body;
                return go;
            }

            return null;
        }

        public static void Reassert(SkinnedMeshRenderer eyes, SkinnedMeshRenderer body, Material want)
        {
            if (eyes is null || eyes.m_CachedPtr == IntPtr.Zero || want is null) return;

            var ghost = BetterFG.Core.AssetManager.PeekGhostMaterial();
            var bodyMat = body is null || body.m_CachedPtr == IntPtr.Zero ? null : body.sharedMaterial;
            var target = ghost is not null && bodyMat is not null && bodyMat.m_CachedPtr == ghost.m_CachedPtr
                ? ghost
                : want;
            if (target.shader is null) return;

            var cur = eyes.sharedMaterial;
            if (cur is not null && cur.shader is not null && cur.shader.m_CachedPtr == target.shader.m_CachedPtr) return;

            eyes.sharedMaterial = target;
            Plugin.Log.LogInfo($"eye overlay was wearing '{(cur is null ? "(none)" : cur.name)}', put ours back");
        }

        public static void SetTint(SkinnedMeshRenderer smr, Color tint)
        {
            if (smr == null) return;
            var shared = smr.sharedMaterial;
            if (shared == null) return;
            smr.sharedMaterial = TintedMaterial(_sourceOf.TryGetValue(shared.GetInstanceID(), out var src) ? src : shared, tint);
        }

        // a MaterialPropertyBlock is per-renderer, so every bean's eyes were their own draw call even
        // though there are only ever a handful of distinct faceplate eye colours in a lobby. one shared
        // material per colour instead: beans that picked the same eyes now batch together, and a tint
        // change is a material swap rather than a block rebuild.
        static readonly Dictionary<long, Material> _tinted = new Dictionary<long, Material>();
        static readonly Dictionary<int, Material> _sourceOf = new Dictionary<int, Material>();

        static Material TintedMaterial(Material shared, Color tint)
        {
            var c = (Color32)tint;
            long key = ((long)shared.GetInstanceID() << 32) | (uint)((c.r << 24) | (c.g << 16) | (c.b << 8) | c.a);
            if (_tinted.TryGetValue(key, out var hit) && hit != null) return hit;

            var mat = new Material(shared) { hideFlags = HideFlags.HideAndDontSave };
            mat.SetColor("_MultiplyColor", tint);
            mat.renderQueue = shared.renderQueue + 1;
            _tinted[key] = mat;
            _sourceOf[mat.GetInstanceID()] = shared;
            return mat;
        }

        public static void Detach(GameObject spawned)
        {
            if (spawned == null) return;
            UnityEngine.Object.Destroy(spawned);
        }

        // the carved mesh comes out bean-independent — weights collapsed to a two-bone palette at
        // indices 0/1 — so every bean that shares a body shape shares one mesh. el/er are resolved per
        // bean by the caller against its own bone array, so a differently ordered skeleton still binds
        // to the right joints.
        static Carved Carve(Mesh src, int el, int er)
        {
            if (src == null) return null;
            string name = src.name ?? "";
            int clone = name.IndexOf("(Clone)", StringComparison.Ordinal);
            if (clone >= 0) name = name.Substring(0, clone);
            // el/er are part of the key on purpose: the carved mesh keeps the SOURCE bone weights, so its
            // indices are only meaningful against a skeleton ordered the same way. a bean whose eye joints
            // sit at different indices gets its own carve instead of silently inheriting deformed eyes.
            string key = name + "|" + src.vertexCount + "|" + src.subMeshCount + "|" + el + "/" + er;

            if (_carved.TryGetValue(key, out var cached)) return cached;

            Carved built = src.isReadable ? Extract(src, el, er) : null;
            _carved[key] = built;

            if (built != null)
                Plugin.Log.LogInfo($"carved eyes off {name}: {built.mesh.vertexCount} verts, {built.mesh.triangles.Length / 3} tris, {built.palette.Length}/{src.bindposes.Length} bones (eyes {el}/{er}) — shared from here on");

            return built;
        }

        static Carved Extract(Mesh src, int el, int er)
        {
            var weights = src.boneWeights;
            var verts = src.vertices;
            var norms = src.normals;
            var uvs = src.uv;
            var tris = src.triangles;

            var isEye = new bool[verts.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                var w = weights[i];
                float total = 0f;
                if (w.boneIndex0 == el || w.boneIndex0 == er) total += w.weight0;
                if (w.boneIndex1 == el || w.boneIndex1 == er) total += w.weight1;
                if (w.boneIndex2 == el || w.boneIndex2 == er) total += w.weight2;
                if (w.boneIndex3 == el || w.boneIndex3 == er) total += w.weight3;
                isEye[i] = total > EyeWeight;
            }

            var remap = new int[verts.Length];
            for (int i = 0; i < remap.Length; i++) remap[i] = -1;

            var outVerts = new List<Vector3>();
            var outNorms = new List<Vector3>();
            var outUvs = new List<Vector2>();
            var outWeights = new List<BoneWeight>();
            var outTris = new List<int>();

            bool hasNorms = norms != null && norms.Length == verts.Length;
            bool hasUvs = uvs != null && uvs.Length == verts.Length;

            for (int t = 0; t < tris.Length; t += 3)
            {
                if (!isEye[tris[t]] || !isEye[tris[t + 1]] || !isEye[tris[t + 2]]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int v = tris[t + k];
                    if (remap[v] < 0)
                    {
                        remap[v] = outVerts.Count;
                        var n = hasNorms ? norms[v] : Vector3.zero;
                        outVerts.Add(verts[v] + n * SurfaceLift);
                        outNorms.Add(n);
                        outUvs.Add(hasUvs ? uvs[v] : Vector2.zero);
                        outWeights.Add(weights[v]);
                    }
                    outTris.Add(remap[v]);
                }
            }

            if (outTris.Count == 0) return null;

            var srcBindposes = src.bindposes;
            var palette = new List<int>();
            var toCompact = new Dictionary<int, int>();
            var packed = new BoneWeight[outWeights.Count];
            for (int i = 0; i < outWeights.Count; i++)
            {
                var w = outWeights[i];
                w.boneIndex0 = Compact(w.boneIndex0, w.weight0, palette, toCompact);
                w.boneIndex1 = Compact(w.boneIndex1, w.weight1, palette, toCompact);
                w.boneIndex2 = Compact(w.boneIndex2, w.weight2, palette, toCompact);
                w.boneIndex3 = Compact(w.boneIndex3, w.weight3, palette, toCompact);
                packed[i] = w;
            }
            if (palette.Count == 0) palette.Add(el);

            var binds = new Matrix4x4[palette.Count];
            for (int i = 0; i < palette.Count; i++) binds[i] = srcBindposes[palette[i]];

            var mesh = new Mesh { name = src.name + "_BettrFGEyes", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(outVerts.ToArray());
            if (hasNorms) mesh.SetNormals(outNorms.ToArray());
            if (hasUvs) mesh.SetUVs(0, outUvs.ToArray());
            mesh.SetTriangles(outTris.ToArray(), 0);
            mesh.boneWeights = packed;
            mesh.bindposes = binds;
            mesh.RecalculateBounds();
            return new Carved { mesh = mesh, palette = palette.ToArray() };
        }

        static int Compact(int srcBone, float weight, List<int> palette, Dictionary<int, int> toCompact)
        {
            if (weight <= 0f) return 0;
            if (toCompact.TryGetValue(srcBone, out int slot)) return slot;
            slot = palette.Count;
            palette.Add(srcBone);
            toCompact[srcBone] = slot;
            return slot;
        }
    }
}
