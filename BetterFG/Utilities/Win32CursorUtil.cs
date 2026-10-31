using System;
using System.Runtime.InteropServices;

namespace BetterFG.Utilities
{
    internal static class Win32CursorUtil
    {
        [DllImport("user32.dll")] static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);
        [DllImport("user32.dll")] static extern IntPtr SetCursor(IntPtr hCursor);

        const int IDC_SIZEWE = 32644;
        static IntPtr _sizeWe = IntPtr.Zero;

        public static void SetSizeWe()
        {
            if (_sizeWe == IntPtr.Zero) _sizeWe = LoadCursor(IntPtr.Zero, new IntPtr(IDC_SIZEWE));
            SetCursor(_sizeWe);
        }
    }
}
