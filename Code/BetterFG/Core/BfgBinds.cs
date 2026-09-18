using System.Collections.Generic;
using BetterFG.Services;
using HarmonyLib;
using Rewired;
using Rewired.Data;
using Rewired.Data.Mapping;
using UnityEngine;

namespace BetterFG.Core
{
    public sealed class BfgBind
    {
        public string Id;
        public int ActionId;
        public string LabelKey;
        public KeyboardKeyCode Key;
        public int PadLike = -1;

        public bool Down => NavPromptCore.PollActionDirect(ActionId, null, false);
    }

    public static class BfgBinds
    {
        private const int FirstActionId = 9000;

        public static readonly BfgBind RespawnMode = new BfgBind { Id = "respawn_mode", LabelKey = "binds.respawn_mode", Key = KeyboardKeyCode.B, PadLike = RewiredConsts.Action.Default_ShowNames };
        public static readonly BfgBind Respawn = new BfgBind { Id = "respawn", LabelKey = "binds.respawn", Key = KeyboardKeyCode.R, PadLike = RewiredConsts.Action.Default_ResetRunOrSkipRound };
        public static readonly BfgBind PbSave = new BfgBind { Id = "pb_save", LabelKey = "binds.pb_save", Key = KeyboardKeyCode.F, PadLike = RewiredConsts.Action.Menu_Favourite };
        public static readonly BfgBind PbFavorite = new BfgBind { Id = "pb_favorite", LabelKey = "binds.pb_favorite", Key = KeyboardKeyCode.G, PadLike = RewiredConsts.Action.Menu_EmoteMenu1 };
        public static readonly BfgBind PbOverride = new BfgBind { Id = "pb_override", LabelKey = "binds.pb_override", Key = KeyboardKeyCode.F, PadLike = RewiredConsts.Action.Menu_Favourite };
        public static readonly BfgBind SquadsToggle = new BfgBind { Id = "squads_toggle", LabelKey = "binds.squads_toggle", Key = KeyboardKeyCode.L, PadLike = RewiredConsts.Action.LevelEditorReticle_Zoom_Camera_Out_Button };

        public static readonly Dictionary<string, BfgBind> All = new Dictionary<string, BfgBind>();

        public static bool IsOurs(int actionId) => actionId >= FirstActionId && actionId < FirstActionId + All.Count;

        static BfgBinds()
        {
            var list = new[] { RespawnMode, Respawn, PbSave, PbFavorite, PbOverride, SquadsToggle };
            for (int i = 0; i < list.Length; i++)
            {
                list[i].ActionId = FirstActionId + i;
                All[list[i].Id] = list[i];
            }
        }

        internal static void Register(UserData ud)
        {
            if (ud == null || ud.ContainsAction(RespawnMode.ActionId)) return;

            var jump = ud.GetActionById(RewiredConsts.Action.Default_Jump);
            int actionCategory = jump.categoryId;
            int mapCategory = RewiredConsts.Category.Default;

            foreach (var b in All.Values)
            {
                var a = new InputAction();
                a._id = b.ActionId;
                a._name = "BettrFG_" + b.Id;
                a._descriptiveName = b.LabelKey;
                a._type = InputActionType.Button;
                a._userAssignable = true;
                a._categoryId = actionCategory;
                a._behaviorId = jump.behaviorId;
                ud.actions.Add(a);
                ud.actionCategoryMap.AddAction(actionCategory, b.ActionId);

                var kb = ud.GetKeyboardMap(mapCategory, RewiredConsts.Layout.Keyboard.Default);
                kb.AddActionElementMap();
                var kaem = kb.GetActionElementMap(kb.actionElementMaps.Count - 1);
                kaem._actionCategoryId = actionCategory;
                kaem._actionId = b.ActionId;
                kaem._elementType = ControllerElementType.Button;
                kaem._elementIdentifierId = KeyboardElementId(b.Key);
                kaem._keyboardKeyCode = b.Key;
                kaem._axisContribution = Pole.Positive;
                kaem._axisRange = AxisRange.Full;

                if (b.PadLike < 0) continue;
                int seeded = 0;
                foreach (var maps in new[] { ud.joystickMaps, ud.customControllerMaps })
                    for (int m = 0; m < maps.Count; m++)
                    {
                        var map = maps[m];
                        if (map.categoryId != mapCategory) continue;
                        ActionElementMap src = null;
                        for (int o = 0; o < maps.Count && src == null; o++)
                        {
                            var other = maps[o];
                            if (other.hardwareGuidString != map.hardwareGuidString || other.customControllerUid != map.customControllerUid || other.layoutId != map.layoutId) continue;
                            for (int i = 0; i < other.actionElementMaps.Count; i++)
                                if (other.actionElementMaps[i]._actionId == b.PadLike) { src = other.actionElementMaps[i]; break; }
                        }
                        if (src == null) continue;
                        map.AddActionElementMap();
                        var aem = map.GetActionElementMap(map.actionElementMaps.Count - 1);
                        aem._actionCategoryId = actionCategory;
                        aem._actionId = b.ActionId;
                        aem._elementType = src._elementType;
                        aem._elementIdentifierId = src._elementIdentifierId;
                        aem._axisContribution = src._axisContribution;
                        aem._axisRange = src._axisRange;
                        aem._invert = src._invert;
                        seeded++;
                    }
                Plugin.Log.LogInfo($"rewired action {b.ActionId} ({b.Id}) registered, key {b.Key} (element {kaem._elementIdentifierId}), pad copied off action {b.PadLike} into {seeded} maps");
            }
        }

        private static int KeyboardElementId(KeyboardKeyCode key)
        {
            for (int i = 0; i < 512; i++)
                if (Keyboard.GetKeyboardKeyCodeByButtonIndex(i) == key) return i;
            Plugin.Log.LogWarning($"no keyboard button index for {key}?");
            return 0;
        }

        internal static void OnMainMenuEntered()
        {
            foreach (var b in All.Values)
                NavPromptCore.SetCmsString(b.LabelKey, LocalizationService.Get(b.LabelKey));

            if (!ReInput.isReady || ReInput.players.playerCount == 0) return;
            var p = ReInput.players.GetPlayer(0);
            var kb = p.controllers.Keyboard;
            if (kb == null) return;
            var map = p.controllers.maps.GetMap(ControllerType.Keyboard, kb.id, RewiredConsts.Category.Default, RewiredConsts.Layout.Keyboard.Default);
            if (map == null) return;
            bool changed = false;
            foreach (var b in All.Values)
            {
                string flag = "binds.seeded2." + b.Id;
                if (SettingsService.Get(flag, "") == "1") continue;
                SettingsService.Set(flag, "1");
                map.DeleteElementMapsWithAction(b.ActionId);
                map.CreateElementMap(b.ActionId, Pole.Positive, (KeyCode)(int)b.Key, ModifierKeyFlags.None);
                changed = true;
                Plugin.Log.LogInfo($"first run for bind {b.Id}, keyboard map now has {b.Key} for it");
            }
            if (changed) ReInput.userDataStore?.Save();
        }
    }

    [HarmonyPatch(typeof(InputManager_Base), "Awake")]
    internal static class BfgBindsRegisterPatch
    {
        [HarmonyPrefix]
        public static void Prefix(InputManager_Base __instance)
        {
            if (ReInput.isReady) { Plugin.Log.LogWarning("rewired already up before InputManager.Awake, can't add our actions this boot"); return; }
            BfgBinds.Register(__instance._userData);
        }
    }

    [HarmonyPatch(typeof(FGClient.FallGuysControlRebinding), "OnConflictFound")]
    internal static class BfgBindsConflictPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(InputMapper.ConflictFoundEventData data)
        {
            if (data == null) return true;
            var action = data.assignment?.action;
            bool ours = action != null && BfgBinds.IsOurs(action.id);
            if (!ours)
            {
                var conflicts = data.conflicts?.TryCast<Il2CppSystem.Collections.Generic.List<ElementAssignmentConflictInfo>>();
                if (conflicts == null || conflicts.Count == 0) return true;
                ours = true;
                for (int i = 0; i < conflicts.Count; i++)
                    if (!BfgBinds.IsOurs(conflicts[i].actionId)) { ours = false; break; }
            }
            if (!ours) return true;
            Plugin.Log.LogInfo($"bind conflict involves a BettrFG action, keeping both (assigning {action?.name ?? "?"})");
            data.responseCallback?.Invoke(InputMapper.ConflictResponse.Add);
            return false;
        }
    }
}
