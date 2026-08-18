using System.Drawing;

namespace XisoConverterGui;

internal static class Palette
{
    public static readonly Color RowPending = Color.White;
    public static readonly Color RowToDo = Color.FromArgb(232, 241, 252);
    public static readonly Color RowWorking = Color.FromArgb(255, 245, 209);
    public static readonly Color RowOk = Color.FromArgb(230, 246, 232);
    public static readonly Color RowSkipped = Color.FromArgb(244, 244, 244);
    public static readonly Color RowFailed = Color.FromArgb(253, 230, 230);

    /// <summary>Set aside on purpose, which is not the same as skipped or failed.</summary>
    public static readonly Color RowNotXbox = Color.FromArgb(248, 245, 236);

    public static readonly Color TextNormal = Color.FromArgb(20, 20, 20);
    public static readonly Color TextMuted = Color.FromArgb(128, 128, 128);

    /// <summary>Used on the folder cell when the script had to rename for FATX.</summary>
    public static readonly Color Renamed = Color.FromArgb(166, 87, 0);

    public static readonly Color Error = Color.FromArgb(185, 28, 28);
    public static readonly Color Note = Color.FromArgb(146, 94, 12);
    public static readonly Color Ok = Color.FromArgb(21, 128, 61);

    public static readonly Color LogBack = Color.FromArgb(30, 30, 30);
    public static readonly Color LogFore = Color.FromArgb(220, 220, 220);

    public static Color RowBack(string status) => status switch
    {
        GameStatus.ToDo => RowToDo,
        GameStatus.Working => RowWorking,
        GameStatus.Ok => RowOk,
        GameStatus.Skipped => RowSkipped,
        GameStatus.Cancelled => RowSkipped,
        GameStatus.Failed => RowFailed,
        GameStatus.NotXbox => RowNotXbox,
        _ => RowPending,
    };

    public static Color RowFore(string status) => status switch
    {
        GameStatus.Skipped => TextMuted,
        GameStatus.Cancelled => TextMuted,
        GameStatus.NotXbox => Note,
        _ => TextNormal,
    };
}
