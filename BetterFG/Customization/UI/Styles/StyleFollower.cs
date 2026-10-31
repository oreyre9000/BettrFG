using System;
using UnityEngine;

namespace BetterFG.Customization.UI.Styles
{
    public class StyleFollower : MonoBehaviour
    {
        public const int TOPBAR = 0;
        public const int LOBBY = 1;
        public const int SUPPRESS_TOPBAR = 2;

        public StyleFollower(IntPtr ptr) : base(ptr) { }

        public int Kind;
        public int Settle;
        public StyleFollower Relay;
        int _left;

        void OnEnable()
        {
            StyledUI.OnFollower(Kind, true);
            _left = Settle;
            Relay?.Arm();
        }

        void OnTransformChildrenChanged() => Relay?.Arm();

        void OnDisable()
        {
            StyledUI.OnFollower(Kind, false);
            Relay?.Arm();
        }

        public void Arm() => _left = Settle;

        void Update()
        {
            if (_left <= 0 || --_left > 0) return;
            StyledUI.OnSettled(Kind, transform);
        }
    }
}
