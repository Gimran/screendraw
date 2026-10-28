using System;
using System.IO;
using System.Threading.Tasks;

namespace ScreenDraw;

/// <summary>Writes unexpected exceptions to %APPDATA%\ScreenDraw\crash.log, so a crash at logon / shutdown can be diagnosed afterwards.</summary>
static class CrashLog
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenDraw", "crash.log");

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("Unhandled exception", e.ExceptionObject as Exception, e.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, e) => { Write("Unobserved task exception", e.Exception); e.SetObserved(); };
    }

    public static void Write(string what, Exception? ex, bool fatal = false)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {what}{(fatal ? " (fatal)" : "")}\r\n" +
                $"  exe: {Environment.ProcessPath}  args: {string.Join(' ', Environment.GetCommandLineArgs()[1..])}\r\n" +
                (ex != null ? ex + "\r\n" : "") + "\r\n");
        }
        catch { /* logging must never throw */ }
    }
}
