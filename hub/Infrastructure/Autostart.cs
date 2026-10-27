using Microsoft.Win32;

namespace VoicedeckEdge.Hub.Infrastructure;

/// <summary>"Mit Windows starten" via HKCU\...\Run – per user, no admin rights.</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VoicedeckEdgeHub";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --autostart");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
