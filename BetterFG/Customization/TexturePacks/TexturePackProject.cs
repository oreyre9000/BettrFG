using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using BetterFG.Services;
using BetterFG.Utilities;
using UnityEngine;

namespace BetterFG.Customization.TexturePacks
{
    public class TexturePackFile
    {
        public string texName;
        public string fileName;
        public string hash;
    }

    public class TexturePackProject
    {
        // absolute folder the user picked for this project - manifest.json, Textures/ and the
        // packed .bfgtp all live directly under it
        public string location = "";
        public string name = "";
        public string author = "";
        public int compressionPercent = 100;
        public string coverArt = "";
        public List<TexturePackFile> files = new List<TexturePackFile>();

        public string ManifestPath => Path.Combine(location, "manifest.json");
        public string TexturesDir => Path.Combine(location, "Textures");
        public string PackedPath => Path.Combine(location, SafeName(name) + ".bfgtp");

        public static string SafeName(string n)
        {
            string safe = n ?? "";
            foreach (var c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
            return string.IsNullOrEmpty(safe) ? "pack" : safe;
        }

        private const string KEY_KNOWN = "texpack.knownProjects";

        // pipe-joined absolute folder paths - the tab's row list. registering just means "show up
        // in the list", never touches anything on disk.
        public static List<string> KnownFolders()
        {
            var result = new List<string>();
            foreach (var f in SettingsService.Get(KEY_KNOWN, "").Split('|'))
                if (!string.IsNullOrEmpty(f)) result.Add(f);
            return result;
        }

        public static void Register(string folder)
        {
            var known = KnownFolders();
            foreach (var f in known)
                if (string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)) return;
            known.Add(folder);
            SettingsService.Set(KEY_KNOWN, string.Join("|", known));
        }

        public static void Unregister(string folder)
        {
            var known = KnownFolders();
            known.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
            SettingsService.Set(KEY_KNOWN, string.Join("|", known));
        }

        public static List<TexturePackProject> LoadKnown()
        {
            var result = new List<TexturePackProject>();
            foreach (var folder in KnownFolders())
            {
                var p = Load(folder);
                if (p != null) result.Add(p);
                else Plugin.Log.LogWarning($"texture pack at {folder} has no manifest anymore, skipping it");
            }
            return result;
        }

        public static TexturePackProject Load(string folder)
        {
            string manifestPath = Path.Combine(folder, "manifest.json");
            if (!File.Exists(manifestPath)) return null;

            string json = File.ReadAllText(manifestPath);
            var p = new TexturePackProject
            {
                location = folder,
                name = JsonUtil.GetValue(json, "name"),
                author = JsonUtil.GetValue(json, "author"),
                compressionPercent = JsonUtil.GetInt(json, "compressionPercent", 100),
                coverArt = JsonUtil.GetValue(json, "coverArt"),
            };
            foreach (var obj in JsonUtil.GetArray(json, "files"))
            {
                var d = JsonUtil.ReadFlatObject(obj);
                if (!d.TryGetValue("texName", out var texName) || string.IsNullOrEmpty(texName)) continue;
                p.files.Add(new TexturePackFile
                {
                    texName = texName,
                    fileName = d.TryGetValue("fileName", out var fn) ? fn : texName + ".png",
                    hash = d.TryGetValue("hash", out var h) ? h : "",
                });
            }
            return p;
        }

        public void Save()
        {
            Directory.CreateDirectory(location);
            File.WriteAllText(ManifestPath, WriteManifest(this));
            Register(location);
        }

        private static string WriteManifest(TexturePackProject p)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"name\": \"{JsonUtil.Escape(p.name)}\",\n");
            sb.Append($"  \"author\": \"{JsonUtil.Escape(p.author)}\",\n");
            sb.Append($"  \"compressionPercent\": {p.compressionPercent},\n");
            sb.Append($"  \"coverArt\": \"{JsonUtil.Escape(p.coverArt ?? "")}\",\n");
            sb.Append("  \"files\": [\n");
            for (int i = 0; i < p.files.Count; i++)
            {
                var f = p.files[i];
                sb.Append("    {");
                sb.Append($"\"texName\": \"{JsonUtil.Escape(f.texName)}\", ");
                sb.Append($"\"fileName\": \"{JsonUtil.Escape(f.fileName)}\", ");
                sb.Append($"\"hash\": \"{f.hash}\"");
                sb.Append("}");
                sb.Append(i < p.files.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        private static readonly Dictionary<string, Texture2D> _coverCache =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        public Texture2D LoadCover()
        {
            if (string.IsNullOrEmpty(coverArt) || !File.Exists(coverArt)) return null;
            if (_coverCache.TryGetValue(coverArt, out var cached) && cached != null) return cached;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(File.ReadAllBytes(coverArt))) { UnityEngine.Object.Destroy(tex); return null; }
            tex.hideFlags = HideFlags.HideAndDontSave;
            _coverCache[coverArt] = tex;
            return tex;
        }

        public static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.MD5.HashData(bytes));

        public static string Hash(string path) => Hash(File.ReadAllBytes(path));

        // only files whose current bytes differ from what was exported (the base game texture) are
        // worth shipping - identical ones mean the user never touched that one
        public IEnumerator PackRoutine(Action<int, int> progress, Action<string, string> done)
        {
            if (!File.Exists(ManifestPath)) { done(null, "no project exported yet"); yield break; }

            var included = new List<TexturePackFile>();
            foreach (var f in files)
            {
                string path = Path.Combine(TexturesDir, f.fileName);
                if (!File.Exists(path)) continue;
                if (Hash(path) == f.hash) continue;
                included.Add(f);
            }
            if (included.Count == 0) { done(null, "nothing modified"); yield break; }

            string outPath = PackedPath;
            string tmpPath = outPath + ".tmp";
            if (File.Exists(tmpPath)) File.Delete(tmpPath);

            bool hasCover = !string.IsNullOrEmpty(coverArt) && File.Exists(coverArt);
            using (var zip = ZipFile.Open(tmpPath, ZipArchiveMode.Create))
            {
                var manifestOut = new TexturePackProject
                {
                    name = name,
                    author = author,
                    compressionPercent = compressionPercent,
                    coverArt = hasCover ? "cover.png" : "",
                    files = included,
                };
                using (var w = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
                    w.Write(WriteManifest(manifestOut));

                if (hasCover) zip.CreateEntryFromFile(coverArt, "cover.png");

                for (int i = 0; i < included.Count; i++)
                {
                    var f = included[i];
                    progress(i + 1, included.Count);
                    yield return null;

                    var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    decoded.LoadImage(File.ReadAllBytes(Path.Combine(TexturesDir, f.fileName)));
                    int w = Mathf.Max(1, decoded.width * compressionPercent / 100);
                    int h = Mathf.Max(1, decoded.height * compressionPercent / 100);
                    var readable = Blit(decoded, w, h, false, true);
                    UnityEngine.Object.Destroy(decoded);

                    byte[] rgba = Raw(readable);
                    byte[] alpha = null;
                    var work = System.Threading.Tasks.Task.Run(() => alpha = SplitAlpha(rgba, w, h));
                    while (!work.IsCompleted) yield return null;

                    string baseName = Path.GetFileNameWithoutExtension(f.fileName);
                    if (alpha != null)
                    {
                        Upload(readable, rgba);
                        var mask = new Texture2D(w, h, TextureFormat.RGBA32, false);
                        Upload(mask, alpha);
                        Write(zip, "Textures/" + baseName + ".a.png", mask.EncodeToPNG());
                        UnityEngine.Object.Destroy(mask);
                    }
                    Write(zip, "Textures/" + baseName + ".jpg", readable.EncodeToJPG(JPEG_QUALITY));
                    UnityEngine.Object.Destroy(readable);
                }
            }
            if (File.Exists(outPath)) File.Delete(outPath);
            File.Move(tmpPath, outPath);
            Plugin.Log.LogInfo($"packed {included.Count} textures into {Path.GetFileName(outPath)}, {new FileInfo(outPath).Length / 1024}KB");
            done(outPath, null);
        }

        public static IEnumerator ImportRoutine(string bfgtpPath, Action<int, int> progress, Action<TexturePackProject, string> done)
        {
            TexturePackProject p = null;
            var entries = new List<(string baseName, byte[] colour, byte[] alpha)>();
            byte[] cover = null;
            string error = null;
            try
            {
                using var zip = ZipFile.OpenRead(bfgtpPath);
                var manifest = zip.GetEntry("manifest.json");
                if (manifest == null) throw new InvalidDataException("no manifest.json in it");
                string json;
                using (var mr = new StreamReader(manifest.Open())) json = mr.ReadToEnd();
                p = new TexturePackProject
                {
                    name = JsonUtil.GetValue(json, "name"),
                    author = JsonUtil.GetValue(json, "author"),
                    compressionPercent = JsonUtil.GetInt(json, "compressionPercent", 100),
                };
                foreach (var obj in JsonUtil.GetArray(json, "files"))
                {
                    var d = JsonUtil.ReadFlatObject(obj);
                    if (!d.TryGetValue("texName", out var texName) || string.IsNullOrEmpty(texName)) continue;
                    p.files.Add(new TexturePackFile { texName = texName, fileName = d.TryGetValue("fileName", out var fn) ? fn : texName + ".png", hash = "" });
                }
                var byBase = new Dictionary<string, (byte[] colour, byte[] alpha)>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.Equals("cover.png", StringComparison.OrdinalIgnoreCase)) { cover = ReadAll(entry); continue; }
                    if (!entry.FullName.StartsWith("Textures/", StringComparison.OrdinalIgnoreCase)) continue;
                    string baseName = Path.GetFileNameWithoutExtension(entry.Name);
                    bool isAlpha = baseName.EndsWith(".a", StringComparison.OrdinalIgnoreCase);
                    if (isAlpha) baseName = baseName.Substring(0, baseName.Length - 2);
                    byBase.TryGetValue(baseName, out var pair);
                    if (isAlpha) pair.alpha = ReadAll(entry); else pair.colour = ReadAll(entry);
                    byBase[baseName] = pair;
                }
                foreach (var kv in byBase) if (kv.Value.colour != null) entries.Add((kv.Key, kv.Value.colour, kv.Value.alpha));
            }
            catch (Exception ex) { error = ex.Message; }
            if (error != null) { done(null, error); yield break; }

            p.location = Path.Combine(SettingsService.TexturePacksDir, "Imported", SafeName(p.name));
            Directory.CreateDirectory(p.TexturesDir);
            if (cover != null)
            {
                p.coverArt = Path.Combine(p.location, "cover.png");
                File.WriteAllBytes(p.coverArt, cover);
            }

            var fileByBase = new Dictionary<string, TexturePackFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in p.files) fileByBase[Path.GetFileNameWithoutExtension(f.fileName)] = f;

            for (int i = 0; i < entries.Count; i++)
            {
                progress(i + 1, entries.Count);
                yield return null;
                var (baseName, colour, alpha) = entries[i];
                var tex = Decode(colour, alpha, false, true);
                if (!fileByBase.TryGetValue(baseName, out var f))
                {
                    f = new TexturePackFile { texName = baseName, fileName = baseName + ".png", hash = "" };
                    p.files.Add(f);
                }
                f.fileName = Path.GetFileNameWithoutExtension(f.fileName) + ".png";
                File.WriteAllBytes(Path.Combine(p.TexturesDir, f.fileName), tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
            }

            p.Save();
            if (!string.Equals(Path.GetFullPath(bfgtpPath), Path.GetFullPath(p.PackedPath), StringComparison.OrdinalIgnoreCase))
                File.Copy(bfgtpPath, p.PackedPath, true);
            Plugin.Log.LogInfo($"imported pack '{p.name}' by {p.author}, {entries.Count} textures, now living at {p.location}");
            done(p, null);
        }

        private static byte[] ReadAll(ZipArchiveEntry entry)
        {
            using var ms = new MemoryStream();
            using (var s = entry.Open()) s.CopyTo(ms);
            return ms.ToArray();
        }

        private static void Write(ZipArchive zip, string path, byte[] bytes)
        {
            using var s = zip.CreateEntry(path, System.IO.Compression.CompressionLevel.Optimal).Open();
            s.Write(bytes, 0, bytes.Length);
        }

        private const int JPEG_QUALITY = 92;
        private const int DILATE_PASSES = 4;

        private static byte[] SplitAlpha(byte[] rgba, int w, int h)
        {
            int n = w * h;
            bool any = false;
            for (int i = 3; i < rgba.Length; i += 4) if (rgba[i] < 255) { any = true; break; }
            if (!any) return null;

            var alpha = new byte[rgba.Length];
            var filled = new bool[n];
            for (int i = 0; i < n; i++)
            {
                byte a = rgba[i * 4 + 3];
                alpha[i * 4] = alpha[i * 4 + 1] = alpha[i * 4 + 2] = a;
                alpha[i * 4 + 3] = 255;
                filled[i] = a > 0;
            }
            for (int pass = 0; pass < DILATE_PASSES; pass++)
            {
                var next = (bool[])filled.Clone();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (filled[i]) continue;
                        int from = -1;
                        if (x > 0 && filled[i - 1]) from = i - 1;
                        else if (x < w - 1 && filled[i + 1]) from = i + 1;
                        else if (y > 0 && filled[i - w]) from = i - w;
                        else if (y < h - 1 && filled[i + w]) from = i + w;
                        if (from < 0) continue;
                        rgba[i * 4] = rgba[from * 4];
                        rgba[i * 4 + 1] = rgba[from * 4 + 1];
                        rgba[i * 4 + 2] = rgba[from * 4 + 2];
                        next[i] = true;
                    }
                filled = next;
            }
            return alpha;
        }

        public static Texture2D Decode(byte[] colour, byte[] alpha, bool mips, bool readable)
        {
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            decoded.LoadImage(colour);
            int w = decoded.width, h = decoded.height;
            if (alpha == null)
            {
                var plain = Blit(decoded, w, h, mips, readable);
                UnityEngine.Object.Destroy(decoded);
                return plain;
            }

            var maskDecoded = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            maskDecoded.LoadImage(alpha);
            var colourTex = Blit(decoded, w, h, false, true);
            var maskTex = Blit(maskDecoded, w, h, false, true);
            byte[] rgba = Raw(colourTex);
            byte[] mask = Raw(maskTex);
            for (int i = 0; i < rgba.Length; i += 4) rgba[i + 3] = mask[i];
            Upload(colourTex, rgba);
            UnityEngine.Object.Destroy(decoded);
            UnityEngine.Object.Destroy(maskDecoded);
            UnityEngine.Object.Destroy(maskTex);
            if (!mips && readable) return colourTex;
            var final = Blit(colourTex, w, h, mips, readable);
            UnityEngine.Object.Destroy(colourTex);
            return final;
        }

        public static Texture2D Blit(Texture src, int w, int h, bool mips, bool readable)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, mips) { hideFlags = HideFlags.HideAndDontSave };
            var prev = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply(mips, !readable);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        public static byte[] Raw(Texture2D readable)
        {
            var arr = readable.GetRawTextureData();
            var bytes = new byte[arr.Length];
            System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(arr.Pointer, 4 * IntPtr.Size), bytes, 0, bytes.Length);
            return bytes;
        }

        public static unsafe void Upload(Texture2D readable, byte[] rgba)
        {
            fixed (byte* p = rgba) readable.LoadRawTextureData((IntPtr)p, rgba.Length);
            readable.Apply(false, false);
        }
    }
}
