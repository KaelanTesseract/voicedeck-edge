using VoicedeckEdge.Hub.Infrastructure;
using VoicedeckEdge.Hub.Server;

namespace VoicedeckEdge.Hub.Tray;

/// <summary>Shared look for the hub's small windows.</summary>
internal abstract class HubForm : Form
{
    protected HubForm(string title)
    {
        Text = title;
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Icon = TrayIcons.Get(TrayState.Ok);
        Padding = new Padding(16);
    }

    protected static FlowLayoutPanel ButtonRow(params Button[] buttons)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(0, 12, 0, 0) };
        foreach (var b in buttons)
        {
            b.AutoSize = true;
            b.Padding = new Padding(10, 3, 10, 3);
            row.Controls.Add(b);
        }
        return row;
    }
}

/// <summary>"Allow widget?" – shown on top, denies by itself after 60 seconds.</summary>
internal sealed class PairingDialog : HubForm
{
    private readonly TaskCompletionSource<bool> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly System.Windows.Forms.Timer _timeout = new() { Interval = 60_000 };

    public Task<bool> Result => _result.Task;

    public PairingDialog(string origin) : base(Strings.PairTitle)
    {
        TopMost = true;
        ShowInTaskbar = true;
        ClientSize = new Size(460, 220);
        var text = new Label { Text = Strings.PairText(origin), Dock = DockStyle.Fill };
        var allow = new Button { Text = Strings.Allow };
        var deny = new Button { Text = Strings.Deny };
        allow.Click += (_, _) => Finish(true);
        deny.Click += (_, _) => Finish(false);
        CancelButton = deny;
        Controls.Add(text);
        Controls.Add(ButtonRow(allow, deny));

        _timeout.Tick += (_, _) => Finish(false);
        _timeout.Start();
        FormClosed += (_, _) => _result.TrySetResult(false);
    }

    public void Finish(bool allowed)
    {
        _timeout.Stop();
        _result.TrySetResult(allowed);
        if (!IsDisposed) Close();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
    }
}

/// <summary>Lists paired widgets and lets the user unpair them.</summary>
internal sealed class PairedWidgetsForm : HubForm
{
    public PairedWidgetsForm(PairingStore pairings) : base(Strings.PairedTitle)
    {
        ClientSize = new Size(520, 300);
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
        list.Columns.Add(Strings.PairedColumnDate, 170);
        list.Columns.Add(Strings.PairedColumnOrigin, 300);

        void Refill()
        {
            list.Items.Clear();
            foreach (var p in pairings.All)
                list.Items.Add(new ListViewItem([p.Created.ToLocalTime().ToString("g"), p.Origin]) { Tag = p.Id });
            if (list.Items.Count == 0)
                list.Items.Add(new ListViewItem([Strings.PairedEmpty, ""]) { ForeColor = SystemColors.GrayText });
        }

        var revoke = new Button { Text = Strings.PairedRevoke };
        var close = new Button { Text = Strings.Close, DialogResult = DialogResult.Cancel };
        revoke.Click += (_, _) =>
        {
            if (list.SelectedItems.Count == 1 && list.SelectedItems[0].Tag is string id)
            {
                pairings.Revoke(id);
                Refill();
            }
        };
        CancelButton = close;
        Refill();
        Controls.Add(list);
        Controls.Add(ButtonRow(close, revoke));
    }
}
