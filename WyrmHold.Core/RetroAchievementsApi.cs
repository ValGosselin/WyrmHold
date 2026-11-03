using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wyrmhold.Core;

/// <summary>
/// L'API web de RetroAchievements (documentation : api-docs.retroachievements.org).
/// La clé part dans l'adresse (paramètre y) : ne jamais écrire une adresse complète dans le journal.
/// </summary>
public class RetroAchievementsApi
{
    private const string BaseUrl = "https://retroachievements.org/API/";

    private static readonly HttpClient Http = CreateClient();

    private const int CompletionPageSize = 500;

    // Un nombre écrit entre guillemets est accepté aussi : la documentation ne garantit pas le type de chaque champ.
    internal static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly TimeSpan DefaultPause = TimeSpan.FromMinutes(2);
    private static DateTime _pausedUntil = DateTime.MinValue;

    // La clé est relue à chaque appel : une clé changée sert tout de suite.
    private readonly Secrets _secrets;
    private string ApiKey => Uri.EscapeDataString(_secrets.RetroAchievementsApiKey);

    // Le compte à lire : l'ULID s'il est connu, car un pseudo peut changer.
    public string User => _secrets.RetroAchievementsUlid.Length > 0 ? _secrets.RetroAchievementsUlid : _secrets.RetroAchievementsUser;

    public RetroAchievementsApi(Secrets secrets)
    {
        _secrets = secrets;
    }

    /// <summary>
    /// Le profil d'un compte : sert aussi à vérifier le pseudo et la clé.
    /// Lance une erreur si RetroAchievements refuse : 401 = clé refusée (essai du 2 novembre 2026),
    /// 404 = pseudo inconnu (code de API_GetUserProfile.php, lu le 2 novembre 2026),
    /// 422 = pseudo impossible (essai du 2 novembre 2026).
    /// </summary>
    public async Task<RetroAchievementsProfile> GetUserProfileAsync(string user, CancellationToken cancellationToken = default)
    {
        string json = await GetJsonAsync($"{BaseUrl}API_GetUserProfile.php?u={Uri.EscapeDataString(user)}&y={ApiKey}", cancellationToken);

        return System.Text.Json.JsonSerializer.Deserialize<RetroAchievementsProfile>(json)
            ?? throw new HttpRequestException("Réponse vide de RetroAchievements.");
    }

    /// <summary>
    /// Les jeux d'une console qui ont des succès, avec les empreintes acceptées pour chacun.
    /// Réponse très longue et qui change peu : la documentation demande de la garder en cache.
    /// </summary>
    public async Task<List<RetroAchievementsGame>> GetGameListAsync(int consoleId, CancellationToken cancellationToken = default)
    {
        string json = await GetJsonAsync($"{BaseUrl}API_GetGameList.php?i={consoleId}&f=1&h=1&y={ApiKey}", cancellationToken);

        return System.Text.Json.JsonSerializer.Deserialize<List<RetroAchievementsGame>>(json) ?? new List<RetroAchievementsGame>();
    }

    /// <summary>
    /// Les versions acceptées pour un jeu, avec le nom du fichier de chacune : on sait ainsi laquelle le jeu attend.
    /// </summary>
    public async Task<List<RetroAchievementsVersion>> GetGameVersionsAsync(int gameId, CancellationToken cancellationToken = default)
    {
        string json = await GetJsonAsync($"{BaseUrl}API_GetGameHashes.php?i={gameId}&y={ApiKey}", cancellationToken);

        return System.Text.Json.JsonSerializer.Deserialize<RetroAchievementsVersions>(json)?.Results ?? new List<RetroAchievementsVersion>();
    }

    /// <summary>
    /// La progression d'un compte dans tous ses jeux, en une seule lecture (par pages de 500).
    /// Un jeu jamais lancé n'y figure pas.
    /// </summary>
    public async Task<List<RetroAchievementsCompletion>> GetUserCompletionAsync(string user, CancellationToken cancellationToken = default)
    {
        List<RetroAchievementsCompletion> games = new List<RetroAchievementsCompletion>();
        int total;

        do
        {
            string json = await GetJsonAsync(
                $"{BaseUrl}API_GetUserCompletionProgress.php?u={Uri.EscapeDataString(user)}&c={CompletionPageSize}&o={games.Count}&y={ApiKey}",
                cancellationToken);

            RetroAchievementsCompletionPage? page = JsonSerializer.Deserialize<RetroAchievementsCompletionPage>(json, ReadOptions);

            if (page?.Results is not { Count: > 0 })
            {
                break;
            }

            games.AddRange(page.Results);
            total = page.Total;
        }
        while (games.Count < total);

        return games;
    }

    /// <summary>
    /// Tous les succès d'un jeu, avec ceux que ce compte a débloqués.
    /// </summary>
    public async Task<RetroAchievementsGameProgress> GetGameProgressAsync(int gameId, string user, CancellationToken cancellationToken = default)
    {
        string json = await GetJsonAsync(
            $"{BaseUrl}API_GetGameInfoAndUserProgress.php?g={gameId}&u={Uri.EscapeDataString(user)}&y={ApiKey}",
            cancellationToken);

        return JsonSerializer.Deserialize<RetroAchievementsGameProgress>(json, ReadOptions)
            ?? throw new HttpRequestException("Réponse vide de RetroAchievements.");
    }

    /// <summary>
    /// Les succès débloqués par ce compte pendant une période (temps Unix), tous jeux confondus.
    /// Une réponse courte, faite pour être relue souvent pendant une partie.
    /// </summary>
    public async Task<List<RetroAchievementsEarned>> GetEarnedBetweenAsync(string user, long fromUnix, long toUnix, CancellationToken cancellationToken = default)
    {
        string json = await GetJsonAsync(
            $"{BaseUrl}API_GetAchievementsEarnedBetween.php?u={Uri.EscapeDataString(user)}&f={fromUnix}&t={toUnix}&y={ApiKey}",
            cancellationToken);

        return JsonSerializer.Deserialize<List<RetroAchievementsEarned>>(json, ReadOptions) ?? new List<RetroAchievementsEarned>();
    }

    /// <summary>
    /// Les numéros de RetroAchievements pour une console (fichier rc_consoles.h de rcheevos, lu le 2 novembre 2026).
    /// Un jeu Game Boy peut être rangé en Game Boy Color et un jeu DS en DSi : on cherche dans les deux.
    /// </summary>
    public static int[] GetConsoleIds(GameConsole console) => console switch
    {
        GameConsole.GameBoy or GameConsole.GameBoyColor => new[] { 4, 6 },
        GameConsole.GameBoyAdvance => new[] { 5 },
        GameConsole.NintendoDs => new[] { 18, 78 },
        GameConsole.GameCube => new[] { 16 },
        GameConsole.Wii => new[] { 19 },
        _ => Array.Empty<int>()
    };

    // Toutes les demandes passent ici : après un 429, plus rien n'est demandé pendant la pause.
    // Le site active une limite sans publier de chiffres : la durée par défaut est un choix de Wyrmhold.
    private static async Task<string> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow < _pausedUntil)
        {
            throw new AchievementSourceUnavailableException("RetroAchievements : trop de demandes, pause en cours.");
        }

        using HttpResponseMessage response = await Http.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            TimeSpan pause = response.Headers.RetryAfter?.Delta ?? DefaultPause;
            _pausedUntil = DateTime.UtcNow + pause;
            Logger.Log($"RetroAchievements : trop de demandes (429), pause de {pause.TotalSeconds:0} s.");
            throw new AchievementSourceUnavailableException("RetroAchievements : trop de demandes, pause en cours.");
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static HttpClient CreateClient()
    {
        HttpClient client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Wyrmhold/{AppInfo.Version}");
        return client;
    }
}

public record RetroAchievementsProfile(
    [property: JsonPropertyName("User")] string User,
    [property: JsonPropertyName("ULID")] string? Ulid,
    [property: JsonPropertyName("TotalPoints")] int TotalPoints,
    [property: JsonPropertyName("TotalSoftcorePoints")] int TotalSoftcorePoints);

public record RetroAchievementsGame(
    [property: JsonPropertyName("ID")] int Id,
    [property: JsonPropertyName("Title")] string Title,
    [property: JsonPropertyName("NumAchievements")] int AchievementCount,
    [property: JsonPropertyName("Hashes")] List<string>? Hashes)
{
    public bool Accepts(string hash) => Hashes?.Contains(hash, StringComparer.OrdinalIgnoreCase) == true;
}

public record RetroAchievementsCompletionPage(
    [property: JsonPropertyName("Total")] int Total,
    [property: JsonPropertyName("Results")] List<RetroAchievementsCompletion>? Results);

public record RetroAchievementsCompletion(
    [property: JsonPropertyName("GameID")] int GameId,
    [property: JsonPropertyName("MaxPossible")] int MaxPossible,
    [property: JsonPropertyName("NumAwarded")] int Unlocked,
    [property: JsonPropertyName("NumAwardedHardcore")] int UnlockedHardcore);

// Achievements reste un JsonElement : c'est un dictionnaire (numéro → succès), et il pourrait être une liste vide.
public record RetroAchievementsGameProgress(
    [property: JsonPropertyName("NumDistinctPlayers")] int PlayerCount,
    [property: JsonPropertyName("Achievements")] JsonElement Achievements);

public record RetroAchievementsAchievement(
    [property: JsonPropertyName("ID")] int Id,
    [property: JsonPropertyName("Title")] string Title,
    [property: JsonPropertyName("Description")] string? Description,
    [property: JsonPropertyName("BadgeName")] string? BadgeName,
    [property: JsonPropertyName("DisplayOrder")] int DisplayOrder,
    [property: JsonPropertyName("NumAwarded")] int AwardedCount,
    [property: JsonPropertyName("DateEarned")] string? DateEarned,
    [property: JsonPropertyName("DateEarnedHardcore")] string? DateEarnedHardcore);

public record RetroAchievementsEarned(
    [property: JsonPropertyName("AchievementID")] int AchievementId,
    [property: JsonPropertyName("GameID")] int GameId);

public record RetroAchievementsVersions(
    [property: JsonPropertyName("Results")] List<RetroAchievementsVersion>? Results);

public record RetroAchievementsVersion(
    [property: JsonPropertyName("MD5")] string Hash,
    [property: JsonPropertyName("Name")] string? FileName);
