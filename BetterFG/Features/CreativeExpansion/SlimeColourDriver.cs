using System;
using UnityEngine;

namespace BetterFG.Features.CreativeExpansion
{
    public class SlimeColourDriver : MonoBehaviour
    {
        public SlimeColourDriver(IntPtr ptr) : base(ptr) { }

        void LateUpdate() => SlimeColours.FollowFloorUnderPlayer();
    }
}
