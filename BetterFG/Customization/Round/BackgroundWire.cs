using System;
using UnityEngine;

namespace BetterFG.Customization.Round
{
    public enum BackgroundKind { Round, Theme, Package, PackageGone }

    public static class BackgroundWire
    {
        public static event Action<BackgroundKind, string, GameObject> Changed;

        public static void Raise(BackgroundKind kind, string id, GameObject root = null)
        {
            if (Changed == null) return;
            foreach (Action<BackgroundKind, string, GameObject> sub in Changed.GetInvocationList())
            {
                try { sub(kind, id, root); }
                catch (Exception ex) { Plugin.Log.LogWarning($"background wire: {sub.Method.DeclaringType?.Name} choked on {kind} {id}: {ex.Message}"); }
            }
        }
    }
}
