namespace Wyrmhold.Core;

/// <summary>
/// Les succès maison : la save du jeu est lue (jamais modifiée), puis chaque règle du fichier JSON est évaluée.
/// Comme toute source, elle est relue pendant la partie par LiveAchievementTracker : un succès apparaît dès que
/// le jeu a sauvegardé. La save ne garde pas la date des événements : les succès n'ont pas de date de déblocage.
/// </summary>
public class CustomAchievementProvider : IAchievementProvider
{
    private readonly EmulatorProvider _emulator;

    public CustomAchievementProvider(EmulatorProvider emulator)
    {
        _emulator = emulator;
    }

    public Platform Platform => _emulator.Platform;

    public bool CanHaveAchievements(Game game)
    {
        return game.IsInstalled && CustomAchievementCatalog.Find(game.PlatformGameId) is not null;
    }

    public async Task<AchievementProgress> GetProgressAsync(Game game, CancellationToken cancellationToken = default)
    {
        List<AchievementDetail> achievements = await GetAchievementsAsync(game, cancellationToken);
        return new AchievementProgress(achievements.Count(a => a.IsUnlocked), achievements.Count);
    }

    public Task<List<AchievementDetail>> GetAchievementsAsync(Game game, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            CustomGameDefinition? definition = CustomAchievementCatalog.Find(game.PlatformGameId);

            if (definition is null)
            {
                return new List<AchievementDetail>();
            }

            // Pas encore de save : tous les succès sont à faire.
            string? savePath = _emulator.FindSaveFile(definition.TitleId, definition.SaveFile);

            return ToDetails(definition, savePath is null ? new Dictionary<string, long>() : ReadValues(definition, savePath));
        }, cancellationToken);
    }

    /// <summary>
    /// Lit une save en mémoire, puis la transforme en valeurs nommées. Le fichier d'origine n'est qu'ouvert en lecture.
    /// </summary>
    public static Dictionary<string, long> ReadValues(CustomGameDefinition definition, string savePath)
    {
        return CreateReader(definition.Reader).Read(savePath);
    }

    public static List<AchievementDetail> ToDetails(CustomGameDefinition definition, IReadOnlyDictionary<string, long> values)
    {
        return definition.Achievements
            .Select((achievement, index) => new AchievementDetail
            {
                Id = achievement.Id,
                Name = achievement.Title,
                Description = achievement.Description,
                IsUnlocked = achievement.Rule.IsMet(values),
                IsHidden = achievement.IsHidden,
                Order = index,
                ProgressValue = achievement.Rule is { Type: "atLeast", Key: string key } ? Math.Min(values.GetValueOrDefault(key), achievement.Rule.Value) : null,
                ProgressMax = achievement.Rule.Type == "atLeast" ? achievement.Rule.Value : null
            })
            .ToList();
    }

    private static ICustomSaveReader CreateReader(string name) => name switch
    {
        "letsgo" => new LetsGoSaveReader(),
        "inazuma" => new InazumaGoSaveReader(),
        _ => throw new InvalidDataException($"Lecteur de save inconnu : {name}.")
    };
}
