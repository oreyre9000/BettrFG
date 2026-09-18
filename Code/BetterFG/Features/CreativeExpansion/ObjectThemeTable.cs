namespace BetterFG.Features.CreativeExpansion
{
    internal sealed class ObjectTheme
    {
        public string LocKey;
        public string[] XSmall;
        public string[] Small;
        public string[] Medium;
        public string[] Large;
        public string[] XLarge;
        public string[] Materials;
        public bool MirrorX;
        public float RotateX;
        public float RotateZ;
        // Uniform multiplier for season art authored at a different size to the creative object.
        public float Scale;
        // Material for when the game has flipped the object (a conveyor running backwards).
        public string Reverse;
        public string Attach;
    }

    internal enum ObjectThemeFit
    {
        None,
        Footprint,
        Hanging,
        // Same silhouette, different origin: match the art's bounds centre to the stock's and don't
        // scale. The season see saws and bridge decks are authored metres apart in Y.
        Center,
        // Part whose size runs along Z, such as a rotating arm: scale to the stock part's length.
        Length,
    }

    internal sealed class ObjectThemeFamily
    {
        public string[] Prefixes;
        public string[] Nodes;
        public int Extras;
        // Index into Nodes that extra parts are parented alongside. Defaults to the first node.
        public int ExtrasNode;
        public ObjectThemeFit Fit;
        public bool Drawable;
        // The game rewrites this family's materials itself, so watch for drift and re-apply.
        public bool Volatile;
        public bool Balloon;
        public ObjectTheme[] Themes;
    }

    internal static class ObjectThemeTable
    {
        internal const string Hidden = "-";

        private const string RetroObstacles = "39e4480005d45ad49993485d2d264be0";
        private const string RetroPassive = "9a47fba1895dcbc45a6c851eed29b544";
        private const string RetroHardPlastic = "8dee5bfe26a0d4746871536d50b87681";

        private const string RetroPunchFbx = "96707e723ac127a4d93f6753eb871d69";
        private const string PartyPunchFbx = "796a099d425409c44acb55ac147ea61f";
        private const string SunkenPunchFbx = "a4e956f56fdca914cabc9ed0aab2fde6";
        private const string StadiumPunchFbx = "1c65d23d35bf46648924cf213dcf518d";
        private const string SpacePunchFbx = "83d9c67e631ade742a957605fa1941a6";
        private const string RetroDrawbridgeFbx = "9777ad3e9ac40624593250e045c8d4d5";
        private const string RetroTurntableFbx = "8c7afc2bce48f114a8f1cdbc6c62c9c0";
        private const string JungleTurntableFbx = "d374977a5f604c14d9ce94e2dc7988d2";
        private const string PartyTurntableFbx = "c3704cb8ccfc63b4d8ea6146275a0489";
        private const string StadiumTurntableFbx = "03ed9241211a7c14b85b51309d3fdc43";
        private const string SpaceTurntableFbx = "2d17b86e754c6504c8ea3457170c914b";
        private const string PartyPlatformFbx = "9d75772408f9db54fa4124aa5f33923c";
        private const string SunkenTurntableFbx = "91867dce832c2d74b8b3ef3553162953";
        private const string PartyDrawbridgeFbx = "be3ba5d90a6e9314fa8331bc676a75ae";
        private const string SunkenBumperShapes = "dfb2432111d635a419f1808d4d4ea72f";
        private const string ForceFieldPostsS01 = "0dd0e060f90e358488347480b5183efd";
        private const string ForceFieldPostsS04 = "78e16b690feeecd4eba5f8e5ffd11583";
        private const string JungleArmFbx = "81bdb71f53124814fbf6e139e9a1b85c";
        private const string StadiumArmFbx = "f9e7ea037bd0c23459995fdb659d1d4d";
        private const string SunkenBarFbx = "d73f372a8523f07469fb868e51ce5192";
        private const string SonicArmFbx = "85b57ee208cd4fe4ead2335c9ad3d232";
        private const string WinterFanFbx = "1a1731e6c8daedb4e9e7813073882a9a";
        private const string SpaceFloorFanFbx = "ece8ffdedc9b9a84f93e5c2ca127e367";
        private const string JungleFanFbx = "d50eaf84f88b3764eb7fb8a9a6b31b3c";
        private const string SunkenFanFbx = "6dd0053f84a41e740bcd9c6503f6132f";
        private const string SpaceFanFbx = "8f81f90b38aa0784b951ff43a88a5f49";
        private const string RetroRotatingWall = "b1d6bb5d025db2e469e39980f5426324";
        private const string StadiumSpinDoor = "112335b26c9d0d4428610c3fbdc1e675";
        private const string JungleButton = "a1c4223170c64b445bb521ccac9eb052";
        private const string FutureButton = "688acc6f69ae6e242adf93676864fb25";
        private const string SunkenButton = "73918d06b5072b747bf9a50898c218aa";
        private const string WinterPunchFbx = "6064855578580df468b3489766905dec";
        private const string PartyMoveBoxFbx = "a8fbb958a8bd93549aa7faa29f324af4";
        private const string SwingLogS02 = "722954268ed07c44bb88915621bf3f3c";
        private const string SwingLogSY01 = "abad52eff41c1ee49aaba0c37fd06835";
        private const string SwingLogSY02 = "3f2a7a6e26c50b14fbfdadd7c55bfaf0";
        private const string SunkenFlipperFbx = "a413a2a351c74b640ad9c235d9619912";
        private const string RetroFlipperFbx = "ea1625df8d4a65a4fb0a37877db95eaa";
        private const string FruitBouncyShapes = "232bff8e75cf1a94594f1166e7b4c45e,33e95256b49b77e48b5756e1a1d53b92";
        private const string MedievalMovableProps =
            "16bb7c62ac7e029409e151f9ae3fc190,df4fcaecaf535e644ad07e7b1d1cae45," +
            "48e0ff8e48444594f80c0abdfdd74243,288b81f7c3c33ec45b556d056fe714f4";

        internal static readonly ObjectThemeFamily[] All =
        {
            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Drum_" },
                Nodes = new[] { "PB_S01_Drum_" },
                Fit = ObjectThemeFit.Footprint,
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.jungle",
                        Small = new[] { "18fc12aec1f772240a58f59b17f2ba05" },
                        Medium = new[] { "a3b8306cf7877964fb4a7cd5cd2b73ea" },
                        Materials = new[] { "b0bb27ed9d8638a458812489693775e2" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.party",
                        Small = new[] { "8f0542607d7bf1444b77ec7202d32cb4" },
                        Medium = new[] { "1febb6c9b326af3468d52b7a3adc7860" },
                        Materials = new[] { "831a61ab880fa9945b73d7765be4888b" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.retro",
                        Small = new[] { "c73f4c840bb06be4b92dbba594cb528b" },
                        Medium = new[] { "aab5df05d8c1dc0438afb2393bed6721" },
                        Materials = new[] { RetroObstacles + ",e17ee52d26b9a6c439bda598057d18f3" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.stadium",
                        Small = new[] { "14f6a33a3abbdd2448b53c80b35035a0" },
                        Medium = new[] { "6611f37759852ef47bc6ada564687113" },
                        Materials = new[] { "cc3e1b296c294114e9437a3c2a8d7c57" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.space",
                        Medium = new[] { "4a96d6fcc03358a49bf368e0ab0cb36c" },
                        Materials = new[] { "cec9c9fda5133454ca9033a0baf04254" },
                    },
                    new ObjectTheme
                    {
                        // The Sonic spring, from the Blue Jay collab round. Same footprint as the
                        // small drum, so the footprint fit sizes it to whichever drum it lands on.
                        LocKey = "objectthemes.sonic",
                        Medium = new[] { "3d6d63503352c9e49af5f3d0afc652cc" },
                        Materials = new[] { "2f7023f3891808a4186f6c7d4b74bdb7" },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Obstacle_SpinningHammer_" },
                Nodes = new[] { "Obstacle_SpinningHammer_" },
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.jungle",
                        Medium = new[] { "b04efbbe3c018a7428c880702e977295" },
                        Materials = new[] { "f4d313e882ede9c47b701a8bb2efc792" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.party",
                        Medium = new[] { "d1cf5e6321ff85349bdb9790556c7912" },
                        Materials = new[] { "327490b14e11991409f3ddd000e09d2d" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.sunken",
                        Medium = new[] { "5331e42b1dbb9e546a72047634708b57|ENV_S09_Hammer_MO" },
                        Materials = new[] { "1c271afc456f9354592c030cc3268cae" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.retro",
                        Medium = new[] { "f10cb92b4160b4742b3795e5cd774795" },
                        Materials = new[] { RetroObstacles },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.stadium",
                        Medium = new[] { "82cde09224da24d41a832f34d9703973" },
                        Materials = new[] { "78ee2b85cad38974ea8b8bf57627b628" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.space",
                        Medium = new[] { "0000d470dea4f8c418aef93db88e6794" },
                        Materials = new[] { "7b88664af145b2e4f9a97b92021f318b" },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Obstacle_Punch_" },
                Nodes = new[]
                {
                    "PRP_PunchingObstacle_MO",
                    "PRP_PunchingObstacle_Lid_MO",
                    "PRP_PunchingObstacle_Pistons_MO",
                    "PRP_PunchingObstacle_Punch_MO",
                },
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.party",
                        Medium = new[]
                        {
                            PartyPunchFbx + "|ENV_S06_PunchingObstacle_Box_MO",
                            PartyPunchFbx + "|ENV_S06_PunchingObstacle_Lid_MO",
                            PartyPunchFbx + "|ENV_S06_PunchingObstacle_Pistions_MO",
                            PartyPunchFbx + "|ENV_S06_PunchingObstacle_Glove_MO",
                        },
                        Materials = new[] { "89b5dbd3d7eee614eb68ed280711ab93" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.sunken",
                        Medium = new[]
                        {
                            SunkenPunchFbx + "|ENV_S09_PuncherBox_MO",
                            SunkenPunchFbx + "|ENV_S09_PuncherLid_MO",
                            SunkenPunchFbx + "|ENV_S09_Puncher_Pistions_MO",
                            SunkenPunchFbx + "|ENV_S09_PuncherGlove_MO",
                        },
                        Materials = new[] { "1eba8fc2339ea2a4e84e3afe0ebec939" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.retro",
                        Medium = new[]
                        {
                            RetroPunchFbx + "|ENV_S10_Retro_ScreenBox_01a_MO",
                            RetroPunchFbx + "|ENV_S10_Retro_Screen_01a_MO",
                            RetroPunchFbx + "|ENV_S10_Retro_Arm_01a_MO",
                            RetroPunchFbx + "|ENV_S10_Retro_Glove_01a_MO",
                        },
                        Materials = new[] { RetroObstacles },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.stadium",
                        Medium = new[]
                        {
                            StadiumPunchFbx + "|ENV_SY01_PunchingObstacle_Box_MO",
                            StadiumPunchFbx + "|ENV_SY01_PunchingObstacle_Lid_MO",
                            StadiumPunchFbx + "|ENV_SY01_PunchingObstacle_Pistions_MO",
                            StadiumPunchFbx + "|ENV_SY01_PunchingObstacle_Glove_MO",
                        },
                        Materials = new[] { "e43777e549734e542aea646989974989" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.winter",
                        Medium = new[]
                        {
                            WinterPunchFbx + "|PRP_PunchingObstacle_MO",
                            WinterPunchFbx + "|PRP_PunchingObstacle_Lid_MO",
                            WinterPunchFbx + "|PRP_PunchingObstacle_Pistons_MO",
                            WinterPunchFbx + "|PRP_PunchingObstacle_Punch_MO",
                        },
                        Materials = new[] { "c5985aef74cdd244cab84e00bbf4bc19,b699d98b3e98f9f4ca00eaefc1dc5053" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.space",
                        Medium = new[]
                        {
                            SpacePunchFbx + "|ENV_SY02_PunchingObstacle_Box_MO",
                            SpacePunchFbx + "|ENV_SY02_PunchingObstacle_Lid_MO",
                            SpacePunchFbx + "|ENV_SY02_PunchingObstacle_Pistons_MO",
                            SpacePunchFbx + "|ENV_SY02_PunchingObstacle_Glove_MO",
                        },
                        Materials = new[] { "64077a26f4d2be84b8738de499174a6c" },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Obstacle_TurnstileSingle_" },
                Nodes = new[] { "Obstacle_Turnstile" },
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.retro",
                        Medium = new[] { "7220e181b77f8984ea1313c4459f0211" },
                        Materials = new[] { RetroObstacles },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.halloween",
                        Materials = new[] { "351502dffc6db8c428a0db8601208964" },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Obstacle_TurnstileDouble_" },
                Nodes = new[] { "Obstacle_Turnstile" },
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.sunken",
                        Medium = new[] { "c7dc3b921531ff140b17edc104403640" },
                        Materials = new[] { "e7f77808583e47147b097aa52898c019" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.retro",
                        Medium = new[] { "7878704ff87a268489752061ccb58209" },
                        Materials = new[] { RetroObstacles },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.jungle",
                        Medium = new[] { "e9ab4ae0f8aa5e14698c40bb17376d76|ENV_S05_CC_Turnstile_Top_MO" },
                        Materials = new[] { "ce81f782288109b42935f1b64eb612c8" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.halloween",
                        Materials = new[] { "351502dffc6db8c428a0db8601208964" },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Obstacle_SwingingAxe_" },
                Nodes = new[] { "Obstacle_SwingingAxe_" },
                Fit = ObjectThemeFit.Hanging,
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.retro",
                        Medium = new[] { "787ff0276f2af274caad4df5361128bf" },
                        Materials = new[] { RetroObstacles },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.stadium",
                        Medium = new[] { "bb1ed19aa4f77f84d8b0f99c1b9641e4" },
                        Materials = new[] { "810990917c2de5242b8ad41fd1cddc13" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.halloween",
                        Materials = new[] { "a3ee6e6f933b7ef4b9d649ee103cc8f6" },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Obstacle_SwingingClub_" },
                Nodes = new[] { "Obstacle_SwingingClub_" },
                // Every season authors the log as barrel + arms in one fbx; the creative object is a
                // single node, so slot 1 is our own extra node carrying the fbx's own offset.
                Extras = 1,
                Fit = ObjectThemeFit.Hanging,
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.retro",
                        Medium = new[] { "70351fee613873b46b9f6678baf7d561", "" },
                        Materials = new[] { RetroObstacles },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.medieval",
                        Medium = new[]
                        {
                            SwingLogS02 + "|ENV_Swinging_Log_Handles_MO",
                            SwingLogS02 + "|ENV_Swinging_Log_MO||0,0,0",
                        },
                        Materials = new[] { "7a10292890ff8624381484527ff4c2b6" },
                    },
                    new ObjectTheme
                    {
                        // Rendered against the stock club: this fbx is already the right way up.
                        LocKey = "objectthemes.stadium",
                        Medium = new[]
                        {
                            SwingLogSY01 + "|ENV_SY01_SpinningLog_Piviots_MO",
                            SwingLogSY01 + "|ENV_SY01_SpinningLog_Log_MO||0,-12.41,0",
                        },
                        Materials = new[] { "877b303b90a570148a39c0b77d186237" },
                    },
                    new ObjectTheme
                    {
                        // Already the right way up, and far enough off the stock proportions that
                        // both nodes are placed outright to sit on the club's own collision.
                        LocKey = "objectthemes.space",
                        Medium = new[]
                        {
                            SwingLogSY02 + "|ENV_SY02_SpikeLog_Swing_MO_low||0,11.8588,0,0,0.70710678,0,0.70710678,0.57,0.3809,0.57",
                            SwingLogSY02 + "|ENV_SY02_SpikeLog_MO_low||0,0.6532,0,0,0.70710678,0,0.70710678,0.47,0.47,0.57",
                        },
                        Materials = new[] { "700ce58b88389e24ab2dd11f41f5d228" },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Obstacle_Wrecking_Ball_" },
                Nodes = new[] { "Wrecking_ball_01_MO", "Wrecking_ball_Cable", "Wrecking_Ball_Floater_MO" },
                Fit = ObjectThemeFit.Center,
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.retro",
                        Medium = new[]
                        {
                            "6d0586bc23891924c94d9ada6e0c1e56",
                            "35e816862c7979d4497e769983c84a1d",
                            "4b2b2f21065c51948ad3a8697327ce79",
                        },
                        Large = new[]
                        {
                            "5c9c4fc7fdbc7924d84b0c11a2c708de",
                            "a7784fca6e8ba0447b418c372f0c84ea",
                            "29be4565e62c464499bd16a56cafe7e7",
                        },
                        Materials = new[] { RetroObstacles },
                    },
                    new ObjectTheme
                    {
                        // "Pendulam" in the game files, which is why it is easy to miss.
                        LocKey = "objectthemes.future",
                        Medium = new[]
                        {
                            "42d9b9eb9e9c8154ea2362329fcf22e6|ENV_S04_Sphere_MO|3baed22c5c7c2034baa64b42b40ad9e5",
                            "6598cf9002e034246a0e8007afe1a7e0|ENV_S04_PendulamMainBody",
                            "",
                        },
                        Materials = new[] { "1ee70089726e5d64fac145e619e89e25" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.space",
                        Medium = new[]
                        {
                            "6f023c8776468d6498ea2110c2c586f8|ENV_SY02_WreckingBall_Ball_MO",
                            "6f023c8776468d6498ea2110c2c586f8|ENV_SY02_WreckingBall_Stick_MO",
                            "",
                        },
                        Materials = new[]
                        {
                            "fe95c3f6146e5e344bb46e912ec2a783,b3cd32d9b716a71409d3a65e434e13fc",
                            "b3cd32d9b716a71409d3a65e434e13fc",
                        },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Obstacle_Flipper_" },
                // The paddle is a SkinnedMeshRenderer (four blend shapes drive its squash), the
                // frame under it is a plain MeshRenderer.
                Nodes = new[] { "PRP_S03_Flipper_Frame_MO", "PRP_S03_Flipper_MO" },
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.retro",
                        Medium = new[]
                        {
                            RetroFlipperFbx + "|ENV_S10_Retro_Flipper_Frame_01a_MO",
                            RetroFlipperFbx + "|ENV_S10_Retro_Flipper_Below_01a_MO",
                        },
                        Materials = new[] { RetroObstacles },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.sunken",
                        Medium = new[]
                        {
                            SunkenFlipperFbx + "|PRP_S09_Flipper_Frame_MO",
                            SunkenFlipperFbx + "|PRP_S09_Flipper_Bellow_MO",
                        },
                        Materials = new[] { "8f049533a0dd8a843a9e1b3009e18333" },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Feature_Drawbridge_" },
                Nodes = new[] { "ENV_S01_Vanilla_Flap_01a_MO", "ENV_S01_Vanilla_Base_01a_MO" },
                Fit = ObjectThemeFit.Center,
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.party",
                        Medium = new[]
                        {
                            PartyDrawbridgeFbx + "|ENV_S06_DrawBridgeSmall_Detail_MO",
                            PartyDrawbridgeFbx + "|ENV_S06_DrawBridge_MO",
                        },
                        Materials = new[]
                        {
                            "4973e6b772f68a541862e28ba3a6f4c8",
                            "78a2782da6db6d543a6c3403a25b159c,f3f8272d5544c624ebbc1dcdfd8cf79a," +
                            "00789fec02c906548bcc8d8a6e948305",
                        },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.retro",
                        Medium = new[]
                        {
                            RetroDrawbridgeFbx + "|ENV_S10_Retro_Flap_01a_MO",
                            RetroDrawbridgeFbx + "|ENV_S10_Retro_Base_01a_MO",
                        },
                        Materials = new[] { RetroPassive + "," + RetroHardPlastic, RetroHardPlastic },
                    },
                    new ObjectTheme
                    {
                        // Knight Fever authors its bridge upright with the hinge rod at the bottom, so
                        // it is laid flat here and pinned to the stock hinge edge.
                        LocKey = "objectthemes.medieval",
                        Medium = new[]
                        {
                            "9262f7aab3d5d2b4f97ba8b65781505a||1404ba7a535cb47479749b5cb032451b|" +
                            "0,0.07,-0.15,-0.7071068,0,0,0.7071068,0.836,0.836,0.836",
                            "",
                        },
                        Materials = new[] { "1404ba7a535cb47479749b5cb032451b" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.jungle",
                        Small = new[] { "28bfcfc645941cf4c83b660dc08b8447", "" },
                        Medium = new[] { "28bfcfc645941cf4c83b660dc08b8447", "" },
                        Large = new[] { "e3cdfe0c7f54d764a90e560b19eb3b78", "" },
                        Materials = new[] { "eade5b8943eea5a43822c654f8a1193e" },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_SeeSaw_" },
                Nodes = new[] { "ENV_SeeSawPlatform_MO" },
                Fit = ObjectThemeFit.Center,
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        // The show has no S02 top/bottom plate, so a "medieval" set left two of the four
                        // slots stock and read as one recoloured strip. These colourways are complete.
                        LocKey = "objectthemes.pink",
                        Materials = new[] { ",d8a734a397e85f0439f7e5921daf715f,e9114ad7178865c4bb18fff3d50b03e6,399652eb08ad6d7498b3e1259d4a84d6" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.retro",
                        Medium = new[] { "28f21165f9f226d489ba5ba2f2e47cc4" },
                        Materials = new[] { RetroPassive },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.future",
                        Medium = new[] { "beaf49ec0e69aa245a55068b6bb204a9" },
                        Materials = new[] { "fc45adee75660f34dbce544c141919c8" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.yellow",
                        Materials = new[] { ",9200e797c1f709340b6ee1a30d2b0378,b08356810d3afc04192a41557e710120,2703367d12dc1df4fa894c908b256806" },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Obstacle_SpinDisc_" },
                Nodes = new[] { "ENV_spinPlat_main_MO", "ENV_spinPlat_propeller_MO", "ENV_spinPlat_centre_MO" },
                Extras = 1,
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.jungle",
                        MirrorX = true,
                        Medium = new[]
                        {
                            JungleTurntableFbx + "|ENV_S05_Turntable_MO",
                            JungleTurntableFbx + "|ENV_S05_Turntable_Propeller_MO",
                            JungleTurntableFbx + "|ENV_S05_Turntable_Middle_MO",
                        },
                        Materials = new[] { "8af48778756d1964bbc8c664c1eabee4" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.party",
                        MirrorX = true,
                        Medium = new[]
                        {
                            PartyPlatformFbx + "|ENV_S06_Platform_MO",
                            PartyTurntableFbx + "|ENV_S06_Propeller_MO",
                            PartyTurntableFbx + "|ENV_S06_Star_MO",
                        },
                        Materials = new[] { "e9522b953cd534141a2117fc9e490a8b" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.sunken",
                        MirrorX = true,
                        Medium = new[]
                        {
                            SunkenTurntableFbx + "|ENV_S09_TurnTable_MO",
                            PartyTurntableFbx + "|ENV_S06_Propeller_MO",
                            PartyTurntableFbx + "|ENV_S06_Star_MO",
                        },
                        Materials = new[]
                        {
                            "f741f8d5bafaaee42b24bd8706fce18f",
                            "e9522b953cd534141a2117fc9e490a8b",
                            "e9522b953cd534141a2117fc9e490a8b",
                        },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.retro",
                        Medium = new[]
                        {
                            RetroTurntableFbx + "|ENV_S10_Turntable_Outer_MO",
                            RetroTurntableFbx + "|ENV_S10_Turntable_Centre_MO",
                            "",
                            RetroTurntableFbx + "|ENV_S10_Turntable_Arrow_MO",
                        },
                        Materials = new[]
                        {
                            "5ee2960eb9b3883438390fe7a6785b97",
                            "4c6f11fed43515f4b9780f8195b51e0d",
                            "4c6f11fed43515f4b9780f8195b51e0d",
                            "4c6f11fed43515f4b9780f8195b51e0d",
                        },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.stadium",
                        Medium = new[]
                        {
                            StadiumTurntableFbx + "|ENV_SY01_Turntable_MO",
                            StadiumTurntableFbx + "|ENV_SY01_Turntable_Propeller_MO",
                            "",
                        },
                        Materials = new[] { "525a42389633d894280fbe3a52f5812b" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.space",
                        MirrorX = true,
                        Medium = new[] { SpaceTurntableFbx + "|ENV_SY02_Turntable_MO", "", "" },
                        Materials = new[]
                        {
                            "2063ebf44d1ca1741aad158c8fc06e78,dc7c50e5ff194404b9ef134b59b15c3a," +
                            "c1e85cfbe869b5647aa4cbc030f74c4b",
                        },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Obstacle_FanPlatform_" },
                Nodes = new[]
                {
                    "Center_Piece",
                    "Frame_Inner",
                    "Frame_Outer",
                    "Obstacle_FanPlatform_Beam_Single_",
                    "Obstacle_SpinningBeam_BeamDouble_",
                },
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.retro",
                        Small = new[]
                        {
                            "d0ec42d2aef1356418908b642c9224d1",
                            "25a3e7cf768d937478ed8c0ca29dc5ad",
                            "eb5e88e72973954429228356b8002de9",
                            "ad7dc8ee0cd58364eae1b096fe7dbdda",
                            "2e3ce71fac05c6d4eba5ce0efb7b48f8",
                        },
                        Medium = new[]
                        {
                            "9b7a9ea92749001419f9e7ed930b9bcd",
                            "3e1d8f8fb96883445b986991c0e6cdc2",
                            "283a997483d7ca642ab4abe264d5c400",
                            "08bab3d0335225845bdc1894a253bf87",
                            "fd7e2a90becfd074287e7e079daf9a99",
                        },
                        Large = new[]
                        {
                            "eb50d2d97d7d40a46b4ac3464b08c87a",
                            "ec7029a1dfca9a749a2e247760028b53",
                            "21a323f72d3c3b142a9caf8c2543e65f",
                            "90b8e37283dcde04eab427e0219f18cd",
                            "fb6895bb354c6864d8f4780ca937d246",
                        },
                        Materials = new[]
                        {
                            RetroHardPlastic,
                            RetroPassive,
                            RetroHardPlastic,
                            RetroObstacles,
                            RetroObstacles,
                        },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.medieval",
                        Small = new[]
                        {
                            "28d78638a1f0dc44b9a9af2e1691a1fe|PRP_BigFan_Propeller|8deee5ef7915690418efa2dbc677f7e2|0,1.0207,0,0,0,0,1,0.61319,0.61319,0.61319",
                            "28d78638a1f0dc44b9a9af2e1691a1fe|PRP_BigFan_Blades|636bb44d9cb42eb4ba51768232792ffd,48e0ff8e48444594f80c0abdfdd74243,b851464032f376c4390ab0948fdf076f,8deee5ef7915690418efa2dbc677f7e2|0,1.0207,0,0,0,0,1,0.61319,0.61319,0.61319",
                            Hidden,
                        },
                        Medium = new[]
                        {
                            "28d78638a1f0dc44b9a9af2e1691a1fe|PRP_BigFan_Propeller|8deee5ef7915690418efa2dbc677f7e2|0,1.4664,0,0,0,0,1,0.85364,0.85364,0.85364",
                            "28d78638a1f0dc44b9a9af2e1691a1fe|PRP_BigFan_Blades|636bb44d9cb42eb4ba51768232792ffd,48e0ff8e48444594f80c0abdfdd74243,b851464032f376c4390ab0948fdf076f,8deee5ef7915690418efa2dbc677f7e2|0,1.4664,0,0,0,0,1,0.85364,0.85364,0.85364",
                            Hidden,
                        },
                        Large = new[]
                        {
                            "28d78638a1f0dc44b9a9af2e1691a1fe|PRP_BigFan_Propeller|8deee5ef7915690418efa2dbc677f7e2|0,1.9165,0,0,0,0,1,1.071,1.071,1.071",
                            "28d78638a1f0dc44b9a9af2e1691a1fe|PRP_BigFan_Blades|636bb44d9cb42eb4ba51768232792ffd,48e0ff8e48444594f80c0abdfdd74243,b851464032f376c4390ab0948fdf076f,8deee5ef7915690418efa2dbc677f7e2|0,1.9165,0,0,0,0,1,1.071,1.071,1.071",
                            Hidden,
                        },
                        Materials = new[] { "8deee5ef7915690418efa2dbc677f7e2" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.jungle",
                        Small = new[]
                        {
                            "cce30bd283ec0c84c86d731442ae8d57|PRP_S05_BigFan_Propeller_MO|357a8987e00efbe4abea8fe95b46ad85|0,1.0209,0,0,0,0,1,0.61319,0.61319,0.61319",
                            "cce30bd283ec0c84c86d731442ae8d57|PRP_S05_BigFan_Blades_MO|357a8987e00efbe4abea8fe95b46ad85|0,1.0209,0,0,0,0,1,0.61319,0.61319,0.61319",
                            Hidden,
                        },
                        Medium = new[]
                        {
                            "cce30bd283ec0c84c86d731442ae8d57|PRP_S05_BigFan_Propeller_MO|357a8987e00efbe4abea8fe95b46ad85|0,1.4666,0,0,0,0,1,0.85364,0.85364,0.85364",
                            "cce30bd283ec0c84c86d731442ae8d57|PRP_S05_BigFan_Blades_MO|357a8987e00efbe4abea8fe95b46ad85|0,1.4666,0,0,0,0,1,0.85364,0.85364,0.85364",
                            Hidden,
                        },
                        Large = new[]
                        {
                            "cce30bd283ec0c84c86d731442ae8d57|PRP_S05_BigFan_Propeller_MO|357a8987e00efbe4abea8fe95b46ad85|0,1.9168,0,0,0,0,1,1.071,1.071,1.071",
                            "cce30bd283ec0c84c86d731442ae8d57|PRP_S05_BigFan_Blades_MO|357a8987e00efbe4abea8fe95b46ad85|0,1.9168,0,0,0,0,1,1.071,1.071,1.071",
                            Hidden,
                        },
                        Materials = new[] { "357a8987e00efbe4abea8fe95b46ad85" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.party",
                        Small = new[]
                        {
                            "2e18294debf6df1429ef40daa48cac9a|PRP_S06_BigFan_Propeller_MO|794aa41ad24f1074cb4ae9fa9d134dbd|0,1.0168,0,0,0,0,1,60.037,60.037,60.037",
                            "2e18294debf6df1429ef40daa48cac9a|PRP_S06_BigFan_MO|794aa41ad24f1074cb4ae9fa9d134dbd|0,1.0168,0,0,0,0,1,60.037,60.037,60.037",
                            Hidden,
                        },
                        Medium = new[]
                        {
                            "2e18294debf6df1429ef40daa48cac9a|PRP_S06_BigFan_Propeller_MO|794aa41ad24f1074cb4ae9fa9d134dbd|0,1.4609,0,0,0,0,1,83.579,83.579,83.579",
                            "2e18294debf6df1429ef40daa48cac9a|PRP_S06_BigFan_MO|794aa41ad24f1074cb4ae9fa9d134dbd|0,1.4609,0,0,0,0,1,83.579,83.579,83.579",
                            Hidden,
                        },
                        Large = new[]
                        {
                            "2e18294debf6df1429ef40daa48cac9a|PRP_S06_BigFan_Propeller_MO|794aa41ad24f1074cb4ae9fa9d134dbd|0,1.9096,0,0,0,0,1,104.86,104.86,104.86",
                            "2e18294debf6df1429ef40daa48cac9a|PRP_S06_BigFan_MO|794aa41ad24f1074cb4ae9fa9d134dbd|0,1.9096,0,0,0,0,1,104.86,104.86,104.86",
                            Hidden,
                        },
                        Materials = new[] { "794aa41ad24f1074cb4ae9fa9d134dbd" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.stadium",
                        Small = new[]
                        {
                            "228b4415c7ea8cf47a244f18b2f6bd2b|ENV_SY01_RotatingFan_Propeller_MO|0fcac27e209b8204da923d67f1aeef21|0,0.8229,0,0,0,0,1,0.61319,0.61319,0.61319",
                            "228b4415c7ea8cf47a244f18b2f6bd2b|ENV_SY01_RotatingFan_Blades_MO|0fcac27e209b8204da923d67f1aeef21|0,0.8229,0,0,0,0,1,0.61319,0.61319,0.61319",
                            Hidden,
                        },
                        Medium = new[]
                        {
                            "228b4415c7ea8cf47a244f18b2f6bd2b|ENV_SY01_RotatingFan_Propeller_MO|0fcac27e209b8204da923d67f1aeef21|0,1.191,0,0,0,0,1,0.85364,0.85364,0.85364",
                            "228b4415c7ea8cf47a244f18b2f6bd2b|ENV_SY01_RotatingFan_Blades_MO|0fcac27e209b8204da923d67f1aeef21|0,1.191,0,0,0,0,1,0.85364,0.85364,0.85364",
                            Hidden,
                        },
                        Large = new[]
                        {
                            "228b4415c7ea8cf47a244f18b2f6bd2b|ENV_SY01_RotatingFan_Propeller_MO|0fcac27e209b8204da923d67f1aeef21|0,1.571,0,0,0,0,1,1.071,1.071,1.071",
                            "228b4415c7ea8cf47a244f18b2f6bd2b|ENV_SY01_RotatingFan_Blades_MO|0fcac27e209b8204da923d67f1aeef21|0,1.571,0,0,0,0,1,1.071,1.071,1.071",
                            Hidden,
                        },
                        Materials = new[] { "0fcac27e209b8204da923d67f1aeef21" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.space",
                        Small = new[]
                        {
                            "feec60ac6386cb24f86ea18f97f27996|ENV_SY02_FanPlatform_Propeller_MO|2063ebf44d1ca1741aad158c8fc06e78|0,0.74453,0,0,0,0,1,0.61319,0.61319,0.61319",
                            "feec60ac6386cb24f86ea18f97f27996|ENV_SY02_BigFan_Blades_MO|2063ebf44d1ca1741aad158c8fc06e78,dc7c50e5ff194404b9ef134b59b15c3a,dc7c50e5ff194404b9ef134b59b15c3a,37ee1311b6e94ce4a923f29c3f75de89,c1e85cfbe869b5647aa4cbc030f74c4b,568a5b81b6f0ead4eb31bfaa1355f6e3|0,0.74453,0,0,0,0,1,0.61319,0.61319,0.61319",
                            Hidden,
                        },
                        Medium = new[]
                        {
                            "feec60ac6386cb24f86ea18f97f27996|ENV_SY02_FanPlatform_Propeller_MO|2063ebf44d1ca1741aad158c8fc06e78|0,1.0819,0,0,0,0,1,0.85364,0.85364,0.85364",
                            "feec60ac6386cb24f86ea18f97f27996|ENV_SY02_BigFan_Blades_MO|2063ebf44d1ca1741aad158c8fc06e78,dc7c50e5ff194404b9ef134b59b15c3a,dc7c50e5ff194404b9ef134b59b15c3a,37ee1311b6e94ce4a923f29c3f75de89,c1e85cfbe869b5647aa4cbc030f74c4b,568a5b81b6f0ead4eb31bfaa1355f6e3|0,1.0819,0,0,0,0,1,0.85364,0.85364,0.85364",
                            Hidden,
                        },
                        Large = new[]
                        {
                            "feec60ac6386cb24f86ea18f97f27996|ENV_SY02_FanPlatform_Propeller_MO|2063ebf44d1ca1741aad158c8fc06e78|0,1.4341,0,0,0,0,1,1.071,1.071,1.071",
                            "feec60ac6386cb24f86ea18f97f27996|ENV_SY02_BigFan_Blades_MO|2063ebf44d1ca1741aad158c8fc06e78,dc7c50e5ff194404b9ef134b59b15c3a,dc7c50e5ff194404b9ef134b59b15c3a,37ee1311b6e94ce4a923f29c3f75de89,c1e85cfbe869b5647aa4cbc030f74c4b,568a5b81b6f0ead4eb31bfaa1355f6e3|0,1.4341,0,0,0,0,1,1.071,1.071,1.071",
                            Hidden,
                        },
                        Materials = new[] { "2063ebf44d1ca1741aad158c8fc06e78" },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Obstacle_Bumper_" },
                Nodes = new[] { "Obstacle_Bumper_" },
                Fit = ObjectThemeFit.Footprint,
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.fruit",
                        Medium = new[] { "f187c64ba6dbab247b008a09328db75d" },
                        Materials = new[] { "7209dc52b40723445b342cd6b1583e4b" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.sunken",
                        Medium = new[] { "124061efb1a433b4398b37c4d1879f4d" },
                        Materials = new[] { "020c2aae2ebb9ab438f6d4670bb037ee" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.retro",
                        Medium = new[] { "dcc882b1a7899494ebb228442d37db6e" },
                        Materials = new[] { RetroObstacles },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.stadium",
                        Medium = new[] { "7bef0ce086fac794ca00a308ccb4bfe5" },
                        Materials = new[] { "d56533e67a375a840ac90f22e944f1ac" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.space",
                        Medium = new[] { "d5ea1e97f5cbe1c4aba84cc13b108d6b" },
                        Materials = new[] { "702629378acd4224985f20ad944da2a4,53845119f3c7da74091fcf0b06cf0802" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.halloween",
                        Materials = new[] { "08f2790a31bed7447b1b72564d5b93f2" },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Obstacle_BlizzardFan_" },
                Nodes = new[] { "Obstacle_BlizzardFan_Vanilla_", "Obstacle_BlizzardFan_Fanblades_Vanilla_" },
                Fit = ObjectThemeFit.Footprint,
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        // Lifted from the game's own Placeable_Obstacle_BlizzardFan_Retro: housing mesh
                        // swapped 1:1, blades left stock, two materials on the housing.
                        LocKey = "objectthemes.retro",
                        Medium = new[]
                        {
                            "4da7acd3a3e8c0f40ae59e03436bb15a||" + RetroObstacles +
                            ",990b0fedde8a0634eb0fbd7252612a62|0,0,0,0,0,0,1,1,1,1",
                            "",
                        },
                        Materials = new[] { RetroObstacles + ",990b0fedde8a0634eb0fbd7252612a62" },
                    },
                    new ObjectTheme
                    {
                        // The show fans are about 60% of the creative one and their blades sit off the
                        // hub, so housing + blades are fitted together using the fbx's own offset.
                        LocKey = "objectthemes.jungle",
                        Medium = new[]
                        {
                            JungleFanFbx + "|ENV_S05_StandingFan_MO",
                            JungleFanFbx + "|ENV_S05_StandingFanBlades_MO||0.01,3.18,0.62",
                        },
                        Materials = new[] { "1dcdb6468e4005f4ca4053b1997d6d53" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.sunken",
                        Medium = new[]
                        {
                            SunkenFanFbx + "|ENV_S09_Fan_Frame_MO",
                            SunkenFanFbx + "|ENV_S09_Fan_Blades_MO||0,3.57,-0.07",
                        },
                        Materials = new[] { "bdf396f56587e0948aa9447e2d9d9e19" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.space",
                        Medium = new[]
                        {
                            SpaceFanFbx + "|ENV_SY02_Fan_MO",
                            SpaceFanFbx + "|ENV_S03_Fan_Blades_MO||0,0,0",
                        },
                        Materials = new[] { "a59ebb99591e87847ae7eed69ed71e4e" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.winter",
                        Medium = new[]
                        {
                            WinterFanFbx + "|ENV_S03_BodyFan_MO",
                            WinterFanFbx + "|ENV_S03_Fanblades_MO||0,0.03,0.86",
                        },
                        Materials = new[] { "8c51f5a8a293a314ab86e8b4cd3eb6f4" },
                    },
                },
            },

            new ObjectThemeFamily
            {
                // Must precede the long beam: "Placeable_Obstacle_SpinningBeam_" is a prefix of this.
                Prefixes = new[] { "Placeable_Obstacle_SpinningBeamShort_" },
                Nodes = new[]
                {
                    "Obstacle_SpinningBeam_BeamSingleShort_",
                    "Obstacle_SpinningBeam_BeamDoubleShort_",
                    "Obstacle_TurnstileBase_",
                },
                Fit = ObjectThemeFit.Length,
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.retro",
                        Large = new[]
                        {
                            "ca47d2e414e94f547a1f6f6836198d1a",
                            "1ec4a98bbff3ede488cb4696403edbd5",
                            "93fa8bc28c16eab4b8fb145ecca267df",
                        },
                        Materials = new[] { RetroObstacles, RetroObstacles, RetroHardPlastic },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.jungle",
                        Medium = new[] { JungleArmFbx + "|ENV_S05_RotatingArmBumber_MO", "", "" },
                        Materials = new[] { "04971cf1de5f183479041212ff9fcaaa" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.stadium",
                        Medium = new[] { StadiumArmFbx + "|ENV_SY01_RotatingArmBumber_MO", "", "" },
                        Materials = new[] { "2394d1d26cf788e42832dfd480d45fe4" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.sonic",
                        Medium = new[] { SonicArmFbx + "|ENV_Bluejay_RotatingArmBumber_MO", "", "" },
                        Materials = new[] { "804a2d57c1166d3438bc5768deeb9fac" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.sunken",
                        Medium = new[] { SunkenBarFbx + "|ENV_S09_SpinningBar_ShortArm_MO", "", "" },
                        Materials = new[] { "814c5b511222cb54fa94d2ea13f71d21" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.party",
                        Medium = new[] { "", "98a9de728ef4f8f4893fed147db6d6c2", "" },
                        Materials = new[] { "1ac73961dfb553949a760691ca19a5d7" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.space",
                        Medium = new[]
                        {
                            "b50554f2086547444b73f7e00ae0c349",
                            "f47a4bb22c762d04680bee326ceaa47f",
                            "",
                        },
                        Materials = new[] { "2063ebf44d1ca1741aad158c8fc06e78" },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Obstacle_SpinningBeam_" },
                Fit = ObjectThemeFit.Length,
                Nodes = new[]
                {
                    "Frame",
                    "Ring_Mat_1",
                    "Ring_Mat_2",
                    "Obstacle_FanPlatform_Beam_Single_",
                    "Obstacle_SpinningBeam_BeamDouble_",
                },
                // Slot 5: the season's centre cap. It hangs off the arm group rather than the base
                // disc, because the base disc is what the object's Floor toggle hides.
                Extras = 1,
                ExtrasNode = 3,
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.retro",
                        Small = new[]
                        {
                            "e76a8209a2d1bc14ab15f615fc39ce20",
                            "0107b6071fbe6c94eb71cefd587a0f69",
                            "f212f01a2054f8b41a392f0f17b9716f",
                            "ad7dc8ee0cd58364eae1b096fe7dbdda",
                            "2e3ce71fac05c6d4eba5ce0efb7b48f8",
                        },
                        Medium = new[]
                        {
                            "3f2aebb9df356ba46a47407aee04287e",
                            "7fc06cb0093f69b4e8a1de15220b3eaf",
                            "7f8e00d99ba2dbf40b5b8caa7a0dc2b2",
                            "08bab3d0335225845bdc1894a253bf87",
                            "fd7e2a90becfd074287e7e079daf9a99",
                        },
                        Large = new[]
                        {
                            "e513ea7f9b2e5e64fac6c827de2e5581",
                            "f65ef9c3390d86744807fe71f1e164a0",
                            "ce505ed166bbeb348a01fefc1b99c40a",
                            "90b8e37283dcde04eab427e0219f18cd",
                            "fb6895bb354c6864d8f4780ca937d246",
                        },
                        Materials = new[]
                        {
                            RetroHardPlastic,
                            RetroHardPlastic,
                            RetroPassive,
                            RetroObstacles,
                            RetroObstacles,
                        },
                    },
                    new ObjectTheme
                    {
                        // One-sided arm, so it themes the single beam only and the double keeps stock.
                        LocKey = "objectthemes.jungle",
                        Small = new[]
                        {
                            "", "", "",
                            JungleArmFbx + "|ENV_S05_RotatingArmBumber_MO",
                            "",
                            JungleArmFbx + "|ENV_S05_RotatingArmBase_MO||0,0,0,0,0,0,1,0.576,0.576,0.576",
                        },
                        Medium = new[]
                        {
                            "", "", "",
                            JungleArmFbx + "|ENV_S05_RotatingArmBumber_MO",
                            "",
                            JungleArmFbx + "|ENV_S05_RotatingArmBase_MO||0,0,0,0,0,0,1,0.808,0.808,0.808",
                        },
                        Large = new[]
                        {
                            "", "", "",
                            JungleArmFbx + "|ENV_S05_RotatingArmBumber_MO",
                            "",
                            JungleArmFbx + "|ENV_S05_RotatingArmBase_MO||0,0,0,0,0,0,1,1,1,1",
                        },
                        Materials = new[] { "04971cf1de5f183479041212ff9fcaaa" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.stadium",
                        Small = new[]
                        {
                            "", "", "",
                            StadiumArmFbx + "|ENV_SY01_RotatingArmBumber_MO",
                            "",
                            StadiumArmFbx + "|ENV_SY01_RotatingArmBase_MO||0,0,0,0,0,0,1,0.576,0.576,0.576",
                        },
                        Medium = new[]
                        {
                            "", "", "",
                            StadiumArmFbx + "|ENV_SY01_RotatingArmBumber_MO",
                            "",
                            StadiumArmFbx + "|ENV_SY01_RotatingArmBase_MO||0,0,0,0,0,0,1,0.808,0.808,0.808",
                        },
                        Large = new[]
                        {
                            "", "", "",
                            StadiumArmFbx + "|ENV_SY01_RotatingArmBumber_MO",
                            "",
                            StadiumArmFbx + "|ENV_SY01_RotatingArmBase_MO||0,0,0,0,0,0,1,1,1,1",
                        },
                        Materials = new[] { "2394d1d26cf788e42832dfd480d45fe4" },
                    },
                    new ObjectTheme
                    {
                        // The Sonic collab ships under the codename "Blue Jay".
                        LocKey = "objectthemes.sonic",
                        Small = new[]
                        {
                            "", "", "",
                            SonicArmFbx + "|ENV_Bluejay_RotatingArmBumber_MO",
                            "",
                            SonicArmFbx + "|ENV_Bluejay_RotatingArmPivot_MO||0,0,0,0,0,0,1,0.576,0.576,0.576",
                        },
                        Medium = new[]
                        {
                            "", "", "",
                            SonicArmFbx + "|ENV_Bluejay_RotatingArmBumber_MO",
                            "",
                            SonicArmFbx + "|ENV_Bluejay_RotatingArmPivot_MO||0,0,0,0,0,0,1,0.808,0.808,0.808",
                        },
                        Large = new[]
                        {
                            "", "", "",
                            SonicArmFbx + "|ENV_Bluejay_RotatingArmBumber_MO",
                            "",
                            SonicArmFbx + "|ENV_Bluejay_RotatingArmPivot_MO||0,0,0,0,0,0,1,1,1,1",
                        },
                        Materials = new[] { "804a2d57c1166d3438bc5768deeb9fac" },
                    },
                    new ObjectTheme
                    {
                        // Party's arm is symmetric, so it is a double beam and has no single form.
                        LocKey = "objectthemes.party",
                        Medium = new[]
                        {
                            "", "", "",
                            "",
                            "98a9de728ef4f8f4893fed147db6d6c2",
                        },
                        Materials = new[] { "1ac73961dfb553949a760691ca19a5d7" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.sunken",
                        Small = new[]
                        {
                            "", "", "",
                            SunkenBarFbx + "|ENV_S09_SpinningBar_ShortArm_MO",
                            "",
                            SunkenBarFbx + "|ENV_S09_SpinningBar_Base_MO||0,0,0,0,0,0,1,0.576,0.576,0.576",
                        },
                        Medium = new[]
                        {
                            "", "", "",
                            SunkenBarFbx + "|ENV_S09_SpinningBar_ShortArm_MO",
                            "",
                            SunkenBarFbx + "|ENV_S09_SpinningBar_Base_MO||0,0,0,0,0,0,1,0.808,0.808,0.808",
                        },
                        Large = new[]
                        {
                            "", "", "",
                            SunkenBarFbx + "|ENV_S09_SpinningBar_ShortArm_MO",
                            "",
                            SunkenBarFbx + "|ENV_S09_SpinningBar_Base_MO||0,0,0,0,0,0,1,1,1,1",
                        },
                        Materials = new[] { "814c5b511222cb54fa94d2ea13f71d21" },
                    },
                    new ObjectTheme
                    {
                        // The only season shipping both a single and a double arm.
                        LocKey = "objectthemes.space",
                        Medium = new[]
                        {
                            "", "", "",
                            "b50554f2086547444b73f7e00ae0c349",
                            "f47a4bb22c762d04680bee326ceaa47f",
                        },
                        Materials = new[] { "2063ebf44d1ca1741aad158c8fc06e78" },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Obstacle_SpinDoor_" },
                Nodes = new[] { "Obstacle_SpinDoor_Ground_Vanilla_", "Obstacle_SpinDoor_Main_Vanilla_" },
                Fit = ObjectThemeFit.Center,
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.retro",
                        Medium = new[]
                        {
                            RetroRotatingWall + "|ENV_S10_RotatingWall_Ground_01a_MO",
                            RetroRotatingWall + "|ENV_S10_RotatingWall_Wall_01a_MO",
                        },
                        Materials = new[] { RetroObstacles },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.stadium",
                        Medium = new[]
                        {
                            StadiumSpinDoor + "|ENV_SY01_spinDoorGround_MO",
                            StadiumSpinDoor + "|ENV_SY01_spinDoorMain_MO",
                        },
                        Materials = new[] { "80decdbf82c3c7c4daf10f090a31d71a" },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Obstacle_FloorFan_" },
                Nodes = new[] { "Obstacle_FloorFan_Vanilla_", "Obstacle_FloorFan_Blades_Vanilla_" },
                Fit = ObjectThemeFit.Footprint,
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        // Placement lifted from the game's own retro placeable: it scales the housing
                        // 1.6x and nudges it back, the blades stay stock.
                        LocKey = "objectthemes.retro",
                        Medium = new[]
                        {
                            "da97e792388060a4096f2d471a81e4a2||" + RetroObstacles +
                            ",990b0fedde8a0634eb0fbd7252612a62|0,-0.03,0.047,0,0,0,1,1.6,1.6,1.6",
                            "",
                        },
                        Materials = new[] { RetroObstacles + ",990b0fedde8a0634eb0fbd7252612a62" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.winter",
                        Medium = new[]
                        {
                            WinterFanFbx + "|ENV_S03_FloorFan_MO",
                            WinterFanFbx + "|ENV_S03_Fanblades_MO||0,0.43,0",
                        },
                        Materials = new[] { "8c51f5a8a293a314ab86e8b4cd3eb6f4" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.sunken",
                        Medium = new[]
                        {
                            SunkenFanFbx + "|ENV_S09_FloorFan_Frame_MO",
                            SunkenFanFbx + "|ENV_S09_FloorFan_Blades_MO||0,1.57,0.08",
                        },
                        Materials = new[] { "bdf396f56587e0948aa9447e2d9d9e19" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.space",
                        Medium = new[]
                        {
                            SpaceFloorFanFbx + "|ENV_SY02_Fan_Floor_MO",
                            SpaceFloorFanFbx + "|ENV_SY02_Fan_Blades_Floor_MO||0,0,0",
                        },
                        Materials = new[] { "a59ebb99591e87847ae7eed69ed71e4e" },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Obstacle_BoomBlaster_" },
                Nodes = new[] { "Model" },
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    // The game's own retro blaster is a straight material swap on the same mesh.
                    new ObjectTheme { LocKey = "objectthemes.retro", Materials = new[] { "b0a601ab70fe35f4ab0c1644587c90e6" } },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Feature_Floating_MultiCannon_", "Placeable_Feature_Floating_Cannon_" },
                Nodes = new[] { "PROPS_Canon_Base" },
                Balloon = true,
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.retro",
                        Medium = new[] { "271e62810552d2b41a619f44f8590d88" },
                        Materials = new[] { RetroObstacles },
                    },
                    new ObjectTheme { LocKey = "objectthemes.future", Materials = new[] { "6d442edab2f79be4b9eef3427ccaddd4" } },
                    new ObjectTheme { LocKey = "objectthemes.jungle", Materials = new[] { "ae6b31a5aa2866744993a7fac9c33f02" } },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.space",
                        Medium = new[] { "a7fb1b33e2e6da642acd9198e994aa67|PROPS_Canon_Base||-0.107,-0.56,0.582,0,0,0,1,1,1,1" },
                        Materials = new[] { "ff3f4262d653cb545b3b12b4767c9513" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.party",
                        Medium = new[] { "a452eb7113684be42aac1700ea3890d5|ENV_S06_Canon_Body_MO||0.0408,19.22,2.101,0,0,0,1,1,1,1" },
                        Materials = new[] { "794aa41ad24f1074cb4ae9fa9d134dbd" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.sunken",
                        Medium = new[] { "fd9d08d5f151358418a42938dce9bc20|ENV_S09_Cannon_head_MO||0.0408,-0.49,0.156,0,0,0,1,1,1,1" },
                        Materials = new[] { "bdf396f56587e0948aa9447e2d9d9e19" },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Rule_Button_" },
                // Platform first: "ENV_Button" is a prefix of "ENV_ButtonPlatform".
                Nodes = new[] { "ENV_ButtonPlatform", "ENV_Button" },
                Fit = ObjectThemeFit.Center,
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        // Exactly what the game's own Placeable_Rule_Button_Future uses.
                        LocKey = "objectthemes.future",
                        Medium = new[]
                        {
                            FutureButton + "|ENV_ButtonPlatform",
                            FutureButton + "|ENV_Button",
                        },
                        Materials = new[] { "a1264b7858a4c7141bd9b843d9da17cb" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.jungle",
                        Medium = new[]
                        {
                            JungleButton + "|ENV_S05_ButtonPlatform",
                            JungleButton + "|ENV_S05_Button",
                        },
                        Materials = new[] { "1a9474f852fea884eab7ba8e99655c29" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.party",
                        Medium = new[]
                        {
                            FutureButton + "|ENV_ButtonPlatform",
                            FutureButton + "|ENV_Button",
                        },
                        Materials = new[] { "af71a74d3225f5447927f3e195e30ea4" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.sunken",
                        Medium = new[]
                        {
                            SunkenButton + "|ENV_S09_Dispenser_ButtonRim_MO",
                            SunkenButton + "|ENV_S09_Dispenser_Button_MO",
                        },
                        Materials = new[] { "bd99f5375f2560c478667487569b152a" },
                    },
                    new ObjectTheme
                    {
                        // Halloween ships no button mesh of its own - the HW bundle has only a collider -
                        // so like the halloween bumper it is the stock mesh wearing the HW atlas.
                        LocKey = "objectthemes.halloween",
                        Materials = new[] { "fe6ae7131fc393b4dacad4c860ccaa4e" },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Rule_Trigger_Zone_" },
                Nodes = new[] { "TriggerZoneMeshVisible" },
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme { LocKey = "objectthemes.gravitylow", Attach = GravityZoneArt.Low },
                    new ObjectTheme { LocKey = "objectthemes.gravityhigh", Attach = GravityZoneArt.High },
                },
            },

            new ObjectThemeFamily
            {
                // The belt is one flat mesh in every season and the game itself only swaps the scrolling
                // material on it, which is what its own retro conveyor does.
                Prefixes = new[] { "Placeable_Floor_Conveyor_", "Placeable_Feature_Conveyor_", "Placeable_Floor_Vinyl_Conveyor" },
                Nodes = new[] { "Mesh" },
                Volatile = true,
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme { LocKey = "objectthemes.medieval", Materials = new[] { "08d820d032baf844f9876b540a071240" },
                                      Reverse = "58afff2337ae3f34aac79a9d5384a65d" },
                    new ObjectTheme { LocKey = "objectthemes.winter", Materials = new[] { "afaad5f1cd42d1d448d6d4f0831bdb88" },
                                      Reverse = "f81e5add391bcdb49bcf0df7390158ab" },
                    new ObjectTheme { LocKey = "objectthemes.pistachio", Materials = new[] { "7e04e197bd4a1cc448ab026ed44ee4cc" },
                                      Reverse = "2e3c51f18f55e3e41acf5f14a0e9a5da" },
                    new ObjectTheme { LocKey = "objectthemes.space", Materials = new[] { "a66aa09d53ef3b544be594395ebbc169" },
                                      Reverse = "5de56ee933e6f6f4bb2d8a785b29efea" },
                    new ObjectTheme { LocKey = "objectthemes.future", Materials = new[] { "3182abe66d1db044fa3cca2510b83916" } },
                    new ObjectTheme { LocKey = "objectthemes.stadium", Materials = new[] { "04e4bab7adb233e4a9acfa31d8265533" },
                                      Reverse = "0d0057ddef485664fad9cf599dabca35" },
                    new ObjectTheme { LocKey = "objectthemes.sonic", Materials = new[] { "c0b2e3718fdc81243afe07cfcd89d303" } },
                    new ObjectTheme { LocKey = "objectthemes.jungle", Materials = new[] { "aef2f87c4b89a1940a5eb08171a511c1" } },
                    new ObjectTheme { LocKey = "objectthemes.party", Materials = new[] { "2e9918d0fc209e54cb728515f6d7612c" } },
                    new ObjectTheme { LocKey = "objectthemes.retro", Materials = new[] { "07ec0c33d65b302498384ef1c92f9f29" },
                                      Reverse = "f46ab8584af51e849837ce0755282d7c" },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_BasicBlocks_HexagonPrism_" },
                Nodes = new[] { "ENV_S01_Vanilla_Block_HexagonPrism_01_MO" },
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme { LocKey = "objectthemes.space", Materials = new[] { "dd497fd026201fe47952cb451857905a" } },
                    new ObjectTheme { LocKey = "objectthemes.stadium", Materials = new[] { "6e2f718e6fccbf54ca18f61f3c752878" } },
                    new ObjectTheme { LocKey = "objectthemes.blue", Materials = new[] { "cc9ad147e86ce0349bc89516c2e2f56d" } },
                    new ObjectTheme { LocKey = "objectthemes.pink", Materials = new[] { "6b922fd8eae0867438600f213a69a73f" } },
                    new ObjectTheme { LocKey = "objectthemes.purple", Materials = new[] { "ec13326c07668ed45b9f0d9ab003c2bd" } },
                    new ObjectTheme { LocKey = "objectthemes.green", Materials = new[] { "267f81e6eeadbe642b21060476152810" } },
                    new ObjectTheme { LocKey = "objectthemes.yellow", Materials = new[] { "8c0801a4ca486c94892e4ada65871890" } },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Feature_PachinkoPillar_" },
                Nodes = new[] { "Feature_PachinkoPillar_" },
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme { LocKey = "objectthemes.blue", Materials = new[] { "6ec23ad4cf2aeb1479d71d905ca035a4" } },
                    new ObjectTheme { LocKey = "objectthemes.pink", Materials = new[] { "2d466a7019d898c40a817535c7aa129a" } },
                    new ObjectTheme { LocKey = "objectthemes.purple", Materials = new[] { "4f8741827506fc74c922f45537d1aa48" } },
                    new ObjectTheme { LocKey = "objectthemes.green", Materials = new[] { "dce983f2ef88ecd4ebccf4ec39f2bb57" } },
                    new ObjectTheme { LocKey = "objectthemes.red", Materials = new[] { "27e6f2cf7aaeff24b9e1d6bd2d23fbd7" } },
                    new ObjectTheme { LocKey = "objectthemes.winter", Materials = new[] { "6041ccec1d493634f9b2c32110089698" } },
                    new ObjectTheme { LocKey = "objectthemes.party", Materials = new[] { "1dd818b03fe5ef54797d7f92a4ad42c8" } },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Obstacle_Forcefield_" },
                Nodes = new[] { "Visual_Forcefield", "ForcefieldTop", "ForcefieldMid", "ForcefieldBottom" },
                Drawable = true,
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme { LocKey = "objectthemes.halloween", Materials = new[] { "869e8c2d4a9ae56419a8524e0d9e9997" } },
                    new ObjectTheme { LocKey = "objectthemes.winter", Materials = new[] { "518b5bce78171fa4abbca9fa3fa7ea93" } },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.space",
                        Materials = new[] { "bddb59431f0c6f642a3c181d17750fc7" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.future",
                        Materials = new[] { "6c7f208267f7e5b439e60704121b9f10" },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Feature_Square_" },
                Nodes = new[] { "Feature_Square_" },
                Fit = ObjectThemeFit.Footprint,
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.sunken",
                        Medium = new[] { "4f76a714c460a7b4c805d03bfe59f350" },
                        Materials = new[] { SunkenBumperShapes },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.retro",
                        Small = new[] { "5f6389d1e7a0f704cbd6ada738056a02" },
                        Medium = new[] { "3e4a93044f7c9624c9056915c43e9d23" },
                        Large = new[] { "fe1e20e1f74c12f40980414827755b4d" },
                        Materials = new[] { RetroObstacles },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.future",
                        Medium = new[] { "47bbef5b23eeeb54e9c6c8566ad7022d" },
                        Materials = new[] { FruitBouncyShapes },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.space",
                        Medium = new[] { "174134505147c584aadbfe8a6183441c" },
                        Materials = new[] { "9c32b49bd87d7b4409f01a94efc29121" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.medieval",
                        Materials = new[] { "2b50e494bbb131048a3862ae0f2e2707," },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.winter",
                        Materials = new[] { ",245961f64bfcc2643b10be5780499e96" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.jungle",
                        Materials = new[] { "396f596435b23c34597c0a7d65b8a30d," },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.party",
                        Materials = new[] { "4882339b2c3d38c4ba27b4556326312b," },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.stadium",
                        Materials = new[] { "28afc407d1f52154da50299816dfee40," },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.halloween",
                        Materials = new[] { "5a92610732c3d734696c3692f680a301," },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Feature_Triangle_" },
                // Feature_Triangle_* is an empty pivot on the live placeable; the art hangs two
                // levels below it and the base plate is a separate node the game never reskins.
                Nodes = new[] { "PB_bouncyTriangle01" },
                Fit = ObjectThemeFit.Footprint,
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.sunken",
                        Small = new[] { "71ca36e19bce8e04390240ae691085b8" },
                        Medium = new[] { "a0f760baf0490b7459766e21f0c5d139" },
                        Large = new[] { "e84a15eca6a48454b9c0ef998d758da2" },
                        Materials = new[] { SunkenBumperShapes },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.retro",
                        Small = new[] { "a9c8de25ac22f374a9d560e8d493f0b4" },
                        Medium = new[] { "f0e3f47254daa624daa0d81bb62f4595" },
                        Large = new[] { "b2e306c7df5ce7043b9b88f71247a7d6" },
                        Materials = new[] { RetroObstacles },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.future",
                        Medium = new[] { "1208ea3f87f9e934db4c303c069ef719" },
                        Materials = new[] { FruitBouncyShapes },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.medieval",
                        Materials = new[] { ",2b50e494bbb131048a3862ae0f2e2707" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.winter",
                        Materials = new[] { "245961f64bfcc2643b10be5780499e96," },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.jungle",
                        Materials = new[] { ",146ef42245dc8a0438151b8afa3363de" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.party",
                        Materials = new[] { ",4882339b2c3d38c4ba27b4556326312b" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.stadium",
                        Materials = new[] { ",28afc407d1f52154da50299816dfee40" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.space",
                        Materials = new[] { ",4fb6f3d80c8aab0409501b3bd1bbc776" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.halloween",
                        Materials = new[] { ",5a92610732c3d734696c3692f680a301" },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Obstacle_DoubleSpeedArch_", "Placeable_Obstacle_SpeedArch_" },
                Nodes = new[] { "ENV_SY01_SpeedArch_Base_MO", "ENV_SY01_SpeedArch_Inside_MO", "ENV_SY01_SpeedArch_Column_MO" },
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme { LocKey = "objectthemes.sonic", Materials = new[] { "56b8dbce78a612649952a95590d322c5" } },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Decoration_Hover_Arrow_" },
                Nodes = new[] { "Decoration_Hover_Arrow_Vanilla_MEDIUM_Arrow_Sign" },
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.retro",
                        Medium = new[] { "06882e73ec958ab4ab3ddbfe042fc640||c69889a207c113d43b4d26e7830a846d|" +
                                         "0.006,5.14,-0.17,-0.707,0,0,0.707,8.951,8.951,8.951" },
                        Materials = new[] { "c69889a207c113d43b4d26e7830a846d" },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Feature_Step_Ramp_", "Placeable_Feature_Move_Ramp_" },
                Nodes = new[] { "Feature_Ramp_Vanilla_" },
                Fit = ObjectThemeFit.Footprint,
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        // One S02 mesh covers all three sizes: the footprint fit scales it to whichever
                        // stock ramp is under it, and its origin sits mid-mesh rather than at the low end.
                        LocKey = "objectthemes.medieval",
                        Medium = new[] { "72807247f4dbd10499de15fccceeeae1" },
                        Materials = new[] { MedievalMovableProps },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Feature_Move_Box_With_Fan_" },
                Nodes = new[] { "Box_Model", "Fan_Blade_Model" },
                Fit = ObjectThemeFit.Footprint,
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.medieval",
                        RotateX = -90f,
                        XSmall = new[]
                        {
                            "f016e9a7611a8be43b3a2d0c7cd78373|ENV_S02_MovableBox_01_MO|48e0ff8e48444594f80c0abdfdd74243,df4fcaecaf535e644ad07e7b1d1cae45,288b81f7c3c33ec45b556d056fe714f4",
                            "478970479e8bc1840a4a18514dc85e74|ENV_S01_Vanilla_Fan_02a_MO||0,1.4735,0,0,0,0,1,0.6,1,0.6",
                        },
                        Small = new[]
                        {
                            "294d3914451f5b048b9340f7029b88e9|ENV_S02_MovableBox_02_MO|16bb7c62ac7e029409e151f9ae3fc190,df4fcaecaf535e644ad07e7b1d1cae45,48e0ff8e48444594f80c0abdfdd74243,288b81f7c3c33ec45b556d056fe714f4",
                            "478970479e8bc1840a4a18514dc85e74|ENV_S01_Vanilla_Fan_02a_MO||0,3.1608,0,0,0,0,1,1,1,1",
                        },
                        Medium = new[]
                        {
                            "dcbbde6525d496b49957ee173707457b|ENV_S02_MovableBox_03_MO|48e0ff8e48444594f80c0abdfdd74243,16bb7c62ac7e029409e151f9ae3fc190,df4fcaecaf535e644ad07e7b1d1cae45,48e0ff8e48444594f80c0abdfdd74243,288b81f7c3c33ec45b556d056fe714f4",
                            "478970479e8bc1840a4a18514dc85e74|ENV_S01_Vanilla_Fan_02a_MO||0,4.564,0,0,0,0,1,1,1,1",
                        },
                        Large = new[]
                        {
                            "378e747fba29da24294d22bfb5ec5c81|ENV_S02_MovableBox_04_MO|16bb7c62ac7e029409e151f9ae3fc190,df4fcaecaf535e644ad07e7b1d1cae45,48e0ff8e48444594f80c0abdfdd74243,288b81f7c3c33ec45b556d056fe714f4",
                            "478970479e8bc1840a4a18514dc85e74|ENV_S01_Vanilla_Fan_02a_MO||0,6.183,0,0,0,0,1,1,1,1",
                        },
                        XLarge = new[]
                        {
                            "085fa80223a8f7544b6357a3ad4009f0|ENV_S02_MovableBox_05_MO|16bb7c62ac7e029409e151f9ae3fc190,df4fcaecaf535e644ad07e7b1d1cae45,48e0ff8e48444594f80c0abdfdd74243,288b81f7c3c33ec45b556d056fe714f4",
                            "478970479e8bc1840a4a18514dc85e74|ENV_S01_Vanilla_Fan_02a_MO||0,7.696,0,0,0,0,1,1,1,1",
                        },
                        Materials = new[] { "16bb7c62ac7e029409e151f9ae3fc190,df4fcaecaf535e644ad07e7b1d1cae45,48e0ff8e48444594f80c0abdfdd74243,288b81f7c3c33ec45b556d056fe714f4" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.party",
                        XSmall = new[]
                        {
                            "a8fbb958a8bd93549aa7faa29f324af4|ENV_S06_MovableBox_01",
                            "478970479e8bc1840a4a18514dc85e74|ENV_S01_Vanilla_Fan_02a_MO||0,1.4769,0,0,0,0,1,0.6,1,0.6",
                        },
                        Small = new[]
                        {
                            "a8fbb958a8bd93549aa7faa29f324af4|ENV_S06_MovableBox_02",
                            "478970479e8bc1840a4a18514dc85e74|ENV_S01_Vanilla_Fan_02a_MO||0,3.152,0,0,0,0,1,1,1,1",
                        },
                        Medium = new[]
                        {
                            "a8fbb958a8bd93549aa7faa29f324af4|ENV_S06_MovableBox_03",
                            "478970479e8bc1840a4a18514dc85e74|ENV_S01_Vanilla_Fan_02a_MO||0,4.5688,0,0,0,0,1,1,1,1",
                        },
                        Large = new[]
                        {
                            "a8fbb958a8bd93549aa7faa29f324af4|ENV_S06_MovableBox_04",
                            "478970479e8bc1840a4a18514dc85e74|ENV_S01_Vanilla_Fan_02a_MO||0,6.25,0,0,0,0,1,1,1,1",
                        },
                        XLarge = new[]
                        {
                            "a8fbb958a8bd93549aa7faa29f324af4|ENV_S06_MovableBox_05",
                            "478970479e8bc1840a4a18514dc85e74|ENV_S01_Vanilla_Fan_02a_MO||0,7.7693,0,0,0,0,1,1,1,1",
                        },
                        Materials = new[] { "ea071fb292211554386198f6a6cdf3a1" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.space",
                        XSmall = new[]
                        {
                            "b610671c2ce78c1458a81c3bc3fe98d6||66db45e99a64c6a408a8f1e71e2595cf",
                            "478970479e8bc1840a4a18514dc85e74|ENV_S01_Vanilla_Fan_02a_MO||0,1.4895,0,0,0,0,1,0.6,1,0.6",
                        },
                        Small = new[]
                        {
                            "314d36261935bde4298596ea39e721d4||66db45e99a64c6a408a8f1e71e2595cf",
                            "478970479e8bc1840a4a18514dc85e74|ENV_S01_Vanilla_Fan_02a_MO||0,2.9305,0,0,0,0,1,1,1,1",
                        },
                        Medium = new[]
                        {
                            "92e73fa7e79351d4c85eee73529b687a||66db45e99a64c6a408a8f1e71e2595cf",
                            "478970479e8bc1840a4a18514dc85e74|ENV_S01_Vanilla_Fan_02a_MO||0,4.3898,0,0,0,0,1,1,1,1",
                        },
                        Large = new[]
                        {
                            "1104ff7df1bf28a43bd6c8e1aa1b1daf||303b4690e4d4a444fa99c910f36346f1",
                            "478970479e8bc1840a4a18514dc85e74|ENV_S01_Vanilla_Fan_02a_MO||0,5.9567,0,0,0,0,1,1,1,1",
                        },
                        XLarge = new[]
                        {
                            "13bbea6539d48054fbc7cd01f28a7940||303b4690e4d4a444fa99c910f36346f1",
                            "478970479e8bc1840a4a18514dc85e74|ENV_S01_Vanilla_Fan_02a_MO||0,7.5477,0,0,0,0,1,1,1,1",
                        },
                        Materials = new[] { "66db45e99a64c6a408a8f1e71e2595cf" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.halloween",
                        XSmall = new[]
                        {
                            "09a6ddc4137b5cd4d974178a0806dc99",
                            "478970479e8bc1840a4a18514dc85e74|ENV_S01_Vanilla_Fan_02a_MO||0,1.9678,0,0,0,0,1,0.6,1,0.6",
                        },
                        Small = new[]
                        {
                            "09a6ddc4137b5cd4d974178a0806dc99",
                            "478970479e8bc1840a4a18514dc85e74|ENV_S01_Vanilla_Fan_02a_MO||0,3.1608,0,0,0,0,1,1,1,1",
                        },
                        Medium = new[]
                        {
                            "09a6ddc4137b5cd4d974178a0806dc99",
                            "478970479e8bc1840a4a18514dc85e74|ENV_S01_Vanilla_Fan_02a_MO||0,3.8818,0,0,0,0,1,1,1,1",
                        },
                        Large = new[]
                        {
                            "09a6ddc4137b5cd4d974178a0806dc99",
                            "478970479e8bc1840a4a18514dc85e74|ENV_S01_Vanilla_Fan_02a_MO||0,3.8831,0,0,0,0,1,1,1,1",
                        },
                        XLarge = new[]
                        {
                            "09a6ddc4137b5cd4d974178a0806dc99",
                            "478970479e8bc1840a4a18514dc85e74|ENV_S01_Vanilla_Fan_02a_MO||0,3.4803,0,0,0,0,1,1,1,1",
                        },
                        Materials = new[] { "ba096fc98b4279d46b134eefac74ad96,e89aedc7551d7b646869c5808993f9df" },
                    },
                },
            },

            new ObjectThemeFamily
            {
                Prefixes = new[] { "Placeable_Feature_Move_Box_" },
                Nodes = new[] { "Feature_Move_Box_Vanilla_" },
                Fit = ObjectThemeFit.Footprint,
                Themes = new[]
                {
                    new ObjectTheme { LocKey = "objectthemes.original" },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.medieval",
                        RotateX = -90f,
                        XSmall = new[] { "f016e9a7611a8be43b3a2d0c7cd78373|ENV_S02_MovableBox_01_MO|48e0ff8e48444594f80c0abdfdd74243,df4fcaecaf535e644ad07e7b1d1cae45,288b81f7c3c33ec45b556d056fe714f4" },
                        Small = new[] { "294d3914451f5b048b9340f7029b88e9|ENV_S02_MovableBox_02_MO|16bb7c62ac7e029409e151f9ae3fc190,df4fcaecaf535e644ad07e7b1d1cae45,48e0ff8e48444594f80c0abdfdd74243,288b81f7c3c33ec45b556d056fe714f4" },
                        Medium = new[] { "dcbbde6525d496b49957ee173707457b|ENV_S02_MovableBox_03_MO|48e0ff8e48444594f80c0abdfdd74243,16bb7c62ac7e029409e151f9ae3fc190,df4fcaecaf535e644ad07e7b1d1cae45,48e0ff8e48444594f80c0abdfdd74243,288b81f7c3c33ec45b556d056fe714f4" },
                        Large = new[] { "378e747fba29da24294d22bfb5ec5c81|ENV_S02_MovableBox_04_MO|16bb7c62ac7e029409e151f9ae3fc190,df4fcaecaf535e644ad07e7b1d1cae45,48e0ff8e48444594f80c0abdfdd74243,288b81f7c3c33ec45b556d056fe714f4" },
                        XLarge = new[] { "085fa80223a8f7544b6357a3ad4009f0|ENV_S02_MovableBox_05_MO|16bb7c62ac7e029409e151f9ae3fc190,df4fcaecaf535e644ad07e7b1d1cae45,48e0ff8e48444594f80c0abdfdd74243,288b81f7c3c33ec45b556d056fe714f4" },
                        Materials = new[] { "16bb7c62ac7e029409e151f9ae3fc190,df4fcaecaf535e644ad07e7b1d1cae45,48e0ff8e48444594f80c0abdfdd74243,288b81f7c3c33ec45b556d056fe714f4" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.party",
                        XSmall = new[] { PartyMoveBoxFbx + "|ENV_S06_MovableBox_01" },
                        Small = new[] { PartyMoveBoxFbx + "|ENV_S06_MovableBox_02" },
                        Medium = new[] { PartyMoveBoxFbx + "|ENV_S06_MovableBox_03" },
                        Large = new[] { PartyMoveBoxFbx + "|ENV_S06_MovableBox_04" },
                        XLarge = new[] { PartyMoveBoxFbx + "|ENV_S06_MovableBox_05" },
                        Materials = new[] { "ea071fb292211554386198f6a6cdf3a1" },
                    },
                    new ObjectTheme
                    {
                        LocKey = "objectthemes.space",
                        XSmall = new[] { "b610671c2ce78c1458a81c3bc3fe98d6||66db45e99a64c6a408a8f1e71e2595cf" },
                        Small = new[] { "314d36261935bde4298596ea39e721d4||66db45e99a64c6a408a8f1e71e2595cf" },
                        Medium = new[] { "92e73fa7e79351d4c85eee73529b687a||66db45e99a64c6a408a8f1e71e2595cf" },
                        Large = new[] { "1104ff7df1bf28a43bd6c8e1aa1b1daf||303b4690e4d4a444fa99c910f36346f1" },
                        XLarge = new[] { "13bbea6539d48054fbc7cd01f28a7940||303b4690e4d4a444fa99c910f36346f1" },
                        Materials = new[] { "66db45e99a64c6a408a8f1e71e2595cf" },
                    },
                    new ObjectTheme
                    {
                        // Halloween ships its own box mesh; the recolour this replaced put the HW atlas
                        // on the vanilla box, which is why its uvs were wrong.
                        LocKey = "objectthemes.halloween",
                        Medium = new[] { "09a6ddc4137b5cd4d974178a0806dc99" },
                        Materials = new[] { "ba096fc98b4279d46b134eefac74ad96,e89aedc7551d7b646869c5808993f9df" },
                    },
                },
            },
        };
    }
}
