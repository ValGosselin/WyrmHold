using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wyrmhold.Core;

/// <summary>
/// Lit les guides de la communauté Steam d'un jeu (IPublishedFileService/QueryFiles, avec la clé Web API).
/// Réglages vérifiés en console le 9 octobre 2026 : voir taches.md, phase 8.
/// </summary>
public class SteamGuidesApi
{
    // filetype=10 : les guides (dans la réponse, un guide a file_type = 9).
    private const int GuidesFileType = 10;

    // query_type : 0 = les mieux notés, 1 = les plus récents.
    private const int RankedByVote = 0;
    private const int RankedByDate = 1;

    // Le maximum que Steam renvoie en une seule fois.
    private const int GuidesToRead = 100;

    // Le paramètre « language » de Steam ne filtre rien : la langue d'un guide est une de ses étiquettes.
    // match_all_tags=false : il suffit d'en avoir une des deux (français OU anglais).
    private static readonly string[] LanguageTags = { "french", "english" };

    // L'étiquette des guides consacrés aux succès.
    public const string AchievementsTag = "Achievements";

    private static readonly HttpClient Http = new HttpClient();

    private readonly Secrets _secrets;

    public SteamGuidesApi(Secrets secrets)
    {
        _secrets = secrets;
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_secrets.SteamApiKey);

    /// <summary>
    /// Les guides en français ou en anglais d'un jeu, les mieux notés d'abord (ou les plus récents).
    /// </summary>
    public async Task<List<SteamGuide>> GetGuidesAsync(string appId, bool newestFirst)
    {
        if (!IsConfigured)
        {
            return new List<SteamGuide>();
        }

        string languageTags = string.Join("", LanguageTags.Select((tag, index) => $"&requiredtags[{index}]={tag}"));

        // Sans les « return_… », Steam ne renvoie que l'identifiant de chaque guide (pas même le titre).
        string url = "https://api.steampowered.com/IPublishedFileService/QueryFiles/v1/"
            + $"?key={_secrets.SteamApiKey}&appid={Uri.EscapeDataString(appId)}"
            + $"&filetype={GuidesFileType}&query_type={(newestFirst ? RankedByDate : RankedByVote)}"
            + $"&numperpage={GuidesToRead}{languageTags}&match_all_tags=false"
            + "&return_short_description=true&return_vote_data=true&return_tags=true";

        string json = await Http.GetStringAsync(url);

        List<SteamGuide> guides = JsonSerializer.Deserialize<SteamGuidesResponse>(json)?.Response?.Guides
            ?? new List<SteamGuide>();

        // Certains guides ont un titre vide ou fait de caractères invisibles : on les écarte.
        return guides.Where(guide => guide.Title.Any(char.IsLetterOrDigit)).ToList();
    }
}

public class SteamGuide
{
    [JsonPropertyName("publishedfileid")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("short_description")]
    public string ShortDescription { get; set; } = "";

    [JsonPropertyName("time_updated")]
    public long UpdatedUnix { get; set; }

    [JsonPropertyName("vote_data")]
    public SteamGuideVotes? Votes { get; set; }

    [JsonPropertyName("tags")]
    public List<SteamGuideTag> Tags { get; set; } = new List<SteamGuideTag>();

    public string Url => $"https://steamcommunity.com/sharedfiles/filedetails/?id={Id}";

    public bool IsAboutAchievements => Tags.Any(tag => tag.Tag.Equals(SteamGuidesApi.AchievementsTag, StringComparison.OrdinalIgnoreCase));

    public bool IsFrench => Tags.Any(tag => tag.Tag.Equals("french", StringComparison.OrdinalIgnoreCase));

    public int VotesUp => Votes?.VotesUp ?? 0;

    // « 👍 1 509 · 99 % · FR · 01/08/2025 » : le pourcentage est la part de votes positifs.
    public string InfoText
    {
        get
        {
            List<string> parts = new List<string>();
            int votesTotal = VotesUp + (Votes?.VotesDown ?? 0);

            if (votesTotal > 0)
            {
                parts.Add($"👍 {VotesUp:N0}");
                parts.Add($"{100.0 * VotesUp / votesTotal:0} %");
            }

            if (IsFrench)
            {
                parts.Add("FR");
            }

            if (UpdatedUnix > 0)
            {
                parts.Add(DateTimeOffset.FromUnixTimeSeconds(UpdatedUnix).LocalDateTime.ToString("dd/MM/yyyy"));
            }

            return string.Join(" · ", parts);
        }
    }
}

public class GuidesResult
{
    // null = jeu introuvable sur Steam (pas de guides ni de forum Steam).
    public string? SteamAppId { get; init; }

    public List<SteamGuide> Guides { get; init; } = new List<SteamGuide>();

    // Une phrase à afficher au-dessus de la liste (vide si tout va bien).
    public string Message { get; init; } = "";
}

public class SteamGuideVotes
{
    [JsonPropertyName("votes_up")]
    public int VotesUp { get; set; }

    [JsonPropertyName("votes_down")]
    public int VotesDown { get; set; }
}

public class SteamGuideTag
{
    [JsonPropertyName("tag")]
    public string Tag { get; set; } = "";
}

internal class SteamGuidesResponse
{
    [JsonPropertyName("response")]
    public SteamGuidesList? Response { get; set; }
}

internal class SteamGuidesList
{
    [JsonPropertyName("publishedfiledetails")]
    public List<SteamGuide> Guides { get; set; } = new List<SteamGuide>();
}
