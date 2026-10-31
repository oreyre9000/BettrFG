using System;
using System.Collections.Generic;
using BetterFG.Customization.Player;
using UnityEngine;

namespace BetterFG.Customization.SkinCreator
{
    internal static class SkinRig
    {
        public static GameObject Build(ImportedModel m, SkinnedMeshRenderer body, int[] map, GameObject bean, Material[] mats, int layer, out Mesh mesh)
        {
            int n = m.BoneNames.Length;
            var beanBones = body.bones;
            var bp = body.sharedMesh.bindposes;
            var restPos = new Vector3[bp.Length];
            var restRot = new Quaternion[bp.Length];
            for (int i = 0; i < bp.Length; i++)
            {
                var inv = bp[i].inverse;
                restPos[i] = inv.GetColumn(3);
                restRot[i] = Quaternion.LookRotation(inv.GetColumn(2), inv.GetColumn(1));
            }

            var toBean = new int[n];
            for (int j = 0; j < n; j++) toBean[j] = -1;
            for (int i = 0; i < map.Length; i++) if (map[i] >= 0) toBean[map[i]] = i;

            var depth = new int[n];
            for (int j = 0; j < n; j++)
            {
                int c = m.BoneParent[j];
                while (c >= 0) { depth[j]++; c = m.BoneParent[c]; }
            }
            int anchor = -1;
            for (int j = 0; j < n; j++)
                if (toBean[j] >= 0 && (anchor < 0 || depth[j] < depth[anchor])) anchor = j;

            var kids = new List<int>[n];
            for (int j = 0; j < n; j++) kids[j] = new List<int>();
            for (int j = 0; j < n; j++) if (m.BoneParent[j] >= 0) kids[m.BoneParent[j]].Add(j);
            var order = new int[n];
            for (int j = 0; j < n; j++) order[j] = j;
            Array.Sort(order, (a, b) => depth[a].CompareTo(depth[b]));

            var fix = new Matrix4x4[n];
            var bonePos = new Vector3[n];
            foreach (int j in order)
            {
                var up = m.BoneParent[j] >= 0 ? fix[m.BoneParent[j]] : Matrix4x4.identity;
                var pj = up.MultiplyPoint3x4(m.BonePos[j]);
                fix[j] = up;
                int next = -1, branches = 0;
                foreach (int kid in kids[j])
                {
                    var queue = new Queue<int>();
                    queue.Enqueue(kid);
                    while (queue.Count > 0)
                    {
                        int c = queue.Dequeue();
                        if (toBean[c] >= 0) { next = c; branches++; break; }
                        foreach (int k in kids[c]) queue.Enqueue(k);
                    }
                }
                if (toBean[j] >= 0 && branches == 1)
                {
                    var want = restPos[toBean[next]] - restPos[toBean[j]];
                    var q = Quaternion.FromToRotation(up.MultiplyPoint3x4(m.BonePos[next]) - pj, want);
                    if (Mathf.Abs(want.normalized.y) < 0.8f && Quaternion.Angle(q, Quaternion.identity) > 12f)
                        fix[j] = Matrix4x4.Translate(pj) * Matrix4x4.Rotate(q) * Matrix4x4.Translate(-pj) * up;
                }
                bonePos[j] = fix[j].MultiplyPoint3x4(m.BonePos[j]);
            }

            int count = m.Pos.Length;
            var pos = new Vector3[count];
            var nrm = new Vector3[count];
            for (int v = 0; v < count; v++)
            {
                int o = v * 4;
                float wsum = m.BoneW[o] + m.BoneW[o + 1] + m.BoneW[o + 2] + m.BoneW[o + 3];
                if (wsum <= 0f) { pos[v] = m.Pos[v]; nrm[v] = m.Nrm[v]; continue; }
                for (int k = 0; k < 4; k++)
                {
                    float w = m.BoneW[o + k];
                    if (w <= 0f) continue;
                    pos[v] += w * (Vector3)fix[m.BoneIdx[o + k]].MultiplyPoint3x4(m.Pos[v]);
                    nrm[v] += w * (Vector3)fix[m.BoneIdx[o + k]].MultiplyVector(m.Nrm[v]);
                }
                nrm[v].Normalize();
            }

            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in pos) { minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
            float s = body.sharedMesh.bounds.size.y / Mathf.Max(maxY - minY, 0.0001f);
            var shift = restPos[toBean[anchor]] - s * bonePos[anchor];
            shift.y = body.sharedMesh.bounds.min.y - s * minY;

            var rot = new Quaternion[n];
            var haveRot = new bool[n];
            Quaternion RotOf(int j)
            {
                if (haveRot[j]) return rot[j];
                rot[j] = toBean[j] >= 0 ? restRot[toBean[j]] : (m.BoneParent[j] >= 0 ? RotOf(m.BoneParent[j]) : Quaternion.identity);
                haveRot[j] = true;
                return rot[j];
            }

            var clone = new GameObject("BettrFG_CustomRig_preview");
            clone.transform.SetParent(body.transform.parent, false);
            clone.transform.localPosition = body.transform.localPosition;
            clone.transform.localRotation = body.transform.localRotation;
            clone.transform.localScale = body.transform.localScale;
            var mainFg = new GameObject("Main FG");
            mainFg.transform.SetParent(clone.transform, false);
            var merge = new GameObject("Body_LOD0 (merge)");
            merge.transform.SetParent(mainFg.transform, false);
            int rootIdx = 0;
            for (int i = 0; i < beanBones.Length; i++) if (beanBones[i].name == "Root") { rootIdx = i; break; }
            merge.transform.position = clone.transform.TransformPoint(restPos[rootIdx]);
            merge.transform.rotation = clone.transform.rotation * restRot[rootIdx];
            var anchorGo = new GameObject("rig_anchor_c");
            anchorGo.transform.SetParent(merge.transform, false);
            string torsoName = "rig_root_c";
            foreach (var t in beanBones[rootIdx].GetComponentsInChildren<Transform>(true))
                if (t.name == "Torso_C_jnt_NoStrechSquash") { torsoName = t.name; break; }
            var rigRoot = new GameObject(torsoName);
            rigRoot.transform.SetParent(anchorGo.transform, false);

            var gos = new Transform[n];
            void Make(int j)
            {
                if (gos[j] != null) return;
                int parent = m.BoneParent[j];
                if (parent >= 0) Make(parent);
                var go = new GameObject(toBean[j] >= 0 ? beanBones[toBean[j]].name : m.BoneNames[j] + "_c");
                go.transform.SetParent(parent >= 0 ? gos[parent] : rigRoot.transform, false);
                go.transform.position = clone.transform.TransformPoint(s * bonePos[j] + shift);
                go.transform.rotation = clone.transform.rotation * RotOf(j);
                gos[j] = go.transform;
            }
            for (int j = 0; j < n; j++) Make(j);

            int top = 0;
            for (int j = 0; j < n; j++) if (m.BoneParent[j] < 0) { top = j; break; }

            var verts = new Vector3[count];
            var weights = new BoneWeight[count];
            for (int v = 0; v < count; v++)
            {
                verts[v] = pos[v] * s + shift;
                int o = v * 4;
                if (m.BoneW[o] + m.BoneW[o + 1] + m.BoneW[o + 2] + m.BoneW[o + 3] <= 0f)
                {
                    weights[v].boneIndex0 = top;
                    weights[v].weight0 = 1f;
                    continue;
                }
                weights[v].boneIndex0 = m.BoneIdx[o]; weights[v].weight0 = m.BoneW[o];
                weights[v].boneIndex1 = m.BoneIdx[o + 1]; weights[v].weight1 = m.BoneW[o + 1];
                weights[v].boneIndex2 = m.BoneIdx[o + 2]; weights[v].weight2 = m.BoneW[o + 2];
                weights[v].boneIndex3 = m.BoneIdx[o + 3]; weights[v].weight3 = m.BoneW[o + 3];
            }

            mesh = new Mesh { name = "BettrFG_SkinCreatorRig" };
            mesh.vertices = verts;
            mesh.normals = nrm;
            mesh.uv = m.Uv;
            SkinRetarget.SetParts(mesh, m);
            mesh.boneWeights = weights;
            var bind = new Matrix4x4[n];
            for (int j = 0; j < n; j++) bind[j] = gos[j].worldToLocalMatrix * merge.transform.localToWorldMatrix;
            mesh.bindposes = bind;
            mesh.RecalculateBounds();

            var smr = merge.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = gos;
            smr.rootBone = gos[top];
            smr.sharedMaterials = mats;
            smr.updateWhenOffscreen = true;

            foreach (var t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

            if (bean == null) return clone;
            var sync = clone.AddComponent<BoneSyncComponent>();
            sync.playerObject = bean;
            sync.isRemote = false;
            return clone;
        }
    }
}
