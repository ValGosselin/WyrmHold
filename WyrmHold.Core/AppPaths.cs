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
        return GetWebViewFolder(platform.ToString());
    }

    // Pour un site qui n'est pas une plateforme de jeux (ex. « IsThereAnyDeal ») :
    // chaque site a son propre dossier, donc ses propres cookies de connexion.
    public static string GetWebViewFolder(string name)
    {
        return Path.Combine(DataFolder, "WebView2", name);
    }
}