using System.Text.Json;
using System.Text.Json.Serialization;

namespace XisoConverterGui;

/// <summary>
/// One line of the newline-delimited JSON stream that convert-xiso.ps1 -Json writes
/// to stdout. Every event kind is folded into this one type: the script emits flat
/// objects and key order is not stable, so everything is parsed by name and unused
/// fields simply stay at their default.
/// </summary>
public sealed class ConverterEvent
{
    [JsonPropertyName("event")] public string? Event { get; set; }

    // start
    [JsonPropertyName("tool")] public string? Tool { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("output")] public string? Output { get; set; }
    [JsonPropertyName("dryRun")] public bool DryRun { get; set; }

    // game-start / game-done / log
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("iso")] public string? Iso { get; set; }
    [JsonPropertyName("folder")] public string? Folder { get; set; }
    [JsonPropertyName("bytes")] public long Bytes { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("seconds")] public double Seconds { get; set; }
    [JsonPropertyName("detail")] public string? Detail { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }

    // summary
    [JsonPropertyName("extracted")] public int Extracted { get; set; }
    [JsonPropertyName("skipped")] public int Skipped { get; set; }
    [JsonPropertyName("failed")] public int Failed { get; set; }
    [JsonPropertyName("todo")] public int Todo { get; set; }
    [JsonPropertyName("notXbox")] public int NotXbox { get; set; }
    [JsonPropertyName("log")] public string? Log { get; set; }

    // warning / error
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("freeBytes")] public long FreeBytes { get; set; }
    [JsonPropertyName("neededBytes")] public long NeededBytes { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Parses one stdout line. The script is documented to write nothing but JSON,
    /// but a malformed line must never take the app down: anything unparseable comes
    /// back as a synthetic log event so the user can still see it in the log pane.
    /// </summary>
    public static ConverterEvent Parse(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length > 0 && trimmed[0] == '{')
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<ConverterEvent>(trimmed, Options);
                if (parsed?.Event is not null) return parsed;
            }
            catch (JsonException)
            {
                // fall through to the raw-text event below
            }
        }

        return new ConverterEvent { Event = "log", Text = line };
    }
}
