using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenBrush;

public readonly record struct InkPoint(Point Position, float Pressure);

// Sampling is independent of rendering: pen pressure is preserved, mouse pressure is
// synthesized from speed. A velocity-adaptive low-pass filter removes subpixel jitter.
public sealed class InkSampler
{
    private Point lastRaw, filtered;
    private double lastTime, pressure = 0.45, speed;
    private bool started;
    public InkPoint Add(Point raw, double seconds, float? penPressure)
    {
        if (!started)
        {
            started = true; lastRaw = filtered = raw; lastTime = seconds;
            pressure = penPressure ?? 0.28f;
            return new(raw, (float)Math.Clamp(pressure, 0.08, 1));
        }
        double dt = Math.Clamp(seconds - lastTime, 0.001, 0.05);
        double velocity = (raw - lastRaw).Length / dt;
        speed += (velocity - speed) * (1 - Math.Exp(-dt * 24));
        double cutoff = penPressure.HasValue ? 28 + speed * 0.15 : 12 + speed * 0.12;
        double alpha = 1 - Math.Exp(-2 * Math.PI * cutoff * dt);
        filtered += (raw - filtered) * alpha;
        double target = penPressure ?? (float)(0.36 + 0.48 * Math.Exp(-speed / 650));
        pressure += (target - pressure) * (1 - Math.Exp(-dt * 30));
        lastRaw = raw; lastTime = seconds;
        return new(filtered, (float)Math.Clamp(pressure, 0.08, 1));
    }
}

public sealed class Mark
{
    private static readonly Brush GraphiteGrain = CreateGraphiteGrain();
    private static Pen? eraserPen;
    private static Pen EraserPen(double radius)
    {
        if (eraserPen?.Thickness == radius * 2) return eraserPen;
        var pen = new Pen(Brushes.Black, radius * 2); pen.Freeze();
        return eraserPen = pen;
    }
    public Tool Tool { get; init; }
    public Tool BrushTool { get; init; } = Tool.Marker;
    public Color Color { get; init; }
    public double Width { get; init; }
    public double InkOpacity { get; init; } = 1;
    public bool FlowColors { get; init; }
    public double StartingHue { get; init; }
    public double ColorCycleSpeed { get; init; } = 0.5;
    public List<InkPoint> Points { get; } = new();
    public Point Start { get; set; }
    public Point End { get; set; }
    public bool Finished { get; set; }
    private DrawingGroup? cached;
    private Geometry? hitGeometry;
    private Geometry? shapeGeometry;
    private Pen? shapePen;
    private Rect? drawingBounds;
    private List<Point>? colorPath;
    private double colorPathLength;
    internal List<Point> ColorPath
    {
        get
        {
            if (colorPath != null) return colorPath;
            if (Points.Count == 0) return colorPath = new();
            var stroke = new Stroke(new StylusPointCollection(Points.Select(p => new StylusPoint(p.Position.X, p.Position.Y))));
            colorPath = stroke.GetBezierStylusPoints().Select(p => (Point)p).ToList();
            colorPathLength = 0;
            for (int i = 1; i < colorPath.Count; i++) colorPathLength += (colorPath[i] - colorPath[i - 1]).Length;
            return colorPath;
        }
    }
    internal double ColorPathLength { get { _ = ColorPath; return colorPathLength; } }
    public void Invalidate() { cached = null; hitGeometry = null; shapeGeometry = null; shapePen = null; drawingBounds = null; colorPath = null; colorPathLength = 0; }
    public DrawingGroup Drawing => cached ??= Build();
    public bool Hit(Point point, double radius)
    {
        _ = Drawing;
        var bounds = drawingBounds ??= Drawing.Bounds;
        if (bounds.IsEmpty) return false;
        bounds.Inflate(radius + 1, radius + 1);
        if (!bounds.Contains(point)) return false;
        if (hitGeometry == null && shapeGeometry != null) hitGeometry = shapeGeometry.GetWidenedPathGeometry(shapePen!);
        return hitGeometry?.FillContains(point) == true || hitGeometry?.StrokeContains(EraserPen(radius), point) == true;
    }
    private DrawingGroup Build()
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            if (Tool == Tool.Marker) DrawMarker(dc);
            else if (Tool is Tool.Ballpoint or Tool.Pencil) DrawInk(dc);
            else DrawShape(dc);
        }
        group.Freeze();
        var drawing = FlowColors && Tool is Tool.Marker or Tool.Ballpoint or Tool.Pencil
            ? FlowColorRendering.Tint(group, this) : group;
        if (InkOpacity == 1) return drawing;
        var translucent = new DrawingGroup { Opacity = InkOpacity };
        translucent.Children.Add(drawing); translucent.Freeze();
        return translucent;
    }
    private void DrawMarker(DrawingContext dc)
    {
        if (Points.Count == 0) return;
        var points = new StylusPointCollection(Points.Select(p => new StylusPoint(p.Position.X, p.Position.Y, 0.5f)));
        var stroke = new Stroke(points, new DrawingAttributes
        {
            Color = Color, Width = Width, Height = Width,
            FitToCurve = true, IgnorePressure = true, StylusTip = StylusTip.Ellipse
        });
        hitGeometry = stroke.GetGeometry();
        stroke.Draw(dc);
    }
    private double[] PreparePressures()
    {
        var pressures = new double[Points.Count];
        double length = 0;
        for (int i = 0; i < Points.Count; i++)
        {
            var sample = Points[i];
            if (i > 0) length += (sample.Position - Points[i - 1].Position).Length;
            double p = sample.Pressure;
            // Arc length, rather than event count, keeps the nib taper independent of
            // mouse / tablet sampling frequency.
            p *= 0.62 + 0.38 * Math.Min(1, length / Math.Max(Width * 2, 4));
            if (Finished && Points.Count > 2)
            {
                double remaining = 0;
                for (int j = i + 1; j < Points.Count && remaining < Width * 3; j++) remaining += (Points[j].Position - Points[j - 1].Position).Length;
                p *= 0.48 + 0.52 * Math.Min(1, remaining / Math.Max(Width * 2, 3));
            }
            pressures[i] = p;
        }
        return pressures;
    }
    private Vector[] PrepareNormals()
    {
        var normals = new Vector[Points.Count];
        for (int i = 0; i < Points.Count; i++)
        {
            Vector tangent = Points[Math.Min(i + 1, Points.Count - 1)].Position - Points[Math.Max(0, i - 1)].Position;
            if (tangent.Length > 0) tangent.Normalize(); else tangent = new Vector(1, 0);
            normals[i] = new Vector(-tangent.Y, tangent.X);
        }
        return normals;
    }
    private Stroke StrokeFor(double[] pressures, double widthFactor, double normalOffset = 0, int strand = -1, Vector[]? normals = null)
    {
        var points = new StylusPointCollection();
        for (int i = 0; i < Points.Count; i++)
        {
            double p = pressures[i];
            Point position = Points[i].Position;
            if (strand >= 0)
            {
                Vector normal = normals![i];
                double grain = Noise(i, strand);
                position += normal * (normalOffset + (grain - 0.5) * Width * 0.09) * p;
                p *= 0.40 + 0.60 * grain;
            }
            points.Add(new StylusPoint(position.X, position.Y, (float)Math.Clamp(p, 0.03, 1)));
        }
        return new Stroke(points, new DrawingAttributes
        {
            Color = Color, Width = Math.Max(0.1, Width * widthFactor), Height = Math.Max(0.1, Width * widthFactor),
            FitToCurve = true, IgnorePressure = false, StylusTip = StylusTip.Ellipse
        });
    }
    private void DrawInk(DrawingContext dc)
    {
        if (Points.Count == 0) return;
        var pressures = PreparePressures();
        var body = StrokeFor(pressures, 1);
        hitGeometry = body.GetGeometry();
        if (Tool == Tool.Ballpoint)
        {
            // Smooth pressure silhouette and a restrained ink core, without a glow.
            dc.PushOpacity(0.90); body.Draw(dc); dc.Pop();
            dc.PushOpacity(0.16); StrokeFor(pressures, 0.48).Draw(dc); dc.Pop();
        }
        else
        {
            // Paper-space micrograin stays fixed as the stroke grows and when zoomed.
            // The pressure silhouette is shared with the live pen renderer.
            dc.PushOpacityMask(GraphiteGrain);
            dc.PushOpacity(0.88); body.Draw(dc); dc.Pop();
            int strands = Math.Clamp((int)(Width * 1.5), 5, 14);
            var normals = PrepareNormals();
            for (int s = 0; s < strands; s++)
            {
                double offset = ((double)s / (strands - 1) - 0.5) * Width * 0.92;
                dc.PushOpacity(0.08 + 0.13 * Noise(s, 37));
                StrokeFor(pressures, Math.Max(0.055, 0.4 / Width), offset, s, normals).Draw(dc);
                dc.Pop();
            }
            dc.Pop();
        }
    }
    private static Brush CreateGraphiteGrain()
    {
        const int size = 128;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            double fine = Noise(x + y * size, 81);
            double paper = Noise(x / 3 + (y / 3) * 43, 127);
            double density = fine * 0.72 + paper * 0.28;
            byte alpha = (byte)(255 * Math.Clamp((density - 0.24) * 1.85, 0.04, 0.96));
            int index = (y * size + x) * 4;
            pixels[index] = pixels[index + 1] = pixels[index + 2] = pixels[index + 3] = alpha;
        }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4); bitmap.Freeze();
        var brush = new ImageBrush(bitmap) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 48, 48), Stretch = Stretch.Fill };
        brush.Freeze(); return brush;
    }
    private static double Noise(int index, int seed)
    {
        uint n = unchecked((uint)(index * 374761393 + seed * 668265263 + 17));
        n = (n ^ (n >> 13)) * 1274126177;
        return (n ^ (n >> 16)) / (double)uint.MaxValue;
    }
    private void DrawShape(DrawingContext dc)
    {
        Brush brush = FlowColors ? FlowColorRendering.ShapeBrush(this) : new SolidColorBrush(Color); brush.Freeze();
        var pen = new Pen(brush, Width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        var rect = new Rect(Start, End);
        Geometry geometry;
        if (Tool == Tool.Rectangle) geometry = new RectangleGeometry(rect);
        else if (Tool == Tool.Ellipse) geometry = new EllipseGeometry(rect);
        else
        {
            var path = new StreamGeometry();
            using var context = path.Open();
            context.BeginFigure(Start, false, false); context.LineTo(End, true, false);
            if (Tool == Tool.Arrow && (End - Start).Length > 0.1)
            {
                Vector direction = End - Start; direction.Normalize();
                Vector normal = new(-direction.Y, direction.X);
                double head = Math.Min((End - Start).Length * 0.45, Math.Max(12, Width * 4));
                context.BeginFigure(End - direction * head + normal * head * 0.48, false, false);
                context.LineTo(End, true, false);
                context.LineTo(End - direction * head - normal * head * 0.48, true, false);
            }
            geometry = path;
        }
        geometry.Freeze();
        shapeGeometry = geometry; shapePen = pen;
        if (BrushTool == Tool.Marker) dc.DrawGeometry(null, pen, geometry);
        else if (BrushTool == Tool.Ballpoint)
        {
            dc.PushOpacity(0.90); dc.DrawGeometry(null, pen, geometry); dc.Pop();
            var core = new Pen(brush, Width * 0.48) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            core.Freeze();
            dc.PushOpacity(0.16); dc.DrawGeometry(null, core, geometry); dc.Pop();
        }
        else
        {
            // Keep exact shape geometry while applying the same paper-space grain as pencil ink.
            dc.PushOpacityMask(GraphiteGrain);
            dc.PushOpacity(0.88); dc.DrawGeometry(null, pen, geometry); dc.Pop();
            var grainCore = new Pen(brush, Math.Max(0.1, Width * 0.35)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            grainCore.Freeze();
            dc.PushOpacity(0.16); dc.DrawGeometry(null, grainCore, geometry); dc.Pop();
            dc.Pop();
        }
    }
}
