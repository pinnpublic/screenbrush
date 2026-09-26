using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ScreenBrush;

internal static class DisabledActionChecks
{
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    private static void Key(byte key, uint flags) => keybd_event(key, 0, flags, UIntPtr.Zero);
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try
            {
                var settings = new Settings { ShowToolbarOnStartup = true };
                foreach (var action in Enum.GetValues<ActionId>()) settings.EnabledActions[action] = false;
                using var controller = new AppController(settings, false);
                controller.Start(); controller.SelectTool(Tool.Marker); await Task.Delay(150);
                Require(controller.Overlays.All(o => o.Surface.DrawingEnabled), "Disabling shortcuts does not block drawing with a selected brush.");
                foreach (string name in new[] { "tools", "colorButtons", "shortcutButtons" })
                {
                    var buttons = (IDictionary)typeof(ToolbarWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller.Toolbar)!;
                    foreach (Button button in buttons.Values)
                        Require(button.IsEnabled && button.Visibility == Visibility.Visible, "Buttons stay visible and interactive with shortcuts disabled.");
                }
                var hotkeys = (Hotkeys)typeof(AppController).GetField("hotkeys", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
                var active = (IDictionary)typeof(Hotkeys).GetField("active", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hotkeys)!;
                Require(active.Count == 0, "No disabled action remains registered, including hook shortcuts.");
                Key(0xA2, 0); Key(0xA0, 0); Key(0x33, 0); Key(0x33, 2); Key(0xA0, 2); Key(0xA2, 2);
                await Task.Delay(180);
                Require(controller.Settings.Tool == Tool.Marker, "Disabled Ctrl+Shift+3 does not select line.");
                var tools = (IDictionary)typeof(ToolbarWindow).GetField("tools", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller.Toolbar)!;
                ((Button)tools[Tool.Line]!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(controller.Settings.EffectiveTool == Tool.Line && controller.Overlays.All(o => o.Surface.DrawingEnabled), "Line button still selects a drawing shape with its shortcut disabled.");
                controller.Execute(ActionId.ColorRed);
                Require(controller.Settings.Color == "#FFEF4444" && !controller.Settings.CycleColors, "Palette remains usable with its shortcut disabled.");
                controller.Execute(ActionId.BoardWhite);
                Require(controller.Settings.WhiteboardEnabled, "Board controls remain usable with shortcuts disabled.");
                controller.Execute(ActionId.ToggleBoard);
                controller.SelectTool(Tool.Marker);
                controller.SelectFreehand();
                settings.EnabledActions[ActionId.Marker] = true;
                settings.EnabledActions[ActionId.Line] = true;
                Require(hotkeys.Apply(settings.ActiveShortcuts()) == null, "Reenabled action registers successfully.");
                Key(0xA2, 0); Key(0xA0, 0); Key(0x33, 0); Key(0x33, 2); Key(0xA0, 2); Key(0xA2, 2);
                await Task.Delay(250);
                Require(controller.Settings.EffectiveTool == Tool.Line && controller.Overlays.All(o => o.Surface.DrawingEnabled), "Reenabled line shortcut restores drawing with enabled brush.");
                using (var blocker = new Hotkeys(System.Windows.Input.Key.LeftCtrl))
                {
                    var reserved = new Shortcut(System.Windows.Input.Key.F10, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt | System.Windows.Input.ModifierKeys.Shift);
                    Require(blocker.Apply(new System.Collections.Generic.Dictionary<ActionId, Shortcut> { [ActionId.ToggleAutoFade] = reserved }) == null, "Conflict fixture reserves a shortcut.");
                    var edited = settings.Clone();
                    edited.EnabledActions[ActionId.ToggleAutoFade] = true;
                    edited.Shortcuts[ActionId.ToggleAutoFade] = reserved;
                    Require(controller.PreviewShortcuts(edited)?.Contains("1409") == true, "Editing detects an external conflict before saving.");
                    edited.EnabledActions[ActionId.ToggleAutoFade] = false;
                    Require(controller.PreviewShortcuts(edited) == null, "Unchecking a conflicting shortcut immediately clears the error.");
                    edited.Shortcuts[ActionId.Line] = edited.Shortcuts[ActionId.Marker];
                    Require(controller.PreviewShortcuts(edited)?.Contains("중복") == true, "Editing detects internal duplicates before saving.");
                    Require(hotkeys.Apply(settings.ActiveShortcuts()) == null, "Cancel restores the original bindings.");
                }
                controller.Quit();
                Require(controller.Disposing, "Quit remains available through direct UI command even when its shortcut is disabled.");
                Console.WriteLine("PASS: disabled shortcuts unregistered, real key ignored then reenabled, buttons and drawing remain usable, palette/board/quit commands work.");
            }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            finally
            {
                Key(0xA0, 2); Key(0xA2, 2); app.Shutdown();
            }
        };
        app.Run();
    }
}
