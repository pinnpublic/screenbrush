using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenBrush;

// Keep native clicks, capture, hover, and drag semantics. Only pointer motion is
// mapped into the live desktop; its visible cursor stays in magnified coordinates.
internal sealed class ZoomInteraction : IDisposable
{
    [DllImport("Magnification.dll")] private static extern bool MagInitialize();
    [DllImport("Magnification.dll")] private static extern bool MagUninitialize();
    [DllImport("Magnification.dll")] private static extern bool MagShowSystemCursor(bool show);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(IntPtr window, uint key, byte alpha, uint flags);
    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { internal int size, flags; internal IntPtr cursor; internal Native.POINT position; }
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { internal int icon; internal uint x, y; internal IntPtr mask, color; }

    private readonly IReadOnlyList<OverlayWindow> overlays;
    private readonly IntPtr toolbar;
    private readonly Dictionary<IntPtr, long> transparent = new();
    private readonly Native.HookProc callback;
    private IntPtr hook, cursorHandle;
    private BitmapSource? cursorImage;
    private Point hotspot, position;
    private bool initialized, cursorHidden, disposed, movingPointer;
    private int buttons;
    private OverlayWindow? dragSurface;
    internal event Action<string>? Failed;
    internal ZoomInteraction(IReadOnlyList<OverlayWindow> overlays, IntPtr toolbar)
    {
        this.overlays = overlays; this.toolbar = toolbar; callback = MouseHook;
        try
        {
            Native.GetCursorPos(out var point); position = new Point(point.X, point.Y);
            initialized = MagInitialize();
            if (!initialized) throw new Win32Exception("확대 화면의 마우스 표시를 준비하지 못했습니다.");
            SyncWindowsCore();
            hook = Native.SetWindowsHookEx(14, callback, Native.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "확대 화면의 일시 조작을 시작하지 못했습니다.");
            RenderCursor(MovePointer());
            CompositionTarget.Rendering += Refresh;
        }
        catch { Dispose(); throw; }
    }
    internal void SyncWindows()
    {
        if (disposed) return;
        try { SyncWindowsCore(); }
        catch (Exception error) when (error is Win32Exception or ArgumentException or InvalidOperationException)
        { StopAfterFailure(error); }
    }
    private void SyncWindowsCore()
    {
        var active = overlays.Where(o => o.ZoomInteractionActive).Select(o => o.Handle).ToHashSet();
        foreach (var window in transparent.Keys.ToArray())
            if (window != toolbar && !active.Contains(window)) RestoreWindow(window);
        foreach (var window in active) MakeTransparent(window);
    }
    private void MakeTransparent(IntPtr window)
    {
        if (window == IntPtr.Zero || transparent.ContainsKey(window)) return;
        long style = Native.GetWindowLongPtr(window, -20).ToInt64();
        transparent.Add(window, style);
        HwndSource.FromHwnd(window)?.AddHook(LayeredStyle);
        Native.SetWindowLongPtr(window, -20, new IntPtr(style | 0x80020));
        if (!SetLayeredWindowAttributes(window, 0, 255, 2)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private IntPtr LayeredStyle(IntPtr window, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        // HwndTarget normally clears LAYERED on opaque WPF windows. Preserve a
        // constant-alpha layer only for this temporary native input pass-through.
        if (message == 0x7C && wparam.ToInt64() == -20 && transparent.ContainsKey(window))
        {
            Marshal.WriteInt32(lparam, 4, Marshal.ReadInt32(lparam, 4) | 0x80020);
            handled = true;
        }
        return IntPtr.Zero;
    }
    private void RestoreWindow(IntPtr window)
    {
        if (!transparent.Remove(window, out long style)) return;
        HwndSource.FromHwnd(window)?.RemoveHook(LayeredStyle);
        long current = Native.GetWindowLongPtr(window, -20).ToInt64();
        Native.SetWindowLongPtr(window, -20, new IntPtr((current & ~0x80020L) | (style & 0x80020)));
    }
    private bool OverToolbar => toolbar != IntPtr.Zero && IsWindowVisible(toolbar) && Native.GetWindowRect(toolbar, out var rect) &&
        position.X >= rect.Left && position.X < rect.Right && position.Y >= rect.Top && position.Y < rect.Bottom;
    private OverlayWindow? SurfaceAtPointer
    {
        get
        {
            if (buttons != 0) return dragSurface?.ZoomInteractionActive == true ? dragSurface : null;
            if (OverToolbar) return null;
            foreach (var overlay in overlays)
                if (overlay.ZoomInteractionActive && overlay.Screen.Bounds.Contains((int)position.X, (int)position.Y)) return overlay;
            return null;
        }
    }
    private IntPtr MouseHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && !disposed && message.ToInt32() == 0x200)
        {
            var mouse = Marshal.PtrToStructure<Native.MouseData>(data);
            // SetCursorPos may notify input hooks on some desktop/session types.
            // Never remap our own synchronous pointer correction a second time.
            if (movingPointer) return Native.CallNextHookEx(hook, code, message, data);
            Native.GetCursorPos(out var real);
            var desktop = System.Windows.Forms.SystemInformation.VirtualScreen;
            position.X = Math.Clamp(position.X + mouse.point.X - real.X, desktop.Left, desktop.Right - 1);
            position.Y = Math.Clamp(position.Y + mouse.point.Y - real.Y, desktop.Top, desktop.Bottom - 1);
            try { MovePointer(); }
            catch (Exception e) when (e is Win32Exception or ArgumentException or InvalidOperationException)
            { StopAfterFailure(e); }
            return new IntPtr(1);
        }
        if (code >= 0 && !disposed)
        {
            int msg = message.ToInt32();
            bool down = msg is 0x201 or 0x204 or 0x207 or 0x20B;
            bool up = msg is 0x202 or 0x205 or 0x208 or 0x20C;
            if (down || up)
            {
                var mouse = Marshal.PtrToStructure<Native.MouseData>(data);
                int bit = msg is 0x201 or 0x202 ? 1 : msg is 0x204 or 0x205 ? 2 : msg is 0x207 or 0x208 ? 4 : (mouse.mouseData >> 16) == 1 ? 8 : 16;
                if (down) { if (buttons == 0) dragSurface = SurfaceAtPointer; buttons |= bit; }
                if (up) { buttons &= ~bit; if (buttons == 0) dragSurface = null; }
            }
        }
        return Native.CallNextHookEx(hook, code, message, data);
    }
    private OverlayWindow? MovePointer()
    {
        var surface = SurfaceAtPointer;
        if (surface != null) MakeTransparent(toolbar); else RestoreWindow(toolbar);
        var real = new Native.POINT { X = (int)Math.Round(position.X), Y = (int)Math.Round(position.Y) };
        if (surface != null) real = surface.MapInteractionPoint(real);
        SetCursorHidden(surface != null);
        Native.GetCursorPos(out var current);
        if (current.X != real.X || current.Y != real.Y)
        {
            movingPointer = true;
            try { SetCursorPos(real.X, real.Y); }
            finally { movingPointer = false; }
        }
        return surface;
    }
    private void SetCursorHidden(bool value)
    {
        if (cursorHidden == value) return;
        if (!MagShowSystemCursor(!value)) throw new Win32Exception("확대 화면의 마우스 표시를 변경하지 못했습니다.");
        cursorHidden = value;
    }
    private void Refresh(object? sender, EventArgs e)
    {
        if (disposed) return;
        try { RenderCursor(MovePointer()); }
        catch (Exception error) when (error is Win32Exception or ArgumentException or InvalidOperationException)
        { StopAfterFailure(error); }
    }
    private void StopAfterFailure(Exception error)
    {
        Dispose();
        overlays[0].Dispatcher.BeginInvoke(() => Failed?.Invoke("확대 화면의 일시 조작을 중단했습니다: " + error.Message));
    }
    private void RenderCursor(OverlayWindow? surface)
    {
        if (surface != null)
        {
            var info = new CursorInfo { size = Marshal.SizeOf<CursorInfo>() };
            if (GetCursorInfo(ref info) && info.cursor != IntPtr.Zero && cursorHandle != info.cursor && GetIconInfo(info.cursor, out var icon))
            {
                try
                {
                    cursorImage = Imaging.CreateBitmapSourceFromHIcon(info.cursor, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    cursorImage.Freeze(); hotspot = new Point(icon.x, icon.y); cursorHandle = info.cursor;
                }
                finally { if (icon.mask != IntPtr.Zero) Native.DeleteObject(icon.mask); if (icon.color != IntPtr.Zero) Native.DeleteObject(icon.color); }
            }
        }
        foreach (var overlay in overlays) overlay.ShowInteractionCursor(overlay == surface ? cursorImage : null, position, hotspot);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        CompositionTarget.Rendering -= Refresh;
        if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
        // Finish a native drag at its document position before restoring the
        // visible pointer. Releasing Ctrl mid-drag must not leave a button held.
        if (dragSurface != null)
        {
            if ((buttons & 1) != 0) mouse_event(4, 0, 0, 0, UIntPtr.Zero);
            if ((buttons & 2) != 0) mouse_event(0x10, 0, 0, 0, UIntPtr.Zero);
            if ((buttons & 4) != 0) mouse_event(0x40, 0, 0, 0, UIntPtr.Zero);
            if ((buttons & 8) != 0) mouse_event(0x100, 0, 0, 1, UIntPtr.Zero);
            if ((buttons & 16) != 0) mouse_event(0x100, 0, 0, 2, UIntPtr.Zero);
        }
        foreach (var window in transparent.Keys.ToArray()) RestoreWindow(window);
        foreach (var overlay in overlays) overlay.ShowInteractionCursor(null, position, hotspot);
        if (initialized)
        {
            SetCursorPos((int)Math.Round(position.X), (int)Math.Round(position.Y));
            if (cursorHidden) MagShowSystemCursor(true);
            MagUninitialize();
        }
    }
}
