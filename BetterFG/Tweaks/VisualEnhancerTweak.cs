using System;
using System.Collections.Generic;
using System.Globalization;
using BetterFG.Customization.Round;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;

namespace BetterFG.Tweaks
{
    public class VisualEnhancerTweak : BfgTweak
    {
        public VisualEnhancerTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "visual_enhancer";
        public override string TweakLabel => "tweak.visual_enhancer";
        public override string TweakTooltip => "tweak.visual_enhancer.tip";

        public static VisualEnhancerTweak Instance { get; private set; }
        void Awake() => Instance = this;

        private const string AuroraSky = "Skybox/Materials/Skybox_S03_Winter_MAT.mat";

        private static readonly HashSet<string> WinterRounds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FallGuy_ChickenChase_01", "FallGuy_Snowy_Scrap", "FallGuy_SnowballSurvival", "FallGuy_ThinIce",
            "FallGuy_Gauntlet_05", "FallGuy_IceClimb_01", "FallGuy_SkeeFall",
        };

        private static readonly HashSet<string> WinterPackages = new HashSet<string> { "SnowyScrap", "ThinIce", "TundraRun" };

        private static readonly string[] Rows =
        {
            "FallGuy_ShortCircuit2|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/ENV_SY01_SPEED_Art/BottomRect/Misc/PB_SY01_Jumbotron_rethemed|1",
            "FallGuy_ShortCircuit2|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/ENV_SY01_SPEED_Art/Loop/Misc/PB_SY01_Jumbotron_rethemed (2)|1",
            "FallGuy_ShortCircuit2|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/ENV_SY01_SPEED_Art/180Curve/Misc/PB_SY01_Jumbotron_rethemed (1)|1",
            "FallGuy_ShortCircuit2|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/EnvironmentLiveEvent/Jumbo/PB_ELE_ShortCircuit2_01_Jumbo/LiveEventObjects|1",
            "FallGuy_ShortCircuit|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/EnvironmentLiveEvent/Jumbo/PB_ELE_ShortCircuit_01_Jumbo/LiveEventObjects|1",
            "FallGuy_PumpkinPie|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/PB_FallGuy_BackGround_Goop_SHalloween/Balloons|1",
            "FallGuy_PumpkinPie|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/PB_FallGuy_BackGround_Goop_SHalloween/Zombie_Hands/ENV_Shalloween_Zombiehand_Skirt_MO (1)|1",
            "FallGuy_PumpkinPie|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/PB_FallGuy_BackGround_Goop_SHalloween/Zombie_Hands/ENV_Shalloween_DirtPile_MO|1",
            "FallGuy_PumpkinPie|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/PB_FallGuy_BackGround_Goop_SHalloween/Zombie_Hands/ENV_Shalloween_DirtPile_MO (1)|1",
            "FallGuy_PumpkinPie|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/PB_FallGuy_BackGround_Goop_SHalloween/Zombie_Hands/ENV_SHalloween_ZombieHand_01a_MO|1",
            "FallGuy_PumpkinPie|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/PB_FallGuy_BackGround_Goop_SHalloween/Zombie_Hands/ENV_SHalloween_ZombieHand_01b_MO|1",
            "FallGuy_PumpkinPie|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/PB_FallGuy_BackGround_Goop_SHalloween/Zombie_Hands/ENV_SHalloween_ZombieHand_01b_MO (1)|1",
            "FallGuy_PumpkinPie|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/PB_FallGuy_BackGround_Goop_SHalloween/Zombie_Hands/ENV_Shalloween_DirtPile_MO (2)|1",
            "FallGuy_BlastBallRuins|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/ENV_S09_BBR_Background_Ruins|1",
            "FallGuy_Gauntlet_02_01|----------------ENVIRONMENT /STATIC(All environment in here will be lightmap)/EnvironmentLiveEvent/Jumbo/PB_ELE_Gauntlet_02_Jumbo/LiveEventObjects|1",
            "FallGuy_SeeSaw_variant2|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/EnvironmentLiveEvent/Jumbo/PB_ELE_SeeSaw_01_Jumbo/LiveEventObjects|1",
            "FallGuy_FallMountain_Hub_Complete|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/ENV_Decal_MO|1",
            "FallGuy_FallMountain_Hub_Complete|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/ENV_Scaffolding_01_MO|1",
            "FallGuy_FallMountain_Hub_Complete|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/ENV_rainbowSide_MO (1)|1",
            "FallGuy_FallMountain_Hub_Complete|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/ENV_rainbowSide_MO (2)|1",
            "FallGuy_FallMountain_Hub_Complete|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/ENV_decalsChecker_MO|1",
            "FallGuy_FallMountain_Hub_Complete|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/PB_FallMountain_Decals|0",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (0)/ENV_SY01_VLLF_Art/ENV_SY01_VLLF_Scaffolding|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (0)/ENV_SY01_VLLF_Art/ENV_SY01_VLLF_Scaffolding (1)|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (1)/ENV_SY01_VLLF_Art/ENV_SY01_VLLF_Scaffolding|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (1)/ENV_SY01_VLLF_Art/ENV_SY01_VLLF_Scaffolding (1)|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (2)/ENV_SY01_VLLF_Art/ENV_SY01_VLLF_Scaffolding|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (2)/ENV_SY01_VLLF_Art/ENV_SY01_VLLF_Scaffolding (1)|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (3)/ENV_SY01_VLLF_Art/ENV_SY01_VLLF_Scaffolding|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (3)/ENV_SY01_VLLF_Art/ENV_SY01_VLLF_Scaffolding (1)|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (4)/ENV_SY01_VLLF_Art/ENV_SY01_VLLF_Scaffolding|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (4)/ENV_SY01_VLLF_Art/ENV_SY01_VLLF_Scaffolding (1)|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (5)/ENV_SY01_VLLF_Art/ENV_SY01_VLLF_Scaffolding|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (5)/ENV_SY01_VLLF_Art/ENV_SY01_VLLF_Scaffolding (1)|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (6)/ENV_SY01_VLLF_Art/ENV_SY01_VLLF_Scaffolding|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (6)/ENV_SY01_VLLF_Art/ENV_SY01_VLLF_Scaffolding (1)|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (7)/ENV_SY01_VLLF_Art/ENV_SY01_VLLF_Scaffolding|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (7)/ENV_SY01_VLLF_Art/ENV_SY01_VLLF_Scaffolding (1)|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (8)/ENV_SY01_VLLF_Art/ENV_SY01_VLLF_Scaffolding|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (8)/ENV_SY01_VLLF_Art/ENV_SY01_VLLF_Scaffolding (1)|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (9)/ENV_SY01_VLLF_Art/ENV_SY01_VLLF_Scaffolding|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (9)/ENV_SY01_VLLF_Art/ENV_SY01_VLLF_Scaffolding (1)|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (0)/ENV_SY01_VLLF_MovableWalls_T1_MO|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (0)/ENV_SY01_VLLF_MovableWalls_T2_MO|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (1)/ENV_SY01_VLLF_MovableWalls_T1_MO|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (1)/ENV_SY01_VLLF_MovableWalls_T2_MO|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (2)/ENV_SY01_VLLF_MovableWalls_T1_MO|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (2)/ENV_SY01_VLLF_MovableWalls_T2_MO|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (3)/ENV_SY01_VLLF_MovableWalls_T1_MO|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (3)/ENV_SY01_VLLF_MovableWalls_T2_MO|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (4)/ENV_SY01_VLLF_MovableWalls_T1_MO|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (4)/ENV_SY01_VLLF_MovableWalls_T2_MO|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (5)/ENV_SY01_VLLF_MovableWalls_T1_MO|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (5)/ENV_SY01_VLLF_MovableWalls_T2_MO|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (6)/ENV_SY01_VLLF_MovableWalls_T1_MO|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (6)/ENV_SY01_VLLF_MovableWalls_T2_MO|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (7)/ENV_SY01_VLLF_MovableWalls_T1_MO|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (7)/ENV_SY01_VLLF_MovableWalls_T2_MO|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (8)/ENV_SY01_VLLF_MovableWalls_T1_MO|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (8)/ENV_SY01_VLLF_MovableWalls_T2_MO|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (9)/ENV_SY01_VLLF_MovableWalls_T1_MO|1",
            "FallGuy_1v1_Volleyfall|----------------GAMEPLAY/PB_Volleyfall_Arena_Smaller (9)/ENV_SY01_VLLF_MovableWalls_T2_MO|1",
            "FallGuy_BiggestFan|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/EnvironmentLiveEvent/Jumbo/PB_ELE_BiggestFan_01_Jumbo/LiveEventObjects|1|-25.0709,112.2389,0",
            "FallGuy_DoorDash|----------------ENVIRONMENT/EnvironmentLiveEvent/Jumbo/PB_ELE_DoorDash_01_Jumbo/LiveEventObjects|1|80.236,134.019,-178.509",
            "FallGuy_FallBall_5|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/EnvironmentLiveEvent/Jumbo/PB_ELE_FallBall_Jumbo/LiveEventObjects|1|0,13.826,0",
            "FallGuy_IceClimb_01|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/EnvironmentLiveEvent/Jumbo/PB_ELE_IceClimb_01_Jumbo/LiveEventObjects|1|0,92.4,0",
            "FallGuy_DodgeFall|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/EnvironmentLiveEvent/Jumbo/PB_ELE_DodgeFall_01_Jumbo/LiveEventObjects|1|0,-92,0",
            "FallGuy_SeeSaw360|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/EnvironmentLiveEvent/Jumbo/PB_ELE_SeeSaw360_01_Jumbo/LiveEventObjects|1|0,39.2,0",
            "FallGuy_ChompChomp_01|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/EnvironmentLiveEvent/Jumbo/PB_ELE_ChompChomp_01_Jumbo/LiveEventObjects|1",
            "FallGuy_FloorFall|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/Structure_Hex_Walls (2)|1",
            "FallGuy_FloorFall|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/Structure_Hex_Floor (1)|1",
            "FallGuy_FloorFall|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/Cage|1",
            "FallGuy_FloorFall|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/Big Cage|1",
            "FallGuy_HexARing|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/EnvironmentLiveEvent/Jumbo/PB_ELE_HexARing_01_Jumbo/LiveEventObjects|1|23.398,62.731,-83.865",
            "FallGuy_Gauntlet_01|----------------ENVIRONMENT/EnvironmentLiveEvent/Jumbo/PB_ELE_Gauntlet_01_Jumbo/LiveEventObjects|1|0,87.064,0",
            "FallGuy_Hoops_01|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/EnvironmentLiveEvent/Jumbo/PB_ELE_Hoops_Jumbo/LiveEventObjects|1|0,-114.218,0",
            "FallGuy_Hoops_Blockade|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/EnvironmentLiveEvent/Jumbo/PB_ELE_HoopsBlockade_01_Jumbo/LiveEventObjects|1",
            "FallGuy_TeamInfected|----------------ENVIRONMENT/STATIC(All environment in here will be lightmap)/EnvironmentLiveEvent/Jumbo/PB_ELE_TeamInfected_Jumbo/LiveEventObjects|1",
        };

        private struct Touched { public GameObject Go; public bool Active; public Vector3 Pos; }

        private static readonly List<Touched> _touched = new List<Touched>();
        private static Material _aurora, _skyBefore;
        private static string _round;
        private static bool _hostWinter, _winter;

        public override void EnableTweak()
        {
            if (_round != null && SceneManager.GetActiveScene().name == _round) ApplyObjects();
            if (_winter) ApplySky();
        }

        public override void DisableTweak()
        {
            foreach (var t in _touched)
            {
                if (t.Go == null) continue;
                Desync(t.Go, false);
                t.Go.SetActive(t.Active);
                t.Go.transform.localPosition = t.Pos;
            }
            _touched.Clear();
            if (_aurora != null && RenderSettings.skybox == _aurora) RenderSettings.skybox = _skyBefore;
        }

        internal static void OnBackground(BackgroundKind kind, string id, GameObject root)
        {
            switch (kind)
            {
                case BackgroundKind.Round:
                    _round = id;
                    _touched.Clear();
                    _winter = _hostWinter = WinterRounds.Contains(id);
                    break;
                case BackgroundKind.Theme:
                    _round = null;
                    _winter = _hostWinter = id.StartsWith("Background_Winter") || id.StartsWith("Background_S03");
                    break;
                case BackgroundKind.Package:
                    _winter = WinterPackages.Contains(id);
                    break;
                case BackgroundKind.PackageGone:
                    _winter = _hostWinter;
                    return;
            }

            if (Instance == null || !Instance.IsEnabled) return;
            if (kind == BackgroundKind.Round) ApplyObjects();
            if (_winter) ApplySky();
        }

        private static void ApplySky()
        {
            if (_aurora == null) _aurora = Addressables.LoadAssetAsync<Material>(AuroraSky).WaitForCompletion();
            if (RenderSettings.skybox == _aurora) return;
            _skyBefore = RenderSettings.skybox;
            RenderSettings.skybox = _aurora;
            Plugin.Log.LogInfo($"aurora up over {(_skyBefore != null ? _skyBefore.name : "no sky")}");
        }

        private static void ApplyObjects()
        {
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            int done = 0, missing = 0;
            foreach (var row in Rows)
            {
                var p = row.Split('|');
                if (p[0] != scene.name) continue;
                int slash = p[1].IndexOf('/');
                string rootName = p[1].Substring(0, slash);
                Transform t = null;
                foreach (var r in roots)
                    if (r.name == rootName) { t = r.transform.Find(p[1].Substring(slash + 1)); break; }
                if (t == null) { missing++; continue; }

                _touched.Add(new Touched { Go = t.gameObject, Active = t.gameObject.activeSelf, Pos = t.localPosition });
                t.gameObject.SetActive(p[2] == "1");
                Desync(t.gameObject, true);
                if (p.Length > 3)
                {
                    var v = p[3].Split(',');
                    t.localPosition = new Vector3(float.Parse(v[0], CultureInfo.InvariantCulture), float.Parse(v[1], CultureInfo.InvariantCulture), float.Parse(v[2], CultureInfo.InvariantCulture));
                }
                done++;
            }
            if (done + missing == 0) return;
            if (missing > 0) Plugin.Log.LogWarning($"visual enhancer: {missing} of {done + missing} {scene.name} objects weren't where the table says, game update?");
            else Plugin.Log.LogInfo($"{scene.name}: {done} unused bits switched back on");
        }

        private static void Desync(GameObject go, bool on)
        {
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.shader.name.Contains("PixelDisplay")) continue;
                    if (!on) { r.SetPropertyBlock(null, i); continue; }
                    var b = new MaterialPropertyBlock();
                    b.SetVector("_MainTexOffset", m.GetVector("_MainTexOffset") + new Vector4(0f, UnityEngine.Random.value, 0f, 0f));
                    b.SetFloat("_ScrollYSpeed", m.GetFloat("_ScrollYSpeed") * UnityEngine.Random.Range(0.4f, 2.5f));
                    b.SetFloat("_PulseSpeed", m.GetFloat("_PulseSpeed") * UnityEngine.Random.Range(0.5f, 2f));
                    b.SetFloat("_DimSpeed", m.GetFloat("_DimSpeed") * UnityEngine.Random.Range(0.4f, 2.5f));
                    r.SetPropertyBlock(b, i);
                }
            }
        }
    }
}
