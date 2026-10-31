using System;
using System.Collections.Generic;
using BetterFG.UI.Windows.Creative;
using BetterFG.Utilities;
using FG.Common;
using FGClient;
using LevelEditor;
using Levels.Obstacles;
using UnityEngine;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class Thrusters
    {
        internal static bool Enabled => FeatureCreativeContent.On("customobjects");

        private static readonly MarkerKind Kind = MarkerKinds.Thruster;
        private static readonly MarkerKind CoreKind = MarkerKinds.ThrusterCore;
        internal static string MarkerName => Kind.Prefab;

        private const string VisualName = "BettrFG_Thruster";
        private const string PickName = "BettrFG_ThrusterPick";
        private const float PickRadius = 1.3f;
        private const float PickHeight = 7f;

        internal const float DefaultSize = 1f;
        internal const float DefaultBeam = 12f;
        internal const float MaxBeam = 15f;

        private const string ThrusterPrefabGuid = "487b80b187e60df458f80c58d4f2cc09";

        private const string ControllerPrefab = "Placeable_Rule_Movement_Controller_Common";
        private const string BumperPrefab = "Placeable_Obstacle_Bumper_Vanilla_MEDIUM";
        internal static readonly string[] Spawns = { Kind.Prefab, ControllerPrefab, BumperPrefab };
        private const float BumperScale = 1.5f;
        private const float BumperHeight = 2.1f;
        private const float BumperClearance = 1.5f;
        private const float BumperTopPerScale = 5.2f;
        private static readonly Vector3 BumperSize = new Vector3(BumperScale, BumperHeight, BumperScale);
        private const int Locked = 255;
        private const float ChargePause = 2f;
        internal const float SameSpot = 0.05f;

        private static MaterialPropertyBlock _block;
        private static readonly Dictionary<int, Transform> _visuals = new Dictionary<int, Transform>();
        private static readonly Dictionary<int, LevelEditorMovementActiveBase> _rigs = new Dictionary<int, LevelEditorMovementActiveBase>();
        private static readonly Dictionary<int, LevelEditorPlaceableObject> _bumpers = new Dictionary<int, LevelEditorPlaceableObject>();
        private static readonly Dictionary<IntPtr, LevelEditorPlaceableObject> _clones = new Dictionary<IntPtr, LevelEditorPlaceableObject>();

        internal static bool IsThruster(LevelEditorPlaceableObject lepo)
            => lepo != null && lepo.name.StartsWith(MarkerName, StringComparison.Ordinal)
               && Kind.MatchesRotation(lepo.RotationData != null ? lepo.RotationData.CurrentRotation : lepo.transform.eulerAngles);

        internal static bool IsThrusterController(LevelEditorPlaceableObject lepo)
            => lepo != null && lepo.name.StartsWith(ControllerPrefab, StringComparison.Ordinal) && MarkerOf(lepo) != null;

        internal static LevelEditorPlaceableObject MarkerOf(LevelEditorPlaceableObject ctrl)
        {
            var col = LevelEditorPlaceableObject.Collection;
            for (int i = 0; i < col.Count; i++)
            {
                var marker = col[i];
                if (!IsThruster(marker)) continue;
                bool owns = _rigs.TryGetValue(marker.gameObject.GetInstanceID(), out var mover) && mover != null
                    ? mover.GetComponentInParent<LevelEditorPlaceableObject>(true).Pointer == ctrl.Pointer
                    : (marker.transform.position - ctrl.transform.position).sqrMagnitude <= SameSpot * SameSpot;
                if (owns) return marker;
            }
            return null;
        }

        internal static bool IsHiddenPart(LevelEditorPlaceableObject lepo)
            => (int)lepo.invalidActions == Locked && lepo.name.StartsWith(BumperPrefab, StringComparison.Ordinal);

        internal static void PlaceAtReticle()
        {
            if (!Enabled) return;
            var mgr = LevelEditorManager.Instance;
            var ctrl = IdentifierObjects.Spawn(ControllerPrefab, IdentifierObjects.ReticleOrigin(mgr), Vector3.zero, Vector3.one, "#FFFFFF", selectAtReticle: true, hide: false);
            if (ctrl == null) return;
            var mover = ctrl.GetComponentInChildren<LevelEditorMovementActiveBase>(true);
            var wp = WaypointOf(mover);
            var wpLepo = wp.GetComponentInParent<LevelEditorPlaceableObject>();
            foreach (var stray in ctrl.GetComponentsInChildren<LevelEditorPlaceableObject>(true))
            {
                if (stray.Pointer == ctrl.Pointer || stray.Pointer == wpLepo.Pointer) continue;
                var gone = stray.transform;
                var oc = ctrl._colliders;
                for (int i = oc.Count - 1; i >= 0; i--) if (oc[i].transform.IsChildOf(gone)) oc.RemoveAt(i);
                var bc = ctrl._baseColliders;
                for (int i = bc.Count - 1; i >= 0; i--) if (bc[i].transform.IsChildOf(gone)) bc.RemoveAt(i);
                foreach (var uc in new[] { ctrl._unityColliders, ctrl._unityCollidersInChildren, ctrl._unityCollidersIncludingInactive })
                    if (uc != null) for (int i = uc.Count - 1; i >= 0; i--) if (uc[i].transform.IsChildOf(gone)) uc.RemoveAt(i);
                LevelEditorPlaceableObject.UnregisterAllCollidersFromPlaceable(stray);
                UnityEngine.Object.Destroy(stray.gameObject);
                Plugin.Log.LogInfo("library copy dragged a second waypoint along, binned it");
            }
            wp.position = ctrl.transform.position + Vector3.up * DefaultBeam;

            var marker = IdentifierObjects.Spawn(MarkerName, ctrl.transform.position, Kind.Rotation, Vector3.one * DefaultSize, "#FFFFFF");
            if (marker == null) return;
            _rigs[marker.gameObject.GetInstanceID()] = mover;
            _fresh = ctrl;
            Sync();
        }

        private static LevelEditorPlaceableObject _fresh;
        internal static bool HasFresh => _fresh != null;

        internal static void DropIfCancelled()
        {
            var held = LevelEditorManager.Instance?.GetReticleBase()?.SelectedObject;
            if (held != null && held.Pointer == _fresh.Pointer) return;
            var ctrl = _fresh;
            _fresh = null;
            DropVisual(ctrl);
            IdentifierObjects.Remove(ctrl);
            Plugin.Log.LogInfo("thruster got cancelled before it was ever placed, binned the lot");
        }

        internal static float SizeOf(LevelEditorPlaceableObject marker) => Mathf.Max(0.1f, marker._levelEditorScaleParameter.CurrentScale.x);

        internal static void SetSize(LevelEditorPlaceableObject marker, float v)
        {
            float s = Mathf.Max(0.1f, v);
            marker._levelEditorScaleParameter._values.SetScale(new Vector3(s, s, s), true, false);
            Sync();
        }

        internal static Color ColourOf(LevelEditorPlaceableObject marker)
        {
            var c = BatchTargets.GetColourParam(marker).CurrentColour;
            c.a = 1f;
            return c;
        }

        internal static void OpenPicker(LevelEditorPlaceableObject marker) => Pick(marker, marker);

        private static int _pickerFor;

        private static void Pick(LevelEditorPlaceableObject marker, LevelEditorPlaceableObject owner)
        {
            _pickerFor = marker.gameObject.GetInstanceID();
            IdentifierObjects.OpenPicker(BatchTargets.GetColourParam(owner));
        }

        internal static bool Previewing(Transform marker)
            => _pickerFor == marker.gameObject.GetInstanceID() && LevelEditorColourPickerParameterViewModel.IsShowing();

        internal static readonly Color StockBeam = new Color(0.659f, 0.458f, 1f);
        internal static readonly Color StockCore = new Color(0.816f, 1f, 0.995f);

        internal static Color BeamSwatch(LevelEditorPlaceableObject marker)
        {
            var c = ColourOf(marker);
            return c == Color.white ? StockBeam : c;
        }

        internal static Color CoreSwatch(LevelEditorPlaceableObject marker)
        {
            var c = CoreColourOf(marker);
            if (c != Color.white) return c;
            return ColourOf(marker) == Color.white ? StockCore : Color.white;
        }

        internal static bool IsCore(LevelEditorPlaceableObject lepo)
            => lepo != null && lepo.name.StartsWith(CoreKind.Prefab, StringComparison.Ordinal)
               && CoreKind.MatchesRotation(lepo.RotationData != null ? lepo.RotationData.CurrentRotation : lepo.transform.eulerAngles);

        private static LevelEditorPlaceableObject CoreAt(Vector3 pos)
        {
            var col = LevelEditorPlaceableObject.Collection;
            for (int i = 0; i < col.Count; i++)
                if (IsCore(col[i]) && (col[i].transform.position - pos).sqrMagnitude <= SameSpot * SameSpot) return col[i];
            return null;
        }

        internal static Color CoreColourOf(LevelEditorPlaceableObject marker)
        {
            var core = CoreAt(marker.transform.position);
            return core != null ? ColourOf(core) : Color.white;
        }

        internal static void OpenCorePicker(LevelEditorPlaceableObject marker)
            => Pick(marker, CoreAt(marker.transform.position) ?? CoreKind.Spawn(marker.transform.position, "#FFFFFF"));

        private static void Follow(LevelEditorPlaceableObject marker, Vector3 to)
        {
            var core = CoreAt(marker.transform.position);
            marker.transform.position = to;
            if (core != null) core.transform.position = to;
        }

        internal static float BeamOf(LevelEditorPlaceableObject marker)
        {
            var wp = WaypointOf(RigOf(marker));
            return wp != null ? Mathf.Min(Vector3.Distance(wp.position, marker.transform.position), MaxBeam) : DefaultBeam;
        }

        internal static float TravelOf(Vector3 markerPos)
        {
            var wp = WaypointOf(ControllerAt(markerPos));
            return wp != null ? Vector3.Distance(wp.position, markerPos) : DefaultBeam;
        }

        internal static void SetBeam(LevelEditorPlaceableObject marker, float v)
        {
            var wp = WaypointOf(RigOf(marker));
            if (wp == null)
            {
                Plugin.Log.LogWarning($"thruster at {marker.transform.position} has no controller under it, beam length has nothing to move");
                return;
            }
            wp.position = marker.transform.position + ControllerOf(marker).transform.up * Mathf.Clamp(v, 0f, MaxBeam);
            Sync();
        }

        private static LevelEditorMovementActiveBase RigOf(LevelEditorPlaceableObject marker)
        {
            int id = marker.gameObject.GetInstanceID();
            if (_rigs.TryGetValue(id, out var mover) && mover != null) return mover;
            mover = ControllerAt(marker.transform.position);
            if (mover != null) _rigs[id] = mover;
            return mover;
        }

        private static LevelEditorPlaceableObject ControllerOf(LevelEditorPlaceableObject marker)
            => RigOf(marker)?.GetComponentInParent<LevelEditorPlaceableObject>(true);

        internal static void OnCloned(LevelEditorPlaceableObject clone, LevelEditorPlaceableObject source)
        {
            if (!IsThruster(clone) && IsThrusterController(source)) _clones[clone.Pointer] = MarkerOf(source);
        }

        internal static void OnPlaced(LevelEditorPlaceableObject lepo)
        {
            if (IdentifierObjects.InEditor() && !Enabled) return;
            if (IsThruster(lepo)) { BuildRig(lepo); Sync(); return; }
            if (!lepo.name.StartsWith(ControllerPrefab, StringComparison.Ordinal)) return;
            if (_fresh != null && _fresh.Pointer == lepo.Pointer) _fresh = null;

            if (_clones.TryGetValue(lepo.Pointer, out var source))
            {
                _clones.Remove(lepo.Pointer);
                var marker = IdentifierObjects.Spawn(MarkerName, lepo.transform.position, Kind.Rotation,
                    Vector3.one * SizeOf(source), "#" + ColorUtility.ToHtmlStringRGB(ColourOf(source)));
                if (marker == null) return;
                var core = CoreAt(source.transform.position);
                if (core != null) CoreKind.Spawn(lepo.transform.position, "#" + ColorUtility.ToHtmlStringRGB(ColourOf(core)));
                BuildRig(marker);
                Plugin.Log.LogInfo($"duplicated a thruster, fresh marker and bumper at {lepo.transform.position}");
                Sync();
                return;
            }

            var owner = MarkerOf(lepo);
            if (owner == null) return;
            Follow(owner, lepo.transform.position);
            if (DrivenOf(lepo) == null) BuildRig(owner);
            Sync();
        }

        private static void BuildRig(LevelEditorPlaceableObject marker)
        {
            var pos = marker.transform.position;
            var ctrl = ControllerOf(marker);
            bool fresh = ctrl == null;
            if (fresh) ctrl = IdentifierObjects.Spawn(ControllerPrefab, pos, Vector3.zero, Vector3.one, "#FFFFFF", hide: false);
            if (ctrl == null) { Plugin.Log.LogWarning($"no movement controller would spawn for the thruster at {pos}, it stays art only"); return; }
            _rigs[marker.gameObject.GetInstanceID()] = ctrl.GetComponentInChildren<LevelEditorMovementActiveBase>(true);
            var up = ctrl.transform.up;

            if (DrivenOf(ctrl) == null)
            {
                var bumper = IdentifierObjects.Spawn(BumperPrefab, pos - up * RestDrop, ctrl.transform.eulerAngles, BumperSize, "#FFFFFF", hide: false);
                if (bumper == null) { Plugin.Log.LogWarning($"thruster at {pos} got a controller but the bumper wouldn't spawn"); return; }
                ctrl.LinkController.LinkReceiver(bumper, false);
                fresh = true;
            }

            if (!fresh) return;

            var speed = ctrl.GetComponentInChildren<LevelEditorMovementSpeedParameter>(true);
            speed.MovementStep = speed.MovementSpeedSettingNames.Length - 1;
            ctrl.GetComponentInChildren<LevelEditorMovementPauseDelayParameter>(true).CurrentDelay = ChargePause;
            WaypointOf(RigOf(marker)).position = pos + up * DefaultBeam;
            Plugin.Log.LogInfo($"thruster rig at {pos}: controller on the marker, bumper parked {RestDrop:0.##} under it along {up}, travel {DefaultBeam}");
        }

        internal const float BumperTop = BumperTopPerScale * BumperHeight;
        private const float RestDrop = BumperTop + BumperClearance;

        internal static void HideFromEditor(LevelEditorPlaceableObject lepo, bool killColliders, bool locked)
        {
            foreach (var r in lepo.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = true;

            if (locked)
            {
                lepo.invalidActions = (LevelEditorPlaceableObject.Action)Locked;
                BatchTargets.EditorCollisionOff(lepo);
                DropGizmos(lepo);
            }

            if (!killColliders) return;
            foreach (var c in lepo.GetComponentsInChildren<Collider>(true))
                if (!c.isTrigger) c.enabled = false;
        }

        private static void DropGizmos(LevelEditorPlaceableObject lepo, bool ownIconOnly = false)
        {
            var mgr = LevelEditorWorldSpaceUIManager.Instance;
            var live = mgr?._activeWorldSpaceUIElements;
            if (live == null) return;

            var ours = new List<LevelEditorWorldSpaceUI>();
            for (int i = 0; i < live.Count; i++)
            {
                var ui = live[i];
                if (ui == null) continue;
                bool sends = ui.Transmitter?.Pointer == lepo.Pointer;
                if (ownIconOnly ? sends && ui.Receiver == null : sends || ui.Receiver?.Pointer == lepo.Pointer) ours.Add(ui);
            }
            foreach (var ui in ours) mgr.RemoveWorldSpaceUIForObject(ui, true);
        }

        internal static void LockRig(Vector3 markerPos, LevelEditorPlaceableObject marker)
        {
            var mover = marker != null ? RigOf(marker) : ControllerAt(markerPos);
            var ctrl = mover?.GetComponentInParent<LevelEditorPlaceableObject>();
            if (ctrl == null) return;

            HideFromEditor(ctrl, true, false);
            MuteLinks(ctrl, null);
            DropGizmos(ctrl, true);

            var wpLepo = WaypointOf(mover)?.GetComponentInParent<LevelEditorPlaceableObject>();
            if (wpLepo != null)
            {
                HideFromEditor(wpLepo, true, true);
                IdentifierObjects.OutlineWith(wpLepo, Array.Empty<Renderer>());
            }

            var driven = DrivenOf(ctrl);
            if (driven == null) return;
            MuteLinks(driven, ctrl);
            HideFromEditor(driven, false, true);
            if (InPlaytest()) return;

            var rot = ctrl.transform.rotation;
            var up = ctrl.transform.up;
            var wp = WaypointOf(mover);
            var aim = markerPos + up * Mathf.Min(Vector3.Distance(wp.position, markerPos), MaxBeam);
            if ((wp.position - aim).sqrMagnitude > 0.0025f) wp.position = aim;

            if ((driven.transform.localScale - BumperSize).sqrMagnitude > 0.0001f) driven.transform.localScale = BumperSize;
            if (Quaternion.Angle(driven.transform.rotation, rot) > 0.1f) driven.transform.rotation = rot;

            var rest = markerPos - up * RestDrop;
            var at = driven.transform.position;
            if ((at - rest).sqrMagnitude <= 0.0025f) return;
            driven.transform.position = rest;
            Plugin.Log.LogInfo($"thruster bumper was at {at}, put back under the base at {rest}");
        }

        private static void MuteLinks(LevelEditorPlaceableObject lepo, LevelEditorPlaceableObject transmitter)
        {
            var ui = lepo.GetComponent<LevelEditorLegacyLinkUI>();
            ui.enabled = false;
            ui._isInitialised = false;
            var mgr = LevelEditorWorldSpaceUIManager.Instance;
            var live = mgr._activeWorldSpaceUIElements;
            if (ui._spawnedWorldSpaceUI != null)
            {
                if (live.Contains(ui._spawnedWorldSpaceUI)) mgr.RemoveWorldSpaceUIForObject(ui._spawnedWorldSpaceUI, true);
                ui._spawnedWorldSpaceUI = null;
            }
            if (ui._transmitterUIWidget != null)
            {
                if (live.Contains(ui._transmitterUIWidget)) mgr.RemoveWorldSpaceUIForObject(ui._transmitterUIWidget, true);
                ui._transmitterUIWidget = null;
            }
            if (transmitter == null || !ui._transmitterLinkCables.TryGetValue(transmitter, out var cable)) return;
            if (live.Contains(cable)) mgr.RemoveWorldSpaceUIForObject(cable, true);
            ui._transmitterLinkCables.Remove(transmitter);
        }

        internal static LevelEditorPlaceableObject DrivenOf(LevelEditorPlaceableObject ctrl)
        {
            var link = ctrl?.LinkController;
            if (link == null) return null;

            foreach (var driven in UnityEngine.Object.FindObjectsOfType<LevelEditorObjectMovementLink>())
            {
                var lepo = driven.GetComponentInParent<LevelEditorPlaceableObject>();
                if (lepo != null && link.IsReceiverLinkedTo(lepo)) return lepo;
            }
            return null;
        }

        internal static bool InPlaytest() => LevelEditorManager.Instance?.CurrentState?.TryCast<LevelEditorStatePlay>() != null;

        internal static LevelEditorMovementActiveBase ControllerAt(Vector3 pos)
        {
            foreach (var mover in UnityEngine.Object.FindObjectsOfType<LevelEditorMovementActiveBase>())
                if ((mover.transform.position - pos).sqrMagnitude <= SameSpot * SameSpot) return mover;
            return null;
        }

        private static Transform WaypointOf(LevelEditorMovementActiveBase mover)
        {
            var list = mover?._waypointsList;
            return list == null || list.Count == 0 ? null : list[0]?.transform;
        }

        internal static void DropVisual(LevelEditorPlaceableObject lepo)
        {
            if (lepo == null) return;
            bool isMarker = IsThruster(lepo);
            if (!isMarker && !lepo.name.StartsWith(ControllerPrefab, StringComparison.Ordinal)) return;

            var marker = isMarker ? lepo : MarkerOf(lepo);
            if (marker == null) return;
            var ctrl = isMarker ? ControllerOf(marker) : lepo;

            var driven = DrivenOf(ctrl);
            if (driven != null) { driven.invalidActions = 0; IdentifierObjects.Remove(driven); }
            IdentifierObjects.Remove(CoreAt(marker.transform.position));
            IdentifierObjects.Remove(isMarker ? ctrl : marker);
            Plugin.Log.LogInfo($"thruster at {marker.transform.position} went, all of it");

            int gone = marker.gameObject.GetInstanceID();
            _rigs.Remove(gone);
            _bumpers.Remove(gone);
            ForgetVisual(gone);
        }

        internal static void SetRigActive(int markerId, bool active)
        {
            if (!_rigs.TryGetValue(markerId, out var mover) || mover == null) return;
            var ctrl = mover.GetComponentInParent<LevelEditorPlaceableObject>(true);

            if (!active)
            {
                var found = DrivenOf(ctrl);
                if (found != null) _bumpers[markerId] = found;
            }

            if (_bumpers.TryGetValue(markerId, out var bumper) && bumper != null) bumper.gameObject.SetActive(active);
            ctrl.gameObject.SetActive(active);
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
                var handled = new HashSet<int>();
                var lepos = LevelEditorPlaceableObject.Collection;
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
                    var t = mr.transform;
                    var root = t.parent != null && t.parent.name.StartsWith(MarkerName, StringComparison.Ordinal) ? t.parent : t;
                    if (!root.name.StartsWith(MarkerName, StringComparison.Ordinal)) continue;
                    if (!Kind.MatchesRotation(root.eulerAngles) || !handled.Add(root.gameObject.GetInstanceID())) continue;
                    Apply(root, null, false);
                }
            }
            finally { _syncing = false; }
        }

        private static void Apply(Transform root, LevelEditorPlaceableObject marker, bool inEditor)
        {
            if (marker != null)
            {
                var col = BatchTargets.GetCollisionParam(marker);
                if (col != null && col._collisionEnabled) { col._collisionEnabled = false; col.ApplyCollisionParam(true); }
                BatchTargets.EditorCollisionOff(marker);
            }

            int id = root.gameObject.GetInstanceID();
            if (!_visuals.TryGetValue(id, out var visual) || visual == null)
            {
                visual = BuildVisual();
                if (visual == null) return;
                _visuals[id] = visual;
            }

            var ctrl = marker != null ? ControllerOf(marker) : null;
            if (ctrl != null && (root.position - ctrl.transform.position).sqrMagnitude > SameSpot * SameSpot) Follow(marker, ctrl.transform.position);

            visual.position = root.position;
            visual.rotation = ctrl != null ? ctrl.transform.rotation : Quaternion.identity;
            visual.localScale = Vector3.one * (marker != null ? SizeOf(marker) : Mathf.Max(0.1f, root.localScale.x));

            Tint(visual, marker != null ? ColourOf(marker) : RoundColour(root.position, Kind),
                marker != null ? CoreColourOf(marker) : RoundColour(root.position, CoreKind));
            LockRig(root.position, marker);

            visual.GetComponent<ThrusterBeamDriver>().Bind(root, ctrl?.transform, visual.GetComponentInChildren<COMMON_Thruster>(true), TravelOf(root.position));

            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = true;
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) if (c.enabled) c.enabled = false;

            if (ctrl == null) return;

            var pickTf = ctrl.transform.Find(PickName);
            if (pickTf == null)
            {
                var pgo = new GameObject(PickName);
                pgo.transform.SetParent(ctrl.transform, false);
                var capsule = pgo.AddComponent<CapsuleCollider>();
                capsule.isTrigger = true;
                capsule.radius = PickRadius;
                capsule.height = PickHeight;
                capsule.center = new Vector3(0f, PickHeight * 0.5f, 0f);
                pickTf = pgo.transform;
            }
            var pick = pickTf.GetComponent<CapsuleCollider>();
            if (pick.enabled != inEditor) pick.enabled = inEditor;
            if (inEditor && !LevelEditorPlaceableObject.ColliderToPlaceable.ContainsKey(pick)) LevelEditorPlaceableObject.RegisterColliderForPlaceable(ctrl, pick);

            var body = new List<Renderer>();
            foreach (var r in visual.GetComponentsInChildren<MeshRenderer>()) if (r.enabled) body.Add(r);
            IdentifierObjects.OutlineWith(ctrl, body);
        }

        private static readonly int[] BeamIds = { Shader.PropertyToID("_BottomColour"), Shader.PropertyToID("_TopColour") };
        private static readonly int[] CoreIds =
        {
            Shader.PropertyToID("_CentralCoreColour"),
            Shader.PropertyToID("_InteriorCoreTextureColour"),
            Shader.PropertyToID("_RimHighlightColour"),
        };
        private static readonly int InputColourId = Shader.PropertyToID("_InputColour");

        private static readonly Dictionary<int, Color> _stockParticles = new Dictionary<int, Color>();
        private static readonly HashSet<int> _tinted = new HashSet<int>();

        private static void Tint(Transform visual, Color beam, Color core)
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            bool beamStock = beam == Color.white;
            bool allStock = beamStock && core == Color.white;
            Color.RGBToHSV(beam, out float bh, out float bs, out _);
            Color.RGBToHSV(core, out float ch, out float cs, out _);

            foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
            {
                var mat = r.sharedMaterial;
                if (mat == null) continue;
                bool ion = mat.HasProperty(BeamIds[0]);
                if (!ion && !mat.HasProperty(InputColourId)) continue;
                if (allStock) { if (!_tinted.Remove(r.GetInstanceID())) continue; }
                else _tinted.Add(r.GetInstanceID());

                r.GetPropertyBlock(_block);
                if (ion)
                {
                    foreach (int prop in BeamIds) _block.SetColor(prop, beamStock ? mat.GetColor(prop) : Lit(bh, bs, 1f, mat.GetColor(prop)));
                    foreach (int prop in CoreIds)
                    {
                        var stock = mat.GetColor(prop);
                        Color.RGBToHSV(stock, out _, out _, out float v);
                        _block.SetColor(prop, allStock ? stock : Lit(ch, cs, v, stock));
                    }
                }
                else
                {
                    var stock = mat.GetColor(InputColourId);
                    bool rim = r.name.StartsWith("Top Rim", StringComparison.Ordinal);
                    _block.SetColor(InputColourId, beamStock ? stock : Lit(bh, rim ? bs * 0.5f : bs, 1f, stock));
                }
                r.SetPropertyBlock(_block);
            }

            foreach (var ps in visual.GetComponentsInChildren<ParticleSystem>(true))
            {
                int id = ps.GetInstanceID();
                if (beamStock && !_stockParticles.ContainsKey(id)) continue;
                var main = ps.main;
                if (!_stockParticles.TryGetValue(id, out var from)) _stockParticles[id] = from = main.startColor.color;
                main.startColor = new ParticleSystem.MinMaxGradient(beamStock ? from : new Color(1f, 1f, 1f, from.a));
            }
        }

        private static Color Lit(float h, float s, float v, Color stock)
        {
            var c = Color.HSVToRGB(h, s, v, true);
            c.a = stock.a;
            return c;
        }

        private static Color RoundColour(Vector3 pos, MarkerKind kind)
        {
            foreach (var m in IdentifierObjects.ReadRound(kind.Prefab))
            {
                if (!kind.MatchesRotation(m.Rotation) || (m.Position - pos).sqrMagnitude > 1f) continue;
                if (!string.IsNullOrEmpty(m.Hex) && ColorUtility.TryParseHtmlString(m.Hex, out var c)) { c.a = 1f; return c; }
            }
            return Color.white;
        }

        private static Transform BuildVisual()
        {
            var prefab = AssetLoads.Get<GameObject>(ThrusterPrefabGuid);
            if (prefab == null) return null;

            var go = UnityEngine.Object.Instantiate(prefab);
            go.name = VisualName;
            go.SetActive(true);

            foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var b in go.GetComponentsInChildren<Behaviour>(true))
                if (b != null && b.GetIl2CppType().Name == "NavMeshObstacle") b.enabled = false;
            foreach (var seq in go.GetComponentsInChildren<COMMON_Sequence>(true)) seq.enabled = false;

            var body = go.GetComponentInChildren<COMMON_Thruster>(true)?._rb;
            if (body != null) { body.isKinematic = true; body.detectCollisions = false; }

            go.AddComponent<ThrusterBeamDriver>();
            return go.transform;
        }
    }
}
