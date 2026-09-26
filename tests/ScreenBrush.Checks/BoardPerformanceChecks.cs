using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenBrush;

internal static class BoardPerformanceChecks
{
    internal static void Run(string output)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            OverlayWindow? overlay = null;
            try
            {
                overlay = new OverlayWindow(System.Windows.Forms.Screen.PrimaryScreen!); overlay.Show(); overlay.SetEnabled(true);
                var results = new List<object>();
                var board4k = Whiteboard.Load(new Settings { BoardBackground = BoardBackground.Chalkboard });
                var oldImage = new BitmapImage(new Uri(Path.GetFullPath("src/ScreenBrush/Assets/Whiteboard/originals/Chalkboard.png")));
                oldImage.Freeze(); var oldBrush = new ImageBrush(oldImage) { Stretch = Stretch.UniformToFill }; oldBrush.Freeze();
                foreach (var scenario in new[] { ("desktop", false, (Brush)Brushes.White, 1d), ("white", true, (Brush)Brushes.White, 1d),
                    ("old-image", true, (Brush)oldBrush, 1d), ("4k", true, board4k, 1d), ("4k-300", true, board4k, 3d) })
                {
                    overlay.Surface.Clear(); overlay.SetBoard(scenario.Item2, scenario.Item3); overlay.ChangeZoom(scenario.Item4);
                    await Task.Delay(1000);
                    var surface = overlay.Surface; surface.Tool = Tool.Rectangle; surface.CycleColors = true; surface.InkWidth = 9; surface.InkOpacity = .9;
                    var begin = typeof(InkSurface).GetMethod("Begin", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var move = typeof(InkSurface).GetMethod("Continue", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    begin.Invoke(surface, new object?[] { new Point(150, 150), null, false });
                    var gaps = new List<double>(); var watch = Stopwatch.StartNew(); double last = 0;
                    using var process = Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime;
                    EventHandler tick = (_, _) =>
                    {
                        double now = watch.Elapsed.TotalMilliseconds;
                        if (last > 0) gaps.Add(now - last); last = now;
                        move.Invoke(surface, new object?[] { new Point(1400 + 500 * Math.Sin(now / 350), 900 + 350 * Math.Cos(now / 450)), null });
                    };
                    CompositionTarget.Rendering += tick;
                    try { await Task.Delay(3000); } finally { CompositionTarget.Rendering -= tick; }
                    surface.Finish(); gaps.Sort(); process.Refresh();
                    var row = new { name = scenario.Item1, frames = gaps.Count, medianMs = gaps.Count > 0 ? gaps[gaps.Count / 2] : 0,
                        p95Ms = gaps.Count > 0 ? gaps[(int)((gaps.Count - 1) * .95)] : 0,
                        cpuPercent = (process.TotalProcessorTime - cpu).TotalMilliseconds / watch.Elapsed.TotalMilliseconds * 100 / Environment.ProcessorCount,
                        privateMB = process.PrivateMemorySize64 / 1048576.0 };
                    results.Add(row); Console.WriteLine(JsonSerializer.Serialize(row));
                }
                Directory.CreateDirectory("artifacts"); File.WriteAllText(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
            finally { overlay?.Close(); app.Shutdown(Environment.ExitCode); }
        };
        app.Run();
    }
}
