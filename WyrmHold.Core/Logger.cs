namespace Wyrmhold.Core;

public static class Logger
{
    private static readonly string LogFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Wyrmhold");

    private static readonly string LogPath = Path.Combine(LogFolder, "wyrmhold.log");

    public static void Log(string message)
    {
        Directory.CreateDirectory(LogFolder);
        File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
    }
}