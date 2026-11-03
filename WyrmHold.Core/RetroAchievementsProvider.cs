using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Wyrmhold.Core;

/// <summary>
/// Les succès RetroAchievements d'un émulateur. Il y a une instance par émulateur (RetroArch, Dolphin), car
/// LibraryService range une source par plateforme ; elles partagent le même compte et les mêmes listes.
/// Un succès compte en mode normal comme en hardcore (choix de Val du 2 novembre 2026).
/// </summary>
public class RetroAchievementsProvider : IAchievementProvider
{
    // Adresse vérifiée le 2 novembre 2026 : l'image en couleur, et la version grisée avec « _lock ».
    private const string BadgeBaseUrl = "https://media.retroachievements.org/Badge/";

    // Suivi en direct : les succès des dernières 24 h, avec une minute de marge si l'horloge du PC retarde.
    private const long RecentUnlockSeconds = 24 * 60 * 60;
    private const long ClockMarginSeconds = 60;

    private readonly Secrets _secrets;
    private readonly RetroAchievementsApi _api;
    private readonly RetroAchievementsCatalog _catalog;

    public RetroAchievementsProvider(Platform platform, Secrets secrets, RetroAchievementsApi api, RetroAchievementsCatalog catalog)
    {
        Platform = platform;
        _secrets = secrets;
        _api = api;
        _catalog = catalog;
    }

    public Platform Platform { get; }

    public bool CanHaveAchievements(Game game)
    {
        return _secrets.HasRetroAchievements && game.CanHaveRetroAchievements && game.RetroAchievementsGameId > 0;
    }

    public async Task<AchievementProgress> GetProgressAsync(Game game, CancellationToken cancellationToken = default)
    {
        Dictionary<int, RetroAchievementsCompletion> completion = await ReadAsync(() => _catalog.GetCompletionAsync(cancellationToken));

        // Un jeu jamais lancé n'a pas de ligne : son total vient de la liste de la console.
        completion.TryGetValue(game.RetroAchievementsGameId, out RetroAchievementsCompletion? mine);

        if (mine is { MaxPossible: > 0 })
        {
            return new AchievementProgress(Math.Max(mine.Unlocked, mine.UnlockedHardcore), mine.MaxPossible);
        }

        List<RetroAchievementsGame> games = await ReadAsync(() => _catalog.GetGamesAsync(game.Console, cancellationToken));

        return new AchievementProgress(0, games.FirstOrDefault(entry => entry.Id == game.RetroAchievementsGameId)?.AchievementCount ?? 0);
    }

    public async Task<List<AchievementDetail>> GetAchievementsAsync(Game game, CancellationToken cancellationToken = default)
    {
        if (!CanHaveAchievements(game))
        {
            return new List<AchievementDetail>();
        }

        RetroAchievementsGameProgress progress =
            await ReadAsync(() => _api.GetGameProgressAsync(game.RetroAchievementsGameId, _api.User, cancellationToken));

        return ToDetails(progress);
    }

    /// <summary>
    /// Pour le suivi en direct : seulement les succès récents de ce jeu, ce qui évite de relire toute sa liste
    /// plusieurs fois par minute. Le suivi cherche ce qui est nouveau, il n'a pas besoin des anciens.
    /// </summary>
    public async Task<HashSet<string>> GetUnlockedIdsAsync(Game game, CancellationToken cancellationToken = default)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        List<RetroAchievementsEarned> earned = await ReadAsync(
            () => _api.GetEarnedBetweenAsync(_api.User, now - RecentUnlockSeconds, now + ClockMarginSeconds, cancellationToken));

        return earned
            .Where(entry => entry.GameId == game.RetroAchievementsGameId)
            .Select(entry => entry.AchievementId.ToString(CultureInfo.InvariantCulture))
            .ToHashSet();
    }

    /// <summary>
    /// Transforme la réponse du site en succès, sous la même forme que les autres plateformes.
    /// </summary>
    public static List<AchievementDetail> ToDetails(RetroAchievementsGameProgress progress)
    {
        // Un jeu sans succès peut répondre par une liste vide au lieu d'un dictionnaire.
        if (progress.Achievements.ValueKind != JsonValueKind.Object)
        {
            return new List<AchievementDetail>();
        }

        List<AchievementDetail> details = new List<AchievementDetail>();

        foreach (JsonProperty property in progress.Achievements.EnumerateObject())
        {
            RetroAchievementsAchievement? entry = property.Value.Deserialize<RetroAchievementsAchievement>(RetroAchievementsApi.ReadOptions);

            if (entry is null)
            {
                continue;
            }

            bool isUnlocked = entry.DateEarned is not null || entry.DateEarnedHardcore is not null;

            details.Add(new AchievementDetail
            {
                Id = entry.Id.ToString(CultureInfo.InvariantCulture),
                Name = entry.Title,
                Description = entry.Description ?? "",
                IsUnlocked = isUnlocked,
                IsHardcore = entry.DateEarnedHardcore is not null,
                UnlockedUnix = ParseDate(entry.DateEarnedHardcore ?? entry.DateEarned),
                IconUrl = entry.BadgeName is null ? null : $"{BadgeBaseUrl}{entry.BadgeName}{(isUnlocked ? "" : "_lock")}.png",
                RarityPercent = progress.PlayerCount > 0 ? 100.0 * entry.AwardedCount / progress.PlayerCount : null,
                Order = entry.DisplayOrder
            });
        }

        return details;
    }

    // Ces dates n'indiquent pas de fuseau horaire : elles sont en UTC (succès repéré 5 s après son déblocage, le 3 novembre 2026).
    private static long ParseDate(string? date)
    {
        return DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime parsed)
            ? new DateTimeOffset(parsed).ToUnixTimeSeconds()
            : 0;
    }

    // Une clé refusée vaut pour tous les jeux : on arrête la source au lieu d'essayer les suivants.
    private static async Task<T> ReadAsync<T>(Func<Task<T>> read)
    {
        try
        {
            return await read();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new AchievementSourceUnavailableException("RetroAchievements refuse la clé : reconnecte ton compte dans l'onglet Comptes.");
        }
    }
}
