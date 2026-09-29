using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ScreenBrush;

internal static class ModeShortcutChecks
{
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    private static void Key(byte key, bool up = false) => keybd_event(key, 0, up ? 2u : 0u, UIntPtr.Zero);
    private static void Shortcut(byte key) { Key(0xA2); Key(0xA0); Key(key); Key(key, true); Key(0xA0, true); Key(0xA2, true); }
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    internal static void Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try
            {
                using var controller = new AppController(new Settings { ShowToolbarOnStartup = true, PreserveSessionOnEscape = true }, false);
                controller.Start(); await Task.Delay(150);
                var keys = (Hotkeys)typeof(AppController).GetField("hotkeys", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
                var active = (IDictionary)typeof(Hotkeys).GetField("active", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(keys)!;
                Require(active.Count == 1 && active.Contains(ActionId.ToggleDrawing), "Inactive mode registers only the mode toggle: " + controller.Toolbar.StatusText);
                var reserved = controller.Settings.Shortcuts[ActionId.ToggleZoom];
                bool available = Native.RegisterHotKey(IntPtr.Zero, 29001, (uint)reserved.Modifiers, (uint)KeyInterop.VirtualKeyFromKey(reserved.Key));
                if (available) Native.UnregisterHotKey(IntPtr.Zero, 29001);
                Require(available, "Inactive mode leaves zoom shortcut available to other apps.");
                Shortcut(0x31); await Task.Delay(120);
                Require(!controller.Drawing && controller.Settings.BrushTool == Tool.Marker, "Pen shortcut cannot start drawing while inactive.");
                controller.SelectTool(Tool.Rectangle);
                Require(!controller.Drawing && controller.Settings.DrawingMode == DrawingMode.Rectangle, "Tool button only changes selection.");
                controller.Toolbar.Activate(); Key(0xA4); Key(0x73); Key(0x73, true); Key(0xA4, true); await Task.Delay(150);
                Require(!controller.Disposing, "Alt+F4 on the toolbar does not quit in inactive mode.");
                Shortcut(0x71); await Task.Delay(200);
                Require(controller.Drawing && active.Count == controller.Settings.ActiveShortcuts().Count, "Mode shortcut enables drawing and registers enabled actions: " + controller.Toolbar.StatusText);
                Shortcut(0x31); await Task.Delay(120);
                Require(controller.Settings.BrushTool == Tool.Ballpoint, "Pen shortcut works in drawing mode.");
                Key(0xA2); await Task.Delay(180);
                Require(controller.Drawing && controller.Overlays.All(o => !o.Surface.DrawingEnabled), "Enabled Ctrl hold temporarily exposes the screen.");
                controller.Execute(ActionId.ToggleHoldInteraction); await Task.Delay(180);
                Require(!controller.Settings.HoldToInteractEnabled && controller.Overlays.All(o => o.Surface.DrawingEnabled), "Disabling temporary interaction while Ctrl is held immediately restores drawing.");
                Key(0xA2, true);
                Key(0xA4); Shortcut(0x48); Key(0xA4, true); await Task.Delay(180);
                Require(controller.Settings.HoldToInteractEnabled, "Ctrl+Alt+Shift+H enables temporary interaction.");
                Key(0xA4); Shortcut(0x48); Key(0xA4, true); await Task.Delay(180);
                Require(!controller.Settings.HoldToInteractEnabled, "Ctrl+Alt+Shift+H disables temporary interaction.");
                Key(0xA2); await Task.Delay(220);
                Require(controller.Overlays.All(o => o.Surface.DrawingEnabled), "Ctrl and the hold recovery timer leave drawing active when the option is disabled.");
                Key(0xA2, true);
                Key(0xA0); mouse_event(0x800, 0, 0, 120, UIntPtr.Zero); Key(0xA0, true); await Task.Delay(150);
                Require(controller.Overlays.All(o => o.Zoom == 1), "Shift wheel does not zoom in drawing mode.");
                controller.SelectBoard(BoardBackground.White);
                controller.Settings.ToggleZoomPercent = 500;
                Shortcut(0x72); await Task.Delay(500);
                Require(controller.Overlays.Any(o => o.Zoom == 5), "Zoom toggle reaches 500 percent independently of the manual zoom cap.");
                Shortcut(0x72); await Task.Delay(400);
                Require(controller.Overlays.All(o => o.Zoom == 1), "Second zoom toggle returns to 100 percent.");
                controller.Settings.ToggleZoomPercent = 150;
                Shortcut(0x72); await Task.Delay(350);
                Require(controller.Overlays.Any(o => o.Zoom == 1.5), "Zoom toggle supports the 150 percent minimum.");
                Key(0x1B); Key(0x1B, true); await Task.Delay(150);
                Require(!controller.Drawing && active.Count == 1 && controller.Overlays.All(o => !o.Surface.DrawingEnabled && !o.BoardVisible), "Esc releases all other shortcuts and exposes the original screen.");
                Shortcut(0x70); Shortcut(0x72); await Task.Delay(150);
                Require(controller.Toolbar.IsVisible && !controller.Drawing, "Toolbar and zoom shortcuts are inactive after Esc.");
                Shortcut(0x71); await Task.Delay(150);
                Require(controller.Drawing && !controller.Settings.WhiteboardEnabled, "Mode shortcut resumes without automatically restoring whiteboard.");
                Shortcut(0x71); await Task.Delay(150);
                Require(!controller.Drawing && active.Count == 1, "Mode shortcut also turns drawing off.");
                Console.WriteLine("PASS: real mode/pen/zoom/Esc keys, inactive registration release, local Alt+F4, temporary interaction option/toggle, no wheel zoom, 150/500 percent toggles, and mode restoration.");
            }
            catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
            finally { Key(0xA2, true); Key(0xA0, true); Key(0xA4, true); app.Shutdown(); }
        };
        app.Run();
    }
}
