namespace Wyrmhold.Core;

/// <summary>Un jeu dont une mise à jour a ajouté des succès, et combien.</summary>
public record AddedAchievements(Game Game, int Count);

/// <summary>
/// Le bilan d'une lecture des succès (LibraryService.RefreshAchievementsAsync) : combien de jeux ont été relus,
/// et ceux qui ont reçu de nouveaux succès depuis la lecture précédente (du plus grand nombre au plus petit).
/// </summary>
public record AchievementRefreshResult(int RefreshedCount, List<AddedAchievements> Added);
