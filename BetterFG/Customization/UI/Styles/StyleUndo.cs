using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Customization.UI.Styles
{
    internal static class StyleUndo
    {
        static readonly List<(int Id, UnityEngine.Object O, Action Undo)> _undo = new List<(int, UnityEngine.Object, Action)>();
        static readonly HashSet<int> _seen = new HashSet<int>();
        static int _prune = 4096;

        static void Keep<T>(T o, Action<T> undo) where T : UnityEngine.Object
        {
            if (o == null || !_seen.Add(o.GetInstanceID())) return;
            _undo.Add((o.GetInstanceID(), o, () => { if (o != null) undo(o); }));
            if (_undo.Count < _prune) return;
            _undo.RemoveAll(u => u.O == null);
            _seen.Clear();
            foreach (var u in _undo) _seen.Add(u.Id);
            _prune = _undo.Count * 2 + 4096;
        }

        public static void Own(GameObject go) => Keep(go, g => UnityEngine.Object.Destroy(g));

        public static void Active(GameObject go)
        {
            if (go == null) return;
            bool was = go.activeSelf;
            Keep(go, g => g.SetActive(was));
        }

        public static void Enabled(Behaviour b)
        {
            if (b == null) return;
            bool was = b.enabled;
            Keep(b, x => x.enabled = was);
        }

        public static void Rect(Transform t)
        {
            var r = t != null ? t.GetComponent<RectTransform>() : null;
            if (r == null) return;
            Vector2 min = r.anchorMin, max = r.anchorMax, pivot = r.pivot, pos = r.anchoredPosition, size = r.sizeDelta;
            var scale = r.localScale;
            int sib = r.GetSiblingIndex();
            Keep(r, x =>
            {
                x.anchorMin = min; x.anchorMax = max; x.pivot = pivot;
                x.anchoredPosition = pos; x.sizeDelta = size; x.localScale = scale;
                x.SetSiblingIndex(sib);
            });
        }

        public static CanvasGroup Group(Transform t)
        {
            var cg = t.GetComponent<CanvasGroup>();
            bool added = cg == null;
            if (added) cg = t.gameObject.AddComponent<CanvasGroup>();
            float a = added ? 1f : cg.alpha;
            bool rays = added || cg.blocksRaycasts, inter = added || cg.interactable;
            Keep(cg, c => { c.alpha = a; c.blocksRaycasts = rays; c.interactable = inter; });
            return cg;
        }

        public static void Layout(HorizontalOrVerticalLayoutGroup h)
        {
            if (h == null) return;
            var p = h.padding;
            int l = p.left, r = p.right, top = p.top, bot = p.bottom;
            float sp = h.spacing;
            var align = h.childAlignment;
            Keep(h, x => { x.padding = new RectOffset(l, r, top, bot); x.spacing = sp; x.childAlignment = align; });
        }

        public static void Element(LayoutElement e)
        {
            if (e == null) return;
            float w = e.preferredWidth, hgt = e.preferredHeight;
            Keep(e, x => { x.preferredWidth = w; x.preferredHeight = hgt; });
        }

        public static void Graphic(Graphic g)
        {
            if (g == null) return;
            bool en = g.enabled;
            var col = g.color;
            var img = g.TryCast<Image>();
            if (img != null)
            {
                var sp = img.sprite;
                var ty = img.type;
                var mat = img.material;
                float ppu = img.pixelsPerUnitMultiplier;
                bool aspect = img.preserveAspect, center = img.fillCenter;
                Keep(img, x =>
                {
                    x.enabled = en; x.color = col; x.sprite = sp; x.type = ty; x.material = mat;
                    x.pixelsPerUnitMultiplier = ppu; x.preserveAspect = aspect; x.fillCenter = center;
                });
                return;
            }
            var tmp = g.TryCast<TMP_Text>();
            if (tmp != null)
            {
                var font = tmp.font;
                var mat = tmp.fontSharedMaterial;
                float size = tmp.fontSize, min = tmp.fontSizeMin, max = tmp.fontSizeMax, spacing = tmp.characterSpacing;
                var style = tmp.fontStyle;
                bool auto = tmp.enableAutoSizing, wrap = tmp.enableWordWrapping;
                var align = tmp.alignment;
                var overflow = tmp.overflowMode;
                Keep(tmp, x =>
                {
                    x.enabled = en; x.font = font; x.fontSharedMaterial = mat; x.color = col;
                    x.fontSizeMin = min; x.fontSizeMax = max; x.enableAutoSizing = auto; x.fontSize = size;
                    x.characterSpacing = spacing; x.fontStyle = style; x.alignment = align;
                    x.enableWordWrapping = wrap; x.overflowMode = overflow;
                });
                return;
            }
            Keep(g, x => { x.enabled = en; x.color = col; });
        }

        public static void Restore()
        {
            int n = _undo.Count;
            for (int i = n - 1; i >= 0; i--)
                try { _undo[i].Undo(); }
                catch (Exception e) { Plugin.Log.LogWarning($"style undo choked on one: {e.Message}"); }
            _undo.Clear();
            _seen.Clear();
            _prune = 4096;
            Plugin.Log.LogInfo($"put {n} stock ui bits back");
        }
    }
}
