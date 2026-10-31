using System;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterFG.Customization.UI.Styles.Season1
{
    internal static class S1Kit
    {
        static GameObject _donors;

        public static Transform Donor(UIStyle style, string layout)
        {
            if (_donors == null)
            {
                _donors = new GameObject("BFG_StyleDonors");
                UnityEngine.Object.DontDestroyOnLoad(_donors);
                _donors.SetActive(false);
            }
            var t = _donors.transform.Find(layout);
            if (t != null) return t;
            var built = StyleLayout.Build(style.Assets, layout, _donors.transform);
            built.name = layout;
            built.SetActive(true);
            return built.transform;
        }

        public static void Image(Transform dst, Transform donor, bool size = false)
        {
            if (dst == null || donor == null) return;
            var s = dst.GetComponent<Image>();
            var d = donor.GetComponent<Image>();
            if (s == null || d == null) return;
            StyleUndo.Graphic(s);
            s.sprite = d.sprite;
            s.type = d.type;
            s.color = d.color;
            s.material = d.material;
            s.pixelsPerUnitMultiplier = d.pixelsPerUnitMultiplier;
            s.preserveAspect = d.preserveAspect;
            s.fillCenter = d.fillCenter;
            if (!size) return;
            StyleUndo.Rect(dst);
            dst.GetComponent<RectTransform>().sizeDelta = donor.GetComponent<RectTransform>().sizeDelta;
        }

        public static void Text(Transform dst, Transform donor, bool size = true)
        {
            if (dst == null || donor == null) return;
            var s = dst.GetComponent<TMP_Text>();
            var d = donor.GetComponent<TMP_Text>();
            if (s == null || d == null) return;
            StyleUndo.Graphic(s);
            s.font = d.font;
            s.fontSharedMaterial = d.fontSharedMaterial;
            if (size) s.fontSize = d.fontSize;
            s.color = d.color;
            s.characterSpacing = d.characterSpacing;
            s.fontStyle = d.fontStyle;
        }

        public static void Off(Transform t)
        {
            if (t == null) return;
            var g = t.GetComponent<Graphic>();
            StyleUndo.Graphic(g);
            if (g != null) g.enabled = false;
        }

        public static void Stretch(Transform t)
        {
            var rt = t.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        public static void Show(Transform t)
        {
            t.gameObject.SetActive(true);
            var cg = t.GetComponent<CanvasGroup>();
            if (cg != null) { cg.alpha = 1f; cg.interactable = true; cg.blocksRaycasts = true; }
        }

        public static Transform Extract(UIStyle style, string layout, string path, Transform parent, string name)
        {
            var old = parent.Find(name);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var built = StyleLayout.Build(style.Assets, layout, parent);
            var t = built.transform.Find(path);
            t.SetParent(parent, false);
            t.name = name;
            UnityEngine.Object.DestroyImmediate(built);
            StyleUndo.Own(t.gameObject);
            Show(t);
            return t;
        }

        public static void Listen(GameObject go, EventTriggerType type, Action a)
        {
            var et = go.GetComponent<EventTrigger>();
            if (et == null) et = go.AddComponent<EventTrigger>();
            var e = new EventTrigger.Entry { eventID = type };
            e.callback.AddListener(DelegateSupport.ConvertDelegate<UnityAction<BaseEventData>>(new Action<BaseEventData>(_ => a())));
            et.triggers.Add(e);
        }

        public static void Click(GameObject go) =>
            ExecuteEvents.Execute(go, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);

        public static Transform Child(Transform t, string suffix)
        {
            for (int i = 0; i < t.childCount; i++) if (t.GetChild(i).name.EndsWith(suffix)) return t.GetChild(i);
            return null;
        }

        public static bool Ours(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
                if (p.name.StartsWith("BFG_") || p.name.StartsWith("BetterFG") || p.name.StartsWith("BettrFG")) return true;
            return false;
        }

        public static void Intro<T>(GameObject go, Action<T> setup) where T : Component
        {
            bool was = go.activeSelf;
            go.SetActive(false);
            var old = go.GetComponent<T>();
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            setup(go.AddComponent<T>());
            go.SetActive(was);
        }

        public static StyleFollower Follow(GameObject go, int kind, int settle)
        {
            var f = go.GetComponent<StyleFollower>();
            if (f == null) f = go.AddComponent<StyleFollower>();
            f.Kind = kind;
            f.Settle = settle;
            return f;
        }
    }
}
