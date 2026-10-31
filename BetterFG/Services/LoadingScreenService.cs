using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine;
using BetterFG.Core;
using BetterFG.UI;
using BettrFG.uGUI;

namespace BetterFG.Services
{
    // fullscreen cover for canvas open/close transitions, spawned from the bettrfg_ui_canvas_loadingscreen
    // bundle. one shared canvas, reused by whatever's transitioning (replay viewer today) so it always
    // sits above everything and nothing pops in underneath it.
    public static class LoadingScreenService
    {
        private static Canvas _canvas;

        public static bool IsVisible => _canvas != null && _canvas.gameObject.activeSelf;

        // the shared cover's canvas, for callers that have temporarily reordered the UI around it
        // (the onboarding tutorial lifts everything, so it has to put this back on top itself)
        public static Canvas Canvas => _canvas;
        public const int DefaultOrder = 1002;

        public static void Show()
        {
            if (_canvas == null)
            {
                _canvas = UGUIShip.CreateCanvas("BettrFG_LoadingCanvas");
                _canvas.sortingOrder = DefaultOrder;

                var go = AssetManager.SpawnPersistent("bettrfg_ui_canvas_loadingscreen");
                if (go != null)
                {
                    go.transform.SetParent(_canvas.transform, false);
                    var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = rt.offsetMax = Vector2.zero;
                }
            }
            _canvas.gameObject.SetActive(true);
        }

        // fire-and-forget hide for callers that don't need to know when it's actually gone
        public static void Hide()
        {
            if (AssetManager.Instance == null) return;
            AssetManager.Instance.StartCoroutine(HideRoutine().WrapToIl2Cpp());
        }

        // plays the outro on the prefab's own animator instead of just yanking it away, then hides
        // once that's had time to play out. yield on this directly when whatever comes next (a scene
        // reload, say) can't be allowed to race the animation.
        public static IEnumerator HideRoutine()
        {
            if (_canvas == null || !_canvas.gameObject.activeSelf) yield break;

            Animator anim = _canvas.GetComponentInChildren<Animator>(true);
            if (anim != null)
            {
                anim.SetTrigger("end");
                yield return new WaitForSecondsRealtime(0.7f);
            }
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }
    }
}
