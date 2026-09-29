using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ScreenBrush;

internal sealed class Hotkeys : IDisposable
{
    private readonly HwndSource source;
    private readonly Dictionary<ActionId, Shortcut> active = new();
    private readonly Native.HookProc callback;
    private readonly IntPtr hook;
    private readonly DispatcherTimer recovery;
    private bool held, disposed;
    internal Func<bool>? SuppressHold { get; set; }
    internal Key HoldKey { get; set; }
    private bool holdEnabled = true;
    internal bool HoldEnabled
    {
        get => holdEnabled;
        set { if (holdEnabled == value) return; holdEnabled = value; RefreshHold(); }
    }
    internal bool Suspended { get; set; }
    internal bool EditingEnabled { get; set; }
    internal event Action<ActionId>? Triggered;
    internal event Action<bool>? HoldChanged;
    internal event Action? DrawingDismissed;
    internal Hotkeys(Key holdKey)
    {
        HoldKey = holdKey;
        source = new HwndSource(new HwndSourceParameters("ScreenBrush.Hotkeys") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        source.AddHook(Message);
        callback = KeyboardHook;
        hook = Native.SetWindowsHookEx(13, callback, Native.GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) { int error = Marshal.GetLastWin32Error(); source.Dispose(); throw new Win32Exception(error, "일시 조작 키 감지를 시작하지 못했습니다."); }
        recovery = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        recovery.Tick += (_, _) => RefreshHold();
        recovery.Start();
    }
    internal string? Apply(Dictionary<ActionId, Shortcut> desired)
    {
        var previous = new Dictionary<ActionId, Shortcut>(active);
        Clear();
        foreach (var entry in desired)
        {
            if (!Register(entry.Key, entry.Value))
            {
                int error = Marshal.GetLastWin32Error();
                Clear();
                var lost = new List<string>();
                foreach (var old in previous) if (!Register(old.Key, old.Value)) lost.Add(Settings.Label(old.Key));
                return $"{Settings.Label(entry.Key)}: {entry.Value}\n다른 앱이 사용 중이거나 등록할 수 없는 조합입니다. (오류 {error})\n단축키 목록에서 다른 키로 바꾸거나 해당 단축키 체크를 해제한 뒤 저장하세요. 버튼·컨트롤은 계속 사용할 수 있습니다." +
                    (lost.Count == 0 ? "" : "\n복원하지 못한 키: " + string.Join(", ", lost));
            }
        }
        return null;
    }
    private bool Register(ActionId action, Shortcut shortcut)
    {
        if (UsesKeyboardHook(action, shortcut)) { active[action] = shortcut; return true; }
        if (!Native.RegisterHotKey(source.Handle, 100 + (int)action, (uint)shortcut.Modifiers | 0x4000, (uint)KeyInterop.VirtualKeyFromKey(shortcut.Key))) return false;
        active[action] = shortcut;
        return true;
    }
    private static bool UsesKeyboardHook(ActionId action, Shortcut shortcut) =>
        action is ActionId.Undo or ActionId.Redo ||
        (action == ActionId.Quit && shortcut.Key == Key.F4 && shortcut.Modifiers == ModifierKeys.Alt);
    private IntPtr Message(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        if (msg == 0x312)
        {
            handled = true;
            if (!disposed && !Suspended) Triggered?.Invoke((ActionId)(wparam.ToInt32() - 100));
        }
        return IntPtr.Zero;
    }
    private IntPtr KeyboardHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && !disposed && !Suspended)
        {
            if (message.ToInt32() is 0x100 or 0x104)
            {
                var dataKey = Marshal.PtrToStructure<Native.KeyboardData>(data);
                var pressed = new Shortcut(KeyInterop.KeyFromVirtualKey((int)dataKey.vkCode), WheelZoomHook.Modifiers);
                if (EditingEnabled && pressed == new Shortcut(Key.Escape, ModifierKeys.None))
                {
                    source.Dispatcher.BeginInvoke(() => { if (!disposed && !Suspended && EditingEnabled) DrawingDismissed?.Invoke(); });
                    return new IntPtr(1);
                }
                foreach (var action in new[] { ActionId.Undo, ActionId.Redo, ActionId.Quit })
                    if ((action == ActionId.Quit || EditingEnabled) && active.TryGetValue(action, out var shortcut) && UsesKeyboardHook(action, shortcut) && shortcut == pressed)
                    {
                        source.Dispatcher.BeginInvoke(() => { if (!disposed && !Suspended && (action == ActionId.Quit || EditingEnabled)) Triggered?.Invoke(action); });
                        return new IntPtr(1);
                    }
            }
            // Read key state after Windows has processed this hook notification.
            source.Dispatcher.BeginInvoke(RefreshHold);
        }
        return Native.CallNextHookEx(hook, code, message, data);
    }
    private void SetHeld(bool value) { if (held == value) return; held = value; HoldChanged?.Invoke(value); }
    private void RefreshHold() { if (!disposed) SetHeld(HoldEnabled && !Suspended && SuppressHold?.Invoke() != true && (Native.GetAsyncKeyState(KeyInterop.VirtualKeyFromKey(HoldKey)) & 0x8000) != 0); }
    internal void ResetHold() => SetHeld(false);
    private void Clear() { foreach (var pair in active) if (!UsesKeyboardHook(pair.Key, pair.Value)) Native.UnregisterHotKey(source.Handle, 100 + (int)pair.Key); active.Clear(); }
    public void Dispose() { if (disposed) return; disposed = true; Suspended = true; recovery.Stop(); Clear(); Native.UnhookWindowsHookEx(hook); source.Dispose(); }
}
