using System;
using System.Collections.Generic;
using FGClient;
using UnityEngine;

namespace BetterFG.Nametag
{
    public static class CrownRankFovFix
    {
        class Entry
        {
            public Transform root;
            public Transform container;
            public Vector3 lastScale;
        }

        class HudRows
        {
            public int count = -1;
            public readonly List<Entry> entries = new List<Entry>();
        }

        static readonly Dictionary<IntPtr, HudRows> _huds = new Dictionary<IntPtr, HudRows>();
        static Camera _levelCam;
        static Camera _uiCam;
        static int _crownLayer = 5;

        public static void Invalidate() => _huds.Clear();

        public static void Apply(PlayerInfoHUDBase hud)
        {
            var cam = Camera.main;
            if (cam == null) cam = hud._levelCamera;
            if (cam == null || cam.orthographic) return;

            // this runs off PlayerInfoHUDBase.LateUpdate, once a frame with a row loop that's as long as
            // the player list, so every il2cpp round trip in here is paid ~60x a frame late in a show.
            // m_CachedPtr comparisons instead of the == operator: op_Equality is a real il2cpp invoke.
            if (_levelCam is null || _levelCam.m_CachedPtr != cam.m_CachedPtr)
            {
                _levelCam = cam;
                _uiCam = null;
                foreach (var cand in cam.GetComponentsInChildren<Camera>(true))
                {
                    if (cand.GetInstanceID() == cam.GetInstanceID()) continue;
                    if ((cand.cullingMask & (1 << _crownLayer)) == 0) continue;
                    _uiCam = cand;
                    break;
                }
            }
            if (_uiCam == null || _uiCam.orthographic) return;

            float fov = cam.fieldOfView;
            if (Mathf.Abs(_uiCam.fieldOfView - fov) > 0.0005f) _uiCam.fieldOfView = fov;

            if (!CrownRankService.Enabled) return;

            var rows = Rows(hud);
            if (rows == null) return;

            var list = rows.entries;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e.container.m_CachedPtr == IntPtr.Zero) { rows.count = -1; return; }
                if (e.lastScale != Vector3.one) { e.container.localScale = Vector3.one; e.lastScale = Vector3.one; }
                // hasChanged only goes true when something actually moved the container, so the
                // write is skipped outright instead of reading localPosition back to compare
                if (!e.container.hasChanged) continue;
                e.container.localPosition = Vector3.zero;
                e.container.hasChanged = false;
            }
        }

        static HudRows Rows(PlayerInfoHUDBase hud)
        {
            var spawned = hud._spawnedInfoObjects;
            if (spawned == null) return null;
            int count = spawned.Count;
            if (count == 0) return null;

            IntPtr hudId = hud.Pointer;
            if (!_huds.TryGetValue(hudId, out var rows))
            {
                rows = new HudRows();
                _huds[hudId] = rows;
            }
            if (rows.count == count) return rows;

            rows.count = count;
            rows.entries.Clear();

            for (int i = 0; i < count; i++)
            {
                var display = spawned[i].playerInfo;
                if (display == null) continue;

                var go = display.TryCast<PlayerInfoDisplayGameObject>();
                var helper = go != null ? go._crownRankPlayerTagLayoutHelper : null;
                var crown = helper != null ? helper.crownRankObject : null;

                if (go == null || crown == null || crown.transform.childCount == 0) continue;
                if (crown.layer == display.gameObject.layer) continue;

                rows.entries.Add(new Entry
                {
                    root = crown.transform,
                    container = crown.transform.GetChild(0),
                });
            }
            return rows;
        }

        public static void Forget()
        {
            _huds.Clear();
            _levelCam = null;
            _uiCam = null;
        }
    }
}
