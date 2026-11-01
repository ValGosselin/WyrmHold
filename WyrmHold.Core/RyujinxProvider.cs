using System.Globalization;
using System.Text.Json;

namespace Wyrmhold.Core;

/// <summary>
/// Ryujinx (fork Ryubing) : Switch. Vérifié le 1er novembre 2026 sur Ryujinx 1.3.3 :
/// - lancement : « Ryujinx.exe &lt;jeu&gt; » (essai réel : son journal montre l'ouverture du chemin donné) ;
/// - données : dossier « portable » à côté du programme s'il existe, sinon %APPDATA%\Ryujinx ;
/// - Config.json, clé « game_dirs » : les dossiers de jeux ;
/// - games\&lt;titleid&gt;\gui\metadata.json : le titre, le temps joué et la dernière partie de chaque jeu déjà ouvert.
/// Le titre et l'icône d'un NSP sont chiffrés : sans passer par les clés de la console, le titre vient de
/// metadata.json (jeu déjà ouvert une fois dans Ryujinx), sinon du nom du fichier.
/// </summary>
public class RyujinxProvider : EmulatorProvider
{
    public const string Executable = "Ryujinx.exe";

    private const string PortableFolderName = "portable";

    private static readonly HashSet<string> GameExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".nsp", ".xci"
    };

    private record KnownGame(string TitleId, string Title, int PlayedMinutes, long LastPlayedUnix);

    public RyujinxProvider(AppSettings settings) : base(settings, Platform.Ryujinx)
    {
    }

    public override string ExecutableName => Executable;

    protected override IEnumerable<string> KnownFolders()
    {
        yield return UserFolder(Environment.SpecialFolder.ProgramFiles, "Ryujinx");
        yield return UserFolder(Environment.SpecialFolder.LocalApplicationData, "Programs", "Ryujinx");
    }

    protected override List<string> ReadConfiguredFolders(string executablePath)
    {
        string configPath = Path.Combine(FindDataFolder(executablePath), "Config.json");

        if (!File.Exists(configPath))
        {
            return new List<string>();
        }

        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(configPath));

        return config.RootElement.TryGetProperty("game_dirs", out JsonElement folders)
            ? folders.EnumerateArray().Select(folder => folder.GetString()).OfType<string>().ToList()
            : new List<string>();
    }

    protected override List<Game> ScanGames(string executablePath, List<string> folders)
    {
        Dictionary<string, KnownGame> knownGames = ReadKnownGames(FindDataFolder(executablePath));
        List<Game> games = new List<Game>();

        foreach (string path in folders.SelectMany(folder => EnumerateFiles(folder)))
        {
            if (!GameExtensions.Contains(Path.GetExtension(path)))
            {
                continue;
            }

            string fileName = Path.GetFileNameWithoutExtension(path);
            string cleanName = NameTools.CleanRomName(fileName);

            SwitchPackageInfo? package = SwitchPackageReader.TryRead(path);
            string? titleId = package?.TitleId
                ?? NameTools.FindSwitchTitleId(fileName)
                ?? knownGames.Values.FirstOrDefault(known => NameTools.Normalize(known.Title) == NameTools.Normalize(cleanName))?.TitleId;

            // Une mise à jour ou un DLC se range dans le même dossier, mais ce n'est pas un jeu à montrer.
            if (package?.IsBaseGame == false || (titleId is not null && !SwitchPackageReader.IsBaseTitleId(titleId)))
            {
                continue;
            }

            KnownGame? known = titleId is null ? null : knownGames.GetValueOrDefault(titleId);
            Game game = CreateGame(titleId ?? $"file-{NameTools.ToSlug(fileName)}", known?.Title ?? cleanName, GameConsole.Switch, path);

            // Le temps compté par Ryujinx lui-même (la base garde le plus grand des deux chiffres).
            game.PlaytimeMinutes = known?.PlayedMinutes ?? 0;
            game.LastPlayedUnix = known?.LastPlayedUnix ?? 0;
            games.Add(game);
        }

        return games;
    }

    protected override IEnumerable<string> BuildArguments(Game game, string executablePath)
    {
        return new[] { game.InstallPath ?? "" };
    }

    private static string FindDataFolder(string executablePath)
    {
        string portableFolder = Path.Combine(Path.GetDirectoryName(executablePath) ?? "", PortableFolderName);

        return Directory.Exists(portableFolder)
            ? portableFolder
            : UserFolder(Environment.SpecialFolder.ApplicationData, "Ryujinx");
    }

    private static Dictionary<string, KnownGame> ReadKnownGames(string dataFolder)
    {
        Dictionary<string, KnownGame> games = new Dictionary<string, KnownGame>(StringComparer.OrdinalIgnoreCase);
        string gamesFolder = Path.Combine(dataFolder, "games");

        if (!Directory.Exists(gamesFolder))
        {
            return games;
        }

        foreach (string folder in Directory.GetDirectories(gamesFolder))
        {
            string metadataPath = Path.Combine(folder, "gui", "metadata.json");

            if (!File.Exists(metadataPath))
            {
                continue;
            }

            try
            {
                using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
                string titleId = Path.GetFileName(folder).ToLowerInvariant();

                if (ReadKnownGame(titleId, metadata.RootElement) is KnownGame known)
                {
                    games[titleId] = known;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Logger.Log($"Ryujinx : {metadataPath} illisible : {ex.Message}");
            }
        }

        return games;
    }

    private static KnownGame? ReadKnownGame(string titleId, JsonElement metadata)
    {
        if (!metadata.TryGetProperty("title", out JsonElement title) || title.GetString() is not string name || name.Length == 0)
        {
            return null;
        }

        int minutes = metadata.TryGetProperty("timespan_played", out JsonElement played)
            && TimeSpan.TryParse(played.GetString(), CultureInfo.InvariantCulture, out TimeSpan playedTime)
            ? (int)playedTime.TotalMinutes
            : 0;

        long lastPlayed = metadata.TryGetProperty("last_played_utc", out JsonElement last)
            && last.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(last.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset lastTime)
            ? lastTime.ToUnixTimeSeconds()
            : 0;

        return new KnownGame(titleId, name, minutes, lastPlayed);
    }
}
