using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace Wyrmhold.Core;

public class GogProvider : ILibraryProvider
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    public Platform Platform => Platform.Gog;

    public List<Game> GetInstalledGames()
    {
        List<Game> games = new List<Game>();

        foreach (RegistryKey uninstallKey in RegistryHelper.OpenLocalMachineKeys(UninstallPath))
        {
            using (uninstallKey)
            {
                foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                {
                    if (!subKeyName.EndsWith("_is1"))
                    {
                        continue;
                    }

                    string gameId = subKeyName.Substring(0, subKeyName.Length - "_is1".Length);

                    if (games.Any(g => g.PlatformGameId == gameId))
                    {
                        continue;
                    }

                    using RegistryKey? appKey = uninstallKey.OpenSubKey(subKeyName);
                    string? publisher = appKey?.GetValue("Publisher") as string;
                    string? installLocation = appKey?.GetValue("InstallLocation") as string;

                    if (publisher != "GOG.com" || string.IsNullOrEmpty(installLocation))
                    {
                        continue;
                    }

                    try
                    {
                        string installPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installLocation));
                        GogInfo? info = ReadInfo(installPath, gameId);

                        if (info is null)
                        {
                            continue;
                        }

                        if (!string.IsNullOrEmpty(info.RootGameId) && info.RootGameId != info.GameId)
                        {
                            continue;
                        }

                        games.Add(new Game
                        {
                            Platform = Platform.Gog,
                            PlatformGameId = gameId,
                            Name = info.Name,
                            IsInstalled = true,
                            InstallPath = installPath
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Jeu GOG illisible ({gameId}) : {ex.Message}");
                    }
                }
            }
        }

        return games;
    }

    public void Launch(Game game)
    {
        if (game.InstallPath is null)
        {
            Logger.Log($"Jeu GOG sans dossier : {game.Name}");
            return;
        }

        GogInfo? info = ReadInfo(game.InstallPath, game.PlatformGameId);
        GogPlayTask? task = info?.PlayTasks.FirstOrDefault(t => t.IsPrimary && t.Type == "FileTask");

        if (task?.Path is null)
        {
            Logger.Log($"Aucune tâche de lancement GOG pour {game.Name}");
            return;
        }

        string exePath = Path.Combine(game.InstallPath, task.Path);
        string workingDir = string.IsNullOrEmpty(task.WorkingDir)
            ? Path.GetDirectoryName(exePath) ?? game.InstallPath
            : Path.Combine(game.InstallPath, task.WorkingDir);

        Process.Start(new ProcessStartInfo(exePath, task.Arguments ?? "")
        {
            WorkingDirectory = workingDir
        });
    }

    private static GogInfo? ReadInfo(string installPath, string gameId)
    {
        string infoPath = Path.Combine(installPath, $"goggame-{gameId}.info");

        if (!File.Exists(infoPath))
        {
            return null;
        }

        return JsonSerializer.Deserialize<GogInfo>(File.ReadAllText(infoPath), JsonOptions);
    }
}