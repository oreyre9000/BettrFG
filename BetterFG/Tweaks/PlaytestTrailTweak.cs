using System;
using System.Collections.Generic;
using BetterFG.Utilities;
using FG.Common;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class PlaytestTrailTweak : BfgTweak
    {
        public PlaytestTrailTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "playtest_trail";
        public override string TweakLabel => "Playtest trail in creative";
        public override string TweakTooltip => "Draws the path your bean took on the last playtest as a line in the creative editor. Redrawn every time you leave a playtest.";
        public override bool DefaultEnabled => true;

        const float StepDistance = 0.35f;
        const int MaxPoints = 20000;
        const int SkipFramesAfterOrigin = 5;
        const float TeleportDistance = 5f;
        const float ArrowSpacing = 0.75f;
        const float ArrowSpeed = 2f;
        const float ArrowScale = 2.2f;
        const int MaxArrows = 400;
        const string TrailName = "BettrFG_PlaytestTrail";
        static readonly Color TrailColour = new Color(0.15f, 0.85f, 1f);

        static readonly int[] ColourPropertyIds =
        {
            Shader.PropertyToID("_Color"),
            Shader.PropertyToID("_TintColor"),
            Shader.PropertyToID("_BaseColor"),
            Shader.PropertyToID("_MainColor"),
        };

        static readonly string[] UnlitShaderNames =
        {
            "Amplify/VFX_Opaque_Unlit",
            "Unlit/ENV_Standard_Triplanar",
            "Unlit/Color",
        };

        static Shader _unlit;

        static Shader UnlitShader()
        {
            if (_unlit != null) return _unlit;

            var all = Resources.FindObjectsOfTypeAll<Shader>();
            foreach (var want in UnlitShaderNames)
            {
                var s = Shader.Find(want);
                if (s == null)
                    foreach (var c in all)
                        if (c != null && c.name == want) { s = c; break; }
                if (s != null) { _unlit = s; return s; }
            }
            Plugin.Log.LogWarning("no unlit shader anywhere in the build, the trail will look wrong");
            return null;
        }

        readonly List<List<Vector3>> _segments = new List<List<Vector3>>();
        readonly List<List<Vector3>> _arrowPaths = new List<List<Vector3>>();
        readonly List<float[]> _arrowCum = new List<float[]>();
        readonly List<Transform[]> _arrows = new List<Transform[]>();
        int _count;
        int _skip;
        bool _recording;
        GameObject _trail;

        public override void OnLevelEditorPlaytest()
        {
            Clear();
            _segments.Add(new List<Vector3>());
            _recording = true;
        }

        public override void OnLevelEditorPlaytestEnd()
        {
            _recording = false;
            Draw();
        }

        public override void DisableTweak()
        {
            _recording = false;
            Clear();
        }

        void Update()
        {
            if (_recording) Record();
            else if (_trail != null) MoveArrows();
        }

        void MoveArrows()
        {
            float off = Time.time * ArrowSpeed % ArrowSpacing;
            for (int s = 0; s < _arrows.Count; s++)
            {
                var p = _arrowPaths[s];
                var c = _arrowCum[s];
                var a = _arrows[s];
                int seg = 0;
                for (int k = 0; k < a.Length; k++)
                {
                    float d = k * ArrowSpacing + off;
                    bool on = d <= c[c.Length - 1];
                    if (a[k].gameObject.activeSelf != on) a[k].gameObject.SetActive(on);
                    if (!on) continue;
                    while (seg < c.Length - 2 && c[seg + 1] < d) seg++;
                    float len = c[seg + 1] - c[seg];
                    var dir = p[seg + 1] - p[seg];
                    a[k].position = Vector3.Lerp(p[seg], p[seg + 1], len > 0 ? (d - c[seg]) / len : 0) + Vector3.up * 0.05f;
                    if (dir.sqrMagnitude > 1e-6f) a[k].rotation = Quaternion.LookRotation(dir);
                }
            }
        }

        void Record()
        {
            if (_count >= MaxPoints) return;

            var mgr = LevelEditorManager.Instance;
            var bean = mgr != null ? mgr._playerGameObject : null;
            if (bean == null) return;

            var p = bean.transform.position;
            if (p.x * p.x + p.z * p.z < 0.25f && Mathf.Abs(p.y) < 1f)
            {
                _skip = SkipFramesAfterOrigin;
                return;
            }
            if (_skip > 0)
            {
                if (--_skip == 0) _segments.Add(new List<Vector3>());
                return;
            }

            var seg = _segments[_segments.Count - 1];
            if (seg.Count > 0)
            {
                float d = (p - seg[seg.Count - 1]).sqrMagnitude;
                if (d < StepDistance * StepDistance) return;
                if (d > TeleportDistance * TeleportDistance)
                {
                    seg = new List<Vector3>();
                    _segments.Add(seg);
                }
            }
            seg.Add(p);
            _count++;
        }

        void Clear()
        {
            _segments.Clear();
            _arrowPaths.Clear();
            _arrowCum.Clear();
            _arrows.Clear();
            _count = 0;
            _skip = 0;
            if (_trail != null) UnityEngine.Object.Destroy(_trail);
            _trail = null;
        }

        void Draw()
        {
            if (_trail != null) UnityEngine.Object.Destroy(_trail);
            _arrowPaths.Clear();
            _arrowCum.Clear();
            _arrows.Clear();
            if (_count < 2)
            {
                Plugin.Log.LogInfo("playtest trail: barely moved, nothing to draw");
                return;
            }

            _trail = new GameObject(TrailName);
            var mat = new Material(UnlitShader()) { name = TrailName };
            foreach (int id in ColourPropertyIds)
                if (mat.HasProperty(id)) mat.SetColor(id, TrailColour);
            var arrowMat = new Material(mat) { name = TrailName };
            foreach (int id in ColourPropertyIds)
                if (arrowMat.HasProperty(id)) arrowMat.SetColor(id, Color.white);

            var arrowMesh = new Mesh();
            arrowMesh.vertices = new[]
            {
                new Vector3(0, 0, 0.25f), new Vector3(-0.25f, 0, 0), new Vector3(-0.25f, 0, -0.12f),
                new Vector3(0, 0, 0.13f), new Vector3(0.25f, 0, -0.12f), new Vector3(0.25f, 0, 0),
            };
            arrowMesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 5, 0, 2, 1, 0, 3, 2, 0, 4, 3, 0, 5, 4 };
            arrowMesh.RecalculateBounds();
            int arrowTotal = 0;

            foreach (var seg in _segments)
            {
                if (seg.Count < 2) continue;
                var go = new GameObject(TrailName);
                go.transform.SetParent(_trail.transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.numCapVertices = 2;
                line.numCornerVertices = 2;
                line.widthMultiplier = 0.12f;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.startColor = TrailColour;
                line.endColor = TrailColour;
                line.sharedMaterial = mat;
                line.positionCount = seg.Count;
                for (int i = 0; i < seg.Count; i++) line.SetPosition(i, seg[i]);

                var cum = new float[seg.Count];
                for (int i = 1; i < seg.Count; i++) cum[i] = cum[i - 1] + Vector3.Distance(seg[i - 1], seg[i]);
                var arrows = new Transform[Mathf.Clamp((int)(cum[cum.Length - 1] / ArrowSpacing) + 1, 0, MaxArrows - arrowTotal)];
                for (int k = 0; k < arrows.Length; k++)
                {
                    var arrow = new GameObject(TrailName);
                    arrow.transform.SetParent(_trail.transform, false);
                    arrow.transform.localScale = Vector3.one * ArrowScale;
                    arrow.AddComponent<MeshFilter>().sharedMesh = arrowMesh;
                    var r = arrow.AddComponent<MeshRenderer>();
                    r.sharedMaterial = arrowMat;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    arrows[k] = arrow.transform;
                }
                arrowTotal += arrows.Length;
                _arrowPaths.Add(seg);
                _arrowCum.Add(cum);
                _arrows.Add(arrows);
            }
            MoveArrows();

            Plugin.Log.LogInfo($"trail from the last playtest, {_count} points over {_segments.Count} pieces, {arrowTotal} arrows");
        }
    }
}
