namespace XisoConverterGui;

public static class GameStatus
{
    public const string Pending = "Pending";
    public const string Working = "Working";
    public const string Ok = "OK";
    public const string Skipped = "Skipped";
    public const string Failed = "Failed";
    public const string ToDo = "ToDo";
    public const string Cancelled = "Cancelled";

    /// <summary>No MICROSOFT*XBOX*MEDIA signature - not an Original Xbox image.</summary>
    public const string NotXbox = "NotXbox";
}

/// <summary>One ISO in the source folder, as shown in the grid.</summary>
public sealed class GameRow
{
    public required string Iso { get; init; }
    public string Folder { get; set; } = "";
    public long Bytes { get; set; }
    public string Status { get; set; } = GameStatus.Pending;
    public double? Seconds { get; set; }
    public string? Detail { get; set; }

    /// <summary>
    /// True when the script had to change the name to make it FATX-safe. This is the
    /// reassurance the user is here for, so the grid highlights these rows.
    /// </summary>
    public bool Renamed => !string.Equals(Folder, IsoBaseName(Iso), StringComparison.Ordinal);

    /// <summary>
    /// The ISO filename with every trailing image extension removed - the same
    /// starting point convert-xiso.ps1 uses before it applies the FATX rules.
    /// "Halo 2.xiso.iso" -> "Halo 2"
    /// </summary>
    public static string IsoBaseName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        while (name.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".xiso", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..name.LastIndexOf('.')];
        }
        return name;
    }
}

public static class Humanize
{
    private const double Kb = 1024d, Mb = Kb * 1024, Gb = Mb * 1024, Tb = Gb * 1024;

    /// <summary>Matches Format-Size in convert-xiso.ps1 so the GUI and the console agree.</summary>
    public static string Size(long bytes)
    {
        if (bytes <= 0) return "";
        if (bytes >= Tb) return (bytes / Tb).ToString("N2") + " TB";
        if (bytes >= Gb) return (bytes / Gb).ToString("N2") + " GB";
        if (bytes >= Mb) return (bytes / Mb).ToString("N1") + " MB";
        return (bytes / Kb).ToString("N0") + " KB";
    }

    public static string Duration(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes:00}m {ts.Seconds:00}s";
        if (ts.TotalMinutes >= 1) return $"{ts.Minutes}m {ts.Seconds:00}s";
        return seconds.ToString("0.0") + "s";
    }
}
