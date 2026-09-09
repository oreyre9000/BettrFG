using System;
using System.Collections.Generic;
using BetterFG.UI.Windows.Creative;
using BetterFG.Utilities;
using FG.Common;
using LevelEditor;
using TMPro;
using UnityEngine;

namespace BetterFG.Features.CustomIntroCams
{
    internal static class CustomIntroCams
    {
        internal const string MarkerName = "Placeable_BasicBlocks_HexagonPrism_VANILLA";
        internal const float PreviewFov = 55f;
        private const float BaseScale = 0.47f;
        private const float ShotScale = 0.29f;
        private const float ScaleTol = 0.03f;
        internal const float MinDuration = 0.5f;
        internal const float MaxDuration = 60f;
        private const float DefaultDuration = 9f;
        internal const int MinOrder = 1;
        internal const int MaxOrder = 40;
        private const float LookDistance = 30f;
        private const float RigDrop = 2f;

        private const string HoverChildName = "BettrFG_IntroCamHover";
        private const string FrustumChildName = "BettrFG_IntroCamFrustum";
        private const string LabelChildName = "BettrFG_IntroCamLabel";
        private const float FrustumReach = 4.5f;
        private const float LabelHeight = 1.9f;
        private const float LabelScreenScale = 0.0005f;

        private const LevelEditorWorldSpaceUI.WorldSpaceUIType BaseGizmo = LevelEditorWorldSpaceUI.WorldSpaceUIType.MoverMarker;
        private const LevelEditorWorldSpaceUI.WorldSpaceUIType ShotGizmo = LevelEditorWorldSpaceUI.WorldSpaceUIType.CameraVolumeMarker;

        private struct Label
        {
            public RectTransform Ui;
            public Transform Shot;
            public TextMeshProUGUI Text;
            public Canvas Canvas;
        }

        private static readonly Dictionary<int, Label> _labels = new Dictionary<int, Label>();

        internal struct Shot
        {
            public Vector3 Pos;
            public Quaternion Rot;
            public int Order;
            public int Seen;
        }

        private static bool IsShotScale(Vector3 s)
            => Mathf.Abs(s.x - ShotScale) < ScaleTol && Mathf.Abs(s.y - ShotScale) < ScaleTol;

        private static bool IsBaseScale(Vector3 s)
            => Mathf.Abs(s.x - BaseScale) < ScaleTol && Mathf.Abs(s.y - BaseScale) < ScaleTol;

        private static Vector3 ScaleOf(LevelEditorPlaceableObject lepo)
        {
            var sp = lepo._levelEditorScaleParameter;
            return sp != null ? sp.CurrentScale : lepo.transform.localScale;
        }

        internal static bool IsBase(LevelEditorPlaceableObject lepo)
            => lepo != null && lepo.name.StartsWith(MarkerName, StringComparison.Ordinal) && IsBaseScale(ScaleOf(lepo));

        internal static bool IsShot(LevelEditorPlaceableObject lepo)
            => lepo != null && lepo.name.StartsWith(MarkerName, StringComparison.Ordinal) && IsShotScale(ScaleOf(lepo));

        internal static float DurationOf(LevelEditorPlaceableObject lepo)
            => Mathf.Clamp(ScaleOf(lepo).z, MinDuration, MaxDuration);

        internal static int OrderOf(LevelEditorPlaceableObject lepo)
            => Mathf.Clamp(Mathf.RoundToInt(ScaleOf(lepo).z), MinOrder, MaxOrder);

        internal static void SetDuration(LevelEditorPlaceableObject lepo, float seconds)
        {
            var sp = lepo._levelEditorScaleParameter;
            if (sp == null) return;
            sp.SetScale(new Vector3(BaseScale, BaseScale, Mathf.Clamp(seconds, MinDuration, MaxDuration)), true);
        }

        internal static void SetOrder(LevelEditorPlaceableObject lepo, int order)
        {
            var sp = lepo._levelEditorScaleParameter;
            if (sp == null) return;
            sp.SetScale(new Vector3(ShotScale, ShotScale, Mathf.Clamp(order, MinOrder, MaxOrder)), true);
            Sync();
        }

        internal static void PlaceAtReticle()
        {
            var mgr = LevelEditorManager.Instance;
            if (mgr == null) return;

            var reticleBase = mgr.GetReticleBase();
            var origin = reticleBase != null ? reticleBase.ReticlePosition : Vector3.zero;

            int nextOrder = MinOrder;
            var col = LevelEditorPlaceableObject.Collection;
            if (col != null)
                for (int i = 0; i < col.Count; i++)
                    if (IsShot(col[i])) nextOrder = Mathf.Max(nextOrder, OrderOf(col[i]) + 1);

            IdentifierObjects.Spawn(MarkerName, origin, Vector3.zero,
                new Vector3(ShotScale, ShotScale, Mathf.Min(nextOrder, MaxOrder)), "#FFFFFF", selectAtReticle: true);
        }

        internal static void DropMarker(LevelEditorPlaceableObject lepo)
        {
            if (lepo == null) return;
            int id = lepo.gameObject.GetInstanceID();
            GizmoMarkers.Drop(id);
            DropLabel(id);
        }

        private static bool _syncing;
        private static bool _rigPending;

        internal static void Sync()
        {
            if (_syncing) return;
            if (!IdentifierObjects.InEditor()) { SyncRound(); return; }

            var col = LevelEditorPlaceableObject.Collection;
            if (col == null) return;

            _syncing = true;
            Transform firstShot = null;
            int firstOrder = int.MaxValue;
            try
            {
                var keptBases = new HashSet<int>();
                var keptShots = new HashSet<int>();
                for (int i = 0; i < col.Count; i++)
                {
                    var lepo = col[i];
                    bool isBase = IsBase(lepo);
                    if (!isBase && !IsShot(lepo)) continue;

                    (isBase ? keptBases : keptShots).Add(lepo.gameObject.GetInstanceID());
                    Dress(lepo, isBase);

                    if (isBase) continue;
                    int order = OrderOf(lepo);
                    if (order < firstOrder) { firstOrder = order; firstShot = lepo.transform; }
                }

                GizmoMarkers.Prune(BaseGizmo, keptBases);
                GizmoMarkers.Prune(ShotGizmo, keptShots);

                List<int> orphans = null;
                foreach (var kv in _labels)
                    if (!keptShots.Contains(kv.Key)) (orphans ?? (orphans = new List<int>())).Add(kv.Key);
                if (orphans != null) foreach (int id in orphans) DropLabel(id);

                if (keptBases.Count > 0 || keptShots.Count == 0) { firstShot = null; _rigPending = false; }
            }
            finally { _syncing = false; }

            if (firstShot == null || _rigPending) return;

            _rigPending = true;
            var at = firstShot.position + Vector3.down * RigDrop;
            IdentifierObjects.Spawn(MarkerName, at, Vector3.zero,
                new Vector3(BaseScale, BaseScale, DefaultDuration), "#FFFFFF");
            Plugin.Log.LogInfo($"shots but no rig in this level, dropped one under shot {firstOrder} at {at}");
        }

        private static bool _gizmosSuppressed;
        private static Camera _editorCam;

        private static readonly List<Renderer> _gizmoLines = new List<Renderer>();
        private static readonly List<Canvas> _gizmoNumbers = new List<Canvas>();

        internal static void SetGizmoRenderersEnabled(bool on)
        {
            for (int i = _gizmoLines.Count - 1; i >= 0; i--)
            {
                var r = _gizmoLines[i];
                if (r == null) { _gizmoLines.RemoveAt(i); continue; }
                if (r.enabled != on) r.enabled = on;
            }
            for (int i = _gizmoNumbers.Count - 1; i >= 0; i--)
            {
                var c = _gizmoNumbers[i];
                if (c == null) { _gizmoNumbers.RemoveAt(i); continue; }
                if (c.enabled != on) c.enabled = on;
            }
        }

        internal static bool HasLabels => _labels.Count > 0;

        private static float _nextCamProbe;

        internal static Camera EditorCamera()
        {
            if (_editorCam != null) return _editorCam;
            if (Time.unscaledTime < _nextCamProbe) return null;
            _nextCamProbe = Time.unscaledTime + 1f;
            var node = GameObject.Find("LevelEditorCameraNode");
            _editorCam = node != null ? node.GetComponent<Camera>() : Camera.main;
            return _editorCam;
        }

        internal static bool IsRigObject(GameObject go)
            => go != null
               && go.name.StartsWith(MarkerName, StringComparison.Ordinal)
               && go.transform.Find(HoverChildName) != null;

        internal static void ShowShotGizmos(bool visible)
        {
            _gizmosSuppressed = !visible;
            SetGizmoRenderersEnabled(visible);
        }

        internal static void BillboardLabels(Transform camera)
        {
            if (_labels.Count == 0) return;

            List<int> dead = null;
            foreach (var kv in _labels)
            {
                var label = kv.Value;
                if (label.Ui == null || label.Shot == null) { (dead ?? (dead = new List<int>())).Add(kv.Key); continue; }

                var at = label.Shot.position + Vector3.up * LabelHeight;
                label.Ui.position = at;
                label.Ui.rotation = camera.rotation;
                label.Ui.localScale = Vector3.one * (LabelScreenScale * Vector3.Distance(at, camera.position));
            }
            if (dead != null) foreach (int id in dead) DropLabel(id);
        }

        private static void DropLabel(int id)
        {
            if (!_labels.TryGetValue(id, out var label)) return;
            _labels.Remove(id);
            if (label.Ui != null) UnityEngine.Object.Destroy(label.Ui.gameObject);
        }

        private static void Dress(LevelEditorPlaceableObject lepo, bool isBase)
        {
            var root = lepo.transform;
            if (root == null) return;

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var n = r.transform.name;
                if (n == FrustumChildName || n == LabelChildName) continue;
                if (!r.forceRenderingOff) r.forceRenderingOff = true;
            }

            var lossy = root.lossyScale;
            var invScale = new Vector3(
                lossy.x != 0f ? 1f / lossy.x : 1f,
                lossy.y != 0f ? 1f / lossy.y : 1f,
                lossy.z != 0f ? 1f / lossy.z : 1f);

            var hoverTf = root.Find(HoverChildName);
            if (hoverTf == null)
            {
                var hgo = new GameObject(HoverChildName);
                hgo.transform.SetParent(root, false);
                hoverTf = hgo.transform;
                var sphere = hgo.AddComponent<SphereCollider>();
                sphere.isTrigger = true;
                sphere.radius = 1.2f;
                try { LevelEditorPlaceableObject.RegisterColliderForPlaceable(lepo, sphere); }
                catch (Exception ex) { Plugin.Log.LogWarning($"intro cam hover collider wouldn't register: {ex.Message}"); }
            }
            hoverTf.localScale = invScale;

            foreach (var c in root.GetComponentsInChildren<Collider>(true))
            {
                if (c == null) continue;
                if (c.transform == hoverTf) { c.enabled = true; continue; }
                if (c.enabled) c.enabled = false;
            }

            var collision = BatchTargets.GetCollisionParam(lepo);
            if (collision != null && collision._collisionEnabled)
            {
                collision._collisionEnabled = false;
                collision.ApplyCollisionParam(true);
            }

            GizmoMarkers.Ensure(lepo, isBase ? BaseGizmo : ShotGizmo);
            GizmoMarkers.SetVisible(lepo.gameObject.GetInstanceID(), true);

            if (isBase) return;

            var colour = BatchTargets.GetColourParam(lepo);
            var tint = colour != null ? colour.CurrentColour : Color.white;
            DressFrustum(root, invScale, tint);
            DressLabel(lepo, tint, OrderOf(lepo));
        }

        private static void DressFrustum(Transform root, Vector3 invScale, Color tint)
        {
            var tf = root.Find(FrustumChildName);
            LineRenderer line;
            if (tf == null)
            {
                var go = new GameObject(FrustumChildName);
                go.transform.SetParent(root, false);
                tf = go.transform;
                line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.loop = true;
                line.positionCount = 3;
                line.numCapVertices = 0;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.material = new Material(IdentifierObjects.UnlitShader()) { name = FrustumChildName };

                float spread = Mathf.Tan(PreviewFov * 0.5f * Mathf.Deg2Rad) * FrustumReach;
                line.SetPosition(0, Vector3.zero);
                line.SetPosition(1, new Vector3(-spread, 0f, FrustumReach));
                line.SetPosition(2, new Vector3(spread, 0f, FrustumReach));
            }
            else line = tf.GetComponent<LineRenderer>();

            if (!_gizmoLines.Contains(line)) _gizmoLines.Add(line);
            line.enabled = !_gizmosSuppressed;
            tf.localScale = invScale;
            line.widthMultiplier = 0.08f;
            line.startColor = tint;
            line.endColor = new Color(tint.r, tint.g, tint.b, 0.35f);

            var mat = line.material;
            foreach (int id in IdentifierObjects.ColourPropertyIds)
                if (mat.HasProperty(id)) mat.SetColor(id, tint);
        }

        private static void DressLabel(LevelEditorPlaceableObject lepo, Color tint, int order)
        {
            int id = lepo.gameObject.GetInstanceID();
            if (!_labels.TryGetValue(id, out var label) || label.Ui == null)
            {
                var font = Core.AssetManager.NameFontAsset;
                if (font == null)
                {
                    Plugin.Log.LogWarning("no nametag font loaded yet, shot numbers stay off for now");
                    return;
                }

                var go = new GameObject(LabelChildName);
                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;

                var rt = go.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(220f, 110f);

                var tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.font = font;
                tmp.fontSize = 76f;
                tmp.enableAutoSizing = false;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.enableWordWrapping = false;
                tmp.raycastTarget = false;

                label = new Label { Ui = rt, Shot = lepo.transform, Text = tmp, Canvas = canvas };
                _labels[id] = label;
            }

            if (!_gizmoNumbers.Contains(label.Canvas)) _gizmoNumbers.Add(label.Canvas);
            label.Canvas.enabled = !_gizmosSuppressed;
            label.Text.color = tint;
            string want = order.ToString();
            if (label.Text.text != want) label.Text.text = want;
        }

        private static void SyncRound()
        {
            int hidden = 0;
            foreach (var mr in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
            {
                if (mr == null) continue;
                var t = mr.transform;
                var root = t.parent != null && t.parent.name.StartsWith(MarkerName, StringComparison.Ordinal) ? t.parent : t;
                if (!root.name.StartsWith(MarkerName, StringComparison.Ordinal)) continue;

                var s = root.localScale;
                if (!IsBaseScale(s) && !IsShotScale(s)) continue;

                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    if (r != null) r.forceRenderingOff = true;
                foreach (var c in root.GetComponentsInChildren<Collider>(true))
                    if (c != null) c.enabled = false;
                hidden++;
            }
            if (hidden > 0) Plugin.Log.LogInfo($"intro cam gizmos out of sight for the round, {hidden} of them");
        }

        internal static bool TryBuildEditorPath(out Vector3[] path, out Vector3[] look, out float duration)
        {
            var shots = new List<Shot>();
            duration = DefaultDuration;
            bool haveRig = false;

            var col = LevelEditorPlaceableObject.Collection;
            if (col != null)
                for (int i = 0; i < col.Count; i++)
                {
                    var lepo = col[i];
                    if (IsBase(lepo)) { haveRig = true; duration = DurationOf(lepo); continue; }
                    if (!IsShot(lepo)) continue;

                    var euler = lepo.RotationData != null ? lepo.RotationData.CurrentRotation : lepo.transform.eulerAngles;
                    shots.Add(new Shot
                    {
                        Pos = lepo.transform.position,
                        Rot = Quaternion.Euler(euler),
                        Order = OrderOf(lepo),
                        Seen = shots.Count,
                    });
                }

            return Finish(shots, haveRig, out path, out look);
        }

        internal static bool TryBuildPath(out Vector3[] path, out Vector3[] look, out float duration)
        {
            var shots = new List<Shot>();
            duration = DefaultDuration;
            bool haveRig = false;

            foreach (var m in IdentifierObjects.ReadRound(MarkerName))
            {
                if (IsBaseScale(m.Scale))
                {
                    haveRig = true;
                    duration = Mathf.Clamp(m.Scale.z, MinDuration, MaxDuration);
                    continue;
                }
                if (!IsShotScale(m.Scale)) continue;

                shots.Add(new Shot
                {
                    Pos = m.Position,
                    Rot = Quaternion.Euler(m.Rotation),
                    Order = Mathf.Clamp(Mathf.RoundToInt(m.Scale.z), MinOrder, MaxOrder),
                    Seen = shots.Count,
                });
            }

            return Finish(shots, haveRig, out path, out look);
        }

        private static bool Finish(List<Shot> shots, bool haveRig, out Vector3[] path, out Vector3[] look)
        {
            path = null;
            look = null;

            if (!haveRig) return false;
            if (shots.Count == 0)
            {
                Plugin.Log.LogWarning("intro cam rig is in this level but there are no shots on it, back to the procedural flythrough");
                return false;
            }

            shots.Sort(Order);

            int n = Mathf.Max(shots.Count, 2);
            path = new Vector3[n];
            look = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var shot = shots[Mathf.Min(i, shots.Count - 1)];
                path[i] = shot.Pos;
                look[i] = shot.Pos + shot.Rot * Vector3.forward * LookDistance;
            }
            return true;
        }

        internal static void AssignFreeOrder(LevelEditorPlaceableObject lepo)
        {
            var used = new HashSet<int>();
            var col = LevelEditorPlaceableObject.Collection;
            if (col != null)
                for (int i = 0; i < col.Count; i++)
                {
                    var other = col[i];
                    if (other == null || other.Pointer == lepo.Pointer || !IsShot(other)) continue;
                    used.Add(OrderOf(other));
                }

            int want = MinOrder;
            while (want <= MaxOrder && used.Contains(want)) want++;
            if (want > MaxOrder)
            {
                Plugin.Log.LogWarning($"every shot order up to {MaxOrder} is taken, the copy keeps {OrderOf(lepo)}");
                return;
            }

            Plugin.Log.LogInfo($"copied shot slotted in at order {want}");
            SetOrder(lepo, want);
        }

        private static int Order(Shot a, Shot b)
        {
            int c = a.Order.CompareTo(b.Order);
            return c != 0 ? c : a.Seen.CompareTo(b.Seen);
        }
    }
}
