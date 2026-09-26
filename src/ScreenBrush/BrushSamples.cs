using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenBrush;

public static class BrushSamples
{
    public static void Render(string path)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(248, 247, 243)), null, new Rect(0, 0, 1440, 960));
            Text(dc, "ScreenBrush / Ink study", 60, 40, 28);
            Text(dc, "Pressure-sensitive curves · 2× rendering · deterministic graphite texture", 60, 85, 16);
            int row = 0;
            foreach (Tool tool in new[] { Tool.Ballpoint, Tool.Pencil })
            foreach (double width in new[] { 2.0, 5.0, 12.0 })
            {
                double y = 175 + row * 115;
                Text(dc, $"{tool} / {width:0} px", 60, y, 16);
                var mark = new Mark { Tool = tool, Color = Color.FromRgb(37, 67, 108), Width = width, Finished = true };
                for (int i = 0; i < 360; i++)
                {
                    double t = i / 359.0;
                    mark.Points.Add(new InkPoint(new Point(270 + t * 700, y + 12 + 28 * Math.Sin(t * Math.PI * 8)), (float)(0.22 + 0.74 * Math.Sin(Math.PI * t))));
                }
                dc.DrawDrawing(mark.Drawing);
                var loop = new Mark { Tool = tool, Color = Color.FromRgb(42, 45, 51), Width = width, Finished = true };
                for (int i = 0; i < 240; i++)
                {
                    double t = i / 239.0;
                    loop.Points.Add(new InkPoint(new Point(1090 + t * 210 + 27 * Math.Sin(t * 8 * Math.PI), y + 12 + 30 * Math.Cos(t * 8 * Math.PI)), (float)(0.55 + 0.3 * Math.Sin(t * 8 * Math.PI))));
                }
                dc.DrawDrawing(loop.Drawing); row++;
            }
            Text(dc, "Live drawing uses the same renderer. Actual tablet feel must be checked with a pressure-sensitive pen.", 60, 915, 16);
        }
        var bitmap = new RenderTargetBitmap(2880, 1920, 192, 192, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path); encoder.Save(file);
        RenderNewTools(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "new-tools.png"));
    }
    private static void RenderNewTools(string path)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 0, 1440, 840));
            Text(dc, "Marker + continuous color spectrum", 50, 30, 28);
            string[] labels = { "Marker / pressure ignored", "Marker / red → orange → yellow → green → blue → violet", "Ballpoint / color spectrum", "Pencil / color spectrum" };
            Tool[] tools = { Tool.Marker, Tool.Marker, Tool.Ballpoint, Tool.Pencil };
            for (int row = 0; row < tools.Length; row++)
            {
                double y = 140 + row * 125;
                Text(dc, labels[row], 50, y - 45, 17);
                var mark = new Mark { Tool = tools[row], Width = 10, Color = Color.FromRgb(37, 67, 108), FlowColors = row > 0, Finished = true };
                for (int i = 0; i <= 600; i++)
                    mark.Points.Add(new(new Point(60 + i * 2.15, y + 23 * Math.Sin(i / 25.0)), (float)(0.1 + 0.9 * (1 + Math.Sin(i / 40.0)) / 2)));
                dc.DrawDrawing(mark.Drawing);
            }
            Text(dc, "Closed shapes / continuous color at corners and joins", 50, 630, 19);
            Tool[] shapes = { Tool.Rectangle, Tool.Ellipse, Tool.Arrow };
            for (int i = 0; i < shapes.Length; i++)
            {
                var shape = new Mark { Tool = shapes[i], Width = 12, Color = Colors.Blue, FlowColors = true, StartingHue = 160,
                    Start = new Point(70 + i * 450, 690), End = new Point(420 + i * 450, 780) };
                dc.DrawDrawing(shape.Drawing);
            }
        }
        var bitmap = new RenderTargetBitmap(2880, 1680, 192, 192, PixelFormats.Pbgra32); bitmap.Render(visual);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); png.Save(file);
    }
    private static void Text(DrawingContext dc, string text, double x, double y, double size) =>
        dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, Brushes.DimGray, 1), new Point(x, y));
}
