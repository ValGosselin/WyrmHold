namespace Wyrmhold.Core;

public static class Logger
{


    private static readonly string LogPath = Path.Combine(AppPaths.DataFolder, "wyrmhold.log");

    public static void Log(string message)
    {
        File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
    }
}