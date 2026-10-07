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

    public static Task<EpicTokenResponse?> ExchangeCodeAsync(string authorizationCode)
    {
        return RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authorizationCode,
            ["token_type"] = "eg1"
        });
    }

    /// <summary>
    /// Demande un nouveau jeton avec le jeton de renouvellement, sans repasser par la page de connexion.
    /// </summary>
    public static Task<EpicTokenResponse?> RefreshAsync(string refreshToken)
    {
        return RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["token_type"] = "eg1"
        });
    }

    private static async Task<EpicTokenResponse?> RequestTokenAsync(Dictionary<string, string> parameters)
    {
        using HttpRequestMessage request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://account-public-service-prod03.ol.epicgames.com/account/api/oauth/token");

        string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(parameters);

        using HttpResponseMessage response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<EpicTokenResponse>(json);
    }

    /// <summary>
    /// Temps de jeu de chaque jeu du compte, en secondes, rangé par appName
    /// (même adresse que celle utilisée par Playnite).
    /// </summary>
    internal static async Task<Dictionary<string, long>> GetPlaytimesAsync(string accessToken, string accountId)
    {
        string url = "https://library-service.live.use1a.on.epicgames.com/library/api/public/playtime/account/"
            + Uri.EscapeDataString(accountId) + "/all";

        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using HttpResponseMessage response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        List<EpicPlaytimeItem> items = JsonSerializer.Deserialize<List<EpicPlaytimeItem>>(json) ?? new List<EpicPlaytimeItem>();

        Dictionary<string, long> playtimes = new Dictionary<string, long>();

        foreach (EpicPlaytimeItem item in items)
        {
            if (!string.IsNullOrEmpty(item.ArtifactId))
            {
                playtimes[item.ArtifactId] = item.TotalTimeSeconds;
            }
        }

        return playtimes;
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

    // ----- Succès (API GraphQL de la boutique, non documentée : elle peut changer) -----

    private const string GraphQlUrl = "https://launcher.store.epicgames.com/graphql";
    private const string GraphQlUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) EpicGamesLauncher";
    private const string Locale = "fr-FR";

    // Au moins 200 ms entre deux requêtes GraphQL, même quand plusieurs jeux sont lus en parallèle :
    // trop de requêtes d'un coup peuvent faire bloquer Wyrmhold par Epic.
    private static readonly TimeSpan GraphQlMinInterval = TimeSpan.FromMilliseconds(200);
    private static readonly SemaphoreSlim GraphQlGate = new SemaphoreSlim(1, 1);
    private static DateTime _lastGraphQlCall = DateTime.MinValue;

    private const string AchievementSchemaQuery = """
        query Achievement($SandboxId: String!, $Locale: String!) {
          Achievement {
            productAchievementsRecordBySandbox(sandboxId: $SandboxId, locale: $Locale) {
              productId
              totalAchievements
              achievements {
                achievement {
                  name
                  hidden
                  unlockedDisplayName
                  unlockedDescription
                  unlockedIconLink
                  lockedIconLink
                  rarity { percent }
                }
              }
            }
          }
        }
        """;

    private const string PlayerAchievementsQuery = """
        query playerProfileAchievementsByProductId($EpicAccountId: String!, $ProductId: String!) {
          PlayerProfile {
            playerProfile(epicAccountId: $EpicAccountId) {
              productAchievements(productId: $ProductId) {
                ... on PlayerProductAchievementsResponseSuccess {
                  data {
                    playerAchievements {
                      playerAchievement {
                        achievementName
                        unlocked
                        unlockDate
                        progress
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """;

    /// <summary>
    /// La liste des succès d'un jeu (publique), à partir de son « namespace » Epic.
    /// null si le jeu n'a pas de succès.
    /// </summary>
    internal static async Task<EpicProductAchievements?> GetAchievementSchemaAsync(
        string sandboxId, CancellationToken cancellationToken)
    {
        EpicSchemaResponse? response = await QueryGraphQlAsync<EpicSchemaResponse>(
            AchievementSchemaQuery,
            new { SandboxId = sandboxId, Locale },
            null,
            cancellationToken);

        // « Achievement » présent mais sans fiche = le jeu n'a pas de succès.
        // « Achievement » absent = Epic a répondu par une erreur : on ne conclut rien.
        if (response?.Data?.Achievement is null)
        {
            string details = string.Join(" ; ", response?.Errors?.Select(e => e.Message) ?? Enumerable.Empty<string?>());
            throw new HttpRequestException($"Réponse Epic inattendue pour la liste des succès. {details}".Trim());
        }

        return response.Data.Achievement.Record;
    }

    /// <summary>
    /// Tes succès pour un jeu (débloqué ou non, quand, avancement), rangés par nom de succès.
    /// </summary>
    internal static async Task<Dictionary<string, EpicPlayerAchievement>> GetPlayerAchievementsAsync(
        string accountId, string productId, string accessToken, CancellationToken cancellationToken)
    {
        EpicPlayerResponse? response = await QueryGraphQlAsync<EpicPlayerResponse>(
            PlayerAchievementsQuery,
            new { EpicAccountId = accountId, ProductId = productId },
            accessToken,
            cancellationToken);

        EpicPlayerProfile? profile = response?.Data?.PlayerProfile?.Profile;
        bool hasErrors = response?.Errors is { Count: > 0 };

        // Une vraie erreur (Epic renvoie des « errors », ou pas de profil du tout) : on lève une exception
        // pour garder les valeurs déjà enregistrées, au lieu de tout faire passer « non débloqué ».
        if (profile is null || hasErrors)
        {
            string details = string.Join(" ; ", response?.Errors?.Select(e => e.Message) ?? Enumerable.Empty<string?>());
            throw new HttpRequestException($"Réponse Epic inattendue pour tes succès. {details}".Trim());
        }

        EpicPlayerAchievementsData? data = profile.ProductAchievements?.Data;

        // Profil trouvé mais aucune fiche pour ce jeu : c'est le cas des jeux jamais lancés
        // (vérifié sur ta bibliothèque : 59 des 63 jeux concernés n'avaient jamais été joués).
        // Ce n'est pas une erreur : aucun succès débloqué.
        if (data is null)
        {
            return new Dictionary<string, EpicPlayerAchievement>();
        }

        List<EpicPlayerAchievementWrapper> items = data.PlayerAchievements ?? new List<EpicPlayerAchievementWrapper>();

        return items
            .Select(item => item.PlayerAchievement)
            .OfType<EpicPlayerAchievement>()
            .Where(a => !string.IsNullOrEmpty(a.Name))
            .DistinctBy(a => a.Name)
            .ToDictionary(a => a.Name!);
    }

    /// <summary>
    /// Envoie une requête GraphQL : un POST avec un JSON { query, variables }, et une réponse { data, errors }.
    /// </summary>
    private static async Task<T?> QueryGraphQlAsync<T>(string query, object variables, string? accessToken, CancellationToken cancellationToken)
    {
        await WaitForGraphQlTurnAsync(cancellationToken);

        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, GraphQlUrl);
        request.Headers.UserAgent.ParseAdd(GraphQlUserAgent);

        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        string body = JsonSerializer.Serialize(new { query, variables });
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new AchievementSourceUnavailableException("La session Epic a expiré : clique sur Actualiser pour la renouveler.");
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            throw new AchievementSourceUnavailableException(
                $"Epic refuse les requêtes pour l'instant ({(int)response.StatusCode}) : on réessaiera plus tard.");
        }

        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(json);
    }

    private static async Task WaitForGraphQlTurnAsync(CancellationToken cancellationToken)
    {
        // Une seule tâche à la fois passe ici : elle attend si la requête précédente est trop récente.
        await GraphQlGate.WaitAsync(cancellationToken);

        try
        {
            TimeSpan wait = _lastGraphQlCall + GraphQlMinInterval - DateTime.UtcNow;

            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }

            _lastGraphQlCall = DateTime.UtcNow;
        }
        finally
        {
            GraphQlGate.Release();
        }
    }
}

public class EpicTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("account_id")]
    public string? AccountId { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    // Durée de validité du jeton d'accès, en secondes.
    [JsonPropertyName("expires_in")]
    public int ExpiresInSeconds { get; set; }
}

internal class EpicPlaytimeItem
{
    [JsonPropertyName("artifactId")]
    public string ArtifactId { get; set; } = "";

    [JsonPropertyName("totalTime")]
    public long TotalTimeSeconds { get; set; }
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