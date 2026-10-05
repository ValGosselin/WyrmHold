namespace Wyrmhold.Core;

public static class AppPaths
{
    public static string DataFolder { get; } = CreateDataFolder();

    private static string CreateDataFolder()
    {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wyrmhold");

        Directory.CreateDirectory(folder);
        return folder;
    }
    public static string GetWebViewFolder(Platform platform)
    {
        return Path.Combine(DataFolder, "WebView2", platform.ToString());
    }
}