using System.Text.Json;

namespace Wyrmhold.Core;

/// <summary>
/// Comment un jeu émulé a été associé à un jeu RetroAchievements.
/// Hash = sa version est reconnue. Name = trouvé par son nom seulement : l'émulateur ne reconnaîtra sans doute
/// pas cette version. Manual = choisi à la main (numéro 0 = « aucun jeu »), jamais remplacé automatiquement.
/// </summary>
public enum RetroAchievementsMatch
{
    None,
    Hash,
    Name,
    Manual
}

/// <summary>
/// Les jeux de RetroAchievements par console, gardés sur le disque (dossier RetroAchievements) et relus chaque
/// semaine : la documentation demande ce cache, car ces listes sont longues et changent peu.
/// Si le site ne répond pas, l'ancienne liste sert encore.
/// </summary>
public class RetroAchievementsCatalog
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private static readonly string CacheFolder = Path.Combine(AppPaths.DataFolder, "RetroAchievements");

    private readonly RetroAchievementsApi _api;

    // Par numéro de console : la liste déjà lue, et la date à laquelle le site l'a donnée.
    private readonly Dictionary<int, (List<RetroAchievementsGame> Games, DateTime ReadUtc)> _lists =
        new Dictionary<int, (List<RetroAchievementsGame>, DateTime)>();

    // Une lecture à la fois : deux demandes en même temps téléchargeraient deux fois la même liste.
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

    // La progression de tous les jeux du compte : une lecture sert à tous les jeux qui la demandent juste après.
    private static readonly TimeSpan CompletionMaxAge = TimeSpan.FromMinutes(1);
    private Dictionary<int, RetroAchievementsCompletion> _completion = new Dictionary<int, RetroAchievementsCompletion>();
    private DateTime _completionReadUtc = DateTime.MinValue;

    public RetroAchievementsCatalog(RetroAchievementsApi api)
    {
        _api = api;
    }

    /// <summary>
    /// La progression du compte, par numéro de jeu. Évite une demande au site par jeu émulé.
    /// </summary>
    public async Task<Dictionary<int, RetroAchievementsCompletion>> GetCompletionAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (DateTime.UtcNow - _completionReadUtc >= CompletionMaxAge)
            {
                List<RetroAchievementsCompletion> games = await _api.GetUserCompletionAsync(_api.User, cancellationToken);

                _completion = games.GroupBy(game => game.GameId).ToDictionary(group => group.Key, group => group.First());
                _completionReadUtc = DateTime.UtcNow;
            }

            return _completion;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Tous les jeux possibles pour cette console. Lance une erreur seulement s'il n'y a aucune liste, même ancienne.
    /// </summary>
    public async Task<List<RetroAchievementsGame>> GetGamesAsync(GameConsole console, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            List<RetroAchievementsGame> games = new List<RetroAchievementsGame>();

            foreach (int consoleId in RetroAchievementsApi.GetConsoleIds(console))
            {
                games.AddRange(await GetListAsync(consoleId, cancellationToken));
            }

            return games;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Le jeu RetroAchievements d'un jeu émulé : par son empreinte d'abord, sinon par son nom exact.
    /// Les noms sont comparés sans accents, sans ponctuation et sans balises : pas de ressemblance approximative,
    /// un mauvais jeu serait pire que pas de jeu.
    /// </summary>
    public static (int GameId, RetroAchievementsMatch Match) FindMatch(Game game, IReadOnlyList<RetroAchievementsGame> games)
    {
        if (game.RetroAchievementsHash is string hash && games.FirstOrDefault(entry => entry.Accepts(hash)) is RetroAchievementsGame byHash)
        {
            return (byHash.Id, RetroAchievementsMatch.Hash);
        }

        HashSet<string> names = new HashSet<string>
        {
            SimplifyTitle(game.Name),
            SimplifyTitle(Path.GetFileNameWithoutExtension(game.RomFilePath ?? ""))
        };

        RetroAchievementsGame? byName = games.FirstOrDefault(entry => IsMainSet(entry) && names.Contains(SimplifyTitle(entry.Title)));

        return byName is null ? (0, RetroAchievementsMatch.None) : (byName.Id, RetroAchievementsMatch.Name);
    }

    // « ~Hack~ Pokémon Machin » ou « [Subset - Bonus] » : des variantes, à ne jamais choisir d'après le nom.
    private static bool IsMainSet(RetroAchievementsGame game)
    {
        return !game.Title.StartsWith('~') && !game.Title.Contains("[Subset", StringComparison.OrdinalIgnoreCase);
    }

    private static string SimplifyTitle(string title)
    {
        return NameTools.Normalize(NameTools.RemoveDiacritics(NameTools.CleanRomName(title)));
    }

    private async Task<List<RetroAchievementsGame>> GetListAsync(int consoleId, CancellationToken cancellationToken)
    {
        if (_lists.TryGetValue(consoleId, out (List<RetroAchievementsGame> Games, DateTime ReadUtc) known)
            && DateTime.UtcNow - known.ReadUtc < MaxAge)
        {
            return known.Games;
        }

        string path = Path.Combine(CacheFolder, $"console-{consoleId}.json");
        List<RetroAchievementsGame>? onDisk = ReadFile(path);
        DateTime fileDate = File.GetLastWriteTimeUtc(path);

        if (onDisk is not null && DateTime.UtcNow - fileDate < MaxAge)
        {
            _lists[consoleId] = (onDisk, fileDate);
            return onDisk;
        }

        try
        {
            List<RetroAchievementsGame> games = await _api.GetGameListAsync(consoleId, cancellationToken);

            Directory.CreateDirectory(CacheFolder);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(games), cancellationToken);

            _lists[consoleId] = (games, DateTime.UtcNow);
            return games;
        }
        catch (Exception ex) when (onDisk is not null && ex is HttpRequestException or TaskCanceledException or AchievementSourceUnavailableException)
        {
            // Pas gardée en mémoire comme à jour : on réessaiera à la prochaine lecture.
            Logger.Log($"RetroAchievements : liste de la console {consoleId} non relue, l'ancienne sert encore : {ex.Message}");
            return onDisk!;
        }
    }

    private static List<RetroAchievementsGame>? ReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<List<RetroAchievementsGame>>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Logger.Log($"RetroAchievements : cache illisible ({Path.GetFileName(path)}) : {ex.Message}");
            return null;
        }
    }
}
