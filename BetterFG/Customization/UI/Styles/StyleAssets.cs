using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using FallGuysLib.Utils;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using BetterFG.Utilities;

namespace BetterFG.Customization.UI.Styles
{
    public sealed class StyleAssets
    {
        const string TMP_BASE = "TitanOne-Expanded SDF Material - DropShadowAlpha_2_0";
        static Material _tmpBase;

        readonly UIStyle _style;
        AssetBundle _bundle;
        JsonElement _matDefs;
        bool _matDefsLoaded;
        readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();
        readonly Dictionary<string, Texture> _textures = new Dictionary<string, Texture>();
        readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();
        readonly Dictionary<string, TMP_FontAsset> _fonts = new Dictionary<string, TMP_FontAsset>();

        public StyleAssets(UIStyle style)
        {
            _style = style;
        }

        bool _bundleTried;

        AssetBundle Bundle
        {
            get
            {
                if (_bundleTried) return _bundle;
                _bundleTried = true;
                var path = OnlineData.Dir("UIStyles", _style.OnlineDir, "bundle");
                if (!File.Exists(path)) { Plugin.Log.LogWarning($"{_style.Id}: no bundle at {path}, style will be missing its art"); return null; }
                _bundle = AssetBundle.LoadFromFile(path);
                Plugin.Log.LogInfo($"{_style.Id} bundle up, {_bundle?.GetAllAssetNames().Length ?? 0} assets");
                return _bundle;
            }
        }

        public string Text(string name)
        {
            var path = OnlineData.Dir("UIStyles", _style.OnlineDir, name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        public JsonElement Layout(string name) => JsonUtil.Parse(Text("layouts/" + name + ".json"));

        T Load<T>(string name) where T : Object
        {
            var o = Bundle?.LoadAsset(name, Il2CppType.Of<T>());
            if (o == null) return null;
            o.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return o.Cast<T>();
        }

        readonly HashSet<string> _missing = new HashSet<string>();

        public Sprite Sprite(string name)
        {
            if (_sprites.TryGetValue(name, out var s) && (s != null || _missing.Contains(name))) return s;
            s = Load<Sprite>(name);
            if (s == null)
            {
                _missing.Add(name);
                Plugin.Log.LogWarning($"{_style.Id} has no sprite {name}");
            }
            else s.texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return _sprites[name] = s;
        }

        public Texture Texture(string name)
        {
            if (_textures.TryGetValue(name, out var t) && t != null) return t;
            return _textures[name] = Load<Texture2D>(name);
        }

        static string FontFile(string name) => name.Replace(' ', '_').Replace('(', '_').Replace(')', '_');

        public TMP_FontAsset Font(string name)
        {
            if (_fonts.TryGetValue(name, out var fa)) return fa;
            var ta = Load<TextAsset>(FontFile(name));
            if (ta == null) return _fonts[name] = null;
            var j = JsonUtil.Parse(ta.text);
            var atlas = Load<Texture2D>(FontFile(name) + "_0");
            fa = ScriptableObject.CreateInstance<TMP_FontAsset>();
            fa.name = name;
            fa.version = "1.1.0";
            if (fa.fontFeatureTable == null) fa.fontFeatureTable = new TMP_FontFeatureTable();
            if (fa.fallbackFontAssetTable == null) fa.fallbackFontAssetTable = new Il2CppSystem.Collections.Generic.List<TMP_FontAsset>();
            if (fa.glyphTable == null) fa.glyphTable = new Il2CppSystem.Collections.Generic.List<Glyph>();
            if (fa.characterTable == null) fa.characterTable = new Il2CppSystem.Collections.Generic.List<TMP_Character>();
            fa.faceInfo = new FaceInfo
            {
                familyName = j.GetProperty("family").GetString(),
                styleName = j.GetProperty("style").GetString(),
                pointSize = j.GetProperty("pointSize").GetInt32(),
                scale = j.GetProperty("scale").GetSingle(),
                lineHeight = j.GetProperty("lineHeight").GetSingle(),
                ascentLine = j.GetProperty("ascentLine").GetSingle(),
                capLine = j.GetProperty("capLine").GetSingle(),
                meanLine = j.GetProperty("meanLine").GetSingle(),
                baseline = j.GetProperty("baseline").GetSingle(),
                descentLine = j.GetProperty("descentLine").GetSingle(),
                superscriptOffset = j.GetProperty("superscriptOffset").GetSingle(),
                superscriptSize = j.GetProperty("superscriptSize").GetSingle(),
                subscriptOffset = j.GetProperty("subscriptOffset").GetSingle(),
                subscriptSize = j.GetProperty("subscriptSize").GetSingle(),
                underlineOffset = j.GetProperty("underlineOffset").GetSingle(),
                underlineThickness = j.GetProperty("underlineThickness").GetSingle(),
                strikethroughOffset = j.GetProperty("strikethroughOffset").GetSingle(),
                strikethroughThickness = j.GetProperty("strikethroughThickness").GetSingle(),
                tabWidth = j.GetProperty("tabWidth").GetSingle(),
            };
            fa.atlasPopulationMode = AtlasPopulationMode.Static;
            fa.atlasWidth = j.GetProperty("atlasWidth").GetInt32();
            fa.atlasHeight = j.GetProperty("atlasHeight").GetInt32();
            fa.atlasPadding = j.GetProperty("atlasPadding").GetInt32();
            fa.atlasRenderMode = (UnityEngine.TextCore.LowLevel.GlyphRenderMode)j.GetProperty("renderMode").GetInt32();
            fa.atlasTextures = new Il2CppReferenceArray<Texture2D>(new[] { atlas });
            fa.normalStyle = j.GetProperty("weightNormal").GetSingle();
            fa.boldStyle = j.GetProperty("weightBold").GetSingle();
            fa.boldSpacing = j.GetProperty("boldSpacing").GetSingle();
            fa.italicStyle = (byte)j.GetProperty("italicStyle").GetInt32();
            fa.tabSize = (byte)j.GetProperty("tabSize").GetInt32();
            var glyphs = new Dictionary<uint, Glyph>();
            foreach (var g in j.GetProperty("glyphs").EnumerateArray())
            {
                var gl = new Glyph(g[0].GetUInt32(),
                    new GlyphMetrics(g[1].GetSingle(), g[2].GetSingle(), g[3].GetSingle(), g[4].GetSingle(), g[5].GetSingle()),
                    new GlyphRect(g[6].GetInt32(), g[7].GetInt32(), g[8].GetInt32(), g[9].GetInt32()),
                    g[10].GetSingle(), g[11].GetInt32());
                fa.glyphTable.Add(gl);
                glyphs[gl.index] = gl;
            }
            foreach (var c in j.GetProperty("chars").EnumerateArray())
            {
                if (!glyphs.TryGetValue(c[1].GetUInt32(), out var gl)) continue;
                var ch = new TMP_Character(c[0].GetUInt32(), gl) { scale = c[2].GetSingle() };
                fa.characterTable.Add(ch);
            }
            var mat = new Material(TmpBase) { name = name + " Material" };
            mat.DisableKeyword("UNDERLAY_ON");
            mat.SetTexture("_MainTex", atlas);
            mat.SetFloat("_TextureWidth", fa.atlasWidth);
            mat.SetFloat("_TextureHeight", fa.atlasHeight);
            mat.SetFloat("_GradientScale", fa.atlasPadding + 1);
            mat.SetFloat("_WeightNormal", fa.normalStyle);
            mat.SetFloat("_WeightBold", fa.boldStyle);
            fa.material = mat;
            fa.ReadFontAssetDefinition();
            fa.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Plugin.Log.LogInfo($"{_style.Id}: {name} rebuilt, {fa.glyphTable.Count} glyphs");
            return _fonts[name] = fa;
        }

        static Material TmpBase
        {
            get
            {
                if (_tmpBase != null) return _tmpBase;
                foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                    if (m.name == TMP_BASE) { _tmpBase = m; break; }
                return _tmpBase;
            }
        }

        Dictionary<string, Shader> _shaders;

        Shader UiShader(string name)
        {
            if (_shaders == null)
            {
                _shaders = new Dictionary<string, Shader>();
                if (Bundle != null)
                foreach (var o in Bundle.LoadAllAssets(Il2CppType.Of<Shader>()))
                {
                    var sh = o.Cast<Shader>();
                    _shaders[sh.name] = sh;
                }
            }
            if (_shaders.TryGetValue(name, out var own)) return own;
            var f = Shader.Find(name);
            if (f != null) return f;
            foreach (var sh in Resources.FindObjectsOfTypeAll<Shader>()) if (sh.name == name) return sh;
            return null;
        }

        public Material MaterialNamed(string name)
        {
            LoadMatDefs();
            foreach (var p in _matDefs.EnumerateObject())
                if (p.Name.StartsWith(name + "|", System.StringComparison.Ordinal)) return Material(p.Name);
            return null;
        }

        void LoadMatDefs()
        {
            if (_matDefsLoaded) return;
            _matDefs = JsonUtil.Parse(Text("materials.json"));
            _matDefsLoaded = true;
        }

        public Material Material(string key)
        {
            if (_mats.TryGetValue(key, out var m)) return m;
            LoadMatDefs();
            if (!_matDefs.TryGetProperty(key, out var d)) return _mats[key] = null;
            var shaderName = d.GetProperty("shader").GetString();
            bool tmp = shaderName.StartsWith("TextMeshPro/");
            if (tmp) m = new Material(TmpBase);
            else
            {
                var sh = UiShader(shaderName);
                if (sh == null)
                {
                    Plugin.Log.LogWarning($"{_style.Id}: no shader {shaderName} for {key}");
                    return _mats[key] = null;
                }
                m = new Material(sh);
            }
            m.name = _style.Id + " " + d.GetProperty("name").GetString();
            string atlasOf = null;
            foreach (var p in d.GetProperty("props").EnumerateObject())
            {
                var v = p.Value;
                if (tmp && (p.Name == "_MainTex" || p.Name.StartsWith("_Stencil") || p.Name == "_ColorMask" || p.Name.StartsWith("_MaskSoftness") || p.Name == "_ClipRect"))
                {
                    if (p.Name == "_MainTex" && v.ValueKind == JsonValueKind.Object && v.TryGetProperty("$tex", out var at))
                    {
                        var an = at.GetString();
                        int cut = an.IndexOf(" Atlas");
                        if (cut > 0) atlasOf = an.Substring(0, cut);
                    }
                    continue;
                }
                if (!m.HasProperty(p.Name)) continue;
                if (v.ValueKind == JsonValueKind.Number) m.SetFloat(p.Name, v.GetSingle());
                else if (v.ValueKind == JsonValueKind.Array) m.SetVector(p.Name, new Vector4(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle(), v[3].GetSingle()));
                else if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("$tex", out var tn))
                {
                    var tex = Texture(tn.GetString());
                    if (tex != null) m.SetTexture(p.Name, tex);
                }
            }
            var kws = new HashSet<string>();
            foreach (var kw in d.GetProperty("kw").EnumerateArray()) kws.Add(kw.GetString());
            if (tmp)
            {
                foreach (var k in new[] { "UNDERLAY_ON", "OUTLINE_ON" })
                    if (kws.Contains(k)) m.EnableKeyword(k); else m.DisableKeyword(k);
                m.SetColor("_FaceColor", Color.white);
                if (atlasOf != null)
                {
                    var fa = Font(_fonts.ContainsKey(atlasOf) || Load<TextAsset>(FontFile(atlasOf)) != null ? atlasOf : "Asap-Bold SDF (Body)");
                    if (fa != null) m.SetTexture("_MainTex", fa.atlasTexture);
                }
            }
            else foreach (var k in kws) m.EnableKeyword(k);
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return _mats[key] = m;
        }

        public static Material Soften(Material src, float reach, float alpha)
        {
            var m = new Material(src) { name = src.name + " soft" };
            m.SetFloat("_UnderlayOffsetX", src.GetFloat("_UnderlayOffsetX") * reach);
            m.SetFloat("_UnderlayOffsetY", src.GetFloat("_UnderlayOffsetY") * reach);
            m.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, alpha));
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return m;
        }
    }
}
