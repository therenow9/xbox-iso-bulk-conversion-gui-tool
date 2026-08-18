using System.Diagnostics;
using System.Text;

namespace XisoConverterGui;

public sealed class RunOptions
{
    public required string ScriptPath { get; init; }
    public required string ExtractXiso { get; init; }
    public required string Source { get; init; }
    public required string Output { get; init; }
    public bool SkipSystemUpdate { get; init; }
    public bool Force { get; init; }
    public bool DryRun { get; init; }

    /// <summary>
    /// Attempt every image regardless of its media signature. Set when the user has
    /// deliberately ticked a row the script called NotXbox, so their choice wins.
    /// </summary>
    public bool NoMediaCheck { get; init; }

    /// <summary>
    /// Filenames to restrict the run to, or null for "everything in the source folder".
    /// Passed to the script as -IncludeFile, backing the grid's per-row checkboxes.
    /// </summary>
    public IReadOnlyCollection<string>? Include { get; init; }
}

public sealed class RunResult
{
    public int ExitCode { get; init; }
    public bool Cancelled { get; init; }
    public string StdErr { get; init; } = "";
    public string? LaunchError { get; init; }
}

/// <summary>
/// Owns the convert-xiso.ps1 child process and turns its stdout into events.
/// Events are raised on a background thread - the caller marshals them to the UI.
/// </summary>
public static class ConverterRunner
{
    /// <summary>
    /// The script is launched through -Command rather than -File so that
    /// [Console]::OutputEncoding can be forced to UTF-8 first. Windows PowerShell 5.1
    /// otherwise transcodes redirected stdout through the OEM code page and replaces
    /// every non-ASCII character with '?', which quietly corrupts titles like
    /// "Pokemon Cafe" with real accents in them. Arguments travel in environment
    /// variables so no quoting or escaping can mangle a path.
    /// </summary>
    private const string Bootstrap =
        "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); " +
        "& $env:XC_SCRIPT -Json -ExtractXiso $env:XC_TOOL -Source $env:XC_SOURCE -Output $env:XC_OUTPUT";

    public static string PowerShellPath { get; } = ResolvePowerShell();

    private static string ResolvePowerShell()
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var path = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
        return File.Exists(path) ? path : "powershell.exe";
    }

    public static async Task<RunResult> RunAsync(
        RunOptions options, Action<ConverterEvent> onEvent, CancellationToken cancellationToken)
    {
        string? includeFile = null;
        try
        {
            var command = new StringBuilder(Bootstrap);
            if (options.SkipSystemUpdate) command.Append(" -SkipSystemUpdate");
            if (options.Force) command.Append(" -Force");
            if (options.DryRun) command.Append(" -DryRun");
            if (options.NoMediaCheck) command.Append(" -NoMediaCheck");

            if (options.Include is { Count: > 0 })
            {
                includeFile = Path.Combine(Path.GetTempPath(), "XisoConverter-include-" + Guid.NewGuid().ToString("N") + ".txt");
                await File.WriteAllLinesAsync(includeFile, options.Include, new UTF8Encoding(true), CancellationToken.None);
                command.Append(" -IncludeFile $env:XC_INCLUDE");
            }

            command.Append("; exit ([int]$LASTEXITCODE)");

            var psi = new ProcessStartInfo
            {
                FileName = PowerShellPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(options.ScriptPath) ?? Environment.CurrentDirectory,
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(command.ToString());

            psi.Environment["XC_SCRIPT"] = options.ScriptPath;
            psi.Environment["XC_TOOL"] = options.ExtractXiso;
            psi.Environment["XC_SOURCE"] = options.Source;
            psi.Environment["XC_OUTPUT"] = options.Output;
            if (includeFile is not null) psi.Environment["XC_INCLUDE"] = includeFile;

            using var process = new Process { StartInfo = psi };

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                return new RunResult { ExitCode = -1, LaunchError = ex.Message };
            }

            var stdErr = new StringBuilder();

            // Read a line at a time so the UI updates as the conversion runs rather
            // than all at once when it finishes.
            var readOut = Task.Run(async () =>
            {
                while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    if (line.Length == 0) continue;
                    onEvent(ConverterEvent.Parse(line));
                }
            }, CancellationToken.None);

            var readErr = Task.Run(async () =>
            {
                while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    if (stdErr.Length < 8192) stdErr.AppendLine(line);
                }
            }, CancellationToken.None);

            var cancelled = false;
            await using (cancellationToken.Register(() =>
            {
                cancelled = true;
                try { process.Kill(entireProcessTree: true); }
                catch { /* already gone */ }
            }).ConfigureAwait(false))
            {
                await Task.WhenAll(readOut, readErr).ConfigureAwait(false);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            return new RunResult
            {
                ExitCode = process.ExitCode,
                Cancelled = cancelled,
                StdErr = stdErr.ToString().Trim(),
            };
        }
        finally
        {
            if (includeFile is not null)
            {
                try { File.Delete(includeFile); } catch { /* temp file, not worth reporting */ }
            }
        }
    }

    /// <summary>
    /// Locates convert-xiso.ps1. It is copied next to the .exe on build; walking up
    /// the tree as well means the app also runs straight out of bin\Debug during
    /// development, where the real script lives a few folders up.
    /// </summary>
    public static string? FindScript() => FindNearby("convert-xiso.ps1");

    /// <summary>
    /// Finds a bundled extract-xiso, so a release that ships one needs no setup at
    /// all. Returns null when the user is expected to supply their own.
    /// </summary>
    public static string? FindBundledExtractXiso()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "extract-xiso", "extract-xiso.exe");
        if (File.Exists(beside)) return beside;

        // Source-tree layout, so a development build behaves like a bundled release.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "third-party", "extract-xiso", "extract-xiso.exe");
            if (File.Exists(candidate)) return candidate;
        }

        return FindNearby("extract-xiso.exe");
    }

    private static string? FindNearby(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
