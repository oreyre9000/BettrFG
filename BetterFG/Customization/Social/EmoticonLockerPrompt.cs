using System;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.UI;
using FGClient.UI.Core;
using UnityEngine;

namespace BetterFG.Customization.Social
{
    public class EmoticonLockerPrompt : MonoBehaviour
    {
        public EmoticonLockerPrompt(IntPtr ptr) : base(ptr) { }

        private static readonly string[] PadButtonNames = { "Y", "Triangle", "X" };

        private NavigationPromptData _data;
        private bool _injected;

        void Update()
        {
            bool emoticon = EmoticonLockerService.HighlightedEntryId != null && EmoticonLockerService.SectionLive;
            bool phrase = PhraseLockerService.HighlightedEntryId != null && PhraseLockerService.SectionLive;
            bool live = (emoticon || phrase) && !ReportMenuShell.AnyOpen;
            if (live != _injected)
            {
                if (live)
                {
                    if (_data == null)
                        _data = NavPromptInjection.BuildData(NavPrompt.Report, LocalizationService.Get("emoticon_editor.prompt"), "bfg_emoticon_manage_prompt", -1, -1);
                    NavPromptInjection.Add(NavPromptInjection.EmoticonManage, OpenManageMenu, _data,
                        btn => NavPromptCore.ApplyOwnGlyphByElement(btn, KeyCode.Delete, NavPromptCore.CurrentPadElementByName(PadButtonNames)));
                }
                else NavPromptInjection.Remove(NavPromptInjection.EmoticonManage);
                _injected = live;
            }
            if (!live) return;

            if (KeybindService.KeyDown(KeyCode.Delete) || NavPromptCore.ElementDownByName(PadButtonNames))
                OpenManageMenu();
        }

        private static void OpenManageMenu()
        {
            if (EmoticonLockerService.SectionLive && EmoticonLockerService.HighlightedEntryId != null) EmoticonLockerService.OpenManageMenu();
            else PhraseLockerService.OpenManageMenu();
        }
    }
}
