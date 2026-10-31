using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BetterFG.Utilities
{
    public static class Atlas
    {
        static readonly string[] Pages = { "ui", "creative" };
        static readonly bool[] Readable = { true, false };
        static readonly Texture2D[] _pages = new Texture2D[Pages.Length];
        static readonly Assembly asm = Assembly.GetExecutingAssembly();
        static Dictionary<string, (int page, RectInt rect)> _map;

        static Dictionary<string, (int page, RectInt rect)> Map
        {
            get
            {
                if (_map != null) return _map;
                _map = new Dictionary<string, (int, RectInt)>(StringComparer.Ordinal);
                for (int p = 0; p < Pages.Length; p++)
                {
                    using var s = EmbeddedResourceandUnity.Open($"BetterFG.assets.atlas.{Pages[p]}.txt");
                    if (s == null) { Plugin.Log.LogWarning($"no {Pages[p]} atlas manifest in the dll, everything in it will come up blank"); continue; }
                    using var r = new StreamReader(s);
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        var f = line.Split('\t');
                        if (f.Length == 5) _map[f[0]] = (p, new RectInt(int.Parse(f[1]), int.Parse(f[2]), int.Parse(f[3]), int.Parse(f[4])));
                    }
                }
                return _map;
            }
        }

        public static IEnumerable<string> Keys => Map.Keys;

        public static bool Has(string res) => res != null && Map.ContainsKey(res);

        static Texture2D Page(int p)
        {
            if (_pages[p] != null) return _pages[p];
            using var s = asm.GetManifestResourceStream($"BetterFG.assets.atlas.{Pages[p]}.png");
            var bytes = new byte[s.Length];
            s.Read(bytes, 0, bytes.Length);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(bytes, !Readable[p]);
            tex.name = "BettrFG_atlas_" + Pages[p];
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.hideFlags = HideFlags.HideAndDontSave;
            Plugin.Log.LogInfo($"{Pages[p]} atlas decoded, {tex.width}x{tex.height}");
            return _pages[p] = tex;
        }

        public static Sprite Sprite(string res, Vector2 pivot, float ppu = 100f, Vector4 border = default)
        {
            if (!Map.TryGetValue(res, out var e)) { Plugin.Log.LogWarning($"{res} isn't in an atlas"); return null; }
            return Sprite(res, new Rect(0, 0, e.rect.width, e.rect.height), pivot, ppu, border);
        }

        public static Sprite Sprite(string res, Rect sub, Vector2 pivot, float ppu, Vector4 border)
        {
            if (!Map.TryGetValue(res, out var e)) { Plugin.Log.LogWarning($"{res} isn't in an atlas"); return null; }
            var sprite = UnityEngine.Sprite.Create(Page(e.page), new Rect(e.rect.x + sub.x, e.rect.y + sub.y, sub.width, sub.height),
                pivot, ppu, 0, SpriteMeshType.FullRect, border);
            sprite.name = res;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        public static Texture2D Texture(string res, bool mips)
        {
            var (p, r) = Map[res];
            var tex = new Texture2D(r.width, r.height, TextureFormat.RGBA32, mips);
            tex.SetPixels(Page(p).GetPixels(r.x, r.y, r.width, r.height));
            tex.Apply(mips);
            tex.name = res;
            return tex;
        }
    }
}
