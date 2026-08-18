using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace XisoConverterGui;

public sealed class SummaryInfo
{
    public bool DryRun { get; init; }
    public bool Cancelled { get; init; }
    public int Extracted { get; init; }
    public int Skipped { get; init; }
    public int Failed { get; init; }
    public int Todo { get; init; }
    public int NotXbox { get; init; }
    public double Seconds { get; init; }
    public string? LogPath { get; init; }
    public IReadOnlyList<GameRow> Failures { get; init; } = Array.Empty<GameRow>();
    public IReadOnlyList<GameRow> NotXboxImages { get; init; } = Array.Empty<GameRow>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public bool HasDetails => Failures.Count > 0 || NotXboxImages.Count > 0 || Warnings.Count > 0;
}

/// <summary>The end-of-run report: tallies, anything that failed, and a link to the log.</summary>
public sealed class SummaryDialog : Form
{
    public SummaryDialog(SummaryInfo info)
    {
        Text = info.Cancelled ? "Run cancelled" : info.DryRun ? "Scan complete" : "Conversion complete";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        MinimumSize = new Size(460, 220);
        ClientSize = new Size(560, info.HasDetails ? 400 : 200);
        Padding = new Padding(16);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildHeadline(info), 0, 0);
        root.Controls.Add(BuildDetails(info), 0, 1);
        root.Controls.Add(BuildButtons(info), 0, 2);

        Controls.Add(root);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (AcceptButton is Control button) ActiveControl = button;
    }

    private static Control BuildHeadline(SummaryInfo info)
    {
        var text = new StringBuilder();
        if (info.DryRun)
        {
            text.Append($"{info.Todo} to extract   ·   {info.Skipped} already done");
        }
        else
        {
            text.Append($"{info.Extracted} extracted   ·   {info.Skipped} skipped   ·   {info.Failed} failed");
        }
        if (info.NotXbox > 0) text.Append($"   ·   {info.NotXbox} not Xbox");
        text.Append("\r\nElapsed ").Append(Humanize.Duration(info.Seconds));
        if (info.Cancelled) text.Append("   ·   cancelled part-way through");

        return new Label
        {
            Text = text.ToString(),
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = 52,
            Font = new Font("Segoe UI", 10.5f),
            ForeColor = info.Failed > 0 ? Palette.Error : info.Cancelled ? Palette.Note : Palette.Ok,
            Margin = new Padding(0, 0, 0, 10),
        };
    }

    private static Control BuildDetails(SummaryInfo info)
    {
        var lines = new StringBuilder();

        foreach (var warning in info.Warnings) lines.AppendLine("! " + warning);
        if (info.Warnings.Count > 0 && info.HasDetails) lines.AppendLine();

        if (info.NotXboxImages.Count > 0)
        {
            lines.AppendLine("Left alone - no Xbox media signature found:");
            lines.AppendLine();
            foreach (var image in info.NotXboxImages) lines.AppendLine("  " + image.Iso);
            lines.AppendLine();
            lines.AppendLine("These are almost certainly images for another console. If one really");
            lines.AppendLine("is an Xbox game, tick it in the list and convert again - that turns");
            lines.AppendLine("the check off for the run.");
            if (info.Failures.Count > 0) lines.AppendLine();
        }

        if (info.Failures.Count > 0)
        {
            lines.AppendLine("These images failed:");
            lines.AppendLine();
            foreach (var failure in info.Failures)
            {
                lines.AppendLine("  " + failure.Iso);
                if (!string.IsNullOrWhiteSpace(failure.Detail)) lines.AppendLine("      " + failure.Detail);
            }
            lines.AppendLine();
            lines.AppendLine("Usual causes: an incomplete download, a redump-style full dump that");
            lines.AppendLine("needs rebuilding first, or the drive running out of space.");
        }

        if (lines.Length == 0) return new Panel { Dock = DockStyle.Fill, Height = 0 };

        return new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 9f),
            Text = lines.ToString().TrimEnd(),
            Margin = new Padding(0, 0, 0, 12),
            // Without this the box takes focus on open and shows every line selected.
            TabStop = false,
        };
    }

    private Control BuildButtons(SummaryInfo info)
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0),
        };

        var close = new Button
        {
            Text = "Close",
            AutoSize = true,
            Padding = new Padding(14, 3, 14, 3),
            DialogResult = DialogResult.OK,
        };
        flow.Controls.Add(close);
        AcceptButton = close;
        CancelButton = close;

        if (!string.IsNullOrWhiteSpace(info.LogPath) && File.Exists(info.LogPath))
        {
            var open = new Button
            {
                Text = "Open log file",
                AutoSize = true,
                Padding = new Padding(14, 3, 14, 3),
                Margin = new Padding(8, 3, 8, 3),
            };
            open.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(info.LogPath!) { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open the log", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            flow.Controls.Add(open);
        }

        return flow;
    }
}
