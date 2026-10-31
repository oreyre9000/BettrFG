using System;
using UnityEngine;
using UnityEngine.Events;

namespace BepInEx.Logging
{
    public class ManualLogSource
    {
        public void LogInfo(object o) => Debug.Log(o);
        public void LogMessage(object o) => Debug.Log(o);
        public void LogDebug(object o) { }
        public void LogWarning(object o) => Debug.LogWarning(o);
        public void LogError(object o) => Debug.LogError(o);
    }
}

namespace Il2CppInterop.Runtime
{
    internal static class Il2CppType
    {
        public static Type Of<T>() => typeof(T);
    }
}

namespace BetterFG
{
    internal static class Plugin
    {
        public static readonly BepInEx.Logging.ManualLogSource Log = new BepInEx.Logging.ManualLogSource();
    }
}

namespace BettrFG.uGUI
{
    internal static class Input
    {
        public static Vector3 mousePosition;
        public static Vector2 mouseScrollDelta;
        public static bool Held, Down, Up;
        public static bool GetMouseButtonDown(int b) => b == 0 && Down;
        public static bool GetMouseButtonUp(int b) => b == 0 && Up;
        public static bool GetMouseButton(int b) => b == 0 && Held;
    }

    internal static class Time
    {
        public static float unscaledDeltaTime, deltaTime, unscaledTime, time;
        public static int frameCount;
    }

    internal static class ShipFonts
    {
        public static TMPro.TMP_FontAsset FromFile(string path, params object[] _) => TMPro.TMP_Settings.defaultFontAsset;
    }

    internal static class ShipShimExt
    {
        public static void AddListener(this UnityEvent e, Action a) => e.AddListener(new UnityAction(a));
        public static void AddListener<T>(this UnityEvent<T> e, Action<T> a) => e.AddListener(new UnityAction<T>(a));
        public static void RegisterDirtyVerticesCallback(this UnityEngine.UI.Graphic g, Action a) => g.RegisterDirtyVerticesCallback(new UnityAction(a));
        public static T TryCast<T>(this object o) where T : class => o as T;
        public static IntPtr CachedPtr(this UnityEngine.Object o) => o == null ? IntPtr.Zero : (IntPtr)o.GetInstanceID();
    }
}
