using System.Text.Json.Serialization;

namespace Wyrmhold.Core;

// Les réponses GraphQL suivent exactement la forme de la requête : chaque niveau de la requête
// devient une classe. Ça fait beaucoup de petites classes, mais chacune reste très simple.

internal class EpicGraphQlError
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

// ----- Liste des succès d'un jeu (requête « Achievement ») -----

internal class EpicSchemaResponse
{
    [JsonPropertyName("data")]
    public EpicSchemaData? Data { get; set; }

    [JsonPropertyName("errors")]
    public List<EpicGraphQlError>? Errors { get; set; }
}

internal class EpicSchemaData
{
    [JsonPropertyName("Achievement")]
    public EpicSchemaAchievementRoot? Achievement { get; set; }
}

internal class EpicSchemaAchievementRoot
{
    // null si le jeu n'a pas de succès.
    [JsonPropertyName("productAchievementsRecordBySandbox")]
    public EpicProductAchievements? Record { get; set; }
}

internal class EpicProductAchievements
{
    // L'identifiant à donner à la 2e requête (celle de tes succès).
    [JsonPropertyName("productId")]
    public string? ProductId { get; set; }

    [JsonPropertyName("totalAchievements")]
    public int? TotalAchievements { get; set; }

    [JsonPropertyName("achievements")]
    public List<EpicAchievementWrapper> Achievements { get; set; } = new List<EpicAchievementWrapper>();
}

internal class EpicAchievementWrapper
{
    [JsonPropertyName("achievement")]
    public EpicAchievementInfo? Achievement { get; set; }
}

internal class EpicAchievementInfo
{
    // Le même identifiant que « achievementName » dans tes succès.
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("hidden")]
    public bool Hidden { get; set; }

    // Les « unlocked » sont le vrai nom et la vraie description ; les « locked » remplacent souvent
    // ceux des succès cachés par un texte générique. Wyrmhold masque lui-même les succès cachés.
    [JsonPropertyName("unlockedDisplayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("unlockedDescription")]
    public string? Description { get; set; }

    [JsonPropertyName("unlockedIconLink")]
    public string? UnlockedIconUrl { get; set; }

    [JsonPropertyName("lockedIconLink")]
    public string? LockedIconUrl { get; set; }

    [JsonPropertyName("rarity")]
    public EpicRarity? Rarity { get; set; }
}

internal class EpicRarity
{
    [JsonPropertyName("percent")]
    public double? Percent { get; set; }
}

// ----- Tes succès pour un jeu (requête « playerProfileAchievementsByProductId ») -----

internal class EpicPlayerResponse
{
    [JsonPropertyName("data")]
    public EpicPlayerData? Data { get; set; }

    [JsonPropertyName("errors")]
    public List<EpicGraphQlError>? Errors { get; set; }
}

internal class EpicPlayerData
{
    [JsonPropertyName("PlayerProfile")]
    public EpicPlayerProfileRoot? PlayerProfile { get; set; }
}

internal class EpicPlayerProfileRoot
{
    [JsonPropertyName("playerProfile")]
    public EpicPlayerProfile? Profile { get; set; }
}

internal class EpicPlayerProfile
{
    [JsonPropertyName("productAchievements")]
    public EpicProductAchievementsResult? ProductAchievements { get; set; }
}

internal class EpicProductAchievementsResult
{
    // Rempli seulement quand Epic répond « succès » (sinon : profil privé, jeu inconnu…).
    [JsonPropertyName("data")]
    public EpicPlayerAchievementsData? Data { get; set; }
}

internal class EpicPlayerAchievementsData
{
    [JsonPropertyName("playerAchievements")]
    public List<EpicPlayerAchievementWrapper> PlayerAchievements { get; set; } = new List<EpicPlayerAchievementWrapper>();
}

internal class EpicPlayerAchievementWrapper
{
    [JsonPropertyName("playerAchievement")]
    public EpicPlayerAchievement? PlayerAchievement { get; set; }
}

internal class EpicPlayerAchievement
{
    [JsonPropertyName("achievementName")]
    public string? Name { get; set; }

    [JsonPropertyName("unlocked")]
    public bool Unlocked { get; set; }

    // Date au format ISO 8601, null si pas débloqué.
    [JsonPropertyName("unlockDate")]
    public string? UnlockDate { get; set; }

    // Avancement vers le déblocage : entre 0 et 1 d'après la documentation du SDK d'Epic.
    [JsonPropertyName("progress")]
    public double? Progress { get; set; }
}
