using System.ComponentModel;
using System.Diagnostics;
using VoicedeckEdge.Hub.Discord;
using VoicedeckEdge.Hub.Infrastructure;
using VoicedeckEdge.Hub.Server;

namespace VoicedeckEdge.Hub.Tray;

/// <summary>
/// First-run assistant: welcome → create Discord app (client ID) → connect → add widget.
/// Designed for people without technical background: one action per page, nothing to type
/// except pasting the client ID, every value has a copy button.
/// </summary>
internal sealed class SetupWizard : HubForm
{
    private const int ContentWidth = 540;
    private const string SuggestedAppName = "Voicedeck Edge";
    private const string PortalUrl = "https://discord.com/developers/applications";
    public static readonly string WidgetPackage = Path.Combine(AppContext.BaseDirectory, "VoicedeckEdge.icuewidget");

    private readonly HubSettings _settings;
    private readonly DiscordSession _session;
    private readonly Action<string> _saveClientId;
    private readonly CancellationTokenSource _cts = new();

    private readonly Label _heading = new() { AutoSize = true, Font = new Font("Segoe UI", 15f, FontStyle.Bold) };
    private readonly Label _stepLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    private readonly Button _back = new() { Text = Strings.Back };
    private readonly Button _next = new() { Text = Strings.Next };
    private readonly Control[] _pages;
    private readonly string[] _headings = [Strings.WelcomeHeading, Strings.AppHeading, Strings.ConnectHeading, Strings.WidgetHeading];
    private int _page;
    private readonly bool _afterUpdate; // opened by the hub after an update: straight to the widget page

    // page 2: app
    private readonly TextBox _clientId = new() { Width = 260, PlaceholderText = "Client-ID" };
    private readonly Label _clientIdHint = new() { AutoSize = true, MaximumSize = new Size(ContentWidth, 0) };

    // page 3: connect
    private readonly Label _connectStatus = new() { AutoSize = true, MaximumSize = new Size(ContentWidth, 0), Font = new Font("Segoe UI", 11f, FontStyle.Bold) };
    private readonly Button _retry = new() { Text = Strings.ConnectRetry, AutoSize = true, Visible = false };
    private bool _authAttempted;

    // page 4: widget
    private readonly Label _widgetHint = new() { AutoSize = true, MaximumSize = new Size(ContentWidth, 0), ForeColor = SystemColors.GrayText };
    private readonly CheckBox _autostart = new() { Text = Strings.OptionAutostart, AutoSize = true, MaximumSize = new Size(ContentWidth, 0) };
    // AutoSize check boxes clip instead of wrapping: give the long one room for two lines.
    private readonly CheckBox _updates = new()
    {
        Text = Strings.OptionUpdates, AutoSize = false, Size = new Size(ContentWidth, 44),
        CheckAlign = ContentAlignment.TopLeft, TextAlign = ContentAlignment.TopLeft,
    };

    /// <summary>Raised on "Finish" with (autostart, updateCheck).</summary>
    public event Action<bool, bool>? Completed;

    public SetupWizard(HubSettings settings, DiscordSession session, Action<string> saveClientId, bool afterUpdate = false)
        : base(Strings.SetupTitle)
    {
        _afterUpdate = afterUpdate;
        if (afterUpdate) _headings[^1] = Strings.UpdatedHeading(HubServer.HubVersion);
        _settings = settings;
        _session = session;
        _saveClientId = saveClientId;
        ClientSize = new Size(ContentWidth + 40, 470);

        var header = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Padding = new Padding(0, 0, 0, 12) };
        header.Controls.Add(_stepLabel);
        header.Controls.Add(_heading);

        var cancel = new Button { Text = Strings.Cancel, DialogResult = DialogResult.Cancel };
        _back.Click += (_, _) => GoTo(_page - 1);
        _next.Click += (_, _) => OnNext();
        CancelButton = cancel;

        _pages = [BuildWelcome(), BuildApp(), BuildConnect(), BuildWidget()];
        Controls.Add(_content);
        Controls.Add(header);
        Controls.Add(ButtonRow(_next, _back, cancel));

        _session.StatusChanged += OnSessionStatus;
        FormClosed += (_, _) =>
        {
            _session.StatusChanged -= OnSessionStatus;
            _cts.Cancel();
        };
        GoTo(afterUpdate ? _pages.Length - 1 : 0);
    }

    // ---------- pages ----------

    private static FlowLayoutPanel Page() => new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
    };

    private static Label Para(string text, bool gray = false) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(ContentWidth, 0),
        Margin = new Padding(0, 6, 0, 6),
        ForeColor = gray ? SystemColors.GrayText : SystemColors.ControlText,
    };

    /// <summary>Read-only value with a copy button – avoids typos in the Developer Portal.</summary>
    private static FlowLayoutPanel CopyRow(string value)
    {
        var box = new TextBox { Text = value, ReadOnly = true, Width = 260 };
        var copy = new Button { Text = Strings.Copy, AutoSize = true };
        copy.Click += (_, _) =>
        {
            Clipboard.SetText(value);
            copy.Text = Strings.Copied;
        };
        var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(16, 0, 0, 8) };
        row.Controls.Add(box);
        row.Controls.Add(copy);
        return row;
    }

    private Control BuildWelcome()
    {
        var page = Page();
        page.Controls.Add(Para(Strings.WelcomeText));
        page.Controls.Add(Para(Strings.PrivacyNote, gray: true));
        return page;
    }

    private Control BuildApp()
    {
        var page = Page();
        page.Controls.Add(Para(Strings.AppStep1));
        var portal = new Button { Text = Strings.OpenPortal, AutoSize = true, Margin = new Padding(16, 0, 0, 4) };
        portal.Click += (_, _) => OpenUrl(PortalUrl);
        page.Controls.Add(portal);
        page.Controls.Add(CopyRow(SuggestedAppName));
        page.Controls.Add(Para(Strings.AppStep2));
        page.Controls.Add(CopyRow(DiscordOAuth.RedirectUri));
        page.Controls.Add(Para(Strings.AppStep3));

        _clientId.Text = _settings.ClientId ?? "";
        _clientId.Margin = new Padding(16, 0, 0, 4);
        _clientId.TextChanged += (_, _) => ValidateClientId();
        _clientIdHint.Margin = new Padding(16, 0, 0, 0);
        page.Controls.Add(_clientId);
        page.Controls.Add(_clientIdHint);
        return page;
    }

    private Control BuildConnect()
    {
        var page = Page();
        page.Controls.Add(Para(Strings.ConnectText));
        _connectStatus.Margin = new Padding(0, 16, 0, 8);
        page.Controls.Add(_connectStatus);
        _retry.Click += (_, _) =>
        {
            _authAttempted = false;
            RefreshConnect();
        };
        page.Controls.Add(_retry);
        return page;
    }

    private Control BuildWidget()
    {
        var page = Page();
        if (_afterUpdate) page.Controls.Add(Para(Strings.UpdatedText));
        page.Controls.Add(Para(Strings.WidgetText));
        var install = new Button { Text = Strings.WidgetInstall, AutoSize = true, Margin = new Padding(0, 8, 0, 4) };
        install.Click += (_, _) => InstallWidget();
        page.Controls.Add(install);
        page.Controls.Add(_widgetHint);
        if (!File.Exists(WidgetPackage))
        {
            install.Enabled = false;
            _widgetHint.Text = Strings.WidgetMissing;
        }

        // Autostart is recommended (default on); the update check is opt-in (default off).
        _autostart.Checked = Autostart.IsEnabled() || !HubSettings.IsValidClientId(_settings.ClientId);
        _updates.Checked = _settings.UpdateCheck;
        _autostart.Margin = new Padding(0, 20, 0, 4);
        page.Controls.Add(_autostart);
        page.Controls.Add(_updates);
        return page;
    }

    // ---------- navigation ----------

    private void GoTo(int page)
    {
        _page = Math.Clamp(page, 0, _pages.Length - 1);
        _content.Controls.Clear();
        _content.Controls.Add(_pages[_page]);
        _heading.Text = _headings[_page];
        _stepLabel.Text = _afterUpdate ? "" : Strings.StepOf(_page + 1, _pages.Length);
        _back.Enabled = _page > 0;
        _next.Text = _page == _pages.Length - 1 ? Strings.Finish : Strings.Next;
        AcceptButton = _next;

        switch (_page)
        {
            case 1:
                ValidateClientId();
                _clientId.Focus();
                break;
            case 2:
                _authAttempted = false;
                RefreshConnect();
                break;
            default:
                _next.Enabled = true;
                break;
        }
    }

    private void OnNext()
    {
        switch (_page)
        {
            case 1:
                _saveClientId(_clientId.Text.Trim());
                GoTo(2);
                break;
            case 3:
                Completed?.Invoke(_autostart.Checked, _updates.Checked);
                DialogResult = DialogResult.OK;
                Close();
                break;
            default:
                GoTo(_page + 1);
                break;
        }
    }

    private void ValidateClientId()
    {
        var valid = HubSettings.IsValidClientId(_clientId.Text.Trim());
        _clientIdHint.Text = _clientId.Text.Length == 0 ? "" : valid ? Strings.ClientIdOk : Strings.ClientIdInvalid;
        _clientIdHint.ForeColor = valid ? Color.ForestGreen : Color.Firebrick;
        _next.Enabled = valid;
    }

    // ---------- connect page ----------

    private void OnSessionStatus(DiscordStatus _)
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(() => { if (_page == 2) RefreshConnect(); });
    }

    private void RefreshConnect()
    {
        var status = _session.Status;
        _retry.Visible = false;
        _connectStatus.ForeColor = SystemColors.ControlText;
        _next.Enabled = status == DiscordStatus.Ready;

        switch (status)
        {
            case DiscordStatus.Ready:
                _connectStatus.Text = Strings.ConnectDone;
                _connectStatus.ForeColor = Color.ForestGreen;
                break;
            case DiscordStatus.DiscordNotRunning:
                _connectStatus.Text = Strings.ConnectWaitingDiscord;
                break;
            case DiscordStatus.InvalidClientId:
                _connectStatus.Text = Strings.ConnectInvalidId;
                _connectStatus.ForeColor = Color.Firebrick;
                break;
            case DiscordStatus.Authorizing:
                _connectStatus.Text = Strings.ConnectAuthorizing;
                break;
            case DiscordStatus.Unauthorized when !_authAttempted:
                _authAttempted = true;
                _connectStatus.Text = Strings.ConnectAuthorizing;
                _ = _session.AuthorizeAsync(_cts.Token);
                break;
            case DiscordStatus.Unauthorized:
                _connectStatus.Text = Strings.ConnectFailed;
                _connectStatus.ForeColor = Color.Firebrick;
                _retry.Visible = true;
                break;
            default:
                _connectStatus.Text = Strings.ConnectConnecting;
                break;
        }
    }

    // ---------- widget page ----------

    private void InstallWidget()
    {
        try
        {
            Process.Start(new ProcessStartInfo(WidgetPackage) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            // No file association for .icuewidget: show the file so it can be imported by hand.
            _widgetHint.Text = Strings.WidgetOpenFailed + WidgetPackage;
            Process.Start("explorer.exe", $"/select,\"{WidgetPackage}\"");
        }
    }

    internal static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

#if DEBUG
    /// <summary>Documentation screenshots (debug builds only): renders one page to a PNG.</summary>
    internal void RenderPage(int page, string file)
    {
        GoTo(page);
        if (page == 2)
        {
            _connectStatus.Text = Strings.ConnectDone;
            _connectStatus.ForeColor = Color.ForestGreen;
            _next.Enabled = true;
        }
        if (page == 3) _widgetHint.Text = "";
        _clientId.Select(0, 0);
        ActiveControl = _next;
        Application.DoEvents();
        using var bmp = new Bitmap(Width, Height);
        DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
        bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
    }
#endif
}
