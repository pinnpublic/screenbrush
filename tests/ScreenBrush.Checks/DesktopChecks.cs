using System.IO;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenBrush;
using Forms = System.Windows.Forms;

internal static class DesktopChecks
{
    private sealed class FixtureForm : Forms.Form
    {
        internal int Wheels;
        protected override void WndProc(ref Forms.Message message)
        {
            if (message.Msg == 0x20A) Wheels++;
            base.WndProc(ref message);
        }
    }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS: " + message); }
    internal static void AltF4(bool fromSettings = false, bool testToggle = false)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AppController? controller = null;
        using var reserved = new HwndSource(new HwndSourceParameters("ScreenBrush.AltF4ConflictTest") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        if (testToggle)
        {
            bool reservedAltF4 = Native.RegisterHotKey(reserved.Handle, 1, 1, 0x73);
            int registrationError = Marshal.GetLastWin32Error();
            Assert(reservedAltF4 || registrationError == 1409, "Alt F4 conflict exists (reserved by the test or already occupied)");
        }
        bool closed = false, failed = false;
        var timeout = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timeout.Tick += (_, _) => { failed = true; controller?.Dispose(); app.Shutdown(1); };
        app.DispatcherUnhandledException += (_, e) =>
        {
            failed = true; e.Handled = true; Console.Error.WriteLine(e.Exception);
            app.Shutdown(1);
        };
        app.Startup += async (_, _) =>
        {
            controller = new AppController(new Settings { PreserveSessionOnEscape = true, ShowToolbarOnStartup = true }, false);
            controller.Start();
            controller.Toolbar.Closed += (_, _) => closed = true;
            await Task.Delay(250);
            controller.Toolbar.Activate();
            timeout.Start();
            if (testToggle)
            {
                SendToolbarToggle(); await Task.Delay(200);
                Assert(!controller.Toolbar.IsVisible, "real Ctrl Shift F1 hides the toolbar despite Alt F4 registration conflict");
                SendToolbarToggle(); await Task.Delay(200);
                Assert(controller.Toolbar.IsVisible, "real Ctrl Shift F1 restores the toolbar from the tray");
            }
            if (fromSettings)
            {
                var dialogTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                dialogTimer.Tick += (_, _) =>
                {
                    var dialog = app.Windows.OfType<SettingsWindow>().FirstOrDefault();
                    if (dialog == null) return;
                    dialogTimer.Stop(); dialog.Activate();
                    FocusShortcutBox(dialog);
                    SendAltF4();
                };
                dialogTimer.Start();
                try { controller.OpenSettings(); }
                finally { dialogTimer.Stop(); }
                return;
            }
            SendAltF4();
        };
        try { app.Run(); }
        finally { timeout.Stop(); keybd_event(0xA4, 0, 2, UIntPtr.Zero); controller?.Dispose(); if (testToggle) Native.UnregisterHotKey(reserved.Handle, 1); }
        Assert(!failed && closed && controller?.Disposing == true, $"Alt F4 from {(fromSettings ? "settings" : "toolbar")} disposes the application without closing exceptions");
    }
    private static bool FocusShortcutBox(DependencyObject parent)
    {
        if (parent is System.Windows.Controls.TextBox box) { box.Focus(); return true; }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FocusShortcutBox(VisualTreeHelper.GetChild(parent, i))) return true;
        return false;
    }
    private static void SendAltF4()
    {
            keybd_event(0xA4, 0, 0, UIntPtr.Zero);
            keybd_event(0x73, 0, 0, UIntPtr.Zero);
            keybd_event(0x73, 0, 2, UIntPtr.Zero);
            keybd_event(0xA4, 0, 2, UIntPtr.Zero);
    }
    private static void SendToolbarToggle()
    {
        keybd_event(0xA2, 0, 0, UIntPtr.Zero); keybd_event(0xA0, 0, 0, UIntPtr.Zero);
        keybd_event(0x70, 0, 0, UIntPtr.Zero); keybd_event(0x70, 0, 2, UIntPtr.Zero);
        keybd_event(0xA0, 0, 2, UIntPtr.Zero); keybd_event(0xA2, 0, 2, UIntPtr.Zero);
    }
    internal static void StartupOptions()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try
            {
                foreach (bool visible in new[] { false, true })
                {
                    using var controller = new AppController(new Settings { PreserveSessionOnEscape = true, ShowToolbarOnStartup = visible }, false);
                    controller.Start(); await Task.Delay(150);
                    Assert(controller.Toolbar.IsVisible == visible && !controller.Drawing, $"startup window={visible}, drawing=False");
                    Assert(controller.Overlays.All(o => (Native.GetWindowLongPtr(o.Handle, -20).ToInt64() & 0x20) != 0), "startup always passes input through without showing ink or board");
                    controller.Execute(ActionId.ToggleToolbar);
                    Assert(controller.Toolbar.IsVisible == !visible && !controller.Drawing, "toolbar toggle changes visibility without changing drawing mode");
                    controller.Execute(ActionId.ToggleToolbar);
                    Assert(controller.Toolbar.IsVisible == visible && !controller.Drawing, "second toggle restores toolbar visibility");
                    if (visible) Render(controller.Toolbar, "artifacts/toolbar.png");
                }
                Settings savedPosition;
                using (var first = new AppController(new Settings { ShowToolbarOnStartup = true }, false))
                {
                    first.Start(); await Task.Delay(150);
                    first.Toolbar.Left = 80; first.Toolbar.Top = 60; await Task.Delay(150);
                    savedPosition = Settings.ReadSettings(System.Text.Json.JsonSerializer.Serialize(first.Settings));
                    Assert(savedPosition.ToolbarX != null && savedPosition.ToolbarY != null, "toolbar movement updates persistent coordinates");
                }
                foreach (bool remember in new[] { true, false })
                {
                    var settings = savedPosition.Clone(); settings.RememberToolbarPosition = remember;
                    using var restored = new AppController(settings, false);
                    restored.Start(); await Task.Delay(200);
                    var handle = new System.Windows.Interop.WindowInteropHelper(restored.Toolbar).Handle;
                    Assert(Native.GetWindowRect(handle, out var bounds), "restored toolbar bounds available");
                    bool same = bounds.Left == savedPosition.ToolbarX && bounds.Top == savedPosition.ToolbarY;
                    Assert(same == remember, "startup obeys last-position preference");
                    restored.Execute(ActionId.ToggleToolbar); restored.Execute(ActionId.ToggleToolbar);
                    Assert(Native.GetWindowRect(handle, out var shown) && shown.Left == bounds.Left && shown.Top == bounds.Top, "tray round-trip retains position");
                }
            }
            catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
            finally { app.Shutdown(Environment.ExitCode); }
        };
        app.Run();
    }
    internal static void Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AppController? controller = null;
        FixtureForm? fixture = null;
        Native.GetCursorPos(out var originalCursor);
        bool exitRequested = false, exitCompleted = false;
        app.Exit += (_, _) =>
        {
            if (!exitRequested) return;
            exitCompleted = controller?.Disposing == true;
            fixture?.Close(); SetCursorPos(originalCursor.X, originalCursor.Y);
        };
        app.Startup += async (_, _) =>
        {
            try
            {
                fixture = new FixtureForm { Text = "ScreenBrush input verification", FormBorderStyle = Forms.FormBorderStyle.None, WindowState = Forms.FormWindowState.Maximized, BackColor = System.Drawing.Color.FromArgb(231, 236, 243) };
                fixture.Show(); fixture.Activate();
                var fixtureSettings = new Settings { PreserveSessionOnEscape = true, CycleColors = false, WheelZoomEnabled = false, WheelZoomModifiers = System.Windows.Input.ModifierKeys.Control, ZoomStepPercent = 25 };
                fixtureSettings.ToolColors[Tool.Rectangle] = new ToolColor(fixtureSettings.Color, true); // Explicit per-tool rainbow fixture.
                fixtureSettings.Shortcuts[ActionId.Quit] = new(System.Windows.Input.Key.F11, System.Windows.Input.ModifierKeys.None);
                controller = new AppController(fixtureSettings, false); controller.Start();
                Assert(!controller.Toolbar.StatusText.Contains("오류"), "fixture hotkeys register: " + controller.Toolbar.StatusText);
                Assert(!controller.Toolbar.IsVisible && !controller.Drawing, "startup leaves the toolbar hidden and drawing disabled");
                Assert(controller.Overlays.All(o => (Native.GetWindowLongPtr(o.Handle, -20).ToInt64() & 0x20) != 0), "tray startup passes input to the desktop");
                var closeSettings = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                closeSettings.Tick += (_, _) =>
                {
                    var dialog = app.Windows.OfType<SettingsWindow>().FirstOrDefault();
                    if (dialog != null) { closeSettings.Stop(); dialog.Close(); }
                };
                closeSettings.Start(); controller.OpenSettings(); closeSettings.Stop();
                Assert(!controller.Toolbar.IsVisible && !controller.Drawing, "settings can open from the tray without showing the toolbar");
                controller.ShowToolbar();
                Assert(controller.Toolbar.IsVisible, "tray toolbar action opens the controls");
                await Task.Delay(500);
                var overlay = controller.Overlays.First(o => o.Screen.Primary);
                var completed = new List<Mark>(); overlay.Surface.MarkCompleted += completed.Add;
                controller.Execute(ActionId.Marker); controller.Execute(ActionId.CycleColor);
                await Task.Delay(120); // Allow the initially transparent canvas to render its input surface.
                int forwarded = 0; overlay.WheelForward += (_, _) => forwarded++;
                int x = overlay.Screen.Bounds.Left + 240, y = overlay.Screen.Bounds.Top + 240;
                bool moved = SetCursorPos(x, y);
                Native.GetCursorPos(out var actual);
                Console.WriteLine($"Desktop pointer moved={moved}, position={actual.X},{actual.Y}, target={Native.WindowFromPoint(actual)}, overlay={overlay.Handle}, size={overlay.ActualWidth}×{overlay.ActualHeight}");
                Assert((Native.GetWindowLongPtr(overlay.Handle, -20).ToInt64() & 0x20) == 0, "drawing overlay receives input");
                mouse_event(0x2, 0, 0, 0, UIntPtr.Zero);
                for (int i = 0; i < 32; i++) { SetCursorPos(x + i * 5, y + (int)(Math.Sin(i / 4.0) * 35)); await Task.Delay(8); }
                mouse_event(0x4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(150);
                Assert(overlay.Surface.MarkCount == 1, "real mouse input creates a stroke");
                Assert(completed[0].Tool == Tool.Marker && completed[0].FlowColors && completed[0].StartingHue == 0, "marker and spectrum start with red through controller actions");
                controller.SelectTool(Tool.Rectangle);
                SetCursorPos(x, y + 100); mouse_event(0x2, 0, 0, 0, UIntPtr.Zero); await Task.Delay(30);
                SetCursorPos(x + 140, y + 170); await Task.Delay(30); mouse_event(0x4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(100);
                Assert(overlay.Surface.MarkCount == 2, "real mouse drag creates a shape");
                Assert(Math.Abs(completed[1].StartingHue - FlowColorRendering.EndHue(completed[0])) < 0.01, "next mark continues the preceding color");
                controller.Execute(ActionId.Undo); Assert(overlay.Surface.MarkCount == 1, "undo removes the last mark");
                keybd_event(0xA2, 0, 0, UIntPtr.Zero); await Task.Delay(60);
                keybd_event(0x59, 0, 0, UIntPtr.Zero); keybd_event(0x59, 0, 2, UIntPtr.Zero); await Task.Delay(120);
                Assert(overlay.Surface.MarkCount == 2, "Ctrl Y restores the undone shape while Ctrl hold is active");
                keybd_event(0x5A, 0, 0, UIntPtr.Zero); keybd_event(0x5A, 0, 2, UIntPtr.Zero); await Task.Delay(120);
                Assert(overlay.Surface.MarkCount == 1, "Ctrl Z undoes the restored shape");
                keybd_event(0xA2, 0, 2, UIntPtr.Zero); await Task.Delay(120);
                keybd_event(0xA2, 0, 0, UIntPtr.Zero); await Task.Delay(150);
                Assert((Native.GetWindowLongPtr(overlay.Handle, -20).ToInt64() & 0x20) != 0, "hold key enables click-through");
                keybd_event(0xA2, 0, 2, UIntPtr.Zero); await Task.Delay(150);
                Assert((Native.GetWindowLongPtr(overlay.Handle, -20).ToInt64() & 0x20) == 0, "release returns to drawing");
                SetCursorPos(x + 200, y + 200);
                mouse_event(0x800, 0, 0, 120, UIntPtr.Zero); await Task.Delay(200);
                Console.WriteLine($"Wheel forwarded={forwarded}, received={fixture.Wheels}");
                Assert(fixture.Wheels > 0, "wheel reaches the underlying fixture window");
                controller.Execute(ActionId.ZoomIn);
                Assert(overlay.CaptureImage != null && overlay.Surface.Zoom < overlay.Zoom, "first capture is ready before the zoom animation reaches its target");
                await Task.Delay(70);
                Assert(overlay.Surface.Zoom > 1 && overlay.Surface.Zoom < overlay.Zoom, "zoom renders an intermediate scale");
                await Task.Delay(600);
                Console.WriteLine($"Zoom={overlay.Zoom}, capture={overlay.CaptureImage != null}, status={controller.Toolbar.StatusText}");
                Assert(overlay.Zoom > 1 && overlay.CaptureImage != null, "live screen and ink zoom starts");
                var buffer = new byte[4];
                overlay.CaptureImage!.CopyPixels(new Int32Rect(x + 180, y + 220, 1, 1), buffer, 4, 0);
                Assert(Math.Abs(buffer[0] - 243) < 4 && Math.Abs(buffer[1] - 236) < 4, "capture excludes its own overlay without a black frame");
                fixture.BackColor = System.Drawing.Color.FromArgb(200, 216, 230); await Task.Delay(300);
                overlay.CaptureImage.CopyPixels(new Int32Rect(x + 180, y + 220, 1, 1), buffer, 4, 0);
                Assert(Math.Abs(buffer[0] - 230) < 4 && Math.Abs(buffer[1] - 216) < 4, "zoom refreshes live underlying content");
                controller.SelectTool(Tool.Pencil);
                SetCursorPos(x + 60, y + 220); mouse_event(0x2, 0, 0, 0, UIntPtr.Zero); await Task.Delay(30);
                SetCursorPos(x + 130, y + 245); await Task.Delay(30); mouse_event(0x4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(100);
                Assert(overlay.Surface.MarkCount == 2, "drawing also works in the zoom window");
                controller.Execute(ActionId.Redo);
                Assert(overlay.Surface.MarkCount == 2, "new drawing invalidates the previous redo branch");
                int beforeZoomWheel = fixture.Wheels;
                mouse_event(0x800, 0, 0, 120, UIntPtr.Zero); await Task.Delay(100);
                Assert(fixture.Wheels > beforeZoomWheel, "wheel also reaches underlying content while zoomed");
                keybd_event(0xA2, 0, 0, UIntPtr.Zero); await Task.Delay(120);
                Assert(overlay.Surface.Zoom == 1 && overlay.CaptureImage == null, "hold temporarily suspends magnification");
                keybd_event(0xA2, 0, 2, UIntPtr.Zero); await Task.Delay(150);
                Assert(overlay.Surface.Zoom > 1 && overlay.CaptureImage != null, "release restores magnification and ink");
                controller.Execute(ActionId.ZoomReset);
                await Task.Delay(650);
                Assert(overlay.Zoom == 1 && overlay.CaptureImage == null, "reset releases screen capture");
                if (controller.Drawing) controller.EnterScreenMode(); else controller.SelectTool(controller.Settings.Tool); Assert((Native.GetWindowLongPtr(overlay.Handle, -20).ToInt64() & 0x20) != 0, "normal mode passes through");
                controller.Settings.WheelZoomEnabled = true;
                controller.Settings.ZoomStepPercent = 50;
                int beforeShortcutWheel = fixture.Wheels;
                keybd_event(0xA2, 0, 0, UIntPtr.Zero); await Task.Delay(180);
                mouse_event(0x800, 0, 0, 120, UIntPtr.Zero); await Task.Delay(650);
                Assert(overlay.Zoom == 1.5 && overlay.Surface.Zoom == 1.5 && controller.Drawing, "Ctrl wheel starts zoom from normal mode using the selected 50 percent step despite the Ctrl hold key");
                Assert(fixture.Wheels == beforeShortcutWheel, "zoom wheel is consumed rather than forwarded");
                var beforePan = overlay.Surface.ViewMatrix;
                int marksBeforePan = overlay.Surface.MarkCount;
                Native.GetCursorPos(out var panStart);
                mouse_event(0x8, 0, 0, 0, UIntPtr.Zero); await Task.Delay(40);
                SetCursorPos(panStart.X + 35, panStart.Y + 25); await Task.Delay(80);
                mouse_event(0x10, 0, 0, 0, UIntPtr.Zero); await Task.Delay(80);
                Assert(overlay.Surface.ViewMatrix.OffsetX > beforePan.OffsetX && overlay.Surface.ViewMatrix.OffsetY > beforePan.OffsetY, "right drag moves the zoomed desktop and ink together");
                Assert(overlay.Surface.MarkCount == marksBeforePan, "panning does not create ink");
                overlay.PanBy(new Vector(100000, 100000));
                Assert(overlay.Surface.ViewMatrix.OffsetX == 0 && overlay.Surface.ViewMatrix.OffsetY == 0, "pan stops at desktop edges");
                mouse_event(0x800, 0, 0, unchecked((uint)-120), UIntPtr.Zero); await Task.Delay(650);
                Assert(overlay.Zoom == 1, "reverse wheel zooms out");
                controller.Settings.WheelZoomEnabled = false;
                await Task.Delay(180);
                mouse_event(0x800, 0, 0, 120, UIntPtr.Zero); await Task.Delay(180);
                Assert(overlay.Zoom == 1 && fixture.Wheels > beforeShortcutWheel, "disabled wheel zoom preserves underlying wheel input");
                keybd_event(0xA2, 0, 2, UIntPtr.Zero); await Task.Delay(180);
                controller.Settings.ZoomStepPercent = 25;
                if (controller.Drawing) controller.EnterScreenMode(); else controller.SelectTool(controller.Settings.Tool);
                // Performance changes must preserve all tools at high zoom and
                // retire in-flight captures safely during rapid mode changes.
                if (controller.Drawing) controller.EnterScreenMode(); else controller.SelectTool(controller.Settings.Tool);
                foreach (double scale in new[] { 3.0, 6.0 })
                {
                    overlay.ChangeZoom(scale); await Task.Delay(750);
                    foreach (Tool tool in new[] { Tool.Ballpoint, Tool.Pencil, Tool.Rectangle, Tool.Ellipse })
                    {
                        controller.SelectTool(tool);
                        int before = overlay.Surface.MarkCount;
                        SetCursorPos(x + 60, y + 80); mouse_event(0x2, 0, 0, 0, UIntPtr.Zero);
                        for (int i = 0; i < 30; i++) { SetCursorPos(x + 60 + i * 4, y + 80 + i * 2); await Task.Delay(8); }
                        mouse_event(0x4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(120);
                        Assert(overlay.Surface.MarkCount == before + 1, $"{tool} draws at {scale * 100}% without dropping the stroke");
                        controller.Execute(ActionId.Undo);
                        Assert(overlay.Surface.MarkCount == before, "undo restores the completed scene");
                    }
                }
                int beforeClear = overlay.Surface.MarkCount;
                controller.Execute(ActionId.Clear); await Task.Delay(80);
                Assert(overlay.Surface.MarkCount == 0, "clear removes retained completed drawings");
                controller.Execute(ActionId.Undo);
                Assert(overlay.Surface.MarkCount == beforeClear, "undo restores cleared drawings");
                controller.Execute(ActionId.Redo);
                Assert(overlay.Surface.MarkCount == 0, "redo reapplies clear");
                controller.Execute(ActionId.Undo);
                for (int i = 0; i < 8; i++) { if (controller.Drawing) controller.EnterScreenMode(); else controller.SelectTool(controller.Settings.Tool); await Task.Delay(15); }
                await Task.Delay(250);
                Assert(overlay.CaptureImage != null, "rapid mode changes discard old capture sessions and resume live capture");
                controller.Execute(ActionId.ZoomReset); await Task.Delay(750); if (controller.Drawing) controller.EnterScreenMode(); else controller.SelectTool(controller.Settings.Tool);
                controller.ChangeColor("#FF2563EB");
                Assert(!controller.Settings.CycleColors && !overlay.Surface.CycleColors && completed[0].FlowColors, "solid color disables cycling without recoloring old ink");
                await Task.Delay(100);
                Render(controller.Toolbar, "artifacts/toolbar.png");
                var settings = new SettingsWindow(controller) { Owner = controller.Toolbar }; settings.Show(); await Task.Delay(150);
                Render(settings, "artifacts/settings.png"); settings.Close();
                Console.WriteLine("PASS: desktop integration checks complete.");
                exitRequested = true;
                keybd_event(0x7A, 0, 0, UIntPtr.Zero); keybd_event(0x7A, 0, 2, UIntPtr.Zero);
                await Task.Delay(500);
                Assert(controller.Disposing, "F11 invokes app shutdown");
            }
            catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
            finally
            {
                mouse_event(0x14, 0, 0, 0, UIntPtr.Zero); keybd_event(0xA2, 0, 2, UIntPtr.Zero);
                controller?.Dispose(); fixture?.Close(); SetCursorPos(originalCursor.X, originalCursor.Y); app.Shutdown(Environment.ExitCode);
            }
        };
        app.Run();
        if (exitRequested) Assert(exitCompleted, "F11 closes the app and disposes its windows and hotkeys");
    }
    private static void Render(Window window, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); using var file = File.Create(path); png.Save(file);
    }
    internal static void PreviewSettings()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            var window = new SettingsWindow(new AppController(new Settings { PreserveSessionOnEscape = true }, false));
            try
            {
                window.Show(); await Task.Delay(120);
                Render(window, "artifacts/settings.png");
                var root = (System.Windows.Controls.DockPanel)window.Content;
                var scroll = root.Children.OfType<System.Windows.Controls.ScrollViewer>().Single();
                scroll.ScrollToEnd(); await Task.Delay(100);
                Assert(scroll.VerticalOffset > 0, "settings scrollbar reaches the lower controls");
                Render(window, "artifacts/settings-bottom.png");
            }
            catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
            finally { window.Close(); app.Shutdown(Environment.ExitCode); }
        };
        app.Run();
    }
}
