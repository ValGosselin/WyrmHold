using System.Text.Json.Serialization;

namespace Wyrmhold.Core;

/// <summary>
/// Les réponses JSON interceptées sur la page « Games activity » du site d'Ubisoft.
/// </summary>
public class UbisoftCapture
{
    public string? GamesPlayedJson { get; set; }
    public List<string> CatalogJsons { get; } = new List<string>();
    public Dictionary<string, string> StatsJsonBySpaceId { get; } = new Dictionary<string, string>();
}

internal class UbisoftGamesPlayedResponse
{
    [JsonPropertyName("gamesPlayed")]
    public List<UbisoftPlayedGame> GamesPlayed { get; set; } = new List<UbisoftPlayedGame>();
}

internal class UbisoftPlayedGame
{
    [JsonPropertyName("spaceId")]
    public string SpaceId { get; set; } = "";

    [JsonPropertyName("lastPlayed")]
    public UbisoftLastPlayed? LastPlayed { get; set; }

    [JsonPropertyName("applications")]
    public List<UbisoftApplication> Applications { get; set; } = new List<UbisoftApplication>();
}

internal class UbisoftLastPlayed
{
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }
}

internal class UbisoftApplication
{
    [JsonPropertyName("applicationPlatformType")]
    public string ApplicationPlatformType { get; set; } = "";
}

internal class UbisoftCatalogResponse
{
    [JsonPropertyName("games")]
    public List<UbisoftCatalogGame> Games { get; set; } = new List<UbisoftCatalogGame>();
}

internal class UbisoftCatalogGame
{
    [JsonPropertyName("spaceId")]
    public string SpaceId { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("imageUrls")]
    public UbisoftImageUrls? ImageUrls { get; set; }
}

internal class UbisoftImageUrls
{
    [JsonPropertyName("lowBoxArt")]
    public string? LowBoxArt { get; set; }

    [JsonPropertyName("highBoxArt")]
    public string? HighBoxArt { get; set; }
}