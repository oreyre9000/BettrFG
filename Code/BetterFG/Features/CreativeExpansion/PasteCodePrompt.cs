using System;
using BetterFG.Core;
using FGClient;
using FGClient.UI.Core;
using UnityEngine;

namespace BetterFG.Features.CreativeExpansion
{
    // "Paste Code" on the game's own Favourite nav prompt (Triangle on DS4/DS5, Y on Xbox, whatever
    // the equivalent is elsewhere) while the "Enter Code" input field is live. Favourite has no
    // keyboard binding at all in the game's own input config, so this only ever shows/fires on a
    // pad, same as CinematicSpectatorTweak's use of it. Pressing it pushes the clipboard straight
    // through InputCodeViewModel.OnValueChanged - the exact entry point the field itself calls when
    // its value changes - so sanitising/formatting/validation all run the same as if it were typed.
    //
    // The prompt is injected into whatever nav-prompt set the current screen has broadcast, via
    // NavPromptInjection — the manager builds and owns the button itself, just like every game-
    // authored prompt. The press is polled through NavPromptCore.PollActionDirect so it survives
    // the Menu Rewired category being disabled while the input field is typed into.
    // Persistent singleton spawned from Plugin.InitGameObjects.
    public class PasteCodePrompt : MonoBehaviour
    {
        public PasteCodePrompt(IntPtr ptr) : base(ptr) { }

        public static PasteCodePrompt Instance { get; private set; }

        private InputCodeViewModel _vm;
        private NavigationPromptData _data;
        private bool _injected;

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public static void OnInputEnabled(InputCodeViewModel vm)
        {
            if (Instance == null) return;
            Instance._vm = vm;
        }

        public static void OnInputDisabled()
        {
            if (Instance == null) return;
            Instance._vm = null;
        }

        void Update()
        {
            if (!FeatureCreativeExpansion.On("levelcode"))
            {
                if (_injected) { NavPromptInjection.Remove(NavPromptInjection.PasteCode); _injected = false; }
                return;
            }

            bool live = _vm != null && _vm.gameObject.activeInHierarchy;

            if (live != _injected)
            {
                if (live)
                {
                    if (_data == null)
                        _data = NavPromptInjection.BuildData(NavPromptCore.Favourite, "Paste Code", "bfg_pastecode_prompt",
                            RewiredConsts.Action.Menu_Favourite, RewiredConsts.Category.Menu);
                    NavPromptInjection.Add(NavPromptInjection.PasteCode, Paste, _data);
                }
                else NavPromptInjection.Remove(NavPromptInjection.PasteCode);
                _injected = live;
            }
            if (!live) return;

            if (NavPromptCore.PollActionDirect(RewiredConsts.Action.Menu_Favourite, null, true))
                Paste();
        }

        private void Paste()
        {
            string text = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(text) || _vm == null) return;
            _vm.OnValueChanged(text.Trim());
        }
    }
}
