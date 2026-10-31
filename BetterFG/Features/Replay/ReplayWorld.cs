using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using FG.Common;
using FG.Common.Fraggle;
using FGClient;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

namespace BetterFG.Features.Replay
{
    public struct ReplayObjectFrame
    {
        public float t;
        public Vector3 pos;
        public Quaternion rot;
        public Vector3 scale;
        public int stateHash;
        public float animTime;
        public bool active;
    }

    public struct ReplayObjectState
    {
        public float t;
        public int state;
    }

    internal static class ReplayTileUV
    {
        public static int Pack(float u, float v)
        {
            int qu = Mathf.Clamp(Mathf.RoundToInt(u * 4096f), -32768, 32767);
            int qv = Mathf.Clamp(Mathf.RoundToInt(v * 4096f), -32768, 32767);
            return (qu << 16) | (qv & 0xFFFF);
        }

        public static void Unpack(int packed, out float u, out float v)
        {
            u = (packed >> 16) / 4096f;
            v = (short)(packed & 0xFFFF) / 4096f;
        }
    }

    public class ReplayObject
    {
        public string path = "";
        public string guid = "";
        public string owner = "";
        public string prefab = "";
        public float spawnTime = -1f;
        public float despawnTime = -1f;
        public readonly List<ReplayObjectFrame> frames = new List<ReplayObjectFrame>();
        public readonly List<ReplayObjectState> states = new List<ReplayObjectState>();
        public Transform live;
    }

    // a spawner needs two things in the one state channel: which of Generated/Picked/OnCooldown/Empty
    // it is in, and which powerup the balloon is showing
    internal static class ReplayPowerupPickup
    {
        public static int Pack(Levels.Powerups.COMMON_PowerupPickup pickup)
        {
            var powerup = pickup._selectedPowerup;
            uint id = powerup is not null && powerup.m_CachedPtr != IntPtr.Zero ? powerup.ID : 0u;
            return (int)pickup.PickupState | ((int)id << 8);
        }

        public static void Unpack(int packed, out int state, out uint id)
        {
            state = packed & 0xFF;
            id = (uint)(packed >> 8);
        }
    }

    internal static class ReplayWorldPath
    {
        public static string BaseName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";

            int clone = name.IndexOf("(Clone)", StringComparison.Ordinal);
            if (clone > 0) name = name.Substring(0, clone);
            name = name.TrimEnd();

            if (name.EndsWith("]", StringComparison.Ordinal))
            {
                int bracket = name.LastIndexOf(" [", StringComparison.Ordinal);
                if (bracket > 0) name = name.Substring(0, bracket);
            }
            return name;
        }

        public static bool IsRuntimeInstance(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.IndexOf("(Clone)", StringComparison.Ordinal) > 0) return true;
            if (!name.EndsWith("]", StringComparison.Ordinal)) return false;
            int bracket = name.LastIndexOf(" [", StringComparison.Ordinal);
            if (bracket <= 0) return false;
            for (int i = bracket + 2; i < name.Length - 1; i++)
                if (name[i] < '0' || name[i] > '9') return false;
            return name.Length - 1 > bracket + 2;
        }

        public static string Clean(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            if (name.IndexOf('/') < 0 && name.IndexOf(':') < 0 && name.IndexOf('|') < 0) return name;
            return name.Replace('/', '_').Replace(':', '_').Replace('|', '_');
        }

        public static string Build(Transform tf, Transform stopAt, string scenePrefix, int rootIndex)
        {
            var parts = new List<string>();
            var cur = tf;
            while (cur != stopAt && cur.parent != null)
            {
                parts.Add(cur.GetSiblingIndex() + ":" + Clean(cur.name));
                cur = cur.parent;
            }
            if (stopAt == null) parts.Add(rootIndex + ":" + Clean(cur.name));

            var sb = new StringBuilder();
            if (scenePrefix != null) sb.Append(scenePrefix).Append('|');
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                sb.Append(parts[i]);
                if (i > 0) sb.Append('/');
            }
            return sb.ToString();
        }

        public static Transform Resolve(string path, string guid, List<Scene> scenes)
        {
            Transform tf;
            string[] segments;
            int start;

            if (!string.IsNullOrEmpty(guid))
            {
                var owner = LevelIO.GetGameObject(Il2CppSystem.Guid.Parse(guid));
                if (owner == null) return null;
                if (string.IsNullOrEmpty(path)) return owner.transform;
                tf = owner.transform;
                segments = path.Split('/');
                start = 0;
            }
            else
            {
                int bar = path.IndexOf('|');
                if (bar < 0) return null;

                string sceneName = path.Substring(0, bar);
                var scene = default(Scene);
                foreach (var candidate in scenes)
                    if (candidate.name == sceneName) { scene = candidate; break; }
                if (!scene.IsValid() || !scene.isLoaded) return null;

                segments = path.Substring(bar + 1).Split('/');
                int colon = segments[0].IndexOf(':');
                int rootIndex = -1;
                string rootName = segments[0];
                if (colon > 0)
                {
                    int.TryParse(segments[0].Substring(0, colon), out rootIndex);
                    rootName = segments[0].Substring(colon + 1);
                }

                var roots = scene.GetRootGameObjects();
                GameObject root = null;
                if (rootIndex >= 0 && rootIndex < roots.Length && Clean(roots[rootIndex].name) == rootName)
                    root = roots[rootIndex];
                else
                    foreach (var candidate in roots)
                        if (Clean(candidate.name) == rootName) { root = candidate; break; }

                if (root == null) return null;
                tf = root.transform;
                start = 1;
            }

            for (int i = start; i < segments.Length; i++)
            {
                int colon = segments[i].IndexOf(':');
                int index = -1;
                string name = segments[i];
                if (colon > 0)
                {
                    int.TryParse(segments[i].Substring(0, colon), out index);
                    name = segments[i].Substring(colon + 1);
                }
                string bare = BaseName(name);

                Transform next = null;
                if (index >= 0 && index < tf.childCount && Clean(tf.GetChild(index).name) == name)
                    next = tf.GetChild(index);
                else
                    for (int c = 0; c < tf.childCount; c++)
                        if (Clean(tf.GetChild(c).name) == name) { next = tf.GetChild(c); break; }

                if (next == null && bare != name)
                    for (int c = 0; c < tf.childCount; c++)
                        if (BaseName(Clean(tf.GetChild(c).name)) == bare) { next = tf.GetChild(c); break; }

                if (next == null) return null;
                tf = next;
            }
            return tf;
        }
    }

    internal static class ReplayWorldDrivers
    {
        public static void ForEachSimulated(GameObject root, Action<Behaviour> act)
        {
            foreach (var c in root.GetComponentsInChildren<SeededRandomisable>(true)) if (c != null) act(c);
            foreach (var c in root.GetComponentsInChildren<Levels.SeeSaw.COMMON_SeeSaw>(true)) if (c != null) act(c);
            foreach (var c in root.GetComponentsInChildren<FG.Common.Network.RMIBehaviour>(true)) if (c != null) act(c);
            foreach (var c in root.GetComponentsInChildren<FG.Common.AI.TrolleyBotController>(true)) if (c != null) act(c);
            foreach (var c in root.GetComponentsInChildren<NPCAI>(true)) if (c != null) act(c);
            foreach (var c in root.GetComponentsInChildren<MPGNetObject>(true)) if (c != null) act(c);
            foreach (var c in root.GetComponentsInChildren<Levels.ProceduralGeneration.COMMON_RandomSegmentGeneration>(true)) if (c != null) act(c);
            foreach (var c in root.GetComponentsInChildren<VfxSnowballScaleDynamic>(true)) if (c != null) act(c);
        }
    }

    internal static class ReplayWorldRecorder
    {
        const float POS_EPS = 0.0008f;
        const float SCALE_EPS = 0.0008f;
        const float ROT_DOT = 0.999995f;
        const float ANIM_EPS = 0.0005f;
        const int SUBTREE_CAP = 256;
        const int MAX_NODES = 25000;
        const int FRAME_BUDGET = 2500000;
        const int VERIFY_MASK = 15;
        const float WORLD_HZ = 20f;
        const float NET_SWEEP_STEP = 0.1f;
        internal const float SCROLL_Q = 2048f;

        class Node
        {
            public ReplayObject data;
            public Transform tf;
            public GameObject go;
            public Animator anim;
            public bool hasAnim;
            public bool world;
            public bool watchActive;
            public Vector3 pos;
            public Quaternion rot;
            public Vector3 scale;
            public int state;
            public float animTime;
            public bool active;
            public Levels.Obstacles.COMMON_BlastBall blast;
            public LevelEditorDestructibleObjectResponder dest;
            public Levels.TextureUVImageRenderer uv;
            public Levels.Obstacles.COMMON_ConveyorBelt conv;
            public Levels.Obstacles.LevelEditorCommonConveyorBelt convLe;
            public Levels.Powerups.COMMON_PowerupPickup pick;
            public Levels.Obstacles.COMMON_RespawningTile tile;
            public Levels.FloorFall.FloorFall_FallingPlatform floor;
            public int blastState;
            public bool holdPending;
            public float holdTime;
            public bool closed;
            public int visits;
        }

        static readonly List<Node> _nodes = new List<Node>();
        static readonly Dictionary<IntPtr, Node> _spawnedByNet = new Dictionary<IntPtr, Node>();
        static readonly Dictionary<int, Node> _liveLocal = new Dictionary<int, Node>();
        static readonly HashSet<IntPtr> _knownNet = new HashSet<IntPtr>();
        static readonly HashSet<int> _seen = new HashSet<int>();
        static readonly HashSet<int> _keep = new HashSet<int>();
        static readonly HashSet<int> _watchAt = new HashSet<int>();
        static readonly HashSet<IntPtr> _aliveThisTick = new HashSet<IntPtr>();
        static readonly HashSet<int> _particleAt = new HashSet<int>();
        static readonly HashSet<int> _beanAt = new HashSet<int>();
        static readonly HashSet<int> _hudAt = new HashSet<int>();
        static readonly Dictionary<int, LevelEditorPlaceableObject> _placeableAt = new Dictionary<int, LevelEditorPlaceableObject>();
        static readonly Dictionary<int, Animator> _animAt = new Dictionary<int, Animator>();
        static readonly Dictionary<int, Levels.Obstacles.COMMON_BlastBall> _blastAt = new Dictionary<int, Levels.Obstacles.COMMON_BlastBall>();
        static readonly Dictionary<int, LevelEditorDestructibleObjectResponder> _destAt = new Dictionary<int, LevelEditorDestructibleObjectResponder>();
        static readonly Dictionary<int, Levels.TextureUVImageRenderer> _uvAt = new Dictionary<int, Levels.TextureUVImageRenderer>();
        static readonly Dictionary<int, Behaviour> _convAt = new Dictionary<int, Behaviour>();
        static readonly Dictionary<int, Levels.Powerups.COMMON_PowerupPickup> _pickAt = new Dictionary<int, Levels.Powerups.COMMON_PowerupPickup>();
        static readonly Dictionary<int, Levels.Obstacles.COMMON_RespawningTile> _tileAt = new Dictionary<int, Levels.Obstacles.COMMON_RespawningTile>();
        static readonly Dictionary<int, Levels.FloorFall.FloorFall_FallingPlatform> _floorAt = new Dictionary<int, Levels.FloorFall.FloorFall_FallingPlatform>();
        static ReplayRecording _rec;
        static int _frames;
        static int _subtree;
        static int _drivers;
        static int _pooled;
        static int _cursor;
        static float _slice;
        static float _sweep;
        static float _startTime;
        static bool _budgetBlown;
        static int _suppressed;

        public static void PushSuppressSpawns() => _suppressed++;
        public static void PopSuppressSpawns() => _suppressed = Mathf.Max(0, _suppressed - 1);

        public static void Begin(ReplayRecording rec, float t)
        {
            _rec = rec;
            _nodes.Clear();
            _spawnedByNet.Clear();
            _liveLocal.Clear();
            _knownNet.Clear();
            _seen.Clear();
            _keep.Clear();
            _frames = 0;
            _drivers = 0;
            _pooled = 0;
            _cursor = 0;
            _slice = 0f;
            _sweep = 0f;
            _startTime = t;
            _budgetBlown = false;

            try { Discover(); }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"world discovery fell over, the replay won't have the level in it: {ex}");
                _nodes.Clear();
            }

            if (_nodes.Count >= MAX_NODES)
                Plugin.Log.LogWarning($"level has more moving parts than the {MAX_NODES} cap, the tail of the hierarchy won't be in the replay");
            Plugin.Log.LogInfo($"tracking {_nodes.Count} world objects for the replay, off {_drivers} game scripts"
                + (_pooled > 0 ? $" — {_pooled} of them spawned-in things the replay will have to rebuild" : ""));
        }

        public static void End()
        {
            if (_rec != null)
            {
                int moved = 0;
                int rebuilt = 0;
                for (int i = _rec.worldObjects.Count - 1; i >= 0; i--)
                {
                    var obj = _rec.worldObjects[i];
                    if (obj.frames.Count > 0) { moved++; continue; }
                    if (!string.IsNullOrEmpty(obj.prefab) || obj.despawnTime >= 0f || obj.states.Count > 1) { rebuilt++; continue; }
                    _rec.worldObjects.RemoveAt(i);
                }
                Plugin.Log.LogInfo($"{moved} world objects actually did something, {_frames} object frames"
                    + (rebuilt > 0 ? $", plus {rebuilt} that never moved but still have to be spawned or removed" : ""));
            }

            _nodes.Clear();
            _spawnedByNet.Clear();
            _liveLocal.Clear();
            _knownNet.Clear();
            _seen.Clear();
            _keep.Clear();
            ClearIndex();
            _rec = null;
        }

        static void ClearIndex()
        {
            _particleAt.Clear();
            _beanAt.Clear();
            _hudAt.Clear();
            _placeableAt.Clear();
            _animAt.Clear();
            _blastAt.Clear();
            _destAt.Clear();
            _uvAt.Clear();
            _convAt.Clear();
            _pickAt.Clear();
            _tileAt.Clear();
            _floorAt.Clear();
            _watchAt.Clear();
        }

        static void Discover()
        {
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;

                var roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    var root = roots[r];
                    if (root.name.StartsWith("BetterFG_") || root.name.StartsWith("BettrFG_")) continue;
                    if (root.GetComponent<Canvas>() != null) continue;

                    ClearIndex();
                    _keep.Clear();

                    foreach (var c in root.GetComponentsInChildren<ParticleSystem>(true))
                        if (c != null) _particleAt.Add(c.transform.GetInstanceID());
                    foreach (var c in root.GetComponentsInChildren<FallGuysCharacterController>(true))
                        if (c != null) _beanAt.Add(c.transform.GetInstanceID());
                    foreach (var c in root.GetComponentsInChildren<PlayerInfoHUDBase>(true))
                        if (c != null) _hudAt.Add(c.transform.GetInstanceID());
                    foreach (var c in root.GetComponentsInChildren<Levels.Obstacles.COMMON_BlastBall>(true))
                        if (c != null) _blastAt[c.transform.GetInstanceID()] = c;
                    foreach (var c in root.GetComponentsInChildren<LevelEditorDestructibleObjectResponder>(true))
                        if (c != null) _destAt[c.transform.GetInstanceID()] = c;
                    var uvRenderers = root.GetComponentsInChildren<Levels.TextureUVImageRenderer>(true);
                    foreach (var c in uvRenderers)
                        if (c != null) _uvAt[c.transform.GetInstanceID()] = c;
                    foreach (var c in root.GetComponentsInChildren<Levels.Obstacles.COMMON_ConveyorBelt>(true))
                        if (c != null) _convAt[c.transform.GetInstanceID()] = c;
                    foreach (var c in root.GetComponentsInChildren<Levels.Obstacles.LevelEditorCommonConveyorBelt>(true))
                        if (c != null) _convAt[c.transform.GetInstanceID()] = c;
                    foreach (var c in root.GetComponentsInChildren<Levels.Powerups.COMMON_PowerupPickup>(true))
                        if (c != null) _pickAt[c.transform.GetInstanceID()] = c;
                    foreach (var c in root.GetComponentsInChildren<Levels.Obstacles.COMMON_RespawningTile>(true))
                        if (c != null) _tileAt[c.transform.GetInstanceID()] = c;
                    foreach (var c in root.GetComponentsInChildren<Levels.FloorFall.FloorFall_FallingPlatform>(true))
                        if (c != null) _floorAt[c.transform.GetInstanceID()] = c;

                    var anims = root.GetComponentsInChildren<Animator>(true);
                    foreach (var c in anims) if (c != null) _animAt[c.transform.GetInstanceID()] = c;
                    var placeables = root.GetComponentsInChildren<LevelEditorPlaceableObject>(true);
                    foreach (var c in placeables) if (c != null) _placeableAt[c.transform.GetInstanceID()] = c;

                    var drivers = root.GetComponentsInChildren<FGBehaviour>(true);
                    var bodies = root.GetComponentsInChildren<Rigidbody>(true);
                    var joints = root.GetComponentsInChildren<Joint>(true);
                    var collectables = root.GetComponentsInChildren<Levels.ScoreZone.SpawnableCollectable>(true);

                    foreach (var c in drivers) if (c != null) MarkKeep(c.transform);
                    foreach (var c in bodies) if (c != null) MarkKeep(c.transform);
                    foreach (var c in joints) if (c != null) MarkKeep(c.transform);
                    foreach (var c in anims) if (c != null) MarkKeep(c.transform);
                    foreach (var c in placeables) if (c != null) MarkKeep(c.transform);
                    foreach (var c in uvRenderers) if (c != null) MarkKeep(c.transform);
                    foreach (var c in collectables) if (c != null) MarkKeep(c.transform);
                    foreach (var c in root.GetComponentsInChildren<MPGNetObject>(true)) if (c != null) MarkKeep(c.transform);
                    foreach (var c in root.GetComponentsInChildren<Collider>(true)) if (c != null) MarkKeep(c.transform);
                    foreach (var c in _blastAt.Values) MarkKeep(c.transform);
                    foreach (var c in _destAt.Values) MarkKeep(c.transform);
                    foreach (var c in _convAt.Values) MarkKeep(c.transform);
                    // a tile never moves its own transform, it drops and hides these children, and an
                    // active MeshRenderer isn't reason enough to keep one, so the whole floor replayed intact
                    foreach (var c in _tileAt.Values)
                    {
                        if (c._visualsForRespawn != null) Watch(c._visualsForRespawn.transform);
                        if (c._colliderObject != null) Watch(c._colliderObject.transform);
                        if (c._outerRingObject != null) Watch(c._outerRingObject.transform);
                    }
                    foreach (var c in _floorAt.Values)
                        if (c._visuals != null) Watch(c._visuals.transform);
                    foreach (var c in root.GetComponentsInChildren<Renderer>(true))
                        if (c != null && !c.gameObject.activeSelf) MarkKeep(c.transform);

                    foreach (var c in drivers)
                        if (c != null) { _drivers++; TrackSubtree(c.transform, scene.name, r); }
                    foreach (var c in bodies)
                        if (c != null) TrackSubtree(c.transform, scene.name, r);
                    foreach (var c in anims)
                        if (c != null) TrackSubtree(c.transform, scene.name, r);
                    foreach (var c in joints)
                        if (c != null) TrackSubtree(c.transform, scene.name, r);
                    foreach (var c in placeables)
                        if (c != null) TrackSubtree(c.transform, scene.name, r);
                    foreach (var c in uvRenderers)
                        if (c != null) TrackSubtree(c.transform, scene.name, r);
                    foreach (var c in collectables)
                        if (c != null) TrackSubtree(c.transform, scene.name, r);

                    foreach (var sw in root.GetComponentsInChildren<SetSwitcher>(true))
                    {
                        if (sw == null || string.IsNullOrEmpty(sw.ChosenKey)) continue;
                        _rec.sets.Add(new ReplaySet
                        {
                            key = string.IsNullOrEmpty(sw._cmsKey) ? sw.gameObject.name : sw._cmsKey,
                            chosen = sw.ChosenKey,
                            path = ReplayWorldPath.Build(sw.transform, null, scene.name, r),
                        });
                    }
                }
            }

            if (_rec.sets.Count > 0)
                Plugin.Log.LogInfo($"{_rec.sets.Count} set switchers noted down with their chosen variation");
        }

        // these go dark the moment the tile breaks, so they can't wait for the every-16th-visit activeSelf check
        static void Watch(Transform tf)
        {
            _watchAt.Add(tf.GetInstanceID());
            MarkKeep(tf);
        }

        static void MarkKeep(Transform tf)
        {
            for (var cur = tf; cur is not null; cur = cur.parent)
                if (!_keep.Add(cur.GetInstanceID())) return;
        }

        static void TrackSubtree(Transform tf, string sceneName, int rootIndex)
        {
            if (_nodes.Count >= MAX_NODES) return;
            if (_seen.Contains(tf.GetInstanceID())) return;
            if (tf.GetComponentInParent<FallGuysCharacterController>(true) != null) return;
            if (tf.GetComponentInParent<Camera>(true) != null) return;

            var placeable = _placeableAt.Count > 0 ? tf.GetComponentInParent<LevelEditorPlaceableObject>() : null;
            var anchor = placeable == null ? null : placeable.transform;

            _subtree = 0;
            Track(tf, anchor, placeable == null ? null : placeable._GUID.ToString(), null,
                anchor != null ? ReplayWorldPath.Build(tf, anchor, null, 0) : ReplayWorldPath.Build(tf, null, sceneName, rootIndex),
                tf.name, tf.parent == null, _startTime, false);
        }

        static Node TrackSpawnedSubtree(Transform tf, string prefabName, float t)
        {
            if (_nodes.Count >= MAX_NODES) return null;
            if (_seen.Contains(tf.GetInstanceID())) return null;

            ClearIndex();
            foreach (var c in tf.GetComponentsInChildren<ParticleSystem>(true)) if (c != null) _particleAt.Add(c.transform.GetInstanceID());
            foreach (var c in tf.GetComponentsInChildren<FallGuysCharacterController>(true)) if (c != null) _beanAt.Add(c.transform.GetInstanceID());
            foreach (var c in tf.GetComponentsInChildren<PlayerInfoHUDBase>(true)) if (c != null) _hudAt.Add(c.transform.GetInstanceID());
            foreach (var c in tf.GetComponentsInChildren<Levels.Obstacles.COMMON_BlastBall>(true)) if (c != null) _blastAt[c.transform.GetInstanceID()] = c;
            foreach (var c in tf.GetComponentsInChildren<LevelEditorDestructibleObjectResponder>(true)) if (c != null) _destAt[c.transform.GetInstanceID()] = c;
            foreach (var c in tf.GetComponentsInChildren<Levels.TextureUVImageRenderer>(true)) if (c != null) _uvAt[c.transform.GetInstanceID()] = c;
            foreach (var c in tf.GetComponentsInChildren<Levels.Obstacles.COMMON_ConveyorBelt>(true)) if (c != null) _convAt[c.transform.GetInstanceID()] = c;
            foreach (var c in tf.GetComponentsInChildren<Levels.Obstacles.LevelEditorCommonConveyorBelt>(true)) if (c != null) _convAt[c.transform.GetInstanceID()] = c;
            foreach (var c in tf.GetComponentsInChildren<Levels.Powerups.COMMON_PowerupPickup>(true)) if (c != null) _pickAt[c.transform.GetInstanceID()] = c;
            foreach (var c in tf.GetComponentsInChildren<Levels.Obstacles.COMMON_RespawningTile>(true)) if (c != null) _tileAt[c.transform.GetInstanceID()] = c;
            foreach (var c in tf.GetComponentsInChildren<Animator>(true)) if (c != null) _animAt[c.transform.GetInstanceID()] = c;
            foreach (var c in tf.GetComponentsInChildren<LevelEditorPlaceableObject>(true)) if (c != null) _placeableAt[c.transform.GetInstanceID()] = c;

            _subtree = 0;
            return Track(tf, null, null, null, "spawn:" + tf.GetInstanceID(), tf.name, true, t, true, prefabName);
        }

        static Node Track(Transform tf, Transform anchor, string guid, string owner, string path, string name, bool sceneRoot, float t, bool keepAll, string forcedPrefab = null)
        {
            if (_nodes.Count >= MAX_NODES || _subtree >= SUBTREE_CAP) return null;
            int id = tf.GetInstanceID();
            if (!keepAll && !_keep.Contains(id)) return null;
            if (!_seen.Add(id)) return null;
            if (_particleAt.Contains(id) || _beanAt.Contains(id) || _hudAt.Contains(id)) return null;

            if (_placeableAt.TryGetValue(id, out var placeable))
            {
                anchor = tf;
                guid = placeable._GUID.ToString();
                path = "";
            }

            var data = new ReplayObject { path = path };
            if (anchor != null)
            {
                data.guid = guid ?? "";
                data.owner = owner ?? "";
            }

            bool spawned = forcedPrefab != null || (anchor == null && sceneRoot && ReplayWorldPath.IsRuntimeInstance(name));
            if (spawned)
            {
                data.prefab = forcedPrefab ?? ReplayWorldPath.BaseName(name);
                if (forcedPrefab != null) data.spawnTime = t;
                _pooled++;
                anchor = tf;
                owner = data.path;
                guid = null;
                path = "";
            }

            _animAt.TryGetValue(id, out var anim);
            _blastAt.TryGetValue(id, out var blast);
            _destAt.TryGetValue(id, out var dest);
            _uvAt.TryGetValue(id, out var uv);
            _convAt.TryGetValue(id, out var conveyor);
            _pickAt.TryGetValue(id, out var pickup);
            _tileAt.TryGetValue(id, out var tile);
            _floorAt.TryGetValue(id, out var floor);
            var node = Add(data, tf, spawned ? tf.GetComponentInChildren<Animator>(true) : anim, blast, dest, uv, conveyor, pickup, tile, floor, spawned, _subtree == 0, t);
            _subtree++;

            string sep = path.Length > 0 ? "/" : "";
            bool childKeepAll = keepAll || anim != null;
            int children = tf.GetChildCount();
            for (int i = 0; i < children; i++)
            {
                var child = tf.GetChild(i);
                string childName = child.name;
                Track(child, anchor, guid, owner, path + sep + i + ":" + ReplayWorldPath.Clean(childName), childName, false, t, childKeepAll);
            }

            return node;
        }

        static Node Add(ReplayObject data, Transform tf, Animator anim, Levels.Obstacles.COMMON_BlastBall blast,
            LevelEditorDestructibleObjectResponder dest, Levels.TextureUVImageRenderer uv, Behaviour conveyor,
            Levels.Powerups.COMMON_PowerupPickup pickup, Levels.Obstacles.COMMON_RespawningTile tile,
            Levels.FloorFall.FloorFall_FallingPlatform floor, bool world, bool seed, float t)
        {
            var go = tf.gameObject;
            var node = new Node
            {
                data = data,
                tf = tf,
                go = go,
                anim = anim,
                hasAnim = anim != null && anim.runtimeAnimatorController != null,
                world = world,
                scale = tf.localScale,
                active = go.activeSelf,
                holdPending = true,
                holdTime = t,
            };
            if (world) tf.GetPositionAndRotation(out node.pos, out node.rot);
            else tf.GetLocalPositionAndRotation(out node.pos, out node.rot);

            if (blast != null) node.blast = blast;
            else if (dest != null) node.dest = dest;
            else if (uv != null) node.uv = uv;
            else if (conveyor != null)
            {
                node.conv = conveyor.TryCast<Levels.Obstacles.COMMON_ConveyorBelt>();
                if (node.conv == null) node.convLe = conveyor.TryCast<Levels.Obstacles.LevelEditorCommonConveyorBelt>();
            }
            else if (pickup != null) node.pick = pickup;
            else if (tile != null) node.tile = tile;
            else if (floor != null) node.floor = floor;

            node.watchActive = world || seed || !node.active || anim != null
                || node.blast is not null || node.dest is not null || node.uv is not null
                || node.conv is not null || node.convLe is not null || node.pick is not null
                || node.tile is not null || node.floor is not null
                || _watchAt.Contains(tf.GetInstanceID());

            if (ReadState(node, out node.blastState))
                data.states.Add(new ReplayObjectState { t = t, state = node.blastState });

            if (node.hasAnim && anim.isActiveAndEnabled)
            {
                var info = anim.GetCurrentAnimatorStateInfo(0);
                node.state = info.shortNameHash;
                node.animTime = info.normalizedTime;
            }

            tf.hasChanged = false;

            if (!string.IsNullOrEmpty(data.prefab))
            {
                data.frames.Add(new ReplayObjectFrame
                {
                    t = t,
                    pos = node.pos,
                    rot = node.rot,
                    scale = node.scale,
                    stateHash = node.state,
                    animTime = node.animTime,
                    active = node.active,
                });
                node.holdPending = false;
                _frames++;
            }

            _nodes.Add(node);
            _rec.worldObjects.Add(data);
            return node;
        }

        static bool ReadState(Node node, out int state)
        {
            state = 0;
            if (node.blast is not null && node.blast.m_CachedPtr != IntPtr.Zero) { state = (int)node.blast._state; return true; }
            if (node.dest is not null && node.dest.m_CachedPtr != IntPtr.Zero) { state = node.dest._isDestroyed ? 0 : node.dest._hitsLeftToDestroy; return true; }
            if (node.uv is not null && node.uv.m_CachedPtr != IntPtr.Zero) { state = ReadUV(node.uv); return true; }
            if (node.conv is not null && node.conv.m_CachedPtr != IntPtr.Zero) { state = Mathf.RoundToInt(node.conv._visualPositionNormalised * SCROLL_Q); return true; }
            if (node.convLe is not null && node.convLe.m_CachedPtr != IntPtr.Zero) { state = Mathf.RoundToInt(node.convLe._currentScroll * SCROLL_Q); return true; }
            if (node.pick is not null && node.pick.m_CachedPtr != IntPtr.Zero) { state = ReplayPowerupPickup.Pack(node.pick); return true; }
            if (node.tile is not null && node.tile.m_CachedPtr != IntPtr.Zero) { state = (int)node.tile.TileState; return true; }
            if (node.floor is not null && node.floor.m_CachedPtr != IntPtr.Zero) { state = node.floor._hasTriggerStarted ? 1 : 0; return true; }
            return false;
        }

        static int ReadUV(Levels.TextureUVImageRenderer uv)
        {
            var block = uv._propertyBlock;
            if (block == null) return 0;
            var offset = block.GetVector(Levels.TextureUVImageRenderer.MainTexUVOffset);
            return ReplayTileUV.Pack(offset.x, offset.y);
        }

        static bool IsBettrFGOwned(Transform tf)
        {
            for (var t = tf; t != null; t = t.parent)
                if (t.name.StartsWith("BetterFG_") || t.name.StartsWith("BettrFG_")) return true;
            return false;
        }

        public static void OnLocalSpawn(GameObject go, float t)
        {
            if (_rec == null || _budgetBlown || go == null || _nodes.Count >= MAX_NODES) return;
            if (_suppressed > 0) return;

            var tf = go.transform;
            int id = tf.GetInstanceID();

            OnLocalDespawn(go, t);
            if (_seen.Contains(id) || IsBettrFGOwned(tf)) return;

            var node = TrackSpawnedSubtree(tf, ReplayWorldPath.BaseName(go.name), t);
            if (node != null) _liveLocal[id] = node;
        }

        public static void OnLocalDespawn(GameObject go, float t)
        {
            if (_rec == null || _liveLocal.Count == 0 || go == null) return;

            int id = go.transform.GetInstanceID();
            if (!_liveLocal.TryGetValue(id, out var node)) return;
            _liveLocal.Remove(id);

            node.closed = true;
            if (node.data.despawnTime < 0f) node.data.despawnTime = t;
        }

        static void SweepNetObjects(float t)
        {
            var manager = GlobalGameStateClient.Instance?.GameStateView?.GetNetObjectManager;
            if (manager == null || manager._netObjects == null) return;

            _aliveThisTick.Clear();
            foreach (var kvp in manager._netObjects)
            {
                var netObj = kvp.Value;
                if (netObj is null) continue;

                IntPtr id = netObj.m_CachedPtr;
                if (id == IntPtr.Zero) continue;
                _aliveThisTick.Add(id);

                if (!_knownNet.Add(id)) continue;
                if (netObj.IsFallGuy) continue;

                var go = netObj.gameObject;
                if (go == null) continue;

                var tf = go.transform;
                if (_seen.Contains(tf.GetInstanceID())) continue;

                var node = TrackSpawnedSubtree(tf, ReplayWorldPath.BaseName(go.name), t);
                if (node != null) _spawnedByNet[id] = node;
            }

            foreach (var kvp in _spawnedByNet)
            {
                var node = kvp.Value;
                if (node.closed || _aliveThisTick.Contains(kvp.Key)) continue;
                node.closed = true;
                node.data.despawnTime = t;
            }
        }

        public static void Sample(float t, float dt)
        {
            if (_rec == null || _budgetBlown) return;

            _sweep += dt;
            if (_sweep >= NET_SWEEP_STEP)
            {
                _sweep = 0f;
                SweepNetObjects(t);
            }

            int count = _nodes.Count;
            if (count == 0) return;

            _slice += dt * WORLD_HZ * count;
            int todo = (int)_slice;
            if (todo <= 0) return;
            if (todo >= count) { todo = count; _slice = 0f; }
            else _slice -= todo;

            for (int i = 0; i < todo; i++)
            {
                if (_cursor >= _nodes.Count) _cursor = 0;
                Visit(_nodes[_cursor++], t);
            }

            if (_frames <= FRAME_BUDGET) return;
            _budgetBlown = true;
            Plugin.Log.LogWarning($"world recording hit {_frames} frames, that's the cap. the rest of the round keeps players but freezes the level");
        }

        static void Visit(Node node, float t)
        {
            if (node.closed) return;

            var tf = node.tf;
            if (tf.m_CachedPtr == IntPtr.Zero)
            {
                node.closed = true;
                if (node.data.despawnTime < 0f) node.data.despawnTime = t;

                if (node.data.frames.Count == 0)
                {
                    node.data.frames.Add(new ReplayObjectFrame
                    {
                        t = node.holdTime,
                        pos = node.pos,
                        rot = node.rot,
                        scale = node.scale,
                        stateHash = node.state,
                        animTime = node.animTime,
                        active = node.active,
                    });
                    _frames++;
                }
                return;
            }

            bool verify = (++node.visits & VERIFY_MASK) == 0;

            if (ReadState(node, out int channel) && channel != node.blastState)
            {
                node.blastState = channel;
                node.data.states.Add(new ReplayObjectState { t = t, state = channel });
            }

            Vector3 pos;
            Quaternion rot;
            if (node.world) tf.GetPositionAndRotation(out pos, out rot);
            else tf.GetLocalPositionAndRotation(out pos, out rot);

            var scale = node.scale;
            if (verify || node.world || tf.hasChanged) scale = tf.localScale;

            bool active = node.active;
            if (node.watchActive || verify)
            {
                active = node.go.activeSelf;
                if (active != node.active) node.watchActive = true;
            }

            if (node.hasAnim)
            {
                if (node.anim.m_CachedPtr == IntPtr.Zero) node.hasAnim = false;
                else if (verify) node.hasAnim = node.anim.runtimeAnimatorController != null;
            }

            int state = node.state;
            float animTime = node.animTime;
            if (node.hasAnim && active)
            {
                var info = node.anim.GetCurrentAnimatorStateInfo(0);
                state = info.shortNameHash;
                animTime = info.normalizedTime;
            }

            float px = pos.x - node.pos.x, py = pos.y - node.pos.y, pz = pos.z - node.pos.z;
            float sx = scale.x - node.scale.x, sy = scale.y - node.scale.y, sz = scale.z - node.scale.z;
            float dot = rot.x * node.rot.x + rot.y * node.rot.y + rot.z * node.rot.z + rot.w * node.rot.w;
            float ad = animTime - node.animTime;

            bool changed = px * px + py * py + pz * pz > POS_EPS * POS_EPS
                || (dot < 0f ? -dot : dot) < ROT_DOT
                || sx * sx + sy * sy + sz * sz > SCALE_EPS * SCALE_EPS
                || active != node.active
                || state != node.state
                || (ad < 0f ? -ad : ad) > ANIM_EPS;

            if (!changed)
            {
                node.holdPending = true;
                node.holdTime = t;
                return;
            }

            if (node.holdPending)
            {
                node.data.frames.Add(new ReplayObjectFrame
                {
                    t = node.holdTime,
                    pos = node.pos,
                    rot = node.rot,
                    scale = node.scale,
                    stateHash = node.state,
                    animTime = node.animTime,
                    active = node.active,
                });
                node.holdPending = false;
                _frames++;
            }

            node.pos = pos;
            node.rot = rot;
            node.scale = scale;
            node.active = active;
            node.state = state;
            node.animTime = animTime;

            node.data.frames.Add(new ReplayObjectFrame
            {
                t = t,
                pos = pos,
                rot = rot,
                scale = scale,
                stateHash = state,
                animTime = animTime,
                active = active,
            });
            _frames++;
        }
    }

    internal class ReplayWorldPlayer
    {
        class Live
        {
            public ReplayObject data;
            public Transform tf;
            public Animator anim;
            public Levels.Obstacles.COMMON_BlastBall blast;
            public LevelEditorDestructibleObjectResponder dest;
            public Levels.TextureUVImageRenderer uv;
            public Levels.Obstacles.COMMON_ConveyorBelt conv;
            public Levels.Obstacles.LevelEditorCommonConveyorBelt convLe;
            public Levels.Powerups.COMMON_PowerupPickup pick;
            public bool world;
            public int cursor;
            public int stateCursor;
            public int stateFailures;
            public bool active;
            public bool spawned;
        }

        struct PinnedBody
        {
            public Rigidbody rb;
            public bool isKinematic;
            public bool useGravity;
            public RigidbodyInterpolation interpolation;
        }

        struct StoppedAnimator
        {
            public Animator anim;
            public float speed;
        }

        readonly ReplayRecording _rec;
        readonly List<Scene> _scenes;
        readonly List<Live> _live = new List<Live>();
        readonly List<GameObject> _spawned = new List<GameObject>();
        readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>();
        readonly List<AsyncOperationHandle<GameObject>> _prefabHandles = new List<AsyncOperationHandle<GameObject>>();
        readonly List<PinnedBody> _pinned = new List<PinnedBody>();
        readonly List<Behaviour> _switchedOff = new List<Behaviour>();
        readonly List<StoppedAnimator> _stopped = new List<StoppedAnimator>();

        public ReplayWorldPlayer(ReplayRecording rec, List<Scene> scenes)
        {
            _rec = rec;
            _scenes = scenes;
        }

        public int Count => _live.Count;

        public IEnumerator Prepare()
        {
            Freeze();

            var found = new Transform[_rec.worldObjects.Count];
            var wanted = new HashSet<string>();
            for (int i = 0; i < _rec.worldObjects.Count; i++)
            {
                var obj = _rec.worldObjects[i];
                if (!string.IsNullOrEmpty(obj.path) || !string.IsNullOrEmpty(obj.guid))
                    found[i] = ReplayWorldPath.Resolve(obj.path, obj.guid, _scenes);
                if (found[i] == null && !string.IsNullOrEmpty(obj.prefab)) wanted.Add(obj.prefab);
            }

            if (wanted.Count > 0) yield return LoadPrefabs(wanted);

            int missing = 0;
            var resolved = new Transform[_rec.worldObjects.Count];
            var isClone = new bool[_rec.worldObjects.Count];
            var owners = new Dictionary<string, Transform>();

            for (int i = 0; i < _rec.worldObjects.Count; i++)
            {
                var obj = _rec.worldObjects[i];
                var tf = found[i];

                if (tf == null && !string.IsNullOrEmpty(obj.owner) && owners.TryGetValue(obj.owner, out var ownerTf))
                {
                    tf = ownerTf;
                    foreach (var segment in obj.path.Split('/'))
                    {
                        int colon = segment.IndexOf(':');
                        string name = colon > 0 ? segment.Substring(colon + 1) : segment;

                        Transform next = null;
                        for (int c = 0; c < tf.childCount; c++)
                            if (tf.GetChild(c).name == name) { next = tf.GetChild(c); break; }

                        if (next == null) { tf = null; break; }
                        tf = next;
                    }
                }

                if (tf == null)
                {
                    if (string.IsNullOrEmpty(obj.prefab) || !_prefabs.TryGetValue(obj.prefab, out var prefab))
                    {
                        missing++;
                        continue;
                    }
                    var clone = UnityEngine.Object.Instantiate(prefab);
                    clone.name = "BettrFG_ReplayObject_" + obj.prefab;
                    _spawned.Add(clone);
                    tf = clone.transform;
                    isClone[i] = true;

                    foreach (var rb in tf.GetComponentsInChildren<Rigidbody>(true)) Pin(rb);
                    ReplayWorldDrivers.ForEachSimulated(clone, SwitchOff);
                    foreach (var c in clone.GetComponentsInChildren<FG.Common.CarryObject>(true)) SwitchOff(c);
                    foreach (var c in clone.GetComponentsInChildren<Levels.Obstacles.COMMON_SelfRespawner>(true)) SwitchOff(c);
                    clone.SetActive(true);
                    owners[obj.path] = tf;
                }
                resolved[i] = tf;
            }

            yield return null;
            yield return null;

            for (int i = 0; i < _rec.worldObjects.Count; i++)
            {
                var tf = resolved[i];
                if (tf == null) continue;

                var obj = _rec.worldObjects[i];
                bool spawned = isClone[i];

                var anim = spawned ? tf.GetComponentInChildren<Animator>(true) : tf.GetComponent<Animator>();
                if (anim != null) Stop(anim);

                if (!spawned)
                {
                    foreach (var rb in tf.GetComponentsInChildren<Rigidbody>(true)) Pin(rb);
                    ReplayWorldDrivers.ForEachSimulated(tf.gameObject, SwitchOff);
                }

                obj.live = tf;
                _live.Add(new Live
                {
                    data = obj,
                    tf = tf,
                    anim = anim,
                    blast = obj.states.Count > 0 ? tf.GetComponent<Levels.Obstacles.COMMON_BlastBall>() : null,
                    dest = obj.states.Count > 0 ? tf.GetComponent<LevelEditorDestructibleObjectResponder>() : null,
                    uv = obj.states.Count > 0 ? tf.GetComponent<Levels.TextureUVImageRenderer>() : null,
                    conv = obj.states.Count > 0 ? tf.GetComponent<Levels.Obstacles.COMMON_ConveyorBelt>() : null,
                    convLe = obj.states.Count > 0 ? tf.GetComponent<Levels.Obstacles.LevelEditorCommonConveyorBelt>() : null,
                    pick = obj.states.Count > 0 ? tf.GetComponent<Levels.Powerups.COMMON_PowerupPickup>() : null,
                    world = !string.IsNullOrEmpty(obj.prefab),
                    active = tf.gameObject.activeSelf,
                    spawned = spawned,
                    stateCursor = -1,
                });
            }

            Plugin.Log.LogInfo($"replay world: {_live.Count} objects hooked up, {_spawned.Count} rebuilt from prefabs, {missing} couldn't be found");
        }

        void Freeze()
        {
            int bodies = 0;
            int drivers = 0;
            foreach (var scene in _scenes)
            {
                if (!scene.IsValid() || !scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root == null || root.name.StartsWith("BetterFG_") || root.name.StartsWith("BettrFG_")) continue;

                    foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
                    {
                        Pin(rb);
                        bodies++;
                    }
                    ReplayWorldDrivers.ForEachSimulated(root, driver =>
                    {
                        if (!driver.enabled) return;
                        SwitchOff(driver);
                        drivers++;
                    });
                }
            }
            Plugin.Log.LogInfo($"level frozen for playback: {bodies} rigidbodies pinned, {drivers} seesaws/rotators/platforms/bots switched off");
        }

        IEnumerator LoadPrefabs(HashSet<string> wanted)
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (candidate == null || !wanted.Contains(candidate.name)) continue;
                if (_prefabs.TryGetValue(candidate.name, out var have) && !have.activeInHierarchy) continue;
                _prefabs[candidate.name] = candidate;
            }

            if (_prefabs.Count > 0)
                Plugin.Log.LogInfo($"{_prefabs.Count}/{wanted.Count} spawned object types picked straight out of memory");
            if (_prefabs.Count >= wanted.Count) yield break;

            var cfg = GlobalGameStateClient.Instance?._configuration;
            if (cfg == null || cfg.sceneNetworkPrefabs == null)
            {
                Plugin.Log.LogWarning($"no network prefab config around, {wanted.Count - _prefabs.Count} spawned object types stay missing");
                yield break;
            }

            for (int pass = 0; pass < 2 && _prefabs.Count < wanted.Count; pass++)
            {
                foreach (var group in cfg.sceneNetworkPrefabs)
                {
                    if (group == null || group.networkPrefabAssetRefs == null) continue;

                    bool ours = group.sceneName == _rec.sceneName
                        || group.sceneName == "FallGuy_Shared"
                        || group.sceneName == FraggleCommonManager.BootstrapSceneName;
                    if (ours != (pass == 0)) continue;

                    foreach (var aref in group.networkPrefabAssetRefs)
                    {
                        if (aref == null || !aref.RuntimeKeyIsValid()) continue;
                        var handle = Addressables.LoadAssetAsync<GameObject>(aref.RuntimeKey);
                        while (!handle.IsDone) yield return null;
                        if (handle.Status != AsyncOperationStatus.Succeeded) continue;

                        var prefab = handle.Result;
                        if (prefab == null || !wanted.Contains(prefab.name))
                        {
                            Addressables.Release(handle);
                            continue;
                        }

                        _prefabs[prefab.name] = prefab;
                        _prefabHandles.Add(handle);
                        if (_prefabs.Count >= wanted.Count) yield break;
                    }
                }
            }

            if (_prefabs.Count >= wanted.Count) yield break;
            foreach (var name in wanted)
                if (!_prefabs.ContainsKey(name))
                    Plugin.Log.LogWarning($"no network prefab called '{name}' anywhere in the config, those spawns stay missing from the replay");
        }

        public void Apply(float time)
        {
            for (int i = 0; i < _live.Count; i++)
            {
                var live = _live[i];
                var frames = live.data.frames;
                if (live.tf == null) continue;

                if (frames.Count == 0)
                {
                    if (live.blast != null) DriveBlastBall(live, time);
                    else if (live.dest != null) DriveDestructible(live, time, live.tf.position);
                    else if (live.uv != null) DriveTileImage(live, time);
                    else if (live.pick != null) DrivePowerupPickup(live, time);
                    else DriveConveyor(live, time);
                    continue;
                }

                bool alive = time >= live.data.spawnTime
                    && (live.data.despawnTime < 0f || time < live.data.despawnTime);

                int c = live.cursor;
                if (c >= frames.Count || frames[c].t > time) c = 0;
                while (c + 1 < frames.Count && frames[c + 1].t <= time) c++;
                live.cursor = c;

                var a = frames[c];
                var pos = a.pos;
                var rot = a.rot;
                var scale = a.scale;
                float animTime = a.animTime;

                if (c + 1 < frames.Count)
                {
                    var b = frames[c + 1];
                    float den = b.t - a.t;
                    float f = den > 0f ? Mathf.Clamp01((time - a.t) / den) : 0f;
                    pos = Vector3.Lerp(a.pos, b.pos, f);
                    rot = Quaternion.Slerp(a.rot, b.rot, f);
                    scale = Vector3.Lerp(a.scale, b.scale, f);
                    if (b.stateHash == a.stateHash) animTime = Mathf.Lerp(a.animTime, b.animTime, f);
                }

                bool active = a.active && alive;
                if (active != live.active)
                {
                    live.tf.gameObject.SetActive(active);
                    live.active = active;
                }
                if (!active) continue;

                if (live.world) live.tf.SetPositionAndRotation(pos, rot);
                else
                {
                    live.tf.localPosition = pos;
                    live.tf.localRotation = rot;
                }
                live.tf.localScale = scale;

                if (live.blast != null) DriveBlastBall(live, time);
                else if (live.dest != null) DriveDestructible(live, time, pos);
                else if (live.uv != null) DriveTileImage(live, time);
                else if (live.pick != null) DrivePowerupPickup(live, time);
                else DriveConveyor(live, time);

                if (live.anim == null || a.stateHash == 0 || live.anim.runtimeAnimatorController == null) continue;
                live.anim.Play(a.stateHash, 0, animTime);
                live.anim.Update(0f);
            }
        }

        void DriveBlastBall(Live live, float time)
        {
            var states = live.data.states;

            int want = -1;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i].t > time) break;
                want = i;
            }

            if (want == live.stateCursor) return;
            if (want < 0) { live.stateCursor = -1; return; }

            bool first = live.stateCursor < 0;
            live.stateCursor = want;
            if (first && want == 0) return;

            int state = states[want].state;

            // StartBlastSequence goes out over the network and dies quietly with no server, so the ball
            // never reached Exploded and the explosion vfx never played. drive the states by hand instead
            try
            {
                switch (state)
                {
                    case 1: live.blast.AdvanceToActivated(Time.time); break;
                    case 2: live.blast.AdvanceToReadyToExplode(); break;
                    case 3: live.blast.AdvanceToExploded(); break;
                    default: if (!first) live.blast.ResetToPrimed(); break;
                }
            }
            catch (Exception ex)
            {
                if (++live.stateFailures >= 4)
                {
                    Plugin.Log.LogWarning($"blast ball keeps throwing on state {state} ({ex.Message}), leaving it alone now");
                    live.blast = null;
                }
            }
        }

        void DrivePowerupPickup(Live live, float time)
        {
            var states = live.data.states;

            int want = -1;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i].t > time) break;
                want = i;
            }

            if (want == live.stateCursor || want < 0) return;
            live.stateCursor = want;

            ReplayPowerupPickup.Unpack(states[want].state, out int state, out uint id);

            try
            {
                // the pop sfx is already in the recording, so the visuals go up silently
                if (state != 0) { live.pick.ClearVisuals(false); return; }

                var powerup = live.pick.GetPowerupSOFromPool(id);
                if (powerup != null) live.pick.SetVisualsForPowerup(powerup);
            }
            catch (Exception ex)
            {
                if (++live.stateFailures >= 4)
                {
                    Plugin.Log.LogWarning($"powerup spawner keeps throwing on state {state} ({ex.Message}), leaving it alone now");
                    live.pick = null;
                }
            }
        }

        void DriveDestructible(Live live, float time, Vector3 pos)
        {
            var states = live.data.states;

            int want = -1;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i].t > time) break;
                want = i;
            }

            if (want == live.stateCursor || want < 0) return;

            bool first = live.stateCursor < 0;
            int before = first ? states[0].state : states[live.stateCursor].state;
            int hits = states[want].state;
            live.stateCursor = want;
            if (first && want == 0) return;

            try
            {
                if (hits > before)
                {
                    live.dest.ResetObstacleCompletely();
                    return;
                }

                for (int i = before; i > hits; i--)
                    live.dest.TriggerHit_Local(pos, Vector3.up, 0u);
            }
            catch (Exception ex)
            {
                if (++live.stateFailures >= 4)
                {
                    Plugin.Log.LogWarning($"destructible keeps throwing on {hits} hits left ({ex.Message}), leaving it alone now");
                    live.dest = null;
                }
            }
        }

        void DriveTileImage(Live live, float time)
        {
            var states = live.data.states;

            int want = -1;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i].t > time) break;
                want = i;
            }

            if (want == live.stateCursor || want < 0) return;
            live.stateCursor = want;

            ReplayTileUV.Unpack(states[want].state, out float u, out float v);
            live.uv.SetImage(u, v);
        }

        void DriveConveyor(Live live, float time)
        {
            if (live.conv == null && live.convLe == null) return;

            var states = live.data.states;
            if (states.Count == 0) return;

            int c = live.stateCursor;
            if (c < 0 || c >= states.Count || states[c].t > time) c = 0;
            while (c + 1 < states.Count && states[c + 1].t <= time) c++;
            live.stateCursor = c;

            float scroll = states[c].state / ReplayWorldRecorder.SCROLL_Q;
            if (c + 1 < states.Count)
            {
                float next = states[c + 1].state / ReplayWorldRecorder.SCROLL_Q;
                float den = states[c + 1].t - states[c].t;
                if (den > 0f && Mathf.Abs(next - scroll) <= 0.5f)
                    scroll = Mathf.Lerp(scroll, next, Mathf.Clamp01((time - states[c].t) / den));
            }

            if (live.conv != null)
            {
                live.conv._visualPositionNormalised = scroll;
                live.conv.ScrollConveyorVisuals();
            }
            else
            {
                live.convLe._currentScroll = scroll;
                live.convLe.ScrollConveyorVisuals();
            }
        }

        void Pin(Rigidbody rb)
        {
            _pinned.Add(new PinnedBody { rb = rb, isKinematic = rb.isKinematic, useGravity = rb.useGravity, interpolation = rb.interpolation });
            rb.interpolation = RigidbodyInterpolation.None;
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        void SwitchOff(Behaviour driver)
        {
            if (!driver.enabled) return;
            _switchedOff.Add(driver);
            driver.enabled = false;
        }

        void Stop(Animator anim)
        {
            _stopped.Add(new StoppedAnimator { anim = anim, speed = anim.speed });
            anim.speed = 0f;
        }

        public void Release()
        {
            foreach (var go in _spawned)
                if (go != null) UnityEngine.Object.Destroy(go);
            _spawned.Clear();

            int bodies = 0, drivers = 0, animators = 0;

            foreach (var body in _pinned)
            {
                if (body.rb == null) continue;
                body.rb.isKinematic = body.isKinematic;
                body.rb.useGravity = body.useGravity;
                body.rb.interpolation = body.interpolation;
                bodies++;
            }
            foreach (var driver in _switchedOff)
            {
                if (driver == null) continue;
                driver.enabled = true;
                drivers++;
            }
            foreach (var stopped in _stopped)
            {
                if (stopped.anim == null) continue;
                stopped.anim.speed = stopped.speed;
                animators++;
            }

            int released = _prefabHandles.Count;
            foreach (var handle in _prefabHandles) if (handle.IsValid()) Addressables.Release(handle);
            _prefabHandles.Clear();

            foreach (var obj in _rec.worldObjects) obj.live = null;

            _pinned.Clear();
            _switchedOff.Clear();
            _stopped.Clear();
            _live.Clear();
            _prefabs.Clear();

            Plugin.Log.LogInfo($"world handed back: {bodies} rigidbodies unpinned, {drivers} scripts switched back on, {animators} animators running again, {released} prefab handles released");
        }
    }

    internal class ReplayStarchartPlayer
    {
        readonly ReplayRecording _rec;
        readonly List<Scene> _scenes;
        readonly List<Levels.Starlink.StarlinkMapWalkway> _lit = new List<Levels.Starlink.StarlinkMapWalkway>();
        int _cursor;

        public ReplayStarchartPlayer(ReplayRecording rec, List<Scene> scenes)
        {
            _rec = rec;
            _scenes = scenes;
        }

        public void Seek(float time)
        {
            var events = _rec.starchartEvents;
            int i = 0;
            while (i < events.Count && events[i].t <= time) i++;
            _cursor = i;
            Sync(i);
        }

        public void Advance(float from, float to)
        {
            var events = _rec.starchartEvents;
            if (events.Count == 0) return;
            if (to < from) { Seek(to); return; }

            while (_cursor < events.Count && events[_cursor].t <= to)
            {
                Light(events[_cursor]);
                _cursor++;
            }
        }

        void Sync(int eventCount)
        {
            var wanted = new List<Levels.Starlink.StarlinkMapWalkway>();
            for (int e = 0; e < eventCount; e++) Collect(_rec.starchartEvents[e], wanted);

            foreach (var w in _lit)
                if (w != null && !wanted.Contains(w)) w.HideWalkway();
            foreach (var w in wanted)
                if (w != null && !_lit.Contains(w)) w.LightUpWalkway();

            _lit.Clear();
            _lit.AddRange(wanted);
        }

        void Light(ReplayStarchartEvent ev)
        {
            for (int i = 0; i < ev.pathCount; i++)
            {
                var w = Resolve(_rec.starchartPaths[ev.pathStart + i]);
                if (w == null || _lit.Contains(w)) continue;
                w.LightUpWalkway();
                _lit.Add(w);
            }
        }

        void Collect(ReplayStarchartEvent ev, List<Levels.Starlink.StarlinkMapWalkway> into)
        {
            for (int i = 0; i < ev.pathCount; i++)
            {
                var w = Resolve(_rec.starchartPaths[ev.pathStart + i]);
                if (w != null) into.Add(w);
            }
        }

        Levels.Starlink.StarlinkMapWalkway Resolve(string path)
        {
            var tf = ReplayWorldPath.Resolve(path, "", _scenes);
            return tf != null ? tf.GetComponent<Levels.Starlink.StarlinkMapWalkway>() : null;
        }
    }

    internal class ReplayVfxPlayer
    {
        readonly ReplayRecording _rec;
        readonly Dictionary<uint, FG.Common.Character.FallGuyVFXController> _controllers;
        int _cursor;

        public ReplayVfxPlayer(ReplayRecording rec, Dictionary<uint, FG.Common.Character.FallGuyVFXController> controllers)
        {
            _rec = rec;
            _controllers = controllers;
        }

        public void Seek(float time)
        {
            var events = _rec.diveSlideVfxEvents;
            int i = 0;
            while (i < events.Count && events[i].t <= time) i++;
            _cursor = i;
        }

        public void Advance(float from, float to)
        {
            var events = _rec.diveSlideVfxEvents;
            if (events.Count == 0) return;
            if (to < from) { Seek(to); return; }

            while (_cursor < events.Count && events[_cursor].t <= to)
            {
                var ev = events[_cursor];
                _cursor++;
                if (ev.t > from && _controllers.TryGetValue(ev.playerId, out var vfx) && vfx != null)
                    vfx.HandleOnDive(new FG.Common.Character.VfxDiveEvent());
            }
        }
    }

    // the chicken and the invisibility shimmer are the two powerup looks nothing else in the replay
    // carries: the chicken hangs off the bean's own rig and the shimmer is a shader swap, so neither
    // shows up in the player frames or the world objects. bean ball rides the bean's animator state
    // and the rhino is a net object, both already recorded
    internal class ReplayPowerupPlayer
    {
        class Track
        {
            public uint playerId;
            public GameObject chicken;
            public Levels.Invisibeans.InvisibilityVisualsController invis;
            public bool chickenOn;
            public bool invisOn;
        }

        readonly ReplayRecording _rec;
        readonly List<Track> _tracks = new List<Track>();

        public ReplayPowerupPlayer(ReplayRecording rec)
        {
            _rec = rec;
        }

        public int AttachedCount => _tracks.Count;

        public void Attach(uint playerId, GameObject bean)
        {
            if (_rec.powerupEvents.Count == 0) return;

            var controller = bean.GetComponentInChildren<RubberChickenController>(true);
            var track = new Track
            {
                playerId = playerId,
                chicken = controller != null ? controller.RubberChickenObject : null,
                invis = bean.GetComponentInChildren<Levels.Invisibeans.InvisibilityVisualsController>(true),
            };
            if (track.chicken == null && track.invis == null) return;

            if (track.chicken != null) track.chicken.SetActive(false);
            _tracks.Add(track);
        }

        public void Apply(float time)
        {
            var events = _rec.powerupEvents;
            foreach (var track in _tracks)
            {
                bool chicken = false;
                bool invis = false;
                for (int i = 0; i < events.Count; i++)
                {
                    var e = events[i];
                    if (e.playerId != track.playerId || e.t > time) continue;
                    if (e.kind == (int)ReplayPowerupKind.RubberChicken) chicken = e.on;
                    else invis = e.on;
                }

                if (chicken != track.chickenOn && track.chicken != null)
                {
                    track.chickenOn = chicken;
                    track.chicken.SetActive(chicken);
                }

                if (invis == track.invisOn || track.invis == null) continue;
                track.invisOn = invis;
                try
                {
                    if (invis) track.invis.EnablePowerupVersion();
                    else track.invis.DisablePowerupVersion();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"invisibility wouldn't {(invis ? "go on" : "come off")} for player {track.playerId}, dropping it: {ex.Message}");
                    track.invis = null;
                }
            }
        }
    }

    internal class ReplayTailPlayer
    {
        class Track
        {
            public uint playerId;
            public Transform tail;
            public bool active;
        }

        readonly ReplayRecording _rec;
        readonly List<Track> _tracks = new List<Track>();
        bool _moaned;

        public ReplayTailPlayer(ReplayRecording rec)
        {
            _rec = rec;
        }

        public void Attach(uint playerId, GameObject bean)
        {
            if (_rec.tailEvents.Count == 0) return;

            var accessory = bean.GetComponentInChildren<Levels.Tag.TailTagAccessory>(true);
            if (accessory == null)
            {
                if (!_moaned)
                {
                    Plugin.Log.LogWarning($"replay bean has no TailTagAccessory on it, so the {_rec.tailEvents.Count} tail events in this replay have nothing to show");
                    _moaned = true;
                }
                return;
            }

            var root = accessory.gameObject;
            var tail = accessory._tail;
            UnityEngine.Object.Destroy(accessory);

            if (tail == null) tail = root;
            else root.SetActive(true);
            tail.SetActive(false);

            _tracks.Add(new Track { playerId = playerId, tail = tail.transform });
        }

        public int AttachedCount => _tracks.Count;

        public void Apply(float time)
        {
            var events = _rec.tailEvents;
            foreach (var track in _tracks)
            {
                if (track.tail == null) continue;

                bool on = false;
                for (int i = 0; i < events.Count; i++)
                {
                    var e = events[i];
                    if (e.playerId != track.playerId || e.t > time) continue;
                    on = e.enabled;
                }

                if (on == track.active) continue;
                track.active = on;
                track.tail.gameObject.SetActive(on);
            }
        }
    }
}
