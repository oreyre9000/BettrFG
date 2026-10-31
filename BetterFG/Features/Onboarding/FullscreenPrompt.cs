using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.UI;
using FGClient;
using UnityEngine;

namespace BetterFG.Features.Onboarding
{
    public class FullscreenPrompt : PromptHost
    {
        public FullscreenPrompt(IntPtr ptr) : base(ptr) { }

        private const int CanvasOrder = 1402;
        private const int BlockerOrder = 1401;

        private static FullscreenPrompt _live;
        private static bool _pass;
        private static Action _intercepted;
        private Action _open;
        private Action _cancel;

        public static bool Intercept(string mode, Action open)
        {
            if (_pass || GlobalGameStateClient.Instance?._gameStateMachine?.CurrentState?.TryCast<StateMainMenu>() != null) return false;
            if (_live != null) return true;
            _intercepted = open;
            Ask("fullscreen_prompt." + mode + ".title", "fullscreen_prompt." + mode + ".body", "ui.ok",
                new Action(RunIntercepted), PopUpKind.Warning);
            return true;
        }

        private static void RunIntercepted()
        {
            _pass = true;
            try { _intercepted(); }
            finally { _pass = false; }
        }

        public static void Ask(string title, string body, string confirm, Action onConfirm, PopUpKind kind = PopUpKind.None,
            float customH = 0f, Action<RectTransform> onCustom = null, Action onCancel = null)
        {
            if (_live != null) return;

            var go = new GameObject("BettrFG_FullscreenPrompt");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            _live = go.AddComponent<FullscreenPrompt>();
            _live._open = onConfirm;
            _live._cancel = onCancel;
            _live.CreateSurfaces("FullscreenPrompt", CanvasOrder, BlockerOrder, true);
            _live._blockerCanvas.GetComponentInChildren<UnityEngine.UI.Image>().color = new Color(0f, 0f, 0f, OnboardingPopup.DimAlpha(kind));
            _live.Swap(OnboardingPopup.Show(_live._canvas, title, body,
                Vector2.zero, new Vector2(0.5f, 0.5f),
                new[]
                {
                    new OnboardingPopup.ButtonSpec("ui.cancel", new Action(_live.Cancel), OnboardingPopup.BtnGhost),
                    new OnboardingPopup.ButtonSpec(confirm, new Action(_live.Confirm), OnboardingPopup.ConfirmColor(kind)),
                }, customH: customH, onCustom: onCustom, kind: kind));
        }

        private void Confirm() => StartCoroutine(Finish(true).WrapToIl2Cpp());

        private void Cancel() => StartCoroutine(Finish(false).WrapToIl2Cpp());

        private IEnumerator Finish(bool open)
        {
            if (_live != this) yield break;
            _live = null;
            yield return TearDownSurfaces();
            if (open) _open();
            else if (_cancel != null) _cancel();
            else BetterFGUIMan.Instance?.SetVisible(true);
            Destroy(gameObject);
        }
    }
}
