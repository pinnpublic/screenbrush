using System;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;

namespace ScreenBrush;

internal static class ScreenSave
{
    internal static string DefaultName(DateTime time) => "screenbrush_" + time.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + ".png";

    internal static string Write(BitmapSource image, string path)
    {
        path = Path.GetFullPath(Path.ChangeExtension(path, ".png"));
        string folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);
        string stem = Path.GetFileNameWithoutExtension(path);
        for (int index = 0; ; index++)
        {
            string candidate = index == 0 ? path : Path.Combine(folder, $"{stem}_{index}.png");
            FileStream file;
            try { file = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
            catch (IOException) when (File.Exists(candidate)) { continue; }
            try
            {
                using (file)
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    encoder.Save(file);
                }
                return candidate;
            }
            catch { file.Dispose(); File.Delete(candidate); throw; }
        }
    }
}
