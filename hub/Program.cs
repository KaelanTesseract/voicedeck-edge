using VoicedeckEdge.Hub.Discord;
using VoicedeckEdge.Hub.Infrastructure;
using VoicedeckEdge.Hub.Server;
using VoicedeckEdge.Hub.Tray;

namespace VoicedeckEdge.Hub;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Used by the uninstaller: revoke the Discord sign-in and delete every file, no UI.
        if (args.Contains("--delete-all-data")) return DeleteAllData();
#if DEBUG
        // Documentation screenshots: VoicedeckEdgeHub.exe --render-wizard <dir> <de|en>
        if (args.Length >= 3 && args[0] == "--render-wizard") return RenderWizard(args[1], args[2]);
#endif

        using var mutex = new Mutex(initiallyOwned: true, @"Local\VoicedeckEdgeHub", out var isFirst);
        ApplicationConfiguration.Initialize();
        if (!isFirst)
        {
            MessageBox.Show(Strings.AlreadyRunning, Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        Log.Init(AppPaths.LogDir);
        Log.Info($"Hub startet, Version {HubServer.HubVersion}");
        Application.Run(new TrayContext(startedByAutostart: args.Contains("--autostart"), devMode: args.Contains("--dev")));
        return 0;
    }

#if DEBUG
    private static int RenderWizard(string dir, string lang)
    {
        // Must happen before Strings is touched: it picks the language once.
        System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo(lang);
        ApplicationConfiguration.Initialize();
        Directory.CreateDirectory(dir);
        var settings = new HubSettings { ClientId = "1234567890123456789" };
        var session = new DiscordSession(settings, new TokenStore(Path.GetTempFileName()), new DiscordOAuth(new HttpClient()));
        using var wizard = new SetupWizard(settings, session, _ => { })
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-4000, -4000),
            ShowInTaskbar = false,
        };
        wizard.Show();
        string[] names = ["welcome", "discord-app", "connect", "widget"];
        for (var i = 0; i < names.Length; i++)
            wizard.RenderPage(i, Path.Combine(dir, $"wizard-{i + 1}-{names[i]}.png"));
        return 0;
    }
#endif

    private static int DeleteAllData()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var result = DataReset.RunAsync(new DiscordOAuth(http), new TokenStore(AppPaths.TokenFile),
            new PairingStore(AppPaths.PairingFile), images: null, AppPaths.DataDir, AppPaths.LogDir,
            CancellationToken.None).GetAwaiter().GetResult();
        return result.HadSignIn && !result.Revoked ? 2 : 0;
    }
}
