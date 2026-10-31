using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace BetterFG.Features.Replay
{
    internal class PictureMeta
    {
        public bool isUgc;
        public string shareCode = "";
        public string level = "";

        public string Label =>
            isUgc ? (string.IsNullOrEmpty(shareCode) ? "creative level" : shareCode)
                  : (string.IsNullOrEmpty(level) ? "unknown level" : level);
    }

    internal static class ReplayImages
    {
        public const string Extension = ".png";
        const int LIST_W = 320;
        const int LIST_H = 180;
        const string META_KEYWORD = "BettrFG";

        public static string Dir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BettrFG", "Images");

        public static string Capture(Camera cam, ReplayRecording rec, string label)
        {
            int w = Screen.width;
            int h = Screen.height;

            var rt = new RenderTexture(w, h, 24) { hideFlags = HideFlags.HideAndDontSave };
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;

            var shot = new Texture2D(w, h, TextureFormat.RGB24, false);
            var was = RenderTexture.active;
            RenderTexture.active = rt;
            shot.ReadPixels(new Rect(0f, 0f, w, h), 0, 0, false);
            shot.Apply(false);
            RenderTexture.active = was;

            byte[] png = shot.EncodeToPNG();
            UnityEngine.Object.Destroy(shot);
            rt.Release();
            UnityEngine.Object.Destroy(rt);

            png = WithMetadata(png, rec);

            string safe = string.Concat((string.IsNullOrEmpty(label) ? "replay" : label).Split(Path.GetInvalidFileNameChars()));
            string path = Path.Combine(Dir, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + safe + Extension);
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllBytes(path, png);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"picture wouldn't write to {path}: {ex.Message}");
                return null;
            }

            Plugin.Log.LogInfo($"snapped {w}x{h} on {(rec.isUgc ? rec.shareCode : rec.roundName)}, {png.Length / 1024}kb -> {Path.GetFileName(path)}");
            _files = null;
            return path;
        }

        static byte[] WithMetadata(byte[] png, ReplayRecording rec)
        {
            var text = new StringBuilder();
            text.Append("ugc=").Append(rec.isUgc ? '1' : '0');
            if (rec.isUgc) text.Append("\nshareCode=").Append(rec.shareCode ?? "");
            else text.Append("\nlevel=").Append(rec.roundName ?? "");

            byte[] keyword = Encoding.Latin1.GetBytes(META_KEYWORD);
            byte[] body = Encoding.Latin1.GetBytes(text.ToString());
            int dataLen = keyword.Length + 1 + body.Length;

            var chunk = new byte[12 + dataLen];
            WriteBE(chunk, 0, dataLen);
            chunk[4] = (byte)'t'; chunk[5] = (byte)'E'; chunk[6] = (byte)'X'; chunk[7] = (byte)'t';
            Buffer.BlockCopy(keyword, 0, chunk, 8, keyword.Length);
            chunk[8 + keyword.Length] = 0;
            Buffer.BlockCopy(body, 0, chunk, 9 + keyword.Length, body.Length);
            WriteBE(chunk, 8 + dataLen, (int)Crc32(chunk, 4, 4 + dataLen));

            int insertAt = 8 + 12 + ReadBE(png, 8);

            var result = new byte[png.Length + chunk.Length];
            Buffer.BlockCopy(png, 0, result, 0, insertAt);
            Buffer.BlockCopy(chunk, 0, result, insertAt, chunk.Length);
            Buffer.BlockCopy(png, insertAt, result, insertAt + chunk.Length, png.Length - insertAt);
            return result;
        }

        static readonly Dictionary<string, PictureMeta> _metaCache = new Dictionary<string, PictureMeta>();

        public static PictureMeta ReadMeta(string path)
        {
            if (_metaCache.TryGetValue(path, out var cached)) return cached;

            var meta = new PictureMeta();
            _metaCache[path] = meta;

            var head = new byte[4096];
            int got;
            try
            {
                using var fs = File.OpenRead(path);
                got = fs.Read(head, 0, head.Length);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"couldn't read {Path.GetFileName(path)}'s header: {ex.Message}");
                return meta;
            }

            int pos = 8;
            while (pos + 12 <= got)
            {
                int len = ReadBE(head, pos);
                if (len < 0 || pos + 12 + len > got) break;

                bool isText = head[pos + 4] == 't' && head[pos + 5] == 'E' && head[pos + 6] == 'X' && head[pos + 7] == 't';
                if (isText)
                {
                    string chunk = Encoding.Latin1.GetString(head, pos + 8, len);
                    int split = chunk.IndexOf('\0');
                    if (split > 0 && chunk.Substring(0, split) == META_KEYWORD)
                    {
                        foreach (string line in chunk.Substring(split + 1).Split('\n'))
                        {
                            int eq = line.IndexOf('=');
                            if (eq <= 0) continue;
                            string key = line.Substring(0, eq);
                            string value = line.Substring(eq + 1);
                            if (key == "ugc") meta.isUgc = value == "1";
                            else if (key == "shareCode") meta.shareCode = value;
                            else if (key == "level") meta.level = value;
                        }
                        return meta;
                    }
                }

                if (head[pos + 4] == 'I' && head[pos + 5] == 'D' && head[pos + 6] == 'A' && head[pos + 7] == 'T') break;
                pos += 12 + len;
            }
            return meta;
        }

        static int ReadBE(byte[] b, int i) => (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];

        static void WriteBE(byte[] b, int i, int v)
        {
            b[i] = (byte)(v >> 24);
            b[i + 1] = (byte)(v >> 16);
            b[i + 2] = (byte)(v >> 8);
            b[i + 3] = (byte)v;
        }

        static uint[] _crcTable;

        static uint Crc32(byte[] buf, int offset, int count)
        {
            if (_crcTable == null)
            {
                _crcTable = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    _crcTable[n] = c;
                }
            }

            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < count; i++) crc = _crcTable[(crc ^ buf[offset + i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        static List<string> _files;

        public static List<string> ListFiles()
        {
            if (_files != null) return _files;

            _files = new List<string>();
            try
            {
                if (Directory.Exists(Dir))
                {
                    var infos = new DirectoryInfo(Dir).GetFiles("*" + Extension);
                    Array.Sort(infos, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                    foreach (var f in infos) _files.Add(f.FullName);
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"couldn't list the pictures folder: {ex.Message}"); }
            return _files;
        }

        public static void Forget() => _files = null;

        static readonly Dictionary<string, RenderTexture> _thumbs = new Dictionary<string, RenderTexture>();

        public static Texture Thumb(string path)
        {
            if (_thumbs.TryGetValue(path, out var cached) && cached != null) return cached;

            try
            {
                var full = new Texture2D(2, 2, TextureFormat.RGB24, false);
                full.LoadImage(File.ReadAllBytes(path));

                var rt = new RenderTexture(LIST_W, LIST_H, 0) { hideFlags = HideFlags.HideAndDontSave };
                rt.Create();
                Graphics.Blit(full, rt);
                UnityEngine.Object.Destroy(full);

                rt.filterMode = FilterMode.Bilinear;
                _thumbs[path] = rt;
                return rt;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"{Path.GetFileName(path)} wouldn't decode: {ex.Message}");
                return null;
            }
        }

        public static bool Delete(string path)
        {
            try { File.Delete(path); }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"couldn't bin {Path.GetFileName(path)}: {ex.Message}");
                return false;
            }

            if (_thumbs.TryGetValue(path, out var rt))
            {
                _thumbs.Remove(path);
                if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
            }
            _metaCache.Remove(path);
            _files = null;
            Plugin.Log.LogInfo($"binned {Path.GetFileName(path)}");
            return true;
        }

        public static void Open(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { Plugin.Log.LogWarning($"nothing wanted to open {path}: {ex.Message}"); }
        }
    }
}
