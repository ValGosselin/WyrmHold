using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wyrmhold.Core;

public class SteamGridDbApi
{
    private const string BaseUrl = "https://www.steamgriddb.com/api/v2/";

    private static readonly HttpClient Http = new HttpClient();

    // La clé est relue à chaque appel : une clé changée dans Réglages sert tout de suite.
    private readonly Secrets _secrets;
    private string ApiKey => _secrets.SteamGridDbApiKey;

    public SteamGridDbApi(Secrets secrets)
    {
        _secrets = secrets;
    }

    public bool IsConfigured => !string.IsNullOrEmpty(ApiKey);

    /// <summary>
    /// Vérifie la clé (assistant et Réglages) : la fiche d'un jeu connu (9022 = Portal).
    /// Lance une erreur si la clé est refusée.
    /// Essai du 9 octobre 2026 : la recherche (search/autocomplete) répond même avec une fausse clé,
    /// games/id répond 401 : c'est donc lui qui sert de test. Autre piège : une réponse déjà demandée
    /// avec une bonne clé est gardée par le cache de Cloudflare (« cf-cache-status: HIT ») et renvoyée
    /// même avec une fausse clé. Un paramètre « _ » différent à chaque fois oblige à vraiment vérifier.
    /// </summary>
    public async Task TestAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}games/id/9022?_={DateTime.UtcNow.Ticks}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

        using HttpResponseMessage response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

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
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

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
