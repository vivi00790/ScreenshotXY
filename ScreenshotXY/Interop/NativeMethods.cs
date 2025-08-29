using System;
using System.Runtime.InteropServices;

namespace ScreenshotXY.Interop;

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    internal static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);
        
    [DllImport("user32.dll")]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    internal const int WmHotkey = 0x0312;
    internal const uint ModAlt = 0x0001;
    internal const uint ModControl = 0x0002;
    internal const uint ModShift = 0x0004;
    internal const uint ModWin = 0x0008;
}