using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using UnityEngine;

namespace BetterFG.Utilities
{
    public static class AnimatedImage
    {
        public sealed class Result
        {
            public Texture2D[] Frames;
            public float[] Delays;
        }

        public static bool CanAnimate(string path)
        {
            string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
            return ext == ".gif" || ext == ".webp" || ext == ".png" || ext == ".apng";
        }

        public static Result Decode(byte[] data)
        {
            try
            {
                using var img = SixLabors.ImageSharp.Image.Load<Rgba32>(data);
                var format = img.Metadata.DecodedImageFormat;
                int w = img.Width, h = img.Height, n = img.Frames.Count;
                var frames = new Texture2D[n];
                var delays = new float[n];
                var px = new byte[w * h * 4];
                var flipped = new byte[px.Length];
                int stride = w * 4;
                for (int i = 0; i < n; i++)
                {
                    var f = img.Frames[i];
                    f.CopyPixelDataTo(px);
                    for (int y = 0; y < h; y++) Buffer.BlockCopy(px, y * stride, flipped, (h - 1 - y) * stride, stride);
                    var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                    tex.LoadRawTextureData(flipped);
                    tex.Apply();
                    frames[i] = tex;

                    float d = 0.1f;
                    if (format is GifFormat) d = f.Metadata.GetGifMetadata().FrameDelay / 100f;
                    else if (format is WebpFormat) d = f.Metadata.GetWebpMetadata().FrameDelay / 1000f;
                    else if (format is PngFormat) d = (float)f.Metadata.GetPngMetadata().FrameDelay.ToDouble();
                    delays[i] = d < 0.02f ? 0.1f : d;
                }
                return new Result { Frames = frames, Delays = delays };
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"couldn't decode that image ({data?.Length ?? 0} bytes): {ex.Message}");
                return null;
            }
        }
    }
}
