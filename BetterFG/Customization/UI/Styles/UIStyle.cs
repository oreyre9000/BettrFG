using System.Collections;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.Utilities;
using FGClient;
using UnityEngine;

namespace BetterFG.Customization.UI.Styles
{
    public class UIStyle
    {
        public virtual string Id => "current";
        public virtual string LabelId => "layout.style.current";
        public virtual string Resources => null;
        public virtual string OnlineDir => null;
        public virtual TopBarSpec TopBar => null;
        public virtual LobbySpec Lobby => null;

        public virtual void ApplyMenu(MainMenuManager menu) { }
        public virtual void TeardownMenu() { }
        public virtual void OnScreen(Component screen) { }
        public virtual void OnModal(Component modal) { }
        public virtual void OnFocus(Component screen) { }
        public virtual void OnNavPrompts(Transform parent) { }
        public virtual void OnHud(Transform root) { }
        public virtual void OnLoading(Transform root) { }
        public virtual void OnPause(Transform root) { }
        public virtual void OnSettled(int kind, Transform t) { }
        public virtual void OnView(MainMenuViews view) { }
        public virtual bool OnShopMove(Component handler, int dir) => false;

        StyleAssets _assets;
        public StyleAssets Assets => Resources == null ? null : _assets ??= new StyleAssets(this);
    }

    public static class UIStyles
    {
        public const string SETTING = "ui.style";
        public static readonly UIStyle[] All = { new UIStyle(), new Season1.Season1Style() };
        static UIStyle _active;
        static bool _hasArt;
        static MainMenuManager _menu;

        public static UIStyle Active
        {
            get
            {
                if (_active != null) return _active;
                _active = Find(SettingsService.Get(SETTING, "current"));
                _hasArt = _active.OnlineDir == null;
                if (!_hasArt && AssetManager.Instance != null) AssetManager.Instance.StartCoroutine(FetchArt(_active).WrapToIl2Cpp());
                return _active;
            }
        }
        public static bool Styled => Active.Resources != null && _hasArt;

        static bool HasArt(UIStyle s) => s.OnlineDir == null
            || File.Exists(OnlineData.Dir("UIStyles", s.OnlineDir, "bundle")) && File.Exists(OnlineData.Dir("UIStyles", s.OnlineDir, "materials.json"));

        public static void Select(string id)
        {
            var next = Find(id);
            if (next == Active) return;
            StyledUI.Teardown();
            if (next.Resources != null) foreach (var k in MenuLayoutService.Kinds) MenuLayoutService.RestoreLive(k);
            _active = next;
            _hasArt = next.OnlineDir == null;
            SettingsService.Set(SETTING, next.Id);
            Plugin.Log.LogInfo($"ui style now {next.Id}");
            if (_hasArt) ApplyAll();
            else AssetManager.Instance.StartCoroutine(FetchArt(next).WrapToIl2Cpp());
        }

        public static void OnMainMenuEntered(MainMenuManager menu)
        {
            _menu = menu;
            if (Styled) StyledUI.Apply(menu);
            else if (Active.Resources != null) AssetManager.Instance.StartCoroutine(FetchArt(Active).WrapToIl2Cpp());
        }

        static void ApplyAll()
        {
            foreach (var k in MenuLayoutService.Kinds) MenuLayoutService.Apply(k);
            if (_menu != null) StyledUI.Apply(_menu);
        }

        static bool _fetching;

        static IEnumerator FetchArt(UIStyle style)
        {
            if (_fetching) yield break;
            _fetching = true;
            yield return OnlineData.Fetch("UIStyles/" + style.OnlineDir).WrapToIl2Cpp();
            _fetching = false;
            if (style != Active || _hasArt || !HasArt(style)) yield break;
            _hasArt = true;
            ApplyAll();
        }

        public static void OnTopBarTabsChanged()
        {
            if (Styled && _menu != null) StyledTopBar.Build(Active, _menu);
        }

        public static void OnScreen(Component screen) { if (Styled) Active.OnScreen(screen); }
        public static void OnModal(Component modal) { if (Styled) Active.OnModal(modal); }
        public static void OnFocus(Component screen) { if (Styled) Active.OnFocus(screen); }
        public static void OnNavPrompts(Transform parent) { if (Styled) Active.OnNavPrompts(parent); }
        public static void OnHud(Transform root) { if (Styled && root != null) Active.OnHud(root); }
        public static void OnLoading(Transform root) { if (Styled) Active.OnLoading(root); }
        public static void OnPause(Transform root) { if (Styled) Active.OnPause(root); }
        public static bool OnShopMove(Component handler, int dir) => Styled && Active.OnShopMove(handler, dir);

        static UIStyle Find(string id)
        {
            foreach (var s in All) if (s.Id == id) return s;
            return All[0];
        }
    }
}
