using Microsoft.Win32;

namespace NexaTextGrab;

internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "NexaTextGrab";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(AppName) is string value && value.Contains("Nexa Text Grab", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void Enable()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Could not locate the application executable.");
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey, true);
        key.SetValue(AppName, $"\"{executable}\"");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        key?.DeleteValue(AppName, false);
    }
}
