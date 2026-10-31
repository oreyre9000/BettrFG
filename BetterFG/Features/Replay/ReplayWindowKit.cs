using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Features.Onboarding;
using BetterFG.UI;
using BetterFG.Utilities;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.Features.Replay
{
    internal static class ReplayWindowKit
    {
        public const float ROW = 22f;
        public const float PAD = 8f;
        public const float ARROW = 22f;
        public const float HEAD = 26f;
        public const float LABEL_W = 86f;
        const int TITLE_FS = 21;
        const float STRIPE_INSET = 4f;

        public static readonly Color PANEL_BG = new Color(0.06f, 0.07f, 0.09f, 0.94f);
        public static readonly Color HEADER_BG = new Color(0.13f, 0.15f, 0.19f, 1f);
        public static readonly Color BTN_DARK = new Color(0.18f, 0.18f, 0.2f, 1f);
        public static readonly Color BTN_GREEN = new Color(0.22f, 0.42f, 0.26f, 1f);
        public static readonly Color BTN_RED = new Color(0.45f, 0.18f, 0.18f, 1f);
        public static readonly Color HINT = new Color(1f, 1f, 1f, 0.45f);
        public static readonly Color ROW_EVEN = new Color(1f, 1f, 1f, 0.09f);
        public static readonly Color ROW_ODD = new Color(0f, 0f, 0f, 0f);
        public static readonly Color PICKABLE = new Color(1f, 1f, 1f, 0.07f);

        public static RectTransform Content(RectTransform window)
        {
            var go = new GameObject("Content");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(window, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Text Title(RectTransform content, float width, string text)
        {
            var label = UGUIShip.CreateLabel(content, new Rect(0f, 0f, width - PAD * 2f, HEAD), text, TITLE_FS, Color.white);
            label.fontStyle = FontStyle.Bold;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            UGUIShip.Unstylize(label);
            var rt = label.rectTransform;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(PAD + 4f, -(4f + HEAD * 0.5f));
            rt.localRotation = OnboardingPopup.TitleRot;
            rt.localScale = OnboardingPopup.TitleScale;
            return label;
        }

        static Sprite _mainSprite;
        static Sprite _editSprite;

        public static void MainBackdrop(RectTransform window) =>
            Backdrop(window, "BetterFG.assets.ui.replay.mainwindow.png", ref _mainSprite,
                new Vector4(80f, 200f, 480f, 80f), new Rect(48f, 36f, 990f, 690f));

        public static void EditBackdrop(RectTransform window) =>
            Backdrop(window, "BetterFG.assets.ui.replay.editwindow.png", ref _editSprite,
                new Vector4(80f, 200f, 200f, 60f), new Rect(36f, 18f, 1014f, 708f));

        static void Backdrop(RectTransform window, string resource, ref Sprite cache, Vector4 border, Rect panel)
        {
            if (cache == null)
            {
                var tex = EmbeddedResourceandUnity.LoadTexture(resource);
                cache = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), Vector2.zero,
                    100f, 0, SpriteMeshType.FullRect, border);
                cache.hideFlags = HideFlags.HideAndDontSave;
            }

            float s = window.sizeDelta.x / panel.width;
            float texW = cache.rect.width;
            float texH = cache.rect.height;

            var go = new GameObject("Backdrop");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(window, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-panel.x * s, -(texH - panel.yMax) * s);
            rt.offsetMax = new Vector2((texW - panel.xMax) * s, panel.y * s);
            var img = go.AddComponent<Image>();
            img.sprite = cache;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f / s;
            img.raycastTarget = false;
            rt.SetAsFirstSibling();

            var anchor = new GameObject("Panel").AddComponent<RectTransform>();
            anchor.SetParent(window, false);
            anchor.anchorMin = Vector2.zero;
            anchor.anchorMax = Vector2.one;
            anchor.offsetMin = anchor.offsetMax = Vector2.zero;
            anchor.SetAsFirstSibling();

            Pop(window);
        }

        public static void Pop(RectTransform window)
        {
            window.localScale = new Vector3(0f, 1f, 1f);
            ReplayViewer.Instance.StartCoroutine(PopRoutine(window.gameObject).WrapToIl2Cpp());
        }

        static IEnumerator PopRoutine(GameObject go)
        {
            yield return PromptHost.OpenPopup(go);
            if (go == null) yield break;
            var old = go.transform.Find("Pops");
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            OnboardingPopup.AddCornerPops(go);
        }

        public static void Close(RectTransform window)
        {
            if (window == null) return;
            if (ReplayViewer.Instance == null) { UnityEngine.Object.Destroy(window.gameObject); return; }
            ReplayViewer.Instance.StartCoroutine(PromptHost.CloseThenDestroy(window.gameObject).WrapToIl2Cpp());
        }

        public const float SECTION_H = 26f;

        public static void Section(RectTransform content, float y, float width, string title)
        {
            var label = UGUIShip.CreateLabel(content, new Rect(PAD, y + 10f, width - PAD * 2f, 16f), title, UIScale.FS_SM, Color.white);
            label.fontStyle = FontStyle.Bold;
            label.transform.localScale = new Vector3(1.15f, 1.15f, 1f);
        }

        public static void Stripe(RectTransform content, float y, float width, int row) =>
            ColorFill(UGUIShip.CreatePanel(content, new Rect(STRIPE_INSET, y, width - STRIPE_INSET * 2f, ROW),
                row % 2 == 0 ? ROW_EVEN : ROW_ODD, "Row"));

        public static RectTransform ColorFill(RectTransform rt)
        {
            var img = rt.GetComponent<Image>();
            UGUIShip.ApplyDeluxColorFill(img, 2f);
            img.raycastTarget = false;
            return rt;
        }

        public static void Carousel(RectTransform content, float y, float width, int row,
            string label, string value, Action prev, Action next, Action centre = null)
        {
            Stripe(content, y, width, row);
            UGUIShip.CreateLabel(content, new Rect(PAD, y, LABEL_W, ROW), label, UIScale.FS_SM, HINT);

            float x = PAD + LABEL_W;
            float valueW = width - x - PAD - ARROW * 2f - 4f;

            UGUIShip.CreateButton(content, new Rect(x, y, ARROW, ROW), "<", BTN_DARK, Color.white, UIScale.FS_SM, prev);

            if (centre == null)
                UGUIShip.CreateLabel(content, new Rect(x + ARROW + 2f, y, valueW, ROW), value,
                    UIScale.FS_SM, Color.white, TextAnchor.MiddleCenter);
            else
                UGUIShip.CreateButton(content, new Rect(x + ARROW + 2f, y, valueW, ROW), value,
                    PICKABLE, Color.white, UIScale.FS_SM, centre);

            UGUIShip.CreateButton(content, new Rect(x + ARROW + valueW + 4f, y, ARROW, ROW), ">", BTN_DARK, Color.white, UIScale.FS_SM, next);
        }

        public static int Step(int index, int dir, int count, bool wrap)
        {
            if (count <= 0) return 0;
            index += dir;
            if (!wrap) return Mathf.Clamp(index, 0, count - 1);
            while (index < 0) index += count;
            return index % count;
        }

        public static float Step(float v, int dir, float step, float min, float max, bool wrap)
        {
            float nv = v + dir * step;
            float span = max - min;
            if (!wrap || span <= 0f) return Mathf.Clamp(nv, min, max);
            return min + Mathf.Repeat(nv - min, span);
        }
    }
}
