namespace VoicedeckEdge.Hub.Infrastructure;

/// <summary>All places the hub writes to. Roaming = settings/secrets, Local = logs.</summary>
internal static class AppPaths
{
    public static string DataDir { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoicedeckEdge"));

    public static string LogDir { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoicedeckEdge", "logs"));

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string TokenFile => Path.Combine(DataDir, "tokens.bin");
    public static string PairingFile => Path.Combine(DataDir, "pairings.json");

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
