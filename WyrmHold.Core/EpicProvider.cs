using System.Diagnostics;
using System.Text.Json;

namespace Wyrmhold.Core;

public class EpicProvider : ILibraryProvider
{
    public Platform Platform => Platform.Epic;

    public List<Game> GetInstalledGames()
    {
        List<Game> games = new List<Game>();

        string manifestsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

        if (!Directory.Exists(manifestsFolder))
        {
            return games;
        }

        foreach (string itemPath in Directory.GetFiles(manifestsFolder, "*.item"))
        {
            try
            {
                string json = File.ReadAllText(itemPath);
                EpicManifest? manifest = JsonSerializer.Deserialize<EpicManifest>(json);

                if (manifest is null
                    || manifest.IsIncompleteInstall
                    || (!string.IsNullOrEmpty(manifest.MainGameAppName) && manifest.AppName != manifest.MainGameAppName)
                    || !manifest.AppCategories.Contains("games")
                    || string.IsNullOrEmpty(manifest.InstallLocation))
                {
                    continue;
                }

                games.Add(new Game
                {
                    Platform = Platform.Epic,
                    PlatformGameId = manifest.AppName,
                    Name = manifest.DisplayName,
                    IsInstalled = true,
                    InstallPath = Path.GetFullPath(manifest.InstallLocation),
                    SizeOnDiskBytes = manifest.InstallSize,
                    InstalledVersion = string.IsNullOrEmpty(manifest.AppVersionString) ? null : manifest.AppVersionString
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"Fichier Epic illisible {itemPath} : {ex.Message}");
            }
        }

        return games;
    }

    public void Launch(Game game)
    {
        string launchUrl = $"com.epicgames.launcher://apps/{game.PlatformGameId}?action=launch&silent=true";
        Process.Start(new ProcessStartInfo(launchUrl) { UseShellExecute = true });
    }
}