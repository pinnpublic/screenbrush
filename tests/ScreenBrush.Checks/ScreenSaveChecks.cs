using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenBrush;

internal static class ScreenSaveChecks
{
    internal static void Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try
            {
                using var controller = new AppController(new Settings { WhiteboardEnabled = true, ShowToolbarOnStartup = true }, false);
                controller.Start(); controller.SelectBoard(BoardBackground.White);
                var overlay = controller.Overlays[0];
                var surface = overlay.Surface;
                controller.ChangeColor("#FFFF0000"); controller.ChangeOpacity(1);
                typeof(InkSurface).GetMethod("Begin", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(surface, new object?[] { new Point(300, 300), null, false });
                typeof(InkSurface).GetMethod("Continue", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(surface, new object?[] { new Point(450, 300), null });
                surface.Finish();
                await Task.Delay(200);
                foreach (double zoom in new[] { 1d, 3d })
                {
                    overlay.ChangeZoom(zoom);
                    for (int i = 0; i < 90 && Math.Abs(surface.Zoom - zoom) > .001; i++) await Task.Delay(16);
                    var snapshot = overlay.CaptureSnapshot();
                    if (snapshot.PixelWidth != overlay.Screen.Bounds.Width || snapshot.PixelHeight != overlay.Screen.Bounds.Height)
                        throw new Exception("Capture dimensions must match physical monitor pixels.");
                    var bytes = new byte[snapshot.PixelWidth * snapshot.PixelHeight * 4]; snapshot.CopyPixels(bytes, snapshot.PixelWidth * 4, 0);
                    bool red = false, white = false;
                    for (int i = 0; i < bytes.Length; i += 4) { red |= bytes[i + 2] > 200 && bytes[i + 1] < 80; white |= bytes[i] > 240 && bytes[i + 1] > 240 && bytes[i + 2] > 240; }
                    if (!white || (zoom == 1 && !red)) throw new Exception("PNG snapshot must contain whiteboard background and ink.");
                    Directory.CreateDirectory("artifacts");
                    ScreenSave.Write(snapshot, Path.GetFullPath($"artifacts/save-check-{zoom}.png"));
                }
                controller.Settings.CaptureDirectory = Path.GetFullPath("artifacts/save-integration");
                controller.Execute(ActionId.SaveScreen);
                var busy = typeof(AppController).GetField("savingScreen", BindingFlags.Instance | BindingFlags.NonPublic)!;
                for (int i = 0; i < 150 && (bool)busy.GetValue(controller)!; i++) await Task.Delay(20);
                if ((bool)busy.GetValue(controller)! || !Directory.Exists(controller.Settings.CaptureDirectory) || Directory.GetFiles(controller.Settings.CaptureDirectory, "screenbrush_*.png").Length == 0 || !controller.Toolbar.IsVisible)
                    throw new Exception("Save action must write a PNG and preserve toolbar visibility.");
                controller.ToggleBoard();
                controller.Execute(ActionId.ZoomReset);
                await Task.Delay(700);
                if (!overlay.SnapshotUsesDesktop) throw new Exception("Normal view must use desktop capture.");
                int before = Directory.GetFiles(controller.Settings.CaptureDirectory, "*.png").Length;
                controller.Execute(ActionId.SaveScreen);
                for (int i = 0; i < 150 && (bool)busy.GetValue(controller)!; i++) await Task.Delay(20);
                if ((bool)busy.GetValue(controller)! || Directory.GetFiles(controller.Settings.CaptureDirectory, "*.png").Length != before + 1 || !controller.Toolbar.IsVisible)
                    throw new Exception("Normal desktop capture must save and restore the toolbar: " + controller.Toolbar.StatusText);
                controller.Toolbar.Hide();
                controller.Execute(ActionId.SaveScreen);
                for (int i = 0; i < 150 && (bool)busy.GetValue(controller)!; i++) await Task.Delay(20);
                if (controller.Toolbar.IsVisible || Directory.GetFiles(controller.Settings.CaptureDirectory, "*.png").Length != before + 2)
                    throw new Exception("Capture must preserve an intentionally hidden toolbar.");
                Console.WriteLine("PASS: PNG board/zoom and desktop snapshots, physical dimensions, ink/background pixels, automatic save, visible/hidden toolbar restoration.");
            }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            finally { app.Shutdown(); }
        };
        app.Run();
    }
}

