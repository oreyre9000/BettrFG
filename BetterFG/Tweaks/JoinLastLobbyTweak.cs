using System;
using BetterFG.Core;
using BetterFG.Services;
using FG.Common.UI;
using FGClient.ShowSelector;
using FGClient.UI;
using Il2CppInterop.Runtime;
using MPG.Utility;
using Rewired;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Tweaks
{
    public class JoinLastLobbyTweak : BfgTweak
    {
        public JoinLastLobbyTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "join_last_lobby";
        public override string TweakLabel => "tweak.join_last_lobby";
        public override bool DefaultEnabled => true;

        private const int PopupAction = 178;
        private const string CodeKey = "join_last_lobby.code";

        private static JoinLastLobbyTweak _inst;
        private ModalMessageWithInputFieldViewModel _vm;
        private GameObject _row;
        private TMPro.TextMeshProUGUI _label;

        void Awake() => _inst = this;

        public override void DisableTweak()
        {
            if (_row != null) _row.SetActive(false);
            _vm = null;
        }

        public static void OnInputModal(Component c)
        {
            if (_inst == null || !_inst.IsEnabled) return;
            var vm = c.TryCast<ModalMessageWithInputFieldViewModel>();
            if (vm != null) _inst.Attach(vm);
        }

        private void Attach(ModalMessageWithInputFieldViewModel vm)
        {
            var onClosed = vm._onInputFieldModalClosed;
            bool lobby = onClosed != null && onClosed.Target != null && onClosed.Target.TryCast<ShowSelectorPrivateLobbiesTabViewModel>() != null;
            if (_row != null) _row.SetActive(false);
            _vm = null;
            if (!lobby) return;

            vm.MessageAdditional = "";
            vm._onInputFieldModalClosed = Il2CppSystem.Delegate.Combine(onClosed,
                DelegateSupport.ConvertDelegate<ModalMessageWithInputFieldViewModel.ModalMessageClosedDelegate>(new Action<bool, string>(OnClosed)))
                .Cast<ModalMessageWithInputFieldViewModel.ModalMessageClosedDelegate>();

            string code = SettingsService.Get(CodeKey, "");
            if (code.Length == 0) return;

            if (_row == null || _row.transform.parent != vm.transform.Find("Content")) BuildRow(vm);
            _label.text = LocalizationService.Format("join_last_lobby.button", code);
            _row.SetActive(true);
            _vm = vm;
        }

        private void BuildRow(ModalMessageWithInputFieldViewModel vm)
        {
            if (_row != null) Destroy(_row);
            ModalMessageWithInputFieldViewModel tpl = vm;
            foreach (var t in Resources.FindObjectsOfTypeAll<ModalMessageWithInputFieldViewModel>())
                if (!t.gameObject.scene.IsValid()) { tpl = t; break; }

            var content = vm.transform.Find("Content");
            _row = new GameObject("BettrFG_JoinLastLobby", Il2CppType.Of<RectTransform>());
            _row.transform.SetParent(content, false);
            _row.transform.SetSiblingIndex(content.Find("InputTextArea").GetSiblingIndex() + 1);
            _row.AddComponent<LayoutElement>().preferredHeight = 50;

            var btn = Instantiate(tpl.transform.Find("ButtonContainer/LeftButton").gameObject, _row.transform, false);
            DestroyImmediate(btn.GetComponent<global::UI.OnInputKeyDownClickUIButton>());
            DestroyImmediate(btn.GetComponent<FGClient.DelayedButton>());
            DestroyImmediate(btn.GetComponent<UIButtonTween>());
            btn.AddComponent<Button>().onClick.AddListener(new Action(Paste));
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(380, 50);
            foreach (var r in btn.GetComponentsInChildren<RectTransform>(true))
                if (r.name == "Panel_Shadow") r.anchoredPosition = new Vector2(0, -6);

            var inner = btn.transform.Find("Content");
            var glyph = inner.Find("ControlGlyphButton");
            glyph.localScale = Vector3.one * 0.8f;
            var acu = glyph.GetComponent<ActiveControllerUI>();
            acu._mappeable = false;
            acu._notMappeableKeyboardSprite = NavPromptCore.KeyboardGlyph(KeyCode.Tab);
            acu._notMappeableJoystickSprite = NavPromptCore.ResolvePadGlyph(SingletonBehaviour<SpriteManager>.Instance, PopupAction);
            acu._notMappeableKeyboardGlyphText = "";
            acu._notMappeableJoystickGlyphText = "";
            acu.UpdateGlyphsWithActiveController();
            var lbl = inner.Find("Text");
            DestroyImmediate(lbl.GetComponent<Mediatonic.Tools.MVVM.TextBinding>());
            _label = lbl.GetComponent<TMPro.TextMeshProUGUI>();
            _label.enableAutoSizing = false;
            _label.enableWordWrapping = false;
            _label.fontSize = 26;
        }

        private static void OnClosed(bool wasOk, string code)
        {
            if (!wasOk || string.IsNullOrWhiteSpace(code)) return;
            SettingsService.Set(CodeKey, code.Trim().ToUpperInvariant());
        }

        private void Paste()
        {
            if (_vm == null) return;
            _vm._inputField.text = SettingsService.Get(CodeKey, "");
            _vm._inputField.MoveTextEnd(false);
        }

        void Update()
        {
            if (_vm == null) return;
            if (!_vm.gameObject.activeInHierarchy) { _vm = null; return; }
            if (!_vm._isInFocus) return;
            bool alt = KeybindService.KeyHeld(KeyCode.LeftAlt) || KeybindService.KeyHeld(KeyCode.RightAlt);
            bool pad = ReInput.controllers.GetLastActiveControllerType() == ControllerType.Joystick;
            if ((!alt && KeybindService.KeyDown(KeyCode.Tab)) || (pad && ReInput.players.GetPlayer(0).GetButtonDown(PopupAction))) Paste();
        }
    }
}
