using System.Collections.Generic;
using BetterFG.Services;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;
using NodeEntry = LevelEditorParameterMenuViewModel.ParameterMenuNodeEntry;

namespace BetterFG.Features.CreativeExpansion
{
    public enum ParamCategory { General, Appearance, Size, Behaviour, Physics, Spawning, Scoring, Links, SavedGroups }

    public static class ParamCategories
    {
        public const string HeaderPrefix = "BFG_ParamHeader_";
        private const float HeaderHeight = 58f;

        private static readonly Dictionary<string, ParamCategory> _byName = new Dictionary<string, ParamCategory>
        {
            ["wle_visibility"] = ParamCategory.General,
            ["wle_param_start_active"] = ParamCategory.General,

            ["wle_param_29"] = ParamCategory.Appearance,
            ["wle_param_32"] = ParamCategory.Appearance,
            ["wle_param_33"] = ParamCategory.Appearance,
            ["wle_param_51"] = ParamCategory.Appearance,
            ["wle_param_72"] = ParamCategory.Appearance,
            ["wle_param_unlit_toggle"] = ParamCategory.Appearance,
            ["wle_shape"] = ParamCategory.Appearance,
            ["wle_edge_type"] = ParamCategory.Appearance,

            ["wle_length"] = ParamCategory.Size,
            ["wle_height"] = ParamCategory.Size,
            ["wle_depth"] = ParamCategory.Size,
            ["wle_width"] = ParamCategory.Size,
            ["wle_thickness"] = ParamCategory.Size,
            ["wle_size"] = ParamCategory.Size,
            ["wle_scale"] = ParamCategory.Size,
            ["wle_reset_scale"] = ParamCategory.Size,
            ["wle_overhang"] = ParamCategory.Size,
            ["wle_rows"] = ParamCategory.Size,
            ["wle_columns"] = ParamCategory.Size,
            ["wle_num_platforms"] = ParamCategory.Size,

            ["wle_collision"] = ParamCategory.Physics,
            ["wle_param_surfacedefinition"] = ParamCategory.Physics,
            ["wle_param_physicsobject"] = ParamCategory.Physics,
            ["wle_param_physicsobject_weight"] = ParamCategory.Physics,
            ["wle_param_physicsobject_grabbable"] = ParamCategory.Physics,
            ["wle_param_destructibleobject"] = ParamCategory.Physics,
            ["wle_param_destructibleobject_forces_needed"] = ParamCategory.Physics,
            ["wle_param_destructibleobject_hits_required"] = ParamCategory.Physics,
            ["wle_param_destructibleobject_points_awarded"] = ParamCategory.Physics,
            ["wle_param_101"] = ParamCategory.Physics,
            ["wle_param_lock_rotation"] = ParamCategory.Physics,
            ["wle_param_ignore_axis"] = ParamCategory.Physics,
            ["wle_param_74"] = ParamCategory.Physics,
            ["wle_param_75"] = ParamCategory.Physics,

            ["wle_param_despawn"] = ParamCategory.Spawning,
            ["wle_despawn_type"] = ParamCategory.Spawning,
            ["wle_despawn_timer"] = ParamCategory.Spawning,
            ["wle_respawn_type"] = ParamCategory.Spawning,
            ["wle_respawn_timer"] = ParamCategory.Spawning,
            ["wle_param_respawn_time_min"] = ParamCategory.Spawning,
            ["wle_param_respawn_time_max"] = ParamCategory.Spawning,
            ["wle_param_quantity"] = ParamCategory.Spawning,
            ["wle_param_83"] = ParamCategory.Spawning,
            ["wle_param_120"] = ParamCategory.Spawning,
            ["wle_param_121"] = ParamCategory.Spawning,
            ["wle_param_135"] = ParamCategory.Spawning,
            ["wle_param_136"] = ParamCategory.Spawning,
            ["wle_param_137"] = ParamCategory.Spawning,
            ["wle_param_138"] = ParamCategory.Spawning,
            ["wle_param_139"] = ParamCategory.Spawning,
            ["wle_param_140"] = ParamCategory.Spawning,

            ["wle_param_point_scoring"] = ParamCategory.Scoring,
            ["wle_param_points_awarded"] = ParamCategory.Scoring,
            ["wle_param_scoring_type"] = ParamCategory.Scoring,
            ["wle_param_scoring_frequency"] = ParamCategory.Scoring,
            ["wle_param_score_feedback_audio"] = ParamCategory.Scoring,
            ["wle_param_score_feedback_ui"] = ParamCategory.Scoring,
            ["wle_param_comparison_operator"] = ParamCategory.Scoring,
            ["wle_param_zone_check"] = ParamCategory.Scoring,
            ["wle_param_trigger_object"] = ParamCategory.Scoring,

            ["wle_object_param_modify_links"] = ParamCategory.Links,
            ["wle_link_visibility"] = ParamCategory.Links,
        };

        public static NodeEntry Tag(this NodeEntry row, ParamCategory category)
        {
            if (row != null) _byName[row._nodeData.ParamName] = category;
            return row;
        }

        private static ParamCategory[] Resolve(Il2CppReferenceArray<NodeEntry> entries)
        {
            var cats = new ParamCategory[entries.Length];
            var last = ParamCategory.General;
            for (int i = 0; i < entries.Length; i++)
            {
                var data = entries[i]._nodeData;
                if (!_byName.TryGetValue(data.ParamName, out var cat)) cat = data.IsHidden ? last : ParamCategory.Behaviour;
                cats[i] = last = cat;
            }
            return cats;
        }

        internal static void Sort(LevelEditorParameterMenuViewModel vm)
        {
            var entries = vm.NodeEntries;
            if (entries == null || entries.Length < 2) return;

            var cats = Resolve(entries);
            var order = new List<int>(entries.Length);
            for (int i = 0; i < entries.Length; i++) order.Add(i);
            order.Sort((a, b) => cats[a] != cats[b] ? cats[a].CompareTo(cats[b]) : a.CompareTo(b));

            bool moved = false;
            for (int i = 0; i < order.Count && !moved; i++) moved = order[i] != i;
            if (!moved) return;

            var sorted = new Il2CppReferenceArray<NodeEntry>(entries.Length);
            for (int i = 0; i < order.Count; i++) sorted[i] = entries[order[i]];
            vm.NodeEntries = sorted;
        }

        internal static void AddHeaders(ParameterMenuCollectionBinding binding)
        {
            var vm = binding.GetComponent<LevelEditorParameterMenuViewModel>();
            var content = binding._itemsParent;
            if (vm == null) return;

            for (int i = content.childCount - 1; i >= 0; i--)
                if (content.GetChild(i).name.StartsWith(HeaderPrefix)) Object.DestroyImmediate(content.GetChild(i).gameObject);

            var entries = vm.NodeEntries;
            if (entries == null || entries.Length != content.childCount) return;

            var cats = Resolve(entries);
            var shown = new HashSet<ParamCategory>();
            for (int i = 0; i < entries.Length; i++)
                if (!entries[i]._nodeData.IsHidden) shown.Add(cats[i]);
            if (shown.Count < 2) return;

            var rows = new Transform[content.childCount];
            for (int i = 0; i < rows.Length; i++) rows[i] = content.GetChild(i);
            var font = rows[0].GetComponentInChildren<TMP_Text>();
            float width = rows[0].GetComponent<RectTransform>().sizeDelta.x;

            int sibling = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                if ((i == 0 || cats[i] != cats[i - 1]) && shown.Remove(cats[i]))
                {
                    var go = new GameObject(HeaderPrefix + cats[i]);
                    go.layer = content.gameObject.layer;
                    go.transform.SetParent(content, false);
                    var text = go.AddComponent<TextMeshProUGUI>();
                    go.GetComponent<RectTransform>().sizeDelta = new Vector2(width, HeaderHeight);
                    text.font = font.font;
                    text.fontSharedMaterial = font.fontSharedMaterial;
                    text.fontSize = font.fontSize * 0.85f;
                    text.fontStyle = FontStyles.UpperCase;
                    text.color = new Color(1f, 1f, 1f, 0.6f);
                    text.alignment = TextAlignmentOptions.BottomLeft;
                    text.margin = new Vector4(12f, 0f, 0f, 4f);
                    text.raycastTarget = false;
                    text.text = LocalizationService.Get("paramcategory." + cats[i].ToString().ToLowerInvariant());
                    go.transform.SetSiblingIndex(sibling++);
                }
                rows[i].SetSiblingIndex(sibling++);
            }
        }
    }

    [HarmonyPatch(typeof(ParameterMenuCollectionBinding), "HandleChanged")]
    internal static class ParamCategoryHeaderPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ParameterMenuCollectionBinding __instance) => ParamCategories.AddHeaders(__instance);
    }
}
