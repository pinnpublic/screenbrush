using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ScreenBrush;

public sealed class InkSurface : FrameworkElement
{
    private readonly List<Mark> marks = new();
    private readonly Stack<List<Mark>> history = new();
    private readonly Stack<List<Mark>> redoHistory = new();
    private readonly DrawingGroup completedScene = new();
    private readonly Dictionary<Mark, DrawingGroup> fading = new();
    public bool AutoFadeEnabled { get; set; }
    public double AutoFadeHoldSeconds { get; set; } = 3;
    public double AutoFadeDurationSeconds { get; set; } = 1.5;
    private bool sceneDirty;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private InkSampler sampler = new();
    private Mark? pending;
    private bool drawing, stylus, erasing;
    private Point previousErase;
    public Tool Tool { get; set; }
    public Tool BrushTool { get; set; } = Tool.Marker;
    public Color InkColor { get; set; }
    public bool CycleColors { get; set; }
    public double ColorCycleSpeed { get; set; } = 0.5;
    internal Func<double>? ColorPhaseForNewMark { get; set; }
    internal event Action<Mark>? MarkCompleted;
    public double InkWidth { get; set; } = 3;
    public double InkOpacity { get; set; } = 1;
    public double Zoom { get; private set; } = 1;
    private Point origin;
    internal MatrixTransform ViewTransform { get; } = new();
    public bool DrawingEnabled { get; set; } = true;
    public int MarkCount => marks.Count;
    public InkSurface()
    {
        Focusable = false;
        Cursor = Cursors.Pen;
        Stylus.SetIsPressAndHoldEnabled(this, false);
        Stylus.SetIsFlicksEnabled(this, false);
        Stylus.SetIsTapFeedbackEnabled(this, false);
        Stylus.SetIsTouchFeedbackEnabled(this, false);
        LostMouseCapture += (_, _) => { if (!stylus) Finish(); };
        LostStylusCapture += (_, _) => { if (stylus) Finish(); };
    }
    public void SetView(double zoom, Point anchor) { Finish(); SetAnimatedView(zoom, anchor); }
    internal void SetAnimatedView(double zoom, Point anchor) { Zoom = zoom; origin = anchor; ViewTransform.Matrix = ViewMatrix; }
    public Point ToDocument(Point point) => new(origin.X + (point.X - origin.X) / Zoom, origin.Y + (point.Y - origin.Y) / Zoom);
    public Matrix ViewMatrix => new(Zoom, 0, 0, Zoom, origin.X * (1 - Zoom), origin.Y * (1 - Zoom));
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        dc.PushTransform(ViewTransform);
        if (sceneDirty)
        {
            using var scene = completedScene.Open();
            foreach (var mark in marks) scene.DrawDrawing(DisplayDrawing(mark));
            sceneDirty = false;
        }
        dc.DrawDrawing(completedScene);
        if (pending != null) dc.DrawDrawing(pending.Drawing);
        dc.Pop(); dc.Pop();
    }
    private void Begin(Point point, float? pressure, bool inverted = false)
    {
        if (!DrawingEnabled) return;
        Finish(); drawing = true; erasing = Tool == Tool.Eraser || inverted;
        SaveUndoState(trim: !erasing);
        if (!erasing) redoHistory.Clear();
        point = ToDocument(point); previousErase = point;
        if (erasing) { Erase(point); return; }
        sampler = new InkSampler();
        pending = new Mark
        {
            Tool = Tool, BrushTool = BrushTool, Color = InkColor, Width = InkWidth, InkOpacity = InkOpacity, Start = point, End = point,
            FlowColors = CycleColors, StartingHue = CycleColors ? ColorPhaseForNewMark?.Invoke() ?? 0 : 0,
            ColorCycleSpeed = ColorCycleSpeed
        };
        pending.Points.Add(sampler.Add(point, clock.Elapsed.TotalSeconds, pressure));
        InvalidateVisual();
    }
    private void Continue(Point point, float? pressure)
    {
        if (!drawing) return;
        point = ToDocument(point);
        if (erasing)
        {
            Vector distance = point - previousErase;
            int steps = Math.Max(1, (int)(distance.Length / (5 / Zoom)));
            for (int i = 1; i <= steps; i++) Erase(previousErase + distance * ((double)i / steps));
            previousErase = point; return;
        }
        if (pending == null) return;
        if (pending.Tool is Tool.Ballpoint or Tool.Pencil or Tool.Marker)
        {
            if ((pending.Points[^1].Position - point).Length < 0.12 / Zoom) return;
            pending.Points.Add(sampler.Add(point, clock.Elapsed.TotalSeconds, pressure));
        }
        else
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                Vector delta = point - pending.Start;
                if (Tool is Tool.Rectangle or Tool.Ellipse)
                {
                    double edge = Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y));
                    point = pending.Start + new Vector(Math.Sign(delta.X) * edge, Math.Sign(delta.Y) * edge);
                }
                else
                {
                    double angle = Math.Round(Math.Atan2(delta.Y, delta.X) / (Math.PI / 4)) * (Math.PI / 4);
                    point = pending.Start + new Vector(Math.Cos(angle), Math.Sin(angle)) * delta.Length;
                }
            }
            pending.End = point;
        }
        pending.Invalidate(); InvalidateVisual();
    }
    private void Erase(Point point)
    {
        if (marks.RemoveAll(mark => mark.Hit(point, 10 / Zoom)) == 0) return;
        redoHistory.Clear();
        sceneDirty = true; InvalidateVisual();
    }
    public void Finish()
    {
        if (!drawing) return;
        drawing = false;
        if (erasing)
        {
            if (history.TryPeek(out var previous) && previous.SequenceEqual(marks)) history.Pop();
            else TrimHistory();
        }
        if (pending != null)
        {
            pending.Finished = true; pending.Invalidate(); marks.Add(pending);
            if (AutoFadeEnabled) StartFade(pending);
            MarkCompleted?.Invoke(pending); pending = null;
            if (!sceneDirty) completedScene.Children.Add(DisplayDrawing(marks[^1]));
        }
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (IsStylusCaptured) ReleaseStylusCapture();
        stylus = false;
        InvalidateVisual();
    }
    private DrawingGroup DisplayDrawing(Mark mark) => fading.TryGetValue(mark, out var visual) ? visual : mark.Drawing;
    private void StartFade(Mark mark)
    {
        // Animate only an opacity wrapper; keep the existing stroke geometry cached.
        var visual = new DrawingGroup();
        visual.Children.Add(mark.Drawing);
        fading.Add(mark, visual);
        var animation = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(AutoFadeDurationSeconds))
        { BeginTime = TimeSpan.FromSeconds(AutoFadeHoldSeconds), FillBehavior = FillBehavior.HoldEnd };
        animation.Completed += (_, _) =>
        {
            if (!fading.Remove(mark)) return;
            marks.Remove(mark);
            foreach (var state in history) state.Remove(mark);
            foreach (var state in redoHistory) state.Remove(mark);
            completedScene.Children.Remove(visual);
            visual.BeginAnimation(DrawingGroup.OpacityProperty, null);
            InvalidateVisual();
        };
        visual.BeginAnimation(DrawingGroup.OpacityProperty, animation);
    }
    private void SaveUndoState(bool trim = true)
    {
        history.Push(new List<Mark>(marks));
        if (trim) TrimHistory();
    }
    private void TrimHistory()
    {
        if (history.Count > 100) { var keep = history.Take(100).Reverse().ToArray(); history.Clear(); foreach (var h in keep) history.Push(h); }
    }
    public void Undo() { Finish(); if (history.TryPop(out var state)) { redoHistory.Push(new List<Mark>(marks)); marks.Clear(); marks.AddRange(state); sceneDirty = true; InvalidateVisual(); } }
    public void Redo() { Finish(); if (redoHistory.TryPop(out var state)) { SaveUndoState(); marks.Clear(); marks.AddRange(state); sceneDirty = true; InvalidateVisual(); } }
    internal void DiscardSession()
    {
        Finish();
        foreach (var visual in fading.Values) visual.BeginAnimation(DrawingGroup.OpacityProperty, null);
        fading.Clear();
        marks.Clear(); history.Clear(); redoHistory.Clear();
        completedScene.Children.Clear(); sceneDirty = false;
        InvalidateVisual();
    }
    public void Clear() { Finish(); if (marks.Count == 0) return; SaveUndoState(); redoHistory.Clear(); marks.Clear(); sceneDirty = true; InvalidateVisual(); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null || !DrawingEnabled) return;
        Begin(e.GetPosition(this), null); CaptureMouse(); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e) { if (!stylus && e.StylusDevice == null && e.LeftButton == MouseButtonState.Pressed) Continue(e.GetPosition(this), null); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { if (!stylus && e.StylusDevice == null) { Continue(e.GetPosition(this), null); Finish(); e.Handled = true; } }
    protected override void OnStylusDown(StylusDownEventArgs e)
    {
        if (!DrawingEnabled || e.StylusDevice.TabletDevice.Type == TabletDeviceType.Touch) return;
        var points = e.GetStylusPoints(this);
        Begin((Point)points[0], points[0].PressureFactor, e.Inverted);
        stylus = true; CaptureStylus();
        foreach (var p in points.Skip(1)) Continue((Point)p, p.PressureFactor);
        e.Handled = true;
    }
    protected override void OnStylusMove(StylusEventArgs e)
    {
        if (!stylus) return;
        foreach (var p in e.GetStylusPoints(this)) Continue((Point)p, p.PressureFactor);
        e.Handled = true;
    }
    protected override void OnStylusUp(StylusEventArgs e)
    {
        if (!stylus) return;
        foreach (var p in e.GetStylusPoints(this)) Continue((Point)p, p.PressureFactor);
        Finish(); e.Handled = true;
    }
}
