using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ScreenBrush;

internal static class Native
{
    [DllImport("dwmapi.dll")] internal static extern int DwmFlush();
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardData { public uint vkCode, scanCode, flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseData { public POINT point; public uint mouseData, flags, time; public UIntPtr extra; }
    internal delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] internal static extern bool EnableWindow(IntPtr window, bool enabled);
    [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr window, ref POINT point);
    [DllImport("user32.dll")] private static extern IntPtr ChildWindowFromPointEx(IntPtr parent, POINT point, uint flags);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] internal static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
    internal static void PassThrough(Window window, bool enabled)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        long style = GetWindowLongPtr(hwnd, -20).ToInt64();
        style = enabled ? style | 0x20 : style & ~0x20L;
        SetWindowLongPtr(hwnd, -20, new IntPtr(style | 0x08000000 | 0x80)); // NOACTIVATE | TOOLWINDOW
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x37);
    }
    internal static IntPtr WindowBelowAt(POINT point, HashSet<IntPtr> excluded)
    {
        var visited = new HashSet<IntPtr>();
        for (var window = GetTopWindow(IntPtr.Zero); window != IntPtr.Zero && visited.Add(window); window = GetWindow(window, 2))
        {
            if (excluded.Contains(window) || !IsWindowVisible(window) || !IsWindowEnabled(window)) continue;
            if (!GetWindowRect(window, out var rect) || point.X < rect.Left || point.X >= rect.Right || point.Y < rect.Top || point.Y >= rect.Bottom) continue;
            var target = window;
            for (int depth = 0; depth < 32; depth++)
            {
                var client = point;
                if (!ScreenToClient(target, ref client)) break;
                var child = ChildWindowFromPointEx(target, client, 7);
                if (child == IntPtr.Zero || child == target) break;
                target = child;
            }
            return target;
        }
        return IntPtr.Zero;
    }
}
