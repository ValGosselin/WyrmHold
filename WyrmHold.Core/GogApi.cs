using System.Globalization;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wyrmhold.Core;

// UserId : ton identifiant GOG, nécessaire pour lire tes succès.
public record GogTokens(string AccessToken, string RefreshToken, string UserId);

public static class GogApi
{
    private const string ClientId = "46899977096215655";
    private const string ClientSecret = "9d85c43b1482497dbbce61f6e4aa173a433796eeae2ca8c5f6129f2dc4de46d9";
    private const string RedirectUri = "https://embed.gog.com/on_login_success?origin=client";

    public const string LoginSuccessUrl = "https://embed.gog.com/on_login_success";

    public static string LoginUrl =>
        "https://auth.gog.com/auth"
        + $"?client_id={ClientId}"
        + $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}"
        + "&response_type=code&layout=client2";

    private static readonly HttpClient Http = new HttpClient();

    public static Task<GogTokens?> ExchangeCodeAsync(string loginCode)
    {
        return RequestTokensAsync(
            "grant_type=authorization_code"
            + $"&code={Uri.EscapeDataString(loginCode)}"
            + $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}");
    }

    public static Task<GogTokens?> RefreshAsync(string refreshToken)
    {
        return RequestTokensAsync(
            "grant_type=refresh_token"
            + $"&refresh_token={Uri.EscapeDataString(refreshToken)}");
    }

    private static async Task<GogTokens?> RequestTokensAsync(string parameters)
    {
        string url = "https://auth.gog.com/token"
            + $"?client_id={ClientId}&client_secret={ClientSecret}&{parameters}";

        string json = await Http.GetStringAsync(url);
        GogTokenResponse? response = JsonSerializer.Deserialize<GogTokenResponse>(json);

        if (response is null
            || string.IsNullOrEmpty(response.AccessToken)
            || string.IsNullOrEmpty(response.RefreshToken))
        {
            return null;
        }

        return new GogTokens(response.AccessToken, response.RefreshToken, response.UserId ?? "");
    }

    public static async Task<List<long>> GetOwnedGameIdsAsync(string accessToken)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "https://embed.gog.com/user/data/games");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using HttpResponseMessage response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<GogOwnedResponse>(json)?.Owned ?? new List<long>();
    }

    internal static async Task<List<GogProduct>> GetProductsAsync(IEnumerable<long> productIds)
    {
        List<GogProduct> products = new List<GogProduct>();

        foreach (long[] batch in productIds.Chunk(50))
        {
            string json = await Http.GetStringAsync("https://api.gog.com/products?ids=" + string.Join(",", batch));
            products.AddRange(JsonSerializer.Deserialize<List<GogProduct>>(json) ?? new List<GogProduct>());
        }

        return products;
    }

    // ----- Succès -----

    /// <summary>
    /// Tes succès pour un jeu GOG (adresse interne de GOG, non documentée : elle peut changer).
    /// Liste vide si le jeu n'a pas de succès.
    /// </summary>
    public static async Task<List<AchievementDetail>> GetAchievementsAsync(
        string productId, string userId, string accessToken, CancellationToken cancellationToken = default)
    {
        string url = $"https://gameplay.gog.com/clients/{Uri.EscapeDataString(productId)}"
            + $"/users/{Uri.EscapeDataString(userId)}/achievements";

        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new AchievementSourceUnavailableException("La session GOG a expiré : reconnecte-toi dans l'onglet Comptes.");
        }

        // Un jeu sans succès (ou sans fonctions Galaxy) n'a pas de page de succès.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new List<AchievementDetail>();
        }

        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        List<GogAchievement> items = JsonSerializer.Deserialize<GogAchievementsResponse>(json)?.Items
            ?? new List<GogAchievement>();

        return items.Select((item, index) => new AchievementDetail
        {
            // « ?? "" » : si GOG envoie null, System.Text.Json met null même dans un string non-nullable.
            Id = item.Key ?? "",
            Name = (item.Name ?? "").Trim(),
            Description = (item.Description ?? "").Trim(),
            IsUnlocked = item.DateUnlocked is not null,
            UnlockedUnix = ParseGogDate(item.DateUnlocked),
            IsHidden = !item.Visible,
            IconUrl = item.DateUnlocked is not null ? item.ImageUrlUnlocked : item.ImageUrlLocked,
            RarityPercent = item.Rarity,
            Order = index
        }).ToList();
    }

    /// <summary>
    /// Si le fuseau horaire est écrit sans deux-points (« +0000 »), .NET ne sait pas le lire :
    /// on les ajoute (« +00:00 ») par prudence avant de lire la date.
    /// </summary>
    private static long ParseGogDate(string? date)
    {
        if (string.IsNullOrEmpty(date))
        {
            return 0;
        }

        string fixedDate = Regex.Replace(date, @"([+-]\d{2})(\d{2})$", "$1:$2");

        return DateTimeOffset.TryParse(fixedDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
            ? parsed.ToUnixTimeSeconds()
            : 0;
    }
}

internal class GogTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }
}

internal class GogOwnedResponse
{
    [JsonPropertyName("owned")]
    public List<long> Owned { get; set; } = new List<long>();
}

internal class GogProduct
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("game_type")]
    public string GameType { get; set; } = "";
}

internal class GogAchievementsResponse
{
    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    [JsonPropertyName("items")]
    public List<GogAchievement> Items { get; set; } = new List<GogAchievement>();
}

internal class GogAchievement
{
    [JsonPropertyName("achievement_key")]
    public string? Key { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    // false = succès caché.
    [JsonPropertyName("visible")]
    public bool Visible { get; set; } = true;

    [JsonPropertyName("image_url_unlocked")]
    public string? ImageUrlUnlocked { get; set; }

    [JsonPropertyName("image_url_locked")]
    public string? ImageUrlLocked { get; set; }

    // Pourcentage des joueurs qui l'ont.
    [JsonPropertyName("rarity")]
    public double? Rarity { get; set; }

    // null tant que le succès n'est pas débloqué. Lu comme du texte (voir ParseGogDate).
    [JsonPropertyName("date_unlocked")]
    public string? DateUnlocked { get; set; }
}
