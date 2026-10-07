using System.Collections.Concurrent;
using System.Globalization;

namespace Wyrmhold.Core;

/// <summary>
/// La session Epic utilisée pour lire tes succès : ton jeton, ton identifiant, et jusqu'à quand le jeton est valable.
/// </summary>
internal record EpicSession(string AccessToken, string AccountId, string? RefreshToken, DateTimeOffset ExpiresAt);

internal class EpicAchievementProvider : IAchievementProvider
{
    private readonly EpicCatalogCache _catalog;
    private EpicSession? _session;

    // La liste des succès d'un jeu ne change presque jamais : on la garde pendant la session.
    // ConcurrentDictionary, car plusieurs jeux sont lus en même temps (Parallel.ForEachAsync).
    private readonly ConcurrentDictionary<string, EpicProductAchievements?> _schemas =
        new ConcurrentDictionary<string, EpicProductAchievements?>();

    public EpicAchievementProvider(EpicCatalogCache catalog)
    {
        _catalog = catalog;
    }

    public Platform Platform => Platform.Epic;

    public EpicSession? Session => _session;

    public void SetSession(EpicSession? session)
    {
        _session = session;
    }

    public bool CanHaveAchievements(Game game)
    {
        return _session is not null && FindNamespace(game) is not null;
    }

    public async Task<AchievementProgress> GetProgressAsync(Game game, CancellationToken cancellationToken = default)
    {
        List<AchievementDetail> achievements = await GetAchievementsAsync(game, cancellationToken);
        return new AchievementProgress(achievements.Count(a => a.IsUnlocked), achievements.Count);
    }

    public async Task<List<AchievementDetail>> GetAchievementsAsync(Game game, CancellationToken cancellationToken = default)
    {
        EpicSession session = _session
            ?? throw new AchievementSourceUnavailableException("Compte Epic non connecté.");

        string sandboxId = FindNamespace(game)
            ?? throw new InvalidOperationException($"Namespace Epic inconnu pour {game.Name}.");

        // 1. La liste des succès du jeu (publique), gardée en mémoire après la 1re lecture.
        if (!_schemas.TryGetValue(sandboxId, out EpicProductAchievements? schema))
        {
            schema = await EpicApi.GetAchievementSchemaAsync(sandboxId, cancellationToken);
            _schemas[sandboxId] = schema;
        }

        if (schema is null || string.IsNullOrEmpty(schema.ProductId) || schema.Achievements.Count == 0)
        {
            return new List<AchievementDetail>();   // jeu sans succès
        }

        // 2. Tes succès pour ce jeu.
        Dictionary<string, EpicPlayerAchievement> player = await EpicApi.GetPlayerAchievementsAsync(
            session.AccountId, schema.ProductId, session.AccessToken, cancellationToken);

        // 3. On assemble les deux, dans l'ordre de la liste du jeu.
        List<AchievementDetail> details = new List<AchievementDetail>();
        List<EpicAchievementInfo> infos = schema.Achievements
            .Select(wrapper => wrapper.Achievement)
            .OfType<EpicAchievementInfo>()
            .Where(info => !string.IsNullOrEmpty(info.Name))
            .ToList();

        for (int i = 0; i < infos.Count; i++)
        {
            EpicAchievementInfo info = infos[i];
            player.TryGetValue(info.Name!, out EpicPlayerAchievement? mine);
            bool isUnlocked = mine?.Unlocked == true;

            // L'avancement d'Epic est un pourcentage entre 0 et 1 (d'après son SDK).
            // Par prudence, une valeur au-dessus de 1 est lue comme un pourcentage sur 100.
            double? progress = mine?.Progress;
            double? progressMax = progress is > 1 ? 100 : 1;
            bool hasProgress = !isUnlocked && progress is > 0;

            details.Add(new AchievementDetail
            {
                Id = info.Name!,
                Name = (info.DisplayName ?? info.Name!).Trim(),
                Description = (info.Description ?? "").Trim(),
                IsUnlocked = isUnlocked,
                UnlockedUnix = ParseDate(mine?.UnlockDate),
                IsHidden = info.Hidden,
                IconUrl = isUnlocked ? info.UnlockedIconUrl : info.LockedIconUrl,
                RarityPercent = info.Rarity?.Percent,
                Order = i,
                ProgressValue = hasProgress ? progress : null,
                ProgressMax = hasProgress ? progressMax : null,
                ProgressIsPercent = true
            });
        }

        return details;
    }

    private string? FindNamespace(Game game)
    {
        string? sandboxId = _catalog.Find(game.PlatformGameId)?.Namespace;
        return string.IsNullOrEmpty(sandboxId) ? null : sandboxId;
    }

    private static long ParseDate(string? date)
    {
        return DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
            ? parsed.ToUnixTimeSeconds()
            : 0;
    }
}
