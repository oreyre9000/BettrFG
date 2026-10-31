using System.Collections.Generic;
using UnityEngine;
using Text = UnityEngine.UI.Text;

namespace BettrFG.uGUI
{
    // recolors a link Text on hover. lives on the link's transparent hit rect so CreateLinkLabel
    // callers don't have to poll hover state themselves.
    public sealed class LinkHover
    {
        public Text Text;
        public Color Idle;
        public Color Hover;
        private GameObject _go;
        private RectTransform _rt;
        private bool _over;

        private static readonly List<LinkHover> _all = new List<LinkHover>();

        public static LinkHover Attach(GameObject go, Text text, Color idle, Color hover)
        {
            var h = new LinkHover { _go = go, _rt = go.GetComponent<RectTransform>(), Text = text, Idle = idle, Hover = hover };
            _all.Add(h);
            return h;
        }

        internal static void Pump()
        {
            if (_all.Count == 0) return;
            var mp = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
            for (int i = _all.Count - 1; i >= 0; i--)
            {
                var h = _all[i];
                if (h._go == null || h.Text == null) { _all.RemoveAt(i); continue; }
                if (!h._go.activeInHierarchy) continue;
                bool over = RectTransformUtility.RectangleContainsScreenPoint(h._rt, mp, null);
                if (over == h._over) continue;
                h._over = over;
                h.Text.color = over ? h.Hover : h.Idle;
            }
        }
    }
}
