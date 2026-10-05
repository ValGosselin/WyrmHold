using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wyrmhold.Core;

public record GogTokens(string AccessToken, string RefreshToken);

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

        return new GogTokens(response.AccessToken, response.RefreshToken);
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
}

internal class GogTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }
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