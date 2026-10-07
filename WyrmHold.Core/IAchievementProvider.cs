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
}

public record AchievementProgress(int Unlocked, int Total);

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
