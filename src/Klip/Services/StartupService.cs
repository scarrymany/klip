using System.Diagnostics;
using Microsoft.Win32;

namespace Klip.Services;

public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Klip";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var path = ResolveExecutablePath();
        if (string.IsNullOrEmpty(path))
            return;

        key.SetValue(ValueName, $"\"{path}\"");
    }

    public static void RepairIfEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(ValueName) is not string configured || string.IsNullOrWhiteSpace(configured))
                return;

            var path = ResolveExecutablePath();
            if (string.IsNullOrWhiteSpace(path))
                return;

            var expected = $"\"{path}\"";
            if (!configured.Equals(expected, StringComparison.OrdinalIgnoreCase))
                key.SetValue(ValueName, expected);
        }
        catch
        {
            // Startup repair must never block launching Klip.
        }
    }

    private static string? ResolveExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path))
            return path;

        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }
}
