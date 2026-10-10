namespace Wyrmhold.Core;

/// <summary>D'où vient un jeu gratuit de l'onglet « 🎁 Gratuits » de la page Boutiques.</summary>
public enum FreeGameSource
{
    SteamDemo,       // démo Steam
    SteamFreeToPlay, // jeu gratuit (free-to-play) sur Steam
    EpicGiveaway,    // jeu offert pour un temps limité sur l'Epic Games Store
    SteamGame        // n'importe quel jeu Steam (onglet Résultats filtré par tag) : pas forcément gratuit
}

/// <summary>
/// Un jeu gratuit à découvrir : une démo ou un free-to-play Steam, ou un jeu offert par Epic.
/// Wyrmhold ne l'installe pas lui-même : il passe la main au launcher (Steam) ou ouvre la page (Epic).
/// </summary>
public class FreeGame
{
    public FreeGameSource Source { get; init; }
    public string Title { get; init; } = "";

    // Steam seulement : le numéro du jeu (appid), qui sert à l'installer et à le reconnaître dans ta bibliothèque.
    public int? SteamAppId { get; init; }

    // Petite image pour la liste, grande image pour la présentation (null si inconnue).
    public string? ImageUrl { get; init; }
    public string? CoverUrl { get; init; }

    public string StoreUrl { get; init; } = "";

    // Textes déjà prêts à afficher (vides s'ils sont inconnus).
    public string ReleaseText { get; init; } = "";   // « 20 juil. 2022 »
    public string ReviewText { get; init; } = "";    // « 98 % d'avis positifs (771) »
    public string Description { get; init; } = "";  // Epic seulement (Steam : description chargée à part)

    // Epic seulement : la période pendant laquelle le jeu est offert.
    public DateTime? FreeFrom { get; init; }
    public DateTime? FreeUntil { get; init; }

    /// <summary>Faux pour un jeu Epic annoncé mais pas encore offert.</summary>
    public bool IsAvailableNow => Source != FreeGameSource.EpicGiveaway || (FreeFrom <= DateTime.Now && DateTime.Now < FreeUntil);

    public string SourceText => Source switch
    {
        FreeGameSource.SteamDemo => "Démo Steam",
        FreeGameSource.SteamFreeToPlay => "Gratuit sur Steam",
        FreeGameSource.SteamGame => "Steam",
        _ => "Epic Games Store"
    };

    /// <summary>« Offert jusqu'au 15/10 à 17:00 » ou « Offert à partir du 15/10 à 17:00 ».</summary>
    public string AvailabilityText => Source != FreeGameSource.EpicGiveaway
        ? ""
        : IsAvailableNow
            ? $"Offert jusqu'au {FreeUntil:dd/MM à HH:mm}"
            : $"Offert à partir du {FreeFrom:dd/MM à HH:mm}";

    /// <summary>La ligne grise sous le titre, dans la liste : source, date, avis ou période.</summary>
    public string SummaryLine => string.Join(" · ",
        new[] { SourceText, ReleaseText, ReviewText, AvailabilityText }.Where(text => text.Length > 0));

    /// <summary>
    /// Le lien qui demande à Steam d'installer le jeu : Steam ouvre sa propre fenêtre d'installation
    /// (dossier, place nécessaire), et c'est toi qui confirmes. null pour un jeu qui n'est pas sur Steam.
    /// </summary>
    public string? SteamInstallUrl => SteamAppId is int appId ? $"steam://install/{appId}" : null;
}
