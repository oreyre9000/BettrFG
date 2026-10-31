using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI
{
    public static class PrivacyNotice
    {
        public const string KEY_SEEN = "privacy.seen";
        private const int CanvasOrder = 1500;
        private const int CoverOrder = 1550;
        private const float W = 560f, H = 270f, Pad = 22f, BtnW = 120f, BtnH = 32f;

        private static Canvas _canvas;
        private static GameObject _box;
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

            _canvas = UGUIShip.CreateCanvas("BetterFG_PrivacyCanvas");
            _canvas.sortingOrder = CanvasOrder;
            var bg = new GameObject("Black").AddComponent<RectTransform>();
            bg.SetParent(_canvas.transform, false);
            bg.anchorMin = Vector2.zero;
            bg.anchorMax = Vector2.one;
            bg.offsetMin = bg.offsetMax = Vector2.zero;
            bg.gameObject.AddComponent<Image>().color = Color.black;

            var box = new GameObject("Box").AddComponent<RectTransform>();
            box.SetParent(_canvas.transform, false);
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(W, H);
            _box = box.gameObject;
            UGUIShip.CreatePanel(box, new Rect(0f, 0f, W, H), new Color(0.11f, 0.11f, 0.12f, 1f));

            var title = UGUIShip.CreateLabel(box, new Rect(Pad, Pad, W - Pad * 2f, 30f), "Privacy", 22);
            title.fontStyle = FontStyle.Bold;
            var body = UGUIShip.CreateLabel(box, new Rect(Pad, Pad + 42f, W - Pad * 2f, H - Pad * 3f - 42f - BtnH),
                "When you apply a customization from a repo, BettrFG sends an anonymous count with the repo and pack name only. No account, player name, platform ID or device ID is sent. Your IP address is only used to count each player once a day and is never stored by BettrFG. Change this any time in Options. Full policy: bettrfg.com/privacy", 14, new Color(1f, 1f, 1f, 0.85f), TextAnchor.UpperLeft);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;

            float by = H - Pad - BtnH;
            UGUIShip.CreateButton(box, new Rect(W - Pad - BtnW * 2f - 10f, by, BtnW, BtnH), "Turn off",
                new Color(0.18f, 0.18f, 0.18f, 1f), Color.white, 13, new Action(TurnOff));
            UGUIShip.CreateButton(box, new Rect(W - Pad - BtnW, by, BtnW, BtnH), "Continue",
                new Color(0.25f, 0.5f, 0.25f, 1f), Color.white, 13, new Action(Continue));
            Plugin.Log.LogInfo("privacy notice up, holding the rest of boot until it's answered");

            yield return new WaitForSecondsRealtime(0.15f);
            yield return LoadingScreenService.HideRoutine();
            LoadingScreenService.Canvas.sortingOrder = LoadingScreenService.DefaultOrder;
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
            UnityEngine.Object.Destroy(_box);
            _box = null;

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
