using System;
using System.Collections.Generic;
using System.IO;
using BetterFG.Features.Replay;
using BetterFG.Features.UnityRound.Editor;
using FG.Common;
using UnityEngine;

namespace BetterFG.Features.CreativeExpansion
{
    internal static class PublishThumbnail
    {
        internal static bool Enabled => FeatureCreativeExpansion.On("creativethumbnail");

        static Texture2D _source;

        public static string ChosenPath { get; private set; }
        public static string ChosenShareCode { get; private set; }

        public static bool Armed { get; set; }

        public static bool HasChoice => _source != null;

        public static bool ShouldSwap =>
            Enabled && Armed && _source != null && CreativeRoundMemory.GetCurrentShareCode() == ChosenShareCode;

        public static List<string> PicturesFor(string shareCode)
        {
            var hits = new List<string>();
            if (string.IsNullOrEmpty(shareCode)) return hits;

            foreach (string path in ReplayImages.ListFiles())
            {
                var meta = ReplayImages.ReadMeta(path);
                if (meta.isUgc && string.Equals(meta.shareCode, shareCode, StringComparison.OrdinalIgnoreCase))
                    hits.Add(path);
            }
            return hits;
        }

        public static void Choose(string path)
        {
            if (!Enabled) return;
            Clear();

            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            tex.LoadImage(File.ReadAllBytes(path));
            _source = tex;
            ChosenPath = path;
            ChosenShareCode = CreativeRoundMemory.GetCurrentShareCode();

            Plugin.Log.LogInfo($"publish thumbnail for {ChosenShareCode} is now {Path.GetFileName(path)}, {tex.width}x{tex.height}");
        }

        public static Texture2D EditorSizedCopy()
        {
            var mgr = LevelEditorManager.Instance;
            return Fit(mgr.ThumbWidthHighRes, mgr.ThumbHeightHighRes);
        }

        public static void PushToEditor() => LevelEditorManager.Instance.IO.SetThumbnail(EditorSizedCopy());

        public static void Clear()
        {
            if (_source != null) UnityEngine.Object.Destroy(_source);
            _source = null;
            ChosenPath = null;
            ChosenShareCode = null;
        }

        public static Texture2D Fit(int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0);

            float src = (float)_source.width / _source.height;
            float dst = (float)w / h;
            var scale = Vector2.one;
            var offset = Vector2.zero;
            if (src > dst) { scale.x = dst / src; offset.x = (1f - scale.x) * 0.5f; }
            else { scale.y = src / dst; offset.y = (1f - scale.y) * 0.5f; }
            Graphics.Blit(_source, rt, scale, offset);

            var was = RenderTexture.active;
            RenderTexture.active = rt;
            var fitted = new Texture2D(w, h, TextureFormat.RGB24, false);
            fitted.ReadPixels(new Rect(0f, 0f, w, h), 0, 0, false);
            fitted.Apply(false);
            RenderTexture.active = was;
            RenderTexture.ReleaseTemporary(rt);

            return fitted;
        }

        public static byte[] EncodeFor(int w, int h, int reference)
        {
            int cap = Math.Max(reference * 2, 120 * 1024);
            var fitted = Fit(w, h);

            int quality = 80;
            byte[] jpg = fitted.EncodeToJPG(quality);
            while (jpg.Length > cap && quality > 30)
            {
                quality -= 15;
                jpg = fitted.EncodeToJPG(quality);
            }

            UnityEngine.Object.Destroy(fitted);
            if (jpg.Length > cap) Plugin.Log.LogWarning($"thumbnail still {jpg.Length / 1024}kb at quality {quality}, over the {cap / 1024}kb we aimed for");
            return jpg;
        }
    }
}
