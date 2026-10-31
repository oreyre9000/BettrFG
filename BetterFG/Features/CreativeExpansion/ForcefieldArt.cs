using System.Collections.Generic;
using Levels.Obstacles;
using UnityEngine;

namespace BetterFG.Features.CreativeExpansion
{
    internal sealed class ForcefieldLook
    {
        public string[] Rails;
        public string RailMaterial;
        public string Field;
        public string FieldMaterial;
        public float FieldDepth = 1f;
    }

    internal static class ForcefieldArt
    {
        private const string RailName = "BettrFG_ThemeRail";
        private const string FieldName = "BettrFG_ThemeField";
        private static readonly int ScaleXId = Shader.PropertyToID("_ScaleObject_X");
        private static readonly int ScaleZId = Shader.PropertyToID("_ScaleObject_Z");
        private static readonly Dictionary<int, Mesh[]> _rails = new Dictionary<int, Mesh[]>();
        private static MaterialPropertyBlock _block;

        internal static bool Apply(Transform root, ForcefieldLook look)
        {
            var ff = root.GetComponentInChildren<COMMON_Forcefield>(true);
            if (ff == null) return false;

            var rails = new Mesh[look.Rails.Length];
            for (int i = 0; i < rails.Length; i++)
                if ((rails[i] = ObjectThemes.LoadMesh(look.Rails[i])) == null) return false;
            var railMat = ObjectThemes.LoadMaterial(look.RailMaterial);
            var field = ObjectThemes.LoadMesh(look.Field);
            var fieldMat = ObjectThemes.LoadMaterial(look.FieldMaterial);
            if (railMat == null || field == null || fieldMat == null) return false;

            Clear(root);
            AddRail(ff._startGenerator, rails, railMat, -90f);
            AddRail(ff._endGenerator, rails, railMat, 90f);

            var go = new GameObject(FieldName);
            go.transform.SetParent(ff._bridge, false);
            go.transform.localScale = new Vector3(1f, 1f, look.FieldDepth);
            go.AddComponent<MeshFilter>().sharedMesh = field;
            go.AddComponent<MeshRenderer>().sharedMaterial = fieldMat;

            Fit(root);
            return true;
        }

        private static void AddRail(Transform generator, Mesh[] rails, Material mat, float yaw)
        {
            var go = new GameObject(RailName);
            go.transform.SetParent(generator, false);
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = rails[0];
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            _rails[go.transform.GetInstanceID()] = rails;
        }

        internal static void Fit(Transform root)
        {
            var ff = root != null ? root.GetComponentInChildren<COMMON_Forcefield>(true) : null;
            if (ff == null) return;
            FitRail(ff._startGenerator);
            FitRail(ff._endGenerator);

            var field = ff._bridge.Find(FieldName);
            if (field == null) return;
            var r = field.GetComponent<MeshRenderer>();
            if (!r.sharedMaterial.HasProperty(ScaleXId)) return;
            var m = ff._bridge.localToWorldMatrix;
            _block ??= new MaterialPropertyBlock();
            _block.SetFloat(ScaleXId, ((Vector3)m.GetColumn(0)).magnitude);
            _block.SetFloat(ScaleZId, ((Vector3)m.GetColumn(2)).magnitude);
            r.SetPropertyBlock(_block);
        }

        private static void FitRail(Transform generator)
        {
            var rail = generator.Find(RailName);
            if (rail == null || !_rails.TryGetValue(rail.GetInstanceID(), out var rails)) return;

            float min = float.MaxValue, max = float.MinValue;
            foreach (var r in generator.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.transform == rail) continue;
                var b = r.GetComponent<MeshFilter>().sharedMesh.bounds;
                var toGen = generator.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    float z = toGen.MultiplyPoint3x4(corner).z;
                    min = Mathf.Min(min, z);
                    max = Mathf.Max(max, z);
                }
            }
            if (max <= min) return;

            float span = max - min;
            var best = rails[0];
            foreach (var mesh in rails)
                if (Mathf.Abs(mesh.bounds.size.x - span) < Mathf.Abs(best.bounds.size.x - span)) best = mesh;

            rail.GetComponent<MeshFilter>().sharedMesh = best;
            rail.localPosition = new Vector3(0f, 0f, (min + max) * 0.5f);
            rail.localScale = new Vector3(span / best.bounds.size.x, 1f, 1f);
        }

        internal static void Clear(Transform root)
        {
            var ff = root != null ? root.GetComponentInChildren<COMMON_Forcefield>(true) : null;
            if (ff == null) return;
            foreach (var parent in new[] { ff._startGenerator, ff._endGenerator, ff._bridge })
                for (var old = parent.Find(RailName) ?? parent.Find(FieldName); old != null; old = parent.Find(RailName) ?? parent.Find(FieldName))
                {
                    _rails.Remove(old.GetInstanceID());
                    Object.DestroyImmediate(old.gameObject);
                }
        }
    }
}
