using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using ScreenBrush;
using Forms = System.Windows.Forms;

internal static class ZoomInteractionChecks
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    private static void Ctrl(bool down) => keybd_event(0xA2, 0, down ? 0u : 2u, UIntPtr.Zero);
    private static void Mouse(uint flags, uint data = 0) => mouse_event(flags, 0, 0, data, UIntPtr.Zero);
    private static int visualX, visualY;
    private static void MoveTo(int x, int y)
    {
        Native.GetCursorPos(out var real);
        var desktop = Forms.SystemInformation.VirtualScreen;
        int targetX = real.X + x - visualX, targetY = real.Y + y - visualY;
        mouse_event(0xE001, (uint)Math.Ceiling((targetX - desktop.Left) * 65536.0 / desktop.Width), (uint)Math.Ceiling((targetY - desktop.Top) * 65536.0 / desktop.Height), 0, UIntPtr.Zero);
        visualX = x; visualY = y;
    }
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    private sealed class Fixture : Forms.Form
    {
        internal int WheelCount, UpCount, DragMoves;
        internal System.Drawing.Point DownPoint { get; private set; }
        protected override void OnMouseDown(Forms.MouseEventArgs e) { DownPoint = e.Location; Capture = true; base.OnMouseDown(e); }
        protected override void OnMouseMove(Forms.MouseEventArgs e) { if ((e.Button & Forms.MouseButtons.Left) != 0) DragMoves++; base.OnMouseMove(e); }
        protected override void OnMouseUp(Forms.MouseEventArgs e) { UpCount++; Capture = false; base.OnMouseUp(e); }
        protected override void WndProc(ref Forms.Message message) { if (message.Msg == 0x20A) WheelCount++; if (message.Msg == 0x200 && (message.WParam.ToInt64() & 1) != 0) DragMoves++; base.WndProc(ref message); }
    }
    internal static void Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Native.GetCursorPos(out var original);
        app.Startup += async (_, _) =>
        {
            using var fixture = new Fixture { TopMost = true, FormBorderStyle = Forms.FormBorderStyle.None, StartPosition = Forms.FormStartPosition.Manual,
                Bounds = Forms.Screen.PrimaryScreen!.Bounds, BackColor = System.Drawing.Color.FromArgb(215, 224, 235) };
            using var controller = new AppController(new Settings { PreserveSessionOnEscape = true, ToggleZoomPercent = 300 }, false);
            try
            {
                fixture.Show(); fixture.Activate();
                var bounds = fixture.Bounds;
                int cx = bounds.Left + bounds.Width / 2, cy = bounds.Top + bounds.Height / 2;
                SetCursorPos(cx, cy);
                controller.Start(); controller.ToggleDrawing(); controller.Execute(ActionId.ToggleZoom);
                await Task.Delay(700); fixture.TopMost = false;
                var overlay = controller.Overlays.Single(o => o.Screen.Primary);
                var transform = overlay.Surface.ViewMatrix;
                var pixels = overlay.CaptureImage;
                Require(overlay.Surface.Zoom == 3 && pixels != null, "Desktop fixture is zoomed to 300 percent.");
                visualX = cx; visualY = cy; Ctrl(true); await Task.Delay(220);
                Require(overlay.Surface.ViewMatrix == transform && ReferenceEquals(pixels, overlay.CaptureImage) && overlay.ZoomInteractionActive && !overlay.Surface.DrawingEnabled,
                    "Ctrl keeps the exact zoom, anchor, and live capture buffer without zooming out.");
                // Ensure the layered input pass-through still excludes itself from
                // capture and continues to present live content instead of black.
                fixture.BackColor = System.Drawing.Color.FromArgb(180, 200, 220);
                await Task.Delay(250);
                var livePixel = new byte[4];
                for (int i = 0; i < 25; i++)
                {
                    overlay.CaptureImage!.CopyPixels(new Int32Rect(bounds.Width / 2, bounds.Height / 2, 1, 1), livePixel, 4, 0);
                    if (livePixel[0] == 220 && livePixel[1] == 200 && livePixel[2] == 180) break;
                    await Task.Delay(60);
                }
                Require(livePixel[0] == 220 && livePixel[1] == 200 && livePixel[2] == 180, $"Held magnification keeps live desktop capture without self-capture or black frames: {string.Join(',', livePixel)}.");
                await Task.Delay(150);
                Native.SetWindowDisplayAffinity(overlay.Handle, 0); Native.DwmFlush();
                using (var screen = new System.Drawing.Bitmap(1, 1))
                {
                    using (var graphics = System.Drawing.Graphics.FromImage(screen)) graphics.CopyFromScreen(cx + 100, cy + 100, 0, 0, new System.Drawing.Size(1, 1));
                    Require(screen.GetPixel(0, 0).ToArgb() == fixture.BackColor.ToArgb(), $"The actual held zoom window presents its updated desktop image: actual={screen.GetPixel(0, 0)}, expected={fixture.BackColor}.");
                }
                Native.SetWindowDisplayAffinity(overlay.Handle, 0x11);
                MoveTo(cx + 180, cy + 120); await Task.Delay(70); Mouse(2); await Task.Delay(100); Mouse(4); await Task.Delay(100);
                Require(Math.Abs(fixture.DownPoint.X - (bounds.Width / 2 + 60)) <= 2 && Math.Abs(fixture.DownPoint.Y - (bounds.Height / 2 + 40)) <= 2 && fixture.UpCount == 1,
                    $"Click reaches underlying document coordinates, actual={fixture.DownPoint}.");
                Mouse(0x800, 120); await Task.Delay(150);
                Require(fixture.WheelCount > 0, "Scroll reaches the underlying application at unchanged zoom.");
                Mouse(2); await Task.Delay(60);
                int dragBefore = fixture.DragMoves;
                MoveTo(cx + 300, cy + 180); await Task.Delay(90); Mouse(4); await Task.Delay(90);
                Require(fixture.DragMoves > dragBefore && fixture.UpCount == 2, $"Drag reaches the original target even when that app captures the mouse: moves={fixture.DragMoves}, before={dragBefore}, ups={fixture.UpCount}.");
                using var button = new Forms.Button { Text = "Click fixture", Bounds = new System.Drawing.Rectangle(bounds.Width / 2 - 120, bounds.Height / 2 - 80, 70, 35) };
                int clicks = 0; button.Click += (_, _) => clicks++;
                fixture.Controls.Add(button);
                MoveTo(cx - 255, cy - 180); await Task.Delay(70); Mouse(2); await Task.Delay(70); Mouse(4); await Task.Delay(150);
                Require(clicks == 1, "An actual underlying button responds to the remapped click.");
                Ctrl(false); await Task.Delay(180);
                Native.GetCursorPos(out var returned);
                Require(Math.Abs(returned.X - visualX) <= 4 && Math.Abs(returned.Y - visualY) <= 4, "Ctrl release restores the visible cursor position without a jump.");
                Require((Native.GetWindowLongPtr(overlay.Handle, -20).ToInt64() & 0x80020) == 0, "Temporary layered/pass-through styles are removed after releasing Ctrl.");
                Require(overlay.Surface.ViewMatrix == transform && ReferenceEquals(pixels, overlay.CaptureImage) && overlay.Surface.DrawingEnabled && !overlay.ZoomInteractionActive,
                    "Releasing Ctrl resumes drawing without recreating the zoom view.");
                overlay.PanBy(new Vector(35, 25)); var panned = overlay.Surface.ViewMatrix;
                Ctrl(true); await Task.Delay(120); Ctrl(false); await Task.Delay(120);
                Require(overlay.Surface.ViewMatrix == panned, "Panned view position also survives temporary interaction.");
                controller.SelectBoard(BoardBackground.White);
                Ctrl(true); await Task.Delay(160);
                Require(!overlay.BoardVisible && overlay.CaptureImage != null && overlay.Surface.ViewMatrix == panned, "Holding Ctrl on a zoomed board reveals the desktop at the same zoom and position.");
                Ctrl(false); await Task.Delay(160);
                Require(overlay.BoardVisible && overlay.CaptureImage == null && overlay.Surface.ViewMatrix == panned, "Releasing Ctrl restores the board without changing zoom or pan.");
                controller.ToggleBoard(); await Task.Delay(180);
                Ctrl(true); await Task.Delay(160); MoveTo(cx + 250, cy + 200); await Task.Delay(70); Mouse(2); await Task.Delay(70);
                int beforeRelease = fixture.UpCount;
                Ctrl(false); await Task.Delay(160);
                Require((Native.GetAsyncKeyState(1) & 0x8000) == 0 && fixture.UpCount > beforeRelease, "Ctrl release during a native drag releases that button before restoring the pointer.");
                for (int i = 0; i < 5; i++)
                {
                    Ctrl(true); await Task.Delay(70); Ctrl(false); await Task.Delay(70);
                    Require(overlay.Surface.ViewMatrix == panned && (Native.GetWindowLongPtr(overlay.Handle, -20).ToInt64() & 0x80020) == 0, "Repeated holds retain zoom and restore native styles.");
                }
                Ctrl(true); await Task.Delay(130);
                var secondary = controller.Overlays.FirstOrDefault(o => !o.Screen.Primary);
                if (secondary != null)
                {
                    var second = secondary.Screen.Bounds;
                    MoveTo(second.Left + second.Width / 2, second.Top + second.Height / 2); await Task.Delay(100);
                    controller.Execute(ActionId.ToggleZoom); await Task.Delay(700);
                    Require(secondary.ZoomInteractionActive && (Native.GetWindowLongPtr(secondary.Handle, -20).ToInt64() & 0x80020) == 0x80020,
                        "A second monitor zoomed during an existing Ctrl hold also passes native input through.");
                    Ctrl(false); await Task.Delay(120);
                    Require(controller.Overlays.Where(o => o.Zoom > 1).All(o => (Native.GetWindowLongPtr(o.Handle, -20).ToInt64() & 0x80020) == 0), "Releasing Ctrl restores every zoom window's input styles.");
                    Ctrl(true); await Task.Delay(130);
                }
                controller.EnterScreenMode(); await Task.Delay(120);
                Require(!overlay.ZoomInteractionActive && !overlay.Surface.DrawingEnabled && overlay.Surface.Zoom == 1 && overlay.CaptureImage == null,
                    "Esc still removes the zoom overlay and temporary input hook.");
                Console.WriteLine("PASS: held zoom/pan stability, live buffer reuse, real mapped click/button/scroll/drag, board transitions, and Esc cleanup.");
            }
            catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
            finally { Ctrl(false); Mouse(4); controller.Dispose(); fixture.Close(); SetCursorPos(original.X, original.Y); app.Shutdown(); }
        };
        app.Run();
    }
}
