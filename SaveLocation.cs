using System;
using System.IO;

namespace ScreenDraw;

static class SaveLocation
{
    public static string Prepare(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            throw new IOException(Loc.T("Укажите полный путь к папке сохранения."));
        string full = Path.GetFullPath(directory);
        string root = Path.GetPathRoot(full)!;
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(Loc.T("Диск или сетевая папка недоступны: ") + root);
        Directory.CreateDirectory(full);
        return full;
    }

    // Retry the same operation in a user-selected folder. Cancellation never reports success.
    public static string? Save(string? directory, Action<string> write, Func<string?, Exception, string?> choose)
    {
        while (true)
        {
            try
            {
                string full = Prepare(directory);
                write(full);
                return full;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
            {
                directory = choose(directory, ex);
                if (directory == null) return null;
            }
        }
    }
}
