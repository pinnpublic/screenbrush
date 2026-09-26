using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace ScreenBrush;

internal sealed class WheelZoomHook : IDisposable
{
    private readonly Native.HookProc callback;
    private readonly IntPtr hook;
    internal Func<int, Native.POINT, bool>? Wheel { get; set; }

    internal WheelZoomHook()
    {
        callback = OnMouse;
        hook = Native.SetWindowsHookEx(14, callback, Native.GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "휠 확대 입력을 감지하지 못했습니다.");
    }

    internal static ModifierKeys Modifiers
    {
        get
        {
            ModifierKeys value = ModifierKeys.None;
            if (Down(0x11)) value |= ModifierKeys.Control;
            if (Down(0x10)) value |= ModifierKeys.Shift;
            if (Down(0x12)) value |= ModifierKeys.Alt;
            if (Down(0x5B) || Down(0x5C)) value |= ModifierKeys.Windows;
            return value;
        }
    }
    private static bool Down(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;
    private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && message.ToInt32() == 0x20A)
        {
            var mouse = Marshal.PtrToStructure<Native.MouseData>(data);
            if (Wheel?.Invoke(unchecked((short)(mouse.mouseData >> 16)), mouse.point) == true) return new IntPtr(1);
        }
        return Native.CallNextHookEx(hook, code, message, data);
    }
    public void Dispose() => Native.UnhookWindowsHookEx(hook);
}
