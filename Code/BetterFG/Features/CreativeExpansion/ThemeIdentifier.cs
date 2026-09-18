using BetterFG.Services;
using BetterFG.Utilities;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LevelEditor;
using NodeEntry = LevelEditorParameterMenuViewModel.ParameterMenuNodeEntry;

namespace BetterFG.Features.CreativeExpansion
{
    internal sealed class ThemeIdentifier : IIdentifierObject
    {
        public bool KeepButtons => true;

        public bool Matches(LevelEditorPlaceableObject lepo) => Definers.IsDefinerObject(lepo);

        public string DisplayName(LevelEditorPlaceableObject lepo)
        {
            Definers.TryGetEntry(lepo, out var entry);
            return LocalizationService.Format("custombackgrounds.theme_identifier_name_fmt", entry.Title);
        }

        public string Description(LevelEditorPlaceableObject lepo) => LocalizationService.Get("custombackgrounds.theme_identifier_description");

        public void PrepareRows(LevelEditorPlaceableObject lepo) { }

        public void CleanupRows(LevelEditorPlaceableObject lepo) { }

        public Il2CppReferenceArray<NodeEntry> FilterRows(LevelEditorParameterMenuViewModel vm, LevelEditorPlaceableObject lepo)
            => new Il2CppReferenceArray<NodeEntry>(0);
    }
}
