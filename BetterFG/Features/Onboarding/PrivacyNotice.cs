using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.UI;
using BetterFG.UI.SideWheel;
using BettrFG.uGUI;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Features.Onboarding
{
    public static class PrivacyNotice
    {
        public const string KEY_SEEN = "privacy.seen";
        private const int CanvasOrder = 1500;
        private const int CoverOrder = 1550;

        private static Canvas _canvas;
        private static GameObject _popup;
        private static Action _next;

        public static bool Seen => SettingsService.Get(KEY_SEEN, "false") == "true";

        public static void ShowThen(Action next)
        {
            if (Seen) { next(); return; }
            _next = next;
            AssetManager.Instance.StartCoroutine(OpenRoutine().WrapToIl2Cpp());
        }

        private static IEnumerator OpenRoutine()
        {
            Cover();
            yield return new WaitForSecondsRealtime(0.35f);

            var uim = BetterFGUIMan.Instance;
            if (uim != null) { uim.SetVisibleForced(false); uim.VisibilityLocked = true; }
            SideWheelManager.InputLocked = true;

            _canvas = UGUIShip.CreateCanvas("BetterFG_PrivacyCanvas");
            _canvas.sortingOrder = CanvasOrder;
            var bg = new GameObject("Black");
            bg.transform.SetParent(_canvas.transform, false);
            var rt = bg.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            bg.AddComponent<Image>().color = Color.black;

            _popup = OnboardingPopup.Show(_canvas, "privacy.title", "privacy.body", Vector2.zero, new Vector2(0.5f, 0.5f),
                new[]
                {
                    new OnboardingPopup.ButtonSpec("privacy.turn_off", new Action(TurnOff), OnboardingPopup.BtnGhost),
                    new OnboardingPopup.ButtonSpec("privacy.continue", new Action(Continue), OnboardingPopup.BtnPrimary),
                });
            _popup.transform.localScale = new Vector3(0f, 1f, 1f);
            Plugin.Log.LogInfo("privacy notice up, holding the rest of boot until it's answered");

            yield return new WaitForSecondsRealtime(0.15f);
            yield return LoadingScreenService.HideRoutine();
            LoadingScreenService.Canvas.sortingOrder = LoadingScreenService.DefaultOrder;
            yield return PromptHost.OpenPopup(_popup);
            OnboardingPopup.AddCornerPops(_popup);
        }

        private static void Continue() => Close(true);

        private static void TurnOff() => Close(false);

        private static void Close(bool share)
        {
            if (_next == null) return;
            var next = _next;
            _next = null;
            UsageStats.On = share;
            SettingsService.Set(KEY_SEEN, "true");
            Plugin.Log.LogInfo($"privacy answered, usage stats {(share ? "on" : "off")}");
            AssetManager.Instance.StartCoroutine(CloseRoutine(next).WrapToIl2Cpp());
        }

        private static IEnumerator CloseRoutine(Action next)
        {
            Cover();
            yield return new WaitForSecondsRealtime(0.35f);
            UnityEngine.Object.Destroy(_popup);
            _popup = null;

            var uim = BetterFGUIMan.Instance;
            if (uim != null) uim.VisibilityLocked = false;
            SideWheelManager.InputLocked = false;

            yield return new WaitForSecondsRealtime(0.15f);
            yield return LoadingScreenService.HideRoutine();
            LoadingScreenService.Canvas.sortingOrder = LoadingScreenService.DefaultOrder;
            next();
            UnityEngine.Object.Destroy(_canvas.gameObject);
            _canvas = null;
        }

        private static void Cover()
        {
            LoadingScreenService.Show();
            LoadingScreenService.Canvas.sortingOrder = CoverOrder;
        }
    }
}
