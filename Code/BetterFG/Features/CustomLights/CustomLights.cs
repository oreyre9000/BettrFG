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

namespace BetterFG.Features.CustomLights
{
    internal static class CustomLights
    {
        internal const string MarkerName = "Placeable_Block_Primitive_Sphere_Complete_Vanilla_MEDIUM";
        private static readonly Vector3 IdentityRotation = new Vector3(13.37f, 0f, 0f);
        private const float RotationTolerance = 0.5f;
        private const string LightChildName = "BettrFG_Light";
        private const string HoverChildName = "BettrFG_LightHover";
        private const float DefaultRadius = 3f;
        private const float DefaultIntensity = 1.5f;

        private const string GizmoRes = "BetterFG.assets.features.customlights.gizmo.png";

        private static readonly Dictionary<int, LevelEditorWorldSpaceUI> _markers = new Dictionary<int, LevelEditorWorldSpaceUI>();
        private static ScriptableObjects.LevelEditorWorldSpaceUIData _gizmoData;
        private static bool _gizmoFailed;
        private static bool _delayedRunning;

        private struct RoundLight { public Vector3 Pos; public Color Rgb; public float Radius; public float Intensity; }
        private static readonly List<RoundLight> _roundLights = new List<RoundLight>();
        private static bool _roundLightsParsed;

        internal static void InvalidateRoundLights()
        {
            _roundLights.Clear();
            _roundLightsParsed = false;
            _delayedRunning = false;
        }

        private static void ParseRoundLights()
        {
            _roundLightsParsed = true;
            foreach (var m in IdentifierObjects.ReadRound(MarkerName))
            {
                if (Vector3.Distance(m.Rotation, IdentityRotation) >= RotationTolerance) continue;

                Color rgb = Color.white;
                if (!string.IsNullOrEmpty(m.Hex) && !ColorUtility.TryParseHtmlString(m.Hex, out rgb)) rgb = Color.white;

                _roundLights.Add(new RoundLight
                {
                    Pos = m.Position,
                    Rgb = rgb,
                    Radius = Mathf.Max(0.1f, m.Scale.x),
                    Intensity = Mathf.Max(0f, m.Scale.z),
                });
            }
            Plugin.Log.LogInfo($"lights: {_roundLights.Count} seeded from round json");
        }

        private static bool TryMatchRoundLight(Vector3 pos, out RoundLight data)
        {
            data = default;
            if (!_roundLightsParsed) ParseRoundLights();
            if (_roundLights.Count == 0) return false;

            int bestIdx = -1;
            float bestSq = 1f;
            for (int i = 0; i < _roundLights.Count; i++)
            {
                float d = (_roundLights[i].Pos - pos).sqrMagnitude;
                if (d < bestSq) { bestSq = d; bestIdx = i; }
            }
            if (bestIdx < 0) return false;
            data = _roundLights[bestIdx];
            return true;
        }

        internal static void PlaceAtReticle()
        {
            var mgr = LevelEditorManager.Instance;
            if (mgr == null) return;

            var reticleBase = mgr.GetReticleBase();
            var origin = reticleBase != null ? reticleBase.ReticlePosition : Vector3.zero;

            var lepo = IdentifierObjects.Spawn(MarkerName, origin, IdentityRotation,
                new Vector3(DefaultRadius, DefaultRadius, DefaultIntensity), "#FFFFFF", selectAtReticle: true);
            if (lepo == null) return;

            Sync();
        }

        internal static bool IsLight(LevelEditorPlaceableObject lepo)
        {
            if (lepo == null || !lepo.name.StartsWith(MarkerName, StringComparison.Ordinal)) return false;
            Vector3 rot = lepo.RotationData != null ? lepo.RotationData.CurrentRotation : lepo.transform.eulerAngles;
            return Vector3.Distance(rot, IdentityRotation) < RotationTolerance;
        }

        internal static bool IsLightGO(GameObject go)
            => go != null && go.name.StartsWith(MarkerName, StringComparison.Ordinal) && go.transform.Find(LightChildName) != null;

        internal static void DecollideLight(LevelEditorPlaceableObject lepo)
        {
            if (lepo == null) return;
            var hoverTf = lepo.transform.Find(HoverChildName);
            foreach (var c in lepo.GetComponentsInChildren<Collider>(true))
            {
                if (c == null || (hoverTf != null && c.transform == hoverTf)) continue;
                if (c.enabled) c.enabled = false;
            }
            var col = BatchTargets.GetCollisionParam(lepo);
            if (col != null && col._collisionEnabled) col._collisionEnabled = false;
        }

        internal static float RadiusOf(LevelEditorPlaceableObject lepo)
        {
            var sp = lepo._levelEditorScaleParameter;
            return sp != null ? Mathf.Max(0.1f, sp.CurrentScale.x) : DefaultRadius;
        }

        internal static float IntensityOf(LevelEditorPlaceableObject lepo)
        {
            var sp = lepo._levelEditorScaleParameter;
            return sp != null ? Mathf.Max(0f, sp.CurrentScale.z) : DefaultIntensity;
        }

        internal static Color ColourOf(LevelEditorPlaceableObject lepo)
        {
            var colour = BatchTargets.GetColourParam(lepo);
            return colour != null ? colour.CurrentColour : Color.white;
        }

        internal static void SetRadius(LevelEditorPlaceableObject lepo, float r)
        {
            var sp = lepo._levelEditorScaleParameter;
            if (sp == null) return;
            var s = sp.CurrentScale;
            sp.SetScale(new Vector3(Mathf.Max(0.1f, r), Mathf.Max(0.1f, r), s.z), true);
            Sync();
        }

        internal static void SetIntensity(LevelEditorPlaceableObject lepo, float i)
        {
            var sp = lepo._levelEditorScaleParameter;
            if (sp == null) return;
            var s = sp.CurrentScale;
            sp.SetScale(new Vector3(s.x, s.y, Mathf.Max(0f, i)), true);
            Sync();
        }

        private static bool InEditor()
        {
            try { return GlobalGameStateClient.Instance != null && GlobalGameStateClient.Instance.IsInCreativeEditor; }
            catch { return LevelEditorManager.Instance != null; }
        }

        private static LevelEditorWorldSpaceUI EnsureMarker(LevelEditorPlaceableObject lepo)
        {
            int id = lepo.gameObject.GetInstanceID();
            _markers.TryGetValue(id, out var m);

            var mgr = LevelEditorWorldSpaceUIManager.Instance;
            if (mgr == null) return m;

            if (m == null)
            {
                m = mgr.CreateObjectWorldSpaceUI(LevelEditorWorldSpaceUI.WorldSpaceUIType.CameraVolumeMarker, lepo.transform);
                if (m == null) return null;
                _markers[id] = m;
                foreach (var c in m.GetComponentsInChildren<Collider>(true))
                    if (c != null) UnityEngine.Object.Destroy(c);
            }

            if (_gizmoData == null && !_gizmoFailed)
            {
                var src = mgr.GetUIItemData(LevelEditorWorldSpaceUI.WorldSpaceUIType.CameraVolumeMarker);
                var sprite = EmbeddedResourceandUnity.LoadSprite(GizmoRes);
                if (src == null || sprite == null)
                {
                    _gizmoFailed = true;
                    Plugin.Log.LogWarning($"light gizmo sprite didn't load (donor data {(src == null ? "null" : "ok")}), markers stay the camera icon");
                }
                else
                {
                    _gizmoData = UnityEngine.Object.Instantiate(src);
                    _gizmoData.hideFlags = HideFlags.HideAndDontSave;
                    _gizmoData.UIValid = sprite;
                    _gizmoData.UIInvalid = sprite;
                    _gizmoData.UIValidHighlighted = sprite;
                    _gizmoData.UIInvalidHighlighted = sprite;
                    Plugin.Log.LogInfo("cloned the camera volume marker data, bulb sprite in all four slots");
                }
            }

            if (_gizmoData != null)
            {
                var cur = m._currentUIData;
                if (cur == null || cur.Pointer != _gizmoData.Pointer)
                {
                    m.InitialiseUIObject(_gizmoData);
                    m.SetSprite(Color.white, true);
                    Plugin.Log.LogInfo($"repointed light marker {id} at the bulb data (was {(cur == null ? "nothing" : "the game's")})");
                }
            }
            return m;
        }

        internal static void DropMarker(LevelEditorPlaceableObject lepo)
        {
            if (lepo == null) return;
            RemoveMarker(lepo.gameObject.GetInstanceID());
        }

        private static void RemoveMarker(int id)
        {
            if (!_markers.TryGetValue(id, out var m)) return;
            _markers.Remove(id);
            if (m == null) return;

            var mgr = LevelEditorWorldSpaceUIManager.Instance;
            if (mgr != null) mgr.RemoveWorldSpaceUIForObject(m, true);
            else UnityEngine.Object.Destroy(m.gameObject);
        }

        internal static void SyncDelayed()
        {
            if (_delayedRunning) return;
            var host = BeanMonitorService.Instance;
            if (host == null) return;
            _delayedRunning = true;
            host.StartCoroutine(DelayedRoutine().WrapToIl2Cpp());
        }

        private static IEnumerator DelayedRoutine()
        {
            int[] waits = new[] { 1, 5, 15, 45, 120 };
            foreach (int w in waits)
            {
                for (int i = 0; i < w; i++) yield return null;
                Sync();
                CustomIntroCams.CustomIntroCams.Sync();
                NeonMaterial.NeonMaterial.Sync();
            }
            _delayedRunning = false;
        }

        private static bool _syncing;

        internal static void Sync()
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                bool inEditor = InEditor();

                var lepos = LevelEditorPlaceableObject.Collection;
                var handled = new HashSet<int>();
                if (lepos != null)
                {
                    for (int idx = 0; idx < lepos.Count; idx++)
                    {
                        var lepo = lepos[idx];
                        if (!IsLight(lepo)) continue;
                        handled.Add(lepo.gameObject.GetInstanceID());
                        ApplyLight(lepo.transform, lepo, inEditor);
                    }
                }

                if (!inEditor)
                {
                    foreach (var mr in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
                    {
                        if (mr == null) continue;
                        var t = mr.transform;
                        var root = t.parent != null && t.parent.name.StartsWith(MarkerName, StringComparison.Ordinal) ? t.parent : t;
                        if (!root.name.StartsWith(MarkerName, StringComparison.Ordinal)) continue;
                        if (handled.Contains(root.gameObject.GetInstanceID())) continue;
                        if (Vector3.Distance(root.eulerAngles, IdentityRotation) >= RotationTolerance) continue;
                        handled.Add(root.gameObject.GetInstanceID());
                        ApplyLight(root, null, inEditor);
                    }
                }

                if (_markers.Count > 0)
                {
                    List<int> orphans = null;
                    foreach (var kvp in _markers)
                        if (!handled.Contains(kvp.Key)) (orphans ?? (orphans = new List<int>())).Add(kvp.Key);
                    if (orphans != null)
                        foreach (int id in orphans) RemoveMarker(id);
                }
            }
            finally { _syncing = false; }
        }

        private static void ApplyLight(Transform root, LevelEditorPlaceableObject lepo, bool inEditor)
        {
            if (lepo != null)
            {
                var col = BatchTargets.GetCollisionParam(lepo);
                if (col != null && col._collisionEnabled) { col._collisionEnabled = false; col.ApplyCollisionParam(true); }

                var colParam = BatchTargets.GetColourParam(lepo);
                if (colParam != null) colParam._isColourDisabled = false;
            }

            var colParamOnly = root.GetComponentInChildren<LevelEditorColourChangerParameter>(true);
            var scaleParam = root.GetComponentInChildren<LevelEditorScaleParameter>(true);

            var rgb = colParamOnly != null ? colParamOnly.CurrentColour : Color.white;
            float radius = scaleParam != null ? Mathf.Max(0.1f, scaleParam.CurrentScale.x) : Mathf.Max(0.1f, root.localScale.x);
            float intensity = scaleParam != null ? Mathf.Max(0f, scaleParam.CurrentScale.z) : Mathf.Max(0f, root.localScale.z);

            if (!inEditor)
            {
                if (TryMatchRoundLight(root.position, out var seed))
                {
                    rgb = seed.Rgb;
                    radius = seed.Radius;
                    intensity = seed.Intensity;
                    Plugin.Log.LogInfo($"round light @ {root.position}: rgb={rgb} r={radius} i={intensity} (from json)");
                }
                else Plugin.Log.LogWarning($"round light @ {root.position} had no json seed, defaulting white ({_roundLights.Count} known)");
            }

            var lightTf = root.Find(LightChildName);
            Light light;
            if (lightTf == null)
            {
                var go = new GameObject(LightChildName);
                go.transform.SetParent(root, false);
                light = go.AddComponent<Light>();
                light.type = LightType.Point;
            }
            else light = lightTf.GetComponent<Light>();

            var lossy = root.lossyScale;
            var invScale = new Vector3(
                lossy.x != 0f ? 1f / lossy.x : 1f,
                lossy.y != 0f ? 1f / lossy.y : 1f,
                lossy.z != 0f ? 1f / lossy.z : 1f);
            light.transform.localScale = invScale;

            light.color = rgb;
            light.range = radius;
            light.intensity = intensity;

            Transform hoverTf = root.Find(HoverChildName);
            SphereCollider hover;
            if (hoverTf == null)
            {
                var hgo = new GameObject(HoverChildName);
                hgo.transform.SetParent(root, false);
                hoverTf = hgo.transform;
                hover = hgo.AddComponent<SphereCollider>();
                hover.isTrigger = true;
                hover.radius = 1.2f;
                if (lepo != null)
                {
                    try { LevelEditorPlaceableObject.RegisterColliderForPlaceable(lepo, hover); }
                    catch (System.Exception ex) { Plugin.Log.LogWarning($"light hover collider register threw: {ex.Message}"); }
                }
            }
            else hover = hoverTf.GetComponent<SphereCollider>();
            hoverTf.localScale = invScale;
            hover.enabled = inEditor;

            if (lepo != null)
            {
                var marker = inEditor ? EnsureMarker(lepo) : null;
                if (marker != null) marker.gameObject.SetActive(inEditor);
                else if (_markers.TryGetValue(lepo.gameObject.GetInstanceID(), out var m) && m != null) m.gameObject.SetActive(false);
            }

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (!r.forceRenderingOff) r.forceRenderingOff = true;
            }

            foreach (var c in root.GetComponentsInChildren<Collider>(true))
            {
                if (c == null) continue;
                if (c.transform == hoverTf) continue;
                if (c.enabled) c.enabled = false;
            }
        }


    }
}
