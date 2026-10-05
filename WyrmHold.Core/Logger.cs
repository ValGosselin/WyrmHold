namespace Wyrmhold.Core;

public static class Logger
{


    private static readonly string LogPath = Path.Combine(AppPaths.DataFolder, "wyrmhold.log");
    private static readonly object WriteLock = new object();

    public static void Log(string message)
    {
        lock (WriteLock)
        {
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
    }
}