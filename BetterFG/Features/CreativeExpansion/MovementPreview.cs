using System;
using System.Collections.Generic;
using BetterFG.Services;
using BetterFG.Utilities;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LevelEditor;
using UnityEngine;
using NodeEntry = LevelEditorParameterMenuViewModel.ParameterMenuNodeEntry;

namespace BetterFG.Features.CreativeExpansion
{
    // the controller's own Preview() only hides its gizmos: LevelEditorMovementActiveBase never
    // forwards it to what it drives. the receivers each carry a LevelEditorObjectMovementLink
    // (a LevelEditorCommonMoveAround) whose Preview() sets the path up correctly, but its
    // FixedUpdate is gated on IsObjectCurrentlyActiveAndGamePlaying, which is false in the editor
    // (measured live: gated=False while IsObjectCurrentlyActive()=True). so the button opens that
    // gate for the receivers it started, and the game's own motion does the rest.
    internal static class MovementPreview
    {
        internal const string GateKey = "movementpreview.gate";

        private static readonly List<LevelEditorActiveObjectBase> _running = new List<LevelEditorActiveObjectBase>();
        private static readonly HashSet<IntPtr> _runningPtrs = new HashSet<IntPtr>();
        // teleport mode runs the object through the self-respawner, which switches its renderers off
        // and only switches them back when the coroutine finishes. stopping mid-teleport leaves the
        // mesh invisible, so we put back whatever was visible when we started.
        private static readonly List<Renderer> _wasVisible = new List<Renderer>();
        private static ParameterChangedIndex _cb;
        private static NodeEntry _button;
        private static int _startFrame = -1;

        internal static bool Running => _running.Count > 0;

        internal static bool IsRunning(LevelEditorActiveObjectBase obj)
            => _runningPtrs.Count > 0 && obj != null && _runningPtrs.Contains(obj.Pointer);

        internal static NodeEntry Button()
        {
            if (_cb == null) _cb = DelegateSupport.ConvertDelegate<ParameterChangedIndex>(new Action<int>(OnPressed));
            _button = ParameterUtils.CreateButtonEntry(Label(), ParameterWrapMode.NoWrap, _cb, null, null, false, 1f, null, false, false, null);
            return _button;
        }

        private static string Label() => LocalizationService.Get(Running ? "movementpreview.stop" : "movementpreview.play");

        private static void Relabel()
        {
            if (_button == null || !LevelEditorParameterMenuViewModel.IsParametersScreenOpen()) return;
            _button.NodeData.ParamName = Label();
            _button.UpdateVm();
        }

        // the press itself comes through the parameter-change plumbing, so ignore the end/value
        // signals the game fires on the same frame we started.
        internal static void StopFromGame()
        {
            if (Time.frameCount == _startFrame) return;
            Stop(false);
        }

        internal static void Stop(bool rebuildMenu)
        {
            if (_running.Count == 0) return;

            for (int i = 0; i < _running.Count; i++)
            {
                var mover = _running[i];
                if (mover != null && mover.PreviewingParameter) mover.EndPreview();
            }
            for (int i = 0; i < _wasVisible.Count; i++)
                if (_wasVisible[i] != null) _wasVisible[i].enabled = true;

            _running.Clear();
            _runningPtrs.Clear();
            _wasVisible.Clear();
            PatchGate.Request(GateKey, "movementpreview", false);
            Plugin.Log.LogInfo("movement preview off, the receivers are back where they were placed");
            if (rebuildMenu) Relabel();
        }

        private static void OnPressed(int _)
        {
            if (_running.Count > 0) { Stop(true); return; }

            var ctrl = ParameterUtils.SelectedObject;
            var link = ctrl != null ? ctrl.LinkController : null;
            if (link == null)
            {
                Plugin.Log.LogWarning($"movement preview: {(ctrl == null ? "nothing is selected" : ctrl.name + " has no link controller")}");
                return;
            }

            var movers = new List<LevelEditorActiveObjectBase>();
            foreach (var m in UnityEngine.Object.FindObjectsOfType<LevelEditorObjectMovementLink>()) movers.Add(m);
            foreach (var r in UnityEngine.Object.FindObjectsOfType<LevelEditorObjectRotatorLink>()) movers.Add(r);
            foreach (var mover in movers)
            {
                if (mover == null) continue;
                var lepo = mover.GetComponentInParent<LevelEditorPlaceableObject>();
                if (lepo == null || !link.IsReceiverLinkedTo(lepo)) continue;

                _running.Add(mover);
                _runningPtrs.Add(mover.Pointer);
                foreach (var r in lepo.GetComponentsInChildren<Renderer>(true))
                    if (r != null && r.enabled) _wasVisible.Add(r);
            }

            if (_running.Count == 0)
            {
                Plugin.Log.LogWarning("movement preview: nothing is linked to this movement controller, so there is no path to run");
                return;
            }

            _startFrame = Time.frameCount;
            PatchGate.Request(GateKey, "movementpreview", true);
            for (int i = 0; i < _running.Count; i++) _running[i].Preview();
            Plugin.Log.LogInfo($"movement preview on, {_running.Count} receiver(s) running their path");
            Relabel();
        }
    }

    [HarmonyPatch(typeof(LevelEditorMovementControllerModeParameter), nameof(LevelEditorMovementControllerModeParameter.CreateParameterEntries))]
    internal static class MovementPreviewButtonPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref Il2CppReferenceArray<NodeEntry> __result)
        {
            var row = MovementPreview.Button();
            if (row == null) return;
            MovementPreviewButtonPatch.Append(ref __result, row);
        }

        internal static void Append(ref Il2CppReferenceArray<NodeEntry> __result, NodeEntry row)
        {
            int n = __result != null ? __result.Length : 0;
            var grown = new Il2CppReferenceArray<NodeEntry>(n + 1);
            for (int i = 0; i < n; i++) grown[i] = __result[i];
            grown[n] = row;
            __result = grown;
        }
    }

    [HarmonyPatch(typeof(LevelEditorRotationSpeedParameter), nameof(LevelEditorRotationSpeedParameter.CreateParameterEntries))]
    internal static class RotationPreviewButtonPatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorPlaceableObject obj, ref Il2CppReferenceArray<NodeEntry> __result)
        {
            if (obj == null || obj.LinkController == null) return;
            MovementPreviewButtonPatch.Append(ref __result, MovementPreview.Button());
        }
    }

    // only in while a preview is running, and only ever true for the receivers we started: this
    // sits on every active object's FixedUpdate, so it does not get to be idle.
    [BfgPatchGate(MovementPreview.GateKey)]
    [HarmonyPatch(typeof(LevelEditorActiveObjectBase), nameof(LevelEditorActiveObjectBase.IsObjectCurrentlyActiveAndGamePlaying))]
    internal static class MovementPreviewGatePatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorActiveObjectBase __instance, ref bool __result)
        {
            if (!__result && MovementPreview.IsRunning(__instance)) __result = true;
        }
    }

    // the game ends previews on the selected object's own active objects, and ours live on the
    // receivers, so without these they would keep running (and stay displaced, or stuck invisible
    // mid-teleport) when the menu closes or another parameter is changed under them.
    [HarmonyPatch(typeof(ParameterUtils), nameof(ParameterUtils.EndParameterPreview))]
    internal static class MovementPreviewEndPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => MovementPreview.StopFromGame();
    }

    [HarmonyPatch(typeof(ParameterUtils), "OnValueChangePreview")]
    internal static class MovementPreviewValueChangedPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => MovementPreview.StopFromGame();
    }
}
