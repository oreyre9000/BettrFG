using System;
using System.IO;
using UnityEngine;

namespace BetterFG.Features.Replay
{
    internal static class ReplayThumbnail
    {
        public const string Extension = ".jpg";
        const int WIDTH = 1920;
        const int HEIGHT = 1080;
        const int QUALITY = 10;
        const int LIST_W = 256;
        const int LIST_H = 144;

        public static string PathFor(string replayPath) => Path.ChangeExtension(replayPath, Extension);

        static readonly System.Collections.Generic.Dictionary<string, RenderTexture> _cache =
            new System.Collections.Generic.Dictionary<string, RenderTexture>();

        public static void Forget(string replayPath)
        {
            if (!_cache.TryGetValue(replayPath, out var rt)) return;
            _cache.Remove(replayPath);
            if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
        }

        public static bool HasThumbnail(string replayPath) => File.Exists(PathFor(replayPath));

        public static Texture Peek(string replayPath)
            => _cache.TryGetValue(replayPath, out var cached) && cached != null ? cached : null;

        public static Texture Load(string replayPath)
        {
            if (_cache.TryGetValue(replayPath, out var cached) && cached != null) return cached;

            string path = PathFor(replayPath);
            if (!File.Exists(path)) return null;

            try
            {
                var full = new Texture2D(2, 2, TextureFormat.RGB24, false);
                full.LoadImage(File.ReadAllBytes(path));

                var rt = new RenderTexture(LIST_W, LIST_H, 0) { hideFlags = HideFlags.HideAndDontSave };
                rt.Create();
                Graphics.Blit(full, rt);
                UnityEngine.Object.Destroy(full);

                rt.filterMode = FilterMode.Bilinear;
                _cache[replayPath] = rt;
                return rt;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"preview for {Path.GetFileName(replayPath)} wouldn't load: {ex.Message}");
                return null;
            }
        }

        public static RenderTexture Grab(Camera cam)
        {
            if (cam == null) return null;

            var rt = new RenderTexture(WIDTH, HEIGHT, 24);
            rt.hideFlags = HideFlags.HideAndDontSave;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            return rt;
        }

        public static byte[] Encode(RenderTexture rt)
        {
            if (rt == null) return null;

            var shot = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            var was = RenderTexture.active;
            RenderTexture.active = rt;
            shot.ReadPixels(new Rect(0f, 0f, rt.width, rt.height), 0, 0, false);
            shot.Apply(false);
            RenderTexture.active = was;

            byte[] jpg = shot.EncodeToJPG(QUALITY);

            UnityEngine.Object.Destroy(shot);
            Release(rt);
            return jpg;
        }

        public static void Release(RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            UnityEngine.Object.Destroy(rt);
        }

        public static byte[] Shoot(Camera cam) => Encode(Grab(cam));

        public static void Write(byte[] jpg, string replayPath)
        {
            if (jpg == null || jpg.Length == 0 || string.IsNullOrEmpty(replayPath) || !File.Exists(replayPath)) return;

            try
            {
                File.WriteAllBytes(PathFor(replayPath), jpg);
                File.SetLastWriteTime(replayPath, DateTime.Now);
                Forget(replayPath);
                Plugin.Log.LogInfo($"preview shot for {Path.GetFileName(replayPath)}, {jpg.Length / 1024}kb");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"couldn't write the preview next to {Path.GetFileName(replayPath)}: {ex.Message}");
            }
        }

        public static void Capture(Camera cam, string replayPath)
        {
            if (cam == null || string.IsNullOrEmpty(replayPath) || !File.Exists(replayPath)) return;
            Write(Shoot(cam), replayPath);
        }
    }
}
