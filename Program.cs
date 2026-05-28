using System;
using System.IO;
using System.Windows.Forms;

namespace Romestead.MapWorkshop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.ThreadException += (s, e) => WriteCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => WriteCrash(e.ExceptionObject as Exception);

        var config = AppConfig.Load();
        var gameRoot = GameFolderResolver.Resolve(config);
        if (string.IsNullOrEmpty(gameRoot))
        {
            // User cancelled the picker. Nothing to do.
            return;
        }

        Paths.SetGameRoot(gameRoot);

        Application.Run(new MainForm(config));
    }

    public static string CrashLogPath { get; } = Path.Combine(AppConfig.ConfigDir, "crash.log");

    private static void WriteCrash(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            File.AppendAllText(CrashLogPath,
                $"[{DateTime.Now:o}] {ex.GetType().FullName}: {ex.Message}\r\n{ex.StackTrace}\r\n\r\n");
        }
        catch { /* best effort */ }
    }
}
