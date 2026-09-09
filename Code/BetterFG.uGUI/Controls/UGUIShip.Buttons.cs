using System;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

namespace BettrFG.uGUI
{
    public static partial class UGUIShip
    {
        public enum ButtonStyle { Classic, Delux }

        // default look for every CreateButton that doesn't pass one explicitly.
        public static ButtonStyle DefaultButtonStyle = ButtonStyle.Delux;

        // paints the button background. classic = tinted button.png + shine; delux = untinted black
        // slice with a tinted colorfill slice on top. returns true if the caller should still add the
        // shine overlay (classic only).
        public static readonly Color TOGGLE_ON = new Color(0.3f, 0.75f, 0.3f, 1f);
        public static readonly Color TOGGLE_OFF = new Color(0.55f, 0.55f, 0.55f, 1f);

        private const float DeluxNormal = 0.65f;
        private const float DeluxHover = 0.95f;
        private const float DeluxPressed = 0.5f;

        private static bool SkinButton(GameObject go, Image img, Button btn, Color bgColor,
            bool customSprite, ButtonStyle style)
        {
            var cols = btn.colors;
            cols.fadeDuration = 0f;

            if (customSprite && style == ButtonStyle.Delux)
            {
                // white fill: the Button ColorTint drives it through canvasRenderer, so the graphic's
                // own colour must stay white or normal/hover both come out as bgColor².
                var fimg = ApplyDeluxSkin(go, img, Color.white, withShine: true);
                if (fimg != null)
                {
                    cols.normalColor = bgColor * DeluxNormal;
                    cols.highlightedColor = bgColor * DeluxHover;
                    cols.pressedColor = bgColor * DeluxPressed;
                    // Unity's default disabledColor is a bright grey, so a button inside a
                    // CanvasGroup that isn't interactable yet (the startup fades) lights up.
                    cols.disabledColor = bgColor * DeluxNormal;
                    btn.colors = cols;
                    btn.targetGraphic = fimg;
                    return false;
                }
            }

            img.color = bgColor;
            if (customSprite)
            {
                var s = GetButtonSprite();
                if (s != null) { img.sprite = s; img.type = Image.Type.Simple; }
            }
            cols.normalColor = bgColor;
            cols.highlightedColor = bgColor * 1.2f;
            cols.pressedColor = bgColor * 0.7f;
            cols.disabledColor = bgColor;
            btn.colors = cols;
            return customSprite;
        }

        public static void PlaySelectSound() => PlayClick();

        public static void WireButtonAudio(GameObject btn, bool skipHoverSound = false)
        {
            var trigger = btn.GetComponent<EventTrigger>() ?? btn.AddComponent<EventTrigger>();

            if (!skipHoverSound)
            {
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(new Action<BaseEventData>(_ => PlayHover()));
                trigger.triggers.Add(enter);
            }

        }

        private static void AddButtonClick(Button btn, Action onClick)
        {
            btn.onClick.AddListener(new Action(() =>
            {
                PlayClick();
                onClick?.Invoke();
                if (EventSystem.current != null)
                    EventSystem.current.SetSelectedGameObject(null);
            }));
        }

        // �� Button (Rect overload) ��������������������������������������������
        // passThroughDrag: skip the EventTrigger entirely. it implements IDragHandler, so uGUI hands
        // it the drag and an enclosing ScrollRect never sees one — list rows can't be dragged, and a
        // drag that starts on a row still counts as a click on release. with no trigger the
        // ScrollRect is the drag handler, wheel included, and the click cancels itself on drag.
        public static Button CreateButton(Transform parent, Rect rect, string label,
            Color bgColor, Color textColor, int fontSize = 13, Action onClick = null,
            bool skipHoverSound = false, bool customSprite = true, bool passThroughDrag = false,
            ButtonStyle? style = null)
        {
            var go = new GameObject("Button_" + label);
            go.transform.SetParent(parent, false);
            SetPixelRect(go.AddComponent<RectTransform>(), rect);

            var img = go.AddComponent<Image>();
            var btn = go.AddComponent<Button>();
            bool wantShine = SkinButton(go, img, btn, bgColor, customSprite, style ?? DefaultButtonStyle);

            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;

            AddButtonClick(btn, onClick);

            if (wantShine)
            {
                var shineGo = BuildShine(go);
                if (shineGo != null) WireShineHover(go, shineGo);
            }

            if (!passThroughDrag)
            {
                WireButtonAudio(go, skipHoverSound);
                ForwardScrollToParent(go);
            }

            CreateLabel(go.transform,
                new Rect(0, 0, rect.width, rect.height),
                label, fontSize, textColor, TextAnchor.MiddleCenter);

            return btn;
        }

        // �� Icon button (texture, no label, no background) ��������������������
        /// <summary>
        /// Creates a clickable RawImage button with no background or label.
        /// Navigation disabled, deselects on click, hover sound wired.
        /// Used by SideWheelManager icon slots.
        /// </summary>
        // hold-to-repeat button state: fires onFire once on press, then after HoldDelay seconds of
        // holding, repeats every RepeatInterval seconds until released. caller ticks
        // Tick(Time.unscaledDeltaTime) from its own ManagedUpdate/Update — no new MonoBehaviour/IL2Cpp
        // registration needed.
        public sealed class HoldButtonState
        {
            public float HoldDelay = 0.5f;
            public float RepeatInterval = 0.05f;
            public Action OnRelease; // fires once when the hold ends — used to commit one undo entry per hold
            private Action _fire;
            private bool _held;
            private bool _firedOnce;
            private float _timer;

            public void Tick(float dt)
            {
                if (!_held) return;
                _timer += dt;
                float threshold = _firedOnce ? RepeatInterval : HoldDelay;
                if (_timer >= threshold)
                {
                    _timer = 0f;
                    _firedOnce = true;
                    _fire?.Invoke();
                }
            }

            private void Press() { _held = true; _firedOnce = false; _timer = 0f; }
            private void Release()
            {
                if (!_held) return;
                _held = false;
                OnRelease?.Invoke();
            }

            public static HoldButtonState Wire(GameObject go, Action onFire)
            {
                var state = new HoldButtonState { _fire = onFire };
                var trigger = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();

                var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
                down.callback.AddListener(new Action<BaseEventData>(_ => { state.Press(); onFire?.Invoke(); }));
                trigger.triggers.Add(down);

                var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
                up.callback.AddListener(new Action<BaseEventData>(_ => state.Release()));
                trigger.triggers.Add(up);

                var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                exit.callback.AddListener(new Action<BaseEventData>(_ => state.Release()));
                trigger.triggers.Add(exit);

                return state;
            }
        }

        // button that fires onFire immediately on press, then repeats while held (HoldDelay, then
        // RepeatInterval). returns the HoldButtonState via out param — caller must call Tick() every frame.
        public static Button CreateHoldButton(Transform parent, Rect rect, string label,
            Color bgColor, Color textColor, int fontSize, Action onFire, out HoldButtonState holdState)
        {
            var btn = CreateButton(parent, rect, label, bgColor, textColor, fontSize, onClick: null);
            holdState = HoldButtonState.Wire(btn.gameObject, onFire);
            return btn;
        }

        public static Button CreateIconButton(Transform parent, Vector2 size, Texture2D icon,
            Action onClick, Action onHoverEnter = null, Action onHoverExit = null)
        {
            var go = new GameObject("IconButton");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            var raw = go.AddComponent<RawImage>();
            raw.texture = icon;
            raw.raycastTarget = true;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = raw;
            var cols = btn.colors;
            cols.normalColor = Color.white;
            cols.highlightedColor = Color.white;
            cols.pressedColor = Color.white;
            cols.disabledColor = Color.white;
            cols.colorMultiplier = 1f;
            btn.colors = cols;
            btn.transition = Selectable.Transition.None;
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;

            if (onClick != null)
                btn.onClick.AddListener(new Action(() =>
                {
                    onClick();
                    if (EventSystem.current != null)
                        EventSystem.current.SetSelectedGameObject(null);
                }));

            var trigger = go.AddComponent<EventTrigger>();

            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(new Action<BaseEventData>(_ =>
            {
                PlayHover();
                onHoverEnter?.Invoke();
            }));
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(new Action<BaseEventData>(_ => onHoverExit?.Invoke()));
            trigger.triggers.Add(exit);

            ForwardScrollToParent(go);

            return btn;
        }

        // �� Sprite button (custom sprite, no shine, standard click + hover audio) ��
        // for icon buttons that use a Sprite (not a Texture2D RawImage). pass an optional
        // hoverSprite to swap on hover/press. returns the Image so callers can change the sprite
        // later (e.g. a toggle star). no shine � icon buttons don't get the shine overlay.
        public static (Button btn, Image img) CreateSpriteButton(Transform parent, Rect rect,
            Sprite idle, Sprite hover = null, Action onClick = null, bool preserveAspect = true)
        {
            var go = new GameObject("SpriteButton");
            go.transform.SetParent(parent, false);
            SetPixelRect(go.AddComponent<RectTransform>(), rect);

            var img = go.AddComponent<Image>();
            img.color = Color.white;
            img.preserveAspect = preserveAspect;
            if (idle != null) img.sprite = idle;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            if (hover != null)
            {
                btn.transition = Selectable.Transition.SpriteSwap;
                var st = btn.spriteState;
                st.highlightedSprite = hover;
                st.pressedSprite = hover;
                st.selectedSprite = hover;
                btn.spriteState = st;
            }
            else btn.transition = Selectable.Transition.None;

            AddButtonClick(btn, onClick);
            WireButtonAudio(go); // hover sound, no shine
            ForwardScrollToParent(go);

            return (btn, img);
        }

        // �� Button (layout group variant) ������������������������������������
        public static Button CreateButton(Transform parent, string label,
            Color bgColor, Color textColor, int fontSize = 13, Action onClick = null,
            bool skipHoverSound = false, bool customSprite = true, bool shine = true,
            ButtonStyle? style = null, bool passThroughDrag = false)
        {
            var go = new GameObject("Button_" + label);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();

            var img = go.AddComponent<Image>();
            var btn = go.AddComponent<Button>();
            bool wantShine = SkinButton(go, img, btn, bgColor, customSprite, style ?? DefaultButtonStyle) && shine;

            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;

            AddButtonClick(btn, onClick);

            if (wantShine)
            {
                var shineGo = BuildShine(go);
                if (shineGo != null) WireShineHover(go, shineGo);
            }

            if (!passThroughDrag)
            {
                WireButtonAudio(go, skipHoverSound);
                ForwardScrollToParent(go);
            }

            var lblGo = new GameObject("Label");
            lblGo.transform.SetParent(go.transform, false);
            var lblRt = lblGo.AddComponent<RectTransform>();
            lblRt.anchorMin = Vector2.zero;
            lblRt.anchorMax = Vector2.one;
            lblRt.offsetMin = lblRt.offsetMax = Vector2.zero;
            var t = lblGo.AddComponent<Text>();
            t.text = LocText(label);
            Stylize(t);
            t.fontSize = fontSize;
            t.color = textColor;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            LocBind(t, label);

            return btn;
        }

        // �� Icon button (texture only, no bg, nav disabled, hover sound) ������
        public static Button CreateIconButton(Transform parent, Rect rect, Texture2D icon,
            Action onClick = null, int hoveredIdx = -1, Action<int> onHoverEnter = null,
            Action<int> onHoverExit = null, int idx = -1)
        {
            var go = new GameObject("IconButton");
            go.transform.SetParent(parent, false);
            SetPixelRect(go.AddComponent<RectTransform>(), rect);

            var raw = go.AddComponent<RawImage>();
            raw.texture = icon;
            raw.raycastTarget = true;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = raw;
            var cols = btn.colors;
            cols.normalColor = Color.white;
            cols.highlightedColor = Color.white;
            cols.pressedColor = Color.white;
            cols.disabledColor = Color.white;
            cols.colorMultiplier = 1f;
            btn.colors = cols;
            btn.transition = Selectable.Transition.None;
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;

            if (onClick != null)
                btn.onClick.AddListener(new Action(() =>
                {
                    onClick();
                    if (EventSystem.current != null)
                        EventSystem.current.SetSelectedGameObject(null);
                }));

            var trigger = go.AddComponent<EventTrigger>();

            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(new Action<BaseEventData>(_ =>
            {
                PlayHover();
                onHoverEnter?.Invoke(idx);
            }));
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(new Action<BaseEventData>(_ => onHoverExit?.Invoke(idx)));
            trigger.triggers.Add(exit);

            ForwardScrollToParent(go);

            return btn;
        }

        // delux buttons drive their visible color through targetGraphic (the ColorFill child, at
        // SkinButton's Delux* multipliers) — mirror that here so re-coloring after creation (selection
        // state, etc.) doesn't jump brighter than the button looked when it was first built, and
        // don't touch the root Image, which is the untinted black base, not the button's color.
        public static void SetButtonColor(Button btn, Color color)
        {
            if (btn == null) return;
            var cols = btn.colors;
            bool delux = btn.targetGraphic != null && btn.targetGraphic.gameObject != btn.gameObject;
            if (delux)
            {
                cols.normalColor = color * DeluxNormal;
                cols.highlightedColor = color * DeluxHover;
                cols.pressedColor = color * DeluxPressed;
                btn.colors = cols;
                return;
            }
            cols.normalColor = color;
            cols.highlightedColor = color * 1.2f;
            cols.pressedColor = color * 0.8f;
            btn.colors = cols;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = color;
        }

        public static void SetButtonSelected(Button btn, bool selected, Color selectedColor)
            => SetButtonColor(btn, selected ? selectedColor : new Color(0.2f, 0.2f, 0.2f, 1f));
    }
}
