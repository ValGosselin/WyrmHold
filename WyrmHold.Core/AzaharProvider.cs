using System.Text.RegularExpressions;

namespace Wyrmhold.Core;

/// <summary>
/// Azahar : 3DS. Vérifié le 1er novembre 2026 sur Azahar 2126.2 (exécutable « azahar.exe ») et dans son code :
/// - lancement : « azahar.exe &lt;jeu&gt; » ;
/// - un .cia ne se lance PAS : Azahar demande de l'installer. Ses jeux installés sont des .app rangés dans sdmc ;
/// - données : dossier « user » à côté du programme s'il existe, sinon %APPDATA%\Azahar ;
/// - config\qt-config.ini, section [UI] : Paths\gamedirs\1\path=…, avec les valeurs spéciales INSTALLED et SYSTEM.
/// </summary>
public class AzaharProvider : EmulatorProvider
{
    public const string Executable = "azahar.exe";

    private const string PortableFolderName = "user";

    // Valeur spéciale de qt-config.ini : les jeux installés depuis un .cia.
    private const string InstalledMarker = "INSTALLED";
    private const string SystemMarker = "SYSTEM";

    // Les jeux installés sur la carte SD émulée : title\00040000\<id>\content\*.app (00040000 = jeux, ni DLC ni système).
    private const string InstalledTitlesPattern = @"title\00040000";

    private const string CiaExtension = ".cia";

    private const int TitleIdLength = 16;

    // Les jeux que l'on peut lancer directement (liste de game_list.cpp, sans les programmes .elf et .axf).
    private static readonly HashSet<string> GameExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".3ds", ".cci", ".cxi", ".3dsx", ".zcci", ".zcxi", ".z3dsx"
    };

    /// <summary>
    /// Ce qu'on retient de qt-config.ini.
    /// </summary>
    public record AzaharConfig(List<AzaharGameFolder> GameFolders, string? SdmcFolder);

    public record AzaharGameFolder(string Path, bool IncludeSubfolders);

    public AzaharProvider(AppSettings settings) : base(settings, Platform.Azahar)
    {
    }

    public override string ExecutableName => Executable;

    protected override IEnumerable<string> KnownFolders()
    {
        yield return UserFolder(Environment.SpecialFolder.ProgramFiles, "Azahar");
        yield return UserFolder(Environment.SpecialFolder.LocalApplicationData, "Programs", "Azahar");
    }

    protected override List<string> ReadConfiguredFolders(string executablePath)
    {
        return ReadConfig(executablePath).GameFolders
            .Where(folder => !IsSpecialFolder(folder.Path))
            .Select(folder => folder.Path)
            .ToList();
    }

    protected override List<Game> ScanGames(string executablePath, List<string> folders)
    {
        AzaharConfig config = ReadConfig(executablePath);
        List<Game> games = ScanInstalledGames(config, executablePath);

        // Un dossier d'Azahar respecte sa case « sous-dossiers » ; un dossier ajouté dans Wyrmhold est lu en entier.
        HashSet<string> shallow = config.GameFolders
            .Where(folder => !folder.IncludeSubfolders && !IsSpecialFolder(folder.Path))
            .Select(folder => Path.TrimEndingDirectorySeparator(folder.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int ignoredCiaFiles = 0;

        foreach (string folder in folders)
        {
            foreach (string path in EnumerateFiles(folder, recursive: !shallow.Contains(folder)))
            {
                string extension = Path.GetExtension(path);

                if (extension.Equals(CiaExtension, StringComparison.OrdinalIgnoreCase))
                {
                    ignoredCiaFiles++;
                }
                else if (GameExtensions.Contains(extension))
                {
                    games.Add(CreateGameFromFile(path));
                }
            }
        }

        if (ignoredCiaFiles > 0)
        {
            Logger.Log($"Azahar : {ignoredCiaFiles} fichier(s) .cia ignoré(s) : ils se lancent une fois installés dans Azahar.");
        }

        return games;
    }

    protected override IEnumerable<string> BuildArguments(Game game, string executablePath)
    {
        return new[] { game.InstallPath ?? "" };
    }

    /// <summary>
    /// La save d'un jeu sur la carte SD émulée (vérifié le 3 novembre 2026 sur Azahar 2126.2) :
    /// Nintendo 3DS\&lt;id&gt;\&lt;id&gt;\title\&lt;8 premiers chiffres&gt;\&lt;8 derniers&gt;\data\00000001\&lt;fichier&gt;.
    /// </summary>
    public override string? FindSaveFile(string titleId, string fileName)
    {
        if (FindExecutable() is not string executablePath || titleId.Length != TitleIdLength)
        {
            return null;
        }

        string sdmc = ReadConfig(executablePath).SdmcFolder ?? Path.Combine(FindDataFolder(executablePath), "sdmc");
        string root = Path.Combine(sdmc, "Nintendo 3DS");

        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory.EnumerateDirectories(root)
            .SelectMany(Directory.EnumerateDirectories)
            .Select(folder => Path.Combine(folder, "title", titleId[..8], titleId[8..], "data", "00000001", fileName))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Lit qt-config.ini. Qt écrit « \\ » pour une barre oblique inverse, et met entre guillemets un chemin
    /// qui contient certains caractères.
    /// </summary>
    public static AzaharConfig ParseConfig(IEnumerable<string> lines)
    {
        Dictionary<int, string> paths = new Dictionary<int, string>();
        Dictionary<int, bool> deepScans = new Dictionary<int, bool>();
        string section = "";
        bool useCustomStorage = false;
        string? sdmcFolder = null;

        foreach (string line in lines.Select(text => text.Trim()))
        {
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line;
                continue;
            }

            Match gameFolder = Regex.Match(line, @"^Paths\\gamedirs\\(\d+)\\(path|deep_scan)=(.*)$");

            if (section == "[UI]" && gameFolder.Success)
            {
                int index = int.Parse(gameFolder.Groups[1].Value);

                if (gameFolder.Groups[2].Value == "path")
                {
                    paths[index] = Unescape(gameFolder.Groups[3].Value);
                }
                else
                {
                    deepScans[index] = gameFolder.Groups[3].Value == "true";
                }
            }
            else if (line.StartsWith("use_custom_storage=", StringComparison.Ordinal))
            {
                useCustomStorage = line.EndsWith("=true", StringComparison.Ordinal);
            }
            else if (line.StartsWith("sdmc_directory=", StringComparison.Ordinal))
            {
                sdmcFolder = Unescape(line.Substring("sdmc_directory=".Length));
            }
        }

        List<AzaharGameFolder> folders = paths
            .OrderBy(entry => entry.Key)
            .Select(entry => new AzaharGameFolder(entry.Value, deepScans.GetValueOrDefault(entry.Key)))
            .ToList();

        return new AzaharConfig(folders, useCustomStorage && !string.IsNullOrEmpty(sdmcFolder) ? sdmcFolder : null);
    }

    private static string Unescape(string value)
    {
        return value.Trim().Trim('"').Replace(@"\\", @"\");
    }

    private static bool IsSpecialFolder(string path)
    {
        return path is InstalledMarker or SystemMarker;
    }

    private AzaharConfig ReadConfig(string executablePath)
    {
        string configPath = Path.Combine(FindDataFolder(executablePath), "config", "qt-config.ini");

        try
        {
            return File.Exists(configPath)
                ? ParseConfig(File.ReadAllLines(configPath))
                : new AzaharConfig(new List<AzaharGameFolder>(), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Log($"Azahar : configuration illisible : {ex.Message}");
            return new AzaharConfig(new List<AzaharGameFolder>(), null);
        }
    }

    private static string FindDataFolder(string executablePath)
    {
        string portableFolder = Path.Combine(Path.GetDirectoryName(executablePath) ?? "", PortableFolderName);

        return Directory.Exists(portableFolder)
            ? portableFolder
            : UserFolder(Environment.SpecialFolder.ApplicationData, "Azahar");
    }

    // Un jeu installé = un dossier de titre ; son programme est le plus gros .app de son dossier « content ».
    private List<Game> ScanInstalledGames(AzaharConfig config, string executablePath)
    {
        string sdmc = config.SdmcFolder ?? Path.Combine(FindDataFolder(executablePath), "sdmc");
        string root = Path.Combine(sdmc, "Nintendo 3DS");
        List<Game> games = new List<Game>();

        IEnumerable<IGrouping<string?, string>> titles = EnumerateFiles(root)
            .Where(path => path.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
                && path.Contains(InstalledTitlesPattern, StringComparison.OrdinalIgnoreCase))
            .GroupBy(Path.GetDirectoryName);

        foreach (IGrouping<string?, string> title in titles)
        {
            games.Add(CreateGameFromFile(title.MaxBy(path => new FileInfo(path).Length)!));
        }

        return games;
    }

    private Game CreateGameFromFile(string path)
    {
        string fileName = Path.GetFileNameWithoutExtension(path);
        ThreeDsRomInfo? info = ThreeDsRomHeader.TryRead(path);

        // Sans titre lisible, le nom d'un .app installé (« 00000000 ») ne veut rien dire : on montre son Title ID.
        string name = info?.Title
            ?? (path.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && info?.TitleId is string titleId
                ? titleId
                : NameTools.CleanRomName(fileName));

        return CreateGame(info?.TitleId ?? $"file-{NameTools.ToSlug(fileName)}", name, GameConsole.Nintendo3ds, path);
    }
}
