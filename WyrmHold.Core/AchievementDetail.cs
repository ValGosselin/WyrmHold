namespace Wyrmhold.Core;

/// <summary>
/// Un succès d'un jeu, avec ce qu'il faut faire pour l'obtenir.
/// Même forme pour toutes les plateformes : la fenêtre des succès n'a pas à savoir d'où il vient.
/// </summary>
public class AchievementDetail
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    // Ce qu'il faut faire. Parfois vide pour un succès caché pas encore débloqué.
    public string Description { get; init; } = "";

    public bool IsUnlocked { get; init; }
    public long UnlockedUnix { get; init; }

    // Caché : le jeu ne veut pas qu'on sache ce qu'il faut faire avant de l'avoir débloqué.
    public bool IsHidden { get; init; }

    // L'icône en couleur (débloqué) ou grisée (pas encore).
    public string? IconUrl { get; init; }

    // Pourcentage des joueurs qui l'ont (null = inconnu).
    public double? RarityPercent { get; init; }

    // Ordre dans lequel le jeu présente ses succès.
    public int Order { get; init; }

    public string UnlockedText => IsUnlocked && UnlockedUnix > 0
        ? "Débloqué le " + DateTimeOffset.FromUnixTimeSeconds(UnlockedUnix).LocalDateTime.ToString("dd/MM/yyyy")
        : IsUnlocked ? "Débloqué" : "";

    public string RarityText => RarityPercent is null
        ? ""
        : $"{RarityPercent.Value:0.#} % des joueurs";

    public bool HasDescription => Description.Length > 0;

    // Avancement d'un succès à compteur (« 37 / 100 ») : null si le succès n'en a pas.
    public double? ProgressValue { get; init; }
    public double? ProgressMax { get; init; }

    public bool HasProgress => ProgressValue is not null && ProgressMax is > 0;

    public double ProgressRatio => HasProgress ? Math.Min(1, ProgressValue!.Value / ProgressMax!.Value) : 0;

    public string ProgressText => HasProgress ? $"{ProgressValue:N0} / {ProgressMax:N0}" : "";
}
