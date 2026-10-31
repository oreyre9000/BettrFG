using BetterFG.UI.SideWheel;
using BetterFG.UI.Windows;
using UnityEngine;

namespace BetterFG.UI.SideWheel
{
    public static class SidewheelRegistry
    {
        public static void RegisterAll(SideWheelManager wheel)
        {
            Register<AudioSettingsWindow>(wheel,
                "sidewheel.audio_settings",
                "BetterFG.assets.ui.side.audioset.png",
                "BetterFG_AudioSettingsWindow");

            Register<MenuMusicWindow>(wheel,
                "sidewheel.menu_music",
                "BetterFG.assets.ui.side.menumusicset.png",
                "BetterFG_MenuMusicWindow");

            Register<PlayerdetailsWindow>(wheel,
                "sidewheel.player_details",
                "BetterFG.assets.ui.side.nameset.png",
                "BetterFG_PlayerdetailsWindow");

            Register<PlayerScaleWindow>(wheel,
                "sidewheel.player_scale",
                "BetterFG.assets.ui.side.scaleset.png",
                "BetterFG_PlayerScaleWindow");

            Register<TweaksWindow>(wheel,
                "sidewheel.tweaks",
                "BetterFG.assets.ui.side.tweakset.png",
                "BetterFG_TweaksWindow");

            Register<OptionsWindow>(wheel,
                "sidewheel.options",
                "BetterFG.assets.ui.side.keybindset.png",
                "BetterFG_OptionsWindow");

            Register<UsersWindow>(wheel,
                "sidewheel.users",
                "BetterFG.assets.ui.side.presetset.png",
                "BetterFG_UsersWindow");

#if PROFILES
            Register<ProfilesWindow>(wheel,
                "sidewheel.profiles",
                "BetterFG.assets.ui.side.profileset.png",
                "BetterFG_ProfilesWindow");
#endif

            Register<CreditsWindow>(wheel,
                "sidewheel.credits",
                "BetterFG.assets.ui.feature.star.featurestar_star.png",
                "BetterFG_CreditsWindow");

            wheel.CenterOn("sidewheel.options");
        }

        private static void Register<T>(SideWheelManager wheel, string locId, string iconResource, string goName)
            where T : BetterFGWindow
        {
            var icon = SideWheelManager.LoadEmbedded(iconResource);
            wheel.RegisterEntry(locId, icon, () =>
            {
                var go = new GameObject(goName);
                Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                return go.AddComponent<T>();
            });
        }
    }
}