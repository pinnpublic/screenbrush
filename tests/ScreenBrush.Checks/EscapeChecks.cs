using System;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ScreenBrush;

internal static class EscapeChecks
{
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    private static void Escape() { keybd_event(0x1B, 0, 0, UIntPtr.Zero); keybd_event(0x1B, 0, 2, UIntPtr.Zero); }
    private static void Require(bool result, string message) { if (!result) throw new Exception(message); }
    internal static void Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try
            {
                using var controller = new AppController(new Settings { PreserveSessionOnEscape = true, ShowToolbarOnStartup = true }, false);
                controller.Start();
                controller.SelectTool(Tool.Marker);
                var surface = controller.Overlays[0].Surface;
                var begin = typeof(InkSurface).GetMethod("Begin", BindingFlags.Instance | BindingFlags.NonPublic)!;
                begin.Invoke(surface, new object?[] { new Point(20, 20), null, false }); surface.Finish();
                begin.Invoke(surface, new object?[] { new Point(40, 40), null, false }); surface.Finish();
                surface.Undo();
                await Task.Delay(200);
                Escape(); await Task.Delay(200);
                Require(!controller.Drawing && controller.Toolbar.IsVisible && !controller.Disposing, "Esc leaves drawing without hiding or quitting toolbar.");
                Require(surface.MarkCount == 1 && controller.Overlays.All(o => o.Surface.Visibility == Visibility.Hidden), "Esc hides ink on all monitors without removing marks.");
                var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(controller.Overlays[0]);
                var pixels = new byte[64 * 64 * 4]; bitmap.CopyPixels(pixels, 64 * 4, 0);
                Require(Enumerable.Range(0, 64 * 64).All(i => pixels[i * 4 + 3] == 0), "Screen-operation overlay renders no visible ink.");
                controller.Toolbar.Hide();
                controller.SelectTool(Tool.Marker);
                Require(controller.Drawing && surface.Visibility == Visibility.Visible && surface.MarkCount == 1, "Selecting a tool reveals retained drawing.");
                surface.Redo(); Require(surface.MarkCount == 2, "Redo survives Esc.");
                surface.Undo(); Require(surface.MarkCount == 1, "Undo survives Esc.");
                Escape(); await Task.Delay(200);
                Require(!controller.Drawing && !controller.Toolbar.IsVisible, "Esc works with toolbar hidden.");
                controller.ShowToolbar();
                controller.SelectBoard(BoardBackground.White);
                controller.Overlays[0].ChangeZoom(2);
                await Task.Delay(700);
                Escape(); await Task.Delay(200);
                Require(!controller.Drawing && !controller.Overlays[0].BoardVisible && !controller.Settings.WhiteboardEnabled, "Esc turns off board even when preserving ink.");
                Require(surface.Zoom == 1 && controller.Overlays[0].Zoom == 2 && controller.Overlays[0].CaptureImage == null && surface.Visibility == Visibility.Hidden, "Esc shows original desktop without ink or capture while retaining target zoom.");
                controller.SelectTool(Tool.Pencil);
                await Task.Delay(700);
                Require(!controller.Overlays[0].BoardVisible && !controller.Settings.WhiteboardEnabled && surface.Zoom == 2 && surface.MarkCount == 1 && surface.Visibility == Visibility.Visible, "Selecting tool restores ink and zoom, never whiteboard.");
                controller.ToggleBoard();
                Require(controller.Overlays[0].BoardVisible, "Explicit board command still enables the board.");
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                timer.Tick += (_, _) => { timer.Stop(); Escape(); };
                timer.Start();
                controller.OpenSettings();
                Require(controller.Drawing && controller.Toolbar.IsVisible, "Esc closes settings without leaving drawing or hiding toolbar.");
                controller.Settings.PreserveSessionOnEscape = false;
                // Settings-window Esc must not clear content even with preservation disabled.
                timer.Start(); controller.OpenSettings();
                Require(surface.MarkCount == 1 && controller.Drawing, "Settings Esc never discards the session.");
                Escape(); await Task.Delay(200);
                Require(controller.Overlays.All(o => o.Surface.MarkCount == 0 && o.Zoom == 1 && !o.BoardVisible && o.Surface.Visibility == Visibility.Hidden), "Unchecked Esc clears every monitor and resets its display.");
                Require(!controller.Settings.WhiteboardEnabled && !controller.Drawing, "Unchecked Esc disables whiteboard and drawing.");
                surface.Undo(); surface.Redo();
                Require(surface.MarkCount == 0, "Discarded session cannot return via Undo or Redo.");
                controller.SelectTool(Tool.Marker); await Task.Delay(200);
                Require(surface.MarkCount == 0 && surface.Zoom == 1 && !controller.Overlays[0].BoardVisible, "Unchecked Esc resumes with an empty normal canvas.");
                Console.WriteLine("PASS: injected Esc hides ink/board/zoom without erasing undo/redo; tool selection restores content; settings Esc stays local.");
            }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            finally { app.Shutdown(); }
        };
        app.Run();
    }
}
