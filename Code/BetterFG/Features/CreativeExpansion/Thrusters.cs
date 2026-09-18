using System;
using System.Collections.Generic;
using BetterFG.UI.Windows.Creative;
using BetterFG.Utilities;
using FG.Common;
using FGClient;
using LevelEditor;
using Levels.Obstacles;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class Thrusters
    {
        internal static bool Enabled => FeatureCreativeExpansion.On("customobjects");

        private static readonly MarkerKind Kind = MarkerKinds.Thruster;
        internal static string MarkerName => Kind.Prefab;
        private static Vector3 IdentityRotation => Kind.Rotation;
        private const float RotationTolerance = MarkerKind.RotationTolerance;

        private const string VisualName = "BettrFG_Thruster";
        private const string PickCollider = "Placement_Collider";
        private const string PickName = "BettrFG_ThrusterPick";
        private const float PickRadius = 1.3f;
        private const float PickHeight = 7f;

        internal const float DefaultSize = 1f;
        internal const float DefaultBeam = 12f;

        private const string ThrusterPrefabGuid = "487b80b187e60df458f80c58d4f2cc09";

        private const string ControllerPrefab = "Placeable_Rule_Movement_Controller_Common";
        private const string BumperPrefab = "Placeable_Obstacle_Bumper_Vanilla_MEDIUM";
        private const float BumperScale = 1.5f;
        private const float BumperHeight = 2.1f;
        private const float BumperClearance = 1.5f;
        private const float BumperTopPerScale = 5.2f;
        private static readonly Vector3 BumperSize = new Vector3(BumperScale, BumperHeight, BumperScale);
        private const int Locked = 255;
        private const float ChargePause = 2f;
        internal const float SameSpot = 0.05f;

        private static readonly Dictionary<string, UnityEngine.Object> _assets = new Dictionary<string, UnityEngine.Object>();
        private static MaterialPropertyBlock _block;
        private static readonly Dictionary<int, Transform> _visuals = new Dictionary<int, Transform>();
        private static readonly Dictionary<int, LevelEditorMovementActiveBase> _rigs = new Dictionary<int, LevelEditorMovementActiveBase>();
        private static readonly Dictionary<int, LevelEditorPlaceableObject> _bumpers = new Dictionary<int, LevelEditorPlaceableObject>();

        internal static bool IsThruster(LevelEditorPlaceableObject lepo)
        {
            if (lepo == null || !lepo.name.StartsWith(MarkerName, StringComparison.Ordinal)) return false;
            Vector3 rot = lepo.RotationData != null ? lepo.RotationData.CurrentRotation : lepo.transform.eulerAngles;
            return Vector3.Distance(rot, IdentityRotation) < RotationTolerance;
        }

        internal static bool IsThrusterGO(GameObject go)
            => go != null && go.name.StartsWith(MarkerName, StringComparison.Ordinal)
               && Vector3.Distance(go.transform.eulerAngles, IdentityRotation) < RotationTolerance;

        internal static void PlaceAtReticle()
        {
            if (!Enabled) return;
            var mgr = LevelEditorManager.Instance;
            if (mgr == null) return;

            var reticleBase = mgr.GetReticleBase();
            var origin = reticleBase != null ? reticleBase.ReticlePosition : Vector3.zero;

            if (IdentifierObjects.Spawn(MarkerName, origin, IdentityRotation,
                Vector3.one * DefaultSize, "#FFFFFF", selectAtReticle: true) == null) return;

            Sync();
        }

        internal static float SizeOf(LevelEditorPlaceableObject lepo)
        {
            var sp = lepo._levelEditorScaleParameter;
            return sp != null ? Mathf.Max(0.1f, sp.CurrentScale.x) : DefaultSize;
        }

        internal static void SetSize(LevelEditorPlaceableObject lepo, float v)
        {
            var sp = lepo._levelEditorScaleParameter;
            if (sp == null) return;
            float s = Mathf.Max(0.1f, v);
            try { sp.SetScale(new Vector3(s, s, s), true); }
            catch (Exception ex) { Plugin.Log.LogWarning($"thruster size: the game threw setting scale, carrying on ({ex.Message})"); }
            Sync();
        }

        internal static Color ColourOf(LevelEditorPlaceableObject lepo)
        {
            var param = BatchTargets.GetColourParam(lepo);
            if (param == null) return Color.white;
            var c = param.CurrentColour;
            c.a = 1f;
            return c;
        }

        internal static void OpenPicker(LevelEditorPlaceableObject lepo)
        {
            var param = BatchTargets.GetColourParam(lepo);
            if (param == null)
            {
                Plugin.Log.LogWarning($"thruster at {lepo.transform.position} has no colour parameter, no picker to open");
                return;
            }
            LevelEditorColourPickerParameterViewModel.OpenColourPickerScreen(param);
        }

        internal static float BeamOf(LevelEditorPlaceableObject lepo)
            => TravelOf(lepo.transform.position);

        internal static float TravelOf(Vector3 markerPos)
        {
            var wp = WaypointOf(ControllerAt(markerPos));
            return wp != null ? Mathf.Max(0f, wp.position.y - markerPos.y) : DefaultBeam;
        }

        internal static void SetBeam(LevelEditorPlaceableObject lepo, float v)
        {
            var wp = WaypointOf(ControllerAt(lepo.transform.position));
            if (wp == null)
            {
                Plugin.Log.LogWarning($"thruster at {lepo.transform.position} has no controller under it, beam length has nothing to move");
                return;
            }
            var pos = lepo.transform.position;
            wp.position = new Vector3(pos.x, pos.y + Mathf.Max(0f, v), pos.z);
            Sync();
        }

        internal static void OnPlaced(LevelEditorPlaceableObject lepo)
        {
            if ((IdentifierObjects.InEditor() && !Enabled) || !IsThruster(lepo)) return;

            var pos = lepo.transform.position;
            if (ControllerAt(pos) != null) return;
            if (MoveRig(lepo, pos)) { Sync(); return; }

            var ctrl = IdentifierObjects.Spawn(ControllerPrefab, pos, Vector3.zero, Vector3.one, "#FFFFFF", hide: false);
            var bumper = IdentifierObjects.Spawn(BumperPrefab, new Vector3(pos.x, RestingY(pos.y), pos.z), Vector3.zero,
                BumperSize, "#FFFFFF", hide: false);
            if (ctrl == null || bumper == null)
            {
                Plugin.Log.LogWarning($"thruster at {pos} could not build its rig (controller {(ctrl == null ? "missing" : "ok")}, "
                    + $"bumper {(bumper == null ? "missing" : "ok")}), it stays art only");
                return;
            }

            var link = ctrl.LinkController;
            if (link != null && !link.IsReceiverLinkedTo(bumper)) link.LinkReceiver(bumper, false);
            else if (link == null) Plugin.Log.LogWarning("the movement controller came up with no link controller, nothing to push");

            var speed = ctrl.GetComponentInChildren<LevelEditorMovementSpeedParameter>(true);
            if (speed != null)
            {
                var steps = speed.MovementSpeedSettingNames;
                speed.MovementStep = steps != null && steps.Length > 0 ? steps.Length - 1 : 7;
            }
            else Plugin.Log.LogWarning("thruster controller has no speed parameter, it launches at whatever the default is");

            var pause = ctrl.GetComponentInChildren<LevelEditorMovementPauseDelayParameter>(true);
            if (pause != null) pause.CurrentDelay = ChargePause;
            else Plugin.Log.LogWarning("thruster controller has no pause delay parameter, it will fire with no charge window");

            var mover = ctrl.GetComponentInChildren<LevelEditorMovementActiveBase>(true);
            _rigs[lepo.gameObject.GetInstanceID()] = mover;

            var wp = WaypointOf(mover);
            if (wp != null) wp.position = pos + Vector3.up * DefaultBeam;
            else Plugin.Log.LogWarning("no waypoint on the thruster controller yet, it keeps its default sideways path");

            Plugin.Log.LogInfo($"thruster rig at {pos}: controller on the marker, bumper parked at "
                + $"{bumper.transform.position.y:0.##}, travel {DefaultBeam}, speed step {(speed != null ? speed.MovementStep : -1)}");
            Sync();
        }

        private static bool MoveRig(LevelEditorPlaceableObject marker, Vector3 pos)
        {
            int id = marker.gameObject.GetInstanceID();
            if (!_rigs.TryGetValue(id, out var mover) || mover == null) { _rigs.Remove(id); return false; }

            var ctrl = mover.GetComponentInParent<LevelEditorPlaceableObject>();
            if (ctrl == null) { _rigs.Remove(id); return false; }

            var delta = pos - mover.transform.position;
            ctrl.transform.position += delta;

            var driven = DrivenOf(ctrl);
            if (driven != null) driven.transform.position += delta;
            else Plugin.Log.LogWarning($"thruster moved to {pos} but nothing is linked to its controller, no bumper to bring");

            Plugin.Log.LogInfo($"thruster moved to {pos}, its controller and bumper came with it");
            return true;
        }

        internal static float RestingY(float floorY)
            => floorY - (BumperTopPerScale * BumperHeight + BumperClearance);

        internal static void HideFromEditor(LevelEditorPlaceableObject lepo, bool killColliders)
        {
            foreach (var r in lepo.GetComponentsInChildren<Renderer>(true))
                if (r != null) r.forceRenderingOff = true;

            lepo.invalidActions = (LevelEditorPlaceableObject.Action)Locked;
            DropGizmos(lepo);

            if (!killColliders) return;

            foreach (var c in lepo.GetComponentsInChildren<Collider>(true))
                if (c != null && !c.isTrigger) c.enabled = false;
        }

        private static void DropGizmos(LevelEditorPlaceableObject lepo)
        {
            var mgr = LevelEditorWorldSpaceUIManager.Instance;
            var live = mgr != null ? mgr._activeWorldSpaceUIElements : null;
            if (live == null) return;

            List<LevelEditorWorldSpaceUI> ours = null;
            for (int i = 0; i < live.Count; i++)
            {
                var ui = live[i];
                if (ui == null) continue;
                var tx = ui.Transmitter;
                var rx = ui.Receiver;
                if ((tx == null || tx.Pointer != lepo.Pointer) && (rx == null || rx.Pointer != lepo.Pointer)) continue;
                (ours ?? (ours = new List<LevelEditorWorldSpaceUI>())).Add(ui);
            }

            if (ours == null) return;
            for (int i = 0; i < ours.Count; i++) mgr.RemoveWorldSpaceUIForObject(ours[i], true);
        }

        internal static void LockRig(Vector3 markerPos, LevelEditorPlaceableObject marker)
        {
            var mover = ControllerAt(markerPos);
            if (mover == null && marker != null && MoveRig(marker, markerPos)) mover = ControllerAt(markerPos);

            var ctrl = mover != null ? mover.GetComponentInParent<LevelEditorPlaceableObject>() : null;
            if (ctrl == null) return;

            if (marker != null) _rigs[marker.gameObject.GetInstanceID()] = mover;

            HideFromEditor(ctrl, true);

            var wp = WaypointOf(mover);
            var wpLepo = wp != null ? wp.GetComponentInParent<LevelEditorPlaceableObject>() : null;
            if (wpLepo != null) HideFromEditor(wpLepo, true);

            var driven = DrivenOf(ctrl);
            if (driven == null) return;
            HideFromEditor(driven, false);
            if (InPlaytest()) return;

            if ((driven.transform.localScale - BumperSize).sqrMagnitude > 0.0001f)
            {
                Plugin.Log.LogInfo($"thruster bumper at {driven.transform.position} was {driven.transform.localScale}, taking it to {BumperSize}");
                driven.transform.localScale = BumperSize;
            }

            float rest = RestingY(markerPos.y);
            if (Mathf.Abs(driven.transform.position.y - rest) > 0.05f)
            {
                var at = driven.transform.position;
                driven.transform.position = new Vector3(at.x, rest, at.z);
                Plugin.Log.LogInfo($"thruster bumper had drifted up to {at.y:0.##} (the editor snaps it to the ground), put it back at {rest:0.##}");
            }
        }

        internal static LevelEditorPlaceableObject DrivenOf(LevelEditorPlaceableObject ctrl)
        {
            var link = ctrl != null ? ctrl.LinkController : null;
            if (link == null) return null;

            foreach (var driven in UnityEngine.Object.FindObjectsOfType<LevelEditorObjectMovementLink>())
            {
                if (driven == null) continue;
                var lepo = driven.GetComponentInParent<LevelEditorPlaceableObject>();
                if (lepo != null && link.IsReceiverLinkedTo(lepo)) return lepo;
            }
            return null;
        }

        internal static void DropRig(LevelEditorPlaceableObject marker)
        {
            var mover = ControllerAt(marker.transform.position);
            var ctrl = mover != null ? mover.GetComponentInParent<LevelEditorPlaceableObject>() : null;
            if (ctrl == null) return;

            var driven = DrivenOf(ctrl);
            if (driven != null)
            {
                driven.invalidActions = 0;
                IdentifierObjects.Remove(driven);
            }

            ctrl.invalidActions = 0;
            IdentifierObjects.Remove(ctrl);
            Plugin.Log.LogInfo($"thruster at {marker.transform.position} went, so its controller and bumper went with it");
        }

        internal static bool InPlaytest()
        {
            var mgr = LevelEditorManager.Instance;
            var state = mgr != null ? mgr.CurrentState : null;
            return state != null && state.TryCast<LevelEditorStatePlay>() != null;
        }

        internal static LevelEditorMovementActiveBase ControllerAt(Vector3 pos)
        {
            foreach (var mover in UnityEngine.Object.FindObjectsOfType<LevelEditorMovementActiveBase>())
                if (mover != null && (mover.transform.position - pos).sqrMagnitude <= SameSpot * SameSpot) return mover;
            return null;
        }

        private static Transform WaypointOf(LevelEditorMovementActiveBase mover)
        {
            var list = mover != null ? mover._waypointsList : null;
            if (list == null || list.Count == 0 || list[0] == null) return null;
            return list[0].transform;
        }

        internal static void DropVisual(LevelEditorPlaceableObject lepo)
        {
            if (lepo == null || !IsThruster(lepo)) return;
            DropRig(lepo);
            int gone = lepo.gameObject.GetInstanceID();
            _rigs.Remove(gone);
            _bumpers.Remove(gone);
            ForgetVisual(gone);
        }

        internal static void SetRigActive(int markerId, bool active)
        {
            if (!_rigs.TryGetValue(markerId, out var mover) || mover == null) return;

            var ctrl = mover.GetComponentInParent<LevelEditorPlaceableObject>(true);
            if (ctrl == null) return;

            if (!active)
            {
                var found = DrivenOf(ctrl);
                if (found != null) _bumpers[markerId] = found;
            }

            if (_bumpers.TryGetValue(markerId, out var bumper) && bumper != null && bumper.gameObject.activeSelf != active)
                bumper.gameObject.SetActive(active);
            if (ctrl.gameObject.activeSelf != active) ctrl.gameObject.SetActive(active);
            if (active) _bumpers.Remove(markerId);
        }

        private static void ForgetVisual(int id)
        {
            if (!_visuals.TryGetValue(id, out var visual)) return;
            _visuals.Remove(id);
            if (visual != null) UnityEngine.Object.Destroy(visual.gameObject);
        }

        private static bool _syncing;

        internal static void Sync()
        {
            if (_syncing) return;
            bool inEditor = IdentifierObjects.InEditor();
            if (inEditor && !Enabled) return;
            _syncing = true;
            try
            {
                var lepos = LevelEditorPlaceableObject.Collection;
                var handled = new HashSet<int>();
                if (lepos != null)
                    for (int i = 0; i < lepos.Count; i++)
                    {
                        var lepo = lepos[i];
                        if (!IsThruster(lepo)) continue;
                        int id = lepo.gameObject.GetInstanceID();
                        handled.Add(id);

                        if (!lepo.gameObject.activeInHierarchy)
                        {
                            SetRigActive(id, false);
                            ForgetVisual(id);
                            continue;
                        }

                        SetRigActive(id, true);
                        Apply(lepo.transform, lepo, inEditor);
                    }

                if (inEditor) return;

                foreach (var mr in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
                {
                    if (mr == null) continue;
                    var t = mr.transform;
                    var root = t.parent != null && t.parent.name.StartsWith(MarkerName, StringComparison.Ordinal) ? t.parent : t;
                    if (!root.name.StartsWith(MarkerName, StringComparison.Ordinal)) continue;
                    if (!handled.Add(root.gameObject.GetInstanceID())) continue;
                    if (Vector3.Distance(root.eulerAngles, IdentityRotation) >= RotationTolerance) continue;
                    Apply(root, null, inEditor);
                }
            }
            finally { _syncing = false; }
        }

        private static void Apply(Transform root, LevelEditorPlaceableObject lepo, bool inEditor)
        {
            float size = lepo != null ? SizeOf(lepo) : Mathf.Max(0.1f, root.localScale.x);

            if (lepo != null)
            {
                var col = BatchTargets.GetCollisionParam(lepo);
                if (col != null && col._collisionEnabled) { col._collisionEnabled = false; col.ApplyCollisionParam(true); }
            }

            int id = root.gameObject.GetInstanceID();
            if (!_visuals.TryGetValue(id, out var visual) || visual == null)
            {
                visual = BuildVisual();
                if (visual == null) return;
                _visuals[id] = visual;
            }

            visual.position = root.position;
            visual.rotation = Quaternion.identity;
            visual.localScale = Vector3.one * size;

            Tint(visual, lepo != null ? ColourOf(lepo) : RoundColour(root.position));
            LockRig(root.position, lepo);

            var driver = visual.GetComponent<ThrusterBeamDriver>();
            if (driver != null) driver.Bind(root, visual.GetComponentInChildren<COMMON_Thruster>(true), TravelOf(root.position));

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r != null && !r.forceRenderingOff) r.forceRenderingOff = true;

            var pickTf = root.Find(PickName);
            CapsuleCollider pick;
            if (pickTf == null)
            {
                var pgo = new GameObject(PickName);
                pgo.transform.SetParent(root, false);
                pickTf = pgo.transform;
                var donor = root.Find(PickCollider);
                pgo.layer = donor != null ? donor.gameObject.layer : root.gameObject.layer;
                pick = pgo.AddComponent<CapsuleCollider>();
                pick.isTrigger = true;
                if (lepo != null)
                {
                    try { LevelEditorPlaceableObject.RegisterColliderForPlaceable(lepo, pick); }
                    catch (Exception ex) { Plugin.Log.LogWarning($"thruster pick collider wouldn't register: {ex.Message}"); }
                }
            }
            else pick = pickTf.GetComponent<CapsuleCollider>();

            pick.radius = PickRadius;
            pick.height = PickHeight;
            pick.center = new Vector3(0f, PickHeight * 0.5f, 0f);

            foreach (var c in root.GetComponentsInChildren<Collider>(true))
            {
                if (c == null) continue;
                bool keep = inEditor && c.transform == pickTf;
                if (c.enabled != keep) c.enabled = keep;
            }
        }

        private static readonly int[] IonColourIds =
        {
            Shader.PropertyToID("_TopColour"),
            Shader.PropertyToID("_BottomColour"),
            Shader.PropertyToID("_CentralCoreColour"),
            Shader.PropertyToID("_InteriorCoreTextureColour"),
            Shader.PropertyToID("_RimHighlightColour"),
            Shader.PropertyToID("_InputColour"),
            Shader.PropertyToID("_TintColor"),
            Shader.PropertyToID("_Color"),
        };

        private static bool IsBasePart(string name)
            => name.StartsWith("Base", StringComparison.Ordinal) || name.StartsWith("PB_", StringComparison.Ordinal);

        private static void Tint(Transform visual, Color colour)
        {
            if (colour == Color.white) return;
            if (_block == null) _block = new MaterialPropertyBlock();

            foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || IsBasePart(r.name)) continue;
                var mat = r.sharedMaterial;
                if (mat == null) continue;

                r.GetPropertyBlock(_block);
                for (int i = 0; i < IonColourIds.Length; i++)
                {
                    if (!mat.HasProperty(IonColourIds[i])) continue;
                    var stock = mat.GetColor(IonColourIds[i]);
                    float punch = Mathf.Max(1f, Mathf.Max(stock.r, Mathf.Max(stock.g, stock.b)));
                    var tinted = colour * punch;
                    tinted.a = stock.a;
                    _block.SetColor(IonColourIds[i], tinted);
                }
                r.SetPropertyBlock(_block);
            }

            foreach (var ps in visual.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;
                try
                {
                    var main = ps.main;
                    var stock = main.startColor.color;
                    float punch = Mathf.Max(1f, Mathf.Max(stock.r, Mathf.Max(stock.g, stock.b)));
                    var tinted = colour * punch;
                    tinted.a = stock.a;
                    main.startColor = new ParticleSystem.MinMaxGradient(tinted);
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"thruster particles kept their own colour, {ps.name} threw: {ex.Message}"); }
            }
        }

        private static Color RoundColour(Vector3 pos)
        {
            foreach (var m in IdentifierObjects.ReadRound(MarkerName))
            {
                if (Vector3.Distance(m.Rotation, IdentityRotation) >= RotationTolerance) continue;
                if ((m.Position - pos).sqrMagnitude > 1f) continue;
                if (!string.IsNullOrEmpty(m.Hex) && ColorUtility.TryParseHtmlString(m.Hex, out var c)) { c.a = 1f; return c; }
            }
            return Color.white;
        }

        private static Transform BuildVisual()
        {
            var prefab = Load<GameObject>(ThrusterPrefabGuid);
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"thruster prefab {ThrusterPrefabGuid} wouldn't load, no art to show");
                return null;
            }

            var go = UnityEngine.Object.Instantiate(prefab);
            go.name = VisualName;
            go.SetActive(true);

            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                if (c != null) c.enabled = false;

            foreach (var b in go.GetComponentsInChildren<Behaviour>(true))
                if (b != null && b.GetIl2CppType().Name == "NavMeshObstacle") b.enabled = false;

            foreach (var seq in go.GetComponentsInChildren<COMMON_Sequence>(true))
                if (seq != null) seq.enabled = false;

            var thruster = go.GetComponentInChildren<COMMON_Thruster>(true);
            var body = thruster != null ? thruster._rb : null;
            if (body != null) { body.isKinematic = true; body.detectCollisions = false; }

            go.AddComponent<ThrusterBeamDriver>();
            return go.transform;
        }

        private static T Load<T>(string guid) where T : UnityEngine.Object
        {
            if (_assets.TryGetValue(guid, out var hit)) return hit as T;
            T asset = null;
            try { asset = Addressables.LoadAssetAsync<T>(guid).WaitForCompletion(); }
            catch (Exception ex) { Plugin.Log.LogWarning($"thruster: {guid} wouldn't load: {ex.Message}"); }
            _assets[guid] = asset;
            return asset;
        }
    }
}
