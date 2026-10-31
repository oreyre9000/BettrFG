using System;
using System.Collections.Generic;
using System.IO;
using BetterFG.Customization.Pets;
using BetterFG.Customization.Player;
using BetterFG.Customization.Presets;
using BetterFG.Customization.Social;
using BetterFG.Customization.UI;
using BetterFG.Nametag;
using BetterFG.Patches;
using BetterFG.Services;

namespace BetterFG.Customization.Users
{
    public static class UserService
    {
        public static void MigratePresets()
        {
            if (!SettingsService.MigratedThisRun) return;
            string main = Path.Combine(SettingsService.UsersDir, SettingsService.MainUser);
            foreach (var preset in PresetService.List())
            {
                string name = SettingsService.SanitizeUserName(preset);
                string dir = Path.Combine(SettingsService.UsersDir, name);
                if (name.Length == 0 || Directory.Exists(dir))
                {
                    Plugin.Log.LogWarning($"preset '{preset}' clashes with an existing user, left it as a preset only");
                    continue;
                }
                try
                {
                    PresetService.ToUser(preset, dir, main);
                    Plugin.Log.LogInfo($"preset '{preset}' is a user now");
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"preset '{preset}' didn't convert: {ex.Message}"); }
            }
        }

        public static string Create(string name, bool copyCurrent)
        {
            name = SettingsService.SanitizeUserName(name);
            string dir = Path.Combine(SettingsService.UsersDir, name);
            if (name.Length == 0 || Directory.Exists(dir)) return null;
            Directory.CreateDirectory(dir);
            if (!copyCurrent)
            {
                Plugin.Log.LogInfo($"fresh user {name}");
                return name;
            }

            SettingsService.Flush();
            string src = SettingsService.UserDir;
            string srcFiles = Path.Combine(src, "files"), dstFiles = Path.Combine(dir, "files");
            if (Directory.Exists(srcFiles))
            {
                Directory.CreateDirectory(dstFiles);
                foreach (var f in Directory.GetFiles(srcFiles)) File.Copy(f, Path.Combine(dstFiles, Path.GetFileName(f)));
            }
            foreach (var f in Directory.GetFiles(src))
            {
                string dst = Path.Combine(dir, Path.GetFileName(f));
                if (Path.GetFileName(f) == "settings.txt") File.WriteAllText(dst, File.ReadAllText(f).Replace(srcFiles, dstFiles));
                else File.Copy(f, dst);
            }
            Plugin.Log.LogInfo($"{name} starts as a copy of {SettingsService.ActiveUser}");
            return name;
        }

        public static void Delete(string name)
        {
            if (name.Equals(SettingsService.ActiveUser, StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                Directory.Delete(Path.Combine(SettingsService.UsersDir, SettingsService.SanitizeUserName(name)), true);
                Plugin.Log.LogInfo($"user {name} gone");
            }
            catch (Exception ex) { Plugin.Log.LogError($"couldn't delete user {name}: {ex.Message}"); }
        }

        public static void Switch(string name)
        {
            if (name.Equals(SettingsService.ActiveUser, StringComparison.OrdinalIgnoreCase)) return;
            SettingsService.SwitchUser(name);
            PhraseInjectionService.SetEntries(PhraseSettingsService.Load());
            EmoticonInjectionService.SetEntries(EmoticonSettingsService.Load());
            EmoteInjectionService.SetEntries(EmoteSettingsService.Load());
            PetService.Instance?.Reload();
            ApplyLook();
        }

        public static void ApplyLook()
        {
            // diff against what's equipped, don't nuke: RemoveOneSkinByFile is the tab's own targeted
            // unequip (no GEO churn for items, proper cosmetic re-composite when a body skin comes
            // off), and RestoreFromSettings skips files that still have an active slot — so switching
            // presets only ever touches the difference. game cosmetics sync themselves from settings
            // (RestoreSavedGameCosmetics removes unwanted + adds missing).
            var app = CustomizationServices.ApplicationService;
            var catalog = CustomizationServices.CatalogService;
            var loader = CustomizationServices.LoaderService;
            var plinth = CustomizationServices.PlinthApp;
            if (app != null && catalog != null && loader != null && plinth != null)
            {
                var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in SettingsService.Get("skin.multi.files", "").Split(','))
                    if (!string.IsNullOrWhiteSpace(f)) wanted.Add(f.Trim());

                foreach (var slot in app.GetActiveSlots())
                    if (slot?.skinInfo != null && !wanted.Contains(slot.skinInfo.file))
                        app.RemoveOneSkinByFile(slot.skinInfo.file);

                app.RestoreFromSettings(catalog.AvailableSkins, loader, plinth, force: true);
                app.RestoreSavedGameCosmetics();
                if (plinth.HasPlinthApplied && !MenuCustomizationApplication.TryGetSavedPlinthEntry(out _, out _, out _, out _, out _))
                    plinth.RemovePlinth();
            }
            BetterFG.UI.Tabs.UIScalingTab.ApplyCanvasScalingFromSettings();
            NametagFinder.ReapplyAllNameplates();

            // menu bg/fg/cam — SpawnMenuBg is the menu-enter restore-everything path (gradient,
            // image bg, ambient/sun, plinth colour, circles pattern). fg gets a revert first since
            // ReapplyForegroundFromSettings doesn't undo old tints when the new preset has fewer
            // colours on; same idea for cam (reset, then apply only if the preset has cam keys)
            // and the pattern (restore original if the new preset has none).
            var menuApp = MenuCustomizationApplication.Instance;
            if (menuApp != null)
            {
                menuApp.SpawnMenuBg();
                if (SettingsService.Get(MenuCustomizationApplication.KEY_PATTERN_PATH, "") == "")
                    menuApp.RestorePattern();
                menuApp.RevertForeground();
                menuApp.ReapplyForegroundFromSettings();
                menuApp.ResetCam();
                MenuCustomizationApplication.AutoApplyCamFromSettings();
            }

            // per-screen banners: SpawnMenuBg already handled the FallForce menu path via its
            // Apply call. if a loading screen is live right now, repaint it so the new screen.*
            // values take effect without waiting for the next fade-in.
            LoadingScreenBg.ReapplyActive();
        }
    }
}
