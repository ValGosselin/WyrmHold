using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wyrmhold.Core;

// ----- Réponse de IPlayerService/GetGameAchievements : la liste des succès du jeu -----
// Contrairement à GetSchemaForGame, elle donne aussi la description des succès cachés,
// et la rareté dans la même réponse. On la préfère donc, et on garde l'autre en secours.

internal class SteamGameAchievementsResponse
{
    [JsonPropertyName("response")]
    public SteamGameAchievementsData? Response { get; set; }
}

internal class SteamGameAchievementsData
{
    [JsonPropertyName("achievements")]
    public List<SteamGameAchievement> Achievements { get; set; } = new List<SteamGameAchievement>();
}

internal class SteamGameAchievement
{
    // Le même identifiant que « apiname » dans GetPlayerAchievements.
    [JsonPropertyName("internal_name")]
    public string? ApiName { get; set; }

    [JsonPropertyName("localized_name")]
    public string? Name { get; set; }

    [JsonPropertyName("localized_desc")]
    public string? Description { get; set; }

    [JsonPropertyName("hidden")]
    public bool Hidden { get; set; }

    // Seulement le nom du fichier : l'adresse complète est construite dans SteamWebApi.
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("icon_gray")]
    public string? IconGray { get; set; }

    // JsonElement : on garde la valeur brute, qu'elle arrive en nombre ou en texte, et on la lit dans Percent.
    [JsonPropertyName("player_percent_unlocked")]
    public JsonElement PlayerPercentUnlocked { get; set; }

    public double? Percent => PlayerPercentUnlocked.ValueKind switch
    {
        JsonValueKind.Number => PlayerPercentUnlocked.GetDouble(),
        JsonValueKind.String when double.TryParse(PlayerPercentUnlocked.GetString(), NumberStyles.Float,
            CultureInfo.InvariantCulture, out double percent) => percent,
        _ => null
    };
}

// ----- Réponse de ISteamUserStats/GetSchemaForGame : la liste des succès du jeu (solution de secours) -----

internal class SteamSchemaResponse
{
    [JsonPropertyName("game")]
    public SteamSchemaGame? Game { get; set; }
}

internal class SteamSchemaGame
{
    [JsonPropertyName("availableGameStats")]
    public SteamSchemaStats? AvailableGameStats { get; set; }
}

internal class SteamSchemaStats
{
    [JsonPropertyName("achievements")]
    public List<SteamSchemaAchievement> Achievements { get; set; } = new List<SteamSchemaAchievement>();

    // Les compteurs que le jeu envoie à Steam (pas forcément reliés à un succès).
    [JsonPropertyName("stats")]
    public List<SteamSchemaStat> Stats { get; set; } = new List<SteamSchemaStat>();
}

internal class SteamSchemaStat
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    // Souvent vide : beaucoup de studios ne donnent pas de nom lisible à leurs statistiques.
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
}

// ----- Réponse de ISteamUserStats/GetUserStatsForGame : tes valeurs pour chaque statistique -----

internal class SteamUserStatsResponse
{
    [JsonPropertyName("playerstats")]
    public SteamUserStats? PlayerStats { get; set; }
}

internal class SteamUserStats
{
    [JsonPropertyName("stats")]
    public List<SteamUserStat> Stats { get; set; } = new List<SteamUserStat>();
}

internal class SteamUserStat
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("value")]
    public double Value { get; set; }
}

internal class SteamSchemaAchievement
{
    // Le même identifiant que « apiname » dans GetPlayerAchievements.
    [JsonPropertyName("name")]
    public string ApiName { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    // Absente pour la plupart des succès cachés.
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    // 1 = succès caché.
    [JsonPropertyName("hidden")]
    public int Hidden { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("icongray")]
    public string? IconGray { get; set; }
}

// ----- Réponse de ISteamUserStats/GetGlobalAchievementPercentagesForApp : la rareté (solution de secours) -----

internal class SteamGlobalPercentagesResponse
{
    [JsonPropertyName("achievementpercentages")]
    public SteamGlobalPercentages? AchievementPercentages { get; set; }
}

internal class SteamGlobalPercentages
{
    [JsonPropertyName("achievements")]
    public List<SteamGlobalPercentage> Achievements { get; set; } = new List<SteamGlobalPercentage>();
}

internal class SteamGlobalPercentage
{
    [JsonPropertyName("name")]
    public string ApiName { get; set; } = "";

    // Lu qu'il arrive en nombre (12.3) ou en texte ("12.3") : par prudence, on accepte les deux.
    [JsonPropertyName("percent")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public double Percent { get; set; }
}
