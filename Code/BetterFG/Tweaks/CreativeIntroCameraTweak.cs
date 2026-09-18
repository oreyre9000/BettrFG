using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Nametag;
using FG.Common;
using FGClient;
using Levels.Progression;
using Levels.ScoreZone;
using UnityEngine;
using FallGuysLib.Camera;

namespace BetterFG.Tweaks
{
    public class CreativeIntroCameraTweak : BfgTweak
    {
        public CreativeIntroCameraTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "creative_intro_camera";
        public override string TweakLabel => "tweak.creative_intro_cameras";
        public override bool DefaultEnabled => true;

        public static CreativeIntroCameraTweak Instance { get; private set; }
        void Awake() => Instance = this;

        const float SpawnFraction = 0.3f;
        const float TravelSpeed = 27f;
        const float MinFlight = 7f;
        const float MaxFlight = 10f;
        const float ReturnSweep = 1.9f;
        const float SweepArc = 22f;
        const float OrbitRadius = 26f;
        const float OrbitHeight = 13f;
        const float OrbitSpeed = 0.17f;
        const float EyeHeight = 1.2f;
        const float Clearance = 9f;
        const float MinRise = 12f;
        const float MaxRise = 26f;
        const int RiseLookahead = 6;
        const float Weave = 13f;
        const float BackOff = 20f;
        const float EndBackOff = 38f;
        const float EndRise = 14f;
        const float TailStart = 0.7f;
        const float LookAhead = 1.7f;
        const float AimClearance = 16f;
        const float Fov = 55f;
        const float FadeTime = 0.45f;
        const float RestoreDelay = 2f;
        const int ArcSamples = 96;
        const float NametagPollStep = 0.5f;
        const int NametagPolls = 6;

        static readonly float[,] Shape =
        {
            { 0.00f, -0.85f, 16f, 0.10f },
            { 0.24f,  0.70f,  9f, 0.34f },
            { 0.50f, -0.55f, 17f, 0.62f },
            { 0.76f,  0.60f,  8f, 0.88f },
            { 1.00f, -0.25f, 13f, 1.00f },
        };

        static readonly HashSet<uint> _spawned = new HashSet<uint>();

        bool _running;
        bool _usedThisRound;
        bool _hasFinish;
        bool _targetIsEndZone;
        Vector3 _start;
        Vector3 _finish;
        Vector3[] _path;
        Vector3[] _lookPath;
        float[] _arc;
        float _flightLength;
        float _flightDuration;
        float _t0;
        float _angle;
        float _side;
        float _swing;
        float _phase;
        float _heightScale;
        float _originalFov;
        GameObject _bannerState;
        bool _bannerWasActive;
        Camera _cam;
        Transform _camT;
        Cinemachine.CinemachineBrain _brain;
        readonly List<Vector3> _scoreTargets = new List<Vector3>();
        readonly List<CanvasGroup> _hidden = new List<CanvasGroup>();
        readonly List<CanvasGroupFader> _faders = new List<CanvasGroupFader>();

        static readonly string[] LoadingRoots =
        {
            "UICanvas_Client_V2(Clone)/LoadingScreen",
            "UICanvas_Client_V2(Clone)/LoadingScreenOverlay",
        };

        public override void DisableTweak() => Stop();

        public override void OnRoundStart() => Stop();

        public static void OnCleanupLoadingScreens()
        {
            if (Instance == null || Instance._running) return;

            for (int i = 0; i < Instance._hidden.Count; i++)
            {
                var cg = Instance._hidden[i];
                if (cg != null) { cg.alpha = 1f; cg.blocksRaycasts = true; }
            }
            Instance._hidden.Clear();
            Instance._faders.Clear();
        }

        public override void OnStateChanged(GameStateMachine.IGameState newState)
        {
            if (_running && newState != null && newState.TryCast<StateGameInProgress>() == null) Stop();
        }

        public static void OnClientGameManagerShutdown()
        {
            _spawned.Clear();
            if (Instance == null) return;
            Instance._usedThisRound = false;
            Instance.Stop();

            for (int i = 0; i < Instance._hidden.Count; i++)
            {
                var cg = Instance._hidden[i];
                if (cg == null) continue;
                cg.alpha = 1f;
                cg.blocksRaycasts = true;
            }
            Instance._hidden.Clear();
            Instance._faders.Clear();
        }

        public static void OnPlayerSpawned(uint playerId)
        {
            var inst = Instance;
            if (inst == null || !inst.IsEnabled || inst._running || inst._usedThisRound) return;
            _spawned.Add(playerId);

            var gsv = GlobalGameStateClient.Instance?.GameStateView;
            if (gsv == null) return;

            int total = (int)gsv.InitialRoundPlayerCount;
            if (total <= 0 || _spawned.Count < Mathf.CeilToInt(total * SpawnFraction)) return;

            ClientGameManager cgm;
            if (!gsv.GetLiveClientGameManager(out cgm) || cgm == null || !cgm.IsUGCRound) return;

            inst.Begin(total, cgm._round?.DisplayNameUnindented);
        }

        void Begin(int total, string levelName)
        {
            var director = CameraLocator.GetCameraDirector();
            if (director == null) { Plugin.Log.LogWarning("creative intro: no camera director, skipping the flythrough"); return; }

            _cam = director.MainNativeCam;
            if (_cam == null) { Plugin.Log.LogWarning("creative intro: director had no native cam"); return; }

            if (!ResolveStart()) { Plugin.Log.LogWarning("creative intro: couldn't work out where the start is, leaving the camera alone"); return; }
            ResolveTargets();

            _usedThisRound = true;
            _camT = _cam.transform;
            _brain = director.Brain;
            _originalFov = _cam.fieldOfView;
            if (_brain != null) _brain.enabled = false;
            _cam.fieldOfView = Fov;

            uint h = StableHash(levelName);
            _side = (h & 1u) == 0u ? -1f : 1f;
            _swing = 0.75f + ((h >> 11) & 255u) / 255f * 0.5f;
            _heightScale = 0.85f + ((h >> 19) & 255u) / 255f * 0.35f;
            _phase = ((h >> 3) & 255u) / 255f * Mathf.PI;
            _angle = ((h >> 1) & 1023u) / 1024f * Mathf.PI * 2f;

            if (BetterFG.Features.CreativeExpansion.CustomIntroCams.TryBuildPath(out var authored, out var authoredLook, out float authoredDuration))
            {
                _path = authored;
                _lookPath = authoredLook;
                BuildArcTable();
                _flightDuration = authoredDuration;
                Plugin.Log.LogInfo($"authored intro cams: {_path.Length} shot(s) over {_flightDuration:0.00}s");
            }
            else if (_hasFinish) BuildFlight();

            _t0 = Time.time;
            _running = true;

            HideLoadingScreen();
            PlayIntroBanner();
            StartCoroutine(PollNametags().WrapToIl2Cpp());

            if (_path != null) Plugin.Log.LogInfo($"creative intro rolling, {_spawned.Count}/{total} in — {_flightLength:0}u of camera track over {_flightDuration:0.0}s");
            else Plugin.Log.LogInfo($"creative intro rolling, {_spawned.Count}/{total} in — no route to fly, circling the start instead");
        }

        void BuildFlight()
        {
            var planner = new CreativeIntroRoute();
            IntroRoute route = _targetIsEndZone ? planner.PlanRace(_start, _finish)
                                                : planner.PlanTour(_start, _scoreTargets);

            if (route != null)
            {
                BuildRoutePath(route);
                Plugin.Log.LogInfo($"intro route: {route.How}, {route.Length:0}u end to end");
            }
            else
            {
                BuildShapePath();
                Plugin.Log.LogWarning("couldn't walk a route through this level, falling back to the plain start-to-finish arc");
            }

            BuildArcTable();
            _flightDuration = Mathf.Clamp(_flightLength / TravelSpeed, MinFlight, MaxFlight);
        }

        bool ResolveStart()
        {
            var sum = Vector3.zero;
            int n = 0;
            foreach (var sp in UnityEngine.Object.FindObjectsOfType<MultiplayerStartingPosition>())
            {
                if (sp == null) continue;
                sum += sp.transform.position;
                n++;
            }

            if (n == 0)
            {
                foreach (var bean in UnityEngine.Object.FindObjectsOfType<FallGuysCharacterController>())
                {
                    if (bean == null) continue;
                    sum += bean.transform.position;
                    n++;
                }
                if (n > 0) Plugin.Log.LogInfo($"no starting positions in this one, averaging {n} beans for the start instead");
            }

            if (n == 0) return false;
            _start = sum / n;
            return true;
        }

        void ResolveTargets()
        {
            _scoreTargets.Clear();

            var sum = Vector3.zero;
            int n = 0;
            foreach (var ez in UnityEngine.Object.FindObjectsOfType<COMMON_ObjectiveReachEndZone>())
            {
                if (ez == null || !ez.gameObject.activeInHierarchy) continue;
                sum += ez.transform.position;
                n++;
            }
            if (n > 0)
            {
                _finish = sum / n;
                _hasFinish = true;
                _targetIsEndZone = true;
                return;
            }
            _targetIsEndZone = false;

            foreach (var p in UnityEngine.Object.FindObjectsOfType<LevelEditorUseForPointScoringParameter>())
            {
                if (p == null || !p._useForPointScoring || !p.gameObject.activeInHierarchy) continue;
                _scoreTargets.Add(p.transform.position);
            }
            foreach (var b in UnityEngine.Object.FindObjectsOfType<COMMON_ScoringBubble>())
            {
                if (b == null || !b.gameObject.activeInHierarchy) continue;
                _scoreTargets.Add(b.transform.position);
            }
            foreach (var z in UnityEngine.Object.FindObjectsOfType<ScoreZone>())
            {
                if (z == null || !z.gameObject.activeInHierarchy) continue;
                _scoreTargets.Add(z.transform.position);
            }
            foreach (var gq in UnityEngine.Object.FindObjectsOfType<COMMON_GrabToQualify>())
            {
                if (gq == null || !gq.gameObject.activeInHierarchy) continue;
                _scoreTargets.Add(gq.transform.position);
            }

            _hasFinish = _scoreTargets.Count > 0;
            if (!_hasFinish) return;

            sum = Vector3.zero;
            foreach (var t in _scoreTargets) sum += t;
            _finish = sum / _scoreTargets.Count;
            Plugin.Log.LogInfo($"points round by the looks of it, {_scoreTargets.Count} scoring things to fly at instead of a finish line");
        }

        void BuildRoutePath(IntroRoute route)
        {
            var pts = route.Points;
            int n = pts.Length;
            _path = new Vector3[n];
            _lookPath = new Vector3[n];
            float spacing = route.Length / Mathf.Max(n - 1, 1);

            var forwardMax = new float[n];
            for (int i = 0; i < n; i++)
            {
                float m = pts[i].y;
                int end = Mathf.Min(n - 1, i + RiseLookahead);
                for (int j = i; j <= end; j++) if (pts[j].y > m) m = pts[j].y;
                forwardMax[i] = m;
            }

            for (int i = 0; i < n; i++)
            {
                Vector3 fwd = Tangent(pts, i);
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                float f = i / (float)(n - 1);

                float tail = Mathf.Max(0f, (f - TailStart) / (1f - TailStart));
                tail = tail * tail * (3f - 2f * tail);

                float weave = Mathf.Sin(f * Mathf.PI * 2.6f + _phase) * _side * _swing * Weave * (1f - tail * 0.65f);
                float rise = Mathf.Clamp((forwardMax[i] - pts[i].y) + Clearance, MinRise, MaxRise);

                _path[i] = pts[i] + right * weave + Vector3.up * (rise + EndRise * tail)
                           - fwd * (BackOff * Mathf.Max(0f, 1f - f * 4f) + EndBackOff * tail);
                if (route.Look == null)
                {
                    _lookPath[i] = LookAheadPoint(pts, i, spacing * LookAhead) + Vector3.up * EyeHeight;
                    continue;
                }

                _lookPath[i] = route.Look[i];
                float above = route.Look[i].y + AimClearance;
                if (_path[i].y < above) _path[i].y = above;
            }
        }

        static Vector3 Tangent(Vector3[] pts, int i)
        {
            int a = Mathf.Max(i - 1, 0);
            int b = Mathf.Min(i + 1, pts.Length - 1);
            Vector3 d = pts[b] - pts[a];
            d.y = 0f;
            return d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward;
        }

        static Vector3 LookAheadPoint(Vector3[] pts, int i, float dist)
        {
            for (int k = i; k < pts.Length - 1; k++)
            {
                float seg = Vector3.Distance(pts[k], pts[k + 1]);
                if (dist <= seg) return Vector3.Lerp(pts[k], pts[k + 1], seg > 0.001f ? dist / seg : 0f);
                dist -= seg;
            }
            return pts[pts.Length - 1];
        }

        void BuildShapePath()
        {
            Vector3 flat = _finish - _start;
            flat.y = 0f;
            float len = flat.magnitude;
            Vector3 fwd = len > 0.01f ? flat / len : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            float lat = Mathf.Clamp(len * 0.3f, 8f, 26f) * _side * _swing;
            int n = Shape.GetLength(0);
            _path = new Vector3[n];
            _lookPath = new Vector3[n];

            for (int i = 0; i < n; i++)
            {
                float f = Shape[i, 0];
                float tail = Mathf.Max(0f, (f - TailStart) / (1f - TailStart));
                tail = tail * tail * (3f - 2f * tail);

                Vector3 spine = Vector3.Lerp(_start, _finish, f) - fwd * (BackOff * (1f - f) + EndBackOff * tail);
                _path[i] = spine + right * (Shape[i, 1] * lat * (1f - tail * 0.65f))
                           + Vector3.up * (Shape[i, 2] * _heightScale + EndRise * tail);
                _lookPath[i] = Vector3.Lerp(_start, _finish, Shape[i, 3]) + Vector3.up * EyeHeight;
            }
        }

        void BuildArcTable() => _arc = ArcTable(_path, out _flightLength);

        float ArcParam(float e) => ArcParam(_arc, _flightLength, e);

        internal static float[] ArcTable(Vector3[] path, out float length)
        {
            var arc = new float[ArcSamples + 1];
            Vector3 prev = Spline(path, 0f);
            float acc = 0f;
            for (int i = 1; i <= ArcSamples; i++)
            {
                Vector3 p = Spline(path, i / (float)ArcSamples);
                acc += Vector3.Distance(prev, p);
                prev = p;
                arc[i] = acc;
            }
            length = acc;
            return arc;
        }

        internal static float ArcParam(float[] arc, float length, float e)
        {
            float target = e * length;
            int i = 1;
            while (i < arc.Length && arc[i] < target) i++;
            if (i >= arc.Length) return 1f;
            float a = arc[i - 1], b = arc[i];
            return (i - 1 + (b > a ? (target - a) / (b - a) : 0f)) / ArcSamples;
        }

        internal static Vector3 Spline(Vector3[] p, float t)
        {
            int last = p.Length - 1;
            float x = Mathf.Clamp01(t) * last;
            int i = Mathf.Min((int)x, last - 1);
            float u = x - i;
            Vector3 p0 = p[Mathf.Max(i - 1, 0)];
            Vector3 p1 = p[i];
            Vector3 p2 = p[i + 1];
            Vector3 p3 = p[Mathf.Min(i + 2, last)];
            return 0.5f * (2f * p1
                           + (p2 - p0) * u
                           + (2f * p0 - 5f * p1 + 4f * p2 - p3) * (u * u)
                           + (-p0 + 3f * p1 - 3f * p2 + p3) * (u * u * u));
        }

        static uint StableHash(string s)
        {
            uint h = 2166136261u;
            if (string.IsNullOrEmpty(s)) return h;
            for (int i = 0; i < s.Length; i++)
            {
                h ^= s[i];
                h *= 16777619u;
            }
            return h;
        }

        void PlayIntroBanner()
        {
            var gameStates = GameObject.Find("UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates");
            var introState = gameStates != null ? gameStates.transform.Find("IntroCameraState") : null;
            if (introState == null) { Plugin.Log.LogWarning("no IntroCameraState under GameStates, skipping the level name banner"); return; }

            var state = introState.GetComponent<InGameIntroCameraState>();
            var banner = state != null ? state._introCamBannerUI : null;
            if (banner == null) { Plugin.Log.LogWarning("IntroCameraState had no banner ui on it, no level name then"); return; }

            _bannerState = introState.gameObject;
            _bannerWasActive = _bannerState.activeSelf;
            _bannerState.SetActive(true);
            banner.Play();
        }

        IEnumerator PollNametags()
        {
            for (int i = 0; i < NametagPolls; i++)
            {
                if (!_running) yield break;

                var tag = NametagFinder.FindLocalNameTagSprite();
                if (tag != null)
                {
                    NametagIconApplicator.OnLocalTagSpawned();
                    NametagPatchHub.RefreshRemoteNametags(tag.GetComponentInParent<PlayerInfoHUDBase>());
                }

                yield return new WaitForSeconds(NametagPollStep);
            }
        }

        void HideLoadingScreen()
        {
            _hidden.Clear();
            _faders.Clear();
            foreach (var path in LoadingRoots)
            {
                var go = GameObject.Find(path);
                if (go == null) continue;

                var cg = go.GetComponent<CanvasGroup>();
                if (cg == null) cg = go.AddComponent<CanvasGroup>();
                var fader = go.GetComponent<CanvasGroupFader>();
                if (fader == null) fader = go.AddComponent<CanvasGroupFader>();

                fader._pCanvasGroup = cg;
                fader.initialiseFader(0f, FadeTime, cg.alpha, 0f, null);
                cg.blocksRaycasts = false;
                _hidden.Add(cg);
                _faders.Add(fader);
            }
            if (_hidden.Count == 0) Plugin.Log.LogWarning("intro cam is up but the loading screen roots weren't there to fade, it'll sit over the top");
        }

        void OrbitPose(float angle, out Vector3 pos, out Vector3 look)
        {
            pos = _start + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * OrbitRadius + Vector3.up * OrbitHeight;
            look = _start + Vector3.up * EyeHeight;
        }

        void LateUpdate()
        {
            if (!_running) return;
            if (_cam == null) { Stop(); return; }

            if (_brain != null && _brain.enabled) _brain.enabled = false;

            float t = Time.time - _t0;
            Vector3 pos, look;

            if (_path != null && t < _flightDuration)
            {
                float raw = t / _flightDuration;
                float e = raw * raw * (3f - 2f * raw);
                float u = ArcParam(e);
                pos = Spline(_path, u);
                look = Spline(_lookPath, u);
            }
            else if (_path != null && t < _flightDuration + ReturnSweep)
            {
                float f = (t - _flightDuration) / ReturnSweep;
                float s = f * f * (3f - 2f * f);
                OrbitPose(_angle, out var orbitPos, out var orbitLook);
                pos = Vector3.Lerp(Spline(_path, 1f), orbitPos, s) + Vector3.up * (Mathf.Sin(f * Mathf.PI) * SweepArc);
                look = Vector3.Lerp(Spline(_lookPath, 1f), orbitLook, s);
            }
            else
            {
                _angle += OrbitSpeed * Time.deltaTime;
                OrbitPose(_angle, out pos, out look);
            }

            Vector3 dir = look - pos;
            if (dir.sqrMagnitude < 0.0001f) return;
            _camT.SetPositionAndRotation(pos, Quaternion.LookRotation(dir, Vector3.up));
        }

        void Stop()
        {
            if (!_running) return;
            _running = false;

            if (_cam != null) _cam.fieldOfView = _originalFov;
            if (_brain != null) _brain.enabled = true;
            for (int i = 0; i < _hidden.Count; i++)
            {
                var cg = _hidden[i];
                if (cg != null) cg.blocksRaycasts = true;
                var fader = _faders[i];
                if (fader != null) fader.initialiseFader(RestoreDelay, FadeTime, 0f, 1f, null);
            }

            if (_bannerState != null) _bannerState.SetActive(_bannerWasActive);
            _bannerState = null;

            _path = null;
            _lookPath = null;
            _cam = null;
            _camT = null;
            _brain = null;
            Plugin.Log.LogInfo("creative intro done, cinemachine has the camera back");
        }
    }
}
