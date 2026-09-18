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
        public override string TweakLabel => "tweak.playtest_trail";
        public override string TweakTooltip => "tweak.playtest_trail.tip";
        public override bool DefaultEnabled => true;

        const float StepDistance = 0.35f;
        const int MaxPoints = 20000;
        const string TrailName = "BettrFG_PlaytestTrail";
        static readonly Color TrailColour = new Color(0.15f, 0.85f, 1f);

        readonly List<Vector3> _points = new List<Vector3>();
        bool _recording;
        GameObject _trail;

        public override void OnLevelEditorPlaytest()
        {
            Clear();
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
            if (!_recording || _points.Count >= MaxPoints) return;

            var mgr = LevelEditorManager.Instance;
            var bean = mgr != null ? mgr._playerGameObject : null;
            if (bean == null) return;

            var p = bean.transform.position;
            if (_points.Count > 0 && (p - _points[_points.Count - 1]).sqrMagnitude < StepDistance * StepDistance) return;
            _points.Add(p);
        }

        void Clear()
        {
            _points.Clear();
            if (_trail != null) UnityEngine.Object.Destroy(_trail);
            _trail = null;
        }

        void Draw()
        {
            if (_trail != null) UnityEngine.Object.Destroy(_trail);
            if (_points.Count < 2)
            {
                Plugin.Log.LogInfo("playtest trail: barely moved, nothing to draw");
                return;
            }

            _trail = new GameObject(TrailName);
            var line = _trail.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.widthMultiplier = 0.12f;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.startColor = TrailColour;
            line.endColor = TrailColour;
            line.material = new Material(IdentifierObjects.UnlitShader()) { name = TrailName };

            var mat = line.material;
            foreach (int id in IdentifierObjects.ColourPropertyIds)
                if (mat.HasProperty(id)) mat.SetColor(id, TrailColour);

            line.positionCount = _points.Count;
            for (int i = 0; i < _points.Count; i++) line.SetPosition(i, _points[i]);

            Plugin.Log.LogInfo($"trail from the last playtest, {_points.Count} points");
        }
    }
}
