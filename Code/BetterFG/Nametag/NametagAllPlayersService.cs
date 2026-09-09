using System;
using System.Collections.Generic;
using BetterFG.Services;
using FGClient;
using UnityEngine;

namespace BetterFG.Nametag
{
    public static class NametagAllPlayersService
    {
        public const string KEY_HIDE_ARROW = "nametag.all.hide_arrow";
        public const string KEY_SCALE = "nametag.all.scale";
        public const string KEY_ARROW_SCALE = "nametag.all.arrow_scale";

        private const float LIFT_SHARE = 0.72f;

        private static bool _hideArrow;
        private static float _scale = 1f;
        private static float _arrowScale = 1f;
        private static int _readFrame = -1;

        private struct ArrowBase
        {
            public Vector3 scale;
            public float height;
        }

        private static readonly Dictionary<IntPtr, ArrowBase> _arrowBase = new Dictionary<IntPtr, ArrowBase>();
        private static readonly Dictionary<IntPtr, Vector3> _restBase = new Dictionary<IntPtr, Vector3>();

        private class Row
        {
            public Transform t;
            public Vector3 lastScale;
            public GameObject arrowGo;
            public Transform arrowT;
            public ArrowBase arrowBase;
            public Vector3 lastArrowScale;
            public bool lastArrowActive;
            public Transform[] rest;
            public Vector3[] restBase;
            public float lastLift;
        }

        private class HudCache
        {
            public int count = -1;
            public readonly List<Row> rows = new List<Row>();
        }

        private static readonly Dictionary<IntPtr, HudCache> _huds = new Dictionary<IntPtr, HudCache>();

        public static void Invalidate() { _huds.Clear(); _readFrame = -1; }

        private static Vector3 BaseOf(Transform t)
        {
            if (!_restBase.TryGetValue(t.Pointer, out var bp))
            {
                bp = t.localPosition;
                _restBase[t.Pointer] = bp;
            }
            return bp;
        }

        public static void Tick(PlayerInfoHUDBase hud)
        {
            var spawned = hud?._spawnedInfoObjects;
            if (spawned == null) return;

            int frame = Time.frameCount;
            if (_readFrame != frame)
            {
                _readFrame = frame;
                _hideArrow = SettingsService.Get(KEY_HIDE_ARROW, "false") == "true";
                _scale = float.TryParse(SettingsService.Get(KEY_SCALE, "1"),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                    out float s) ? Mathf.Clamp(s, 0.05f, 10f) : 1f;
                _arrowScale = float.TryParse(SettingsService.Get(KEY_ARROW_SCALE, "1"),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                    out float a) ? Mathf.Clamp(a, 0.05f, 10f) : 1f;
            }

            bool identity = !_hideArrow && Mathf.Abs(_scale - 1f) < 0.0005f
                            && Mathf.Abs(_arrowScale - 1f) < 0.0005f;
            if (identity && !_huds.ContainsKey(hud.Pointer)) return;

            int count = spawned.Count;
            IntPtr id = hud.Pointer;
            if (!_huds.TryGetValue(id, out var cache))
            {
                cache = new HudCache();
                _huds[id] = cache;
            }
            if (cache.count != count)
            {
                cache.count = count;
                cache.rows.Clear();
                for (int i = 0; i < count; i++)
                {
                    var display = spawned[i]?.playerInfo;
                    if (display == null) continue;
                    GameObject arrow = null;
                    float arrowH = 0f;
                    Transform platform = null;
                    var go = display.TryCast<PlayerInfoDisplayGameObject>();
                    if (go != null && go._arrowRenderer != null)
                    {
                        arrow = go._arrowRenderer.gameObject;
                        var spr = go._arrowRenderer.sprite;
                        arrowH = spr != null ? spr.bounds.max.y * arrow.transform.localScale.y : 0f;
                        if (go._platformIconRenderer != null) platform = go._platformIconRenderer.transform;
                    }
                    else
                    {
                        var canvas = display.TryCast<PlayerInfoDisplayCanvas>();
                        if (canvas != null && canvas._arrowImage != null)
                        {
                            arrow = canvas._arrowImage.gameObject;
                            var art = canvas._arrowImage.rectTransform;
                            arrowH = art.rect.height * art.localScale.y * (1f - art.pivot.y);
                            if (canvas._platformIconImage != null) platform = canvas._platformIconImage.transform;
                        }
                    }
                    var arrowT = arrow != null ? arrow.transform : null;
                    var ab = new ArrowBase { scale = Vector3.one };
                    Transform[] rest = null;
                    Vector3[] restBase = null;
                    if (arrowT != null)
                    {
                        if (!_arrowBase.TryGetValue(arrowT.Pointer, out ab))
                        {
                            ab = new ArrowBase { scale = arrowT.localScale, height = arrowH };
                            _arrowBase[arrowT.Pointer] = ab;
                        }
                        var rt = new List<Transform>(2);
                        var rb = new List<Vector3>(2);
                        var nameGroup = NametagIconApplicator.NameAndCrownOf(display);
                        if (nameGroup != null) { rt.Add(nameGroup); rb.Add(BaseOf(nameGroup)); }
                        if (platform != null) { rt.Add(platform); rb.Add(BaseOf(platform)); }
                        rest = rt.ToArray();
                        restBase = rb.ToArray();
                    }
                    cache.rows.Add(new Row
                    {
                        t = display.transform, arrowGo = arrow, arrowT = arrowT, arrowBase = ab,
                        rest = rest, restBase = restBase, lastLift = float.NaN,
                        lastScale = Vector3.one, lastArrowScale = ab.scale, lastArrowActive = true,
                    });
                }
            }

            var wantScale = new Vector3(_scale, _scale, _scale);
            bool wantArrow = !_hideArrow;
            var list = cache.rows;
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                if (r.t == null || r.t.m_CachedPtr == IntPtr.Zero) { cache.count = -1; return; }
                if (r.lastScale != wantScale) { r.t.localScale = wantScale; r.lastScale = wantScale; }
                if (r.arrowGo == null) continue;
                if (r.lastArrowActive != wantArrow)
                {
                    r.arrowGo.SetActive(wantArrow);
                    r.lastArrowActive = wantArrow;
                }
                var wantArrowScale = r.arrowBase.scale * _arrowScale;
                if (r.lastArrowScale != wantArrowScale)
                {
                    r.arrowT.localScale = wantArrowScale;
                    r.lastArrowScale = wantArrowScale;
                }

                if (r.rest == null || r.rest.Length == 0) continue;
                float lift = (_arrowScale - 1f) * r.arrowBase.height * LIFT_SHARE;
                bool liftChanged = r.lastLift != lift;
                r.lastLift = lift;
                for (int k = 0; k < r.rest.Length; k++)
                {
                    var target = r.rest[k];
                    if (!liftChanged && !target.hasChanged) continue;
                    var bp = r.restBase[k];
                    target.localPosition = new Vector3(bp.x, bp.y + lift, bp.z);
                    target.hasChanged = false;
                }
            }
        }
    }
}
