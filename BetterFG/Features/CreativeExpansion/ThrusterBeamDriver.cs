using System;
using System.Collections.Generic;
using LevelEditor;
using Levels.Obstacles;
using UnityEngine;

namespace BetterFG.Features.CreativeExpansion
{
    public class ThrusterBeamDriver : MonoBehaviour
    {
        public ThrusterBeamDriver(IntPtr ptr) : base(ptr) { }

        private const int SearchAttempts = 5;
        private const float SearchInterval = 1f;
        private const float Idle = 0.02f;

        private readonly List<Collider> _solids = new List<Collider>();
        private Transform _marker;
        private Transform _controller;
        private COMMON_Thruster _thruster;
        private GameObject _beam;
        private float _travel;
        private Transform _target;
        private bool _extending;
        private float _nextSearch;
        private int _attempts;

        internal void Bind(Transform marker, Transform controller, COMMON_Thruster thruster, float travel)
        {
            _marker = marker;
            if (controller != null) _controller = controller;
            _thruster = thruster;
            _travel = Mathf.Max(0.01f, travel);

            if (_target == null) { _attempts = 0; _nextSearch = 0f; }
            if (_thruster == null) return;

            _thruster._beamLength = _travel;
            _thruster._sideBumpVelocity = 0f;
            _thruster._topBumpVelocity = 0f;

            _beam = _thruster._beamVisual;
            if (_beam != null && _beam.activeSelf != _extending) _beam.SetActive(_extending);
        }

        private void LateUpdate()
        {
            if (_marker == null) { UnityEngine.Object.Destroy(gameObject); return; }
            if (!_marker.gameObject.activeInHierarchy)
            {
                Thrusters.SetRigActive(_marker.gameObject.GetInstanceID(), false);
                UnityEngine.Object.Destroy(gameObject);
                return;
            }
            if (Thrusters.HasFresh) Thrusters.DropIfCancelled();
            if (_thruster == null) return;

            transform.position = _controller != null ? _controller.position : _marker.position;
            transform.rotation = _controller != null ? _controller.rotation : Quaternion.identity;

            for (int i = 0; i < _solids.Count; i++)
            {
                var c = _solids[i];
                if (c is not null && c.m_CachedPtr != IntPtr.Zero && c.enabled) c.enabled = false;
            }

            if (Thrusters.Previewing(_marker))
            {
                if (!_extending)
                {
                    if (_beam != null) _beam.SetActive(true);
                    _thruster.GoToExtending();
                    _extending = true;
                }
                _thruster.UpdateBeamLength(_travel, true);
                return;
            }

            if (_target == null)
            {
                if (_attempts >= SearchAttempts || Time.time < _nextSearch) return;
                _nextSearch = Time.time + SearchInterval;
                _attempts++;
                _target = FindDrivenObject();
                if (_target == null) return;
            }

            float proportion = Mathf.Clamp01((Vector3.Dot(_target.position - transform.position, transform.up) + Thrusters.BumperTop) / _travel);

            try
            {
                if (proportion > Idle)
                {
                    if (!_extending)
                    {
                        if (_beam != null) _beam.SetActive(true);
                        _thruster.GoToExtending();
                        _extending = true;
                    }
                    _thruster.UpdateBeamLength(proportion * _travel, true);
                }
                else if (_extending)
                {
                    _thruster.GoToPrimed();
                    _thruster.UpdateBeamLength(0f, true);
                    if (_beam != null) _beam.SetActive(false);
                    _extending = false;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"the game's thruster threw driving its beam, dropping this one: {ex.Message}");
                _thruster = null;
            }
        }

        private void Claim(LevelEditorPlaceableObject lepo)
        {
            foreach (var c in lepo.GetComponentsInChildren<Collider>(true))
                if (c != null && !c.isTrigger && !_solids.Contains(c)) _solids.Add(c);
        }

        private Transform FindDrivenObject()
        {
            var pos = _marker.position;

            LevelEditorPlaceableObject controller = null;
            foreach (var mover in UnityEngine.Object.FindObjectsOfType<LevelEditorMovementActiveBase>())
            {
                if (mover == null) continue;
                if ((mover.transform.position - pos).sqrMagnitude > Thrusters.SameSpot * Thrusters.SameSpot) continue;
                controller = mover.GetComponentInParent<LevelEditorPlaceableObject>();
                break;
            }

            if (controller != null)
            {
                Thrusters.HideFromEditor(controller, true, false);
                Claim(controller);
                _controller = controller.transform;

                var mover = controller.GetComponentInChildren<LevelEditorMovementActiveBase>(true);
                var waypoints = mover != null ? mover._waypointsList : null;
                if (waypoints != null && waypoints.Count > 0 && waypoints[0] != null)
                {
                    var wpLepo = waypoints[0].GetComponentInParent<LevelEditorPlaceableObject>();
                    if (wpLepo != null) Claim(wpLepo);
                }
                var marker = _marker.GetComponentInParent<LevelEditorPlaceableObject>();
                if (marker != null) Claim(marker);
            }

            if (controller == null || controller.LinkController == null)
            {
                if (_attempts >= SearchAttempts)
                    Plugin.Log.LogWarning($"no movement controller sitting on the thruster at {pos}, its beam has nothing to follow");
                return null;
            }

            var bumper = Thrusters.DrivenOf(controller);
            if (bumper == null)
            {
                if (_attempts >= SearchAttempts)
                    Plugin.Log.LogWarning($"thruster at {pos} found its controller but nothing linked to it yet, so the beam stays down");
                return null;
            }

            Thrusters.HideFromEditor(bumper, false, true);
            return bumper.transform;
        }
    }
}
