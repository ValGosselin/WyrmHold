using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wyrmhold.Core;

public static class EpicApi
{
    private const string ClientId = "34a02cf8f4414e29b15921876da36f9a";
    private const string ClientSecret = "daafbccc737745039dffe53d94fc76cf";

    public const string AuthorizationCodeUrl =
        "https://www.epicgames.com/id/api/redirect?clientId=" + ClientId + "&responseType=code";

    public static string LoginUrl =>
        "https://www.epicgames.com/id/login?redirectUrl=" + Uri.EscapeDataString(AuthorizationCodeUrl);

    private static readonly HttpClient Http = new HttpClient();

    public static string? ReadAuthorizationCode(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("authorizationCode", out JsonElement code)
                && code.ValueKind == JsonValueKind.String)
            {
                return code.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    public static async Task<string?> ExchangeCodeAsync(string authorizationCode)
    {
        using HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://account-public-service-prod03.ol.epicgames.com/account/api/oauth/token");

        string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authorizationCode,
            ["token_type"] = "eg1"
        });

        using HttpResponseMessage response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<EpicTokenResponse>(json)?.AccessToken;
    }

    internal static async Task<List<EpicLibraryRecord>> GetLibraryRecordsAsync(string accessToken)
    {
        List<EpicLibraryRecord> records = new List<EpicLibraryRecord>();
        string? cursor = null;

        do
        {
            string url = "https://library-service.live.use1a.on.epicgames.com/library/api/public/items?includeMetadata=true";

            if (cursor is not null)
            {
                url += "&cursor=" + Uri.EscapeDataString(cursor);
            }

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using HttpResponseMessage response = await Http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            EpicLibraryPage? page = JsonSerializer.Deserialize<EpicLibraryPage>(json);

            if (page is null)
            {
                break;
            }

            records.AddRange(page.Records);
            cursor = page.ResponseMetadata?.NextCursor;
        }
        while (!string.IsNullOrEmpty(cursor));

        return records;
    }

    internal static async Task<EpicCatalogItem?> GetCatalogItemAsync(string catalogNamespace, string catalogItemId, string accessToken)
    {
        string url = "https://catalog-public-service-prod06.ol.epicgames.com/catalog/api/shared/namespace/"
            + $"{Uri.EscapeDataString(catalogNamespace)}/bulk/items"
            + $"?id={Uri.EscapeDataString(catalogItemId)}"
            + "&includeDLCDetails=true&includeMainGameDetails=true&country=FR&locale=fr";

        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using HttpResponseMessage response = await Http.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        Dictionary<string, EpicCatalogItem>? items = JsonSerializer.Deserialize<Dictionary<string, EpicCatalogItem>>(json);

        return items is not null && items.TryGetValue(catalogItemId, out EpicCatalogItem? item) ? item : null;
    }
}

internal class EpicTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }
}

internal class EpicLibraryPage
{
    [JsonPropertyName("records")]
    public List<EpicLibraryRecord> Records { get; set; } = new List<EpicLibraryRecord>();

    [JsonPropertyName("responseMetadata")]
    public EpicResponseMetadata? ResponseMetadata { get; set; }
}

internal class EpicResponseMetadata
{
    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; set; }
}

internal class EpicLibraryRecord
{
    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = "";

    [JsonPropertyName("catalogItemId")]
    public string CatalogItemId { get; set; } = "";

    [JsonPropertyName("appName")]
    public string AppName { get; set; } = "";
}

internal class EpicCatalogItem
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("categories")]
    public List<EpicCategory> Categories { get; set; } = new List<EpicCategory>();

    [JsonPropertyName("mainGameItem")]
    public EpicMainGameItem? MainGameItem { get; set; }

    [JsonPropertyName("keyImages")]
    public List<EpicKeyImage> KeyImages { get; set; } = new List<EpicKeyImage>();
}

internal class EpicKeyImage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}

internal class EpicCategory
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";
}

internal class EpicMainGameItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
}