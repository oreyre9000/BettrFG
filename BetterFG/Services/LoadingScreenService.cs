using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.UI;
using UnityEngine;

namespace BetterFG.Services
{
    public static class LoadingScreenService
    {
        private static Canvas _canvas;

        public static bool IsVisible => _canvas != null && _canvas.gameObject.activeSelf;

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

        public static void Hide()
        {
            if (AssetManager.Instance == null) return;
            AssetManager.Instance.StartCoroutine(HideRoutine().WrapToIl2Cpp());
        }

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
