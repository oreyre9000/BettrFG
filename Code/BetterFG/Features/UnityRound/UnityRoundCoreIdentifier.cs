using System;
using System.Collections.Generic;
using BetterFG.Features.UnityRound.Editor;
using BetterFG.Services;
using BetterFG.Utilities;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LevelEditor;
using NodeEntry = LevelEditorParameterMenuViewModel.ParameterMenuNodeEntry;

namespace BetterFG.Features.UnityRound
{
    internal sealed class UnityRoundCoreIdentifier : IIdentifierObject
    {
        private enum Category { Root, Location, Settings, Publish }

        private static Category _category = Category.Root;
        private static readonly HashSet<IntPtr> _ourRows = new HashSet<IntPtr>();
        private static LevelEditorPlaceableObject _current;

        private static ParameterChangedIndex _locationCb, _settingsCb, _publishCb, _backCb;
        private static ParameterChangedIndex _pathCb, _loadCb, _unloadCb, _ambienceCb, _urlCb;
        private static Il2CppSystem.Action<bool> _hideBackgroundCb, _hideObjectsCb, _hideFinishCb, _hideThemeLightingCb;

        public bool KeepButtons => false;

        public bool Matches(LevelEditorPlaceableObject lepo) => UnityRoundCoreObject.Enabled && UnityRoundCoreObject.IsCore(lepo);

        public string DisplayName(LevelEditorPlaceableObject lepo) => LocalizationService.Get("unityroundcore.name");

        public string Description(LevelEditorPlaceableObject lepo) => LocalizationService.Get("unityroundcore.description");

        public void CleanupRows(LevelEditorPlaceableObject lepo)
        {
            var existing = lepo.CustomParameters;
            if (existing == null || _ourRows.Count == 0) { _ourRows.Clear(); return; }

            for (int i = existing.Count - 1; i >= 0; i--)
            {
                var e = existing[i].ParameterEntry;
                if (e == null || !_ourRows.Remove(e.Pointer)) continue;
                existing.RemoveAt(i);
            }
            _ourRows.Clear();
        }

        public void PrepareRows(LevelEditorPlaceableObject lepo)
        {
            _current = lepo;
            EnsureCallbacks();

            switch (_category)
            {
                case Category.Location: BuildLocationRows(lepo); break;
                case Category.Settings: BuildSettingsRows(lepo); break;
                case Category.Publish: BuildPublishRows(lepo); break;
                default: BuildRootRows(lepo); break;
            }
        }

        public Il2CppReferenceArray<NodeEntry> FilterRows(LevelEditorParameterMenuViewModel vm, LevelEditorPlaceableObject lepo)
        {
            var entries = vm.NodeEntries;
            var kept = new List<NodeEntry>(entries != null ? entries.Length : 0);
            if (entries != null)
                for (int i = 0; i < entries.Length; i++)
                {
                    var e = entries[i];
                    if (e != null && _ourRows.Contains(e.Pointer)) kept.Add(e);
                }

            var arr = new Il2CppReferenceArray<NodeEntry>(kept.Count);
            for (int i = 0; i < kept.Count; i++) arr[i] = kept[i];
            return arr;
        }

        private static void EnsureCallbacks()
        {
            if (_locationCb == null) _locationCb = ToIndex(() => SetCategory(Category.Location));
            if (_settingsCb == null) _settingsCb = ToIndex(() => SetCategory(Category.Settings));
            if (_publishCb == null) _publishCb = ToIndex(() => SetCategory(Category.Publish));
            if (_backCb == null) _backCb = ToIndex(() => SetCategory(Category.Root));
            if (_pathCb == null) _pathCb = ToIndex(() => UnityRoundCoreObject.Browse(RebuildMenu));
            if (_loadCb == null) _loadCb = ToIndex(UnityRoundCoreObject.Load);
            if (_unloadCb == null) _unloadCb = ToIndex(UnityRoundCoreObject.Unload);
            if (_ambienceCb == null) _ambienceCb = ToIndex(() => UnityRoundCoreObject.OpenAmbiencePicker(_current));
            if (_urlCb == null) { _urlCb = ToIndex(() => { }); UnityRoundCoreUrlEntry.SetRebuild(RebuildMenu); }
            if (_hideBackgroundCb == null) _hideBackgroundCb = ToBool(() => UnityRoundCoreObject.SetHideBackground(!UnityRoundLoader.HideBackground));
            if (_hideObjectsCb == null) _hideObjectsCb = ToBool(() => UnityRoundCoreObject.SetHideObjects(!UnityRoundCoreObject.HideAllObjects));
            if (_hideFinishCb == null) _hideFinishCb = ToBool(() => UnityRoundCoreObject.SetHideFinish(!UnityRoundLoader.HideFinishLines));
            if (_hideThemeLightingCb == null) _hideThemeLightingCb = ToBool(() => UnityRoundCoreObject.SetHideThemeLighting(!UnityRoundLoader.HideThemeLighting));
        }

        private static ParameterChangedIndex ToIndex(Action action)
            => DelegateSupport.ConvertDelegate<ParameterChangedIndex>(new Action<int>(_ => action()));

        private static Il2CppSystem.Action<bool> ToBool(Action action)
            => DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>(new Action<bool>(_ => action()));

        private static void SetCategory(Category category)
        {
            _category = category;
            RebuildMenu();
        }

        private static void RebuildMenu()
        {
            if (_current != null) LevelEditorParameterMenuViewModel.UpdateParametersScreen(_current, 0);
        }

        private static void BuildRootRows(LevelEditorPlaceableObject lepo)
        {
            AddButton(lepo, LocalizationService.Get("unityroundcore.cat_location"), _locationCb);
            AddButton(lepo, LocalizationService.Get("unityroundcore.cat_settings"), _settingsCb);
            AddButton(lepo, LocalizationService.Get("unityroundcore.cat_publish"), _publishCb);
        }

        private static void BuildLocationRows(LevelEditorPlaceableObject lepo)
        {
            AddButton(lepo, LocalizationService.Get("unityroundcore.row_back"), _backCb);
            AddButton(lepo, UnityRoundCoreObject.PathLabel(), _pathCb);
            AddButton(lepo, LocalizationService.Get("unityroundcore.row_load"), _loadCb);
            AddButton(lepo, LocalizationService.Get("unityroundcore.row_unload"), _unloadCb);
        }

        private static void BuildSettingsRows(LevelEditorPlaceableObject lepo)
        {
            AddButton(lepo, LocalizationService.Get("unityroundcore.row_back"), _backCb);
            AddBool(lepo, LocalizationService.Get("unityroundcore.row_hidebackground"), UnityRoundLoader.HideBackground, _hideBackgroundCb);
            AddBool(lepo, LocalizationService.Get("unityroundcore.row_hideobjects"), UnityRoundCoreObject.HideAllObjects, _hideObjectsCb);
            AddBool(lepo, LocalizationService.Get("unityroundcore.row_hidefinish"), UnityRoundLoader.HideFinishLines, _hideFinishCb);
            AddBool(lepo, LocalizationService.Get("unityroundcore.row_hidethemelighting"), UnityRoundLoader.HideThemeLighting, _hideThemeLightingCb);
            AddButton(lepo, UnityRoundCoreObject.AmbienceLabel(), _ambienceCb);
        }

        private static void BuildPublishRows(LevelEditorPlaceableObject lepo)
        {
            AddButton(lepo, LocalizationService.Get("unityroundcore.row_back"), _backCb);
            AddString(lepo, UnityRoundCoreUrlEntry.UrlParamName, UrlValue(), _urlCb);
        }

        private static string UrlValue()
        {
            string url = SettingsService.Get(UnityRoundLoader.ShareUrlSettingKey, "");
            return string.IsNullOrEmpty(url) ? LocalizationService.Get("unityroundcore.row_url_unset") : url;
        }

        private static void AddString(LevelEditorPlaceableObject lepo, string label, string value, ParameterChangedIndex cb)
        {
            var items = new Il2CppSystem.Collections.ArrayList();
            items.Add((Il2CppSystem.String)value);

            var row = ParameterUtils.CreateStringEntry(label, 0, items.Cast<Il2CppSystem.Collections.ICollection>(),
                ParameterWrapMode.NoWrap, cb, null, null, false, 1f, null, false, false);
            if (row == null) return;
            var rows = lepo.CustomParameters;
            lepo.AddParameter(row, rows != null ? rows.Count : 0);
            _ourRows.Add(row.Pointer);
        }

        private static void AddButton(LevelEditorPlaceableObject lepo, string label, ParameterChangedIndex cb)
        {
            var row = ParameterUtils.CreateButtonEntry(label, ParameterWrapMode.NoWrap, cb, null, null, false, 1f, null, false, false, null);
            if (row == null) return;
            var rows = lepo.CustomParameters;
            lepo.AddParameter(row, rows != null ? rows.Count : 0);
            _ourRows.Add(row.Pointer);
        }

        private static void AddBool(LevelEditorPlaceableObject lepo, string label, bool value, Il2CppSystem.Action<bool> cb)
        {
            var row = ParameterUtils.CreateBoolEntry(label, value, ParameterWrapMode.Wrap, cb, null, null, false, 1f, null, false, false);
            if (row == null) return;
            var rows = lepo.CustomParameters;
            lepo.AddParameter(row, rows != null ? rows.Count : 0);
            _ourRows.Add(row.Pointer);
        }
    }
}
