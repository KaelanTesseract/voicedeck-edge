using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace VoicedeckEdge.Hub.Tray;

internal enum TrayState { Ok, Waiting, Problem }

/// <summary>Draws the tray icon at runtime: a "V" badge with a coloured status dot.</summary>
internal static class TrayIcons
{
    private static readonly Dictionary<TrayState, Icon> Cache = [];

    public static Icon Get(TrayState state)
    {
        if (Cache.TryGetValue(state, out var icon)) return icon;
        var dot = state switch
        {
            TrayState.Ok => Color.FromArgb(35, 165, 90),
            TrayState.Waiting => Color.FromArgb(240, 178, 50),
            _ => Color.FromArgb(218, 55, 60),
        };

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var badge = RoundedRect(new Rectangle(1, 1, 30, 30), 8);
            using var fill = new SolidBrush(Color.FromArgb(88, 101, 242));
            g.FillPath(fill, badge);
            using var font = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("V", font, Brushes.White, new RectangleF(0, 0, 30, 31), format);
            using var dotBrush = new SolidBrush(dot);
            using var ring = new Pen(Color.FromArgb(30, 31, 34), 2);
            g.FillEllipse(dotBrush, 19, 19, 12, 12);
            g.DrawEllipse(ring, 19, 19, 12, 12);
        }
        icon = Icon.FromHandle(bmp.GetHicon());
        Cache[state] = icon;
        return icon;
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
