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
    
}