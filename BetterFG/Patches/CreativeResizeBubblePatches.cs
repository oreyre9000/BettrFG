using FG.Common;
using FGClient;
using HarmonyLib;
using LevelEditor;
using Rewired;
using Il2CppPromptList = Il2CppSystem.Collections.Generic.List<LevelEditorPrompt>;

namespace BetterFG.Patches
{
    internal static class CreativeResizeBubble
    {
        internal static bool ForceSelect;
        private static LevelEditorPrompt _padGrab, _keyGrab;

        internal static bool OnPad => ReInput.controllers.GetLastActiveControllerType() == ControllerType.Joystick;

        internal static int GrabAction => _padGrab == null ? -1 : (OnPad ? _padGrab : _keyGrab).RewiredAction;

        internal static bool Hovered(LevelEditorStateReticleBase reticle)
        {
            var ui = reticle.WorldSpaceUIIn2DReticle;
            return ui != null && ui.CurrentType == LevelEditorWorldSpaceUI.WorldSpaceUIType.Resize;
        }

        private static LevelEditorPrompt Find(LevelEditorContextualPromptList list, LE_PromptMode mode, ContextualPromptID id)
        {
            var all = list.GetPrompts(new Il2CppPromptList(), mode);
            for (int i = 0; i < all.Count; i++) if (all[i].PromptID == id) return all[i];
            return null;
        }

        private static LevelEditorPrompt GrabFrom(LevelEditorPrompt button, LevelEditorPrompt select) => new LevelEditorPrompt
        {
            PromptID = ContextualPromptID.Select,
            PromptKey = select.PromptKey,
            RewiredAction = button.RewiredAction,
            RewiredCategory = button.RewiredCategory,
            CanDeactivate = select.CanDeactivate,
            Mappable = button.Mappable,
            NonMappablePCSprite = button.NonMappablePCSprite,
            NonMappableControlSpriteXbox = button.NonMappableControlSpriteXbox,
            NonMappableControlSpritePS = button.NonMappableControlSpritePS,
            NonMappableControlSpriteSwitch = button.NonMappableControlSpriteSwitch,
        };

        internal static Il2CppPromptList Merge(LevelEditorContextualPromptList list, Il2CppPromptList unused, Il2CppPromptList resize)
        {
            var hovering = list.GetPrompts(unused, LE_PromptMode.Hovering);
            if (_padGrab == null)
            {
                var select = Find(list, LE_PromptMode.Hovering, ContextualPromptID.Select);
                _padGrab = GrabFrom(Find(list, LE_PromptMode.Hovering, ContextualPromptID.Link), select);
                _keyGrab = GrabFrom(Find(list, LE_PromptMode.Selected, ContextualPromptID.Tilt), select);
            }

            var merged = new Il2CppPromptList();
            for (int i = 0; i < hovering.Count; i++)
            {
                var id = hovering[i].PromptID;
                if (id != ContextualPromptID.Select && id != ContextualPromptID.Link) merged.Add(hovering[i]);
            }
            merged.Add(OnPad ? _padGrab : _keyGrab);
            for (int i = 0; i < resize.Count; i++) merged.Add(resize[i]);
            return merged;
        }
    }

    [HarmonyPatch(typeof(LevelEditorContextualPromptList), nameof(LevelEditorContextualPromptList.GetPrompts))]
    public class CreativeResizeBubblePromptsPatch
    {
        private static bool _merging;

        [HarmonyPostfix]
        public static void Postfix(LevelEditorContextualPromptList __instance, Il2CppPromptList __0, LE_PromptMode __1, ref Il2CppPromptList __result)
        {
            if (_merging) return;
            bool bubble = __1 == LE_PromptMode.WorldSpaceUI_Resize;
            if (bubble)
            {
                _merging = true;
                __result = CreativeResizeBubble.Merge(__instance, __0, __result);
                _merging = false;
            }
            Utilities.PatchGate.Request(Services.FGInputLockService.EditorUiLockGateKey, "resize bubble", bubble);
        }
    }

    [HarmonyPatch(typeof(LevelEditorStateReticleInputHandler), "HandleSelectObject")]
    public class CreativeResizeBubbleGrabPatch
    {
        private static LevelEditorWorldSpaceUI _bubble;

        [HarmonyPrefix]
        public static void Prefix(LevelEditorStateReticleInputHandler __instance)
        {
            int action = CreativeResizeBubble.GrabAction;
            if (action < 0 || !CreativeResizeBubble.Hovered(__instance)) return;
            if (!__instance._rewiredPlayer.GetButtonDown(action)) return;
            _bubble = __instance.WorldSpaceUIIn2DReticle;
            __instance.WorldSpaceUIIn2DReticle = null;
            CreativeResizeBubble.ForceSelect = true;
        }

        [HarmonyPostfix]
        public static void Postfix(LevelEditorStateReticleInputHandler __instance)
        {
            if (!CreativeResizeBubble.ForceSelect) return;
            CreativeResizeBubble.ForceSelect = false;
            __instance.WorldSpaceUIIn2DReticle = _bubble;
            _bubble = null;
            Plugin.Log.LogInfo($"grabbed {__instance.SelectedObject?.name} off its resize bubble");
        }
    }
}
