using System;
using System.IO;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using BetterFG.UI;
using FGClient;
using FGClient.Customiser;
using FGClient.UI;
using FGClient.UI.Core;
using Character;
using MPG.Utility;
using UnityEngine;

namespace BetterFG.Customization.Social
{
    internal static class EmoticonLockerService
    {
        private const int AddTileSpeechId = 68999;
        private const int TileSpeechIdBase = 69000;

        private static readonly Dictionary<IntPtr, string> Tiles = new Dictionary<IntPtr, string>();
        private const int WheelSpeechIdBase = 69500;
        private static CustomiserEmoticonsSection _section;
        private static ImageSpeechOption _artSource;
        internal static ImageSpeechOption ArtSource => _artSource;

        private static EmoticonEntry _draft;

        internal static void InjectTiles(CustomiserEmoticonsSection section, Il2CppSystem.Collections.Generic.List<ItemDefinitionSO> opts)
        {
            _section = section;
            for (int i = opts.Count - 1; i >= 0; i--)
                if (opts[i] == null || Tiles.ContainsKey(opts[i].Pointer)) opts.RemoveAt(i);
            Tiles.Clear();

            ImageSpeechOption src = null;
            var byId = new Dictionary<string, ImageSpeechOption>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < opts.Count; i++)
            {
                var iso = opts[i].TryCast<ImageSpeechOption>();
                if (iso == null) continue;
                if (src == null) src = iso;
                var id = iso.CMSData?.Cast<FG.Common.CMS.CMSItemDefinition>()?.Id;
                if (!string.IsNullOrEmpty(id)) byId[id] = iso;
            }
            if (src == null) { Plugin.Log.LogInfo("emoticon grid has nothing to clone tile art from, skipping the custom tiles"); return; }

            _artSource = src;

            var entries = EmoticonSettingsService.Load();
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var art = !string.IsNullOrEmpty(e.itemId) && byId.TryGetValue(e.itemId, out var matched) ? matched : src;
                var tile = EmoticonInjectionService.BuildOption(e, art, TileSpeechIdBase + i);
                opts.Add(tile);
                Tiles[tile.Pointer] = e.id;
            }

            var add = EmoticonInjectionService.BuildOption(new EmoticonEntry(), src, AddTileSpeechId);
            add.name = "bfg_add_emoticon";
            var cached = new ItemDefinitionSO.CacheableAtlasSprite();
            cached.Cache(LockerEditorUtil.PlusSprite(), true);
            add._sprite = LockerEditorUtil.PlusSprite();
            add._cachedAtlasSprite = cached;
            opts.Add(add);
            Tiles[add.Pointer] = "";
            Plugin.Log.LogInfo($"locker grid: {entries.Count} custom emoticon tiles + the add tile");
        }

        internal static void DecorateWheel(SocialWheelViewModel wheel, ref Il2CppSystem.Collections.Generic.List<ItemDefinitionSO> list)
        {
            if (list == null || list.Count == 0 || !LockerEditorUtil.IsMenuWheel(wheel)) return;

            ImageSpeechOption src = null;
            for (int i = 0; i < list.Count; i++)
            {
                var iso = list[i]?.TryCast<ImageSpeechOption>();
                if (iso == null) continue;
                if (iso.CMSGroupID == "cosmetics_phrases") return;
                if (src == null) src = iso;
            }
            if (src == null) src = _artSource;
            if (src == null) return;

            Il2CppSystem.Collections.Generic.List<ItemDefinitionSO> copy = null;
            foreach (var e in EmoticonSettingsService.Load())
            {
                if (!e.enabled || e.slot < 0 || e.slot >= list.Count) continue;
                if (copy == null)
                {
                    copy = new Il2CppSystem.Collections.Generic.List<ItemDefinitionSO>(list.Count);
                    for (int i = 0; i < list.Count; i++) copy.Add(list[i]);
                }
                copy[e.slot] = EmoticonInjectionService.BuildOption(e, src, WheelSpeechIdBase + e.slot);
            }
            if (copy != null) list = copy;
        }

        internal static bool HandleSelect(ItemDefinitionSO option)
        {
            if (option == null || !Tiles.TryGetValue(option.Pointer, out var id) || id != "") return false;
            OpenEditor();
            return true;
        }

        internal static bool HandleBind(SocialWheelItemDirection direction, SocialOption emoticon)
        {
            if (emoticon == null) return false;
            bool ours = Tiles.TryGetValue(emoticon.Pointer, out var id);
            if (ours && id == "") return true;

            int slot = (int)direction;
            var entries = EmoticonSettingsService.Load();
            var entry = ours ? entries.Find(x => x.id == id) : null;
            bool changed = false;
            foreach (var other in entries)
                if (other != entry && other.enabled && other.slot == slot) { other.enabled = false; changed = true; }
            if (entry != null)
            {
                entry.slot = slot;
                entry.enabled = true;
                changed = true;
                Plugin.Log.LogInfo($"custom emoticon '{entry.name}' now on wheel slot {direction}");
            }
            else if (changed) Plugin.Log.LogInfo($"real emoticon took slot {direction}, custom one there switched off");

            if (changed)
            {
                EmoticonSettingsService.Save(entries);
                EmoticonInjectionService.ApplyAll(entries);
            }
            return ours;
        }

        internal static string HighlightedEntryId { get; private set; }
        internal static bool SectionLive => _section != null && _section.isActiveAndEnabled;

        internal static void OnHighlighted(CustomiserEmoticonsSection section, int idx)
        {
            HighlightedEntryId = null;
            var opts = section.Options;
            if (opts == null || idx < 0 || idx >= opts.Count) return;
            var opt = opts[idx];
            if (opt != null && Tiles.TryGetValue(opt.Pointer, out var id) && id != "") HighlightedEntryId = id;
        }

        internal static void OpenManageMenu()
        {
            var entry = EmoticonSettingsService.Load().Find(x => x.id == HighlightedEntryId);
            if (entry == null) return;
            _draft = entry;
            BeanMonitorService.Instance.StartCoroutine(
                ReportMenuShell.Open("emoticon_editor.manage_title", "", false, WireManage).WrapToIl2Cpp());
        }

        private static void WireManage()
        {
            ReportMenuShell.SetHeader(0, string.IsNullOrEmpty(_draft.name) ? "emoticon_editor.header" : _draft.name);
            ReportMenuShell.SetRow(0, "emoticon_editor.edit", () => LockerEditorUtil.ReopenAfterClose(() => OpenEditor(_draft)));
            ReportMenuShell.SetRow(1, "emoticon_editor.delete", Delete);
            for (int i = 2; i < 6; i++) ReportMenuShell.HideRow(i);
        }

        private static void Delete()
        {
            string id = _draft.id;
            var entries = EmoticonSettingsService.Load();
            int removed = entries.RemoveAll(x => x.id == id);
            EmoticonSettingsService.Save(entries);
            EmoticonInjectionService.ApplyAll(entries);
            Plugin.Log.LogInfo($"deleted custom emoticon '{_draft.name}' ({removed} row), {entries.Count} left");
            ReportMenuShell.Close();
            _draft = null;
            RefreshGridAndWheel();
        }

        private static void RefreshGridAndWheel()
        {
            if (_section == null) return;
            _section.GetComponent<CustomiserPooledGridOptionSelection>().PooledGridLayout
                .SetCollection(_section.Options.Cast<Il2CppSystem.Collections.IEnumerable>());
            _section._customiserWheelViewModel?.RefreshEquippedWheelItemsBySection(_section);
        }

        private static bool _editing;

        private static void OpenEditor(EmoticonEntry existing = null)
        {
            _editing = existing != null;
            _draft = existing ?? new EmoticonEntry();
            if (!_editing) _draft.enabled = false;
            BeanMonitorService.Instance.StartCoroutine(
                ReportMenuShell.Open(_editing ? "emoticon_editor.edit_title" : "emoticon_editor.title", "", false, WireEditor).WrapToIl2Cpp());
        }

        private static void WireEditor()
        {
            ReportMenuShell.SetHeader(0, "emoticon_editor.header");
            ReportMenuShell.SetRow(0, ImageLabel(), PickImage);
            ReportMenuShell.SetRow(1, NameLabel(), EditName);
            ReportMenuShell.SetRow(2, SoundLabel(0), () => PickSound(0));
            ReportMenuShell.SetRow(3, SoundLabel(1), () => PickSound(1));
            ReportMenuShell.SetRow(4, SoundLabel(2), () => PickSound(2));
            ReportMenuShell.SetRow(5, "emoticon_editor.save", Save);

            LockerEditorUtil.AddPreview(0, _draft.imagePath);
        }

        private static string ImageLabel() => LockerEditorUtil.FileLabel("emoticon_editor.row_image", _draft.imagePath);
        private static string NameLabel() => string.Format(LocalizationService.Get("emoticon_editor.row_name"),
            string.IsNullOrEmpty(_draft.name) ? LockerEditorUtil.None : _draft.name);
        private static string SoundLabel(int i) => LockerEditorUtil.SoundLabel(i, _draft.soundPaths[i]);

        private static void PickImage() => LockerEditorUtil.PickPng("emoticon_editor.pick_image", path =>
        {
            _draft.imagePath = path;
            ReportMenuShell.Relabel(0, ImageLabel());
            ReportMenuShell.Relabel(5, "emoticon_editor.save");
            LockerEditorUtil.ShowPreview(path);
        });

        private static void PickSound(int i) => LockerEditorUtil.PickAudio("emoticon_editor.pick_sound", path =>
        {
            _draft.soundPaths[i] = path;
            ReportMenuShell.Relabel(2 + i, SoundLabel(i));
        });

        private static void EditName() => LockerEditorUtil.OpenTextModal("emoticon_editor.name_title", _draft.name, "emoticon_editor.name_placeholder", txt =>
        {
            _draft.name = txt;
            ReportMenuShell.Relabel(1, NameLabel());
        });

        private static void Save()
        {
            if (string.IsNullOrEmpty(_draft.imagePath))
            {
                ReportMenuShell.Relabel(5, "emoticon_editor.save_needs_image");
                return;
            }
            if (string.IsNullOrEmpty(_draft.name)) _draft.name = Path.GetFileNameWithoutExtension(_draft.imagePath);

            var entries = EmoticonSettingsService.Load();
            int at = entries.FindIndex(x => x.id == _draft.id);
            if (at >= 0) entries[at] = _draft; else entries.Add(_draft);
            EmoticonSettingsService.Save(entries);
            EmoticonInjectionService.ApplyAll(entries);
            Plugin.Log.LogInfo($"saved custom emoticon '{_draft.name}' from the locker, {entries.Count} total");
            ReportMenuShell.Close();
            _draft = null;
            RefreshGridAndWheel();
        }
    }
}
