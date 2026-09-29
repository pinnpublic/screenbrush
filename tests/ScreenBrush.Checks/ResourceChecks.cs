using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using ScreenBrush;
using Forms = System.Windows.Forms;

internal static class ResourceChecks
{
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    private static readonly List<object> results = new();
    private static object Snapshot()
    {
        using var process = Process.GetCurrentProcess(); process.Refresh();
        return new { workingSetMB = Math.Round(process.WorkingSet64 / 1048576.0, 1), privateMB = Math.Round(process.PrivateMemorySize64 / 1048576.0, 1),
            managedMB = Math.Round(GC.GetTotalMemory(false) / 1048576.0, 1), handles = process.HandleCount,
            gdi = GetGuiResources(process.Handle, 0), user = GetGuiResources(process.Handle, 1), threads = process.Threads.Count };
    }
    private static async Task Measure(string phase, Func<Task> workload)
    {
        using var process = Process.GetCurrentProcess();
        var before = Snapshot(); var cpu = process.TotalProcessorTime; var clock = Stopwatch.StartNew();
        long allocated = GC.GetTotalAllocatedBytes(); int gen2 = GC.CollectionCount(2);
        await workload();
        double seconds = clock.Elapsed.TotalSeconds;
        double corePercent = (process.TotalProcessorTime - cpu).TotalSeconds / seconds * 100;
        var row = new { phase, seconds = Math.Round(seconds, 2), cpuMachinePercent = Math.Round(corePercent / Environment.ProcessorCount, 2),
            cpuOneCorePercent = Math.Round(corePercent, 2), allocatedMB = Math.Round((GC.GetTotalAllocatedBytes() - allocated) / 1048576.0, 2),
            gen2Collections = GC.CollectionCount(2) - gen2, before, after = Snapshot() };
        results.Add(row); Console.WriteLine(JsonSerializer.Serialize(row));
    }
    internal static void Run(string output = "artifacts/resource-check.json")
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AppController? controller = null; Forms.Form? fixture = null;
        Native.GetCursorPos(out var originalCursor);
        app.Startup += async (_, _) =>
        {
            try
            {
                fixture = new Forms.Form { Text = "ScreenBrush resource check", WindowState = Forms.FormWindowState.Maximized,
                    BackColor = System.Drawing.Color.FromArgb(210, 225, 240) };
                fixture.Show(); fixture.Activate();
                controller = new AppController(new Settings { PreserveSessionOnEscape = true }, false); controller.Start();
                var overlay = controller.Overlays.First(o => o.Screen.Primary);
                var bounds = overlay.Screen.Bounds;
                SetCursorPos(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                await Task.Delay(1500);
                await Measure("tray-idle", () => Task.Delay(8000));
                controller.ToggleDrawing();
                await Measure("drawing-idle", () => Task.Delay(8000));
                overlay.ChangeZoom(3); await Task.Delay(1000);
                await Measure("zoom-300-idle", () => Task.Delay(8000));
                overlay.ChangeZoom(6); await Task.Delay(1000);
                await Measure("zoom-600-idle", () => Task.Delay(8000));
                await Measure("zoom-600-pencil-shapes", async () =>
                {
                    foreach (var tool in new[] { Tool.Pencil, Tool.Rectangle, Tool.Ballpoint, Tool.Ellipse, Tool.Marker, Tool.Pencil })
                    {
                        controller.SelectTool(tool);
                        SetCursorPos(bounds.Left + 450, bounds.Top + 350);
                        mouse_event(2, 0, 0, 0, UIntPtr.Zero);
                        for (int i = 0; i < 70; i++)
                        {
                            SetCursorPos(bounds.Left + 450 + i * 4, bounds.Top + 350 + (int)(40 * Math.Sin(i / 9.0)));
                            await Task.Delay(12);
                        }
                        mouse_event(4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(180);
                        controller.Execute(ActionId.Undo);
                    }
                });
                controller.Execute(ActionId.ZoomReset); await Task.Delay(1000);
                for (int batch = 1; batch <= 2; batch++)
                {
                    await Measure($"zoom-toggle-10-batch-{batch}", async () =>
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            overlay.ChangeZoom(6); await Task.Delay(220);
                            controller.ToggleDrawing(); await Task.Delay(100);
                            controller.ToggleDrawing(); await Task.Delay(220);
                            controller.Execute(ActionId.ZoomReset); await Task.Delay(700);
                        }
                    });
                    await Measure($"settled-after-batch-{batch}", () => Task.Delay(5000));
                    // Diagnostic only: never introduce forced GC in the application.
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); await Task.Delay(300);
                    results.Add(new { phase = $"reachable-after-gc-{batch}", resources = Snapshot() });
                }
                controller.ToggleDrawing();
                await Measure("returned-tray-idle", () => Task.Delay(8000));
                controller.Dispose();
                await Measure("after-dispose", () => Task.Delay(2000));
                Directory.CreateDirectory("artifacts");
                File.WriteAllText(output, JsonSerializer.Serialize(new { logicalProcessors = Environment.ProcessorCount,
                    screens = Forms.Screen.AllScreens.Select(s => new { s.Bounds.Width, s.Bounds.Height }), results }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
            finally
            {
                mouse_event(4, 0, 0, 0, UIntPtr.Zero); controller?.Dispose(); fixture?.Close();
                SetCursorPos(originalCursor.X, originalCursor.Y); app.Shutdown(Environment.ExitCode);
            }
        };
        app.Run();
    }
}
