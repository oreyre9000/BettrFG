using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using FG.Common;
using FGClient;

namespace BetterFG.Customization.Player
{
    // one texture slot swap within an entry - texName is the ORIGINAL texture's name, the
    public class SkinTexOverride
    {
        public string texName;
        public string texPath;
        public byte[] texData;
    }

    // one shader property tweak on a named material - matName is the CleanMatName identity we
    // match against on a bean's live materials. kind: "color" | "vector" (both use x/y/z/w),
    // or "float" (covers Float/Range/Int - f only)
    public class MatPropOverride
    {
        public string matName;
        public string prop;
        public string kind;
        public float f;
        public float x, y, z, w;
    }

    public class SkinTexEntry
    {
        public string entryName;
        public bool enabled;
        public string category = SkinTexCategory.Upper;
        public string costumeName;

        public List<string> matNames = new List<string>();
        public List<SkinTexOverride> overrides = new List<SkinTexOverride>();
        public List<MatPropOverride> matProps = new List<MatPropOverride>();
    }

    public static class SkinTexCategory
    {
        public const string Upper = "upper";
        public const string Lower = "lower";
        public const string Pattern = "pattern";
        public const string Colour = "colour";
        public const string Faceplate = "faceplate";

        public static bool IsOptionField(string category)
            => category == Colour || category == Faceplate;
    }

    public partial class SkinApplicationService
    {
        // keyed by bean instance id — original materials per renderer before we touched them
        private Dictionary<int, List<CustomTexOriginal>> customTexOriginals = new Dictionary<int, List<CustomTexOriginal>>();
        // was a fixed 4-name list (_MainTex/_BaseMap/etc) - now reads every texture slot the
        // shader actually declares, so metallic/normal/emission maps show up too, not just albedo
        public static IEnumerable<string> GetTextureProps(Material mat)
        {
            var shader = mat != null ? mat.shader : null;
            if (shader == null) yield break;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
                if (shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Texture)
                    yield return shader.GetPropertyName(i);
        }

        // every non-texture property the shader declares - rim colour, emissive, metallic,
        // smoothness, gradient vectors, whatever the shader actually has. rangeMin/Max are only
        // meaningful when type is Range (Shader.GetPropertyRangeLimits); otherwise both 0.
        public static IEnumerable<(string name, UnityEngine.Rendering.ShaderPropertyType type, float rangeMin, float rangeMax)> GetEditableProps(Material mat)
        {
            var shader = mat != null ? mat.shader : null;
            if (shader == null) yield break;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                var t = shader.GetPropertyType(i);
                if (t == UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                float rMin = 0f, rMax = 0f;
                if (t == UnityEngine.Rendering.ShaderPropertyType.Range)
                {
                    var limits = shader.GetPropertyRangeLimits(i);
                    rMin = limits.x; rMax = limits.y;
                }
                yield return (shader.GetPropertyName(i), t, rMin, rMax);
            }
        }

        private struct CustomTexOriginal
        {
            public Renderer renderer;
            public int matIdx;
            public string prop;
            public Texture texture;
            public string textureName;
        }

        // keyed by bean instance id — original color/float values before a MatPropOverride touched them
        private Dictionary<int, List<CustomPropOriginal>> customPropOriginals = new Dictionary<int, List<CustomPropOriginal>>();

        private struct CustomPropOriginal
        {
            public Renderer renderer;
            public int matIdx;
            public string prop;
            public string kind;
            public float f;
            public Vector4 v;
        }

        // process-wide cache of decoded custom textures. each bean push used to re-read the file
        // and re-decode the PNG/JPG on the main thread, which stacks freezes during state changes
        // (round load, qual, reward — each pushes the local bean and OnBeansFound iterates every
        // saved entry). keyed on path + write timestamp so editing the file invalidates.
        private static readonly Dictionary<string, (long stamp, Texture2D tex)> _customTexCache =
            new Dictionary<string, (long, Texture2D)>(StringComparer.OrdinalIgnoreCase);

        const string KEY_ENTRY_COUNT = "skintex.entryCount";

        public static int EntryCount
            => int.TryParse(SettingsService.Get(KEY_ENTRY_COUNT, "0"), out int c) ? c : 0;

        private static List<SkinTexEntry> _entriesCache;
        private static int _entriesCacheVersion = -1;

        public static List<SkinTexEntry> LoadEntries()
        {
            if (_entriesCache != null && _entriesCacheVersion == SettingsService.Version) return _entriesCache;
            _entriesCache = ReadTexEntries(SettingsService.Get);
            _entriesCacheVersion = SettingsService.Version;
            return _entriesCache;
        }

        // keyPrefix/countKey are pluggable so a non-global owner (a pet's own skin textures) can
        // reuse this exact serialization shape under its own settings keys instead of the shared
        // "skintex.entry.*" catalog
        public static List<SkinTexEntry> ReadTexEntries(System.Func<string, string, string> get, string keyPrefix = "skintex.entry.", string countKey = KEY_ENTRY_COUNT)
        {
            string EK(int i, string f) => $"{keyPrefix}{i}.{f}";
            var entries = new List<SkinTexEntry>();
            int count = int.TryParse(get(countKey, "0"), out int ec) ? ec : 0;
            for (int i = 0; i < count; i++)
            {
                var e = new SkinTexEntry
                {
                    entryName = get(EK(i, "name"), "entry " + i),
                    enabled = get(EK(i, "enabled"), "1") == "1",
                    category = get(EK(i, "category"), SkinTexCategory.Upper),
                    costumeName = get(EK(i, "costume"), "")
                };

                // matNames come back pipe-joined so match building works without recaching the costume
                foreach (var n in get(EK(i, "matNames"), "").Split('|'))
                    if (!string.IsNullOrEmpty(n)) e.matNames.Add(n);

                int ovCount = int.TryParse(get(EK(i, "overrideCount"), "0"), out int oc) ? oc : 0;
                for (int j = 0; j < ovCount; j++)
                {
                    string texName = get(EK(i, $"override.{j}.texName"), "");
                    string texPath = get(EK(i, $"override.{j}.texPath"), "");
                    if (string.IsNullOrEmpty(texName)) continue;
                    e.overrides.Add(new SkinTexOverride { texName = texName, texPath = texPath });
                }

                int propCount = int.TryParse(get(EK(i, "propCount"), "0"), out int pc) ? pc : 0;
                for (int k = 0; k < propCount; k++)
                {
                    string matName = get(EK(i, $"prop.{k}.matName"), "");
                    string propName = get(EK(i, $"prop.{k}.name"), "");
                    string kind = get(EK(i, $"prop.{k}.kind"), "");
                    if (string.IsNullOrEmpty(matName) || string.IsNullOrEmpty(propName) || string.IsNullOrEmpty(kind)) continue;
                    var po = new MatPropOverride { matName = matName, prop = propName, kind = kind };
                    if (kind == "float")
                    {
                        float.TryParse(get(EK(i, $"prop.{k}.f"), "0"), out po.f);
                    }
                    else
                    {
                        float.TryParse(get(EK(i, $"prop.{k}.x"), "0"), out po.x);
                        float.TryParse(get(EK(i, $"prop.{k}.y"), "0"), out po.y);
                        float.TryParse(get(EK(i, $"prop.{k}.z"), "0"), out po.z);
                        float.TryParse(get(EK(i, $"prop.{k}.w"), "1"), out po.w);
                    }
                    e.matProps.Add(po);
                }

                entries.Add(e);
            }
            return entries;
        }

        public static void SaveEntries(List<SkinTexEntry> entries, string keyPrefix = "skintex.entry.", string countKey = KEY_ENTRY_COUNT)
        {
            string EK(int i, string f) => $"{keyPrefix}{i}.{f}";
            SettingsService.Set(countKey, entries.Count.ToString());
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                SettingsService.Set(EK(i, "name"), e.entryName);
                SettingsService.Set(EK(i, "enabled"), e.enabled ? "1" : "0");
                SettingsService.Set(EK(i, "category"), string.IsNullOrEmpty(e.category) ? SkinTexCategory.Upper : e.category);
                SettingsService.Set(EK(i, "costume"), e.costumeName);
                SettingsService.Set(EK(i, "matNames"), string.Join("|", e.matNames));
                SettingsService.Set(EK(i, "overrideCount"), e.overrides.Count.ToString());
                for (int j = 0; j < e.overrides.Count; j++)
                {
                    SettingsService.Set(EK(i, $"override.{j}.texName"), e.overrides[j].texName);
                    SettingsService.Set(EK(i, $"override.{j}.texPath"), e.overrides[j].texPath ?? "");
                }

                SettingsService.Set(EK(i, "propCount"), e.matProps.Count.ToString());
                for (int k = 0; k < e.matProps.Count; k++)
                {
                    var po = e.matProps[k];
                    SettingsService.Set(EK(i, $"prop.{k}.matName"), po.matName);
                    SettingsService.Set(EK(i, $"prop.{k}.name"), po.prop);
                    SettingsService.Set(EK(i, $"prop.{k}.kind"), po.kind);
                    if (po.kind == "float")
                    {
                        SettingsService.Set(EK(i, $"prop.{k}.f"), po.f.ToString());
                    }
                    else
                    {
                        SettingsService.Set(EK(i, $"prop.{k}.x"), po.x.ToString());
                        SettingsService.Set(EK(i, $"prop.{k}.y"), po.y.ToString());
                        SettingsService.Set(EK(i, $"prop.{k}.z"), po.z.ToString());
                        SettingsService.Set(EK(i, $"prop.{k}.w"), po.w.ToString());
                    }
                }
            }
        }

        // decode every enabled entry's texture up front (plugin load) so the first per-bean
        // auto-reapply is a cache hit instead of a file read + png decode on that frame
        public static void PrewarmCustomTexCache()
        {
            foreach (var entry in LoadEntries())
                if (entry.enabled)
                    foreach (var ov in entry.overrides)
                        GetCachedCustomTex(ov);
        }

        public static Texture2D GetCachedCustomTex(SkinTexOverride ov)
        {
            if (ov.texData != null && ov.texData.Length > 0)
                return DecodeCustomTex("replay:" + ov.texName, ov.texData.Length, ov.texData);
            return GetCachedCustomTex(ov.texPath);
        }

        // thumbnail for the entry row - first override that has a decodable texture
        public static Texture2D GetCachedCustomTex(SkinTexEntry entry)
        {
            foreach (var ov in entry.overrides)
            {
                var t = GetCachedCustomTex(ov);
                if (t != null) return t;
            }
            return null;
        }

        private static Texture2D GetCachedCustomTex(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            long stamp;
            try { stamp = System.IO.File.GetLastWriteTimeUtc(path).Ticks; } catch { return null; }
            if (_customTexCache.TryGetValue(path, out var hit) && hit.stamp == stamp && hit.tex != null)
                return hit.tex;
            byte[] data;
            try { data = System.IO.File.ReadAllBytes(path); }
            catch (Exception ex) { Plugin.Log.LogWarning($"read {path}: {ex.Message}"); return null; }
            return DecodeCustomTex(path, stamp, data);
        }

        private static Texture2D DecodeCustomTex(string key, long stamp, byte[] data)
        {
            if (_customTexCache.TryGetValue(key, out var hit) && hit.stamp == stamp && hit.tex != null)
                return hit.tex;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(data);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            _customTexCache[key] = (stamp, tex);
            return tex;
        }

        public static Texture GetMaterialTexture(Material mat)
        {
            if (mat == null) return null;
            foreach (var prop in GetTextureProps(mat))
            {
                var t = mat.GetTexture(prop);
                if (t != null) return t;
            }
            return null;
        }

        // finds the texture on this material whose own name matches (a material can carry
        // several named textures now - albedo, metallic map, normal map, etc)
        private static Texture FindNamedTexture(Material mat, string name)
        {
            if (mat == null || string.IsNullOrEmpty(name)) return null;
            foreach (var prop in GetTextureProps(mat))
            {
                var t = mat.GetTexture(prop);
                if (t != null && t.name == name) return t;
            }
            return null;
        }

        public static Texture ResolveSourceTexture(List<Material> mats, int idx, string matName)
        {
            if (mats != null && idx >= 0 && idx < mats.Count)
            {
                var t = FindNamedTexture(mats[idx], matName) ?? GetMaterialTexture(mats[idx]);
                if (t != null) return t;
            }

            if (Instance == null || string.IsNullOrEmpty(matName)) return null;
            var bean = BeanMonitorService.LocalPlayerBean;
            if (bean == null) return null;

            if (Instance.customTexOriginals.TryGetValue(bean.GetInstanceID(), out var originals))
                foreach (var o in originals)
                    if (o.textureName == matName) return o.texture;

            var geo = FindBeanGEO(bean);
            if (geo == null) return null;
            foreach (var r in geo.GetComponentsInChildren<Renderer>(true))
            {
                var shared = r.sharedMaterials;
                if (shared == null) continue;
                foreach (var m in shared)
                {
                    var t = FindNamedTexture(m, matName);
                    if (t != null) return t;
                    if (CleanMatName(m.name) == matName)
                    {
                        t = GetMaterialTexture(m);
                        if (t != null) return t;
                    }
                }
            }
            return null;
        }

        public static bool SaveTexturePng(Texture src, string path, out string error)
        {
            error = null;
            if (src == null) { error = "no source texture"; return false; }

            int w = Mathf.Max(1, src.width);
            int h = Mathf.Max(1, src.height);
            var prev = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D readable = null;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0f, 0f, w, h), 0, 0);
                readable.Apply();
                System.IO.File.WriteAllBytes(path, readable.EncodeToPNG());
                Plugin.Log.LogInfo($"dumped {src.name} ({w}x{h}) to {path}");
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (readable != null) UnityEngine.Object.Destroy(readable);
            }
        }

        public static List<GameObject> GatherBeans()
        {
            var beans = new List<GameObject>();
            if (BeanMonitorService.LocalPlayerBean != null)
                beans.Add(BeanMonitorService.LocalPlayerBean);
            foreach (var b in BeanMonitorService.GetTrackedBeans())
                if (b != null && !beans.Contains(b)) beans.Add(b);
            return beans;
        }

        public static int ApplyEntryToBean(SkinTexEntry entry, GameObject bean)
        {
            if (Instance == null || bean == null) return 0;
            if (SkinTexCategory.IsOptionField(entry.category)) return 0;
            int total = 0;
            foreach (var ov in entry.overrides)
            {
                if (string.IsNullOrEmpty(ov.texName)) continue;
                var tex = GetCachedCustomTex(ov);
                if (tex == null) continue;
                total += Instance.ApplyCustomTexture(bean, 0, tex, new HashSet<string> { ov.texName });
            }
            total += Instance.ApplyMatProps(bean, entry.matProps);
            return total;
        }

        public static void ApplyEntriesToBean(List<SkinTexEntry> entries, GameObject bean)
        {
            foreach (var entry in entries)
                if (entry.enabled) ApplyEntryToBean(entry, bean);
        }

        public static int ApplyEntry(SkinTexEntry entry)
        {
            int total = 0;
            foreach (var bean in GatherBeans())
                total += ApplyEntryToBean(entry, bean);
            return total;
        }

        public static void RevertAllBeans()
        {
            if (Instance == null) return;
            foreach (var bean in GatherBeans())
                Instance.RevertCustomTexture(bean);
            RevertAllOptionOverrides();
        }

        public static void ReapplyAllEnabledFromSettings()
            => ReapplyAllEnabled(LoadEntries(), null);

        // wipe everything first, then put back whatever is still switched on
        public static void ReapplyAllEnabled(List<SkinTexEntry> entries, Action<string> status)
        {
            if (Instance == null) return;
            RevertAllBeans();

            foreach (var entry in entries)
            {
                if (!entry.enabled) continue;
                if (SkinTexCategory.IsOptionField(entry.category))
                {
                    bool ok = ApplyOptionOverrideEntry(entry);
                    status?.Invoke(ok ? $"applied {entry.entryName}" : $"{entry.entryName}: option not loaded");
                }
                else
                {
                    int n = ApplyEntry(entry);
                    status?.Invoke(n > 0 ? $"applied {entry.entryName}" : "nothing matched");
                }
            }
            RepushAffectedOptionsToBeans();
        }

        private struct OptionOriginal { public string prop; public string kind; public Color c; public float f; }
        private static readonly Dictionary<UnityEngine.Object, List<OptionOriginal>> _optionOriginals = new Dictionary<UnityEngine.Object, List<OptionOriginal>>();

        private static bool ApplyOptionOverrideEntry(SkinTexEntry entry)
        {
            var opt = FindOptionByName(entry.category, entry.costumeName);
            if (opt == null) return false;
            if (!_optionOriginals.TryGetValue(opt, out var originals))
            {
                originals = new List<OptionOriginal>();
                _optionOriginals[opt] = originals;
            }
            foreach (var po in entry.matProps)
            {
                if (po == null || string.IsNullOrEmpty(po.prop)) continue;
                if (!TryReadOptionField(opt, po.prop, out string kind, out Color c, out float f)) continue;
                bool already = false;
                foreach (var o in originals) if (o.prop == po.prop) { already = true; break; }
                if (!already) originals.Add(new OptionOriginal { prop = po.prop, kind = kind, c = c, f = f });
                if (po.kind == "color") TryWriteOptionField(opt, po.prop, new Color(po.x, po.y, po.z, po.w), 0f);
                else if (po.kind == "float") TryWriteOptionField(opt, po.prop, Color.clear, po.f);
            }
            return true;
        }

        public static void PreviewOptionOverride(SkinTexEntry entry)
        {
            if (Instance == null || entry == null) return;
            RevertAllOptionOverrides();
            ApplyOptionOverrideEntry(entry);
            RepushAffectedOptionsToBeans();
        }

        public static void RevertAllOptionOverrides()
        {
            var affected = new List<UnityEngine.Object>();
            foreach (var kv in _optionOriginals)
            {
                var opt = kv.Key;
                if (opt == null) continue;
                affected.Add(opt);
                foreach (var o in kv.Value)
                {
                    if (o.kind == "color") TryWriteOptionField(opt, o.prop, o.c, 0f);
                    else TryWriteOptionField(opt, o.prop, Color.clear, o.f);
                }
            }
            _optionOriginals.Clear();
            RepushOptionsToBeans(affected);
        }

        private static void RepushAffectedOptionsToBeans()
        {
            if (_optionOriginals.Count == 0) return;
            var list = new List<UnityEngine.Object>();
            foreach (var kv in _optionOriginals) list.Add(kv.Key);
            RepushOptionsToBeans(list);
        }

        private static void RepushOptionsToBeans(List<UnityEngine.Object> options)
        {
            if (options == null || options.Count == 0) return;
            CustomisationSelections localSel = null;
            try
            {
                var mm = GameObject.Find("MainMenuManager")?.GetComponent<MainMenuManager>();
                localSel = mm?._playerProfile?.CustomisationSelections;
            }
            catch { }

            var localBean = BeanMonitorService.LocalPlayerBean;
            foreach (var opt in options)
            {
                if (opt == null) continue;
                ColourOption co = null; FaceplateOption fp = null; SkinPatternOption sp = null;
                try { co = opt.TryCast<ColourOption>(); } catch { }
                try { fp = opt.TryCast<FaceplateOption>(); } catch { }
                try { sp = opt.TryCast<SkinPatternOption>(); } catch { }

                bool localMatches = false;
                if (localSel != null)
                {
                    try
                    {
                        if (co != null && localSel.ColourOption == co) localMatches = true;
                        else if (fp != null && localSel.FaceplateOption == fp) localMatches = true;
                        else if (sp != null && localSel.PatternOption == sp) localMatches = true;
                    }
                    catch { }
                }
                if (Instance != null)
                {
                    if (co != null && Instance.activeColour.On && Instance.activeColour.option == co) localMatches = true;
                    else if (fp != null && Instance.activeFaceplate.On && Instance.activeFaceplate.option == fp) localMatches = true;
                    else if (sp != null && Instance.activePattern.On && Instance.activePattern.option == sp) localMatches = true;
                }
                if (!localMatches) continue;

                if (localBean != null) PushOne(localBean, co, fp, sp);
                foreach (var bean in BeanMonitorService.GetTrackedBeans())
                    if (bean != null && bean != localBean) PushOne(bean, co, fp, sp);
            }
        }

        private static void PushOne(GameObject bean, ColourOption co, FaceplateOption fp, SkinPatternOption sp)
        {
            var fgch = bean.GetComponent<FallguyCustomisationHandler>();
            if (fgch == null) return;
            try
            {
                if (co != null) fgch.UpdateColourOption(co);
                else if (fp != null) fgch.UpdateFaceplateColours(fp);
                else if (sp != null) { try { sp.LoadBlocking(); } catch { } fgch.UpdatePatternTexture(sp); }
            }
            catch { }
        }

        public static UnityEngine.Object FindOptionByName(string category, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Il2CppSystem.Type t;
            if (category == SkinTexCategory.Colour) t = Il2CppInterop.Runtime.Il2CppType.Of<ColourOption>();
            else if (category == SkinTexCategory.Faceplate) t = Il2CppInterop.Runtime.Il2CppType.Of<FaceplateOption>();
            else if (category == SkinTexCategory.Pattern) t = Il2CppInterop.Runtime.Il2CppType.Of<SkinPatternOption>();
            else if (category == SkinTexCategory.Upper || category == SkinTexCategory.Lower) t = Il2CppInterop.Runtime.Il2CppType.Of<CostumeOption>();
            else return null;

            var raw = Resources.FindObjectsOfTypeAll(t);
            for (int i = 0; raw != null && i < raw.Length; i++)
            {
                var o = raw[i];
                if (o == null) continue;
                if (o.name == name) return o;
            }
            return null;
        }

        public static bool TryReadOptionField(UnityEngine.Object opt, string prop, out string kind, out Color c, out float f)
        {
            kind = ""; c = default; f = 0f;
            if (opt == null || string.IsNullOrEmpty(prop)) return false;
            try
            {
                ColourOption co = null; try { co = opt.TryCast<ColourOption>(); } catch { }
                if (co != null)
                {
                    switch (prop)
                    {
                        case "primaryColour": kind = "color"; c = co.primaryColour; return true;
                        case "secondaryColour": kind = "color"; c = co.secondaryColour; return true;
                        case "rimColor": kind = "color"; c = co.rimColor; return true;
                        case "primarySmoothness": kind = "float"; f = co.primarySmoothness; return true;
                        case "secondarySmoothness": kind = "float"; f = co.secondarySmoothness; return true;
                        case "primaryMetallic": kind = "float"; f = co.primaryMetallic; return true;
                        case "secondaryMetallic": kind = "float"; f = co.secondaryMetallic; return true;
                        case "rimPower": kind = "float"; f = co.rimPower; return true;
                    }
                    return false;
                }
                FaceplateOption fp = null; try { fp = opt.TryCast<FaceplateOption>(); } catch { }
                if (fp != null)
                {
                    switch (prop)
                    {
                        case "eyesColour": kind = "color"; c = fp.eyesColour; return true;
                        case "faceColour": kind = "color"; c = fp.faceColour; return true;
                        case "eyesSmoothness": kind = "float"; f = fp.eyesSmoothness; return true;
                        case "eyesMetallic": kind = "float"; f = fp.eyesMetallic; return true;
                        case "faceSmoothness": kind = "float"; f = fp.faceSmoothness; return true;
                        case "faceMetallic": kind = "float"; f = fp.faceMetallic; return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static void TryWriteOptionField(UnityEngine.Object opt, string prop, Color c, float f)
        {
            if (opt == null || string.IsNullOrEmpty(prop)) return;
            try
            {
                ColourOption co = null; try { co = opt.TryCast<ColourOption>(); } catch { }
                if (co != null)
                {
                    switch (prop)
                    {
                        case "primaryColour": co.primaryColour = c; return;
                        case "secondaryColour": co.secondaryColour = c; return;
                        case "rimColor": co.rimColor = c; return;
                        case "primarySmoothness": co.primarySmoothness = f; return;
                        case "secondarySmoothness": co.secondarySmoothness = f; return;
                        case "primaryMetallic": co.primaryMetallic = f; return;
                        case "secondaryMetallic": co.secondaryMetallic = f; return;
                        case "rimPower": co.rimPower = f; return;
                    }
                    return;
                }
                FaceplateOption fp = null; try { fp = opt.TryCast<FaceplateOption>(); } catch { }
                if (fp != null)
                {
                    switch (prop)
                    {
                        case "eyesColour": fp.eyesColour = c; return;
                        case "faceColour": fp.faceColour = c; return;
                        case "eyesSmoothness": fp.eyesSmoothness = f; return;
                        case "eyesMetallic": fp.eyesMetallic = f; return;
                        case "faceSmoothness": fp.faceSmoothness = f; return;
                        case "faceMetallic": fp.faceMetallic = f; return;
                    }
                }
            }
            catch { }
        }

        private static readonly Dictionary<string, Texture2D> _iconTexCache =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, ItemDefinitionSO> _optionByName;
        private static float _optionByNameStamp;
        private const float OPTION_NAME_TTL = 3f;

        private static Dictionary<string, ItemDefinitionSO> GetOptionByName()
        {
            if (_optionByName != null && Time.realtimeSinceStartup - _optionByNameStamp < OPTION_NAME_TTL)
                return _optionByName;

            var map = new Dictionary<string, ItemDefinitionSO>(StringComparer.OrdinalIgnoreCase);
            void Scan(Il2CppSystem.Type t)
            {
                var raw = Resources.FindObjectsOfTypeAll(t);
                if (raw == null) return;
                for (int i = 0; i < raw.Length; i++)
                {
                    var o = raw[i];
                    if (o == null || string.IsNullOrEmpty(o.name)) continue;
                    ItemDefinitionSO opt;
                    try { opt = o.Cast<ItemDefinitionSO>(); } catch { continue; }
                    if (opt != null && !map.ContainsKey(o.name)) map[o.name] = opt;
                }
            }
            Scan(Il2CppInterop.Runtime.Il2CppType.Of<CostumeOption>());
            Scan(Il2CppInterop.Runtime.Il2CppType.Of<SkinPatternOption>());
            Scan(Il2CppInterop.Runtime.Il2CppType.Of<ColourOption>());
            Scan(Il2CppInterop.Runtime.Il2CppType.Of<FaceplateOption>());

            _optionByName = map;
            _optionByNameStamp = Time.realtimeSinceStartup;
            return map;
        }

        public static string GetOptionDisplayName(UnityEngine.Object option)
        {
            if (option == null) return "";
            try { return GetGameName(option.Cast<ItemDefinitionSO>(), ""); } catch { }
            try { return option.name ?? ""; } catch { }
            return "";
        }

        // caches an owned CLONE of each option's icon, not the game's live sprite - the game can
        // destroy/reload its atlas assets underneath us (asset unload, menu re-entry) and a held
        // reference to the original just goes stale/blank. our clone's pixel data is ours forever.
        private static readonly Dictionary<string, Sprite> _iconSpriteCache =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        public static Sprite ResolveOptionIconSprite(UnityEngine.Object option)
        {
            if (option == null) return null;
            string key = option.name;
            if (!string.IsNullOrEmpty(key) && _iconSpriteCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            ItemDefinitionSO def = null;
            try { def = option.Cast<ItemDefinitionSO>(); } catch { }
            if (def == null) return null;

            Sprite src = null;
            try { src = def.MenuDisplaySprite; } catch { }
            if (src == null)
            {
                try { src = def._spriteAtlasLoadableAsset.AssetRef.LoadAsset<Sprite>().Result; } catch { }
            }
            if (src == null || src.texture == null) return null;

            var crop = CropAtlasToTexture2D(src, string.IsNullOrEmpty(key) ? src.name : key);
            if (crop == null) return null;

            var owned = Sprite.Create(crop, new Rect(0f, 0f, crop.width, crop.height),
                new Vector2(src.pivot.x / Mathf.Max(1f, src.rect.width), src.pivot.y / Mathf.Max(1f, src.rect.height)));
            owned.name = "bfg_icon_" + crop.name;
            owned.hideFlags = HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;
            UnityEngine.Object.DontDestroyOnLoad(crop);

            if (!string.IsNullOrEmpty(key)) _iconSpriteCache[key] = owned;
            return owned;
        }

        // crops a sprite's rect out of its atlas texture into a standalone Texture2D we own outright
        public static Texture2D CropAtlasToTexture2D(Sprite spr, string name)
        {
            var atlas = spr.texture;
            if (atlas == null) return null;

            var tr = spr.textureRect;
            int rx = Mathf.Clamp(Mathf.FloorToInt(tr.x), 0, atlas.width);
            int ry = Mathf.Clamp(Mathf.FloorToInt(tr.y), 0, atlas.height);
            int rw = Mathf.Clamp(Mathf.CeilToInt(tr.width), 1, atlas.width - rx);
            int rh = Mathf.Clamp(Mathf.CeilToInt(tr.height), 1, atlas.height - ry);

            var rt = RenderTexture.GetTemporary(atlas.width, atlas.height, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Graphics.Blit(atlas, rt);
            RenderTexture.active = rt;
            var crop = new Texture2D(rw, rh, TextureFormat.RGBA32, false);
            crop.ReadPixels(new Rect(rx, ry, rw, rh), 0, 0);
            crop.Apply();
            crop.hideFlags = HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;
            crop.name = "bfg_iconsrc_" + name;
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return crop;
        }

        // display name for an option's stored internal name (CostumeOption/SkinPatternOption/etc
        // asset name) - never show that raw name in UI, always resolve it through here first
        public static string ResolveOptionDisplayName(string optionName)
        {
            if (string.IsNullOrEmpty(optionName)) return "";
            return GetOptionByName().TryGetValue(optionName, out var opt) && opt != null
                ? GetOptionDisplayName(opt)
                : optionName;
        }

        public static Texture2D ResolveOptionIconTexture(string category, string optionName)
        {
            if (string.IsNullOrEmpty(optionName)) return null;
            if (_iconTexCache.TryGetValue(optionName, out var owned) && owned != null) return owned;

            if (!GetOptionByName().TryGetValue(optionName, out var opt) || opt == null) return null;

            Sprite spr = null;
            try { spr = opt.MenuDisplaySprite; } catch { }
            if (spr == null)
            {
                try { spr = opt._spriteAtlasLoadableAsset.AssetRef.LoadAsset<Sprite>().Result; } catch { }
            }
            if (spr == null || spr.texture == null) return null;

            var crop = CropAtlasToTexture2D(spr, optionName);
            if (crop == null) return null;

            UnityEngine.Object.DontDestroyOnLoad(crop);
            _iconTexCache[optionName] = crop;
            return crop;
        }

        public static IEnumerable<(string prop, string kind)> GetOptionFields(string category)
        {
            if (category == SkinTexCategory.Colour)
            {
                yield return ("primaryColour", "color");
                yield return ("secondaryColour", "color");
                yield return ("rimColor", "color");
                yield return ("rimPower", "float");
                yield return ("primarySmoothness", "float");
                yield return ("secondarySmoothness", "float");
                yield return ("primaryMetallic", "float");
                yield return ("secondaryMetallic", "float");
            }
            else if (category == SkinTexCategory.Faceplate)
            {
                yield return ("eyesColour", "color");
                yield return ("faceColour", "color");
                yield return ("eyesSmoothness", "float");
                yield return ("eyesMetallic", "float");
                yield return ("faceSmoothness", "float");
                yield return ("faceMetallic", "float");
            }
        }

        public int TryAutoReapplyCustomTextureForBean(GameObject bean)
        {
            if (bean == null) return 0;
            int beanId = bean.GetInstanceID();
            if (customTexOriginals.ContainsKey(beanId)) return 0;

            var geo = FindBeanGEO(bean);
            if (geo == null) return 0;

            int total = 0;
            foreach (var r in geo.GetComponentsInChildren<Renderer>(true))
                if (ApplyEnabledEntriesToRenderer(bean, r)) total++;
            return total;
        }

        public bool ApplyEnabledEntriesToRenderer(GameObject bean, Renderer r)
        {
            if (bean == null || r == null) return false;
            var entries = LoadEntries();
            if (entries.Count == 0) return false;

            int beanId = bean.GetInstanceID();
            bool hadTex = customTexOriginals.TryGetValue(beanId, out var texOriginals);
            if (texOriginals == null) texOriginals = new List<CustomTexOriginal>();
            bool hadProps = customPropOriginals.TryGetValue(beanId, out var propOriginals);
            if (propOriginals == null) propOriginals = new List<CustomPropOriginal>();

            bool touchedTex = false, touchedProps = false;
            foreach (var entry in entries)
            {
                if (!entry.enabled || SkinTexCategory.IsOptionField(entry.category)) continue;
                foreach (var ov in entry.overrides)
                {
                    if (string.IsNullOrEmpty(ov.texName)) continue;
                    var tex = GetCachedCustomTex(ov);
                    if (tex == null) continue;
                    int texSlot = 0;
                    if (ApplyTextureToRenderer(r, 0, tex, new HashSet<string> { ov.texName }, texOriginals, ref texSlot))
                        touchedTex = true;
                }
                if (ApplyMatPropsToRenderer(r, entry.matProps, propOriginals)) touchedProps = true;
            }

            if (touchedTex && !hadTex) customTexOriginals[beanId] = texOriginals;
            if (touchedProps && !hadProps) customPropOriginals[beanId] = propOriginals;
            return touchedTex || touchedProps;
        }


        // scans bean GEO so normal costumes, custom skins, and additive cosmetics all count
        public int ApplyCustomTexture(GameObject bean, int matSlotIdx, Texture2D tex, HashSet<string> matchTexNames)
        {
            if (bean == null || tex == null) return 0;

            var geo = FindBeanGEO(bean);
            if (geo == null) return 0;

            int texSlot = 0;
            return ApplyTextureToGameObject(geo.gameObject, matSlotIdx, tex, matchTexNames, bean.GetInstanceID(), ref texSlot);
        }

        private int ApplyTextureToGameObject(GameObject costumeObj, int matSlotIdx, Texture2D tex, HashSet<string> matchTexNames, int beanId, ref int texSlot)
        {
            int count = 0;
            var renderers = costumeObj.GetComponentsInChildren<Renderer>(true);
            bool alreadyHadOriginals = customTexOriginals.TryGetValue(beanId, out var originalList);
            if (originalList == null)
                originalList = new List<CustomTexOriginal>();

            foreach (var r in renderers)
                if (ApplyTextureToRenderer(r, matSlotIdx, tex, matchTexNames, originalList, ref texSlot)) count++;

            if (count > 0 && !alreadyHadOriginals)
                customTexOriginals[beanId] = originalList;
            return count;
        }

        private bool ApplyTextureToRenderer(Renderer r, int matSlotIdx, Texture2D tex, HashSet<string> matchTexNames, List<CustomTexOriginal> originalList, ref int texSlot)
        {
            if (r == null) return false;
            var mats = r.materials;
            if (mats == null) return false;
            bool touched = false;

            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;

                bool hadTextureSlot = false;

                foreach (var prop in GetTextureProps(m))
                {
                    try
                    {
                        var originalTex = FindCustomTexOriginal(originalList, r, i, prop, out string savedName);
                        if (originalTex == null)
                            originalTex = m.GetTexture(prop);
                        if (originalTex == null) continue;

                        hadTextureSlot = true;
                        string originalTexName = savedName ?? originalTex.name ?? "";
                        string matName = CleanMatName(m.name);
                        bool hasNameFilter = matchTexNames != null && matchTexNames.Count > 0;
                        bool nameMatch = hasNameFilter && (matchTexNames.Contains(originalTexName) || matchTexNames.Contains(matName));
                        if (hasNameFilter ? !nameMatch : texSlot != matSlotIdx) continue;

                        RememberCustomTexOriginal(originalList, r, i, prop, originalTex, originalTexName);

                        if (!string.IsNullOrEmpty(originalTexName))
                            tex.name = originalTexName;
                        m.SetTexture(prop, tex);
                        touched = true;
                        break;
                    }
                    catch { }
                }

                if (hadTextureSlot) texSlot++;
            }

            if (touched) r.materials = mats;
            return touched;
        }

        // scans bean GEO for materials whose (cleaned) name matches, sets the color/float
        public int ApplyMatProps(GameObject bean, List<MatPropOverride> props)
        {
            if (bean == null || props == null || props.Count == 0) return 0;
            var geo = FindBeanGEO(bean);
            if (geo == null) return 0;
            return ApplyMatPropsToGameObject(geo.gameObject, props, bean.GetInstanceID());
        }

        private int ApplyMatPropsToGameObject(GameObject costumeObj, List<MatPropOverride> props, int beanId)
        {
            int count = 0;
            var renderers = costumeObj.GetComponentsInChildren<Renderer>(true);
            bool alreadyHadOriginals = customPropOriginals.TryGetValue(beanId, out var originalList);
            if (originalList == null)
                originalList = new List<CustomPropOriginal>();

            foreach (var r in renderers)
                if (ApplyMatPropsToRenderer(r, props, originalList)) count++;

            if (count > 0 && !alreadyHadOriginals)
                customPropOriginals[beanId] = originalList;
            return count;
        }

        private bool ApplyMatPropsToRenderer(Renderer r, List<MatPropOverride> props, List<CustomPropOriginal> originalList)
        {
            if (r == null || props == null || props.Count == 0) return false;
            var mats = r.materials;
            if (mats == null) return false;
            bool touched = false;

            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                string matName = CleanMatName(m.name);

                foreach (var po in props)
                {
                    if (po.matName != matName || !m.HasProperty(po.prop)) continue;
                    try
                    {
                        RememberCustomPropOriginal(originalList, r, i, po.prop, po.kind, m);
                        if (po.kind == "color") m.SetColor(po.prop, new Color(po.x, po.y, po.z, po.w));
                        else if (po.kind == "vector") m.SetVector(po.prop, new Vector4(po.x, po.y, po.z, po.w));
                        else m.SetFloat(po.prop, po.f);
                        touched = true;
                    }
                    catch { }
                }
            }

            if (touched) r.materials = mats;
            return touched;
        }

        private static void RememberCustomPropOriginal(List<CustomPropOriginal> originals, Renderer renderer, int matIdx, string prop, string kind, Material m)
        {
            foreach (var o in originals)
                if (o.renderer == renderer && o.matIdx == matIdx && o.prop == prop) return;

            originals.Add(new CustomPropOriginal
            {
                renderer = renderer,
                matIdx = matIdx,
                prop = prop,
                kind = kind,
                f = kind == "float" ? m.GetFloat(prop) : 0f,
                v = kind == "color" ? (Vector4)m.GetColor(prop) : kind == "vector" ? m.GetVector(prop) : default
            });
        }

        public void RevertMatProps(GameObject bean)
        {
            if (bean == null) return;
            int beanId = bean.GetInstanceID();
            if (!customPropOriginals.TryGetValue(beanId, out var originals)) return;

            foreach (var o in originals)
            {
                var r = o.renderer;
                if (r == null) continue;
                try
                {
                    var mats = r.materials;
                    if (mats == null || o.matIdx < 0 || o.matIdx >= mats.Length) continue;
                    var m = mats[o.matIdx];
                    if (m == null || !m.HasProperty(o.prop)) continue;
                    if (o.kind == "color") m.SetColor(o.prop, (Color)o.v);
                    else if (o.kind == "vector") m.SetVector(o.prop, o.v);
                    else m.SetFloat(o.prop, o.f);
                    r.materials = mats;
                }
                catch { }
            }

            customPropOriginals.Remove(beanId);
        }

        public static string CleanMatName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.EndsWith(" (Instance)") ? name.Substring(0, name.Length - 11) : name;
        }

        private static Texture FindCustomTexOriginal(List<CustomTexOriginal> originals, Renderer renderer, int matIdx, string prop, out string textureName)
        {
            textureName = null;
            if (originals == null) return null;

            foreach (var o in originals)
                if (o.renderer == renderer && o.matIdx == matIdx && o.prop == prop)
                {
                    textureName = o.textureName;
                    return o.texture;
                }

            return null;
        }

        private static void RememberCustomTexOriginal(List<CustomTexOriginal> originals, Renderer renderer, int matIdx, string prop, Texture texture, string textureName)
        {
            if (originals == null || renderer == null || texture == null) return;

            foreach (var o in originals)
                if (o.renderer == renderer && o.matIdx == matIdx && o.prop == prop) return;

            originals.Add(new CustomTexOriginal
            {
                renderer = renderer,
                matIdx = matIdx,
                prop = prop,
                texture = texture,
                textureName = textureName
            });
        }

        public void RevertCustomTexture(GameObject bean)
        {
            if (bean == null) return;
            RevertMatProps(bean);

            int beanId = bean.GetInstanceID();
            if (!customTexOriginals.TryGetValue(beanId, out var originals)) return;

            foreach (var o in originals)
            {
                var r = o.renderer;
                if (r == null) continue;
                try
                {
                    var mats = r.materials;
                    if (mats == null || o.matIdx < 0 || o.matIdx >= mats.Length) continue;
                    var m = mats[o.matIdx];
                    if (m == null || !m.HasProperty(o.prop)) continue;
                    m.SetTexture(o.prop, o.texture);
                    r.materials = mats;
                }
                catch { }
            }

            customTexOriginals.Remove(beanId);
        }
    }
}
