using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Features.UnityRound.Behaviours;
using FG.Common;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Levels.Progression;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BetterFG.Features.UnityRound
{
    // Handles all post-instantiation modifications to the active round.
    // Called by BetterFGUnityRounds after the prefab is in the scene.

    public static class BetterFGRoundPostmodifier
    {
        public static void Apply(GameObject round, MapInfo info, ref Material pendingSkybox)
        {
            _liveInfo = info;
            _liveSkybox = pendingSkybox;
            ApplyEnvironment(info, ref pendingSkybox);
            // spawnpoint -> CheckpointZone injection removed; checkpoints come from the level editor now
            //RemapStartingPositions(round);
            RemapPhysicMaterials(round);
            DisableLightProbesOnAllRenderers();
            RegisterEndgoals(round);
            BetterFGUnityRounds.Instance.StartCoroutine(DisableCreativeModeObjectsSoon(
                info != null && info.keepExistingObjects, info != null && info.hideFinishLines,
                info != null && info.hideBackground, info != null && info.hideThemeLighting).WrapToIl2Cpp());
            RegisterMantleTargets(round);
            FreezeAnimations(round);
            UnityRoundAnchors.Sync();
        }

        private static MapInfo _liveInfo;
        private static Material _liveSkybox;

        // the vanilla Background_ root's own LevelEditorThemeLighting.Apply overwrites RenderSettings
        // with its own skybox/ambient after ours; CustomBackgroundThemeLightingPatch calls this the
        // moment that happens so ours wins back, instead of guessing how many frames to wait.
        public static void ReassertEnvironmentIfLoaded()
        {
            if (_liveInfo == null || !_liveInfo.hideBackground) return;
            var skybox = _liveSkybox;
            ApplyEnvironment(_liveInfo, ref skybox);
        }

        public static void ClearLiveEnvironment()
        {
            _liveInfo = null;
            _liveSkybox = null;
        }

        private static readonly System.Collections.Generic.List<Animator> _animators
            = new System.Collections.Generic.List<Animator>();
        private static readonly System.Collections.Generic.List<Animation> _animations
            = new System.Collections.Generic.List<Animation>();

        public static void FreezeAnimations(GameObject round)
        {
            _animators.Clear();
            _animations.Clear();
            if (round == null) return;

            foreach (var a in round.GetComponentsInChildren<Animator>(true))
            {
                _animators.Add(a);
                a.Rebind();
                a.Update(0f);
                a.enabled = false;
            }

            foreach (var a in round.GetComponentsInChildren<Animation>(true))
            {
                _animations.Add(a);
                a.Rewind();
                a.Stop();
            }
        }

        public static void StartAnimations()
        {
            foreach (var a in _animators)
            {
                if (a == null) continue;
                a.enabled = true;
                a.Rebind();
                a.Update(0f);
            }

            foreach (var a in _animations)
            {
                if (a == null) continue;
                a.Rewind();
                a.Play();
            }
        }

        public static void RefreezeAnimations()
        {
            foreach (var a in _animators)
            {
                if (a == null) continue;
                a.Rebind();
                a.Update(0f);
                a.enabled = false;
            }

            foreach (var a in _animations)
            {
                if (a == null) continue;
                a.Rewind();
                a.Stop();
            }
        }

        // the Background_ roots aren't in the scene yet the frame the round goes in, so disabling right
        // here found nothing
        private static IEnumerator DisableCreativeModeObjectsSoon(bool keepExisting, bool hideFinishLines, bool hideBackground, bool hideThemeLighting)
        {
            for (int i = 0; i < 5; i++) yield return null;
            DisableCreativeModeObjects(disablePlaceables: true, keepExisting: keepExisting, hideFinishLines: hideFinishLines,
                hideBackground: hideBackground, hideThemeLighting: hideThemeLighting);
        }

        private static void ApplyEnvironment(MapInfo info, ref Material pendingSkybox)
        {
            if (info == null) return;

            RenderSettings.ambientMode = info.ambientMode;
            RenderSettings.ambientLight = info.ambientLight;
            RenderSettings.reflectionIntensity = info.reflectionIntensity;
            RenderSettings.fog = info.fog;
            RenderSettings.fogDensity = info.fogDensity;
            RenderSettings.fogColor = info.fogColor;

            if (pendingSkybox != null)
            {
                RenderSettings.skybox = pendingSkybox;
                pendingSkybox = null;
            }

        }

        // spawnpoint -> CheckpointZone injection commented out; checkpoints now come from the level editor.
        /*
        // Creates a CheckpointZonePositions on the spawnpoints child,
        // fills its Locators, injects it into CheckpointManager._checkpointZones.
        private static void RemapStartingPositions(GameObject round)
        {
            if (round == null) return;

            Transform spawnRoot = FindSpawnpoints(round.transform);
            if (spawnRoot == null)
            {
                Plugin.Log.LogWarning("BetterFGRoundPostmodifier: no spawnpoints child found, skipping remap");
                return;
            }

            var locators = new Il2CppReferenceArray<Transform>(spawnRoot.childCount);
            for (int i = 0; i < spawnRoot.childCount; i++) locators[i] = spawnRoot.GetChild(i);

            if (locators.Count == 0)
            {
                Plugin.Log.LogWarning("BetterFGRoundPostmodifier: spawnpoints has no children");
                return;
            }

            spawnRoot.gameObject.AddComponent<BoxCollider>();
            var zone = spawnRoot.gameObject.AddComponent<CheckpointZonePositions>();
            zone.Locators = locators;

            CheckpointManager cm = null;
            int sceneCount = SceneManager.sceneCount;
            for (int si = 0; si < sceneCount; si++)
            {
                var s = SceneManager.GetSceneAt(si);
                if (!s.isLoaded) continue;
                foreach (var root in s.GetRootGameObjects())
                {
                    cm = root.GetComponentInChildren<CheckpointManager>(includeInactive: true);
                    if (cm != null) break;
                }
                if (cm != null) break;
            }

            if (cm == null)
            {
                Plugin.Log.LogWarning("BetterFGRoundPostmodifier: CheckpointManager not found");
                return;
            }

            var existing = cm._checkpointZones;
            int oldLen = (existing != null && existing.Count > 0) ? existing.Count : 0;
            var newZones = new Il2CppReferenceArray<CheckpointZone>(oldLen + 1);
            for (int i = 0; i < oldLen; i++) newZones[i] = existing[i];
            newZones[oldLen] = zone;
            cm._checkpointZones = newZones;

            Plugin.Log.LogInfo($"BetterFGRoundPostmodifier: injected zone with {locators.Count} locators (was {oldLen} zones)");
        }

        private static Transform FindSpawnpoints(Transform t)
        {
            if (t.name.ToLower() == "spawnpoints") return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var found = FindSpawnpoints(t.GetChild(i));
                if (found != null) return found;
            }
            return null;
        }
        */

        // Walks all colliders in the round and swaps our custom physic materials
        // with the game's real ones from SurfaceDefinitions.SurfaceModifiers.
        // index 0 = Slippery(Slide-able), 1 = Ice(Slide-able), 2 = Ice, 3 = Slippery
        private static void RemapPhysicMaterials(GameObject round)
        {
            if (round == null) return;

            var pm = UnityEngine.Object.FindObjectOfType<PhysicsManager>();
            if (pm == null)
            {
                Plugin.Log.LogWarning("BetterFGRoundPostmodifier: PhysicsManager not found, skipping physic material remap");
                return;
            }

            PhysicMaterial playerSlip = null, ice = null, iceTiles = null, slime = null;
            foreach (var key in pm._physicsSurfaceDict.Keys)
            {
                switch (key.name)
                {
                    case "PlayerSlip": playerSlip = key; break;
                    case "Ice": ice = key; break;
                    case "IceTiles": iceTiles = key; break;
                    case "Slime": slime = key; break;
                }
            }
            foreach (var col in round.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                if (col.material == null) continue;
                string matName = col.material.name;

                PhysicMaterial replacement = null;
                if (matName.Contains("Slippery") && matName.Contains("Slide")) replacement = slime;
                else if (matName.Contains("Ice") && matName.Contains("Slide")) replacement = ice;
                else if (matName.Contains("Ice")) replacement = iceTiles;
                else if (matName.Contains("Slippery")) replacement = playerSlip;

                if (replacement == null) continue;
                col.material = replacement;
            }

        }

        // exactly what we switched off, so restore is precise + symmetric
        private static readonly System.Collections.Generic.List<GameObject> _disabled
            = new System.Collections.Generic.List<GameObject>();

        // finish lines we ghosted instead of disabling, with the materials they had before
        private static readonly System.Collections.Generic.List<(Renderer r, Il2CppReferenceArray<Material> mats)> _ghosted
            = new System.Collections.Generic.List<(Renderer, Il2CppReferenceArray<Material>)>();

        // Operates only on scene 0's ROOT objects (no deep recursion - that was breaking stuff).
        // hideBackground owns the Background_ roots; keepExisting owns the Placeable_/PB_ roots.
        public static void DisableCreativeModeObjects(bool disablePlaceables, bool keepExisting = false, bool hideFinishLines = false,
            bool hideBackground = false, bool hideThemeLighting = false)
        {
            RestoreCreativeModeObjects();

            var s = SceneManager.GetSceneAt(0);
            if (!s.isLoaded) return;

            foreach (var root in s.GetRootGameObjects())
                ApplyRules(root, disablePlaceables, keepExisting, hideFinishLines, hideBackground, hideThemeLighting);
        }

        public static void ApplyToNewObject(GameObject root)
        {
            if (root == null || !Editor.UnityRoundLoader.HasSpawned) return;
            ApplyRules(root, true, Editor.UnityRoundLoader.KeepExistingObjects, Editor.UnityRoundLoader.HideFinishLines,
                Editor.UnityRoundLoader.HideBackground, Editor.UnityRoundLoader.HideThemeLighting);
        }

        private static void ApplyRules(GameObject root, bool disablePlaceables, bool keepExisting, bool hideFinishLines,
            bool hideBackground, bool hideThemeLighting)
        {
            string n = root.name;

            if (n.StartsWith("Background_"))
            {
                if (hideThemeLighting) Disable(root.transform.Find("LIGHTING"));
                if (!hideBackground) return;
                BetterFG.Tweaks.Background3dTweak.Cancel();
                Disable(root);
                return;
            }

            bool isPlaceable = n.StartsWith("Placeable_") || n.StartsWith("PB_");
            bool isFinishLine = isPlaceable
                && n.IndexOf("Floor", StringComparison.OrdinalIgnoreCase) >= 0
                && n.IndexOf("End", StringComparison.OrdinalIgnoreCase) >= 0;

            if (keepExisting && hideFinishLines && isFinishLine)
            {
                if (Editor.UnityRoundLoader.EditorPreview) Ghost(root); else HideRenderers(root);
                return;
            }

            if (keepExisting || !disablePlaceables || !isPlaceable) return;

            // keep checkpoints / spawnpoints alive so the round stays playable, and the Unity
            // Round Core gizmo alive so it can still be selected to configure the loaded round
            bool keep = n.IndexOf("checkpoint", StringComparison.OrdinalIgnoreCase) >= 0
                     || n.IndexOf("spawnpoint", StringComparison.OrdinalIgnoreCase) >= 0
                     || BetterFG.Features.CreativeExpansion.CustomLights.IsLightGO(root)
                     || BetterFG.Features.UnityRound.UnityRoundCoreObject.IsCore(root.GetComponent<LevelEditorPlaceableObject>())
                     || UnityRoundAnchors.IsAnchor(root.GetComponent<LevelEditorPlaceableObject>())
                     || BetterFG.UI.Windows.Creative.BatchLink.IsController(root.GetComponent<LevelEditorPlaceableObject>());
            if (keep) return;

            if (isFinishLine)
            {
                if (Editor.UnityRoundLoader.EditorPreview) Ghost(root); else HideRenderers(root);
                return;
            }

            Disable(root);
        }

        private static readonly System.Collections.Generic.List<Renderer> _hiddenRenderers
            = new System.Collections.Generic.List<Renderer>();

        private static void HideRenderers(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (r.forceRenderingOff) continue;
                r.forceRenderingOff = true;
                _hiddenRenderers.Add(r);
            }
        }

        private static void Ghost(GameObject root)
        {
            var mat = Core.AssetManager.GhostMaterial;
            if (mat == null) { Plugin.Log.LogWarning($"no ghost material, leaving {root.name} as-is"); return; }

            foreach (var r in root.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                // load-over-load: already ghosted from the last round, don't record the ghost mat as its original
                if (r.sharedMaterial == mat) continue;

                var old = r.sharedMaterials;
                var mats = new Il2CppReferenceArray<Material>(old.Length);
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
                _ghosted.Add((r, old));
            }
        }

        // Re-enables exactly what DisableCreativeModeObjects switched off, and un-ghosts the finish lines.
        public static void RestoreCreativeModeObjects()
        {
            foreach (var go in _disabled)
                if (go != null) go.SetActive(true);
            _disabled.Clear();

            foreach (var (r, mats) in _ghosted)
                if (r != null) r.sharedMaterials = mats;
            _ghosted.Clear();

            foreach (var r in _hiddenRenderers)
                if (r != null) r.forceRenderingOff = false;
            _hiddenRenderers.Clear();
        }

        private static void Disable(Transform t) { if (t != null) Disable(t.gameObject); }
        private static void Disable(GameObject go)
        {
            if (go == null) return;
            go.SetActive(false);
            _disabled.Add(go);
        }

        private static void RegisterMantleTargets(GameObject round)
        {
            if (round == null) return;

            var rng = new System.Random();
            foreach (var col in round.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                var go = col.gameObject;
                var id = go.AddComponent<OfflineGrabTargetID>();
                id._hashID = (uint)rng.Next(1000, 15001);
                id.Type = OfflineGrabTargetID.OfflineGrabTargetIDType.Mantle;
                id.RegisterGeometryHashIDIfValid();
            }
        }

        private static void DisableLightProbesOnAllRenderers()
        {
            int sceneCount = SceneManager.sceneCount;
            for (int si = 0; si < sceneCount; si++)
            {
                var s = SceneManager.GetSceneAt(si);
                if (!s.isLoaded) continue;
                foreach (var root in s.GetRootGameObjects())
                {
                    var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
                    foreach (var r in renderers) { r.lightProbeUsage = LightProbeUsage.Off; }
                }
            }
        }

        private static Transform FindEndgoals(Transform t)
        {
            if (t.name.ToLower() == "endgoals") return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var found = FindEndgoals(t.GetChild(i));
                if (found != null) return found;
            }
            return null;
        }

        internal static void RegisterEndgoals(GameObject round)
        {
            if (round == null) return;

            var endgoalsRoot = FindEndgoals(round.transform);
            if (endgoalsRoot == null || endgoalsRoot.childCount == 0)
            {
                Plugin.Log.LogWarning("BetterFGRoundPostmodifier: no endgoals found in round hierarchy");
                return;
            }

            // grab CCCC from the real endzone before anything gets disabled
            var realEndzone = UnityEngine.Object.FindObjectOfType<COMMON_ObjectiveReachEndZone>();
            if (realEndzone != null)
            {
                BetterFGUnityRounds.CcccPosition = realEndzone.transform.position;
                BetterFGUnityRounds.CcccTransform = realEndzone.transform;
            }
            else
                Plugin.Log.LogWarning("BetterFGRoundPostmodifier: COMMON_ObjectiveReachEndZone not found, CCCC unavailable");

            var goals = new GameObject[endgoalsRoot.childCount];
            for (int i = 0; i < endgoalsRoot.childCount; i++)
            {
                var go = endgoalsRoot.GetChild(i).gameObject;
                var col = go.GetComponent<Collider>();
                if (col == null) col = go.AddComponent<BoxCollider>();
                col.isTrigger = true;
                go.AddComponent<CustomEndzoneTrigger>();
                go.AddComponent<COMMON_ObjectiveReachEndZone>();
                goals[i] = go;
            }

            BetterFGUnityRounds.ActiveEndgoals = goals;
        }
    }
}
