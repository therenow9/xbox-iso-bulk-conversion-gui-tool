using System.Drawing;
using System.Windows.Forms;

namespace XisoConverterGui;

public sealed partial class MainForm
{
    private TextBox _txtTool = null!, _txtSource = null!, _txtOutput = null!;
    private Label _lblToolWarn = null!, _lblSourceWarn = null!, _lblOutputWarn = null!;
    private Button _btnToolBrowse = null!, _btnSourceBrowse = null!, _btnOutputBrowse = null!;

    private CheckBox _chkSkipSystemUpdate = null!, _chkForce = null!, _chkDryRun = null!;
    private Button _btnRefresh = null!;

    private DataGridView _grid = null!;
    private SplitContainer _split = null!;
    private TextBox _txtLog = null!;
    private CheckBox _chkShowLog = null!;

    private ProgressBar _progress = null!;
    private Label _lblCurrent = null!;
    private Button _btnConvert = null!, _btnCancel = null!;

    private StatusStrip _status = null!;
    private ToolStripStatusLabel _lblTally = null!, _lblSelection = null!, _lblScript = null!;

    private ToolTip _tips = null!;

    private const string ColInclude = "include";
    private const string ColIso = "iso";
    private const string ColSize = "size";
    private const string ColFolder = "folder";
    private const string ColStatus = "status";
    private const string ColTime = "time";

    private void BuildUi()
    {
        Text = "XISO Converter";
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.Manual;
        Icon = LoadAppIcon();
        _tips = new ToolTip { AutoPopDelay = 20000, InitialDelay = 400, ReshowDelay = 100 };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(0),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // paths
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // options
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // grid + log
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // progress + buttons
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // status strip

        root.Controls.Add(BuildPathsPanel(), 0, 0);
        root.Controls.Add(BuildOptionsPanel(), 0, 1);
        root.Controls.Add(BuildSplit(), 0, 2);
        root.Controls.Add(BuildBottomPanel(), 0, 3);
        root.Controls.Add(BuildStatusStrip(), 0, 4);

        Controls.Add(root);
    }

    // ------------------------------------------------------------------ paths --

    private Control BuildPathsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            RowCount = 3,
            Padding = new Padding(10, 10, 10, 4),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));

        AddPathRow(panel, 0, "extract-xiso.exe", out _txtTool, out _btnToolBrowse, out _lblToolWarn);
        AddPathRow(panel, 1, "Source folder", out _txtSource, out _btnSourceBrowse, out _lblSourceWarn);
        AddPathRow(panel, 2, "Output folder", out _txtOutput, out _btnOutputBrowse, out _lblOutputWarn);

        _btnToolBrowse.Click += (_, _) => BrowseForTool();
        _btnSourceBrowse.Click += (_, _) => BrowseForFolder(_txtSource, "Pick the folder holding your .iso images");
        _btnOutputBrowse.Click += (_, _) => BrowseForFolder(_txtOutput, "Pick the folder to extract game folders into");

        return panel;
    }

    private static void AddPathRow(TableLayoutPanel panel, int row, string caption,
        out TextBox box, out Button browse, out Label warn)
    {
        var label = new Label
        {
            Text = caption,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 7, 8, 3),
        };

        box = new TextBox
        {
            ReadOnly = true,
            Dock = DockStyle.Fill,
            BackColor = SystemColors.Window,
            Margin = new Padding(0, 3, 6, 3),
        };

        browse = new Button
        {
            Text = "Browse…",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 2, 8, 2),
            Padding = new Padding(8, 1, 8, 1),
        };

        warn = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = Palette.Error,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 3, 0, 3),
        };

        panel.Controls.Add(label, 0, row);
        panel.Controls.Add(box, 1, row);
        panel.Controls.Add(browse, 2, row);
        panel.Controls.Add(warn, 3, row);
    }

    // ---------------------------------------------------------------- options --

    private Control BuildOptionsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(10, 0, 10, 6),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0),
        };

        _chkSkipSystemUpdate = new CheckBox { Text = "Skip $SystemUpdate", AutoSize = true, Margin = new Padding(0, 4, 18, 4) };
        _chkForce = new CheckBox { Text = "Force re-extract", AutoSize = true, Margin = new Padding(0, 4, 18, 4) };
        _chkDryRun = new CheckBox { Text = "Dry run", AutoSize = true, Margin = new Padding(0, 4, 18, 4) };

        _tips.SetToolTip(_chkSkipSystemUpdate, "Passes -s to extract-xiso so the $SystemUpdate folder is left out.");
        _tips.SetToolTip(_chkForce, "Re-extract games that already have a default.xbe.");
        _tips.SetToolTip(_chkDryRun, "Convert does a dry run: reports what would happen and writes nothing.");

        flow.Controls.Add(_chkSkipSystemUpdate);
        flow.Controls.Add(_chkForce);
        flow.Controls.Add(_chkDryRun);

        _btnRefresh = new Button
        {
            Text = "Refresh list",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 1, 8, 1),
            Margin = new Padding(0),
        };
        _tips.SetToolTip(_btnRefresh, "Re-scan the source folder (runs the script with -DryRun).");

        panel.Controls.Add(flow, 0, 0);
        panel.Controls.Add(_btnRefresh, 1, 0);
        return panel;
    }

    // ------------------------------------------------------------- grid + log --

    private Control BuildSplit()
    {
        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            Margin = new Padding(10, 0, 10, 0),
            SplitterWidth = 6,
            Panel1MinSize = 160,
            Panel2MinSize = 80,
        };

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
            AutoGenerateColumns = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            EnableHeadersVisualStyles = false,
            StandardTab = true,
        };
        _grid.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(210, 226, 246);
        _grid.DefaultCellStyle.SelectionForeColor = Color.Black;
        _grid.RowTemplate.Height = 22;

        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = ColInclude,
            // U+FE0E keeps this a drawn glyph rather than an emoji, so the header does
            // not look like a select-all widget it isn't.
            HeaderText = "☑︎",
            Width = 34,
            Resizable = DataGridViewTriState.False,
            SortMode = DataGridViewColumnSortMode.Automatic,
            ToolTipText = "Include this image in the next run",
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = ColIso,
            HeaderText = "ISO",
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 44,
            SortMode = DataGridViewColumnSortMode.Automatic,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = ColSize,
            HeaderText = "Size",
            ReadOnly = true,
            Width = 92,
            ValueType = typeof(long),
            SortMode = DataGridViewColumnSortMode.Automatic,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight },
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = ColFolder,
            HeaderText = "→ Folder",
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 44,
            SortMode = DataGridViewColumnSortMode.Automatic,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = ColStatus,
            HeaderText = "Status",
            ReadOnly = true,
            Width = 82,
            SortMode = DataGridViewColumnSortMode.Automatic,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = ColTime,
            HeaderText = "Time",
            ReadOnly = true,
            Width = 74,
            ValueType = typeof(double),
            SortMode = DataGridViewColumnSortMode.Automatic,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight },
        });

        var menu = new ContextMenuStrip();
        menu.Items.Add("Select all", null, (_, _) => SetAllChecked(_ => true));
        menu.Items.Add("Select none", null, (_, _) => SetAllChecked(_ => false));
        menu.Items.Add("Select only what's new", null, (_, _) => SetAllChecked(r => r.Status == GameStatus.ToDo));
        _grid.ContextMenuStrip = menu;

        _txtLog = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 8.5f),
            BackColor = Palette.LogBack,
            ForeColor = Palette.LogFore,
            BorderStyle = BorderStyle.FixedSingle,
        };

        _split.Panel1.Controls.Add(_grid);
        _split.Panel2.Controls.Add(_txtLog);
        return _split;
    }

    // ----------------------------------------------------------------- bottom --

    private Control BuildBottomPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(10, 6, 10, 4),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _lblCurrent = new Label
        {
            Text = "Ready.",
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = 18,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 0, 4),
        };
        panel.Controls.Add(_lblCurrent, 0, 0);
        panel.SetColumnSpan(_lblCurrent, 2);

        _progress = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Height = 22,
            Minimum = 0,
            Maximum = 1,
            Margin = new Padding(0, 3, 12, 0),
        };
        panel.Controls.Add(_progress, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
        };

        _chkShowLog = new CheckBox
        {
            Text = "Show log",
            AutoSize = true,
            Checked = true,
            Margin = new Padding(0, 6, 14, 0),
        };
        _btnConvert = new Button
        {
            Text = "Convert",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14, 3, 14, 3),
            Margin = new Padding(0, 0, 8, 0),
        };
        _btnCancel = new Button
        {
            Text = "Cancel",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14, 3, 14, 3),
            Enabled = false,
            Margin = new Padding(0),
        };

        buttons.Controls.Add(_chkShowLog);
        buttons.Controls.Add(_btnConvert);
        buttons.Controls.Add(_btnCancel);
        panel.Controls.Add(buttons, 1, 1);

        return panel;
    }

    /// <summary>
    /// Form.Icon does not inherit the executable's icon, so it is loaded from the
    /// embedded copy. A missing resource just leaves the stock glyph in place.
    /// </summary>
    private static Icon? LoadAppIcon()
    {
        try
        {
            using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("XisoConverterGui.app.ico");
            return stream is null ? null : new Icon(stream);
        }
        catch
        {
            return null;
        }
    }

    private Control BuildStatusStrip()
    {
        _status = new StatusStrip { Dock = DockStyle.Fill, SizingGrip = true };
        _lblTally = new ToolStripStatusLabel("Extracted 0   ·   Skipped 0   ·   Failed 0");
        _lblSelection = new ToolStripStatusLabel("") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _lblScript = new ToolStripStatusLabel("") { ForeColor = Palette.TextMuted };
        _status.Items.Add(_lblTally);
        _status.Items.Add(new ToolStripSeparator());
        _status.Items.Add(_lblSelection);
        _status.Items.Add(_lblScript);
        return _status;
    }
}
