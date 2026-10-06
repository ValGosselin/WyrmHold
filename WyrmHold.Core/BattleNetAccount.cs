using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wyrmhold.Core;

public record BattleNetOwnedGame(string Name, string LaunchCode, long LastPlayedUnix);

/// <summary>
/// Lit la liste des jeux du compte Battle.net renvoyée par le site account.battle.net.
/// </summary>
public static class BattleNetAccount
{
    // Ouvrir cette page renouvelle le cookie de connexion du site (méthode reprise de Playnite).
    public const string AccountSettingsUrl = "https://account.battle.net/oauth2/authorization/account-settings";
    public const string GamesUrl = "https://account.battle.net/api/games-and-subs";

    public static List<BattleNetOwnedGame> ReadOwnedGames(string json)
    {
        BattleNetGamesResponse? response = JsonSerializer.Deserialize<BattleNetGamesResponse>(json);

        if (response is null)
        {
            throw new InvalidOperationException("La liste des jeux Battle.net est illisible.");
        }

        return response.GameAccounts
            .Where(account => !string.IsNullOrWhiteSpace(account.LocalizedGameName))
            .Select(account => new BattleNetOwnedGame(
                NameTools.CleanForSearch(account.LocalizedGameName!),
                TitleIdToLaunchCode(account.TitleId),
                (account.LastPlayedDateMillis ?? 0) / 1000))
            .ToList();
    }

    /// <summary>
    /// Le titleId est le code de lancement du jeu écrit sous forme de nombre :
    /// ses 4 octets sont les 4 lettres du code (1465140039 = "WTCG" pour Hearthstone).
    /// </summary>
    public static string TitleIdToLaunchCode(long titleId)
    {
        char[] letters =
        {
            (char)((titleId >> 24) & 0xFF),
            (char)((titleId >> 16) & 0xFF),
            (char)((titleId >> 8) & 0xFF),
            (char)(titleId & 0xFF)
        };

        return letters.All(char.IsAsciiLetterOrDigit)
            ? new string(letters)
            : titleId.ToString();
    }
}

public class BattleNetGamesResponse
{
    [JsonPropertyName("gameAccounts")]
    public List<BattleNetGameAccount> GameAccounts { get; set; } = new List<BattleNetGameAccount>();
}

public class BattleNetGameAccount
{
    [JsonPropertyName("titleId")]
    public long TitleId { get; set; }

    [JsonPropertyName("localizedGameName")]
    public string? LocalizedGameName { get; set; }

    [JsonPropertyName("lastPlayedDateMillis")]
    public long? LastPlayedDateMillis { get; set; }
}