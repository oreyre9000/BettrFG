using System;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using FGClient.UI.Core;
using UnityEngine;
using LB = Wushu.LevelEditor.Runtime.UI.LevelBrowser;

namespace BetterFG.Features.CreativeExpansion
{
    // Watches the creative Level Browser. While a real level tile is highlighted it floats a nav
    // prompt in the game's own NavigationOverlay row — Share on a pad, O on keyboard. Pressing it
    // opens the game's UGC report popup repurposed as an Import / Export chooser
    // (LevelPortReportMenu). Persistent singleton spawned from Plugin.InitGameObjects.
    public class LevelBrowserPortPrompt : MonoBehaviour
    {
        public LevelBrowserPortPrompt(IntPtr ptr) : base(ptr) { }

        public static LevelBrowserPortPrompt Instance { get; private set; }

        private LB.LevelBrowserTileViewModel _tileVm;
        private NavigationPromptData _data;
        private bool _injected;

        private static readonly string[] ShareButtonNames = { "Share", "Create", "View", "Back", "Minus" };

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // LevelBrowserTileViewModel.OnSelected postfix routes here
        public static void OnTileSelected(LB.LevelBrowserTileViewModel vm)
        {
            var inst = Instance;
            if (inst == null) return;
            inst._tileVm = vm;
        }

        void Update()
        {
            BetterFG.UI.WinDialogs.Tick();
            BetterFG.Features.CreativeExpansion.LevelBrowserFilter.Tick(_tileVm);

            if (!FeatureCreativeExpansion.On("levelport"))
            {
                if (_injected) { NavPromptInjection.Remove(NavPromptInjection.LevelPort); _injected = false; }
                return;
            }

            bool live = _tileVm != null && _tileVm.gameObject.activeInHierarchy && _tileVm.HasLevel;
            if (live != _injected)
            {
                if (live)
                {
                    if (_data == null)
                        _data = NavPromptInjection.BuildData(NavPrompt.Report, "Import / Export", "bfg_levelport_prompt", -1, -1);
                    NavPromptInjection.Add(NavPromptInjection.LevelPort, Trigger, _data,
                        btn => NavPromptCore.ApplyOwnGlyphByElement(btn, KeyCode.O, NavPromptCore.CurrentPadElementByName(ShareButtonNames)));
                }
                else NavPromptInjection.Remove(NavPromptInjection.LevelPort);
                _injected = live;
            }
            if (!live) return;

            if (LevelPortReportMenu.AnyOpen) return;
            if (BetterFG.Services.KeybindService.KeyDown(KeyCode.O) ||
                NavPromptCore.ElementDownByName(ShareButtonNames))
                Trigger();
        }

        private void Trigger()
        {
            if (LevelPortReportMenu.AnyOpen || _tileVm == null) return;
            StartCoroutine(LevelPortReportMenu.OpenRoutine(_tileVm.TileData).WrapToIl2Cpp());
        }
    }
}
