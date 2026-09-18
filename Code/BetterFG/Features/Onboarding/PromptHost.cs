using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using BettrFG.uGUI;
using UnityEngine;

namespace BetterFG.Features.Onboarding
{
    public class PromptHost : MonoBehaviour
    {
        public PromptHost(IntPtr ptr) : base(ptr) { }

        protected Canvas _canvas;
        protected Canvas _blockerCanvas;
        protected OnboardingBlocker _blocker;
        protected GameObject _popup;
        private readonly List<Canvas> _hidden = new List<Canvas>();

        protected void CreateSurfaces(string name, int canvasOrder, int blockerOrder, bool hideRest = false)
        {
            if (hideRest)
                foreach (var c in UIScaleService.All)
                    if (c != null && c.enabled && !c.name.EndsWith("Backdrop")) { c.enabled = false; _hidden.Add(c); }

            _blockerCanvas = UGUIShip.CreateCanvas("BetterFG_" + name + "Blocker");
            _blockerCanvas.sortingOrder = blockerOrder;
            _blocker = new OnboardingBlocker(_blockerCanvas);
            _blocker.SetHole(null);

            _canvas = UGUIShip.CreateCanvas("BetterFG_" + name + "Canvas");
            _canvas.sortingOrder = canvasOrder;
        }

        protected void Swap(GameObject next)
        {
            if (_popup != null) StartCoroutine(CloseThenDestroy(_popup).WrapToIl2Cpp());
            _popup = next;
            StartCoroutine(OpenPopup(next).WrapToIl2Cpp());
        }

        protected void CloseCurrent()
        {
            if (_popup == null) return;
            StartCoroutine(CloseThenDestroy(_popup).WrapToIl2Cpp());
            _popup = null;
        }

        public static IEnumerator CloseThenDestroy(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            float e = 0f;
            while (e < OnboardingPopup.AnimDur)
            {
                e += Time.unscaledDeltaTime;
                rt.localScale = new Vector3(1f - Mathf.Clamp01(e / OnboardingPopup.AnimDur), 1f, 1f);
                yield return null;
            }
            UnityEngine.Object.Destroy(go);
        }

        public static IEnumerator OpenPopup(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            float e = 0f;
            while (e < OnboardingPopup.AnimDur)
            {
                e += Time.unscaledDeltaTime;
                rt.localScale = new Vector3(
                    OnboardingPopup.OpenCurve.Evaluate(Mathf.Clamp01(e / OnboardingPopup.AnimDur)), 1f, 1f);
                yield return null;
            }
            rt.localScale = Vector3.one;
        }

        protected IEnumerator TearDownSurfaces()
        {
            if (_popup != null)
            {
                yield return CloseThenDestroy(_popup);
                _popup = null;
            }
            if (_canvas != null) UnityEngine.Object.Destroy(_canvas.gameObject);
            if (_blockerCanvas != null) UnityEngine.Object.Destroy(_blockerCanvas.gameObject);
            foreach (var c in _hidden) if (c != null) c.enabled = true;
            _hidden.Clear();
        }
    }
}
