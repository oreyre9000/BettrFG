using System.Collections.Generic;
using BetterFG.Features.CreativeExpansion;
using BetterFG.Tweaks;
using BetterFG.Utilities;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BetterFG.Customization.Round
{
    internal static class BetaDoorDash
    {
        private const string SceneName = "FallGuy_DoorDash";
        private const float Chance = 0.001f;
        private const string Bundle = "terrain_beta_day";
        private const string MainKey = "3712ac044da95ef499a666e1212ff93a";
        private const float FrontStep = 42.95f;

        private const string Rail = "a412118bab414224999a8cdcd185db9b";
        private const string Post = "348040afc12d83a47b611de19f7474c9";
        private const string GoldDecals = "9c31c5fe6a48d3b449648ffeab2c7ed8";
        private const string DoorTop = "749ee0a293821c94fbd7609af924ba22";
        private const string GroundPlates = "5b863827aae573e49afa0f4de8f2d304";
        private const string GroundPlain = "ea25a8b8fe071eb44bd9f557b1109d49";
        private const string GroundJoints = "f5ce817f6ea5d674aad00f9f683692f4";

        private static readonly (string Prefix, string Guid)[] Paints =
        {
            ("decal_checker", "c7aec99f2a85cd14c9306e1c4a74240c"),
            ("sideCircles01", "30464215de3e996429011f93a1c80577"),
            ("sideCircles02", "2774099647ea15440853616b09ad3f41"),
            ("sideCircles03", "a65fa549893f33645b169cc5566759ac"),
            ("sideCircles04", "ee719d40258c5734fa8f14435bd26eee"),
            ("sideCircles05", "487a5e30a51808a4cbbcc85ecfad85c3"),
            ("sidePillar", "a1715933d2a031140841a9e3f677f361"),
            ("vines", "374caa85e1bfbbf4bb29d30322e84a4f"),
            ("FallWall", "ca1568c62645e2f4786148adddf7b086"),
            ("InflatedArch", "024931ca6a9f1cc4f8f37372fadf526b"),
        };

        private static readonly Vector3 MainPos = new Vector3(43.5f, 2.45f, 44.7f);
        private static readonly Vector3 TopShift = new Vector3(0f, -0.25f, 0.3f);

        internal static readonly Vector3 TerrainPos = new Vector3(-3.6785f, 136.9314f, 105.5914f);

        internal static bool Active;
        private static int _built = -1;
        private static Material _chevron;

        internal static void Roll(string scene) => Active = scene == SceneName && UnityEngine.Random.value <= Chance;

        internal static bool Owns(string scene) => Active && scene == SceneName;

        internal static string Pick(string scene) => Owns(scene) && Background3dTweak.Have(Bundle) ? "bettrfg:" + Bundle : null;

        internal static string OnReplayLoaded(bool recorded, string pick)
        {
            Active = recorded && SceneManager.GetActiveScene().name == SceneName;
            if (!Active) return pick;
            Build();
            return pick == "bettrfg:" + Bundle && !Background3dTweak.Have(Bundle) ? "" : pick;
        }

        internal static void Lighting(string scene)
        {
            if (!Owns(scene)) return;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(1f, 0.86f, 0.5f);
            RenderSettings.ambientEquatorColor = new Color(0.98f, 0.82f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.92f, 0.76f, 0.42f);
            RenderSettings.ambientIntensity = 1.3f;
            RenderSettings.sun.shadowStrength = 0.7f;
        }

        internal static void Build()
        {
            var scene = SceneManager.GetActiveScene();
            if (!Active || scene.name != SceneName || scene.handle == _built) return;
            _built = scene.handle;

            Transform env = null, level = null;
            foreach (var top in scene.GetRootGameObjects())
            {
                if (top.name == "----------------ENVIRONMENT") env = top.transform;
                else if (top.name == "----------------SYSTEMS/MANAGERS") level = top.transform.Find("PB_LevelVariation_DoorDash");
            }
            var background = env.Find("PB_FallGuy_BackGround_Terrain");
            var start = env.Find("PB_StartingPositions_Large");
            foreach (var r in env.GetComponentsInChildren<MeshRenderer>(true))
                if (!r.transform.IsChildOf(background) && !r.transform.IsChildOf(start)) r.enabled = false;

            var root = new GameObject("BettrFG_BetaDoorDash");
            SceneManager.MoveGameObjectToScene(root, scene);
            World(root.transform);

            int doors = 0;
            var original = level.Find("SetSwitchers/Doors/Original");
            for (int s = 0; s < original.childCount; s++)
            {
                var set = original.GetChild(s);
                DoorDashArt.Apply(set, DoorDashArt.Beta, true);
                for (int d = 0; d < set.childCount; d++, doors++) DoorDashArt.Dress(set.GetChild(d));
            }
            Plugin.Log.LogInfo($"beta door dash is up, {doors} doors restyled");
        }

        private static void World(Transform root)
        {
            var main = UnityEngine.Object.Instantiate(AssetLoads.Get<GameObject>(MainKey), root);
            main.name = "BettrFG_BetaMain";
            foreach (var c in main.GetComponentsInChildren<Behaviour>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in main.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in main.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(c);
            main.transform.position = MainPos;

            foreach (var mf in main.GetComponentsInChildren<MeshFilter>(true))
            {
                var paint = PaintFor(mf.sharedMesh);
                if (paint == null) continue;
                var mats = new Il2CppReferenceArray<Material>(Mathf.Max(1, mf.sharedMesh.subMeshCount));
                for (int i = 0; i < mats.Length; i++) mats[i] = paint;
                mf.GetComponent<MeshRenderer>().sharedMaterials = mats;
            }

            var doorGroups = new List<Transform>();
            for (int i = 0; i < main.transform.childCount; i++)
                if (main.transform.GetChild(i).name.StartsWith("Door_")) doorGroups.Add(main.transform.GetChild(i));
            foreach (var g in doorGroups) g.gameObject.SetActive(false);

            var rows = new List<MeshRenderer>();
            foreach (var r in main.GetComponentsInChildren<MeshRenderer>(false))
                if (r.bounds.min.z >= -74f && r.bounds.max.z <= -26f) rows.Add(r);
            for (int k = 1; k <= 2; k++)
            {
                var front = new GameObject("BettrFG_BetaFront" + k).transform;
                front.SetParent(root, false);
                foreach (var r in rows) Clone(r, front, new Vector3(0f, 0f, -FrontStep * k));
            }

            foreach (var g in doorGroups)
            {
                g.gameObject.SetActive(true);
                foreach (var r in g.GetComponentsInChildren<MeshRenderer>(true))
                {
                    bool top = r.GetComponent<MeshFilter>().sharedMesh.name.Contains("top");
                    r.enabled = top;
                    if (!top) continue;
                    r.transform.position += TopShift;
                    if (g.name != "Door_A") continue;
                    for (int k = 1; k <= 2; k++) Clone(r, root, new Vector3(0f, 0f, -FrontStep * k));
                }
            }
        }

        private static Material PaintFor(Mesh mesh)
        {
            string n = mesh.name;
            var size = mesh.bounds.size;
            if (n.StartsWith("bouncyWalls")) return AssetLoads.Get<Material>(Mathf.Max(size.x, size.z) < 4f ? Post : Rail);
            if (n.StartsWith("decals_arrows"))
            {
                if (_chevron == null)
                {
                    _chevron = new Material(AssetLoads.Get<Material>(GoldDecals));
                    _chevron.SetFloat("_SmoothnessRange", 0f);
                }
                return _chevron;
            }
            if (n.Contains("_top02")) return AssetLoads.Get<Material>(DoorTop);
            if (n.Length > 6 && n.StartsWith("ground") && char.IsDigit(n[6]))
            {
                bool plate = size.x > 1f && size.z > 1f && n != "ground07" && n != "ground10";
                return AssetLoads.Get<Material>(plate ? GroundPlates : size.x < 0.5f || size.z < 0.5f ? GroundJoints : GroundPlain);
            }
            foreach (var p in Paints)
                if (n.StartsWith(p.Prefix)) return AssetLoads.Get<Material>(p.Guid);
            return null;
        }

        private static void Clone(MeshRenderer from, Transform parent, Vector3 shift)
        {
            var go = new GameObject(from.name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(from.transform.position + shift, from.transform.rotation);
            go.transform.localScale = from.transform.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = from.GetComponent<MeshFilter>().sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = from.sharedMaterials;
        }
    }
}
