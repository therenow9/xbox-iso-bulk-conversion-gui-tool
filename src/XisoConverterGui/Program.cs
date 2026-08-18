using System.Windows.Forms;

namespace XisoConverterGui;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // A crash in a background continuation should say what went wrong rather than
        // vanishing the window.
        Application.ThreadException += (_, e) => ShowCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowCrash(e.ExceptionObject as Exception);

        Application.Run(new MainForm());
    }

    private static void ShowCrash(Exception? ex)
    {
        MessageBox.Show(
            "XISO Converter hit an unexpected error:\r\n\r\n" + (ex?.ToString() ?? "unknown"),
            "Unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
