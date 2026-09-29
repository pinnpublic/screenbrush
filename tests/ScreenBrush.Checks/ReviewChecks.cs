using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenBrush;
using Forms = System.Windows.Forms;

internal static class ReviewChecks
{
    private static readonly MethodInfo Begin = typeof(InkSurface).GetMethod("Begin", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo Continue = typeof(InkSurface).GetMethod("Continue", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo Pending = typeof(InkSurface).GetField("pending", BindingFlags.Instance | BindingFlags.NonPublic)!;
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    internal static void CheckInk(Action<bool, string> check)
    {
        foreach (var tool in new[] { Tool.Marker, Tool.Line, Tool.Arrow, Tool.Rectangle, Tool.Ellipse })
        {
            var surface = new InkSurface { Tool = tool, CycleColors = true, InkWidth = 9, InkColor = Colors.Blue };
            Begin.Invoke(surface, new object?[] { new Point(20, 50), null, false });
            Continue.Invoke(surface, new object?[] { new Point(120, 80), null });
            var mark = (Mark)Pending.GetValue(surface)!; var preview = mark.Drawing;
            Continue.Invoke(surface, new object?[] { new Point(120, 80), null });
            if (tool != Tool.Marker) check(ReferenceEquals(preview, mark.Drawing), "An unchanged shape endpoint keeps its preview cache: " + tool);
            // Marker filtering can advance on a repeated raw position; capture its final preview.
            preview = mark.Drawing; double hue = FlowColorRendering.EndHue(mark);
            surface.Finish();
            check(ReferenceEquals(preview, mark.Drawing) && FlowColorRendering.EndHue(mark) == hue, "Completion reuses identical marker/shape geometry and color: " + tool);
            surface.DiscardSession();
        }
        var prepare = typeof(Mark).GetMethod("PreparePressures", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (double width in new[] { .5, .9, 1, 9, 80 })
        foreach (bool finished in new[] { false, true })
        {
            var mark = new Mark { Tool = Tool.Pencil, Width = width, Finished = finished };
            for (int i = 0; i < 240; i++) mark.Points.Add(new InkPoint(new Point(i * .003, Math.Sin(i / 30.0) * .001), (float)(.2 + i / 400.0)));
            double[] actual = (double[])prepare.Invoke(mark, null)!;
            double length = 0;
            for (int i = 0; i < mark.Points.Count; i++)
            {
                if (i > 0) length += (mark.Points[i].Position - mark.Points[i - 1].Position).Length;
                double expected = mark.Points[i].Pressure * (.62 + .38 * Math.Min(1, length / Math.Max(width * 2, 4)));
                if (finished)
                {
                    double remaining = 0;
                    for (int j = i + 1; j < mark.Points.Count && remaining < width * 3; j++) remaining += (mark.Points[j].Position - mark.Points[j - 1].Position).Length;
                    expected *= .48 + .52 * Math.Min(1, remaining / Math.Max(width * 2, 3));
                }
                check(actual[i] == expected, "Cached segment lengths preserve exact dense-packet taper pressure.");
            }
        }
    }
    internal static void Render(string output)
    {
        var hashes = new Dictionary<string, string>(); var finishTimes = new List<object>();
        foreach (var tool in new[] { Tool.Marker, Tool.Ballpoint, Tool.Pencil, Tool.Line, Tool.Arrow, Tool.Rectangle, Tool.Ellipse })
        foreach (var brush in tool is Tool.Marker or Tool.Ballpoint or Tool.Pencil ? new[] { Tool.Marker } : new[] { Tool.Marker, Tool.Ballpoint, Tool.Pencil })
        foreach (bool cycle in new[] { false, true })
        {
            var surface = new InkSurface { Tool = tool, BrushTool = brush, CycleColors = cycle, ColorCycleSpeed = 2, InkColor = Colors.RoyalBlue, InkWidth = 9, InkOpacity = .9 };
            Begin.Invoke(surface, new object?[] { new Point(20, 50), .5f, false });
            for (int i = 1; i < 600; i++) Continue.Invoke(surface, new object?[] { new Point(20 + i * .8, 50 + 22 * Math.Sin(i / 20.0)), .2f + .7f * (float)Math.Abs(Math.Sin(i / 35.0)) });
            var mark = (Mark)Pending.GetValue(surface)!;
            _ = mark.Drawing; // Measure mouse-up work after the preview already exists.
            string key = $"{tool}-{brush}-{cycle}";
            hashes[key + "-live"] = Hash(mark.Drawing);
            var before = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
            surface.Finish(); watch.Stop();
            finishTimes.Add(new { tool, brush, cycle, finishMs = watch.Elapsed.TotalMilliseconds, allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before });
            hashes[key + "-finished"] = Hash(mark.Drawing);
            surface.DiscardSession();
        }
        File.WriteAllText(output, JsonSerializer.Serialize(hashes, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.ChangeExtension(output, ".timings.json"), JsonSerializer.Serialize(finishTimes, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Recorded {hashes.Count} live/completed pixel hashes and mouse-up timings: {output}");
    }
    private static string Hash(Drawing drawing)
    {
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) { dc.PushTransform(new ScaleTransform(3, 3)); dc.DrawDrawing(drawing); dc.Pop(); }
        var bitmap = new RenderTargetBitmap(1600, 260, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var bytes = new byte[1600 * 260 * 4]; bitmap.CopyPixels(bytes, 1600 * 4, 0);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
    internal static void RunZoom()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Native.GetCursorPos(out var pointer);
        app.Startup += async (_, _) =>
        {
            using var fixture = new Forms.Form { FormBorderStyle = Forms.FormBorderStyle.None, Bounds = Forms.Screen.PrimaryScreen!.Bounds, StartPosition = Forms.FormStartPosition.Manual, BackColor = System.Drawing.Color.SlateGray };
            using var controller = new AppController(new Settings { ShowToolbarOnStartup = true }, false);
            try
            {
                fixture.Show(); fixture.Activate(); controller.Start(); controller.ToggleDrawing();
                var bounds = fixture.Bounds; SetCursorPos(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                var overlay = controller.Overlays.Single(o => o.Screen.Primary);
                controller.Execute(ActionId.ToggleZoom); await Task.Delay(750);
                IntPtr toolbar = new WindowInteropHelper(controller.Toolbar).Handle;
                Require(GetWindowDisplayAffinity(toolbar, out var active) && active == 0x11, "Toolbar stays out of active desktop capture.");
                foreach (var action in new[] { ActionId.ToggleZoom, ActionId.ZoomReset })
                {
                    controller.Execute(action);
                    Require(overlay.Surface.Zoom > 1 && GetWindowDisplayAffinity(toolbar, out var during) && during == 0x11, "Toolbar must remain excluded throughout the zoom-out animation: " + action);
                    await Task.Delay(750);
                    Require(overlay.CaptureImage == null && GetWindowDisplayAffinity(toolbar, out var after) && after == 0, "Capture exclusion ends when the last desktop capture ends.");
                    controller.Execute(ActionId.ToggleZoom); await Task.Delay(750);
                }
                Console.WriteLine("PASS: toolbar capture exclusion across toggle/reset animations.");
            }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            finally { controller.Dispose(); fixture.Close(); SetCursorPos(pointer.X, pointer.Y); app.Shutdown(); }
        };
        app.Run();
    }
}
