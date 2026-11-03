using System.Diagnostics;

namespace Wyrmhold.Core;

/// <summary>
/// Un jeu émulé n'a pas pu être lancé, avec un message à montrer tel quel (ex. « aucun cœur installé »).
/// </summary>
public class EmulatorLaunchException : Exception
{
    public EmulatorLaunchException(string message) : base(message)
    {
    }
}

/// <summary>
/// Ce que tous les émulateurs ont en commun : trouver le programme, parcourir les dossiers de jeux, lancer un jeu.
/// Chaque émulateur (RetroArchProvider, DolphinProvider…) ajoute ce qui lui est propre : où il range sa
/// configuration, comment reconnaître ses jeux, et sa ligne de commande.
/// Pour un jeu émulé, Game.InstallPath est le fichier du jeu (voir GameWatcher pour le temps de jeu).
/// </summary>
public abstract class EmulatorProvider : ILibraryProvider
{
    // Les fichiers des jeux sont rarement plus profonds. Évite de se perdre dans un dossier trop large.
    private const int MaxFolderDepth = 8;

    // Tous les programmes d'émulateurs : le détecteur de jeu lit la ligne de commande de ceux-là seulement.
    private static readonly HashSet<string> ExecutableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        RetroArchProvider.Executable,
        DolphinProvider.Executable,
        RyujinxProvider.Executable,
        AzaharProvider.Executable
    };

    private readonly AppSettings _appSettings;

    protected EmulatorProvider(AppSettings settings, Platform platform)
    {
        _appSettings = settings;
        Platform = platform;
        Settings = settings.GetEmulator(platform);
    }

    public Platform Platform { get; }

    public EmulatorSettings Settings { get; }

    public string Name => Platform.ToString();

    public abstract string ExecutableName { get; }

    // Les dossiers où l'émulateur s'installe d'habitude.
    protected abstract IEnumerable<string> KnownFolders();

    // Les dossiers de jeux déclarés dans la configuration de l'émulateur (pour un chemin de programme connu).
    protected abstract List<string> ReadConfiguredFolders(string executablePath);

    // Les jeux de ces dossiers, en plus de ce que l'émulateur connaît lui-même.
    protected abstract List<Game> ScanGames(string executablePath, List<string> folders);

    // Les paramètres de la ligne de commande pour lancer ce jeu.
    protected abstract IEnumerable<string> BuildArguments(Game game, string executablePath);

    public static bool IsEmulatorProgram(string executablePath)
    {
        return ExecutableNames.Contains(Path.GetFileName(executablePath));
    }

    /// <summary>
    /// Le programme de l'émulateur : celui choisi dans Comptes s'il existe encore, sinon celui trouvé tout seul.
    /// </summary>
    public string? FindExecutable()
    {
        if (Settings.ExecutablePath is string chosen && File.Exists(chosen))
        {
            return chosen;
        }

        try
        {
            return EmulatorLocator.Find(ExecutableName, Name, KnownFolders());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Logger.Log($"{Name} : recherche du programme impossible : {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Les dossiers de jeux que l'émulateur déclare lui-même. Une configuration illisible ne bloque rien :
    /// il reste les dossiers ajoutés dans Wyrmhold.
    /// </summary>
    public List<string> GetConfiguredFolders()
    {
        if (FindExecutable() is not string executablePath)
        {
            return new List<string>();
        }

        try
        {
            return ReadConfiguredFolders(executablePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Logger.Log($"{Name} : configuration illisible : {ex.Message}");
            return new List<string>();
        }
    }

    public List<Game> GetInstalledGames()
    {
        // Pas d'émulateur = pas de jeux à lancer : ils sont masqués, avec leur temps de jeu gardé en base.
        if (FindExecutable() is not string executablePath)
        {
            return new List<Game>();
        }

        List<string> folders = GetConfiguredFolders()
            .Concat(Settings.GameFolders)
            .Select(folder => Path.TrimEndingDirectorySeparator(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Le même jeu trouvé deux fois (deux dossiers, playlist + dossier) : une seule ligne, la première.
        return ScanGames(executablePath, folders)
            .GroupBy(game => game.PlatformGameId)
            .Select(group => group.First())
            .ToList();
    }

    public void Launch(Game game)
    {
        if (FindExecutable() is not string executablePath)
        {
            throw new EmulatorLaunchException($"{Name} est introuvable. Indique son programme dans Comptes → Émulateurs.");
        }

        if (!File.Exists(game.RomFilePath))
        {
            throw new EmulatorLaunchException($"Le fichier de {game.Name} est introuvable. Clique sur Actualiser s'il a été déplacé.");
        }

        Process.Start(BuildStartInfo(game, executablePath))?.Dispose();
    }

    /// <summary>
    /// La commande complète. ArgumentList met les guillemets tout seul : un nom avec des espaces ou
    /// des accents arrive intact à l'émulateur.
    /// </summary>
    public ProcessStartInfo BuildStartInfo(Game game, string executablePath)
    {
        ProcessStartInfo start = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? ""
        };

        foreach (string argument in BuildArguments(game, executablePath))
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }

    /// <summary>
    /// Le fichier de save d'un jeu, pour les succès maison (null s'il n'y en a pas ou si l'émulateur ne le sait pas).
    /// </summary>
    public virtual string? FindSaveFile(string titleId, string fileName) => null;

    /// <summary>
    /// Vrai si Wyrmhold annoncera lui-même les succès de ce jeu : l'émulateur n'a alors pas à les annoncer aussi
    /// (choix de Val du 2 novembre 2026). Faux si le suivi est coupé : on laisse ses notifications à l'émulateur,
    /// pour ne jamais se retrouver sans aucune notification.
    /// </summary>
    protected bool WyrmholdAnnouncesAchievements(Game game)
    {
        return _appSettings.OverlayEnabled
            && _appSettings.IsAchievementSourceEnabled(Platform)
            && Secrets.Current.HasRetroAchievements
            && game.RetroAchievementsGameId > 0;
    }

    protected Game CreateGame(string gameId, string name, GameConsole console, string romPath)
    {
        return new Game
        {
            Platform = Platform,
            PlatformGameId = gameId,
            Name = name,
            Console = console,
            IsInstalled = true,
            InstallPath = romPath,
            SizeOnDiskBytes = FileSize(romPath.Split('#')[0])
        };
    }

    // Les fichiers d'un dossier ; un dossier absent (disque débranché) ou interdit est simplement sauté.
    protected static IEnumerable<string> EnumerateFiles(string folder, bool recursive = true)
    {
        if (!Directory.Exists(folder))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(folder, "*", new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            MaxRecursionDepth = MaxFolderDepth,
            IgnoreInaccessible = true
        });
    }

    protected static string UserFolder(Environment.SpecialFolder folder, params string[] parts)
    {
        return Path.Combine(new[] { Environment.GetFolderPath(folder) }.Concat(parts).ToArray());
    }

    private static long FileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
