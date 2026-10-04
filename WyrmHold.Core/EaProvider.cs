using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Win32;

namespace Wyrmhold.Core;

public class EaProvider : ILibraryProvider
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public Platform Platform => Platform.Ea;

    public List<Game> GetInstalledGames()
    {
        List<Game> games = new List<Game>();

        foreach (RegistryKey uninstallKey in RegistryHelper.OpenLocalMachineKeys(UninstallPath))
        {
            using (uninstallKey)
            {
                foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                {
                    using RegistryKey? appKey = uninstallKey.OpenSubKey(subKeyName);

                    string? installLocation = appKey?.GetValue("InstallLocation") as string;
                    string? uninstallString = appKey?.GetValue("UninstallString") as string;
                    string? displayName = appKey?.GetValue("DisplayName") as string;

                    if (string.IsNullOrEmpty(installLocation))
                    {
                        continue;
                    }

                    if (uninstallString is not null
                        && uninstallString.Contains("steam://", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        string installPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installLocation));
                        string xmlPath = Path.Combine(installPath, "__Installer", "installerdata.xml");

                        if (!File.Exists(xmlPath))
                        {
                            continue;
                        }

                        XDocument document = XDocument.Load(xmlPath);

                        string? contentId = document.Descendants("contentID")
                            .Select(e => e.Value.Trim())
                            .FirstOrDefault(id => id.Length > 0);

                        if (contentId is null || games.Any(g => g.PlatformGameId == contentId))
                        {
                            continue;
                        }

                        string name = GetTitle(document) ?? displayName ?? Path.GetFileName(installPath);

                        games.Add(new Game
                        {
                            Platform = Platform.Ea,
                            PlatformGameId = contentId,
                            Name = CleanName(name),
                            IsInstalled = true,
                            InstallPath = installPath
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Jeu EA illisible ({subKeyName}) : {ex.Message}");
                    }
                }
            }
        }

        return games;
    }

    public void Launch(Game game)
    {
        string launchUrl = $"origin2://game/launch?offerIds={game.PlatformGameId}";

        string eaLauncherPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Electronic Arts", "EA Desktop", "EA Desktop", "EALauncher.exe");

        if (File.Exists(eaLauncherPath))
        {
            Process.Start(new ProcessStartInfo(eaLauncherPath, $"\"{launchUrl}\""));
            return;
        }

        Logger.Log($"EALauncher.exe introuvable ({eaLauncherPath}), essai avec le lien direct");
        Process.Start(new ProcessStartInfo(launchUrl) { UseShellExecute = true });
    }

    private static string? GetTitle(XDocument document)
    {
        List<XElement> titles = document.Descendants("gameTitle").ToList();

        XElement? title = titles.FirstOrDefault(t => (string?)t.Attribute("locale") == "fr_FR")
                       ?? titles.FirstOrDefault(t => (string?)t.Attribute("locale") == "en_US")
                       ?? titles.FirstOrDefault();

        return title?.Value.Trim();
    }

    private static string CleanName(string name)
    {
        return name.Replace("™", "").Replace("®", "").Trim();
    }
}