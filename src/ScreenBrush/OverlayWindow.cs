using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace ScreenBrush;

internal sealed class OverlayWindow : Window
{
    internal InkSurface Surface { get; } = new();
    internal Forms.Screen Screen { get; }
    internal event Action<OverlayWindow, int>? WheelForward;
    internal event Action<string>? CaptureFailed;
    internal event Action? ViewPresented;
    private bool boardEnabled;
    private readonly Border board = new() { Background = Brushes.White, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    internal bool BoardVisible => board.Visibility == Visibility.Visible;
    internal void SetBoard(bool value, Brush brush)
    {
        Surface.Finish();
        if (value != boardEnabled) HideZoomWindow();
        boardEnabled = value; board.Background = brush;
        UpdateView();
    }
    private readonly Image background = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly DispatcherTimer captureTimer;
    private DesktopCapture? capture;
    private Task<byte[]>? captureTask;
    private WriteableBitmap? pixels;
    private double zoom = 1;
    private Point anchor;
    private bool enabled;
    private readonly Grid canvas;
    private Window? zoomWindow;
    private bool zoomVisible;
    private double displayedZoom = 1, animationTarget = 1, zoomVelocity;
    private readonly Stopwatch animationClock = new();
    private bool animating, panning;
    private Point panPosition;
    internal IntPtr Handle => new WindowInteropHelper(zoomVisible ? zoomWindow! : this).Handle;
    internal double Zoom => zoom;
    internal BitmapSource? CaptureImage => pixels;
    internal OverlayWindow(Forms.Screen screen)
    {
        Screen = screen;
        Title = "ScreenBrush Canvas";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Left = screen.Bounds.Left; Top = screen.Bounds.Top;
        Width = screen.Bounds.Width; Height = screen.Bounds.Height;
        canvas = new Grid { ClipToBounds = true };
        canvas.Children.Add(background); canvas.Children.Add(board); canvas.Children.Add(Surface); Content = canvas;
        background.RenderTransform = Surface.ViewTransform; board.RenderTransform = Surface.ViewTransform;
        canvas.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (!enabled || displayedZoom <= 1) return;
            Surface.Finish();
            panPosition = e.GetPosition(canvas);
            panning = canvas.CaptureMouse();
            if (panning) { canvas.Cursor = Cursors.Hand; canvas.ForceCursor = true; e.Handled = true; }
        };
        canvas.PreviewMouseMove += (_, e) =>
        {
            if (!panning) return;
            if (e.RightButton != MouseButtonState.Pressed) { EndPan(); return; }
            Point position = e.GetPosition(canvas);
            PanBy(position - panPosition); panPosition = position; e.Handled = true;
        };
        canvas.PreviewMouseRightButtonUp += (_, e) => { if (panning) { EndPan(); e.Handled = true; } };
        canvas.LostMouseCapture += (_, _) => { panning = false; canvas.Cursor = null; canvas.ForceCursor = false; };
        captureTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(66) };
        captureTimer.Tick += async (_, _) => await CaptureNextFrame();
        SourceInitialized += (_, _) =>
        {
            Native.PassThrough(this, true);
            HwndSource.FromHwnd(Handle)?.AddHook(WindowMessage);
        };
        Loaded += (_, _) => Native.SetWindowPos(Handle, new IntPtr(-1), screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height, 0x10);
        PreviewMouseWheel += (_, e) => { WheelForward?.Invoke(this, e.Delta); e.Handled = true; };
        Closed += (_, _) => { Surface.DiscardSession(); StopAnimation(); EndPan(); captureTimer.Stop(); ReleaseCapture(); zoomWindow?.Close(); };
    }
    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        if (msg == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
        if (msg == 0x20A && enabled)
        {
            WheelForward?.Invoke(this, unchecked((short)(wparam.ToInt64() >> 16)));
            handled = true;
        }
        return IntPtr.Zero;
    }
    internal void SetEnabled(bool value)
    {
        Surface.DrawingEnabled = value;
        if (enabled == value) return;
        EndPan();
        Surface.Finish(); enabled = value;
        // A fully zero-alpha layered pixel is skipped by native hit testing.
        // One alpha level makes the drawing canvas receive input without an opaque veil.
        Background = value ? new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) : Brushes.Transparent;
        Surface.DrawingEnabled = value;
        SetPassThrough(!value);
        UpdateView();
    }
    internal void SetPassThrough(bool value) => Native.PassThrough(zoomVisible ? zoomWindow! : this, value);
    internal void ChangeZoom(double value)
    {
        if (zoom == 1 && displayedZoom == 1)
        {
            Native.GetCursorPos(out var pointer);
            anchor = PointFromScreen(new Point(pointer.X, pointer.Y));
            anchor = new Point(Math.Clamp(anchor.X, 0, ActualWidth), Math.Clamp(anchor.Y, 0, ActualHeight));
        }
        zoom = Math.Clamp(value, 1, 6);
        UpdateView();
    }
    private void UpdateView()
    {
        board.Visibility = boardEnabled && enabled ? Visibility.Visible : Visibility.Collapsed;
        canvas.Background = boardEnabled && enabled ? Brushes.White : null;
        if (!enabled)
        {
            StopAnimation(); displayedZoom = 1; ApplyTransform(); HideZoomWindow(); return;
        }
        if ((boardEnabled || zoom > 1) && !zoomVisible && !ShowZoomWindow()) return;
        if (animating && animationTarget == zoom) return;
        if (displayedZoom == zoom) return;
        Surface.Finish();
        animationTarget = zoom;
        if (!animating) { animating = true; animationClock.Restart(); CompositionTarget.Rendering += AnimateZoom; }
    }
    private void AnimateZoom(object? sender, EventArgs e)
    {
        double elapsed = Math.Min(animationClock.Elapsed.TotalSeconds, 0.05);
        animationClock.Restart();
        // Critically damped motion preserves velocity when a new wheel tick arrives.
        const double response = 20;
        double offset = displayedZoom - animationTarget;
        double momentum = zoomVelocity + response * offset;
        double decay = Math.Exp(-response * elapsed);
        displayedZoom = animationTarget + (offset + momentum * elapsed) * decay;
        zoomVelocity = (zoomVelocity - response * momentum * elapsed) * decay;
        bool complete = Math.Abs(displayedZoom - animationTarget) < 0.002 && Math.Abs(zoomVelocity) < 0.03;
        if (complete) displayedZoom = animationTarget;
        ApplyTransform();
        if (complete)
        {
            StopAnimation();
            if (displayedZoom == 1) { EndPan(); if (!boardEnabled) HideZoomWindow(); }
        }
    }
    private void StopAnimation() { CompositionTarget.Rendering -= AnimateZoom; animating = false; animationClock.Stop(); zoomVelocity = 0; }
    private void ApplyTransform()
    {
        Surface.SetAnimatedView(displayedZoom, anchor);
    }
    internal void PanBy(Vector distance)
    {
        if (displayedZoom <= 1) return;
        anchor = new Point(Math.Clamp(anchor.X - distance.X / (displayedZoom - 1), 0, ActualWidth),
            Math.Clamp(anchor.Y - distance.Y / (displayedZoom - 1), 0, ActualHeight));
        ApplyTransform();
    }
    private void EndPan() { panning = false; if (canvas.IsMouseCaptured) canvas.ReleaseMouseCapture(); canvas.Cursor = null; canvas.ForceCursor = false; }
    private void HideZoomWindow()
    {
        captureTimer.Stop();
        if (zoomVisible)
        {
            zoomWindow!.Content = null; Content = canvas;
            background.Source = null;
            Show(); UpdateLayout(); Native.PassThrough(this, !enabled);
            zoomWindow.Hide(); zoomVisible = false;
            ViewPresented?.Invoke();
        }
        background.Source = null; if (zoomWindow != null) zoomWindow.Background = Brushes.Black;
        ReleaseCapture(); pixels = null;
    }
    private bool ShowZoomWindow()
    {
        if (zoomVisible) return true;
        zoomWindow ??= CreateZoomWindow();
        var hwnd = new WindowInteropHelper(zoomWindow).EnsureHandle();
        if (!boardEnabled && !Native.SetWindowDisplayAffinity(hwnd, 0x11))
        {
            int error = Marshal.GetLastWin32Error();
            zoom = 1; Surface.SetView(1, anchor);
            CaptureFailed?.Invoke($"이 환경에서는 화면 확대를 시작할 수 없습니다. (오류 {error})");
            return false;
        }
        // Prepare the first desktop frame before showing an opaque window.
        // Flush the hidden ink overlay so its strokes are not baked into the capture.
        Hide();
        if (!boardEnabled)
        {
            Native.DwmFlush(); CaptureFrame();
            if (pixels == null) { Show(); return false; }
        }
        ApplyTransform();
        Content = null; zoomWindow.Content = canvas;
        zoomWindow.Background = boardEnabled ? Brushes.White : new ImageBrush(pixels) { Stretch = Stretch.Fill };
        // Let WPF present its first frame off-screen, then reveal the ready window.
        // A populated Image alone does not prevent the native window's initial clear.
        zoomWindow.Left = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth + 256;
        EventHandler? reveal = null;
        reveal = (_, _) =>
        {
            zoomWindow.ContentRendered -= reveal;
            if (zoomVisible)
            {
                Native.SetWindowPos(hwnd, new IntPtr(-1), Screen.Bounds.Left, Screen.Bounds.Top, Screen.Bounds.Width, Screen.Bounds.Height, 0x10);
                ViewPresented?.Invoke();
            }
        };
        zoomWindow.ContentRendered += reveal;
        zoomVisible = true;
        zoomWindow.Show();
        Native.PassThrough(zoomWindow, false);
        if (!boardEnabled) captureTimer.Start();
        return true;
    }
    private Window CreateZoomWindow()
    {
        // Capture exclusion is not supported on WPF's per-pixel layered windows.
        // An opaque companion window displays the live desktop while zoomed.
        var window = new Window
        {
            Title = "ScreenBrush Zoom", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            Background = Brushes.Black, Topmost = true, ShowActivated = false, ShowInTaskbar = false,
            Left = Left, Top = Top, Width = ActualWidth, Height = ActualHeight
        };
        window.SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(WindowMessage);
        return window;
    }
    internal BitmapSource CaptureSnapshot()
    {
        Surface.Finish();
        if (!zoomVisible)
        {
            using var desktop = new DesktopCapture(Screen.Bounds);
            var result = BitmapSource.Create(desktop.Width, desktop.Height, 96, 96, PixelFormats.Bgr32, null, desktop.Read(), desktop.Width * 4);
            result.Freeze(); return result;
        }
        canvas.UpdateLayout();
        int width = Screen.Bounds.Width, height = Screen.Bounds.Height;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var rect = new Rect(0, 0, width, height);
            dc.DrawRectangle(boardEnabled ? Brushes.White : new ImageBrush(pixels), null, rect);
            dc.DrawRectangle(new VisualBrush(canvas) {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, canvas.ActualWidth, canvas.ActualHeight), Stretch = Stretch.Fill
            }, null, rect);
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    internal bool SnapshotUsesDesktop => !zoomVisible;
    private void CaptureFrame()
    {
        try
        {
            capture ??= new DesktopCapture(Screen.Bounds);
            PresentFrame(capture, capture.Read());
        }
        catch (Exception e) when (e is Win32Exception or ExternalException or ArgumentException)
        {
            FailCapture(e);
        }
    }
    private async Task CaptureNextFrame()
    {
        var session = capture;
        if (!zoomVisible || session == null || captureTask != null) return;
        var task = Task.Run(session.Read);
        captureTask = task;
        try
        {
            byte[] frame = await task;
            if (zoomVisible && ReferenceEquals(capture, session)) PresentFrame(session, frame, true);
        }
        catch (Exception e) when (e is Win32Exception or ExternalException or ArgumentException)
        {
            if (ReferenceEquals(capture, session)) FailCapture(e);
        }
        finally { if (ReferenceEquals(captureTask, task)) captureTask = null; }
    }
    private void PresentFrame(DesktopCapture session, byte[] frame, bool nonBlocking = false)
    {
        pixels ??= new WriteableBitmap(session.Width, session.Height, 96, 96, PixelFormats.Bgr32, null);
        if (!nonBlocking) pixels.WritePixels(new Int32Rect(0, 0, session.Width, session.Height), frame, session.Width * 4, 0);
        else
        {
            if (!pixels.TryLock(new Duration(TimeSpan.Zero))) return;
            try
            {
                int stride = session.Width * 4;
                if (pixels.BackBufferStride == stride) Marshal.Copy(frame, 0, pixels.BackBuffer, frame.Length);
                else for (int y = 0; y < session.Height; y++)
                    Marshal.Copy(frame, y * stride, IntPtr.Add(pixels.BackBuffer, y * pixels.BackBufferStride), stride);
                pixels.AddDirtyRect(new Int32Rect(0, 0, session.Width, session.Height));
            }
            finally { pixels.Unlock(); }
        }
        background.Source = pixels;
    }
    private void ReleaseCapture()
    {
        var session = capture; capture = null;
        if (session == null) return;
        if (captureTask is { IsCompleted: false } task)
            _ = task.ContinueWith(_ => session.Dispose(), TaskScheduler.Default);
        else session.Dispose();
    }
    private void FailCapture(Exception error)
    {
        zoom = 1; StopAnimation(); displayedZoom = 1; captureTimer.Stop(); pixels = null; background.Source = null; Surface.SetView(1, anchor);
        HideZoomWindow();
        CaptureFailed?.Invoke("화면 확대를 중단했습니다: " + error.Message);
    }
}
