using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterFG.Customization.UI.Styles.Season1
{
    internal static class S1Tiles
    {
        public static void Pair(UIStyle style, Transform tile, GameObject stock, string label = null)
        {
            foreach (var c in tile.GetComponents<MonoBehaviour>())
            {
                var n = c.GetIl2CppType().Name;
                if (n == "UICustomToggle" || n == "EventTrigger" || n == "Toggle") Object.DestroyImmediate(c);
            }
            var act = S1Kit.Child(tile, "Active");
            var dis = S1Kit.Child(tile, "Disabled");
            var vfx = S1Kit.Child(tile, "VFX");
            S1Kit.Show(act);
            S1Kit.Show(dis);
            act.localScale = dis.localScale = Vector3.one;
            var actCg = act.GetComponent<CanvasGroup>();
            var disCg = dis.GetComponent<CanvasGroup>();
            var tw = tile.GetComponent<TweenOnToggleSubMenu>();
            if (tw != null)
            {
                tw._toggleToTween = null;
                tw._VFXPopline = null;
                tw._toggleOn = act.GetComponent<RectTransform>();
                tw._toggleOff = dis.GetComponent<RectTransform>();
                tw._activeToggleCanvasGroup = actCg;
                tw._inactiveToggleCanvasGroup = disCg;
                tw._alphaInTime = 0.1f;
                tw._playSFXOnSelect = false;
                tw._playTweenOnSelect = false;
                tw._selectOnMouseHover = false;
                tw._shouldCenterOnItem = false;
                if (tw._currentTweeners == null) tw._currentTweeners = new Il2CppSystem.Collections.Generic.List<DG.Tweening.Tweener>();
            }
            Animation pop = null;
            if (vfx != null)
            {
                var an = vfx.GetComponent<Animator>();
                if (an != null) Object.DestroyImmediate(an);
                foreach (var g in vfx.GetComponentsInChildren<Image>(true)) g.color = new Color(g.color.r, g.color.g, g.color.b, 0f);
                vfx.gameObject.SetActive(true);
                pop = StyleAnim.Load(style.Assets, vfx.gameObject, "PopLine");
            }
            if (label != null) foreach (var t in tile.GetComponentsInChildren<TMP_Text>(true)) t.text = label;
            var hit = tile.GetComponent<Image>();
            if (hit == null) hit = tile.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;
            tile.gameObject.SetActive(stock.activeSelf);

            void Set(bool on, bool animate)
            {
                if (tile == null) return;
                if (tw != null && animate)
                {
                    if (on) tw.StartTweenOn(); else tw.StartTweenOff();
                    if (on && pop != null) pop.Play("VFX_UI_Popline");
                    return;
                }
                actCg.alpha = on ? 1f : 0f;
                disCg.alpha = on ? 0f : 1f;
            }

            var menuTab = stock.GetComponent<FGClient.UI.UIButtonMenuTab>();
            S1Kit.Listen(tile.gameObject, EventTriggerType.PointerClick, () =>
            {
                if (menuTab != null) menuTab.Click();
                else S1Kit.Click(stock);
            });
            S1Kit.Listen(tile.gameObject, EventTriggerType.PointerEnter, () =>
                ExecuteEvents.Execute(stock, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler));
            S1Kit.Listen(tile.gameObject, EventTriggerType.PointerExit, () =>
                ExecuteEvents.Execute(stock, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler));
            bool last = menuTab != null ? menuTab.IsHighlightedTab : EventSystem.current != null && EventSystem.current.currentSelectedGameObject == stock;
            void Change(bool on)
            {
                if (on == last) return;
                last = on;
                Set(on, true);
            }
            if (menuTab != null) StyledUI.Highlights[stock.GetInstanceID()] = Change;
            else
            {
                S1Kit.Listen(stock, EventTriggerType.Select, () => Change(true));
                S1Kit.Listen(stock, EventTriggerType.Deselect, () => Change(false));
            }
            Set(last, false);
        }
    }
}
