using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Wyrmhold.Core;

/// <summary>
/// RetroArch : Game Boy, Game Boy Color, Game Boy Advance et DS. Vérifié le 1er novembre 2026 sur RetroArch 1.22.2 :
/// - lancement : « retroarch.exe -L &lt;cœur&gt; &lt;jeu&gt; » (--help). RetroArch ne contient aucun cœur après
///   son installation : ils se téléchargent depuis son menu (Mise à jour en ligne → Téléchargeur de cœurs) ;
/// - retroarch.cfg, à côté du programme : playlist_directory = ":\playlists", libretro_directory = ":\cores"
///   (« : » = le dossier du programme), rgui_browser_directory = "default" si aucun dossier n'est choisi ;
/// - playlists .lpl (JSON) : une liste « items » avec path, label, core_path et db_name. Un jeu dans une archive
///   s'écrit « jeux.zip#jeu.gba », et core_path vaut « DETECT » si aucun cœur n'est choisi.
/// </summary>
public class RetroArchProvider : EmulatorProvider
{
    public const string Executable = "retroarch.exe";

    private const string DetectValue = "DETECT";
    private const string DefaultValue = "default";
    private const string CoreFileSuffix = "_libretro.dll";

    // Les playlists que RetroArch écrit lui-même (historique, favoris…) : ce ne sont pas des bibliothèques.
    private const string BuiltInPlaylistPrefix = "content_";

    private static readonly string QuietConfigPath = Path.Combine(AppPaths.DataFolder, "RetroAchievements", "retroarch-sans-notifications.cfg");

    private static readonly Dictionary<string, GameConsole> ConsoleByExtension = new Dictionary<string, GameConsole>(StringComparer.OrdinalIgnoreCase)
    {
        { ".gb", GameConsole.GameBoy },
        { ".gbc", GameConsole.GameBoyColor },
        { ".gba", GameConsole.GameBoyAdvance },
        { ".nds", GameConsole.NintendoDs }
    };

    private static readonly Dictionary<string, GameConsole> ConsoleByPlaylist = new Dictionary<string, GameConsole>(StringComparer.OrdinalIgnoreCase)
    {
        { "Nintendo - Game Boy", GameConsole.GameBoy },
        { "Nintendo - Game Boy Color", GameConsole.GameBoyColor },
        { "Nintendo - Game Boy Advance", GameConsole.GameBoyAdvance },
        { "Nintendo - Nintendo DS", GameConsole.NintendoDs }
    };

    // Les cœurs essayés pour chaque console, dans cet ordre (noms de fichiers vérifiés dans les définitions
    // d'émulateurs de Playnite). D'abord ceux que RetroAchievements déclare compatibles (liste lue le
    // 2 novembre 2026) : sameboy et gpsp n'y sont pas, ils ne servent que si aucun autre n'est installé.
    private static readonly Dictionary<GameConsole, string[]> DefaultCores = new Dictionary<GameConsole, string[]>
    {
        { GameConsole.GameBoy, new[] { "gambatte_libretro.dll", "mgba_libretro.dll", "sameboy_libretro.dll" } },
        { GameConsole.GameBoyColor, new[] { "gambatte_libretro.dll", "mgba_libretro.dll", "sameboy_libretro.dll" } },
        { GameConsole.GameBoyAdvance, new[] { "mgba_libretro.dll", "gpsp_libretro.dll" } },
        { GameConsole.NintendoDs, new[] { "melondsds_libretro.dll", "melonds_libretro.dll", "desmume_libretro.dll" } }
    };

    /// <summary>
    /// Les consoles de RetroArch, dans l'ordre de l'écran de réglages.
    /// </summary>
    public static IReadOnlyList<GameConsole> Consoles { get; } = DefaultCores.Keys.ToList();

    /// <summary>
    /// Une ligne de playlist.
    /// </summary>
    public record PlaylistItem(string Path, string? Label, string? CorePath, string? DatabaseName);

    // Le cœur choisi dans une playlist RetroArch, par identifiant de jeu (rempli à chaque lecture).
    private readonly ConcurrentDictionary<string, string> _playlistCores = new ConcurrentDictionary<string, string>();

    public RetroArchProvider(AppSettings settings) : base(settings, Platform.RetroArch)
    {
    }

    public override string ExecutableName => Executable;

    protected override IEnumerable<string> KnownFolders()
    {
        yield return @"C:\RetroArch-Win64";
        yield return @"C:\RetroArch";
        yield return UserFolder(Environment.SpecialFolder.ProgramFiles, "RetroArch");

        foreach (string steamapps in SteamProvider.GetSteamappsFolders())
        {
            yield return Path.Combine(steamapps, "common", "RetroArch");
        }
    }

    protected override List<string> ReadConfiguredFolders(string executablePath)
    {
        string? folder = ReadConfigFolder(executablePath, "rgui_browser_directory", null);

        return folder is null ? new List<string>() : new List<string> { folder };
    }

    /// <summary>
    /// Les cœurs présents dans le dossier de RetroArch (nom du fichier, ex. « mgba_libretro.dll »).
    /// </summary>
    public List<string> GetInstalledCores()
    {
        if (FindExecutable() is not string executablePath)
        {
            return new List<string>();
        }

        return EnumerateFiles(FindCoresFolder(executablePath), recursive: false)
            .Where(path => path.EndsWith(CoreFileSuffix, StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetFileName(path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    protected override List<Game> ScanGames(string executablePath, List<string> folders)
    {
        _playlistCores.Clear();

        List<Game> games = ScanPlaylists(executablePath);

        foreach (string path in folders.SelectMany(folder => EnumerateFiles(folder)))
        {
            string extension = Path.GetExtension(path);

            if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                // Dans une archive, c'est le jeu à l'intérieur qui donne la console.
                if (FindGameInArchive(path) is string entryName)
                {
                    AddGame(games, $"{path}#{entryName}", null, null);
                }
            }
            else if (ConsoleByExtension.ContainsKey(extension))
            {
                AddGame(games, path, null, null);
            }
        }

        return games;
    }

    protected override IEnumerable<string> BuildArguments(Game game, string executablePath)
    {
        // --load-menu-on-error : si le jeu ne charge pas, le menu de RetroArch explique pourquoi (sinon il se ferme sans rien dire).
        List<string> arguments = new List<string> { "-L", FindCore(game, executablePath), "--load-menu-on-error" };

        if (WyrmholdAnnouncesAchievements(game))
        {
            arguments.Add($"--appendconfig={WriteQuietConfig()}");
        }

        arguments.Add(game.InstallPath ?? "");
        return arguments;
    }

    /// <summary>
    /// Le fichier ajouté à la configuration de RetroArch pour ce lancement : pas de notification de succès.
    /// Vérifié le 2 novembre 2026 sur une copie de retroarch.cfg : sans la ligne config_save_on_exit, RetroArch recopie
    /// ces valeurs dans sa configuration en quittant ; avec elle, le fichier reste identique octet par octet.
    /// Conséquence : un réglage changé dans le menu de RetroArch pendant cette partie n'est pas gardé.
    /// </summary>
    private static string WriteQuietConfig()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(QuietConfigPath)!);
        File.WriteAllLines(QuietConfigPath, new[]
        {
            "config_save_on_exit = \"false\"",
            "cheevos_visibility_unlock = \"false\"",
            "cheevos_visibility_mastery = \"false\""
        });

        return QuietConfigPath;
    }

    /// <summary>
    /// Lit une playlist .lpl. Une playlist à l'ancien format (texte, avant RetroArch 1.7.5) n'est pas lue.
    /// </summary>
    public static List<PlaylistItem> ParsePlaylist(string json)
    {
        using JsonDocument playlist = JsonDocument.Parse(json);

        if (!playlist.RootElement.TryGetProperty("items", out JsonElement items))
        {
            return new List<PlaylistItem>();
        }

        return items.EnumerateArray()
            .Where(item => !string.IsNullOrEmpty(ReadText(item, "path")))
            .Select(item => new PlaylistItem(ReadText(item, "path")!, ReadText(item, "label"), ReadText(item, "core_path"), ReadText(item, "db_name")))
            .ToList();
    }

    private static string? ReadText(JsonElement item, string key)
    {
        return item.TryGetProperty(key, out JsonElement value) ? value.GetString() : null;
    }

    private List<Game> ScanPlaylists(string executablePath)
    {
        string? playlistsFolder = ReadConfigFolder(executablePath, "playlist_directory", "playlists");
        List<Game> games = new List<Game>();

        if (playlistsFolder is null)
        {
            return games;
        }

        foreach (string playlistPath in EnumerateFiles(playlistsFolder, recursive: false))
        {
            string playlistName = Path.GetFileNameWithoutExtension(playlistPath);

            if (!playlistPath.EndsWith(".lpl", StringComparison.OrdinalIgnoreCase)
                || playlistName.StartsWith(BuiltInPlaylistPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                foreach (PlaylistItem item in ParsePlaylist(File.ReadAllText(playlistPath)))
                {
                    string database = Path.GetFileNameWithoutExtension(item.DatabaseName ?? playlistName);
                    GameConsole? console = ConsoleByPlaylist.TryGetValue(database, out GameConsole fromDatabase) ? fromDatabase
                        : ConsoleByPlaylist.TryGetValue(playlistName, out GameConsole fromName) ? fromName
                        : null;

                    if (File.Exists(item.Path.Split('#')[0]))
                    {
                        AddGame(games, item.Path, item.Label, console, ResolveFolder(item.CorePath, executablePath));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Logger.Log($"RetroArch : playlist illisible ({playlistName}) : {ex.Message}");
            }
        }

        return games;
    }

    private void AddGame(List<Game> games, string path, string? label, GameConsole? playlistConsole, string? corePath = null)
    {
        string[] parts = path.Split('#');

        // L'extension du jeu : celle du fichier dans l'archive s'il y en a un.
        string extension = Path.GetExtension(parts[^1]);
        GameConsole? console = ConsoleByExtension.TryGetValue(extension, out GameConsole found) ? found : playlistConsole;

        if (console is not GameConsole gameConsole)
        {
            return;
        }

        // Le nom de l'archive pour un jeu archivé : la playlist et le dossier donnent alors le même identifiant.
        string fileName = Path.GetFileNameWithoutExtension(parts[0]);
        string gameId = $"{GameConsoles.ShortName(gameConsole).ToLowerInvariant()}-{NameTools.ToSlug(fileName)}";

        games.Add(CreateGame(gameId, FindName(path, label, fileName, gameConsole), gameConsole, path));

        if (corePath is not null)
        {
            _playlistCores[gameId] = corePath;
        }
    }

    // Le titre de la playlist vient de la base de RetroArch : c'est le meilleur. Sinon, sur DS, le titre de la cartouche.
    // Sur Game Boy et GBA, le nom interne est tronqué et en majuscules (« POKEMON EMER ») : le nom du fichier est meilleur.
    private static string FindName(string path, string? label, string fileName, GameConsole console)
    {
        if (!string.IsNullOrWhiteSpace(label))
        {
            return NameTools.CleanRomName(label);
        }

        string? title = console == GameConsole.NintendoDs && !path.Contains('#') ? DsRomHeader.TryReadTitle(path) : null;
        return title ?? NameTools.CleanRomName(fileName);
    }

    private static string? FindGameInArchive(string path)
    {
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(path);

            return archive.Entries
                .FirstOrDefault(entry => ConsoleByExtension.ContainsKey(Path.GetExtension(entry.Name)))?
                .FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Logger.Log($"RetroArch : archive illisible ({Path.GetFileName(path)}) : {ex.Message}");
            return null;
        }
    }

    // Le cœur de ce jeu : celui choisi dans Wyrmhold, sinon celui de la playlist, sinon le premier installé de la liste.
    private string FindCore(Game game, string executablePath)
    {
        string coresFolder = FindCoresFolder(executablePath);
        List<string> candidates = new List<string>();

        if (Settings.Cores.TryGetValue(game.Console.ToString(), out string? chosen))
        {
            candidates.Add(Path.Combine(coresFolder, chosen));
        }

        if (_playlistCores.TryGetValue(game.PlatformGameId, out string? fromPlaylist))
        {
            candidates.Add(fromPlaylist);
        }

        candidates.AddRange(DefaultCores.GetValueOrDefault(game.Console, Array.Empty<string>())
            .Select(core => Path.Combine(coresFolder, core)));

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new EmulatorLaunchException(
                $"Aucun cœur RetroArch n'est installé pour {game.ConsoleName}.\n\n"
                + "Ouvre RetroArch → Mise à jour en ligne (Online Updater) → Téléchargeur de cœurs (Core Downloader), "
                + "puis installe par exemple mGBA (Game Boy Advance), Gambatte (Game Boy) ou melonDS (DS).");
    }

    private string FindCoresFolder(string executablePath)
    {
        return ReadConfigFolder(executablePath, "libretro_directory", "cores")
            ?? Path.Combine(Path.GetDirectoryName(executablePath) ?? "", "cores");
    }

    // Un dossier de retroarch.cfg. Absent ou « default » : le dossier par défaut à côté du programme (ou null).
    private static string? ReadConfigFolder(string executablePath, string key, string? defaultFolder)
    {
        string? value = FindConfigPath(executablePath) is string configPath && File.Exists(configPath)
            ? ReadConfigValue(File.ReadLines(configPath), key)
            : null;

        if (string.IsNullOrEmpty(value) || value == DefaultValue)
        {
            return defaultFolder is null ? null : Path.Combine(Path.GetDirectoryName(executablePath) ?? "", defaultFolder);
        }

        return ResolveFolder(value, executablePath);
    }

    /// <summary>
    /// La valeur d'une ligne « clé = "valeur" » de retroarch.cfg, ou null.
    /// </summary>
    public static string? ReadConfigValue(IEnumerable<string> lines, string key)
    {
        foreach (string line in lines)
        {
            Match match = Regex.Match(line, $@"^\s*{Regex.Escape(key)}\s*=\s*""?(.*?)""?\s*$");

            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    // Le fichier écrit à côté du programme (vérifié), ou dans AppData pour les anciennes versions.
    private static string? FindConfigPath(string executablePath)
    {
        return new[]
        {
            Path.Combine(Path.GetDirectoryName(executablePath) ?? "", "retroarch.cfg"),
            UserFolder(Environment.SpecialFolder.ApplicationData, "RetroArch", "retroarch.cfg")
        }.FirstOrDefault(File.Exists);
    }

    // « :\cores » devient « <dossier du programme>\cores ». DETECT = rien de choisi.
    private static string? ResolveFolder(string? value, string executablePath)
    {
        if (string.IsNullOrEmpty(value) || value == DetectValue)
        {
            return null;
        }

        return value.StartsWith(':')
            ? (Path.GetDirectoryName(executablePath) ?? "") + value.Substring(1)
            : value;
    }
}
