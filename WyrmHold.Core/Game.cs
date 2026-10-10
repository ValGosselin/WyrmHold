
using System.ComponentModel;

namespace Wyrmhold.Core;

public class Game : INotifyPropertyChanged
{
    public bool? IsFamilyShared { get; set; }
    public string? OwnerSteamId { get; set; }
    public string InstalledText => IsInstalled ? "✓" : "";
    public string OriginText => IsFamilyShared == true ? "Famille" : "";
    public Platform Platform { get; set; }
    public string PlatformGameId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsInstalled { get; set; }
    public string? InstallPath { get; set; }
    public long LastPlayedUnix { get; set; }
    public string? Description { get; set; }
    public string? Developers { get; set; }
    public long ReleaseDateUnix { get; set; }
    public bool IsEarlyAccess { get; set; }
    public string? Tags { get; set; }
    public HashSet<long> CollectionIds { get; } = new HashSet<long>();
    public long MetadataUpdatedUnix { get; set; }

    // Date à laquelle le jeu est apparu dans Wyrmhold (temps Unix).
    public long AddedUnix { get; set; }

    // Place occupée sur le disque, en octets (0 = inconnue).
    public long SizeOnDiskBytes { get; set; }

    // Comme Steam et Windows, on compte 1 Go = 1024 × 1024 × 1024 octets.
    private const double BytesPerGigabyte = 1024.0 * 1024 * 1024;
    private const double BytesPerMegabyte = 1024.0 * 1024;

    public string SizeText => !IsInstalled || SizeOnDiskBytes <= 0
        ? ""
        : SizeOnDiskBytes >= BytesPerGigabyte
            ? $"{SizeOnDiskBytes / BytesPerGigabyte:0.00} Go"
            : $"{SizeOnDiskBytes / BytesPerMegabyte:0} Mo";
    private bool _isFavorite;

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value)
            {
                return;
            }

            _isFavorite = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFavorite)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ----- Joueurs en jeu (Steam) -----

    private int? _playersInGame;

    /// <summary>
    /// Les joueurs en jeu en ce moment sur Steam (null = inconnu ou pas de chiffre). Pas gardé en base :
    /// relu au démarrage puis toutes les 10 minutes. Pour un jeu hors Steam, c'est le chiffre du même jeu sur Steam.
    /// </summary>
    public int? PlayersInGame
    {
        get => _playersInGame;
        set
        {
            if (_playersInGame == value)
            {
                return;
            }

            _playersInGame = value;

            // Chaîne vide = « toutes les propriétés ont changé » : le badge, son texte et son infobulle.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    public bool HasPlayersInGame => PlayersInGame is not null;

    // Le texte du badge : « 22,1 k en jeu ». Pour un jeu acheté ailleurs, c'est le chiffre de Steam :
    // seule l'infobulle le précise (« · Steam » sur la tuile retiré le 10 octobre 2026, à la demande de Val).
    public string PlayersInGameText => PlayersInGame is int count
        ? FormatPlayers(count) + " en jeu"
        : "";

    public string PlayersInGameToolTip => PlayersInGame is not int count
        ? ""
        : Platform == Platform.Steam
            ? $"{count.ToString("N0", French)} joueur(s) en jeu en ce moment sur Steam"
            : $"{count.ToString("N0", French)} joueur(s) en jeu en ce moment sur Steam. "
              + $"Les joueurs de {PlatformName} ne sont pas comptés : {PlatformName} ne publie pas ce chiffre.";

    private static readonly System.Globalization.CultureInfo French = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

    // 845 → « 845 », 22 134 → « 22,1 k », 667 391 → « 667 k », 1 234 567 → « 1,2 M ».
    private static string FormatPlayers(int count) => count switch
    {
        < 1_000 => count.ToString(French),
        < 100_000 => (count / 1_000.0).ToString("0.#", French) + " k",
        < 1_000_000 => (count / 1_000).ToString(French) + " k",
        _ => (count / 1_000_000.0).ToString("0.#", French) + " M"
    };
    public string ReleaseDateText => ReleaseDateUnix == 0
    ? ""
    : DateTimeOffset.FromUnixTimeSeconds(ReleaseDateUnix).LocalDateTime.ToString("dd/MM/yyyy");

    public string InstallStatusText => IsInstalled ? "Installé" : "Non installé";

    public string[] TagList => string.IsNullOrEmpty(Tags)
        ? Array.Empty<string>()
        : Tags.Split(", ");

    public string LastPlayedText => LastPlayedUnix == 0
        ? ""
        : DateTimeOffset.FromUnixTimeSeconds(LastPlayedUnix).LocalDateTime.ToString("dd/MM/yyyy");

    public string? CoverPath { get; set; }

    public string Subtitle => string.Join(" · ",
        new[] { PlatformName, PlaytimeText, OriginText }.Where(text => text.Length > 0));
    public string PlatformName => Platform switch
    {
        Platform.Steam => "Steam",
        Platform.Epic => "Epic Games",
        Platform.Ubisoft => "Ubisoft",
        Platform.Gog => "GOG",
        Platform.Ea => "EA",
        Platform.BattleNet => "Battle.net",
        _ => Platform.ToString()
    };

    public int PlaytimeMinutes { get; set; }

    public string PlaytimeText => PlaytimeMinutes switch
    {
        0 => "",
        < 60 => $"{PlaytimeMinutes} min",
        _ => $"{PlaytimeMinutes / 60} h"
    };

    // ----- Mises à jour -----

    // Pendant combien de jours un jeu mis à jour garde son badge « Mis à jour ».
    public const int RecentUpdateDays = 7;

    // La version installée, telle que le lanceur l'écrit : numéro de build pour Steam,
    // AppVersionString pour Epic. null = inconnue (autres lanceurs).
    public string? InstalledVersion { get; set; }

    // Le moment où Wyrmhold a vu la version changer (temps Unix, 0 = jamais).
    public long LastUpdateDetectedUnix { get; set; }

    public bool IsRecentlyUpdated => LastUpdateDetectedUnix > 0
        && LastUpdateDetectedUnix >= DateTimeOffset.UtcNow.AddDays(-RecentUpdateDays).ToUnixTimeSeconds();

    // Pour un jeu non Steam : l'appid Steam du même jeu, qui sert à lire ses patch notes.
    // null = pas encore cherché, "" = cherché mais introuvable sur Steam.
    public string? SteamAppId { get; set; }

    // ----- Succès -----

    public int AchievementsUnlocked { get; set; }
    public int AchievementsTotal { get; set; }

    // Vrai si le jeu était à 100 % et que son total a augmenté depuis (nouveaux succès à faire).
    public bool HasNewAchievements { get; set; }

    // Dernière fois que les succès ont été lus (temps Unix, 0 = jamais).
    public long AchievementsCheckedUnix { get; set; }

    public bool HasAchievements => AchievementsTotal > 0;

    public bool IsAchievementsComplete => AchievementsTotal > 0 && AchievementsUnlocked == AchievementsTotal;

    public int MissingAchievements => AchievementsTotal - AchievementsUnlocked;

    public double AchievementsRatio => AchievementsTotal == 0
        ? 0
        : (double)AchievementsUnlocked / AchievementsTotal;

    // « 37/50 » sur la vignette.
    public string AchievementsText => AchievementsTotal == 0
        ? ""
        : $"{AchievementsUnlocked}/{AchievementsTotal}";

    // « 37/50 (74 %) » sur la fiche.
    public string AchievementsDetailText => AchievementsTotal == 0
        ? ""
        : $"{AchievementsUnlocked}/{AchievementsTotal} ({AchievementsRatio:P0})";

    public string NewAchievementsText => $"Succès ajoutés : {MissingAchievements} à faire";

    // ----- Regroupement des copies (affichage seulement : chaque copie garde sa ligne en base) -----

    // La clé qui reconnaît un même jeu sur plusieurs plateformes : son nom sans espaces, ponctuation
    // ni symboles (« Rocket League® » et « Rocket League » donnent la même clé).
    public string GroupKey => NameTools.Normalize(Name);

    // Toutes les copies de ce jeu (lui compris), rangées par plateforme. Rempli par la fenêtre.
    public List<Game> Copies { get; set; } = new List<Game>();

    public bool HasOtherCopies => Copies.Count > 1;

    // Ses copies, ou lui seul s'il n'a pas encore été regroupé.
    public IEnumerable<Game> CopiesOrSelf => Copies.Count > 0 ? Copies : new[] { this };

    // Titre doré sur la tuile : au moins une copie est à 100 %.
    public bool AnyCopyComplete => CopiesOrSelf.Any(copy => copy.IsAchievementsComplete);

    // Les pastilles de la tuile : une par plateforme, dorée si cette copie est à 100 %.
    public IEnumerable<CopyBadge> CopyBadges => CopiesOrSelf.Select(copy => new CopyBadge(copy.PlatformShortName, copy.IsAchievementsComplete));

    public string PlatformShortName => Platform switch
    {
        Platform.Epic => "Epic",
        _ => PlatformName
    };

    // Le texte d'une copie dans la liste « Plateforme » de la fiche.
    public string CopyLabel => string.Join(" · ",
        new[] { PlatformName, InstallStatusText, PlaytimeText, AchievementsText }.Where(text => text.Length > 0));

    public string LastUpdateText => LastUpdateDetectedUnix == 0
        ? ""
        : DateTimeOffset.FromUnixTimeSeconds(LastUpdateDetectedUnix).LocalDateTime.ToString("dd/MM/yyyy");

    /// <summary>
    /// Enregistre une nouvelle lecture des succès et tient à jour le badge « Succès ajoutés ».
    /// </summary>
    public void ApplyAchievementProgress(AchievementProgress progress, long checkedUnix)
    {
        // À regarder AVANT d'écraser les anciennes valeurs : après, on ne saurait plus.
        bool wasComplete = IsAchievementsComplete;
        bool totalIncreased = progress.Total > AchievementsTotal;

        AchievementsUnlocked = progress.Unlocked;
        AchievementsTotal = progress.Total;
        AchievementsCheckedUnix = checkedUnix;

        if (IsAchievementsComplete || AchievementsTotal == 0)
        {
            // Revenu à 100 % (ou plus de succès du tout) : le badge disparaît.
            HasNewAchievements = false;
        }
        else if (wasComplete && totalIncreased)
        {
            // Était à 100 %, et le jeu a reçu de nouveaux succès.
            HasNewAchievements = true;
        }
        // Sinon, on garde la valeur d'avant : le badge reste jusqu'au retour à 100 %.

        // Chaîne vide = « toutes les propriétés ont changé » : la fiche et la vignette se redessinent.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}

/// <summary>
/// Une pastille de plateforme sur la tuile d'un jeu possédé plusieurs fois.
/// </summary>
public record CopyBadge(string Name, bool IsComplete);
