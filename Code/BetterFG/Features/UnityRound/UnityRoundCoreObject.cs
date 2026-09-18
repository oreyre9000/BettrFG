using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Features.UnityRound.Editor;
using BetterFG.Services;
using BetterFG.UI;
using BetterFG.UI.Windows.Creative;
using BetterFG.Utilities;
using FG.Common;
using FG.Common.Fraggle;
using FGClient;
using FGClient.UI;
using FGClient.UI.Core;
using LevelEditor;
using MPG.Utility;
using UnityEngine;

namespace BetterFG.Features.UnityRound
{
    internal static class UnityRoundCoreObject
    {
        internal static bool Enabled => BetterFG.Features.CreativeExpansion.FeatureCreativeExpansion.On("customobjects");

        private static readonly MarkerKind Kind = MarkerKinds.UnityRoundCore;
        internal static string MarkerName => Kind.Prefab;
        private static Vector3 IdentityRotation => Kind.Rotation;
        private const float RotationTolerance = MarkerKind.RotationTolerance;

        internal static bool IsCore(LevelEditorPlaceableObject lepo)
        {
            if (lepo == null || !lepo.name.StartsWith(MarkerName, StringComparison.Ordinal)) return false;
            Vector3 rot = lepo.RotationData != null ? lepo.RotationData.CurrentRotation : lepo.transform.eulerAngles;
            return Vector3.Distance(rot, IdentityRotation) < RotationTolerance;
        }

        internal static void PlaceAtReticle()
        {
            if (!Enabled) return;
            var mgr = LevelEditorManager.Instance;
            if (mgr == null) return;

            var origin = IdentifierObjects.ReticleOrigin(mgr);

            string hex = "#" + ColorUtility.ToHtmlStringRGB(RenderSettings.ambientLight);
            var lepo = IdentifierObjects.Spawn(MarkerName, origin, IdentityRotation, Vector3.one * 0.5f, hex, selectAtReticle: true, hide: true);
            if (lepo == null) { Plugin.Log.LogWarning("unity round core: spawn returned null"); return; }

            Sync();
        }

        internal static void Sync()
        {
            if (!Enabled) return;
            var lepos = LevelEditorPlaceableObject.Collection;
            var keep = new HashSet<int>();
            if (lepos != null)
                for (int i = 0; i < lepos.Count; i++)
                {
                    var lepo = lepos[i];
                    if (!IsCore(lepo)) continue;

                    if (!IdentifierObjects.InEditor())
                    {
                        foreach (var r in lepo.GetComponentsInChildren<Renderer>(true))
                            if (r != null) r.forceRenderingOff = true;
                        continue;
                    }

                    int id = lepo.gameObject.GetInstanceID();
                    keep.Add(id);

                    BatchTargets.HideAndDecollide(lepo);
                    var gizmo = UnityRoundCoreGizmo.Use(lepo);
                    foreach (var r in lepo.GetComponentsInChildren<Renderer>(true))
                        if (r != null && (gizmo == null || !r.transform.IsChildOf(gizmo.transform)))
                            r.forceRenderingOff = true;
                }
            UnityRoundCoreGizmo.ReturnAllExcept(keep);
            GizmoMarkers.Prune(LevelEditorWorldSpaceUI.WorldSpaceUIType.CameraVolumeMarker, keep);
        }


        internal static string PathLabel()
        {
            string path = SettingsService.Get(UnityRoundLoader.PathSettingKey, "");
            return string.IsNullOrEmpty(path)
                ? LocalizationService.Get("unityroundcore.row_path_unset")
                : string.Format(LocalizationService.Get("unityroundcore.row_path"), System.IO.Path.GetFileName(path));
        }

        internal static void Browse(Action onDone)
            => WinDialogs.PickFile(LocalizationService.Get("unityroundcore.browse_title"), path =>
            {
                if (!string.IsNullOrEmpty(path)) SettingsService.Set(UnityRoundLoader.PathSettingKey, path);
                onDone?.Invoke();
            });

        internal static void Load()
        {
            string path = SettingsService.Get(UnityRoundLoader.PathSettingKey, "");
            if (UnityRoundLoader.LoadFromInfoJson(path, out string error))
                Plugin.Log.LogInfo($"unity round core: loaded {path}");
            else
                Plugin.Log.LogWarning($"unity round core: load failed: {error}");
        }

        internal static void Unload() => UnityRoundLoader.UnloadAndForget();


        internal static bool HideAllObjects => !UnityRoundLoader.KeepExistingObjects;

        internal static void SetHideObjects(bool hide)
        {
            UnityRoundLoader.SetKeepExistingObjects(!hide);
            PersistCurrentInfo(info => info.keepExistingObjects = UnityRoundLoader.KeepExistingObjects);
        }

        internal static void SetHideBackground(bool hide)
        {
            UnityRoundLoader.SetHideBackground(hide);
            PersistCurrentInfo(info => info.hideBackground = UnityRoundLoader.HideBackground);
        }

        internal static void SetHideThemeLighting(bool hide)
        {
            UnityRoundLoader.SetHideThemeLighting(hide);
            PersistCurrentInfo(info => info.hideThemeLighting = UnityRoundLoader.HideThemeLighting);
        }

        internal static void SetHideFinish(bool hide)
        {
            UnityRoundLoader.SetHideFinishLines(hide);
            PersistCurrentInfo(info => info.hideFinishLines = UnityRoundLoader.HideFinishLines);
        }

        internal static string AmbienceLabel()
            => string.Format(LocalizationService.Get("unityroundcore.row_ambience"), "#" + ColorUtility.ToHtmlStringRGB(RenderSettings.ambientLight));

        internal static void OpenAmbiencePicker(LevelEditorPlaceableObject lepo)
        {
            if (lepo == null) return;
            var param = BatchTargets.GetColourParam(lepo);
            if (param == null)
            {
                Plugin.Log.LogWarning("unity round core: gizmo has no colour parameter, no picker to open");
                return;
            }
            LevelEditorColourPickerParameterViewModel.OpenColourPickerScreen(param);
        }

        internal static void OnColourChanged(LevelEditorPlaceableObject lepo)
        {
            var param = BatchTargets.GetColourParam(lepo);
            if (param == null) return;
            var c = param.CurrentColour;
            c.a = 1f;
            RenderSettings.ambientLight = c;
            PersistCurrentInfo(info => info.ambientLight = c);
        }

        private static Action<MapInfo> _pendingInfo;
        private static string _pendingInfoPath;

        private static void PersistCurrentInfo(Action<MapInfo> mutate)
        {
            string path = UnityRoundLoader.LoadedJsonPath;
            if (string.IsNullOrEmpty(path)) return;
            if (_pendingInfoPath != path) { _pendingInfo = null; _pendingInfoPath = path; }
            _pendingInfo += mutate;
        }

        internal static void OnLevelSaved(LevelEditorLevelSavedEvent evt)
        {
            var result = evt.Results;
            if (result != LevelEditorSavedEvent.Result.No_Error
                && result != LevelEditorSavedEvent.Result.Thumbnail_Save_Failed
                && result != LevelEditorSavedEvent.Result.Thumbnail_Load_Failed)
            {
                Plugin.Log.LogWarning($"save came back {result}, the round options stay queued");
                return;
            }

            var mutate = _pendingInfo;
            string path = _pendingInfoPath;
            _pendingInfo = null;
            _pendingInfoPath = null;
            if (mutate == null || string.IsNullOrEmpty(path)) return;

            if (BetterFGUnityRounds.WriteInfoFields(path, mutate, out string error))
            {
                UnityRoundLoader.NoteInfoWritten();
                Plugin.Log.LogInfo($"round options down into {System.IO.Path.GetFileName(path)}");
            }
            else
                Plugin.Log.LogWarning($"unity round core: couldn't write info.json: {error}");
        }


        internal static void SetDescription()
        {
            string code = UnityRoundLoader.ShareCodeFromUrl(SettingsService.Get(UnityRoundLoader.ShareUrlSettingKey, ""));
            if (string.IsNullOrEmpty(code))
            {
                Plugin.Log.LogWarning("unity round core: no valid github info.json link set");
                return;
            }
            if (!UnityRoundLoader.SetLevelDescription(code, out string error))
                Plugin.Log.LogWarning($"unity round core: couldn't set description: {error}");
        }
    }
}
