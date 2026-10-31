using FGClient;
using UnityEngine;

namespace BetterFG.Customization.UI.Styles
{
    public static class StyledUI
    {
        public static readonly System.Collections.Generic.Dictionary<int, System.Action<bool>> Highlights =
            new System.Collections.Generic.Dictionary<int, System.Action<bool>>();

        public static void OnHighlight(Component tab, bool on)
        {
            if (Highlights.TryGetValue(tab.gameObject.GetInstanceID(), out var a)) a(on);
        }

        public static void Apply(MainMenuManager menu)
        {
            var style = UIStyles.Active;
            StyledTopBar.Build(style, menu);
            StyledLobby.Build(style);
            style.ApplyMenu(menu);
        }

        public static void Teardown()
        {
            StyledTopBar.Teardown();
            StyledTopBar.RemoveSettingsTab();
            StyledLobby.Teardown();
            UIStyles.Active.TeardownMenu();
            Highlights.Clear();
            StyleUndo.Restore();
        }

        public static void Hide(Transform t, bool hide)
        {
            if (t == null) return;
            var cg = hide ? StyleUndo.Group(t) : t.GetComponent<CanvasGroup>();
            if (cg == null) return;
            cg.alpha = hide ? 0f : 1f;
            cg.blocksRaycasts = !hide;
        }

        public static void OnFollower(int kind, bool enabled)
        {
            if (!UIStyles.Styled) return;
            if (kind == StyleFollower.TOPBAR) StyledTopBar.OnRealTopBar(enabled);
            else if (kind == StyleFollower.LOBBY && enabled) StyledLobby.Refresh();
            else if (kind == StyleFollower.SUPPRESS_TOPBAR) StyledTopBar.Suppress(enabled);
        }

        public static void OnSettled(int kind, Transform t)
        {
            if (UIStyles.Styled) UIStyles.Active.OnSettled(kind, t);
        }

        public static void OnNavigatedToView(MainMenuViews view)
        {
            if (!UIStyles.Styled) return;
            StyledTopBar.Reevaluate();
            UIStyles.Active.OnView(view);
        }
    }
}
