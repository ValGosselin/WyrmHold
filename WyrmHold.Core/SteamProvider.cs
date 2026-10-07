using Microsoft.Win32;
using System.Diagnostics;

namespace Wyrmhold.Core;

public class SteamProvider : ILibraryProvider
{
    public Platform Platform => Platform.Steam;
    public List<Game> GetInstalledGames()
    {
        List<Game> games = new List<Game>();

        string? steamPath = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Valve\Steam",
            "SteamPath",
            null) as string;

        if (steamPath is null)
        {
            return games;
        }

        steamPath = Path.GetFullPath(steamPath);
        string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

        if (!File.Exists(vdfPath))
        {
            return games;
        }

        foreach (string folder in GetLibraryFolders(vdfPath))
        {
            string steamappsPath = Path.Combine(folder, "steamapps");

            if (!Directory.Exists(steamappsPath))
            {
                continue;
            }

            foreach (string manifestPath in Directory.GetFiles(steamappsPath, "appmanifest_*.acf"))
            {
                string[] lines = File.ReadAllLines(manifestPath);

                string? appId = ReadValue(lines, "appid");
                string? name = ReadValue(lines, "name");
                string? installDir = ReadValue(lines, "installdir");
                long.TryParse(ReadValue(lines, "SizeOnDisk"), out long sizeOnDisk);

                // Le numéro de build change à chaque mise à jour du jeu.
                string? buildId = ReadValue(lines, "buildid");

                if (appId is null || name is null || installDir is null)
                {
                    continue;
                }
                if (appId == "228980")
                {
                    continue;
                }

                games.Add(new Game
                {
                    Platform = Platform.Steam,
                    PlatformGameId = appId,
                    Name = name,
                    IsInstalled = true,
                    InstallPath = Path.Combine(steamappsPath, "common", installDir),
                    SizeOnDiskBytes = sizeOnDisk,
                    InstalledVersion = buildId
                });
            }
        }

        return games;
    }
    public void Launch(Game game)
    {
        string launchUrl = $"steam://rungameid/{game.PlatformGameId}";
        Process.Start(new ProcessStartInfo(launchUrl) { UseShellExecute = true });
    }
    private static List<string> GetLibraryFolders(string vdfPath)
    {
        List<string> folders = new List<string>();

        foreach (string line in File.ReadAllLines(vdfPath))
        {
            string[] parts = line.Split('"');

            if (parts.Length >= 4 && parts[1] == "path")
            {
                folders.Add(parts[3].Replace(@"\\", @"\"));
            }
        }

        return folders;
    }

    private static string? ReadValue(string[] lines, string key)
    {
        foreach (string line in lines)
        {
            string[] parts = line.Split('"');

            if (parts.Length >= 4 && string.Equals(parts[1], key, StringComparison.OrdinalIgnoreCase))
            {
                return parts[3];
            }
        }

        return null;
    }
}