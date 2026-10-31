using System;
using BetterFG.Utilities;
using FG.Common;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class TriggerVolumes
    {
        internal const string PodName = "POD_UI_Trigger_Volume";
        private const string PrefabName = "Placeable_UI_Trigger_Volume";

        internal static bool IsVolume(LevelEditorPlaceableObject lepo) => lepo.name.StartsWith(PrefabName, StringComparison.Ordinal);

        internal static void Sync()
        {
            bool editing = IdentifierObjects.InEditor() && !Thrusters.InPlaytest();
            var lepos = LevelEditorPlaceableObject.Collection;
            for (int i = 0; i < lepos.Count; i++)
            {
                var lepo = lepos[i];
                if (!IsVolume(lepo)) continue;
                if (lepo.EditorOnlyRenderers != null && lepo.EditorOnlyRenderers.Length > 0)
                    lepo.EditorOnlyRenderers = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Renderer>(0);
                if (editing) MakePickable(lepo);
            }
        }

        private static void MakePickable(LevelEditorPlaceableObject lepo)
        {
            var box = lepo.GetComponentInChildren<LevelEditorTrigger>(true).boxCollider;
            if (box.enabled && !LevelEditorManager.VolumeColliders.ContainsKey(box)) return;
            box.enabled = true;
            box.GetComponent<LevelEditorObjectCollider>()._type = LevelEditorObjectCollider.ObjectColliderType.Static;
            LevelEditorManager.VolumeColliders.Remove(box);
            LevelEditorPlaceableObject.UnregisterAllCollidersFromPlaceable(lepo);
            LevelEditorPlaceableObject.RegisterAllCollidersForPlaceable(lepo);
            lepo.BuildColliderBounds();
            Plugin.Log.LogInfo($"trigger volume {lepo.GetInstanceID()} pickable now, bounds {lepo.ColliderBounds.size}");
        }
    }
}
