using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenBrush;

internal static class WhiteboardChecks
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    internal static void Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try
            {
                using var controller = new AppController(new Settings { PreserveSessionOnEscape = true, ShowToolbarOnStartup = true }, false);
                controller.Start(); controller.SelectTool(Tool.Marker);
                var overlay = controller.Overlays[0];
                var surface = overlay.Surface;
                typeof(InkSurface).GetMethod("Begin", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(surface, new object?[] { new Point(300, 250), null, false });
                surface.Finish();
                Require(surface.MarkCount == 1, "fixture ink");
                controller.Execute(ActionId.BoardNotebook);
                Require(overlay.BoardVisible && surface.MarkCount == 1, "board selection preserves ink");
                overlay.ChangeZoom(3); await WaitForZoom(surface, 3);
                Require(Math.Abs(surface.Zoom - 3) < .01 && overlay.CaptureImage == null, "board zoom without desktop capture");
                RequireToolbarAbove(controller, overlay);
                overlay.PanBy(new Vector(50, 50));
                controller.Execute(ActionId.BoardChalk);
                Require(overlay.BoardVisible && surface.MarkCount == 1 && surface.Zoom > 2.9, "background switching preserves ink and zoom");
                if (controller.Drawing) controller.EnterScreenMode(); else controller.SelectTool(controller.Settings.Tool);
                Require(!overlay.BoardVisible, "screen operation hides board");
                controller.ToggleBoard(); await WaitForZoom(surface, 3);
                Require(overlay.BoardVisible && surface.Zoom > 2.9, "drawing restores board and zoom");
                RequireToolbarAbove(controller, overlay);
                surface.Undo(); Require(surface.MarkCount == 0, "undo on board");
                surface.Redo(); Require(surface.MarkCount == 1, "redo on board");
                controller.Execute(ActionId.BoardDots);
                controller.ChangeColor("#FF22C55E");
                controller.Toolbar.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)controller.Toolbar.ActualWidth, (int)controller.Toolbar.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(controller.Toolbar);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory("artifacts");
                using (var file = File.Create("artifacts/whiteboard-toolbar.png")) encoder.Save(file);
                controller.Execute(ActionId.ToggleBoard); await Task.Delay(200);
                Require(!overlay.BoardVisible && overlay.CaptureImage != null, "leaving board restores desktop zoom");
                RequireToolbarAbove(controller, overlay);
                controller.Execute(ActionId.ToggleBoard);
                Require(overlay.BoardVisible && overlay.CaptureImage == null, "returning to board releases capture");
                await Task.Delay(500);
                RequireToolbarAbove(controller, overlay);
                controller.Toolbar.Hide();
                if (controller.Drawing) controller.EnterScreenMode(); else controller.SelectTool(controller.Settings.Tool); if (controller.Drawing) controller.EnterScreenMode(); else controller.SelectTool(controller.Settings.Tool);
                await Task.Delay(500);
                Require(!controller.Toolbar.IsVisible, "canvas transitions do not reopen a deliberately hidden toolbar");
                Native.GetCursorPos(out var originalPointer);
                try
                {
                    Mark? completedMark = null;
                    surface.MarkCompleted += mark => completedMark = mark;
                    foreach (var brush in new[] { Tool.Marker, Tool.Ballpoint, Tool.Pencil })
                    foreach (var tool in new[] { Tool.Line, Tool.Arrow, Tool.Rectangle, Tool.Ellipse })
                    {
                        controller.SelectTool(brush);
                        controller.SelectTool(tool); surface.Clear();
                        var bounds = overlay.Screen.Bounds;
                        Require(SetCursorPos(bounds.Left + 300, bounds.Top + 300), "move real pointer");
                        mouse_event(2, 0, 0, 0, UIntPtr.Zero); await Task.Delay(60);
                        for (int i = 1; i <= 25; i++)
                        {
                            SetCursorPos(bounds.Left + 300 + i * 14, bounds.Top + 300 + i * 7);
                            await Task.Delay(12);
                        }
                        mouse_event(4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(120);
                        Require(surface.MarkCount == 1 && completedMark?.Tool == tool && completedMark.BrushTool == brush, $"real mouse draws {brush} + {tool} on magnified board");
                    }
                }
                finally { mouse_event(4, 0, 0, 0, UIntPtr.Zero); SetCursorPos(originalPointer.X, originalPointer.Y); }
                Console.WriteLine("PASS: all 12 brush/shape combinations with real mouse on 300% whiteboard");
                Console.WriteLine("PASS: whiteboard integration and injected mouse input");
                app.Shutdown();
            }
            catch (Exception e) { Console.Error.WriteLine(e); app.Shutdown(1); Environment.ExitCode = 1; }
        };
        app.Run();
    }
    private static void Require(bool value, string name) { if (!value) throw new Exception(name); }
    private static void RequireToolbarAbove(AppController controller, OverlayWindow overlay)
    {
        var toolbar = new WindowInteropHelper(controller.Toolbar).Handle;
        for (var window = GetWindow(overlay.Handle, 3); window != IntPtr.Zero; window = GetWindow(window, 3))
            if (window == toolbar && controller.Toolbar.IsVisible) return;
        throw new Exception("Visible toolbar must stay above the presented board/zoom window.");
    }
    private static async Task WaitForZoom(InkSurface surface, double zoom)
    {
        for (int i = 0; i < 100 && Math.Abs(surface.Zoom - zoom) > .001; i++) await Task.Delay(50);
    }
}
