using System.IO;
using UnityEngine;

namespace BetterFG.Editor
{
    public static class CoverImage
    {
        public const int W = 956;
        public const int H = 763;

        public static void Write(string sourcePath, string destDir)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath)) return;

            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!src.LoadImage(File.ReadAllBytes(sourcePath))) { Object.DestroyImmediate(src); return; }

            float srcAspect = (float)src.width / src.height;
            float dstAspect = (float)W / H;

            int cropX, cropY, cropW, cropH;
            if (srcAspect > dstAspect)
            {
                cropH = src.height;
                cropW = Mathf.RoundToInt(src.height * dstAspect);
                cropX = (src.width - cropW) / 2;
                cropY = 0;
            }
            else
            {
                cropW = src.width;
                cropH = Mathf.RoundToInt(src.width / dstAspect);
                cropX = 0;
                cropY = (src.height - cropH) / 2;
            }

            Color[] cropped = src.GetPixels(cropX, cropY, cropW, cropH);
            Object.DestroyImmediate(src);

            var tmp = new Texture2D(cropW, cropH, TextureFormat.RGB24, false);
            tmp.SetPixels(cropped);
            tmp.Apply();

            var rt = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;
            Graphics.Blit(tmp, rt);
            Object.DestroyImmediate(tmp);

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var final = new Texture2D(W, H, TextureFormat.RGB24, false);
            final.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            final.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            File.WriteAllBytes(Path.Combine(destDir, "cover.jpg"), final.EncodeToJPG(92));
            Object.DestroyImmediate(final);
        }
    }
}
