using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wyrmhold.Core;

/// <summary>
/// Lit les actualités d'un jeu Steam (API publique, sans clé).
/// </summary>
public static class SteamNewsApi
{
    // Le flux des annonces officielles publiées par le studio sur Steam.
    private const string AnnouncementsFeed = "steam_community_announcements";

    private static readonly HttpClient Http = new HttpClient();

    /// <summary>
    /// Renvoie les dernières annonces officielles d'un jeu, les plus récentes d'abord.
    /// maxlength=0 demande le texte complet (sinon Steam le coupe).
    /// </summary>
    public static async Task<List<SteamNewsItem>> GetAnnouncementsAsync(string appId, int count)
    {
        string url = "https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/"
            + $"?appid={Uri.EscapeDataString(appId)}&count={count}&maxlength=0&feeds={AnnouncementsFeed}";

        string json = await Http.GetStringAsync(url);

        return JsonSerializer.Deserialize<SteamNewsResponse>(json)?.AppNews?.NewsItems
            ?? new List<SteamNewsItem>();
    }
}

public class SteamNewsItem
{
    // L'étiquette que Valve met sur les notes de mise à jour.
    private const string PatchNotesTag = "patchnotes";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("contents")]
    public string Contents { get; set; } = "";

    [JsonPropertyName("date")]
    public long DateUnix { get; set; }

    // Absent de la réponse quand l'annonce n'a pas d'étiquette.
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new List<string>();

    public bool IsPatchNotes => Tags.Contains(PatchNotesTag);
}

internal class SteamNewsResponse
{
    [JsonPropertyName("appnews")]
    public SteamAppNews? AppNews { get; set; }
}

internal class SteamAppNews
{
    [JsonPropertyName("newsitems")]
    public List<SteamNewsItem> NewsItems { get; set; } = new List<SteamNewsItem>();
}
