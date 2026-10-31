using System;
using System.Collections.Generic;
using FGClient.Rendering.XRay;
using HarmonyLib;
using UnityEngine;

namespace BetterFG.Customization.Player
{
    public static class SkinXRay
    {
        private static readonly HashSet<IntPtr> _emptied = new HashSet<IntPtr>();

        public static void Sync(GameObject bean)
        {
            var tracker = XRayMeshRendererTracker.Instance;
            if (tracker == null || bean == null) return;
            var fgcc = bean.GetComponent<FallGuysCharacterController>();
            if (fgcc == null) return;
            var controllers = tracker.CharacterXRayControllers;
            for (int i = 0; i < controllers.Count; i++)
                if (controllers[i].Character == fgcc) { Sync(tracker, controllers[i]); return; }
        }

        internal static void OnInit(CharacterXRayController ctrl)
        {
            _emptied.Remove(ctrl.Pointer);
            var tracker = XRayMeshRendererTracker.Instance;
            if (tracker != null && ctrl.Character != null) Sync(tracker, ctrl);
        }

        private static void Sync(XRayMeshRendererTracker tracker, CharacterXRayController ctrl)
        {
            var bean = ctrl.Character.gameObject;
            int added = 0;
            foreach (var owner in bean.GetComponentsInChildren<InvisibilitySyncComponent>(false))
                foreach (var r in owner.GetComponentsInChildren<Renderer>(false))
                {
                    if (r.GetComponent<XRayMeshRenderer>() != null) continue;
                    var x = r.gameObject.AddComponent<XRayMeshRenderer>();
                    x._isLodded = false;
                    added++;
                }

            var geo = bean.transform.Find("Character/GEO");
            if (geo != null)
                foreach (var r in geo.GetComponentsInChildren<Renderer>(false))
                {
                    if (!r.name.EndsWith("LOD0") || r.name.StartsWith("Body_") || r.GetComponent<XRayMeshRenderer>() != null) continue;
                    r.gameObject.AddComponent<XRayMeshRenderer>()._isLodded = false;
                    added++;
                }

            var body = bean.transform.Find("Character/GEO/Body_LOD0");
            bool baseHidden = body != null && !body.gameObject.activeSelf;
            bool emptied = _emptied.Contains(ctrl.Pointer);
            if (baseHidden && !emptied)
            {
                ctrl.BodyRenderer.SetEmptyRenderer();
                ctrl.UpperRenderer.SetEmptyRenderer();
                ctrl.LowerRenderer.SetEmptyRenderer();
                _emptied.Add(ctrl.Pointer);
                Plugin.Log.LogInfo($"xray: base bean hidden under a costume on {bean.name}, game slots emptied");
            }
            else if (!baseHidden && emptied)
            {
                _emptied.Remove(ctrl.Pointer);
                tracker.AddXRayControllerForCharacter(ctrl.Character);
                Plugin.Log.LogInfo($"xray: base bean back on {bean.name}, handed the slots back to the game");
            }

            if (added > 0) Plugin.Log.LogInfo($"xray on {added} skin renderers for {bean.name}");
            tracker._isDirty = true;
        }

        internal static void DrawRemainingSubmeshes(XRayCameraRenderer cam)
        {
            var tracker = XRayMeshRendererTracker.Instance;
            var cmd = cam._commandBuffer;
            if (tracker == null || cmd == null || !cam._bufferAdded) return;
            var list = tracker.ActiveRenderers;
            int extra = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var x = list[i];
                if (x == null || x._isLodded) continue;
                var r = x.Renderer;
                if (r == null) continue;
                var smr = r.TryCast<SkinnedMeshRenderer>();
                var mesh = smr != null ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                for (int s = 1; s < mesh.subMeshCount; s++) { cmd.DrawRenderer(r, cam._meshRendererMaskMaterial, s, -1); extra++; }
            }
            if (extra > 0) Plugin.Log.LogDebug($"xray buffer rebuilt, +{extra} submesh draws the game skips");
        }
    }

    [HarmonyPatch(typeof(XRayCameraRenderer), "UpdateCommandBuffer")]
    internal static class SkinXRaySubmeshPatch
    {
        [HarmonyPostfix]
        public static void Postfix(XRayCameraRenderer __instance) => SkinXRay.DrawRemainingSubmeshes(__instance);
    }

    [HarmonyPatch(typeof(CharacterXRayController), nameof(CharacterXRayController.Init))]
    internal static class SkinXRayInitPatch
    {
        [HarmonyPostfix]
        public static void Postfix(CharacterXRayController __instance) => SkinXRay.OnInit(__instance);
    }
}
