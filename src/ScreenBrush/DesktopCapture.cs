using System;
using System.Buffers;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ScreenBrush;

// One instance belongs to one zoom session; reads never overlap.
internal sealed class DesktopCapture : IDisposable
{
    // Bound retained buffers and share them across UI/worker threads. Unlike the
    // shared pool, this avoids one large cached array per returning thread.
    private static readonly ArrayPool<byte> Frames = ArrayPool<byte>.Create(128 * 1024 * 1024, 2);
    private readonly Rectangle bounds;
    private readonly Bitmap bitmap;
    private readonly Graphics graphics;
    private readonly byte[] frame;
    private readonly int frameLength;
    private bool disposed;
    internal int Width => bounds.Width;
    internal int Height => bounds.Height;
    internal DesktopCapture(Rectangle bounds)
    {
        this.bounds = bounds;
        bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        try
        {
            frameLength = checked(bounds.Width * bounds.Height * 4);
            frame = Frames.Rent(frameLength);
            graphics = Graphics.FromImage(bitmap);
        }
        catch { if (frame != null) Frames.Return(frame); bitmap.Dispose(); throw; }
    }
    internal byte[] Read()
    {
        {
            IntPtr source = Native.GetDC(IntPtr.Zero);
            if (source == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "화면 DC를 열 수 없습니다.");
            try
            {
                IntPtr destination = graphics.GetHdc();
                try
                {
                    if (!Native.BitBlt(destination, 0, 0, Width, Height, source, bounds.Left, bounds.Top, 0x40CC0020))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                finally { graphics.ReleaseHdc(destination); }
            }
            finally { Native.ReleaseDC(IntPtr.Zero, source); }
        }
        var data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try { Marshal.Copy(data.Scan0, frame, 0, frameLength); }
        finally { bitmap.UnlockBits(data); }
        return frame;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        graphics.Dispose(); bitmap.Dispose();
        Frames.Return(frame);
    }
}
