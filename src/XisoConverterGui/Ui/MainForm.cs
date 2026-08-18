using System.Collections.Concurrent;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace XisoConverterGui;

public sealed partial class MainForm : Form
{
    private enum RunMode { Scan, Convert }

    private readonly AppSettings _settings;
    private readonly Dictionary<string, DataGridViewRow> _rowByIso = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _logQueue = new();
    private readonly System.Windows.Forms.Timer _logTimer;
    private readonly Font _boldFont;
    private readonly SourceWatcher _watcher = new();

    private string? _scriptPath;
    private CancellationTokenSource? _cts;
    private bool _busy;
    private RunMode _mode = RunMode.Scan;
    private bool _suspendCheckTracking;
    private bool _rescanWhenIdle;

    private int _tallyOk, _tallySkipped, _tallyFailed, _tallyNotXbox;
    private ConverterEvent? _summary;
    private ConverterEvent? _error;
    private readonly List<string> _warnings = new();

    private const int MaxLogChars = 400_000;

    public MainForm()
    {
        _settings = AppSettings.Load();

        BuildUi();
        _boldFont = new Font(_grid.Font, FontStyle.Bold);

        _logTimer = new System.Windows.Forms.Timer { Interval = 150 };
        _logTimer.Tick += (_, _) => DrainLog();

        WireEvents();
        ApplySettings();
        LocateScript();
        ValidatePaths();
        UpdateSelectionLabel();
    }

    private void WireEvents()
    {
        _btnRefresh.Click += async (_, _) => await RefreshListAsync();
        _btnConvert.Click += async (_, _) => await ConvertAsync();
        _btnCancel.Click += (_, _) => RequestCancel();

        _chkShowLog.CheckedChanged += (_, _) => _split.Panel2Collapsed = !_chkShowLog.Checked;

        // Force changes what a scan reports - everything becomes ToDo - so the list
        // should show that straight away rather than after a manual refresh.
        _chkForce.CheckedChanged += async (_, _) =>
        {
            if (!_busy && _scriptPath is not null && PathsUsable()) await RefreshListAsync();
        };

        _grid.CellFormatting += OnCellFormatting;
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += OnCellValueChanged;

        // Space toggles the checkbox on every selected row - handy after a sort.
        _grid.KeyDown += OnGridKeyDown;

        // Raised on a timer thread once the source folder has gone quiet.
        _watcher.Settled += OnSourceSettled;

        FormClosing += OnFormClosing;
    }

    // --------------------------------------------------------------- settings --

    private void ApplySettings()
    {
        // A release that bundles extract-xiso should need no setup, so the tool path
        // fills itself in - but only when the user has not chosen one that still
        // exists. Their choice always wins over the bundled copy.
        var tool = _settings.ExtractXisoPath;
        if (!File.Exists(tool)) tool = ConverterRunner.FindBundledExtractXiso() ?? tool;

        SetPath(_txtTool, tool);
        SetPath(_txtSource, _settings.SourceFolder);
        SetPath(_txtOutput, _settings.OutputFolder);

        _chkSkipSystemUpdate.Checked = _settings.SkipSystemUpdate;
        _chkForce.Checked = _settings.ForceReextract;
        _chkDryRun.Checked = _settings.DryRun;
        _chkShowLog.Checked = _settings.ShowLog;
        _split.Panel2Collapsed = !_settings.ShowLog;

        var size = new Size(
            Math.Max(MinimumSize.Width, _settings.WindowWidth),
            Math.Max(MinimumSize.Height, _settings.WindowHeight));

        var location = new Point(_settings.WindowX, _settings.WindowY);
        if (_settings.WindowX == int.MinValue || !IsOnAScreen(new Rectangle(location, size)))
        {
            StartPosition = FormStartPosition.CenterScreen;
            Size = size;
        }
        else
        {
            Location = location;
            Size = size;
        }

        if (_settings.Maximized) WindowState = FormWindowState.Maximized;
    }

    private static bool IsOnAScreen(Rectangle bounds) =>
        Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds));

    /// <summary>
    /// Fills a path box and leaves it scrolled to the start - assigning Text alone
    /// parks a long path at its far end, which reads as a truncated mess.
    /// </summary>
    private void SetPath(TextBox box, string value)
    {
        box.Text = value;
        box.SelectionStart = 0;
        box.SelectionLength = 0;
        _tips.SetToolTip(box, value);
    }

    private void CaptureSettings()
    {
        _settings.ExtractXisoPath = _txtTool.Text;
        _settings.SourceFolder = _txtSource.Text;
        _settings.OutputFolder = _txtOutput.Text;

        _settings.SkipSystemUpdate = _chkSkipSystemUpdate.Checked;
        _settings.ForceReextract = _chkForce.Checked;
        _settings.DryRun = _chkDryRun.Checked;
        _settings.ShowLog = _chkShowLog.Checked;
        if (!_split.Panel2Collapsed) _settings.LogSplitterDistance = _split.SplitterDistance;

        _settings.Maximized = WindowState == FormWindowState.Maximized;
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.WindowWidth = bounds.Width;
        _settings.WindowHeight = bounds.Height;
        _settings.WindowX = bounds.X;
        _settings.WindowY = bounds.Y;
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // Applied here rather than in ApplySettings: SplitterDistance is clamped to
        // the control's real height, which is not known until the form is laid out.
        if (_settings.LogSplitterDistance > 0 && !_split.Panel2Collapsed)
        {
            var max = _split.Height - _split.Panel2MinSize - _split.SplitterWidth;
            if (max > _split.Panel1MinSize)
                _split.SplitterDistance = Math.Clamp(_settings.LogSplitterDistance, _split.Panel1MinSize, max);
        }

        _logTimer.Start();

        if (_scriptPath is null)
        {
            MessageBox.Show(this,
                "convert-xiso.ps1 was not found next to XisoConverter.exe.\r\n\r\n" +
                "This app is only a front-end: the script does the actual conversion, so " +
                "put it in the same folder as the .exe and restart.",
                "Engine script missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Everything is already configured from last time, so show the user their
        // library rather than an empty grid.
        if (PathsUsable()) await RefreshListAsync();
        StartWatching();
    }

    // ----------------------------------------------------------------- browse --

    private async Task BrowseForTool()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Locate extract-xiso.exe",
            Filter = "extract-xiso.exe|extract-xiso.exe|Programs (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (File.Exists(_txtTool.Text)) dialog.FileName = _txtTool.Text;
        else if (Directory.Exists(_txtTool.Text)) dialog.InitialDirectory = _txtTool.Text;

        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await ApplyPathChange(_txtTool, dialog.FileName);
    }

    private async Task BrowseForFolder(TextBox target, string description)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };
        if (Directory.Exists(target.Text)) dialog.SelectedPath = target.Text;

        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await ApplyPathChange(target, dialog.SelectedPath);
    }

    /// <summary>
    /// Applies a newly chosen path and re-scans. Picking a different source folder and
    /// still being shown the old folder's games is just wrong, and all three paths feed
    /// the scan: the tool runs it, the source is what gets listed, and the output is
    /// what decides which rows come back Skipped.
    /// </summary>
    private async Task ApplyPathChange(TextBox target, string value)
    {
        if (string.Equals(target.Text, value, StringComparison.OrdinalIgnoreCase))
        {
            ValidatePaths();
            return;
        }

        SetPath(target, value);
        ValidatePaths();

        if (ReferenceEquals(target, _txtSource)) StartWatching();
        if (!_busy && _scriptPath is not null && PathsUsable()) await RefreshListAsync();
    }

    /// <summary>
    /// Points the watcher at the current source folder and reports honestly when that
    /// is not possible, so nobody sits waiting for an automatic refresh that will
    /// never come.
    /// </summary>
    private void StartWatching()
    {
        _watcher.Watch(_txtSource.Text);

        var watching = _watcher.IsActive;
        _tips.SetToolTip(_btnRefresh, watching
            ? "Re-scan the source folder. New images are picked up automatically, so this is " +
              "mainly for when you have changed the output folder outside the app."
            : "Re-scan the source folder. This folder cannot be watched for changes, so new " +
              "images will not appear on their own - use this button.");

        if (!watching && Directory.Exists(_txtSource.Text))
        {
            QueueLog("This source folder cannot be watched for changes (common on network shares).");
            QueueLog("New images will not appear on their own - use Refresh list.");
        }
    }

    /// <summary>
    /// A new image finished landing in the source folder, so bring the list up to date
    /// without the user having to ask. Deferred while a conversion is running - the
    /// grid is being driven by that run's event stream and must not be rebuilt underneath it.
    /// </summary>
    private void OnSourceSettled()
    {
        if (IsDisposed || !IsHandleCreated) return;

        try
        {
            BeginInvoke(new Action(async () =>
            {
                if (_busy) { _rescanWhenIdle = true; return; }
                if (_scriptPath is null || !PathsUsable()) return;

                QueueLog("Source folder changed - refreshing the list.");
                await RefreshListAsync();
            }));
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    // ------------------------------------------------------------- validation --

    private bool PathsUsable() =>
        File.Exists(_txtTool.Text) && Directory.Exists(_txtSource.Text) && _txtOutput.Text.Length > 0;

    private void ValidatePaths()
    {
        SetWarning(_lblToolWarn,
            _txtTool.Text.Length == 0 ? "Not set" : File.Exists(_txtTool.Text) ? null : "File not found",
            isError: true);

        SetWarning(_lblSourceWarn,
            _txtSource.Text.Length == 0 ? "Not set" : Directory.Exists(_txtSource.Text) ? null : "Folder not found",
            isError: true);

        // The script creates the output folder itself, so a missing one is a note,
        // not an error.
        var outputMissing = _txtOutput.Text.Length > 0 && !Directory.Exists(_txtOutput.Text);
        SetWarning(_lblOutputWarn,
            _txtOutput.Text.Length == 0 ? "Not set" : outputMissing ? "Will be created" : null,
            isError: !outputMissing);

        UpdateButtons();
    }

    private void SetWarning(Label label, string? message, bool isError)
    {
        label.Text = message ?? "";
        label.ForeColor = isError ? Palette.Error : Palette.Note;
        _tips.SetToolTip(label, message ?? "");
    }

    private void LocateScript()
    {
        _scriptPath = ConverterRunner.FindScript();
        _lblScript.Text = _scriptPath is null ? "convert-xiso.ps1 NOT FOUND" : "Engine: " + Path.GetFileName(_scriptPath);
        _lblScript.ForeColor = _scriptPath is null ? Palette.Error : Palette.TextMuted;
        _lblScript.ToolTipText = _scriptPath ?? "";
    }

    private void UpdateButtons()
    {
        var ready = !_busy && _scriptPath is not null;
        _btnRefresh.Enabled = ready && PathsUsable();
        _btnConvert.Enabled = ready && PathsUsable() && _grid.Rows.Count > 0;
        _btnCancel.Enabled = _busy;
    }

    // -------------------------------------------------------------------- run --

    private async Task RefreshListAsync()
    {
        if (_busy || _scriptPath is null) return;
        if (!PathsUsable()) { ValidatePaths(); return; }

        _mode = RunMode.Scan;
        _lblCurrent.Text = "Scanning " + _txtSource.Text + " …";

        await RunAsync(new RunOptions
        {
            ScriptPath = _scriptPath,
            ExtractXiso = _txtTool.Text,
            Source = _txtSource.Text,
            Output = _txtOutput.Text,
            SkipSystemUpdate = _chkSkipSystemUpdate.Checked,
            Force = _chkForce.Checked,
            DryRun = true,
        });
    }

    private async Task ConvertAsync()
    {
        if (_busy || _scriptPath is null) return;
        if (!PathsUsable()) { ValidatePaths(); return; }

        var selected = SelectedRows().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Nothing is ticked, so there is nothing to convert.",
                "No images selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Ticking a row the scan called NotXbox is an explicit override, so the media
        // check is turned off for that run rather than the request being ignored.
        // Read before the statuses below are reset, or there is nothing left to see.
        var overriding = selected.Any(r => ((GameRow)r.Tag!).Status == GameStatus.NotXbox);

        foreach (var row in selected)
        {
            var game = (GameRow)row.Tag!;
            game.Status = GameStatus.Pending;
            game.Seconds = null;
            game.Detail = null;
            row.Cells[ColStatus].Value = GameStatus.Pending;
            row.Cells[ColTime].Value = null;
            StyleRow(row, game);
        }

        // Everything ticked means "just run the script"; a subset is handed over as
        // an -IncludeFile so the engine still decides what actually happens to each.
        var everything = selected.Count == _grid.Rows.Count;
        var include = everything ? null : selected.Select(r => ((GameRow)r.Tag!).Iso).ToList();

        if (overriding) QueueLog("Media check off for this run - a NotXbox image was ticked by hand.");

        _mode = RunMode.Convert;
        _lblCurrent.Text = _chkDryRun.Checked ? "Dry run …" : "Starting …";

        await RunAsync(new RunOptions
        {
            ScriptPath = _scriptPath,
            ExtractXiso = _txtTool.Text,
            Source = _txtSource.Text,
            Output = _txtOutput.Text,
            SkipSystemUpdate = _chkSkipSystemUpdate.Checked,
            Force = _chkForce.Checked,
            DryRun = _chkDryRun.Checked,
            NoMediaCheck = overriding,
            Include = include,
        });
    }

    private async Task RunAsync(RunOptions options)
    {
        _busy = true;
        _summary = null;
        _error = null;
        _warnings.Clear();
        _tallyOk = _tallySkipped = _tallyFailed = _tallyNotXbox = 0;
        UpdateTally();
        SetRunningState(true);

        _progress.Value = 0;
        _progress.Maximum = 1;

        QueueLog("");
        QueueLog($"=== {(options.DryRun ? "scan" : "convert")} : {DateTime.Now:HH:mm:ss} ===");

        _cts = new CancellationTokenSource();
        RunResult result;
        try
        {
            result = await ConverterRunner.RunAsync(options, OnRunnerEvent, _cts.Token);
        }
        catch (Exception ex)
        {
            result = new RunResult { ExitCode = -1, LaunchError = ex.Message };
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
        }

        DrainLog();

        // A scan is not an achievement, so the bar goes back to empty: a full bar
        // always means a conversion finished.
        if (_mode == RunMode.Scan) _progress.Value = 0;

        FinishRun(options, result);

        _busy = false;
        SetRunningState(false);
        UpdateButtons();
        UpdateSelectionLabel();

        // Images that arrived mid-run were held back until now.
        if (_rescanWhenIdle && _mode == RunMode.Convert && _scriptPath is not null && PathsUsable())
        {
            _rescanWhenIdle = false;
            QueueLog("Source folder changed during the run - refreshing the list.");
            await RefreshListAsync();
        }
        _rescanWhenIdle = false;
    }

    private void FinishRun(RunOptions options, RunResult result)
    {
        if (result.LaunchError is not null)
        {
            _lblCurrent.Text = "Could not start PowerShell.";
            MessageBox.Show(this,
                "Could not start the conversion script:\r\n\r\n" + result.LaunchError,
                "Launch failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // A cancelled run leaves the game it was working on half-extracted in a
        // .partial folder. The script's design means it can never look finished, but
        // clearing it up is the GUI's job.
        if (result.Cancelled)
        {
            foreach (var row in _grid.Rows.Cast<DataGridViewRow>())
            {
                if (row.Tag is GameRow { Status: GameStatus.Working } game)
                {
                    game.Status = GameStatus.Cancelled;
                    row.Cells[ColStatus].Value = GameStatus.Cancelled;
                    StyleRow(row, game);
                }
            }

            var removed = CleanPartials(options.Output);
            QueueLog($"Cancelled. Removed {removed} .partial folder(s).");
            _lblCurrent.Text = $"Cancelled. Removed {removed} unfinished .partial folder(s).";
            DrainLog();
        }
        else if (_error is not null)
        {
            _lblCurrent.Text = _error.Text ?? "The script reported a fatal error.";
            MessageBox.Show(this, _error.Text ?? "The script reported a fatal error.",
                _error.Code switch
                {
                    "tool-missing" => "extract-xiso.exe not found",
                    "source-missing" => "Source folder not found",
                    _ => "Preflight failed",
                },
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        else if (result.ExitCode != 0 && result.ExitCode != 2 && _summary is null)
        {
            var detail = result.StdErr.Length > 0 ? "\r\n\r\n" + result.StdErr : "";
            _lblCurrent.Text = "The script exited with code " + result.ExitCode + ".";
            MessageBox.Show(this,
                "The script exited with code " + result.ExitCode + " without reporting a summary." + detail,
                "Run failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        else
        {
            _lblCurrent.Text = _mode == RunMode.Scan
                ? $"{_grid.Rows.Count} image(s) in the source folder."
                : "Done.";
        }

        if (_summary is null) return;

        if (_mode == RunMode.Scan)
        {
            // A scan is background housekeeping - no dialog, just the grid and tally.
            UpdateSelectionLabel();
            return;
        }

        var games = _grid.Rows.Cast<DataGridViewRow>().Select(r => r.Tag as GameRow).ToList();
        var failures = games.Where(g => g is { Status: GameStatus.Failed }).Select(g => g!).ToList();
        var notXbox = games.Where(g => g is { Status: GameStatus.NotXbox }).Select(g => g!).ToList();

        using var dialog = new SummaryDialog(new SummaryInfo
        {
            DryRun = options.DryRun,
            Cancelled = result.Cancelled,
            Extracted = _summary.Extracted,
            Skipped = _summary.Skipped,
            Failed = _summary.Failed,
            Todo = _summary.Todo,
            // Counted from the grid, not the summary event: an unticked NotXbox row
            // never reaches the script, so its count would read 0 while the list
            // below it named two files. Both now come from the same place.
            NotXbox = notXbox.Count,
            Seconds = _summary.Seconds,
            LogPath = _summary.Log,
            Failures = failures,
            NotXboxImages = notXbox,
            Warnings = _warnings,
        });
        dialog.ShowDialog(this);
    }

    private void RequestCancel()
    {
        if (_cts is null || _cts.IsCancellationRequested) return;
        _btnCancel.Enabled = false;
        _lblCurrent.Text = "Cancelling …";
        QueueLog("Cancel requested - stopping extract-xiso.");
        _cts.Cancel();
    }

    private static int CleanPartials(string output)
    {
        var removed = 0;
        try
        {
            foreach (var folder in Directory.EnumerateDirectories(output, "*.partial"))
            {
                try { Directory.Delete(folder, recursive: true); removed++; }
                catch { /* locked by a straggler process - the next run overwrites it */ }
            }
        }
        catch { /* output folder gone or unreadable */ }
        return removed;
    }

    private void SetRunningState(bool running)
    {
        foreach (var control in new Control[]
                 {
                     _txtTool, _txtSource, _txtOutput,
                     _btnToolBrowse, _btnSourceBrowse, _btnOutputBrowse,
                     _chkSkipSystemUpdate, _chkForce, _chkDryRun, _btnRefresh,
                 })
        {
            control.Enabled = !running;
        }

        _btnConvert.Enabled = !running;
        _btnCancel.Enabled = running;
        _grid.Columns[ColInclude].ReadOnly = running;
        _grid.ContextMenuStrip!.Enabled = !running;
    }

    // ----------------------------------------------------------------- events --

    /// <summary>
    /// Called on the process reader thread. Log text is queued and flushed by a timer
    /// so a chatty extract-xiso cannot bog the UI down; everything else is marshalled
    /// onto the UI thread one event at a time.
    /// </summary>
    private void OnRunnerEvent(ConverterEvent e)
    {
        var line = FormatForLog(e);
        if (line is not null) _logQueue.Enqueue(line);

        if (e.Event == "log") return;
        if (IsDisposed || !IsHandleCreated) return;

        try { BeginInvoke(new Action<ConverterEvent>(HandleEvent), e); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private static string? FormatForLog(ConverterEvent e) => e.Event switch
    {
        "log" => e.Text ?? "",
        "start" => $"Found {e.Total} image(s) in {e.Source}",
        "game-start" => $"[{e.Index}/{e.Total}] {e.Iso}  ->  {e.Folder}",
        "game-done" => e.Status switch
        {
            GameStatus.Failed => $"    FAILED: {e.Detail}",
            GameStatus.NotXbox => $"    NOT AN XBOX IMAGE: {e.Detail}",
            _ => $"    {e.Status}" + (e.Seconds > 0 ? $" in {Humanize.Duration(e.Seconds)}" : ""),
        },
        "warning" => "!  " + DescribeWarning(e),
        "error" => "!! " + e.Text,
        "summary" => $"=== extracted {e.Extracted}, skipped {e.Skipped}, failed {e.Failed}, todo {e.Todo}" +
                     (e.NotXbox > 0 ? $", not Xbox {e.NotXbox}" : "") + " ===",
        _ => null,
    };

    private static string DescribeWarning(ConverterEvent e) => e.Code switch
    {
        "low-space" => $"Low disk space: {Humanize.Size(e.FreeBytes)} free, source images total " +
                       $"{Humanize.Size(e.NeededBytes)}. Already-extracted games are skipped, so this may still be fine.",
        _ => e.Code ?? "unknown warning",
    };

    private void HandleEvent(ConverterEvent e)
    {
        switch (e.Event)
        {
            case "start":
                _progress.Maximum = Math.Max(1, e.Total);
                _progress.Value = 0;
                if (_mode == RunMode.Scan) ClearGrid();
                break;

            case "game-start":
                OnGameStart(e);
                break;

            case "game-done":
                OnGameDone(e);
                break;

            case "warning":
                _warnings.Add(DescribeWarning(e));
                break;

            case "error":
                _error = e;
                break;

            case "summary":
                _summary = e;
                _progress.Value = _progress.Maximum;
                break;
        }
    }

    private void OnGameStart(ConverterEvent e)
    {
        if (e.Iso is null) return;

        var row = GetOrCreateRow(e.Iso);
        var game = (GameRow)row.Tag!;
        game.Folder = e.Folder ?? "";
        game.Bytes = e.Bytes;
        row.Cells[ColIso].Value = game.Iso;
        row.Cells[ColSize].Value = game.Bytes;
        row.Cells[ColFolder].Value = game.Folder;

        if (_mode == RunMode.Convert)
        {
            game.Status = GameStatus.Working;
            row.Cells[ColStatus].Value = GameStatus.Working;
            _lblCurrent.Text = $"[{e.Index}/{e.Total}]  {game.Iso}   →   {game.Folder}";
            ScrollTo(row);
        }

        StyleRow(row, game);

        if (_progress.Maximum < e.Total) _progress.Maximum = Math.Max(1, e.Total);
        _progress.Value = Math.Clamp(e.Index - 1, 0, _progress.Maximum);
    }

    private void OnGameDone(ConverterEvent e)
    {
        if (e.Iso is null) return;

        var row = GetOrCreateRow(e.Iso);
        var game = (GameRow)row.Tag!;
        game.Status = e.Status ?? GameStatus.Pending;
        game.Seconds = e.Seconds > 0 ? e.Seconds : null;
        game.Detail = string.IsNullOrWhiteSpace(e.Detail) ? null : e.Detail;

        // A folder name for an image that will never be extracted is just noise, so
        // the column shows an em dash instead of a destination that cannot happen.
        game.Folder = game.Status == GameStatus.NotXbox ? "" : e.Folder ?? game.Folder;

        row.Cells[ColFolder].Value = game.Status == GameStatus.NotXbox ? "—" : game.Folder;
        row.Cells[ColStatus].Value = game.Status;
        row.Cells[ColTime].Value = game.Seconds;
        StyleRow(row, game);

        if (_mode == RunMode.Scan)
        {
            _suspendCheckTracking = true;
            row.Cells[ColInclude].Value = DefaultCheckedFor(game);
            _suspendCheckTracking = false;
        }
        else
        {
            switch (game.Status)
            {
                case GameStatus.Ok: _tallyOk++; break;
                case GameStatus.Skipped: _tallySkipped++; break;
                case GameStatus.Failed: _tallyFailed++; break;
                case GameStatus.NotXbox: _tallyNotXbox++; break;
            }
            UpdateTally();
        }

        _progress.Value = Math.Clamp(e.Index, 0, _progress.Maximum);
        UpdateSelectionLabel();
    }

    private bool DefaultCheckedFor(GameRow game)
    {
        // New downloads are what the user came for, so those tick by default - but a
        // deliberate tick or untick from a previous session wins.
        if (_settings.CheckedIsos.Contains(game.Iso, StringComparer.OrdinalIgnoreCase)) return true;
        if (_settings.UncheckedIsos.Contains(game.Iso, StringComparer.OrdinalIgnoreCase)) return false;
        return game.Status == GameStatus.ToDo;
    }

    // ------------------------------------------------------------------- grid --

    private void ClearGrid()
    {
        _grid.Rows.Clear();
        _rowByIso.Clear();
    }

    /// <summary>Keeps the game currently being extracted in view.</summary>
    private void ScrollTo(DataGridViewRow row)
    {
        try
        {
            if (row.Index >= 0 && _grid.Rows.Count > 0)
                _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, row.Index - 3);
        }
        catch (InvalidOperationException)
        {
            // Grid is mid-layout or the row is not displayable yet; scrolling is cosmetic.
        }
    }

    private DataGridViewRow GetOrCreateRow(string iso)
    {
        if (_rowByIso.TryGetValue(iso, out var existing)) return existing;

        var index = _grid.Rows.Add();
        var row = _grid.Rows[index];
        var game = new GameRow { Iso = iso };
        row.Tag = game;

        _suspendCheckTracking = true;
        row.Cells[ColInclude].Value = false;
        _suspendCheckTracking = false;
        row.Cells[ColIso].Value = iso;
        row.Cells[ColStatus].Value = game.Status;

        _rowByIso[iso] = row;
        return row;
    }

    private void StyleRow(DataGridViewRow row, GameRow game)
    {
        row.DefaultCellStyle.BackColor = Palette.RowBack(game.Status);
        row.DefaultCellStyle.ForeColor = Palette.RowFore(game.Status);

        var folder = row.Cells[ColFolder];
        if (game.Renamed && game.Folder.Length > 0)
        {
            folder.Style.ForeColor = Palette.Renamed;
            folder.Style.Font = _boldFont;
            folder.ToolTipText =
                "Renamed to survive FATX:\r\n" + GameRow.IsoBaseName(game.Iso) + "\r\n→ " + game.Folder;
        }
        else
        {
            folder.Style.ForeColor = Color.Empty;
            folder.Style.Font = null;
            folder.ToolTipText = "";
        }

        row.Cells[ColStatus].ToolTipText = game.Detail ?? "";
    }

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

        switch (_grid.Columns[e.ColumnIndex].Name)
        {
            case ColSize when e.Value is long bytes:
                e.Value = Humanize.Size(bytes);
                e.FormattingApplied = true;
                break;
            case ColTime when e.Value is double seconds:
                e.Value = seconds > 0 ? Humanize.Duration(seconds) : "";
                e.FormattingApplied = true;
                break;
        }
    }

    private void OnCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_grid.Columns[e.ColumnIndex].Name != ColInclude) return;
        if (_suspendCheckTracking) return;

        if (_grid.Rows[e.RowIndex].Tag is GameRow game)
        {
            RememberCheck(game, IsChecked(_grid.Rows[e.RowIndex]));
        }
        UpdateSelectionLabel();
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Space || _grid.SelectedRows.Count < 2) return;
        if (_grid.Columns[ColInclude].ReadOnly) return;

        var target = !IsChecked(_grid.SelectedRows[0]);
        foreach (var row in _grid.SelectedRows.Cast<DataGridViewRow>()) SetChecked(row, target);
        e.Handled = true;
        UpdateSelectionLabel();
    }

    private void SetAllChecked(Func<GameRow, bool> predicate)
    {
        if (_grid.Columns[ColInclude].ReadOnly) return;
        foreach (var row in _grid.Rows.Cast<DataGridViewRow>())
        {
            if (row.Tag is GameRow game) SetChecked(row, predicate(game));
        }
        UpdateSelectionLabel();
    }

    private void SetChecked(DataGridViewRow row, bool value)
    {
        row.Cells[ColInclude].Value = value;
        if (row.Tag is GameRow game) RememberCheck(game, value);
    }

    private static bool IsChecked(DataGridViewRow row) => row.Cells[ColInclude].Value is true;

    private void RememberCheck(GameRow game, bool isChecked)
    {
        _settings.CheckedIsos.RemoveAll(n => string.Equals(n, game.Iso, StringComparison.OrdinalIgnoreCase));
        _settings.UncheckedIsos.RemoveAll(n => string.Equals(n, game.Iso, StringComparison.OrdinalIgnoreCase));

        if (isChecked == (game.Status == GameStatus.ToDo)) return;
        (isChecked ? _settings.CheckedIsos : _settings.UncheckedIsos).Add(game.Iso);
    }

    private IEnumerable<DataGridViewRow> SelectedRows() =>
        _grid.Rows.Cast<DataGridViewRow>().Where(r => r.Tag is GameRow && IsChecked(r));

    // -------------------------------------------------------------- log + bar --

    private void QueueLog(string line) => _logQueue.Enqueue(line);

    private void DrainLog()
    {
        if (_logQueue.IsEmpty) return;

        var batch = new StringBuilder();
        var count = 0;
        while (count < 4000 && _logQueue.TryDequeue(out var line))
        {
            batch.Append(line).Append("\r\n");
            count++;
        }
        AppendLog(batch.ToString());
    }

    private void AppendLog(string text)
    {
        if (text.Length == 0) return;

        if (_txtLog.TextLength + text.Length > MaxLogChars)
        {
            var kept = _txtLog.Text;
            var from = kept.Length / 2;
            var newline = kept.IndexOf('\n', from);
            _txtLog.Text = newline >= 0 ? kept[(newline + 1)..] : "";
        }

        _txtLog.AppendText(text);
    }

    private void UpdateTally()
    {
        var text = $"Extracted {_tallyOk}   ·   Skipped {_tallySkipped}   ·   Failed {_tallyFailed}";
        if (_tallyNotXbox > 0) text += $"   ·   Not Xbox {_tallyNotXbox}";
        _lblTally.Text = text;
    }

    private void UpdateSelectionLabel()
    {
        var rows = _grid.Rows.Cast<DataGridViewRow>().ToList();
        var total = rows.Count;
        var ticked = SelectedRows().Count();
        var renamed = rows.Count(r => r.Tag is GameRow { Renamed: true } g && g.Folder.Length > 0);
        var notXbox = rows.Count(r => r.Tag is GameRow { Status: GameStatus.NotXbox });

        if (total == 0)
        {
            _lblSelection.Text = "No images listed";
            return;
        }

        var text = $"{ticked} of {total} selected";
        if (renamed > 0) text += $"   ·   {renamed} renamed for FATX";
        if (notXbox > 0) text += $"   ·   {notXbox} not Xbox";
        _lblSelection.Text = text;
    }

    // ---------------------------------------------------------------- closing --

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_busy)
        {
            var answer = MessageBox.Show(this,
                "A conversion is still running. Stop it and close?\r\n\r\n" +
                "The game being extracted will be left unfinished; its .partial folder is removed.",
                "Still converting", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes) { e.Cancel = true; return; }

            _cts?.Cancel();
            CleanPartials(_txtOutput.Text);
        }

        _logTimer.Stop();
        _watcher.Dispose();
        CaptureSettings();
        _settings.Save();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _logTimer.Dispose();
            _watcher.Dispose();
            _boldFont.Dispose();
            _tips.Dispose();
            _cts?.Dispose();
        }
        base.Dispose(disposing);
    }
}
