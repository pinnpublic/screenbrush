using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace ScreenBrush;

internal static class FlowColorRendering
{
    private const double DegreesPerDip = 0.075;
    internal static double EndHue(Mark mark)
    {
        if (IsShape(mark)) return (mark.StartingHue + (mark.End - mark.Start).Length * DegreesPerDip * mark.ColorCycleSpeed) % 360;
        double length = mark.ColorPathLength;
        return (mark.StartingHue + length * DegreesPerDip * mark.ColorCycleSpeed) % 360;
    }
    // Hue follows distance, not elapsed time or render count. Completed ink never
    // changes color when the live stroke grows, the screen repaints, or zoom changes.
    internal static DrawingGroup Tint(DrawingGroup ink, Mark mark)
    {
        var result = new DrawingGroup();
        if (ink.Bounds.IsEmpty) { result.Freeze(); return result; }
        var mask = new DrawingBrush(ink)
        {
            ViewboxUnits = BrushMappingMode.Absolute, Viewbox = ink.Bounds,
            ViewportUnits = BrushMappingMode.Absolute, Viewport = ink.Bounds, Stretch = Stretch.Fill
        };
        mask.Freeze();
        using (var dc = result.Open())
        {
            dc.PushOpacityMask(mask);
            if (IsShape(mark))
            {
                // One spatial gradient covers the whole shape. Walking a closed
                // perimeter assigns different hues to the same start/end corner.
                dc.DrawRectangle(ShapeBrush(mark), null, ink.Bounds);
            }
            else
            {
            // Provides coverage for dots and the extreme edges of fitted curves.
            dc.DrawRectangle(new SolidColorBrush(Hue(mark.StartingHue)), null, ink.Bounds);
            double distance = 0;
            foreach (var path in Paths(mark))
            {
                for (int i = 1; i < path.Count; i++)
                {
                    Point start = path[i - 1], end = path[i];
                    double length = (end - start).Length;
                    if (length < 0.001) continue;
                    // Split long packets so even a fast mouse movement traverses the
                    // color wheel smoothly instead of interpolating across one hue.
                    int pieces = Math.Max(1, (int)Math.Ceiling(length / 8));
                    for (int j = 0; j < pieces; j++)
                    {
                        Point a = start + (end - start) * ((double)j / pieces);
                        Point b = start + (end - start) * ((double)(j + 1) / pieces);
                        Vector direction = (end - start) / length;
                        double padding = mark.Width + 1;
                        // Extend the gradient through the round caps so adjacent
                        // packets overlap with matching colors instead of little bands.
                        var brush = new LinearGradientBrush(Hue(mark.StartingHue + (distance + length * j / pieces - padding) * DegreesPerDip * mark.ColorCycleSpeed),
                            Hue(mark.StartingHue + (distance + length * (j + 1) / pieces + padding) * DegreesPerDip * mark.ColorCycleSpeed), a - direction * padding, b + direction * padding)
                        { MappingMode = BrushMappingMode.Absolute };
                        brush.Freeze();
                        var pen = new Pen(brush, mark.Width * 2 + 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                        pen.Freeze(); dc.DrawLine(pen, a, b);
                    }
                    distance += length;
                }
            }
            }
            dc.Pop();
        }
        result.Freeze(); return result;
    }
    private static bool IsShape(Mark mark) => mark.Tool is Tool.Line or Tool.Arrow or Tool.Rectangle or Tool.Ellipse;
    internal static Brush ShapeBrush(Mark mark)
    {
        double length = (mark.End - mark.Start).Length;
        if (length < 0.001) return new SolidColorBrush(Hue(mark.StartingHue));
        var brush = new LinearGradientBrush { StartPoint = mark.Start, EndPoint = mark.End, MappingMode = BrushMappingMode.Absolute };
        int stops = Math.Max(1, (int)Math.Ceiling(length / 32));
        for (int i = 0; i <= stops; i++)
        {
            double t = (double)i / stops;
            brush.GradientStops.Add(new GradientStop(Hue(mark.StartingHue + length * t * DegreesPerDip * mark.ColorCycleSpeed), t));
        }
        brush.Freeze(); return brush;
    }
    private static IEnumerable<List<Point>> Paths(Mark mark)
    {
        if (mark.Tool is Tool.Ballpoint or Tool.Pencil or Tool.Marker)
        {
            if (mark.Points.Count == 0) yield break;
            yield return mark.ColorPath;
            yield break;
        }
        var rect = new Rect(mark.Start, mark.End);
        if (mark.Tool == Tool.Rectangle)
            yield return new() { rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft, rect.TopLeft };
        else if (mark.Tool == Tool.Ellipse)
        {
            var points = new List<Point>();
            int steps = Math.Clamp((int)Math.Ceiling((rect.Width + rect.Height) * Math.PI / 8), 32, 1024);
            for (int i = 0; i <= steps; i++)
            {
                double angle = i * Math.PI * 2 / steps;
                points.Add(new Point(rect.Left + rect.Width * (1 + Math.Cos(angle)) / 2, rect.Top + rect.Height * (1 + Math.Sin(angle)) / 2));
            }
            yield return points;
        }
        else
        {
            yield return new() { mark.Start, mark.End };
            if (mark.Tool == Tool.Arrow && (mark.End - mark.Start).Length > 0.1)
            {
                Vector direction = mark.End - mark.Start; direction.Normalize();
                Vector normal = new(-direction.Y, direction.X);
                double head = Math.Min((mark.End - mark.Start).Length * 0.45, Math.Max(12, mark.Width * 4));
                yield return new() { mark.End - direction * head + normal * head * 0.48, mark.End, mark.End - direction * head - normal * head * 0.48 };
            }
        }
    }
    private static Color Hue(double degrees)
    {
        double h = ((degrees % 360) + 360) % 360 / 60;
        const double c = 0.80, m = 0.15;
        double x = c * (1 - Math.Abs(h % 2 - 1));
        (double r, double g, double b) = h switch
        {
            < 1 => (c, x, 0.0), < 2 => (x, c, 0.0), < 3 => (0.0, c, x),
            < 4 => (0.0, x, c), < 5 => (x, 0.0, c), _ => (c, 0.0, x)
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
