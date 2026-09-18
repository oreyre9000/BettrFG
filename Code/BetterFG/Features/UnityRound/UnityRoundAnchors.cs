using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Features.CreativeExpansion;
using BetterFG.Features.UnityRound.Editor;
using BetterFG.Services;
using BetterFG.UI.Windows.Creative;
using BetterFG.Utilities;
using FG.Common;
using FGClient;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LevelEditor;
using UnityEngine;
using NodeEntry = LevelEditorParameterMenuViewModel.ParameterMenuNodeEntry;

namespace BetterFG.Features.UnityRound
{
    internal static class UnityRoundAnchors
    {
        internal static bool Enabled => FeatureCreativeExpansion.On("customobjects");

        private static readonly MarkerKind Kind = MarkerKinds.UnityRoundAnchor;
        internal static string MarkerName => Kind.Prefab;

        private const float DefaultScale = 0.5f;
        private const float MatchRadius = 0.25f;
        private const float PickRadius = 60f;
        private const float PickSlack = 1f;

        private sealed class Held
        {
            public Transform Obj;
            public Transform Parent;
            public int Sibling;
            public Vector3 LocalPos;
            public Quaternion LocalRot;
        }

        private static readonly Dictionary<int, Held> _held = new Dictionary<int, Held>();
        private static Il2CppArrayBase<Collider> _linkCols;
        private static LevelEditorPlaceableObject _scratch;
        private static Transform _scratchOwner;

        internal static bool IsAnchor(LevelEditorPlaceableObject lepo) => Enabled && Kind.Matches(lepo);

        private static Transform RoundRoot()
        {
            if (UnityRoundLoader.Spawned != null) return UnityRoundLoader.Spawned.transform;
            return BetterFGUnityRounds.ActiveRound != null ? BetterFGUnityRounds.ActiveRound.transform : null;
        }

        private static bool Live() => !IdentifierObjects.InEditor() || Thrusters.InPlaytest();

        internal static LevelEditorPlaceableObject HoverTarget(LevelEditorPlaceableObject transmitter, Collider gameHit)
        {
            if (!Enabled || !BatchLink.IsController(transmitter)) return null;
            var mgr = LevelEditorManager.Instance;
            var root = RoundRoot();
            if (mgr == null || root == null) return null;

            if (_linkCols == null)
            {
                _linkCols = root.GetComponentsInChildren<Collider>(true);
                Plugin.Log.LogInfo($"linking against '{root.name}'#{root.GetInstanceID()}, {_linkCols.Length} collidable bit(s) in it");
            }

            var reticle = IdentifierObjects.ReticleOrigin(mgr);
            int pick = IndexOf(gameHit);
            string how = "editor raycast";
            if (pick < 0) { pick = IndexOf(CastFromCamera(reticle)); how = "our own raycast"; }
            if (pick < 0) { pick = Inside(_linkCols, reticle); how = "reticle inside bounds"; }
            if (pick < 0) { Miss(reticle); return null; }

            var owner = OwnerOf(_linkCols, pick, root);
            var centre = _linkCols[pick].bounds.center;

            var already = FindAnchorAt(centre);
            if (already != null) { Announce(owner, centre, how, false); return already; }

            if (_scratch == null)
            {
                _scratch = IdentifierObjects.Spawn(MarkerName, centre, Kind.Rotation, Vector3.one * DefaultScale, "#FFFFFF");
                if (_scratch == null)
                {
                    Plugin.Log.LogWarning($"'{owner.name}' wanted an anchor and the half sphere wouldn't spawn, it can't be linked");
                    return null;
                }
            }
            else _scratch.transform.position = centre;

            Hide(_scratch);
            _scratchOwner = owner;
            Announce(owner, centre, how, true);
            return _scratch;
        }

        private static int _lastOwner;

        private static void Announce(Transform owner, Vector3 centre, string how, bool scratch)
        {
            _missed = false;
            int id = owner.GetInstanceID();
            if (_lastOwner == id) return;
            _lastOwner = id;
            Plugin.Log.LogInfo($"link hover on '{owner.name}', anchor {(scratch ? "moved to" : "already at")} {centre}, found by {how}");
        }

        private static bool _missed;

        private static void Miss(Vector3 reticle)
        {
            if (_missed) return;
            _missed = true;
            Plugin.Log.LogInfo($"link hover found no unity object. reticle sat at {reticle}, camera {(EditorCamera() == null ? "not found" : EditorCamera().transform.position.ToString())}");
        }

        private static Camera _cam;

        private static Camera EditorCamera()
        {
            if (_cam != null) return _cam;
            var node = GameObject.Find("LevelEditorCameraNode");
            _cam = node != null ? node.GetComponentInChildren<Camera>() : null;
            return _cam;
        }

        private static Collider CastFromCamera(Vector3 reticle)
        {
            var cam = EditorCamera();
            if (cam == null) return null;

            var from = cam.transform.position;
            var dir = reticle - from;
            if (dir.sqrMagnitude < 0.01f) dir = cam.transform.forward;

            var hits = Physics.RaycastAll(from, dir.normalized, 5000f, ~0, QueryTriggerInteraction.Collide);
            Collider best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                var c = hits[i].collider;
                if (c == null || IndexOf(c) < 0) continue;
                if (hits[i].distance >= bestDist) continue;
                bestDist = hits[i].distance;
                best = c;
            }
            return best;
        }

        private static int IndexOf(Collider c)
        {
            if (c == null || c.isTrigger || _linkCols == null) return -1;
            for (int i = 0; i < _linkCols.Length; i++)
                if (_linkCols[i] != null && _linkCols[i].Pointer == c.Pointer) return i;
            return -1;
        }

        private static LevelEditorPlaceableObject FindAnchorAt(Vector3 centre)
        {
            var col = LevelEditorPlaceableObject.Collection;
            if (col == null) return null;
            for (int i = 0; i < col.Count; i++)
                if (IsAnchor(col[i]) && (col[i].transform.position - centre).sqrMagnitude <= MatchRadius * MatchRadius)
                    return col[i];
            return null;
        }

        private static int Inside(Il2CppArrayBase<Collider> cols, Vector3 origin)
        {
            int pick = -1;
            float bestVol = float.MaxValue;
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] == null || cols[i].isTrigger) continue;
                var b = cols[i].bounds;
                b.Expand(PickSlack);
                if (!b.Contains(origin)) continue;
                float vol = b.size.x * b.size.y * b.size.z;
                if (vol >= bestVol) continue;
                bestVol = vol;
                pick = i;
            }
            return pick;
        }

        private static Transform OwnerOf(Il2CppArrayBase<Collider> cols, int i, Transform root)
        {
            var t = cols[i].transform;
            var prev = i > 0 && cols[i - 1] != null ? cols[i - 1].transform : null;
            int rootId = root.GetInstanceID();
            while (t.parent != null && t.parent.GetInstanceID() != rootId
                   && (prev == null || !prev.IsChildOf(t.parent)))
                t = t.parent;
            return t;
        }

        private static Transform Match(Il2CppArrayBase<Collider> cols, Transform root, Vector3 at)
        {
            int best = -1;
            float bestSq = MatchRadius * MatchRadius;
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] == null || cols[i].isTrigger) continue;
                float d = (cols[i].bounds.center - at).sqrMagnitude;
                if (d > bestSq) continue;
                bestSq = d;
                best = i;
            }
            return best < 0 ? null : OwnerOf(cols, best, root);
        }

        internal static void Sync()
        {
            var col = LevelEditorPlaceableObject.Collection;
            if (col == null) return;

            var root = RoundRoot();
            bool live = Live();
            var cols = root != null ? root.GetComponentsInChildren<Collider>(true) : null;
            if (cols == null) DetachAll();

            for (int i = 0; i < col.Count; i++)
            {
                var lepo = col[i];
                if (!IsAnchor(lepo)) continue;

                if (cols != null)
                {
                    var owner = Match(cols, root, lepo.transform.position);
                    if (owner != null) Attach(lepo, owner);
                    else if (!live) Resnap(lepo, cols);
                }

                Hide(lepo);
            }
        }

        private static void Resnap(LevelEditorPlaceableObject anchor, Il2CppArrayBase<Collider> cols)
        {
            var at = anchor.transform.position;
            int best = -1;
            float bestSq = PickRadius * PickRadius;
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] == null || cols[i].isTrigger) continue;
                float d = (cols[i].bounds.center - at).sqrMagnitude;
                if (d >= bestSq) continue;
                bestSq = d;
                best = i;
            }
            if (best < 0)
            {
                Plugin.Log.LogWarning($"an anchor at {at} has no unity object within {PickRadius} of it, so it drives nothing");
                return;
            }
            anchor.transform.position = cols[best].bounds.center;
        }

        private const int LockedActions = (int)(LevelEditorPlaceableObject.Action.Clone | LevelEditorPlaceableObject.Action.Tilt
            | LevelEditorPlaceableObject.Action.Multiselect | LevelEditorPlaceableObject.Action.Movement | LevelEditorPlaceableObject.Action.Select);

        private static void Hide(LevelEditorPlaceableObject anchor)
        {
            var vis = BatchTargets.GetVisibilityParam(anchor);
            if (vis != null) { vis.VisibilityParam = 0; vis.ApplyVisibilityParam(true); }

            foreach (var r in anchor.GetComponentsInChildren<Renderer>(true))
                if (r != null) r.forceRenderingOff = !InsideDriven(r.transform);

            foreach (var c in anchor.GetComponentsInChildren<Collider>(true))
                if (c != null) c.enabled = InsideDriven(c.transform);

            anchor.invalidActions = (LevelEditorPlaceableObject.Action)LockedActions;
        }

        private static bool InsideDriven(Transform t)
        {
            foreach (var kv in _held)
                if (kv.Value.Obj != null && t.IsChildOf(kv.Value.Obj)) return true;
            return false;
        }

        private static void Attach(LevelEditorPlaceableObject anchor, Transform obj)
        {
            int id = obj.gameObject.GetInstanceID();
            if (_held.ContainsKey(id)) return;

            foreach (var kv in _held)
                if (kv.Value.Obj != null && kv.Value.Obj.parent == anchor.transform)
                {
                    Plugin.Log.LogWarning($"anchor already drives '{kv.Value.Obj.name}', not welding '{obj.name}' onto it too");
                    return;
                }

            Plugin.Log.LogInfo($"attach diag: obj='{obj.name}' objId={id} objParent={(obj.parent == null ? "none" : obj.parent.name + "#" + obj.parent.GetInstanceID())} "
                + $"objSceneValid={obj.gameObject.scene.IsValid()} roundRoot={(RoundRoot() == null ? "null" : RoundRoot().name + "#" + RoundRoot().GetInstanceID())}");

            _held[id] = new Held
            {
                Obj = obj,
                Parent = obj.parent,
                Sibling = obj.GetSiblingIndex(),
                LocalPos = obj.localPosition,
                LocalRot = obj.localRotation,
            };
            obj.SetParent(anchor.transform, true);
            Plugin.Log.LogInfo($"'{obj.name}' now rides the anchor at {anchor.transform.position}");
        }

        internal static void DetachAll()
        {
            if (_held.Count == 0) return;
            int n = _held.Count;
            foreach (var kv in _held) Restore(kv.Value);
            _held.Clear();
            Plugin.Log.LogInfo($"{n} driven object(s) handed back to the round");
        }

        private static bool StillInLiveRound(Transform t)
        {
            var root = RoundRoot();
            return root != null && t != null && (t == root || t.IsChildOf(root));
        }

        private static void Restore(Held h)
        {
            if (h.Obj == null) return;
            if (h.Parent != null && StillInLiveRound(h.Parent))
            {
                h.Obj.SetParent(h.Parent, false);
                h.Obj.SetSiblingIndex(h.Sibling);
                h.Obj.localPosition = h.LocalPos;
                h.Obj.localRotation = h.LocalRot;
                return;
            }

            var root = RoundRoot();
            var equivalent = root != null ? FindByName(root, h.Obj.name) : null;
            if (equivalent != null)
            {
                h.Obj.SetParent(equivalent, false);
                h.Obj.localPosition = h.LocalPos;
                h.Obj.localRotation = h.LocalRot;
                Plugin.Log.LogInfo($"'{h.Obj.name}' had gone stale (round reloaded under it), put it back on the live round instead");
                return;
            }

            Plugin.Log.LogWarning($"'{h.Obj.name}' belonged to a round instance that's gone with no live match by name, dropping it");
            UnityEngine.Object.Destroy(h.Obj.gameObject);
        }

        private static Transform FindByName(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == name) return t;
            return null;
        }

        internal static void OnAnchorDestroying(LevelEditorPlaceableObject lepo)
        {
            if (!IsAnchor(lepo)) return;
            DetachOne(lepo.transform);
        }

        private static void DetachOne(Transform anchorT)
        {
            int key = -1;
            foreach (var kv in _held)
                if (kv.Value.Obj != null && kv.Value.Obj.parent == anchorT) { key = kv.Key; break; }
            if (key < 0) return;

            var h = _held[key];
            _held.Remove(key);
            Restore(h);
        }

        internal static void OnLinkingModeStarted()
        {
            _linkCols = null;
            _lastOwner = 0;
            _missed = false;
        }

        internal static void OnLinkingModeEnded(bool confirmed)
        {
            if (confirmed && _scratch != null && _scratchOwner != null) Attach(_scratch, _scratchOwner);

            _scratch = null;
            _scratchOwner = null;
            _linkCols = null;
            _lastOwner = 0;
            _missed = false;

            ReapSoon();
        }

        internal static void ReapSoon()
        {
            var host = Services.BeanMonitorService.Instance;
            if (host != null) host.StartCoroutine(ReapNextFrame().WrapToIl2Cpp());
            else Reap();
        }

        private static IEnumerator ReapNextFrame()
        {
            yield return null;
            yield return null;
            Reap();
        }

        internal static void Reap()
        {
            if (Live()) return;
            var col = LevelEditorPlaceableObject.Collection;
            if (col == null) return;

            int dropped = 0;
            for (int i = col.Count - 1; i >= 0; i--)
            {
                var lepo = col[i];
                if (!IsAnchor(lepo)) continue;
                if (_scratch != null && lepo.Pointer == _scratch.Pointer) continue;

                var lc = lepo.LinkController;
                if (lc != null && lc.IsAnythingConnected) continue;

                DetachOne(lepo.transform);
                lepo.invalidActions = 0;
                IdentifierObjects.Remove(lepo);
                dropped++;
            }
            if (dropped > 0) Plugin.Log.LogInfo($"binned {dropped} anchor(s) with nothing linked to them");
        }

    }

    internal sealed class UnityRoundAnchorIdentifier : IIdentifierObject
    {
        public bool KeepButtons => false;

        public bool Matches(LevelEditorPlaceableObject lepo) => UnityRoundAnchors.IsAnchor(lepo);

        public string DisplayName(LevelEditorPlaceableObject lepo) => LocalizationService.Get("unityroundanchor.name");

        public string Description(LevelEditorPlaceableObject lepo) => LocalizationService.Get("unityroundanchor.description");

        public void PrepareRows(LevelEditorPlaceableObject lepo) { }

        public void CleanupRows(LevelEditorPlaceableObject lepo) { }

        public Il2CppReferenceArray<NodeEntry> FilterRows(LevelEditorParameterMenuViewModel vm, LevelEditorPlaceableObject lepo)
            => vm.NodeEntries;
    }

    [HarmonyPatch(typeof(LevelEditorStateReticleInputHandler), "TryStartLinkingMode")]
    internal static class UnityRoundAnchorLinkStartPatch
    {
        [HarmonyPrefix]
        public static void Prefix() => UnityRoundAnchors.OnLinkingModeStarted();
    }

    [HarmonyPatch(typeof(LevelEditorStateReticleInputHandler), nameof(LevelEditorStateReticleInputHandler.FindObjectIn2DReticle))]
    internal static class UnityRoundAnchorLinkTargetPatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorStateReticleInputHandler __instance,
            ref UnityEngine.RaycastHit hit, ref LevelEditorPlaceableObject __result)
        {
            if (__result != null) return;
            var transmitter = __instance.CurrentlyLinkingSourceTransmitter;
            if (transmitter == null) return;
            __result = UnityRoundAnchors.HoverTarget(transmitter, hit.collider);
        }
    }

    [HarmonyPatch(typeof(LevelEditorStateReticleInputHandler), "OnCancelObjectLinking")]
    internal static class UnityRoundAnchorLinkEndPatch
    {
        [HarmonyPostfix]
        public static void Postfix([HarmonyArgument(0)] bool confirmed) => UnityRoundAnchors.OnLinkingModeEnded(confirmed);
    }

    [HarmonyPatch(typeof(LevelEditorStatePlay), nameof(LevelEditorStatePlay.EnableState))]
    internal static class UnityRoundAnchorPlaytestStartPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => UnityRoundAnchors.Sync();
    }

    [HarmonyPatch(typeof(LevelEditorObjectRotatorLink), nameof(LevelEditorObjectRotatorLink.SetRotator))]
    internal static class UnityRoundAnchorRotationPivotPatch
    {
        [HarmonyPrefix]
        public static void Prefix(LevelEditorObjectRotatorLink __instance, ref Transform rotationCenter)
        {
            var lepo = __instance.GetComponent<LevelEditorPlaceableObject>();
            if (UnityRoundAnchors.IsAnchor(lepo)) rotationCenter = __instance.transform;
        }
    }

    [HarmonyPatch(typeof(ObjectLinkController), nameof(ObjectLinkController.UnlinkReceiver))]
    internal static class UnityRoundAnchorUnlinkPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => UnityRoundAnchors.ReapSoon();
    }

    [HarmonyPatch(typeof(ObjectLinkController), nameof(ObjectLinkController.UnlinkReceiversFromTransmitter))]
    internal static class UnityRoundAnchorUnlinkAllPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => UnityRoundAnchors.ReapSoon();
    }
}
