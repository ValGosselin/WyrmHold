using System.Net;
using System.Text.Json;

namespace Wyrmhold.Core;

public class SteamWebApi
{
    private static readonly HttpClient Http = new HttpClient();
    public static bool TryParseLoginCookie(string? cookieValue, out string steamId, out string accessToken)
    {
        steamId = "";
        accessToken = "";

        if (string.IsNullOrEmpty(cookieValue))
        {
            return false;
        }

        string[] parts = Uri.UnescapeDataString(cookieValue).Split("||");

        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            return false;
        }

        steamId = parts[0];
        accessToken = parts[1];
        return true;
    }

    private readonly Secrets _secrets;

    public SteamWebApi(Secrets secrets)
    {
        _secrets = secrets;
    }

    public bool IsConfigured =>
        !string.IsNullOrEmpty(_secrets.SteamApiKey) && !string.IsNullOrEmpty(_secrets.SteamId);

    public Task<List<SteamOwnedGame>> GetOwnedGamesAsync()
    {
        return GetGamesAsync("GetOwnedGames", "&include_appinfo=true&include_played_free_games=true");
    }

    public Task<List<SteamOwnedGame>> GetRecentlyPlayedGamesAsync()
    {
        return GetGamesAsync("GetRecentlyPlayedGames", "");
    }

    private async Task<List<SteamOwnedGame>> GetGamesAsync(string method, string extraParameters)
    {
        if (!IsConfigured)
        {
            return new List<SteamOwnedGame>();
        }

        string url = $"https://api.steampowered.com/IPlayerService/{method}/v1/"
            + $"?key={_secrets.SteamApiKey}&steamid={_secrets.SteamId}"
            + extraParameters;

        string json = await Http.GetStringAsync(url);
        SteamOwnedGamesResponse? result = JsonSerializer.Deserialize<SteamOwnedGamesResponse>(json);

        return result?.Response?.Games ?? new List<SteamOwnedGame>();
    }
    public async Task<List<SharedLibraryApp>> GetFamilyLibraryAsync(string accessToken)
    {
        string token = Uri.EscapeDataString(accessToken);

        string groupJson = await Http.GetStringAsync(
            $"https://api.steampowered.com/IFamilyGroupsService/GetFamilyGroupForUser/v1/?access_token={token}");

        FamilyGroupData? family = JsonSerializer.Deserialize<FamilyGroupResponse>(groupJson)?.Response;

        if (family is null || family.IsNotMemberOfAnyGroup || string.IsNullOrEmpty(family.FamilyGroupId))
        {
            return new List<SharedLibraryApp>();
        }

        string libraryJson = await Http.GetStringAsync(
            "https://api.steampowered.com/IFamilyGroupsService/GetSharedLibraryApps/v1/"
            + $"?access_token={token}&family_groupid={family.FamilyGroupId}&include_own=false");

        return JsonSerializer.Deserialize<SharedLibraryResponse>(libraryJson)?.Response?.Apps
            ?? new List<SharedLibraryApp>();
    }

    // ----- Succès -----

    public async Task<AchievementProgress> GetAchievementProgressAsync(string appId, CancellationToken cancellationToken = default)
    {
        string url = "https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/"
            + $"?key={_secrets.SteamApiKey}&steamid={_secrets.SteamId}&appid={Uri.EscapeDataString(appId)}";

        // GetAsync et pas GetStringAsync : pour un jeu sans succès, Steam répond avec une erreur 400
        // et un message, qu'on veut pouvoir lire au lieu de recevoir directement une exception.
        using HttpResponseMessage response = await Http.GetAsync(url, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            throw new AchievementSourceUnavailableException(
                "Steam refuse l'accès : vérifie que les détails de jeu de ton profil sont publics et que ta clé API est valide.");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new AchievementSourceUnavailableException("Trop d'appels à l'API Steam : on réessaiera au prochain démarrage.");
        }

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        SteamPlayerStats? stats = ReadPlayerStats(json);

        if (stats is { Success: true })
        {
            int unlocked = stats.Achievements.Count(a => a.Achieved == 1);
            return new AchievementProgress(unlocked, stats.Achievements.Count);
        }

        string error = stats?.Error ?? "";

        if (error.Contains("not public", StringComparison.OrdinalIgnoreCase))
        {
            throw new AchievementSourceUnavailableException("Ton profil Steam est privé : les succès ne sont pas lisibles.");
        }

        if (error.Contains("no stats", StringComparison.OrdinalIgnoreCase))
        {
            return new AchievementProgress(0, 0);
        }

        throw new HttpRequestException($"Réponse inattendue de Steam ({(int)response.StatusCode}) : {(error.Length > 0 ? error : "illisible")}");
    }

    private static SteamPlayerStats? ReadPlayerStats(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SteamPlayerAchievementsResponse>(json)?.PlayerStats;
        }
        catch (JsonException)
        {
            // Pas du JSON (page d'erreur HTML, par exemple).
            return null;
        }
    }
}