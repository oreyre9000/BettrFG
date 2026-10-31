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
            public bool AtEnd;
            public string Group;
            public string[] Costs;
            public string Pod;
            public Func<bool> Enabled = () => FeatureCreativeContent.On("customobjects");
        }

        private static string _activeKey;

        private static readonly List<Entry> _entries = new List<Entry>();
        private static readonly Dictionary<IntPtr, Entry> _byElement = new Dictionary<IntPtr, Entry>();
        private static readonly Dictionary<string, Entry> _byVariant = new Dictionary<string, Entry>();
        private static readonly List<UnityEngine.Object> _keepAlive = new List<UnityEngine.Object>();
        private static readonly List<PlaceableObjectData> _pods = new List<PlaceableObjectData>();
        private static HashSet<IntPtr> _ours = new HashSet<IntPtr>();

        static CreativeObjects()
        {
            Register("Groups", "creativeobjects.groups_desc",
                "BetterFG.assets.creativeobjects.groups.png", SavedGroupsBrowser.Open, new string[0]);
            _entries[_entries.Count - 1].Enabled = () => CreativeGroups.Enabled;
            Register("Light", "creativeobjects.light_desc",
                "BetterFG.assets.creativeobjects.light.png", BetterFG.Features.CreativeExpansion.CustomLights.PlaceAtReticle,
                new[] { BetterFG.Features.CreativeExpansion.CustomLights.MarkerName });
            RegisterInTab("IntroCam", "creativeobjects.introcam_desc",
                "BetterFG.assets.creativeobjects.introcam.png", BetterFG.Features.CreativeExpansion.CustomIntroCams.PlaceAtReticle, "Interactions",
                new[] { BetterFG.Features.CreativeExpansion.CustomIntroCams.MarkerName });
            RegisterInTab("Thruster", "creativeobjects.thruster_desc",
                "BetterFG.assets.creativeobjects.thruster.png", BetterFG.Features.CreativeExpansion.Thrusters.PlaceAtReticle, "Obstacles",
                BetterFG.Features.CreativeExpansion.Thrusters.Spawns);
            RegisterInTab("UnityRoundCore", "creativeobjects.unityroundcore_desc",
                "BetterFG.assets.creativeobjects.unityroundcore.png", BetterFG.Features.UnityRound.UnityRoundCoreObject.PlaceAtReticle, "Interactions",
                new[] { BetterFG.Features.UnityRound.UnityRoundCoreObject.MarkerName });
            RegisterNative("TriggerVolume", "creativeobjects.triggervolume_desc", TriggerVolumes.PodName, "Decoration");
            var foliage = BetterFG.Features.CreativeExpansion.Foliage.All;
            var groups = new List<string>();
            foreach (var item in foliage) if (!groups.Contains(item.Group)) groups.Add(item.Group);
            foreach (var group in groups)
            for (int i = 0; i < foliage.Length; i++)
            {
                if (foliage[i].Group != group) continue;
                int index = i;
                RegisterInTab($"Foliage{i}", "creativeobjects.foliage_desc", $"BetterFG.assets.creativeobjects.foliage{i}.png",
                    () => BetterFG.Features.CreativeExpansion.Foliage.PlaceAtReticle(index), "Decoration",
                    new[] { BetterFG.Features.CreativeExpansion.Foliage.MarkerName }, atEnd: true, group: BetterFG.Features.CreativeExpansion.Foliage.All[i].Group);
            }
        }

        public static void Register(string name, string description, string iconResource, Action picked, string[] costs)
        {
            _entries.Add(new Entry { Name = name, Description = description, Icon = iconResource, Picked = picked, Costs = costs });
        }

        public static void RegisterInTab(string name, string description, string iconResource, Action picked, string tabHint, string[] costs, bool atEnd = false, string group = null)
        {
            _entries.Add(new Entry { Name = name, Description = description, Icon = iconResource, Picked = picked, Tab = tabHint, Costs = costs, AtEnd = atEnd, Group = group });
        }

        public static void RegisterNative(string name, string description, string pod, string tabHint)
        {
            _entries.Add(new Entry { Name = name, Description = description, Pod = pod, Tab = tabHint });
        }

        public static void RegisterDrawn(string name, string description, string iconResource, string donorPrefab)
        {
            _entries.Add(new Entry { Name = name, Description = description, Icon = iconResource, Donor = donorPrefab });
        }

        public static bool TakeOver(PlaceableVariant_Base variant)
        {
            if (variant == null || !_byVariant.TryGetValue(variant.Guid.ToString(), out var entry) || entry.Picked == null) return false;
            Plugin.Log.LogInfo($"{entry.Name} picked out of the carousel, running its tool instead of placing the stand-in");
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

            var active = _entries.FindAll(e => e.Enabled());
            string activeKey = string.Join("|", active.ConvertAll(e => e.Name));
            bool toggled = _activeKey != null && activeKey != _activeKey;
            _activeKey = activeKey;

            var stale = new HashSet<IntPtr>();
            var current = LevelEditorObjectList.CurrentObjects;
            if (toggled || _pods.Exists(p => current.IndexOf(p) == -1))
            {
                Plugin.Log.LogInfo(toggled
                    ? $"creative object switches changed, {active.Count} of {_entries.Count} tiles on now, rebuilding the carousel entries"
                    : $"object list was reloaded since last editor visit, our {_pods.Count} pods fell out of it, rebuilding");
                foreach (var o in _keepAlive) if (o != null) UnityEngine.Object.Destroy(o);
                _keepAlive.Clear();
                _pods.Clear();
                _byElement.Clear();
                _byVariant.Clear();
                stale = _ours;
                _ours = new HashSet<IntPtr>();
            }

            var seen = new HashSet<IntPtr>();

            var theme = ThemeManager.CurrentThemeData;
            var themeList = theme != null ? theme.ObjectList : null;
            if (themeList != null && seen.Add(themeList.Pointer)) InjectInto(themeList, stale, active);

            for (int i = 0; lists != null && i < lists.Length; i++)
            {
                var list = lists[i];
                if (list != null && seen.Add(list.Pointer)) InjectInto(list, stale, active);
            }
        }

        private static void InjectInto(LevelEditorObjectList list, HashSet<IntPtr> stale, List<Entry> active)
        {
            var flat = list.m_TreeElements;
            if (flat == null || flat.Count == 0)
            {
                Plugin.Log.LogWarning($"{list.name} has no m_TreeElements, nothing to slot our objects into");
                return;
            }

            for (int i = flat.Count - 1; i >= 0; i--)
            {
                if (flat[i] != null && stale.Contains(flat[i].Pointer)) { flat.RemoveAt(i); continue; }
                var owner = flat[i]?.Variant?.Owner;
                if (owner != null && !_byElement.ContainsKey(flat[i].Pointer) && _entries.Exists(e => e.Pod == owner.name)) flat.RemoveAt(i);
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

            foreach (var entry in active)
            {
                if (entry.Tab == null) { stickerGroup.Add(entry); continue; }

                string tabKey = entry.AtEnd ? entry.Tab + "|end" : entry.Tab;
                if (!byTab.TryGetValue(tabKey, out int gi))
                {
                    var (anchor, at) = FindTabAnchor(list, flat, entry.Tab, entry.AtEnd);
                    if (anchor == null)
                    {
                        Plugin.Log.LogWarning($"no '{entry.Tab}' carousel tab anchor in {list.name}, {entry.Name} falls back to the decoration sticker");
                        stickerGroup.Add(entry);
                        continue;
                    }
                    gi = groups.Count;
                    groups.Add((anchor, at, new List<Entry>()));
                    byTab[tabKey] = gi;
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
            {
                int at = g.at;
                string folder = null;
                foreach (var entry in g.entries)
                {
                    if (entry.Group != null && entry.Group != folder)
                    {
                        idCursor++;
                        var folderEl = new VariantTreeElement(entry.Group, 0, idCursor);
                        _ours.Add(folderEl.Pointer);
                        flat.Insert(at++, folderEl);
                    }
                    folder = entry.Group;
                    idCursor++;
                    var built = Build(entry, g.anchor, idCursor, entry.Group != null ? 1 : g.anchor.depth);
                    _ours.Add(built.Pointer);
                    flat.Insert(at++, built);
                    inserted++;
                }
            }

            Plugin.Log.LogInfo($"{inserted} custom object(s) into {list.name} across {groups.Count} anchor point(s), "
                + $"{flat.Count} elements in the list now");
        }

        private static (VariantTreeElement anchor, int at) FindTabAnchor(LevelEditorObjectList list, Il2CppSystem.Collections.Generic.List<VariantTreeElement> flat, string tabHint, bool atEnd)
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

            int last = -1;
            for (int i = 0; i < flat.Count; i++)
            {
                var owner = flat[i]?.Variant?.Owner;
                if (owner == null || !wanted.Contains(owner.Category)) continue;
                if (!atEnd) return (flat[i], i);
                last = i;
            }
            if (last >= 0) return (flat[last], last + 1);

            Plugin.Log.LogWarning($"'{tabHint}' carousel tab found in {list.name} but has no items yet to anchor next to");
            return (null, -1);
        }

        private static VariantTreeElement Build(Entry entry, VariantTreeElement sticker, int id, int depth)
        {
            if (entry.Pod != null) return BuildNative(entry, sticker, id, depth);

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
            pod.costHandler = entry.Costs != null ? CostOf(entry, source.costHandler) : source.costHandler;
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

            var element = new VariantTreeElement(entry.Name, depth, id);
            element.Variant = variant;
            element.Guid = variantGuid;
            element.serializedGuid = variantGuid.ToByteArray();

            _byElement[element.Pointer] = entry;
            _byVariant[variantGuid.ToString()] = entry;
            _pods.Add(pod);
            _keepAlive.Add(pod);
            _keepAlive.Add(variant);
            _keepAlive.Add(proto);

            var current = LevelEditorObjectList.CurrentObjects;
            if (current != null && current.IndexOf(pod) == -1)
                current.Insert(current.Cast<Il2CppSystem.Collections.Generic.List<PlaceableObjectData>>().Count, pod);

            return element;
        }

        private static VariantTreeElement BuildNative(Entry entry, VariantTreeElement anchor, int id, int depth)
        {
            var pod = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<PlaceableObjectData>(entry.Pod).WaitForCompletion();
            var current = LevelEditorObjectList.CurrentObjects;
            if (current.IndexOf(pod) == -1)
                current.Insert(current.Cast<Il2CppSystem.Collections.Generic.List<PlaceableObjectData>>().Count, pod);

            pod.category = anchor.Variant.Owner.category;
            pod.objectNameKey = Key(entry, 'n');
            pod.objectDescriptionKey = Key(entry, 'd');

            var variant = pod.DefaultVariant;
            var element = new VariantTreeElement(entry.Name, depth, id);
            element.Variant = variant;
            element.Guid = variant.Guid;
            element.serializedGuid = variant.serializedGuid;
            _byElement[element.Pointer] = entry;
            return element;
        }

        private static PlaceableObjectCostHandler CostOf(Entry entry, PlaceableObjectCostHandler template)
        {
            int total = 0;
            foreach (var prefab in entry.Costs)
            {
                var handler = BetterFG.Utilities.IdentifierObjects.PodByPrefab(prefab)?.costHandler;
                if (handler == null) Plugin.Log.LogWarning($"{prefab} isn't in this object list, {entry.Name} undercounts its cost");
                else total += handler.GetBaseCost();
            }

            var cost = UnityEngine.Object.Instantiate(template);
            cost.hideFlags = HideFlags.HideAndDontSave;
            cost.CMSData = null;
            cost._baseCost = total;
            cost._isCostDynamic = false;
            cost._useStock = false;
            _keepAlive.Add(cost);
            Plugin.Log.LogInfo($"{entry.Name} costs {total} ({string.Join(" + ", entry.Costs)})");
            return cost;
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

    [HarmonyPatch(typeof(FG.Common.LevelEditorManager), nameof(FG.Common.LevelEditorManager.OnObjectPlacementMenuItemSelected))]
    internal static class CreativeObjectsSelectPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(FG.Common.LevelEditorManager __instance, ref bool __result)
        {
            if (!CreativeObjects.TakeOver(__instance.UI?.CarouselVariant)) return true;
            __result = true;
            return false;
        }
    }
}
