using System;
using UnityEngine;

namespace BetterFG.Customization.Round
{
    public class BackgroundSpin : MonoBehaviour
    {
        public BackgroundSpin(IntPtr ptr) : base(ptr) { }

        public Vector3 Axis = Vector3.right;
        public float DegreesPerSecond;

        void Update() => transform.Rotate(Axis, DegreesPerSecond * Time.deltaTime, Space.Self);
    }
}
