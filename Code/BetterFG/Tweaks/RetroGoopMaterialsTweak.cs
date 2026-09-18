using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LevelEditor;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class RetroGoopMaterialsTweak : BfgTweak
    {
        public RetroGoopMaterialsTweak(IntPtr ptr) : base(ptr) { }

        internal const string GateKey = "tweak.retro_goop_materials";

        public override string TweakId => "retro_goop_materials";
        public override string TweakLabel => "tweak.retro_goop_materials";
        public override bool DefaultEnabled => false;

        public static RetroGoopMaterialsTweak Instance { get; private set; }
        void Awake() => Instance = this;

        private static readonly Dictionary<string, string> Swap = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "VFX_Kill_Zone_Slime", "VFX_Goop_Background_Enviroment" },
            { "VFX_Kill_Zone_Slime_Low", "VFX_Goop_Background_Enviroment" },
            { "Floor_KillZone_MAT", "VFX_Goop_Background_Enviroment" },
            { "VFX_Goop_Survival_BG_Level_Editor_MAT", "VFX_Goop_Background_Enviroment" },
            { "VFX_Goop_Background_Enviroment_Level_Editor", "VFX_Goop_Background_Enviroment" },
        };

        private const string Mine = "BettrFG_";
        private const string ScalingShaderName = "BettrFG/RetroGoopScaling";

        private static readonly int WorldScaleId = Shader.PropertyToID("_WorldScale");
        private static readonly int MainColourId = Shader.PropertyToID("_Color");
        private static readonly int DepthColourId = Shader.PropertyToID("_DepthColor");
        private static readonly int TranslucencyColourId = Shader.PropertyToID("_TranslucencyColor");

        private static readonly int[] LiftedColourIds =
        {
            Shader.PropertyToID("_Color2"),
            Shader.PropertyToID("_Color3"),
            Shader.PropertyToID("_Colour"),
            Shader.PropertyToID("_ColorFresnel"),
            Shader.PropertyToID("_ColourFresnel"),
        };

        private const float Lift = 0.25f;
        private const float DepthDim = 0.55f;
        private const float DepthSat = 1.6f;

        private static readonly Dictionary<string, Material> _sources = new Dictionary<string, Material>(StringComparer.Ordinal);
        private static readonly Dictionary<int, Il2CppReferenceArray<Material>> _restore = new Dictionary<int, Il2CppReferenceArray<Material>>();
        private static readonly Dictionary<int, Renderer> _touched = new Dictionary<int, Renderer>();
        private static bool _scanned;
        private static Shader _scaling;
        private static bool _scalingLooked;

        public override void EnableTweak()
        {
            int n = 0;
            var col = LevelEditorPlaceableObject.Collection;
            if (col != null)
                for (int i = 0; i < col.Count; i++)
                    if (Apply(col[i])) n++;

            foreach (var lava in UnityEngine.Object.FindObjectsOfType<LevelEditorLavaController>())
                if (lava != null && lava.lavaVisuals != null && Skin(lava.lavaVisuals, null)) n++;

            if (n > 0) Plugin.Log.LogInfo($"retro goop on, {n} thing(s) repainted");
        }

        public override void DisableTweak()
        {
            foreach (var kv in _restore)
            {
                var r = _touched.TryGetValue(kv.Key, out var found) ? found : null;
                if (r != null) r.sharedMaterials = kv.Value;
            }
            int n = _restore.Count;
            _restore.Clear();
            _touched.Clear();
            if (n > 0) Plugin.Log.LogInfo($"retro goop off, {n} renderer(s) back on stock");
        }

        public override void OnLevelEditorPlaytest() => ScheduleLavaSkin();
        public override void OnLevelEditorPlaytestEnd() => ScheduleLavaSkin();

        internal static void OnRoundLoaded()
        {
            if (Instance == null || !Instance.IsEnabled) return;
            ScheduleLavaSkin();
        }

        internal static bool Apply(LevelEditorPlaceableObject lepo)
        {
            // No ScheduleLavaSkin here. This runs on RegisterObject, so every object you drop was
            // kicking SkinLava's whole-scene FindObjectsOfType<Renderer> sweep a second later, name
            // marshalling every material in the level - seconds of freeze per placement. Lava visuals
            // have their own trigger in RetroGoopLavaStartPatch; placing a block cannot make new ones.
            if (lepo == null) return false;
            return Skin(lepo.transform, lepo.GetComponentInChildren<LevelEditorDrawableData>(true));
        }

        internal static void OnColourChanged(LevelEditorPlaceableObject lepo)
        {
            if (lepo == null) return;
            foreach (var r in lepo.GetComponentsInChildren<Renderer>(true))
            {
                Recolour(r);
            }
        }

        private static bool _pending;

        internal static void ScheduleLavaSkin()
        {
            if (_pending) return;
            var host = Services.BeanMonitorService.Instance;
            if (host == null) { SkinLava(); return; }
            _pending = true;
            host.StartCoroutine(AfterASecond().WrapToIl2Cpp());
        }

        private static IEnumerator AfterASecond()
        {
            yield return new WaitForSecondsRealtime(1f);
            _pending = false;
            SkinLava();
        }

        private static void SkinLava()
        {
            int skinned = 0, retinted = 0;
            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0) continue;

                bool ours = false, maps = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    if (mats[i].name.StartsWith(Mine, StringComparison.Ordinal)) { ours = true; continue; }
                    if (Swap.ContainsKey(Bare(mats[i].name))) maps = true;
                }

                if (ours) { Recolour(r); retinted++; continue; }
                if (!maps && !UnderLavaVisuals(r.transform)) continue;
                if (SkinOne(r, r.GetComponentInParent<LevelEditorDrawableData>())) skinned++;
            }
            if (skinned > 0 || retinted > 0)
                Plugin.Log.LogInfo($"goop pass: {skinned} newly skinned, {retinted} retinted");
        }

        private static bool UnderLavaVisuals(Transform t)
        {
            for (int hops = 0; t != null && hops < 8; t = t.parent, hops++)
                if (t.name == "LavaVisuals") return true;
            return false;
        }

        private static string Bare(string name)
        {
            int paren = name.IndexOf(" (", StringComparison.Ordinal);
            return paren > 0 ? name.Substring(0, paren) : name;
        }

        private static bool Skin(Transform root, LevelEditorDrawableData drawable)
        {
            bool any = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (SkinOne(r, drawable)) any = true;
            return any;
        }

        private static bool SkinOne(Renderer r, LevelEditorDrawableData drawable)
        {
            if (r == null) return false;
            int id = r.GetInstanceID();
            var stock = _restore.TryGetValue(id, out var had) ? had : r.sharedMaterials;
            if (stock == null || stock.Length == 0) return false;

            bool lava = UnderLavaVisuals(r.transform);
            bool edge = r.name.IndexOf("Edge", StringComparison.OrdinalIgnoreCase) >= 0;
            var mats = new Il2CppReferenceArray<Material>(stock.Length);
            bool hit = false;
            for (int i = 0; i < stock.Length; i++)
            {
                var next = Twin(stock[i], drawable != null);
                if (next == null && lava && !edge && stock[i] != null && !stock[i].name.StartsWith(Mine, StringComparison.Ordinal))
                    next = Build(stock[i], Bare(stock[i].name), "VFX_Goop_Background_Enviroment", false);
                mats[i] = next ?? stock[i];
                if (next != null) hit = true;
            }
            if (!hit) return false;

            if (!_restore.ContainsKey(id))
            {
                var snap = new Il2CppReferenceArray<Material>(stock.Length);
                for (int j = 0; j < stock.Length; j++) snap[j] = stock[j];
                _restore[id] = snap;
            }
            _touched[id] = r;
            r.sharedMaterials = mats;
            Recolour(r);
            return true;
        }

        internal static void SubstituteVisible(LevelEditorObjectMaterialController controller)
        {
            var stored = controller._visibleMaterials;
            if (stored == null || stored.Length == 0) return;

            var lepo = controller.GetComponentInParent<LevelEditorPlaceableObject>();
            bool stretches = lepo != null && lepo.GetComponentInChildren<LevelEditorDrawableData>(true) != null;

            bool changed = false;
            for (int i = 0; i < stored.Length; i++)
            {
                var slot = stored[i];
                if (slot == null) continue;
                for (int j = 0; j < slot.Length; j++)
                {
                    var next = Twin(slot[j], stretches);
                    if (next == null) continue;
                    slot[j] = next;
                    changed = true;
                }
            }
            if (changed && controller._visible) controller.ApplyVisibleMaterials();
        }

        private static Material Twin(Material current, bool stretches)
        {
            if (current == null) return null;

            string name = current.name;
            if (name.StartsWith(Mine, StringComparison.Ordinal)) return null;
            int paren = name.IndexOf(" (", StringComparison.Ordinal);
            if (paren > 0) name = name.Substring(0, paren);
            if (!Swap.TryGetValue(name, out string wanted)) return null;
            return Build(current, name, wanted, stretches);
        }

        private static Material Build(Material current, string name, string wanted, bool stretches)
        {
            var basis = Source(wanted);
            if (basis == null) return null;

            var twin = new Material(basis) { name = Mine + name };

            var sh = ScalingShader();
            if (sh != null)
            {
                twin.shader = sh;
                twin.SetVector(WorldScaleId, current.HasProperty(WorldScaleId)
                    ? current.GetVector(WorldScaleId)
                    : new Vector4(1f, 1f, 1f, 1f));
            }
            else if (stretches) return null;

            if (current.HasProperty(MainColourId) && twin.HasProperty(MainColourId))
                Set(twin, MainColourId, current.GetColor(MainColourId));
            return twin;
        }

        private static MaterialPropertyBlock _block;

        internal static void Recolour(Renderer r)
        {
            if (r == null) return;
            var mat = r.sharedMaterial;
            if (mat == null || !mat.name.StartsWith(Mine, StringComparison.Ordinal)) return;

            if (_block == null) _block = new MaterialPropertyBlock();
            r.GetPropertyBlock(_block);

            Color main = _block.HasColor(MainColourId)
                ? _block.GetColor(MainColourId)
                : (mat.HasProperty(MainColourId) ? mat.GetColor(MainColourId) : Color.white);

            var lifted = Color.Lerp(main, Color.white, Lift);
            foreach (int id in LiftedColourIds)
                if (mat.HasProperty(id)) _block.SetColor(id, Keep(mat, id, lifted));

            if (mat.HasProperty(TranslucencyColourId))
                _block.SetColor(TranslucencyColourId, Keep(mat, TranslucencyColourId, main));

            if (mat.HasProperty(DepthColourId))
            {
                float lum = main.r * 0.299f + main.g * 0.587f + main.b * 0.114f;
                var deep = new Color(
                    Mathf.Clamp01(Mathf.Lerp(lum, main.r, DepthSat) * DepthDim),
                    Mathf.Clamp01(Mathf.Lerp(lum, main.g, DepthSat) * DepthDim),
                    Mathf.Clamp01(Mathf.Lerp(lum, main.b, DepthSat) * DepthDim));
                _block.SetColor(DepthColourId, Keep(mat, DepthColourId, deep));
            }

            r.SetPropertyBlock(_block);
        }

        private static Color Keep(Material mat, int id, Color c)
        {
            var a = mat.GetColor(id).a;
            return new Color(c.r, c.g, c.b, a);
        }

        private static Shader ScalingShader()
        {
            if (_scaling != null) return _scaling;
            if (_scalingLooked) return null;
            _scalingLooked = true;

            _scaling = Core.AssetManager.GetShader("RetroGoopScaling");
            if (_scaling == null) _scaling = Shader.Find(ScalingShaderName);
            if (_scaling == null)
                foreach (var s in Resources.FindObjectsOfTypeAll<Shader>())
                    if (s != null && s.name == ScalingShaderName) { _scaling = s; break; }

            if (_scaling == null)
                Plugin.Log.LogWarning($"{ScalingShaderName} didn't come out of bettrfg_shader_oldslime, drawables keep their stock material, the goop shader can't stretch on its own");
            return _scaling;
        }

        private static void Set(Material mat, int id, Color c)
        {
            if (!mat.HasProperty(id)) return;
            mat.SetColor(id, new Color(c.r, c.g, c.b, mat.GetColor(id).a));
        }

        private static Material Source(string name)
        {
            if (_sources.TryGetValue(name, out var m) && m != null) return m;
            if (!_scanned && Time.realtimeSinceStartup - _lastScan > 1f) Scan();
            _sources.TryGetValue(name, out m);
            return m != null ? m : null;
        }

        private static float _lastScan = -99f;

        private static void Scan()
        {
            _lastScan = Time.realtimeSinceStartup;
            var wanted = new HashSet<string>(Swap.Values, StringComparer.Ordinal);

            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m != null && wanted.Contains(m.name) && !_sources.ContainsKey(m.name)) _sources[m.name] = m;

            if (_sources.Count < wanted.Count) ScanBundles(wanted);

            _scanned = _sources.Count == wanted.Count;
            if (_scanned) return;

            foreach (string w in wanted)
                if (!_sources.ContainsKey(w))
                    Plugin.Log.LogWarning($"{w} isn't loaded yet, will look again in a second");
        }

        private static void ScanBundles(HashSet<string> wanted)
        {
            foreach (var ab in Resources.FindObjectsOfTypeAll<AssetBundle>())
            {
                if (ab == null) continue;

                Il2CppStringArray paths;
                try { paths = ab.GetAllAssetNames(); }
                catch { continue; }
                if (paths == null) continue;

                for (int i = 0; i < paths.Length; i++)
                {
                    string p = paths[i];
                    if (p == null || !p.EndsWith(".mat", StringComparison.Ordinal)) continue;

                    int slash = p.LastIndexOf('/');
                    string leaf = p.Substring(slash + 1, p.Length - slash - 5);

                    foreach (string w in wanted)
                    {
                        if (_sources.ContainsKey(w) || !string.Equals(leaf, w, StringComparison.OrdinalIgnoreCase)) continue;
                        var loaded = ab.LoadAsset(p, Il2CppInterop.Runtime.Il2CppType.Of<Material>())?.TryCast<Material>();
                        if (loaded != null) _sources[w] = loaded;
                        break;
                    }
                }
                if (_sources.Count == wanted.Count) return;
            }
        }
    }

    [HarmonyPatch(typeof(LevelEditorObjectMaterialController), nameof(LevelEditorObjectMaterialController.OverrideVisibleMaterials))]
    [BetterFG.Utilities.BfgPatchGate(RetroGoopMaterialsTweak.GateKey)]
    internal static class RetroGoopSurfacePatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorObjectMaterialController __instance)
            => RetroGoopMaterialsTweak.SubstituteVisible(__instance);
    }

    [HarmonyPatch(typeof(FG.Common.LevelEditorManager), nameof(FG.Common.LevelEditorManager.RegisterObject))]
    [BetterFG.Utilities.BfgPatchGate(RetroGoopMaterialsTweak.GateKey)]
    internal static class RetroGoopRegisterObjectPatch
    {
        [HarmonyPostfix]
        public static void Postfix([HarmonyArgument(0)] LevelEditorPlaceableObject placeableObject)
            => RetroGoopMaterialsTweak.Apply(placeableObject);
    }

    [HarmonyPatch(typeof(LevelEditorLavaController), "Start")]
    [BetterFG.Utilities.BfgPatchGate(RetroGoopMaterialsTweak.GateKey)]
    internal static class RetroGoopLavaStartPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => RetroGoopMaterialsTweak.ScheduleLavaSkin();
    }

    [HarmonyPatch(typeof(LevelEditorColourChangerParameter), nameof(LevelEditorColourChangerParameter.SetColour))]
    [BetterFG.Utilities.BfgPatchGate(RetroGoopMaterialsTweak.GateKey)]
    internal static class RetroGoopColourPatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorColourChangerParameter __instance)
            => RetroGoopMaterialsTweak.OnColourChanged(__instance.GetComponentInParent<LevelEditorPlaceableObject>());
    }
}
