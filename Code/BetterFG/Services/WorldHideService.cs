using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BetterFG.Services
{
    public static class WorldHideService
    {
        static readonly string[] GameUiRoots =
        {
            "UICanvas_Client_V2(Clone)",
            "Prefab_UI_NavigationOverlay(Clone)",
        };

        class Scope
        {
            public object owner;
            public readonly List<GameObject> roots = new List<GameObject>();
            public readonly List<Canvas> canvases = new List<Canvas>();
        }

        static readonly List<Scope> _scopes = new List<Scope>();
        static bool _menuMusicWasPlaying;

        public static bool Active => _scopes.Count > 0;

        public static void HideCanvases(object owner)
        {
            var scope = Push(owner);
            if (scope == null) return;
            TakeGameUi(scope);
            Plugin.Log.LogInfo($"{owner.GetType().Name} took the screen, {scope.canvases.Count} game canvases switched off");
        }

        public static void HideWorld(object owner, Func<GameObject, bool> keepVisible = null)
        {
            var scope = Push(owner);
            if (scope == null) return;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded) continue;
                foreach (var root in s.GetRootGameObjects())
                {
                    if (root == null || !root.activeSelf) continue;
                    if (root.name.StartsWith("BetterFG_") || root.name.StartsWith("BettrFG_")) continue;
                    if (root.GetComponentInChildren<SoundBankLoadingListener>(true) != null) continue;
                    if (keepVisible != null && keepVisible(root)) continue;
                    root.SetActive(false);
                    scope.roots.Add(root);
                }
            }

            TakeGameUi(scope);
            Plugin.Log.LogInfo($"{owner.GetType().Name} took the world, {scope.roots.Count} roots off + {scope.canvases.Count} canvases");
        }

        public static int Show(object owner)
        {
            var scope = Find(owner);
            if (scope == null) return 0;
            _scopes.Remove(scope);

            int restored = 0;
            foreach (var c in scope.canvases)
                if (c != null) { c.enabled = true; restored++; }
            foreach (var go in scope.roots)
                if (go != null) { go.SetActive(true); restored++; }

            if (_scopes.Count == 0)
            {
                FGInputLockService.SetScreenLock(false);
                if (_menuMusicWasPlaying) MenuMusicService.Resume();
                MenuMusicService.SetGameMenuMusicPaused(MenuMusicService.Enabled);
            }

            int lost = scope.roots.Count + scope.canvases.Count - restored;
            if (lost > 0) Plugin.Log.LogWarning($"gave the screen back to {owner.GetType().Name}, {lost} of {restored + lost} things had been destroyed under us");
            return restored;
        }

        static void TakeGameUi(Scope scope)
        {
            foreach (string path in GameUiRoots)
            {
                var go = GameObject.Find(path);
                if (go == null) continue;
                foreach (var canvas in go.GetComponentsInChildren<Canvas>(true))
                {
                    if (canvas == null || !canvas.enabled) continue;
                    canvas.enabled = false;
                    scope.canvases.Add(canvas);
                }
            }
        }

        static Scope Push(object owner)
        {
            if (owner == null || Find(owner) != null) return null;
            var scope = new Scope { owner = owner };
            _scopes.Add(scope);
            if (_scopes.Count == 1)
            {
                _menuMusicWasPlaying = MenuMusicService.IsPlaying;
                MenuMusicService.Pause();
                MenuMusicService.SetGameMenuMusicPaused(true);
                FGInputLockService.SetScreenLock(true);
            }
            return scope;
        }

        static Scope Find(object owner)
        {
            for (int i = 0; i < _scopes.Count; i++)
                if (ReferenceEquals(_scopes[i].owner, owner)) return _scopes[i];
            return null;
        }
    }
}
