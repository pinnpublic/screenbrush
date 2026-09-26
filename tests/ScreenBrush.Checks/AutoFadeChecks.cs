using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using ScreenBrush;

internal static class AutoFadeChecks
{
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Stroke(InkSurface surface, double y)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(InkSurface).GetMethod("Begin", flags)!.Invoke(surface, new object?[] { new Point(20, y), null, false });
        typeof(InkSurface).GetMethod("Continue", flags)!.Invoke(surface, new object?[] { new Point(200, y), null });
        surface.Finish();
    }
    internal static void Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            var surface = new InkSurface { Tool = Tool.Line, InkColor = Colors.Red, InkWidth = 9, AutoFadeEnabled = true };
            var window = new Window { Width = 300, Height = 250, Content = surface };
            try
            {
                window.Show();
                Stroke(surface, 30);
                var fading = (IDictionary)typeof(InkSurface).GetField("fading", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(surface)!;
                DrawingGroup? visual = null;
                foreach (DrawingGroup item in fading.Values) visual = item;
                await Task.Delay(1000);
                Require(surface.MarkCount == 1 && visual!.Opacity == 1, "Stroke must remain opaque during three-second hold.");
                surface.AutoFadeEnabled = false;
                Stroke(surface, 60);
                await Task.Delay(2650);
                Require(visual!.Opacity > 0 && visual.Opacity < 1 && surface.MarkCount == 2, "Default fade must be gradual after hold; toggling off does not reset existing fade.");
                await Task.Delay(1200);
                Require(surface.MarkCount == 1 && fading.Count == 0, "Expired stroke removed and animation released; ordinary stroke remains.");
                surface.Undo(); surface.Undo(); surface.Redo(); surface.Redo();
                Require(surface.MarkCount == 1, "Undo and redo never resurrect expired ink and retain ordinary ink.");
                surface.AutoFadeEnabled = true; surface.AutoFadeHoldSeconds = 0; surface.AutoFadeDurationSeconds = 0.3;
                surface.Tool = Tool.Marker;
                Stroke(surface, 90);
                surface.Undo();
                await Task.Delay(600);
                surface.Redo();
                Require(surface.MarkCount == 1 && fading.Count == 0, "Undone freehand stroke expires without redo resurrection.");
                surface.AutoFadeHoldSeconds = 0.2;
                typeof(InkSurface).GetMethod("Begin", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(surface, new object?[] { new Point(20, 120), null, false });
                await Task.Delay(700);
                Require(fading.Count == 0 && surface.MarkCount == 1, "No fade timer runs while a long stroke is still being drawn.");
                typeof(InkSurface).GetMethod("Continue", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(surface, new object?[] { new Point(220, 120), null });
                surface.Finish();
                Require(fading.Count == 1, "One opacity animation controls the entire completed stroke.");
                await Task.Delay(100);
                foreach (DrawingGroup item in fading.Values) Require(item.Opacity == 1, "Hold time starts at completion, regardless of drawing duration.");
                await Task.Delay(650);
                Require(surface.MarkCount == 1 && fading.Count == 0, "The entire long stroke expires together.");
                Stroke(surface, 120); surface.DiscardSession();
                await Task.Delay(500);
                Require(surface.MarkCount == 0 && fading.Count == 0, "Discard cancels all fading ink safely.");
                Console.WriteLine("PASS: auto fade defaults, hold, partial opacity, completion, toggle, freehand, undo/redo, and discard cleanup.");
            }
            catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
            finally { surface.DiscardSession(); window.Close(); app.Shutdown(); }
        };
        app.Run();
    }
}
