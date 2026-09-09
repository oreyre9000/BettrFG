using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Customization.Player;
using BetterFG.Nametag;
using BetterFG.Network;
using BetterFG.UI;
using BetterFG.UI.Tabs;
using BetterFG.Utilities;
using Character;
using FG.Common;
using FG.Common.Fraggle;
using FGClient;
using MPG.Utility;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BetterFG.Features.Replay
{
    public struct ReplayFrame
    {
        public float t;
        public Vector3 pos;
        public Quaternion rot;
        public int stateHash;
        public float animTime;
        public bool ragdoll;
        public Quaternion upperBody;
        public Quaternion armLeft;
        public Quaternion armRight;
    }

    public class ReplayPlayer
    {
        public uint playerId;
        public string name = "";
        public string generatedName = "";
        public string accountId = "";
        public string platformId = "";
        public int teamId;
        public uint squadId;
        public string partyId = "";
        public bool isLocal;
        public bool isBot;
        public string colour = "";
        public string pattern = "";
        public string costumeTop = "";
        public string costumeBottom = "";
        public string costumeFull = "";
        public string faceplate = "";
        public string victoryPose = "";
        public string nickname = "";
        public string nameplate = "";
        public int fameEarnedBadge;
        public int crownsEarned;
        public DateTime fameUpdatedAt;
        public float bfgScale;
        public string bfgCosmetics = "";
        public string bfgColour = "";
        public string bfgPattern = "";
        public string bfgFaceplate = "";
        public RemoteNametagInfo nametag;
        public float outTime = -1f;
        public float inTime;
#if MACHINIMA
        public readonly List<MachinimaMove> moves = new List<MachinimaMove>();
        public readonly List<MachinimaAction> actions = new List<MachinimaAction>();
#endif
        public readonly List<RemoteSkinEntry> bfgSkins = new List<RemoteSkinEntry>();
        public readonly List<SkinTexEntry> bfgTextures = new List<SkinTexEntry>();
        public readonly List<ReplayFrame> frames = new List<ReplayFrame>();
    }

    public class ReplaySet
    {
        public string key = "";
        public string chosen = "";
        public string path = "";
    }

    public class ReplayGhost
    {
        public string name = "";
        public readonly List<ReplayFrame> frames = new List<ReplayFrame>();
    }

    public struct ReplayAudioEvent
    {
        public float t;
        public float end;
        public uint playerId;
        public Vector3 pos;
        public int key;
        public int paramStart;
        public int paramCount;
    }

    public struct ReplayStarchartEvent
    {
        public float t;
        public int pathStart;
        public int pathCount;
    }

    public struct ReplayVfxEvent
    {
        public float t;
        public uint playerId;
    }

    public struct ReplayTailEvent
    {
        public float t;
        public uint playerId;
        public bool enabled;
    }

    public enum ReplayPowerupKind { RubberChicken, Invisibility }

    public struct ReplayPowerupEvent
    {
        public float t;
        public uint playerId;
        public int kind;
        public bool on;
    }

    public struct ReplayAudioParam
    {
        public int name;
        public float value;
    }

    public enum ReplayCameraType { Free, Fixed, Gameplay, FixedObject }

    public enum ReplayLookAt { FixedRotation, Player, Object }

    public enum ReplayEasingCurve { Linear, Constant, Sine, Quadratic, Cubic, Quartic, Quintic, Exponential, Circular, Back, Elastic, Bounce }

    public enum ReplayEasingDirection { In, Out, InOut, OutIn }

    public enum ReplayShakeKind { None, Explosion, Continuous }

    public static class ReplayShake
    {
        public const int TierCount = 5;
        public static readonly string[] TierLabels = { "XS", "S", "M", "L", "XL" };

        static readonly float[] ExplosionRotDeg = { 0.6f, 1.2f, 2.2f, 3.5f, 5.5f };
        static readonly float[] ExplosionFovDeg = { 1.0f, 2.0f, 3.5f, 5.5f, 8.0f };
        static readonly float[] ExplosionDuration = { 0.35f, 0.5f, 0.7f, 0.9f, 1.2f };
        const float ExplosionRotHz = 11f;
        const float ExplosionFovHz = 8f;

        static readonly float[] ContinuousRotDeg = { 0.15f, 0.35f, 0.7f, 1.3f, 2.2f };
        static readonly float[] ContinuousFovDeg = { 0.3f, 0.6f, 1.2f, 2.2f, 3.5f };
        const float ContinuousRotHz = 1.75f;
        const float ContinuousFovHz = 1.25f;

        static float Noise(float time, float hz, float seed) =>
            Mathf.PerlinNoise(time * hz + seed, seed * 0.37f) * 2f - 1f;

        public static void Evaluate(ReplayKeyframe a, ReplayKeyframe b, float raw, float time, float noiseTime, out Vector3 rotEuler, out float fov)
        {
            rotEuler = Vector3.zero;
            fov = 0f;
            if (a == null) return;

            bool blending = b != null && !b.cut;

            if (a.shakeKind == ReplayShakeKind.Explosion)
                AddExplosion(a, time, noiseTime, ref rotEuler, ref fov);
            else if (a.shakeKind == ReplayShakeKind.Continuous)
                AddContinuous(a, noiseTime, blending ? 1f - raw : 1f, ref rotEuler, ref fov);

            if (blending && b.shakeKind == ReplayShakeKind.Continuous)
                AddContinuous(b, noiseTime, raw, ref rotEuler, ref fov);
        }

        static void AddExplosion(ReplayKeyframe k, float time, float noiseTime, ref Vector3 rotEuler, ref float fov)
        {
            int tier = Mathf.Clamp(k.shakeTier, 0, TierCount - 1);
            float elapsed = time - k.time;
            float duration = ExplosionDuration[tier];
            if (elapsed < 0f || elapsed > duration) return;

            float envelope = 1f - elapsed / duration;
            envelope *= envelope;
            float seed = k.time * 12.9898f;

            rotEuler += new Vector3(
                Noise(noiseTime, ExplosionRotHz, seed + 1f),
                Noise(noiseTime, ExplosionRotHz, seed + 2f),
                Noise(noiseTime, ExplosionRotHz, seed + 3f)) * (ExplosionRotDeg[tier] * envelope);
            fov += Noise(noiseTime, ExplosionFovHz, seed + 4f) * (ExplosionFovDeg[tier] * envelope);
        }

        static void AddContinuous(ReplayKeyframe k, float noiseTime, float weight, ref Vector3 rotEuler, ref float fov)
        {
            if (weight <= 0f) return;
            int tier = Mathf.Clamp(k.shakeTier, 0, TierCount - 1);
            float seed = k.time * 12.9898f;

            rotEuler += new Vector3(
                Noise(noiseTime, ContinuousRotHz, seed + 1f),
                Noise(noiseTime, ContinuousRotHz, seed + 2f),
                Noise(noiseTime, ContinuousRotHz, seed + 3f)) * (ContinuousRotDeg[tier] * weight);
            fov += Noise(noiseTime, ContinuousFovHz, seed + 4f) * (ContinuousFovDeg[tier] * weight);
        }
    }

    public static class ReplayEase
    {
        public const int CurveCount = 12;
        public const int DirectionCount = 4;

        public static float Apply(ReplayEasingCurve curve, ReplayEasingDirection direction, float t)
        {
            t = Mathf.Clamp01(t);
            if (curve == ReplayEasingCurve.Linear) return t;
            if (curve == ReplayEasingCurve.Constant) return 0f;

            switch (direction)
            {
                case ReplayEasingDirection.Out:
                    return 1f - In(curve, 1f - t);
                case ReplayEasingDirection.InOut:
                    return t < 0.5f ? In(curve, t * 2f) * 0.5f : 1f - In(curve, 2f - t * 2f) * 0.5f;
                case ReplayEasingDirection.OutIn:
                    return t < 0.5f ? (1f - In(curve, 1f - t * 2f)) * 0.5f : 0.5f + In(curve, t * 2f - 1f) * 0.5f;
                default:
                    return In(curve, t);
            }
        }

        static float In(ReplayEasingCurve curve, float t)
        {
            switch (curve)
            {
                case ReplayEasingCurve.Sine: return 1f - Mathf.Cos(t * Mathf.PI * 0.5f);
                case ReplayEasingCurve.Quadratic: return t * t;
                case ReplayEasingCurve.Cubic: return t * t * t;
                case ReplayEasingCurve.Quartic: return t * t * t * t;
                case ReplayEasingCurve.Quintic: return t * t * t * t * t;
                case ReplayEasingCurve.Exponential: return t <= 0f ? 0f : Mathf.Pow(2f, 10f * (t - 1f));
                case ReplayEasingCurve.Circular: return 1f - Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
                case ReplayEasingCurve.Back: return 2.70158f * t * t * t - 1.70158f * t * t;
                case ReplayEasingCurve.Elastic:
                    if (t <= 0f || t >= 1f) return t;
                    return -Mathf.Pow(2f, 10f * t - 10f) * Mathf.Sin((t * 10f - 10.75f) * (2f * Mathf.PI / 3f));
                case ReplayEasingCurve.Bounce: return 1f - BounceOut(1f - t);
                default: return t;
            }
        }

        static float BounceOut(float t)
        {
            const float n = 7.5625f;
            const float d = 2.75f;
            if (t < 1f / d) return n * t * t;
            if (t < 2f / d) { t -= 1.5f / d; return n * t * t + 0.75f; }
            if (t < 2.5f / d) { t -= 2.25f / d; return n * t * t + 0.9375f; }
            t -= 2.625f / d;
            return n * t * t + 0.984375f;
        }
    }

    public class ReplayKeyframe
    {
        public float time;
        public ReplayCameraType cameraType = ReplayCameraType.Free;
        public ReplayLookAt lookAt = ReplayLookAt.FixedRotation;
        public ReplayEasingCurve easingCurve = ReplayEasingCurve.Linear;
        public ReplayEasingDirection easingDirection = ReplayEasingDirection.InOut;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public float fov;
        public uint targetPlayerId;
        public uint lookAtPlayerId;
        public int targetObject = -1;
        public int lookAtObject = -1;
        public float speed = 1f;
        public bool cut;
        public bool cutToNext;
        public ReplayShakeKind shakeKind = ReplayShakeKind.None;
        public int shakeTier = 2;

        public static readonly float[] Speeds = { 0.1f, 0.25f, 0.5f, 0.75f, 1f, 1.5f, 2f, 3f, 4f };

        public static string ShakeKindLabel(ReplayShakeKind k) =>
            k == ReplayShakeKind.None ? "None" : k == ReplayShakeKind.Explosion ? "Explosion" : "Continuous";

        public static string SpeedLabel(float speed) => speed.ToString("0.##") + "x";

        public static string CutLabel(bool cut) => cut ? "Hard cut" : "Blend in";

        public static string TypeLabel(ReplayCameraType t) =>
            t == ReplayCameraType.Free ? "Free camera"
            : t == ReplayCameraType.Fixed ? "Fixed camera"
            : t == ReplayCameraType.Gameplay ? "Gameplay camera" : "Fixed to object";

        public static string LookAtLabel(ReplayLookAt l) =>
            l == ReplayLookAt.FixedRotation ? "Fixed rotation"
            : l == ReplayLookAt.Player ? "Certain player" : "Certain object";

        public static string EasingLabel(ReplayEasingCurve c)
        {
            switch (c)
            {
                case ReplayEasingCurve.Constant: return "Constant";
                case ReplayEasingCurve.Sine: return "Sine";
                case ReplayEasingCurve.Quadratic: return "Quadratic";
                case ReplayEasingCurve.Cubic: return "Cubic";
                case ReplayEasingCurve.Quartic: return "Quartic";
                case ReplayEasingCurve.Quintic: return "Quintic";
                case ReplayEasingCurve.Exponential: return "Exponential";
                case ReplayEasingCurve.Circular: return "Circular";
                case ReplayEasingCurve.Back: return "Back";
                case ReplayEasingCurve.Elastic: return "Elastic";
                case ReplayEasingCurve.Bounce: return "Bounce";
                default: return "Linear";
            }
        }

        public static string DirectionLabel(ReplayEasingDirection d)
        {
            switch (d)
            {
                case ReplayEasingDirection.Out: return "Out";
                case ReplayEasingDirection.InOut: return "In & out";
                case ReplayEasingDirection.OutIn: return "Out & in";
                default: return "In";
            }
        }
    }

    public enum ReplayVisibilityMode { All, None, Only }

    public class ReplayVisibilityKeyframe
    {
        public float time;
        public bool showPhrases = true;
        public ReplayVisibilityMode crowns = ReplayVisibilityMode.All;
        public readonly List<uint> crownOnlyPlayers = new List<uint>();
        public ReplayVisibilityMode names = ReplayVisibilityMode.All;
        public readonly List<uint> nameOnlyPlayers = new List<uint>();
        public ReplayVisibilityMode players = ReplayVisibilityMode.All;
        public readonly List<uint> onlyPlayers = new List<uint>();
        public bool showGhosts = true;

        public static string PlayersLabel(ReplayVisibilityMode m) =>
            m == ReplayVisibilityMode.All ? "All" : m == ReplayVisibilityMode.None ? "None" : "Only";
    }

    public class ReplayPostFxKeyframe
    {
        public float time;
        public float exposure;
        public float contrast;
        public float saturation;
        public float temperature;
        public float tint;
        public float vignette;
        public float chromaticAberration;
        public float bloomIntensity;
        public float bloomThreshold = 1f;
        public float sharpenAmount;
        public float sharpenRadius = 1f;
        public float dofStrength;
        public float dofDistance = 10f;

        public const float ExposureMin = -3f, ExposureMax = 3f, ExposureStep = 0.1f;
        public const float GradeMin = -100f, GradeMax = 100f, GradeStep = 5f;
        public const float VignetteMin = 0f, VignetteMax = 1f, VignetteStep = 0.05f;
        public const float ChromaMin = 0f, ChromaMax = 1f, ChromaStep = 0.05f;
        public const float BloomIntensityMin = 0f, BloomIntensityMax = 5f, BloomIntensityStep = 0.25f;
        public const float BloomThresholdMin = 0f, BloomThresholdMax = 2f, BloomThresholdStep = 0.1f;
        public const float SharpenAmountMin = 0f, SharpenAmountMax = 5f, SharpenAmountStep = 0.25f;
        public const float SharpenRadiusMin = 0.25f, SharpenRadiusMax = 4f, SharpenRadiusStep = 0.25f;
        public const float DofStrengthMin = 0f, DofStrengthMax = 1f, DofStrengthStep = 0.05f;
        public const float DofDistanceMin = 0.5f, DofDistanceMax = 80f;
    }

    public class ReplayRecording
    {
        public int version = SaveReplay.FormatVersion;
        public string name = "";
        public string recordedAt = "";
        public string buildHash = "";
        public string roundId = "";
        public string roundName = "";
        public string sceneName = "";
        public string archetypeId = "";
        public string shareCode = "";
        public int levelVersion;
        public string backgroundScene = "";
        public string levelJson = "";
        public bool isUgc;
        public bool isMachinima;
        public bool isFinal;
        public uint squadSize;
        public float duration;
        public float trimStart;
        public float trimEnd;
        public string sourcePath = "";
        public byte[] thumbJpg;
        public readonly List<ReplaySet> sets = new List<ReplaySet>();
        public readonly List<string> starchartPaths = new List<string>();
        public readonly List<ReplayStarchartEvent> starchartEvents = new List<ReplayStarchartEvent>();
        public readonly List<ReplayVfxEvent> diveSlideVfxEvents = new List<ReplayVfxEvent>();
        public readonly List<ReplayTailEvent> tailEvents = new List<ReplayTailEvent>();
        public readonly List<ReplayPowerupEvent> powerupEvents = new List<ReplayPowerupEvent>();
        public string tailPrefab = "";
        public string tailBoneName = "";
        public Vector3 tailLocalPos;
        public Quaternion tailLocalRot = Quaternion.identity;
        public Vector3 tailLocalScale = Vector3.one;
        public string petId = "";
        public readonly List<ReplayFrame> petFrames = new List<ReplayFrame>();
        public readonly List<ReplayPlayer> players = new List<ReplayPlayer>();
        public readonly List<ReplayGhost> ghosts = new List<ReplayGhost>();
        public readonly List<ReplayKeyframe> keyframes = new List<ReplayKeyframe>();
        public readonly List<ReplayVisibilityKeyframe> visibilityKeyframes = new List<ReplayVisibilityKeyframe>();
        public readonly List<ReplayPostFxKeyframe> postFxKeyframes = new List<ReplayPostFxKeyframe>();
        public readonly List<ReplayFrame> cameraFrames = new List<ReplayFrame>();
        public readonly List<ReplayObject> worldObjects = new List<ReplayObject>();
        public readonly List<string> audioKeys = new List<string>();
        public readonly List<string> audioParamNames = new List<string>();
        public readonly List<ReplayAudioParam> audioParams = new List<ReplayAudioParam>();
        public readonly List<ReplayAudioEvent> audioEvents = new List<ReplayAudioEvent>();
        public readonly List<ReplaySpeechOption> speechOptions = new List<ReplaySpeechOption>();
        public readonly List<ReplaySpeechEvent> speechEvents = new List<ReplaySpeechEvent>();

        public void SortKeyframes() => keyframes.Sort((a, b) => a.time.CompareTo(b.time));
        public void SortVisibilityKeyframes() => visibilityKeyframes.Sort((a, b) => a.time.CompareTo(b.time));
        public void SortPostFxKeyframes() => postFxKeyframes.Sort((a, b) => a.time.CompareTo(b.time));
    }

    internal static class FeatureReplay
    {
        public const float SampleHz = 30f;
        const float THUMB_AT = 10f;

        public static readonly BfgFeature feature = new BfgFeature("replay", "ui.replays_2", true, new List<FeatureSetting>
        {
            new FeatureSetting { id = "record", label = "ui.auto_record_rounds", defaultOn = false },
        },
        note: "Saves what happened each round to AppData/BettrFG/Replays.");

        public static bool AutoRecord => FeatureRegistry.IsOn("replay", "record")
            && BetterFG.Services.SettingsService.Get("debug.disable_replay", "false") != "true";

        public static void SetAutoRecord(bool on)
        {
            if (on) feature.SetEnabled(true);
            feature.Set("record", on);
        }

        class Tracked
        {
            public ReplayPlayer player;
            public Transform tf;
            public Animator anim;
            public Transform animTf;
            public IntPtr beanPtr;
            public Vector3 lastPos;
            public Transform upperBody;
            public Transform armLeft;
            public Transform armRight;
            public GameObject chicken;
            public Levels.Invisibeans.InvisibilityVisualsController invis;
            public bool chickenOn;
            public bool invisOn;
        }

        static ReplayRecording _live;
        static int _recGen;
        static Transform _gameCam;
        static Animator _petAnim;
        static IntPtr _petAnimPtr;
        static RenderTexture _thumbRt;
        static ClientGameStateView _gsv;
        static long _worldTicks;
        static long _playerTicks;
        static int _sampledFrames;
        static readonly Dictionary<uint, Tracked> _tracked = new Dictionary<uint, Tracked>();
        static readonly Dictionary<IntPtr, uint> _beanOwners = new Dictionary<IntPtr, uint>();
        static readonly Dictionary<IntPtr, FallGuysCharacterController> _animBeans = new Dictionary<IntPtr, FallGuysCharacterController>();
        static bool _culledRemotes = true;
        static readonly Dictionary<string, int> _keyIds = new Dictionary<string, int>();
        static readonly Dictionary<string, int> _paramIds = new Dictionary<string, int>();
        static readonly Dictionary<int, int> _speechIds = new Dictionary<int, int>();
        static readonly Dictionary<uint, float> _lastDiveSlideTimes = new Dictionary<uint, float>();
        static readonly Dictionary<IntPtr, int> _pairKeys = new Dictionary<IntPtr, int>();
        static readonly Dictionary<IntPtr, int[]> _paramSets = new Dictionary<IntPtr, int[]>();
        static readonly Dictionary<IntPtr, int> _openSounds = new Dictionary<IntPtr, int>();
        static readonly HashSet<string> _audioManagerKeys = new HashSet<string>();
        static readonly Dictionary<string, float> _strayAudioAt = new Dictionary<string, float>();

        public static ReplayRecording Live => _live;

        public static float GameplayTime => _gsv?.GameplayTimeElapsed ?? 0f;

        static void SweepOrphanedViewerObjects()
        {
            if (ReplayViewer.Instance != null) return;

            int killed = 0;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || go.transform.parent != null || !go.scene.IsValid()) continue;
                if (!go.name.StartsWith("BettrFG_Replay", StringComparison.Ordinal)
                    && !go.name.StartsWith("BettrFG_Ghost_", StringComparison.Ordinal)) continue;
                UnityEngine.Object.Destroy(go);
                killed++;
            }
            if (killed > 0) Plugin.Log.LogInfo($"swept {killed} orphaned replay objects before this round started");
        }

        public static void OnCleanupLoadingScreens()
        {
            SweepOrphanedViewerObjects();

            ReplayThumbnail.Release(_thumbRt);
            _thumbRt = null;
            if (!AutoRecord) { _live = null; ReplayCaptureHooks.SetActive(false); return; }

            var rec = new ReplayRecording
            {
                recordedAt = DateTime.UtcNow.ToString("o"),
                buildHash = BetterFGInfo.BuildHash,
            };

            ClientGameManager cgm = null;
            var gsv = GlobalGameStateClient.Instance?.GameStateView;
            if (gsv != null) gsv.GetLiveClientGameManager(out cgm);

            if (cgm != null)
            {
                rec.isUgc = cgm.IsUGCRound;
                rec.isFinal = cgm.IsFinalRound;
                rec.squadSize = cgm.SquadSize;

                var round = cgm._round;
                if (round != null)
                {
                    rec.roundId = round.Id ?? "";
                    rec.roundName = round.DisplayNameUnindented ?? "";
                    rec.archetypeId = round.Archetype?.Id ?? "";
                    try { rec.sceneName = round.GetSceneName() ?? ""; }
                    catch (Exception ex) { Plugin.Log.LogWarning($"replay: no scene name off the round ({ex.Message})"); }
                }
            }

            if (string.IsNullOrEmpty(rec.sceneName))
                rec.sceneName = gsv?.CurrentGameLevelName ?? "";

            if (rec.isUgc) CaptureCreativeLevel(rec);

            var audio = UnityEngine.Object.FindObjectOfType<AudioManager>();
            if (audio != null)
            {
                _culledRemotes = audio.RemovePlayersAudioActive;
                audio.RemovePlayersAudioActive = false;
                if (_culledRemotes) Plugin.Log.LogInfo("other players' audio was being culled, switched that off so the replay actually gets their sounds");
            }

            _tracked.Clear();
            _beanOwners.Clear();
            _animBeans.Clear();
            _keyIds.Clear();
            _paramIds.Clear();
            _speechIds.Clear();
            _pairKeys.Clear();
            _paramSets.Clear();
            _openSounds.Clear();
            _audioManagerKeys.Clear();
            _strayAudioAt.Clear();
            _lastDiveSlideTimes.Clear();
            ReplaySlideEvent.Reset();
            _gameCam = null;
            _petAnim = null;
            _petAnimPtr = IntPtr.Zero;
            _worldTicks = 0;
            _playerTicks = 0;
            _sampledFrames = 0;
            _gsv = gsv;
            _live = rec;
            ReplayCaptureHooks.SetActive(true);

            if (cgm?._clientPlayerManager?._playerIdIndex != null)
            {
                var cpm = cgm._clientPlayerManager;
                foreach (var kvp in cpm._playerIdIndex)
                {
                    if (BetterFG.Utilities.BeanNetworkUtil.IsFakeBean(kvp.Key)) continue;
                    var data = kvp.Value;
                    if (data == null) continue;
                    var p = Resolve(kvp.Key);
                    p.name = data.playerKey ?? p.name;
                    p.accountId = data.accountID ?? p.accountId;
                    p.platformId = data.platformID ?? p.platformId;
                    p.teamId = data.TeamID;
                    p.squadId = data.SquadID;
                    p.partyId = data.partyId ?? p.partyId;
                    p.isLocal = data.isLocalPlayer;
                    p.isBot = data.isBot;
                    ReadCustomisation(p, cpm.GetPlayerCustomisationSelection(kvp.Key));
                }
            }

            ReplayWorldRecorder.Begin(rec, _gsv != null ? _gsv.GameplayTimeElapsed : 0f);

            _recGen++;
            BetterFGUIMan.Instance.StartCoroutine(RecordCoroutine(_recGen).WrapToIl2Cpp());
            Plugin.Log.LogInfo($"replay recording {rec.roundName} ({rec.sceneName}), {rec.sets.Count} sets, {rec.players.Count} players in already");
        }

        static IEnumerator RecordCoroutine(int gen)
        {
            const float step = 1f / SampleHz;
            float due = step;
            int ticks = 0;

            while (_live != null && _recGen == gen)
            {
                yield return null;
                if (_live == null || _recGen != gen) yield break;
                if (_gsv is null) continue;

                float t = _gsv.GameplayTimeElapsed;
                float dt = Time.unscaledDeltaTime;

                long mark = Stopwatch.GetTimestamp();
                ReplayWorldRecorder.Sample(t, dt);
                long afterWorld = Stopwatch.GetTimestamp();
                _worldTicks += afterWorld - mark;
                _sampledFrames++;

                due -= dt;
                if (due > 0f) continue;
                due = due < -step ? step : due + step;

                ClientGameManager cgm = null;
                _gsv.GetLiveClientGameManager(out cgm);
                var index = cgm?._clientPlayerManager?._playerIdIndex;
                if (index == null) continue;

                if (_gameCam == null) _gameCam = FallGuysLib.Camera.CameraUtils.GetMainCameraTransform();
                if (_gameCam != null)
                {
                    _gameCam.GetPositionAndRotation(out var camPos, out var camRot);
                    _live.cameraFrames.Add(new ReplayFrame { t = t, pos = camPos, rot = camRot });
                }

                if (_thumbRt == null && t >= THUMB_AT)
                    _thumbRt = ReplayThumbnail.Grab(FallGuysLib.Camera.CameraUtils.GetMainCamera());

                bool scaleTick = (++ticks & 31) == 0;
                var ident = Quaternion.identity;

                foreach (var kvp in index)
                {
                    if (BetterFG.Utilities.BeanNetworkUtil.IsFakeBean(kvp.Key)) continue;
                    var fgcc = kvp.Value?.fgcc;
                    if (fgcc is null) continue;

                    IntPtr beanPtr = fgcc.m_CachedPtr;
                    if (beanPtr == IntPtr.Zero) continue;

                    if (!_tracked.TryGetValue(kvp.Key, out var tracked) || tracked.beanPtr != beanPtr)
                    {
                        var found = BeanAnimationUtil.FindAnimator(fgcc.gameObject);
                        var rag = fgcc.GetComponentInChildren<FG.Common.Character.RagdollController>(true);
                        tracked = new Tracked
                        {
                            player = Resolve(kvp.Key),
                            tf = fgcc.transform,
                            anim = found,
                            animTf = found != null ? found.transform : null,
                            beanPtr = beanPtr,
                            upperBody = rag?.GetJoint(FG.Common.Character.RagdollJoint.ID.UpperBody)?.CachedTransform,
                            armLeft = rag?.GetJoint(FG.Common.Character.RagdollJoint.ID.ArmLeft)?.CachedTransform,
                            armRight = rag?.GetJoint(FG.Common.Character.RagdollJoint.ID.ArmRight)?.CachedTransform,
                        };
                        _tracked[kvp.Key] = tracked;
                        _beanOwners[beanPtr] = kvp.Key;
                        if (found != null) _animBeans[found.m_CachedPtr] = fgcc;

                        var chickenCtl = fgcc.GetComponentInChildren<RubberChickenController>(true);
                        tracked.chicken = chickenCtl != null ? chickenCtl.RubberChickenObject : null;
                        tracked.invis = fgcc.GetComponentInChildren<Levels.Invisibeans.InvisibilityVisualsController>(true);

                        var accessory = fgcc.GetComponentInChildren<Levels.Tag.TailTagAccessory>(true);
                        if (accessory != null) CaptureTailState(accessory, accessory.AccessoryEnabled);
                    }

                    int sh = 0;
                    float at = 0f;
                    if (tracked.anim is not null && tracked.anim.m_CachedPtr != IntPtr.Zero)
                    {
                        var info = tracked.anim.GetCurrentAnimatorStateInfo(0);
                        sh = info.shortNameHash;
                        at = info.normalizedTime;
                        if (scaleTick) tracked.player.bfgScale = tracked.animTf.lossyScale.x;
                    }

                    bool ragged = tracked.upperBody is not null && tracked.upperBody.m_CachedPtr != IntPtr.Zero;
                    Quaternion upper = ident, armL = ident, armR = ident;
                    if (ragged)
                    {
                        tracked.upperBody.GetLocalPositionAndRotation(out _, out upper);
                        if (tracked.armLeft is not null) tracked.armLeft.GetLocalPositionAndRotation(out _, out armL);
                        if (tracked.armRight is not null) tracked.armRight.GetLocalPositionAndRotation(out _, out armR);
                    }

                    // EnableRubberChickenObject/EnablePowerupVersion both got hooked and neither ever fired
                    // — il2cpp inlines them into their callers. reading the result is immune to that
                    if (tracked.chicken is not null && tracked.chicken.m_CachedPtr != IntPtr.Zero
                        && tracked.chicken.activeSelf != tracked.chickenOn)
                    {
                        tracked.chickenOn = !tracked.chickenOn;
                        _live.powerupEvents.Add(new ReplayPowerupEvent { t = t, playerId = kvp.Key, kind = (int)ReplayPowerupKind.RubberChicken, on = tracked.chickenOn });
                    }
                    if (tracked.invis is not null && tracked.invis.m_CachedPtr != IntPtr.Zero
                        && tracked.invis.IsPowerupMode != tracked.invisOn)
                    {
                        tracked.invisOn = !tracked.invisOn;
                        _live.powerupEvents.Add(new ReplayPowerupEvent { t = t, playerId = kvp.Key, kind = (int)ReplayPowerupKind.Invisibility, on = tracked.invisOn });
                    }

                    tracked.tf.GetPositionAndRotation(out var pos, out var rot);
                    tracked.lastPos = pos;
                    tracked.player.frames.Add(new ReplayFrame
                    {
                        t = t,
                        pos = pos,
                        rot = rot,
                        stateHash = sh,
                        animTime = at,
                        ragdoll = ragged,
                        upperBody = upper,
                        armLeft = armL,
                        armRight = armR,
                    });
                }

                CapturePet(t);

                _playerTicks += Stopwatch.GetTimestamp() - afterWorld;
            }
        }

        static void CapturePet(float t)
        {
            var fgcc = BetterFG.Customization.Pets.PetService.Instance?.LiveFgcc;
            if (fgcc is null || fgcc.m_CachedPtr == IntPtr.Zero) return;

            if (_petAnimPtr != fgcc.m_CachedPtr)
            {
                _petAnimPtr = fgcc.m_CachedPtr;
                _petAnim = BeanAnimationUtil.FindAnimator(fgcc.gameObject);
                if (_petAnim == null) _petAnim = fgcc.GetComponentInChildren<Animator>(true);
            }

            fgcc.transform.GetPositionAndRotation(out var pos, out var rot);

            int sh = 0;
            float at = 0f;
            if (_petAnim is not null && _petAnim.m_CachedPtr != IntPtr.Zero)
            {
                var info = _petAnim.GetCurrentAnimatorStateInfo(0);
                sh = info.shortNameHash;
                at = info.normalizedTime;
            }

            _live.petFrames.Add(new ReplayFrame { t = t, pos = pos, rot = rot, stateHash = sh, animTime = at });
        }

        static void CaptureCreativeLevel(ReplayRecording rec)
        {
            var fcm = SingletonBehaviour<FraggleCommonManager>.Instance;
            if (fcm == null)
            {
                Plugin.Log.LogWarning("creative round with no FraggleCommonManager around, so no level to save with the replay");
                return;
            }

            rec.backgroundScene = fcm.BackgroundSceneToLoad ?? "";
            rec.levelJson = fcm.LevelLoader?.TryCast<LevelLoader>()?.WholeFile ?? "";

            if (rec.roundId.StartsWith("ugc-"))
            {
                string code = rec.roundId.Substring(4);
                int cut = code.LastIndexOf('_');
                if (cut > 0 && int.TryParse(code.Substring(cut + 1), out int version))
                {
                    rec.levelVersion = version;
                    code = code.Substring(0, cut);
                }
                rec.shareCode = code;
            }

            if (string.IsNullOrEmpty(rec.levelJson) && !string.IsNullOrEmpty(rec.shareCode))
                rec.levelJson = fcm.FraggleLevelsCache?.GetLevelJSONFromCache(rec.shareCode, null) ?? "";

            Plugin.Log.LogInfo($"creative level {rec.shareCode} v{rec.levelVersion} on {rec.backgroundScene}, {rec.levelJson.Length} chars of level json saved with it");
        }

        public static void OnPlayerSpawned(uint playerId, string accountId, string platformId, string playerName,
            string playerGeneratedName, int teamId, uint squadId, string partyId,
            CustomisationSelections customisationSelections, bool isLocalPlayer, bool isNPC)
        {
            if (_live == null || BetterFG.Utilities.BeanNetworkUtil.IsFakeBean(playerId)) return;

            var p = Resolve(playerId);
            if (!string.IsNullOrEmpty(playerName)) p.name = playerName;
            if (!string.IsNullOrEmpty(playerGeneratedName)) p.generatedName = playerGeneratedName;
            if (!string.IsNullOrEmpty(accountId)) p.accountId = accountId;
            if (!string.IsNullOrEmpty(platformId)) p.platformId = platformId;
            p.teamId = teamId;
            p.squadId = squadId;
            if (!string.IsNullOrEmpty(partyId)) p.partyId = partyId;
            p.isLocal = isLocalPlayer;
            p.isBot = isNPC;
            ReadCustomisation(p, customisationSelections);
        }

        public static void OnServerPlayerProgress(GameMessageServerPlayerProgress progressMessage)
        {
            if (_live == null || progressMessage == null) return;
            if (BetterFG.Utilities.BeanNetworkUtil.IsFakeBean(progressMessage.playerId)) return;
            var p = Resolve(progressMessage.playerId);
            if (p.outTime < 0f) p.outTime = GameplayTime;
        }

        public static void BeginGhostSpawn() => ReplayWorldRecorder.PushSuppressSpawns();

        public static void EndGhostSpawn() => ReplayWorldRecorder.PopSuppressSpawns();

        public static void OnGhostSpawned(string name, List<(float t, Vector3 pos, Quaternion rot, int stateHash, float animTime)> frames)
        {
            if (_live == null || frames == null || frames.Count == 0) return;

            var ghost = new ReplayGhost { name = name };
            foreach (var (t, pos, rot, sh, at) in frames)
                ghost.frames.Add(new ReplayFrame { t = t, pos = pos, rot = rot, stateHash = sh, animTime = at });
            _live.ghosts.Add(ghost);
            Plugin.Log.LogInfo($"replay: ghost '{name}' baked in, {ghost.frames.Count} frames");
        }

        public static void OnClientGameManagerShutdown()
        {
            var rec = _live;
            _live = null;
            ReplayCaptureHooks.SetActive(false);
            ReplayWorldRecorder.End();
            _tracked.Clear();
            _beanOwners.Clear();
            _animBeans.Clear();
            _keyIds.Clear();
            _paramIds.Clear();
            _speechIds.Clear();
            _pairKeys.Clear();
            _paramSets.Clear();
            _openSounds.Clear();
            _gameCam = null;
            _petAnim = null;
            _petAnimPtr = IntPtr.Zero;
            if (rec == null) { _gsv = null; return; }

            rec.duration = _gsv?.GameplayTimeElapsed ?? 0f;
            _gsv = null;

            if (rec.petFrames.Count > 0)
                rec.petId = BetterFG.Customization.Pets.PetStore.ActivePetId ?? "";

            var audio = UnityEngine.Object.FindObjectOfType<AudioManager>();
            if (audio != null) audio.RemovePlayersAudioActive = _culledRemotes;
            rec.thumbJpg = ReplayThumbnail.Encode(_thumbRt);
            _thumbRt = null;
            CaptureBfgLooks(rec);

            rec.players.RemoveAll(p => p.frames.Count == 0);

            int frames = 0;
            foreach (var p in rec.players) frames += p.frames.Count;
            Plugin.Log.LogInfo($"round over, {frames} frames across {rec.players.Count} players, {rec.audioEvents.Count} bean sounds over {rec.audioKeys.Count} events");

            if (_sampledFrames > 0)
            {
                double toMs = 1000.0 / Stopwatch.Frequency;
                Plugin.Log.LogInfo($"recorder cost {_worldTicks * toMs / _sampledFrames:0.000}ms/frame on the level, {_playerTicks * toMs / _sampledFrames:0.000}ms/frame on beans, over {_sampledFrames} frames");
            }
            SaveReplay.Write(rec);
        }

        static void CaptureBfgLooks(ReplayRecording rec)
        {
            var app = CustomizationServices.ApplicationService;
            int dressed = 0;

            foreach (var p in rec.players)
            {
                var profile = RemoteProfileStore.TryGet(p.name);
                if (p.isLocal)
                {
                    if (profile == null) profile = RemoteProfileStore.LocalLoadout();
                    p.nametag = NametagIconApplicator.BuildLocalNametagInfo();
                }
                else if (profile != null) p.nametag = profile.nametag;
                if (profile != null) p.bfgSkins.AddRange(profile.skins);

                if (p.isLocal && app != null)
                {
                    p.bfgCosmetics = string.Join("|", app.GetAppliedGameCosmeticIds());
                    p.bfgColour = app.GetAppliedGameColourId();
                    p.bfgPattern = app.GetAppliedGamePatternId();
                    p.bfgFaceplate = app.GetAppliedGameFaceplateId();

                    foreach (var tex in SkinApplicationService.LoadEntries())
                        if (tex.enabled && tex.overrides.Count > 0) p.bfgTextures.Add(tex);
                }

                if (p.bfgSkins.Count > 0 || p.bfgTextures.Count > 0 || !string.IsNullOrEmpty(p.bfgCosmetics)) dressed++;
            }

            if (dressed > 0) Plugin.Log.LogInfo($"replay kept the BettrFG look for {dressed}/{rec.players.Count} players");
        }

        public static void CaptureStarchartButtonPress(Levels.Starlink.StarlinkNode node)
        {
            if (_live == null || node == null || node.ButtonWalkways == null) return;

            int start = _live.starchartPaths.Count;
            foreach (var w in node.ButtonWalkways)
            {
                if (w == null) continue;

                var scene = w.gameObject.scene;
                var roots = scene.GetRootGameObjects();
                var top = w.transform;
                while (top.parent != null) top = top.parent;
                int rootIndex = Array.IndexOf(roots, top.gameObject);

                _live.starchartPaths.Add(ReplayWorldPath.Build(w.transform, null, scene.name, rootIndex));
            }

            int count = _live.starchartPaths.Count - start;
            if (count == 0) return;

            _live.starchartEvents.Add(new ReplayStarchartEvent { t = GameplayTime, pathStart = start, pathCount = count });
            Plugin.Log.LogInfo($"starchart button pressed at {GameplayTime:0.0}s, {count} walkway(s) lighting up");
        }

        public static void CaptureDiveSlideVfx(FG.Common.Character.FallGuyVFXController controller)
        {
            if (_live == null) return;

            var fgcc = controller.GetComponent<FallGuysCharacterController>();
            if (fgcc == null || !_beanOwners.TryGetValue(fgcc.m_CachedPtr, out uint owner)) return;

            _lastDiveSlideTimes[owner] = GameplayTime;
            _live.diveSlideVfxEvents.Add(new ReplayVfxEvent { t = GameplayTime, playerId = owner });
        }

        public static void CaptureTailState(Levels.Tag.TailTagAccessory accessory, bool enabled)
        {
            if (_live == null || accessory == null) return;

            var fgcc = accessory.GetComponentInParent<FallGuysCharacterController>(true);
            if (fgcc == null || !_beanOwners.TryGetValue(fgcc.m_CachedPtr, out uint owner)) return;

            if (string.IsNullOrEmpty(_live.tailPrefab) && accessory.transform.parent != null)
            {
                var tf = accessory.transform;
                _live.tailPrefab = ReplayWorldPath.BaseName(tf.name);
                _live.tailBoneName = ReplayWorldPath.BaseName(tf.parent.name);
                tf.GetLocalPositionAndRotation(out _live.tailLocalPos, out _live.tailLocalRot);
                _live.tailLocalScale = tf.localScale;
                Plugin.Log.LogInfo($"tail rig captured: '{_live.tailPrefab}' hanging off '{_live.tailBoneName}'");
            }

            _live.tailEvents.Add(new ReplayTailEvent { t = GameplayTime, playerId = owner, enabled = enabled });
        }

        public static void CaptureSlideAudio(List<(string name, float value)> parameters, IntPtr handle)
        {
            if (_live == null) return;

            uint owner = 0;
            Vector3 pos = Vector3.zero;
            float bestDelta = 2.5f;
            foreach (var kvp in _lastDiveSlideTimes)
            {
                float delta = GameplayTime - kvp.Value;
                if (delta < 0f || delta >= bestDelta) continue;
                if (!_tracked.TryGetValue(kvp.Key, out var candidate) || candidate.tf.m_CachedPtr == IntPtr.Zero) continue;

                bestDelta = delta;
                owner = kvp.Key;
                pos = candidate.lastPos;
            }

            int paramStart = _live.audioParams.Count;
            if (parameters != null)
                foreach (var (name, value) in parameters)
                    _live.audioParams.Add(new ReplayAudioParam { name = Intern(_live.audioParamNames, _paramIds, name), value = value });

            _live.audioEvents.Add(new ReplayAudioEvent
            {
                t = GameplayTime,
                end = -1f,
                playerId = owner,
                pos = pos,
                key = Intern(_live.audioKeys, _keyIds, "F_Slide"),
                paramStart = paramStart,
                paramCount = parameters?.Count ?? 0,
            });
            _openSounds[handle] = _live.audioEvents.Count - 1;
        }

        public static void CloseSlideAudio(IntPtr handle)
        {
            if (_live == null || !_openSounds.TryGetValue(handle, out int index)) return;
            _openSounds.Remove(handle);

            var sound = _live.audioEvents[index];
            sound.end = GameplayTime;
            _live.audioEvents[index] = sound;
        }

        public static FallGuysCharacterController BeanFor(Animator animator)
        {
            IntPtr id = animator.m_CachedPtr;
            if (_animBeans.TryGetValue(id, out var bean)) return bean;
            bean = animator.GetComponentInParent<FallGuysCharacterController>();
            _animBeans[id] = bean;
            return bean;
        }

        public static void CaptureAudio(AudioEvent2D3DPairSO pair, FallGuysCharacterController controller, Vector3 pos, AudioParamContainer parameters)
        {
            if (_live == null || pair == null) return;

            IntPtr handle = pair.Pointer;
            if (!_pairKeys.TryGetValue(handle, out int key))
            {
                string name = pair.audioEvent3D;
                if (string.IsNullOrEmpty(name)) name = pair.audioEvent2D;
                key = string.IsNullOrEmpty(name) ? -1 : Intern(_live.audioKeys, _keyIds, name);
                if (key >= 0) _audioManagerKeys.Add(name);
                _pairKeys[handle] = key;
            }
            if (key < 0) return;

            RecordAudio(key, controller, pos, parameters);
        }

        public static void CaptureAudio(string key, FallGuysCharacterController controller, Vector3 pos)
        {
            if (_live == null || string.IsNullOrEmpty(key)) return;
            if (key.StartsWith("UI_Gen_", StringComparison.Ordinal)) return;
            _audioManagerKeys.Add(key);
            RecordAudio(Intern(_live.audioKeys, _keyIds, key), controller, pos, null);
        }

        // a sound fmod started that no AudioManager hook claimed. keys AudioManager does play are left
        // alone so nothing lands twice, and a key is taken at most once every 50ms so a looping emitter
        // restarting can't fill the file
        public static void CaptureStrayAudio(string key, Vector3 pos) => CaptureStrayAudio(key, pos, IntPtr.Zero);

        public static void CaptureStrayAudio(string key, Vector3 pos, IntPtr loopHandle)
        {
            if (_live == null || string.IsNullOrEmpty(key)) return;
            if (_audioManagerKeys.Contains(key) || key.StartsWith("UI_Gen_", StringComparison.Ordinal)) return;

            float now = GameplayTime;
            if (_strayAudioAt.TryGetValue(key, out float when) && now - when < 0.05f) return;
            _strayAudioAt[key] = now;

            int index = RecordAudio(Intern(_live.audioKeys, _keyIds, key), null, pos, null);
            if (loopHandle != IntPtr.Zero && index >= 0) _openSounds[loopHandle] = index;
        }

        // a looping fmod event a vfx prefab started for itself has just stopped. close the open event
        // so playback stops it too instead of looping the speech-arch / ambience noise forever
        public static void CloseStrayAudio(IntPtr handle)
        {
            if (_live == null || !_openSounds.TryGetValue(handle, out int index)) return;
            _openSounds.Remove(handle);

            var sound = _live.audioEvents[index];
            if (sound.end < 0f) { sound.end = GameplayTime; _live.audioEvents[index] = sound; }
        }

        public static void CaptureHeldAudio(EventInstanceReference reference, string key, Vector3 pos)
        {
            if (_live == null || reference == null || string.IsNullOrEmpty(key)) return;
            if (key.StartsWith("UI_Gen_", StringComparison.Ordinal)) return;
            _audioManagerKeys.Add(key);

            int index = RecordAudio(Intern(_live.audioKeys, _keyIds, key), null, pos, null);
            if (index >= 0) _openSounds[reference.Pointer] = index;
        }

        public static void CloseHeldAudio(EventInstanceReference reference)
        {
            if (_live == null || reference == null) return;
            if (!_openSounds.TryGetValue(reference.Pointer, out int index)) return;
            _openSounds.Remove(reference.Pointer);

            var sound = _live.audioEvents[index];
            sound.end = GameplayTime;
            _live.audioEvents[index] = sound;
        }

        static int RecordAudio(int key, FallGuysCharacterController controller, Vector3 pos, AudioParamContainer parameters)
        {
            var rec = _live;
            int paramStart = rec.audioParams.Count;
            int paramCount = 0;

            var values = parameters?._floatValues;
            if (values != null)
            {
                var names = parameters._floatNames;
                if (names != null && names.Length == values.Length)
                {
                    IntPtr handle = names.Pointer;
                    if (!_paramSets.TryGetValue(handle, out var ids) || ids.Length != names.Length)
                    {
                        ids = new int[names.Length];
                        for (int i = 0; i < ids.Length; i++)
                        {
                            string name = names[i];
                            ids[i] = string.IsNullOrEmpty(name) ? -1 : Intern(rec.audioParamNames, _paramIds, name);
                        }
                        _paramSets[handle] = ids;
                    }

                    for (int i = 0; i < ids.Length; i++)
                    {
                        if (ids[i] < 0) continue;
                        rec.audioParams.Add(new ReplayAudioParam { name = ids[i], value = values[i] });
                        paramCount++;
                    }
                }
            }

            uint owner = 0;
            if (controller != null) _beanOwners.TryGetValue(controller.m_CachedPtr, out owner);
            else
            {
                float best = 4f * 4f;
                foreach (var kvp in _tracked)
                {
                    if (kvp.Value.tf.m_CachedPtr == IntPtr.Zero) continue;
                    float d2 = (kvp.Value.lastPos - pos).sqrMagnitude;
                    if (d2 < best) { best = d2; owner = kvp.Key; }
                }
            }

            rec.audioEvents.Add(new ReplayAudioEvent
            {
                t = GameplayTime,
                end = -1f,
                playerId = owner,
                pos = pos,
                key = key,
                paramStart = paramStart,
                paramCount = paramCount,
            });
            return rec.audioEvents.Count - 1;
        }

        public static void CaptureSpeech(GameObject bean, int optionId)
        {
            if (_live == null || bean == null) return;

            var fgcc = bean.GetComponent<FallGuysCharacterController>();
            if (fgcc == null || !_beanOwners.TryGetValue(fgcc.m_CachedPtr, out uint owner)) return;

            if (!_speechIds.TryGetValue(optionId, out int index))
            {
                var lookup = SingletonBehaviour<SpeechOptionsManager>.Instance?._speechOptionsLookup;
                var option = lookup != null && lookup.ContainsKey(optionId) ? lookup[optionId] : null;
                var image = option?.TryCast<ImageSpeechOption>();

                index = image == null ? -1 : _live.speechOptions.Count;
                if (image != null) _live.speechOptions.Add(Describe(option, image, optionId));
                _speechIds[optionId] = index;

                if (option == null) Plugin.Log.LogWarning($"replay: speech option {optionId} isn't in the lookup, that bubble won't be saved");
                else if (image == null) Plugin.Log.LogInfo($"{option.name} is an action prompt, not a phrase or emoticon — leaving it out of the replay");
            }
            if (index < 0) return;

            float t = _gsv?.GameplayTimeElapsed ?? 0f;
            var saved = _live.speechOptions[index];
            _live.speechEvents.Add(new ReplaySpeechEvent
            {
                t = t,
                end = t + saved.duration,
                playerId = owner,
                option = index,
            });
        }

        static ReplaySpeechOption Describe(SocialOption option, ImageSpeechOption image, int optionId)
        {
            var saved = new ReplaySpeechOption
            {
                itemId = ItemId(option),
                speechId = optionId,
                duration = option.HasDuration && option.Duration > 0f ? option.Duration : 3f,
                text = option.DisplayName ?? "",
                hasText = option.TryCast<TextAndImageSpeechOption>() != null,
                shiny = option.IsShiny,
            };

            var sprite = image._sprite;
            if (!Packable(sprite)) sprite = image.CurrentSprite;
            if (!Packable(sprite)) return saved;

            try
            {
                saved.image = sprite.texture.EncodeToPNG();
                Plugin.Log.LogInfo($"packed the custom art for '{option.name}' with the replay, {saved.image.Length / 1024}kb");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"couldn't pack the art for '{option.name}': {ex.Message}"); }

            return saved;
        }

        static bool Packable(Sprite sprite)
        {
            var texture = sprite?.texture;
            if (texture == null || !texture.isReadable) return false;
            return sprite.rect.width == texture.width && sprite.rect.height == texture.height;
        }

        static int Intern(List<string> table, Dictionary<string, int> ids, string value)
        {
            if (ids.TryGetValue(value, out int index)) return index;
            index = table.Count;
            table.Add(value);
            ids[value] = index;
            return index;
        }

        static ReplayPlayer Resolve(uint playerId)
        {
            var list = _live.players;
            for (int i = 0; i < list.Count; i++)
                if (list[i].playerId == playerId) return list[i];
            var p = new ReplayPlayer { playerId = playerId };
            list.Add(p);
            return p;
        }

        static void ReadCustomisation(ReplayPlayer p, CustomisationSelections fallback)
        {
            var sel = FallGuysLib.Players.PlayerUtils.GetClientPlayerManager()?.GetPlayerMetadataFromPlayerId(p.playerId)?.Selections;
            if (sel == null) sel = fallback;
            if (sel == null) return;
            p.colour = ItemId(sel.ColourOption);
            p.pattern = ItemId(sel.PatternOption);
            p.costumeTop = CostumeId(sel.CostumeTopOption);
            p.costumeBottom = CostumeId(sel.CostumeBottomOption);
            p.costumeFull = CostumeId(sel.CostumeFullOption);
            p.faceplate = ItemId(sel.FaceplateOption);
            p.victoryPose = ItemId(sel.VictoryPoseOption);
            p.nameplate = ItemId(sel.NameplateOption);
            p.nickname = ItemId(sel.NicknameOption);
            p.fameEarnedBadge = sel.FameEarnedBadge;
            p.crownsEarned = sel.CrownsEarned;
            p.fameUpdatedAt = new DateTime(sel.FameUpdatedAt.Ticks);
        }

        static string CostumeId(CostumeOption item)
        {
            if (item == null || item.IsNoneCostume) return "";
            return ItemId(item);
        }

        public static string ItemId(ItemDefinitionSO item)
        {
            if (item == null) return "";
            return FirstNonEmpty(item.FullItemId, item.ItemId, item.name);
        }

        static string ItemId(ItemDefinitionClass item)
        {
            if (item == null) return "";
            return FirstNonEmpty(item.FullItemId, item.ItemId, item.DisplayName);
        }

        static string FirstNonEmpty(string a, string b, string c)
        {
            if (!string.IsNullOrEmpty(a)) return a;
            if (!string.IsNullOrEmpty(b)) return b;
            return c ?? "";
        }
    }
}
