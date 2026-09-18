using System;
using System.Collections.Generic;
using BetterFG.Services;
using BetterFG.UI.Windows.Creative;
using BetterFG.Utilities;
using FG.Common.CMS;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ScriptableObjects;
using TreeView;
using UnityEngine;

namespace BetterFG.Features.CreativeExpansion
{
    public static class CreativeObjects
    {
        private sealed class Entry
        {
            public string Name;
            public string Description;
            public string Icon;
            public Action Picked;
            public Sprite Sprite;
            public string Donor;
            public string Tab;
        }

        private static readonly List<Entry> _entries = new List<Entry>();
        private static readonly Dictionary<IntPtr, Entry> _byElement = new Dictionary<IntPtr, Entry>();
        private static readonly List<UnityEngine.Object> _keepAlive = new List<UnityEngine.Object>();

        static CreativeObjects()
        {
            Register("Groups", "creativeobjects.groups_desc",
                "BetterFG.assets.creativeobjects.groups.png", BatchEditWindow.OpenGroupsTool);
            Register("Light", "creativeobjects.light_desc",
                null, BetterFG.Features.CreativeExpansion.CustomLights.PlaceAtReticle);
            Register("IntroCam", "creativeobjects.introcam_desc",
                null, BetterFG.Features.CreativeExpansion.CustomIntroCams.PlaceAtReticle);
            Register("Thruster", "creativeobjects.thruster_desc",
                null, BetterFG.Features.CreativeExpansion.Thrusters.PlaceAtReticle);
            RegisterInTab("UnityRoundCore", "creativeobjects.unityroundcore_desc",
                null, BetterFG.Features.UnityRound.UnityRoundCoreObject.PlaceAtReticle, "Interactions");
        }

        public static void Register(string name, string description, string iconResource, Action picked)
        {
            if (BetterFG.Utilities.FeatureAvailability.Off(name)) return;
            _entries.Add(new Entry { Name = name, Description = description, Icon = iconResource, Picked = picked });
        }

        public static void RegisterInTab(string name, string description, string iconResource, Action picked, string tabHint)
        {
            if (BetterFG.Utilities.FeatureAvailability.Off(name)) return;
            _entries.Add(new Entry { Name = name, Description = description, Icon = iconResource, Picked = picked, Tab = tabHint });
        }

        public static void RegisterDrawn(string name, string description, string iconResource, string donorPrefab)
        {
            if (BetterFG.Utilities.FeatureAvailability.Off(name)) return;
            _entries.Add(new Entry { Name = name, Description = description, Icon = iconResource, Donor = donorPrefab });
        }

        public static bool TakeOver(VariantTreeElement element, string how)
        {
            if (element == null || !_byElement.TryGetValue(element.Pointer, out var entry)) return false;
            if (entry.Picked == null)
            {
                Plugin.Log.LogInfo($"{entry.Name} picked ({how}), handing it to the editor so it can be drawn out");
                return false;
            }
            Plugin.Log.LogInfo($"{entry.Name} picked out of the carousel ({how}), running its tool instead of placing anything");
            entry.Picked();
            return true;
        }

        public static void Inject(Il2CppReferenceArray<LevelEditorObjectList> lists)
        {
            if (_entries.Count == 0) return;

            var strings = CMSLoader.Instance._localisedStrings._localisedStrings;
            foreach (var entry in _entries)
            {
                strings[Key(entry, 'n')] = LocalizationService.Get($"creativeobjects.{entry.Name.ToLowerInvariant()}");
                strings[Key(entry, 'd')] = LocalizationService.Get(entry.Description);
            }

            var seen = new HashSet<IntPtr>();

            var theme = ThemeManager.CurrentThemeData;
            var themeList = theme != null ? theme.ObjectList : null;
            if (themeList != null && seen.Add(themeList.Pointer)) InjectInto(themeList);

            for (int i = 0; lists != null && i < lists.Length; i++)
            {
                var list = lists[i];
                if (list != null && seen.Add(list.Pointer)) InjectInto(list);
            }
        }

        private static void InjectInto(LevelEditorObjectList list)
        {
            var flat = list.m_TreeElements;
            if (flat == null || flat.Count == 0)
            {
                Plugin.Log.LogWarning($"{list.name} has no m_TreeElements, nothing to slot our objects into");
                return;
            }

            int maxId = 0;
            VariantTreeElement sticker = null;
            int stickerAt = -1;
            for (int i = 0; i < flat.Count; i++)
            {
                var el = flat[i];
                if (el == null) continue;
                if (_byElement.ContainsKey(el.Pointer)) return;
                if (el.id > maxId) maxId = el.id;
                if (sticker == null && IsSticker(el)) { sticker = el; stickerAt = i; }
            }

            var groups = new List<(VariantTreeElement anchor, int at, List<Entry> entries)>();
            var byTab = new Dictionary<string, int>();
            var stickerGroup = new List<Entry>();

            foreach (var entry in _entries)
            {
                if (entry.Tab == null) { stickerGroup.Add(entry); continue; }

                if (!byTab.TryGetValue(entry.Tab, out int gi))
                {
                    var (anchor, at) = FindTabAnchor(list, flat, entry.Tab);
                    if (anchor == null)
                    {
                        Plugin.Log.LogWarning($"no '{entry.Tab}' carousel tab anchor in {list.name}, {entry.Name} falls back to the decoration sticker");
                        stickerGroup.Add(entry);
                        continue;
                    }
                    gi = groups.Count;
                    groups.Add((anchor, at, new List<Entry>()));
                    byTab[entry.Tab] = gi;
                }
                groups[gi].entries.Add(entry);
            }

            if (stickerGroup.Count > 0)
            {
                if (sticker == null)
                    Plugin.Log.LogWarning($"no sticker entry in {list.name}'s {flat.Count} tree elements, {stickerGroup.Count} object(s) stay out");
                else
                    groups.Add((sticker, stickerAt, stickerGroup));
            }

            groups.Sort((a, b) => b.at.CompareTo(a.at));

            int inserted = 0;
            int idCursor = maxId;
            foreach (var g in groups)
                for (int i = 0; i < g.entries.Count; i++)
                {
                    idCursor++;
                    flat.Insert(g.at + i, Build(g.entries[i], g.anchor, idCursor));
                    inserted++;
                }

            Plugin.Log.LogInfo($"{inserted} custom object(s) into {list.name} across {groups.Count} anchor point(s), "
                + $"{flat.Count} elements in the list now");
        }

        private static (VariantTreeElement anchor, int at) FindTabAnchor(LevelEditorObjectList list, Il2CppSystem.Collections.Generic.List<VariantTreeElement> flat, string tabHint)
        {
            CategoryVisualData[] tabs = null;
            try { tabs = list.CarrouselTabsModel?.CategoryTabData; } catch { }
            if (tabs == null || tabs.Length == 0) return (null, -1);

            var gameStrings = CMSLoader.Instance._localisedStrings._localisedStrings;

            HashSet<LevelEditorPlaceableObject.Category> wanted = null;
            var seenTabs = new List<string>();
            foreach (var tab in tabs)
            {
                if (tab == null || tab.Categories == null) continue;
                string label = (gameStrings != null && gameStrings.TryGetValue(tab.LocTag, out string gs) ? gs : null) ?? tab.LocTag ?? "";
                seenTabs.Add($"{tab.LocTag}='{label}'");
                if (label.IndexOf(tabHint, StringComparison.OrdinalIgnoreCase) < 0) continue;
                wanted = new HashSet<LevelEditorPlaceableObject.Category>();
                for (int i = 0; i < tab.Categories.Length; i++) wanted.Add(tab.Categories[i]);
                break;
            }

            if (wanted == null)
            {
                Plugin.Log.LogWarning($"carousel tabs in {list.name}: [{string.Join(", ", seenTabs)}], none match '{tabHint}'");
                return (null, -1);
            }

            for (int i = 0; i < flat.Count; i++)
            {
                var owner = flat[i]?.Variant?.Owner;
                if (owner != null && wanted.Contains(owner.Category)) return (flat[i], i);
            }

            Plugin.Log.LogWarning($"'{tabHint}' carousel tab found in {list.name} but has no items yet to anchor next to");
            return (null, -1);
        }

        private static VariantTreeElement Build(Entry entry, VariantTreeElement sticker, int id)
        {
            var donor = entry.Donor != null ? BetterFG.Utilities.IdentifierObjects.PodByPrefab(entry.Donor) : null;
            if (entry.Donor != null && donor == null)
                Plugin.Log.LogWarning($"{entry.Donor} isn't in this object list, {entry.Name} falls back to the sticker shape and won't draw out");

            var source = donor != null ? donor : sticker.Variant.Owner;

            var podGuid = Il2CppSystem.Guid.NewGuid();
            var pod = ScriptableObject.CreateInstance(Il2CppType.Of<PlaceableObjectData>()).Cast<PlaceableObjectData>();
            pod.name = $"POD_BettrFG_{entry.Name}";
            pod.objectNameKey = Key(entry, 'n');
            pod.objectDescriptionKey = Key(entry, 'd');
            pod.category = source.category;
            pod.costHandler = source.costHandler;
            pod.rotationHandler = source.rotationHandler;
            pod.matchingCriteria = source.matchingCriteria;
            var icon = Icon(entry);
            pod.menuSprite = icon != null ? icon : source.menuSprite;
            pod.Guid = podGuid;
            pod.serializedGuid = podGuid.ToByteArray();
            pod.hideFlags = HideFlags.HideAndDontSave;

            var proto = UnityEngine.Object.Instantiate(donor != null ? donor.DefaultVariant.Prefab : sticker.Variant.Prefab);
            proto.SetActive(false);
            proto.name = $"Placeable_BettrFG_{entry.Name}";
            proto.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(proto);

            var variantGuid = Il2CppSystem.Guid.NewGuid();
            var variant = ScriptableObject.CreateInstance(Il2CppType.Of<PlaceableVariant_Prefab>())
                .Cast<PlaceableVariant_Prefab>();
            variant.name = $"BettrFG_{entry.Name}Variant";
            variant.prefab = proto;
            variant.Guid = variantGuid;
            variant.serializedGuid = variantGuid.ToByteArray();
            variant.hideFlags = HideFlags.HideAndDontSave;
            variant.SetOwner(pod);

            pod.objectVariants = new Il2CppReferenceArray<PlaceableVariant_Base>(1);
            pod.objectVariants[0] = variant;
            pod.defaultVariant = variant;

            var element = new VariantTreeElement(entry.Name, sticker.depth, id);
            element.Variant = variant;
            element.Guid = variantGuid;
            element.serializedGuid = variantGuid.ToByteArray();

            _byElement[element.Pointer] = entry;
            _keepAlive.Add(pod);
            _keepAlive.Add(variant);
            _keepAlive.Add(proto);

            var current = LevelEditorObjectList.CurrentObjects;
            if (current != null && current.IndexOf(pod) == -1)
                current.Insert(current.Cast<Il2CppSystem.Collections.Generic.List<PlaceableObjectData>>().Count, pod);

            return element;
        }

        private static string Key(Entry entry, char kind) => $"bfg_co_{entry.Name.ToLowerInvariant()}_{kind}";

        private static Sprite Icon(Entry entry)
        {
            if (entry.Icon == null) return entry.Sprite;
            entry.Sprite = EmbeddedResourceandUnity.LoadSprite(entry.Icon);
            if (entry.Sprite == null)
                Plugin.Log.LogWarning($"{entry.Icon} didn't load, {entry.Name} keeps whatever icon the sticker had");
            entry.Icon = null;
            return entry.Sprite;
        }

        private static bool IsSticker(VariantTreeElement element)
        {
            var variant = element.Variant;
            var owner = variant != null ? variant.Owner : null;
            if (owner == null) return false;
            if (owner.Category != LevelEditorPlaceableObject.Category.Decoration) return false;
            try
            {
                var prefab = owner.GetDefaultObject();
                return prefab != null && prefab.GetComponentInChildren<LevelEditorDecal>(true) != null;
            }
            catch { return false; }
        }
    }

    [HarmonyPatch(typeof(LevelEditorCarrouselViewModel), nameof(LevelEditorCarrouselViewModel.ShowCarrousel))]
    internal static class CreativeObjectsInjectPatch
    {
        [HarmonyPrefix]
        public static void Prefix([HarmonyArgument(0)] Il2CppReferenceArray<LevelEditorObjectList> objectLists)
            => CreativeObjects.Inject(objectLists);
    }

    [HarmonyPatch(typeof(FG.Common.LevelEditorManager), nameof(FG.Common.LevelEditorManager.RegisterObject))]
    internal static class CreativeObjectsWakePatch
    {
        [HarmonyPrefix]
        public static void Prefix([HarmonyArgument(0)] LevelEditorPlaceableObject placeableObject)
        {
            if (placeableObject != null && !placeableObject.gameObject.activeSelf
                && placeableObject.name.StartsWith("Placeable_BettrFG_", StringComparison.Ordinal))
                placeableObject.gameObject.SetActive(true);
        }
    }

    [HarmonyPatch(typeof(FG.Common.LevelEditorManagerUI), nameof(FG.Common.LevelEditorManagerUI.OnItemSelected))]
    internal static class CreativeObjectsSelectPatch
    {
        [HarmonyPrefix]
        public static bool Prefix([HarmonyArgument(0)] CarrouselItemSelectedEvent evt)
            => evt == null || !CreativeObjects.TakeOver(evt.treeElement, "carousel select");
    }
}
