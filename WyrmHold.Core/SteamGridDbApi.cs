using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wyrmhold.Core;

public class SteamGridDbApi
{
    private const string BaseUrl = "https://www.steamgriddb.com/api/v2/";

    private static readonly HttpClient Http = new HttpClient();

    private readonly string _apiKey;

    public SteamGridDbApi(string apiKey)
    {
        _apiKey = apiKey;
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public async Task<string?> FindCoverUrlAsync(Game game)
    {
        if (game.Platform == Platform.Steam)
        {
            string? steamCover = await FindVerticalGridAsync($"grids/steam/{game.PlatformGameId}");

            if (steamCover is not null)
            {
                return steamCover;
            }
        }

        string cleanedName = CleanForSearch(game.Name);
        List<SgdbGame>? results = await GetAsync<List<SgdbGame>>(
            $"search/autocomplete/{Uri.EscapeDataString(cleanedName)}");

        SgdbGame? match = results?.FirstOrDefault(r => NormalizeName(r.Name) == NormalizeName(cleanedName));

        if (match is null)
        {
            return null;
        }

        return await FindVerticalGridAsync($"grids/game/{match.Id}");
    }

    private async Task<string?> FindVerticalGridAsync(string gridsPath)
    {
        List<SgdbGrid>? grids = await GetAsync<List<SgdbGrid>>(
            $"{gridsPath}?types=static&mimes=image/png,image/jpeg");

        return grids?.FirstOrDefault(g => g.Height > g.Width)?.Url;
    }

    private async Task<T?> GetAsync<T>(string path)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using HttpResponseMessage response = await Http.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        SgdbResponse<T>? result = JsonSerializer.Deserialize<SgdbResponse<T>>(json);

        return result is null ? default : result.Data;
    }

    private static string CleanForSearch(string name)
    {
        string cleaned = name.Replace("™", "").Replace("®", "").Replace("©", "").Trim();

        if (cleaned.EndsWith(" Demo", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(0, cleaned.Length - " Demo".Length).Trim();
        }

        return cleaned;
    }
    private static string NormalizeName(string name)
    {
        return new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}

internal class SgdbResponse<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

internal class SgdbGame
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

internal class SgdbGrid
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}
