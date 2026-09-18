using System;
using UnityEngine;

namespace BetterFG.Features.UnityRound
{
    public class UnityRoundCoreBillboard : MonoBehaviour
    {
        public UnityRoundCoreBillboard(IntPtr ptr) : base(ptr) { }

        internal static readonly Quaternion FacingOffset = Quaternion.Euler(15f, 221f, 15f);

        private Transform _cam;

        void LateUpdate()
        {
            if (_cam == null)
            {
                var node = GameObject.Find("LevelEditorCameraNode");
                var cam = node != null ? node.GetComponentInChildren<Camera>() : null;
                if (cam == null) return;
                _cam = cam.transform;
            }

            transform.rotation = Quaternion.LookRotation(transform.position - _cam.position) * FacingOffset;
        }
    }
}
