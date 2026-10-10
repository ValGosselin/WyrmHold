using System.Net;
using System.Text.Json;

namespace Wyrmhold.Core;

/// <summary>
/// Le nombre de joueurs en jeu en ce moment sur Steam, pour un jeu.
/// Méthode publique de Steam, sans clé API (vérifiée le 10 octobre 2026) :
/// ISteamUserStats/GetNumberOfCurrentPlayers/v1/?appid=730 → {"response":{"player_count":667391,"result":1}} ;
/// un jeu que Steam ne suit pas répond « 404 introuvable ».
/// Les autres plateformes (Epic, GOG, Ubisoft, EA, Battle.net) n'ont pas d'équivalent public.
/// </summary>
public static class SteamPlayerCountApi
{
    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// Le nombre de joueurs en jeu, ou null si Steam n'a pas de chiffre pour ce jeu (404, réponse sans chiffre).
    /// Une autre erreur (réseau…) remonte : l'appelant garde alors l'ancien chiffre.
    /// </summary>
    public static async Task<int?> GetCurrentPlayersAsync(string appId, CancellationToken token = default)
    {
        string url = $"https://api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/v1/?appid={Uri.EscapeDataString(appId)}";

        using HttpResponseMessage response = await Http.GetAsync(url, token);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));

        // result = 1 : réponse valide (autre valeur = pas de chiffre pour ce jeu).
        if (document.RootElement.TryGetProperty("response", out JsonElement body)
            && body.TryGetProperty("result", out JsonElement result) && result.GetInt32() == 1
            && body.TryGetProperty("player_count", out JsonElement count))
        {
            return count.GetInt32();
        }

        return null;
    }
}
