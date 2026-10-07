namespace Wyrmhold.Core;

public class GogAchievementProvider : IAchievementProvider
{
    // Le jeton du compte GOG, donné par LibraryService (null = compte non connecté).
    private GogTokens? _tokens;

    public Platform Platform => Platform.Gog;

    public void SetSession(GogTokens? tokens)
    {
        _tokens = tokens;
    }

    // GOG ne dit pas à l'avance quels jeux ont des succès : on les essaie tous une première fois,
    // puis la règle de LibraryService (joué ou mis à jour depuis) limite les appels.
    public bool CanHaveAchievements(Game game)
    {
        return _tokens is not null && _tokens.UserId.Length > 0;
    }

    public async Task<AchievementProgress> GetProgressAsync(Game game, CancellationToken cancellationToken = default)
    {
        // GOG n'a pas d'adresse « juste le compte » : on lit la liste et on compte.
        List<AchievementDetail> achievements = await GetAchievementsAsync(game, cancellationToken);
        return new AchievementProgress(achievements.Count(a => a.IsUnlocked), achievements.Count);
    }

    public Task<List<AchievementDetail>> GetAchievementsAsync(Game game, CancellationToken cancellationToken = default)
    {
        if (_tokens is null || _tokens.UserId.Length == 0)
        {
            throw new AchievementSourceUnavailableException("Compte GOG non connecté.");
        }

        return GogApi.GetAchievementsAsync(game.PlatformGameId, _tokens.UserId, _tokens.AccessToken, cancellationToken);
    }
}
