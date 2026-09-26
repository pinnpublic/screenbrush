using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenBrush;

public enum BoardBackground { White, Black, CustomColor, Chalkboard, Notebook, Dots, CustomImage }

internal static class Whiteboard
{
    internal static readonly (ActionId Action, BoardBackground Background, string Name)[] Palette =
    {
        (ActionId.BoardWhite, BoardBackground.White, "흰색"),
        (ActionId.BoardBlack, BoardBackground.Black, "검정"),
        (ActionId.BoardColor, BoardBackground.CustomColor, "사용자 색"),
        (ActionId.BoardChalk, BoardBackground.Chalkboard, "1 칠판"),
        (ActionId.BoardNotebook, BoardBackground.Notebook, "2 노트"),
        (ActionId.BoardDots, BoardBackground.Dots, "3 도트"),
        (ActionId.BoardImage, BoardBackground.CustomImage, "사용자 이미지")
    };

    internal static Brush Load(Settings settings)
    {
        var kind = settings.BoardBackground;
        if (kind == BoardBackground.White) return Brushes.White;
        if (kind == BoardBackground.Black) return Brushes.Black;
        if (kind == BoardBackground.CustomColor)
        {
            var color = (Color)ColorConverter.ConvertFromString(settings.BoardColor);
            color.A = 255;
            var solid = new SolidColorBrush(color); solid.Freeze(); return solid;
        }
        using var stream = kind == BoardBackground.CustomImage
            ? File.OpenRead(settings.BoardImagePath)
            : typeof(Whiteboard).Assembly.GetManifestResourceStream($"ScreenBrush.Board.{kind}.png")
                ?? throw new IOException("화이트보드 배경 리소스가 없습니다.");
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        // Built-in backgrounds retain all source detail, including 4K assets.
        // Keep the existing decode budget for user-provided files.
        if (kind == BoardBackground.CustomImage) bitmap.DecodePixelWidth = 1920;
        bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        var brush = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
        brush.Freeze(); return brush;
    }

    internal static bool TryLoad(Settings settings, out Brush brush, out string? error)
    {
        try { brush = Load(settings); error = null; return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Runtime.InteropServices.COMException or FileFormatException)
        { brush = Brushes.White; error = "화이트보드 이미지를 열 수 없습니다. 설정에서 파일을 다시 지정하세요. " + e.Message; return false; }
    }
}
