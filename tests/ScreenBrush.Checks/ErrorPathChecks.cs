using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ScreenBrush;

internal static class ErrorPathChecks
{
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try
            {
                using var controller = new AppController(new Settings { ShowToolbarOnStartup = true }, false);
                controller.Start(); controller.ToggleDrawing(); controller.SelectTool(Tool.Marker);
                bool timedOut = false;
                IntPtr dialog = IntPtr.Zero;
                var escape = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                escape.Tick += (_, _) => { escape.Stop(); dialog = GetForegroundWindow(); keybd_event(0x1B, 0, 0, UIntPtr.Zero); keybd_event(0x1B, 0, 2, UIntPtr.Zero); };
                var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timeout.Tick += (_, _) => { timeout.Stop(); timedOut = true; if (dialog != IntPtr.Zero) Native.PostMessage(dialog, 0x10, IntPtr.Zero, IntPtr.Zero); };
                escape.Start(); timeout.Start();
                controller.ChooseColor();
                escape.Stop(); timeout.Stop();
                Require(!timedOut && controller.Drawing && controller.Toolbar.IsVisible, "Esc must close native color dialog and restore drawing.");
                var overlay = controller.Overlays[0];
                overlay.ChangeZoom(2);
                await Task.Delay(700);
                Require(!overlay.SnapshotUsesDesktop, "Zoom fixture must be active.");
                typeof(OverlayWindow).GetMethod("FailCapture", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, new object[] { new System.ComponentModel.Win32Exception(5) });
                Require(overlay.SnapshotUsesDesktop && overlay.CaptureImage == null && overlay.Zoom == 1 && overlay.IsVisible, "Capture failure must release opaque companion and restore normal overlay.");
                overlay.ChangeZoom(2); await Task.Delay(700);
                Require(!overlay.SnapshotUsesDesktop && overlay.CaptureImage != null, "Zoom can restart after a capture failure.");
                using (var hotkeys = new Hotkeys(Key.LeftCtrl))
                {
                    int callbacks = 0;
                    hotkeys.HoldChanged += _ => callbacks++;
                    hotkeys.Dispose(); hotkeys.Dispose();
                    typeof(Hotkeys).GetMethod("RefreshHold", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hotkeys, null);
                    Require(callbacks == 0, "Disposed hooks ignore queued refreshes and support repeat disposal.");
                }
                bool completed = false;
                var write = Task.Run(async () => { await Task.Delay(60); completed = true; return "test.png"; });
                typeof(AppController).GetField("pendingScreenSave", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, write);
                controller.Dispose();
                Require(completed, "Disposal waits for already-started background PNG encoding.");
                Console.WriteLine("PASS: native color-dialog Esc, injected capture failure/recovery, disposed callbacks and pending-write shutdown.");
            }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            finally { app.Shutdown(); }
        };
        app.Run();
    }
}
