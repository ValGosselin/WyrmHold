namespace Wyrmhold.Core;

/// <summary>
/// Une source de succès pour une plateforme (Steam aujourd'hui, GOG ou Epic plus tard).
/// Même principe que ILibraryProvider : ajouter une source = écrire une classe de plus.
/// </summary>
public interface IAchievementProvider
{
    Platform Platform { get; }

    /// <summary>
    /// Vrai si ce jeu peut avoir des succès : on n'interroge pas les autres (un appel par jeu, c'est cher).
    /// </summary>
    bool CanHaveAchievements(Game game);

    /// <summary>
    /// Lit la progression d'un jeu. Un jeu sans succès renvoie (0, 0).
    /// Lève AchievementSourceUnavailableException si toute la source est hors service.
    /// </summary>
    Task<AchievementProgress> GetProgressAsync(Game game, CancellationToken cancellationToken = default);

    /// <summary>
    /// La liste complète des succès du jeu, débloqués ou non (plus lent : appelé à l'ouverture de la liste).
    /// </summary>
    Task<List<AchievementDetail>> GetAchievementsAsync(Game game, CancellationToken cancellationToken = default);

    /// <summary>
    /// Les identifiants des succès débloqués, pour le suivi en direct (relu toutes les 60 s).
    /// Par défaut, on lit la liste complète ; une source peut proposer plus léger (Steam : un seul appel).
    /// </summary>
    async Task<HashSet<string>> GetUnlockedIdsAsync(Game game, CancellationToken cancellationToken = default)
    {
        List<AchievementDetail> achievements = await GetAchievementsAsync(game, cancellationToken);
        return achievements.Where(a => a.IsUnlocked).Select(a => a.Id).ToHashSet();
    }
}

public record AchievementProgress(int Unlocked, int Total)
{
    /// <summary>
    /// Les succès débloqués et leur date, pour l'historique de la page Statistiques.
    /// null = cette lecture ne donne pas les dates (succès maison) : l'historique du jeu n'est pas touché.
    /// </summary>
    public IReadOnlyList<UnlockDate>? Unlocks { get; init; }

    /// <summary>La progression d'une liste complète de succès, avec leurs dates.</summary>
    public static AchievementProgress FromDetails(List<AchievementDetail> details)
    {
        return new AchievementProgress(details.Count(d => d.IsUnlocked), details.Count)
        {
            Unlocks = details
                .Where(d => d.IsUnlocked)
                .Select(d => new UnlockDate(d.Id, d.UnlockedUnix) { Name = d.Name, RarityPercent = d.RarityPercent })
                .ToList()
        };
    }
}

/// <summary>
/// Un succès débloqué et sa date (temps Unix ; 0 = date inconnue : le succès compte comme lu, mais n'est pas dans le calendrier).
/// </summary>
public record UnlockDate(string Id, long UnlockedUnix)
{
    // Le nom du succès et le % de joueurs qui l'ont (page Statistiques, étape 3). null = pas donné par cette lecture :
    // la valeur déjà en base est gardée (la lecture légère de Steam n'a ni l'un ni l'autre).
    public string? Name { get; init; }
    public double? RarityPercent { get; init; }
}

/// <summary>
/// Un succès de l'historique, tel qu'il est en base (voir GameDatabase.LoadAchievementUnlocks).
/// </summary>
public record StoredUnlock(string GameKey, string AchievementId, long UnlockedUnix, string? Name, double? RarityPercent);

/// <summary>
/// La source entière ne répond plus (profil privé, clé refusée, trop d'appels…) :
/// inutile d'interroger les jeux suivants. Les dernières valeurs en base restent affichées.
/// </summary>
public class AchievementSourceUnavailableException : Exception
{
    public AchievementSourceUnavailableException(string message) : base(message)
    {
    }
}
