using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using BetterFG.UI;
using FGClient;
using HarmonyLib;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class ChangeSplashScreenTweak : BfgTweak
    {
        public ChangeSplashScreenTweak(System.IntPtr ptr) : base(ptr) { }

        public override string TweakId => "custom_splash";
        public override string TweakLabel => "tweak.custom_splash_screen";
        public override bool DefaultEnabled => true;

        private const string PathKey = "tweak.custom_splash.path";

        private LoadingScreenViewModel _targetVm;
        private Sprite _originalSprite;
        private Sprite _customSprite;

        public static ChangeSplashScreenTweak Instance { get; private set; }
        void Awake() => Instance = this;

        public override List<TweakButton> GetCustomButtons() => new List<TweakButton>
        {
            new TweakButton { Label = "ui.load_png", Width = 46f, OnClick = PickAndReload }
        };

        public override TweakPreview GetPreview() => new TweakPreview
        {
            Height = 72f,
            Get = () => _customSprite != null ? _customSprite.texture : null
        };

        private void PickAndReload()
        {
            WinDialogs.PickPng("Select Splash PNG", path =>
            {
                if (path == null) return;
                var bytes = File.ReadAllBytes(path);
                SettingsService.Set(PathKey, path);
                StartCoroutine(ApplyBytes(bytes).WrapToIl2Cpp());
            });
        }

        private System.Collections.IEnumerator ApplyBytes(byte[] bytes)
        {
            yield return null;
            if (IsEnabled) DisableTweak();
            _customSprite = LoadSprite(bytes);
            ApplyToLiveInstance();
        }

        public override void EnableTweak()
        {
            var bytes = LoadBytes();
            if (bytes == null) return;
            _customSprite = LoadSprite(bytes);
            ApplyToLiveInstance();
        }

        private void ApplyToLiveInstance()
        {
            foreach (var vm in Resources.FindObjectsOfTypeAll<LoadingScreenViewModel>())
            {
                if (vm == null || vm._loadingScreenImage == null) continue;
                _targetVm = vm;
                _originalSprite = vm._loadingScreenImage.sprite;
                vm._loadingScreenImage.sprite = _customSprite;
                break;
            }
        }

        public override void DisableTweak()
        {
            if (_targetVm != null && _targetVm._loadingScreenImage != null && _originalSprite != null)
                _targetVm._loadingScreenImage.sprite = _originalSprite;
        }

        private byte[] LoadBytes()
        {
            var saved = SettingsService.Get(PathKey, "");
            if (!string.IsNullOrEmpty(saved) && File.Exists(saved))
                return File.ReadAllBytes(saved);
            return null;
        }

        private static Sprite LoadSprite(byte[] bytes)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            ImageConversion.LoadImage(tex, bytes);
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        // called from the shared LoadingScreenViewModel.UpdateDisplay hub in GameStatePatches.
        // RefreshLoadingScreenImage reassigns _loadingScreenImage.sprite from the game's own
        // asset whenever the screen shows/updates, which is what was stomping the old sprite-name
        // scan. writing straight to the instance's own field on every tick beats that race.
        public static void OnLoadingScreenUpdateDisplay(LoadingScreenViewModel instance)
        {
            var inst = Instance;
            if (inst == null || !inst.IsEnabled || inst._customSprite == null) return;
            if (instance == null || instance._loadingScreenImage == null) return;
            if (inst._originalSprite == null) inst._originalSprite = instance._loadingScreenImage.sprite;
            instance._loadingScreenImage.sprite = inst._customSprite;
            inst._targetVm = instance;
        }
    }
}