using System;
using System.Collections.Generic;
using BetterFG.Core;
using FGClient.UI.Core;
using FGClient.UI.PrivateLobby;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Tweaks
{
    public class RandomShowSelectTweak : BfgTweak
    {
        public RandomShowSelectTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "random_show_select";
        public override string TweakLabel => "tweak.random_show_select";
        public override bool DefaultEnabled => true;
        public override string TweakTooltip => "tweak.random_show_select_tip";

        public static RandomShowSelectTweak Instance { get; private set; }
        void Awake() => Instance = this;

        private static readonly string[] LeftStickNames = { "Left Stick Button" };

        Transform _content;
        PrivateLobbyShowListViewModel _screenVm;
        NavigationPromptData _data;
        bool _injected;
        int _rowsSeen = -1;
        bool _hasShow;
        readonly List<PrivateLobbyShowListEntryViewModel> _liveScratch = new List<PrivateLobbyShowListEntryViewModel>();

        // PrivateLobbyShowListViewModel.Awake, off the same patch LobbyShowSearchTweak uses.
        public static void OnShowListAwake(PrivateLobbyShowListViewModel vm)
        {
            var inst = Instance;
            if (inst == null || !inst.IsEnabled || vm == null) return;
            inst._screenVm = vm;
            inst._content = vm.transform.Find(LobbyShowSearchTweak.ContentPath);
            inst._rowsSeen = -1;
        }

        public static void OnShowListClosed()
        {
            var inst = Instance;
            if (inst == null) return;
            inst._screenVm = null;
            inst._content = null;
            inst.DestroyPrompt();
        }

        void Update()
        {
            bool live = HasAnyShow();
            if (live != _injected)
            {
                if (live)
                {
                    if (_data == null)
                        _data = NavPromptInjection.BuildData(NavPrompt.Report, "Random", "bfg_random_show_prompt", -1, -1);
                    NavPromptInjection.Add(NavPromptInjection.RandomShow, Trigger, _data,
                        btn => NavPromptCore.ApplyOwnGlyphByElement(btn, KeyCode.R, NavPromptCore.CurrentPadElementByName(LeftStickNames)));
                }
                else NavPromptInjection.Remove(NavPromptInjection.RandomShow);
                _injected = live;
            }
            if (!live) return;

            if (BetterFG.Services.KeybindService.KeyDown(KeyCode.R) || NavPromptCore.ElementDownByName(LeftStickNames))
                Trigger();
        }

        bool HasAnyShow()
        {
            if (_content == null || _screenVm == null || !_screenVm.gameObject.activeInHierarchy)
            {
                _rowsSeen = -1;
                return false;
            }

            int n = _content.childCount;
            if (n == _rowsSeen) return _hasShow;
            _rowsSeen = n;
            _hasShow = false;

            for (int i = 0; i < n; i++)
            {
                var c = _content.GetChild(i);
                if (c.gameObject.activeInHierarchy && c.GetComponent<PrivateLobbyShowListEntryViewModel>() != null)
                {
                    _hasShow = true;
                    break;
                }
            }
            return _hasShow;
        }

        void Trigger()
        {
            if (_content == null) return;
            _liveScratch.Clear();
            int n = _content.childCount;
            for (int i = 0; i < n; i++)
            {
                var c = _content.GetChild(i);
                if (!c.gameObject.activeInHierarchy) continue;
                var vm = c.GetComponent<PrivateLobbyShowListEntryViewModel>();
                if (vm != null) _liveScratch.Add(vm);
            }
            if (_liveScratch.Count == 0) return;

            var pick = _liveScratch[UnityEngine.Random.Range(0, _liveScratch.Count)];
            Plugin.Log.LogInfo($"random show pick: {pick.ShowName} ({_liveScratch.Count} on screen)");

            var toggle = pick.GetComponent<Toggle>();
            if (toggle != null) toggle.isOn = true;
            else pick.OnClicked();

            _screenVm?.OnConfirmPressed();
            DestroyPrompt();
        }

        void DestroyPrompt()
        {
            if (!_injected) return;
            NavPromptInjection.Remove(NavPromptInjection.RandomShow);
            _injected = false;
        }

        public override void DisableTweak()
        {
            DestroyPrompt();
            _content = null;
            _screenVm = null;
        }
    }
}
