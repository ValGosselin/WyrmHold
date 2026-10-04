using System.Diagnostics;
using Microsoft.Win32;

namespace Wyrmhold.Core;

public class UbisoftProvider : ILibraryProvider
{
    public Platform Platform => Platform.Ubisoft;

    public List<Game> GetInstalledGames()
    {
        List<Game> games = new List<Game>();

        foreach (RegistryKey installsKey in RegistryHelper.OpenLocalMachineKeys(@"SOFTWARE\Ubisoft\Launcher\Installs"))
        {
            using (installsKey)
            {
                foreach (string gameId in installsKey.GetSubKeyNames())
                {
                    if (games.Any(g => g.PlatformGameId == gameId))
                    {
                        continue;
                    }

                    using RegistryKey? gameKey = installsKey.OpenSubKey(gameId);
                    string? installDir = gameKey?.GetValue("InstallDir") as string;

                    if (string.IsNullOrEmpty(installDir))
                    {
                        continue;
                    }

                    installDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDir));

                    if (!Directory.Exists(installDir))
                    {
                        continue;
                    }

                    string name = RegistryHelper.ReadLocalMachineValue(
                        $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Uplay Install {gameId}",
                        "DisplayName")
                        ?? Path.GetFileName(installDir);

                    games.Add(new Game
                    {
                        Platform = Platform.Ubisoft,
                        PlatformGameId = gameId,
                        Name = name,
                        IsInstalled = true,
                        InstallPath = installDir
                    });
                }
            }
        }

        return games;
    }

    public void Launch(Game game)
    {
        string launchUrl = $"uplay://launch/{game.PlatformGameId}/0";
        Process.Start(new ProcessStartInfo(launchUrl) { UseShellExecute = true });
    }
}