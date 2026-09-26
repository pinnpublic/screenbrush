using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenBrush;

internal static class AppIcon
{
    private static Stream Open() => typeof(AppIcon).Assembly.GetManifestResourceStream("ScreenBrush.AppIcon.ico")!;

    internal static ImageSource WindowImage { get; } = LoadWindowImage();

    private static ImageSource LoadWindowImage()
    {
        using var stream = Open();
        var image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        image.Freeze();
        return image;
    }

    internal static System.Drawing.Icon CreateTrayIcon()
    {
        using var stream = Open();
        using var icon = new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
        return (System.Drawing.Icon)icon.Clone();
    }
}
