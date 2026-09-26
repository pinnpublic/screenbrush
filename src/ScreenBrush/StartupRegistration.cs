using System;
using System.IO;
using Microsoft.Win32;

namespace ScreenBrush;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ScreenBrush";

    internal static string Command
    {
        get
        {
            string executable = Path.Combine(AppContext.BaseDirectory, "ScreenBrush.exe");
            if (!File.Exists(executable)) throw new IOException("ScreenBrush.exe를 찾을 수 없습니다.");
            return "\"" + executable + "\"";
        }
    }

    internal static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
    }

    // Keep the previous registration if saving the other settings fails.
    internal static void Save(bool enabled, Action saveSettings)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        object? previous = key.GetValue(ValueName);
        var kind = previous == null ? RegistryValueKind.String : key.GetValueKind(ValueName);
        if (enabled) key.SetValue(ValueName, Command, RegistryValueKind.String);
        else key.DeleteValue(ValueName, false);
        try { saveSettings(); }
        catch
        {
            if (previous == null) key.DeleteValue(ValueName, false);
            else key.SetValue(ValueName, previous, kind);
            throw;
        }
    }
}
