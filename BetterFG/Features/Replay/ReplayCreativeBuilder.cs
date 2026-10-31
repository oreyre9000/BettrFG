using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using FG.Common;
using FG.Common.Fraggle;
using FG.Common.LevelEditor.Serialization;
using BetterFG.Utilities;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MPG.Utility;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Wushu.Integration;

namespace BetterFG.Features.Replay
{
    internal class ReplayCreativeBuilder
    {
        readonly ReplayRecording _rec;
        readonly MonoBehaviour _host;
        readonly Func<string, bool, IEnumerator> _loadScene;

        public Il2CppSystem.Object PreviousLevelLoader { get; private set; }
        public bool TouchedFraggle { get; private set; }
        public string Background { get; private set; }

        public ReplayCreativeBuilder(ReplayRecording rec, MonoBehaviour host, Func<string, bool, IEnumerator> loadScene)
        {
            _rec = rec;
            _host = host;
            _loadScene = loadScene;
        }

        public IEnumerator Build()
        {
            var fcm = SingletonBehaviour<FraggleCommonManager>.Instance;

            string json = _rec.levelJson;
            if (string.IsNullOrEmpty(json) && fcm != null && !string.IsNullOrEmpty(_rec.shareCode))
            {
                json = fcm.FraggleLevelsCache?.GetLevelJSONFromCache(_rec.shareCode, null);
                if (!string.IsNullOrEmpty(json))
                    Plugin.Log.LogInfo($"no level json in the replay, but {_rec.shareCode} was still sat in the game's cache");
            }

            if (string.IsNullOrEmpty(json))
            {
                Plugin.Log.LogWarning($"creative replay {_rec.shareCode} came without its level json, recorded before BettrFG kept it. nothing to rebuild");
                yield break;
            }

            var loader = LevelLoader.CreateLevelLoaderFromDownloadedJSON(json, LevelDto());
            if (loader == null)
            {
                Plugin.Log.LogWarning($"{json.Length} chars of level json and LevelLoader still wouldn't take it");
                yield break;
            }

            var theme = loader.GetTheme();
            Background = _rec.backgroundScene;
            if (string.IsNullOrEmpty(Background)) Background = theme?.BackgroundSceneName;

            string skyboxId = JsonUtil.GetValue(json, "SkyboxId");
            if (string.IsNullOrEmpty(skyboxId)) skyboxId = JsonUtil.GetValue(json, "SkyboxID");
            if (string.IsNullOrEmpty(skyboxId)) skyboxId = theme?.SkyboxId;

            int before = PlaceableCount();

            if (theme != null)
            {
                ThemeManager.SetLevelTheme(theme);

                var objectList = theme.ObjectList;
                if (objectList != null && LevelEditorObjectList.CurrentObjects == null)
                {
                    _host.StartCoroutine(objectList.Load(false));

                    float dbDeadline = Time.realtimeSinceStartup + 5f;
                    while (LevelEditorObjectList.CurrentObjects == null && Time.realtimeSinceStartup < dbDeadline)
                        yield return null;
                }

                Plugin.Log.LogInfo($"theme {theme.ID} set as current, object database {(LevelEditorObjectList.CurrentObjects != null ? "loaded" : "STILL MISSING")}");
            }

            if (fcm != null)
            {
                PreviousLevelLoader = fcm.LevelLoader;
                TouchedFraggle = true;
                fcm.LevelLoader = loader;
                if (!string.IsNullOrEmpty(Background)) fcm.BackgroundSceneToLoad = Background;
            }

            yield return _host.StartCoroutine(_loadScene(FraggleCommonManager.BootstrapSceneName, false).WrapToIl2Cpp());
            yield return _host.StartCoroutine(_loadScene(FraggleCommonManager.CommonSceneName, false).WrapToIl2Cpp());

            if (!string.IsNullOrEmpty(Background))
                yield return _host.StartCoroutine(_loadScene(Background, true).WrapToIl2Cpp());

            if (theme != null)
            {
                ThemeManager.SetLevelTheme(theme);
                yield return _host.StartCoroutine(Await(ThemeManager.LoadPostSceneAssets(), 15f).WrapToIl2Cpp());
            }

            float deadline = Time.realtimeSinceStartup + 3f;
            while (PlaceableCount() <= before && Time.realtimeSinceStartup < deadline) yield return null;

            if (PlaceableCount() <= before)
            {
                if (LevelEditorObjectList.CurrentObjects == null)
                {
                    Plugin.Log.LogWarning($"no object database for {theme?.ID}, so {_rec.shareCode} can't be placed");
                    yield break;
                }

                yield return _host.StartCoroutine(Await(loader.PreloadJSON(), 20f).WrapToIl2Cpp());

                var schema = loader.LevelSchema;
                int floors = schema?.Floors?.Length ?? 0;
                int others = schema?.OtherObjects?.Length ?? 0;
                if (floors + others == 0)
                {
                    Plugin.Log.LogWarning("the level json never parsed into a schema, nothing to place");
                    yield break;
                }

                yield return _host.StartCoroutine(Await(loader.PrecacheObjects(), 30f).WrapToIl2Cpp());
                Plugin.Log.LogInfo($"placing {floors} floors and {others} objects by hand, the bootstrap wouldn't");

                PlaceAll(loader, schema);
            }

            Physics.SyncTransforms();

            int placed = PlaceableCount() - before;
            if (placed <= 0)
                Plugin.Log.LogWarning($"nothing got placed for {_rec.shareCode} (object database {(LevelEditorObjectList.CurrentObjects != null ? "was loaded" : "was MISSING")})");
            else
                Plugin.Log.LogInfo($"creative level {_rec.shareCode} v{_rec.levelVersion} rebuilt on {Background} - {placed} placeables");

            yield return _host.StartCoroutine(ApplyThemeLook(loader, theme, skyboxId).WrapToIl2Cpp());
        }

        IEnumerator ApplyThemeLook(LevelLoader loader, ThemeData theme, string skyboxId)
        {
            if (theme != null) ThemeManager.SetLevelTheme(theme);

            yield return new WaitForSecondsRealtime(1f);

            var sceneBg = ThemeManager._sceneBackgroundAndLighting;
            LevelEditorThemeLighting sceneLighting = null;
            if (sceneBg != null && sceneBg.m_CachedPtr != IntPtr.Zero)
                sceneLighting = sceneBg.GetComponentInChildren<LevelEditorThemeLighting>(true);

            if (sceneLighting == null)
            {
                var bgScene = SceneManager.GetSceneByName(Background ?? "");
                if (bgScene.IsValid() && bgScene.isLoaded)
                {
                    foreach (var root in bgScene.GetRootGameObjects())
                    {
                        sceneLighting = root.GetComponentInChildren<LevelEditorThemeLighting>(true);
                        if (sceneLighting != null) { sceneBg = root; break; }
                    }
                }
            }

            if (sceneLighting != null)
            {
                Plugin.Log.LogInfo($"{sceneBg.name} turned up on its own, not building a second background");
                ThemeManager._sceneBackgroundAndLighting = sceneBg;
                yield return _host.StartCoroutine(ApplyLighting(sceneLighting).WrapToIl2Cpp());
                yield break;
            }

            Plugin.Log.LogInfo("no background from the game a second in, putting one up ourselves");

            var defs = loader.GetGameMode()?.SkyboxDefinitions;
            if (defs == null || defs.Definitions == null || defs.Definitions.Length == 0)
            {
                Plugin.Log.LogWarning($"game mode {_rec.archetypeId} has no skybox list, so nothing to build {skyboxId} from");
                yield break;
            }

            var aref = string.IsNullOrEmpty(skyboxId) ? null : defs.GetSkyboxPrefab(skyboxId);
            if (aref == null || !aref.RuntimeKeyIsValid())
            {
                var fallback = defs.Definitions[Mathf.Clamp(defs.DefaultIndex, 0, defs.Definitions.Length - 1)];
                Plugin.Log.LogWarning($"'{skyboxId}' isn't a skybox this game mode knows about, dropping back to {fallback.SkyboxId}");
                skyboxId = fallback.SkyboxId;
                aref = fallback.SkyboxRef;
            }

            var handle = Addressables.LoadAssetAsync<GameObject>(aref.RuntimeKey);
            while (!handle.IsDone) yield return null;

            if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            {
                Plugin.Log.LogWarning($"addressables wouldn't give up the {skyboxId} background prefab ({handle.Status})");
                yield break;
            }

            var bg = UnityEngine.Object.Instantiate(handle.Result);
            bg.name = handle.Result.name;
            bg.SetActive(true);
            ThemeManager._sceneBackgroundAndLighting = bg;

            var lighting = bg.GetComponentInChildren<LevelEditorThemeLighting>(true);
            Plugin.Log.LogInfo($"{bg.name} in for skybox {skyboxId}"
                + (lighting != null ? $", lighting off {lighting.name}" : ", but there's no LIGHTING inside it, so no sun"));
            if (lighting == null) yield break;

            yield return _host.StartCoroutine(ApplyLighting(lighting).WrapToIl2Cpp());
        }

        IEnumerator ApplyLighting(LevelEditorThemeLighting lighting)
        {
            var fll = SingletonBehaviour<FraggleLevelLoader>.Instance;
            if (fll == null) ApplyLightingDirect(lighting);
            else
            {
                try { fll.ApplySkyboxAndLightColours(lighting); }
                catch { ApplyLightingDirect(lighting); }
            }
            var root = lighting.transform.root.gameObject;
            BetterFG.Customization.Round.BackgroundWire.Raise(BetterFG.Customization.Round.BackgroundKind.Theme, root.name.Replace("(Clone)", ""), root);
            yield break;
        }

        static void ApplyLightingDirect(LevelEditorThemeLighting lighting)
        {
            var skybox = lighting._runtimeSkyboxMaterial != null ? lighting._runtimeSkyboxMaterial : lighting._skyboxMaterial;
            if (skybox != null) RenderSettings.skybox = skybox;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = lighting._skyColour;
            RenderSettings.ambientEquatorColor = lighting._equatorColour;
            RenderSettings.ambientGroundColor = lighting._groundColour;

            RenderSettings.fog = lighting._fogActive && !lighting._disableFog;
            RenderSettings.fogColor = lighting._fogColour;
            RenderSettings.fogDensity = lighting._fogDensity;

            if (lighting._sunSource != null) RenderSettings.sun = lighting._sunSource;
            DynamicGI.UpdateEnvironment();

            Plugin.Log.LogInfo($"the game wouldn't apply the theme lighting, put it on ourselves off {lighting.name} (skybox {(skybox != null ? skybox.name : "none")})");
        }

        static void PlaceAll(LevelLoader loader, UGCLevelDataSchema schema)
        {
            var built = new List<GameObject>();
            var from = new List<UGCObjectDataSchema>();

            string step = "guids";
            try
            {
                LevelLoader.StartOriginalToInstanceGuidDictionary();
                step = "floors";
                Place(schema.Floors, built, from);
                step = "triggers";
                Place(schema.SeparateTriggers, built, from);
                step = "objects";
                Place(schema.OtherObjects, built, from);
                LevelLoader.FinishOriginalToInstanceGuidDictionary();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"placing the level died on {step}: {ex}");
                return;
            }

            int dressed = 0;
            string firstFailure = null;
            for (int i = 0; i < built.Count; i++)
            {
                if (built[i] == null) continue;
                try
                {
                    LevelLoader.ConfigureIndividualObjectPostLoad(built[i], (LevelEditorPlaceableObject)null, from[i]);
                    dressed++;
                }
                catch (Exception ex) { firstFailure ??= ex.Message; }
            }

            Plugin.Log.LogInfo($"size, extents and colours onto {dressed}/{built.Count} objects"
                + (firstFailure != null ? $" — the rest choked on {firstFailure}" : ""));

            try { loader.LoadFillets(schema); }
            catch (Exception ex) { Plugin.Log.LogWarning($"fillets wouldn't go on: {ex.Message}"); }

            try { LevelLoader.ApplyDeferredSnapping(); }
            catch (Exception ex) { Plugin.Log.LogWarning($"snapping wouldn't run: {ex.Message}"); }
        }

        static void Place(Il2CppReferenceArray<UGCObjectDataSchema> schemas, List<GameObject> built, List<UGCObjectDataSchema> from)
        {
            if (schemas == null) return;

            int failed = 0;
            string firstFailure = null;
            for (int i = 0; i < schemas.Length; i++)
            {
                if (schemas[i] == null) continue;

                GameObject go;
                try { go = LevelLoader.LoadObject(schemas[i], true); }
                catch (Exception ex) { failed++; firstFailure ??= ex.Message; continue; }

                built.Add(go);
                from.Add(schemas[i]);
            }

            if (failed > 0)
                Plugin.Log.LogWarning($"{failed}/{schemas.Length} objects wouldn't load, the rest went in fine — first one choked on {firstFailure}");
        }

        LevelAggregateDto LevelDto()
        {
            var version = new LevelVersionMetadataDto
            {
                Version = _rec.levelVersion,
                Title = string.IsNullOrEmpty(_rec.roundName) ? _rec.shareCode : _rec.roundName,
                Description = "",
                SceneUrl = "",
                ThumbUrl = "",
                PlatformId = "",
                ClientVersion = "",
                GameModeId = _rec.archetypeId ?? "",
                LevelThemeId = "",
                MaxPlayerCount = (int)Mathf.Max(1, _rec.players.Count),
                IsCompleted = true,
            };

            var dto = new LevelAggregateDto { ShareCode = _rec.shareCode ?? "" };
            dto.VersionHistory = new Il2CppReferenceArray<LevelVersionMetadataDto>(1);
            dto.VersionHistory[0] = version;
            return dto;
        }

        public static void Restore(Il2CppSystem.Object previousLoader)
        {
            var fcm = SingletonBehaviour<FraggleCommonManager>.Instance;
            if (fcm != null) fcm.LevelLoader = previousLoader;
            LevelIO.ResetObjects();
        }

        static int PlaceableCount()
        {
            var placeables = LevelIO._guidToPlaceableObject;
            return placeables != null ? placeables.Count : 0;
        }

        IEnumerator Await(Il2CppSystem.Collections.IEnumerator routine, float seconds)
        {
            bool done = false;
            _host.StartCoroutine(Run(routine, () => done = true).WrapToIl2Cpp());

            float deadline = Time.realtimeSinceStartup + seconds;
            while (!done && Time.realtimeSinceStartup < deadline) yield return null;

            if (!done) Plugin.Log.LogWarning($"a game load routine never finished in {seconds:0}s, carrying on without it");
        }

        IEnumerator Run(Il2CppSystem.Collections.IEnumerator routine, Action onDone)
        {
            yield return _host.StartCoroutine(routine);
            onDone();
        }
    }
}
