using System;
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
    internal static class PhraseLockerService
    {
        private const int AddTileSpeechId = 58999;
        private const int TileSpeechIdBase = 59000;
        private const int WheelSpeechIdBase = 59500;

        private static readonly Dictionary<IntPtr, string> Tiles = new Dictionary<IntPtr, string>();
        private static CustomiserPhrasesSection _section;
        private static PhraseEntry _draft;

        private static TextAndImageSpeechOption BuildOption(PhraseEntry e, ImageSpeechOption src, int speechId)
        {
            var opt = SpeechOptionBuilder.Build(string.IsNullOrEmpty(e.phraseId) ? $"bfg_phrase_{e.id}" : e.phraseId, e.phraseText, src);
            opt._speechId = speechId;
            if (SocialSpriteCache.TryGet(e.imagePath, out var customSprite, out var cacheableSprite))
            {
                opt._sprite = customSprite;
                opt._cachedAtlasSprite = cacheableSprite;
            }
            return opt;
        }

        internal static void InjectTiles(CustomiserPhrasesSection section, Il2CppSystem.Collections.Generic.List<SocialOption> opts)
        {
            _section = section;
            for (int i = opts.Count - 1; i >= 0; i--)
                if (opts[i] == null || Tiles.ContainsKey(opts[i].Pointer)) opts.RemoveAt(i);
            Tiles.Clear();

            ImageSpeechOption src = null;
            for (int i = 0; i < opts.Count && src == null; i++) src = opts[i].TryCast<ImageSpeechOption>();
            if (src == null) { Plugin.Log.LogInfo("phrase grid has nothing to clone tile art from, skipping the custom tiles"); return; }

            var entries = PhraseSettingsService.Load();
            for (int i = 0; i < entries.Count; i++)
            {
                var tile = BuildOption(entries[i], src, TileSpeechIdBase + i);
                opts.Add(tile);
                Tiles[tile.Pointer] = entries[i].id;
            }

            var add = SpeechOptionBuilder.Build("bfg_add_phrase", LocalizationService.Get("phrase_editor.add_tile"), src);
            add._speechId = AddTileSpeechId;
            var cached = new ItemDefinitionSO.CacheableAtlasSprite();
            cached.Cache(LockerEditorUtil.PlusSprite(), true);
            add._sprite = LockerEditorUtil.PlusSprite();
            add._cachedAtlasSprite = cached;
            opts.Add(add);
            Tiles[add.Pointer] = "";
            Plugin.Log.LogInfo($"locker grid: {entries.Count} custom phrase tiles + the add tile");
        }

        internal static void DecorateWheel(SocialWheelViewModel wheel, ref Il2CppSystem.Collections.Generic.List<ItemDefinitionSO> list)
        {
            if (list == null || list.Count == 0 || !LockerEditorUtil.IsMenuWheel(wheel)) return;

            ImageSpeechOption src = null;
            for (int i = 0; i < list.Count && src == null; i++)
            {
                var iso = list[i]?.TryCast<ImageSpeechOption>();
                if (iso != null && iso.CMSGroupID == "cosmetics_phrases") src = iso;
            }
            if (src == null) return;

            Il2CppSystem.Collections.Generic.List<ItemDefinitionSO> copy = null;
            foreach (var e in PhraseSettingsService.Load())
            {
                if (!e.enabled || e.slot < 0 || e.slot >= list.Count) continue;
                if (copy == null)
                {
                    copy = new Il2CppSystem.Collections.Generic.List<ItemDefinitionSO>(list.Count);
                    for (int i = 0; i < list.Count; i++) copy.Add(list[i]);
                }
                copy[e.slot] = BuildOption(e, src, WheelSpeechIdBase + e.slot);
            }
            if (copy != null) list = copy;
        }

        internal static bool HandleSelect(SocialOption option)
        {
            if (option == null || !Tiles.TryGetValue(option.Pointer, out var id) || id != "") return false;
            OpenEditor();
            return true;
        }

        internal static bool HandleBind(SocialWheelItemDirection direction, SocialOption phrase)
        {
            if (phrase == null) return false;
            bool ours = Tiles.TryGetValue(phrase.Pointer, out var id);
            if (ours && id == "") return true;

            int slot = (int)direction;
            var entries = PhraseSettingsService.Load();
            var entry = ours ? entries.Find(x => x.id == id) : null;
            bool changed = false;
            foreach (var other in entries)
                if (other != entry && other.enabled && other.slot == slot) { other.enabled = false; changed = true; }
            if (entry != null)
            {
                entry.slot = slot;
                entry.enabled = true;
                changed = true;
                Plugin.Log.LogInfo($"custom phrase '{entry.phraseText}' now on wheel slot {direction}");
            }
            else if (changed) Plugin.Log.LogInfo($"real phrase took slot {direction}, custom one there switched off");

            if (changed)
            {
                PhraseSettingsService.Save(entries);
                PhraseInjectionService.ApplyAll(entries);
            }
            return ours;
        }

        internal static string HighlightedEntryId { get; private set; }
        internal static bool SectionLive => _section != null && _section.isActiveAndEnabled;

        internal static void OnHighlighted(CustomiserPhrasesSection section, int idx)
        {
            HighlightedEntryId = null;
            var opts = section.Options;
            if (opts == null || idx < 0 || idx >= opts.Count) return;
            var opt = opts[idx];
            if (opt != null && Tiles.TryGetValue(opt.Pointer, out var id) && id != "") HighlightedEntryId = id;
        }

        internal static void OpenManageMenu()
        {
            var entry = PhraseSettingsService.Load().Find(x => x.id == HighlightedEntryId);
            if (entry == null) return;
            _draft = entry;
            BeanMonitorService.Instance.StartCoroutine(
                ReportMenuShell.Open("phrase_editor.manage_title", "", false, WireManage).WrapToIl2Cpp());
        }

        private static void WireManage()
        {
            ReportMenuShell.SetHeader(0, string.IsNullOrEmpty(_draft.phraseText) ? "phrase_editor.header" : _draft.phraseText);
            ReportMenuShell.SetRow(0, "emoticon_editor.edit", () => LockerEditorUtil.ReopenAfterClose(() => OpenEditor(_draft)));
            ReportMenuShell.SetRow(1, "emoticon_editor.delete", Delete);
            for (int i = 2; i < 6; i++) ReportMenuShell.HideRow(i);
        }

        private static void Delete()
        {
            string id = _draft.id;
            var entries = PhraseSettingsService.Load();
            int removed = entries.RemoveAll(x => x.id == id);
            PhraseSettingsService.Save(entries);
            PhraseInjectionService.ApplyAll(entries);
            Plugin.Log.LogInfo($"deleted custom phrase '{_draft.phraseText}' ({removed} row), {entries.Count} left");
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

        private static void OpenEditor(PhraseEntry existing = null)
        {
            _editing = existing != null;
            _draft = existing ?? new PhraseEntry();
            if (!_editing) _draft.enabled = false;
            BeanMonitorService.Instance.StartCoroutine(
                ReportMenuShell.Open(_editing ? "phrase_editor.edit_title" : "phrase_editor.title", "", false, WireEditor).WrapToIl2Cpp());
        }

        private static void WireEditor()
        {
            ReportMenuShell.SetHeader(0, "phrase_editor.header");
            ReportMenuShell.SetRow(0, TextLabel(), EditText);
            ReportMenuShell.SetRow(1, ImageLabel(), PickImage);
            ReportMenuShell.SetRow(2, SoundLabel(0), () => PickSound(0));
            ReportMenuShell.SetRow(3, SoundLabel(1), () => PickSound(1));
            ReportMenuShell.SetRow(4, SoundLabel(2), () => PickSound(2));
            ReportMenuShell.SetRow(5, "emoticon_editor.save", Save);

            LockerEditorUtil.AddPreview(1, _draft.imagePath);
        }

        private static string TextLabel() => string.Format(LocalizationService.Get("phrase_editor.row_text"),
            string.IsNullOrEmpty(_draft.phraseText) ? LockerEditorUtil.None : _draft.phraseText);
        private static string ImageLabel() => LockerEditorUtil.FileLabel("emoticon_editor.row_image", _draft.imagePath);
        private static string SoundLabel(int i) => LockerEditorUtil.SoundLabel(i, _draft.soundPaths[i]);

        private static void PickImage() => LockerEditorUtil.PickPng("emoticon_editor.pick_image", path =>
        {
            _draft.imagePath = path;
            ReportMenuShell.Relabel(1, ImageLabel());
            LockerEditorUtil.ShowPreview(path);
        });

        private static void PickSound(int i) => LockerEditorUtil.PickAudio("emoticon_editor.pick_sound", path =>
        {
            _draft.soundPaths[i] = path;
            ReportMenuShell.Relabel(2 + i, SoundLabel(i));
        });

        private static void EditText() => LockerEditorUtil.OpenTextModal("phrase_editor.text_title", _draft.phraseText, "phrase_editor.text_placeholder", txt =>
        {
            _draft.phraseText = txt;
            ReportMenuShell.Relabel(0, TextLabel());
            ReportMenuShell.Relabel(5, "emoticon_editor.save");
        });

        private static void Save()
        {
            if (string.IsNullOrEmpty(_draft.phraseText))
            {
                ReportMenuShell.Relabel(5, "phrase_editor.save_needs_text");
                return;
            }

            var entries = PhraseSettingsService.Load();
            int at = entries.FindIndex(x => x.id == _draft.id);
            if (at >= 0) entries[at] = _draft; else entries.Add(_draft);
            PhraseSettingsService.Save(entries);
            PhraseInjectionService.ApplyAll(entries);
            Plugin.Log.LogInfo($"saved custom phrase '{_draft.phraseText}' from the locker, {entries.Count} total");
            ReportMenuShell.Close();
            _draft = null;
            RefreshGridAndWheel();
        }
    }
}
