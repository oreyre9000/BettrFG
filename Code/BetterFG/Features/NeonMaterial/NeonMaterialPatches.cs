using System;
using BetterFG.Services;
using BetterFG.UI.Windows.Creative;
using HarmonyLib;
using Il2CppInterop.Runtime;
using LevelEditor;
using NodeEntry = LevelEditorParameterMenuViewModel.ParameterMenuNodeEntry;

namespace BetterFG.Features.NeonMaterial
{
    [HarmonyPatch(typeof(FG.Common.LevelEditorManager), nameof(FG.Common.LevelEditorManager.RegisterObject))]
    internal static class NeonMaterialRegisterObjectPatch
    {
        [HarmonyPostfix]
        public static void Postfix([HarmonyArgument(0)] LevelEditorPlaceableObject placeableObject)
        {
            // same reason as CustomLightRegisterObjectPatch: a level load calls this per placeable
            // and Sync() reads the whole level json, so ride the one shared delayed ladder.
            if (NeonMaterial.IsMarkerObject(placeableObject)) CustomLights.CustomLights.SyncDelayed();
            else NeonMaterial.OnPlaced(placeableObject);
        }
    }

    [HarmonyPatch(typeof(LevelEditorPlaceableObject), nameof(LevelEditorPlaceableObject.CopyPreviousData))]
    internal static class NeonMaterialClonePatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorPlaceableObject __instance, [HarmonyArgument(0)] LevelEditorPlaceableObject previousObject)
            => NeonMaterial.OnCloned(__instance, previousObject);
    }

    [HarmonyPatch(typeof(LevelEditorPlaceableObject), nameof(LevelEditorPlaceableObject.PlaceObject))]
    internal static class NeonMaterialPlacePatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorPlaceableObject __instance) => NeonMaterial.OnPlaced(__instance);
    }

    [HarmonyPatch(typeof(LevelEditorDrawableData), nameof(LevelEditorDrawableData.ApplyScaleToObject))]
    internal static class NeonMaterialDrawableScalePatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorDrawableData __instance)
        {
            BetterFG.Utilities.BfgProbe.Hit("ApplyScaleToObject");
            NeonMaterial.OnDrawableRescaled(__instance);
        }
    }

    [HarmonyPatch(typeof(LevelEditorColourChangerParameter), nameof(LevelEditorColourChangerParameter.SetColour))]
    internal static class NeonMaterialColourLivePatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorColourChangerParameter __instance)
        {
            var lepo = __instance.GetComponentInParent<LevelEditorPlaceableObject>();
            if (lepo == null || NeonMaterial.IsMarkerObject(lepo)) return;
            if (NeonMaterial.IsNeon(lepo)) NeonMaterial.RefreshColour(lepo);
        }
    }

    [HarmonyPatch(typeof(LevelEditorParameterMenuViewModel), "BuildParameterEntries")]
    internal static class NeonMaterialCarouselPatch
    {
        private const string MaterialRowParam = "wle_param_surfacedefinition";

        private static ParameterChangedIndex _cb;
        private static ParameterChangedIndex _origCb;
        private static int _neonSlot = -1;

        [HarmonyPostfix]
        public static void Postfix(LevelEditorParameterMenuViewModel __instance)
        {
            NeonMaterial.LastParamTarget = null;
            _neonSlot = -1;

            var target = __instance._menuData != null ? __instance._menuData.ParamTarget : null;
            if (target == null) return;
            if (BatchTargets.GetSurfaceParam(target) == null) return;

            var entries = __instance.NodeEntries;
            if (entries == null) return;

            NodeEntry matRow = null;
            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                if (e == null || e.NodeType != ParameterNodeType.String) continue;
                if (e._nodeData.ParamName == MaterialRowParam) { matRow = e; break; }
            }
            if (matRow == null) return;

            string label = LocalizationService.Get("neon.material_entry");
            var nd = matRow._nodeData;
            var items = nd.SelectionItems;
            if (items == null) return;

            int count = items.Count;
            bool hasNeon = count > 0 && items[count - 1] != null && items[count - 1].ToString() == label;
            _neonSlot = hasNeon ? count - 1 : count;

            if (_cb == null)
                _cb = DelegateSupport.ConvertDelegate<ParameterChangedIndex>(new Action<int>(OnMaterialIndex));

            if (!hasNeon)
            {
                _origCb = nd.OnChangedIndex;
                items.Add((Il2CppSystem.String)label);
            }

            bool neon = NeonMaterial.IsNeon(target);
            nd.OnChangedIndex = _cb;
            if (neon) nd.SelectedIndex = _neonSlot;
            matRow._nodeData = nd;
            matRow.UpdateVm();

            NeonMaterial.LastParamTarget = target;

            if (neon) NeonMaterial.ApplyNeon(target.transform, NeonMaterial.ColourOf(target));
        }

        private static void OnMaterialIndex(int index)
        {
            var t = NeonMaterial.LastParamTarget;
            if (t == null) return;

            var oc = _origCb;

            if (index == _neonSlot)
            {
                if (oc != null)
                    try { oc.Invoke(0); }
                    catch (Exception ex) { Plugin.Log.LogWarning($"neon: clearing surface to none threw {ex.Message}"); }
                NeonMaterial.SetNeon(t, true);
                return;
            }

            NeonMaterial.SetNeon(t, false);
            if (oc == null) return;
            try { oc.Invoke(index); }
            catch (Exception ex) { Plugin.Log.LogWarning($"neon: surface row passthrough({index}) threw {ex.Message}"); }
        }
    }
}
