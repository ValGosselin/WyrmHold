using System.Text.Json.Serialization;

namespace Wyrmhold.Core;

public class SteamOwnedGame
{
    [JsonPropertyName("appid")]
    public int AppId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("playtime_forever")]
    public int PlaytimeMinutes { get; set; }
    [JsonPropertyName("rtime_last_played")]
    public long LastPlayedUnix { get; set; }
}

internal class SteamOwnedGamesResponse
{
    [JsonPropertyName("response")]
    public SteamOwnedGamesData? Response { get; set; }
}

internal class SteamOwnedGamesData
{
    [JsonPropertyName("games")]
    public List<SteamOwnedGame> Games { get; set; } = new List<SteamOwnedGame>();
}