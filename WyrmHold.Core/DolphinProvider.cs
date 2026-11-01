using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace Wyrmhold.Core;

/// <summary>
/// Dolphin : GameCube et Wii. Vérifié le 1er novembre 2026 sur Dolphin 2609a :
/// - lancement : « Dolphin.exe -b -e &lt;jeu&gt; » (-e charge le jeu, -b cache la fenêtre de Dolphin :
///   il se ferme avec le jeu, sinon sa fenêtre resterait ouverte après) ;
/// - dossiers : Config\Dolphin.ini, section [General], « ISOPaths = 2 » puis « ISOPath0 = … », « ISOPath1 = … » ;
/// - dossier de données (même ordre que Dolphin, UICommon.cpp) : portable.txt ou LocalUserConfig = « User » à côté
///   du programme, sinon UserConfigPath dans le registre, sinon Documents s'il existe, sinon AppData.
/// </summary>
public class DolphinProvider : EmulatorProvider
{
    public const string Executable = "Dolphin.exe";

    private const string UserFolderName = "Dolphin Emulator";
    private const string PortableFolderName = "User";
    private const string RegistryKeyPath = @"Software\Dolphin Emulator";

    // Les formats de disques (Dolphin en accepte d'autres : programmes .dol et .elf, listes .m3u).
    private static readonly HashSet<string> DiscExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".iso", ".gcm", ".rvz", ".wia", ".gcz", ".ciso", ".wbfs"
    };

    // Un WAD est un jeu Wii téléchargeable (WiiWare) : pas d'en-tête de disque, son nom vient du fichier.
    private const string WiiWareExtension = ".wad";

    public DolphinProvider(AppSettings settings) : base(settings, Platform.Dolphin)
    {
    }

    public override string ExecutableName => Executable;

    protected override IEnumerable<string> KnownFolders()
    {
        yield return UserFolder(Environment.SpecialFolder.ProgramFiles, "Dolphin-x64");
        yield return UserFolder(Environment.SpecialFolder.ProgramFiles, "Dolphin");
        yield return UserFolder(Environment.SpecialFolder.MyDocuments, "Dolphin-x64");
        yield return UserFolder(Environment.SpecialFolder.Desktop, "Dolphin-x64");
        yield return UserFolder(Environment.SpecialFolder.UserProfile, "Downloads", "Dolphin-x64");
    }

    protected override List<string> ReadConfiguredFolders(string executablePath)
    {
        string configPath = Path.Combine(FindUserFolder(executablePath), "Config", "Dolphin.ini");

        return File.Exists(configPath) ? ReadIsoPaths(File.ReadAllLines(configPath)) : new List<string>();
    }

    /// <summary>
    /// Les dossiers de la section [General] de Dolphin.ini.
    /// </summary>
    public static List<string> ReadIsoPaths(IEnumerable<string> lines)
    {
        List<string> folders = new List<string>();
        bool isInGeneral = false;

        foreach (string line in lines.Select(text => text.Trim()))
        {
            if (line.StartsWith('['))
            {
                isInGeneral = line.Equals("[General]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            Match match = Regex.Match(line, @"^ISOPath\d+\s*=\s*(.+)$");

            if (isInGeneral && match.Success)
            {
                folders.Add(match.Groups[1].Value.Trim());
            }
        }

        return folders;
    }

    protected override List<Game> ScanGames(string executablePath, List<string> folders)
    {
        List<(DiscImageInfo? Disc, Game Game)> found = new List<(DiscImageInfo?, Game)>();

        foreach (string path in folders.SelectMany(folder => EnumerateFiles(folder)))
        {
            string extension = Path.GetExtension(path);
            string fileName = Path.GetFileNameWithoutExtension(path);
            string cleanName = NameTools.CleanRomName(fileName);

            if (extension.Equals(WiiWareExtension, StringComparison.OrdinalIgnoreCase))
            {
                found.Add((null, CreateGame($"wad-{NameTools.ToSlug(fileName)}", cleanName, GameConsole.Wii, path)));
            }
            else if (DiscExtensions.Contains(extension) && DiscImageHeader.TryRead(path) is DiscImageInfo disc)
            {
                found.Add((disc, CreateGame(disc.GameId, disc.Name ?? cleanName, disc.Console, path)));
            }
        }

        // Un jeu sur deux disques : même numéro de jeu. Le disque 1 d'abord, c'est lui qui reste (voir GetInstalledGames) ;
        // Dolphin demande le changement de disque depuis le jeu.
        return found
            .OrderBy(entry => entry.Disc?.DiscNumber ?? 0)
            .Select(entry => entry.Game)
            .ToList();
    }

    protected override IEnumerable<string> BuildArguments(Game game, string executablePath)
    {
        return new[] { "-b", "-e", game.InstallPath ?? "" };
    }

    private static string FindUserFolder(string executablePath)
    {
        string programFolder = Path.GetDirectoryName(executablePath) ?? "";
        string portableFolder = Path.Combine(programFolder, PortableFolderName);

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);

        bool isPortable = File.Exists(Path.Combine(programFolder, "portable.txt"))
            || key?.GetValue("LocalUserConfig") is int and not 0;

        if (isPortable)
        {
            return portableFolder;
        }

        if (key?.GetValue("UserConfigPath") is string { Length: > 0 } configured)
        {
            return configured;
        }

        string documents = UserFolder(Environment.SpecialFolder.MyDocuments, UserFolderName);

        return Directory.Exists(documents)
            ? documents
            : UserFolder(Environment.SpecialFolder.ApplicationData, UserFolderName);
    }
}
