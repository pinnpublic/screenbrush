using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace ScreenBrush;

public sealed class App : Application
{
    private AppController? controller;
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--render-samples")
        {
            BrushSamples.Render(args.Length > 1 ? args[1] : "brush-samples.png");
            return;
        }
        using var instance = new Mutex(true, "Local\\ScreenBrush.Desktop", out bool first);
        if (!first) { MessageBox.Show("ScreenBrush가 이미 실행 중입니다."); return; }
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            app.controller?.Dispose();
            MessageBox.Show("ScreenBrush를 종료합니다.\n" + e.Exception.Message, "ScreenBrush");
            e.Handled = true;
            app.Shutdown(1);
        };
        app.Startup += (_, _) => { app.controller = new AppController(); app.controller.Start(); };
        app.Exit += (_, _) => app.controller?.Dispose();
        app.Run();
    }
}
