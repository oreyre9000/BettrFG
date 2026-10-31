using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Utilities
{
    internal static class AssetPin
    {
        static Transform _root;
        static readonly HashSet<int> _held = new HashSet<int>();

        static Transform Root()
        {
            if (_root != null) return _root;
            var go = new GameObject("BFG_AssetPins");
            go.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(go);
            _root = go.transform;
            return _root;
        }

        static GameObject Holder()
        {
            var g = new GameObject("pin");
            g.transform.SetParent(Root(), false);
            return g;
        }

        public static void Hold(Sprite s)
        {
            if (s == null || !_held.Add(s.GetInstanceID())) return;
            Holder().AddComponent<Image>().sprite = s;
        }

        public static void Hold(Texture t)
        {
            if (t == null || !_held.Add(t.GetInstanceID())) return;
            Holder().AddComponent<RawImage>().texture = t;
        }
    }
}
