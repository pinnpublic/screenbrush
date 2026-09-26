using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ScreenBrush;

// One instance belongs to one zoom session; reads never overlap.
internal sealed class DesktopCapture : IDisposable
{
    private readonly Rectangle bounds;
    private readonly Bitmap bitmap;
    private readonly Graphics graphics;
    private readonly byte[] frame;
    internal int Width => bounds.Width;
    internal int Height => bounds.Height;
    internal DesktopCapture(Rectangle bounds)
    {
        this.bounds = bounds;
        bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        try
        {
            frame = new byte[checked(bounds.Width * bounds.Height * 4)];
            graphics = Graphics.FromImage(bitmap);
        }
        catch { bitmap.Dispose(); throw; }
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
        try { Marshal.Copy(data.Scan0, frame, 0, frame.Length); }
        finally { bitmap.UnlockBits(data); }
        return frame;
    }
    public void Dispose() { graphics.Dispose(); bitmap.Dispose(); }
}
