namespace Wyrmhold.Core;

public class SteamAchievementProvider : IAchievementProvider
{
    private readonly SteamWebApi _api;

    // Les jeux qui ont une page de statistiques (has_community_visible_stats de GetOwnedGames).
    private readonly HashSet<string> _appsWithStats = new HashSet<string>();

    public SteamAchievementProvider(SteamWebApi api)
    {
        _api = api;
    }

    public Platform Platform => Platform.Steam;

    /// <summary>
    /// Appelé à chaque synchronisation du compte Steam, avec la liste fraîche de GetOwnedGames.
    /// </summary>
    public void SetAppsWithStats(IEnumerable<string> appIds)
    {
        _appsWithStats.Clear();
        _appsWithStats.UnionWith(appIds);
    }

    public bool CanHaveAchievements(Game game)
    {
        if (!_api.IsConfigured)
        {
            return false;
        }

        // Les jeux de la famille ne sont pas dans GetOwnedGames : on ne sait pas s'ils ont des succès,
        // donc on les essaie seulement si tu y as joué.
        return _appsWithStats.Contains(game.PlatformGameId)
            || (game.IsFamilyShared == true && game.PlaytimeMinutes > 0);
    }

    public Task<AchievementProgress> GetProgressAsync(Game game, CancellationToken cancellationToken = default)
    {
        return _api.GetAchievementProgressAsync(game.PlatformGameId, cancellationToken);
    }

    public Task<List<AchievementDetail>> GetAchievementsAsync(Game game, CancellationToken cancellationToken = default)
    {
        return _api.GetAchievementDetailsAsync(game.PlatformGameId, cancellationToken);
    }

    // Plus léger que la version par défaut : la liste complète demande 4 à 5 appels à Steam, ceci un seul.
    public Task<HashSet<string>> GetUnlockedIdsAsync(Game game, CancellationToken cancellationToken = default)
    {
        return _api.GetUnlockedAchievementIdsAsync(game.PlatformGameId, cancellationToken);
    }
}
