using System.Text.Json;

namespace Wyrmhold.Core;

public class SteamWebApi
{
    private static readonly HttpClient Http = new HttpClient();

    private readonly Secrets _secrets;

    public SteamWebApi(Secrets secrets)
    {
        _secrets = secrets;
    }

    public bool IsConfigured =>
        !string.IsNullOrEmpty(_secrets.SteamApiKey) && !string.IsNullOrEmpty(_secrets.SteamId);

    public async Task<List<SteamOwnedGame>> GetOwnedGamesAsync()
    {
        if (!IsConfigured)
        {
            return new List<SteamOwnedGame>();
        }

        string url = "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/"
            + $"?key={_secrets.SteamApiKey}&steamid={_secrets.SteamId}"
            + "&include_appinfo=true&include_played_free_games=true";

        string json = await Http.GetStringAsync(url);
        SteamOwnedGamesResponse? result = JsonSerializer.Deserialize<SteamOwnedGamesResponse>(json);

        return result?.Response?.Games ?? new List<SteamOwnedGame>();
    }
}