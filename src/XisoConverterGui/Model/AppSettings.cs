using System.Text.Json;
using System.Text.Json.Serialization;

namespace XisoConverterGui;

/// <summary>
/// %APPDATA%\XisoConverter\settings.json. Loading never throws: a missing, truncated
/// or hand-mangled file just yields defaults.
/// </summary>
public sealed class AppSettings
{
    public string ExtractXisoPath { get; set; } = "";
    public string SourceFolder { get; set; } = "";
    public string OutputFolder { get; set; } = "";

    public bool SkipSystemUpdate { get; set; } = true;
    public bool ForceReextract { get; set; }
    public bool DryRun { get; set; }

    public bool ShowLog { get; set; } = true;
    public int LogSplitterDistance { get; set; }

    public int WindowWidth { get; set; } = 1040;
    public int WindowHeight { get; set; } = 720;
    public int WindowX { get; set; } = int.MinValue;
    public int WindowY { get; set; } = int.MinValue;
    public bool Maximized { get; set; }

    /// <summary>
    /// Rows the user deliberately ticked or unticked, so a Refresh after new downloads
    /// keeps their choices instead of silently resetting to "everything new".
    /// Only deviations from the ToDo default are stored, and the lists are capped.
    /// </summary>
    public List<string> CheckedIsos { get; set; } = new();
    public List<string> UncheckedIsos { get; set; } = new();

    private const int MaxRemembered = 2000;

    [JsonIgnore]
    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XisoConverter");

    [JsonIgnore]
    public static string FilePath => Path.Combine(Directory, "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch
        {
            // A bad settings file is never a reason to fail to start.
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Trim(CheckedIsos);
            Trim(UncheckedIsos);
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch
        {
            // Losing preferences is not worth an error dialog on the way out.
        }
    }

    private static void Trim(List<string> list)
    {
        if (list.Count > MaxRemembered) list.RemoveRange(0, list.Count - MaxRemembered);
    }
}
