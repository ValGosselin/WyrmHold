using System.Text.RegularExpressions;

namespace Wyrmhold.Core;

/// <summary>Le résultat d'un essai de clé : réussi ou non, et une phrase à montrer à l'utilisateur.</summary>
public record KeyTestResult(bool Success, string Message);

/// <summary>
/// Vérifie une clé avant de l'enregistrer, par un vrai petit appel au service.
/// Les clés essayées sont mises dans un objet Secrets TEMPORAIRE : rien n'est enregistré ici.
/// </summary>
public static class ApiKeyTester
{
    // Une clé Steam Web API : 32 caractères hexadécimaux (0-9, A-F).
    private static readonly Regex SteamKeyFormat = new Regex("^[0-9A-Fa-f]{32}$");

    // Un SteamID64 : 17 chiffres qui commencent par 7656119.
    private static readonly Regex SteamIdFormat = new Regex(@"^7656119\d{10}$");

    public static async Task<KeyTestResult> TestSteamAsync(string apiKey, string steamId)
    {
        apiKey = apiKey.Trim();
        steamId = steamId.Trim();

        if (!SteamIdFormat.IsMatch(steamId))
        {
            return new KeyTestResult(false, "Identifiant Steam manquant : clique sur « Se connecter à Steam ».");
        }

        if (!SteamKeyFormat.IsMatch(apiKey))
        {
            return new KeyTestResult(false, "Une clé Steam fait 32 caractères (chiffres et lettres de A à F). Vérifie le copier-coller.");
        }

        try
        {
            var api = new SteamWebApi(new Secrets { SteamApiKey = apiKey, SteamId = steamId });
            List<SteamOwnedGame> games = await api.GetOwnedGamesAsync();

            if (games.Count == 0)
            {
                // Steam répond, mais ne donne aucun jeu : le plus souvent, les détails de jeu du profil sont privés.
                return new KeyTestResult(false,
                    "Clé acceptée, mais Steam ne donne aucun jeu. Dans Steam : Profil → Modifier le profil → "
                    + "Confidentialité → « Détails des jeux » sur « Public », puis réessaie.");
            }

            return new KeyTestResult(true, $"✔ Clé valide : {games.Count} jeux trouvés sur ton compte.");
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
        {
            return new KeyTestResult(false, "Steam refuse cette clé. Vérifie-la sur steamcommunity.com/dev/apikey.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new KeyTestResult(false, $"Steam ne répond pas (connexion internet ?) : {ex.Message}");
        }
    }

    public static async Task<KeyTestResult> TestSteamGridDbAsync(string apiKey)
    {
        apiKey = apiKey.Trim();

        if (apiKey.Length == 0)
        {
            return new KeyTestResult(false, "Colle d'abord ta clé.");
        }

        try
        {
            await new SteamGridDbApi(new Secrets { SteamGridDbApiKey = apiKey }).TestAsync();
            return new KeyTestResult(true, "✔ Clé valide.");
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
        {
            return new KeyTestResult(false, "SteamGridDB refuse cette clé. Vérifie le copier-coller.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new KeyTestResult(false, $"SteamGridDB ne répond pas (connexion internet ?) : {ex.Message}");
        }
    }

    public static async Task<KeyTestResult> TestIsThereAnyDealAsync(string apiKey)
    {
        apiKey = apiKey.Trim();

        if (apiKey.Length == 0)
        {
            return new KeyTestResult(false, "Colle d'abord ta clé.");
        }

        try
        {
            List<ItadSearchResult> results = await new IsThereAnyDealApi(new Secrets { IsThereAnyDealApiKey = apiKey }).SearchAsync("Portal");
            return new KeyTestResult(true, $"✔ Clé valide ({results.Count} résultats pour « Portal »).");
        }
        catch (HttpRequestException ex) when (ex.Message.Contains(" 401 ") || ex.Message.Contains(" 403 "))
        {
            // IsThereAnyDealApi met le code de la réponse dans le message (« IsThereAnyDeal a répondu 403 … »).
            return new KeyTestResult(false, "IsThereAnyDeal refuse cette clé. Vérifie que tu as copié la « API Key ».");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new KeyTestResult(false, $"IsThereAnyDeal ne répond pas : {ex.Message}");
        }
    }

    /// <summary>« ••••••••3F2A » : de quoi reconnaître une clé sans l'afficher.</summary>
    public static string Mask(string value)
    {
        return value.Length <= 4 ? (value.Length == 0 ? "" : "••••") : "••••••••" + value[^4..];
    }
}
