using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using ScreenBrush;

internal static class PerformanceChecks
{
    internal static void RunCapture()
    {
        using var capture = new DesktopCapture(System.Windows.Forms.Screen.PrimaryScreen!.Bounds);
        Console.WriteLine(JsonSerializer.Serialize(Measure("capture-frame", () => capture.Read()), new JsonSerializerOptions { WriteIndented = true }));
    }
    internal static void Run()
    {
        var pencil = Stroke(Tool.Pencil, true);
        var pen = Stroke(Tool.Ballpoint, false);
        var shape = new Mark { Tool = Tool.Ellipse, Width = 12, Color = Colors.Blue, FlowColors = true, Start = new Point(10, 10), End = new Point(500, 300) };
        var output = new[]
        {
            Measure("pencil-600", () => { pencil.Invalidate(); _ = pencil.Drawing; }),
            Measure("ballpoint-600", () => { pen.Invalidate(); _ = pen.Drawing; }),
            Measure("ellipse-preview", () => { shape.Invalidate(); _ = shape.Drawing; }),
            Measure("distant-hit-1000", () => { for (int i = 0; i < 1000; i++) pencil.Hit(new Point(2000, 2000), 10); }),
            Measure("end-hue-100", () => { for (int i = 0; i < 100; i++) _ = FlowColorRendering.EndHue(pencil); })
        };
        Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static Mark Stroke(Tool tool, bool colors)
    {
        var mark = new Mark { Tool = tool, Color = Colors.Blue, Width = 10, FlowColors = colors, Finished = true };
        for (int i = 0; i < 600; i++) mark.Points.Add(new(new Point(i * 2, 50 + 30 * Math.Sin(i / 15.0)), (float)(0.3 + 0.5 * (1 + Math.Sin(i / 31.0)) / 2)));
        return mark;
    }
    private static object Measure(string name, Action action)
    {
        action(); action();
        var times = new double[12];
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < times.Length; i++) { var timer = Stopwatch.StartNew(); action(); times[i] = timer.Elapsed.TotalMilliseconds; }
        long allocated = (GC.GetAllocatedBytesForCurrentThread() - bytes) / times.Length;
        Array.Sort(times);
        return new { name, medianMs = Math.Round(times[times.Length / 2], 3), maxMs = Math.Round(times[^1], 3), allocatedBytes = allocated };
    }
}
