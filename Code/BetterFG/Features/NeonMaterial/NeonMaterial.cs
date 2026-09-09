using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using BetterFG.UI.Windows.Creative;
using BetterFG.Utilities;
using FG.Common;
using FGClient;
using LevelEditor;
using UnityEngine;

namespace BetterFG.Features.NeonMaterial
{
    internal static class NeonMaterial
    {
        internal const string PillarName = "Placeable_Block_Pillar_Square_Vanilla_MEDIUM";
        private const string ShellName = "BettrFG_NeonShell";
        private const float MarkerScale = 0.01f;
        private const float MatchRadius = 0.3f;
        private const float ColourBoost = 1.4f;
        private const float ShellGrow = 1.01f;

        private static readonly int[] ColorIds = IdentifierObjects.ColourPropertyIds;
        private const string ScalingShaderName = "Amplify/VFX_MatchFall_Unlit_Fresnel_ManualScaling";
        private static readonly int TextureId = Shader.PropertyToID("_Texture");
        private static readonly int WorldScaleId = Shader.PropertyToID("_WorldScale");
        private static Shader _scalingShader;

        private static readonly HashSet<int> _wantNeon = new HashSet<int>();
        private static readonly HashSet<int> _lit = new HashSet<int>();
        private static readonly Dictionary<int, LevelEditorPlaceableObject> _touched = new Dictionary<int, LevelEditorPlaceableObject>();

        private static Shader ShellShader() => IdentifierObjects.UnlitShader();

        private static bool InEditor() => IdentifierObjects.InEditor();

        internal static bool IsMarkerObject(LevelEditorPlaceableObject lepo)
            => lepo != null
               && lepo.name.StartsWith(PillarName, StringComparison.Ordinal)
               && lepo.transform.localScale.x < MarkerScale * 4f;

        internal static bool IsNeon(LevelEditorPlaceableObject lepo)
        {
            if (lepo == null) return false;
            if (_wantNeon.Contains(lepo.gameObject.GetInstanceID())) return true;
            return FindMarker(lepo.transform.position) != null;
        }

        private static LevelEditorPlaceableObject FindMarker(Vector3 pos)
        {
            var col = LevelEditorPlaceableObject.Collection;
            if (col == null) return null;
            for (int i = 0; i < col.Count; i++)
            {
                var m = col[i];
                if (!IsMarkerObject(m)) continue;
                if ((m.transform.position - pos).sqrMagnitude <= MatchRadius * MatchRadius) return m;
            }
            return null;
        }

        internal static Color ColourOf(LevelEditorPlaceableObject lepo)
        {
            var c = BatchTargets.GetColourParam(lepo);
            return c != null ? c.CurrentColour : Color.white;
        }

        internal static void RefreshColour(LevelEditorPlaceableObject target)
        {
            if (target == null) return;
            ApplyNeon(target.transform, ColourOf(target));
        }

        internal static void SetNeon(LevelEditorPlaceableObject target, bool on)
        {
            if (target == null) return;
            int id = target.gameObject.GetInstanceID();
            _touched[id] = target;

            if (on)
            {
                _wantNeon.Add(id);
                ApplyNeon(target.transform, ColourOf(target));
            }
            else
            {
                _wantNeon.Remove(id);
                RevertNeon(target.transform, ColourOf(target));
            }
        }

        internal static void OnCloned(LevelEditorPlaceableObject clone, LevelEditorPlaceableObject source)
        {
            if (clone == null || source == null || IsMarkerObject(clone) || !IsNeon(source)) return;
            int id = clone.gameObject.GetInstanceID();
            _wantNeon.Add(id);
            _touched[id] = clone;
            ApplyNeon(clone.transform, ColourOf(clone));
        }

        internal static void OnPlaced(LevelEditorPlaceableObject lepo)
        {
            if (lepo == null || _touched.Count == 0) return;
            if (!_touched.ContainsKey(lepo.gameObject.GetInstanceID())) return;
            Reconcile();
        }

        internal static LevelEditorPlaceableObject LastParamTarget;

        internal static void OnParamMenuClosed()
        {
            if (LastParamTarget != null && IsNeon(LastParamTarget)) RefreshColour(LastParamTarget);
            LastParamTarget = null;

            if (_touched.Count == 0) return;
            var host = BeanMonitorService.Instance;
            if (host != null) host.StartCoroutine(ReconcileNextFrame().WrapToIl2Cpp());
            else Reconcile();
        }

        private static IEnumerator ReconcileNextFrame()
        {
            for (int i = 0; i < 10; i++) yield return null;
            Reconcile();
        }

        private static void Reconcile()
        {
            if (_touched.Count == 0) return;
            if (LevelEditorParameterMenuViewModel.IsParametersScreenOpen()) return;

            var done = new List<int>();
            foreach (var kv in _touched)
            {
                var lepo = kv.Value;
                if (lepo == null) { done.Add(kv.Key); continue; }

                var pos = lepo.transform.position;
                var marker = FindMarker(pos);
                bool want = _wantNeon.Contains(kv.Key);

                if (want && marker == null)
                {
                    LevelEditorPlaceableObject pillar = null;
                    try { pillar = IdentifierObjects.Spawn(PillarName, pos, Vector3.zero, Vector3.one * MarkerScale, ""); }
                    catch (Exception ex) { Plugin.Log.LogWarning($"neon: pillar spawn for {lepo.name} threw {ex.Message}"); continue; }
                    if (pillar == null) Plugin.Log.LogWarning($"neon: no pillar for {lepo.name} — is {PillarName} in the object list?");
                    else foreach (var r in pillar.GetComponentsInChildren<Renderer>(true))
                        if (r != null) r.forceRenderingOff = true;
                }
                else if (!want && marker != null)
                {
                    IdentifierObjects.Remove(marker);
                }
                done.Add(kv.Key);
            }
            foreach (var id in done) _touched.Remove(id);
        }

        internal static void ResetEditorState()
        {
            _wantNeon.Clear();
            _touched.Clear();
            _lit.Clear();
            LastParamTarget = null;
        }

        internal static void ApplyNeon(Transform root, Color colour)
        {
            var sh = ShellShader();
            if (sh == null) return;

            Color tint = new Color(
                Mathf.Clamp01(colour.r * ColourBoost),
                Mathf.Clamp01(colour.g * ColourBoost),
                Mathf.Clamp01(colour.b * ColourBoost), 1f);

            var drawable = root.GetComponentInChildren<LevelEditorDrawableData>(true);
            if (drawable != null && drawable.Renderer != null)
            {
                ShellDrawable(drawable, tint);
                _lit.Add(root.gameObject.GetInstanceID());
                return;
            }

            bool any = false;

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var t = r.transform;
                if (t.name.StartsWith(PillarName, StringComparison.Ordinal) || t.name == ShellName) continue;

                var existing = t.Find(ShellName);
                if (existing != null)
                {
                    existing.localScale = Vector3.one * ShellGrow;
                    TintShell(existing, sh, tint);
                    any = true;
                    continue;
                }

                Mesh mesh = null;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null) mesh = mf.sharedMesh;
                if (mesh == null)
                {
                    var smr = r.TryCast<SkinnedMeshRenderer>();
                    if (smr != null) mesh = smr.sharedMesh;
                }
                if (mesh == null) continue;

                var shell = new GameObject(ShellName);
                shell.transform.SetParent(t, false);
                shell.transform.localPosition = Vector3.zero;
                shell.transform.localRotation = Quaternion.identity;
                shell.transform.localScale = Vector3.one * ShellGrow;
                shell.AddComponent<MeshFilter>().sharedMesh = mesh;
                var sr = shell.AddComponent<MeshRenderer>();
                sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                sr.receiveShadows = false;
                TintShell(shell.transform, sh, tint);
                any = true;
            }

            if (any) _lit.Add(root.gameObject.GetInstanceID());
        }

        internal static void OnDrawableRescaled(LevelEditorDrawableData drawable)
        {
            if (_lit.Count == 0) return;
            var lepo = drawable.Placeable;
            if (lepo == null || !_lit.Contains(lepo.gameObject.GetInstanceID())) return;
            ApplyNeon(lepo.transform, ColourOf(lepo));
        }

        private static Shader ScalingShader()
        {
            if (_scalingShader != null) return _scalingShader;
            _scalingShader = Shader.Find(ScalingShaderName);
            if (_scalingShader == null)
                foreach (var c in Resources.FindObjectsOfTypeAll<Shader>())
                    if (c != null && c.name == ScalingShaderName) { _scalingShader = c; break; }
            if (_scalingShader == null) Plugin.Log.LogWarning($"no {ScalingShaderName} in the build, floors will not get a neon shell");
            return _scalingShader;
        }

        private static void ShellDrawable(LevelEditorDrawableData drawable, Color tint)
        {
            var shader = ScalingShader();
            if (shader == null) return;

            var r = drawable.Renderer;
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;

            var t = r.transform;
            var shell = t.Find(ShellName);
            if (shell == null)
            {
                var go = new GameObject(ShellName);
                shell = go.transform;
                shell.SetParent(t, false);
                shell.localPosition = Vector3.zero;
                shell.localRotation = Quaternion.identity;
                shell.localScale = Vector3.one * ShellGrow;
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            var sr = shell.GetComponent<MeshRenderer>();
            var mat = sr.sharedMaterial;
            if (mat == null || mat.shader != shader) { mat = new Material(shader) { name = ShellName }; sr.sharedMaterial = mat; }

            var swatch = mat.GetTexture(TextureId)?.TryCast<Texture2D>();
            if (swatch == null) { swatch = new Texture2D(1, 1); mat.SetTexture(TextureId, swatch); }
            swatch.SetPixel(0, 0, tint);
            swatch.Apply();

            mat.SetVector(WorldScaleId, drawable.GetShaderScale());
        }

        private static void TintShell(Transform shell, Shader sh, Color tint)
        {
            var sr = shell.GetComponent<MeshRenderer>();
            if (sr == null) return;

            var mat = sr.sharedMaterial;
            if (mat == null || mat.shader != sh) { mat = new Material(sh) { name = ShellName }; sr.sharedMaterial = mat; }
            foreach (int id in ColorIds)
                if (mat.HasProperty(id)) mat.SetColor(id, tint);

        }


        internal static void RevertNeon(Transform root, Color colour)
        {
            _lit.Remove(root.gameObject.GetInstanceID());

            var drawable = root.GetComponentInChildren<LevelEditorDrawableData>(true);
            var shells = new List<Transform>();
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                if (tr != null && tr.name == ShellName) shells.Add(tr);
            foreach (var s in shells)
                if (s != null) UnityEngine.Object.Destroy(s.gameObject);
        }

        internal static void Sync()
        {
            if (InEditor()) SyncEditor();
            else SyncRound();
        }

        private static void SyncEditor()
        {
            var col = LevelEditorPlaceableObject.Collection;
            if (col == null) return;

            var markers = new List<Vector3>();
            for (int i = 0; i < col.Count; i++)
                if (IsMarkerObject(col[i])) markers.Add(col[i].transform.position);
            if (markers.Count == 0) return;

            int lit = 0;
            for (int i = 0; i < col.Count; i++)
            {
                var lepo = col[i];
                if (lepo == null || IsMarkerObject(lepo)) continue;
                var p = lepo.transform.position;
                bool neon = false;
                for (int j = 0; j < markers.Count; j++)
                    if ((markers[j] - p).sqrMagnitude <= MatchRadius * MatchRadius) { neon = true; break; }
                if (!neon) continue;
                ApplyNeon(lepo.transform, ColourOf(lepo));
                lit++;
            }
            if (lit > 0) Plugin.Log.LogInfo($"neon: lit {lit} object(s) in editor");
        }

        private static void SyncRound()
        {
            int lit = 0, missed = 0;
            foreach (var m in IdentifierObjects.ReadRound(PillarName))
            {
                if (m.Scale.x > MarkerScale * 4f) continue;

                string hex = IdentifierObjects.FindNearestColourHex(m.Position, MatchRadius, PillarName);
                Color colour = Color.white;
                if (!string.IsNullOrEmpty(hex) && !ColorUtility.TryParseHtmlString(hex, out colour)) colour = Color.white;

                if (LitNearest(m.Position, colour)) lit++;
                else missed++;
            }
            if (lit > 0 || missed > 0)
                Plugin.Log.LogInfo($"neon: {lit} lit from round json"
                    + (missed > 0 ? $", {missed} pillar(s) had nothing near them" : ""));
        }

        private static bool LitNearest(Vector3 pos, Color colour)
        {
            var col = LevelEditorPlaceableObject.Collection;
            if (col != null)
            {
                LevelEditorPlaceableObject best = null;
                float bestSq = MatchRadius * MatchRadius;
                for (int i = 0; i < col.Count; i++)
                {
                    var l = col[i];
                    if (l == null || IsMarkerObject(l)) continue;
                    float d = (l.transform.position - pos).sqrMagnitude;
                    if (d < bestSq) { bestSq = d; best = l; }
                }
                if (best != null) { ApplyNeon(best.transform, colour); return true; }
            }

            Transform bestRoot = null;
            float rootSq = 4f;
            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null || r.name.StartsWith(PillarName, StringComparison.Ordinal)) continue;
                float d = (r.transform.position - pos).sqrMagnitude;
                if (d < rootSq) { rootSq = d; bestRoot = r.transform.root; }
            }
            if (bestRoot == null) return false;
            ApplyNeon(bestRoot, colour);
            return true;
        }

    }
}
