using System.Text.Json.Serialization;

namespace Wyrmhold.Core;

public class SteamOwnedGame
{
    [JsonPropertyName("appid")]
    public int AppId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("playtime_forever")]
    public int PlaytimeMinutes { get; set; }
    [JsonPropertyName("rtime_last_played")]
    public long LastPlayedUnix { get; set; }

    // Vrai si le jeu a une page de statistiques (succès ou autres stats).
    [JsonPropertyName("has_community_visible_stats")]
    public bool HasCommunityVisibleStats { get; set; }
}

// ----- Réponse de ISteamUserStats/GetPlayerAchievements -----

internal class SteamPlayerAchievementsResponse
{
    [JsonPropertyName("playerstats")]
    public SteamPlayerStats? PlayerStats { get; set; }
}

internal class SteamPlayerStats
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    // Rempli seulement en cas d'échec (ex. « Requested app has no stats »).
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    // Absent quand le jeu a des statistiques mais aucun succès.
    [JsonPropertyName("achievements")]
    public List<SteamPlayerAchievement> Achievements { get; set; } = new List<SteamPlayerAchievement>();
}

internal class SteamPlayerAchievement
{
    [JsonPropertyName("apiname")]
    public string ApiName { get; set; } = "";

    // 1 = débloqué, 0 = pas encore.
    [JsonPropertyName("achieved")]
    public int Achieved { get; set; }

    [JsonPropertyName("unlocktime")]
    public long UnlockTimeUnix { get; set; }

    // Remplis seulement quand on passe la langue (paramètre « l »).
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

internal class SteamOwnedGamesResponse
{
    [JsonPropertyName("response")]
    public SteamOwnedGamesData? Response { get; set; }
}

internal class SteamOwnedGamesData
{
    [JsonPropertyName("games")]
    public List<SteamOwnedGame> Games { get; set; } = new List<SteamOwnedGame>();
}