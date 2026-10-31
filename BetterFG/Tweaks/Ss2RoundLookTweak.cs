using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Features.Assets;
using BetterFG.Utilities;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BetterFG.Tweaks
{
    public class Ss2RoundLookTweak : BfgTweak
    {
        public Ss2RoundLookTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "ss2_round_look";
        public override string TweakLabel => "tweak.ss2_round_look";
        public override string TweakTooltip => "tweak.ss2_round_look.tip";

        public static Ss2RoundLookTweak Instance { get; private set; }
        void Awake() => Instance = this;

        private static readonly string BakeRoot = OnlineData.Dir("OGLighting");
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static readonly List<Action> _undo = new List<Action>();
        private static readonly List<Action> _bgUndo = new List<Action>();
        private static GameObject _bgRoot;
        private static Dictionary<string, List<string[]>> _values;
        private static Dictionary<string, List<string[]>> _matRows;
        private static LightmapData[] _stockLightmaps;
        private static int _bakedScene;
        private static float _progress = -1f;
        private static bool _cancel;

        internal static readonly Dictionary<string, float> Fetching = new Dictionary<string, float>();
        internal static int FetchSeq;
        internal static bool Bulk;

        public override float Progress => _progress;

        public override List<TweakButton> GetCustomButtons() => new List<TweakButton>
        {
            new TweakButton { Label = "tweak.ss2_round_look.library", OnClick = Ss2RoundLookPrompt.OpenLibrary }
        };

        public override void Toggle()
        {
            if (IsEnabled) SetEnabled(false);
            else Ss2RoundLookPrompt.Ask();
        }

        private static void SetProgress(float p)
        {
            if (p < 0f != _progress < 0f) RowSeq++;
            _progress = p;
        }

        internal static void Fetch(string package) => Instance.StartCoroutine(FetchOne(package).WrapToIl2Cpp());

        internal static void FetchAll() => Instance.StartCoroutine(FetchEverything().WrapToIl2Cpp());

        internal static void CancelFetchAll() => _cancel = true;

        internal static void Remove(string package)
        {
            if (Fetching.ContainsKey(package)) return;
            OnlineData.Uninstall(package);
            FetchSeq++;
        }

        private static IEnumerator FetchOne(string package)
        {
            if (!Fetching.TryAdd(package, 0f)) yield break;
            FetchSeq++;
            try { yield return OnlineData.Fetch(package, p => Fetching[package] = p).WrapToIl2Cpp(); }
            finally { Fetching.Remove(package); FetchSeq++; }
        }

        private static IEnumerator FetchEverything()
        {
            if (Bulk) yield break;
            Bulk = true;
            _cancel = false;
            FetchSeq++;
            yield return OnlineData.LoadManifest().WrapToIl2Cpp();
            int done = 0, failed = 0;
            foreach (var package in OnlineData.Listed("OGLighting/"))
            {
                if (_cancel) break;
                if (OnlineData.Installed(package)) continue;
                yield return FetchOne(package).WrapToIl2Cpp();
                if (OnlineData.Installed(package)) done++; else failed++;
            }
            Plugin.Log.LogInfo($"og lighting bulk {(_cancel ? "cancelled" : "finished")}: {done} in, {failed} didn't come down");
            Bulk = false;
            FetchSeq++;
        }

        private static IEnumerator FetchWithBar(string package)
        {
            SetProgress(0f);
            try { yield return OnlineData.Fetch(package, SetProgress).WrapToIl2Cpp(); }
            finally { SetProgress(-1f); }
        }

        public override void EnableTweak()
        {
            StartCoroutine(FetchThenApply().WrapToIl2Cpp());
            if (_bgRoot != null) FixBackground();
        }

        public override void DisableTweak()
        {
            UndoBackground();
            Restore();
        }

        internal static void OnBackground(Customization.Round.BackgroundKind kind, string id, GameObject root)
        {
            if (kind == Customization.Round.BackgroundKind.Round) OnRoundLoaded();
            else if (kind == Customization.Round.BackgroundKind.Package) OnBackgroundBuilt(root);
            else if (kind == Customization.Round.BackgroundKind.PackageGone) OnBackgroundGone();
        }

        private static void OnBackgroundBuilt(GameObject root)
        {
            _bgRoot = root;
            if (Instance != null && Instance.IsEnabled) FixBackground();
        }

        internal static void OnBackgroundGone()
        {
            UndoBackground();
            _bgRoot = null;
        }

        private static void FixBackground()
        {
            UndoBackground();
            var byName = new Dictionary<string, List<Material>>();
            CollectMaterials(_bgRoot, byName, new HashSet<int>());
            int n = 0;
            foreach (var kv in byName)
            {
                if (!MatRows().TryGetValue(kv.Key, out var rows)) continue;
                foreach (var row in rows)
                {
                    if (row[2] == "float _UVSecPattern" && row[3] == "2" && !AllHaveUv2(_bgRoot.scene, kv.Key)) continue;
                    foreach (var m in kv.Value)
                        if (SetMaterial(m, row[2], row[3], _bgUndo)) n++;
                }
            }
            int statues = AmbientForLightmappedBeans(new[] { _bgRoot }, _bgUndo);
            Plugin.Log.LogInfo($"background {_bgRoot.name}: {n} ss2 material values, {statues} bean statues lit");
        }

        private static void UndoBackground()
        {
            for (int i = _bgUndo.Count - 1; i >= 0; i--)
            {
                try { _bgUndo[i](); }
                catch (Exception ex) { Plugin.Log.LogWarning($"background undo step threw, skipping it: {ex.Message}"); }
            }
            _bgUndo.Clear();
        }

        internal static void OnRoundLoaded()
        {
            if (Instance == null || !Instance.IsEnabled) return;
            Instance.StartCoroutine(FetchThenApply().WrapToIl2Cpp());
        }

        internal static void OnLoadingStarted(FG.Common.CMS.Round round)
        {
            string scene = round?.GetSceneName();
            if (Instance == null || !Instance.IsEnabled || string.IsNullOrEmpty(scene) || !SceneExport.IsUnityRound(scene)) return;
            Instance.StartCoroutine(FetchWithBar("OGLighting/" + scene).WrapToIl2Cpp());
        }

        private static IEnumerator FetchThenApply()
        {
            var scene = SceneManager.GetActiveScene();
            if (!SceneExport.IsUnityRound(scene.name)) yield break;
            if (!OnlineData.Ready("OGLighting/" + scene.name))
            {
                yield return FetchWithBar("OGLighting/" + scene.name).WrapToIl2Cpp();
                if (!Instance.IsEnabled || SceneManager.GetActiveScene().handle != scene.handle) yield break;
            }
            Apply();
        }

        private static void Apply()
        {
            Restore();
            var scene = SceneManager.GetActiveScene();
            if (!SceneExport.IsUnityRound(scene.name)) return;

            var byName = new Dictionary<string, List<Material>>();
            var lights = new Dictionary<string, Light>();
            var probes = new Dictionary<string, ReflectionProbe>();
            var renderers = new Dictionary<string, List<MeshRenderer>>();
            var seen = new HashSet<int>();
            foreach (var root in scene.GetRootGameObjects())
            {
                CollectMaterials(root, byName, seen);
                foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var path = PathOf(mr.transform);
                    if (!renderers.TryGetValue(path, out var list)) renderers[path] = list = new List<MeshRenderer>();
                    list.Add(mr);
                }
                foreach (var l in root.GetComponentsInChildren<Light>(true)) lights[PathOf(l.transform)] = l;
                foreach (var p in root.GetComponentsInChildren<ReflectionProbe>(true)) probes[PathOf(p.transform)] = p;
            }

            int values = 0;
            if (Values().TryGetValue(scene.name, out var rows))
                values = ApplyValues(scene, rows, byName, lights, probes);
            string bake = RestoreBake(scene, renderers);
            int statues = AmbientForLightmappedBeans(scene.GetRootGameObjects(), _undo);
            Plugin.Log.LogInfo($"og lighting on {scene.name}: {values} ss2 values back, {bake}, {statues} bean statues got ambient back");
        }

        private static void CollectMaterials(GameObject root, Dictionary<string, List<Material>> byName, HashSet<int> seen)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !seen.Add(m.GetInstanceID())) continue;
                    var name = m.name.Replace(" (Instance)", "");
                    if (!byName.TryGetValue(name, out var list)) byName[name] = list = new List<Material>();
                    list.Add(m);
                }
        }

        private static int AmbientForLightmappedBeans(IEnumerable<GameObject> roots, List<Action> undo)
        {
            var sh = ReadAmbientProbe();
            if (sh == null) return 0;
            var block = new MaterialPropertyBlock();
            string[] channel = { "r", "g", "b" };
            for (int c = 0; c < 3; c++)
            {
                int o = c * 9;
                block.SetVector("unity_SHA" + channel[c], new Vector4(sh[o + 3], sh[o + 1], sh[o + 2], sh[o] - sh[o + 6]));
                block.SetVector("unity_SHB" + channel[c], new Vector4(sh[o + 4], sh[o + 5], sh[o + 6] * 3f, sh[o + 7]));
            }
            block.SetVector("unity_SHC", new Vector4(sh[8], sh[17], sh[26], 1f));

            int n = 0;
            foreach (var root in roots)
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.lightmapIndex < 0 || r.lightmapIndex >= 65534 || r.lightProbeUsage == LightProbeUsage.CustomProvided) continue;
                    var mats = r.sharedMaterials;
                    var slots = new List<int>();
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] != null && mats[i].shader.name == "Mediatonic/CH_Customization_3") slots.Add(i);
                    if (slots.Count == 0) continue;
                    var usage = r.lightProbeUsage;
                    r.lightProbeUsage = LightProbeUsage.CustomProvided;
                    foreach (var i in slots) r.SetPropertyBlock(block, i);
                    undo.Add(() =>
                    {
                        if (r == null) return;
                        r.lightProbeUsage = usage;
                        foreach (var i in slots) r.SetPropertyBlock(null, i);
                    });
                    n++;
                }
            return n;
        }

        private static void Restore()
        {
            for (int i = _undo.Count - 1; i >= 0; i--)
            {
                try { _undo[i](); }
                catch (Exception ex) { Plugin.Log.LogWarning($"og lighting undo step threw, skipping it: {ex.Message}"); }
            }
            _undo.Clear();
            if (_stockLightmaps != null && SceneManager.GetActiveScene().handle == _bakedScene) LightmapSettings.lightmaps = _stockLightmaps;
            _stockLightmaps = null;
        }

        private static int ApplyValues(Scene scene, List<string[]> rows, Dictionary<string, List<Material>> byName,
            Dictionary<string, Light> lights, Dictionary<string, ReflectionProbe> probes)
        {
            bool renderSaved = false;
            int n = 0;
            foreach (var row in rows)
            {
                switch (row[0])
                {
                    case "render":
                        if (!renderSaved) { SaveRender(); renderSaved = true; }
                        if (SetRender(row[1], row[2])) n++;
                        break;
                    case "ambient":
                        if (SetAmbient(row[1])) n++;
                        break;
                    case "light":
                        if (lights.TryGetValue(row[1], out var l) && SetLight(l, row[2], row[3])) n++;
                        break;
                    case "probe":
                        if (probes.TryGetValue(row[1], out var p) && SetProbe(p, row[2], row[3])) n++;
                        break;
                    case "mat":
                        if (!byName.TryGetValue(row[1], out var list)) break;
                        if (row[2] == "float _UVSecPattern" && row[3] == "2" && !AllHaveUv2(scene, row[1])) break;
                        foreach (var m in list)
                            if (SetMaterial(m, row[2], row[3], _undo)) n++;
                        break;
                }
            }
            return n;
        }

        private static string RestoreBake(Scene scene, Dictionary<string, List<MeshRenderer>> live)
        {
            string dir = Path.Combine(BakeRoot, scene.name);
            string manifest = Path.Combine(dir, "lightmaps.txt");
            bool geomOnly = !File.Exists(manifest);
            if (geomOnly && !File.Exists(Path.Combine(dir, "geom.bin"))) return "no ss2 bake for this round";

            var maps = new Dictionary<int, LightmapData>();
            var owned = new List<UnityEngine.Object>();
            foreach (var line in geomOnly ? Array.Empty<string>() : File.ReadAllLines(manifest))
            {
                var p = line.Split('\t');
                if (p.Length < 9) continue;
                int index = int.Parse(p[0]), w = int.Parse(p[2]), h = int.Parse(p[3]), mips = int.Parse(p[5]);
                var format = (TextureFormat)int.Parse(p[4]);
                var blk = File.ReadAllBytes(Path.Combine(dir, "lm_" + p[0] + "_" + p[1] + ".blk"));
                int size = format == TextureFormat.DXT1 ? 8 : 16, blocks = 0;
                for (int m = 0; m < mips; m++) blocks += (Math.Max(1, w >> m) + 3) / 4 * ((Math.Max(1, h >> m) + 3) / 4);
                var raw = new byte[blocks * size];
                for (int b = 0, from = size + (blocks + 7) / 8; b < blocks; b++)
                {
                    bool stored = (blk[size + (b >> 3)] >> (b & 7) & 1) != 0;
                    Buffer.BlockCopy(blk, stored ? from : 0, raw, b * size, size);
                    if (stored) from += size;
                }
                var tex = new Texture2D(w, h, format, mips, true);
                tex.name = "BettrFG_ss2_" + p[8];
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.ignoreMipmapLimit = true;
                tex.LoadRawTextureData((Il2CppStructArray<byte>)raw);
                tex.Apply(false, true);
                owned.Add(tex);
                if (!maps.TryGetValue(index, out var data)) maps[index] = data = new LightmapData();
                if (p[1] == "Lightmap") data.lightmapColor = tex;
                else if (p[1] == "DirLightmap") data.lightmapDir = tex;
                else data.shadowMask = tex;
            }

            int offset = 0;
            if (!geomOnly)
            {
                _stockLightmaps = LightmapSettings.lightmaps;
                _bakedScene = scene.handle;
                offset = _stockLightmaps.Length;
                var all = new LightmapData[offset + maps.Count];
                for (int i = 0; i < offset; i++) all[i] = _stockLightmaps[i];
                foreach (var kv in maps) all[offset + kv.Key] = kv.Value;
                LightmapSettings.lightmaps = all;
            }

            var geom = File.ReadAllBytes(Path.Combine(dir, "geom.bin"));
            var used = new HashSet<int>();
            var spawned = new List<GameObject>();
            var hidden = new List<Renderer>();
            int built = 0, retargeted = 0, kept = 0;
            foreach (var line in File.ReadAllLines(Path.Combine(dir, "renderers.txt")))
            {
                var p = line.Split('\t');
                if (p.Length < 10 || !live.TryGetValue(p[0], out var candidates)) continue;
                long at = long.Parse(p[5]);
                var center = new Vector3(F(p[7]), F(p[8]), F(p[9]));
                MeshRenderer best = null;
                float bestDistance = float.MaxValue;
                foreach (var c in candidates)
                {
                    if (used.Contains(c.GetInstanceID())) continue;
                    float d = ((at < 0 ? c.transform.position : c.bounds.center) - center).sqrMagnitude;
                    if (d < bestDistance) { bestDistance = d; best = c; }
                }
                if (best == null) continue;
                used.Add(best.GetInstanceID());

                var st = p[2].Split(',');
                if (at < 0)
                {
                    if (best.isPartOfStaticBatch || geomOnly) continue;
                    var stockIndex = best.lightmapIndex;
                    var stockSt = best.lightmapScaleOffset;
                    best.lightmapIndex = offset + int.Parse(p[1]);
                    best.lightmapScaleOffset = new Vector4(F(st[0]), F(st[1]), F(st[2]), F(st[3]));
                    var r = best;
                    _undo.Add(() => { if (r != null) { r.lightmapIndex = stockIndex; r.lightmapScaleOffset = stockSt; } });
                    retargeted++;
                    continue;
                }

                var mats = best.sharedMaterials;
                if ((BitConverter.ToInt32(geom, (int)at + 4) & 1) == 0 && NeedsUv2(mats)) { kept++; continue; }
                var v = p[10].Split(',');
                var place = Matrix4x4.identity;
                for (int i = 0; i < 12; i++) place[i / 4, i % 4] = F(v[i]);
                var mesh = ReadMesh(geom, (int)at, best.transform.worldToLocalMatrix * place, out int subMeshes);
                if (mats.Length != subMeshes) { UnityEngine.Object.Destroy(mesh); continue; }

                var go = new GameObject("BettrFG_ss2_bake");
                go.transform.SetParent(best.transform, false);
                go.layer = best.gameObject.layer;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = mats;
                renderer.lightmapIndex = offset + int.Parse(p[1]);
                renderer.lightmapScaleOffset = new Vector4(F(st[0]), F(st[1]), F(st[2]), F(st[3]));
                renderer.shadowCastingMode = best.shadowCastingMode;
                renderer.receiveShadows = best.receiveShadows;
                renderer.reflectionProbeUsage = best.reflectionProbeUsage;
                renderer.probeAnchor = best.probeAnchor;
                spawned.Add(go);
                owned.Add(mesh);
                if (best.enabled) { best.enabled = false; hidden.Add(best); }
                built++;
            }

            _undo.Add(() =>
            {
                foreach (var go in spawned) if (go != null) UnityEngine.Object.Destroy(go);
                foreach (var r in hidden) if (r != null) r.enabled = true;
                foreach (var o in owned) if (o != null) UnityEngine.Object.Destroy(o);
            });
            return $"ss2 bake: {(geomOnly ? "geometry only, game's own" : maps.Count.ToString())} lightmaps, {built} rebuilt, {retargeted} retargeted, {kept} kept on today's mesh for uv2";
        }

        private static bool NeedsUv2(Material[] mats)
        {
            foreach (var m in mats)
            {
                if (m == null) continue;
                if (m.HasProperty("_UVSecPattern") && m.GetFloat("_UVSecPattern") == 2f && m.GetFloat("_UseDetailAlbedo") > 0.5f) return true;
                if (m.HasProperty("_UVSecNormalMap") && m.GetFloat("_UVSecNormalMap") == 2f && m.GetFloat("_UseDetailNormal") > 0.5f) return true;
                if (m.HasProperty("_UVSecAlbedo") && m.GetFloat("_UVSecAlbedo") == 2f) return true;
                if (m.HasProperty("_UVMetallicSmoothness") && m.GetFloat("_UVMetallicSmoothness") == 2f) return true;
            }
            return false;
        }

        private static bool AllHaveUv2(Scene scene, string materialName)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    bool uses = false;
                    foreach (var m in r.sharedMaterials)
                        if (m != null && m.name.Replace(" (Instance)", "") == materialName) uses = true;
                    if (!uses) continue;
                    var filter = r.GetComponent<MeshFilter>();
                    var mesh = filter != null ? filter.sharedMesh : r.TryCast<SkinnedMeshRenderer>()?.sharedMesh;
                    if (mesh == null || !mesh.HasVertexAttribute(VertexAttribute.TexCoord2)) return false;
                }
            return true;
        }

        internal static Mesh ReadMesh(byte[] bytes, int at, Matrix4x4 toLocal, out int subMeshCount)
        {
            var r = new BinaryReader(new MemoryStream(bytes, at, bytes.Length - at));
            int n = r.ReadInt32(), flags = r.ReadInt32();
            subMeshCount = r.ReadInt32();
            var counts = new int[subMeshCount];
            for (int s = 0; s < subMeshCount; s++) counts[s] = r.ReadInt32();
            var normalMatrix = toLocal.inverse.transpose;
            bool mirrored = toLocal.determinant < 0f;
            var pos = new Vector3[n];
            var nrm = new Vector3[n];
            var tan = new Vector4[n];
            var uv0 = new Vector2[n];
            var uv1 = new Vector2[n];
            var uv2 = new Vector2[n];
            var col = new Color32[n];
            for (int i = 0; i < n; i++) pos[i] = toLocal.MultiplyPoint3x4(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
            for (int i = 0; i < n; i++) nrm[i] = normalMatrix.MultiplyVector(new Vector3(r.ReadInt16(), r.ReadInt16(), r.ReadInt16())).normalized;
            if ((flags & 4) != 0)
                for (int i = 0; i < n; i++)
                {
                    var t = toLocal.MultiplyVector(new Vector3(r.ReadInt16(), r.ReadInt16(), r.ReadInt16())).normalized;
                    tan[i] = new Vector4(t.x, t.y, t.z, Mathf.Sign(r.ReadInt16()));
                }
            for (int i = 0; i < n; i++) uv0[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
            for (int i = 0; i < n; i++) uv1[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
            if ((flags & 1) != 0) for (int i = 0; i < n; i++) uv2[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
            if ((flags & 2) != 0) for (int i = 0; i < n; i++) col[i] = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());

            var mesh = new Mesh { name = "BettrFG_ss2_bake" };
            if (n > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = pos;
            mesh.normals = nrm;
            if ((flags & 4) != 0) mesh.tangents = tan;
            mesh.uv = uv0;
            mesh.uv2 = uv1;
            if ((flags & 1) != 0) mesh.uv3 = uv2;
            if ((flags & 2) != 0) mesh.colors32 = col;
            mesh.subMeshCount = subMeshCount;
            for (int s = 0; s < subMeshCount; s++)
            {
                int count = counts[s];
                var idx = new int[count];
                for (int i = 0; i < count; i++) idx[i] = r.ReadUInt16();
                if (mirrored)
                    for (int i = 0; i + 2 < count; i += 3) { int t = idx[i + 1]; idx[i + 1] = idx[i + 2]; idx[i + 2] = t; }
                mesh.SetTriangles(idx, s);
            }
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static void SaveRender()
        {
            var was = (RenderSettings.ambientMode, RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor,
                RenderSettings.ambientIntensity, RenderSettings.fog, RenderSettings.fogColor, RenderSettings.fogMode, RenderSettings.fogDensity,
                RenderSettings.fogStartDistance, RenderSettings.fogEndDistance, RenderSettings.reflectionIntensity, RenderSettings.subtractiveShadowColor);
            _undo.Add(() =>
            {
                RenderSettings.ambientMode = was.Item1;
                RenderSettings.ambientSkyColor = was.Item2;
                RenderSettings.ambientEquatorColor = was.Item3;
                RenderSettings.ambientGroundColor = was.Item4;
                RenderSettings.ambientIntensity = was.Item5;
                RenderSettings.fog = was.Item6;
                RenderSettings.fogColor = was.Item7;
                RenderSettings.fogMode = was.Item8;
                RenderSettings.fogDensity = was.Item9;
                RenderSettings.fogStartDistance = was.Item10;
                RenderSettings.fogEndDistance = was.Item11;
                RenderSettings.reflectionIntensity = was.Item12;
                RenderSettings.subtractiveShadowColor = was.Item13;
            });
        }

        private static bool SetRender(string key, string value)
        {
            switch (key)
            {
                case "m_AmbientMode": RenderSettings.ambientMode = (AmbientMode)int.Parse(value); return true;
                case "m_AmbientSkyColor": RenderSettings.ambientSkyColor = Col(value); return true;
                case "m_AmbientEquatorColor": RenderSettings.ambientEquatorColor = Col(value); return true;
                case "m_AmbientGroundColor": RenderSettings.ambientGroundColor = Col(value); return true;
                case "m_AmbientIntensity": RenderSettings.ambientIntensity = F(value); return true;
                case "m_Fog": RenderSettings.fog = value == "True"; return true;
                case "m_FogColor": RenderSettings.fogColor = Col(value); return true;
                case "m_FogMode": RenderSettings.fogMode = (FogMode)int.Parse(value); return true;
                case "m_FogDensity": RenderSettings.fogDensity = F(value); return true;
                case "m_LinearFogStart": RenderSettings.fogStartDistance = F(value); return true;
                case "m_LinearFogEnd": RenderSettings.fogEndDistance = F(value); return true;
                case "m_ReflectionIntensity": RenderSettings.reflectionIntensity = F(value); return true;
                case "m_SubtractiveShadowColor": RenderSettings.subtractiveShadowColor = Col(value); return true;
            }
            return false;
        }

        private delegate void ProbePointer(IntPtr sh);
        private static ProbePointer _ambientGet;
        private static ProbePointer _ambientSet;

        private static bool AmbientIcalls()
        {
            if (_ambientSet != null) return true;
            var get = Il2CppInterop.Runtime.IL2CPP.il2cpp_resolve_icall("UnityEngine.RenderSettings::get_ambientProbe_Injected");
            var set = Il2CppInterop.Runtime.IL2CPP.il2cpp_resolve_icall("UnityEngine.RenderSettings::set_ambientProbe_Injected");
            if (get == IntPtr.Zero || set == IntPtr.Zero) return false;
            _ambientGet = Marshal.GetDelegateForFunctionPointer<ProbePointer>(get);
            _ambientSet = Marshal.GetDelegateForFunctionPointer<ProbePointer>(set);
            return true;
        }

        internal static float[] ReadAmbientProbe()
        {
            if (!AmbientIcalls()) return null;
            var sh = new float[27];
            var mem = Marshal.AllocHGlobal(27 * 4);
            try { _ambientGet(mem); Marshal.Copy(mem, sh, 0, 27); }
            finally { Marshal.FreeHGlobal(mem); }
            return sh;
        }

        internal static void WriteAmbientProbe(float[] sh)
        {
            if (!AmbientIcalls()) return;
            var mem = Marshal.AllocHGlobal(27 * 4);
            try { Marshal.Copy(sh, 0, mem, 27); _ambientSet(mem); }
            finally { Marshal.FreeHGlobal(mem); }
        }

        private static bool SetAmbient(string value)
        {
            var parts = value.Split(',');
            if (parts.Length != 27 || parts[0] == "nan") return false;
            if (!AmbientIcalls()) return false;
            var sh = new float[27];
            for (int i = 0; i < 27; i++) sh[i] = F(parts[i]);
            var was = new float[27];
            var mem = Marshal.AllocHGlobal(27 * 4);
            try
            {
                _ambientGet(mem);
                Marshal.Copy(mem, was, 0, 27);
                Marshal.Copy(sh, 0, mem, 27);
                _ambientSet(mem);
            }
            finally { Marshal.FreeHGlobal(mem); }
            _undo.Add(() =>
            {
                var back = Marshal.AllocHGlobal(27 * 4);
                try { Marshal.Copy(was, 0, back, 27); _ambientSet(back); }
                finally { Marshal.FreeHGlobal(back); }
            });
            return true;
        }

        private static bool SetLight(Light l, string key, string value)
        {
            var was = (l.color, l.intensity, l.bounceIntensity, l.shadowStrength, l.colorTemperature, l.useColorTemperature, l.range, l.spotAngle, l.shadows, l.transform.localRotation);
            switch (key)
            {
                case "m_Color": l.color = Col(value); break;
                case "m_Intensity": l.intensity = F(value); break;
                case "m_BounceIntensity": l.bounceIntensity = F(value); break;
                case "m_Shadows.m_Strength": l.shadowStrength = F(value); break;
                case "m_ColorTemperature": l.colorTemperature = F(value); break;
                case "m_UseColorTemperature": l.useColorTemperature = value == "True"; break;
                case "m_Range": l.range = F(value); break;
                case "m_SpotAngle": l.spotAngle = F(value); break;
                case "m_Shadows.m_Type": l.shadows = (LightShadows)int.Parse(value); break;
                case "localRotation":
                    var q = Col(value);
                    l.transform.localRotation = new Quaternion(q.r, q.g, q.b, q.a);
                    break;
                default: return false;
            }
            _undo.Add(() =>
            {
                if (l == null) return;
                l.color = was.Item1;
                l.intensity = was.Item2;
                l.bounceIntensity = was.Item3;
                l.shadowStrength = was.Item4;
                l.colorTemperature = was.Item5;
                l.useColorTemperature = was.Item6;
                l.range = was.Item7;
                l.spotAngle = was.Item8;
                l.shadows = was.Item9;
                l.transform.localRotation = was.Item10;
            });
            return true;
        }

        private static bool SetProbe(ReflectionProbe p, string key, string value)
        {
            var was = (p.intensity, p.blendDistance, p.boxProjection, p.size, p.center);
            switch (key)
            {
                case "m_IntensityMultiplier": p.intensity = F(value); break;
                case "m_BlendDistance": p.blendDistance = F(value); break;
                case "m_BoxProjection": p.boxProjection = value == "True" || value == "1"; break;
                case "m_BoxSize": var s = Col(value); p.size = new Vector3(s.r, s.g, s.b); break;
                case "m_BoxOffset": var o = Col(value); p.center = new Vector3(o.r, o.g, o.b); break;
                default: return false;
            }
            _undo.Add(() =>
            {
                if (p == null) return;
                p.intensity = was.Item1;
                p.blendDistance = was.Item2;
                p.boxProjection = was.Item3;
                p.size = was.Item4;
                p.center = was.Item5;
            });
            return true;
        }

        private static bool SetMaterial(Material m, string key, string value, List<Action> undo)
        {
            bool isFloat = key.StartsWith("float ", StringComparison.Ordinal);
            string prop = key.Substring(6);
            int index = m.shader.FindPropertyIndex(prop);
            if (index < 0) return false;
            var type = m.shader.GetPropertyType(index);
            if (isFloat && (type == ShaderPropertyType.Float || type == ShaderPropertyType.Range))
            {
                float was = m.GetFloat(prop);
                m.SetFloat(prop, F(value));
                undo.Add(() => { if (m != null) m.SetFloat(prop, was); });
                return true;
            }
            if (!isFloat && (type == ShaderPropertyType.Color || type == ShaderPropertyType.Vector))
            {
                var was = m.GetVector(prop);
                var c = Col(value);
                m.SetVector(prop, new Vector4(c.r, c.g, c.b, c.a));
                undo.Add(() => { if (m != null) m.SetVector(prop, was); });
                return true;
            }
            return false;
        }

        private static string PathOf(Transform t)
        {
            var names = new List<string>();
            for (; t != null; t = t.parent) names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }

        private static float F(string s) => float.Parse(s, Inv);

        private static Color Col(string s)
        {
            var p = s.Trim('(', ')').Split(',');
            return new Color(F(p[0]), F(p[1]), F(p[2]), p.Length > 3 ? F(p[3]) : 1f);
        }

        private static Dictionary<string, List<string[]>> MatRows()
        {
            if (_matRows != null) return _matRows;
            var first = new Dictionary<string, string[]>();
            var clash = new HashSet<string>();
            foreach (var rows in Values().Values)
                foreach (var row in rows)
                {
                    if (row[0] != "mat") continue;
                    string key = row[1] + "\t" + row[2];
                    if (!first.TryGetValue(key, out var had)) first[key] = row;
                    else if (had[3] != row[3]) clash.Add(key);
                }
            _matRows = new Dictionary<string, List<string[]>>();
            foreach (var kv in first)
            {
                if (clash.Contains(kv.Key)) continue;
                if (!_matRows.TryGetValue(kv.Value[1], out var list)) _matRows[kv.Value[1]] = list = new List<string[]>();
                list.Add(kv.Value);
            }
            return _matRows;
        }

        private static Dictionary<string, List<string[]>> Values()
        {
            if (_values != null) return _values;
            _values = new Dictionary<string, List<string[]>>();
            var text = EmbeddedResourceandUnity.LoadText("BetterFG.assets.ss2.ss2_values.txt");
            if (text == null) return _values;
            List<string[]> current = null;
            foreach (var line in text.Split('\n'))
            {
                var row = line.TrimEnd('\r').Split('\t');
                if (row.Length < 2) continue;
                if (row[0] == "scene") { _values[row[1]] = current = new List<string[]>(); continue; }
                current?.Add(row);
            }
            return _values;
        }
    }
}
