using System.Diagnostics;
using System.Net;
using VoicedeckEdge.Hub.Discord;
using VoicedeckEdge.Hub.Infrastructure;
using VoicedeckEdge.Hub.Server;
using VoicedeckEdge.Hub.State;

namespace VoicedeckEdge.Hub.Tray;

/// <summary>Composition root and tray UI. Everything else runs on worker threads.</summary>
internal sealed class TrayContext : ApplicationContext, IPairingPrompt
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromDays(1);

    private readonly CancellationTokenSource _cts = new();
    private readonly Control _ui = new();
    private readonly NotifyIcon _tray = new();
    private readonly ToolStripMenuItem _statusItem = new() { Enabled = false };
    private readonly ToolStripMenuItem _authorizeItem = new(Strings.MenuAuthorize);
    private readonly ToolStripMenuItem _autostartItem = new(Strings.MenuAutostart) { CheckOnClick = true };
    private readonly ToolStripMenuItem _updateItem = new(Strings.MenuUpdateCheck) { CheckOnClick = true };
    private readonly HubSettings _settings = HubSettings.Load(AppPaths.SettingsFile);
    private readonly HttpClient _http;
    private readonly DiscordOAuth _oauth;
    private readonly TokenStore _tokenStore = new(AppPaths.TokenFile);
    private readonly DiscordSession _session;
    private readonly HubServer _server;
    private readonly UpdateChecker _updates;
    private bool _portBusy;
    private string? _updateUrl;
    private Form? _openForm;

    public TrayContext(bool startedByAutostart, bool devMode)
    {
        _ui.CreateControl(); // gives us a UI-thread handle for BeginInvoke

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"VoicedeckEdgeHub/{HubServer.HubVersion}");
        _oauth = new DiscordOAuth(_http);
        _updates = new UpdateChecker(_http, HubServer.HubVersion);

        _session = new DiscordSession(_settings, _tokenStore, _oauth);
        var media = new MediaRegistry();
        var state = new StateStore(media);
        var controller = new HubController(_session, state);
        _server = new HubServer(state, controller, new PairingStore(AppPaths.PairingFile), new ImageCache(_http, media), this)
        {
            AllowLocalDevPages = devMode,
        };
        if (devMode) Log.Warn("Entwicklermodus: Testseiten von http://localhost sind zugelassen.");

        BuildTray();
        _session.StatusChanged += _ => _ui.BeginInvoke(UpdateTray);

        try
        {
            _server.Start();
            _ = Task.Run(() => _server.RunAsync(_cts.Token));
        }
        catch (HttpListenerException ex)
        {
            _portBusy = true;
            Log.Error($"Port {HubServer.Port} nicht verfügbar", ex);
        }
        _ = Task.Run(() => controller.RunAsync(_cts.Token));
        _ = Task.Run(() => _session.RunAsync(_cts.Token));
        _ = Task.Run(() => UpdateLoopAsync(_cts.Token));
        UpdateTray();

        var updated = _settings.NoteStartedVersion(HubServer.HubVersion);
        try { _settings.Save(AppPaths.SettingsFile); }
        catch (IOException ex) { Log.Warn($"settings.json nicht speicherbar: {ex.Message}"); }
        if (updated)
            _ui.BeginInvoke(() => ShowSetup(afterUpdate: true)); // new widget version: offer the re-import right away
        else if (!HubSettings.IsValidClientId(_settings.ClientId) && !startedByAutostart)
            _ui.BeginInvoke(() => ShowSetup());
    }

    // ---------- tray ----------

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Strings.MenuSetup, null, (_, _) => ShowSetup());
        _authorizeItem.Click += (_, _) => Authorize();
        menu.Items.Add(_authorizeItem);
        menu.Items.Add(Strings.MenuPaired, null, (_, _) => ShowForm(new PairedWidgetsForm(_server.Pairings)));
        menu.Items.Add(new ToolStripSeparator());

        _autostartItem.Checked = Autostart.IsEnabled();
        _autostartItem.CheckedChanged += (_, _) => Autostart.Set(_autostartItem.Checked);
        menu.Items.Add(_autostartItem);
        _updateItem.Checked = _settings.UpdateCheck;
        _updateItem.CheckedChanged += (_, _) => SetUpdateCheck(_updateItem.Checked);
        menu.Items.Add(_updateItem);
        menu.Items.Add(Strings.MenuLog, null, (_, _) => OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Strings.MenuDeleteAll, null, async (_, _) => await DeleteAllDataAsync());
        menu.Items.Add(Strings.MenuExit, null, (_, _) => Exit());

        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            switch (_session.Status)
            {
                case DiscordStatus.NotConfigured or DiscordStatus.InvalidClientId: ShowSetup(); break;
                case DiscordStatus.Unauthorized: Authorize(); break;
            }
        };
        _tray.BalloonTipClicked += (_, _) =>
        {
            if (_updateUrl != null) SetupWizard.OpenUrl(_updateUrl);
        };
        _tray.Visible = true;
    }

    private void UpdateTray()
    {
        var (text, state) = _portBusy
            ? (Strings.StatusPortBusy, TrayState.Problem)
            : _session.Status switch
            {
                DiscordStatus.Ready => (Strings.StatusReady, TrayState.Ok),
                DiscordStatus.NotConfigured or DiscordStatus.InvalidClientId => (Strings.StatusNotConfigured, TrayState.Problem),
                DiscordStatus.Unauthorized => (Strings.StatusUnauthorized, TrayState.Problem),
                DiscordStatus.DiscordNotRunning => (Strings.StatusDiscordNotRunning, TrayState.Waiting),
                _ => (Strings.StatusConnecting, TrayState.Waiting),
            };
        _statusItem.Text = text;
        _authorizeItem.Visible = _session.Status == DiscordStatus.Unauthorized;
        _tray.Icon = TrayIcons.Get(state);
        var tip = $"{Strings.AppName}: {text}";
        _tray.Text = tip.Length > 127 ? tip[..127] : tip;
    }

    // ---------- setup ----------

    private void ShowSetup() => ShowSetup(afterUpdate: false);

    private void ShowSetup(bool afterUpdate)
    {
        if (_openForm is SetupWizard) { _openForm.Activate(); return; }
        var wizard = new SetupWizard(_settings, _session, SaveClientId, afterUpdate);
        wizard.Completed += (autostart, updates) =>
        {
            _autostartItem.Checked = autostart; // CheckedChanged writes the registry
            _updateItem.Checked = updates;      // CheckedChanged saves the setting
        };
        ShowForm(wizard);
    }

    private void SaveClientId(string clientId)
    {
        var changed = clientId != _settings.ClientId;
        _settings.ClientId = clientId;
        _settings.Save(AppPaths.SettingsFile);
        if (changed || _session.Status is DiscordStatus.NotConfigured or DiscordStatus.InvalidClientId)
            _session.Restart();
    }

    private void Authorize() => _ = _session.AuthorizeAsync(_cts.Token);

    // ---------- updates (opt-in) ----------

    private void SetUpdateCheck(bool enabled)
    {
        if (_settings.UpdateCheck == enabled) return;
        _settings.UpdateCheck = enabled;
        _settings.Save(AppPaths.SettingsFile);
        if (enabled) _ = Task.Run(() => CheckForUpdateAsync(_cts.Token));
    }

    private async Task UpdateLoopAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            while (!ct.IsCancellationRequested)
            {
                await CheckForUpdateAsync(ct);
                await Task.Delay(UpdateInterval, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task CheckForUpdateAsync(CancellationToken ct)
    {
        if (!_settings.UpdateCheck) return;
        if (await _updates.CheckAsync(ct) is not { } update) return;
        _ui.BeginInvoke(() =>
        {
            _updateUrl = update.Url;
            _tray.ShowBalloonTip(10_000, Strings.AppName, Strings.UpdateAvailable(update.Version), ToolTipIcon.Info);
        });
    }

    // ---------- delete all data ----------

    private async Task DeleteAllDataAsync()
    {
        var answer = MessageBox.Show(Strings.DeleteConfirm, Strings.DeleteTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        _openForm?.Close();
        var result = await DataReset.RunAsync(_oauth, _tokenStore, _server.Pairings, _server.Images,
            AppPaths.DataDir, AppPaths.LogDir, _cts.Token);
        _settings.ClientId = null;
        _settings.UpdateCheck = false;
        _updateItem.Checked = false;
        _session.Restart();

        var text = Strings.DeleteDone + (result.HadSignIn && !result.Revoked ? Strings.DeleteRevokeFailed : "");
        MessageBox.Show(text, Strings.DeleteTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ---------- helpers ----------

    private void ShowForm(Form form)
    {
        if (_openForm != null && !_openForm.IsDisposed)
        {
            _openForm.Activate();
            form.Dispose();
            return;
        }
        _openForm = form;
        form.FormClosed += (_, _) => _openForm = null;
        form.Show();
        form.Activate();
    }

    private static void OpenLog()
    {
        var file = Log.CurrentFile;
        if (file != null && File.Exists(file))
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo(AppPaths.LogDir) { UseShellExecute = true });
    }

    private void Exit()
    {
        Log.Info("Hub wird beendet.");
        _cts.Cancel();
        _tray.Visible = false;
        _tray.Dispose();
        ExitThread();
    }

    // ---------- IPairingPrompt ----------

    public Task<bool> AskAsync(string origin, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.BeginInvoke(() =>
        {
            var dialog = new PairingDialog(origin);
            dialog.Result.ContinueWith(t => tcs.TrySetResult(t.Result), TaskScheduler.Default);
            ct.Register(() => _ui.BeginInvoke(() => dialog.Finish(false)));
            dialog.Show();
            System.Media.SystemSounds.Asterisk.Play();
        });
        return tcs.Task;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts.Cancel();
            _tray.Dispose();
            _ui.Dispose();
            _http.Dispose();
        }
        base.Dispose(disposing);
    }
}
