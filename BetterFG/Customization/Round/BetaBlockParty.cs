using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Tweaks;
using BetterFG.Utilities;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BetterFG.Customization.Round
{
    internal static class BetaBlockParty
    {
        private const string SceneName = "FallGuy_Block_Party";
        private const float Chance = 1f;
        private const string Package = "BetaRounds/BlockParty";
        private const string NightBundle = "terrain_beta_night";
        private const string PinkEmissive = "fd604f316697f3745bff491e07e7ea35";
        private const string Smoke = "db694df8c2aebec40a9aeddb8bbf8371";
        private const float FloorTop = 5.61f;

        private static readonly string[] Segments =
        {
            "1e89df882339cbd4180c75e651cc023f", "2731e1108a402f14a94f472496410324", "38668467e93a2bd408c2200a3b9806bc",
            "3fe8a2d7d0b283a4eb1be682aa26614d", "40b8a877e437cec468a0cb017ab18136", "41b3f85fe4a3672418e150f6fb0631a8",
            "4417040fba858f54b81ed3f8b54db3e4", "481a4a637ecdea34dba2d215af9fd510", "537bf661c21f43f4fbb3ed404d19b3a5",
            "5854927c4615c7448839a9b91c5b0c31", "5c0cd5b1ca7a77849b5b1166c79660ce", "5db1dd403159ec345bdee68fad8485f5",
            "6517e1b7544e544458ddda456fe12338", "7343a9ff142654e4a8164512864656b9", "8ca933ff0ac5cf843872043406564207",
            "a26c86d0751681e418aa1d92ee49e126", "a69236022f143554fb981729706bdadf", "adaceeccc4b6ea24c8240a382b39bb30",
            "b5692a96cb48e3e44bdb4d093e8ee60f", "bdad0671194ac0648b168ba2995ef6a6", "c844d06c4675a464c8c79ab89db2a954",
            "d620c01298a3b584f9b70b38863bacce", "e6f89df6e43d72548b72514c1b347761", "f41ff3d1e0abaa34cb1ea3903af3b3b0",
            "f485b63ae2a32e345924826d28845e86", "f73d51f1923a1344c86758b6e321d3b4", "fc84f3cce378f4d49bccc6d6cd7e2628",
        };

        private static readonly Vector3 BetaCenter = new Vector3(5f, 1f, -32f);
        private static readonly Vector3 PlatformPos = new Vector3(0.05f, FloorTop, 0.15f);
        private static readonly Vector3 PlatformScale = new Vector3(39.7f / 40f, 1f, 23.9f / 25f * 340f / 330f);

        internal static bool Active;
        private static int _built = -1;
        private static readonly List<(MeshFilter Filter, Mesh Mesh, Il2CppReferenceArray<Material> Mats)> _swapped = new List<(MeshFilter, Mesh, Il2CppReferenceArray<Material>)>();
        private static Dictionary<string, Mesh> _beta;
        private static readonly Dictionary<string, Mesh> _fitted = new Dictionary<string, Mesh>();
        private static Material _blocks, _yellow;

        internal static void Roll(string scene)
        {
            Active = scene == SceneName && UnityEngine.Random.value <= Chance;
            if (!Active) { PutBack(); return; }
            AssetManager.Instance.StartCoroutine(OnlineData.Fetch(Package).WrapToIl2Cpp());
            if (!Background3dTweak.Have(NightBundle)) AssetManager.Instance.StartCoroutine(Background3dTweak.Fetch(NightBundle).WrapToIl2Cpp());
        }

        internal static bool Owns(string scene) => Active && scene == SceneName;

        internal static string Pick(string scene) => Owns(scene) && Background3dTweak.Have(NightBundle) ? "bettrfg:" + NightBundle : null;

        internal static string OnReplayLoaded(bool recorded, string pick)
        {
            Active = recorded && SceneManager.GetActiveScene().name == SceneName;
            if (!Active) { PutBack(); return pick; }
            if (!Background3dTweak.Have(NightBundle)) AssetManager.Instance.StartCoroutine(Background3dTweak.Fetch(NightBundle).WrapToIl2Cpp());
            Build();
            return pick == "bettrfg:" + NightBundle && !Background3dTweak.Have(NightBundle) ? "" : pick;
        }

        internal static void Sky(string scene, AssetBundle bundle)
        {
            if (!Owns(scene) || bundle == null) return;
            foreach (var n in bundle.GetAllAssetNames())
                if (n.EndsWith(".mat") && n.IndexOf("skybox_geometricblack_mat", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    RenderSettings.skybox = bundle.LoadAsset(n).TryCast<Material>();
                    return;
                }
            Plugin.Log.LogWarning("beta night bundle has no Skybox_GeometricBlack_MAT? block party keeps the round sky");
        }

        internal static void Build()
        {
            var scene = SceneManager.GetActiveScene();
            if (!Active || scene.name != SceneName || scene.handle == _built) return;
            _built = scene.handle;
            AssetManager.Instance.StartCoroutine(BuildWhenReady(scene.handle).WrapToIl2Cpp());
        }

        private static IEnumerator BuildWhenReady(int handle)
        {
            yield return OnlineData.Fetch(Package).WrapToIl2Cpp();
            float give = Time.realtimeSinceStartup + 60f;
            while (!OnlineData.Ready(Package) && Time.realtimeSinceStartup < give) yield return null;
            var scene = SceneManager.GetActiveScene();
            if (scene.handle != handle || !Active) yield break;
            string dir = OnlineData.Dir(Package);
            if (!File.Exists(Path.Combine(dir, "floor.txt"))) { Plugin.Log.LogWarning($"{Package} never landed, block party stays stock this time"); yield break; }

            if (_beta == null) LoadMeshes(dir);
            var basis = AssetLoads.Get<Material>(PinkEmissive);
            if (_blocks == null)
            {
                _blocks = Glow(basis, Tex(dir, "ENV_WallBlocksEmissive_AM", false), Color.white, new Color(1.2f, 1.2f, 1.2f, 1f), new Vector4(0.5f, 1f, 1f, 0f), true);
                _blocks.SetTexture("_MetallicSmoothness", Tex(dir, "ENV_WallBlocksEmissive_MM", true));
                _yellow = Glow(basis, null, new Color(1f, 0.75f, 0f, 1f), new Color(2f, 0.839f, 0f, 1f), new Vector4(0f, 1f, 1.5f, 0f), false);
            }

            int walls = 0;
            foreach (var key in Segments)
            {
                var prefab = AssetLoads.Get<GameObject>(key);
                if (prefab != null) walls += Walls(prefab.transform, true);
            }
            foreach (var top in scene.GetRootGameObjects()) walls += Walls(top.transform, false);

            Platform(scene, dir, basis);
            Plugin.Log.LogInfo($"beta block party is up, {walls} wall pieces swapped");
        }

        private static int Walls(Transform root, bool remember)
        {
            int n = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var cur = mf.sharedMesh;
                if (cur == null || cur.name.StartsWith("BettrFG_")) continue;
                bool trip = cur.name.StartsWith("ENV_BLO_Trip_");
                if (!trip && !cur.name.StartsWith("ENV_BLO_Wall_")) continue;
                var r = mf.GetComponent<MeshRenderer>();
                if (remember) _swapped.Add((mf, cur, r.sharedMaterials));
                bool pink = r.sharedMaterials.Length > 0 && r.sharedMaterials[0] != null && r.sharedMaterials[0].name.Contains("Purple");
                mf.sharedMesh = Fit(cur, trip, pink, mf.transform.lossyScale.y);
                r.sharedMaterials = new Il2CppReferenceArray<Material>(new[] { trip ? _yellow : _blocks });
                n++;
            }
            return n;
        }

        private static Mesh Fit(Mesh cur, bool trip, bool pink, float lossyY)
        {
            var cb = cur.bounds;
            float len = Mathf.Max(cb.size.x, cb.size.z);
            string best = null; float gap = float.MaxValue;
            foreach (var kv in _beta)
            {
                if (kv.Key.StartsWith("ENV_Wall_Low_") != trip) continue;
                if (!trip && kv.Key.EndsWith("_pink") != pink) continue;
                float d = Mathf.Abs(kv.Value.bounds.size.x - len);
                if (d < gap) { gap = d; best = kv.Key; }
            }
            float tall = (trip ? 0.75f : 4.1f) / lossyY;
            string id = cur.name + "|" + best + "|" + tall.ToString("F3", CultureInfo.InvariantCulture);
            if (_fitted.TryGetValue(id, out var done)) return done;

            var src = _beta[best];
            int copies = trip ? 1 : 2;
            var sv = src.vertices; var st = src.uv; var sn = src.normals; var sf = src.triangles;
            var v = new Vector3[sv.Length * copies]; var t = new Vector2[v.Length]; var nr = new Vector3[v.Length]; var f = new int[sf.Length * copies];
            for (int c = 0; c < copies; c++)
            {
                for (int i = 0; i < sv.Length; i++) { v[c * sv.Length + i] = sv[i] + new Vector3(0f, 2f * c, 0f); t[c * sv.Length + i] = st[i]; nr[c * sv.Length + i] = sn[i]; }
                for (int i = 0; i < sf.Length; i++) f[c * sf.Length + i] = sf[i] + c * sv.Length;
            }
            Vector3 mn = v[0], mx = v[0];
            foreach (var p in v) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
            bool alongZ = cb.size.z > cb.size.x;
            float depth = alongZ ? cb.size.x : cb.size.z;
            for (int i = 0; i < v.Length; i++)
            {
                var p = v[i];
                var q = new Vector3((p.x - mn.x) / (mx.x - mn.x) * len - len / 2f, (p.y - mn.y) / (mx.y - mn.y) * tall + cb.min.y, (p.z - mn.z) / (mx.z - mn.z) * depth - depth / 2f);
                if (alongZ) { q = new Vector3(q.z, q.y, q.x); nr[i] = new Vector3(nr[i].z, nr[i].y, nr[i].x); }
                v[i] = q + new Vector3(cb.center.x, 0f, cb.center.z);
            }
            if (alongZ) for (int i = 0; i < f.Length; i += 3) (f[i + 1], f[i + 2]) = (f[i + 2], f[i + 1]);

            var m = new Mesh { name = "BettrFG_" + cur.name };
            m.vertices = v; m.uv = t; m.normals = nr; m.triangles = f;
            m.RecalculateBounds(); m.RecalculateTangents();
            return _fitted[id] = m;
        }

        private static void Platform(Scene scene, string dir, Material basis)
        {
            Transform env = null;
            foreach (var top in scene.GetRootGameObjects()) if (top.name == "----------------ENVIRONMENT") env = top.transform;
            var stat = env.Find("STATIC(All environment in here will be lightmap)");
            stat.Find("ENV_BLO_Platform/ENV_BLO_Platform 1").GetComponent<MeshRenderer>().enabled = false;
            stat.Find("ENV_BLO_Structure").GetComponent<MeshRenderer>().enabled = false;

            var root = new GameObject("BettrFG_BetaBlockParty").transform;
            SceneManager.MoveGameObjectToScene(root.gameObject, scene);
            root.position = PlatformPos;
            var fit = new GameObject("Fit").transform;
            fit.SetParent(root, false);
            fit.localScale = PlatformScale;

            var floor = Glow(basis, Tex(dir, "ENV_BlockParty_Floor_Lit", false), Color.white, Color.white, new Vector4(0.6f, 1f, 1f, 0f), true);
            var rim = new Material(basis) { name = "BettrFG_BetaInflatable" };
            rim.SetTexture("_MainTex", Tex(dir, "ENV_BlockParty_InflatableWall_AM", false));
            rim.SetTexture("_MetallicSmoothness", Tex(dir, "ENV_BlockParty_InflatableWall_MM", true));
            rim.SetColor("_Color", Color.white);
            rim.SetColor("_RimColor", new Color(0.311f, 0.072f, 0.184f, 1f));
            rim.SetFloat("_UseEmissive", 0f);
            rim.DisableKeyword("_USEEMISSIVE_ON");

            Piece(fit, "ENV_BlockParty_Floor", ReadMesh(dir, "floor"), floor);
            Piece(fit, "ENV_BlockParty_InflatableWall", ReadMesh(dir, "inflatablewall"), rim);
            Piece(fit, "ENV_LightBar", ReadMesh(dir, "lightbar"), basis);
            Piece(fit, "ENV_LightBar (2)", ReadMesh(dir, "lightbar2"), basis);

            var smoke = UnityEngine.Object.Instantiate(AssetLoads.Get<GameObject>(Smoke), fit, false);
            smoke.name = "Prefab_VFX_BlockPartySmoke";
            foreach (var c in smoke.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
            smoke.transform.localPosition = new Vector3(0f, -1f, 0f);
        }

        private static void Piece(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = -BetaCenter;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static void PutBack()
        {
            foreach (var s in _swapped)
                if (s.Filter != null)
                {
                    s.Filter.sharedMesh = s.Mesh;
                    s.Filter.GetComponent<MeshRenderer>().sharedMaterials = s.Mats;
                }
            _swapped.Clear();
        }

        private static void LoadMeshes(string dir)
        {
            _beta = new Dictionary<string, Mesh>();
            foreach (var path in Directory.GetFiles(dir, "ENV_Wall_*.txt"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                _beta[name] = ReadMesh(dir, name);
            }
        }

        private static Mesh ReadMesh(string dir, string name)
        {
            var v = new List<Vector3>(); var t = new List<Vector2>(); var n = new List<Vector3>(); var f = new List<int>();
            var ci = CultureInfo.InvariantCulture;
            foreach (var line in File.ReadAllLines(Path.Combine(dir, name + ".txt")))
            {
                var p = line.Split(' ');
                switch (p[0])
                {
                    case "v": v.Add(new Vector3(float.Parse(p[1], ci), float.Parse(p[2], ci), float.Parse(p[3], ci))); break;
                    case "t": t.Add(new Vector2(float.Parse(p[1], ci), float.Parse(p[2], ci))); break;
                    case "n": n.Add(new Vector3(float.Parse(p[1], ci), float.Parse(p[2], ci), float.Parse(p[3], ci))); break;
                    case "f": f.Add(int.Parse(p[1])); f.Add(int.Parse(p[2])); f.Add(int.Parse(p[3])); break;
                }
            }
            var m = new Mesh { name = "BettrFG_Beta_" + name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.vertices = v.ToArray(); m.uv = t.ToArray(); m.normals = n.ToArray(); m.triangles = f.ToArray();
            m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        private static Texture2D Tex(string dir, string name, bool linear)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear) { name = "BettrFG_" + name };
            tex.LoadImage(File.ReadAllBytes(Path.Combine(dir, name + ".png")));
            return tex;
        }

        private static Material Glow(Material basis, Texture2D tex, Color col, Color emission, Vector4 minMaxPower, bool fromAlbedo)
        {
            var m = new Material(basis) { name = "BettrFG_BetaGlow" };
            m.SetTexture("_MainTex", tex);
            m.SetColor("_Color", col);
            m.SetFloat("_UseEmissive", 1f);
            m.SetFloat("_AlbedoToggle", fromAlbedo ? 1f : 0f);
            m.EnableKeyword("_USEEMISSIVE_ON");
            m.SetColor("_EmissionColor", emission);
            m.SetVector("_EmissiveMinMaxPower", minMaxPower);
            return m;
        }
    }
}
