using System;
using UnityEngine;

namespace BetterFG.Customization.UI
{
    public class LayoutSync : MonoBehaviour
    {
        public LayoutSync(IntPtr ptr) : base(ptr) { }

        private void LateUpdate() => MenuLayoutService.Tick();
    }
}
