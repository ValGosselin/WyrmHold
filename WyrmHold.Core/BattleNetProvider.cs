using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Wyrmhold.Core;

public class BattleNetProvider : ILibraryProvider
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly Dictionary<string, string> LaunchCodes = new Dictionary<string, string>
    {
        ["hs_beta"] = "WTCG",
        ["wow"] = "WoW",
        ["s1"] = "S1",
        ["s2"] = "S2"
    };

    public Platform Platform => Platform.BattleNet;

    public List<Game> GetInstalledGames()
    {
        List<Game> games = new List<Game>();

        foreach (RegistryKey uninstallKey in RegistryHelper.OpenLocalMachineKeys(UninstallPath))
        {
            using (uninstallKey)
            {
                foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                {
                    if (string.Equals(subKeyName, "Battle.net", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    using RegistryKey? appKey = uninstallKey.OpenSubKey(subKeyName);

                    string? uninstallString = appKey?.GetValue("UninstallString") as string;
                    string? name = appKey?.GetValue("DisplayName") as string;
                    string? installLocation = appKey?.GetValue("InstallLocation") as string;

                    if (uninstallString is null || name is null || !uninstallString.Contains("Battle.net"))
                    {
                        continue;
                    }

                    Match match = Regex.Match(uninstallString, @"--uid=(\S+)");

                    if (!match.Success)
                    {
                        continue;
                    }

                    string uid = match.Groups[1].Value;

                    if (games.Any(g => g.PlatformGameId == uid))
                    {
                        continue;
                    }

                    games.Add(new Game
                    {
                        Platform = Platform.BattleNet,
                        PlatformGameId = uid,
                        Name = name,
                        IsInstalled = true,
                        InstallPath = string.IsNullOrEmpty(installLocation) ? null : installLocation
                    });
                }
            }
        }

        return games;
    }

    public void Launch(Game game)
    {
        string? folder = RegistryHelper.ReadLocalMachineValue($@"{UninstallPath}\Battle.net", "InstallLocation");
        folder ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Battle.net");

        string exePath = Path.Combine(folder, "Battle.net.exe");

        if (!LaunchCodes.TryGetValue(game.PlatformGameId, out string? code))
        {
            Logger.Log($"Code de lancement Battle.net inconnu pour {game.Name} (uid {game.PlatformGameId})");
            Process.Start(exePath);
            return;
        }

        Process.Start(new ProcessStartInfo(exePath, $"--exec=\"launch {code}\""));
    }
}